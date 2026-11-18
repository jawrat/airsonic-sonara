using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;
using Sonara.Core.Entities;

namespace Sonara.Api.Media;

/// <summary>On-demand audio HLS. Publish only a completed cache; cancellation kills ffmpeg and removes partial files.</summary>
public sealed class HlsCache(IOptions<SonaraOptions> options)
{
    private readonly SemaphoreSlim _gate = new(2);
    public async Task<string?> PrepareAsync(Track track, int rate, CancellationToken ct)
    {
        var root = Path.Combine(Path.GetFullPath(options.Value.DataDir), "cache", "hls");
        var identity = $"{track.Id}|{track.Path}|{track.LastModifiedUtc.Ticks}|{track.Size}|{rate}";
        var dir = Path.Combine(root, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity))));
        var manifest = Path.Combine(dir, "index.m3u8");
        if (File.Exists(manifest)) return dir;
        await _gate.WaitAsync(ct);
        string? temporary = null;
        try
        {
            if (File.Exists(manifest)) return dir;
            Directory.CreateDirectory(root);
            // Bound abandoned/stale cache growth. Recently used entries are kept for 24 hours.
            foreach (var old in Directory.EnumerateDirectories(root))
                if (Directory.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-1))
                    try { Directory.Delete(old, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            temporary = dir + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Directory.CreateDirectory(temporary);
            var info = new ProcessStartInfo(options.Value.Transcoding.FfmpegPath) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
            foreach (var arg in new[] { "-nostdin", "-v", "error", "-i", track.Path, "-map", "0:a:0", "-vn", "-c:a", "aac", "-b:a", $"{rate}k", "-f", "hls", "-hls_time", "6", "-hls_list_size", "0", "-hls_playlist_type", "vod", "-hls_segment_filename", Path.Combine(temporary, "segment-%05d.ts"), Path.Combine(temporary, "index.m3u8") }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info);
            if (process is null) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(10));
            using var registration = timeout.Token.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await error;
            if (process.ExitCode != 0 || !File.Exists(Path.Combine(temporary, "index.m3u8"))) return null;
            try { Directory.Move(temporary, dir); temporary = null; }
            catch (IOException) when (File.Exists(manifest)) { /* Another conversion published the same cache first. */ }
            return dir;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
        catch (IOException) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
        finally
        {
            if (temporary is not null) try { Directory.Delete(temporary, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            _gate.Release();
        }
    }
}
