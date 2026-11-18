using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;
using Sonara.Data;

namespace Sonara.Api.Scanning;

/// <summary>
/// Drives scanning: one pass at startup, a periodic rescan, and (optionally) file system watchers
/// that trigger a scan once changes settle. Watchers don't see changes on network shares or some
/// container mounts, which is what the periodic rescan is for.
/// </summary>
public sealed class ScannerService(
    IMediaScanner scanner,
    IServiceScopeFactory scopeFactory,
    IOptions<SonaraOptions> options,
    ILogger<ScannerService> logger) : BackgroundService
{
    private readonly ScannerOptions _options = options.Value.Scanner;
    private readonly List<FileSystemWatcher> _watchers = [];
    private ChangeDebouncer? _debouncer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting (and migrations/seeding, which run before it) first.
        await Task.Yield();

        if (_options.ScanOnStartup) scanner.TryStart(fullScan: false);

        if (_options.Watch) await StartWatchersAsync(stoppingToken);

        if (_options.RescanIntervalMinutes <= 0)
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.RescanIntervalMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (scanner.TryStart(fullScan: false)) logger.LogInformation("Periodic rescan started");
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task StartWatchersAsync(CancellationToken ct)
    {
        _debouncer = new ChangeDebouncer(TimeSpan.FromSeconds(Math.Max(_options.WatchDebounceSeconds, 1)), OnQuiet);

        List<string> paths;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
            paths = await db.MusicFolders.AsNoTracking().Where(f => f.Enabled).Select(f => f.Path).ToListAsync(ct);
        }

        foreach (var path in paths.Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                                   | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Created += (_, e) => Changed(e.FullPath);
                watcher.Changed += (_, e) => Changed(e.FullPath);
                watcher.Deleted += (_, e) => Changed(e.FullPath);
                watcher.Renamed += (_, e) => { Changed(e.OldFullPath); Changed(e.FullPath); };
                watcher.Error += (_, e) =>
                {
                    // Buffer overflow etc.: we may have missed events, so just scan.
                    logger.LogWarning(e.GetException(), "File watcher error on {Path}; scheduling a scan", path);
                    _debouncer?.Signal();
                };
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
                logger.LogInformation("Watching {Path} for changes", path);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // Typically the inotify watch limit (fs.inotify.max_user_watches). The periodic rescan still runs.
                logger.LogWarning(ex, "Could not watch {Path}; relying on periodic rescans", path);
            }
        }
    }

    private void Changed(string? path)
    {
        if (ScanRules.IsRelevantChange(path)) _debouncer?.Signal();
    }

    private void OnQuiet()
    {
        // A scan may already be running (and could have missed what just changed): try again later.
        if (!scanner.TryStart(fullScan: false)) _debouncer?.Signal();
        else logger.LogInformation("Library change detected; scan started");
    }

    public override void Dispose()
    {
        foreach (var w in _watchers) w.Dispose();
        _debouncer?.Dispose();
        base.Dispose();
    }
}
