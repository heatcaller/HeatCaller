namespace HeatCaller.Tests;

/// <summary>
/// Driver experience L (22 Oct 2026, item 3): when a race still to run is
/// expected to start - the run sheet's time plus the day's lateness, or
/// tonight's race cycle, or nothing.
/// </summary>
public sealed class DriverExp_L_RaceEtaTests
{
    /// <summary>Tonight at a local time, as UTC - the way the laptop's clock reads it.</summary>
    private static DateTime Local(int hour, int minute, int day = 22) =>
        new DateTime(2026, 10, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();

    private static ScheduleEntry Line(string roundId, string time) =>
        new() { RoundId = roundId, ScheduledTime = time };

    [Fact]
    public void A_run_sheet_time_plus_the_minutes_late_is_the_eta()
    {
        var eta = RaceEta.For("r2", away: 1, raceOnTrack: false,
            new[] { Line("r1", "19:30"), Line("r2", "19:40") }, minutesLate: 5, startsUtc: null, nowUtc: Local(19, 30));

        Assert.Equal(Local(19, 45), eta);
    }

    [Fact]
    public void A_day_running_early_brings_the_eta_forward()
    {
        var eta = RaceEta.For("r2", 1, false, new[] { Line("r2", "19:40") }, minutesLate: -4, null, Local(19, 30));

        Assert.Equal(Local(19, 36), eta);
    }

    [Fact]
    public void A_time_already_gone_by_falls_back_to_the_cycle()
    {
        // Tonight's starts eight minutes apart; two races to go before it.
        var starts = new[] { Local(19, 0), Local(19, 8), Local(19, 16) };

        var eta = RaceEta.For("r2", away: 2, raceOnTrack: false,
            new[] { Line("r2", "19:00") }, minutesLate: 0, starts, Local(19, 30));

        Assert.Equal(Local(19, 46), eta);
    }

    [Fact]
    public void A_time_gone_by_with_no_cycle_says_nothing()
    {
        var eta = RaceEta.For("r2", 2, false, new[] { Line("r2", "19:00") }, 0, new[] { Local(19, 0) }, Local(19, 30));

        Assert.Null(eta);
    }

    [Fact]
    public void Without_times_it_is_the_median_cycle_plus_half_while_a_race_runs()
    {
        // Gaps of 6, 10 and 7 minutes: the median is 7. One race before
        // theirs, and one on track now that is (on average) half done.
        var starts = new[] { Local(19, 0), Local(19, 6), Local(19, 16), Local(19, 23) };

        var running = RaceEta.For("r9", away: 1, raceOnTrack: true, Array.Empty<ScheduleEntry>(), null, starts, Local(19, 25));
        var between = RaceEta.For("r9", away: 1, raceOnTrack: false, Array.Empty<ScheduleEntry>(), null, starts, Local(19, 25));

        Assert.Equal(Local(19, 25) + TimeSpan.FromMinutes(10.5), running);
        Assert.Equal(Local(19, 32), between);
    }

    [Fact]
    public void A_time_that_does_not_read_is_treated_as_no_time()
    {
        var starts = new[] { Local(19, 0), Local(19, 5), Local(19, 10) };

        var eta = RaceEta.For("r2", 1, false, new[] { Line("r2", "TBD") }, 10, starts, Local(19, 12));

        Assert.Equal(Local(19, 17), eta);
    }

    [Fact]
    public void One_start_or_one_gap_is_not_a_cycle()
    {
        Assert.Null(RaceEta.For("r2", 1, false, null, null, new[] { Local(19, 0) }, Local(19, 5)));
        Assert.Null(RaceEta.For("r2", 1, false, null, null, new[] { Local(19, 0), Local(19, 7) }, Local(19, 10)));
        Assert.Null(RaceEta.For("r2", 1, false, null, null, null, Local(19, 10)));
    }

    [Fact]
    public void The_cycle_is_clamped_to_three_and_twenty_minutes()
    {
        var quick = new[] { Local(19, 0), Local(19, 1), Local(19, 2) };
        var slow = new[] { Local(18, 0), Local(18, 40), Local(19, 25) };

        Assert.Equal(TimeSpan.FromMinutes(3), RaceEta.Cycle(quick, Local(19, 5)));
        Assert.Equal(TimeSpan.FromMinutes(20), RaceEta.Cycle(slow, Local(19, 30)));
    }

    [Fact]
    public void Starts_from_an_earlier_night_do_not_count()
    {
        // Last night's three starts, and only one tonight.
        var starts = new[] { Local(19, 0, day: 21), Local(19, 8, day: 21), Local(19, 16, day: 21), Local(19, 0) };

        Assert.Null(RaceEta.Cycle(starts, Local(19, 10)));
    }

    [Fact]
    public void A_slot_after_midnight_is_put_on_the_next_day()
    {
        // 00:10 seen at 23:55 is fifteen minutes away, not a day ago.
        var eta = RaceEta.For("r5", 3, false, new[] { Line("r5", "00:10") }, 0, null, Local(23, 55));

        Assert.Equal(Local(0, 10, day: 23), eta);
    }

    [Fact]
    public void A_round_with_no_line_and_no_cycle_has_no_eta()
    {
        Assert.Null(RaceEta.For("r2", 0, false, new[] { Line("r1", "19:00") }, 0, null, Local(18, 0)));
        Assert.Null(RaceEta.For(null, 0, false, new[] { Line("", "19:00") }, 0, null, Local(18, 0)));
    }
}
