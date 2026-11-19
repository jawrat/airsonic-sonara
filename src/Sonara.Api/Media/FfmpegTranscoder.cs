using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;

namespace Sonara.Api.Media;

/// <summary>Runs ffmpeg and pipes its output straight into the HTTP response.</summary>
public sealed class FfmpegTranscoder : IDisposable
{
    private readonly TranscodingOptions _options;
    private readonly ILogger<FfmpegTranscoder> _logger;
    private readonly SemaphoreSlim _slots;

    public FfmpegTranscoder(IOptions<SonaraOptions> options, ILogger<FfmpegTranscoder> logger)
    {
        _options = options.Value.Transcoding;
        _logger = logger;
        var max = _options.MaxConcurrent > 0 ? _options.MaxConcurrent : Environment.ProcessorCount;
        _slots = new SemaphoreSlim(max, max);
    }

    public void Dispose() => _slots.Dispose();

    /// <returns>
    /// True if the response was handled (including the client disconnecting part-way). False if ffmpeg
    /// could not start or produced nothing, in which case nothing has been written and the caller can
    /// still send an error.
    /// </returns>
    public async Task<bool> StreamAsync(HttpContext http, string inputPath, TranscodePlan plan, int startSeconds)
    {
        var ct = http.RequestAborted;

        try
        {
            if (!await _slots.WaitAsync(TimeSpan.FromSeconds(5), ct))
            {
                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                http.Response.Headers.RetryAfter = "5";
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            return true; // client gave up while queued
        }

        try
        {
            return await RunAsync(http, inputPath, plan, startSeconds, ct);
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task<bool> RunAsync(HttpContext http, string inputPath, TranscodePlan plan, int startSeconds, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(_options.FfmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in TranscodePlanner.BuildArguments(inputPath, plan, startSeconds))
            startInfo.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            _logger.LogError(ex, "Could not start '{Ffmpeg}'. Is ffmpeg installed and on the PATH?", _options.FfmpegPath);
            return false;
        }

        _logger.LogInformation(
            "Transcoding {Path} to {Format} at {BitRate} kbps (offset {Offset}s)",
            inputPath, plan.Format, plan.BitRateKbps, startSeconds);

        // Drain stderr continuously; otherwise ffmpeg can block once the pipe buffer fills.
        var stderr = process.StandardError.ReadToEndAsync();

        try
        {
            var output = process.StandardOutput.BaseStream;
            var buffer = new byte[64 * 1024];

            // Read the first chunk before committing to a 200 response, so a failure can still be reported.
            var read = await output.ReadAsync(buffer, ct);
            if (read == 0)
            {
                await process.WaitForExitAsync(ct);
                _logger.LogWarning("ffmpeg produced no output for {Path}: {Error}", inputPath, await stderr);
                return false;
            }

            http.Response.ContentType = plan.ContentType;
            http.Response.Headers["Accept-Ranges"] = "none"; // seeking is done with timeOffset instead

            do
            {
                await http.Response.Body.WriteAsync(buffer.AsMemory(0, read), ct);
                read = await output.ReadAsync(buffer, ct);
            }
            while (read > 0);

            await process.WaitForExitAsync(ct);
            if (process.ExitCode != 0)
                _logger.LogWarning("ffmpeg exited with {Code} for {Path}: {Error}", process.ExitCode, inputPath, await stderr);

            return true;
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            return true; // the client went away; the finally block stops ffmpeg
        }
        finally
        {
            KillQuietly(process);
        }
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }
    }
}
