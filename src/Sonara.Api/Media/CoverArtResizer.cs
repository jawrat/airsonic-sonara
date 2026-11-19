using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;

namespace Sonara.Api.Media;

/// <summary>Resizes cover art with ffmpeg (already needed for transcoding) and caches results on disk.</summary>
public sealed class CoverArtResizer : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _ffmpeg;
    private readonly string _cacheDir;
    private readonly ILogger<CoverArtResizer> _logger;
    private readonly SemaphoreSlim _slots = new(2, 2);

    public CoverArtResizer(IOptions<SonaraOptions> options, ILogger<CoverArtResizer> logger)
    {
        _ffmpeg = options.Value.Transcoding.FfmpegPath;
        _cacheDir = Path.Combine(Path.GetFullPath(options.Value.DataDir), "cache", "covers");
        _logger = logger;
    }

    public void Dispose() => _slots.Dispose();

    /// <returns>JPEG bytes, or null if resizing failed (the caller then sends the original).</returns>
    public async Task<byte[]?> GetAsync(byte[] source, int size, CancellationToken ct, string? coverId = null)
    {
        var cachePath = Path.Combine(_cacheDir, CoverArtPlanner.CacheFileName(source, size));
        try
        {
            if (File.Exists(cachePath)) return await File.ReadAllBytesAsync(cachePath, ct);
        }
        catch (IOException)
        {
            // fall through and regenerate
        }

        await _slots.WaitAsync(ct);
        try
        {
            var resized = await RunAsync(source, size, ct, coverId);
            if (resized is null) return null;

            try
            {
                Directory.CreateDirectory(_cacheDir);
                var temp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                await File.WriteAllBytesAsync(temp, resized, ct);
                File.Move(temp, cachePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not write cover art cache in {Dir}", _cacheDir);
            }
            return resized;
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task<byte[]?> RunAsync(byte[] source, int size, CancellationToken ct, string? coverId)
    {
        var startInfo = new ProcessStartInfo(_ffmpeg)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in CoverArtPlanner.BuildArguments(size)) startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Could not start '{Ffmpeg}' to resize cover art", _ffmpeg);
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);

        try
        {
            var stderr = process.StandardError.ReadToEndAsync(cts.Token);
            var output = new MemoryStream();
            var reading = process.StandardOutput.BaseStream.CopyToAsync(output, cts.Token);

            try
            {
                await process.StandardInput.BaseStream.WriteAsync(source, cts.Token);
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // ffmpeg stopped reading early (it has what it needs, or it failed); judge by its exit code.
            }

            await reading;
            await process.WaitForExitAsync(cts.Token);

            if (process.ExitCode != 0 || output.Length == 0)
            {
                _logger.LogWarning("ffmpeg could not resize cover art {CoverId}: {Error}", coverId, await stderr);
                return null;
            }
            return output.ToArray();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Resizing cover art {CoverId} timed out", coverId);
            return null;
        }
        finally
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // already gone
            }
        }
    }
}
