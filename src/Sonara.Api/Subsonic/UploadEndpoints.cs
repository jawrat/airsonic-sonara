using Sonara.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sonara.Api.Options;
using Sonara.Api.Scanning;
using Sonara.Api.Uploads;
using Sonara.Data;
using static Sonara.Api.Subsonic.SubsonicJson;

namespace Sonara.Api.Subsonic;

public static partial class SubsonicEndpoints
{
    private static void MapUploads(RouteGroupBuilder rest)
    {
        Map(rest, "getUploadFolders", GetUploadFolders);
        rest.MapPost("/uploadMusic", UploadMusic).WithMetadata(new SubsonicOperation("uploadMusic"));
        rest.MapPost("/uploadMusic.view", UploadMusic).WithMetadata(new SubsonicOperation("uploadMusic"));
    }
    private static async Task<IResult> GetUploadFolders(SonaraDbContext db, IOptions<SonaraOptions> options)
    {
        var folders = await db.MusicFolders.AsNoTracking().Where(f => f.Enabled && f.AllowUploads).OrderBy(f => f.Name).ToListAsync();
        return SubsonicResult.Ok(Obj(("uploadFolders", Obj(("maxFileMegabytes", Math.Clamp(options.Value.Uploads.MaxFileMegabytes, 1, 1024)),
            ("folder", Arr(folders.Select(f => Obj(("id", f.Id), ("name", f.Name)))))))));
    }
    private static async Task<IResult> UploadMusic(int? musicFolderId, string? subfolder, HttpContext h, SonaraDbContext db,
        IOptions<SonaraOptions> options, IMediaScanner scanner)
    {
        if (!musicFolderId.HasValue || !h.Request.HasFormContentType) return MissingParam();
        var folder = await db.MusicFolders.FirstOrDefaultAsync(f => f.Id == musicFolderId && f.Enabled && f.AllowUploads);
        if (folder is null) return Forbidden();
        var form = await h.Request.ReadFormAsync(h.RequestAborted);
        if (form.Files.Count != 1) return SubsonicResult.Error(0, "Upload one audio file per request.");
        var file = form.Files[0];
        var limit = (long)Math.Clamp(options.Value.Uploads.MaxFileMegabytes, 1, 1024) * 1024 * 1024;
        if (file.Length < 1 || file.Length > limit) return SubsonicResult.Error(0, "The audio file exceeds the upload size limit or is empty.");
        string? temp = null;
        try
        {
            var name = UploadPaths.FileName(file.FileName);
            if (!ScanRules.IsAudioFile(name)) throw new InvalidDataException("Choose a supported audio file, not an archive or playlist.");
            var destination = UploadPaths.DirectoryPath(folder.Path, subfolder);
            if (File.Exists(Path.Combine(destination, name))) return SubsonicResult.Error(0, "That filename already exists. Rename the file before uploading.");
            var staging = Path.Combine(Path.GetFullPath(options.Value.DataDir), "upload-staging"); Directory.CreateDirectory(staging);
            temp = Path.Combine(staging, Guid.NewGuid().ToString("N") + Path.GetExtension(name));
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) await file.CopyToAsync(output, h.RequestAborted);
            using (var media = TagLib.File.Create(temp))
                if (media.Properties.Duration <= TimeSpan.Zero || media.Properties.AudioSampleRate <= 0) throw new InvalidDataException("The file does not contain readable audio.");
            // Copy to a non-audio temporary extension on the destination filesystem, then atomically publish without overwrite.
            Directory.CreateDirectory(destination);
            destination = UploadPaths.DirectoryPath(folder.Path, subfolder);
            var localTemp = Path.Combine(destination, ".sonara-upload-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                await using (var source = File.OpenRead(temp))
                await using (var target = new FileStream(localTemp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await source.CopyToAsync(target, h.RequestAborted);
                UploadPaths.DirectoryPath(folder.Path, subfolder);
                File.Move(localTemp, Path.Combine(destination, name), overwrite: false);
            }
            finally { if (File.Exists(localTemp)) File.Delete(localTemp); }
            scanner.RequestScan();
            return SubsonicResult.Ok(Obj(("upload", Obj(("name", name), ("bytes", file.Length), ("scanRequested", true)))));
        }
        catch (Exception ex) when (ex is InvalidDataException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
        { return SubsonicResult.Error(0, ex is InvalidDataException ? ex.Message : "The file does not contain readable audio."); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return SubsonicResult.Error(0, "The upload could not be saved. Check write permissions, free space and duplicate filenames."); }
        finally { if (temp != null && File.Exists(temp)) File.Delete(temp); }
    }
}
