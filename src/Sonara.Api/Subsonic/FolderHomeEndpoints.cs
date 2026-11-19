using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    // Physical directories containing scanned music, independent of artist/album tags.
    // Query filters and musicFolderId restrict the catalog before any grouping occurs.
    private static async Task<IResult> GetFolderList(string? type, int? size, int? offset, HttpContext h, SonaraDbContext db)
    {
        if (type is not (null or "newest" or "alphabeticalByName")) return SubsonicResult.Error(0, "Choose newest or alphabeticalByName.");
        var roots = await db.MusicFolders.AsNoTracking().ToDictionaryAsync(f => f.Id);
        var tracks = await ScopedTracks(h, db).Select(t => new { t.Id, t.Path, t.MusicFolderId, t.CreatedUtc }).ToListAsync(h.RequestAborted);
        var directories = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        var loose = new List<JsonObject>();
        foreach (var track in tracks)
        {
            if (!roots.TryGetValue(track.MusicFolderId, out var root)) continue;
            var relative = Path.GetRelativePath(root.Path, track.Path).Replace('\\', '/');
            if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)) continue;
            var slash = relative.LastIndexOf('/');
            if (slash < 0) { loose.Add(Obj(("id", track.Id.ToString()), ("isDir", false), ("created", JsonValue.Create(track.CreatedUtc)))); continue; }
            var path = relative[..slash]; var id = DirectoryId(root.Id, path);
            if (!directories.TryGetValue(id, out var directory))
            {
                var parent = path.Contains('/') ? path[..path.LastIndexOf('/')] : "";
                directory = Obj(("id", id), ("title", path.Split('/').Last()), ("isDir", true), ("coverArt", id),
                    ("parent", DirectoryId(root.Id, parent)), ("parentName", parent), ("path", path),
                    ("musicFolder", root.Name), ("songCount", 0), ("created", JsonValue.Create(track.CreatedUtc)));
                directories.Add(id, directory);
            }
            directory["songCount"] = directory["songCount"]!.GetValue<int>() + 1;
            if (track.CreatedUtc < directory["created"]!.GetValue<DateTime>()) directory["created"] = JsonValue.Create(track.CreatedUtc);
        }
        IEnumerable<JsonObject> sorted = type == "alphabeticalByName"
            ? directories.Values.OrderBy(d => d["title"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase)
                .ThenBy(d => d["path"]!.GetValue<string>(), StringComparer.Ordinal).ThenBy(d => d["id"]!.GetValue<string>(), StringComparer.Ordinal)
            : directories.Values.OrderByDescending(d => d["created"]!.GetValue<DateTime>())
                .ThenBy(d => d["title"]!.GetValue<string>(), StringComparer.OrdinalIgnoreCase).ThenBy(d => d["id"]!.GetValue<string>(), StringComparer.Ordinal);
        // Keep loose root-level songs reachable, without adding a synthetic Music tile.
        var all = sorted.Concat(loose.OrderByDescending(d => d["created"]!.GetValue<DateTime>()).ThenBy(d => d["id"]!.GetValue<string>(), StringComparer.Ordinal)).ToList();
        var page = all.Skip(Math.Max(0, offset ?? 0)).Take(Math.Clamp(size ?? 49, 1, 500)).ToList();
        var songIds = page.Where(d => d["isDir"]!.GetValue<bool>() == false).Select(d => int.Parse(d["id"]!.GetValue<string>())).ToList();
        var songs = await WithTags(ScopedTracks(h, db)).Where(t => songIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        var rendered = page.Select(d => d["isDir"]!.GetValue<bool>() ? d : songs.TryGetValue(int.Parse(d["id"]!.GetValue<string>()), out var song) ? SongJson(song) : null).OfType<JsonObject>();
        return SubsonicResult.Ok(Obj(("directory", Obj(("id", "folder-home"), ("name", "Music folders"), ("totalChildren", all.Count), ("child", Arr(rendered))))));
    }
}
