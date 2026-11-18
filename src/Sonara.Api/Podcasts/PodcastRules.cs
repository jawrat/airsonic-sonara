namespace Sonara.Api.Podcasts;

public enum MediaKind
{
    Track,
    Episode,
    Channel,
}

/// <summary>What an id from a client refers to: "123" is a song, "pe-12" an episode, "pc-3" a channel.</summary>
public readonly record struct MediaRef(MediaKind Kind, int Id)
{
    public static MediaRef? Parse(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id.StartsWith("pe-", StringComparison.Ordinal) && int.TryParse(id[3..], out var e) && e > 0)
            return new MediaRef(MediaKind.Episode, e);
        if (id.StartsWith("pc-", StringComparison.Ordinal) && int.TryParse(id[3..], out var c) && c > 0)
            return new MediaRef(MediaKind.Channel, c);
        return int.TryParse(id, out var t) && t > 0 ? new MediaRef(MediaKind.Track, t) : null;
    }

    public static string EpisodeId(int episodeId) => "pe-" + episodeId;
    public static string ChannelId(int channelId) => "pc-" + channelId;

    /// <summary>
    /// The saved play queue is a list of integers (one column), so an episode is stored as its negative id.
    /// </summary>
    public int ToQueueCode() => Kind == MediaKind.Episode ? -Id : Id;

    public static MediaRef FromQueueCode(int code) =>
        code < 0 ? new MediaRef(MediaKind.Episode, -code) : new MediaRef(MediaKind.Track, code);
}

public static class PodcastRules
{
    private static readonly HashSet<string> AudioExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "mp3", "m4a", "aac", "ogg", "oga", "opus", "flac", "wav" };

    private static readonly Dictionary<string, string> ExtensionByMime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/mpeg"] = "mp3", ["audio/mp3"] = "mp3",
        ["audio/mp4"] = "m4a", ["audio/x-m4a"] = "m4a", ["audio/m4a"] = "m4a",
        ["audio/aac"] = "aac", ["audio/ogg"] = "ogg", ["audio/opus"] = "opus",
        ["audio/flac"] = "flac", ["audio/x-flac"] = "flac",
        ["audio/wav"] = "wav", ["audio/x-wav"] = "wav",
    };

    private static readonly Dictionary<string, string> MimeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mp3"] = "audio/mpeg", ["m4a"] = "audio/mp4", ["aac"] = "audio/aac", ["ogg"] = "audio/ogg",
        ["oga"] = "audio/ogg", ["opus"] = "audio/ogg", ["flac"] = "audio/flac", ["wav"] = "audio/wav",
    };

    /// <summary>Audio enclosure? Declared audio types pass; unlabeled ones are judged by file extension.</summary>
    public static bool IsAudio(string? mime, string url)
    {
        if (!string.IsNullOrEmpty(mime) && mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return true;
        var generic = string.IsNullOrEmpty(mime) || mime.StartsWith("application/", StringComparison.OrdinalIgnoreCase);
        return generic && AudioExtensions.Contains(ExtensionOf(url));
    }

    /// <summary>File extension (no dot) to store an episode under.</summary>
    public static string SuffixFor(string? mime, string url)
    {
        var fromUrl = ExtensionOf(url);
        if (AudioExtensions.Contains(fromUrl)) return fromUrl.ToLowerInvariant();
        if (mime is not null && ExtensionByMime.TryGetValue(mime.Split(';')[0].Trim(), out var ext)) return ext;
        return "mp3";
    }

    public static string ContentTypeFor(string suffix) =>
        MimeByExtension.GetValueOrDefault(suffix, "application/octet-stream");

    private static string ExtensionOf(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;
        return Path.GetExtension(path).TrimStart('.');
    }

    /// <summary>A title turned into something safe to use in a file name on any OS.</summary>
    public static string SafeFileName(string? title, int maxLength = 80)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var chars = (title ?? "").Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).ToArray();
        var name = new string(chars).Trim().Trim('.', ' ');
        if (name.Length > maxLength) name = name[..maxLength].TrimEnd('.', ' ');
        return name.Length == 0 ? "episode" : name;
    }

    public static string StatusName(Sonara.Core.Entities.PodcastStatus status) => status.ToString().ToLowerInvariant();
}
