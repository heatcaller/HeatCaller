using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// BUG HUNT 8 (20 Oct 2026) - CommentaryReaders.TitleOnTheLine left the race
/// being staged out of the rounds remaining when its race day already had a
/// counted race on it (a B-main filed earlier tonight, or the first leg of
/// a two-leg A-main). Every rival was then priced one race short, and the PA
/// could say "nobody left can reach him" when somebody could. It now
/// refuses, the same way it refuses two counted races on one day.
///
/// Fixture built the same way as CommentaryStakesTests.Setup, with one knob
/// added: whether the next race is filed under the last counted race's day
/// or a day of its own. Setup is called once per test, as there.
/// </summary>
public sealed class BugHunt8_CommentaryReaders_csTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Stock = "Box Stock";

    private static string Id(string name) => name.ToLowerInvariant();

    private static TitleStakes? Ask(bool nextSharesLastDay)
    {
        var profiles = ProfileStore.Load();

        foreach (var name in new[] { "Bill", "Sam", "Carl", "Dot" })
        {
            profiles.Upsert(new DriverProfile { Transponder = Id(name), DriverName = name });
        }

        var rounds = RoundDefinitionStore.Load();
        var events = RaceEventStore.Load();
        var races = new List<SessionRecord>();
        var when = new DateTime(2026, 5, 1, 19, 0, 0, DateTimeKind.Utc);
        RaceEvent? lastDay = null;

        // Four rounds of Bill beating Sam, one race per day.
        for (var i = 0; i < 4; i++)
        {
            var past = new RoundDefinition
            {
                ClassName = Stock,
                RoundLabel = "A-Main",
                Kind = RoundKind.Main,
                MainTier = 0,
            };

            var race = new SessionRecord
            {
                Id = "r" + i.ToString(CultureInfo.InvariantCulture),
                SessionType = RaceSessionType.Race,
                Label = "A-Main",
                RoundId = past.Id,
                StartedAtUtc = when.AddDays(7 * i),
                EndedAtUtc = when.AddDays(7 * i).AddMinutes(8),
                Results = new[] { "Bill", "Sam", "Carl", "Dot" }
                    .Select((name, place) => new SessionResultEntry
                    {
                        Transponder = Id(name),
                        DriverName = name,
                        Position = place + 1,
                        Laps = 12,
                    })
                    .ToList(),
            };

            past.LinkedSessionId = race.Id;
            rounds.Add(past);
            races.Add(race);

            lastDay = new RaceEvent
            {
                Name = "Round " + (i + 1).ToString(CultureInfo.InvariantCulture),
                StartedUtc = when.AddDays(7 * i),
                RoundIds = new List<string> { past.Id },
                ClassNames = new List<string> { Stock },
            };
        }

        var next = new RoundDefinition
        {
            ClassName = Stock,
            RoundLabel = "A-Main",
            Kind = RoundKind.Main,
            MainTier = 0,
            AssignedTransponders = new[] { "Bill", "Sam", "Carl", "Dot" }.Select(Id).ToList(),
        };

        rounds.Add(next);

        // Re-add the earlier days, then the last one with or without the
        // next race on it.
        for (var i = 0; i < 3; i++)
        {
            events.Upsert(new RaceEvent
            {
                Name = "Round " + (i + 1).ToString(CultureInfo.InvariantCulture),
                StartedUtc = when.AddDays(7 * i),
                FinishedUtc = when.AddDays(7 * i).AddHours(4),
                RoundIds = new List<string> { races[i].RoundId! },
                ClassNames = new List<string> { Stock },
            });
        }

        if (nextSharesLastDay)
        {
            lastDay!.RoundIds.Add(next.Id);
            events.Upsert(lastDay);
        }
        else
        {
            events.Upsert(lastDay!);
            events.Upsert(new RaceEvent
            {
                Name = "Tonight",
                StartedUtc = when.AddDays(7 * 4),
                RoundIds = new List<string> { next.Id },
                ClassNames = new List<string> { Stock },
            });
        }

        var seasons = SeasonStore.Load();

        seasons.Upsert(new Season
        {
            Name = "Winter Series",
            ClassName = Stock,
            AutoInclude = true,
            MainsOnly = true,
            CountBestRounds = 4,
            PlannedRounds = 6,
            PointsScheme = Enumerable.Range(1, 10)
                .Select(p => new PointsSchemeEntry { Position = p, Points = 11 - p })
                .ToList(),
        });

        return CommentaryReaders.TitleOnTheLine(
            next, next.AssignedTransponders, seasons, rounds, events,
            SessionHistoryStore.FromSessions(races), profiles);
    }

    [Fact]
    public void The_race_being_staged_on_a_day_already_counted_says_nothing()
    {
        // Tonight already holds a counted race, so the race about to run is
        // a second counting race on one day - not the shape the proof
        // plays out. Before the fix this answered with one round too few.
        Assert.Null(Ask(nextSharesLastDay: true));
    }

    [Fact]
    public void The_race_being_staged_on_a_day_of_its_own_still_answers()
    {
        var stakes = Ask(nextSharesLastDay: false);

        Assert.NotNull(stakes);
        Assert.Equal(4, stakes!.RoundsCounted);
        Assert.Equal(2, stakes.RoundsLeft);
    }
}
