using Sonara.Api.Security;
using Microsoft.EntityFrameworkCore;
using Sonara.Api.Media;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapAvatars(RouteGroupBuilder rest)
    {
        Map(rest, "getAvatars", () => SubsonicResult.Ok(Obj(("avatars", Obj(("avatar", Arr(AvatarImages.Builtins().Select(id => Obj(("id", id), ("name", id.Replace('-', ' ')))))))))));
        rest.MapPost("/setAvatar", SetAvatar).WithMetadata(new SubsonicOperation("setAvatar")); rest.MapPost("/setAvatar.view", SetAvatar).WithMetadata(new SubsonicOperation("setAvatar"));
    }
    private static async Task<IResult> SetAvatar(string? username, string? avatarId, HttpContext h, SonaraDbContext db, AvatarImages images)
    {
        var me = SubsonicAuthFilter.CurrentUser(h)!; var name = string.IsNullOrEmpty(username) ? me.Username : username;
        if (!me.IsAdmin && !name.Equals(me.Username, StringComparison.OrdinalIgnoreCase)) return Forbidden();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == name.ToLower()); if (user is null) return NotFound();
        if (avatarId is null) return MissingParam();
        if (avatarId == "custom")
        {
            if (!h.Request.HasFormContentType) return MissingParam();
            var files = (await h.Request.ReadFormAsync(h.RequestAborted)).Files;
            if (files.Count != 1 || files[0].Length is < 1 or > 2097152) return SubsonicResult.Error(0, "Choose one PNG or JPEG image up to 2 MB.");
            using var source = new MemoryStream(); await files[0].CopyToAsync(source, h.RequestAborted);
            var normalized = await images.Normalize(source.ToArray(), h.RequestAborted);
            if (normalized is null) return SubsonicResult.Error(0, "Could not read the image. Use a PNG or JPEG up to 4096 × 4096 pixels; ffmpeg must be installed.");
            user.CustomAvatar = normalized;
        }
        else if (avatarId != "" && AvatarImages.BuiltinPath(avatarId) is null) return SubsonicResult.Error(0, "Unknown built-in avatar.");
        else user.CustomAvatar = null;
        user.AvatarId = avatarId; await db.SaveChangesAsync(); return SubsonicResult.Ok();
    }
    private static async Task<IResult> GetAvatar(string? username, string? avatarId, HttpContext h, SonaraDbContext db)
    {
        h.Response.Headers.CacheControl = "no-store"; h.Response.Headers.XContentTypeOptions = "nosniff";
        if (avatarId != null)
        {
            var preview = AvatarImages.BuiltinPath(avatarId); return preview is null ? NotFound() : Results.File(preview, "image/png");
        }
        if (string.IsNullOrEmpty(username)) return MissingParam();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        if (user is null) return NotFound();
        if (user.AvatarId == "custom" && user.CustomAvatar != null) return Results.Bytes(user.CustomAvatar, "image/png");
        var path = AvatarImages.BuiltinPath(user.AvatarId);
        if (path != null) return Results.File(path, "image/png");
        return Results.Text("<svg xmlns='http://www.w3.org/2000/svg' width='128' height='128'><rect width='128' height='128' rx='16' fill='#1b1d21'/><circle cx='64' cy='43' r='23' fill='#88bbff'/><path d='M20 116v-12a44 44 0 0 1 88 0v12' fill='#88bbff'/></svg>", "image/svg+xml");
    }
}
