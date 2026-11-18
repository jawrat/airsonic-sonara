using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sonara.Api.Backups;
using Sonara.Api.Options;
using Sonara.Api.Playlists;
using Sonara.Api.Security;
using Sonara.Data;

namespace Sonara.Tests;

public class PlaylistFilesTests
{
    private static readonly PlaylistFileTrack[] Catalog = [new(1, "/music/A/one.mp3", "A/one.mp3", "Music", "One", "Artist", 60), new(2, "/music/A/two.mp3", "A/two.mp3", "Music", "Two", "Artist", 70)];
    [Fact]
    public void M3u_preserves_order_duplicates_unicode_and_comments()
    {
        var entries = PlaylistFiles.Parse("\uFEFF#EXTM3U\n#EXTINF:60,One\n./Music/A/one.mp3\nA/two.mp3\nA/one.mp3\n", "m3u8");
        var matches = PlaylistFiles.Match(entries, Catalog);
        Assert.Equal(new int?[] { 1, 2, 1 }, matches.Select(m => m.TrackId));
    }
    [Fact]
    public void Pls_numeric_order_and_export_round_trip_work()
    {
        var entries = PlaylistFiles.Parse("[playlist]\nFile2=A/two.mp3\nFile1=A/one.mp3\n", "pls");
        Assert.Equal(new int?[] { 1, 2 }, PlaylistFiles.Match(entries, Catalog).Select(m => m.TrackId));
        foreach (var kind in new[] { "m3u8", "pls" })
            Assert.Equal(new int?[] { 2, 1, 2 }, PlaylistFiles.Match(PlaylistFiles.Parse(PlaylistFiles.Export([Catalog[1], Catalog[0], Catalog[1]], kind), kind), Catalog).Select(m => m.TrackId));
    }
    [Fact]
    public void Unknown_roots_can_match_unique_relative_paths_but_ambiguous_paths_are_not_guessed()
    {
        Assert.Equal(1, PlaylistFiles.Match(["D:\\OldMusic\\A\\one.mp3"], Catalog)[0].TrackId);
        var duplicate = Catalog.Append(new(3, "/other/A/one.mp3", "A/one.mp3", "Other", "Other one", "Other", 60)).ToList();
        Assert.Equal("ambiguous", PlaylistFiles.Match(["A/one.mp3"], duplicate)[0].Status);
        Assert.Equal(1, PlaylistFiles.Match(["/music/A/one.mp3"], duplicate)[0].TrackId);
        Assert.Equal("unsupported", PlaylistFiles.Match(["https://example.com/song.mp3"], Catalog)[0].Status);
    }
    [Fact]
    public void Empty_oversized_and_unsupported_formats_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => PlaylistFiles.Parse("#EXTM3U", "m3u8"));
        Assert.Throws<InvalidDataException>(() => PlaylistFiles.Parse(new string('x', PlaylistFiles.MaxCharacters + 1), "m3u8"));
        Assert.Throws<InvalidDataException>(() => PlaylistFiles.Parse("<xml/>", "xspf"));
        Assert.Throws<InvalidDataException>(() => PlaylistFiles.Parse("File1=a\nFile1=b", "pls"));
    }
}
public class BackupRulesTests
{
    [Fact]
    public void Backup_names_cannot_escape_the_managed_folder()
    {
        Assert.True(BackupArchive.IsName(BackupArchive.NewName())); Assert.False(BackupArchive.IsName("../secret.key")); Assert.False(BackupArchive.IsName("other.zip"));
    }
    [Fact]
    public void Passwords_are_not_command_arguments()
    {
        var info = PgTools.StartInfo("pg_dump", "Host=db;Database=sonara;Username=sonara;Password=private-secret;SSL Mode=VerifyFull", ["--format=custom", "--file", "dump"]);
        Assert.DoesNotContain(info.ArgumentList, a => a.Contains("private-secret")); Assert.Equal("private-secret", info.Environment["PGPASSWORD"]); Assert.Equal("verify-full", info.Environment["PGSSLMODE"]);
    }
    [Fact]
    public void Live_data_directory_lock_rejects_another_owner()
    {
        var root = Path.Combine(Path.GetTempPath(), "sonara-lock-" + Guid.NewGuid().ToString("N"));
        try { using var live = InstanceLock.Acquire(root); Assert.Throws<InvalidOperationException>(() => InstanceLock.Acquire(root)); }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Unexpected_zip_paths_are_rejected_before_extraction()
    {
        var root = Path.Combine(Path.GetTempPath(), "sonara-zip-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "bad.zip"); using (var z = ZipFile.Open(file, ZipArchiveMode.Create)) z.CreateEntry("../secret.key");
            await Assert.ThrowsAsync<InvalidDataException>(() => BackupArchive.ValidateAsync(file, Path.Combine(root, "out"), CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(root, "secret.key")));
        }
        finally { Directory.Delete(root, true); }
    }
}

[Collection("PostgreSQL integration")]
public class PlaylistFileIntegrationTests(ParityHost host) : IClassFixture<ParityHost>
{
    [DatabaseFact]
    public async Task Import_preview_creates_nothing_apply_preserves_order_and_export_round_trips()
    {
        using var client = host.CreateClient();
        const string content = "#EXTM3U\nArtist/Album/first.wav\nmissing.mp3\nArtist/Album/second.wav\nArtist/Album/first.wav\nhttps://example.com/song.mp3\n";
        async Task<System.Text.Json.Nodes.JsonNode> Import(bool preview)
        {
            var result = await client.PostAsync("/rest/importPlaylistFile", new FormUrlEncodedContent(new Dictionary<string, string> { ["u"] = "listener", ["p"] = "Test-Password-123!", ["f"] = "json", ["content"] = content, ["format"] = "m3u8", ["name"] = "Portability", ["dryRun"] = preview.ToString() }));
            return System.Text.Json.Nodes.JsonNode.Parse(await result.Content.ReadAsStringAsync())!["subsonic-response"]!;
        }
        var preview = await Import(true); Assert.Equal(3, preview["playlistImport"]!["matched"]!.GetValue<int>()); Assert.Equal(2, preview["playlistImport"]!["unmatched"]!.GetValue<int>());
        using (var scope = host.Services.CreateScope()) Assert.False(await scope.ServiceProvider.GetRequiredService<SonaraDbContext>().Playlists.AnyAsync(p => p.Name == "Portability"));
        var created = await Import(false); var id = created["playlistImport"]!["playlistId"]!.ToString();
        var playlist = await host.Call("getPlaylist", extra: "&id=" + id); Assert.False(playlist["playlist"]!["public"]!.GetValue<bool>()); Assert.Equal(3, playlist["playlist"]!["entry"]!.AsArray().Count);
        var export = await client.GetStringAsync(host.Url("exportPlaylistFile", extra: "&id=" + id + "&format=m3u8"));
        Assert.Equal(3, PlaylistFiles.Parse(export, "m3u8").Count); Assert.DoesNotContain(host.Root, export);
    }
    [DatabaseFact]
    public async Task Backup_operations_require_admin_and_reject_arbitrary_file_names()
    {
        var denied = await host.Call("getBackups"); Assert.Equal(50, denied["error"]!["code"]!.GetValue<int>());
        var listed = await host.Call("getBackups", "admin"); Assert.Equal("ok", listed["status"]!.ToString());
        var bad = await host.Call("downloadBackup", "admin", "&name=../secret.key"); Assert.Equal(70, bad["error"]!["code"]!.GetValue<int>());
    }
}

public sealed class BackupDatabaseFactAttribute : FactAttribute
{
    public BackupDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SONARA_TEST_DATABASE")) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SONARA_TEST_PG_DUMP")) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SONARA_TEST_PG_RESTORE")))
            Skip = "Set SONARA_TEST_DATABASE plus SONARA_TEST_PG_DUMP and SONARA_TEST_PG_RESTORE for real backup/restore tests.";
    }
}
[Collection("PostgreSQL integration")]
public class BackupRestoreIntegrationTests(ParityHost host) : IClassFixture<ParityHost>
{
    [BackupDatabaseFact]
    public async Task Background_backup_prunes_only_older_managed_archives_after_success()
    {
        var options = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SonaraOptions>>().Value;
        var original = options.Backups;
        options.Backups = new() { KeepLatest = 1, PgDumpPath = Environment.GetEnvironmentVariable("SONARA_TEST_PG_DUMP")!, PgRestorePath = Environment.GetEnvironmentVariable("SONARA_TEST_PG_RESTORE")! };
        try
        {
            var folder = BackupArchive.Folder(options); Directory.CreateDirectory(folder);
            var old = Path.Combine(folder, BackupArchive.NewName()); await File.WriteAllTextAsync(old, "old managed archive"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));
            var notes = Path.Combine(folder, "keep-my-notes.zip"); await File.WriteAllTextAsync(notes, "unmanaged");
            var service = host.Services.GetRequiredService<BackupService>(); Assert.True(service.Queue()); Assert.False(service.Queue());
            for (var i = 0; i < 200 && service.Status.Busy; i++) await Task.Delay(50);
            Assert.False(service.Status.Busy); Assert.Null(service.Status.LastError); Assert.Equal("completed", service.Status.Phase);
            Assert.False(File.Exists(old)); Assert.True(File.Exists(notes)); Assert.Single(service.List());
        }
        finally { options.Backups = original; }
    }

    [BackupDatabaseFact]
    public async Task Real_archive_restore_recovers_users_and_key_and_refuses_overwrite_or_live_restore()
    {
        using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<SonaraDbContext>();
        var source = new SonaraOptions { DataDir = host.Root, Database = new() { ConnectionString = db.Database.GetConnectionString() },
            Backups = new() { PgDumpPath = Environment.GetEnvironmentVariable("SONARA_TEST_PG_DUMP")!, PgRestorePath = Environment.GetEnvironmentVariable("SONARA_TEST_PG_RESTORE")! } };
        var name = await BackupArchive.CreateAsync(source, CancellationToken.None); var archive = Path.Combine(BackupArchive.Folder(source), name);
        Assert.Equal(0, await BackupCommand.RunAsync(["--inspect-backup", archive], source));
        Assert.Equal(1, await BackupCommand.RunAsync(["--restore-backup", archive, "--target-database", "never-created"], source)); // --confirm-stopped is mandatory
        var restoredRoot = Path.Combine(host.Root, "restored");
        var targetName = "sonara_restore_" + Guid.NewGuid().ToString("N");
        await using var admin = new Npgsql.NpgsqlConnection(Environment.GetEnvironmentVariable("SONARA_TEST_DATABASE")); await admin.OpenAsync();
        await using (var create = new Npgsql.NpgsqlCommand($"CREATE DATABASE \"{targetName}\"", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => BackupArchive.RestoreAsync(source, archive, targetName, CancellationToken.None)); // source data directory is serving requests
            var targetOptions = new SonaraOptions { DataDir = restoredRoot, Database = source.Database, Backups = source.Backups };
            await BackupArchive.RestoreAsync(targetOptions, archive, targetName, CancellationToken.None);
            var keyText = await File.ReadAllTextAsync(Path.Combine(restoredRoot, "secret.key"));
            Assert.Equal(await File.ReadAllTextAsync(Path.Combine(host.Root, "secret.key")), keyText);
            var targetCs = new Npgsql.NpgsqlConnectionStringBuilder(source.Database.ConnectionString) { Database = targetName, Pooling = false };
            await using (var target = new Npgsql.NpgsqlConnection(targetCs.ConnectionString))
            {
                await target.OpenAsync(); await using var command = new Npgsql.NpgsqlCommand("SELECT \"SubsonicSecret\" FROM \"Users\" WHERE \"Username\"='listener'", target);
                var secret = (string)(await command.ExecuteScalarAsync())!;
                Assert.Equal("Test-Password-123!", new CredentialProtector(Convert.FromBase64String(keyText.Trim())).Unprotect(secret));
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => BackupArchive.RestoreAsync(targetOptions, archive, targetName, CancellationToken.None));
            Assert.Equal(keyText, await File.ReadAllTextAsync(Path.Combine(restoredRoot, "secret.key")));
            var corrupted = Path.Combine(host.Root, "corrupt.zip"); File.Copy(archive, corrupted);
            using (var zip = ZipFile.Open(corrupted, ZipArchiveMode.Update))
            {
                zip.GetEntry("secret.key")!.Delete(); using var writer = new StreamWriter(zip.CreateEntry("secret.key").Open()); writer.Write(Convert.ToBase64String(new byte[32]));
            }
            await Assert.ThrowsAsync<InvalidDataException>(() => BackupArchive.ValidateAsync(corrupted, Path.Combine(host.Root, "bad-inspection"), CancellationToken.None));
        }
        finally { await using var drop = new Npgsql.NpgsqlCommand($"DROP DATABASE \"{targetName}\" WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync(); }
    }
}
