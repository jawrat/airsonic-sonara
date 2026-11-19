using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonara.Api.Subsonic;
using Sonara.Data;

namespace Sonara.Tests;

[Collection("PostgreSQL integration")]
public class LibraryCoverTests(ParityHost host) : IClassFixture<ParityHost>
{
    [DatabaseFact]
    public async Task Representative_covers_respect_user_folder_scope_and_directory_boundaries()
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        var visible = await db.Tracks.Include(t => t.Album).FirstAsync(t => t.Id == host.PublicTrack);
        var hidden = await db.Tracks.FirstAsync(t => t.Id == host.HiddenTrack);
        var artistId = visible.Album.ArtistId;
        var hiddenArt = Path.Combine(Path.GetDirectoryName(hidden.Path)!, "cover.png");
        await File.WriteAllBytesAsync(hiddenArt, CoverImageFixture.OtherPng);
        using var client = host.CreateClient();
        // The artist exists in both folders, but a listener must not inherit hidden artwork.
        var response = await client.GetAsync(host.Url("getCoverArt", extra: "&id=ar-" + artistId));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        response = await client.GetAsync(host.Url("getCoverArt", "admin", "&id=ar-" + artistId + "&musicFolderId=" + host.HiddenFolder));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hiddenBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal<byte>(CoverImageFixture.OtherPng, hiddenBytes);

        await File.WriteAllBytesAsync(Path.Combine(Path.GetDirectoryName(visible.Path)!, "cover.png"), CoverImageFixture.Png);
        foreach (var id in new[] { "ar-" + artistId, SubsonicEndpoints.DirectoryId(host.Folder, ""), SubsonicEndpoints.DirectoryId(host.Folder, "Artist") })
        {
            response = await client.GetAsync(host.Url("getCoverArt", extra: "&id=" + id));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var visibleBytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal<byte>(CoverImageFixture.Png, visibleBytes);
        }
        response = await client.GetAsync(host.Url("getCoverArt.view", extra: "&id=al-" + host.PublicAlbum + "&size=160").Replace("f=json", "f=xml"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        var thumbnail = await response.Content.ReadAsByteArrayAsync();
        Assert.True(thumbnail.Length > 2 && thumbnail[0] == 0xff && thumbnail[1] == 0xd8);
        var indexes = await host.Call("getIndexes");
        Assert.Equal(SubsonicEndpoints.DirectoryId(host.Folder, "Artist"), indexes["indexes"]!["index"]![0]!["artist"]![0]!["coverArt"]!.ToString());
        foreach (var id in new[] { SubsonicEndpoints.DirectoryId(host.HiddenFolder, ""), SubsonicEndpoints.DirectoryId(host.Folder, "../two"), "dir-1-%%%", "ar-invalid" })
        {
            response = await client.GetAsync(host.Url("getCoverArt", extra: "&id=" + Uri.EscapeDataString(id)));
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        response = await client.GetAsync(host.Url("getCoverArt", extra: "&id=ar-" + artistId + "&musicFolderId=" + host.HiddenFolder));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var artists = await host.Call("getArtists");
        Assert.Equal("ar-" + artistId, artists["artists"]!["index"]![0]!["artist"]![0]!["coverArt"]!.ToString());
        var folders = await host.Call("getMusicFolders");
        Assert.Equal(SubsonicEndpoints.DirectoryId(host.Folder, ""), folders["musicFolders"]!["musicFolder"]![0]!["coverArt"]!.ToString());
        var directory = await host.Call("getMusicDirectory", extra: "&id=" + host.Folder);
        Assert.Equal(SubsonicEndpoints.DirectoryId(host.Folder, "Artist"), directory["directory"]!["child"]![0]!["coverArt"]!.ToString());
    }
}
