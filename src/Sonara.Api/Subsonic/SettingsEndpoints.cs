using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;
using Sonara.Api.Settings;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapSettings(RouteGroupBuilder rest)
    {
        Map(rest, "getSettings", GetSettings);
        Map(rest, "updateSettings", UpdateSettings);
        Map(rest, "getMusicFolderSettings", GetMusicFolderSettings);
        Map(rest, "saveMusicFolder", SaveMusicFolder);
        Map(rest, "deleteMusicFolder", DeleteMusicFolder);
    }
    private static async Task<IResult> GetSettings(HttpContext h, SonaraDbContext db, IOptions<SonaraOptions> options)
    {
        if (!IsAdmin(h)) return Forbidden();
        var saved = await db.ServerSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        return SubsonicResult.Ok(Obj(("settings", Obj(("setting", Arr(SettingCatalog.Definitions.Select(d => Obj(
            ("key", d.Key), ("label", d.Label), ("kind", d.Kind), ("min", d.Min), ("max", d.Max),
            ("value", saved.GetValueOrDefault(d.Key, SettingCatalog.Read(options.Value, d.Key))),
            ("effectiveValue", SettingCatalog.Read(options.Value, d.Key)), ("restartRequired", true)))))))));
    }
    private static async Task<IResult> UpdateSettings(HttpContext h, SonaraDbContext db)
    {
        if (!IsAdmin(h)) return Forbidden();
        var keys = Values(h, "key"); var values = Values(h, "value");
        if (keys.Count == 0 || keys.Count != values.Count || keys.Distinct().Count() != keys.Count) return MissingParam();
        for (var i = 0; i < keys.Count; i++) if (!SettingCatalog.Validate(keys[i], values[i])) return SubsonicResult.Error(0, "Invalid setting: " + keys[i]);
        for (var i = 0; i < keys.Count; i++)
        {
            var row = await db.ServerSettings.FindAsync(keys[i]);
            if (row is null) db.Add(new ServerSetting { Key = keys[i], Value = values[i] }); else row.Value = values[i];
        }
        await db.SaveChangesAsync(); return SubsonicResult.Ok(Obj(("restartRequired", true)));
    }
    private static async Task<IResult> GetMusicFolderSettings(HttpContext h, SonaraDbContext db)
    {
        if (!IsAdmin(h)) return Forbidden();
        var folders = await db.MusicFolders.IgnoreQueryFilters().OrderBy(f => f.Id).ToListAsync();
        return SubsonicResult.Ok(Obj(("musicFolderSettings", Obj(("folder", Arr(folders.Select(f => Obj(("id", f.Id), ("name", f.Name), ("path", f.Path), ("enabled", f.Enabled), ("allowUploads", f.AllowUploads)))))))));
    }
    private static async Task<IResult> SaveMusicFolder(int? id, string? name, string? path, bool? enabled, bool? allowUploads, HttpContext h, SonaraDbContext db)
    {
        if (!IsAdmin(h)) return Forbidden();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) return MissingParam();
        if (name.Length > 200 || !Path.IsPathFullyQualified(path) || !Directory.Exists(path)) return SubsonicResult.Error(0, "Use an existing absolute folder path on the server.");
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var folders = await db.MusicFolders.IgnoreQueryFilters().ToListAsync();
        if (folders.Any(f => f.Id != id && (f.Path == fullPath || fullPath.StartsWith(f.Path.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal) || f.Path.StartsWith(fullPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))))
            return SubsonicResult.Error(0, "Music folders must not duplicate or overlap one another.");
        var row = id.HasValue ? folders.FirstOrDefault(f => f.Id == id) : new MusicFolder();
        if (row is null) return NotFound();
        if (id.HasValue && row.Path != fullPath && await db.Tracks.IgnoreQueryFilters().AnyAsync(t => t.MusicFolderId == id))
            return SubsonicResult.Error(0, "Disable the existing folder and register a new one to move a scanned library.");
        if (!id.HasValue) db.Add(row);
        row.Name = name.Trim(); row.Path = fullPath; row.Enabled = enabled ?? true;
        if (allowUploads.HasValue) row.AllowUploads = allowUploads.Value;
        await db.SaveChangesAsync(); return SubsonicResult.Ok(Obj(("musicFolder", Obj(("id", row.Id), ("name", row.Name)))));
    }
    private static async Task<IResult> DeleteMusicFolder(int? id, HttpContext h, SonaraDbContext db)
    {
        if (!IsAdmin(h)) return Forbidden();
        if (!id.HasValue) return MissingParam();
        var row = await db.MusicFolders.IgnoreQueryFilters().FirstOrDefaultAsync(f => f.Id == id);
        if (row is null) return NotFound();
        // Preserve track IDs, playlists, bookmarks and media files. Disable, rather than cascade-delete the library.
        row.Enabled = false; await db.SaveChangesAsync(); return SubsonicResult.Ok();
    }
}
