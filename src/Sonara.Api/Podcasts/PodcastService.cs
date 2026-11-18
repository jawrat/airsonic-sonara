using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;
using Sonara.Core.Entities;
using Sonara.Data;

namespace Sonara.Api.Podcasts;

/// <summary>A problem worth showing to the user (bad feed, already subscribed, download too big...).</summary>
public sealed class PodcastException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class PodcastService(
    SonaraDbContext db,
    PodcastHttp http,
    IPodcastQueue queue,
    PodcastFileGate fileGate,
    IOptions<SonaraOptions> options,
    ILogger<PodcastService> logger)
{
    private const long MaxFeedBytes = 20L * 1024 * 1024;

    private PodcastOptions Settings => options.Value.Podcasts;

    public string Folder => Path.GetFullPath(string.IsNullOrWhiteSpace(Settings.Folder)
        ? Path.Combine(options.Value.DataDir, "podcasts")
        : Settings.Folder);

    // ---- subscribing and refreshing ------------------------------------------------------------

    public async Task<PodcastChannel> SubscribeAsync(string? url, CancellationToken ct)
    {
        if (!PodcastHttp.IsAllowedUrl(url, out var uri))
            throw new PodcastException("Enter a full http:// or https:// feed address.");

        var normalized = PodcastOpml.Normalize(uri.AbsoluteUri)!;
        if (await db.PodcastChannels.AnyAsync(c => c.Url == normalized, ct))
            throw new PodcastException("You are already subscribed to that podcast.");

        var feed = await FetchFeedAsync(uri, ct);
        var channel = new PodcastChannel
        {
            Url = normalized,
            Title = feed.Title,
            Description = feed.Description,
            ImageUrl = feed.ImageUrl,
            Status = PodcastStatus.Completed,
            LastRefreshUtc = DateTime.UtcNow,
        };
        AddEpisodes(channel, feed, existing: []);
        db.PodcastChannels.Add(channel);
        await db.SaveChangesAsync(ct);

        await FetchImageAsync(channel, ct);

        // A new subscription shouldn't pull a back catalogue of hundreds of episodes.
        foreach (var episode in channel.Episodes.OrderByDescending(e => e.PublishUtc).Take(Settings.AutoDownloadLatest))
            queue.Enqueue(PodcastJob.Download(episode.Id));

        return channel;
    }

    /// <returns>How many new episodes were found.</returns>
    public async Task<int> RefreshAsync(PodcastChannel channel, CancellationToken ct)
    {
        try
        {
            if (!PodcastHttp.IsAllowedUrl(channel.Url, out var uri))
                throw new PodcastException("The stored feed address is not valid.");

            var feed = await FetchFeedAsync(uri, ct);
            var existing = await db.PodcastEpisodes.Where(e => e.ChannelId == channel.Id)
                .Select(e => e.Guid).ToListAsync(ct);

            channel.Title = feed.Title;
            channel.Description = feed.Description;
            if (feed.ImageUrl != channel.ImageUrl)
            {
                channel.ImageUrl = feed.ImageUrl;
                channel.ImagePath = null;
            }

            var firstRefresh = channel.LastRefreshUtc is null;
            var added = AddEpisodes(channel, feed, existing.ToHashSet());
            channel.Status = PodcastStatus.Completed;
            channel.ErrorMessage = null;
            channel.LastRefreshUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            if (channel.ImagePath is null) await FetchImageAsync(channel, ct);

            if (Settings.AutoDownloadLatest > 0)
            {
                var selected = firstRefresh
                    ? added.OrderByDescending(e => e.PublishUtc).Take(Settings.AutoDownloadLatest)
                    : added.AsEnumerable();
                foreach (var episode in selected) queue.Enqueue(PodcastJob.Download(episode.Id));
            }
            return added.Count;
        }
        catch (PodcastException ex)
        {
            channel.Status = PodcastStatus.Error;
            channel.ErrorMessage = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task RefreshAllAsync(CancellationToken ct)
    {
        foreach (var channel in await db.PodcastChannels.ToListAsync(ct))
        {
            try
            {
                var added = await RefreshAsync(channel, ct);
                if (added > 0) logger.LogInformation("Podcast '{Title}': {Count} new episode(s)", channel.Title, added);
            }
            catch (PodcastException ex)
            {
                logger.LogWarning("Could not refresh podcast '{Title}': {Message}", channel.Title, ex.Message);
            }
        }
    }

    private static List<PodcastEpisode> AddEpisodes(PodcastChannel channel, ParsedFeed feed, HashSet<string> existing)
    {
        var added = new List<PodcastEpisode>();
        foreach (var item in feed.Episodes.Where(e => !existing.Contains(e.Guid)))
        {
            var episode = new PodcastEpisode
            {
                Guid = item.Guid,
                Title = item.Title,
                Description = item.Description,
                PublishUtc = item.PublishUtc,
                AudioUrl = item.AudioUrl,
                ContentType = item.MimeType,
                Size = item.Length,
                DurationSeconds = item.DurationSeconds,
                Status = PodcastStatus.New,
            };
            channel.Episodes.Add(episode);
            added.Add(episode);
        }
        return added;
    }

    private async Task<ParsedFeed> FetchFeedAsync(Uri uri, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using var response = await http.Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new PodcastException($"The feed server answered {(int)response.StatusCode} {response.ReasonPhrase}.");

            var body = await PodcastHttp.ReadLimitedAsync(response, MaxFeedBytes, timeout.Token);
            using var stream = new MemoryStream(body);
            return FeedParser.Parse(stream);
        }
        catch (FeedFormatException ex)
        {
            throw new PodcastException(ex.Message, ex);
        }
        catch (InvalidDataException ex)
        {
            throw new PodcastException(ex.Message, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PodcastException("Could not reach the feed: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new PodcastException("The feed server took too long to answer.", ex);
        }
    }

    // ---- images ----------------------------------------------------------------------------------

    private async Task FetchImageAsync(PodcastChannel channel, CancellationToken ct)
    {
        if (!PodcastHttp.IsAllowedUrl(channel.ImageUrl, out var uri)) return;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            using var response = await http.Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode) return;

            var type = response.Content.Headers.ContentType?.MediaType;
            var ext = type switch
            {
                "image/jpeg" => "jpg",
                "image/png" => "png",
                "image/webp" => "webp",
                "image/gif" => "gif",
                _ => null,
            };
            if (ext is null) return;

            var bytes = await PodcastHttp.ReadLimitedAsync(response, Math.Clamp(Settings.MaxCoverMegabytes, 1, 100) * 1024L * 1024, timeout.Token);
            var dir = ChannelFolder(channel.Id);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "cover." + ext);
            await File.WriteAllBytesAsync(path, bytes, ct);
            channel.ImagePath = path;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException
                                       or UnauthorizedAccessException or TaskCanceledException)
        {
            logger.LogInformation("No cover image for podcast '{Title}': {Message}", channel.Title, ex.Message);
        }
    }

    // ---- downloading -------------------------------------------------------------------------------

    public async Task DownloadAsync(int episodeId, CancellationToken ct)
    {
        await DownloadCoreAsync(episodeId, ct);
    }

    private async Task DownloadCoreAsync(int episodeId, CancellationToken ct)
    {
        var episode = await db.PodcastEpisodes.Include(e => e.Channel).FirstOrDefaultAsync(e => e.Id == episodeId, ct);
        if (episode is null) return;
        using (var lease = await fileGate.EnterAsync(ct))
        {
            await db.Entry(episode).ReloadAsync(ct);
            if (db.Entry(episode).State == EntityState.Detached) return;
            if (episode.Status == PodcastStatus.Completed && episode.FilePath is not null && File.Exists(episode.FilePath)) return;
            episode.Status = PodcastStatus.Downloading;
            episode.ErrorMessage = null;
            await db.SaveChangesAsync(ct);
        }

        var suffix = PodcastRules.SuffixFor(episode.ContentType, episode.AudioUrl);
        var dir = ChannelFolder(episode.ChannelId);
        var final = Path.Combine(dir, $"{episode.Id}-{PodcastRules.SafeFileName(episode.Title)}.{suffix}");
        var partial = final + ".part";
        var maxBytes = Math.Max(1, Settings.MaxEpisodeMegabytes) * 1024L * 1024;

        try
        {
            if (!PodcastHttp.IsAllowedUrl(episode.AudioUrl, out var uri))
                throw new PodcastException("The episode address is not valid.");

            Directory.CreateDirectory(dir);
            await PodcastDownload.DownloadAsync(http.Client, uri, partial, maxBytes, ct,
                (attempt, error) => logger.LogInformation("Retrying podcast episode '{Title}' from {Host}, attempt {Attempt}/{Maximum}: {Detail}",
                    episode.Title, uri.Host, attempt, PodcastDownload.MaxAttempts, PodcastDownload.Describe(error)));

            File.Move(partial, final, overwrite: true);

            episode.FilePath = final;
            episode.Size = new FileInfo(final).Length;
            episode.Suffix = suffix;
            episode.ContentType = PodcastRules.ContentTypeFor(suffix);
            episode.DurationSeconds = ReadDuration(final) is > 0 and var seconds ? seconds : episode.DurationSeconds;
            episode.Status = PodcastStatus.Completed;
            episode.DownloadedUtc = DateTime.UtcNow;
            logger.LogInformation("Downloaded podcast episode '{Title}' ({Size} bytes)", episode.Title, episode.Size);
        }
        catch (Exception ex) when (ex is PodcastException or HttpRequestException or InvalidDataException
                                       or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            TryDelete(partial);
            if (ex is OperationCanceledException && ct.IsCancellationRequested) throw;
            episode.Status = PodcastStatus.Error;
            episode.ErrorMessage = ex is PodcastException ? ex.Message : "Download failed: " + PodcastDownload.Describe(ex);
            logger.LogWarning("Podcast download failed for '{Title}': {Message}", episode.Title, ex.Message);
        }

        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static int ReadDuration(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            return (int)Math.Round(file.Properties.Duration.TotalSeconds);
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException or IOException)
        {
            return 0;
        }
    }

    // ---- removing --------------------------------------------------------------------------------

    public async Task DeleteEpisodeFileAsync(PodcastEpisode episode)
    {
        using var lease = await fileGate.EnterAsync();
        await db.Entry(episode).ReloadAsync();
        if (db.Entry(episode).State == EntityState.Detached) throw new PodcastException("This episode no longer exists.");
        if (episode.Status == PodcastStatus.Downloading) throw new PodcastException("Wait for this download to finish before removing its file.");
        if (episode.FilePath is not null)
        {
            if (!RetentionRules.IsManagedFile(Folder, episode.ChannelId, episode.FilePath)) throw new PodcastException("This is not a regular file in the podcast's managed storage folder.");
            try { if (File.Exists(episode.FilePath)) File.Delete(episode.FilePath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new PodcastException("Could not remove this download; its record has been preserved.", ex); }
        }
        episode.FilePath = null;
        episode.Status = PodcastStatus.New;
        episode.ErrorMessage = null;
        await db.SaveChangesAsync();
    }

    public async Task DeleteChannelAsync(PodcastChannel channel)
    {
        using var lease = await fileGate.EnterAsync();
        if (await db.PodcastEpisodes.AnyAsync(e => e.ChannelId == channel.Id && e.Status == PodcastStatus.Downloading))
            throw new PodcastException("Wait for this podcast's active download to finish before unsubscribing.");
        var folder = ChannelFolder(channel.Id);
        db.PodcastChannels.Remove(channel); // episodes and bookmarks cascade
        await db.SaveChangesAsync();
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Could not remove {Folder}: {Message}", folder, ex.Message);
        }
    }

    /// <summary>Episodes live in {Folder}/{channelId}/, so a channel's files are never mixed with another's.</summary>
    private string ChannelFolder(int channelId) => Path.Combine(Folder, channelId.ToString());

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // leave it; nothing else depends on it
        }
    }
}
