using Sonara.Api.Subsonic;

namespace Sonara.Tests;

public class PlaylistRulesTests
{
    [Fact]
    public void Removal_indexes_refer_to_the_original_positions()
    {
        var result = PlaylistRules.Edit([10, 20, 30, 40], removeIndexes: [0, 2], add: []);
        Assert.Equal([20, 40], result);
    }

    [Fact]
    public void Added_tracks_are_appended_after_removals()
    {
        var result = PlaylistRules.Edit([1, 2, 3], removeIndexes: [1], add: [9, 8]);
        Assert.Equal([1, 3, 9, 8], result);
    }

    [Fact]
    public void Out_of_range_and_duplicate_indexes_are_ignored()
    {
        var result = PlaylistRules.Edit([1, 2], removeIndexes: [-1, 5, 0, 0], add: []);
        Assert.Equal([2], result);
    }

    [Fact]
    public void The_same_track_can_appear_twice()
    {
        Assert.Equal([1, 1], PlaylistRules.Edit([1], [], [1]));
    }

    [Theory]
    [InlineData(1, false, 1, false, true)]   // owner
    [InlineData(1, false, 2, false, false)]  // someone else, private
    [InlineData(1, true, 2, false, true)]    // someone else, public
    [InlineData(1, false, 2, true, true)]    // admin
    public void Viewing_rules(int owner, bool isPublic, int user, bool admin, bool expected) =>
        Assert.Equal(expected, PlaylistRules.CanView(owner, isPublic, user, admin));

    [Theory]
    [InlineData(1, 1, false, true)]
    [InlineData(1, 2, false, false)]
    [InlineData(1, 2, true, true)]
    public void Modify_rules(int owner, int user, bool admin, bool expected) =>
        Assert.Equal(expected, PlaylistRules.CanModify(owner, user, admin));
}
