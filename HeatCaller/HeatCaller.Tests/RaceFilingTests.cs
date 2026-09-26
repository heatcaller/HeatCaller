namespace HeatCaller.Tests;

/// <summary>
/// RaceFiling.Commit - what happens when a race finishes. The rule from the
/// 25 Sep audit: nothing but the history write happens until the history
/// write has landed. A race that could not be saved leaves its heat unrun
/// and its crash journal in place, so the next launch offers it back.
/// </summary>
public sealed class RaceFilingTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly RaceJournal _journal = new();

    private readonly List<string> _steps = new();
    private readonly List<string> _log = new();

    private bool _roundLinkedWhenAnnounced;
    private bool _journalThereWhenAnnounced;

    public RaceFilingTests()
    {
        // A race in progress leaves this behind; Commit's job is to decide
        // whether it goes.
        File.WriteAllText(JournalPath, "{}");
    }

    public void Dispose() => _data.Dispose();

    private string JournalPath => _data.PathOf("race-in-progress.json");

    [Fact]
    public void A_race_that_could_not_be_saved_leaves_its_heat_unrun_and_its_journal_in_place()
    {
        var history = SessionHistoryStore.Load();
        Assert.True(history.Add(new SessionRecord { Label = "Earlier race" }));
        var heat = AddRound("Q1 Heat 1");

        RaceFiling.Outcome outcome;
        using (new FileStream(_data.PathOf("race-history.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            outcome = Commit(Race("Q1 Heat 1"), heat, history);
        }

        Assert.Equal(RaceFiling.Outcome.NotSaved, outcome);
        Assert.False(_rounds.Get(heat.Id)!.IsRecorded);
        Assert.False(RoundDefinitionStore.Load().Get(heat.Id)!.IsRecorded);
        Assert.True(File.Exists(JournalPath));
        Assert.Empty(_steps); // no badges, no summary, no "saved"
    }

    [Fact]
    public void A_history_that_could_not_be_read_at_startup_is_not_saved_over_and_the_journal_stays()
    {
        Assert.True(SessionHistoryStore.Load().Add(new SessionRecord { Label = "Last week's A-Main" }));
        var before = File.ReadAllText(_data.PathOf("race-history.json"));

        SessionHistoryStore history;
        using (new FileStream(_data.PathOf("race-history.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            history = SessionHistoryStore.Load();
        }

        var outcome = Commit(Race("Q1 Heat 1"), AddRound("Q1 Heat 1"), history);

        Assert.Equal(RaceFiling.Outcome.NotSaved, outcome);
        Assert.Equal(before, File.ReadAllText(_data.PathOf("race-history.json")));
        Assert.True(File.Exists(JournalPath));
    }

    [Fact]
    public void A_saved_race_links_its_heat_then_lets_the_journal_go()
    {
        var history = SessionHistoryStore.Load();
        var heat = AddRound("Q1 Heat 1");
        var race = Race("Q1 Heat 1");

        var outcome = Commit(race, heat, history);

        Assert.Equal(RaceFiling.Outcome.Saved, outcome);
        Assert.NotNull(SessionHistoryStore.Load().Get(race.Id));
        Assert.Equal(race.Id, RoundDefinitionStore.Load().Get(heat.Id)!.LinkedSessionId);
        Assert.False(File.Exists(JournalPath));
        Assert.Equal(new[] { "afterSaved", "beforeJournalCleared" }, _steps);
    }

    [Fact]
    public void The_page_hears_the_race_was_saved_only_after_its_heat_is_linked()
    {
        // SessionSaved rebuilds the "Round:" box from the unrun rounds. Raised
        // before the link, it re-selected the heat just run, and the next race
        // was filed against it too (25 Sep audit).
        Commit(Race("Q1 Heat 1"), AddRound("Q1 Heat 1"), SessionHistoryStore.Load());

        Assert.True(_roundLinkedWhenAnnounced);
        Assert.True(_journalThereWhenAnnounced); // and the journal is still there until the very end
    }

    [Fact]
    public void A_demo_race_is_saved_but_never_marks_a_real_heat_as_run()
    {
        var heat = AddRound("Q1 Heat 1");
        var demo = Race("[DEMO] Q1 Heat 1");
        demo.IsDemo = true;

        var outcome = Commit(demo, heat, SessionHistoryStore.Load());

        Assert.Equal(RaceFiling.Outcome.Saved, outcome);
        Assert.False(_rounds.Get(heat.Id)!.IsRecorded);
        Assert.False(File.Exists(JournalPath));
    }

    [Fact]
    public void A_heat_deleted_since_it_was_picked_is_reported_not_silently_skipped()
    {
        var gone = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 1" }; // never in the store

        var outcome = Commit(Race("Q1 Heat 1"), gone, SessionHistoryStore.Load());

        Assert.Equal(RaceFiling.Outcome.Saved, outcome);
        Assert.Contains(_log, line => line.Contains("Could NOT link"));
    }

    [Fact]
    public void A_race_with_no_heat_says_so()
    {
        var outcome = Commit(Race("Practice start"), round: null, SessionHistoryStore.Load());

        Assert.Equal(RaceFiling.Outcome.Saved, outcome);
        Assert.Contains(_log, line => line.Contains("NOT tied to a heat"));
    }

    [Fact]
    public void A_finish_call_that_throws_does_not_stop_the_race_being_filed()
    {
        var filed = false;

        RaceFiling.AfterTheFlag(
            () => throw new InvalidOperationException("a voice template with a hole in it"),
            () => filed = true,
            _log.Add);

        Assert.True(filed);
        Assert.Contains(_log, line => line.Contains("saved all the same"));
    }

    [Fact]
    public void The_finish_call_still_comes_first()
    {
        RaceFiling.AfterTheFlag(() => _steps.Add("call"), () => _steps.Add("file"), _log.Add);

        Assert.Equal(new[] { "call", "file" }, _steps);
    }

    // --- Helpers ------------------------------------------------------------------

    private RoundDefinition AddRound(string label)
    {
        var round = new RoundDefinition { ClassName = "Stock", RoundLabel = label, Kind = RoundKind.Qualifier };
        _rounds.Add(round);
        return round;
    }

    private static SessionRecord Race(string label) => new()
    {
        Label = label,
        SessionType = RaceSessionType.Race,
        StartedAtUtc = DateTime.UtcNow,
        EndedAtUtc = DateTime.UtcNow,
        Results = new List<SessionResultEntry>
        {
            new() { Position = 1, Transponder = "A", DriverName = "A", Laps = 10, ElapsedMs = 300_000 },
        },
    };

    private RaceFiling.Outcome Commit(SessionRecord record, RoundDefinition? round, SessionHistoryStore history) =>
        RaceFiling.Commit(
            record,
            round,
            false,
            true,
            history,
            _rounds,
            _journal,
            () => _steps.Add("afterSaved"),
            () =>
            {
                _steps.Add("beforeJournalCleared");
                _roundLinkedWhenAnnounced = round is not null && _rounds.Get(round.Id)?.IsRecorded == true;
                _journalThereWhenAnnounced = File.Exists(JournalPath);
            },
            _log.Add);
}
