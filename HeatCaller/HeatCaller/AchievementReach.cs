namespace HeatCaller;

/// <summary>
/// The counting badges a driver is closest to - "2 more podiums for Podium
/// Regular" - for their night page and, as bare counts, the club's badges
/// page.
/// </summary>
/// <remarks>
/// 22 Oct 2026 (driver experience, item 5). Built on the same steps the
/// badge window draws its progress bars from (AchievementProgress), so the
/// phone and the laptop can never disagree about how far along somebody is.
/// Only counting badges: one that either happens in a race or doesn't has
/// no "nearly". The counts behind it are already derivable from the public
/// results, so the counts (not the words) may go on the public badges page.
/// </remarks>
internal static class AchievementReach
{
    /// <summary>One badge within reach.</summary>
    /// <param name="Id">The badge's permanent id (AchievementCatalogue).</param>
    /// <param name="Name">Its name, as the badges page shows it.</param>
    /// <param name="Have">How many they have now - races, laps, podiums...</param>
    /// <param name="Need">How many the badge needs.</param>
    /// <param name="Noun">The noun for the number still to go: "podium" when one is left, "podiums" otherwise.</param>
    /// <param name="Words">The whole line: "2 more podiums for Podium Regular".</param>
    public sealed record Near(string Id, string Name, int Have, int Need, string Noun, string Words)
    {
        /// <summary>How many are still to go - never below one.</summary>
        public int Left => Need - Have;
    }

    /// <summary>
    /// At most <paramref name="most"/> badges this driver hasn't earned yet,
    /// nearest first: by the share still to go ((Need - Have) / Need), then by
    /// the count still to go, then in catalogue order. Empty when
    /// <paramref name="driver"/> is null or <paramref name="most"/> is zero
    /// or less.
    /// </summary>
    /// <param name="offeredAt">
    /// When given, only badges on offer there (AchievementCatalogue.IsOffered),
    /// so a club's drivers are never told how near they are to a home badge.
    /// </param>
    public static IReadOnlyList<Near> For(DriverAchievements? driver, int most, RacingSetting? offeredAt = null)
    {
        if (driver is null || most <= 0)
        {
            return Array.Empty<Near>();
        }

        var near = new List<(Near Near, int Order)>();
        var order = 0;

        foreach (var badge in AchievementCatalogue.All)
        {
            order++;

            if (badge is null
                || (offeredAt is { } setting && !AchievementCatalogue.IsOffered(badge, setting))
                || driver.Has(badge.Id)
                || AchievementProgress.For(badge.Id, driver) is not { } step)
            {
                continue;
            }

            var need = step.Target;
            var have = Math.Max(0, step.Current);

            // A count already past the target is one the next race files as
            // earned. Nothing to be "nearly" about.
            if (need <= 0 || have >= need)
            {
                continue;
            }

            var left = need - have;
            var noun = Plural.Word(left, Singular(step.Noun), step.Noun);

            near.Add((new Near(badge.Id, badge.Name, have, need, noun, $"{left} more {noun} for {badge.Name}"), order));
        }

        return near
            .OrderBy(n => (double)n.Near.Left / n.Near.Need)
            .ThenBy(n => n.Near.Left)
            .ThenBy(n => n.Order)
            .Take(most)
            .Select(n => n.Near)
            .ToList();
    }

    /// <summary>"podiums" to "podium", "track records" to "track record". Every noun AchievementProgress uses takes a plain s.</summary>
    private static string Singular(string plural) =>
        plural.EndsWith('s') && plural.Length > 1 ? plural[..^1] : plural;
}
