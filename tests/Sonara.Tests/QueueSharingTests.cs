using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonara.Api.Subsonic;
using Sonara.Core.Entities;
using Sonara.Data;

namespace Sonara.Tests;

[Collection("PostgreSQL integration")]
public class QueueSharingTests(ParityHost host) : IClassFixture<ParityHost>
{
    private async Task<JsonObject> Post(string operation, IEnumerable<KeyValuePair<string, string>> values, string user = "listener")
    {
        using var client = host.CreateClient();
        using var response = await client.PostAsync(host.Url(operation, user), new FormUrlEncodedContent(values));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, text);
        return JsonNode.Parse(text)!["subsonic-response"]!.AsObject();
    }
    private static void Ok(JsonObject result) => Assert.Equal("ok", result["status"]!.ToString());

    [DatabaseFact]
    public async Task Long_queue_playlist_preserves_duplicates_and_empty_queue_stays_empty()
    {
        var ids = Enumerable.Repeat(host.PublicTrack.ToString(), 1200).ToArray();
        var playlist = await Post("createPlaylist", ids.Select(id => KeyValuePair.Create("songId", id)).Append(KeyValuePair.Create("name", "Saved queue")));
        Ok(playlist); Assert.Equal(1200, playlist["playlist"]!["entry"]!.AsArray().Count);
        Assert.False(playlist["playlist"]!["public"]!.GetValue<bool>());
        var saved = await Post("savePlayQueue", ids.Select(id => KeyValuePair.Create("id", id)).Append(KeyValuePair.Create("current", ids[0])));
        Ok(saved); Assert.Equal(1200, (await host.Call("getPlayQueue"))["playQueue"]!["entry"]!.AsArray().Count);
        Ok(await Post("savePlayQueue", [KeyValuePair.Create("position", "0")]));
        Assert.Null((await host.Call("getPlayQueue"))["playQueue"]);
        // A fresh request is the same persisted state the next login restores.
        Assert.Null((await host.Call("getPlayQueue"))["playQueue"]);
    }

    [DatabaseFact]
    public async Task Short_folder_album_playlist_shares_are_scoped_snapshots_and_revocable()
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        var otherTrack = await db.Tracks.Where(t => t.MusicFolderId == host.Folder && t.Id != host.PublicTrack).Select(t => t.Id).FirstAsync();
        var playlist = await Post("createPlaylist", new[] { KeyValuePair.Create("name", "Ordered mix"), KeyValuePair.Create("songId", otherTrack.ToString()), KeyValuePair.Create("songId", host.PublicTrack.ToString()), KeyValuePair.Create("songId", otherTrack.ToString()) });
        Ok(playlist); var playlistId = playlist["playlist"]!["id"]!.ToString();
        foreach (var target in new[] { "al-" + host.PublicAlbum, SubsonicEndpoints.DirectoryId(host.Folder, "Artist"), "pl-" + playlistId })
        {
            var created = await host.Call("createShare", extra: "&id=" + target + "&description=Test"); Ok(created);
            var share = created["shares"]!["share"]![0]!; var token = share["id"]!.ToString();
            Assert.Matches("^[A-Za-z0-9_-]{16}$", token); Assert.EndsWith("/s/" + token, share["url"]!.ToString());
            if (target.StartsWith("pl-")) Assert.Equal(new[] { otherTrack.ToString(), host.PublicTrack.ToString(), otherTrack.ToString() }, share["entry"]!.AsArray().Select(e => e!["id"]!.ToString()));
            using var client = host.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/s/" + token)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/s/{token}/stream/{host.HiddenTrack}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/s/{token}/stream/{host.PublicTrack}")).StatusCode);
            var denied = await host.Call("deleteShare", "denied", "&id=" + token); Assert.Equal("failed", denied["status"]!.ToString());
            if (target.StartsWith("pl-"))
            {
                Ok(await host.Call("deletePlaylist", extra: "&id=" + playlistId));
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/s/" + token)).StatusCode);
            }
            Ok(await host.Call("deleteShare", extra: "&id=" + token));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/s/" + token)).StatusCode);
        }
        foreach (var target in new[] { "al-" + host.HiddenAlbum, SubsonicEndpoints.DirectoryId(host.HiddenFolder, ""), SubsonicEndpoints.DirectoryId(host.Folder, "../two") })
            Assert.Equal("failed", (await host.Call("createShare", extra: "&id=" + target))["status"]!.ToString());
        var privatePlaylist = await Post("createPlaylist", new[] { KeyValuePair.Create("name", "Admin private"), KeyValuePair.Create("songId", host.PublicTrack.ToString()) }, "admin");
        Assert.Equal("failed", (await host.Call("createShare", extra: "&id=pl-" + privatePlaylist["playlist"]!["id"]))["status"]!.ToString());
        var legacy = new MediaShare { Id = new string('a', 48), UserId = await db.Users.Where(u => u.Username == "listener").Select(u => u.Id).SingleAsync(), TrackIds = [host.PublicTrack] };
        db.Add(legacy); await db.SaveChangesAsync();
        using var legacyClient = host.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await legacyClient.GetAsync("/share/" + legacy.Id)).StatusCode);
        Ok(await host.Call("updateShare", extra: "&id=" + legacy.Id + "&expires=1"));
        Assert.Equal(HttpStatusCode.NotFound, (await legacyClient.GetAsync("/share/" + legacy.Id)).StatusCode);
    }
}
