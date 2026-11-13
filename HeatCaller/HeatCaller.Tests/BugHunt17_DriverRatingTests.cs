namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): the driver rating counted the 0-lap rows a
/// hand-released grid saves for cars that never moved, so a race one car ran
/// was rated as a win over the whole roster and the no-shows were rated too.
/// </summary>
public sealed class BugHunt17_DriverRatingTests
{
    private static SessionResultEntry Row(string id, int position, int laps, int start, string? outcome = null) => new()
    {
        Transponder = id,
        DriverName = id,
        Position = position,
        Laps = laps,
        LapsLed = position == 1 ? laps : 0,
        FastLapMs = laps > 0 ? 10_000 + position * 100 : 0,
        StartPosition = start,
        Outcome = outcome ?? string.Empty,
    };

    private static SessionRecord Race(params SessionResultEntry[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = "Heat 1",
        StartedAtUtc = new DateTime(2026, 11, 13, 19, 0, 0, DateTimeKind.Utc),
        Results = rows.ToList(),
    };

    [Fact]
    public void A_race_only_one_car_ran_is_not_rated_whatever_the_roster()
    {
        var ratings = DriverRating.ForSession(Race(
            Row("A", 1, 10, 1),
            Row("B", 2, 0, 2),
            Row("C", 3, 0, 3),
            Row("D", 4, 0, 4)));

        Assert.Empty(ratings);
    }

    [Fact]
    public void Cars_that_never_turned_a_lap_are_not_in_the_field_or_rated()
    {
        var withNoShows = DriverRating.ForSession(Race(
            Row("A", 1, 10, 1),
            Row("B", 2, 10, 3),
            Row("C", 3, 9, 5),
            Row("D", 4, 0, 2),
            Row("E", 5, 0, 4),
            Row("F", 6, 0, 6)));

        var justTheRunners = DriverRating.ForSession(Race(
            Row("A", 1, 10, 1),
            Row("B", 2, 10, 2),
            Row("C", 3, 9, 3)));

        Assert.Equal(new[] { "A", "B", "C" }, withNoShows.Keys.OrderBy(k => k));

        // The same race with or without the rows of the cars that sat on the
        // line - grid slots closed up behind them too.
        foreach (var id in new[] { "A", "B", "C" })
        {
            Assert.Equal(justTheRunners[id], withNoShows[id]);
        }
    }

    [Fact]
    public void A_disqualified_car_is_still_in_the_field_and_rated_nothing()
    {
        var ratings = DriverRating.ForSession(Race(
            Row("A", 1, 10, 1),
            Row("B", 2, 0, 2, "DQ")));

        Assert.Equal(2, ratings.Count);
        Assert.Equal(0, ratings["B"]);
    }

    [Fact]
    public void The_live_ratings_leave_out_cars_yet_to_turn_a_lap()
    {
        RacerStanding Car(string id, int laps, int start) => new()
        {
            TransponderId = id,
            Laps = laps,
            LapsLed = 0,
            FastestLapMs = laps > 0 ? 10_000 : 0,
            StartPosition = start,
        };

        Assert.Empty(DriverRating.ForStandings(new[] { Car("A", 5, 1), Car("B", 0, 2), Car("C", 0, 3) }));
        Assert.Equal(new[] { "A", "B" }, DriverRating.ForStandings(new[] { Car("A", 5, 1), Car("B", 4, 2), Car("C", 0, 3) }).Keys.OrderBy(k => k));
    }
}
