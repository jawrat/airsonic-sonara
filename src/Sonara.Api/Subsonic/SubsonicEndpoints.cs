using Sonara.Api.Podcasts;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sonara.Api.Media;
using Sonara.Api.Options;
using Sonara.Api.Scanning;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private const string IgnoredArticles = "The El La Los Las Le Les";

    public static void MapSubsonic(this WebApplication app)
    {
        var rest = app.MapGroup("/rest").AddEndpointFilter<SubsonicAuthFilter>();

        Map(rest, "ping", () => SubsonicResult.Ok());
        Map(rest, "getLicense", () => SubsonicResult.Ok(Obj(("license", Obj(("valid", true))))));
        Map(rest, "getOpenSubsonicExtensions",
            () => SubsonicResult.Ok(Obj(("openSubsonicExtensions", new JsonArray()))));

        Map(rest, "getMusicFolders", GetMusicFolders);
        Map(rest, "getIndexes", GetIndexes);
        Map(rest, "getArtists", GetArtists);
        Map(rest, "getArtist", GetArtist);
        Map(rest, "getAlbum", GetAlbum);
        Map(rest, "getSong", GetSong);
        Map(rest, "getAlbumList2", GetAlbumList2);
        Map(rest, "search3", Search3);
        Map(rest, "stream", Stream);
        Map(rest, "download", Download);
        Map(rest, "getCoverArt", GetCoverArt);
        Map(rest, "startScan", StartScan);
        Map(rest, "getScanStatus", GetScanStatus);
        MapPlaylists(rest);
        MapPlaylistFiles(rest);
        MapUsers(rest);
        MapStars(rest);
        MapBookmarks(rest);
        MapPodcasts(rest);
        MapBrowsing(rest);
        MapAnnotations(rest);
        MapCommunity(rest);
        MapSettings(rest);
        MapShares(rest);
        MapBackups(rest);
        MapUploads(rest);
        MapAvatars(rest);
        Map(rest, "exportPodcasts/opml", ExportPodcastOpml);
        rest.MapMethods("/{**unknown}", ["GET", "POST"], () =>
            SubsonicResult.Error(0, "Unknown or unsupported API operation."));
    }

    /// <summary>Clients call both /rest/ping and /rest/ping.view, via GET or POST.</summary>
    private static void Map(RouteGroupBuilder group, string name, Delegate handler)
    {
        group.MapMethods($"/{name}", ["GET", "POST"], handler);
        group.MapMethods($"/{name}.view", ["GET", "POST"], handler);
    }

    // ---- browsing -----------------------------------------------------------------------

    private static async Task<IResult> GetMusicFolders(SonaraDbContext db)
    {
        var folders = await db.MusicFolders.AsNoTracking()
            .Where(f => f.Enabled).OrderBy(f => f.Id).ToListAsync();

        return SubsonicResult.Ok(Obj(("musicFolders", Obj(
            ("musicFolder", Arr(folders.Select(f => Obj(("id", f.Id), ("name", f.Name), ("coverArt", DirectoryId(f.Id, ""))))))))));
    }

    private static async Task<IResult> GetIndexes(HttpContext http, SonaraDbContext db)
    {
        var folders = await db.MusicFolders.AsNoTracking().ToDictionaryAsync(f => f.Id);
        var tracks = await ScopedTracks(http, db).Include(t => t.Artist).Include(t => t.Album).ToListAsync();
        var modified = tracks.Count == 0 ? 0L : new DateTimeOffset(DateTime.SpecifyKind(tracks.Max(t => t.LastModifiedUtc), DateTimeKind.Utc)).ToUnixTimeMilliseconds();
        if (long.TryParse(http.Request.Query["ifModifiedSince"], out var since) && since >= modified)
            return SubsonicResult.Ok();
        var roots = new Dictionary<string, JsonObject>(); var loose = new List<JsonNode>();
        foreach (var track in tracks)
        {
            if (!folders.TryGetValue(track.MusicFolderId, out var folder)) continue;
            var relative = Path.GetRelativePath(folder.Path, track.Path).Replace('\\', '/');
            if (relative.StartsWith("../")) continue;
            var slash = relative.IndexOf('/');
            if (slash < 0) { var song = SongJson(track); song["parent"] = DirectoryId(folder.Id, ""); loose.Add(song); }
            else
            {
                var name = relative[..slash]; var id = DirectoryId(folder.Id, name);
                roots.TryAdd(id, Obj(("id", id), ("name", name), ("coverArt", id)));
            }
        }
        var indexes = roots.Values.GroupBy(r => IndexLetter(r["name"]!.GetValue<string>())).OrderBy(g => g.Key)
            .Select(g => Obj(("name", g.Key), ("artist", Arr(g.OrderBy(r => r["name"]!.GetValue<string>())))));
        return SubsonicResult.Ok(Obj(("indexes", Obj(("lastModified", modified), ("ignoredArticles", IgnoredArticles),
            ("index", Arr(indexes)), ("child", Arr(loose))))));
    }

    private static async Task<IResult> GetArtists(HttpContext http, SonaraDbContext db)
    {
        var scoped = ScopedTracks(http, db);
        var query = db.Artists.AsNoTracking().Where(a => scoped.Any(t => t.Album.ArtistId == a.Id));
        var term = http.Request.Query["query"].ToString().Trim().ToLowerInvariant();
        if (term.Length > 0) query = query.Where(a => a.Name.ToLower().Contains(term));
        query = query.OrderBy(a => a.Name).ThenBy(a => a.Id);
        if (http.Request.Query.ContainsKey("size") || http.Request.Query.ContainsKey("offset"))
            query = query.Skip(Offset(http)).Take(Math.Clamp(Number(http, "size") ?? 100, 1, 500));
        var artists = await query.ToListAsync();
        await ScopeArtistCounts(artists, db, scoped);
        return SubsonicResult.Ok(Obj(("artists", Obj(
            ("ignoredArticles", IgnoredArticles),
            ("index", IndexGroups(artists))))));
    }

    private static async Task<IResult> GetArtist(string? id, HttpContext http, SonaraDbContext db)
    {
        if (!int.TryParse(id, out var artistId)) return MissingParam();

        var artist = await db.Artists.AsNoTracking().FirstOrDefaultAsync(a => a.Id == artistId);
        if (artist is null) return NotFound();

        var albums = await db.Albums.AsNoTracking().Include(a => a.Artist)
            .Where(a => a.ArtistId == artistId)
            .OrderBy(a => a.Year).ThenBy(a => a.Title).ToListAsync();

        var stars = await Stars(http, db);
        await ScopeArtistCounts([artist], db);
        var json = stars.Artist(ArtistJson(artist));
        var albumJson = albums.Select(a => stars.Album(AlbumJson(a))).ToList();
        await AnnotateAlbums(albumJson, albums.Select(a => a.Id).ToList(), SubsonicAuthFilter.CurrentUser(http)!.Id, db);
        json["album"] = Arr(albumJson);
        return SubsonicResult.Ok(Obj(("artist", json)));
    }

    private static async Task<IResult> GetAlbum(string? id, HttpContext http, SonaraDbContext db)
    {
        if (!int.TryParse(id, out var albumId)) return MissingParam();

        var album = await db.Albums.AsNoTracking().Include(a => a.Artist)
            .FirstOrDefaultAsync(a => a.Id == albumId);
        if (album is null) return NotFound();

        var songs = await db.Tracks.AsNoTracking()
            .Include(t => t.Artist).Include(t => t.Album)
            .Where(t => t.AlbumId == albumId)
            .OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ThenBy(t => t.Title)
            .ToListAsync();

        var stars = await Stars(http, db);
        var json = stars.Album(AlbumJson(album));
        var entries = songs.Select(t => stars.Song(SongJson(t))).ToList();
        await AnnotateTracks(entries, songs.Select(t => t.Id).ToList(), SubsonicAuthFilter.CurrentUser(http)!.Id, db);
        await AnnotateAlbums([json], [album.Id], SubsonicAuthFilter.CurrentUser(http)!.Id, db);
        json["song"] = Arr(entries);
        return SubsonicResult.Ok(Obj(("album", json)));
    }

    private static async Task<IResult> GetSong(string? id, HttpContext http, SonaraDbContext db)
    {
        if (MediaRef.Parse(id) is { Kind: MediaKind.Episode } episodeRef)
        {
            var episode = await db.PodcastEpisodes.AsNoTracking().Include(e => e.Channel).FirstOrDefaultAsync(e => e.Id == episodeRef.Id);
            return episode is null ? NotFound() : SubsonicResult.Ok(Obj(("song", EpisodeEntryJson(episode, episode.Channel.Title))));
        }
        if (!int.TryParse(id, out var trackId)) return MissingParam();

        var track = await db.Tracks.AsNoTracking()
            .Include(t => t.Artist).Include(t => t.Album)
            .FirstOrDefaultAsync(t => t.Id == trackId);
        if (track is null) return NotFound();
        var json = (await Stars(http, db)).Song(SongJson(track));
        await AnnotateTracks([json], [track.Id], SubsonicAuthFilter.CurrentUser(http)!.Id, db);
        return SubsonicResult.Ok(Obj(("song", json)));
    }

    private static Task<IResult> GetAlbumList2(string? type, int? size, int? offset, HttpContext http, SonaraDbContext db) =>
        AlbumList(type, size, offset, http, db, legacy: false);

    private static Task<IResult> Search3(string? query, int? artistCount, int? albumCount, int? songCount,
        HttpContext http, SonaraDbContext db) => SearchTags(query, artistCount, albumCount, songCount, http, db, legacy: false);

    // ---- media --------------------------------------------------------------------------

    /// <summary>
    /// Serves original audio with range support or converts using client options/user preferences.
    /// Non-administrator account bitrate limits take precedence over raw/original requests.
    /// </summary>
    private static async Task<IResult> Stream(
        string? id, string? format, int? maxBitRate, int? timeOffset,
        HttpContext http, SonaraDbContext db, FfmpegTranscoder transcoder, IOptions<SonaraOptions> options)
    {
        var user = SubsonicAuthFilter.CurrentUser(http)!;
        (format, maxBitRate) = TranscodePlanner.Preferences(user, format, maxBitRate);
        if (user.MaxBitRate > 0 && !user.IsAdmin)
        {
            if (!options.Value.Transcoding.Enabled) return SubsonicResult.Error(50, "A bitrate limit requires transcoding.");
            maxBitRate = maxBitRate is > 0 ? Math.Min(maxBitRate.Value, user.MaxBitRate) : user.MaxBitRate;
            if (string.Equals(format, "raw", StringComparison.OrdinalIgnoreCase) || format is not ("mp3" or "opus" or "aac")) format = null;
        }
        var media = MediaRef.Parse(id);
        if (media is null) return MissingParam();
        Track? track;
        if (media.Value.Kind == MediaKind.Episode)
        {
            var episode = await db.PodcastEpisodes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == media.Value.Id);
            if (episode?.FilePath is null || episode.Status != PodcastStatus.Completed) return NotFound();
            track = new Track { Path = episode.FilePath, Suffix = episode.Suffix ?? "mp3", ContentType = episode.ContentType ?? "audio/mpeg" };
        }
        else if (media.Value.Kind == MediaKind.Track)
            track = await db.Tracks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == media.Value.Id);
        else return NotFound();
        if (track is null || !File.Exists(track.Path)) return NotFound();
        if (!user.IsAdmin && user.MaxBitRate is > 0 and < 32) return SubsonicResult.Error(50, "Minimum supported bitrate is 32 kbps.");

        var plan = TranscodePlanner.Plan(track.Suffix, track.BitRate, format, maxBitRate, options.Value.Transcoding);
        if (plan is null && !user.IsAdmin && user.MaxBitRate > 0 && (track.BitRate <= 0 || track.BitRate > user.MaxBitRate))
            return SubsonicResult.Error(50, "The configured transcode format cannot enforce this user's bitrate limit.");
        if (plan is null)
            return Results.File(track.Path, contentType: track.ContentType, enableRangeProcessing: true);

        var handled = await transcoder.StreamAsync(http, track.Path, plan, Math.Max(timeOffset ?? 0, 0));
        return handled
            ? Results.Empty
            : SubsonicResult.Error(0, "Transcoding failed. Retry with format=raw to get the original file.");
    }

    private static Task<IResult> Download(string? id, SonaraDbContext db) => SendFile(id, db, download: true);

    /// <summary>
    /// Cover ids are "al-{albumId}". Art is resolved on demand (folder image, then embedded tag),
    /// so no schema change is needed. With "size" the image is scaled down (never up) with ffmpeg and cached on disk; the original is
    /// returned only when no size is given or resizing fails.
    /// </summary>
    private static string ContentTypeOfImage(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        _ => "image/jpeg",
    };

    private static async Task<IResult> GetCoverArt(
        string? id, int? size, HttpContext http, SonaraDbContext db, CoverArtResizer resizer)
    {
        if (string.IsNullOrEmpty(id)) return MissingParam();

        CoverImage? image;
        if (id.StartsWith("pc-", StringComparison.Ordinal))
        {
            // Podcast channel image, downloaded when the podcast was added.
            if (MediaRef.Parse(id) is not { Kind: MediaKind.Channel } channelRef) return Results.NotFound();
            var imagePath = await db.PodcastChannels.AsNoTracking()
                .Where(c => c.Id == channelRef.Id).Select(c => c.ImagePath).FirstOrDefaultAsync();
            image = imagePath is not null && File.Exists(imagePath)
                ? new CoverImage(await File.ReadAllBytesAsync(imagePath), ContentTypeOfImage(imagePath))
                : null;
        }
        else if (id.StartsWith("ar-", StringComparison.Ordinal) || id.StartsWith("dir-", StringComparison.Ordinal))
        {
            var tracks = ScopedTracks(http, db);
            if (id.StartsWith("ar-", StringComparison.Ordinal))
            {
                if (!int.TryParse(id[3..], out var artistId)) return Results.NotFound();
                tracks = tracks.Where(t => t.Album.ArtistId == artistId);
            }
            else
            {
                tracks = tracks.Where(t => DirectoryTracks(id, db).Any(d => d.Id == t.Id));
            }
            // One representative track per album, rather than rereading every audio file.
            // Folder covers are checked first across all candidates; embedded-tag reads are bounded.
            var paths = await tracks.GroupBy(t => t.AlbumId)
                .Select(g => g.OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ThenBy(t => t.Id).First().Path)
                .ToListAsync(http.RequestAborted);
            paths.Sort(StringComparer.Ordinal);
            image = await Task.Run(() => CoverArtFinder.FindRepresentative(paths), http.RequestAborted);
        }
        else
        {
            var raw = id.StartsWith("al-", StringComparison.Ordinal) ? id[3..] : id;
            if (!int.TryParse(raw, out var albumId)) return Results.NotFound();

            var paths = await db.Tracks.AsNoTracking()
                .Where(t => t.AlbumId == albumId)
                .OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber)
                .Select(t => t.Path)
                .Take(5)
                .ToListAsync();

            // File and tag reads are blocking; keep them off the request thread.
            image = await Task.Run(() => CoverArtFinder.Find(paths));
        }
        if (image is null) return Results.NotFound();

        http.Response.Headers.CacheControl = "private, max-age=86400";

        if (CoverArtPlanner.ClampSize(size) is { } px)
        {
            var resized = await resizer.GetAsync(image.Data, px, http.RequestAborted, id);
            if (resized is not null) return Results.File(resized, "image/jpeg");
            // Resizing failed (ffmpeg missing?): the original is still better than nothing.
        }
        return Results.File(image.Data, image.ContentType);
    }

    private static async Task<IResult> SendEpisode(int episodeId, SonaraDbContext db, bool download)
    {
        var episode = await db.PodcastEpisodes.AsNoTracking().FirstOrDefaultAsync(e => e.Id == episodeId);
        if (episode is null || episode.Status != PodcastStatus.Completed || episode.FilePath is null
            || !File.Exists(episode.FilePath))
            return NotFound();

        return Results.File(
            episode.FilePath,
            contentType: episode.ContentType ?? "application/octet-stream",
            fileDownloadName: download ? Path.GetFileName(episode.FilePath) : null,
            enableRangeProcessing: true);
    }

    private static async Task<IResult> SendFile(string? id, SonaraDbContext db, bool download)
    {
        var media = MediaRef.Parse(id);
        if (media is null) return MissingParam();
        if (media.Value.Kind == MediaKind.Episode) return await SendEpisode(media.Value.Id, db, download);
        if (media.Value.Kind != MediaKind.Track) return NotFound();
        var trackId = media.Value.Id;

        var track = await db.Tracks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == trackId);
        if (track is null || !File.Exists(track.Path)) return NotFound();

        return Results.File(
            track.Path,
            contentType: track.ContentType,
            fileDownloadName: download ? Path.GetFileName(track.Path) : null,
            enableRangeProcessing: true);
    }

    // ---- scanning -----------------------------------------------------------------------

    private static IResult StartScan(bool? fullScan, HttpContext http, IMediaScanner scanner)
    {
        if (SubsonicAuthFilter.CurrentUser(http)?.IsAdmin != true)
            return SubsonicResult.Error(50, "User is not authorized for the given operation.");

        scanner.TryStart(fullScan: fullScan ?? false);
        return ScanStatusResult(scanner);
    }

    private static IResult GetScanStatus(IMediaScanner scanner) => ScanStatusResult(scanner);

    private static IResult ScanStatusResult(IMediaScanner scanner)
    {
        var status = scanner.Status;
        return SubsonicResult.Ok(Obj(("scanStatus", Obj(("scanning", status.Scanning), ("count", status.Count)))));
    }

    // ---- JSON shaping -------------------------------------------------------------------

    private static JsonArray IndexGroups(List<Artist> artists) =>
        Arr(artists
            .GroupBy(a => IndexLetter(a.Name))
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Obj(("name", g.Key), ("artist", Arr(g.Select(ArtistJson))))));

    private static string IndexLetter(string name)
    {
        var first = name.TrimStart().FirstOrDefault();
        return char.IsLetter(first) ? char.ToUpperInvariant(first).ToString() : "#";
    }

    private static JsonObject ArtistJson(Artist a) =>
        Obj(("id", a.Id.ToString()), ("name", a.Name), ("albumCount", a.AlbumCount), ("coverArt", $"ar-{a.Id}"));

    private static JsonObject AlbumJson(Album a) => Obj(
        ("id", a.Id.ToString()),
        ("name", a.Title),
        ("title", a.Title),
        ("coverArt", $"al-{a.Id}"),
        ("artist", a.Artist?.Name),
        ("artistId", a.ArtistId.ToString()),
        ("songCount", a.SongCount),
        ("duration", a.DurationSeconds),
        ("year", a.Year),
        ("genre", a.Genre),
        ("created", a.CreatedUtc));

    private static JsonObject SongJson(Track t) => Obj(
        ("id", t.Id.ToString()),
        ("parent", $"al-{t.AlbumId}"),
        ("isDir", false),
        ("isVideo", false),
        ("type", "music"),
        ("title", t.Title),
        ("album", t.Album?.Title),
        ("albumId", t.AlbumId.ToString()),
        ("coverArt", $"al-{t.AlbumId}"),
        ("artist", t.Artist?.Name),
        ("artistId", t.ArtistId.ToString()),
        ("track", t.TrackNumber),
        ("discNumber", t.DiscNumber),
        ("year", t.Year),
        ("genre", t.Genre),
        ("size", t.Size),
        ("suffix", t.Suffix),
        ("contentType", t.ContentType),
        ("duration", t.DurationSeconds),
        ("bitRate", t.BitRate),
        // Deliberately not the absolute server path.
        ("path", $"{t.Artist?.Name}/{t.Album?.Title}/{Path.GetFileName(t.Path)}"));

    private static IResult MissingParam() => SubsonicResult.Error(10, "Required parameter is missing.");

    private static IResult NotFound() => SubsonicResult.Error(70, "The requested data was not found.");
}
