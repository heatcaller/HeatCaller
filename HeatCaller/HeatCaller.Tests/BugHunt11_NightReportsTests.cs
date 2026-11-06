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
    public void A_slot_past_the_field_is_no_grid_place()
    {
        // Slot 8 left over from a bigger race, in a two-car heat.
        var race = Assert.Single(DriverNightExtras.Races(Sam, new[]
        {
            Race(
                Row(Kim, 1, start: 1, 1400, 9000, 8800),
                Row(Sam, 2, start: 8, 1500, 9500, 9400)),
        }, null));

        Assert.Null(race.Start);
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
