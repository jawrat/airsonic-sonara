using System.Globalization;
using Sonara.Api.Options;

namespace Sonara.Api.Media;

public sealed record TranscodePlan(string Format, int BitRateKbps, string ContentType, string Codec, string Container);

/// <summary>
/// Decides whether a /rest/stream request needs transcoding, and builds the ffmpeg arguments.
/// Pure logic with no I/O, so it is easy to test.
/// </summary>
public static class TranscodePlanner
{
    public const int MinBitRate = 32;
    public const int MaxBitRate = 320;

    private static readonly Dictionary<string, (string Codec, string Container, string ContentType)> Formats =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["mp3"] = ("libmp3lame", "mp3", "audio/mpeg"),
            ["opus"] = ("libopus", "ogg", "audio/ogg"),
            ["aac"] = ("aac", "adts", "audio/aac"),
        };

    private static readonly HashSet<string> LosslessSuffixes =
        new(StringComparer.OrdinalIgnoreCase) { "flac", "wav", "ape", "wv", "aiff", "aif" };

    // Explicit client values override preferences; the administrator's cap is applied afterwards.
    public static (string? Format, int? BitRate) Preferences(Sonara.Core.Entities.User user, string? format, int? bitRate)
    {
        if (string.IsNullOrWhiteSpace(format)) format = string.IsNullOrEmpty(user.PreferredTranscodeFormat) ? null : user.PreferredTranscodeFormat;
        if (bitRate is null && user.PreferredTranscodeBitRate > 0) bitRate = user.PreferredTranscodeBitRate;
        return (format, bitRate);
    }

    /// <summary>Returns null when the original file should be served as-is.</summary>
    /// <param name="format">The client's "format" parameter ("raw" means never transcode).</param>
    /// <param name="maxBitRate">The client's "maxBitRate" in kbps (0 or null means no limit).</param>
    public static TranscodePlan? Plan(
        string trackSuffix, int trackBitRateKbps, string? format, int? maxBitRate, TranscodingOptions options)
    {
        if (!options.Enabled) return null;

        var requested = format?.Trim();
        if (string.Equals(requested, "raw", StringComparison.OrdinalIgnoreCase)) return null;

        var hasFormat = !string.IsNullOrEmpty(requested);
        var cap = maxBitRate is > 0 ? maxBitRate.Value : 0;

        // The client asked for nothing: send the original. This is what keeps FLAC bit-perfect.
        if (!hasFormat && cap == 0) return null;

        var target = hasFormat ? requested! : options.DefaultFormat;
        if (!Formats.TryGetValue(target, out var spec)) return null; // unknown format: fall back to the original

        var sourceFits = cap == 0 || (trackBitRateKbps > 0 && trackBitRateKbps <= cap);
        var sameFormat = string.Equals(trackSuffix, target, StringComparison.OrdinalIgnoreCase);
        if (sourceFits && (sameFormat || !hasFormat)) return null; // re-encoding would only lose quality

        var kbps = Math.Clamp(cap > 0 ? cap : options.DefaultBitRate, MinBitRate, MaxBitRate);

        // Never inflate a lossy source beyond its own bitrate.
        if (trackBitRateKbps > 0 && trackBitRateKbps < kbps && !LosslessSuffixes.Contains(trackSuffix))
            kbps = Math.Max(MinBitRate, trackBitRateKbps);

        return new TranscodePlan(target.ToLowerInvariant(), kbps, spec.ContentType, spec.Codec, spec.Container);
    }

    /// <summary>
    /// Arguments for ffmpeg, to be added one by one to ProcessStartInfo.ArgumentList (no shell involved).
    /// Output goes to stdout.
    /// </summary>
    public static IReadOnlyList<string> BuildArguments(string inputPath, TranscodePlan plan, int startSeconds)
    {
        var args = new List<string> { "-nostdin", "-v", "error" };

        if (startSeconds > 0)
        {
            args.Add("-ss");
            args.Add(startSeconds.ToString(CultureInfo.InvariantCulture));
        }

        args.AddRange([
            "-i", inputPath,
            "-map", "0:a:0",
            "-vn",
            "-map_metadata", "-1",
            "-c:a", plan.Codec,
            "-b:a", $"{plan.BitRateKbps}k",
            "-f", plan.Container,
            "pipe:1",
        ]);
        return args;
    }
}
