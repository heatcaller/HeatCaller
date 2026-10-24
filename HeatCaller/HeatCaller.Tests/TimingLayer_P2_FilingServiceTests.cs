namespace HeatCaller.Tests;

/// <summary>
/// P2 of the timing layer (23 Oct 2026): filing a finished race moved out of
/// Race Control into RaceFilingService, which holds no WinForms control and so
/// can be driven here with a real session, real stores and the page's parts
/// as plain callbacks. The order of the history write itself is
/// RaceFiling.Commit's, pinned in RaceFilingTests; these pin what the service
/// does around it - the page's callbacks in their old order, the session
/// start, and the unfiled-race list and its retries.
/// </summary>
public sealed class TimingLayer_P2_FilingServiceTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly TrackLayoutStore _trackLayouts = TrackLayoutStore.Load();
    private readonly AppSettings _settings = new();
    private readonly RaceControlSession _race = new();
    private readonly RaceTiming _timing;
    private readonly RaceJournalRecorder _recorder;

    private readonly List<string> _steps = new();
    private readonly List<string> _log = new();

    public TimingLayer_P2_FilingServiceTests()
    {
        _timing = new RaceTiming(_race);
        _recorder = new RaceJournalRecorder(_timing);

        // A race in progress leaves this behind; the filing decides whether it goes.
        File.WriteAllText(JournalPath, "{}");
    }

    public void Dispose()
    {
        // However a test ended, the swap goes back to the real one.
        SafeFile.ReplaceFile = RealReplace;
        _data.Dispose();
    }

    private string JournalPath => _data.PathOf("race-in-progress.json");

    // --- A race that files ----------------------------------------------------------

    [Fact]
    public void A_finished_race_is_saved_against_its_heat_and_the_page_hears_in_the_old_order()
    {
        var history = SessionHistoryStore.Load();
        var heat = AddRound("Q1 Heat 1");
        var filing = Service(history);
        RunARace();
        _recorder.SessionStartedAtUtc = new DateTime(2026, 10, 23, 19, 0, 0, DateTimeKind.Utc);

        filing.FileCompletedRace(Input(heat));

        var saved = Assert.Single(SessionHistoryStore.Load().All, s => s.Label == "Q1 Heat 1");
        Assert.Equal(heat.Id, saved.RoundId);
        Assert.Equal("Oval", saved.TrackLayoutName);
        Assert.Equal(new DateTime(2026, 10, 23, 19, 0, 0, DateTimeKind.Utc), saved.StartedAtUtc);
        Assert.Equal(2, saved.Results.Count);
        Assert.Equal(saved.Id, RoundDefinitionStore.Load().Get(heat.Id)!.LinkedSessionId);
        Assert.False(File.Exists(JournalPath));

        // The session start is let go once the write has landed.
        Assert.Null(_recorder.SessionStartedAtUtc);
        Assert.False(filing.HasUnfiledRaces);

        Assert.Equal(
            new[]
            {
                "ClearRaceSummary", "NoteGhostsBeaten", "TakeRound", "BuildLabel", "ApplyBlackFlags", "BeforeCommit",
                "Filed", "AwardAchievements", "SpeakSummary", "SaveResultsCard", "SessionSaved", "FilingFinished",
            },
            _steps);
    }

    [Fact]
    public void A_race_with_no_session_start_falls_back_to_the_race_start_and_says_so()
    {
        var filing = Service(SessionHistoryStore.Load());
        RunARace();
        Assert.Null(_recorder.SessionStartedAtUtc);

        filing.FileCompletedRace(Input(round: null));

        Assert.Contains(_log, line => line.Contains("Start time was missing"));
        Assert.Single(SessionHistoryStore.Load().All);
    }

    [Fact]
    public void A_quick_race_is_never_filed_against_the_next_heat()
    {
        var heat = AddRound("Q1 Heat 1");
        var filing = Service(SessionHistoryStore.Load());
        RunARace();

        filing.FileCompletedRace(Input(round: null, wasQuickRace: true, pickNext: heat));

        Assert.DoesNotContain("PickNextRound", _steps);
        Assert.Contains(_log, line => line.Contains("Quick Race - kept in Race History on its own"));
        Assert.False(RoundDefinitionStore.Load().Get(heat.Id)!.IsRecorded);
    }

    [Fact]
    public void With_no_round_picked_the_next_heat_is_taken_and_said()
    {
        var heat = AddRound("Q1 Heat 1");
        var filing = Service(SessionHistoryStore.Load());
        RunARace();

        filing.FileCompletedRace(Input(round: null, pickNext: heat));

        Assert.Contains("PickNextRound", _steps);
        Assert.True(RoundDefinitionStore.Load().Get(heat.Id)!.IsRecorded);
        Assert.Contains(_log, line => line.Contains("assigned to the next one in the program"));
    }

    // --- The ways out that save nothing ---------------------------------------------

    [Fact]
    public void Practice_is_not_saved_and_its_journal_goes()
    {
        var filing = Service(SessionHistoryStore.Load());
        RunARace();
        _race.IsPracticeMode = true;

        filing.FileCompletedRace(Input(round: null));

        Assert.Contains(_log, line => line.Contains("Practice session - not auto-saved"));
        Assert.False(File.Exists(JournalPath));
        Assert.Empty(_steps);
    }

    [Fact]
    public void An_unsaved_demo_signals_the_end_of_the_broadcast_and_lets_its_round_go()
    {
        var filing = Service(SessionHistoryStore.Load());
        RunARace();

        filing.FileCompletedRace(Input(round: null, isDemo: true));

        Assert.Equal(new[] { "ClearRaceSummary", "SignalBroadcastOver", "DemoOver" }, _steps);
        Assert.Contains(_log, line => line.Contains("Demo race finished but was NOT saved"));
        Assert.False(File.Exists(JournalPath));
    }

    [Fact]
    public void A_race_nobody_crossed_in_is_not_saved()
    {
        var filing = Service(SessionHistoryStore.Load());
        _race.Stage();
        _race.GreenFlag(0);

        filing.FileCompletedRace(Input(round: null));

        Assert.Contains(_log, line => line.Contains("no racers had any recorded laps"));
        Assert.Contains("SignalBroadcastOver", _steps);
        Assert.False(File.Exists(JournalPath));
    }

    // --- A race that could not be filed ---------------------------------------------

    [Fact]
    public void A_race_that_could_not_be_written_is_kept_its_journal_stays_and_the_operator_is_told()
    {
        var history = HistoryWithAnEarlierRace();
        var heat = AddRound("Q1 Heat 1");
        var filing = Service(history);
        RunARace();
        _recorder.SessionStartedAtUtc = DateTime.UtcNow;
        string? caption = null;

        using (HoldHistory())
        {
            filing.FileCompletedRace(Input(heat) with { ShowNotSaved = (_, c) => caption = c });
        }

        Assert.True(filing.HasUnfiledRaces);
        Assert.Equal(1, filing.UnfiledRaceCount);
        Assert.Equal("Race not saved", caption);
        Assert.True(File.Exists(JournalPath));
        Assert.False(RoundDefinitionStore.Load().Get(heat.Id)!.IsRecorded);
        Assert.Null(_recorder.SessionStartedAtUtc);
        Assert.Contains(_log, line => line.Contains("COULD NOT SAVE \"Q1 Heat 1\""));

        // No badges, no recap - but the broadcast is still signalled as over,
        // and the filing still let go of its recap and its Quick Race stamp.
        Assert.DoesNotContain("AwardAchievements", _steps);
        Assert.DoesNotContain("SpeakSummary", _steps);
        Assert.Contains("SignalBroadcastOver", _steps);
        Assert.Contains("FilingFinished", _steps);
    }

    [Fact]
    public void A_race_that_could_not_be_written_is_filed_as_the_next_race_writes_its_first_journal()
    {
        var history = HistoryWithAnEarlierRace();
        var heat = AddRound("Q1 Heat 1");
        var filing = Service(history);
        _recorder.BeforeEachWrite = filing.RetryUnfiledBeforeJournal;
        RunARace();
        _recorder.SessionStartedAtUtc = DateTime.UtcNow;

        using (HoldHistory())
        {
            filing.FileCompletedRace(Input(heat));
        }

        Assert.True(filing.HasUnfiledRaces);

        // The next race is staged; its first journal write is what would
        // replace the failed race's journal, so the retry comes first.
        _race.Reset();
        _race.Stage();
        _steps.Clear();
        _recorder.Write(force: true);

        Assert.False(filing.HasUnfiledRaces);
        Assert.Contains(SessionHistoryStore.Load().All, s => s.Label == "Q1 Heat 1");
        Assert.True(RoundDefinitionStore.Load().Get(heat.Id)!.IsRecorded);
        Assert.Contains(_log, line => line.Contains("which could not be saved earlier, is now in the race history"));

        // Quiet while the next race is being staged: no page refresh, no badges.
        Assert.Empty(_steps);
    }

    [Fact]
    public void The_retry_at_the_next_race_is_tried_once_for_that_race_and_warns_once()
    {
        var history = HistoryWithAnEarlierRace();
        var filing = Service(history);
        _recorder.BeforeEachWrite = filing.RetryUnfiledBeforeJournal;
        RunARace();

        using (HoldHistory())
        {
            filing.FileCompletedRace(Input(round: null));

            _race.Reset();
            _race.Stage();
            _recorder.Write(force: true);
            _recorder.Write(force: true);
        }

        Assert.True(filing.HasUnfiledRaces);
        Assert.Single(_log, line => line.Contains("[session] WARNING: 1 earlier race still could not be saved"));
    }

    [Fact]
    public void A_race_that_could_not_be_written_is_filed_after_the_next_race_files()
    {
        var history = HistoryWithAnEarlierRace();
        var filing = Service(history);
        RunARace();

        using (HoldHistory())
        {
            filing.FileCompletedRace(Input(round: null, label: "A-Main"));
        }

        Assert.True(filing.HasUnfiledRaces);

        // The next race files; the earlier one goes in straight after it,
        // and the pages hear of it too.
        _race.Reset();
        RunARace();
        _steps.Clear();
        filing.FileCompletedRace(Input(round: null, label: "B-Main"));

        Assert.False(filing.HasUnfiledRaces);
        var labels = SessionHistoryStore.Load().All.Select(s => s.Label).ToList();
        Assert.Contains("A-Main", labels);
        Assert.Contains("B-Main", labels);
        Assert.Equal(2, _steps.Count(step => step == "SessionSaved"));
    }

    [Fact]
    public void While_a_race_is_unfiled_its_journal_is_never_cleared()
    {
        var history = HistoryWithAnEarlierRace();
        var filing = Service(history);
        RunARace();

        using (HoldHistory())
        {
            filing.FileCompletedRace(Input(round: null));
        }

        // Practice, an unsaved demo and Reset all clear the journal - but
        // not while it is the only copy of a race on disk.
        _race.IsPracticeMode = true;
        filing.FileCompletedRace(Input(round: null));
        _race.IsPracticeMode = false;
        filing.ClearJournalUnlessUnfiled();

        Assert.True(File.Exists(JournalPath));
    }

    [Fact]
    public void A_race_deleted_from_history_while_unfiled_is_never_brought_back()
    {
        var history = HistoryWithAnEarlierRace();
        var filing = Service(history);
        RunARace();
        SessionRecord? filed = null;

        using (HoldHistory())
        {
            filing.FileCompletedRace(Input(round: null) with { BeforeCommit = (record, _) => filed = record });
        }

        Assert.NotNull(filed);
        history.Delete(filed!.Id);

        _race.Reset();
        _race.Stage();
        filing.RetryUnfiledBeforeJournal();

        Assert.False(filing.HasUnfiledRaces);
        Assert.DoesNotContain(SessionHistoryStore.Load().All, s => s.Id == filed.Id);
    }

    // --- Helpers ----------------------------------------------------------------------

    /// <summary>The swap SafeFile makes, as it was before any test here changed it.</summary>
    private static readonly Action<string, string> RealReplace = SafeFile.ReplaceFile;

    // WHY (24 Oct 2026, timing bug hunt): an explicit static constructor, so
    // RealReplace is taken when the class is first used - before any test
    // swaps the hook. Without it the field could be filled lazily inside
    // HoldHistory's own lambda, capture that lambda, and recurse for ever.
    static TimingLayer_P2_FilingServiceTests()
    {
    }

    /// <summary>
    /// Makes every write of race-history.json fail until disposed, as a sync
    /// tool holding the file does - through SafeFile's own test hook, the way
    /// BugHunt10_RelaySignupDeskTests and BugHunt4DataTests do it. A handle
    /// opened with FileShare.None does this on Windows but not on Linux,
    /// where the swap still lands. (The hook is static; the test assembly
    /// runs with parallelization off - see TestSetup - so no other test sees
    /// it, and it is put back on dispose and again in Dispose.)
    /// </summary>
    private static IDisposable HoldHistory()
    {
        SafeFile.ReplaceFile = (temp, path) =>
        {
            if (string.Equals(Path.GetFileName(path), "race-history.json", StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            RealReplace(temp, path);
        };

        return new PutBack();
    }

    private sealed class PutBack : IDisposable
    {
        public void Dispose() => SafeFile.ReplaceFile = RealReplace;
    }

    private RaceFilingService Service(SessionHistoryStore history) =>
        new(_timing, _recorder, history, _profiles, _trackLayouts, _settings, _rounds)
        {
            Log = _log.Add,
        };

    private SessionHistoryStore HistoryWithAnEarlierRace()
    {
        var history = SessionHistoryStore.Load();
        Assert.True(history.Add(new SessionRecord { Label = "Earlier race" }));
        return history;
    }

    private RoundDefinition AddRound(string label)
    {
        var round = new RoundDefinition { ClassName = "Stock", RoundLabel = label, Kind = RoundKind.Qualifier };
        _rounds.Add(round);
        return round;
    }

    /// <summary>Two cars, three crossings each, still racing - what the board holds at the flag.</summary>
    private void RunARace()
    {
        _race.Stage();
        _race.GreenFlag(0);

        for (var lap = 1; lap <= 3; lap++)
        {
            Cross("AAAAAA", (uint)(lap * 20_000));
            Cross("BBBBBB", (uint)(lap * 20_000 + 700));
        }
    }

    private void Cross(string car, uint atMs) =>
        _race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = atMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

    /// <summary>The input Race Control builds, with every callback noting itself.</summary>
    private RaceFilingInput Input(
        RoundDefinition? round,
        bool wasQuickRace = false,
        bool isDemo = false,
        RoundDefinition? pickNext = null,
        string? label = null) => new()
    {
        WasQuickRace = wasQuickRace,
        IsDemo = isDemo,
        TrackLayoutName = "Oval",
        TakeRound = () => { _steps.Add("TakeRound"); return round; },
        PickNextRound = _ => { _steps.Add("PickNextRound"); return pickNext; },
        BuildLabel = r => { _steps.Add("BuildLabel"); return label ?? r?.RoundLabel ?? "Race"; },
        ClearRaceSummary = () => _steps.Add("ClearRaceSummary"),
        SignalBroadcastOver = () => _steps.Add("SignalBroadcastOver"),
        DemoOver = () => _steps.Add("DemoOver"),
        NoteGhostsBeaten = _ => _steps.Add("NoteGhostsBeaten"),
        ApplyBlackFlags = _ => _steps.Add("ApplyBlackFlags"),
        BeforeCommit = (_, _) => _steps.Add("BeforeCommit"),
        Filed = _ => _steps.Add("Filed"),
        NoBadgesForDemo = () => _steps.Add("NoBadgesForDemo"),
        AwardAchievements = _ => _steps.Add("AwardAchievements"),
        SpeakSummary = (_, _) => _steps.Add("SpeakSummary"),
        SaveResultsCard = _ => _steps.Add("SaveResultsCard"),
        SessionSaved = () => _steps.Add("SessionSaved"),
        FilingFinished = () => _steps.Add("FilingFinished"),
        ShowNotSaved = (_, _) => _steps.Add("ShowNotSaved"),
    };
}
