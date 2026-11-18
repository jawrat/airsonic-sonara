using Sonara.Api.Backups;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapBackups(RouteGroupBuilder rest)
    {
        Map(rest, "getBackups", GetBackups);
        Map(rest, "createBackup", CreateBackup);
        Map(rest, "downloadBackup", DownloadBackup);
    }
    private static IResult GetBackups(HttpContext h, BackupService backups)
    {
        if (!IsAdmin(h)) return Forbidden();
        var status = backups.Status;
        IReadOnlyList<BackupFile> files;
        try { files = backups.List(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return SubsonicResult.Error(0, "The backup folder cannot be read. Check its server-side permissions."); }
        return SubsonicResult.Ok(Obj(("backups", Obj(("busy", status.Busy), ("phase", status.Phase), ("lastFile", status.LastFile),
            ("lastError", status.LastError), ("lastCompleted", status.LastCompletedUtc),
            ("backup", Arr(files.Select(f => Obj(("name", f.Name), ("bytes", f.Bytes), ("created", f.CreatedUtc)))))))));
    }
    private static IResult CreateBackup(HttpContext h, BackupService backups)
    {
        if (!IsAdmin(h)) return Forbidden();
        return backups.Queue() ? SubsonicResult.Ok(Obj(("queued", true))) : SubsonicResult.Error(0, "A backup is already queued or running.");
    }
    private static IResult DownloadBackup(string? name, HttpContext h, BackupService backups)
    {
        if (!IsAdmin(h)) return Forbidden();
        if (name is null) return MissingParam();
        var path = backups.Resolve(name); if (path is null) return NotFound();
        h.Response.Headers.CacheControl = "no-store";
        return Results.File(path, "application/zip", name, enableRangeProcessing: true);
    }
}
