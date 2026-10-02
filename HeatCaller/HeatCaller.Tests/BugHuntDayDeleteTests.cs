namespace HeatCaller.Tests;

/// <summary>
/// Delete Day and the round on track (2 Oct bug hunt). Deleting a single
/// round from the programme refuses the one out on track, and so does the
/// phone, but deleting its whole day used to take it anyway - and the
/// race's result was then saved against a round and a day that no longer
/// existed. RaceDayDeleter now finds the day holding that round first.
/// </summary>
public sealed class BugHuntDayDeleteTests
{
    private static RaceEvent Day(string name, params string[] roundIds) =>
        new() { Name = name, RoundIds = roundIds.ToList() };

    [Fact]
    public void The_day_holding_the_round_on_track_is_found()
    {
        var other = Day("Last week", "r1", "r2");
        var tonight = Day("Tonight", "r3", "a-main");

        var busy = RaceDayDeleter.DayWithRoundOnTrack(new[] { other, tonight }, "a-main");

        Assert.Same(tonight, busy);
    }

    [Fact]
    public void Nothing_on_track_refuses_nothing()
    {
        var tonight = Day("Tonight", "r3", "a-main");

        Assert.Null(RaceDayDeleter.DayWithRoundOnTrack(new[] { tonight }, null));
        Assert.Null(RaceDayDeleter.DayWithRoundOnTrack(new[] { tonight }, string.Empty));
    }

    [Fact]
    public void A_round_on_track_on_another_day_does_not_block_this_one()
    {
        var lastWeek = Day("Last week", "r1", "r2");

        Assert.Null(RaceDayDeleter.DayWithRoundOnTrack(new[] { lastWeek }, "a-main"));
    }
}
