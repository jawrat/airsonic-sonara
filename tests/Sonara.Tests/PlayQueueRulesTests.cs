using Sonara.Api.Subsonic;

namespace Sonara.Tests;

public class PlayQueueRulesTests
{
    [Fact]
    public void Current_track_in_the_queue_is_kept()
    {
        var (ids, current) = PlayQueueRules.Normalize([5, 6, 7], 6);
        Assert.Equal([5, 6, 7], ids);
        Assert.Equal(6, current);
    }

    [Fact]
    public void Current_track_missing_from_the_queue_is_dropped()
    {
        Assert.Null(PlayQueueRules.Normalize([5, 6], 99).Current);
        Assert.Null(PlayQueueRules.Normalize([5, 6], null).Current);
    }

    [Fact]
    public void Queue_length_is_capped()
    {
        var (ids, _) = PlayQueueRules.Normalize(Enumerable.Range(1, PlayQueueRules.MaxLength + 500), null);
        Assert.Equal(PlayQueueRules.MaxLength, ids.Count);
    }

    [Fact]
    public void Duplicates_and_order_are_preserved() =>
        Assert.Equal([3, 1, 3], PlayQueueRules.Normalize([3, 1, 3], 1).Ids);
}
