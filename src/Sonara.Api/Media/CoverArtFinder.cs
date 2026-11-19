namespace Sonara.Api.Media;

public sealed record CoverImage(byte[] Data, string ContentType);

/// <summary>
/// Finds cover art for an album. Looks for a conventional image file next to the tracks first
/// (cheap), then falls back to a picture embedded in the audio tags.
/// </summary>
public static class CoverArtFinder
{
    /// <summary>Base names (case-insensitive), in order of preference.</summary>
    private static readonly string[] Names = ["cover", "folder", "front", "album", "albumart"];

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
    };

    public static CoverImage? Find(IEnumerable<string> trackPaths)
    {
        var paths = trackPaths.ToList();

        foreach (var dir in paths.Select(Path.GetDirectoryName).Where(d => d is not null).Distinct())
        {
            var image = FromFolder(dir!);
            if (image is not null) return image;
        }

        foreach (var path in paths)
        {
            var image = FromTag(path);
            if (image is not null) return image;
        }

        return null;
    }

    /// <summary>Prefer artwork from descendant albums; bound expensive embedded tag reads.</summary>
    public static CoverImage? FindRepresentative(IEnumerable<string> trackPaths)
    {
        var paths = trackPaths.ToList();
        foreach (var directory in paths.Select(Path.GetDirectoryName).OfType<string>().Distinct())
        {
            if (FromFolder(directory) is { } image) return image;
        }
        foreach (var path in paths.Take(20))
        {
            if (FromTag(path) is { } image) return image;
        }
        return null;
    }

    public static CoverImage? FromFolder(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return null;

            var candidates = Directory.EnumerateFiles(directory)
                .Select(f => (File: f, Ext: Path.GetExtension(f), Rank: Array.IndexOf(Names, Path.GetFileNameWithoutExtension(f).ToLowerInvariant())))
                .Where(x => x.Rank >= 0 && ImageTypes.ContainsKey(x.Ext))
                .OrderBy(x => x.Rank)
                .ToList();

            foreach (var candidate in candidates)
            {
                try
                {
                    var bytes = File.ReadAllBytes(candidate.File);
                    // File extensions and embedded MIME labels are often wrong. Reject obvious
                    // HTML/text/empty placeholders and use the actual image type on the wire.
                    if (ImageType(bytes) is { } type) return new CoverImage(bytes, type);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // A signature check is deliberately cheap; it does not prove that the entire image
    // decodes. ffmpeg remains responsible for decoding/resizing, and the UI handles failures.
    private static string? ImageType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 24 && data.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (data.Length >= 4 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff) return "image/jpeg";
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (data.Length >= 10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8))) return "image/gif";
        if (data.Length >= 14 && data[..2].SequenceEqual("BM"u8)) return "image/bmp";
        if (data.Length >= 8 && (data[..4].SequenceEqual(new byte[] { 73, 73, 42, 0 }) || data[..4].SequenceEqual(new byte[] { 77, 77, 0, 42 }))) return "image/tiff";
        return null;
    }

    public static CoverImage? FromTag(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var pictures = file.Tag.Pictures;
            if (pictures.Length == 0) return null;

            foreach (var picture in pictures.OrderBy(p => p.Type == TagLib.PictureType.FrontCover ? 0 : 1))
            {
                var data = picture.Data.Data;
                if (ImageType(data) is { } type) return new CoverImage(data, type);
            }
            return null;
        }
        catch (Exception)
        {
            // Unsupported/corrupt file or missing file: just no art.
            return null;
        }
    }
}
