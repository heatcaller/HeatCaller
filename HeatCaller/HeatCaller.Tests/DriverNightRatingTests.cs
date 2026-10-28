namespace HeatCaller.Tests;

/// <summary>
/// The driver rating on a driver's own night (28 Oct 2026, asked for by
/// Asher: the night page's Driver score, next to the report card) - each
/// race in DriverNightExtras.Races carries the same rating DriverRating
/// gives that race everywhere else.
/// </summary>
public sealed class DriverNightRatingTests
{
    private const string Sam = "3012345";

    [Fact]
    public void Each_race_carries_the_drivers_rating_for_it()
    {
        var session = Race(Row(Sam, 1000, 9000, 9000), Row("3099999", 1000, 9500, 9500));

        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { session }, null));

        Assert.NotNull(race.Rating);
        Assert.Equal(DriverRating.ForSession(session)[Sam], race.Rating!.Value);
    }

    [Fact]
    public void A_race_with_nobody_to_race_has_no_rating()
    {
        var race = Assert.Single(DriverNightExtras.Races(Sam, new[] { Race(Row(Sam, 1000, 9000)) }, null));

        Assert.Null(race.Rating);
    }

    private static SessionResultEntry Row(string transponder, params long[] laps) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Laps = Math.Max(0, laps.Length - 1),
        LapTimesMs = laps.ToList(),
        FastLapMs = laps.Skip(1).DefaultIfEmpty(0).Min(),
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
