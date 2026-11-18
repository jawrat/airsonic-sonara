using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapBrowsing(RouteGroupBuilder rest)
    {
        Map(rest, "getMusicDirectory", GetMusicDirectory);
        Map(rest, "getGenres", GetGenres);
        Map(rest, "getAlbumList", (string? type, int? size, int? offset, HttpContext h, SonaraDbContext db) => AlbumList(type, size, offset, h, db, true));
        Map(rest, "search2", (string? query, int? artistCount, int? albumCount, int? songCount, HttpContext h, SonaraDbContext db) => SearchTags(query, artistCount, albumCount, songCount, h, db, true));
        Map(rest, "search", SearchLegacy);
        Map(rest, "getRandomSongs", GetRandomSongs);
        Map(rest, "getSongsByGenre", GetSongsByGenre);
        Map(rest, "getTopSongs", GetTopSongs);
        Map(rest, "getSimilarSongs", (string? id, int? count, HttpContext h, SonaraDbContext db) => SimilarSongs(id, count, h, db, true));
        Map(rest, "getSimilarSongs2", (string? id, int? count, HttpContext h, SonaraDbContext db) => SimilarSongs(id, count, h, db, false));
        Map(rest, "getArtistInfo", (string? id, HttpContext h, SonaraDbContext db) => ArtistInfo(id, h, db, true));
        Map(rest, "getArtistInfo2", (string? id, HttpContext h, SonaraDbContext db) => ArtistInfo(id, h, db, false));
        Map(rest, "getAlbumInfo", (string? id, SonaraDbContext db) => AlbumInfo(id, db, true));
        Map(rest, "getAlbumInfo2", (string? id, SonaraDbContext db) => AlbumInfo(id, db, false));
        Map(rest, "getStarred", GetStarred);
        Map(rest, "getVideos", () => SubsonicResult.Ok(Obj(("videos", Obj(("video", new JsonArray()))))));
        Map(rest, "hls", Hls);
        foreach (var name in new[] { "getVideoInfo", "getCaptions", "jukeboxControl" })
        {
            var operation = name;
            Map(rest, name, () => SubsonicResult.Error(0, $"{operation} is not supported by this music-only server."));
        }
    }

    private static int? Number(HttpContext h, string key) => int.TryParse(h.Request.Query[key], out var n) ? n : null;
    private static int Count(int? value, int fallback = 20) => Math.Clamp(value ?? fallback, 0, 500);
    private static int Offset(HttpContext h, string key = "offset") => Math.Max(0, Number(h, key) ?? 0);
    private static IQueryable<Track> ScopedTracks(HttpContext h, SonaraDbContext db)
    {
        var q = db.Tracks.AsNoTracking();
        return Number(h, "musicFolderId") is { } folder ? q.Where(t => t.MusicFolderId == folder) : q;
    }
    private static IQueryable<Album> ScopedAlbums(HttpContext h, SonaraDbContext db)
    {
        var tracks = ScopedTracks(h, db);
        return db.Albums.AsNoTracking().Include(a => a.Artist).Where(a => tracks.Any(t => t.AlbumId == a.Id));
    }
    private static IQueryable<Track> WithTags(IQueryable<Track> q) => q.Include(t => t.Artist).Include(t => t.Album);
    private static JsonObject LegacyAlbum(Album a)
    {
        var json = AlbumJson(a);
        json["id"] = $"al-{a.Id}";
        json["parent"] = $"ar-{a.ArtistId}";
        json["isDir"] = true;
        return json;
    }
    private static JsonObject LegacyArtist(Artist a) => Obj(("id", $"ar-{a.Id}"), ("name", a.Name), ("title", a.Name), ("isDir", true));
    private static int? TaggedId(string? value, string prefix) =>
        int.TryParse(value?.StartsWith(prefix, StringComparison.Ordinal) == true ? value[prefix.Length..] : value, out var id) && id > 0 ? id : null;

    private static async Task<IResult> AlbumList(string? type, int? size, int? offset, HttpContext h, SonaraDbContext db, bool legacy)
    {
        if (string.IsNullOrEmpty(type)) return MissingParam();
        var q = ScopedAlbums(h, db);
        if (h.Request.Query.ContainsKey("artistId"))
        {
            if (!int.TryParse(h.Request.Query["artistId"], out var artistId) || artistId <= 0) return SubsonicResult.Error(0, "Invalid artist ID.");
            q = q.Where(a => a.ArtistId == artistId);
        }
        var user = SubsonicAuthFilter.CurrentUser(h)!;
        var history = db.PlayEvents.Where(p => p.UserId == user.Id);
        var stars = await Stars(h, db);
        switch (type)
        {
            case "newest": q = q.OrderByDescending(a => a.CreatedUtc).ThenBy(a => a.Id); break;
            case "random": q = q.OrderBy(a => EF.Functions.Random()); break;
            case "alphabeticalByName": q = q.OrderBy(a => a.Title).ThenBy(a => a.Id); break;
            case "alphabeticalByArtist": q = q.OrderBy(a => a.Artist.Name).ThenBy(a => a.Title).ThenBy(a => a.Id); break;
            case "starred":
                var ids = stars.Albums.Keys.ToList();
                q = q.Where(a => ids.Contains(a.Id)).OrderByDescending(a => db.Stars.Where(s => s.UserId == user.Id && s.AlbumId == a.Id).Select(s => s.CreatedUtc).Max());
                break;
            case "highest": q = q.Where(a => db.MediaRatings.Any(r => r.MediaId == "al-" + a.Id))
                .OrderByDescending(a => db.MediaRatings.Where(r => r.MediaId == "al-" + a.Id).Average(r => r.Rating)).ThenBy(a => a.Id); break;
            case "frequent": q = q.Where(a => history.Any(p => db.Tracks.Any(t => t.Id == p.TrackId && t.AlbumId == a.Id)))
                .OrderByDescending(a => history.Count(p => db.Tracks.Any(t => t.Id == p.TrackId && t.AlbumId == a.Id))).ThenBy(a => a.Id); break;
            case "recent": q = q.Where(a => history.Any(p => db.Tracks.Any(t => t.Id == p.TrackId && t.AlbumId == a.Id)))
                .OrderByDescending(a => history.Where(p => db.Tracks.Any(t => t.Id == p.TrackId && t.AlbumId == a.Id)).Max(p => p.PlayedUtc)).ThenBy(a => a.Id); break;
            case "byGenre":
                var genre = h.Request.Query["genre"].ToString();
                if (string.IsNullOrEmpty(genre)) return MissingParam();
                q = q.Where(a => a.Genre != null && a.Genre.ToLower() == genre.ToLower()).OrderBy(a => a.Title).ThenBy(a => a.Id); break;
            case "byYear":
                var from = Number(h, "fromYear"); var to = Number(h, "toYear");
                if (from is null || to is null) return MissingParam();
                var min = Math.Min(from.Value, to.Value); var max = Math.Max(from.Value, to.Value);
                q = q.Where(a => a.Year >= min && a.Year <= max);
                q = from > to ? q.OrderByDescending(a => a.Year).ThenBy(a => a.Id) : q.OrderBy(a => a.Year).ThenBy(a => a.Id);
                break;
            default: return SubsonicResult.Error(0, "Invalid album list type.");
        }
        var albums = await q.Skip(Math.Max(offset ?? 0, 0)).Take(Count(size, 10)).ToListAsync();
        var items = albums.Select(a => stars.Album(AlbumJson(a))).ToList();
        if (legacy) for (var i = 0; i < items.Count; i++) { items[i]["id"] = $"al-{albums[i].Id}"; items[i]["parent"] = $"ar-{albums[i].ArtistId}"; items[i]["isDir"] = true; }
        await AnnotateAlbums(items, albums.Select(a => a.Id).ToList(), user.Id, db, ScopedTracks(h, db));
        return SubsonicResult.Ok(Obj((legacy ? "albumList" : "albumList2", Obj(("album", Arr(items))))));
    }

    private static async Task<IResult> SearchTags(string? query, int? artistCount, int? albumCount, int? songCount, HttpContext h, SonaraDbContext db, bool legacy)
    {
        if (query is null) return MissingParam();
        var term = query.Trim().Trim('"').ToLowerInvariant();
        var tracks = ScopedTracks(h, db);
        var artists = await db.Artists.AsNoTracking().Where(a => a.Name.ToLower().Contains(term) && tracks.Any(t => t.ArtistId == a.Id || t.Album.ArtistId == a.Id))
            .OrderBy(a => a.Name).ThenBy(a => a.Id).Skip(Offset(h, "artistOffset")).Take(Count(artistCount)).ToListAsync();
        var albums = await ScopedAlbums(h, db).Where(a => a.Title.ToLower().Contains(term)).OrderBy(a => a.Title).ThenBy(a => a.Id)
            .Skip(Offset(h, "albumOffset")).Take(Count(albumCount)).ToListAsync();
        var songs = await WithTags(tracks).Where(t => t.Title.ToLower().Contains(term)).OrderBy(t => t.Title).ThenBy(t => t.Id)
            .Skip(Offset(h, "songOffset")).Take(Count(songCount)).ToListAsync();
        var stars = await Stars(h, db);
        await ScopeArtistCounts(artists, db, tracks);
        var albumJson = albums.Select(a => stars.Album(AlbumJson(a))).ToList();
        await AnnotateAlbums(albumJson, albums.Select(a => a.Id).ToList(), SubsonicAuthFilter.CurrentUser(h)!.Id, db, tracks);
        if (legacy) for (var i = 0; i < albumJson.Count; i++) { albumJson[i]["id"] = $"al-{albums[i].Id}"; albumJson[i]["isDir"] = true; albumJson[i]["parent"] = $"ar-{albums[i].ArtistId}"; }
        return SubsonicResult.Ok(Obj((legacy ? "searchResult2" : "searchResult3", Obj(
            ("artist", Arr(artists.Select(a => legacy ? LegacyArtist(a) : stars.Artist(ArtistJson(a))))),
            ("album", Arr(albumJson)),
            ("song", Arr(songs.Select(t => stars.Song(SongJson(t)))))))));
    }

    private static async Task<IResult> SearchLegacy(string? artist, string? album, string? title, string? any, int? count, int? offset, HttpContext h, SonaraDbContext db)
    {
        var q = WithTags(ScopedTracks(h, db));
        if (!string.IsNullOrWhiteSpace(artist)) q = q.Where(t => t.Artist.Name.ToLower().Contains(artist.ToLower()));
        if (!string.IsNullOrWhiteSpace(album)) q = q.Where(t => t.Album.Title.ToLower().Contains(album.ToLower()));
        if (!string.IsNullOrWhiteSpace(title)) q = q.Where(t => t.Title.ToLower().Contains(title.ToLower()));
        if (!string.IsNullOrWhiteSpace(any)) q = q.Where(t => t.Title.ToLower().Contains(any.ToLower()) || t.Artist.Name.ToLower().Contains(any.ToLower()) || t.Album.Title.ToLower().Contains(any.ToLower()));
        var total = await q.CountAsync();
        var songs = await q.OrderBy(t => t.Title).ThenBy(t => t.Id).Skip(Math.Max(0, offset ?? 0)).Take(Count(count)).ToListAsync();
        return SubsonicResult.Ok(Obj(("searchResult", Obj(("offset", Math.Max(0, offset ?? 0)), ("totalHits", total), ("match", Arr(songs.Select(SongJson)))))));
    }

    private static async Task<IResult> GetGenres(HttpContext h, SonaraDbContext db)
    {
        var genres = await ScopedTracks(h, db).Where(t => t.Genre != null && t.Genre != "")
            .GroupBy(t => t.Genre!).Select(g => new { Name = g.Key, Songs = g.Count(), Albums = g.Select(t => t.AlbumId).Distinct().Count() }).OrderBy(g => g.Name).ToListAsync();
        return SubsonicResult.Ok(Obj(("genres", Obj(("genre", Arr(genres.Select(g => Obj(("value", g.Name), ("songCount", g.Songs), ("albumCount", g.Albums)))))))));
    }
    private static async Task<IResult> GetRandomSongs(int? size, string? genre, int? fromYear, int? toYear, HttpContext h, SonaraDbContext db)
    {
        var q = ScopedTracks(h, db);
        if (genre is not null) q = q.Where(t => t.Genre != null && t.Genre.ToLower() == genre.ToLower());
        if (fromYear is not null) q = q.Where(t => t.Year >= fromYear);
        if (toYear is not null) q = q.Where(t => t.Year <= toYear);
        return await SongsResult("randomSongs", WithTags(q).OrderBy(t => EF.Functions.Random()).Take(Count(size, 10)), h, db);
    }
    private static Task<IResult> GetSongsByGenre(string? genre, int? count, int? offset, HttpContext h, SonaraDbContext db) =>
        genre is null ? Task.FromResult(MissingParam()) : SongsResult("songsByGenre", WithTags(ScopedTracks(h, db))
            .Where(t => t.Genre != null && t.Genre.ToLower() == genre.ToLower()).OrderBy(t => t.Title).ThenBy(t => t.Id)
            .Skip(Math.Max(offset ?? 0, 0)).Take(Count(count, 10)), h, db);
    private static Task<IResult> GetTopSongs(string? artist, int? count, HttpContext h, SonaraDbContext db) =>
        artist is null ? Task.FromResult(MissingParam()) : SongsResult("topSongs", WithTags(ScopedTracks(h, db))
            .Where(t => t.Artist.Name.ToLower() == artist.ToLower()).OrderByDescending(t => db.PlayEvents.Count(p => p.TrackId == t.Id)).ThenBy(t => t.Id).Take(Count(count, 50)), h, db);
    private static async Task<IResult> SongsResult(string key, IQueryable<Track> q, HttpContext h, SonaraDbContext db)
    {
        var songs = await q.ToListAsync(); var stars = await Stars(h, db);
        var items = songs.Select(t => stars.Song(SongJson(t))).ToList();
        await AnnotateTracks(items, songs.Select(t => t.Id).ToList(), SubsonicAuthFilter.CurrentUser(h)!.Id, db);
        return SubsonicResult.Ok(Obj((key, Obj(("song", Arr(items))))));
    }
    private static async Task<IResult> SimilarSongs(string? id, int? count, HttpContext h, SonaraDbContext db, bool legacy)
    {
        if (!legacy)
        {
            var artistId = TaggedId(id, "ar-");
            if (!await db.Artists.AnyAsync(a => a.Id == artistId)) return NotFound();
            var own = db.Tracks.Where(t => t.ArtistId == artistId || t.Album.ArtistId == artistId);
            var genres = own.Where(t => t.Genre != null).Select(t => t.Genre).Distinct();
            return await SongsResult("similarSongs2", WithTags(ScopedTracks(h, db))
                .Where(t => t.ArtistId == artistId || (t.Genre != null && genres.Contains(t.Genre)))
                .OrderBy(t => EF.Functions.Random()).Take(Count(count, 50)), h, db);
        }
        var trackId = TaggedId(id, "t-");
        var albumSeedId = TaggedId(id, "al-");
        var seed = id?.StartsWith("dir-") == true ? DirectoryTracks(id, db) :
            id?.StartsWith("al-") == true ? db.Tracks.Where(t => t.AlbumId == albumSeedId) : db.Tracks.Where(t => t.Id == trackId);
        var track = await seed.FirstOrDefaultAsync();
        if (track is null) return NotFound();
        return await SongsResult("similarSongs", WithTags(ScopedTracks(h, db))
            .Where(t => t.Id != track.Id && (t.ArtistId == track.ArtistId || (track.Genre != null && t.Genre == track.Genre)))
            .OrderBy(t => EF.Functions.Random()).Take(Count(count, 50)), h, db);
    }
    private static async Task<IResult> ArtistInfo(string? id, HttpContext h, SonaraDbContext db, bool legacy)
    {
        if (legacy && id?.StartsWith("dir-") == true)
        {
            if (!await DirectoryTracks(id, db).AnyAsync()) return NotFound();
        }
        else
        {
            var artistId = TaggedId(id, "ar-");
            if (!await db.Artists.AnyAsync(a => a.Id == artistId)) return NotFound();
        }
        return SubsonicResult.Ok(Obj((legacy ? "artistInfo" : "artistInfo2", Obj(("similarArtist", new JsonArray())))));
    }
    private static async Task<IResult> AlbumInfo(string? id, SonaraDbContext db, bool legacy)
    {
        var albumId = TaggedId(id, "al-");
        var exists = legacy && id?.StartsWith("dir-") == true ? await DirectoryTracks(id, db).AnyAsync() : await db.Albums.AnyAsync(a => a.Id == albumId);
        return !exists ? NotFound() : SubsonicResult.Ok(Obj((legacy ? "albumInfo" : "albumInfo2", new JsonObject())));
    }
    private static async Task<IResult> GetStarred(HttpContext h, SonaraDbContext db)
    {
        var stars = await Stars(h, db);
        var songs = await WithTags(ScopedTracks(h, db)).Where(t => stars.Tracks.Keys.Contains(t.Id)).ToListAsync();
        var albums = await ScopedAlbums(h, db).Where(a => stars.Albums.Keys.Contains(a.Id)).ToListAsync();
        var artists = await db.Artists.Where(a => stars.Artists.Keys.Contains(a.Id)).ToListAsync();
        var albumJson = albums.Select(a => { var j = stars.Album(AlbumJson(a)); j["id"] = $"al-{a.Id}"; j["isDir"] = true; return j; });
        return SubsonicResult.Ok(Obj(("starred", Obj(("artist", Arr(artists.Select(LegacyArtist))), ("album", Arr(albumJson)), ("song", Arr(songs.Select(t => stars.Song(SongJson(t)))))))));
    }

    // IDs encode a relative directory within a registered folder. A supplied ID never grants filesystem access:
    // every child is derived exclusively from tracks visible under the caller's folder query filters.
    public static string DirectoryId(int folder, string relative) => $"dir-{folder}-" + Convert.ToBase64String(Encoding.UTF8.GetBytes(relative)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static (int Folder, string Relative)? ParseDirectoryId(string? id)
    {
        if (id is null || !id.StartsWith("dir-")) return null;
        var split = id.IndexOf('-', 4);
        if (split < 0 || !int.TryParse(id[4..split], out var folder) || folder < 1) return null;
        try { var encoded = id[(split + 1)..].Replace('-', '+').Replace('_', '/'); return (folder, Encoding.UTF8.GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')))); }
        catch (FormatException) { return null; }
    }
    private static IQueryable<Track> DirectoryTracks(string? id, SonaraDbContext db)
    {
        if (ParseDirectoryId(id) is not { } dir || dir.Relative.Split('/').Any(p => p is ".." or ".")) return db.Tracks.Where(t => false);
        var separator = Path.DirectorySeparatorChar.ToString();
        var relative = dir.Relative.Replace('/', Path.DirectorySeparatorChar);
        return db.Tracks.Where(t => t.MusicFolderId == dir.Folder &&
            t.Path.StartsWith(t.MusicFolder.Path.TrimEnd(Path.DirectorySeparatorChar) + separator + (relative == "" ? "" : relative + separator)));
    }
    private static IResult DirectoryResult(HttpContext h, JsonObject directory)
    {
        var children = directory["child"]?.AsArray() ?? new JsonArray();
        directory["totalChildren"] = children.Count;
        if (h.Request.Query.ContainsKey("size") || h.Request.Query.ContainsKey("offset"))
            directory["child"] = Arr(children.Skip(Offset(h)).Take(Math.Clamp(Number(h, "size") ?? 100, 1, 500)).Select(c => c!.DeepClone()));
        return SubsonicResult.Ok(Obj(("directory", directory)));
    }
    private static async Task<IResult> GetMusicDirectory(string? id, HttpContext h, SonaraDbContext db)
    {
        if (id is null) return MissingParam();
        var stars = await Stars(h, db);
        if (id.StartsWith("ar-"))
        {
            var aid = TaggedId(id, "ar-"); var artist = await db.Artists.FirstOrDefaultAsync(a => a.Id == aid);
            if (artist is null) return NotFound();
            var albums = await ScopedAlbums(h, db).Where(a => a.ArtistId == aid).OrderBy(a => a.Title).ToListAsync();
            return DirectoryResult(h, Obj(("id", id), ("name", artist.Name), ("child", Arr(albums.Select(LegacyAlbum)))));
        }
        if (id.StartsWith("al-"))
        {
            var aid = TaggedId(id, "al-"); var album = await db.Albums.Include(a => a.Artist).FirstOrDefaultAsync(a => a.Id == aid);
            if (album is null) return NotFound();
            var songs = await WithTags(db.Tracks).Where(t => t.AlbumId == aid).OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber).ThenBy(t => t.Id).ToListAsync();
            return DirectoryResult(h, Obj(("id", id), ("parent", $"ar-{album.ArtistId}"), ("name", album.Title), ("child", Arr(songs.Select(t => stars.Song(SongJson(t)))))));
        }
        var parsed = ParseDirectoryId(id);
        if (parsed is null && int.TryParse(id, out var fid)) parsed = (fid, ""); // folder root
        if (parsed is not { } dir) return NotFound();
        var folder = await db.MusicFolders.FirstOrDefaultAsync(f => f.Id == dir.Folder);
        if (folder is null) return NotFound();
        var tracks = await db.Tracks.AsNoTracking().Where(t => t.MusicFolderId == folder.Id)
            .Select(t => new { t.Id, t.Path, t.Title }).ToListAsync();
        var prefix = dir.Relative.Length == 0 ? "" : dir.Relative.TrimEnd('/') + "/";
        var children = new List<JsonNode>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in tracks)
        {
            var relative = Path.GetRelativePath(folder.Path, t.Path).Replace('\\', '/');
            if (relative.StartsWith("../") || !relative.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = relative[prefix.Length..]; var slash = rest.IndexOf('/');
            if (slash < 0) children.Add(Obj(("id", t.Id.ToString()), ("parent", DirectoryId(folder.Id, dir.Relative)), ("title", t.Title), ("isDir", false)));
            else if (seen.Add(rest[..slash])) children.Add(Obj(("id", DirectoryId(folder.Id, prefix + rest[..slash])), ("parent", DirectoryId(folder.Id, dir.Relative)), ("title", rest[..slash]), ("isDir", true)));
        }
        if (dir.Relative.Length > 0 && children.Count == 0) return NotFound();
        var ordered = children.OrderBy(j => j["isDir"]?.GetValue<bool>() == true ? 0 : 1)
            .ThenBy(j => j["title"]?.GetValue<string>()).ThenBy(j => j["id"]?.GetValue<string>()).ToList();
        var total = ordered.Count;
        var page = h.Request.Query.ContainsKey("size") || h.Request.Query.ContainsKey("offset")
            ? ordered.Skip(Offset(h)).Take(Math.Clamp(Number(h, "size") ?? 100, 1, 500)).ToList() : ordered;
        var songIds = page.Where(j => j["isDir"]?.GetValue<bool>() != true).Select(j => int.Parse(j["id"]!.GetValue<string>())).ToList();
        var songsById = await WithTags(db.Tracks).Where(t => songIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id);
        var rendered = new List<JsonNode>();
        foreach (var j in page)
        {
            if (j["isDir"]?.GetValue<bool>() == true) { rendered.Add(j); continue; }
            if (!songsById.TryGetValue(int.Parse(j["id"]!.GetValue<string>()), out var track)) continue;
            var song = stars.Song(SongJson(track));
            song["parent"] = DirectoryId(folder.Id, dir.Relative); rendered.Add(song);
        }
        var parent = dir.Relative.Contains('/') ? dir.Relative[..dir.Relative.LastIndexOf('/')] : "";
        return SubsonicResult.Ok(Obj(("directory", Obj(("id", DirectoryId(folder.Id, dir.Relative)),
            ("parent", dir.Relative.Length == 0 ? null : DirectoryId(folder.Id, parent)), ("totalChildren", total),
            ("name", dir.Relative.Length == 0 ? folder.Name : dir.Relative.Split('/').Last()), ("child", Arr(rendered))))));
    }
}
