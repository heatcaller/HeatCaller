namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8, the AI commentary and the facts it is handed (17 Oct 2026):
///
///  - A08-1: TidyIntro rewrote an earlier race's label that the facts
///    themselves carried into the round's own label.
///  - A08-2: the budget saved once-a-night counts for lines still in the
///    air, so a restart treated them as said.
///  - A08-3: an unclosed think block from the built-in model was spoken.
///  - A08-5: the relay's line skipped LlmText.CleanLine.
///  - A09-1: the recap called a retirement in P3 a "first ever podium".
///  - A09-2: "They last met earlier today" about the night before.
///  - A09-3 / A09-4 / A09-6: the season piece's rounds counted, its
///    attendance honour on a tie, and its count of drivers scoring.
///  - A09-5: a run of wins on a layout went straight past a race a rental
///    won.
/// </summary>
public sealed class BugHunt8FCTests : IDisposable
{
    // Declared first: every store below loads from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    // A08-1: another race's label from the facts is left alone
    // ------------------------------------------------------------------

    [Fact]
    public void An_earlier_races_label_the_facts_carried_is_not_corrected_to_this_one()
    {
        var notes = new List<string>();
        var names = new[] { "Sam", "Q3 Heat 3", "Q1 Heat 1" };

        Assert.Equal(
            "Sam is 0.40s quicker than back in Q1 Heat 1.",
            CommentaryService.TidyIntro("Sam is 0.40s quicker than back in Q1 Heat 1.", "Q3 Heat 3", names, 45, notes));
        Assert.Empty(notes);

        // A heat number that is not in the facts is still put right.
        Assert.Equal(
            "Welcome to Q3 Heat 3!",
            CommentaryService.TidyIntro("Welcome to Heat 2!", "Q3 Heat 3", names, 45, notes));
        Assert.NotEmpty(notes);
    }

    // ------------------------------------------------------------------
    // A08-2: a held reservation is not saved as said
    // ------------------------------------------------------------------

    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 13, 19, 0, 0, DateTimeKind.Unspecified);

        public Func<DateTime> Read => () => Now;
    }

    private static CommentaryBudget Night(Clock clock, string? saved = null)
    {
        var budget = new CommentaryBudget(clock.Read);

        budget.Restore(saved, "day-1");
        budget.MonthlyUsage(0, 400);
        budget.NightsLeftThisMonth = 4;
        budget.NightShape(10, 30);

        return budget;
    }

    [Fact]
    public void A_cold_open_still_in_the_air_is_not_saved_as_said()
    {
        var clock = new Clock();
        var first = Night(clock);

        var held = first.Reserve(SegmentKind.ColdOpen);
        Assert.True(held.Allowed);

        // Saved while the cold open is still being written.
        var after = Night(clock, first.Save());

        Assert.True(after.Peek(SegmentKind.ColdOpen).Allowed);

        // In this process the hold still binds.
        Assert.False(first.Peek(SegmentKind.ColdOpen).Allowed);

        // Once it is said, a restart knows.
        first.Settle(held.Ticket);

        Assert.False(Night(clock, first.Save()).Peek(SegmentKind.ColdOpen).Allowed);
    }

    // ------------------------------------------------------------------
    // A08-3: thinking is never spoken
    // ------------------------------------------------------------------

    [Fact]
    public void An_unclosed_think_block_is_not_commentary()
    {
        Assert.Equal(string.Empty, BundledLlmClient.StripThinking("<think>Okay the user wants"));
        Assert.Equal("Sam wins!", BundledLlmClient.StripThinking("</think>Sam wins!"));
        Assert.Equal("Sam wins! ", BundledLlmClient.StripThinking("Sam wins! <think>hmm"));
        Assert.Equal("Sam wins!", BundledLlmClient.StripThinking("Sam wins!"));
    }

    // ------------------------------------------------------------------
    // A08-5: the relay's line is tidied like the local brains'
    // ------------------------------------------------------------------

    [Fact]
    public void The_relays_line_loses_its_label_and_quotes()
    {
        Assert.Equal(
            "Sam takes it by three tenths!",
            RelayCommentaryClient.Finish("Commentary: \"Sam takes it by three tenths!\"", "Say who won.", 60, trimCutOff: true, out var cut));
        Assert.False(cut);
    }

    [Fact]
    public void The_relay_test_buttons_OK_is_returned_as_written()
    {
        Assert.Equal("OK", RelayCommentaryClient.Finish("OK", "Reply with exactly: OK", 10, trimCutOff: false, out var cut));
        Assert.False(cut);
    }

    [Fact]
    public void A_cut_off_relay_line_is_still_kept_to_its_last_sentence()
    {
        Assert.Equal(
            "Sam takes it by three tenths!",
            RelayCommentaryClient.Finish("Sam takes it by three tenths! And Bill is", "Say who won.", 60, trimCutOff: true, out var cut));
        Assert.True(cut);
    }

    // ------------------------------------------------------------------
    // A09-1: a retirement is not a podium
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
            return Task.FromResult<string?>("And that is a wrap on the A-Main!");
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private static SessionResultEntry Finisher(string name, int position, int laps, string outcome = "") => new()
    {
        Transponder = name,
        DriverName = name,
        Position = position,
        Laps = laps,
        ElapsedMs = 300_000 + position * 1_000,
        Outcome = outcome,
    };

    private static async Task<string> RecapFacts(params SessionResultEntry[] results)
    {
        var model = new Scribe();
        var desk = new CommentaryService(
            ProfileStore.Load(),
            new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60 })
        {
            Client = model,
        };

        await desk.BuildRaceSummaryAsync(
            new SessionRecord
            {
                Label = "A-Main",
                SessionType = RaceSessionType.Race,
                StartedAtUtc = new DateTime(2026, 10, 10, 19, 0, 0, DateTimeKind.Utc),
                EndedAtUtc = new DateTime(2026, 10, 10, 19, 10, 0, DateTimeKind.Utc),
                Results = results.ToList(),
            },
            history: new RaceForm(
                new[]
                {
                    new DriverForm("Sam", "Sam", 5, 2, 3),
                    new DriverForm("Bill", "Bill", 5, 0, 0),
                    new DriverForm("Ann", "Ann", 5, 0, 0),
                },
                Array.Empty<HeadToHead>(),
                "Box Stock"));

        return model.Facts;
    }

    [Fact]
    public async Task A_car_that_never_turned_a_lap_is_not_given_a_first_podium()
    {
        var facts = await RecapFacts(Finisher("Sam", 1, 12), Finisher("Bill", 2, 12), Finisher("Ann", 3, 0));

        Assert.Contains("Bill's first ever Box Stock podium here", facts);
        Assert.DoesNotContain("Ann's first ever", facts);
    }

    [Fact]
    public async Task A_DNF_in_third_is_not_given_a_first_podium()
    {
        var facts = await RecapFacts(Finisher("Sam", 1, 12), Finisher("Bill", 2, 12), Finisher("Ann", 3, 9, "DNF"));

        Assert.DoesNotContain("Ann's first ever", facts);
    }

    [Fact]
    public async Task A_real_third_place_still_is_a_first_podium()
    {
        var facts = await RecapFacts(Finisher("Sam", 1, 12), Finisher("Bill", 2, 12), Finisher("Ann", 3, 11));

        Assert.Contains("Ann's first ever Box Stock podium here", facts);
    }

    // ------------------------------------------------------------------
    // A09-2: how long ago, counted in club nights
    // ------------------------------------------------------------------

    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void The_night_before_at_a_weekend_meeting_is_yesterday()
    {
        // Sat 21:00 to Sun 20:00: 23 hours, two club nights.
        Assert.Equal("yesterday", PastMeetings.HowLongAgo(Utc(17, 21), Utc(18, 20), TimeZoneInfo.Utc));
    }

    [Fact]
    public void Two_nights_back_is_not_yesterday()
    {
        // Fri 21:00 to Sun 20:00: 47 hours, two nights back.
        Assert.Equal("a few days ago", PastMeetings.HowLongAgo(Utc(16, 21), Utc(18, 20), TimeZoneInfo.Utc));
    }

    [Fact]
    public void A_meeting_that_runs_past_midnight_is_still_one_night()
    {
        Assert.Equal("earlier today", PastMeetings.HowLongAgo(Utc(17, 23, 30), Utc(18, 0, 45), TimeZoneInfo.Utc));
    }

    [Fact]
    public void The_night_is_read_on_the_clubs_own_clock()
    {
        // 10:00 UTC is 05:00 at UTC-5 - still Friday night there - and
        // 23:00 UTC is Saturday evening in both.
        var club = TimeZoneInfo.CreateCustomTimeZone("Club", TimeSpan.FromHours(-5), "Club", "Club");

        Assert.Equal("yesterday", PastMeetings.HowLongAgo(Utc(17, 10), Utc(17, 23), club));
        Assert.Equal("earlier today", PastMeetings.HowLongAgo(Utc(17, 10), Utc(17, 23), TimeZoneInfo.Utc));
    }

    // ------------------------------------------------------------------
    // A09-3 / A09-4 / A09-6: the season piece
    // ------------------------------------------------------------------

    private static readonly List<SessionRecord> OneRace = new()
    {
        new SessionRecord
        {
            Id = "a",
            SessionType = RaceSessionType.Race,
            Label = "Heat 1",
            TrackLayoutName = "Winter Indoor",
            StartedAtUtc = new DateTime(2026, 3, 14, 19, 0, 0, DateTimeKind.Utc),
            EndedAtUtc = new DateTime(2026, 3, 14, 19, 8, 0, DateTimeKind.Utc),
            Results = new List<SessionResultEntry>
            {
                new() { Transponder = "bill", DriverName = "Bill", Position = 1, Laps = 3, LapTimesMs = new List<long> { 10000, 9500, 9400 }, StartPosition = 1, ElapsedMs = 28900 },
                new() { Transponder = "sam", DriverName = "Sam", Position = 2, Laps = 3, LapTimesMs = new List<long> { 10000, 9600, 9500 }, StartPosition = 2, ElapsedMs = 29100 },
            },
        },
    };

    private static SeasonStandingEntry Entry(string name, int points, int wins, int rounds, int dropped = 0) => new()
    {
        DriverName = name,
        TotalPoints = points,
        Wins = wins,
        SessionsCounted = rounds,
        DroppedRounds = dropped,
    };

    [Fact]
    public void The_champions_rounds_counted_leave_out_the_dropped_ones()
    {
        var text = SeasonPiece.FactsFor(
            new Season { Name = "Winter Series", CountBestRounds = 8 },
            OneRace,
            new List<SeasonStandingEntry> { Entry("Bill", 40, 2, 12, dropped: 4), Entry("Sam", 30, 1, 10, dropped: 2) }).Text;

        Assert.Contains("from 8 rounds counted", text);
        Assert.DoesNotContain("12 rounds", text);
    }

    [Fact]
    public void A_tie_for_most_rounds_raced_names_nobody()
    {
        var text = SeasonPiece.FactsFor(
            new Season { Name = "Winter Series" },
            OneRace,
            new List<SeasonStandingEntry> { Entry("Bill", 40, 2, 6), Entry("Sam", 33, 1, 11), Entry("Ann", 20, 0, 11) }).Text;

        Assert.DoesNotContain("raced the most rounds", text);
    }

    [Fact]
    public void A_driver_on_no_points_is_not_counted_as_scoring()
    {
        var text = SeasonPiece.FactsFor(
            new Season { Name = "Winter Series" },
            OneRace,
            new List<SeasonStandingEntry> { Entry("Bill", 40, 2, 1), Entry("Sam", 0, 0, 1) }).Text;

        Assert.Contains("1 driver scoring", text);
    }

    // ------------------------------------------------------------------
    // A09-5: a rental's win breaks a run
    // ------------------------------------------------------------------

    /// <summary>Seven in the evening, which NightOf reads as that day's race night.</summary>
    private static readonly DateTime Now = new(2026, 10, 11, 19, 0, 0, DateTimeKind.Local);

    private static SessionRecord Raced(DateTime endedLocal, params (string Transponder, int Position)[] results) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        SessionType = RaceSessionType.Race,
        TrackLayoutName = "Club Oval",
        StartedAtUtc = endedLocal.ToUniversalTime().AddMinutes(-6),
        EndedAtUtc = endedLocal.ToUniversalTime(),
        Results = results
            .Select(r => new SessionResultEntry
            {
                Transponder = r.Transponder,
                DriverName = r.Transponder,
                Position = r.Position,
                FastLapMs = 11_000 + r.Position * 100,
                Laps = 12,
            })
            .ToList(),
    };

    private static ProfileStore WithARental()
    {
        var profiles = ProfileStore.Load();

        profiles.Upsert(new DriverProfile { Transponder = "A", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "B", DriverName = "Bill" });
        profiles.Upsert(new DriverProfile { Transponder = "C", DriverName = "Carl" });
        profiles.Upsert(new DriverProfile { Transponder = "R", DriverName = "Rental One", IsRental = true });

        return profiles;
    }

    [Fact]
    public void A_race_a_rental_won_breaks_the_run_of_wins()
    {
        var history = SessionHistoryStore.FromSessions(new[]
        {
            Raced(Now.AddDays(-28), ("B", 1), ("A", 2), ("C", 3)),
            Raced(Now.AddDays(-21), ("R", 1), ("B", 2), ("A", 3), ("C", 4)),
            Raced(Now.AddDays(-14), ("B", 1), ("A", 2), ("C", 3)),
            Raced(Now.AddDays(-7), ("B", 1), ("A", 2), ("C", 3)),
        });

        var legend = CommentaryReaders.LayoutStory(history, WithARental(), "Club Oval", new[] { "A" }, Now);

        Assert.NotNull(legend);
        Assert.Equal("Bill", legend!.WinnerName);
        Assert.Equal(2, legend.WinnerRun);
        Assert.True(legend.WinnerRun < CommentaryRaceFacts.LeastRunToCall);
    }

    [Fact]
    public void A_run_a_rental_holds_is_no_run_at_all()
    {
        var history = SessionHistoryStore.FromSessions(new[]
        {
            Raced(Now.AddDays(-14), ("B", 1), ("A", 2), ("C", 3)),
            Raced(Now.AddDays(-7), ("R", 1), ("B", 2), ("A", 3), ("C", 4)),
        });

        var legend = CommentaryReaders.LayoutStory(history, WithARental(), "Club Oval", new[] { "A" }, Now);

        Assert.NotNull(legend);
        Assert.Equal(string.Empty, legend!.WinnerName);
        Assert.Equal(0, legend.WinnerRun);
    }
}
