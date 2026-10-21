namespace HeatCaller.Tests;

/// <summary>
/// BUG HUNT 8 (20 Oct 2026) - the season piece keyed each moment on the race
/// and the kind only, and FactPack drops a repeated key. So when two drivers
/// both charged through the field in the same race, the second charge was
/// silently lost (and still used up one of the twelve moment slots).
/// </summary>
public sealed class BugHunt8_SeasonPiece_csTests
{
    private static readonly DateTime March = new(2026, 3, 14, 19, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Two_charges_in_one_race_are_both_moments_of_the_season()
    {
        // Ann from tenth to first (nine places) and Bob from ninth to second
        // (seven places), both well over the four a charge needs. Everybody
        // else started ahead of them and finished behind them, so nobody
        // else made up a place.
        var rows = new List<SessionResultEntry>
        {
            Row("Ann", position: 1, startPosition: 10, lapMs: 9000),
            Row("Bob", position: 2, startPosition: 9, lapMs: 9100),
        };

        for (var i = 0; i < 8; i++)
        {
            rows.Add(Row($"Driver{(char)('C' + i)}", position: 3 + i, startPosition: 1 + i, lapMs: 10000 + i * 100));
        }

        var facts = SeasonPiece.FactsFor(
            new Season { Name = "Winter Series" },
            new List<SessionRecord>
            {
                new()
                {
                    Id = "a",
                    SessionType = RaceSessionType.Race,
                    Label = "A-Main",
                    TrackLayoutName = "Winter Indoor",
                    StartedAtUtc = March,
                    EndedAtUtc = March.AddMinutes(8),
                    Results = rows,
                },
            },
            new List<SeasonStandingEntry>
            {
                new() { DriverName = "Ann", TotalPoints = 40, Wins = 1, SessionsCounted = 1 },
                new() { DriverName = "Bob", TotalPoints = 35, Wins = 0, SessionsCounted = 1 },
            });

        Assert.Contains("Ann made up 9 places", facts.Text);
        Assert.Contains("Bob made up 7 places", facts.Text);
    }

    private static SessionResultEntry Row(string name, int position, int startPosition, long lapMs)
    {
        var times = new List<long> { lapMs, lapMs, lapMs };

        return new SessionResultEntry
        {
            Transponder = name.ToLowerInvariant(),
            DriverName = name,
            Position = position,
            Laps = times.Count,
            LapTimesMs = times,
            StartPosition = startPosition,
            ElapsedMs = times.Sum(),
        };
    }
}
