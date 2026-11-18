using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;
using Sonara.Core.Entities;
using Sonara.Data;

namespace Sonara.Api.Podcasts;

public sealed record RetentionEpisode(int Id, int ChannelId, DateTime SortUtc, DateTime DownloadedUtc, bool Protected);
public sealed record RetentionCandidate(int Id, string Title, string Channel, long Bytes, string Reason);
public sealed record RetentionPreview(bool Enabled, int Days, int KeepLatest, int ProtectedCount, IReadOnlyList<RetentionCandidate> Candidates)
{
    public long Bytes => Candidates.Sum(c => c.Bytes);
}
public sealed record RetentionResult(int Removed, int Failed, long Bytes);

public static class RetentionRules
{
    public const int GraceHours = 24;
    public static Dictionary<int, string> Select(IEnumerable<RetentionEpisode> episodes, int days, int keepLatest, DateTime now)
    {
        var result = new Dictionary<int, string>();
        if (days <= 0 && keepLatest <= 0) return result;
        foreach (var channel in episodes.GroupBy(e => e.ChannelId))
        {
            var ranked = channel.OrderByDescending(e => e.SortUtc).ThenByDescending(e => e.Id).ToList();
            for (var index = 0; index < ranked.Count; index++)
            {
                var e = ranked[index];
                if (e.Protected || e.DownloadedUtc >= now.AddHours(-GraceHours)) continue;
                var old = days > 0 && e.DownloadedUtc < now.AddDays(-days);
                var excess = keepLatest > 0 && index >= keepLatest;
                if (old || excess) result[e.Id] = old && excess ? "age and count" : old ? "age" : "count";
            }
        }
        return result;
    }
    public static bool IsManagedFile(string root, int channel, string? path)
    {
        if (!OwnsPath(root, channel, path)) return false;
        try
        {
            var file = new FileInfo(path!);
            var parent = new DirectoryInfo(file.DirectoryName!);
            return parent.Exists && !parent.Attributes.HasFlag(FileAttributes.ReparsePoint) && parent.LinkTarget is null &&
                !Directory.Exists(path) && file.LinkTarget is null && (!file.Exists || !file.Attributes.HasFlag(FileAttributes.ReparsePoint));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    public static bool OwnsPath(string root, int channel, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return false;
        try
        {
            var expected = Path.Combine(Path.GetFullPath(root), channel.ToString());
            return string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), expected,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}

public sealed class PodcastRetention(SonaraDbContext db, IOptions<SonaraOptions> options, PodcastFileGate gate, ILogger<PodcastRetention> logger)
{
    private string Folder => Path.GetFullPath(string.IsNullOrWhiteSpace(options.Value.Podcasts.Folder)
        ? Path.Combine(options.Value.DataDir, "podcasts") : options.Value.Podcasts.Folder);
    private async Task<(RetentionPreview Preview, List<PodcastEpisode> Episodes)> PlanAsync(CancellationToken ct)
    {
        var settings = options.Value.Podcasts;
        var enabled = settings.RetentionDays > 0 || settings.RetentionKeepLatest > 0;
        if (!enabled) return (new(false, settings.RetentionDays, settings.RetentionKeepLatest, 0, []), []);
        var episodes = await db.PodcastEpisodes.AsNoTracking().Include(e => e.Channel)
            .Where(e => e.Status == PodcastStatus.Completed && e.FilePath != null).ToListAsync(ct);
        var bookmarks = (await db.Bookmarks.Where(b => b.EpisodeId != null).Select(b => b.EpisodeId!.Value).ToListAsync(ct)).ToHashSet();
        var queues = await db.PlayQueues.AsNoTracking().Select(q => q.TrackIds).ToListAsync(ct);
        var queued = queues.SelectMany(q => q).Where(id => id < 0 && id != int.MinValue).Select(id => -id).ToHashSet();
        var now = DateTime.UtcNow;
        var inputs = new List<RetentionEpisode>();
        foreach (var e in episodes)
        {
            var downloaded = e.DownloadedUtc ?? now; // Unknown legacy timestamps are never guessed to be old.
            var safe = RetentionRules.IsManagedFile(Folder, e.ChannelId, e.FilePath);
            e.Size = 0; // Preview/reclaimed byte totals use current files, not stale enclosure lengths.
            try
            {
                if (safe && File.Exists(e.FilePath))
                {
                    var file = new FileInfo(e.FilePath!);
                    e.Size = file.Length;
                    safe = !file.Attributes.HasFlag(FileAttributes.ReparsePoint) &&
                        !new DirectoryInfo(file.DirectoryName!).Attributes.HasFlag(FileAttributes.ReparsePoint);
                    if (e.DownloadedUtc is null) downloaded = file.LastWriteTimeUtc;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { safe = false; }
            inputs.Add(new(e.Id, e.ChannelId, e.PublishUtc ?? downloaded, downloaded,
                e.Pinned || bookmarks.Contains(e.Id) || queued.Contains(e.Id) || !safe));
        }
        var selected = RetentionRules.Select(inputs, settings.RetentionDays, settings.RetentionKeepLatest, now);
        var candidates = episodes.Where(e => selected.ContainsKey(e.Id)).Select(e => new RetentionCandidate(e.Id, e.Title, e.Channel.Title, Math.Max(0, e.Size), selected[e.Id])).ToList();
        return (new(enabled, settings.RetentionDays, settings.RetentionKeepLatest, inputs.Count(e => e.Protected || e.DownloadedUtc >= now.AddHours(-RetentionRules.GraceHours)), candidates), episodes);
    }
    public async Task<RetentionPreview> PreviewAsync(CancellationToken ct = default)
    {
        using var lease = await gate.EnterAsync(ct);
        return (await PlanAsync(ct)).Preview;
    }
    public async Task<RetentionResult> RunAsync(CancellationToken ct = default)
    {
        using var lease = await gate.EnterAsync(ct);
        var (preview, _) = await PlanAsync(ct);
        if (!preview.Enabled) return new(0, 0, 0);
        var removed = 0; var failed = 0; long bytes = 0;
        foreach (var item in preview.Candidates)
        {
            ct.ThrowIfCancellationRequested();
            // Recheck current protection just before deletion: previews are never deletion instructions.
            db.ChangeTracker.Clear();
            var e = await db.PodcastEpisodes.FirstOrDefaultAsync(e => e.Id == item.Id, ct);
            if (e is null || e.Status != PodcastStatus.Completed || e.Pinned || !RetentionRules.IsManagedFile(Folder, e.ChannelId, e.FilePath)) continue;
            if (await db.Bookmarks.AnyAsync(b => b.EpisodeId == e.Id, ct)) continue;
            if (await db.PlayQueues.AnyAsync(q => q.TrackIds.Contains(-e.Id), ct)) continue;
            try
            {
                if (File.Exists(e.FilePath)) File.Delete(e.FilePath!);
                e.FilePath = null; e.Status = PodcastStatus.New; e.ErrorMessage = null;
                await db.SaveChangesAsync(ct);
                removed++; bytes += item.Bytes;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++; logger.LogWarning("Could not remove podcast episode {Id}: {Message}", e.Id, ex.Message);
            }
        }
        logger.LogInformation("Podcast retention: removed {Removed}, failed {Failed}, bytes {Bytes}", removed, failed, bytes);
        return new(removed, failed, bytes);
    }
}
