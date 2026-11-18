using System.Diagnostics;
using Npgsql;
using Sonara.Api.Options;

namespace Sonara.Api.Backups;

public static class PgTools
{
    public static ProcessStartInfo StartInfo(string executable, string connectionString, IEnumerable<string> args)
    {
        var c = new NpgsqlConnectionStringBuilder(connectionString);
        var p = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        foreach (var arg in args) p.ArgumentList.Add(arg);
        foreach (var key in new[] { "PGHOSTADDR", "PGSERVICE", "PGSERVICEFILE", "PGPASSFILE", "PGOPTIONS" }) p.Environment.Remove(key);
        // Connection credentials never appear in command arguments or the manifest.
        p.Environment["PGHOST"] = c.Host ?? "localhost"; p.Environment["PGPORT"] = c.Port.ToString();
        p.Environment["PGDATABASE"] = c.Database ?? ""; p.Environment["PGUSER"] = c.Username ?? "";
        p.Environment["PGPASSWORD"] = c.Password ?? ""; p.Environment["PGCONNECT_TIMEOUT"] = Math.Max(1, c.Timeout).ToString();
        p.Environment["PGSSLMODE"] = c.SslMode.ToString().ToLowerInvariant().Replace("verifyca", "verify-ca").Replace("verifyfull", "verify-full");
        if (!string.IsNullOrEmpty(c.RootCertificate)) p.Environment["PGSSLROOTCERT"] = c.RootCertificate;
        return p;
    }
    public static async Task RunAsync(string executable, string connectionString, IEnumerable<string> args, CancellationToken ct)
    {
        using var p = new Process { StartInfo = StartInfo(executable, connectionString, args) };
        try { p.Start(); }
        catch (System.ComponentModel.Win32Exception ex) { throw new InvalidOperationException($"Cannot start {Path.GetFileName(executable)}. Install matching PostgreSQL client tools, or configure their paths.", ex); }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromHours(2));
        using var abort = timeout.Token.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch (InvalidOperationException) { } });
        var errors = p.StandardError.ReadToEndAsync(timeout.Token); var output = p.StandardOutput.ReadToEndAsync(timeout.Token);
        await p.WaitForExitAsync(timeout.Token); await output; var error = await errors;
        if (p.ExitCode != 0)
        {
            var password = new NpgsqlConnectionStringBuilder(connectionString).Password;
            if (!string.IsNullOrEmpty(password)) error = error.Replace(password, "[redacted]", StringComparison.Ordinal);
            throw new InvalidOperationException($"{Path.GetFileName(executable)} failed (exit {p.ExitCode}): {error[..Math.Min(error.Length, 1200)]}");
        }
    }
}
