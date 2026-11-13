namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: "Held off Sam for N laps" summed each car's lap times as if
/// both ran on one clock. In a qualifying start (own clock from the first
/// crossing) or a staggered start on "own release" they do not, so a car
/// half a lap away all race read as nose to tail. Each result row now
/// carries its car's clock zero (SessionResultEntry.ClockZeroMs, written
/// when the race is filed) and the line is worked out from the green.
/// </summary>
public sealed class BugHunt17_RaceLinesTests
{
    private const string Me = "3012345";
    private const string Sam = "3099999";

    private static SessionResultEntry Row(string who, string name, int pos, long clockZeroMs, params long[] laps) => new()
    {
        Transponder = who,
        DriverName = name,
        Position = pos,
        Laps = Math.Max(0, laps.Length - 1),
        LapTimesMs = laps.ToList(),
        FastLapMs = laps.Skip(1).DefaultIfEmpty(0).Min(),
        ClockZeroMs = clockZeroMs,
    };

    private static SessionRecord Race(params SessionResultEntry[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = "Q1 Heat 1",
        StartedAtUtc = new DateTime(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc),
        EndedAtUtc = new DateTime(2026, 11, 13, 19, 5, 0, DateTimeKind.Utc),
        Results = rows.ToList(),
    };

    [Fact]
    public void A_car_six_seconds_up_the_road_on_its_own_clock_was_not_held_off()
    {
        // A qualifying start: I crossed first 1 s after the green, Sam 7 s
        // after it. On their own clocks Sam is a tenth a lap slower - which
        // used to read as "Held off Sam for 5 laps".
        var me = Row(Me, "Asher Kent", 4, 1000, 0, 10000, 10000, 10000, 10000, 10000);
        var sam = Row(Sam, "Sam Parker", 5, 7000, 0, 10100, 10100, 10100, 10100, 10100);

        Assert.Null(RaceLines.HeldOffLine(Race(me, sam), me));
    }

    [Fact]
    public void A_car_really_on_the_bumper_from_a_later_start_was_held_off()
    {
        // Sam started 300 ms after me and matched my laps: 300 ms behind at
        // the line every lap, on the green's clock.
        var me = Row(Me, "Asher Kent", 4, 1000, 0, 10000, 10000, 10000, 10000);
        var sam = Row(Sam, "Sam Parker", 5, 1300, 0, 10000, 10000, 10000, 10000);

        Assert.Equal("Held off Sam for 4 laps", RaceLines.HeldOffLine(Race(me, sam), me));
    }

    [Fact]
    public void A_result_with_no_clock_zero_reads_as_one_clock_from_the_green()
    {
        // Every race on a shared clock, and every result saved before this.
        var me = Row(Me, "Asher Kent", 4, 0, 2000, 10000, 10000, 10000, 10000);
        var sam = Row(Sam, "Sam Parker", 5, 0, 2300, 10000, 10000, 10000, 10000);

        Assert.Equal("Held off Sam for 4 laps", RaceLines.HeldOffLine(Race(me, sam), me));
    }

    [Fact]
    public void Filing_a_race_writes_each_cars_clock_zero_on_its_row()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", "RaceFilingService.cs"));

        Assert.Contains(
            "results[i].ClockZeroMs = RaceControlControl.ClockZeroSinceGreenMs(racers[i], startType, staggerClock);",
            source, StringComparison.Ordinal);
    }
}
