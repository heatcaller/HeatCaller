namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), F087: "first ever" from a history too young
/// to know.
///
/// On a club's first night or two with the app, nearly every driver has a
/// zero on file - so the intro announced long-time members as "making their
/// first ever start at this club" and the recap called every winner a
/// first-time winner and every P2 and P3 a first podium. The sign-off's own
/// debut count already waits for CommentaryShowFacts.LeastDaysOnFile nights;
/// these lines now wait for the same bar when the caller says how many
/// nights are on file.
/// </summary>
public sealed class BugHunt24_CommentaryHistoryFactsTests
{
    private static DriverForm Form(
        string id,
        string name,
        int races,
        int wins,
        int podiums,
        int[]? recent = null) =>
        new(id, name, races, wins, podiums, recent);

    private static IReadOnlyList<(string DriverId, int Position)> Finished(params (string, int)[] order) =>
        order.Select(o => (DriverId: o.Item1, Position: o.Item2)).ToList();

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(40, true)]
    public void BugHunt24_The_history_knows_a_first_only_from_four_nights_on_file(int? nights, bool knows)
    {
        Assert.Equal(knows, CommentaryHistoryFacts.KnowsFirsts(nights));
    }

    [Fact]
    public void BugHunt24_A_young_history_does_not_call_a_long_time_member_a_debutant()
    {
        var grid = new[] { Form("bill", "Bill", races: 0, wins: 0, podiums: 0) };

        var facts = CommentaryHistoryFacts.ForIntro(
            grid, Array.Empty<HeadToHead>(), "heat-2", "Box Stock", nightKey: "night-1", nightsOnFile: 1);

        Assert.DoesNotContain(facts, f => f.Text.Contains("first ever", StringComparison.Ordinal));
    }

    [Fact]
    public void BugHunt24_A_young_history_still_speaks_a_run_of_form()
    {
        var grid = new[]
        {
            Form("bill", "Bill", races: 0, wins: 0, podiums: 0),
            Form("sam", "Sam", races: 3, wins: 3, podiums: 3, recent: new[] { 1, 1, 1 }),
        };

        var facts = CommentaryHistoryFacts.ForIntro(
            grid, Array.Empty<HeadToHead>(), "heat-4", "Box Stock", nightsOnFile: 1);

        var only = Assert.Single(facts);
        Assert.Contains("Sam has won the last three Box Stock races here", only.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(4)]
    public void BugHunt24_A_true_debut_is_still_said_once_the_history_can_know(int? nights)
    {
        var grid = new[] { Form("bill", "Bill", races: 0, wins: 0, podiums: 0) };

        var facts = CommentaryHistoryFacts.ForIntro(
            grid, Array.Empty<HeadToHead>(), "heat-2", "Box Stock", nightsOnFile: nights);

        Assert.Contains(facts, f => f.Text == "Bill is making their first ever Box Stock start at this club.");
    }

    [Fact]
    public void BugHunt24_A_young_history_does_not_call_a_win_or_a_podium_a_first()
    {
        var grid = new[]
        {
            Form("sam", "Sam", races: 1, wins: 0, podiums: 0),
            Form("bill", "Bill", races: 1, wins: 0, podiums: 0),
            Form("pat", "Pat", races: 1, wins: 0, podiums: 0),
        };

        var facts = CommentaryHistoryFacts.ForRecap(
            grid, Finished(("sam", 1), ("bill", 2), ("pat", 3)), "heat-2", "Box Stock", nightsOnFile: 1);

        Assert.DoesNotContain(facts, f => f.Text.Contains("first ever", StringComparison.Ordinal));
        Assert.Null(CommentaryHistoryFacts.ResultInContext(grid[0], 1, "heat-2", "Box Stock", nightsOnFile: 2));
        Assert.Null(CommentaryHistoryFacts.ResultInContext(grid[1], 2, "heat-2", "Box Stock", nightsOnFile: 3));
    }

    [Fact]
    public void BugHunt24_A_young_history_still_counts_a_win_it_has_on_file()
    {
        var grid = new[] { Form("sam", "Sam", races: 2, wins: 1, podiums: 2) };

        var block = Assert.Single(CommentaryHistoryFacts.ForRecap(
            grid, Finished(("sam", 1)), "heat-3", "Box Stock", nightsOnFile: 1));

        Assert.Equal("Sam's 2nd Box Stock win here.", block.Text);
    }

    [Fact]
    public void BugHunt24_With_four_nights_on_file_a_first_win_and_a_first_podium_are_still_said()
    {
        var grid = new[]
        {
            Form("sam", "Sam", races: 1, wins: 0, podiums: 0),
            Form("bill", "Bill", races: 1, wins: 0, podiums: 0),
        };

        var facts = CommentaryHistoryFacts.ForRecap(
            grid, Finished(("sam", 1), ("bill", 2)), "heat-2", "Box Stock", nightsOnFile: 4);

        Assert.Contains(facts, f => f.Text == "That is Sam's first ever Box Stock win here.");
        Assert.Contains(facts, f => f.Text == "Bill's first ever Box Stock podium here.");
    }

    [Fact]
    public void BugHunt24_A_race_form_carries_its_nights_on_file_and_defaults_to_not_counted()
    {
        var plain = new RaceForm(Array.Empty<DriverForm>(), Array.Empty<HeadToHead>(), "Box Stock", "night-1");
        var counted = plain with { NightsOnFile = 2 };

        Assert.Null(plain.NightsOnFile);
        Assert.Equal(2, counted.NightsOnFile);
    }
}
