using System.Globalization;
using Sonara.Api.Options;

namespace Sonara.Api.Settings;

public sealed record SettingDefinition(string Key, string Label, string Kind, int? Min = null, int? Max = null);

/// <summary>Explicit allowlist: connection strings, encryption keys and credentials never enter the settings API.</summary>
public static class SettingCatalog
{
    public static readonly SettingDefinition[] Definitions =
    [
        new("Uploads.MaxFileMegabytes", "Maximum music upload size (MB)", "int", 1, 1024),
        new("Backups.IntervalHours", "Backup interval (hours; 0 disables scheduling)", "int", 0, 8760),
        new("Backups.KeepLatest", "Completed backups to retain", "int", 1, 365),
        new("PublicBaseUrl", "Public URL for share links", "url"),
        new("Scanner.ScanOnStartup", "Scan on startup", "bool"),
        new("Scanner.RescanIntervalMinutes", "Rescan interval (minutes; 0 disables)", "int", 0, 525600),
        new("Scanner.Watch", "Watch folders", "bool"),
        new("Scanner.WatchDebounceSeconds", "Watch quiet time (seconds)", "int", 1, 3600),
        new("Scanner.Parallelism", "Scanner parallelism (0 = automatic)", "int", 0, 64),
        new("Transcoding.Enabled", "Enable transcoding", "bool"),
        new("Transcoding.DefaultFormat", "Default transcode format", "format"),
        new("Transcoding.DefaultBitRate", "Default bitrate (kbps)", "int", 32, 512),
        new("Transcoding.MaxConcurrent", "Concurrent transcodes (0 = automatic)", "int", 0, 64),
        new("Podcasts.RefreshIntervalMinutes", "Podcast refresh interval (minutes; 0 disables)", "int", 0, 525600),
        new("Podcasts.AutoDownloadLatest", "Episodes downloaded on subscription", "int", 0, 100),
        new("Podcasts.RetentionDays", "Remove podcast downloads older than (days; 0 disables)", "int", 0, 36500),
        new("Podcasts.RetentionKeepLatest", "Keep latest downloads per podcast (0 = unlimited)", "int", 0, 10000),
        new("Podcasts.MaxCoverMegabytes", "Maximum podcast cover image size (MB)", "int", 1, 100),
        new("Podcasts.MaxEpisodeMegabytes", "Maximum episode size (MB)", "int", 1, 10240),
    ];
    public static bool Validate(string key, string value)
    {
        var d = Definitions.FirstOrDefault(d => d.Key == key);
        return d?.Kind switch
        {
            "bool" => bool.TryParse(value, out _),
            "int" => int.TryParse(value, out var n) && n >= d.Min && n <= d.Max,
            "format" => value is "mp3" or "opus" or "aac",
            "url" => value == "" || (Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme is "https" or "http" && u.UserInfo == "" && u.Query == "" && u.Fragment == "" && u.AbsolutePath == "/"),
            _ => false,
        };
    }
    public static string Read(SonaraOptions options, string key) => Convert.ToString(Property(options, key).Property.GetValue(Property(options, key).Owner), CultureInfo.InvariantCulture) ?? "";
    public static void Apply(SonaraOptions options, string key, string value)
    {
        if (!Validate(key, value)) return;
        var (owner, property) = Property(options, key);
        property.SetValue(owner, Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture));
    }
    private static (object Owner, System.Reflection.PropertyInfo Property) Property(SonaraOptions options, string key)
    {
        object owner = options; var parts = key.Split('.');
        foreach (var part in parts[..^1]) owner = owner.GetType().GetProperty(part)!.GetValue(owner)!;
        return (owner, owner.GetType().GetProperty(parts[^1])!);
    }
}
