namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the driver card. A driver in a championship
/// on carried-in points only had no championship line on their card, and
/// the card counted and listed setups by typed name only while the Setups
/// tab beside it also takes those linked to the driver's cars.
/// </summary>
public sealed class BugHunt24_DriverStatsTests
{
    private static readonly DateTime Day = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);

    private static DriverStatsQuery Query(string name) => new()
    {
        Transponder = "T3",
        Transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T3" },
        DriverName = name,
    };

    private static IReadOnlyList<SeasonStandingEntry> WinterTable() => new List<SeasonStandingEntry>
    {
        new() { DriverName = "Alex", TotalPoints = 200, Wins = 3 },
        new() { DriverName = "Bo", TotalPoints = 150, Wins = 1 },
        new() { DriverName = "Sam Lee", TotalPoints = 120, CarriedIn = 120 },
        new() { DriverName = "Cy", TotalPoints = 90 },
    };

    private static DriverStatsSnapshot Snapshot(
        IReadOnlyList<SessionRecord>? sessions = null,
        IReadOnlyList<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>? championships = null,
        IReadOnlyList<SetupEntry>? setups = null,
        IReadOnlyList<(string CarId, string Owner)>? carOwners = null) => new()
    {
        Sessions = sessions ?? Array.Empty<SessionRecord>(),
        RoundClasses = new Dictionary<string, string>(),
        Rivalries = Array.Empty<(string A, string B)>(),
        Championships = championships ?? Array.Empty<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>(),
        Setups = setups ?? Array.Empty<SetupEntry>(),
        CarOwners = carOwners ?? Array.Empty<(string CarId, string Owner)>(),
    };

    // ------------------------------------------------------------------
    //  #171: carried-in points alone still give a championship line
    // ------------------------------------------------------------------

    [Fact]
    public void A_driver_in_the_table_on_carried_points_alone_has_a_championship_line()
    {
        var stats = DriverStats.Compute(
            Query("sam lee"),
            Snapshot(championships: new[] { ("Winter Series", WinterTable()) }));

        var line = Assert.Single(stats.Championships);
        Assert.Equal("Winter Series", line.Season);
        Assert.Equal(3, line.Position);
        Assert.Equal(4, line.FieldSize);
        Assert.Equal(120, line.Points);
    }

    [Fact]
    public void A_driver_whose_only_saved_session_has_no_name_still_finds_their_carried_points_row()
    {
        // Timed in practice before their profile existed: the result is
        // filed under the transponder, not the name the points were typed in.
        var practice = new SessionRecord
        {
            SessionType = RaceSessionType.Practice,
            Label = "Practice",
            TrackLayoutName = "Main",
            StartedAtUtc = Day,
            EndedAtUtc = Day.AddMinutes(5),
            Results = new List<SessionResultEntry>
            {
                new() { Transponder = "T3", DriverName = string.Empty, Position = 1, Laps = 8, FastLapMs = 9900 },
            },
        };

        var stats = DriverStats.Compute(
            Query("Sam Lee"),
            Snapshot(sessions: new[] { practice }, championships: new[] { ("Winter Series", WinterTable()) }));

        Assert.Equal(1, stats.PracticeSessions);
        var line = Assert.Single(stats.Championships);
        Assert.Equal(3, line.Position);
        Assert.Equal(120, line.Points);
    }

    [Fact]
    public void A_driver_with_no_name_and_no_results_still_has_no_championship_line()
    {
        var stats = DriverStats.Compute(
            Query(string.Empty),
            Snapshot(championships: new[] { ("Winter Series", WinterTable()) }));

        Assert.Empty(stats.Championships);
    }

    // ------------------------------------------------------------------
    //  #172: setups on the driver's cars count, whatever name they carry
    // ------------------------------------------------------------------

    [Fact]
    public void A_setup_on_the_drivers_car_filed_under_another_name_is_theirs()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { "C1" };
        var onTheirCar = new SetupEntry { CarId = "C1", DriverName = "Dave" };

        Assert.True(DriverStats.IsDriversSetup(onTheirCar, "David Smith", ids));
    }

    [Fact]
    public void The_setup_count_takes_the_drivers_cars_and_their_name_as_the_Setups_tab_does()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { "C1" };

        var setups = new[]
        {
            new SetupEntry { CarId = "C1", DriverName = "Dave" },          // their car, old name
            new SetupEntry { CarId = null, DriverName = " david smith " }, // their name, no car
            new SetupEntry { CarId = "C9", DriverName = "David Smith" },   // their name, a car passed on
            new SetupEntry { CarId = "C9", DriverName = "Bo" },            // somebody else's
        };

        Assert.Equal(3, DriverStats.CountDriversSetups(setups, "David Smith", ids));
    }

    [Fact]
    public void A_driver_with_no_name_still_counts_the_setups_on_their_cars()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { "C1" };

        var setups = new[]
        {
            new SetupEntry { CarId = "C1", DriverName = "Dave" },
            new SetupEntry { CarId = null, DriverName = string.Empty },
        };

        Assert.Equal(1, DriverStats.CountDriversSetups(setups, string.Empty, ids));
    }

    [Fact]
    public void The_cards_recent_activity_lists_a_setup_on_their_car_filed_under_another_name()
    {
        var setup = new SetupEntry { CarId = "C1", DriverName = "Dave", CarModel = "B7", RecordedAtUtc = Day };

        var stats = DriverStats.Compute(
            Query("David Smith"),
            Snapshot(setups: new[] { setup }, carOwners: new[] { ("C1", "t3") }));

        var line = Assert.Single(stats.RecentActivity);
        Assert.Equal(ActivityKind.Setup, line.Kind);
        Assert.StartsWith("Setup on B7", line.Text);
    }

    [Fact]
    public void The_cards_recent_activity_leaves_out_a_setup_on_somebody_elses_car()
    {
        var setup = new SetupEntry { CarId = "C2", DriverName = "Bo", CarModel = "B7", RecordedAtUtc = Day };

        var stats = DriverStats.Compute(
            Query("David Smith"),
            Snapshot(setups: new[] { setup }, carOwners: new[] { ("C1", "T3"), ("C2", "T8") }));

        Assert.Empty(stats.RecentActivity);
    }
}
