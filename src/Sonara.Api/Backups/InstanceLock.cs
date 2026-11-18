namespace Sonara.Api.Backups;

/// <summary>Prevents a restore from replacing the credential key while this data directory is serving requests.</summary>
public static class InstanceLock
{
    public static IDisposable Acquire(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        try { return new FileStream(Path.Combine(dataDir, "sonara.instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new InvalidOperationException("This Sonara data directory is in use. Stop the application before restoring or starting another instance.", ex); }
    }
}
