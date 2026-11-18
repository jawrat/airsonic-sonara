using System.Net;
using System.Net.Sockets;

namespace Sonara.Api.Podcasts;

/// <summary>An HttpClient for fetching user-supplied URLs, plus helpers to cap how much it reads.</summary>
public sealed class PodcastHttp : IDisposable
{
    public HttpClient Client { get; }

    public PodcastHttp(bool allowPrivateAddresses)
    {
        var handler = new SocketsHttpHandler
        {
            // Never go through a proxy: the address check below has to see the real destination.
            UseProxy = false,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectTimeout = TimeSpan.FromSeconds(15),
            // Runs for every connection, including each redirect hop. We resolve the name ourselves and
            // connect to the address we checked, so DNS rebinding can't swap in an internal address.
            ConnectCallback = async (context, ct) =>
            {
                var host = context.DnsEndPoint.Host;
                var addresses = await Dns.GetHostAddressesAsync(host, ct);
                var allowed = addresses.Where(a => allowPrivateAddresses || IpGuard.IsPublic(a)).ToArray();
                if (allowed.Length == 0)
                    throw new HttpRequestException($"'{host}' does not resolve to a public internet address.");

                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

        Client = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromMinutes(30) };
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("Sonara/1.0 (+podcast fetcher)");
    }

    public void Dispose() => Client.Dispose();

    /// <summary>Only plain http(s) URLs are accepted.</summary>
    public static bool IsAllowedUrl(string? url, out Uri uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(parsed.UserInfo))
        {
            uri = parsed;
            return true;
        }
        uri = null!;
        return false;
    }

    /// <summary>Reads a response body, failing if it is larger than <paramref name="maxBytes"/>.</summary>
    public static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, long maxBytes, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException($"The response is larger than the {maxBytes / 1024 / 1024} MB limit.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        await CopyLimitedAsync(stream, buffer, maxBytes, ct);
        return buffer.ToArray();
    }

    public static async Task CopyLimitedAsync(Stream source, Stream destination, long maxBytes, CancellationToken ct)
    {
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(chunk, ct)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new InvalidDataException($"The download is larger than the {maxBytes / 1024 / 1024} MB limit.");
            await destination.WriteAsync(chunk.AsMemory(0, read), ct);
        }
    }
}
