using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sonara.Api.Media;
using Sonara.Api.Options;
using Sonara.Data;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static async Task<IResult> Hls(string? id, string? part, HttpContext h, SonaraDbContext db, HlsCache cache, IOptions<SonaraOptions> options)
    {
        if (!int.TryParse(id, out var trackId)) return MissingParam();
        var user = SubsonicAuthFilter.CurrentUser(h)!;
        var track = await db.Tracks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == trackId);
        if (track is null || !File.Exists(track.Path)) return NotFound();
        if (!options.Value.Transcoding.Enabled) return SubsonicResult.Error(0, "HLS requires transcoding.");
        if (part is not null && !Regex.IsMatch(part, "^segment-[0-9]{5,8}\\.ts$", RegexOptions.CultureInvariant)) return NotFound();
        var rates = Values(h, "bitRate");
        if (rates.Any(r => !int.TryParse(r, out var n) || n < 32 || n > 320)) return SubsonicResult.Error(0, "HLS bitrate must be from 32 to 320 kbps.");
        var rate = rates.Count == 0 ? (user.PreferredTranscodeBitRate > 0 ? user.PreferredTranscodeBitRate : 192) : int.Parse(rates[0]);
        if (!user.IsAdmin && user.MaxBitRate > 0) rate = Math.Min(rate, user.MaxBitRate);
        if (rate < 32) return Forbidden();
        if (part is null && rates.Count > 1)
        {
            var variants = rates.Select(int.Parse).Select(r => !user.IsAdmin && user.MaxBitRate > 0 ? Math.Min(r, user.MaxBitRate) : r).Distinct().ToList();
            var master = new System.Text.StringBuilder("#EXTM3U\n");
            foreach (var variant in variants)
            {
                var variantQuery = new QueryBuilder();
                foreach (var pair in h.Request.Query.Where(p => p.Key is not ("part" or "bitRate"))) foreach (var v in pair.Value) variantQuery.Add(pair.Key, v ?? "");
                variantQuery.Add("bitRate", variant.ToString());
                master.AppendLine($"#EXT-X-STREAM-INF:BANDWIDTH={variant * 1000 + 16000},CODECS=\"mp4a.40.2\"");
                master.AppendLine(h.Request.PathBase + h.Request.Path + variantQuery.ToQueryString());
            }
            h.Response.Headers.CacheControl = "no-store";
            return Results.Text(master.ToString(), "application/vnd.apple.mpegurl");
        }
        var folder = await cache.PrepareAsync(track, rate, h.RequestAborted);
        if (folder is null) return SubsonicResult.Error(0, "HLS conversion failed.");
        Directory.SetLastWriteTimeUtc(folder, DateTime.UtcNow);
        h.Response.Headers.CacheControl = "no-store";
        if (part is not null)
        {
            var segment = Path.Combine(folder, part);
            return File.Exists(segment) ? Results.File(segment, "video/mp2t", enableRangeProcessing: true) : NotFound();
        }
        var lines = await File.ReadAllLinesAsync(Path.Combine(folder, "index.m3u8"), h.RequestAborted);
        var query = new QueryBuilder();
        foreach (var pair in h.Request.Query.Where(p => p.Key is not ("part" or "bitRate"))) foreach (var v in pair.Value) query.Add(pair.Key, v ?? "");
        query.Add("bitRate", rate.ToString());
        // The auth parameters remain attached to every segment request; segments are never public static files.
        var text = string.Join('\n', lines.Select(line => line.StartsWith('#') || line.Length == 0 ? line :
            h.Request.PathBase + h.Request.Path + query.ToQueryString() + "&part=" + Uri.EscapeDataString(Path.GetFileName(line)))) + "\n";
        return Results.Text(text, "application/vnd.apple.mpegurl");
    }
}
