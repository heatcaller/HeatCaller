namespace HeatCaller.Tests;

/// <summary>
/// Decoder-down mode (11 Nov 2026, race-night build plan 6.1): when it is
/// offered, which taps count, the grid's fixed order and shape, typing a
/// whole result's lap counts into Edit Result, and that a hand-scored lap
/// ends up marked "12*" in the saved result through the existing paths.
/// </summary>
public sealed class HandScoringTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- When it is offered -------------------------------------------------------

    [Theory]
    [InlineData("decoder-off", true)]
    [InlineData("decoder-quiet", true)]
    [InlineData("no-crossings", true)]
    [InlineData("missed-lap:ABC", false)]
    [InlineData("split-down", false)]
    [InlineData("check-failed", false)]
    [InlineData(null, false)]
    public void Only_the_whole_field_going_uncounted_means_laps_are_not_counted(string? key, bool expected) =>
        Assert.Equal(expected, HandScoring.MeansLapsAreNotCounted(key));

    [Fact]
    public void It_is_offered_mid_race_when_the_decoder_is_down()
    {
        Assert.True(HandScoring.ShouldOffer("decoder-quiet", RaceState.Racing, practice: false, alreadyOn: false));
    }

    [Fact]
    public void It_is_not_offered_once_on_in_practice_before_the_green_or_after_the_race()
    {
        Assert.False(HandScoring.ShouldOffer("decoder-off", RaceState.Racing, practice: false, alreadyOn: true));
        Assert.False(HandScoring.ShouldOffer("decoder-off", RaceState.Racing, practice: true, alreadyOn: false));
        Assert.False(HandScoring.ShouldOffer("decoder-off", RaceState.Staging, practice: false, alreadyOn: false));
        Assert.False(HandScoring.ShouldOffer("decoder-off", RaceState.Countdown, practice: false, alreadyOn: false));
        Assert.False(HandScoring.ShouldOffer("decoder-off", RaceState.Finished, practice: false, alreadyOn: false));
        Assert.False(HandScoring.ShouldOffer("missed-lap:A", RaceState.Racing, practice: false, alreadyOn: false));
    }

    // --- Which taps count -------------------------------------------------------

    [Fact]
    public void It_can_only_be_switched_on_while_a_race_runs()
    {
        Assert.Null(HandScoring.StartRefusal(RaceState.Racing, practice: false));
        Assert.NotNull(HandScoring.StartRefusal(RaceState.Idle, practice: false));
        Assert.NotNull(HandScoring.StartRefusal(RaceState.Finished, practice: false));
        Assert.Contains("practice", HandScoring.StartRefusal(RaceState.Racing, practice: true));
    }

    [Fact]
    public void A_tap_needs_the_car_on_the_board()
    {
        Assert.Null(HandScoring.LapRefusal(RaceState.Racing, false, onBoard: true, "Sam"));
        Assert.Contains("Sam", HandScoring.LapRefusal(RaceState.Racing, false, onBoard: false, "Sam"));
        Assert.NotNull(HandScoring.LapRefusal(RaceState.Finished, false, onBoard: true, "Sam"));
    }

    [Fact]
    public void A_second_tap_within_a_second_is_the_same_lap()
    {
        Assert.False(HandScoring.IsDoubleTap(null, 5_000));
        Assert.True(HandScoring.IsDoubleTap(5_000, 5_400));
        Assert.True(HandScoring.IsDoubleTap(5_000, 5_999));
        Assert.False(HandScoring.IsDoubleTap(5_000, 6_000));

        // A clock gone backwards is never a double tap.
        Assert.False(HandScoring.IsDoubleTap(5_000, 4_000));
    }

    // --- The grid -------------------------------------------------------

    [Fact]
    public void The_buttons_keep_grid_order_whatever_the_laps_and_ghosts_are_left_out()
    {
        var standings = new[]
        {
            // Running order: C leads on laps - the grid must not follow it.
            new RacerStanding { TransponderId = "C", Laps = 9, StartPosition = 3 },
            new RacerStanding { TransponderId = "A", Laps = 7, StartPosition = 1 },
            new RacerStanding { TransponderId = "Z", Laps = 8, StartPosition = 0 },
            new RacerStanding { TransponderId = "B", Laps = 6, StartPosition = 2 },
            new RacerStanding { TransponderId = "GHOST", Laps = 9, StartPosition = 4 },
        };

        var names = new Dictionary<string, string> { ["A"] = "Amy", ["B"] = "Ben", ["C"] = "Cal" };

        var cars = HandScoring.CarsFor(standings, t => names.GetValueOrDefault(t), t => t == "GHOST");

        Assert.Equal(new[] { "A", "B", "C", "Z" }, cars.Select(c => c.Transponder));
        Assert.Equal("Amy", cars[0].Name);

        // No name: the transponder, so the button still says which car.
        Assert.Equal("Z", cars[3].Name);
    }

    [Fact]
    public void A_button_shows_hand_laps_with_the_results_mark()
    {
        // Four laps timed, two added by hand.
        var standing = new RacerStanding { TransponderId = "A", Laps = 6, StartPosition = 1 };
        standing.LapTimesMs.AddRange(new long[] { 2_000, 11_000, 11_200, 10_900 });

        var car = HandScoring.CarsFor(new[] { standing }, _ => "Amy", _ => false).Single();

        Assert.Equal(2, car.ByHand);
        Assert.Equal("6*", car.LapsText);
    }

    [Theory]
    [InlineData(1, 1600, 900, 1, 1)]
    [InlineData(8, 1600, 900, 3, 3)]
    [InlineData(10, 1600, 900, 3, 4)]
    [InlineData(4, 400, 900, 1, 4)]
    public void The_grid_shape_makes_the_buttons_as_big_as_they_can_be(int cars, int width, int height, int columns, int rows)
    {
        Assert.Equal((columns, rows), HandScoring.GridShape(cars, width, height));
    }

    [Fact]
    public void Every_grid_shape_holds_every_car_with_no_empty_row()
    {
        for (var cars = 1; cars <= 24; cars++)
        {
            foreach (var (w, h) in new[] { (1920, 1000), (800, 1200), (375, 600), (3000, 400) })
            {
                var (columns, rows) = HandScoring.GridShape(cars, w, h);

                Assert.True(columns * rows >= cars);
                Assert.True((rows - 1) * columns < cars);
            }
        }

        Assert.Equal((1, 1), HandScoring.GridShape(0, 800, 600));
    }

    // --- Afterwards: typing the lap counts into Edit Result ---------------------------

    [Fact]
    public void Typing_a_lap_count_sets_the_added_laps_and_keeps_a_lap_penalty()
    {
        Assert.Equal(3, HandScoring.AddedLapsFor(racedLaps: 9, lapPenalty: 0, wantedTotal: 12));
        Assert.Equal(-2, HandScoring.AddedLapsFor(racedLaps: 9, lapPenalty: 0, wantedTotal: 7));

        // A lap penalty stands on top of what is typed.
        Assert.Equal(4, HandScoring.AddedLapsFor(racedLaps: 9, lapPenalty: 1, wantedTotal: 12));
        Assert.Equal(12, HandScoring.TotalLaps(9, 4, 1));

        // And the two are the same sum both ways round.
        for (var raced = 0; raced < 20; raced += 3)
        {
            for (var wanted = 0; wanted < 25; wanted += 4)
            {
                Assert.Equal(wanted, HandScoring.TotalLaps(raced, HandScoring.AddedLapsFor(raced, 2, wanted), 2));
            }
        }
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData(" 0 ", 0)]
    [InlineData("-3", null)]
    [InlineData("1.5", null)]
    [InlineData("", null)]
    [InlineData("twelve", null)]
    public void A_lap_count_is_a_whole_number_from_none_up(string text, int? expected) =>
        Assert.Equal(expected, HandScoring.ParseLapCount(text));

    [Fact]
    public void A_whole_result_typed_in_comes_out_in_order_and_marked_counted_by_hand()
    {
        // The decoder died after lap 3: everyone has 3 timed laps.
        var record = Race(Driver("A", 3), Driver("B", 3), Driver("C", 3));

        // Off the hand-scoring sheet: C 11, A 12, B 10.
        var typed = new Dictionary<string, int> { ["A"] = 12, ["B"] = 10, ["C"] = 11 };
        var adjustments = record.Results
            .Select(r => new ResultAdjustment(r.Transponder, HandScoring.AddedLapsFor(r.Laps, 0, typed[r.Transponder]), 0, 0, ""))
            .ToList();

        Assert.Null(ResultEditor.Validate(record, adjustments));
        ResultEditor.Apply(record, adjustments, "decoder down - hand scored", "Laptop");

        var order = record.Results.OrderBy(r => r.Position).ToList();
        Assert.Equal(new[] { "A", "C", "B" }, order.Select(r => r.Transponder));
        Assert.Equal(new[] { 12, 11, 10 }, order.Select(r => r.Laps));

        // The laps added show as counted by hand - the existing mark.
        Assert.Equal("12*", HandCountedLaps.LapsText(order[0].Laps, HandCountedLaps.In(order[0])));
        Assert.Equal(9, HandCountedLaps.In(order[0]));
    }

    // --- During the race: a tap is the existing +1 Lap ---------------------------

    [Fact]
    public void Taps_after_the_loop_dies_are_hand_laps_in_the_filed_result()
    {
        var race = new RaceControlSession { LapLimit = 50 };
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);

        Seen(race, "A", 100_500);
        Seen(race, "A", 111_000);

        // The loop goes; the director taps A three times.
        for (var i = 0; i < 3; i++)
        {
            Assert.NotNull(race.EditStanding("A", () => race.AdjustLaps("A", +1)));
        }

        var car = HandScoring.CarsFor(race.GetOrderedStandings(), _ => "Amy", _ => false).Single();
        Assert.Equal(5, car.Laps);
        Assert.Equal(3, car.ByHand);
        Assert.Equal("5*", car.LapsText);

        var rows = RaceFiling.ResultsFrom(race.GetOrderedStandings(), ProfileStore.Load());
        Assert.Equal(3, rows.Single(r => r.Transponder == "A").LapsByHand);
    }

    private static void Seen(RaceControlSession race, string car, uint deviceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = deviceTimeMs,
            SignalHits = 60,
            SignalStrength = 247,
        });

    private static SessionResultEntry Driver(string transponder, int laps)
    {
        var times = Enumerable.Range(0, laps).Select(i => i == 0 ? 2_000L : 11_000L + i * 10).ToList();

        return new SessionResultEntry
        {
            Transponder = transponder,
            DriverName = transponder,
            Laps = laps,
            ElapsedMs = times.Sum(),
            LapTimesMs = times,
            FastLapMs = times.Skip(1).DefaultIfEmpty(0).Min(),
            Status = "active",
        };
    }

    private static SessionRecord Race(params SessionResultEntry[] results)
    {
        for (var i = 0; i < results.Length; i++)
        {
            results[i].Position = i + 1;
        }

        return new SessionRecord
        {
            Label = "A-Main",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = DateTime.UtcNow,
            Results = results.ToList(),
        };
    }
}
