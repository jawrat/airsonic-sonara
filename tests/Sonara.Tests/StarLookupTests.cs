using System.Text.Json.Nodes;
using Sonara.Api.Subsonic;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Tests;

public class StarLookupTests
{
    private static readonly DateTime At = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    private static StarLookup Lookup() =>
        new(new() { [1] = At }, new() { [2] = At }, new() { [3] = At });

    [Fact]
    public void Starred_items_get_a_timestamp()
    {
        var song = Lookup().Song(Obj(("id", "1")));
        Assert.Equal("2026-03-04T05:06:07Z", song["starred"]!.GetValue<string>());
    }

    [Fact]
    public void Each_kind_looks_in_its_own_table()
    {
        var l = Lookup();
        Assert.Null(l.Song(Obj(("id", "2")))["starred"]);   // 2 is a starred album, not a track
        Assert.NotNull(l.Album(Obj(("id", "2")))["starred"]);
        Assert.NotNull(l.Artist(Obj(("id", "3")))["starred"]);
    }

    [Fact]
    public void Unstarred_or_malformed_ids_are_left_alone()
    {
        var l = Lookup();
        Assert.Null(l.Song(Obj(("id", "99")))["starred"]);
        Assert.Null(l.Song(Obj(("id", "abc")))["starred"]);
        Assert.Null(l.Song(new JsonObject())["starred"]);
    }
}
