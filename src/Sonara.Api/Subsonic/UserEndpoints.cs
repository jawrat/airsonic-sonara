using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Sonara.Api.Security;
using Sonara.Core.Entities;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapUsers(RouteGroupBuilder rest)
    {
        Map(rest, "getUser", GetUser);
        Map(rest, "setTranscodingPreferences", SetTranscodingPreferences);
        Map(rest, "getUsers", GetUsers);
        Map(rest, "createUser", CreateUser);
        Map(rest, "updateUser", UpdateUser);
        Map(rest, "deleteUser", DeleteUser);
        Map(rest, "changePassword", ChangePassword);
    }

    private static readonly Dictionary<string, string> UserRoleProperties = new()
    {
        ["settingsRole"] = nameof(User.SettingsRole), ["streamRole"] = nameof(User.StreamRole),
        ["downloadRole"] = nameof(User.DownloadRole), ["playlistRole"] = nameof(User.PlaylistRole),
        ["coverArtRole"] = nameof(User.CoverArtRole), ["podcastRole"] = nameof(User.PodcastRole),
        ["shareRole"] = nameof(User.ShareRole), ["uploadRole"] = nameof(User.UploadRole),
        ["commentRole"] = nameof(User.CommentRole),
    };
    private static JsonObject UserJson(User u, IEnumerable<int>? folders = null)
    {
        var json = Obj(("username", u.Username), ("email", u.Email), ("adminRole", u.IsAdmin),
            ("avatarId", u.AvatarId), ("hasCustomAvatar", u.CustomAvatar != null),
            ("preferredTranscodeFormat", u.PreferredTranscodeFormat), ("preferredTranscodeBitRate", u.PreferredTranscodeBitRate),
            ("scrobblingEnabled", false), ("allFolders", u.AllFolders), ("maxBitRate", u.MaxBitRate),
            ("folder", new JsonArray((folders ?? []).Select(i => (JsonNode?)JsonValue.Create(i)).ToArray())));
        foreach (var (role, property) in UserRoleProperties)
            json[role] = u.IsAdmin || (bool)typeof(User).GetProperty(property)!.GetValue(u)!;
        // No server-side audio device or video conversion support.
        json["jukeboxRole"] = false; json["videoConversionRole"] = false; json["uploadRole"] = u.IsAdmin || u.UploadRole;
        return json;
    }
    private static async Task<JsonObject> UserWithFolders(User u, SonaraDbContext db)
    {
        var folders = await db.MusicFolders.IgnoreQueryFilters().Where(f => f.Enabled &&
            (u.IsAdmin || u.AllFolders || db.UserMusicFolders.Any(x => x.UserId == u.Id && x.MusicFolderId == f.Id)))
            .Select(f => f.Id).ToListAsync();
        return UserJson(u, folders);
    }
    private static string? ValidateUserOptions(HttpContext h)
    {
        foreach (var role in UserRoleProperties.Keys.Append("allFolders"))
            if (h.Request.Query.ContainsKey(role) && !bool.TryParse(h.Request.Query[role], out _)) return "Invalid boolean: " + role;
        if (h.Request.Query.ContainsKey("maxBitRate") && (!int.TryParse(h.Request.Query["maxBitRate"], out var rate) || (rate != 0 && rate < 32) || rate > 512))
            return "Maximum bitrate must be 0 (unlimited) or from 32 to 512.";
        if (h.Request.Query["email"].ToString().Length > 320) return "Email address is too long.";
        if (Values(h, "musicFolderId").Any(v => !int.TryParse(v, out var id) || id <= 0)) return "Invalid folder ID.";
        if (h.Request.Query["ldapAuthenticated"] == "true") return "LDAP authentication is not configured.";
        if (h.Request.Query["jukeboxRole"] == "true" || h.Request.Query["videoConversionRole"] == "true")
            return "Jukebox and video conversion are not supported.";
        return null;
    }
    private static async Task ApplyUserOptions(User u, HttpContext h, SonaraDbContext db)
    {
        foreach (var (role, property) in UserRoleProperties)
            if (bool.TryParse(h.Request.Query[role], out var value)) typeof(User).GetProperty(property)!.SetValue(u, value);
        if (h.Request.Query.ContainsKey("email")) u.Email = h.Request.Query["email"].ToString();
        if (int.TryParse(h.Request.Query["maxBitRate"], out var rate)) u.MaxBitRate = rate;
        if (bool.TryParse(h.Request.Query["allFolders"], out var all)) u.AllFolders = all;
        if (h.Request.Query.ContainsKey("musicFolderId") || h.Request.Query["allFolders"].ToString().Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            var ids = IntValues(h, "musicFolderId").Distinct().ToList();
            var existing = await db.UserMusicFolders.Where(f => f.UserId == u.Id).ToListAsync();
            db.UserMusicFolders.RemoveRange(existing.Where(f => !ids.Contains(f.MusicFolderId)));
            foreach (var id in ids.Where(id => existing.All(f => f.MusicFolderId != id)))
                db.UserMusicFolders.Add(new UserMusicFolder { UserId = u.Id, MusicFolderId = id });
            u.AllFolders = false;
        }
    }
    private static async Task<bool> ValidFolders(HttpContext h, SonaraDbContext db)
    {
        var ids = IntValues(h, "musicFolderId").Distinct().ToList();
        return await db.MusicFolders.IgnoreQueryFilters().CountAsync(f => ids.Contains(f.Id)) == ids.Count;
    }

    private static bool IsAdmin(HttpContext http) => SubsonicAuthFilter.CurrentUser(http)?.IsAdmin == true;

    private static async Task<IResult> SetTranscodingPreferences(string? username, string? preferredTranscodeFormat,
        int? preferredTranscodeBitRate, HttpContext http, SonaraDbContext db)
    {
        var me = SubsonicAuthFilter.CurrentUser(http)!;
        var name = string.IsNullOrEmpty(username) ? me.Username : username;
        if (!me.IsAdmin && !string.Equals(name, me.Username, StringComparison.OrdinalIgnoreCase)) return Forbidden();
        var format = preferredTranscodeFormat?.Trim().ToLowerInvariant();
        if (format is null || preferredTranscodeBitRate is null) return MissingParam();
        if (format is not ("" or "raw" or "mp3" or "opus" or "aac")) return SubsonicResult.Error(0, "Choose default, original, MP3, Opus or AAC.");
        if (preferredTranscodeBitRate != 0 && (preferredTranscodeBitRate < 32 || preferredTranscodeBitRate > 320))
            return SubsonicResult.Error(0, "Preferred bitrate must be 0 (default) or from 32 to 320 kbps.");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == name.ToLower());
        if (user is null) return NotFound();
        user.PreferredTranscodeFormat = format;
        user.PreferredTranscodeBitRate = preferredTranscodeBitRate.Value;
        await db.SaveChangesAsync();
        return SubsonicResult.Ok(Obj(("user", await UserWithFolders(user, db))));
    }

    private static async Task<IResult> GetUser(string? username, HttpContext http, SonaraDbContext db)
    {
        var me = SubsonicAuthFilter.CurrentUser(http)!;
        var name = string.IsNullOrEmpty(username) ? me.Username : username;
        if (!me.IsAdmin && !string.Equals(name, me.Username, StringComparison.OrdinalIgnoreCase))
            return Forbidden();

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username.ToLower() == name.ToLower());
        return user is null ? NotFound() : SubsonicResult.Ok(Obj(("user", await UserWithFolders(user, db))));
    }

    private static async Task<IResult> GetUsers(HttpContext http, SonaraDbContext db)
    {
        if (!IsAdmin(http)) return Forbidden();
        var users = await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync();
        var items = new List<JsonNode>();
        foreach (var user in users) items.Add(await UserWithFolders(user, db));
        return SubsonicResult.Ok(Obj(("users", Obj(("user", Arr(items))))));
    }

    private static async Task<IResult> CreateUser(
        string? username, string? password, bool? adminRole,
        HttpContext http, SonaraDbContext db, UserService users)
    {
        if (!IsAdmin(http)) return Forbidden();
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return MissingParam();

        if (ValidateUserOptions(http) is { } invalid) return SubsonicResult.Error(0, invalid);
        if (!await ValidFolders(http, db)) return NotFound();
        var plain = UserRules.DecodePassword(password);
        if (!UserRules.IsValidUsername(username))
            return SubsonicResult.Error(0, "Usernames may contain letters, digits and . _ @ - (max 64).");
        if (!UserRules.IsValidPassword(plain))
            return SubsonicResult.Error(0, $"Password must be at least {UserRules.MinPasswordLength} characters.");
        if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
            return SubsonicResult.Error(0, "That username is already taken.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var created = await users.CreateAsync(username, plain!, adminRole ?? false);
        await ApplyUserOptions(created, http, db);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return SubsonicResult.Ok();
    }

    private static async Task<IResult> UpdateUser(
        string? username, string? password, bool? adminRole,
        HttpContext http, SonaraDbContext db, UserService users)
    {
        if (!IsAdmin(http)) return Forbidden();
        if (string.IsNullOrEmpty(username)) return MissingParam();

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(739201610)");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user is null) return NotFound();

        if (ValidateUserOptions(http) is { } invalid) return SubsonicResult.Error(0, invalid);
        if (!await ValidFolders(http, db)) return NotFound();
        await ApplyUserOptions(user, http, db);
        if (adminRole is false && user.IsAdmin)
        {
            var all = await db.Users.AsNoTracking().Select(u => new { u.Id, u.IsAdmin }).ToListAsync();
            if (UserRules.WouldRemoveLastAdmin(all.Select(u => (u.Id, u.IsAdmin)), user.Id))
                return SubsonicResult.Error(0, "At least one administrator must remain.");
        }
        if (adminRole.HasValue) user.IsAdmin = adminRole.Value;

        if (!string.IsNullOrEmpty(password))
        {
            var plain = UserRules.DecodePassword(password);
            if (!UserRules.IsValidPassword(plain))
                return SubsonicResult.Error(0, $"Password must be at least {UserRules.MinPasswordLength} characters.");
            await users.SetPasswordAsync(user, plain!);
        }
        else
        {
            await db.SaveChangesAsync();
        }
        await transaction.CommitAsync();
        return SubsonicResult.Ok();
    }

    private static async Task<IResult> DeleteUser(string? username, HttpContext http, SonaraDbContext db)
    {
        if (!IsAdmin(http)) return Forbidden();
        if (string.IsNullOrEmpty(username)) return MissingParam();

        var me = SubsonicAuthFilter.CurrentUser(http)!;
        if (string.Equals(username, me.Username, StringComparison.OrdinalIgnoreCase))
            return SubsonicResult.Error(0, "You cannot delete your own account.");

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(739201610)");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user is null) return NotFound();

        var all = await db.Users.AsNoTracking().Select(u => new { u.Id, u.IsAdmin }).ToListAsync();
        if (user.IsAdmin && UserRules.WouldRemoveLastAdmin(all.Select(u => (u.Id, u.IsAdmin)), user.Id))
            return SubsonicResult.Error(0, "At least one administrator must remain.");

        db.Users.Remove(user); // their playlists cascade
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return SubsonicResult.Ok();
    }

    private static async Task<IResult> ChangePassword(
        string? username, string? password, HttpContext http, SonaraDbContext db, UserService users)
    {
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return MissingParam();

        var me = SubsonicAuthFilter.CurrentUser(http)!;
        if (!me.IsAdmin && !string.Equals(username, me.Username, StringComparison.OrdinalIgnoreCase))
            return Forbidden();

        var plain = UserRules.DecodePassword(password);
        if (!UserRules.IsValidPassword(plain))
            return SubsonicResult.Error(0, $"Password must be at least {UserRules.MinPasswordLength} characters.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user is null) return NotFound();

        await users.SetPasswordAsync(user, plain!);
        return SubsonicResult.Ok();
    }
}
