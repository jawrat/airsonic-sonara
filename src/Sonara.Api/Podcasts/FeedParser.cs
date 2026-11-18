using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Sonara.Api.Podcasts;

public sealed record ParsedEpisode(
    string Guid, string Title, string? Description, DateTime? PublishUtc,
    string AudioUrl, long Length, string? MimeType, int DurationSeconds);

public sealed record ParsedFeed(string Title, string? Description, string? ImageUrl, IReadOnlyList<ParsedEpisode> Episodes);

public sealed class FeedFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Reads RSS podcast feeds. Pure and defensive: feeds are untrusted input.</summary>
public static partial class FeedParser
{
    private static readonly XNamespace ITunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";

    private static readonly Dictionary<string, string> ZoneOffsets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["UT"] = "+0000", ["UTC"] = "+0000", ["GMT"] = "+0000",
        ["EST"] = "-0500", ["EDT"] = "-0400", ["CST"] = "-0600", ["CDT"] = "-0500",
        ["MST"] = "-0700", ["MDT"] = "-0600", ["PST"] = "-0800", ["PDT"] = "-0700",
    };

    public static ParsedFeed Parse(string xml) => Parse(new StringReader(xml));

    public static ParsedFeed Parse(Stream xml) => Parse(XmlReader.Create(xml, Settings()));

    private static ParsedFeed Parse(TextReader reader) => Parse(XmlReader.Create(reader, Settings()));

    private static XmlReaderSettings Settings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit, // no entity expansion tricks
        XmlResolver = null,
        MaxCharactersInDocument = 50_000_000,
    };

    private static ParsedFeed Parse(XmlReader reader)
    {
        using var xmlReader = reader;
        XDocument doc;
        try
        {
            doc = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new FeedFormatException("That URL did not return a valid RSS feed.", ex);
        }

        var channel = doc.Root is { Name.LocalName: "rss" } rss ? rss.Element("channel") : null;
        if (channel is null) throw new FeedFormatException("That URL is not an RSS podcast feed.");

        var title = Clean(channel.Element("title")?.Value) ?? "Untitled podcast";
        var description = StripHtml(channel.Element("description")?.Value ?? channel.Element(ITunes + "summary")?.Value);
        var image = channel.Element(ITunes + "image")?.Attribute("href")?.Value
                    ?? channel.Element("image")?.Element("url")?.Value;

        var episodes = new List<ParsedEpisode>();
        var seen = new HashSet<string>();
        foreach (var item in channel.Elements("item"))
        {
            var enclosure = item.Element("enclosure");
            var url = enclosure?.Attribute("url")?.Value?.Trim();
            var mime = enclosure?.Attribute("type")?.Value?.Trim();
            if (url is null || !PodcastHttp.IsAllowedUrl(url, out _) || !PodcastRules.IsAudio(mime, url)) continue;

            var guid = Clean(item.Element("guid")?.Value) ?? url;
            if (!seen.Add(guid)) continue;

            _ = long.TryParse(enclosure!.Attribute("length")?.Value, out var length);
            episodes.Add(new ParsedEpisode(
                guid,
                Clean(item.Element("title")?.Value) ?? "Untitled episode",
                StripHtml(item.Element("description")?.Value ?? item.Element(ITunes + "summary")?.Value),
                ParseDate(item.Element("pubDate")?.Value),
                url,
                Math.Max(length, 0),
                mime,
                ParseDuration(item.Element(ITunes + "duration")?.Value)));
        }

        return new ParsedFeed(title, description, Clean(image), episodes);
    }

    /// <summary>Accepts "3600", "12:34" and "1:02:03" (fractions allowed). Returns 0 if unparseable.</summary>
    public static int ParseDuration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        double total = 0;
        foreach (var part in text.Trim().Split(':'))
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || n < 0) return 0;
            total = total * 60 + n;
        }
        return total is > 0 and < 7 * 24 * 3600 ? (int)Math.Round(total, MidpointRounding.AwayFromZero) : 0;
    }

    /// <summary>Parses RFC 822 dates, including the North American zone names RSS feeds still use.</summary>
    public static DateTime? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();

        var zone = ZoneRegex().Match(value);
        if (zone.Success && ZoneOffsets.TryGetValue(zone.Groups[1].Value, out var offset))
            value = value[..zone.Index] + " " + offset;

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.UtcDateTime
            : null;
    }

    /// <summary>Descriptions are often HTML; keep the text, bounded in length.</summary>
    public static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        var text = WebUtility.HtmlDecode(TagRegex().Replace(html, " "));
        text = WhitespaceRegex().Replace(text, " ").Trim();
        if (text.Length == 0) return null;
        return text.Length > 2000 ? text[..2000] + "…" : text;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex(@"\s([A-Za-z]{2,4})$")]
    private static partial Regex ZoneRegex();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
