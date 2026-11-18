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

            if (candidates.Count == 0) return null;

            var best = candidates[0];
            return new CoverImage(File.ReadAllBytes(best.File), ImageTypes[best.Ext]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static CoverImage? FromTag(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var pictures = file.Tag.Pictures;
            if (pictures.Length == 0) return null;

            var picture = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover) ?? pictures[0];
            var data = picture.Data.Data;
            if (data.Length == 0) return null;

            var type = picture.MimeType;
            if (string.IsNullOrWhiteSpace(type) || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                type = "image/jpeg";

            return new CoverImage(data, type);
        }
        catch (Exception)
        {
            // Unsupported/corrupt file or missing file: just no art.
            return null;
        }
    }
}
