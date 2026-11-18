using Microsoft.EntityFrameworkCore;
using Sonara.Api.Podcasts;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private sealed record ImportItem(string Title, string Url, string Status, string? Message = null);
    private static async Task<IResult> ImportPodcastOpml(string? opml, bool? dryRun, HttpContext h, SonaraDbContext db, IPodcastQueue queue)
    {
        if (string.IsNullOrWhiteSpace(opml)) return MissingParam();
        IReadOnlyList<OpmlFeed> feeds;
        try { feeds = PodcastOpml.Parse(opml); }
        catch (PodcastException ex) { return SubsonicResult.Error(0, ex.Message); }
        var preview = dryRun ?? true;
        var selected = Values(h, "selectedUrl").Select(PodcastOpml.Normalize).ToList();
        if (!preview && (selected.Count == 0 || selected.Any(url => url is null)))
            return SubsonicResult.Error(0, "Select at least one podcast from the import preview.");
        var available = feeds.Where(f => f.Error is null).Select(f => f.Url).ToHashSet(StringComparer.Ordinal);
        if (!preview && selected.Any(url => !available.Contains(url!)))
            return SubsonicResult.Error(0, "A selected podcast is not in this OPML file. Preview again.");
        var chosen = selected.Where(url => url is not null).Select(url => url!).ToHashSet(StringComparer.Ordinal);
        var known = (await db.PodcastChannels.AsNoTracking().Select(c => c.Url).ToListAsync(h.RequestAborted))
            .Select(url => PodcastOpml.Normalize(url) ?? url).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<ImportItem>(); var channels = new List<PodcastChannel>();
        foreach (var feed in feeds)
        {
            if (feed.Error is not null) { results.Add(new(feed.Title, feed.Url, "invalid", feed.Error)); continue; }
            if (!seen.Add(feed.Url)) { results.Add(new(feed.Title, feed.Url, "duplicate")); continue; }
            if (known.Contains(feed.Url)) { results.Add(new(feed.Title, feed.Url, "existing")); continue; }
            if (!preview && !chosen.Contains(feed.Url)) { results.Add(new(feed.Title, feed.Url, "notSelected")); continue; }
            results.Add(new(feed.Title, feed.Url, preview ? "ready" : "queued"));
            if (!preview) channels.Add(new PodcastChannel { Url = feed.Url, Title = feed.Title, Status = PodcastStatus.New });
        }
        if (channels.Count > 0)
        {
            db.PodcastChannels.AddRange(channels);
            try { await db.SaveChangesAsync(h.RequestAborted); }
            catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            { return SubsonicResult.Error(0, "Subscriptions changed during import. Preview again and retry; no new subscriptions were saved by this request."); }
            foreach (var channel in channels) queue.Enqueue(PodcastJob.Refresh(channel.Id));
        }
        return SubsonicResult.Ok(Obj(("podcastImport", Obj(("dryRun", preview), ("ready", results.Count(r => r.Status == "ready")),
            ("queued", channels.Count), ("existing", results.Count(r => r.Status == "existing")), ("duplicate", results.Count(r => r.Status == "duplicate")),
            ("invalid", results.Count(r => r.Status == "invalid")),
            ("item", Arr(results.Select(r => Obj(("title", r.Title), ("url", r.Url), ("status", r.Status), ("message", r.Message)))))))));
    }
    private static async Task<IResult> SetPodcastEpisodePinned(string? id, bool? pinned, HttpContext h, SonaraDbContext db, PodcastFileGate gate)
    {
        if (!int.TryParse(id, out var episodeId) || pinned is null) return MissingParam();
        using var lease = await gate.EnterAsync(h.RequestAborted);
        var episode = await db.PodcastEpisodes.FindAsync([episodeId], h.RequestAborted);
        if (episode is null) return NotFound();
        episode.Pinned = pinned.Value;
        await db.SaveChangesAsync(h.RequestAborted);
        return SubsonicResult.Ok();
    }
    private static async Task<IResult> GetPodcastRetentionPreview(HttpContext h, PodcastRetention retention)
    {
        if (!IsAdmin(h)) return Forbidden();
        var preview = await retention.PreviewAsync(h.RequestAborted);
        return SubsonicResult.Ok(Obj(("podcastRetention", Obj(("enabled", preview.Enabled), ("days", preview.Days), ("keepLatest", preview.KeepLatest),
            ("protected", preview.ProtectedCount), ("count", preview.Candidates.Count), ("bytes", preview.Bytes),
            ("episode", Arr(preview.Candidates.Take(200).Select(c => Obj(("id", c.Id), ("title", c.Title), ("channel", c.Channel), ("bytes", c.Bytes), ("reason", c.Reason)))))))));
    }
    private static async Task<IResult> RunPodcastRetention(HttpContext h, PodcastRetention retention)
    {
        if (!IsAdmin(h)) return Forbidden();
        var result = await retention.RunAsync(h.RequestAborted);
        return SubsonicResult.Ok(Obj(("podcastRetentionResult", Obj(("removed", result.Removed), ("failed", result.Failed), ("bytes", result.Bytes)))));
    }
}
