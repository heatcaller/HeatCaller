namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the badge catalogue: two badges were both
/// called "Machine" (clean-fifty and consistent) and two were both called
/// "Regular" (ten-races and twentyfive-races), so the announcer called the
/// same name twice for different badges. Names are now unique; ids, which
/// are what gets saved, are unchanged.
/// </summary>
public sealed class BugHunt24_AchievementModelsTests
{
    [Fact]
    public void Every_badge_name_is_used_by_only_one_badge()
    {
        var repeated = AchievementCatalogue.All
            .GroupBy(b => b.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key + ": " + string.Join(", ", g.Select(b => b.Id)))
            .ToList();

        Assert.Empty(repeated);
    }

    [Fact]
    public void The_renamed_badges_keep_their_ids_and_the_guides_names_stay_put()
    {
        AchievementDefinition ById(string id) => Assert.Single(AchievementCatalogue.All, b => b.Id == id);

        Assert.Equal("clean-fifty", AchievementCatalogue.CleanFifty);
        Assert.Equal("consistent", AchievementCatalogue.Consistent);
        Assert.Equal("ten-races", AchievementCatalogue.TenRaces);
        Assert.Equal("twentyfive-races", AchievementCatalogue.TwentyFiveRaces);

        // The user guide names these two, so they keep their names.
        Assert.Equal("Machine", ById(AchievementCatalogue.Consistent).Name);
        Assert.Equal("Regular", ById(AchievementCatalogue.TenRaces).Name);

        Assert.Equal("Untouchable", ById(AchievementCatalogue.CleanFifty).Name);
        Assert.Equal("Seasoned", ById(AchievementCatalogue.TwentyFiveRaces).Name);
    }
}
