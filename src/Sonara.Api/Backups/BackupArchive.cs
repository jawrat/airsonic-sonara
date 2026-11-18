using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using Sonara.Api.Options;
using Sonara.Api.Security;

namespace Sonara.Api.Backups;

public sealed record BackupManifest(int FormatVersion, DateTime CreatedUtc, string ApplicationVersion, string DatabaseSha256, string KeySha256, string PostgreSqlVersion);
public sealed record ValidatedBackup(BackupManifest Manifest, string DatabaseFile, string KeyFile);

public static class BackupArchive
{
    public static string Folder(SonaraOptions o) => Path.GetFullPath(string.IsNullOrWhiteSpace(o.Backups.Folder) ? Path.Combine(o.DataDir, "backups") : o.Backups.Folder);
    public static string NewName() => $"sonara-backup-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid().ToString("N")[..8]}.zip";
    public static bool IsName(string name) => System.Text.RegularExpressions.Regex.IsMatch(name, @"^sonara-backup-\d{8}T\d{9}Z-[a-f0-9]{8}\.zip$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    public static void Private(string path) { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    private static async Task<string> Hash(string path, CancellationToken ct) { await using var s = File.OpenRead(path); return Convert.ToHexStringLower(await SHA256.HashDataAsync(s, ct)); }
    private static void ValidateKey(string text)
    {
        try { if (Convert.FromBase64String(text.Trim()).Length != 32) throw new FormatException(); }
        catch (FormatException ex) { throw new InvalidDataException("The backup does not contain a valid Sonara credential key.", ex); }
    }
    public static async Task<string> CreateAsync(SonaraOptions options, CancellationToken ct)
    {
        var cs = options.Database.ConnectionString ?? throw new InvalidOperationException("A database connection string is required.");
        var folder = Folder(options); Directory.CreateDirectory(folder);
        var temp = Path.Combine(folder, ".creating-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var destination = Path.Combine(folder, NewName()); var staged = Path.Combine(temp, "backup.zip");
        try
        {
            var key = Path.Combine(temp, "secret.key");
            var keyText = await File.ReadAllTextAsync(Path.Combine(Path.GetFullPath(options.DataDir), "secret.key"), ct); ValidateKey(keyText);
            await File.WriteAllTextAsync(key, keyText, ct); Private(key);
            await ValidateCredentialsAsync(cs, keyText, ct);
            var dump = Path.Combine(temp, "database.dump");
            await PgTools.RunAsync(options.Backups.PgDumpPath, cs, ["--format=custom", "--no-owner", "--no-privileges", "--file", dump], ct);
            await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync(ct);
            var manifest = new BackupManifest(1, DateTime.UtcNow, typeof(BackupArchive).Assembly.GetName().Version?.ToString() ?? "unknown", await Hash(dump, ct), await Hash(key, ct), connection.PostgreSqlVersion.ToString());
            await File.WriteAllTextAsync(Path.Combine(temp, "manifest.json"), JsonSerializer.Serialize(manifest), ct);
            using (var zip = ZipFile.Open(staged, ZipArchiveMode.Create))
                foreach (var file in new[] { "database.dump", "secret.key", "manifest.json" }) zip.CreateEntryFromFile(Path.Combine(temp, file), file, CompressionLevel.NoCompression);
            Private(staged); File.Move(staged, destination);
            return Path.GetFileName(destination);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
    public static async Task<ValidatedBackup> ValidateAsync(string archive, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var zip = ZipFile.OpenRead(archive);
        var names = new HashSet<string>(["database.dump", "secret.key", "manifest.json"], StringComparer.Ordinal);
        if (zip.Entries.Count != 3 || !zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal).SetEquals(names)) throw new InvalidDataException("Invalid Sonara backup entries.");
        foreach (var entry in zip.Entries)
        {
            var maximum = entry.FullName == "database.dump" ? 20L * 1024 * 1024 * 1024 : entry.FullName == "secret.key" ? 1024L : 65536L;
            if (entry.Length < 1 || entry.Length > maximum) throw new InvalidDataException("A backup entry has an unsupported size.");
            await using var input = entry.Open(); var path = Path.Combine(destination, entry.FullName);
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Private(path); var buffer = new byte[81920]; long total = 0; int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0) { total += read; if (total > maximum) throw new InvalidDataException("Backup entry exceeds its size limit."); await file.WriteAsync(buffer.AsMemory(0, read), ct); }
            }
        }
        var manifest = JsonSerializer.Deserialize<BackupManifest>(await File.ReadAllTextAsync(Path.Combine(destination, "manifest.json"), ct)) ?? throw new InvalidDataException("Missing backup manifest.");
        var dump = Path.Combine(destination, "database.dump"); var key = Path.Combine(destination, "secret.key");
        if (manifest.FormatVersion != 1 || await Hash(dump, ct) != manifest.DatabaseSha256 || await Hash(key, ct) != manifest.KeySha256) throw new InvalidDataException("Backup format or integrity check failed.");
        ValidateKey(await File.ReadAllTextAsync(key, ct));
        await using (var s = File.OpenRead(dump)) { var magic = new byte[5]; await s.ReadExactlyAsync(magic, ct); if (!magic.AsSpan().SequenceEqual("PGDMP"u8)) throw new InvalidDataException("The backup database archive is not a PostgreSQL custom dump."); }
        return new(manifest, dump, key);
    }
    public static async Task ValidateCredentialsAsync(string cs, string keyText, CancellationToken ct)
    {
        var protector = new CredentialProtector(Convert.FromBase64String(keyText.Trim()));
        await using var c = new NpgsqlConnection(cs); await c.OpenAsync(ct);
        await using var command = new NpgsqlCommand("SELECT \"SubsonicSecret\" FROM \"Users\"", c);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            try { _ = protector.Unprotect(reader.GetString(0)); }
            catch (Exception ex) when (ex is CryptographicException or FormatException) { throw new InvalidDataException("The credential key does not match the database. No usable backup/restore pair can be confirmed.", ex); }
    }
    public static async Task RestoreAsync(SonaraOptions options, string archive, string targetDatabase, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(targetDatabase, @"^[A-Za-z_][A-Za-z0-9_-]{0,62}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Specify --target-database using a simple database name (letters, digits, underscore or hyphen), with an existing empty database.");
        var data = Path.GetFullPath(options.DataDir); using var instance = InstanceLock.Acquire(data);
        var temp = Path.Combine(data, ".restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            var validated = await ValidateAsync(archive, temp, ct);
            var target = new NpgsqlConnectionStringBuilder(options.Database.ConnectionString) { Database = targetDatabase, Pooling = false };
            await using (var c = new NpgsqlConnection(target.ConnectionString))
            {
                await c.OpenAsync(ct);
                await using var tables = new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT LIKE 'pg_%' AND n.nspname <> 'information_schema' AND c.relkind IN ('r','p','v','m','S','f'))", c);
                if ((bool)(await tables.ExecuteScalarAsync(ct))!) throw new InvalidOperationException("Restore requires an empty target database. Existing data will not be overwritten.");
                await using var sessions = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid()", c);
                if ((long)(await sessions.ExecuteScalarAsync(ct))! != 0) throw new InvalidOperationException("The target database is in use. Stop its clients before restoring.");
            }
            var keyPath = Path.Combine(data, "secret.key");
            if (File.Exists(keyPath))
            {
                if (File.GetAttributes(keyPath).HasFlag(FileAttributes.ReadOnly)) throw new InvalidOperationException("The credential key is read-only. Make the stopped server's data directory writable before restoring.");
                var previous = Path.Combine(temp, "previous.key"); File.Copy(keyPath, previous); Private(previous);
                var preserved = Path.Combine(data, "secret.key.before-restore-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".bak");
                File.Copy(previous, preserved); Private(preserved);
            }
            await PgTools.RunAsync(options.Backups.PgRestorePath, target.ConnectionString,
                ["--dbname", targetDatabase, "--no-owner", "--no-privileges", "--single-transaction", "--exit-on-error", validated.DatabaseFile], ct);
            var text = await File.ReadAllTextAsync(validated.KeyFile, ct); await ValidateCredentialsAsync(target.ConnectionString, text, ct);
            File.Move(validated.KeyFile, keyPath, overwrite: true); Private(keyPath);
        }
        finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
