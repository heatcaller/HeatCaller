namespace HeatCaller.Tests;

/// <summary>
/// The information that was collected for the commentary and never reached
/// it (7 Nov 2026): what race control did to a result and whether it
/// changed who won, the handicap result, the race director's note, tonight's
/// lap targets, what is behind a championship total, a driver's place in the
/// title, a night missed, the practice and career figures, and the season's
/// own piece. Each section pins the sentence the model is handed, because a
/// fact that is worked out and then worded wrongly is the same silence with
/// extra steps.
/// </summary>
public sealed class CommentaryGapsTests : IDisposable
{
    /// <summary>Declared first: ProfileStore.Load reads from wherever AppPaths points.</summary>
    private readonly TempAppData _data = new();

    public void Dispose()
    {
        DriverTargets.ClearAll();
        _data.Dispose();
    }

    private static readonly DateTime Start = new(2026, 11, 6, 19, 0, 0, DateTimeKind.Utc);

    private static string Named(string transponder) => transponder;

    private static SessionResultEntry Row(string who, int position, int laps, long ms, long fastLap = 20_000) => new()
    {
        Transponder = who,
        DriverName = who,
        Position = position,
        Laps = laps,
        ElapsedMs = ms,
        FastLapMs = fastLap,
    };

    private static SessionRecord Race(params SessionResultEntry[] rows)
    {
        var race = new SessionRecord
        {
            Label = "Heat 3",
            SessionType = RaceSessionType.Race,
            StartedAtUtc = Start,
            EndedAtUtc = Start.AddMinutes(5),
        };

        race.Results.AddRange(rows);
        return race;
    }

    // ------------------------------------------------------------------
    //  What race control did
    // ------------------------------------------------------------------

    [Fact]
    public void Warnings_are_said_with_their_reasons_and_what_they_cost()
    {
        var sam = Row("Sam", 1, 12, 300_000);
        sam.Warnings.Add(new RaceWarning { Number = 1, Reason = "Blocking", Action = WarningAction.Warning });
        sam.Warnings.Add(new RaceWarning { Number = 2, Reason = "Blocking", Action = WarningAction.TimePenalty, PenaltyMs = 5_000 });
        sam.RacePenaltyMs = 5_000;

        var lines = CommentaryOfficialFacts.ForRace(Race(sam, Row("Bill", 2, 11, 300_000)), Named);

        var line = Assert.Single(lines);
        Assert.Equal("Sam: warning 1 for blocking; warning 2 for blocking, a 5 second penalty", line);
    }

    [Fact]
    public void A_jump_start_is_named_as_one_and_a_forgiven_one_says_no_time_was_added()
    {
        var sam = Row("Sam", 1, 12, 300_000);
        sam.JumpStarted = true;
        sam.RacePenaltyMs = 2_500;

        var bill = Row("Bill", 2, 12, 301_000);
        bill.JumpStarted = true;

        var lines = CommentaryOfficialFacts.ForRace(Race(sam, bill), Named);

        Assert.Contains("Sam: jumped the start, 2.5 seconds added", lines);
        Assert.Contains("Bill: jumped the start, no time added", lines);
    }

    [Fact]
    public void A_black_flag_is_said_once_and_not_again_as_its_own_correction()
    {
        var sam = Row("Sam", 3, 0, 300_000);
        sam.Warnings.Add(new RaceWarning { Number = 4, Reason = "Rough driving", Action = WarningAction.BlackFlag });
        sam.Outcome = ResultEditor.Dq;

        var race = Race(Row("Bill", 1, 12, 300_000), Row("Ann", 2, 12, 301_000), sam);
        race.Edits.Add(new ResultEdit { Transponder = "Sam", Change = "finisher -> DQ", Reason = "Black flag from race control (warning limit reached)" });

        var line = Assert.Single(CommentaryOfficialFacts.ForRace(race, Named));

        Assert.Equal("Sam: black-flagged on warning 4 for rough driving, and disqualified", line);
    }

    [Fact]
    public void A_correction_after_the_race_says_what_the_result_is_now_and_why()
    {
        var sam = Row("Sam", 2, 12, 305_000);
        sam.TimePenaltyMs = 5_000;
        sam.AsRaced = new ResultAsRaced { Position = 1, Laps = 12, ElapsedMs = 300_000 };

        var race = Race(Row("Bill", 1, 12, 302_000), sam);
        race.Edits.Add(new ResultEdit { Transponder = "Sam", Change = "time penalty 0s -> 5s", Reason = "Cut the chicane on lap 9" });

        var line = Assert.Single(CommentaryOfficialFacts.ForRace(race, Named));

        Assert.Equal("Sam: after the race, 5 seconds added as a penalty, because: Cut the chicane on lap 9", line);

        // Never the log's own arrow, which the prompt guard would strip to a dash.
        Assert.DoesNotContain("0s", line);
    }

    [Fact]
    public void A_clean_race_carries_no_race_control_at_all()
    {
        var race = Race(Row("Sam", 1, 12, 300_000), Row("Bill", 2, 12, 301_000));

        Assert.Empty(CommentaryOfficialFacts.ForRace(race, Named));
        Assert.Empty(CommentaryOfficialFacts.PenaltiesDecided(race, Named));
    }

    [Fact]
    public void A_penalty_that_decided_the_win_says_who_had_it_on_the_road()
    {
        // Sam crossed first by two seconds and carries five.
        var sam = Row("Sam", 2, 12, 305_000);
        sam.RacePenaltyMs = 5_000;

        var race = Race(Row("Bill", 1, 12, 302_000), sam, Row("Ann", 3, 11, 300_000));

        var lines = CommentaryOfficialFacts.PenaltiesDecided(race, Named);

        Assert.Equal(
            "Sam had the winning result on the road, but penalties dropped them down the order, so the win went to Bill.",
            Assert.Single(lines));
    }

    [Fact]
    public void A_disqualified_road_winner_is_said_to_have_been_disqualified()
    {
        var sam = Row("Sam", 3, 0, 300_000);
        sam.Outcome = ResultEditor.Dq;
        sam.AsRaced = new ResultAsRaced { Position = 1, Laps = 13, ElapsedMs = 300_000 };

        var race = Race(Row("Bill", 1, 12, 302_000), Row("Ann", 2, 12, 303_000), sam);

        Assert.Equal(
            "Sam had the winning result on the road but was disqualified, so the win went to Bill.",
            Assert.Single(CommentaryOfficialFacts.PenaltiesDecided(race, Named)));
    }

    [Fact]
    public void A_penalty_further_down_says_how_many_places_it_cost()
    {
        var ann = Row("Ann", 4, 12, 310_000);
        ann.TimePenaltyMs = 10_000;
        ann.AsRaced = new ResultAsRaced { Position = 2, Laps = 12, ElapsedMs = 300_500 };

        var race = Race(Row("Bill", 1, 12, 300_000), Row("Cy", 2, 12, 302_000), Row("Dee", 3, 12, 303_000), ann);

        Assert.Equal("Ann lost 2 places to penalties.", Assert.Single(CommentaryOfficialFacts.PenaltiesDecided(race, Named)));
    }

    [Fact]
    public void A_missed_crossing_put_back_after_the_race_is_not_a_penalty()
    {
        // Laps added by hand are a correction of the count, so the road
        // result keeps them, and nobody "lost" anything.
        var sam = Row("Sam", 1, 13, 300_000);
        sam.AddedLaps = 1;
        sam.AsRaced = new ResultAsRaced { Position = 2, Laps = 12, ElapsedMs = 300_000 };

        var race = Race(sam, Row("Bill", 2, 12, 299_000));

        Assert.Empty(CommentaryOfficialFacts.PenaltiesDecided(race, Named));
    }

    [Fact]
    public void The_race_directors_note_is_one_line_with_no_brackets()
    {
        var race = Race(Row("Sam", 1, 12, 300_000));
        race.Notes = "Damp track after the shower.\r\n<Marshal> post 3 short-handed [again]";

        Assert.Equal("Damp track after the shower. Marshal post 3 short-handed again", CommentaryOfficialFacts.Note(race));
    }

    // ------------------------------------------------------------------
    //  Handicap and lap targets
    // ------------------------------------------------------------------

    [Fact]
    public void A_handicap_class_says_who_won_on_handicap_and_who_was_first_on_the_road()
    {
        var sam = Row("Sam", 1, 12, 300_000);
        var bill = Row("Bill", 2, 11, 300_000);

        var handicap = new RaceHandicap(HandicapMode.TimeCredit, new[]
        {
            new HandicapCalculator.Adjusted(bill, 2, 1, 30, 0),
            new HandicapCalculator.Adjusted(sam, 1, 2, 0, 0),
        });

        var lines = CommentaryOfficialFacts.Handicap(handicap, Named);

        Assert.Equal("This class is scored on handicap: each driver's time credit comes off their race time.", lines[0]);
        Assert.Contains("  On handicap P1 Bill - credit 30 seconds, P2 on the road", lines);
        Assert.Contains("  On handicap P2 Sam - no credit, P1 on the road", lines);
        Assert.Equal("On handicap the race goes to Bill; Sam was first on the road.", lines[^1]);
    }

    [Fact]
    public void No_credits_means_no_handicap_lines()
    {
        var handicap = new RaceHandicap(HandicapMode.LapCredit, new[]
        {
            new HandicapCalculator.Adjusted(Row("Sam", 1, 12, 300_000), 1, 1, 0, 0),
        });

        Assert.Empty(CommentaryOfficialFacts.Handicap(handicap, Named));
        Assert.Empty(CommentaryOfficialFacts.Handicap(null, Named));
    }

    [Fact]
    public void Only_a_beaten_lap_target_is_said()
    {
        var targets = new Dictionary<string, long> { ["Sam"] = 20_500, ["Bill"] = 19_000 };

        var lines = CommentaryOfficialFacts.TargetsBeaten(
            new[] { Row("Sam", 1, 12, 300_000, fastLap: 20_100), Row("Bill", 2, 12, 301_000, fastLap: 19_800) },
            who => targets.TryGetValue(who, out var ms) ? ms : null,
            Named);

        Assert.Equal("Sam beat the target lap race control set for them tonight, 20.500s", Assert.Single(lines));
    }

    // ------------------------------------------------------------------
    //  The recap's prompt
    // ------------------------------------------------------------------

    private sealed class Scribe : ICommentaryBrain
    {
        public string Facts { get; private set; } = string.Empty;

        public event Action<string>? StatusMessage;

        public string Describe => "the test";

        public Task<string?> GenerateAsync(
            string systemPrompt,
            string userPrompt,
            int maxTokens,
            double temperature = 0.7,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            Facts = userPrompt;
            StatusMessage?.Invoke(string.Empty);
            return Task.FromResult<string?>("Here they are!");
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private static (CommentaryService Desk, Scribe Model) Desk()
    {
        var model = new Scribe();

        var desk = new CommentaryService(
            ProfileStore.Load(),
            new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60 })
        {
            Client = model,
        };

        return (desk, model);
    }

    [Fact]
    public async Task The_recap_is_handed_race_control_the_penalties_effect_the_handicap_and_the_note()
    {
        var (desk, model) = Desk();

        var sam = Row("Sam", 2, 12, 305_000);
        sam.Warnings.Add(new RaceWarning { Number = 1, Reason = "Track cutting", Action = WarningAction.TimePenalty, PenaltyMs = 5_000 });
        sam.RacePenaltyMs = 5_000;

        var bill = Row("Bill", 1, 12, 302_000);
        var dee = Row("Dee", 3, 0, 0);
        dee.Outcome = ResultEditor.Dnf;

        var race = Race(bill, sam, dee);
        race.Notes = "Track drying out through the heat.";

        var handicap = new RaceHandicap(HandicapMode.TimeCredit, new[]
        {
            new HandicapCalculator.Adjusted(sam, 2, 1, 10, 0),
            new HandicapCalculator.Adjusted(bill, 1, 2, 0, 0),
        });

        await desk.BuildRaceSummaryAsync(race, handicap: handicap);

        Assert.Contains("P3 Dee - 0 laps - did not finish", model.Facts);
        Assert.Contains("Race control in this race, exactly as recorded:\n  Sam: warning 1 for track cutting, a 5 second penalty", model.Facts);
        Assert.Contains("What the penalties did to the result:\n  Sam had the winning result on the road", model.Facts);
        Assert.Contains("On handicap the race goes to Sam; Bill was first on the road.", model.Facts);
        Assert.Contains("Race director's note on this race: \"Track drying out through the heat.\"", model.Facts);

        Assert.Contains("do not describe the race as if the road order had stood", model.Facts);
        Assert.Contains("never judge a driver by it", model.Facts);
        Assert.Contains("say who won on handicap as well as who crossed the line first", model.Facts);
        Assert.Contains("The race director's note is background", model.Facts);
    }

    [Fact]
    public async Task A_clean_race_recap_carries_none_of_it()
    {
        var (desk, model) = Desk();

        await desk.BuildRaceSummaryAsync(Race(Row("Sam", 1, 12, 300_000), Row("Bill", 2, 12, 301_000)));

        Assert.DoesNotContain("Race control in this race", model.Facts);
        Assert.DoesNotContain("What the penalties did", model.Facts);
        Assert.DoesNotContain("handicap", model.Facts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("race director", model.Facts, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Lap targets", model.Facts);
    }

    [Fact]
    public async Task A_beaten_lap_target_reaches_the_recap()
    {
        var (desk, model) = Desk();
        DriverTargets.Set("Sam", 20_500);

        await desk.BuildRaceSummaryAsync(Race(Row("Sam", 1, 12, 300_000, fastLap: 20_200), Row("Bill", 2, 12, 301_000)));

        Assert.Contains("Lap targets: Sam beat the target lap race control set for them tonight, 20.500s", model.Facts);
    }

    // ------------------------------------------------------------------
    //  The night
    // ------------------------------------------------------------------

    [Fact]
    public void The_night_says_what_race_control_decided_with_the_race_it_was_in()
    {
        var sam = Row("Sam", 2, 12, 305_000);
        sam.LapPenalty = 1;
        sam.AsRaced = new ResultAsRaced { Position = 2, Laps = 13, ElapsedMs = 305_000 };

        var heat = Race(Row("Bill", 1, 12, 302_000), sam);
        heat.Edits.Add(new ResultEdit { Transponder = "Sam", Reason = "Missed the chicane" });
        heat.Notes = "Rain stopped play for ten minutes.";

        var lines = CommentaryOfficialFacts.ForTheNight(new[] { heat }, Named);

        // What changed, and not the reason typed for it: tonight's recap is
        // the piece passed on to people who were not there.
        Assert.Contains("In Heat 3, race control changed Sam's result after the race, a 1 lap penalty.", lines);
        Assert.DoesNotContain(lines, line => line.Contains("Missed the chicane"));
        Assert.Contains("Race director's note on Heat 3: \"Rain stopped play for ten minutes.\"", lines);
    }

    [Fact]
    public void Race_control_goes_into_the_nights_recap_and_never_the_sign_off()
    {
        var night = new NightDone(4, 120, RaceControl: new[] { "In Heat 3, Sam was black-flagged for rough driving and disqualified." });

        Assert.DoesNotContain(CommentaryShowFacts.SignOff(night, "day-1"), b => b.Text.Contains("black-flagged"));

        var block = Assert.Single(CommentaryShowFacts.RaceControlTonight(night, "day-1"));
        Assert.Contains("black-flagged", block.Text);
        Assert.Equal("night:day-1:control:0", block.Key);
    }

    // ------------------------------------------------------------------
    //  The championship
    // ------------------------------------------------------------------

    [Fact]
    public void What_is_behind_a_total_is_said_only_when_there_is_something()
    {
        Assert.Equal(string.Empty, CommentaryService.BehindTheTotal(0, 0, 0, 0));
        Assert.Equal(
            "; 2 rounds dropped under the best-rounds rule, worth 14 points; 40 points carried in from before; 1 race without a finish",
            CommentaryService.BehindTheTotal(2, 14, 40, 1));

        Assert.True(CommentaryService.AnyBehindTheTotal(new[] { "P1 Sam - 88 points" + CommentaryService.BehindTheTotal(1, 0, 0, 0) }));
        Assert.False(CommentaryService.AnyBehindTheTotal(new[] { "P1 Sam - 88 points, leads the championship" }));
    }

    [Fact]
    public void The_form_guide_says_where_a_driver_stands_in_the_title()
    {
        var grid = new[]
        {
            new DriverForm("A", "Sam", 10, 3, 6, new[] { 2, 1, 4, 2 }),
            new DriverForm("B", "Bill", 10, 1, 3, new[] { 3, 5, 1, 6 }),
        };

        var stake = new ChampionshipAtStake("Winter Series", 6, "Sam", true, new[]
        {
            new ChampionshipStanding("Sam", "Sam", 1, 1, 88, 0),
        });

        var filled = CommentaryReaders.WithChampionship(grid, stake);

        Assert.Equal(1, filled[0].ChampionshipPosition);
        Assert.Null(filled[1].ChampionshipPosition);

        var sam = CommentaryHistoryFacts.FormLine(filled[0], "race-1", "Box Stock");
        var bill = CommentaryHistoryFacts.FormLine(filled[1], "race-1", "Box Stock");

        Assert.EndsWith("Sam is 1st in the championship going into this race.", sam!.Text);
        Assert.DoesNotContain("championship", bill!.Text);
    }

    // ------------------------------------------------------------------
    //  The weeks, and the rest of a driver's time here
    // ------------------------------------------------------------------

    [Fact]
    public void Missing_just_one_of_a_full_window_is_said()
    {
        var weeks = new DriverWeeks("A", "Sam", 6, 5, 1, 0, 10, false, "3 October", 0);

        var block = CommentaryWeekFacts.OneDriver(weeks, "Box Stock", "night-1");

        Assert.Equal("Sam has missed just one of the club's last six Box Stock nights.", block!.Text);

        // Two misses is not "just one".
        Assert.Null(CommentaryWeekFacts.OneDriver(new DriverWeeks("A", "Sam", 6, 4, 2, 0, 8, false, "3 October", 0), "Box Stock", "night-1"));
    }

    private static SessionRecord Session(RaceSessionType type, DateTime startUtc, string who, int laps, int lapsLed = 0)
    {
        var session = new SessionRecord { SessionType = type, StartedAtUtc = startUtc, EndedAtUtc = startUtc.AddMinutes(5) };
        session.Results.Add(new SessionResultEntry { Transponder = who, DriverName = who, Position = 1, Laps = laps, LapsLed = lapsLed });
        return session;
    }

    [Fact]
    public void Practice_this_week_comes_first_in_a_drivers_also_line()
    {
        var history = new[]
        {
            Session(RaceSessionType.Practice, Start.AddDays(-2), "Sam", 15),
            Session(RaceSessionType.Practice, Start.AddDays(-1), "Sam", 25),
            Session(RaceSessionType.Practice, Start.AddDays(-20), "Sam", 90),
        };

        Assert.Equal("forty practice laps over two practice sessions this week", DriverIntros.Career(history, "Sam", Start));
    }

    [Fact]
    public void Career_laps_led_then_days_on_track_then_tonights_target()
    {
        var led = Enumerable.Range(1, 6).Select(i => Session(RaceSessionType.Race, Start.AddDays(-7 * i), "Sam", 12, lapsLed: 10)).ToList();
        Assert.Equal("has led sixty laps in races here", DriverIntros.Career(led, "Sam", Start));

        var days = Enumerable.Range(1, 10).Select(i => Session(RaceSessionType.Race, Start.AddDays(-7 * i), "Sam", 12)).ToList();
        Assert.Equal("on track here on ten different days", DriverIntros.Career(days, "Sam", Start));

        Assert.StartsWith("chasing a target lap of", DriverIntros.Career(Array.Empty<SessionRecord>(), "Sam", Start, 20_500));
        Assert.Equal(string.Empty, DriverIntros.Career(Array.Empty<SessionRecord>(), "Sam", Start));
    }

    [Fact]
    public void The_also_line_is_labelled_in_the_intro_facts()
    {
        Assert.Equal(
            "1. grid position 1; name Sam; also: has led sixty laps in races here",
            DriverIntros.FactLine(new IntroDriver("1", 1, "Sam", "Sam", Career: "has led sixty laps in races here"), 0));
    }

    // ------------------------------------------------------------------
    //  The season's piece
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_season_piece_is_written_from_its_facts_and_its_own_instruction()
    {
        var (desk, model) = Desk();

        var facts = FactPack.Build(new[] { new FactBlock("season:1", "The Winter Series ran over six nights.") });

        await desk.BuildSeasonPieceAsync(facts);

        Assert.Contains("The Winter Series ran over six nights.", model.Facts);
        Assert.Contains(SeasonPiece.Instruction, model.Facts);
    }
}
