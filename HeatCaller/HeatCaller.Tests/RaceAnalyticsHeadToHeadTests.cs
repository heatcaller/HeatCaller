namespace HeatCaller.Tests;

/// <summary>
/// Head to Head's who-beats-whom grid and matchup card (13 Nov 2026, the
/// Race Analytics rework): which races and drivers count, the squares and
/// their tints, the sentence the card reads out, and the page built from a
/// club's history.
/// </summary>
public sealed class RaceAnalyticsHeadToHeadTests
{
    private static readonly DateTime Start = new(2026, 8, 7, 19, 0, 0, DateTimeKind.Local);

    /// <summary>A race on Club Infield: each driver's place, best lap and laps (a start entry first).</summary>
    private static SessionRecord Race(DateTime whenLocal, params (string Name, int Position, long BestMs)[] results) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionType = RaceSessionType.Race,
        TrackLayoutName = "Club Infield",
        StartedAtUtc = whenLocal.ToUniversalTime(),
        EndedAtUtc = whenLocal.ToUniversalTime().AddMinutes(5),
        Results = results.Select(r => new SessionResultEntry
        {
            Transponder = r.Name.StartsWith(Ghosts.Prefix, StringComparison.Ordinal) ? r.Name : "T-" + r.Name,
            DriverName = r.Name,
            Position = r.Position,
            FastLapMs = r.BestMs,
            Laps = 12,
            LapTimesMs = new List<long> { 3_000 }.Concat(Enumerable.Repeat(r.BestMs, 11)).ToList(),
        }).ToList(),
    };

    // --- Which races count ---------------------------------------------------

    [Fact]
    public void Demo_races_ghosts_and_cars_that_never_moved_are_left_out_and_the_field_closes_up()
    {
        var real = Race(Start, (Ghosts.Prefix + "1", 1, 13_000), ("Sam", 2, 14_000), ("Leo", 3, 14_100), ("Kit", 4, 14_200));
        real.Results.Single(r => r.DriverName == "Kit").Laps = 0;

        var demo = Race(Start.AddDays(7), ("Leo", 1, 14_000), ("Sam", 2, 14_100));
        demo.IsDemo = true;

        var fields = RaceCoach.Fields(new[] { demo, real });

        var field = Assert.Single(fields);
        Assert.Equal(new[] { "Sam", "Leo" }, field.Finishers.Select(f => f.Name));
        Assert.Equal(1, field.Find("sam")!.Place);
        Assert.Equal(2, field.Find("Leo")!.Place);
    }

    [Fact]
    public void Since_this_year_leaves_out_last_year_s_races()
    {
        var now = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Local);
        var old = Race(new DateTime(2025, 12, 20, 19, 0, 0, DateTimeKind.Local), ("Sam", 1, 14_000), ("Leo", 2, 14_100));
        var recent = Race(new DateTime(2026, 10, 1, 19, 0, 0, DateTimeKind.Local), ("Leo", 1, 14_000), ("Sam", 2, 14_100));

        Assert.Equal(2, RaceCoach.Fields(new[] { old, recent }, RaceCoach.SinceUtc(RaceCoach.Since.AllTime, now)).Count);
        Assert.Single(RaceCoach.Fields(new[] { old, recent }, RaceCoach.SinceUtc(RaceCoach.Since.ThisYear, now)));
        Assert.Single(RaceCoach.Fields(new[] { old, recent }, RaceCoach.SinceUtc(RaceCoach.Since.LastThreeMonths, now)));
    }

    // --- The grid -------------------------------------------------------------

    [Fact]
    public void Each_square_is_the_row_driver_s_wins_and_the_grid_is_strongest_first()
    {
        var fields = RaceCoach.Fields(new[]
        {
            Race(Start, ("Priya", 1, 13_900), ("Sam", 2, 14_000), ("Leo", 3, 13_950)),
            Race(Start.AddDays(7), ("Priya", 1, 13_900), ("Leo", 2, 13_950), ("Sam", 3, 14_000)),
            Race(Start.AddDays(14), ("Sam", 1, 14_000), ("Priya", 2, 13_900), ("Leo", 3, 13_950)),
        });

        var m = RaceCoach.BuildMatrix(fields, RaceCoach.Beat.FinishedAhead);

        Assert.Equal(new[] { "Priya", "Sam", "Leo" }, m.Drivers);

        var priya = m.IndexOf("Priya");
        var sam = m.IndexOf("Sam");
        var leo = m.IndexOf("Leo");

        Assert.Equal(new RaceCoach.Cell(2, 1), m[priya, sam]);
        Assert.Equal(new RaceCoach.Cell(1, 2), m[sam, priya]);
        Assert.Equal(new RaceCoach.Cell(2, 1), m[sam, leo]);
        Assert.Equal(new RaceCoach.Cell(0, 0), m[leo, leo]);

        // On the quicker best lap the order changes: Priya, then Leo, then Sam.
        var laps = RaceCoach.BuildMatrix(fields, RaceCoach.Beat.QuickerBestLap);

        Assert.Equal(new[] { "Priya", "Leo", "Sam" }, laps.Drivers);
        Assert.Equal(new RaceCoach.Cell(3, 0), laps[laps.IndexOf("Leo"), laps.IndexOf("Sam")]);
    }

    [Fact]
    public void A_tied_best_lap_counts_for_neither()
    {
        var fields = RaceCoach.Fields(new[]
        {
            Race(Start, ("Sam", 1, 14_000), ("Leo", 2, 14_000)),
            Race(Start.AddDays(7), ("Sam", 1, 14_000), ("Leo", 2, 13_900)),
            Race(Start.AddDays(14), ("Sam", 1, 14_000), ("Leo", 2, 13_900)),
        });

        var m = RaceCoach.BuildMatrix(fields, RaceCoach.Beat.QuickerBestLap);

        Assert.Equal(new RaceCoach.Cell(2, 0), m[m.IndexOf("Leo"), m.IndexOf("Sam")]);
    }

    [Fact]
    public void A_driver_needs_three_races_with_someone_to_be_a_regular()
    {
        var fields = RaceCoach.Fields(new[]
        {
            Race(Start, ("Sam", 1, 14_000), ("Leo", 2, 14_100), ("Kit", 3, 14_200)),
            Race(Start.AddDays(7), ("Sam", 1, 14_000), ("Leo", 2, 14_100), ("Kit", 3, 14_200)),
            Race(Start.AddDays(14), ("Sam", 1, 14_000), ("Leo", 2, 14_100)),
            Race(Start.AddDays(21), ("Kit", 1, 14_200)),
        });

        var m = RaceCoach.BuildMatrix(fields, RaceCoach.Beat.FinishedAhead);

        Assert.Equal(new[] { "Sam", "Leo" }, m.Drivers);
        Assert.Empty(m.Others);
    }

    [Fact]
    public void Past_the_cap_the_rest_are_named_rather_than_shown()
    {
        var fields = RaceCoach.Fields(Sample());

        var m = RaceCoach.BuildMatrix(fields, RaceCoach.Beat.FinishedAhead, most: 4);

        Assert.Equal(4, m.Drivers.Count);
        Assert.Equal(2, m.Others.Count);
        Assert.Empty(m.Drivers.Intersect(m.Others));
    }

    [Fact]
    public void A_driver_kept_out_of_the_records_is_kept_out_of_the_grid()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "T-Kit", DriverName = "Kit", ExcludeFromRecords = true });

        var fields = RaceCoach.Fields(new[]
        {
            Race(Start, ("Kit", 1, 13_000), ("Sam", 2, 14_000), ("Leo", 3, 14_100)),
            Race(Start.AddDays(7), ("Kit", 1, 13_000), ("Leo", 2, 14_000), ("Sam", 3, 14_100)),
            Race(Start.AddDays(14), ("Kit", 1, 13_000), ("Sam", 2, 14_000), ("Leo", 3, 14_100)),
        });

        var m = RaceCoach.BuildMatrix(fields, RaceCoach.Beat.FinishedAhead, profiles);

        Assert.Equal(new[] { "Sam", "Leo" }, m.Drivers);
        Assert.Equal(new RaceCoach.Cell(2, 1), m[m.IndexOf("Sam"), m.IndexOf("Leo")]);
    }

    [Theory]
    [InlineData(6, 4, RaceCoach.Tint.Winning)]
    [InlineData(5, 4, RaceCoach.Tint.Even)]
    [InlineData(5, 5, RaceCoach.Tint.Even)]
    [InlineData(4, 6, RaceCoach.Tint.Losing)]
    [InlineData(0, 3, RaceCoach.Tint.Losing)]
    [InlineData(0, 0, RaceCoach.Tint.None)]
    public void A_square_is_green_at_60_percent_red_at_40_and_amber_between(int wins, int losses, RaceCoach.Tint tint)
    {
        Assert.Equal(tint, RaceCoach.TintOf(new RaceCoach.Cell(wins, losses)));
    }

    // --- The pair -------------------------------------------------------------

    [Fact]
    public void A_pair_counts_finishes_quicker_laps_fastest_of_the_race_and_the_last_five()
    {
        var sessions = new List<SessionRecord>();

        // Seven races: Leo quicker on the lap every time, Sam ahead in five.
        for (var i = 0; i < 7; i++)
        {
            var samAhead = i is not (1 or 3);
            sessions.Add(Race(Start.AddDays(7 * i),
                ("Priya", 1, 13_800),
                ("Sam", samAhead ? 2 : 3, 14_200),
                ("Leo", samAhead ? 3 : 2, 14_000)));
        }

        // A fastest lap of the race for Leo once, when Priya was away.
        sessions.Add(Race(Start.AddDays(70), ("Leo", 1, 13_700), ("Sam", 2, 14_200)));

        var pair = RaceCoach.Pair(RaceCoach.Fields(sessions), "Sam", "Leo");

        Assert.Equal(8, pair.Shared);
        Assert.Equal((5, 3), (pair.AheadA, pair.AheadB));
        Assert.Equal((0, 8), (pair.QuickerA, pair.QuickerB));
        Assert.Equal((0, 1), (pair.FastestOfRaceA, pair.FastestOfRaceB));
        Assert.Equal((3, 2), (pair.LastFiveA, pair.LastFiveB));
        Assert.Equal("Club Infield", pair.Track);
        Assert.Equal((14_200L, 13_700L), (pair.BestA, pair.BestB));
        Assert.Equal("Leo 8–0", RaceCoach.Leads("Sam", "Leo", pair.QuickerA, pair.QuickerB));
        Assert.Equal("Sam 5–3", RaceCoach.Leads("Sam", "Leo", pair.AheadA, pair.AheadB));
    }

    [Fact]
    public void The_best_lap_gap_is_in_car_lengths_when_the_track_has_a_length()
    {
        var pair = new RaceCoach.PairRecord { A = "Sam Okafor", B = "Leo Martins", BestA = 15_300, BestB = 15_000 };

        // 0.3 s of a 15 s lap on a track 120 cars long (180 ft of 1/10): 2.4 lengths.
        Assert.Equal("Leo by 0.300s, 2 car lengths", RaceCoach.BestLapGap(pair, 120));
        Assert.Equal("Leo by 0.300s", RaceCoach.BestLapGap(pair, null));
        Assert.Null(RaceCoach.BestLapGap(pair with { BestA = 0 }, 120));
    }

    [Fact]
    public void Two_drivers_with_the_same_first_name_are_called_by_their_whole_names()
    {
        Assert.Equal(("Sam", "Leo"), RaceCoach.ShortNames("Sam Okafor", "Leo Martins"));
        Assert.Equal(("Sam Okafor", "Sam Reid"), RaceCoach.ShortNames("Sam Okafor", "Sam Reid"));
    }

    // --- The sentence ---------------------------------------------------------

    [Fact]
    public void Pace_one_way_and_results_the_other_is_the_story_and_the_mistakes_say_why()
    {
        var pair = new RaceCoach.PairRecord
        {
            A = "Sam Okafor", B = "Leo Martins", Shared = 14,
            AheadA = 9, AheadB = 5, QuickerA = 3, QuickerB = 11,
            MistakesA = 0.5, MistakesB = 1.6,
        };

        Assert.Equal(
            "Leo has had the quicker lap in 11 of 14 races. Sam finished ahead in 9, because Leo's races have more mistakes in them.",
            RaceCoach.Reading(pair));

        // As tidy as each other: the results are racecraft.
        Assert.EndsWith("so Sam is winning on racecraft, not raw speed.", RaceCoach.Reading(pair with { MistakesB = 0.6 }));
    }

    [Fact]
    public void One_driver_with_the_pace_and_the_results_hears_the_gap_in_car_lengths()
    {
        var pair = new RaceCoach.PairRecord
        {
            A = "Priya Shah", B = "Sam Okafor", Shared = 10,
            AheadA = 8, AheadB = 2, QuickerA = 9, QuickerB = 1,
            Track = "Club Infield", BestA = 14_000, BestB = 14_300,
        };

        Assert.Equal(
            "Priya has the pace and the results: quicker lap in 9 of 10, ahead in 8 of 10. On Club Infield the best laps are 3 car lengths apart.",
            RaceCoach.Reading(pair, 140));
        Assert.EndsWith("ahead in 8 of 10.", RaceCoach.Reading(pair));
    }

    [Fact]
    public void Too_few_races_and_none_at_all_say_so()
    {
        var pair = new RaceCoach.PairRecord { A = "Sam", B = "Leo", Shared = 2, AheadA = 2 };

        Assert.Equal("Only 2 races together so far. Too few to call.", RaceCoach.Reading(pair));
        Assert.Equal("Sam and Leo have not raced each other yet.", RaceCoach.Reading(pair with { Shared = 0, AheadA = 0 }));
    }

    [Fact]
    public void A_close_rivalry_reads_as_close()
    {
        var pair = new RaceCoach.PairRecord { A = "Sam", B = "Leo", Shared = 10, AheadA = 5, AheadB = 5, QuickerA = 5, QuickerB = 5 };

        Assert.StartsWith("Dead level at the flag, 5–5", RaceCoach.Reading(pair));
        Assert.DoesNotContain("—", RaceCoach.Reading(pair));
    }

    // --- The page -------------------------------------------------------------

    /// <summary>
    /// A club's season: six drivers, fifteen races on Club Infield, the
    /// finishing order from each one's total time, so it varies with the
    /// laps. Leo is quick and crashes; Priya is quick and tidy.
    /// </summary>
    internal static List<SessionRecord> Sample()
    {
        var random = new Random(7);
        var drivers = new (string Name, long Pace, double Crashes)[]
        {
            ("Priya Shah", 13_900, 0.3), ("Leo Martins", 13_860, 1.6), ("Sam Okafor", 13_960, 0.6),
            ("Dana Reyes", 14_020, 0.5), ("Tom Webb", 14_080, 0.4), ("Kit Moss", 14_160, 0.9),
        };

        var sessions = new List<SessionRecord>();

        for (var race = 0; race < 15; race++)
        {
            var entries = new List<(string Name, List<long> Laps)>();

            foreach (var (name, pace, crashes) in drivers)
            {
                // Kit missed a few nights.
                if (name == "Kit Moss" && race % 3 == 1)
                {
                    continue;
                }

                var laps = new List<long> { 4_000 + random.Next(0, 600) };

                for (var lap = 0; lap < 20; lap++)
                {
                    var time = pace + random.Next(-150, 350) - race * 6;

                    if (random.NextDouble() < crashes / 20)
                    {
                        time += 2_500 + random.Next(0, 1_500);
                    }

                    laps.Add(time);
                }

                entries.Add((name, laps));
            }

            var order = entries.OrderBy(e => e.Laps.Sum()).ToList();

            sessions.Add(new SessionRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionType = RaceSessionType.Race,
                TrackLayoutName = "Club Infield",
                StartedAtUtc = Start.AddDays(race * 6).ToUniversalTime(),
                EndedAtUtc = Start.AddDays(race * 6).AddMinutes(6).ToUniversalTime(),
                Results = order.Select((e, i) => new SessionResultEntry
                {
                    Position = i + 1,
                    Transponder = "T-" + e.Name,
                    DriverName = e.Name,
                    Laps = e.Laps.Count,
                    LapTimesMs = e.Laps,
                    FastLapMs = e.Laps.Skip(1).Min(),
                    ElapsedMs = e.Laps.Sum(),
                }).ToList(),
            });
        }

        return sessions;
    }

    [Fact]
    public void The_page_opens_on_the_grid_with_the_top_two_in_the_card_and_the_details_kept()
    {
        var seen = StaThread.Run(() =>
        {
            using var page = new HeadToHeadControl(SessionHistoryStore.FromSessions(Sample()));

            var matrix = page.Matrix!;
            var showing = page.Showing;
            var details = page.DetailRows;
            var deciding = page.Deciding;

            // A square clicked opens that pair, the row driver first.
            page.Grid.ClickSquare(2, 4);
            var clicked = page.Showing;
            var clickedDetails = page.DetailRows;

            return (matrix, showing, details, deciding, clicked, clickedDetails, names: page.PickerNames);
        }, TimeSpan.FromSeconds(60), () => "building Head to Head");

        Assert.Equal(6, seen.matrix.Drivers.Count);
        Assert.Equal((seen.matrix.Drivers[0], seen.matrix.Drivers[1]), (seen.showing.A, seen.showing.B));
        Assert.Matches("^\\d+–\\d+$", seen.showing.Score);
        Assert.False(string.IsNullOrWhiteSpace(seen.showing.Reading));
        Assert.DoesNotContain(seen.deciding, d => string.IsNullOrWhiteSpace(d));

        // The full comparison and the races one by one are still there.
        Assert.True(seen.details.Comparison >= 19, $"comparison rows: {seen.details.Comparison}");
        Assert.True(seen.details.Races > 0);

        Assert.Equal((seen.matrix.Drivers[2], seen.matrix.Drivers[4]), (seen.clicked.A, seen.clicked.B));
        Assert.True(seen.clickedDetails.Races > 0);
        Assert.Equal(6, seen.names.Count);
    }

    [Fact]
    public void A_hidden_driver_is_out_of_the_grid_but_can_still_be_picked()
    {
        var seen = StaThread.Run(() =>
        {
            var profiles = ProfileStore.Load();
            profiles.Upsert(new DriverProfile { Transponder = "T-Kit Moss", DriverName = "Kit Moss", ExcludeFromRecords = true });

            using var page = new HeadToHeadControl(SessionHistoryStore.FromSessions(Sample())) { Profiles = profiles };
            page.RefreshAll();

            return (page.Matrix!.Drivers.ToList(), page.PickerNames.ToList());
        }, TimeSpan.FromSeconds(60), () => "building Head to Head with a hidden driver");

        Assert.Equal(5, seen.Item1.Count);
        Assert.DoesNotContain("Kit Moss", seen.Item1);
        Assert.Contains("Kit Moss", seen.Item2);
    }

    [Fact]
    public void Counting_the_quicker_best_lap_recounts_the_grid_and_the_score()
    {
        var seen = StaThread.Run(() =>
        {
            using var page = new HeadToHeadControl(SessionHistoryStore.FromSessions(Sample()));

            page.Grid.ClickSquare(0, 1);
            var a = page.Showing.A;
            var b = page.Showing.B;

            page.Choose(RaceCoach.Beat.QuickerBestLap, RaceCoach.Since.AllTime);

            return (a, b, page.Showing, page.Matrix!);
        }, TimeSpan.FromSeconds(60), () => "recounting Head to Head");

        var pair = RaceCoach.Pair(RaceCoach.Fields(Sample()), seen.a, seen.b);

        Assert.Equal((seen.a, seen.b), (seen.Showing.A, seen.Showing.B));
        Assert.Equal($"{pair.QuickerA}–{pair.QuickerB}", seen.Showing.Score);
        Assert.Equal(RaceCoach.BuildMatrix(RaceCoach.Fields(Sample()), RaceCoach.Beat.QuickerBestLap).Drivers, seen.Item4.Drivers);
    }

    [Fact]
    public void With_no_races_the_page_says_so()
    {
        var reading = StaThread.Run(() =>
        {
            using var page = new HeadToHeadControl(SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()));
            return page.Showing.Reading;
        }, TimeSpan.FromSeconds(60), () => "building an empty Head to Head");

        Assert.StartsWith("No saved races for real drivers yet.", reading);
    }
}
