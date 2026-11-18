using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapAnnotations(RouteGroupBuilder rest)
    {
        Map(rest, "setRating", SetRating);
        Map(rest, "scrobble", Scrobble);
        Map(rest, "getNowPlaying", GetNowPlaying);
        Map(rest, "getLyrics", GetLyrics);
        Map(rest, "getAvatar", GetAvatar);
    }
    private static async Task AnnotateTracks(List<JsonObject> items, List<int> ids, int user, SonaraDbContext db)
    {
        var keys = ids.Select(i => "t-" + i).ToList();
        var ratings = await db.MediaRatings.AsNoTracking().Where(r => keys.Contains(r.MediaId)).ToListAsync();
        var plays = await db.PlayEvents.AsNoTracking().Where(p => p.UserId == user && ids.Contains(p.TrackId))
            .GroupBy(p => p.TrackId).Select(g => new { Id = g.Key, Count = g.Count(), Last = g.Max(p => p.PlayedUtc) }).ToDictionaryAsync(p => p.Id);
        for (var i = 0; i < items.Count; i++)
        {
            var rs = ratings.Where(r => r.MediaId == "t-" + ids[i]).ToList();
            if (rs.Count > 0) items[i]["averageRating"] = rs.Average(r => r.Rating);
            var mine = rs.FirstOrDefault(r => r.UserId == user);
            if (mine is not null) items[i]["userRating"] = mine.Rating;
            if (plays.TryGetValue(ids[i], out var play)) { items[i]["playCount"] = play.Count; items[i]["played"] = ToNode(play.Last); }
        }
    }
    private static async Task AnnotateAlbums(List<JsonObject> items, List<int> ids, int user, SonaraDbContext db, IQueryable<Track>? visibleTracks = null)
    {
        var stats = await (visibleTracks ?? db.Tracks.AsNoTracking()).Where(t => ids.Contains(t.AlbumId)).GroupBy(t => t.AlbumId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Duration = g.Sum(t => t.DurationSeconds) }).ToDictionaryAsync(s => s.Id);
        for (var i = 0; i < items.Count; i++)
        {
            items[i]["songCount"] = stats.GetValueOrDefault(ids[i])?.Count ?? 0;
            items[i]["duration"] = stats.GetValueOrDefault(ids[i])?.Duration ?? 0;
        }
        var keys = ids.Select(i => "al-" + i).ToList();
        var ratings = await db.MediaRatings.AsNoTracking().Where(r => keys.Contains(r.MediaId)).ToListAsync();
        for (var i = 0; i < items.Count; i++)
        {
            var rs = ratings.Where(r => r.MediaId == "al-" + ids[i]).ToList();
            if (rs.Count > 0) items[i]["averageRating"] = rs.Average(r => r.Rating);
            var mine = rs.FirstOrDefault(r => r.UserId == user);
            if (mine is not null) items[i]["userRating"] = mine.Rating;
        }
    }
    private static async Task ScopeArtistCounts(List<Artist> artists, SonaraDbContext db, IQueryable<Track>? visibleTracks = null)
    {
        var ids = artists.Select(a => a.Id).ToList();
        var rows = await (visibleTracks ?? db.Tracks.AsNoTracking()).Where(t => ids.Contains(t.ArtistId) || ids.Contains(t.Album.ArtistId))
            .Select(t => new { t.ArtistId, AlbumArtistId = t.Album.ArtistId, t.AlbumId }).Distinct().ToListAsync();
        foreach (var a in artists) a.AlbumCount = rows.Where(r => r.ArtistId == a.Id || r.AlbumArtistId == a.Id).Select(r => r.AlbumId).Distinct().Count();
    }
    private static async Task<IResult> SetRating(string? id, int? rating, HttpContext h, SonaraDbContext db)
    {
        if (id is null || rating is null) return MissingParam();
        if (rating is < 0 or > 5) return SubsonicResult.Error(0, "Rating must be from 0 to 5 (0 clears it).");
        string key;
        if (id.StartsWith("al-") && TaggedId(id, "al-") is { } album && await db.Albums.AnyAsync(a => a.Id == album)) key = $"al-{album}";
        else if (id.StartsWith("dir-"))
        {
            var albums = await DirectoryTracks(id, db).Select(t => t.AlbumId).Distinct().ToListAsync();
            if (albums.Count == 0) return NotFound();
            key = albums.Count == 1 ? $"al-{albums[0]}" : id;
        }
        else if (int.TryParse(id, out var track) && await db.Tracks.AnyAsync(t => t.Id == track)) key = $"t-{track}";
        else return NotFound();
        var user = SubsonicAuthFilter.CurrentUser(h)!;
        var row = await db.MediaRatings.FindAsync(user.Id, key);
        if (rating == 0) { if (row is not null) db.Remove(row); }
        else if (row is null) db.Add(new MediaRating { UserId = user.Id, MediaId = key, Rating = rating.Value });
        else row.Rating = rating.Value;
        await db.SaveChangesAsync();
        return SubsonicResult.Ok();
    }
    private static async Task<IResult> Scrobble(bool? submission, string? c, HttpContext h, SonaraDbContext db)
    {
        var raw = Values(h, "id"); var times = Values(h, "time");
        if (raw.Count == 0) return MissingParam();
        if (raw.Count > 500 || (times.Count != 0 && times.Count != raw.Count)) return SubsonicResult.Error(0, "Provide at most 500 IDs and one timestamp per ID.");
        var ids = new List<int>(); var dates = new List<DateTime>();
        for (var i = 0; i < raw.Count; i++)
        {
            if (!int.TryParse(raw[i], out var id) || id <= 0) return NotFound();
            ids.Add(id);
            if (times.Count == 0) dates.Add(DateTime.UtcNow);
            else
            {
                if (!long.TryParse(times[i], out var ms)) return SubsonicResult.Error(0, "Invalid timestamp.");
                try { dates.Add(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime); }
                catch (ArgumentOutOfRangeException) { return SubsonicResult.Error(0, "Invalid timestamp."); }
            }
        }
        if ((await ExistingTrackIds(db, ids)).Count != ids.Distinct().Count()) return NotFound();
        var user = SubsonicAuthFilter.CurrentUser(h)!;
        if (submission ?? true)
        {
            for (var i = 0; i < ids.Count; i++) db.PlayEvents.Add(new PlayEvent { UserId = user.Id, TrackId = ids[i], PlayedUtc = dates[i] });
        }
        var client = string.IsNullOrWhiteSpace(c) ? "unknown" : c[..Math.Min(c.Length, 64)];
        var now = await db.NowPlayingEntries.FindAsync(user.Id, client);
        if (now is null) { now = new NowPlayingEntry { UserId = user.Id, Client = client }; db.Add(now); }
        now.TrackId = ids[^1]; now.StartedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return SubsonicResult.Ok();
    }
    private static async Task<IResult> GetNowPlaying(HttpContext h, SonaraDbContext db)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-15);
        var rows = await db.NowPlayingEntries.AsNoTracking().Where(n => n.StartedUtc >= cutoff).ToListAsync();
        var ids = rows.Select(n => n.TrackId).Distinct().ToList();
        var tracks = await WithTags(db.Tracks).Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Username);
        var items = new List<JsonNode>();
        foreach (var row in rows)
        {
            if (!tracks.TryGetValue(row.TrackId, out var t) || (DateTime.UtcNow - row.StartedUtc).TotalSeconds > Math.Max(t.DurationSeconds, 60) + 60) continue;
            var j = SongJson(t); j["username"] = users.GetValueOrDefault(row.UserId); j["minutesAgo"] = Math.Max(0, (int)(DateTime.UtcNow - row.StartedUtc).TotalMinutes);
            j["playerId"] = row.UserId; j["playerName"] = row.Client; items.Add(j);
        }
        return SubsonicResult.Ok(Obj(("nowPlaying", Obj(("entry", Arr(items))))));
    }
    private static async Task<IResult> GetLyrics(string? artist, string? title, SonaraDbContext db)
    {
        if (string.IsNullOrWhiteSpace(title)) return SubsonicResult.Ok(Obj(("lyrics", new JsonObject())));
        var q = WithTags(db.Tracks).Where(t => t.Title.ToLower() == title.ToLower());
        if (!string.IsNullOrWhiteSpace(artist)) q = q.Where(t => t.Artist.Name.ToLower() == artist.ToLower());
        var track = await q.OrderBy(t => t.Id).FirstOrDefaultAsync();
        string? lyrics = null;
        if (track is not null)
        {
            foreach (var ext in new[] { ".lrc", ".txt" })
            {
                var sidecar = Path.ChangeExtension(track.Path, ext);
                if (!File.Exists(sidecar) || new FileInfo(sidecar).Length > 1024 * 1024) continue;
                try { lyrics = await File.ReadAllTextAsync(sidecar); break; } catch (IOException) { }
            }
            if (lyrics is null && File.Exists(track.Path))
                try { using var file = TagLib.File.Create(track.Path); lyrics = file.Tag.Lyrics; } catch (Exception ex) when (ex is IOException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException) { }
        }
        return SubsonicResult.Ok(Obj(("lyrics", Obj(("artist", track?.Artist.Name ?? artist), ("title", track?.Title ?? title), ("value", lyrics)))));
    }
    private static async Task<IResult> ExportPodcastOpml(SonaraDbContext db)
    {
        var channels = await db.PodcastChannels.AsNoTracking().OrderBy(c => c.Title).ToListAsync();
        var xml = new XDocument(new XElement("opml", new XAttribute("version", "2.0"),
            new XElement("head", new XElement("title", "Sonara Podcasts")),
            new XElement("body", channels.Select(c => new XElement("outline", new XAttribute("type", "rss"), new XAttribute("text", c.Title), new XAttribute("title", c.Title), new XAttribute("xmlUrl", c.Url))))));
        return Results.File(System.Text.Encoding.UTF8.GetBytes(xml.ToString()), "text/x-opml", "podcasts.opml");
    }
}
