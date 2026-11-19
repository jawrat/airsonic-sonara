using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Sonara.Api.Security;
using Sonara.Core.Entities;
using Sonara.Data;

namespace Sonara.Api.Subsonic;

/// <summary>
/// Authenticates every /rest call (u + t/s token, or u + p). On success the User is placed in
/// HttpContext.Items under <see cref="UserItemKey"/>.
/// </summary>
public sealed class SubsonicAuthFilter(SonaraDbContext db, CredentialProtector protector, AuthenticationThrottle throttle) : IEndpointFilter
{
    public const string UserItemKey = "Sonara.User";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var denied = await AuthorizeAsync(context.HttpContext, includeForm: true);
        return denied ?? await next(context);
    }

    public async Task<IResult?> AuthorizeAsync(HttpContext http, bool includeForm)
    {
        if (CurrentUser(http) is not null) return null;
        var request = http.Request;
        var form = includeForm && request.HasFormContentType ? await request.ReadFormAsync() : null;

        string? Param(string name)
        {
            var v = request.Query[name].FirstOrDefault();
            return !string.IsNullOrEmpty(v) ? v : form?[name].FirstOrDefault();
        }

        var username = Param("u");
        var token = Param("t");
        var salt = Param("s");
        var password = Param("p");

        var hasTokenAuth = !string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(salt);
        if (string.IsNullOrEmpty(username) || (!hasTokenAuth && string.IsNullOrEmpty(password)))
            return SubsonicResult.Error(10, "Required parameter is missing.");

        if (throttle.IsBlocked(http))
        {
            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            http.Response.Headers.RetryAfter = "60";
            return SubsonicResult.Error(0, "Too many failed sign-in attempts. Retry in one minute.");
        }
        if (username.Length > 64 || token?.Length > 128 || salt?.Length > 256 || password?.Length > 8192)
        {
            throttle.Failed(http);
            return SubsonicResult.Error(40, "Wrong username or password.");
        }
        var normalized = username.ToLowerInvariant();
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalized);

        var secret = "";
        if (user is not null)
        {
            try
            {
                secret = protector.Unprotect(user.SubsonicSecret);
            }
            catch (CryptographicException)
            {
                secret = "";
            }
        }

        if (user is null || !SubsonicCredentials.Verify(secret, token, salt, password))
        {
            throttle.Failed(http);
            return SubsonicResult.Error(40, "Wrong username or password.");
        }

        db.AccessUserId = user.Id;
        db.AccessAllFolders = user.IsAdmin || user.AllFolders;
        http.Items[UserItemKey] = user;
        var operation = http.GetEndpoint()?.Metadata.GetMetadata<SubsonicOperation>()?.Name;
        if (operation is null) return SubsonicResult.Error(0, "Unknown or unsupported API operation.");
        if (!user.IsAdmin && !OperationAllowed(user, operation))
            return SubsonicResult.Error(50, "User is not authorized for the given operation.");
        return null;
    }

    private static readonly HashSet<string> GeneralOperations = new(StringComparer.Ordinal)
    {
        "ping", "getLicense", "getOpenSubsonicExtensions", "getMusicFolders", "getMusicDirectory", "getIndexes", "getArtists", "getArtist", "getAlbum", "getSong",
        "getAlbumList", "getAlbumList2", "search", "search2", "search3", "getPlaylist", "getPlaylists", "getStarred", "getStarred2",
        "getBookmarks", "createBookmark", "deleteBookmark", "getPlayQueue", "savePlayQueue", "star", "unstar", "scrobble",
        "getGenres", "getSongsByGenre", "getRandomSongs", "getSimilarSongs", "getSimilarSongs2", "getTopSongs", "getVideos", "getVideoInfo", "getCaptions", "jukeboxControl",
        "getAlbumInfo", "getAlbumInfo2", "getArtistInfo", "getArtistInfo2", "getNowPlaying", "getLyrics", "getUser", "getAvatars",
        "getChatMessages", "getInternetRadioStations", "getShares", "getPodcasts", "getNewestPodcasts", "getScanStatus", "exportPlaylistFile", "exportPodcasts/opml",
    };

    public static bool OperationAllowed(User user, string operation) => user.IsAdmin || operation switch
    {
        "stream" or "hls" => user.StreamRole,
        "getUploadFolders" or "uploadMusic" => user.UploadRole,
        "download" => user.DownloadRole,
        "getCoverArt" or "getAvatar" => user.CoverArtRole,
        "createPlaylist" or "updatePlaylist" or "deletePlaylist" or "importPlaylistFile" => user.PlaylistRole,
        "createShare" or "updateShare" or "deleteShare" => user.ShareRole,
        "createPodcastChannel" or "deletePodcastChannel" or "refreshPodcasts" or
            "downloadPodcastEpisode" or "deletePodcastEpisode" or "searchPodcasts" or "importPodcastOpml" or "setPodcastEpisodePinned" => user.PodcastRole,
        "changePassword" or "setAvatar" or "setTranscodingPreferences" => user.SettingsRole,
        "setRating" or "addChatMessage" => user.CommentRole,
        _ => GeneralOperations.Contains(operation),
    };

    public static User? CurrentUser(HttpContext http) =>
        http.Items.TryGetValue(UserItemKey, out var u) ? u as User : null;
}
