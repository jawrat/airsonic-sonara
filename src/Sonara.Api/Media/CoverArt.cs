namespace Sonara.Api.Media;

/// <summary>Finds cover art for an album: an image file next to the tracks, else a picture embedded in a tag.</summary>
public static class CoverArt
{
    /// <summary>File name stems (without extension), best first.</summary>
    private static readonly string[] Stems = ["cover", "folder", "front", "albumart", "album"];

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
    };

    /// <summary>Returns the best-matching image in the directory, or null.</summary>
    public static string? FindFolderImage(string directory)
    {
        string[] files;
        try
        {
            if (!Directory.Exists(directory)) return null;
            files = Directory.GetFiles(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return files
            .Where(f => ImageTypes.ContainsKey(Path.GetExtension(f)))
            .Select(f => (Path: f, Rank: Array.FindIndex(
                Stems, s => s.Equals(Path.GetFileNameWithoutExtension(f), StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .Select(x => x.Path)
            .FirstOrDefault();
    }

    public static string ContentTypeFor(string imagePath) =>
        ImageTypes.GetValueOrDefault(Path.GetExtension(imagePath), "application/octet-stream");

    /// <summary>Reads the front-cover picture (or the first picture) embedded in an audio file's tags.</summary>
    public static (byte[] Data, string ContentType)? ReadEmbedded(string audioPath)
    {
        try
        {
            using var file = TagLib.File.Create(audioPath);
            var pictures = file.Tag.Pictures;
            var picture = pictures.FirstOrDefault(p => p.Type == TagLib.PictureType.FrontCover)
                          ?? pictures.FirstOrDefault();
            if (picture is null || picture.Data.Count == 0) return null;

            var mime = string.IsNullOrWhiteSpace(picture.MimeType) ? "image/jpeg" : picture.MimeType;
            return (picture.Data.Data, mime);
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException)
        {
            return null;
        }
    }
}
