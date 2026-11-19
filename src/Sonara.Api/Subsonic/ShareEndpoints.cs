using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sonara.Api.Media;
using Sonara.Api.Options;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapShares(RouteGroupBuilder rest)
    {
        Map(rest, "getShares", GetShares);
        Map(rest, "createShare", CreateShare);
        Map(rest, "updateShare", UpdateShare);
        Map(rest, "deleteShare", DeleteShare);
    }
    public static void MapPublicShares(this WebApplication app)
    {
        app.MapGet("/s/{token}", PublicSharePage);
        app.MapGet("/s/{token}/stream/{trackId:int}", PublicShareStream);
        app.MapGet("/share/{token}", PublicSharePage);
        app.MapGet("/share/{token}/stream/{trackId:int}", PublicShareStream);
    }
    private static string ShareUrl(MediaShare s, IOptions<SonaraOptions> options, HttpContext h) =>
        (string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl) ? $"{h.Request.Scheme}://{h.Request.Host}{h.Request.PathBase}" : options.Value.PublicBaseUrl.TrimEnd('/')) + (s.Id.Length == 16 ? "/s/" : "/share/") + s.Id;
    private static JsonObject ShareJson(MediaShare s, string? owner, IEnumerable<Track> tracks, IOptions<SonaraOptions> options, HttpContext h) => Obj(
        ("id", s.Id), ("url", ShareUrl(s, options, h)), ("description", s.Description), ("username", owner),
        ("created", s.CreatedUtc), ("expires", s.ExpiresUtc), ("lastVisited", s.LastVisitedUtc), ("visitCount", s.VisitCount),
        ("entry", Arr(tracks.Select(SongJson))));
    private static async Task<IResult> GetShares(HttpContext h, SonaraDbContext db, IOptions<SonaraOptions> options)
    {
        var user = SubsonicAuthFilter.CurrentUser(h)!;
        var shares = await db.MediaShares.AsNoTracking().Where(s => s.UserId == user.Id || user.IsAdmin).OrderByDescending(s => s.CreatedUtc).ToListAsync();
        var ids = shares.SelectMany(s => s.TrackIds).Distinct().ToList();
        var tracks = await WithTags(db.Tracks).Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        var users = await db.Users.ToDictionaryAsync(u => u.Id, u => u.Username);
        return SubsonicResult.Ok(Obj(("shares", Obj(("share", Arr(shares.Select(s => ShareJson(s, users.GetValueOrDefault(s.UserId), s.TrackIds.Where(tracks.ContainsKey).Select(i => tracks[i]), options, h))))))));
    }
    private static async Task<IResult> CreateShare(string? description, long? expires, HttpContext h, SonaraDbContext db, IOptions<SonaraOptions> options)
    {
        var raw = Values(h, "id");
        if (raw.Count == 0) return MissingParam();
        const int limit = 5000;
        if (raw.Count > limit || description?.Length > 2000) return SubsonicResult.Error(0, "Share is too large.");
        var user = SubsonicAuthFilter.CurrentUser(h)!;
        if (!user.IsAdmin && !user.StreamRole) return Forbidden();
        var ids = new List<int>();
        foreach (var id in raw)
        {
            if (id.StartsWith("al-") && TaggedId(id, "al-") is { } albumId)
                ids.AddRange(await db.Tracks.Where(t => t.AlbumId == albumId).OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ThenBy(t => t.Id).Select(t => t.Id).Take(limit + 1).ToListAsync());
            else if (id.StartsWith("dir-", StringComparison.Ordinal))
                ids.AddRange(await DirectoryTracks(id, db).OrderBy(t => t.Path).ThenBy(t => t.Id).Select(t => t.Id).Take(limit + 1).ToListAsync());
            else if (id.StartsWith("pl-") && TaggedId(id, "pl-") is { } playlistId)
            {
                var playlist = await db.Playlists.AsNoTracking().FirstOrDefaultAsync(p => p.Id == playlistId);
                if (playlist is null) return NotFound();
                if (!PlaylistRules.CanView(playlist.OwnerId, playlist.IsPublic, user.Id, user.IsAdmin)) return Forbidden();
                ids.AddRange(await db.PlaylistEntries.Where(e => e.PlaylistId == playlistId && db.Tracks.Any(t => t.Id == e.TrackId))
                    .OrderBy(e => e.Position).Select(e => e.TrackId).Take(limit + 1).ToListAsync());
            }
            else if (int.TryParse(id, out var trackId) && trackId > 0) ids.Add(trackId);
            else return NotFound();
            if (ids.Count > limit) return SubsonicResult.Error(0, "A share can contain at most 5000 tracks. Choose a smaller folder or playlist.");
        }
        if (ids.Count == 0) return SubsonicResult.Error(0, "There are no accessible music tracks to share.");
        var distinctIds = ids.Distinct().ToList();
        var tracks = await WithTags(db.Tracks).Where(t => distinctIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        if (tracks.Count != distinctIds.Count) return NotFound();
        if (!TryExpiry(expires, out var expiry)) return SubsonicResult.Error(0, "Invalid expiration timestamp.");
        var share = new MediaShare { Id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12)).TrimEnd('=').Replace('+', '-').Replace('/', '_'), UserId = user.Id, TrackIds = ids, Description = description, ExpiresUtc = expiry };
        db.Add(share); await db.SaveChangesAsync();
        return SubsonicResult.Ok(Obj(("shares", Obj(("share", new JsonArray(ShareJson(share, user.Username, ids.Select(i => tracks[i]), options, h)))))));
    }
    public static bool TryExpiry(long? value, out DateTime? expiry)
    {
        expiry = null;
        if (value is null or 0) return true;
        try { expiry = DateTimeOffset.FromUnixTimeMilliseconds(value.Value).UtcDateTime; return value > 0; }
        catch (ArgumentOutOfRangeException) { return false; }
    }
    private static async Task<IResult> UpdateShare(string? id, string? description, long? expires, HttpContext h, SonaraDbContext db)
    {
        if (id is null) return MissingParam();
        var user = SubsonicAuthFilter.CurrentUser(h)!;
        var s = await db.MediaShares.FindAsync(id);
        if (s is null) return NotFound();
        if (s.UserId != user.Id && !user.IsAdmin) return Forbidden();
        if (description?.Length > 2000 || !TryExpiry(expires, out var expiry)) return SubsonicResult.Error(0, "Invalid share settings.");
        if (description is not null) s.Description = description;
        if (expires.HasValue) s.ExpiresUtc = expiry;
        await db.SaveChangesAsync(); return SubsonicResult.Ok();
    }
    private static async Task<IResult> DeleteShare(string? id, HttpContext h, SonaraDbContext db)
    {
        if (id is null) return MissingParam();
        var user = SubsonicAuthFilter.CurrentUser(h)!;
        var s = await db.MediaShares.FindAsync(id);
        if (s is null) return NotFound();
        if (s.UserId != user.Id && !user.IsAdmin) return Forbidden();
        db.Remove(s); await db.SaveChangesAsync(); return SubsonicResult.Ok();
    }
    private static async Task<MediaShare?> ResolvePublicShare(string token, HttpContext h, SonaraDbContext db)
    {
        // Keep previously issued 48-character links working alongside new short tokens.
        if (token.Length == 16)
        {
            if (!token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return null;
        }
        else if (token.Length != 48 || !token.All(char.IsAsciiHexDigit)) return null;
        var s = await db.MediaShares.AsNoTracking().FirstOrDefaultAsync(s => s.Id == token && (s.ExpiresUtc == null || s.ExpiresUtc > DateTime.UtcNow));
        if (s is null) return null;
        var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == s.UserId);
        if (owner is null || (!owner.IsAdmin && (!owner.ShareRole || !owner.StreamRole))) return null;
        db.AccessUserId = owner.Id; db.AccessAllFolders = owner.IsAdmin || owner.AllFolders;
        h.Items[SubsonicAuthFilter.UserItemKey] = owner;
        h.Response.Headers.CacheControl = "no-store";
        h.Response.Headers["Referrer-Policy"] = "no-referrer";
        return s;
    }
    private static async Task<IResult> PublicSharePage(string token, HttpContext h, SonaraDbContext db)
    {
        var share = await ResolvePublicShare(token, h, db);
        if (share is null) return Results.NotFound();
        var tracks = await WithTags(db.Tracks).Where(t => share.TrackIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        await db.MediaShares.Where(s => s.Id == token).ExecuteUpdateAsync(s => s.SetProperty(x => x.VisitCount, x => x.VisitCount + 1).SetProperty(x => x.LastVisitedUtc, DateTime.UtcNow));
        h.Response.Headers["Content-Security-Policy"] = "default-src 'none'; media-src 'self'; style-src 'unsafe-inline'; frame-ancestors 'none'";
        var items = share.TrackIds.Where(tracks.ContainsKey).Select(i => $"<li><p>{WebUtility.HtmlEncode(tracks[i].Title)} — {WebUtility.HtmlEncode(tracks[i].Artist.Name)}</p><audio controls preload='none' src='/s/{token}/stream/{i}'></audio></li>");
        return Results.Content("<!doctype html><html><head><meta name='viewport' content='width=device-width'><title>Sonara share</title><style>body{font:16px system-ui;background:#111;color:#eee;max-width:48rem;margin:3rem auto;padding:1rem}audio{width:100%}li{margin:1rem 0}</style></head><body><h1>Sonara</h1><p>" + WebUtility.HtmlEncode(share.Description) + "</p><ol>" + string.Join("", items) + "</ol></body></html>", "text/html");
    }
    private static async Task<IResult> PublicShareStream(string token, int trackId, HttpContext h, SonaraDbContext db, FfmpegTranscoder transcoder, IOptions<SonaraOptions> options)
    {
        var share = await ResolvePublicShare(token, h, db);
        if (share is null || !share.TrackIds.Contains(trackId)) return Results.NotFound();
        return await Stream(trackId.ToString(), null, null, null, h, db, transcoder, options);
    }
}
