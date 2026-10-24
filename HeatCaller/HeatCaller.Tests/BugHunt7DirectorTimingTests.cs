namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, director timing: what the director does with a line whose
/// moment changed while it was being written, and with two passes asked
/// for at once.
///
/// The brain here can do something DURING the write (the flag falls, a
/// faster lap comes in) and can be held open, because every one of these
/// bugs lived in the seconds between asking for a line and getting it.
/// </summary>
public sealed class BugHunt7DirectorTimingTests : IDisposable
{
    private readonly Rig _rig = new();

    public void Dispose() => _rig.Dispose();

    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 8, 19, 0, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    private sealed class Brain : ICommentaryBrain
    {
        private readonly Clock _clock;

        public Brain(Clock clock) => _clock = clock;

        public string? Answer { get; set; } = "They are nose to tail out there!";

        public TimeSpan Takes { get; set; } = TimeSpan.FromSeconds(2);

        /// <summary>Runs while the line is being written, after the time it takes has passed.</summary>
        public Action? During { get; set; }

        /// <summary>When set, the write does not come back until this completes.</summary>
        public Task? Gate { get; set; }

        public int Asked { get; private set; }

        public event Action<string>? StatusMessage;

        public string Describe => "the test";

        public async Task<string?> GenerateAsync(
            string systemPrompt,
            string userPrompt,
            int maxTokens,
            double temperature = 0.7,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            Asked++;
            _clock.Advance(Takes);

            var during = During;
            During = null;
            during?.Invoke();

            if (Gate is { } gate)
            {
                await gate.ConfigureAwait(false);
            }

            // Keeps the compiler happy about an event nothing subscribes to.
            StatusMessage?.Invoke(string.Empty);

            return Answer;
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private sealed class Rig : IDisposable
    {
        private readonly TempAppData _data = new();

        public Clock Clock { get; } = new();

        public AppSettings Settings { get; } = new()
        {
            CommentaryEnabled = true,
            CommentaryMaxWords = 40,
        };

        public CommentaryService Commentary { get; }

        public Brain Model { get; }

        public CommentaryDirector Director { get; }

        public List<string> Console { get; } = new();

        public Rig()
        {
            Model = new Brain(Clock);
            Commentary = new CommentaryService(ProfileStore.Load(), Settings) { Client = Model };
            Director = new CommentaryDirector(Commentary, Settings, Clock.Read);

            Director.Status += Console.Add;
            Director.NameFor = id => id == "A" ? "Sam" : "Bill";
            Director.NewMeeting("day-1");
            Director.MonthlyUsage(0, 400);
            Director.NightShape(10, 30);
            Director.RaceStarted("heat-1", "Box Stock");
        }

        public void AFight()
        {
            for (var i = 0; i < 6; i++)
            {
                Director.Standings(new[] { new PairGap("A", "B", 1, 0.3, 0) });
                Clock.Advance(TimeSpan.FromSeconds(5));
            }
        }

        /// <summary>Four slow laps, so the next quick one is the fastest of the race.</summary>
        public void WarmUp()
        {
            for (var i = 0; i < 4; i++)
            {
                Director.Lap($"warm-{i}", 20.0 + i);
            }
        }

        public void ATable() => Director.Championship = () => new[]
        {
            new ChampionshipStanding("A", "Sam", PositionNow: 1, PositionBefore: 2, Points: 88, BehindLeader: 0),
            new ChampionshipStanding("B", "Bill", PositionNow: 2, PositionBefore: 1, Points: 85, BehindLeader: 3),
        };

        public void Dispose() => _data.Dispose();
    }

    // ------------------------------------------------------------------
    // The phase changed during the write
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_battle_call_that_comes_back_after_the_flag_is_dropped_and_not_charged()
    {
        _rig.AFight();

        // A record lap waiting as well, so a pass that carried on down its
        // in-race order after the flag would write a second line.
        _rig.WarmUp();
        _rig.Director.Lap("A", 12.0);

        _rig.Model.During = _rig.Director.RaceFinished;

        Assert.Null(await _rig.Director.NextAsync());

        Assert.Equal(1, _rig.Model.Asked);
        // WHY (23 Oct 2026, bug hunt 10): the relay keeps the charge for a line it wrote, so a written line that is dropped is charged too.
        Assert.Equal(1, _rig.Director.Budget.SpentTonight);
        Assert.Equal(0, _rig.Director.Budget.InFlight);
        Assert.Equal(0, _rig.Director.Said.Count);
        Assert.Contains(_rig.Console, line => line.Contains("finished while it was being written"));
    }

    [Fact]
    public async Task A_between_races_segment_that_comes_back_after_the_green_is_dropped_and_not_charged()
    {
        _rig.Director.RaceFinished();
        _rig.ATable();

        _rig.Model.During = () => _rig.Director.RaceStarted("heat-2", "Box Stock");

        Assert.Null(await _rig.Director.NextAsync());

        Assert.Equal(1, _rig.Model.Asked);
        // WHY (23 Oct 2026, bug hunt 10): the relay keeps the charge for a line it wrote, so a written line that is dropped is charged too.
        Assert.Equal(1, _rig.Director.Budget.SpentTonight);
        Assert.Equal(0, _rig.Director.Said.Count);
        Assert.Contains(_rig.Console, line => line.Contains("went green while it was being written"));
    }

    // ------------------------------------------------------------------
    // One pass at a time
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_programme_waits_for_the_tick_s_pass_rather_than_running_beside_it()
    {
        _rig.AFight();

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _rig.Model.Gate = gate.Task;

        var tick = _rig.Director.NextAsync();

        Assert.False(tick.IsCompleted);
        Assert.Equal(1, _rig.Model.Asked);

        // The tick asking again half a second later gives way at once.
        Assert.Null(await _rig.Director.NextAsync());
        Assert.Equal(1, _rig.Model.Asked);

        // The flag falls and the programme asks for its colour.
        _rig.Model.Gate = null;
        _rig.ATable();

        var programme = _rig.Director.ForTheProgrammeAsync();

        Assert.False(programme.IsCompleted);
        Assert.Equal(1, _rig.Model.Asked);

        gate.SetResult();

        // The battle call came back after the flag, so it is dropped...
        Assert.Null(await tick);

        // ...and only then does the programme write its own.
        var said = await programme;

        Assert.NotNull(said);
        Assert.Equal(SegmentKind.ChampionshipCheck, said!.Kind);
        Assert.Equal(2, _rig.Model.Asked);

        // Two, not one (23 Oct 2026, bug hunt 10): the dropped battle call was
        // written and the relay kept its charge, so the budget keeps it too.
        Assert.Equal(2, _rig.Director.Budget.SpentTonight);
        Assert.Equal(0, _rig.Director.Budget.InFlight);
    }

    // ------------------------------------------------------------------
    // Record laps
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_faster_lap_seen_while_a_record_is_being_written_is_still_called()
    {
        _rig.WarmUp();
        _rig.Director.Lap("A", 12.0);

        // B goes quicker still while A's lap is being written up.
        _rig.Model.During = () => _rig.Director.Lap("B", 11.5);

        var first = await _rig.Director.NextAsync();

        Assert.NotNull(first);
        Assert.Equal(SegmentKind.PaceNote, first!.Kind);
        Assert.True(_rig.Director.Said.Spent("pace:heat-1:SessionBest:A"));

        _rig.Clock.Advance(CommentaryBudget.Spec(SegmentKind.PaceNote).MinGap);

        var second = await _rig.Director.NextAsync();

        Assert.NotNull(second);
        Assert.Equal(SegmentKind.PaceNote, second!.Kind);
        Assert.True(_rig.Director.Said.Spent("pace:heat-1:SessionBest:B"));
    }

    [Fact]
    public async Task A_personal_best_does_not_push_out_the_fastest_lap_of_the_race()
    {
        _rig.WarmUp();
        _rig.Director.Lap("A", 12.0);

        // warm-0 beats their own 20.0 - a personal best, and a smaller
        // thing than the race's fastest lap still waiting to be said.
        _rig.Director.Lap("warm-0", 19.0);

        var said = await _rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.True(_rig.Director.Said.Spent("pace:heat-1:SessionBest:A"));
        Assert.False(_rig.Director.Said.Spent("pace:heat-1:PersonalBest:warm-0"));
    }
}
