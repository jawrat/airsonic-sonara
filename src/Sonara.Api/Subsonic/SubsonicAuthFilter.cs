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
public sealed class SubsonicAuthFilter(SonaraDbContext db, CredentialProtector protector) : IEndpointFilter
{
    public const string UserItemKey = "Sonara.User";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var request = http.Request;
        var form = request.HasFormContentType ? await request.ReadFormAsync() : null;

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
            return SubsonicResult.Error(40, "Wrong username or password.");

        db.AccessUserId = user.Id;
        db.AccessAllFolders = user.IsAdmin || user.AllFolders;
        http.Items[UserItemKey] = user;
        var operation = request.Path.Value!.Split('/').Last().Replace(".view", "");
        if (!user.IsAdmin && !OperationAllowed(user, operation))
            return SubsonicResult.Error(50, "User is not authorized for the given operation.");
        return await next(context);
    }

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
        "changePassword" => user.SettingsRole,
        "setRating" or "addChatMessage" => user.CommentRole,
        _ => true,
    };

    public static User? CurrentUser(HttpContext http) =>
        http.Items.TryGetValue(UserItemKey, out var u) ? u as User : null;
}
