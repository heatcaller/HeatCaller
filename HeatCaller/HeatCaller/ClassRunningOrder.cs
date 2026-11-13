namespace HeatCaller;

/// <summary>
/// The order tonight's classes run in within each round (12 Nov 2026, setup
/// plan D7).
///
/// WHY: which class goes first decides who marshals for whom, how long a
/// driver in two classes gets between their races, and when the juniors
/// finish (parents want them done early). Every round was built in the
/// Classes page's order, so the only way to change it was to drag heats about
/// after every build. The race day now says the order, and can turn it round
/// each round so the same class isn't always first out on a green track.
/// The rest guard (RestOrder) still runs after it, and only moves a heat to
/// stop a driver racing twice in a row.
/// </summary>
public static class ClassRunningOrder
{
    /// <summary>
    /// The classes in tonight's running order for one round: those named in
    /// <paramref name="order"/> first, in its order, then any others in the
    /// order given. With <paramref name="rotate"/>, round 2 starts with the
    /// second class, round 3 with the third, and so on.
    /// </summary>
    /// <param name="round">The round, from 0 for Q1.</param>
    public static List<T> Arrange<T>(IReadOnlyList<T> classes, Func<T, string> nameOf, IReadOnlyList<string>? order, bool rotate, int round)
    {
        var placed = (order ?? Array.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToList();

        int Place(T c)
        {
            var at = placed.FindIndex(n => string.Equals(n, nameOf(c)?.Trim(), StringComparison.OrdinalIgnoreCase));
            return at < 0 ? int.MaxValue : at;
        }

        // A stable sort: classes not in the order keep their own order.
        var arranged = classes
            .Select((c, i) => (c, i))
            .OrderBy(x => Place(x.c))
            .ThenBy(x => x.i)
            .Select(x => x.c)
            .ToList();

        if (!rotate || arranged.Count < 2)
        {
            return arranged;
        }

        var shift = ((round % arranged.Count) + arranged.Count) % arranged.Count;

        return arranged.Skip(shift).Concat(arranged.Take(shift)).ToList();
    }

    /// <summary>
    /// Tonight's order on one line for the Setup page: "Juniors, Stock, Mod",
    /// with "(turned round each round)" when it rotates. "As on the Classes
    /// page" when no order is set.
    /// </summary>
    public static string Summary(IReadOnlyList<string> tonight, IReadOnlyList<string>? order, bool rotate)
    {
        if ((order?.Count ?? 0) == 0 && !rotate)
        {
            return "As on the Classes page";
        }

        var names = Arrange(tonight, n => n, order, rotate: false, round: 0);
        var line = names.Count == 0 ? "As on the Classes page" : string.Join(", ", names);

        return rotate ? line + " (turned round each round)" : line;
    }
}
