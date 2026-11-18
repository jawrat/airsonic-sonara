namespace Sonara.Api.Subsonic;

/// <summary>Pure list manipulation for playlists, kept separate so it can be unit tested.</summary>
public static class PlaylistRules
{
    /// <summary>
    /// Removes the entries at the given zero-based positions (out-of-range and duplicate indexes are
    /// ignored), then appends the new tracks. Positions are relative to the list before any change.
    /// </summary>
    public static List<int> Edit(IReadOnlyList<int> current, IEnumerable<int> removeIndexes, IEnumerable<int> add)
    {
        var drop = removeIndexes.Where(i => i >= 0 && i < current.Count).ToHashSet();
        var result = current.Where((_, i) => !drop.Contains(i)).ToList();
        result.AddRange(add);
        return result;
    }

    public static bool CanView(int ownerId, bool isPublic, int userId, bool isAdmin) =>
        isAdmin || isPublic || ownerId == userId;

    public static bool CanModify(int ownerId, int userId, bool isAdmin) => isAdmin || ownerId == userId;
}
