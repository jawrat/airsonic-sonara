using System.Text;
using System.Text.RegularExpressions;

namespace Sonara.Api.Playlists;

public sealed record PlaylistFileTrack(int Id, string Path, string RelativePath, string FolderName, string Title, string Artist, int Duration);
public sealed record PlaylistFileMatch(int Index, string Entry, string Status, int? TrackId, string? Title);

/// <summary>Playlist references are matched only against an already-authorized catalog; no paths or URLs are fetched.</summary>
public static class PlaylistFiles
{
    public const int MaxCharacters = 1024 * 1024;
    public const int MaxEntries = 5000;
    public static List<string> Parse(string content, string format)
    {
        if (content.Length > MaxCharacters) throw new InvalidDataException("Playlist files must be at most 1 MB of text.");
        var lines = content.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        IEnumerable<string> entries;
        if (format is "m3u" or "m3u8") entries = lines.Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'));
        else if (format == "pls")
        {
            var files = new SortedDictionary<int, string>();
            foreach (var line in lines)
            {
                var match = Regex.Match(line.Trim(), @"^File([0-9]+)=(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var i) && i > 0)
                { if (!files.TryAdd(i, match.Groups[2].Value.Trim())) throw new InvalidDataException("A PLS file contains duplicate File indexes."); }
            }
            entries = files.Values;
        }
        else throw new InvalidDataException("Choose an M3U, M3U8 or PLS file.");
        var result = entries.ToList();
        if (result.Count == 0 || result.Count > MaxEntries) throw new InvalidDataException("A playlist must contain 1 to 5000 track references.");
        return result;
    }
    public static string Normalize(string path)
    {
        path = path.Trim().Trim('"');
        if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(path, UriKind.Absolute, out var file)) path = Uri.UnescapeDataString(file.LocalPath);
        path = path.Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal)) path = path[2..];
        return path;
    }
    public static string FolderAlias(string name) => name.Replace('/', '_').Replace('\\', '_').Replace('\r', ' ').Replace('\n', ' ');
    public static List<PlaylistFileMatch> Match(IReadOnlyList<string> entries, IReadOnlyList<PlaylistFileTrack> catalog)
    {
        var exact = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        var relative = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        void Add(Dictionary<string, HashSet<int>> map, string key, int id) { if (!map.TryGetValue(key, out var ids)) map[key] = ids = []; ids.Add(id); }
        foreach (var t in catalog)
        {
            Add(exact, Normalize(t.Path), t.Id);
            Add(relative, Normalize(t.RelativePath), t.Id);
            Add(relative, Normalize(FolderAlias(t.FolderName) + "/" + t.RelativePath), t.Id);
        }
        var byId = catalog.ToDictionary(t => t.Id); var result = new List<PlaylistFileMatch>();
        for (var i = 0; i < entries.Count; i++)
        {
            var value = Normalize(entries[i]);
            if (Regex.IsMatch(value, @"^[a-z][a-z0-9+.-]*://", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            { result.Add(new(i, entries[i], "unsupported", null, null)); continue; }
            HashSet<int>? ids = null;
            if (!exact.TryGetValue(value, out ids) && !relative.TryGetValue(value, out ids))
            {
                // A playlist exported on another OS may have a different mount/drive prefix.
                ids = catalog.Where(t => value.EndsWith("/" + Normalize(t.RelativePath), StringComparison.OrdinalIgnoreCase)).Select(t => t.Id).ToHashSet();
            }
            result.Add(ids.Count == 1 ? new(i, entries[i], "matched", ids.Single(), byId[ids.Single()].Title) : new(i, entries[i], ids.Count == 0 ? "missing" : "ambiguous", null, null));
        }
        return result;
    }
    public static string Export(IReadOnlyList<PlaylistFileTrack> tracks, string format)
    {
        if (format is not ("m3u8" or "pls")) throw new InvalidDataException("Export format must be m3u8 or pls.");
        string Safe(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
        var text = new StringBuilder(format == "pls" ? "[playlist]\n" : "#EXTM3U\n");
        for (var i = 0; i < tracks.Count; i++)
        {
            var t = tracks[i]; var path = FolderAlias(t.FolderName) + "/" + t.RelativePath.Replace('\\', '/');
            if (path.Contains('\r') || path.Contains('\n') || path.StartsWith('/') || path.Contains("../")) throw new InvalidDataException("This playlist contains a path that cannot be represented safely.");
            if (format == "pls") text.Append($"File{i + 1}=./{path}\nTitle{i + 1}={Safe(t.Artist + " - " + t.Title)}\nLength{i + 1}={t.Duration}\n");
            else text.Append($"#EXTINF:{t.Duration},{Safe(t.Artist + " - " + t.Title)}\n./{path}\n");
        }
        if (format == "pls") text.Append($"NumberOfEntries={tracks.Count}\nVersion=2\n");
        return text.ToString();
    }
}
