namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the live Gained column.
///
/// It took the raw grid slot less the row on the board. A stated roster is
/// never renumbered, so behind a no-show every car read "+1" having passed
/// nobody, and a ghost at the top of the board made the pole-sitter read
/// "-1". Both places are now counted among the real cars with a lap.
/// </summary>
public sealed class BugHunt24_LeaderboardKpiTests
{
    private sealed class NoDrivers : IDriverLookup
    {
        public DriverProfile? Get(string transponder) => null;
    }

    private static RacerStanding Car(string id, int startPosition, int laps = 3) =>
        new() { TransponderId = id, StartPosition = startPosition, Laps = laps };

    private static string[] Column(params RacerStanding[] board)
    {
        var context = new KpiContext
        {
            Ordered = board,
            Profiles = new NoDrivers(),
            Tracker = new KpiTracker(),
        };

        var gained = KpiCatalog.Find("gained")!;
        return board.Select((s, i) => gained.Compute(s, i, context).Text).ToArray();
    }

    [Fact]
    public void Gained_NoShowOnStatedRoster_IsNotAPlaceMadeUp()
    {
        // Roster of six, slot 3 never turned up; everyone holds station.
        var cells = Column(Car("1", 1), Car("2", 2), Car("4", 4), Car("5", 5), Car("6", 6));

        Assert.Equal(new[] { "=", "=", "=", "=", "=" }, cells);
    }

    [Fact]
    public void Gained_NoShowOnStatedRoster_RealPassesStillCount()
    {
        // Slot 5 has passed slot 4: one place each way among the real cars.
        var cells = Column(Car("1", 1), Car("2", 2), Car("5", 5), Car("4", 4), Car("6", 6));

        Assert.Equal(new[] { "=", "=", "+1", "-1", "=" }, cells);
    }

    [Fact]
    public void Gained_GhostAheadOfTheField_DoesNotCostThePoleSitterAPlace()
    {
        var cells = Column(Car(Ghosts.Prefix + "1", 0), Car("1", 1), Car("2", 2));

        Assert.Equal(new[] { "-", "=", "=" }, cells);
    }

    [Fact]
    public void Gained_GhostOnTheGrid_IsNotAPlaceGainedByThoseBehindIt()
    {
        // A ghost given grid slot 1 but running last.
        var cells = Column(Car("1", 2), Car("2", 3), Car(Ghosts.Prefix + "1", 1));

        Assert.Equal(new[] { "=", "=", "-" }, cells);
    }

    [Fact]
    public void Gained_CarWithNoLapOrUnknownGrid_ShowsADash()
    {
        var cells = Column(Car("1", 1), Car("2", 0), Car("3", 3, laps: 0));

        Assert.Equal(new[] { "=", "-", "-" }, cells);
    }
}
