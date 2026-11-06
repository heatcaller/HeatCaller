namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026): a driver's night page "started Nth" and the
/// lap list's "Gained N places" used the raw grid slot, which counts
/// no-shows on a stated roster and ghosts on pole, while the lap-by-lap
/// positions count only the real cars. The page now gets the grid place
/// among real starters - the same one the Charge badge and NightMoments use.
/// </summary>
public sealed class BugHunt11_NightReportsTests
{
    private const string Sam = "3012345";
    private const string Kim = "3099999";
    private const string NoShow = "3077777";
    private const string Ghost = "GHOST-1";

    private static readonly DateTime Tonight = new(2026, 11, 5, 21, 0, 0, DateTimeKind.Local);

    [Fact]
    public void A_no_show_on_pole_does_not_push_the_grid_back_a_place()
    {
        // Slot 1 never turned up; Sam from slot 2 leads lap 1.
        var race = Assert.Single(DriverNightExtras.Races(Sam, new[]
        {
            Race(
                Row(Sam, 1, start: 2, 1500, 9000, 8800),
                Row(Kim, 2, start: 3, 1600, 9500, 9400),
                Row(NoShow, 3, start: 1)),
        }, null));

        Assert.Equal(1, race.Start);
        Assert.Equal(1, race.PosAt![0]); // so the page's Start line gains nothing
    }

    [Fact]
    public void A_ghost_on_pole_is_not_a_grid_place()
    {
        var race = Assert.Single(DriverNightExtras.Races(Kim, new[]
        {
            Race(
                Row(Ghost, 1, start: 1, 1400, 8000, 8000),
                Row(Sam, 2, start: 2, 1500, 9000, 8800),
                Row(Kim, 3, start: 3, 1600, 9500, 9400)),
        }, null));

        Assert.Equal(2, race.Start);
    }

    [Fact]
    public void A_slot_past_the_rows_is_ranked_among_the_real_cars()
    {
        // WHY (6 Nov 2026, Bug hunt 11b review): this used to assert no
        // Start at all, from a cap on the number of result rows. The rows
        // are not the slots (see the stated-roster test below), and a
        // borrowed grid's leftover slot is renumbered over the starters at
        // source now, so a slot past the rows is ranked like any other:
        // behind Kim on slot 1, Sam started 2nd of the real cars.
        var race = Assert.Single(DriverNightExtras.Races(Sam, new[]
        {
            Race(
                Row(Kim, 1, start: 1, 1400, 9000, 8800),
                Row(Sam, 2, start: 8, 1500, 9500, 9400)),
        }, null));

        Assert.Equal(2, race.Start);
    }

    [Fact]
    public void A_no_show_on_a_stated_roster_does_not_cost_the_back_slot_its_grid_place()
    {
        // Bug hunt 11b review (6 Nov 2026): a stated roster of six, a normal
        // start, slot 3 never shows - so only five rows are filed (a no-show
        // gets a standing only in a hand-released start) and the slots are
        // not renumbered. Sam from slot 6 is 5th of the real cars and wins.
        var heat = Race(
            Row(Sam, 1, start: 6, 1500, 9000, 8800),
            Row("3011111", 2, start: 1, 1500, 9100, 8900),
            Row("3022222", 3, start: 2, 1500, 9200, 9000),
            Row("3044444", 4, start: 4, 1500, 9300, 9100),
            Row("3055555", 5, start: 5, 1500, 9400, 9200));

        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { heat }, null));

        Assert.Equal(5, race.Start);

        // And the night's moments agree: 5th to 1st is a four-place charge.
        var charge = Assert.Single(NightMoments.ForRace(heat), one => one.What is NightMoments.Kind.Charge);
        Assert.Equal(Sam, charge.Transponder);
        Assert.Equal(4, charge.Places);
    }

    private static SessionResultEntry Row(string who, int pos, int start, params long[] laps) => new()
    {
        Transponder = who,
        DriverName = who,
        Position = pos,
        StartPosition = start,
        Laps = Math.Max(0, laps.Length - 1),
        LapTimesMs = laps.ToList(),
        FastLapMs = laps.Length > 1 ? laps.Skip(1).Min() : 0,
    };

    private static SessionRecord Race(params SessionResultEntry[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = "Heat 1",
        TrackLayoutName = "Club Oval",
        StartedAtUtc = Tonight.AddHours(-1).ToUniversalTime(),
        EndedAtUtc = Tonight.AddHours(-1).ToUniversalTime().AddMinutes(5),
        Results = rows.ToList(),
    };
}
