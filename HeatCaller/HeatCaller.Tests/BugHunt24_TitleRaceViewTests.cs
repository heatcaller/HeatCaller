namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the Championship page's "Can they still win?"
/// card showed a driver level on points with the leader, while rounds were
/// still to go, as "−0".
/// </summary>
public sealed class BugHunt24_TitleRaceViewTests
{
    [Fact]
    public void Level_with_the_leader_mid_season_reads_level_not_minus_zero()
    {
        var race = new TitleRace(3, 2, new List<(string Driver, int Points, bool CanWin)>
        {
            ("Alice", 120, true),
            ("Bob", 120, true),
            ("Cy", 90, true),
        });

        var rows = SeasonsControl.TitleRaceRows(race);

        Assert.Equal(2, rows[1].Place);
        Assert.Equal(0, rows[1].Gap);

        Assert.Equal("120", TitleRaceView.GapWords(rows[0]));
        Assert.Equal("level", TitleRaceView.GapWords(rows[1]));
        Assert.DoesNotContain("0", TitleRaceView.GapWords(rows[1]));
        Assert.Equal("−30", TitleRaceView.GapWords(rows[2]));
    }

    [Fact]
    public void A_driver_in_place_one_still_shows_points_even_with_a_gap_of_zero()
    {
        var row = new TitleRaceView.Row(1, "Annie", 80, 0, TitleRaceView.State.Level);

        Assert.Equal("80", TitleRaceView.GapWords(row));
    }
}
