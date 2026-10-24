namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): a driver's night page lap chart gets the
/// flying laps only. Index 0 of LapTimesMs is the start-lap slot (grid to
/// line), which squashed every real lap into a flat line when it was sent.
/// </summary>
public sealed class BugHunt10_DriverNightExtrasTests
{
    private const string Sam = "3012345";

    private static readonly DateTime Tonight = new(2026, 10, 22, 21, 0, 0, DateTimeKind.Local);

    [Fact]
    public void The_start_lap_is_left_out_of_the_lap_chart()
    {
        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { Race(Row(1400, 12300, 12100, 11900, 11850, 11900)) }, null));

        Assert.Equal(new long[] { 12300, 12100, 11900, 11850, 11900 }, race.LapsMs!);
    }

    [Fact]
    public void A_jump_starters_zero_start_lap_is_left_out_too()
    {
        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { Race(Row(0, 9000, 8800)) }, null));

        Assert.Equal(new long[] { 9000, 8800 }, race.LapsMs!);
    }

    [Fact]
    public void Only_a_start_lap_means_no_lap_chart()
    {
        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { Race(Row(1400)) }, null));

        Assert.Null(race.LapsMs);
    }

    [Fact]
    public void A_null_lap_list_is_no_lap_chart()
    {
        var row = Row();
        row.LapTimesMs = null!;

        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { Race(row) }, null));

        Assert.Null(race.LapsMs);
    }

    [Fact]
    public void The_cap_counts_flying_laps()
    {
        var laps = Enumerable.Range(0, 260).Select(i => 8000L + i).ToArray();
        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { Race(Row(laps)) }, null));

        Assert.Equal(DriverNightExtras.MostLaps, race.LapsMs!.Count);
        Assert.Equal(8001L, race.LapsMs![0]);
    }

    private static SessionResultEntry Row(params long[] laps) => new()
    {
        Transponder = Sam,
        DriverName = "Sam Parker",
        Position = 1,
        Laps = Math.Max(0, laps.Length - 1),
        LapTimesMs = laps.ToList(),
        FastLapMs = 8500,
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
