using System.Text;
using Microsoft.EntityFrameworkCore;
using Sonara.Api.Playlists;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapPlaylistFiles(RouteGroupBuilder rest)
    {
        Map(rest, "importPlaylistFile", ImportPlaylistFile);
        Map(rest, "exportPlaylistFile", ExportPlaylistFile);
    }
    private static async Task<List<PlaylistFileTrack>> PlaylistCatalog(SonaraDbContext db, HttpContext h, List<int>? ids = null)
    {
        IQueryable<Track> query = ScopedTracks(h, db).Include(t => t.Artist).Include(t => t.MusicFolder);
        if (ids is not null) query = query.Where(t => ids.Contains(t.Id));
        var tracks = await query.ToListAsync();
        return tracks.Select(t => new PlaylistFileTrack(t.Id, t.Path, Path.GetRelativePath(t.MusicFolder.Path, t.Path).Replace('\\', '/'), t.MusicFolder.Name, t.Title, t.Artist.Name, t.DurationSeconds)).ToList();
    }
    private static async Task<IResult> ImportPlaylistFile(string? content, string? format, string? name, bool? dryRun, HttpContext h, SonaraDbContext db)
    {
        var user = SubsonicAuthFilter.CurrentUser(h)!;
        if (!user.IsAdmin && !user.PlaylistRole) return Forbidden();
        if (content is null || format is null) return MissingParam();
        try
        {
            var entries = PlaylistFiles.Parse(content, format.ToLowerInvariant());
            var matches = PlaylistFiles.Match(entries, await PlaylistCatalog(db, h));
            var ids = matches.Where(m => m.Status == "matched").Select(m => m.TrackId!.Value).ToList();
            int? playlistId = null;
            if (!(dryRun ?? true))
            {
                if (string.IsNullOrWhiteSpace(name) || name.Length > 200) return SubsonicResult.Error(0, "Choose a playlist name of 1 to 200 characters.");
                if (ids.Count == 0) return SubsonicResult.Error(0, "No accessible library tracks matched this file.");
                var playlist = new Playlist { Name = name.Trim(), OwnerId = user.Id, IsPublic = false };
                AddEntries(playlist, ids, 0); db.Add(playlist); await db.SaveChangesAsync(); playlistId = playlist.Id;
            }
            return SubsonicResult.Ok(Obj(("playlistImport", Obj(("dryRun", dryRun ?? true), ("playlistId", playlistId?.ToString()),
                ("matched", ids.Count), ("unmatched", matches.Count - ids.Count),
                ("totalEntries", matches.Count), ("entry", Arr(matches.Take(200).Select(m => Obj(("index", m.Index), ("path", m.Entry), ("status", m.Status), ("title", m.Title)))))))));
        }
        catch (InvalidDataException ex) { return SubsonicResult.Error(0, ex.Message); }
    }
    private static async Task<IResult> ExportPlaylistFile(string? id, string? format, HttpContext h, SonaraDbContext db)
    {
        if (!int.TryParse(id, out var playlistId)) return MissingParam();
        var user = SubsonicAuthFilter.CurrentUser(h)!;
        var playlist = await db.Playlists.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playlistId);
        if (playlist is null) return NotFound();
        if (!PlaylistRules.CanView(playlist.OwnerId, playlist.IsPublic, user.Id, user.IsAdmin)) return Forbidden();
        var ids = await db.PlaylistEntries.Where(e => e.PlaylistId == playlistId).OrderBy(e => e.Position).Select(e => e.TrackId).ToListAsync();
        var catalog = (await PlaylistCatalog(db, h, ids)).ToDictionary(t => t.Id);
        var tracks = ids.Where(catalog.ContainsKey).Select(i => catalog[i]).ToList();
        var kind = (format ?? "m3u8").ToLowerInvariant();
        try
        {
            var text = PlaylistFiles.Export(tracks, kind);
            h.Response.Headers.CacheControl = "no-store";
            return Results.File(Encoding.UTF8.GetBytes(text), kind == "pls" ? "audio/x-scpls" : "audio/x-mpegurl", $"sonara-playlist-{playlistId}.{kind}");
        }
        catch (InvalidDataException ex) { return SubsonicResult.Error(0, ex.Message); }
    }
}
