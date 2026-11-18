using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

/// <summary>One user's stars, used to add a "starred" timestamp to songs, albums and artists.</summary>
public sealed class StarLookup(
    Dictionary<int, DateTime> tracks, Dictionary<int, DateTime> albums, Dictionary<int, DateTime> artists)
{
    public IReadOnlyDictionary<int, DateTime> Tracks => tracks;
    public IReadOnlyDictionary<int, DateTime> Albums => albums;
    public IReadOnlyDictionary<int, DateTime> Artists => artists;

    public JsonObject Song(JsonObject o) => Mark(o, tracks);
    public JsonObject Album(JsonObject o) => Mark(o, albums);
    public JsonObject Artist(JsonObject o) => Mark(o, artists);

    private static JsonObject Mark(JsonObject o, Dictionary<int, DateTime> map)
    {
        if (o["id"]?.GetValue<string>() is { } id && int.TryParse(id, out var n) && map.TryGetValue(n, out var at))
            o["starred"] = ToNode(at);
        return o;
    }

    public static async Task<StarLookup> LoadAsync(SonaraDbContext db, int userId)
    {
        var stars = await db.Stars.AsNoTracking().Where(s => s.UserId == userId).ToListAsync();
        return new StarLookup(
            stars.Where(s => s.TrackId != null).ToDictionary(s => s.TrackId!.Value, s => s.CreatedUtc),
            stars.Where(s => s.AlbumId != null).ToDictionary(s => s.AlbumId!.Value, s => s.CreatedUtc),
            stars.Where(s => s.ArtistId != null).ToDictionary(s => s.ArtistId!.Value, s => s.CreatedUtc));
    }
}

public static partial class SubsonicEndpoints
{
    private static void MapStars(RouteGroupBuilder rest)
    {
        Map(rest, "star", (HttpContext http, SonaraDbContext db) => SetStars(http, db, star: true));
        Map(rest, "unstar", (HttpContext http, SonaraDbContext db) => SetStars(http, db, star: false));
        Map(rest, "getStarred2", GetStarred2);
    }

    private static Task<StarLookup> Stars(HttpContext http, SonaraDbContext db) =>
        StarLookup.LoadAsync(db, SubsonicAuthFilter.CurrentUser(http)!.Id);

    /// <summary>
    /// Songs come from <c>id</c> (our ids are plain numbers, so older clients that send an album or
    /// artist in <c>id</c> are not distinguishable; use albumId/artistId for those).
    /// </summary>
    private static async Task<IResult> SetStars(HttpContext http, SonaraDbContext db, bool star)
    {
        var userId = SubsonicAuthFilter.CurrentUser(http)!.Id;
        var rawIds = Values(http, "id");
        var trackIds = rawIds.Where(v => int.TryParse(v, out _)).Select(int.Parse).Distinct().ToList();
        var albumIds = IntValues(http, "albumId").Distinct().ToList();
        var artistIds = IntValues(http, "artistId").Distinct().ToList();
        albumIds.AddRange(rawIds.Where(v => v.StartsWith("al-")).Select(v => TaggedId(v, "al-")).Where(i => i.HasValue).Select(i => i!.Value));
        artistIds.AddRange(rawIds.Where(v => v.StartsWith("ar-")).Select(v => TaggedId(v, "ar-")).Where(i => i.HasValue).Select(i => i!.Value));
        foreach (var directory in rawIds.Where(v => v.StartsWith("dir-")))
        {
            var candidates = await DirectoryTracks(directory, db).Select(t => new { t.AlbumId, ArtistId = t.Album.ArtistId }).Distinct().ToListAsync();
            if (candidates.Count == 1) albumIds.Add(candidates[0].AlbumId);
            else if (candidates.Select(t => t.ArtistId).Distinct().ToList() is { Count: 1 } artists) artistIds.Add(artists[0]);
        }
        albumIds = albumIds.Distinct().ToList(); artistIds = artistIds.Distinct().ToList();
        if (trackIds.Count + albumIds.Count + artistIds.Count == 0) return MissingParam();

        var existing = await db.Stars.Where(s => s.UserId == userId).ToListAsync();

        if (star)
        {
            var knownTracks = await ExistingIds(db.Tracks.Select(t => t.Id), trackIds);
            var knownAlbums = await ExistingIds(db.Albums.Select(a => a.Id), albumIds);
            var knownArtists = await ExistingIds(db.Artists.Select(a => a.Id), artistIds);

            foreach (var id in knownTracks.Where(id => existing.All(s => s.TrackId != id)))
                db.Stars.Add(new Star { UserId = userId, TrackId = id });
            foreach (var id in knownAlbums.Where(id => existing.All(s => s.AlbumId != id)))
                db.Stars.Add(new Star { UserId = userId, AlbumId = id });
            foreach (var id in knownArtists.Where(id => existing.All(s => s.ArtistId != id)))
                db.Stars.Add(new Star { UserId = userId, ArtistId = id });
        }
        else
        {
            db.Stars.RemoveRange(existing.Where(s =>
                (s.TrackId is { } t && trackIds.Contains(t))
                || (s.AlbumId is { } a && albumIds.Contains(a))
                || (s.ArtistId is { } r && artistIds.Contains(r))));
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // A concurrent request starred the same item: the unique index did its job.
        }
        return SubsonicResult.Ok();
    }

    private static async Task<List<int>> ExistingIds(IQueryable<int> all, List<int> wanted) =>
        wanted.Count == 0 ? [] : await all.Where(id => wanted.Contains(id)).ToListAsync();

    private static async Task<IResult> GetStarred2(HttpContext http, SonaraDbContext db)
    {
        var stars = await Stars(http, db);
        var trackIds = stars.Tracks.Keys.ToList();
        var albumIds = stars.Albums.Keys.ToList();
        var artistIds = stars.Artists.Keys.ToList();

        var songs = await ScopedTracks(http, db).Include(t => t.Artist).Include(t => t.Album)
            .Where(t => trackIds.Contains(t.Id)).ToListAsync();
        var albums = await ScopedAlbums(http, db).Include(a => a.Artist)
            .Where(a => albumIds.Contains(a.Id)).ToListAsync();
        var scoped = ScopedTracks(http, db);
        var artists = await db.Artists.AsNoTracking().Where(a => artistIds.Contains(a.Id) && scoped.Any(t => t.ArtistId == a.Id || t.Album.ArtistId == a.Id)).ToListAsync();
        await ScopeArtistCounts(artists, db);
        var albumItems = albums.OrderBy(a => a.Title).Select(a => stars.Album(AlbumJson(a))).ToList();
        await AnnotateAlbums(albumItems, albums.OrderBy(a => a.Title).Select(a => a.Id).ToList(), SubsonicAuthFilter.CurrentUser(http)!.Id, db);

        return SubsonicResult.Ok(Obj(("starred2", Obj(
            ("artist", Arr(artists.OrderBy(a => a.Name).Select(a => stars.Artist(ArtistJson(a))))),
            ("album", Arr(albumItems)),
            ("song", Arr(songs.OrderByDescending(t => stars.Tracks[t.Id]).Select(t => stars.Song(SongJson(t)))))))));
    }
}
