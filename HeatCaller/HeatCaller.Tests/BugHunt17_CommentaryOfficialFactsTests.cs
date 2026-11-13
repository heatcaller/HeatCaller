namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), places "lost to penalties" that a DNF cost.
///
/// A driver with a small penalty who was then marked DNF sits ahead of the
/// backmarkers on the road (more laps) and under every finisher in the
/// result, so the recap was told they "lost 2 places to penalties". A
/// penalised DNF is now left out like any other retirement, and a
/// disqualified car no longer hides a place somebody behind it lost.
/// </summary>
public sealed class BugHunt17_CommentaryOfficialFactsTests
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
    public void A_penalised_driver_marked_DNF_did_not_lose_places_to_penalties()
    {
        // Sam took 5 seconds early, broke down on lap 11, and was marked DNF.
        var sam = Row("Sam", 4, 11, 280_000);
        sam.RacePenaltyMs = 5_000;
        sam.Outcome = ResultEditor.Dnf;

        var race = Race(Row("Bill", 1, 12, 300_000), Row("Ann", 2, 9, 301_000), Row("Cy", 3, 8, 302_000), sam);

        Assert.Empty(CommentaryOfficialFacts.PenaltiesDecided(race, Named));
    }

    [Fact]
    public void A_penalised_DNF_who_led_on_the_road_is_not_said_to_have_lost_the_win_to_penalties()
    {
        var sam = Row("Sam", 3, 13, 290_000);
        sam.RacePenaltyMs = 5_000;
        sam.Outcome = ResultEditor.Dnf;

        var race = Race(Row("Bill", 1, 12, 300_000), Row("Ann", 2, 12, 301_000), sam);

        Assert.Empty(CommentaryOfficialFacts.PenaltiesDecided(race, Named));
    }

    [Fact]
    public void A_penalised_DNF_does_not_hide_a_place_a_finisher_lost()
    {
        var sam = Row("Sam", 4, 12, 250_000);
        sam.RacePenaltyMs = 5_000;
        sam.Outcome = ResultEditor.Dnf;

        var ann = Row("Ann", 3, 12, 310_500);
        ann.TimePenaltyMs = 10_000;
        ann.AsRaced = new ResultAsRaced { Position = 2, Laps = 12, ElapsedMs = 300_500 };

        var race = Race(Row("Bill", 1, 12, 300_000), Row("Cy", 2, 12, 302_000), ann, sam);

        Assert.Equal("Ann lost 1 place to penalties.", Assert.Single(CommentaryOfficialFacts.PenaltiesDecided(race, Named)));
    }

    [Fact]
    public void A_disqualified_car_does_not_hide_a_place_lost_behind_it()
    {
        var sam = Row("Sam", 3, 0, 300_000);
        sam.Outcome = ResultEditor.Dq;
        sam.AsRaced = new ResultAsRaced { Position = 1, Laps = 13, ElapsedMs = 300_000 };

        var ann = Row("Ann", 2, 12, 305_000);
        ann.TimePenaltyMs = 5_000;
        ann.AsRaced = new ResultAsRaced { Position = 2, Laps = 12, ElapsedMs = 300_000 };

        var race = Race(Row("Bill", 1, 12, 302_000), ann, sam);

        Assert.Equal(
            new[]
            {
                "Sam had the winning result on the road but was disqualified, so the win went to Bill.",
                "Ann lost 1 place to penalties.",
            },
            CommentaryOfficialFacts.PenaltiesDecided(race, Named));
    }
}
