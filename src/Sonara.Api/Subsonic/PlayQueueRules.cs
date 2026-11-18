namespace Sonara.Api.Subsonic;

public static class PlayQueueRules
{
    public const int MaxLength = 2000;

    /// <summary>
    /// Caps the queue length and drops a "current" track that is not in the queue (the client would
    /// otherwise resume on something it can't find).
    /// </summary>
    public static (List<int> Ids, int? Current) Normalize(IEnumerable<int> ids, int? current)
    {
        var list = ids.Take(MaxLength).ToList();
        return (list, current is { } c && list.Contains(c) ? c : null);
    }
}
