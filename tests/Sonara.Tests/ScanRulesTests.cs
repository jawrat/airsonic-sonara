using Sonara.Api.Scanning;

namespace Sonara.Tests;

public class ScanRulesTests
{
    private static readonly DateTime T = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unchanged_known_file_is_skipped() =>
        Assert.False(ScanRules.NeedsRead(false, true, 100, T, 100, T));

    [Fact]
    public void New_file_is_read() =>
        Assert.True(ScanRules.NeedsRead(false, false, 0, default, 100, T));

    [Fact]
    public void Changed_size_or_time_is_read()
    {
        Assert.True(ScanRules.NeedsRead(false, true, 100, T, 101, T));
        Assert.True(ScanRules.NeedsRead(false, true, 100, T, 100, T.AddSeconds(5)));
    }

    [Fact]
    public void Sub_second_timestamp_jitter_is_ignored() =>
        Assert.False(ScanRules.NeedsRead(false, true, 100, T, 100, T.AddMilliseconds(400)));

    [Fact]
    public void Full_scan_rereads_everything() =>
        Assert.True(ScanRules.NeedsRead(true, true, 100, T, 100, T));

    [Theory]
    [InlineData("/m/a.flac", true)]
    [InlineData("/m/a.MP3", true)]
    [InlineData("/m/cover.jpg", false)]
    [InlineData("/m/notes.txt", false)]
    public void Audio_detection(string path, bool expected) =>
        Assert.Equal(expected, ScanRules.IsAudioFile(path));

    [Theory]
    [InlineData("/m/a.flac", true)]
    [InlineData("/m/New Album", true)]   // directory
    [InlineData("/m/cover.jpg", false)]
    [InlineData("/m/.a.flac.part", false)]
    [InlineData(null, true)]
    public void Watcher_relevance(string? path, bool expected) =>
        Assert.Equal(expected, ScanRules.IsRelevantChange(path));

    [Theory]
    [InlineData(4, 4)]
    [InlineData(1000, 64)]
    public void Explicit_parallelism_is_honoured_up_to_a_cap(int configured, int expected) =>
        Assert.Equal(expected, ScanRules.EffectiveParallelism(configured));

    [Fact]
    public void Default_parallelism_is_between_one_and_eight()
    {
        var n = ScanRules.EffectiveParallelism(0);
        Assert.InRange(n, 1, 8);
    }

    [Fact]
    public async Task A_burst_of_signals_fires_once()
    {
        var fired = 0;
        using var d = new ChangeDebouncer(TimeSpan.FromMilliseconds(100), () => Interlocked.Increment(ref fired));

        for (var i = 0; i < 5; i++)
        {
            d.Signal();
            await Task.Delay(20);
        }
        Assert.Equal(0, Volatile.Read(ref fired)); // still inside the quiet period

        await Task.Delay(600);
        Assert.Equal(1, Volatile.Read(ref fired));
    }
}
