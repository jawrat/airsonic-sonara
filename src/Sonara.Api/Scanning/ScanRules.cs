namespace Sonara.Api.Scanning;

/// <summary>Pure decisions used by the scanner, separated so they can be unit tested.</summary>
public static class ScanRules
{
    public static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".mp3"] = "audio/mpeg",
            [".flac"] = "audio/flac",
            [".ogg"] = "audio/ogg",
            [".oga"] = "audio/ogg",
            [".opus"] = "audio/ogg",
            [".m4a"] = "audio/mp4",
            [".aac"] = "audio/aac",
            [".wav"] = "audio/wav",
            [".wma"] = "audio/x-ms-wma",
        };

    public static bool IsAudioFile(string path) => ContentTypes.ContainsKey(Path.GetExtension(path));

    /// <summary>
    /// A file system event is worth reacting to if it concerns an audio file, or has no extension
    /// (most likely a directory being created, renamed or deleted).
    /// </summary>
    public static bool IsRelevantChange(string? path)
    {
        if (string.IsNullOrEmpty(path)) return true;
        var ext = Path.GetExtension(path);
        return ext.Length == 0 || ContentTypes.ContainsKey(ext);
    }

    /// <summary>True when the file's tags must be (re)read.</summary>
    public static bool NeedsRead(
        bool fullScan, bool alreadyKnown, long knownSize, DateTime knownModifiedUtc,
        long fileSize, DateTime fileModifiedUtc) =>
        fullScan
        || !alreadyKnown
        || knownSize != fileSize
        || Math.Abs((knownModifiedUtc - fileModifiedUtc).TotalSeconds) >= 1;

    public static int EffectiveParallelism(int configured) =>
        configured > 0 ? Math.Min(configured, 64) : Math.Clamp(Environment.ProcessorCount, 1, 8);
}
