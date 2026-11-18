using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sonara.Api.Podcasts;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapPodcasts(RouteGroupBuilder rest)
    {
        Map(rest, "getPodcasts", GetPodcasts);
        Map(rest, "getNewestPodcasts", GetNewestPodcasts);
        Map(rest, "refreshPodcasts", RefreshPodcasts);
        Map(rest, "createPodcastChannel", CreatePodcastChannel);
        Map(rest, "deletePodcastChannel", DeletePodcastChannel);
        Map(rest, "downloadPodcastEpisode", DownloadPodcastEpisode);
        Map(rest, "deletePodcastEpisode", DeletePodcastEpisode);
        Map(rest, "importPodcastOpml", ImportPodcastOpml);
        Map(rest, "setPodcastEpisodePinned", SetPodcastEpisodePinned);
        Map(rest, "getPodcastRetentionPreview", GetPodcastRetentionPreview);
        Map(rest, "runPodcastRetention", RunPodcastRetention);
        Map(rest, "searchPodcasts", SearchPodcasts); // Sonara extension (Podcast Index lookup)
    }

    /// <summary>The episode as the Subsonic podcast calls describe it.</summary>
    private static JsonObject EpisodeJson(PodcastEpisode e)
    {
        var json = Obj(
            ("id", e.Id.ToString()),
            ("channelId", e.ChannelId.ToString()),
            ("title", e.Title),
            ("description", e.Description),
            ("publishDate", e.PublishUtc),
            ("downloaded", e.DownloadedUtc),
            ("pinned", e.Pinned),
            ("status", PodcastRules.StatusName(e.Status)),
            ("errorMessage", e.ErrorMessage),
            ("isDir", false),
            ("genre", "Podcast"),
            ("coverArt", MediaRef.ChannelId(e.ChannelId)),
            ("size", e.Size),
            ("contentType", e.ContentType),
            ("suffix", e.Suffix),
            ("duration", e.DurationSeconds));
        if (e.Status == PodcastStatus.Completed) json["streamId"] = MediaRef.EpisodeId(e.Id);
        return json;
    }

    /// <summary>The episode shaped like a song, for bookmarks and play queues (its id is the stream id).</summary>
    private static JsonObject EpisodeEntryJson(PodcastEpisode e, string channelTitle) => Obj(
        ("id", MediaRef.EpisodeId(e.Id)),
        ("channelId", e.ChannelId.ToString()),
        ("isDir", false),
        ("title", e.Title),
        ("album", channelTitle),
        ("artist", channelTitle),
        ("coverArt", MediaRef.ChannelId(e.ChannelId)),
        ("duration", e.DurationSeconds),
        ("size", e.Size),
        ("suffix", e.Suffix),
        ("contentType", e.ContentType),
        ("genre", "Podcast"),
        ("type", "podcast"),
        ("isVideo", false));

    private static JsonObject ChannelJson(PodcastChannel c, IEnumerable<PodcastEpisode>? episodes)
    {
        var json = Obj(
            ("id", c.Id.ToString()),
            ("url", c.Url),
            ("title", c.Title),
            ("description", c.Description),
            ("coverArt", MediaRef.ChannelId(c.Id)),
            ("originalImageUrl", c.ImageUrl),
            ("status", PodcastRules.StatusName(c.Status)),
            ("errorMessage", c.ErrorMessage));
        if (episodes is not null) json["episode"] = Arr(episodes.Select(EpisodeJson));
        return json;
    }

    private static async Task<IResult> GetPodcasts(
        string? id, bool? includeEpisodes, HttpContext http, SonaraDbContext db)
    {
        var query = db.PodcastChannels.AsNoTracking().OrderBy(c => c.Title).AsQueryable();
        if (!string.IsNullOrEmpty(id))
        {
            if (!int.TryParse(id, out var channelId)) return MissingParam();
            query = query.Where(c => c.Id == channelId);
        }
        var channels = await query.ToListAsync();

        var episodes = new Dictionary<int, List<PodcastEpisode>>();
        if (includeEpisodes ?? true)
        {
            var ids = channels.Select(c => c.Id).ToList();
            episodes = (await db.PodcastEpisodes.AsNoTracking()
                    .Where(e => ids.Contains(e.ChannelId) && e.Status != PodcastStatus.Deleted)
                    .OrderByDescending(e => e.PublishUtc).ToListAsync())
                .GroupBy(e => e.ChannelId).ToDictionary(g => g.Key, g => g.ToList());
        }

        var withEpisodes = includeEpisodes ?? true;
        return SubsonicResult.Ok(Obj(("podcasts", Obj(("channel", Arr(channels.Select(c =>
            ChannelJson(c, withEpisodes ? episodes.GetValueOrDefault(c.Id) ?? [] : null))))))));
    }

    private static async Task<IResult> GetNewestPodcasts(int? count, SonaraDbContext db)
    {
        var episodes = await db.PodcastEpisodes.AsNoTracking()
            .Where(e => e.Status != PodcastStatus.Deleted)
            .OrderByDescending(e => e.PublishUtc)
            .Take(Math.Clamp(count ?? 20, 1, 200))
            .ToListAsync();
        return SubsonicResult.Ok(Obj(("newestPodcasts", Obj(("episode", Arr(episodes.Select(EpisodeJson)))))));
    }

    /// <summary>Refreshes every podcast, or just one when id is given (Sonara extension). Runs in the background.</summary>
    private static IResult RefreshPodcasts(string? id, HttpContext http, IPodcastQueue queue)
    {
        if (!SubsonicAuthFilter.OperationAllowed(SubsonicAuthFilter.CurrentUser(http)!, "createPodcastChannel")) return Forbidden();
        queue.Enqueue(int.TryParse(id, out var channelId) ? PodcastJob.Refresh(channelId) : PodcastJob.RefreshAll());
        return SubsonicResult.Ok();
    }

    private static async Task<IResult> CreatePodcastChannel(string? url, HttpContext http, PodcastService podcasts)
    {
        if (!SubsonicAuthFilter.OperationAllowed(SubsonicAuthFilter.CurrentUser(http)!, "createPodcastChannel")) return Forbidden();
        if (string.IsNullOrWhiteSpace(url)) return MissingParam();
        try
        {
            var channel = await podcasts.SubscribeAsync(url, http.RequestAborted);
            return SubsonicResult.Ok(Obj(("podcastChannel", ChannelJson(channel, null))));
        }
        catch (PodcastException ex)
        {
            return SubsonicResult.Error(0, ex.Message);
        }
    }

    private static async Task<IResult> DeletePodcastChannel(
        string? id, HttpContext http, SonaraDbContext db, PodcastService podcasts)
    {
        if (!SubsonicAuthFilter.OperationAllowed(SubsonicAuthFilter.CurrentUser(http)!, "createPodcastChannel")) return Forbidden();
        if (!int.TryParse(id, out var channelId)) return MissingParam();
        var channel = await db.PodcastChannels.FirstOrDefaultAsync(c => c.Id == channelId);
        if (channel is null) return NotFound();
        try { await podcasts.DeleteChannelAsync(channel); return SubsonicResult.Ok(); }
        catch (PodcastException ex) { return SubsonicResult.Error(0, ex.Message); }
    }

    private static async Task<IResult> DownloadPodcastEpisode(
        string? id, HttpContext http, SonaraDbContext db, IPodcastQueue queue)
    {
        if (!SubsonicAuthFilter.OperationAllowed(SubsonicAuthFilter.CurrentUser(http)!, "createPodcastChannel")) return Forbidden();
        if (!int.TryParse(id, out var episodeId)) return MissingParam();
        var episode = await db.PodcastEpisodes.FirstOrDefaultAsync(e => e.Id == episodeId);
        if (episode is null) return NotFound();

        if (episode.Status != PodcastStatus.Completed)
        {
            episode.Status = PodcastStatus.Downloading; // shown straight away; the worker does the rest
            episode.ErrorMessage = null;
            await db.SaveChangesAsync();
            queue.Enqueue(PodcastJob.Download(episodeId));
        }
        return SubsonicResult.Ok();
    }

    private static async Task<IResult> DeletePodcastEpisode(
        string? id, HttpContext http, SonaraDbContext db, PodcastService podcasts)
    {
        if (!SubsonicAuthFilter.OperationAllowed(SubsonicAuthFilter.CurrentUser(http)!, "createPodcastChannel")) return Forbidden();
        if (!int.TryParse(id, out var episodeId)) return MissingParam();
        var episode = await db.PodcastEpisodes.FirstOrDefaultAsync(e => e.Id == episodeId);
        if (episode is null) return NotFound();
        try { await podcasts.DeleteEpisodeFileAsync(episode); return SubsonicResult.Ok(); }
        catch (PodcastException ex) { return SubsonicResult.Error(0, ex.Message); }
    }

    private static async Task<IResult> SearchPodcasts(string? query, HttpContext http, PodcastIndexClient index)
    {
        if (!SubsonicAuthFilter.OperationAllowed(SubsonicAuthFilter.CurrentUser(http)!, "createPodcastChannel")) return Forbidden();
        if (string.IsNullOrWhiteSpace(query)) return MissingParam();
        if (!index.Configured)
        {
            return SubsonicResult.Error(0,
                "Podcast search is not set up. Set Sonara__Podcasts__PodcastIndex__ApiKey and " +
                "Sonara__Podcasts__PodcastIndex__ApiSecret (free keys from podcastindex.org).");
        }

        try
        {
            var results = await index.SearchAsync(query.Trim(), http.RequestAborted);
            return SubsonicResult.Ok(Obj(("podcastSearch", Obj(("feed", Arr(results.Select(r => Obj(
                ("title", r.Title),
                ("author", r.Author),
                ("description", r.Description),
                ("url", r.FeedUrl),
                ("image", r.ImageUrl)))))))));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       or InvalidDataException)
        {
            return SubsonicResult.Error(0, "Podcast search failed: " + ex.Message);
        }
    }
}
