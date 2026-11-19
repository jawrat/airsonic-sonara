namespace Sonara.Api.Options;

/// <summary>
/// Bound from the "Sonara" configuration section. Every value can be set with an
/// environment variable, e.g. Sonara__Database__Provider=postgres.
/// </summary>
public sealed class SonaraOptions
{
    public SecurityOptions Security { get; set; } = new();
    public string PublicBaseUrl { get; set; } = "";

    public string DataDir { get; set; } = "data";

    /// <summary>If set and no folders exist yet, this path is registered as the first music folder.</summary>
    public string? MusicFolder { get; set; }

    public string AdminUser { get; set; } = "admin";

    /// <summary>If unset on first run, a random password is generated and logged once.</summary>
    public string? AdminPassword { get; set; }

    public UploadOptions Uploads { get; set; } = new();

    public BackupOptions Backups { get; set; } = new();

    public DatabaseOptions Database { get; set; } = new();

    public TranscodingOptions Transcoding { get; set; } = new();

    public ScannerOptions Scanner { get; set; } = new();

    public PodcastOptions Podcasts { get; set; } = new();
}

public sealed class DatabaseOptions
{
    /// <summary>Required. Npgsql connection string for the PostgreSQL database.</summary>
    public string? ConnectionString { get; set; }
}

/// <summary>
/// Transcoding is selected by client options, stored user preferences or administrator bitrate limits.
/// With no request, preference or limit the original file is sent untouched.
/// </summary>
public sealed class TranscodingOptions
{
    /// <summary>Set false to always serve the original file, whatever the client asks for.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Path or name of the ffmpeg executable.</summary>
    public string FfmpegPath { get; set; } = "ffmpeg";

    /// <summary>Target when a client sends only maxBitRate: mp3, opus or aac.</summary>
    public string DefaultFormat { get; set; } = "mp3";

    /// <summary>Bitrate (kbps) when a client names a format but no maxBitRate.</summary>
    public int DefaultBitRate { get; set; } = 192;

    /// <summary>Simultaneous ffmpeg processes. 0 means one per CPU core.</summary>
    public int MaxConcurrent { get; set; }
}

/// <summary>How and when the library is scanned.</summary>
public sealed class ScannerOptions
{
    /// <summary>Run an incremental scan when the server starts.</summary>
    public bool ScanOnStartup { get; set; } = true;

    /// <summary>Minutes between automatic incremental scans. 0 disables the periodic scan.</summary>
    public int RescanIntervalMinutes { get; set; } = 360;

    /// <summary>Watch music folders for changes and scan shortly after they settle.</summary>
    public bool Watch { get; set; } = true;

    /// <summary>Seconds of quiet after the last file change before a scan starts.</summary>
    public int WatchDebounceSeconds { get; set; } = 15;

    /// <summary>Files whose tags are read at the same time. 0 means one per CPU core (max 8).</summary>
    public int Parallelism { get; set; }
}

public sealed class PodcastOptions
{
    /// <summary>Where episodes are stored. Empty means "podcasts" inside DataDir (/var/podcast in Docker).</summary>
    public string Folder { get; set; } = "";

    /// <summary>Minutes between automatic feed refreshes. 0 disables them.</summary>
    public int RefreshIntervalMinutes { get; set; } = 120;

    /// <summary>
    /// How many of the newest episodes are downloaded when a podcast is added; afterwards every new
    /// episode found by a refresh is downloaded. Set to 0 to download only on request.
    /// </summary>
    public int AutoDownloadLatest { get; set; } = 3;

    /// <summary>Largest episode that will be downloaded, in megabytes.</summary>
    public int MaxEpisodeMegabytes { get; set; } = 1024;

    /// <summary>Maximum compressed cover-image download, in megabytes.</summary>
    public int MaxCoverMegabytes { get; set; } = 20;

    /// <summary>Allow feeds hosted on private/local network addresses. Off by default (SSRF protection).</summary>
    public bool AllowPrivateAddresses { get; set; }

    /// <summary>0 disables age-based cleanup. Age is measured from download time, not publication.</summary>
    public int RetentionDays { get; set; }

    /// <summary>0 disables count-based cleanup. Protected episodes may exceed this limit.</summary>
    public int RetentionKeepLatest { get; set; }

    public PodcastIndexOptions PodcastIndex { get; set; } = new();
}

/// <summary>Credentials for searching podcasts at podcastindex.org (free to register).</summary>
public sealed class PodcastIndexOptions
{
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public bool Configured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ApiSecret);
}

public sealed class BackupOptions
{
    public string Folder { get; set; } = "";
    public int IntervalHours { get; set; }
    public int KeepLatest { get; set; } = 7;
    public string PgDumpPath { get; set; } = "pg_dump";
    public string PgRestorePath { get; set; } = "pg_restore";
}

public sealed class UploadOptions
{
    public int MaxFileMegabytes { get; set; } = 100;
}

public sealed class SecurityOptions
{
    /// <summary>Exact addresses of reverse proxies allowed to supply client IP and HTTPS headers.</summary>
    public string[] TrustedProxyIPs { get; set; } = [];
}
