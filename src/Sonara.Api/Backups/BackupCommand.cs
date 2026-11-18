using Sonara.Api.Options;

namespace Sonara.Api.Backups;

public static class BackupCommand
{
    public static bool IsCommand(string[] args) => args.Any(a => a is "--create-backup" or "--inspect-backup" or "--restore-backup");
    public static async Task<int> RunAsync(string[] args, SonaraOptions options)
    {
        string? Value(string key) { var i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null; }
        try
        {
            if (args.Count(a => a is "--create-backup" or "--inspect-backup" or "--restore-backup") != 1) throw new InvalidOperationException("Choose one backup command.");
            if (args.Contains("--create-backup"))
            {
                var file = await BackupArchive.CreateAsync(options, CancellationToken.None);
                Console.WriteLine("Created backup: " + Path.Combine(BackupArchive.Folder(options), file)); return 0;
            }
            if (args.Contains("--inspect-backup"))
            {
                var archive = Value("--inspect-backup") ?? throw new InvalidOperationException("Provide a backup file.");
                var temp = Path.Combine(Path.GetFullPath(options.DataDir), ".inspect-" + Guid.NewGuid().ToString("N"));
                try { var result = await BackupArchive.ValidateAsync(archive, temp, CancellationToken.None); Console.WriteLine($"Integrity valid; created {result.Manifest.CreatedUtc:O}; PostgreSQL {result.Manifest.PostgreSqlVersion}; database + credential key; no media files."); }
                finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
                return 0;
            }
            if (!args.Contains("--confirm-stopped")) throw new InvalidOperationException("Stop Sonara and supply --confirm-stopped for offline restore.");
            await BackupArchive.RestoreAsync(options, Value("--restore-backup") ?? throw new InvalidOperationException("Provide a backup file."),
                Value("--target-database") ?? throw new InvalidOperationException("Provide --target-database with an existing empty database."), CancellationToken.None);
            Console.WriteLine("Database and credential key restored. Configure Sonara to use the target database before starting it."); return 0;
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (!string.IsNullOrEmpty(options.Database.ConnectionString))
            {
                var password = new Npgsql.NpgsqlConnectionStringBuilder(options.Database.ConnectionString).Password;
                if (!string.IsNullOrEmpty(password)) message = message.Replace(password, "[redacted]", StringComparison.Ordinal);
            }
            Console.Error.WriteLine("Backup command failed: " + message); return 1;
        }
    }
}
