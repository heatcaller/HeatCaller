namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), "so the win went to" a car that was not a
/// classified finisher.
///
/// When the road winner was disqualified and the car now at position 1 never
/// turned a lap (or was disqualified too), the recap was told the win went to
/// that car, while the same prompt said the race had no winner. Now it is
/// told only that the road winner was disqualified.
/// </summary>
public sealed class BugHunt24_CommentaryOfficialFactsTests
{
    private static string Named(string transponder) => transponder;

    private static SessionResultEntry Row(string who, int position, int laps, long ms) => new()
    {
        Transponder = who,
        DriverName = who,
        Position = position,
        Laps = laps,
        ElapsedMs = ms,
        FastLapMs = 20_000,
    };

    private static SessionRecord Race(params SessionResultEntry[] rows)
    {
        var race = new SessionRecord { Label = "Heat 3", SessionType = RaceSessionType.Race };
        race.Results.AddRange(rows);
        return race;
    }

    [Fact]
    public void A_disqualified_road_winner_does_not_hand_the_win_to_a_car_with_no_laps()
    {
        // Sam led on the road and was black-flagged; Bill broke on the grid.
        var sam = Row("Sam", 2, 0, 300_000);
        sam.Outcome = ResultEditor.Dq;
        sam.AsRaced = new ResultAsRaced { Position = 1, Laps = 20, ElapsedMs = 300_000 };

        var race = Race(Row("Bill", 1, 0, 0), sam);

        var lines = CommentaryOfficialFacts.PenaltiesDecided(race, Named);

        Assert.DoesNotContain(lines, l => l.Contains("the win went to"));
        Assert.Equal("Sam had the winning result on the road but was disqualified.", Assert.Single(lines));

        var night = CommentaryOfficialFacts.ForTheNight(new[] { race }, Named);

        Assert.DoesNotContain(night, l => l.Contains("the win went to"));
        Assert.Contains("In Heat 3: Sam had the winning result on the road but was disqualified.", night);
    }

    [Fact]
    public void When_every_car_is_disqualified_the_win_does_not_go_to_another_disqualified_car()
    {
        var ann = Row("Ann", 1, 0, 200_000);
        ann.Outcome = ResultEditor.Dq;
        ann.AsRaced = new ResultAsRaced { Position = 2, Laps = 10, ElapsedMs = 200_000 };

        var sam = Row("Sam", 2, 0, 300_000);
        sam.Outcome = ResultEditor.Dq;
        sam.AsRaced = new ResultAsRaced { Position = 1, Laps = 20, ElapsedMs = 300_000 };

        var race = Race(ann, sam);

        var lines = CommentaryOfficialFacts.PenaltiesDecided(race, Named);

        Assert.DoesNotContain(lines, l => l.Contains("the win went to"));
        Assert.Contains("Sam had the winning result on the road but was disqualified.", lines);
    }
}
