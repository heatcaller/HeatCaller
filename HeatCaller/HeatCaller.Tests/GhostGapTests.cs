namespace HeatCaller.Tests;

/// <summary>
/// The gap to a driver's own-best ghost, the ghost beside them on the
/// practice board, and the driver rating on the report card (22 Oct 2026).
/// </summary>
public sealed class GhostGapTests
{
    [Theory]
    // The ghost crossed 1.2s before the driver: it is 1.2s up the road.
    [InlineData(61_200, 60_000, 10_000, 1_200)]
    // It crossed 9.7s before on a 10s lap - it is about to cross behind them: 0.3s ahead.
    [InlineData(69_700, 60_000, 10_000, -300)]
    // A whole lap and a bit apart is still only the bit: the gap is on the track.
    [InlineData(71_000, 60_000, 10_000, 1_000)]
    // Crossing together.
    [InlineData(60_000, 60_000, 10_000, 0)]
    public void The_gap_is_where_the_ghost_is_on_the_track(long driverMs, long ghostMs, long ghostLapMs, long expected)
    {
        Assert.Equal(expected, OwnBestGhost.GapToGhostMs(driverMs, ghostMs, ghostLapMs));
    }

    [Fact]
    public void No_gap_without_a_ghost_lap()
    {
        Assert.Null(OwnBestGhost.GapToGhostMs(60_000, 60_000, 0));
        Assert.Null(OwnBestGhost.GapToGhostMs(60_000, 0, 10_000));
    }

    [Theory]
    [InlineData(200, "two tenths behind your ghost")]
    [InlineData(-100, "a tenth ahead of your ghost")]
    [InlineData(30, "level with your ghost")]
    public void The_gap_is_said_plainly(long gapMs, string expected)
    {
        Assert.Equal(expected, CoachingWords.GhostGapSpoken(gapMs));
    }

    [Fact]
    public void The_gap_line_is_on_in_practice_and_off_in_races()
    {
        var config = VoiceProfile.CreateDefault().Events[nameof(AnnouncementEventType.GhostGap)];

        Assert.True(config.IsEnabledFor(practice: true));
        Assert.False(config.IsEnabledFor(practice: false));
        Assert.All(config.EffectiveTemplates, line => Assert.Contains("{ghostgap}", line));
    }

    [Fact]
    public void Only_switched_on_own_best_ghosts_are_chasing_anybody()
    {
        var settings = new GhostSettings { Enabled = true };
        var (sams, _) = OwnBestGhost.ChaseFor(settings, "sam", "Sam", null);
        settings.Ghosts.Add(new GhostCar { PaceSource = GhostPaceSource.FixedLapTime });
        var (bills, _) = OwnBestGhost.ChaseFor(settings, "bill", "Bill", null);
        bills.Enabled = false;

        var chased = OwnBestGhost.ChasedBy(settings);

        Assert.Single(chased);
        Assert.Equal("sam", chased[sams.Transponder]);

        settings.Enabled = false;
        Assert.Empty(OwnBestGhost.ChasedBy(settings));
    }

    [Fact]
    public void Each_ghost_goes_just_under_the_driver_it_chases_and_nobody_else_moves()
    {
        var board = new[] { "ann", "G-SAM", "bob", "sam", "cat" };
        var chased = new Dictionary<string, string> { ["G-SAM"] = "sam", ["G-NOBODY"] = "zed" };

        Assert.Equal(new[] { "ann", "bob", "sam", "G-SAM", "cat" }, OwnBestGhost.BesideTheirDriver(board, s => s, chased));
    }

    [Fact]
    public void A_ghost_whose_driver_is_not_on_the_board_stays_put()
    {
        var board = new[] { "ann", "G-SAM", "bob" };
        var chased = new Dictionary<string, string> { ["G-SAM"] = "sam" };

        Assert.Equal(board, OwnBestGhost.BesideTheirDriver(board, s => s, chased));
    }

    private static SessionRecord Race(string label, params (string Who, int Laps, long Best)[] field) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = label,
        Results = field.Select((f, i) => new SessionResultEntry
        {
            Transponder = f.Who,
            DriverName = f.Who,
            Position = i + 1,
            Laps = f.Laps,
            FastLapMs = f.Best,
        }).ToList(),
    };

    [Fact]
    public void The_report_card_gives_the_driver_rating_for_one_race_and_the_average_for_several()
    {
        var heat = Race("Heat 1", ("sam", 20, 9000), ("bill", 19, 9100));
        var main = Race("A-Main", ("bill", 20, 9000), ("sam", 19, 9100));

        var one = CoachReport.RatingLine("sam", new[] { heat }, "tonight");
        Assert.NotNull(one);
        Assert.StartsWith("Driver rating tonight: ", one);
        Assert.EndsWith(" out of 150, in Heat 1.", one);

        var two = CoachReport.RatingLine("sam", new[] { heat, main }, "tonight");
        Assert.NotNull(two);
        Assert.Contains("on average over 2 races - best ", two);
        Assert.EndsWith(", in Heat 1.", two);

        var practice = Race("Practice", ("sam", 20, 9000));
        practice.SessionType = RaceSessionType.Practice;
        Assert.Null(CoachReport.RatingLine("sam", new[] { practice }, "tonight"));
    }
}
