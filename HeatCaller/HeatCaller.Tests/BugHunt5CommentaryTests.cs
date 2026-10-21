using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 5, the commentary layer. What the hunt found:
///
///  - The Qualifying Report's reader was wired without a round scope, so
///    QualifyingCalculator counted every heat the class had ever run and
///    the PA named the wrong TQ (the 25 Sep audit's bug, in the one call
///    site it missed). The fix is a line in MainForm, which needs a whole
///    window to drive; what is pinned down here instead is the reader
///    contract the fix leans on - the sixth argument is the only thing
///    that keeps a report inside tonight.
///  - LlmText.CleanLine took the leading "A:" off a two-hander and
///    flattened its line breaks, so CommentaryExchange.Split could never
///    find a tagged line: the co-host silently never worked locally and
///    every exchange paid for a second, single-voice write on top.
///  - The newer fact builders formatted lap times, gaps and margins on the
///    current culture, so on a comma-decimal PC "12,345s" went into the
///    prompt and came back out of the PA as "twelve thousand three hundred
///    forty-five seconds". These are the suite's first culture tests.
///  - The director re-picked the battle in Spent instead of cooling the one
///    it had just announced, marked facts as said even when the hold came
///    on mid-write, and worked out the later segments' facts after its
///    first await - off the thread every store in this app belongs to.
/// </summary>
public sealed class BugHunt5CommentaryTests : IDisposable
{
    // Declared first: the stores below, and the director's settings, load
    // from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly List<SessionRecord> _sessions = new();
    private readonly Rig _rig = new();

    private const string Stock = "Box Stock";

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    // The Qualifying Report's scope
    // ------------------------------------------------------------------

    [Fact]
    public void Qualifying_counts_only_the_rounds_it_is_scoped_to_and_everything_recorded_without_a_scope()
    {
        // A night in August, and tonight. The August driver went better.
        RunHeat(Result("A", 20, 300_000));

        var tonight = RunHeat(Result("B", 10, 300_000));

        // Scoped to tonight - what RaceProgramControl.QualifyingRoundFilter
        // hands every other caller, and what the Qualifying Report's reader
        // is now given as well.
        var scoped = CommentaryReaders.Qualifying(
            Stock, _rounds, History(), _profiles, null, new[] { tonight.Id });

        Assert.Equal(new[] { "B" }, scoped.Select(q => q.Name).ToArray());

        // With no scope the calculator counts every recorded round, which
        // is why leaving the argument off put a driver who is not even at
        // the track on provisional pole.
        var unscoped = CommentaryReaders.Qualifying(Stock, _rounds, History(), _profiles);

        Assert.Equal(new[] { "A", "B" }, unscoped.Select(q => q.Name).ToArray());
    }

    // ------------------------------------------------------------------
    // The co-host's two-hander, coming back from the local model
    // ------------------------------------------------------------------

    [Fact]
    public void A_two_hander_keeps_its_tags_and_its_line_breaks_so_the_split_can_find_them()
    {
        var tidied = LlmText.CleanLine(
            "A: Bill is all over him.\nB: And he still cannot get by.",
            maxTokens: 64,
            taggedTurns: true);

        var turns = CommentaryExchange.Split(tidied);

        Assert.Equal(2, turns.Count);
        Assert.False(turns[0].FromCoHost);
        Assert.True(turns[1].FromCoHost);
        Assert.Equal("Bill is all over him.", turns[0].Line);
        Assert.Equal("And he still cannot get by.", turns[1].Line);
    }

    [Fact]
    public void A_two_hander_written_with_windows_line_endings_still_splits()
    {
        var tidied = LlmText.CleanLine(
            "A: Four tenths, and holding.\r\nB: He has had three goes at it already.",
            maxTokens: 64,
            taggedTurns: true);

        Assert.Equal(2, CommentaryExchange.Split(tidied).Count);
    }

    [Fact]
    public void Tidied_as_one_line_a_two_hander_cannot_be_split_at_all()
    {
        // The old behaviour, kept here as the thing the flag turns off: the
        // "A:" is taken for a label and the break becomes a space, so Split
        // sees one untagged paragraph and the exchange is lost.
        var flattened = LlmText.CleanLine(
            "A: Bill is all over him.\nB: And he still cannot get by.",
            maxTokens: 64);

        Assert.NotNull(flattened);
        Assert.DoesNotContain("\n", flattened!);
        Assert.Empty(CommentaryExchange.Split(flattened));
    }

    [Fact]
    public void A_single_voice_line_is_still_stripped_of_its_label_and_joined_up_for_speech()
    {
        var tidied = LlmText.CleanLine(
            "Commentary:\nThey are nose to tail out there.\nNeither will give an inch.",
            maxTokens: 64);

        Assert.Equal("They are nose to tail out there. Neither will give an inch.", tidied);
    }

    [Fact]
    public void The_brief_for_a_two_hander_is_recognised_and_an_ordinary_segment_s_is_not()
    {
        Assert.True(LlmText.AsksForTaggedTurns(CommentaryExchange.Brief("Alex", "Dave")));

        Assert.False(LlmText.AsksForTaggedTurns(
            "Call this fight for the crowd in one sentence. Say who is ahead and by how much."));

        Assert.False(LlmText.AsksForTaggedTurns(null));
    }

    // ------------------------------------------------------------------
    // A comma-decimal PC
    //
    // The club laptop's region is whatever the laptop came with, and a
    // prompt is no place to find out: the model repeats the number it was
    // given and the PA reads out what it repeated.
    // ------------------------------------------------------------------

    [Fact]
    public void A_sign_off_on_a_comma_decimal_PC_writes_its_times_with_a_point()
    {
        InCommaDecimal(() =>
        {
            var blocks = CommentaryShowFacts.SignOff(
                new NightDone(
                    Races: 12,
                    Laps: 418,
                    FastestLapSeconds: 12.345,
                    FastestLapHolder: "Sam",
                    ClosestFinishSeconds: 0.06,
                    ClosestFinishWinner: "Bill"),
                "day-1");

            var text = string.Join(" ", blocks.Select(b => b.Text));

            Assert.Contains("12.345s", text);
            Assert.Contains("0.06s", text);
            Assert.DoesNotContain("12,345", text);
            Assert.DoesNotContain("0,06", text);
        });
    }

    [Fact]
    public void A_fight_and_a_record_lap_on_a_comma_decimal_PC_write_their_gaps_with_a_point()
    {
        InCommaDecimal(() =>
        {
            var battle = CommentaryFacts.Battle(
                new Battle("A", "B", 2, 6, TimeSpan.FromSeconds(30), 0.21, 0.34, 1),
                id => id == "A" ? "Sam" : "Bill",
                "heat-1");

            Assert.Contains("within 0.34s", battle.Text);
            Assert.Contains("0.21s", battle.Text);
            Assert.DoesNotContain("0,", battle.Text);

            var pace = CommentaryFacts.Pace(
                new PaceNote("A", 12.345, PaceNoteKind.NightBest, 12.5, "Bill"),
                id => id == "A" ? "Sam" : "Bill",
                "heat-1");

            Assert.Contains("12.345s", pace.Text);
            Assert.Contains("0.155s", pace.Text);
            Assert.Contains("12.500s", pace.Text);
            Assert.DoesNotContain(",", pace.Text);
        });
    }

    [Fact]
    public void A_qualifying_report_on_a_comma_decimal_PC_writes_its_margins_with_a_point()
    {
        InCommaDecimal(() =>
        {
            var blocks = CommentaryReportFacts.QualifyingReport(
                new[]
                {
                    new QualifyingLine("A", "Sam", 1, 12.345),
                    new QualifyingLine("B", "Bill", 2, 12.555),
                    new QualifyingLine("C", "Pat", 3, 12.900, SeasonAveragePosition: 8),
                    new QualifyingLine("D", "Jo", 4, 13.000, MadeTheCut: false),
                },
                Stock,
                "q1");

            var text = string.Join(" ", blocks.Select(b => b.Text));

            // WHY (20 Oct 2026): pole is no longer given a fast-lap "margin";
            // the pole sitter's best lap is the figure that must keep its point.
            Assert.Contains("best lap of 12.345s", text);
            Assert.Contains("about 5 places", text);
            Assert.Contains("by 0.10s", text);
            Assert.DoesNotContain("12,345", text);
            Assert.DoesNotContain("0,10", text);
        });
    }

    // ------------------------------------------------------------------
    // The director
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_cooldown_lands_on_the_fight_that_was_called_and_not_on_the_other_one()
    {
        // Two fights worth calling. Level on lead changes, so the one for
        // the lead is picked.
        TwoFights(swapped: false);

        // And while that line is being written the other pair swap places,
        // which is what makes a fresh Pick change its mind - and is just
        // what the running order does during the seconds a line takes.
        _rig.Model.WhileWriting = () => TwoFights(swapped: true, ticks: 1);

        var said = await _rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.Equal(SegmentKind.BattleWatch, said!.Kind);

        // The fight that was called is the one that was spent...
        Assert.True(_rig.Director.Said.Spent($"battle:heat-1:{CommentaryFacts.Pair("A", "B")}"));

        // ...and the fight still on offer is the pair nobody has mentioned
        // yet. Re-picking in Spent cooled that one instead and left the
        // one just called free to be called again.
        var left = _rig.Director.Battles.Pick();

        Assert.NotNull(left);
        Assert.Equal(CommentaryFacts.Pair("C", "D"), CommentaryFacts.Pair(left!.AheadId, left.BehindId));
    }

    [Fact]
    public async Task A_hold_turned_on_mid_write_spends_neither_the_budget_nor_the_facts()
    {
        AFight();

        _rig.Model.WhileWriting = () => _rig.Director.Held = true;

        Assert.Null(await _rig.Director.NextAsync());

        Assert.Equal(1, _rig.Model.Asked);
        Assert.Equal(0, _rig.Director.Budget.SpentTonight);
        Assert.Equal(0, _rig.Director.Said.Count);
        Assert.Equal(0, _rig.Director.Budget.InFlight);
        Assert.Contains(_rig.Console, line => line.Contains("held while it was being written"));

        // A fact that went unsaid is unspent, which is what Held promises:
        // the fight is still there to call once the hold comes off. Marked,
        // it was off the night for good.
        _rig.Director.Held = false;
        _rig.Model.WhileWriting = null;

        var said = await _rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.Equal(SegmentKind.BattleWatch, said!.Kind);
        Assert.Equal(1, _rig.Director.Budget.SpentTonight);
    }

    [Fact]
    public async Task Every_segment_s_facts_are_worked_out_before_the_first_request_goes_out()
    {
        // The readers walk stores that belong to the UI thread, and from
        // the first await onwards the pass is on a thread-pool thread. So
        // nothing may be read after the first request has gone out.
        var order = new List<string>();

        _rig.Director.RaceFinished();

        // Nothing comes back, so every segment in the pass is tried.
        _rig.Model.Answer = null;
        _rig.Model.WhileWriting = () => order.Add("write");

        _rig.Director.Upcoming = () =>
        {
            order.Add("read");

            return (new[] { new UpcomingRound("Q2 Heat 1", Stock, 8) }, TimeSpan.FromMinutes(4));
        };

        _rig.Director.Championship = () =>
        {
            order.Add("read");

            return new[]
            {
                new ChampionshipStanding("A", "Sam", PositionNow: 1, PositionBefore: 2, Points: 88, BehindLeader: 0),
                new ChampionshipStanding("B", "Bill", PositionNow: 2, PositionBefore: 1, Points: 85, BehindLeader: 3),
            };
        };

        Assert.Null(await _rig.Director.NextAsync());

        var firstWrite = order.IndexOf("write");

        Assert.True(firstWrite >= 0, "nothing reached the model at all");
        Assert.DoesNotContain("read", order.Skip(firstWrite));
    }

    // --- Helpers --------------------------------------------------------------------

    /// <summary>Runs the work on a PC whose region writes one and a half as 1,5.</summary>
    private static void InCommaDecimal(Action work)
    {
        var was = CultureInfo.CurrentCulture;

        CultureInfo.CurrentCulture = new CultureInfo("de-DE");

        try
        {
            work();
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    private SessionHistoryStore History() => SessionHistoryStore.FromSessions(_sessions);

    private static SessionResultEntry Result(string transponder, int laps, long elapsedMs) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
    };

    /// <summary>A qualifier as the app leaves one once raced: in the store, its session saved and linked.</summary>
    private RoundDefinition RunHeat(params SessionResultEntry[] results)
    {
        var round = new RoundDefinition { ClassName = Stock, RoundLabel = "Q1", Kind = RoundKind.Qualifier };

        _rounds.Add(round);

        var session = new SessionRecord
        {
            Label = "Q1",
            SessionType = RaceSessionType.Race,
            RoundId = round.Id,
            StartedAtUtc = DateTime.UtcNow,
            Results = results.ToList(),
        };

        _sessions.Add(session);

        Assert.True(_rounds.MarkRecorded(round.Id, session.Id));

        return round;
    }

    /// <summary>One fight, nose to tail for long enough to be worth calling.</summary>
    private void AFight()
    {
        for (var i = 0; i < 6; i++)
        {
            _rig.Director.Standings(new[] { new PairGap("A", "B", 1, 0.3, 0) });
            _rig.Clock.Advance(TimeSpan.FromSeconds(5));
        }
    }

    /// <summary>A fight for the lead and another for third, both worth calling.</summary>
    private void TwoFights(bool swapped, int ticks = 6)
    {
        for (var i = 0; i < ticks; i++)
        {
            _rig.Director.Standings(swapped
                ? new[] { new PairGap("A", "B", 1, 0.3, 0), new PairGap("D", "C", 3, 0.3, 0) }
                : new[] { new PairGap("A", "B", 1, 0.3, 0), new PairGap("C", "D", 3, 0.3, 0) });

            _rig.Clock.Advance(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 8, 19, 0, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    /// <summary>A brain the test drives, with a hook for what the race does while it writes.</summary>
    private sealed class Brain : ICommentaryBrain
    {
        private readonly Clock _clock;

        public Brain(Clock clock) => _clock = clock;

        public string? Answer { get; set; } = "They are nose to tail out there!";

        public TimeSpan Takes { get; set; } = TimeSpan.FromSeconds(2);

        public int Asked { get; private set; }

        /// <summary>Run inside the request, as the running order and the hold button both are.</summary>
        public Action? WhileWriting { get; set; }

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
            Asked++;
            _clock.Advance(Takes);

            WhileWriting?.Invoke();

            // Keeps the compiler happy about an event nothing subscribes to.
            StatusMessage?.Invoke(string.Empty);

            return Task.FromResult(Answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private sealed class Rig
    {
        public Clock Clock { get; } = new();

        public AppSettings Settings { get; } = new()
        {
            CommentaryEnabled = true,
            CommentaryMaxWords = 40,
        };

        public Brain Model { get; }

        public CommentaryDirector Director { get; }

        public List<string> Console { get; } = new();

        public Rig()
        {
            Model = new Brain(Clock);

            var commentary = new CommentaryService(ProfileStore.Load(), Settings) { Client = Model };

            Director = new CommentaryDirector(commentary, Settings, Clock.Read);

            Director.Status += Console.Add;
            Director.NameFor = id => id switch
            {
                "A" => "Sam",
                "B" => "Bill",
                "C" => "Pat",
                _ => "Jo",
            };

            Director.NewMeeting("day-1");
            Director.MonthlyUsage(0, 400);
            Director.NightShape(10, 30);
            Director.RaceStarted("heat-1", Stock);
        }
    }
}
