using System.Security.Cryptography;

namespace Sonara.Api.Media;

/// <summary>Pure decisions for cover art resizing: size limits, cache names and ffmpeg arguments.</summary>
public static class CoverArtPlanner
{
    public const int MinSize = 16;
    public const int MaxSize = 1200;

    /// <summary>Null means "send the original" (no or non-positive size requested).</summary>
    public static int? ClampSize(int? requested) =>
        requested is > 0 ? Math.Clamp(requested.Value, MinSize, MaxSize) : null;

    /// <summary>Same image bytes and size always map to the same file, so edits to the art never hit a stale cache.</summary>
    public static string CacheFileName(ReadOnlySpan<byte> image, int size) =>
        $"{Convert.ToHexString(SHA256.HashData(image))[..24].ToLowerInvariant()}-{size}.jpg";

    /// <summary>Scales down to <paramref name="size"/> pixels wide (never up), keeps the aspect ratio, outputs JPEG.</summary>
    public static IReadOnlyList<string> BuildArguments(int size) =>
    [
        "-hide_banner", "-loglevel", "error", "-max_alloc", "67108864", "-protocol_whitelist", "pipe",
        "-i", "pipe:0",
        "-vf", $"scale=w=min({size}\\,iw):h=-2",
        "-frames:v", "1",
        "-pix_fmt", "yuvj420p",
        "-q:v", "3",
        "-f", "mjpeg",
        "pipe:1",
    ];
}
