using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;
using Sonara.Core.Entities;
using Sonara.Data;

namespace Sonara.Api.Podcasts;

public enum PodcastJobKind
{
    Download,
    Refresh,
    RefreshAll,
    Retention,
}

public readonly record struct PodcastJob(PodcastJobKind Kind, int Id)
{
    public static PodcastJob Download(int episodeId) => new(PodcastJobKind.Download, episodeId);
    public static PodcastJob Refresh(int channelId) => new(PodcastJobKind.Refresh, channelId);
    public static PodcastJob Retention() => new(PodcastJobKind.Retention, 0);
    public static PodcastJob RefreshAll() => new(PodcastJobKind.RefreshAll, 0);
}

public interface IPodcastQueue
{
    void Enqueue(PodcastJob job);
}

/// <summary>
/// Runs podcast downloads one at a time (kind to bandwidth and disk), plus on-request and periodic refreshes.
/// </summary>
public sealed class PodcastWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SonaraOptions> options,
    ILogger<PodcastWorker> logger) : BackgroundService, IPodcastQueue
{
    private readonly Channel<PodcastJob> _jobs = Channel.CreateUnbounded<PodcastJob>();
    private readonly HashSet<PodcastJob> _pending = [];

    public void Enqueue(PodcastJob job)
    {
        // Don't queue the same download twice.
        lock (_pending)
        {
            if (!_pending.Add(job)) return;
        }
        _jobs.Writer.TryWrite(job);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        await ResetInterruptedDownloadsAsync(stoppingToken);
        await ResumePendingSubscriptionsAsync(stoppingToken);

        var interval = options.Value.Podcasts.RefreshIntervalMinutes;
        var refresher = interval > 0 ? RefreshLoopAsync(TimeSpan.FromMinutes(interval), stoppingToken) : Task.CompletedTask;
        var settings = options.Value.Podcasts;
        var retention = settings.RetentionDays > 0 || settings.RetentionKeepLatest > 0
            ? RetentionLoopAsync(stoppingToken) : Task.CompletedTask;
        await Task.WhenAll(ProcessJobsAsync(stoppingToken), refresher, retention);
    }

    private async Task ProcessJobsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var job in _jobs.Reader.ReadAllAsync(ct))
            {
                lock (_pending) _pending.Remove(job);
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<PodcastService>();
                    var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();

                    var retention = scope.ServiceProvider.GetRequiredService<PodcastRetention>();
                    switch (job.Kind)
                    {
                        case PodcastJobKind.Download:
                            await service.DownloadAsync(job.Id, ct);
                            break;
                        case PodcastJobKind.Refresh:
                            if (await db.PodcastChannels.FindAsync([job.Id], ct) is { } channel)
                                await service.RefreshAsync(channel, ct);
                            break;
                        case PodcastJobKind.RefreshAll:
                            await service.RefreshAllAsync(ct);
                            break;
                        case PodcastJobKind.Retention:
                            await retention.RunAsync(ct);
                            break;
                    }
                    if (job.Kind is PodcastJobKind.Download or PodcastJobKind.Refresh or PodcastJobKind.RefreshAll)
                        await retention.RunAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (PodcastException ex)
                {
                    logger.LogWarning("Podcast job {Kind} {Id} failed: {Message}", job.Kind, job.Id, ex.Message);
                }
                catch (Exception ex)
                {
                    // A bad job must not kill the worker.
                    logger.LogError(ex, "Podcast job {Kind} {Id} crashed", job.Kind, job.Id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task RetentionLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), ct);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
            do { Enqueue(PodcastJob.Retention()); } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task ResumePendingSubscriptionsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        foreach (var id in await db.PodcastChannels.Where(c => c.Status == PodcastStatus.New && c.LastRefreshUtc == null).Select(c => c.Id).ToListAsync(ct))
            Enqueue(PodcastJob.Refresh(id));
    }

    private async Task RefreshLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        try
        {
            // Give startup (migrations, first scan) a moment before hitting the network.
            await Task.Delay(TimeSpan.FromMinutes(2), ct);
            using var timer = new PeriodicTimer(interval);
            do
            {
                Enqueue(PodcastJob.RefreshAll());
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    /// <summary>Anything left "downloading" by a crash or restart goes back to "new" and is retried on demand.</summary>
    private async Task ResetInterruptedDownloadsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
            await db.PodcastEpisodes.Where(e => e.Status == PodcastStatus.Downloading)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, PodcastStatus.New), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not reset interrupted podcast downloads");
        }
    }
}
