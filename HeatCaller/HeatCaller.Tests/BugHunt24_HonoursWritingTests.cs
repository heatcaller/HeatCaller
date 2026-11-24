namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), F147: the AI achievements read's "first
/// achievement ever" and "club first" claims were held only to the facts of
/// the whole race - once one driver's block carried the fact, the read could
/// say it of anybody. Each claim is now tied to the driver or badge it is said
/// of, and that one must carry the fact itself.
/// </summary>
public sealed class BugHunt24_HonoursWritingTests
{
    private static AchievementDefinition Def(string name, AchievementTier tier = AchievementTier.Bronze) =>
        new($"test-{Guid.NewGuid():N}", name, $"Awarded for {name.ToLowerInvariant()}.", AchievementCategory.Race, "star", tier);

    private static AchievementUnlock Unlock(string driver, AchievementDefinition badge) => new(driver, driver, badge, string.Empty);

    private static readonly AchievementDefinition HatTrick = Def("Hat-Trick", AchievementTier.Gold);
    private static readonly AchievementDefinition PersonalBest = Def("Personal Best");

    private static IReadOnlyList<HonoursPick> JaneAndBilly() => RaceHonours.Picked(new[]
    {
        Unlock("Jane Doe", PersonalBest), Unlock("Billy Bob", HatTrick),
    });

    // Only Jane's block says it is her first achievement ever; Billy has ten.
    private const string FirstEverFacts =
        "Jane Doe earned one achievement in this race:\n- Personal Best. A bronze-tier single-race achievement.\n"
        + "This is Jane Doe's first achievement ever.\n"
        + "Billy Bob earned one achievement in this race:\n- Hat-Trick. A gold-tier single-race achievement.\n"
        + "Billy Bob now has ten achievements in all.";

    // Only Personal Best is a club first; three drivers hold Hat-Trick.
    private const string ClubFirstFacts =
        "Jane Doe earned one achievement in this race:\n- Personal Best. A bronze-tier single-race achievement. Nobody at the club had earned it before tonight.\n"
        + "Billy Bob earned one achievement in this race:\n- Hat-Trick. A gold-tier single-race achievement. three drivers at the club hold it now.";

    [Fact]
    public void BugHunt24_FirstEverClaim_AgainstTheWrongDriver_IsTurnedDown()
    {
        var notes = new List<string>();

        Assert.Null(HonoursWriting.Check(
            "Billy Bob takes Hat-Trick, his first ever achievement. Jane Doe picks up Personal Best.",
            JaneAndBilly(), null, FirstEverFacts, notes));
        Assert.Contains(notes, n => n.Contains("Billy Bob", StringComparison.Ordinal));
    }

    [Fact]
    public void BugHunt24_FirstEverClaim_AgainstTheRightDriver_Passes()
    {
        Assert.NotNull(HonoursWriting.Check(
            "Billy Bob takes Hat-Trick. Jane Doe picks up Personal Best, her first ever achievement.",
            JaneAndBilly(), null, FirstEverFacts));

        // Said before her name, it is about the next driver named.
        Assert.NotNull(HonoursWriting.Check(
            "A first achievement ever for Jane Doe, who picks up Personal Best. Billy Bob takes Hat-Trick.",
            JaneAndBilly(), null, FirstEverFacts));
    }

    [Fact]
    public void BugHunt24_ClubFirstClaim_AgainstTheWrongBadge_IsTurnedDown()
    {
        var notes = new List<string>();

        Assert.Null(HonoursWriting.Check(
            "Billy Bob takes Hat-Trick, a club first. Jane Doe takes Personal Best.",
            JaneAndBilly(), null, ClubFirstFacts, notes));
        Assert.Contains(notes, n => n.Contains("Hat-Trick", StringComparison.Ordinal));
    }

    [Fact]
    public void BugHunt24_ClubFirstClaim_AgainstTheRightBadge_Passes()
    {
        Assert.NotNull(HonoursWriting.Check(
            "Jane Doe takes Personal Best, the first at the club. Billy Bob takes Hat-Trick.",
            JaneAndBilly(), null, ClubFirstFacts));
    }

    [Fact]
    public void BugHunt24_BareFirstEver_OfABadgeThatIsNoClubFirst_IsTurnedDown()
    {
        // The old check let a bare "first ever" through whenever any badge in
        // the race was a club first.
        Assert.Null(HonoursWriting.Check(
            "Billy Bob takes Hat-Trick, a first ever for him. Jane Doe takes Personal Best.",
            JaneAndBilly(), null, ClubFirstFacts));
    }
}
