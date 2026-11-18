using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;

namespace Sonara.Api.Backups;

public sealed record BackupFile(string Name, long Bytes, DateTime CreatedUtc);
public sealed record BackupStatus(bool Busy, string Phase, string? LastFile, string? LastError, DateTime? LastCompletedUtc);

public sealed class BackupService(IOptions<SonaraOptions> options, ILogger<BackupService> logger)
{
    private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(1);
    private readonly object _sync = new();
    private BackupStatus _status = new(false, "idle", null, null, null);
    public BackupStatus Status { get { lock (_sync) return _status; } }
    public IReadOnlyList<BackupFile> List()
    {
        var folder = BackupArchive.Folder(options.Value);
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, "sonara-backup-*.zip").Select(p => new FileInfo(p))
            .Where(f => BackupArchive.IsName(f.Name) && !f.Attributes.HasFlag(FileAttributes.ReparsePoint))
            .OrderByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name)
            .Select(f => new BackupFile(f.Name, f.Length, f.LastWriteTimeUtc)).ToList();
    }
    public string? Resolve(string name)
    {
        if (!BackupArchive.IsName(name)) return null;
        var file = new FileInfo(Path.Combine(BackupArchive.Folder(options.Value), name));
        return file.Exists && !file.Attributes.HasFlag(FileAttributes.ReparsePoint) ? file.FullName : null;
    }
    public bool Queue()
    {
        lock (_sync)
        {
            if (_status.Busy) return false;
            _status = _status with { Busy = true, Phase = "queued", LastError = null };
            if (_requests.Writer.TryWrite(true)) return true;
            _status = _status with { Busy = false, Phase = "idle" }; return false;
        }
    }
    public async Task ProcessAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var _ in _requests.Reader.ReadAllAsync(ct))
            {
                lock (_sync) _status = _status with { Phase = "creating" };
                try
                {
                    var file = await BackupArchive.CreateAsync(options.Value, ct);
                    var keep = Math.Clamp(options.Value.Backups.KeepLatest, 1, 365);
                    foreach (var old in List().Skip(keep))
                        try { var path = Resolve(old.Name); if (path is not null) File.Delete(path); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogWarning("Could not prune backup {File}: {Message}", old.Name, ex.Message); }
                    lock (_sync) _status = new(false, "completed", file, null, DateTime.UtcNow);
                    logger.LogInformation("Backup completed: {File}", file);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { lock (_sync) _status = _status with { Busy = false, Phase = "cancelled" }; return; }
                catch (Exception ex)
                {
                    // Do not emit connection strings or exception trees containing configured credentials.
                    var message = ex.Message; var password = new Npgsql.NpgsqlConnectionStringBuilder(options.Value.Database.ConnectionString).Password;
                    if (!string.IsNullOrEmpty(password)) message = message.Replace(password, "[redacted]", StringComparison.Ordinal);
                    lock (_sync) _status = _status with { Busy = false, Phase = "failed", LastError = message[..Math.Min(message.Length, 1500)] };
                    logger.LogWarning("Backup failed: {Message}", Status.LastError);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    public async Task ScheduleAsync(CancellationToken ct)
    {
        var hours = options.Value.Backups.IntervalHours; if (hours <= 0) return;
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            var nextRetry = DateTime.MinValue;
            do
            {
                if (DateTime.UtcNow < nextRetry || Status.Busy) continue;
                try
                {
                    var latest = List().FirstOrDefault()?.CreatedUtc;
                    if (latest is null || DateTime.UtcNow - latest.Value >= TimeSpan.FromHours(hours))
                    { if (Queue()) nextRetry = DateTime.UtcNow.AddMinutes(15); }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { nextRetry = DateTime.UtcNow.AddMinutes(15); logger.LogWarning("Backup scheduling could not inspect the backup folder: {Message}", ex.Message); }
            } while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
}
public sealed class BackupWorker(BackupService backups) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(backups.ProcessAsync(stoppingToken), backups.ScheduleAsync(stoppingToken));
}
