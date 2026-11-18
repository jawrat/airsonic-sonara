using System.Net;
using System.Text;
using Sonara.Api.Podcasts;

namespace Sonara.Tests;

public class IpGuardTests
{
    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("93.184.216.34", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]   // cloud metadata
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd12:3456::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]   // IPv4-mapped private address
    [InlineData("::ffff:8.8.8.8", true)]
    public void Only_public_addresses_pass(string address, bool expected) =>
        Assert.Equal(expected, IpGuard.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("https://example.com/feed.xml", true)]
    [InlineData("http://example.com/feed", true)]
    [InlineData("ftp://example.com/feed", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData("https://user:pw@example.com/feed", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void Only_plain_http_urls_are_allowed(string? url, bool expected) =>
        Assert.Equal(expected, PodcastHttp.IsAllowedUrl(url, out _));
}

public class FeedParserTests
{
    private const string Feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd">
          <channel>
            <title> The Show </title>
            <description>&lt;p&gt;About &amp;amp; things&lt;/p&gt;</description>
            <itunes:image href="https://img.example.com/cover.jpg"/>
            <item>
              <title>Episode 2</title>
              <guid>ep-2</guid>
              <pubDate>Tue, 10 Nov 2026 12:00:00 GMT</pubDate>
              <itunes:duration>1:02:03</itunes:duration>
              <enclosure url="https://cdn.example.com/2.mp3" length="1000" type="audio/mpeg"/>
            </item>
            <item>
              <title>Episode 1</title>
              <pubDate>Mon, 09 Nov 2026 08:00:00 EST</pubDate>
              <enclosure url="https://cdn.example.com/1.m4a?x=1" length="500"/>
            </item>
            <item><title>Video one</title><enclosure url="https://cdn.example.com/v.mp4" type="video/mp4"/></item>
            <item><title>No enclosure</title></item>
            <item><title>Duplicate</title><guid>ep-2</guid><enclosure url="https://cdn.example.com/dup.mp3" type="audio/mpeg"/></item>
            <item><title>Bad scheme</title><enclosure url="file:///etc/passwd" type="audio/mpeg"/></item>
          </channel>
        </rss>
        """;

    [Fact]
    public void Channel_details_are_read_and_cleaned()
    {
        var feed = FeedParser.Parse(Feed);
        Assert.Equal("The Show", feed.Title);
        Assert.Equal("About & things", feed.Description);
        Assert.Equal("https://img.example.com/cover.jpg", feed.ImageUrl);
    }

    [Fact]
    public void Only_valid_unique_audio_episodes_are_kept()
    {
        var episodes = FeedParser.Parse(Feed).Episodes;
        Assert.Equal(["Episode 2", "Episode 1"], episodes.Select(e => e.Title));
    }

    [Fact]
    public void Episode_fields_are_parsed()
    {
        var e = FeedParser.Parse(Feed).Episodes[0];
        Assert.Equal("ep-2", e.Guid);
        Assert.Equal(3723, e.DurationSeconds);
        Assert.Equal(1000, e.Length);
        Assert.Equal(new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc), e.PublishUtc);
    }

    [Fact]
    public void Missing_guid_falls_back_to_the_audio_url_and_zone_names_are_understood()
    {
        var e = FeedParser.Parse(Feed).Episodes[1];
        Assert.Equal("https://cdn.example.com/1.m4a?x=1", e.Guid);
        Assert.Equal(new DateTime(2026, 11, 9, 13, 0, 0, DateTimeKind.Utc), e.PublishUtc); // 08:00 EST
    }

    [Fact]
    public void Non_rss_documents_are_rejected()
    {
        Assert.Throws<FeedFormatException>(() => FeedParser.Parse("<html><body>hi</body></html>"));
        Assert.Throws<FeedFormatException>(() => FeedParser.Parse("not xml at all"));
    }

    [Fact]
    public void Doctype_declarations_are_refused()
    {
        const string xxe = """<?xml version="1.0"?><!DOCTYPE rss [<!ENTITY x SYSTEM "file:///etc/passwd">]><rss><channel><title>&x;</title></channel></rss>""";
        Assert.Throws<FeedFormatException>(() => FeedParser.Parse(xxe));
    }

    [Fact]
    public void A_stream_can_be_parsed_too()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Feed));
        Assert.Equal(2, FeedParser.Parse(stream).Episodes.Count);
    }

    [Theory]
    [InlineData("3600", 3600)]
    [InlineData("12:34", 754)]
    [InlineData("1:02:03", 3723)]
    [InlineData("00:00:30.5", 31)]
    [InlineData("abc", 0)]
    [InlineData("-5", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void Duration_formats(string? text, int expected) =>
        Assert.Equal(expected, FeedParser.ParseDuration(text));

    [Fact]
    public void Long_descriptions_are_truncated() =>
        Assert.True(FeedParser.StripHtml(new string('x', 5000))!.Length <= 2001);
}

public class PodcastRulesTests
{
    [Theory]
    [InlineData("pe-12", MediaKind.Episode, 12)]
    [InlineData("pc-3", MediaKind.Channel, 3)]
    [InlineData("45", MediaKind.Track, 45)]
    public void Ids_are_classified(string id, MediaKind kind, int number) =>
        Assert.Equal(new MediaRef(kind, number), MediaRef.Parse(id));

    [Theory]
    [InlineData("pe-")]
    [InlineData("pe-abc")]
    [InlineData("pe-0")]
    [InlineData("-3")]
    [InlineData("x")]
    [InlineData("")]
    [InlineData(null)]
    public void Bad_ids_are_rejected(string? id) => Assert.Null(MediaRef.Parse(id));

    [Fact]
    public void Queue_codes_round_trip()
    {
        var track = new MediaRef(MediaKind.Track, 7);
        var episode = new MediaRef(MediaKind.Episode, 7);
        Assert.Equal(7, track.ToQueueCode());
        Assert.Equal(-7, episode.ToQueueCode());
        Assert.Equal(track, MediaRef.FromQueueCode(7));
        Assert.Equal(episode, MediaRef.FromQueueCode(-7));
    }

    [Theory]
    [InlineData("audio/mpeg", "https://x/a", true)]
    [InlineData(null, "https://x/a.mp3?token=1", true)]
    [InlineData("application/octet-stream", "https://x/a.flac", true)]
    [InlineData("video/mp4", "https://x/a.mp3", false)]
    [InlineData("application/pdf", "https://x/a.pdf", false)]
    [InlineData(null, "https://x/a", false)]
    public void Audio_detection(string? mime, string url, bool expected) =>
        Assert.Equal(expected, PodcastRules.IsAudio(mime, url));

    [Theory]
    [InlineData("audio/mpeg", "https://x/a", "mp3")]
    [InlineData("audio/x-m4a", "https://x/a", "m4a")]
    [InlineData(null, "https://x/ep.OGG", "ogg")]
    [InlineData("audio/weird", "https://x/a", "mp3")]
    public void Suffix_choice(string? mime, string url, string expected) =>
        Assert.Equal(expected, PodcastRules.SuffixFor(mime, url));

    [Theory]
    [InlineData("A/B: C?*", "A_B_ C__")]
    [InlineData("../../etc/passwd", "_.._etc_passwd")]
    [InlineData("   ", "episode")]
    [InlineData("...", "episode")]
    public void File_names_are_safe(string title, string expected) =>
        Assert.Equal(expected, PodcastRules.SafeFileName(title));

    [Fact]
    public void File_names_are_length_limited() =>
        Assert.True(PodcastRules.SafeFileName(new string('a', 500)).Length <= 80);

    [Fact]
    public void File_names_never_contain_path_separators() =>
        Assert.DoesNotContain(PodcastRules.SafeFileName("a/b\\c").ToCharArray(), c => c is '/' or '\\');
}

public class PodcastIndexTests
{
    [Fact]
    public void Authorization_is_sha1_of_key_secret_and_date() =>
        // sha1("abc" + "def" + "1700000000")
        Assert.Equal(
            Convert.ToHexString(System.Security.Cryptography.SHA1.HashData("abcdef1700000000"u8)).ToLowerInvariant(),
            PodcastIndexClient.ComputeAuth("abc", "def", 1700000000));

    [Fact]
    public void Search_results_are_parsed_and_unusable_ones_dropped()
    {
        const string json = """
            {"status":"true","feeds":[
              {"title":"Good","url":"https://f.example.com/rss","author":"Me","description":"<b>Hi</b>","image":"https://i.example.com/a.jpg"},
              {"title":"No url"},
              {"title":"Bad url","url":"file:///x"},
              {"title":"Artwork only","url":"https://f2.example.com/rss","artwork":"https://i.example.com/b.jpg"}
            ]}
            """;
        var results = PodcastIndexClient.ParseResults(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal(["Good", "Artwork only"], results.Select(r => r.Title));
        Assert.Equal("Hi", results[0].Description);
        Assert.Equal("https://i.example.com/b.jpg", results[1].ImageUrl);
    }

    [Fact]
    public void Missing_feeds_array_gives_no_results() =>
        Assert.Empty(PodcastIndexClient.ParseResults("""{"status":"false"}"""u8));
}
