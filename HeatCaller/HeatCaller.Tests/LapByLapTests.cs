namespace HeatCaller.Tests;

/// <summary>
/// Where each driver was after each lap (28 Oct 2026, asked for by Asher,
/// matching LiveRC's lap-by-lap position graph and the Pos column of a
/// driver's lap list) - LapByLap, and the two documents that carry it: the
/// public result and the driver's own night.
/// </summary>
public sealed class LapByLapTests
{
    [Fact]
    public void A_pass_on_the_road_shows_up_on_the_lap_it_happened()
    {
        // A gets away first (1.0 s to the line against 1.5 s) and leads
        // laps 1 and 2. B is past by the time they cross for the third
        // time: A at 1.0 + 10 + 10 + 12 = 33.0 s, B at 1.5 + 10 + 9.6 + 11.4 = 32.5 s.
        var a = Row("A", 1000, 10000, 10000, 12000);
        var b = Row("B", 1500, 10000, 9600, 11400);

        var places = LapByLap.Positions(new[] { b, a });

        Assert.Equal(new[] { 2, 2, 1 }, places[0]);
        Assert.Equal(new[] { 1, 1, 2 }, places[1]);
    }

    [Fact]
    public void A_driver_a_lap_down_is_behind_everybody_who_crossed_that_lap_before_them()
    {
        var leader = Row("Lead", 1000, 9000, 9000, 9000, 9000);
        var slow = Row("Slow", 1000, 14000, 14000, 14000);

        var places = LapByLap.Positions(new[] { leader, slow });

        Assert.Equal(new[] { 1, 1, 1, 1 }, places[0]);
        Assert.Equal(new[] { 2, 2, 2 }, places[1]);
    }

    [Fact]
    public void A_dead_heat_on_the_clock_goes_to_whoever_finished_higher()
    {
        var first = Row("First", 1000, 10000);
        var second = Row("Second", 1000, 10000);

        var places = LapByLap.Positions(new[] { first, second });

        Assert.Equal(new[] { 1 }, places[0]);
        Assert.Equal(new[] { 2 }, places[1]);
    }

    [Fact]
    public void Ghosts_get_no_positions_and_take_none()
    {
        var ghost = Row(Ghosts.Prefix + "1", 500, 8000, 8000);
        var real = Row("Real", 1000, 10000, 10000);

        var places = LapByLap.Positions(new[] { ghost, real });

        Assert.Empty(places[0]);
        Assert.Equal(new[] { 1, 1 }, places[1]);
    }

    [Fact]
    public void No_laps_and_only_a_start_lap_both_mean_no_positions()
    {
        var none = Row("None");
        var startOnly = Row("Start", 1200);
        none.LapTimesMs = null!;

        var places = LapByLap.Positions(new[] { none, startOnly });

        Assert.Empty(places[0]);
        Assert.Empty(places[1]);
    }

    [Fact]
    public void A_drivers_night_carries_each_laps_position_in_step_with_the_laps()
    {
        var sam = Row("3012345", 1400, 12000, 0, 11000);
        var other = Row("3099999", 1000, 11000, 11000, 11000);
        var session = Race(other, sam);
        session.TimeLimitSeconds = 180;

        var race = Assert.Single(DriverNightExtras.Races("3012345", new[] { session }, null));

        // The 0 ms lap is dropped from the chart, and its position with it:
        // the 11.0 s lap is the third crossing, at 24.4 s, ahead of the
        // other car's third at 34.0 s.
        Assert.Equal(new long[] { 12000, 11000 }, race.LapsMs!);
        Assert.Equal(new[] { 2, 1 }, race.PosAt!);
        Assert.Equal(1400, race.StartMs);
        Assert.Equal(180, race.LengthS);
    }

    [Fact]
    public void A_night_from_a_race_with_no_time_limit_leaves_the_length_out()
    {
        var race = Assert.Single(DriverNightExtras.Races("3012345", new[] { Race(Row("3012345", 1400, 12000)) }, null));

        Assert.Null(race.LengthS);
        Assert.Equal(new[] { 1 }, race.PosAt!);
    }

    private static SessionResultEntry Row(string transponder, params long[] laps) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Laps = Math.Max(0, laps.Length - 1),
        LapTimesMs = laps.ToList(),
    };

    private static SessionRecord Race(params SessionResultEntry[] rows)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i].Position = i + 1;
        }

        return new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "Heat 1",
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-10),
            EndedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            Results = rows.ToList(),
        };
    }
}
