namespace HeatCaller.Tests;

/// <summary>
/// RaceProgramControl.DealQualifyingHeats: the qualifying rounds of a
/// class must not run the same groups together every round, even when
/// the heats divide the class evenly.
/// </summary>
public class BugHuntQualifyingHeatsTests
{
    private static List<string> Roster(int count) =>
        Enumerable.Range(1, count).Select(i => $"T{i:00}").ToList();

    /// <summary>The groups of a round, ignoring heat numbers and grid order.</summary>
    private static string Groups(List<List<string>> heats) =>
        string.Join(" | ", heats.Select(h => string.Join(",", h.OrderBy(t => t, StringComparer.Ordinal)))
            .OrderBy(g => g, StringComparer.Ordinal));

    [Theory]
    [InlineData(16, 8, 3)]
    [InlineData(12, 6, 3)]
    [InlineData(8, 4, 3)]
    [InlineData(24, 8, 4)]
    [InlineData(20, 5, 4)]
    public void EvenlySplitClassesAreRegroupedEveryRound(int drivers, int heatSize, int rounds)
    {
        var roster = Roster(drivers);

        var groups = Enumerable.Range(0, rounds)
            .Select(r => Groups(RaceProgramControl.DealQualifyingHeats(roster, heatSize, r)))
            .ToList();

        Assert.Equal(rounds, groups.Distinct().Count());
    }

    [Theory]
    [InlineData(16, 8)]
    [InlineData(7, 4)]
    [InlineData(9, 8)]
    [InlineData(24, 8)]
    public void EveryDriverRacesOnceAndTheHeatsStayLevel(int drivers, int heatSize)
    {
        var roster = Roster(drivers);

        for (var round = 0; round < 4; round++)
        {
            var heats = RaceProgramControl.DealQualifyingHeats(roster, heatSize, round);

            Assert.Equal((int)Math.Ceiling(drivers / (double)heatSize), heats.Count);
            Assert.Equal(roster.OrderBy(t => t), heats.SelectMany(h => h).OrderBy(t => t));
            Assert.True(heats.Max(h => h.Count) - heats.Min(h => h.Count) <= 1);
        }
    }

    [Fact]
    public void FirstRoundIsTheStraightRoundRobinDeal()
    {
        var heats = RaceProgramControl.DealQualifyingHeats(Roster(16), 8, 0);

        Assert.Equal(new[] { "T01", "T03", "T05", "T07", "T09", "T11", "T13", "T15" }, heats[0]);
        Assert.Equal(new[] { "T02", "T04", "T06", "T08", "T10", "T12", "T14", "T16" }, heats[1]);
    }

    [Fact]
    public void UnevenClassesDealExactlyAsBefore()
    {
        // 7 at 4 a heat: Q2 starts 4 places round (E F G A B C D) and deals
        // round-robin - the same split the race-night rehearsal pins.
        var heats = RaceProgramControl.DealQualifyingHeats(Roster(7), 4, 1);

        Assert.Equal(new[] { "T05", "T07", "T02", "T04" }, heats[0]);
        Assert.Equal(new[] { "T06", "T01", "T03" }, heats[1]);
    }
}
