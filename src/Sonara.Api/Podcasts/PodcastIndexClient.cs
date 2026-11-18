using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;

namespace Sonara.Api.Podcasts;

public sealed record PodcastSearchResult(string Title, string? Author, string? Description, string FeedUrl, string? ImageUrl);

/// <summary>Searches podcastindex.org (free API key and secret required, see https://api.podcastindex.org).</summary>
public sealed class PodcastIndexClient(IOptions<SonaraOptions> options)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly PodcastIndexOptions _options = options.Value.Podcasts.PodcastIndex;

    public bool Configured => _options.Configured;

    /// <summary>Authorization header value: hex SHA-1 of key + secret + the request's unix time.</summary>
    public static string ComputeAuth(string apiKey, string apiSecret, long unixSeconds) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(apiKey + apiSecret + unixSeconds)))
            .ToLowerInvariant();

    public async Task<List<PodcastSearchResult>> SearchAsync(string term, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.podcastindex.org/api/1.0/search/byterm?max=30&q=" + Uri.EscapeDataString(term));
        request.Headers.UserAgent.ParseAdd("Sonara/1.0");
        request.Headers.Add("X-Auth-Key", _options.ApiKey);
        request.Headers.Add("X-Auth-Date", now.ToString());
        request.Headers.Add("Authorization", ComputeAuth(_options.ApiKey, _options.ApiSecret, now));

        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Podcast Index answered {(int)response.StatusCode}.");

        var body = await PodcastHttp.ReadLimitedAsync(response, 5 * 1024 * 1024, ct);
        return ParseResults(body);
    }

    public static List<PodcastSearchResult> ParseResults(ReadOnlySpan<byte> json)
    {
        var results = new List<PodcastSearchResult>();
        using var doc = JsonDocument.Parse(json.ToArray());
        if (!doc.RootElement.TryGetProperty("feeds", out var feeds) || feeds.ValueKind != JsonValueKind.Array)
            return results;

        foreach (var feed in feeds.EnumerateArray())
        {
            var url = Str(feed, "url");
            if (url is null || !PodcastHttp.IsAllowedUrl(url, out _)) continue;
            var image = Str(feed, "image") ?? Str(feed, "artwork");
            results.Add(new PodcastSearchResult(
                Str(feed, "title") ?? url,
                Str(feed, "author") ?? Str(feed, "ownerName"),
                FeedParser.StripHtml(Str(feed, "description")),
                url,
                image is not null && PodcastHttp.IsAllowedUrl(image, out _) ? image : null));
        }
        return results;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString())
            ? p.GetString()!.Trim()
            : null;
}
