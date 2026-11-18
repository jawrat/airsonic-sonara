using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapPlaylists(RouteGroupBuilder rest)
    {
        Map(rest, "getPlaylists", GetPlaylists);
        Map(rest, "getPlaylist", GetPlaylist);
        Map(rest, "createPlaylist", CreatePlaylist);
        Map(rest, "updatePlaylist", UpdatePlaylist);
        Map(rest, "deletePlaylist", DeletePlaylist);
    }

    private static IResult Forbidden() => SubsonicResult.Error(50, "User is not authorized for the given operation.");

    /// <summary>All values of a (possibly repeated) parameter, from the query string and any form body.</summary>
    private static List<string> Values(HttpContext http, string name)
    {
        var values = http.Request.Query[name].ToList();

        return values.Where(v => v is not null).Select(v => v!).ToList();
    }

    private static List<int> IntValues(HttpContext http, string name) =>
        Values(http, name).Select(v => int.TryParse(v, out var n) ? (int?)n : null)
            .Where(n => n.HasValue).Select(n => n!.Value).ToList();

    private static JsonObject PlaylistJson(Playlist p, int songCount, int duration) => Obj(
        ("id", p.Id.ToString()),
        ("name", p.Name),
        ("owner", p.Owner?.Username),
        ("public", p.IsPublic),
        ("songCount", songCount),
        ("duration", duration),
        ("created", p.CreatedUtc),
        ("changed", p.ChangedUtc));

    private static async Task<JsonObject> PlaylistWithEntries(SonaraDbContext db, int playlistId, StarLookup stars)
    {
        var playlist = await db.Playlists.AsNoTracking().Include(p => p.Owner)
            .FirstAsync(p => p.Id == playlistId);
        var tracks = (await db.PlaylistEntries.AsNoTracking()
                .Where(e => e.PlaylistId == playlistId)
                .Include(e => e.Track).ThenInclude(t => t.Artist)
                .Include(e => e.Track).ThenInclude(t => t.Album)
                .OrderBy(e => e.Position)
                .ToListAsync())
            .Select(e => e.Track).ToList();

        var json = PlaylistJson(playlist, tracks.Count, tracks.Sum(t => t.DurationSeconds));
        json["entry"] = Arr(tracks.Select(t => stars.Song(SongJson(t))));
        return json;
    }

    private static async Task<IResult> GetPlaylists(string? username, HttpContext http, SonaraDbContext db)
    {
        var user = SubsonicAuthFilter.CurrentUser(http)!;

        if (username is not null && !user.IsAdmin && !username.Equals(user.Username, StringComparison.OrdinalIgnoreCase)) return Forbidden();
        var query = db.Playlists.AsNoTracking().Include(p => p.Owner)
            .Where(p => p.OwnerId == user.Id || p.IsPublic || user.IsAdmin);
        if (username is not null) query = query.Where(p => p.Owner.Username.ToLower() == username.ToLower());
        var playlists = await query.OrderBy(p => p.Name).ToListAsync();

        var ids = playlists.Select(p => p.Id).ToList();
        var stats = await db.PlaylistEntries.AsNoTracking()
            .Where(e => ids.Contains(e.PlaylistId))
            .GroupBy(e => e.PlaylistId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Duration = g.Sum(e => e.Track.DurationSeconds) })
            .ToDictionaryAsync(x => x.Id);

        return SubsonicResult.Ok(Obj(("playlists", Obj(
            ("playlist", Arr(playlists.Select(p =>
                PlaylistJson(p, stats.GetValueOrDefault(p.Id)?.Count ?? 0, stats.GetValueOrDefault(p.Id)?.Duration ?? 0))))))));
    }

    private static async Task<IResult> GetPlaylist(string? id, HttpContext http, SonaraDbContext db)
    {
        if (!int.TryParse(id, out var playlistId)) return MissingParam();
        var user = SubsonicAuthFilter.CurrentUser(http)!;

        var playlist = await db.Playlists.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playlistId);
        if (playlist is null) return NotFound();
        if (!PlaylistRules.CanView(playlist.OwnerId, playlist.IsPublic, user.Id, user.IsAdmin)) return Forbidden();

        return SubsonicResult.Ok(Obj(("playlist", await PlaylistWithEntries(db, playlistId, await Stars(http, db)))));
    }

    /// <summary>Creates a playlist (name + songId...), or replaces the songs of an existing one (playlistId + songId...).</summary>
    private static async Task<IResult> CreatePlaylist(
        string? playlistId, string? name, HttpContext http, SonaraDbContext db)
    {
        var user = SubsonicAuthFilter.CurrentUser(http)!;
        var songIds = IntValues(http, "songId");

        Playlist playlist;
        if (!string.IsNullOrEmpty(playlistId))
        {
            if (!int.TryParse(playlistId, out var existingId)) return MissingParam();
            var found = await db.Playlists.Include(p => p.Entries).FirstOrDefaultAsync(p => p.Id == existingId);
            if (found is null) return NotFound();
            if (!PlaylistRules.CanModify(found.OwnerId, user.Id, user.IsAdmin)) return Forbidden();
            playlist = found;
            if (!string.IsNullOrWhiteSpace(name)) playlist.Name = name.Trim();
            db.PlaylistEntries.RemoveRange(playlist.Entries);
            playlist.Entries.Clear();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(name)) return MissingParam();
            playlist = new Playlist { Name = name.Trim(), OwnerId = user.Id };
            db.Playlists.Add(playlist);
        }

        var valid = await ExistingTrackIds(db, songIds);
        AddEntries(playlist, songIds.Where(valid.Contains), startAt: 0);
        playlist.ChangedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return SubsonicResult.Ok(Obj(("playlist", await PlaylistWithEntries(db, playlist.Id, await Stars(http, db)))));
    }

    private static async Task<IResult> UpdatePlaylist(
        string? playlistId, string? name, bool? @public, HttpContext http, SonaraDbContext db)
    {
        if (!int.TryParse(playlistId, out var id)) return MissingParam();
        var user = SubsonicAuthFilter.CurrentUser(http)!;

        var playlist = await db.Playlists.Include(p => p.Entries).FirstOrDefaultAsync(p => p.Id == id);
        if (playlist is null) return NotFound();
        if (!PlaylistRules.CanModify(playlist.OwnerId, user.Id, user.IsAdmin)) return Forbidden();

        if (!string.IsNullOrWhiteSpace(name)) playlist.Name = name.Trim();
        if (@public.HasValue) playlist.IsPublic = @public.Value;

        var remove = IntValues(http, "songIndexToRemove");
        var add = IntValues(http, "songIdToAdd");
        if (remove.Count > 0 || add.Count > 0)
        {
            var current = playlist.Entries.OrderBy(e => e.Position).Select(e => e.TrackId).ToList();
            var valid = await ExistingTrackIds(db, add);
            var next = PlaylistRules.Edit(current, remove, add.Where(valid.Contains));

            db.PlaylistEntries.RemoveRange(playlist.Entries);
            playlist.Entries.Clear();
            AddEntries(playlist, next, startAt: 0);
        }

        playlist.ChangedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return SubsonicResult.Ok();
    }

    private static async Task<IResult> DeletePlaylist(string? id, HttpContext http, SonaraDbContext db)
    {
        if (!int.TryParse(id, out var playlistId)) return MissingParam();
        var user = SubsonicAuthFilter.CurrentUser(http)!;

        var playlist = await db.Playlists.FirstOrDefaultAsync(p => p.Id == playlistId);
        if (playlist is null) return NotFound();
        if (!PlaylistRules.CanModify(playlist.OwnerId, user.Id, user.IsAdmin)) return Forbidden();

        db.Playlists.Remove(playlist); // entries cascade
        await db.SaveChangesAsync();
        return SubsonicResult.Ok();
    }

    private static async Task<HashSet<int>> ExistingTrackIds(SonaraDbContext db, List<int> ids) =>
        ids.Count == 0
            ? new HashSet<int>()
            : (await db.Tracks.Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync()).ToHashSet();

    private static void AddEntries(Playlist playlist, IEnumerable<int> trackIds, int startAt)
    {
        var position = startAt;
        foreach (var trackId in trackIds)
            playlist.Entries.Add(new PlaylistEntry { TrackId = trackId, Position = position++ });
    }
}
