namespace Sonara.Api.Podcasts;

/// <summary>Coordinates file removal and download-state transitions in one server process. Network transfers do not hold this gate.</summary>
public sealed class PodcastFileGate
{
    private readonly SemaphoreSlim _gate = new(1);
    public async Task<IDisposable> EnterAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        return new Lease(_gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release(); }
    }
}
