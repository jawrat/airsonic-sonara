using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonara.Api.Subsonic;
using Sonara.Core.Entities;
using Sonara.Data;

namespace Sonara.Tests;

[Collection("PostgreSQL integration")]
public class FolderHomeTests(ParityHost host) : IClassFixture<ParityHost>
{
    [DatabaseFact]
    public async Task Physical_folder_home_is_recent_scoped_paged_and_independent_of_tags()
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        var root = await db.MusicFolders.SingleAsync(f => f.Id == host.Folder);
        var original = await db.Tracks.FirstAsync(t => t.Id == host.PublicTrack);
        foreach (var t in await db.Tracks.Where(t => t.MusicFolderId == host.Folder).ToListAsync()) t.CreatedUtc = new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc);
        Track Add(string relative, int year)
        {
            var t = new Track { AlbumId=original.AlbumId,ArtistId=original.ArtistId,MusicFolderId=root.Id,Title="A tag title",Path=Path.Combine(root.Path,relative),Suffix="mp3",ContentType="audio/mpeg",CreatedUtc=new DateTime(year,1,1,0,0,0,DateTimeKind.Utc),LastModifiedUtc=DateTime.UtcNow };
            db.Add(t);return t;
        }
        Add("Artist/Alpha/first.mp3",2021);Add("Artist/Alpha/later-track.mp3",2026);Add("Artist/Zulu/new.mp3",2024);
        var loose=Add("loose.mp3",2025);await db.SaveChangesAsync();
        var response=await host.Call("getFolderList",extra:"&size=500&type=newest");
        Assert.Equal("ok",response["status"]!.ToString());
        var children=response["directory"]!["child"]!.AsArray();
        var directories=children.Where(c=>c!["isDir"]!.GetValue<bool>()).ToList();
        Assert.Equal(new[]{"Zulu","Alpha","Album"},directories.Select(d=>d!["title"]!.ToString()));
        Assert.Equal(SubsonicEndpoints.DirectoryId(root.Id,"Artist/Zulu"),directories[0]!["id"]!.ToString());
        Assert.Equal("Artist",directories[0]!["parentName"]!.ToString());
        Assert.Equal(2021,directories[1]!["created"]!.GetValue<DateTime>().Year); // Later tracks do not make an old folder newly added.
        Assert.Contains(children,c=>c!["id"]!.ToString()==loose.Id.ToString()&&!c["isDir"]!.GetValue<bool>());
        Assert.DoesNotContain(directories,d=>d!["title"]!.ToString()=="Music"||d["title"]!.ToString()=="Visible album");
        var page=await host.Call("getFolderList",extra:"&size=1&offset=1&type=newest");
        Assert.Single(page["directory"]!["child"]!.AsArray());Assert.Equal("Alpha",page["directory"]!["child"]![0]!["title"]!.ToString());
        var alphabetic=await host.Call("getFolderList",extra:"&size=1&type=alphabeticalByName");Assert.Equal("Album",alphabetic["directory"]!["child"]![0]!["title"]!.ToString());
        var denied=await host.Call("getFolderList",extra:"&musicFolderId="+host.HiddenFolder);Assert.Empty(denied["directory"]!["child"]!.AsArray());
        var index=await host.Call("getIndexes");Assert.Equal("Artist",index["indexes"]!["index"]![0]!["artist"]![0]!["name"]!.ToString());
        var id=SubsonicEndpoints.DirectoryId(root.Id,"Artist");
        var nested=await host.Call("getMusicDirectory",extra:"&id="+id+"&type=newest");Assert.Equal("Zulu",nested["directory"]!["child"]![0]!["title"]!.ToString());
        Assert.Equal(id,nested["directory"]!["indexId"]!.ToString());
        var legacy=await host.Call("getMusicDirectory",extra:"&id="+id);Assert.Equal("Album",legacy["directory"]!["child"]![0]!["title"]!.ToString());
        root.Enabled=false;await db.SaveChangesAsync();
        var disabled=await host.Call("getFolderList");Assert.Empty(disabled["directory"]!["child"]!.AsArray());
    }
}
