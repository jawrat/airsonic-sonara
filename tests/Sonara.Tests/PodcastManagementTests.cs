using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;
using Sonara.Api.Podcasts;
using Sonara.Core.Entities;
using Sonara.Data;

namespace Sonara.Tests;

public class PodcastOpmlTests
{
    [Fact]
    public void Nested_groups_and_unicode_titles_are_supported()
    {
        var feeds = PodcastOpml.Parse("<opml version='2.0'><body><outline text='Group'><outline text='Björk &amp; friends' type='rss' xmlUrl='https://EXAMPLE.com/feed#fragment'/></outline></body></opml>");
        Assert.Single(feeds); Assert.Equal("Björk & friends", feeds[0].Title); Assert.Equal("https://example.com/feed", feeds[0].Url); Assert.Null(feeds[0].Error);
    }
    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:password@example.com/feed")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public void Invalid_urls_are_reported_without_rejecting_other_rows(string url)
    {
        var feeds = PodcastOpml.Parse($"<opml><body><outline xmlUrl='{url}'/><outline xmlUrl='https://example.com/good'/></body></opml>");
        Assert.NotNull(feeds[0].Error); Assert.Null(feeds[1].Error);
    }
    [Theory]
    [InlineData("<rss><channel/></rss>")]
    [InlineData("<opml/>")]
    [InlineData("<opml><body>")]
    [InlineData("<!DOCTYPE opml [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><opml><body><outline text='&xxe;' xmlUrl='https://example.com'/></body></opml>")]
    public void Malformed_documents_and_entities_are_rejected(string xml) => Assert.Throws<PodcastException>(() => PodcastOpml.Parse(xml));
    [Fact]
    public void Size_and_subscription_count_are_bounded()
    {
        Assert.Throws<PodcastException>(() => PodcastOpml.Parse(new string('x', PodcastOpml.MaxCharacters + 1)));
        Assert.Throws<PodcastException>(() => PodcastOpml.Parse("<opml><body>" + string.Concat(Enumerable.Repeat("<outline xmlUrl='https://example.com'/>", 201)) + "</body></opml>"));
    }
}
public class PodcastRetentionRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
    [Fact]
    public void Disabled_policy_selects_nothing() => Assert.Empty(RetentionRules.Select([new(1, 1, Now.AddDays(-100), Now.AddDays(-100), false)], 0, 0, Now));
    [Fact]
    public void Count_is_per_channel_and_protected_and_recent_downloads_survive()
    {
        RetentionEpisode[] episodes = [new(1, 1, Now.AddDays(-1), Now.AddDays(-5), false), new(2, 1, Now.AddDays(-2), Now.AddDays(-5), false),
            new(3, 1, Now.AddDays(-3), Now.AddDays(-5), true), new(4, 1, Now.AddDays(-4), Now, false), new(5, 2, Now.AddDays(-2), Now.AddDays(-5), false)];
        var plan = RetentionRules.Select(episodes, 0, 1, Now); Assert.Equal([2], plan.Keys.ToArray()); Assert.Equal("count", plan[2]);
    }
    [Fact]
    public void Age_uses_download_time_and_either_policy_can_select_a_file()
    {
        var plan = RetentionRules.Select([new(1, 1, Now.AddDays(-100), Now, false), new(2, 1, Now, Now.AddDays(-40), false)], 30, 0, Now);
        Assert.Equal([2], plan.Keys.ToArray()); Assert.Equal("age", plan[2]);
    }
    [Theory]
    [InlineData("/podcasts/1/episode.mp3", true)]
    [InlineData("/podcasts/2/episode.mp3", false)]
    [InlineData("/podcasts/1/../../secret.key", false)]
    [InlineData("/music/song.mp3", false)]
    [InlineData("episode.mp3", false)]
    public void File_removal_stays_within_its_channel(string file, bool expected)
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal(expected, RetentionRules.OwnsPath("/podcasts", 1, file));
    }
}

[Collection("PostgreSQL integration")]
public class PodcastManagementIntegrationTests(ParityHost host) : IClassFixture<ParityHost>
{
    [DatabaseFact]
    public async Task Import_preview_does_not_write_apply_deduplicates_and_export_round_trips()
    {
        using var client = host.CreateClient();
        const string xml = "<opml><body><outline text='Import test' xmlUrl='http://127.0.0.1:9/feed'/><outline xmlUrl='http://127.0.0.1:9/feed#same'/><outline xmlUrl='file:///etc/passwd'/></body></opml>";
        async Task<System.Text.Json.Nodes.JsonNode> Import(bool preview)
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["u"] = "admin", ["p"] = "Test-Password-123!", ["f"] = "json", ["c"] = "test", ["v"] = "1.16.1", ["opml"] = xml, ["dryRun"] = preview.ToString(), ["selectedUrl"] = "http://127.0.0.1:9/feed" });
            var result = await client.PostAsync("/rest/importPodcastOpml.view", form);
            return System.Text.Json.Nodes.JsonNode.Parse(await result.Content.ReadAsStringAsync())!["subsonic-response"]!;
        }
        var preview = await Import(true); Assert.Equal(1, preview["podcastImport"]!["ready"]!.GetValue<int>()); Assert.Equal(1, preview["podcastImport"]!["duplicate"]!.GetValue<int>()); Assert.Equal(1, preview["podcastImport"]!["invalid"]!.GetValue<int>());
        using (var scope = host.Services.CreateScope()) Assert.False(await scope.ServiceProvider.GetRequiredService<SonaraDbContext>().PodcastChannels.AnyAsync(c => c.Url == "http://127.0.0.1:9/feed"));
        var applied = await Import(false); Assert.Equal(1, applied["podcastImport"]!["queued"]!.GetValue<int>());
        var repeat = await Import(false); Assert.Equal(0, repeat["podcastImport"]!["queued"]!.GetValue<int>()); Assert.Equal(1, repeat["podcastImport"]!["existing"]!.GetValue<int>());
        var exported = await client.GetStringAsync(host.Url("exportPodcasts/opml", "admin")); Assert.Contains(PodcastOpml.Parse(exported), f => f.Url == "http://127.0.0.1:9/feed");
        var forbidden = await host.Call("importPodcastOpml", extra: "&opml=%3Copml%2F%3E"); Assert.Equal(50, forbidden["error"]!["code"]!.GetValue<int>());
    }
    [DatabaseFact]
    public async Task Import_requires_selection_and_subscribes_only_to_selected_feeds()
    {
        using var client = host.CreateClient();
        const string a = "http://127.0.0.1:9/choose-a"; const string b = "http://127.0.0.1:9/choose-b";
        var xml = $"<opml><body><outline xmlUrl='{a}'/><outline xmlUrl='{b}'/></body></opml>";
        async Task<System.Text.Json.Nodes.JsonNode> Apply(string? selected)
        {
            var values = new List<KeyValuePair<string, string>> { new("u", "admin"), new("p", "Test-Password-123!"), new("f", "json"), new("dryRun", "false"), new("opml", xml) };
            if (selected is not null) values.Add(new("selectedUrl", selected));
            var response = await client.PostAsync("/rest/importPodcastOpml", new FormUrlEncodedContent(values));
            return System.Text.Json.Nodes.JsonNode.Parse(await response.Content.ReadAsStringAsync())!["subsonic-response"]!;
        }
        Assert.Equal("failed", (await Apply(null))["status"]!.ToString());
        Assert.Equal("failed", (await Apply("https://example.com/not-in-file"))["status"]!.ToString());
        var result = await Apply(b); Assert.Equal(1, result["podcastImport"]!["queued"]!.GetValue<int>());
        using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        Assert.False(await db.PodcastChannels.AnyAsync(c => c.Url == a)); Assert.True(await db.PodcastChannels.AnyAsync(c => c.Url == b));
    }

    [DatabaseFact]
    public async Task Retention_preserves_pins_bookmarks_queues_recent_downloads_and_other_paths()
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SonaraOptions>>().Value.Podcasts;
        options.RetentionDays = 30; options.RetentionKeepLatest = 0;
        try
        {
            var channel = new PodcastChannel { Url = "https://example.com/retention", Title = "Retention test", LastRefreshUtc = DateTime.UtcNow, Status = PodcastStatus.Completed };
            db.Add(channel); await db.SaveChangesAsync();
            var dir = Path.Combine(host.Root, "podcasts", channel.Id.ToString()); Directory.CreateDirectory(dir);
            var episodes = new List<PodcastEpisode>();
            for (var i = 0; i < 6; i++)
            {
                var path = Path.Combine(dir, $"{i}.mp3"); await File.WriteAllBytesAsync(path, [1, 2, 3]);
                episodes.Add(new PodcastEpisode { ChannelId = channel.Id, Guid = "retention-" + i, Title = "Episode " + i, AudioUrl = "https://example.com/audio", FilePath = path,
                    Size = 3, Status = PodcastStatus.Completed, DownloadedUtc = DateTime.UtcNow.AddDays(i == 4 ? 0 : -60), Pinned = i == 1 });
            }
            var foreignPath = Path.Combine(host.Root, "foreign.mp3"); await File.WriteAllBytesAsync(foreignPath, [4, 5, 6]); episodes[5].FilePath = foreignPath;
            db.AddRange(episodes); await db.SaveChangesAsync();
            var user = await db.Users.FirstAsync(u => u.Username == "listener");
            db.Add(new Bookmark { UserId = user.Id, EpisodeId = episodes[2].Id });
            db.Add(new PlayQueue { UserId = user.Id, TrackIds = [-episodes[3].Id], CurrentTrackId = -episodes[3].Id }); await db.SaveChangesAsync();
            var retention = scope.ServiceProvider.GetRequiredService<PodcastRetention>();
            var preview = await retention.PreviewAsync(); Assert.Single(preview.Candidates); Assert.Equal(episodes[0].Id, preview.Candidates[0].Id);
            var run = await retention.RunAsync(); Assert.Equal(1, run.Removed); Assert.Equal(0, run.Failed); Assert.False(File.Exists(Path.Combine(dir, "0.mp3")));
            foreach (var i in Enumerable.Range(1, 5)) Assert.True(File.Exists(episodes[i].FilePath));
            var removed = await db.PodcastEpisodes.AsNoTracking().FirstAsync(e => e.Id == episodes[0].Id); Assert.Null(removed.FilePath); Assert.Equal(PodcastStatus.New, removed.Status);
            var body = await host.Call("getPodcasts", extra: "&id=" + channel.Id); Assert.Contains(body["podcasts"]!["channel"]![0]!["episode"]!.AsArray(), e => e!["id"]!.ToString() == removed.Id.ToString());
            var denied = await host.Call("runPodcastRetention"); Assert.Equal(50, denied["error"]!["code"]!.GetValue<int>());
            if (OperatingSystem.IsLinux() && Environment.UserName != "root")
            {
                var inaccessible = Path.Combine(dir, "cannot-delete.mp3"); await File.WriteAllTextAsync(inaccessible, "keep on failure");
                var failure = new PodcastEpisode { ChannelId = channel.Id, Guid = "failure", Title = "Must survive", FilePath = inaccessible,
                    Status = PodcastStatus.Completed, DownloadedUtc = DateTime.UtcNow.AddDays(-60) };
                db.Add(failure); await db.SaveChangesAsync();
                var mode = File.GetUnixFileMode(dir);
                try
                {
                    File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
                    var failed = await retention.RunAsync(); Assert.Equal(1, failed.Failed); Assert.True(File.Exists(inaccessible));
                    var stored = await db.PodcastEpisodes.AsNoTracking().SingleAsync(e => e.Id == failure.Id);
                    Assert.Equal(inaccessible, stored.FilePath); Assert.Equal(PodcastStatus.Completed, stored.Status);
                }
                finally { File.SetUnixFileMode(dir, mode); }
            }
        }
        finally { options.RetentionDays = 0; options.RetentionKeepLatest = 0; }
    }
    [DatabaseFact]
    public async Task Imported_subscription_limits_first_downloads_then_queues_only_new_episodes()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        async Task Respond(int count)
        {
            using var connection = await listener.AcceptTcpClientAsync();
            await using var stream = connection.GetStream();
            var buffer = new byte[8192]; var received = await stream.ReadAsync(buffer); Assert.True(received > 0);
            var episodes = string.Concat(Enumerable.Range(1, count).Select(i => $"<item><guid>item-{i}</guid><title>Episode {i}</title><pubDate>{DateTime.UtcNow.AddDays(-count + i):r}</pubDate><enclosure url='https://example.com/audio-{i}.mp3' type='audio/mpeg'/></item>"));
            var body = System.Text.Encoding.UTF8.GetBytes("<rss><channel><title>Imported feed</title>" + episodes + "</channel></rss>");
            var headers = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/rss+xml\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers); await stream.WriteAsync(body);
        }
        using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        var channel = new PodcastChannel { Url = $"http://127.0.0.1:{port}/feed", Title = "Queued feed" }; db.Add(channel); await db.SaveChangesAsync();
        var queue = new RecordingPodcastQueue(); using var http = new PodcastHttp(true);
        var service = new PodcastService(db, http, queue, new PodcastFileGate(), Options.Create(new SonaraOptions { DataDir = host.Root, Podcasts = new() { AutoDownloadLatest = 2 } }), Microsoft.Extensions.Logging.Abstractions.NullLogger<PodcastService>.Instance);
        var firstResponse = Respond(10); await service.RefreshAsync(channel, CancellationToken.None); await firstResponse;
        Assert.Equal(2, queue.Jobs.Count); Assert.NotNull(channel.LastRefreshUtc);
        queue.Jobs.Clear();
        var nextResponse = Respond(11); await service.RefreshAsync(channel, CancellationToken.None); await nextResponse;
        Assert.Single(queue.Jobs); Assert.Equal(11, await db.PodcastEpisodes.CountAsync(e => e.ChannelId == channel.Id));
    }

    private sealed class RecordingPodcastQueue : IPodcastQueue
    {
        public List<PodcastJob> Jobs { get; } = [];
        public void Enqueue(PodcastJob job) => Jobs.Add(job);
    }

    [DatabaseFact]
    public async Task Pin_endpoint_requires_podcast_management_and_manual_remove_preserves_episode()
    {
        using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        var channel = new PodcastChannel { Url = "https://example.com/pins", Title = "Pins" }; db.Add(channel); await db.SaveChangesAsync();
        var dir = Path.Combine(host.Root, "podcasts", channel.Id.ToString()); Directory.CreateDirectory(dir); var file = Path.Combine(dir, "pin.mp3"); await File.WriteAllTextAsync(file, "audio");
        var episode = new PodcastEpisode { ChannelId = channel.Id, Guid = "pin-episode", Title = "Pin me", FilePath = file, Status = PodcastStatus.Completed }; db.Add(episode); await db.SaveChangesAsync();
        var denied = await host.Call("setPodcastEpisodePinned", extra: "&id=" + episode.Id + "&pinned=true"); Assert.Equal(50, denied["error"]!["code"]!.GetValue<int>());
        var pinned = await host.Call("setPodcastEpisodePinned", "admin", "&id=" + episode.Id + "&pinned=true"); Assert.Equal("ok", pinned["status"]!.ToString());
        var removed = await host.Call("deletePodcastEpisode", "admin", "&id=" + episode.Id); Assert.Equal("ok", removed["status"]!.ToString()); Assert.False(File.Exists(file));
        await db.Entry(episode).ReloadAsync(); Assert.Equal(PodcastStatus.New, episode.Status); Assert.True(episode.Pinned);
    }
}

public class ManagedPodcastFileTests
{
    [Fact]
    public void Cleanup_refuses_directories_and_symbolic_links()
    {
        var root = Path.Combine(Path.GetTempPath(), "sonara-path-" + Guid.NewGuid().ToString("N"));
        var channel = Path.Combine(root, "1"); Directory.CreateDirectory(channel);
        try
        {
            var folder = Path.Combine(channel, "not-an-episode.mp3"); Directory.CreateDirectory(folder);
            Assert.False(RetentionRules.IsManagedFile(root, 1, folder));
            var outside = Path.Combine(root, "keep.mp3"); File.WriteAllText(outside, "keep");
            if (!OperatingSystem.IsWindows())
            {
                var link = Path.Combine(channel, "symlink.mp3"); File.CreateSymbolicLink(link, outside);
                Assert.False(RetentionRules.IsManagedFile(root, 1, link)); Assert.True(File.Exists(outside));
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
