using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;
using Sonara.Core.Entities;
using Sonara.Data;

namespace Sonara.Api.Scanning;

public sealed record ScanStatus(bool Scanning, int Count);

public interface IMediaScanner
{
    ScanStatus Status { get; }

    /// <summary>Starts a background scan. Returns false if one is already running.</summary>
    bool TryStart(bool fullScan);
    void RequestScan();
}

/// <summary>
/// Walks each music folder, reads tags of new/changed files in parallel (TagLibSharp) and applies the
/// results to the database serially. Triggering is handled by <see cref="ScannerService"/>.
/// </summary>
public sealed class MediaScanner(
    IServiceScopeFactory scopeFactory,
    IOptions<SonaraOptions> options,
    ILogger<MediaScanner> logger) : IMediaScanner
{
    private const int BatchSize = 200;

    private readonly object _scanGate = new();
    private int _pending;
    public void RequestScan()
    {
        lock (_scanGate)
        {
            _pending = 1;
            TryStart(false);
        }
    }

    private int _scanning;
    private int _count;

    public ScanStatus Status => new(Volatile.Read(ref _scanning) == 1, Volatile.Read(ref _count));

    public bool TryStart(bool fullScan)
    {
        lock (_scanGate)
        {
            if (_scanning == 1) return false;
            Volatile.Write(ref _scanning, 1);
            _pending = 0;
            Volatile.Write(ref _count, 0);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await ScanAsync(fullScan);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Media scan failed");
            }
            finally
            {
                lock (_scanGate)
                {
                    Volatile.Write(ref _scanning, 0);
                    if (_pending == 1) TryStart(false);
                }
            }
        });
        return true;
    }

    private async Task ScanAsync(bool fullScan)
    {
        logger.LogInformation("Media scan started (full: {Full})", fullScan);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();

        var parallelism = ScanRules.EffectiveParallelism(options.Value.Scanner.Parallelism);
        var folders = await db.MusicFolders.Where(f => f.Enabled).ToListAsync();

        var artists = (await db.Artists.ToListAsync())
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var albums = (await db.Albums.Include(a => a.Artist).ToListAsync())
            .GroupBy(a => AlbumKey(a.Artist.Name, a.Title), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Artist GetArtist(string name)
        {
            if (!artists.TryGetValue(name, out var artist))
            {
                artist = new Artist { Name = name };
                db.Artists.Add(artist);
                artists[name] = artist;
            }
            return artist;
        }

        Album GetAlbum(Artist artist, string title, int? year, string? genre)
        {
            var key = AlbumKey(artist.Name, title);
            if (!albums.TryGetValue(key, out var album))
            {
                album = new Album { Title = title, Artist = artist, Year = year, Genre = genre };
                db.Albums.Add(album);
                albums[key] = album;
            }
            return album;
        }

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder.Path))
            {
                logger.LogWarning("Music folder {Path} does not exist; skipping", folder.Path);
                continue;
            }

            var existing = await db.Tracks.Where(t => t.MusicFolderId == folder.Id)
                .ToDictionaryAsync(t => t.Path);
            var seen = new HashSet<string>();
            var candidates = new List<Candidate>();

            var enumeration = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
            foreach (var file in Directory.EnumerateFiles(folder.Path, "*", enumeration))
            {
                if (!ScanRules.ContentTypes.TryGetValue(Path.GetExtension(file), out var contentType)) continue;

                var fullPath = Path.GetFullPath(file);
                seen.Add(fullPath);
                Interlocked.Increment(ref _count);

                var info = new FileInfo(fullPath);
                var known = existing.TryGetValue(fullPath, out var track);

                if (ScanRules.NeedsRead(fullScan, known, track?.Size ?? 0, track?.LastModifiedUtc ?? default,
                        info.Length, info.LastWriteTimeUtc))
                {
                    candidates.Add(new Candidate(fullPath, contentType, info.Length, info.LastWriteTimeUtc, track));
                }
            }

            // Tag reading is the slow part (disk + parsing), so do it in parallel; the database
            // work stays on this thread because a DbContext is not thread-safe.
            foreach (var chunk in candidates.Chunk(BatchSize))
            {
                var tags = new TagInfo?[chunk.Length];
                Parallel.For(0, chunk.Length,
                    new ParallelOptions { MaxDegreeOfParallelism = parallelism },
                    i => tags[i] = ReadTags(chunk[i].Path, logger));

                for (var i = 0; i < chunk.Length; i++)
                {
                    var c = chunk[i];
                    var t = tags[i];
                    if (t is null) continue;

                    var albumArtist = GetArtist(t.AlbumArtist);
                    var trackArtist = string.Equals(t.Artist, t.AlbumArtist, StringComparison.OrdinalIgnoreCase)
                        ? albumArtist
                        : GetArtist(t.Artist);
                    var album = GetAlbum(albumArtist, t.Album, t.Year, t.Genre);

                    var track = c.Existing;
                    if (track is null)
                    {
                        track = new Track { Path = c.Path, MusicFolderId = folder.Id };
                        db.Tracks.Add(track);
                    }

                    track.Title = t.Title;
                    track.Album = album;
                    track.Artist = trackArtist;
                    track.TrackNumber = t.TrackNumber;
                    track.DiscNumber = t.DiscNumber;
                    track.Year = t.Year;
                    track.Genre = t.Genre;
                    track.DurationSeconds = t.DurationSeconds;
                    track.BitRate = t.BitRate;
                    track.Size = c.Size;
                    track.Suffix = Path.GetExtension(c.Path).TrimStart('.').ToLowerInvariant();
                    track.ContentType = c.ContentType;
                    track.LastModifiedUtc = c.ModifiedUtc;
                }
                await db.SaveChangesAsync();
            }

            // Safeguard: a folder that exists but yields zero audio files while we already know
            // tracks in it is far more likely an unmounted/empty volume than a deliberate wipe.
            // Keep the library intact rather than deleting everything.
            if (seen.Count == 0 && existing.Count > 0)
            {
                logger.LogWarning(
                    "Music folder {Path} contains no audio files but {Known} tracks are known; skipping removal. " +
                    "Is the volume mounted?", folder.Path, existing.Count);
                continue;
            }

            foreach (var (path, track) in existing)
            {
                if (!seen.Contains(path)) db.Tracks.Remove(track);
            }
            await db.SaveChangesAsync();
            logger.LogInformation("Scanned {Path}: {Seen} files, {Read} read", folder.Path, seen.Count, candidates.Count);
        }

        await RefreshCountsAsync(db);
        logger.LogInformation("Media scan finished: {Count} files seen", Volatile.Read(ref _count));
    }

    /// <summary>Refreshes the denormalised album/artist counters and drops albums that lost all tracks.</summary>
    public static async Task RefreshCountsAsync(SonaraDbContext db)
    {
        var stats = (await db.Tracks
                .GroupBy(t => t.AlbumId)
                .Select(g => new { AlbumId = g.Key, Count = g.Count(), Duration = g.Sum(t => t.DurationSeconds) })
                .ToListAsync())
            .ToDictionary(s => s.AlbumId);

        foreach (var album in await db.Albums.ToListAsync())
        {
            if (stats.TryGetValue(album.Id, out var s))
            {
                album.SongCount = s.Count;
                album.DurationSeconds = s.Duration;
            }
            else
            {
                db.Albums.Remove(album);
            }
        }
        await db.SaveChangesAsync();

        var albumCounts = (await db.Albums
                .GroupBy(a => a.ArtistId)
                .Select(g => new { ArtistId = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.ArtistId, x => x.Count);

        var trackArtists = await db.Tracks.Select(t => t.ArtistId).Distinct().ToListAsync();
        var referenced = trackArtists.ToHashSet();
        foreach (var artist in await db.Artists.ToListAsync())
        {
            artist.AlbumCount = albumCounts.GetValueOrDefault(artist.Id);
            if (artist.AlbumCount == 0 && !referenced.Contains(artist.Id)) db.Artists.Remove(artist);
        }

        await db.SaveChangesAsync();
    }

    private static string AlbumKey(string artist, string title) => $"{artist}\u0001{title}";

    private sealed record Candidate(
        string Path, string ContentType, long Size, DateTime ModifiedUtc, Track? Existing);

    private sealed record TagInfo(
        string Title, string Album, string AlbumArtist, string Artist,
        int? TrackNumber, int? DiscNumber, int? Year, string? Genre,
        int DurationSeconds, int BitRate);

    private static TagInfo? ReadTags(string path, ILogger logger)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;

            var artist = FirstNonEmpty(tag.FirstPerformer, tag.FirstAlbumArtist) ?? "[Unknown Artist]";
            var albumArtist = FirstNonEmpty(tag.FirstAlbumArtist, tag.FirstPerformer) ?? "[Unknown Artist]";

            return new TagInfo(
                Title: FirstNonEmpty(tag.Title) ?? Path.GetFileNameWithoutExtension(path),
                Album: FirstNonEmpty(tag.Album) ?? "[Unknown Album]",
                AlbumArtist: albumArtist,
                Artist: artist,
                TrackNumber: tag.Track > 0 ? (int)tag.Track : null,
                DiscNumber: tag.Disc > 0 ? (int)tag.Disc : null,
                Year: tag.Year > 0 ? (int)tag.Year : null,
                Genre: FirstNonEmpty(tag.FirstGenre),
                DurationSeconds: (int)Math.Round(file.Properties.Duration.TotalSeconds),
                BitRate: file.Properties.AudioBitrate);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read tags from {Path}", path);
            return null;
        }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
