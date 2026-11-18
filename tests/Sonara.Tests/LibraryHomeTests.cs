using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonara.Data;

namespace Sonara.Tests;

[Collection("PostgreSQL integration")]
public class LibraryHomeTests(ParityHost host) : IClassFixture<ParityHost>
{
    [DatabaseFact]
    public async Task Artist_album_filter_keeps_folder_scope_and_newest_order()
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        var album = await db.Albums.FirstAsync(a => a.Id == host.PublicAlbum);
        var artistId = album.ArtistId;
        var result = await host.Call("getAlbumList2", "admin", "&type=newest&artistId=" + artistId);
        var all = result["albumList2"]!["album"]!.AsArray();
        Assert.NotEmpty(all); Assert.All(all, a => Assert.Equal(artistId.ToString(), a!["artistId"]!.ToString()));
        var dates = all.Select(a => a!["created"]!.GetValue<DateTime>()).ToArray(); Assert.Equal(dates.OrderDescending(), dates);
        result = await host.Call("getAlbumList2", "admin", "&type=newest&artistId=" + artistId + "&musicFolderId=" + host.Folder);
        Assert.Single(result["albumList2"]!["album"]!.AsArray());
        result = await host.Call("getAlbumList2", extra: "&type=newest&artistId=" + artistId + "&musicFolderId=" + host.HiddenFolder);
        Assert.Empty(result["albumList2"]!["album"]!.AsArray());
        result = await host.Call("getAlbumList2", extra: "&type=newest&artistId=not-an-id");
        Assert.Equal("failed", result["status"]!.ToString());
    }
}
