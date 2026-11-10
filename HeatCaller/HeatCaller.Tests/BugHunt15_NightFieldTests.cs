namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (9 Nov 2026, B45): the night page's "Nth of M" counted every
/// result row, so a hand-released roster's no-shows and a DQ (laps cut to 0)
/// made the winner "1st of 6" for a heat three cars ran, while Driver Hub
/// (DriverStats) said "P1 of 3". The field now counts cars that turned a lap.
/// </summary>
public sealed class BugHunt15_NightFieldTests
{
    private const string Sam = "3012345";

    private static readonly DateTime Tonight = new(2026, 11, 8, 21, 0, 0, DateTimeKind.Local);

    private static SessionRecord HandReleasedHeat() => Race(
        Row(Sam, 1, 12),
        Row("T2", 2, 11),
        Row("T3", 3, 9),
        Row("T4", 4, 0, outcome: ResultEditor.Dq), // black-flagged, laps cut to 0
        Row("T5", 5, 0),                           // never left the grid
        Row("T6", 6, 0));                          // never left the grid

    [Fact]
    public void The_field_counts_only_the_cars_that_turned_a_lap()
    {
        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { HandReleasedHeat() }, null));

        Assert.Equal(1, race.Pos);
        Assert.Equal(3, race.Of);
    }

    [Fact]
    public void A_driver_who_never_ran_is_never_placed_past_the_field()
    {
        var race = Assert.Single(DriverNightExtras.Races("T5", new[] { HandReleasedHeat() }, null));

        Assert.Equal(5, race.Pos);
        Assert.Equal(5, race.Of);
    }

    private static SessionResultEntry Row(string who, int pos, int laps, string outcome = "") => new()
    {
        Transponder = who,
        DriverName = who,
        Position = pos,
        Laps = laps,
        Outcome = outcome,
        LapTimesMs = new List<long>(),
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
