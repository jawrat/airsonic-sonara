using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sonara.Api.Podcasts;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapBookmarks(RouteGroupBuilder rest)
    {
        Map(rest, "getBookmarks", GetBookmarks);
        Map(rest, "createBookmark", CreateBookmark);
        Map(rest, "deleteBookmark", DeleteBookmark);
        Map(rest, "savePlayQueue", SavePlayQueue);
        Map(rest, "getPlayQueue", GetPlayQueue);
    }

    private static async Task<IResult> GetBookmarks(HttpContext http, SonaraDbContext db)
    {
        var user = SubsonicAuthFilter.CurrentUser(http)!;
        var stars = await Stars(http, db);
        var bookmarks = await db.Bookmarks.AsNoTracking()
            .Include(b => b.Track!).ThenInclude(t => t.Artist)
            .Include(b => b.Track!).ThenInclude(t => t.Album)
            .Include(b => b.Episode!).ThenInclude(e => e.Channel)
            .Where(b => b.UserId == user.Id)
            .OrderByDescending(b => b.ChangedUtc)
            .ToListAsync();

        var items = bookmarks
            .Where(b => b.Track is not null || b.Episode is not null)
            .Select(b =>
            {
                var json = Obj(
                    ("position", b.PositionMs),
                    ("username", user.Username),
                    ("comment", b.Comment),
                    ("created", b.CreatedUtc),
                    ("changed", b.ChangedUtc));
                json["entry"] = b.Track is not null
                    ? stars.Song(SongJson(b.Track))
                    : EpisodeEntryJson(b.Episode!, b.Episode!.Channel.Title);
                return json;
            });

        return SubsonicResult.Ok(Obj(("bookmarks", Obj(("bookmark", Arr(items))))));
    }

    /// <summary>The id may be a song ("123") or a podcast episode ("pe-12").</summary>
    private static async Task<IResult> CreateBookmark(
        string? id, long? position, string? comment, HttpContext http, SonaraDbContext db)
    {
        var media = MediaRef.Parse(id);
        if (media is null || position is null) return MissingParam();
        if (media.Value.Kind == MediaKind.Channel) return NotFound();
        var userId = SubsonicAuthFilter.CurrentUser(http)!.Id;
        var isEpisode = media.Value.Kind == MediaKind.Episode;
        var mediaId = media.Value.Id;

        var exists = isEpisode
            ? await db.PodcastEpisodes.AnyAsync(e => e.Id == mediaId)
            : await db.Tracks.AnyAsync(t => t.Id == mediaId);
        if (!exists) return NotFound();

        var bookmark = await db.Bookmarks.FirstOrDefaultAsync(b => b.UserId == userId &&
            (isEpisode ? b.EpisodeId == mediaId : b.TrackId == mediaId));
        if (bookmark is null)
        {
            bookmark = new Bookmark
            {
                UserId = userId,
                TrackId = isEpisode ? null : mediaId,
                EpisodeId = isEpisode ? mediaId : null,
            };
            db.Bookmarks.Add(bookmark);
        }
        bookmark.PositionMs = Math.Max(0, position.Value);
        bookmark.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        bookmark.ChangedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return SubsonicResult.Ok();
    }

    private static async Task<IResult> DeleteBookmark(string? id, HttpContext http, SonaraDbContext db)
    {
        var media = MediaRef.Parse(id);
        if (media is null) return MissingParam();
        var userId = SubsonicAuthFilter.CurrentUser(http)!.Id;
        var mediaId = media.Value.Id;

        if (media.Value.Kind == MediaKind.Episode)
            await db.Bookmarks.Where(b => b.UserId == userId && b.EpisodeId == mediaId).ExecuteDeleteAsync();
        else
            await db.Bookmarks.Where(b => b.UserId == userId && b.TrackId == mediaId).ExecuteDeleteAsync();
        return SubsonicResult.Ok();
    }

    /// <summary>
    /// Saves the queue (id repeated per item; songs or "pe-" episodes), the current item and the position in it (ms).
    /// Stored as integers: songs by id, episodes as their negative id (see <see cref="MediaRef.ToQueueCode"/>).
    /// </summary>
    private static async Task<IResult> SavePlayQueue(
        string? current, int? currentIndex, long? position, string? c, HttpContext http, SonaraDbContext db)
    {
        var user = SubsonicAuthFilter.CurrentUser(http)!;
        var requested = Values(http, "id").Select(MediaRef.Parse)
            .Where(r => r is { Kind: MediaKind.Track or MediaKind.Episode }).Select(r => r!.Value).ToList();
        var currentRef = MediaRef.Parse(current);

        var knownTracks = await ExistingTrackIds(db, requested.Where(r => r.Kind == MediaKind.Track).Select(r => r.Id).Distinct().ToList());
        var episodeIds = requested.Where(r => r.Kind == MediaKind.Episode).Select(r => r.Id).Distinct().ToList();
        var knownEpisodes = episodeIds.Count == 0
            ? new HashSet<int>()
            : (await db.PodcastEpisodes.Where(e => episodeIds.Contains(e.Id)).Select(e => e.Id).ToListAsync()).ToHashSet();

        var (codes, currentCode) = PlayQueueRules.Normalize(
            requested.Where(r => r.Kind == MediaKind.Episode ? knownEpisodes.Contains(r.Id) : knownTracks.Contains(r.Id))
                .Select(r => r.ToQueueCode()),
            currentRef is { Kind: MediaKind.Track or MediaKind.Episode } cr ? cr.ToQueueCode() : null);

        var queue = await db.PlayQueues.FirstOrDefaultAsync(q => q.UserId == user.Id);
        if (queue is null)
        {
            queue = new PlayQueue { UserId = user.Id };
            db.PlayQueues.Add(queue);
        }
        queue.TrackIds = codes;
        queue.CurrentTrackId = currentCode;
        queue.CurrentIndex = currentIndex is >= 0 && currentIndex < codes.Count && codes[currentIndex.Value] == currentCode
            ? currentIndex : currentCode.HasValue ? codes.IndexOf(currentCode.Value) : null;
        queue.PositionMs = Math.Max(0, position ?? 0);
        queue.ChangedBy = c is { Length: > 0 } ? c[..Math.Min(c.Length, 64)] : null;
        queue.ChangedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return SubsonicResult.Ok();
    }

    private static async Task<IResult> GetPlayQueue(HttpContext http, SonaraDbContext db)
    {
        var user = SubsonicAuthFilter.CurrentUser(http)!;
        var queue = await db.PlayQueues.AsNoTracking().FirstOrDefaultAsync(q => q.UserId == user.Id);
        if (queue is null || queue.TrackIds.Count == 0) return SubsonicResult.Ok();

        var refs = queue.TrackIds.Select(MediaRef.FromQueueCode).ToList();
        var trackIds = refs.Where(r => r.Kind == MediaKind.Track).Select(r => r.Id).Distinct().ToList();
        var episodeIds = refs.Where(r => r.Kind == MediaKind.Episode).Select(r => r.Id).Distinct().ToList();

        var tracks = (await db.Tracks.AsNoTracking().Include(t => t.Artist).Include(t => t.Album)
            .Where(t => trackIds.Contains(t.Id)).ToListAsync()).ToDictionary(t => t.Id);
        var episodes = (await db.PodcastEpisodes.AsNoTracking().Include(e => e.Channel)
            .Where(e => episodeIds.Contains(e.Id)).ToListAsync()).ToDictionary(e => e.Id);
        var stars = await Stars(http, db);

        // Items deleted since the queue was saved are skipped.
        var entries = new List<JsonNode>();
        int? visibleCurrentIndex = null;
        for (var i = 0; i < refs.Count; i++)
        {
            var r = refs[i];
            var visible = r.Kind == MediaKind.Track ? tracks.ContainsKey(r.Id) : episodes.ContainsKey(r.Id);
            if (visible && i == queue.CurrentIndex) visibleCurrentIndex = entries.Count;
            if (r.Kind == MediaKind.Track && tracks.TryGetValue(r.Id, out var t))
                entries.Add(stars.Song(SongJson(t)));
            else if (r.Kind == MediaKind.Episode && episodes.TryGetValue(r.Id, out var e))
                entries.Add(EpisodeEntryJson(e, e.Channel.Title));
        }
        if (entries.Count == 0) return SubsonicResult.Ok();

        var json = Obj(
            ("position", queue.PositionMs),
            ("username", user.Username),
            ("changed", queue.ChangedUtc),
            ("changedBy", queue.ChangedBy));

        // CurrentTrackId holds the same code as the list: negative means an episode.
        if (queue.CurrentTrackId is { } code)
        {
            var cur = MediaRef.FromQueueCode(code);
            var exists = cur.Kind == MediaKind.Episode ? episodes.ContainsKey(cur.Id) : tracks.ContainsKey(cur.Id);
            if (exists) json["current"] = cur.Kind == MediaKind.Episode ? MediaRef.EpisodeId(cur.Id) : cur.Id.ToString();
        }
        if (visibleCurrentIndex.HasValue) json["currentIndex"] = visibleCurrentIndex.Value;
        json["entry"] = Arr(entries);
        return SubsonicResult.Ok(Obj(("playQueue", json)));
    }
}
