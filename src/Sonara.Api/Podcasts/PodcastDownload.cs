using System.Net;
using System.Security.Authentication;
using System.Text.RegularExpressions;

namespace Sonara.Api.Podcasts;

/// <summary>Retries transient remote failures from byte zero. Filesystem writes are never classified as transport failures.</summary>
public static class PodcastDownload
{
    public const int MaxAttempts = 3;
    private sealed class TransportReadException(Exception inner) : IOException("The remote audio transfer was interrupted.", inner);

    public static async Task DownloadAsync(HttpClient client, Uri uri, string partialPath, long maxBytes, CancellationToken ct,
        Action<int, Exception>? retry = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        var completed = false;
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                var pause = TimeSpan.FromSeconds(attempt);
                try
                {
                    using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!response.IsSuccessStatusCode)
                    {
                        var after = response.Headers.RetryAfter;
                        if (after?.Delta is { } delta) pause = delta;
                        else if (after?.Date is { } date) pause = date - DateTimeOffset.UtcNow;
                        throw new HttpRequestException($"The audio server answered HTTP {(int)response.StatusCode}.", null, response.StatusCode);
                    }
                    if (response.Content.Headers.ContentLength > maxBytes)
                        throw new PodcastException($"The episode exceeds the {maxBytes / 1024 / 1024} MB download limit.");
                    await using (var source = await response.Content.ReadAsStreamAsync(ct))
                    await using (var target = File.Create(partialPath))
                    { // Truncate on every attempt; never append corrupted bytes.
                        var buffer = new byte[81920]; long total = 0;
                        while (true)
                        {
                            int read;
                            try { read = await source.ReadAsync(buffer, ct); }
                            catch (IOException ex) { throw new TransportReadException(ex); }
                            if (read == 0) break;
                            total += read;
                            if (total > maxBytes) throw new PodcastException($"The episode exceeds the {maxBytes / 1024 / 1024} MB download limit.");
                            await target.WriteAsync(buffer.AsMemory(0, read), ct);
                        }
                        if (response.Content.Headers.ContentLength is { } length && total != length)
                            throw new TransportReadException(new IOException("The remote response ended before its declared content length."));
                    }
                    completed = true;
                    return;
                }
                catch (Exception ex) when (attempt < MaxAttempts && !ct.IsCancellationRequested && pause <= TimeSpan.FromSeconds(30) && IsTransient(ex))
                {
                    retry?.Invoke(attempt + 1, ex);
                    await delay(TimeSpan.FromSeconds(Math.Clamp(pause.TotalSeconds, 0, 30)), ct);
                }
            }
        }
        finally
        {
            if (!completed) try { File.Delete(partialPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
    public static bool IsTransient(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException) if (e is AuthenticationException) return false;
        return exception is TransportReadException || exception is HttpRequestException h &&
            (h.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
                HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout);
    }
    public static string Describe(Exception exception)
    {
        var parts = new List<string>();
        for (var e = exception; e is not null && parts.Count < 6; e = e.InnerException)
        {
            // Remote enclosure URLs may contain private feed credentials or signed query parameters.
            var message = Regex.Replace(e.Message, @"https?://\S+", "[remote URL]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            parts.Add(e.GetType().Name + ": " + message[..Math.Min(message.Length, 400)]);
        }
        return string.Join(" -> ", parts);
    }
}
