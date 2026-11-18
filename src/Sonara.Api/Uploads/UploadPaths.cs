namespace Sonara.Api.Uploads;

public static class UploadPaths
{
    public static string FileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 180 || name != name.Trim() || name.EndsWith('.') ||
            name.Any(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c)) || name is "." or "..")
            throw new InvalidDataException("Use a plain filename without path components or reserved characters.");
        var stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem) ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3])))
            throw new InvalidDataException("That filename is reserved.");
        return name;
    }
    public static string DirectoryPath(string root, string? relative)
    {
        var full = Path.GetFullPath(root);
        if (!Directory.Exists(full)) throw new InvalidDataException("The music folder is unavailable.");
        CheckAncestors(full);
        var segments = (relative ?? "").Replace('\\', '/').Split('/');
        if ((relative?.Length ?? 0) > 500 || segments.Length > 12) throw new InvalidDataException("The destination subfolder is too long.");
        if (!string.IsNullOrEmpty(relative)) foreach (var segment in segments) full = Path.Combine(full, FileName(segment));
        CheckAncestors(full);
        return full;
    }
    private static void CheckAncestors(string path)
    {
        for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
        {
            if (current.LinkTarget != null || (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint)))
                throw new InvalidDataException("Upload destinations cannot contain symbolic links or junctions.");
            if (File.Exists(current.FullName)) throw new InvalidDataException("A destination directory is a file.");
        }
    }
}
