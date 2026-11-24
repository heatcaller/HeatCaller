namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), F123: a driver credited with the start lap
/// had their dash from the grid dropped from their night page's lap list
/// while startMs still said "one start lap", so the page numbered their
/// first flying lap (lap 3) as lap 2 - a list a lap short of the card, Pos
/// and the lap chart a lap out of step with the public result. The dash now
/// stays in as lap 2, as the public result sends it, marked by startLaps.
/// </summary>
public sealed class BugHunt24_DriverNightExtrasTests
{
    private const string Jumper = "3012345";
    private const string Rival = "3099999";

    private static readonly DateTime Tonight = new(2026, 11, 20, 21, 0, 0, DateTimeKind.Local);

    [Fact]
    public void A_credited_starts_night_laps_keep_the_dash_as_lap_2_in_step_with_the_result()
    {
        var session = Race(Credited(), Ordinary());

        var race = Assert.Single(DriverNightExtras.Races(Jumper, new[] { session }, null));

        // Lap 1 is the start (startMs 0), so the list plus one is the lap count.
        Assert.Equal(new long[] { 4000, 11000, 11200 }, race.LapsMs!);
        Assert.Equal(race.Laps, race.LapsMs!.Count + 1);
        Assert.Equal(0L, race.StartMs);
        Assert.Equal(1, race.StartLaps);

        // Positions in step with LapTimesMs.Skip(1), as the public result sends them.
        Assert.Equal(LapByLap.Positions(session.Results)[0], race.PosAt!.ToArray());
    }

    [Fact]
    public void An_ordinary_start_is_unchanged_and_carries_no_start_laps()
    {
        var race = Assert.Single(DriverNightExtras.Races(Rival, new[] { Race(Credited(), Ordinary()) }, null));

        Assert.Equal(new long[] { 11500, 11400, 11300 }, race.LapsMs!);
        Assert.Equal(1500L, race.StartMs);
        Assert.Null(race.StartLaps);
    }

    [Fact]
    public void A_credited_starts_field_row_keeps_the_dash_too()
    {
        var session = Race(Credited(), Ordinary());

        var field = DriverNightExtras.FieldOf(session.Results, practice: false, abbreviate: false)!;

        Assert.Equal(2, field.Count);
        Assert.Equal(new long[] { 4000, 11000, 11200 }, field[0].LapsMs!);
        Assert.Equal(1, field[0].StartLaps);
        Assert.Equal(LapByLap.Positions(session.Results)[0], field[0].PosAt!.ToArray());
        Assert.Null(field[1].StartLaps);
        Assert.Equal(new long[] { 11500, 11400, 11300 }, field[1].LapsMs!);
    }

    [Fact]
    public void Start_laps_is_one_only_for_a_credited_start_with_a_timed_dash()
    {
        Assert.Equal(1, DriverNightExtras.StartLapsOf(Credited()));
        Assert.Null(DriverNightExtras.StartLapsOf(Ordinary()));

        // A qualifier's start slot is zero too, but not credited: the lap after it is real.
        var qualifier = Credited();
        qualifier.StartLapCredited = false;
        Assert.Null(DriverNightExtras.StartLapsOf(qualifier));

        // An untimed dash is dropped from lapsMs, so marking it would mark a real lap.
        var untimed = Credited();
        untimed.LapTimesMs = new List<long> { 0, 0, 11000, 11200 };
        Assert.Null(DriverNightExtras.StartLapsOf(untimed));
    }

    private static SessionResultEntry Credited() => new()
    {
        Transponder = Jumper,
        DriverName = "Sam Parker",
        Position = 1,
        Laps = 4,
        StartLapCredited = true,
        LapTimesMs = new List<long> { 0, 4000, 11000, 11200 },
        FastLapMs = 11000,
    };

    private static SessionResultEntry Ordinary() => new()
    {
        Transponder = Rival,
        DriverName = "Alex Kent",
        Position = 2,
        Laps = 3,
        LapTimesMs = new List<long> { 1500, 11500, 11400, 11300 },
        FastLapMs = 11300,
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
