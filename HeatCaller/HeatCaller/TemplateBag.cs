namespace HeatCaller;

/// <summary>
/// Picks a voice line's template so the same one does not come round
/// again until every other one for that call has had its turn (16 Oct
/// 2026).
///
/// It used to be a plain random pick, and on 16 Oct the streak call said
/// "N in a row for X. Keep that rhythm." six times in one race out of its
/// three templates - random does that, and a room hears it as a loop. A
/// shuffled bag per list deals each template once in a random order, and
/// a fresh bag never starts with the one the last bag ended on, so no
/// template is ever heard twice running either.
///
/// Keyed by the list's contents rather than by which call it is, so an
/// edit to a call's templates on the Voice page simply starts a new bag.
/// </summary>
internal static class TemplateBag
{
    private static readonly object Gate = new();

    private static readonly Random Shuffle = new();

    private static readonly Dictionary<string, Queue<int>> Bags = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, int> Last = new(StringComparer.Ordinal);

    /// <summary>The next template from <paramref name="templates"/>. The list must not be empty.</summary>
    internal static string Pick(IReadOnlyList<string> templates)
    {
        if (templates.Count == 1)
        {
            return templates[0];
        }

        var key = string.Join("\u0001", templates);

        lock (Gate)
        {
            if (!Bags.TryGetValue(key, out var bag) || bag.Count == 0)
            {
                var order = Enumerable.Range(0, templates.Count).OrderBy(_ => Shuffle.Next()).ToList();

                // Never the last one heard straight away again.
                if (Last.TryGetValue(key, out var last) && order[0] == last)
                {
                    (order[0], order[^1]) = (order[^1], order[0]);
                }

                bag = new Queue<int>(order);
                Bags[key] = bag;

                // Lists edited away leave their bags behind; a few hundred
                // is a whole night of edits, so the lot is simply dropped.
                if (Bags.Count > 500)
                {
                    Bags.Clear();
                    Last.Clear();
                    Bags[key] = bag;
                }
            }

            var next = bag.Dequeue();
            Last[key] = next;

            return templates[next];
        }
    }
}
