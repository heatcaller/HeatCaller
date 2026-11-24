namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the achievements read (RaceHonours.Picked).
/// </summary>
public sealed class BugHunt24_RaceHonoursTests
{
    private static AchievementDefinition Def(string name) =>
        new($"test-{Guid.NewGuid():N}", name, $"Awarded for {name.ToLowerInvariant()}.", AchievementCategory.Race, "star");

    private static readonly AchievementDefinition WeekendWarrior = Def("Weekend Warrior");
    private static readonly AchievementDefinition NailBiter = Def("Nail-Biter");

    // F252: two different drivers who share a name were one pick.

    [Fact]
    public void Picked_TwoDriversWithTheSameName_AreTwoPicks()
    {
        var picks = RaceHonours.Picked(new[]
        {
            new AchievementUnlock("T1", "Sam Smith", WeekendWarrior, string.Empty),
            new AchievementUnlock("T2", "sam smith", WeekendWarrior, string.Empty),
            new AchievementUnlock("T2", "sam smith", NailBiter, string.Empty),
        });

        Assert.Equal(2, picks.Count);

        Assert.Equal("T1", picks[0].Transponder);
        Assert.Equal(new[] { "Weekend Warrior" }, picks[0].Badges);
        Assert.All(picks[0].Unlocks, u => Assert.Equal("T1", u.Transponder));

        // The badge both earned is no longer dropped as a repeat, and the
        // second driver's badges carry the second driver's transponder.
        Assert.Equal("T2", picks[1].Transponder);
        Assert.Equal(new[] { "Weekend Warrior", "Nail-Biter" }, picks[1].Badges);
        Assert.All(picks[1].Unlocks, u => Assert.Equal("T2", u.Transponder));
    }

    [Fact]
    public void Picked_OneDriversUnlocks_AreStillOnePick_UnderTheNameAsSaid()
    {
        var picks = RaceHonours.Picked(new[]
        {
            new AchievementUnlock("T1", "Sam Smith", WeekendWarrior, string.Empty),
            new AchievementUnlock("T1", "Sam Smith", NailBiter, string.Empty),
        });

        var pick = Assert.Single(picks);
        Assert.Equal("Sam Smith", pick.Driver);
        Assert.Equal(new[] { "Weekend Warrior", "Nail-Biter" }, pick.Badges);
    }

    [Fact]
    public void Read_TwoDriversWithTheSameName_NamesBothAndEachOfTheirBadges()
    {
        var read = RaceHonours.Read(new[]
        {
            new AchievementUnlock("T1", "Sam Smith", WeekendWarrior, string.Empty),
            new AchievementUnlock("T2", "Sam Smith", WeekendWarrior, string.Empty),
        });

        Assert.NotNull(read);
        Assert.Equal("Sam Smith picks up Weekend Warrior. Sam Smith takes Weekend Warrior.", read!.Value.Said);
        Assert.Equal(1, read.Value.Badges);
    }
}
