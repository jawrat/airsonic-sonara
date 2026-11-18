using System.Xml;
using System.Xml.Linq;

namespace Sonara.Api.Podcasts;

public sealed record OpmlFeed(string Title, string Url, string? Error);

/// <summary>Bounded OPML parsing; never resolves external entities or fetches feed URLs.</summary>
public static class PodcastOpml
{
    public const int MaxCharacters = 1024 * 1024;
    public const int MaxFeeds = 200;
    public static IReadOnlyList<OpmlFeed> Parse(string xml)
    {
        if (xml.Length > MaxCharacters) throw new PodcastException("OPML files must be at most 1 MB of text.");
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml.TrimStart('\uFEFF')), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = MaxCharacters,
            });
            var doc = XDocument.Load(reader);
            if (doc.Root?.Name.LocalName != "opml") throw new PodcastException("Choose an OPML subscription file.");
            var body = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "body");
            if (body is null) throw new PodcastException("This OPML file has no subscription body.");
            var outlines = body.Descendants().Where(e => e.Name.LocalName == "outline" && e.Attribute("xmlUrl") is not null).ToList();
            if (outlines.Count > MaxFeeds) throw new PodcastException($"Import at most {MaxFeeds} subscriptions at a time.");
            return outlines.Select(o =>
            {
                var raw = o.Attribute("xmlUrl")!.Value.Trim();
                var normalized = Normalize(raw);
                var title = (o.Attribute("title")?.Value ?? o.Attribute("text")?.Value ?? raw).Trim();
                if (title.Length > 500) title = title[..500];
                return new OpmlFeed(title, normalized ?? raw[..Math.Min(raw.Length, 2048)], normalized is null ? "A feed needs an HTTP(S) URL without credentials (maximum 2048 characters)." : null);
            }).ToList();
        }
        catch (XmlException ex) { throw new PodcastException("The OPML file is malformed or contains prohibited XML declarations.", ex); }
    }
    public static string? Normalize(string? url) => url?.Length <= 2048 && PodcastHttp.IsAllowedUrl(url, out var uri)
        ? new UriBuilder(uri) { Fragment = "" }.Uri.AbsoluteUri : null;
}
