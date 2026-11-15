namespace HeatCaller;

/// <summary>
/// Plain words for the badges a young driver's family cheers (14 Nov 2026,
/// stream overlay plan 7.3, from a review written as a family watching from
/// home: "She won't win often yet. What we'd cheer: her best lap ever, her
/// first podium, a race with no crashes"). The badges' own names - "On the
/// Box", "New Heights" - mean nothing to somebody new to it, so the stream's
/// banner says what happened and keeps the badge's name under it.
/// </summary>
internal static class MilestoneWords
{
    private static readonly Dictionary<string, string> ById = new(StringComparer.Ordinal)
    {
        [AchievementCatalogue.FirstRace] = "First race",
        [AchievementCatalogue.FirstWin] = "First win",
        [AchievementCatalogue.FirstPodium] = "First podium",
        [AchievementCatalogue.RookieSensation] = "A win in their first five races",
        [AchievementCatalogue.PersonalBest] = "Best lap ever",
        [AchievementCatalogue.BigStep] = "Best lap ever, by a quarter of a second",
        [AchievementCatalogue.Flawless] = "A clean race",
    };

    /// <summary>The plain title for a badge by its name, or null for one that already says what it means.</summary>
    public static string? PlainTitle(string? badgeName)
    {
        var badge = AchievementCatalogue.All.FirstOrDefault(b => string.Equals(b.Name, badgeName?.Trim(), StringComparison.Ordinal));
        return badge is not null && ById.TryGetValue(badge.Id, out var words) ? words : null;
    }
}
