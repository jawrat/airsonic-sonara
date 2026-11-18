namespace Sonara.Api.Scanning;

/// <summary>
/// Calls <paramref name="action"/> once, <paramref name="quiet"/> after the last <see cref="Signal"/>.
/// A burst of file events (copying an album) therefore causes one scan, not hundreds.
/// </summary>
public sealed class ChangeDebouncer : IDisposable
{
    private readonly Timer _timer;
    private readonly TimeSpan _quiet;

    public ChangeDebouncer(TimeSpan quiet, Action action)
    {
        _quiet = quiet;
        _timer = new Timer(_ => action(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Signal() => _timer.Change(_quiet, Timeout.InfiniteTimeSpan);

    public void Dispose() => _timer.Dispose();
}
