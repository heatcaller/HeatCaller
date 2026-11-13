namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), the commentary director.
///
/// The class between races was the last race's, so the picked round's
/// qualifying report named the wrong class. A fight already called this
/// race was picked again once its cooldown ran out, found its fact spent
/// and kept every other fight from being offered. And before a launch's
/// first green the race key was blank, so Coming Up Next was keyed
/// "upnext:" and came back as already said after a restart.
/// </summary>
public sealed class BugHunt17_CommentaryDirectorTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 8, 19, 0, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    /// <summary>A brain that writes one line and keeps what it was asked.</summary>
    private sealed class Brain : ICommentaryBrain
    {
        private readonly Clock _clock;

        public Brain(Clock clock) => _clock = clock;

        public int Asked { get; private set; }

        public string LastPrompt { get; private set; } = string.Empty;

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
            LastPrompt = systemPrompt + "\n" + userPrompt;
            _clock.Advance(TimeSpan.FromSeconds(2));

            // Keeps the compiler happy about an event nothing subscribes to.
            StatusMessage?.Invoke(string.Empty);

            return Task.FromResult<string?>("And that is the story so far!");
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private readonly Clock _clock = new();

    private readonly AppSettings _settings = new()
    {
        CommentaryEnabled = true,
        CommentaryMaxWords = 40,
    };

    private (CommentaryDirector Director, Brain Model) Launch()
    {
        var model = new Brain(_clock);
        var commentary = new CommentaryService(ProfileStore.Load(), _settings) { Client = model };
        var director = new CommentaryDirector(commentary, _settings, _clock.Read);

        director.NameFor = id => id switch
        {
            "A" => "Sam",
            "B" => "Bill",
            "C" => "Cara",
            "D" => "Dev",
            _ => null,
        };

        director.NewMeeting("day-1");
        director.MonthlyUsage(0, 400);
        director.NightShape(10, 30);

        return (director, model);
    }

    // ------------------------------------------------------------------
    // The class between races
    // ------------------------------------------------------------------

    private static IReadOnlyList<QualifyingLine> StockQualifying() => new[]
    {
        new QualifyingLine("A", "Sam", 1, 9.512),
        new QualifyingLine("B", "Bill", 2, 9.800),
    };

    [Fact]
    public async Task The_qualifying_report_names_the_class_of_the_round_picked_not_the_last_race()
    {
        var (director, model) = Launch();

        director.RaceStarted("mod-heat-3", "Mod");
        director.RaceFinished();

        // The Stock A-Main is picked in the Round box, and the readers
        // follow it.
        director.ClassBetweenRaces("Stock");
        director.Qualifying = StockQualifying;

        var said = await director.NextAsync();

        Assert.NotNull(said);
        Assert.Equal(SegmentKind.QualifyingReport, said!.Kind);
        Assert.Contains("takes pole in Stock", model.LastPrompt);
        Assert.DoesNotContain("pole in Mod", model.LastPrompt);
    }

    [Fact]
    public async Task No_round_picked_leaves_the_class_where_the_last_race_put_it()
    {
        var (director, model) = Launch();

        director.RaceStarted("mod-heat-3", "Mod");
        director.RaceFinished();

        director.ClassBetweenRaces(null);
        director.ClassBetweenRaces("  ");
        director.Qualifying = StockQualifying;

        Assert.NotNull(await director.NextAsync());
        Assert.Contains("takes pole in Mod", model.LastPrompt);
    }

    [Fact]
    public async Task A_race_that_is_running_keeps_its_own_class()
    {
        var (director, model) = Launch();

        director.RaceStarted("mod-heat-3", "Mod");

        // Ignored: the race on track is Mod's whatever is picked.
        director.ClassBetweenRaces("Stock");

        director.RaceFinished();
        director.Qualifying = StockQualifying;

        Assert.NotNull(await director.NextAsync());
        Assert.Contains("takes pole in Mod", model.LastPrompt);
    }

    // ------------------------------------------------------------------
    // A fight already called does not stand in front of the next one
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_fight_called_earlier_in_the_race_does_not_block_a_new_one_behind_it()
    {
        var (director, model) = Launch();

        director.RaceStarted("main-1", "Box Stock");

        // The leaders, nose to tail long enough to be worth a line.
        for (var i = 0; i < 6; i++)
        {
            director.Standings(new[] { new PairGap("A", "B", 1, 0.3, 0) });
            _clock.Advance(TimeSpan.FromSeconds(5));
        }

        var first = await director.NextAsync();

        Assert.NotNull(first);
        Assert.Equal(SegmentKind.BattleWatch, first!.Kind);
        Assert.Contains("Sam", model.LastPrompt);

        // They stay locked together past the two-minute cooldown, and a
        // fight for third starts behind them.
        for (var i = 0; i < 30; i++)
        {
            director.Standings(new[]
            {
                new PairGap("A", "B", 1, 0.3, 0),
                new PairGap("C", "D", 3, 0.3, 0),
            });
            _clock.Advance(TimeSpan.FromSeconds(5));
        }

        var second = await director.NextAsync();

        Assert.NotNull(second);
        Assert.Equal(SegmentKind.BattleWatch, second!.Kind);
        Assert.Equal(2, model.Asked);
        Assert.Contains("Cara", model.LastPrompt);
        Assert.Contains("Dev", model.LastPrompt);
    }

    [Fact]
    public void Pick_passes_over_a_fight_it_is_told_to_skip()
    {
        var watch = new BattleWatch(_clock.Read);

        for (var i = 0; i < 6; i++)
        {
            watch.Observe(new[]
            {
                new PairGap("A", "B", 1, 0.3, 0),
                new PairGap("C", "D", 3, 0.3, 0),
            });
            _clock.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Equal("A", watch.Pick()?.AheadId);
        Assert.Equal("C", watch.Pick(fight => fight.AheadId == "A")?.AheadId);
        Assert.Null(watch.Pick(_ => true));
    }

    // ------------------------------------------------------------------
    // Coming Up Next before the first green, across a restart
    // ------------------------------------------------------------------

    [Fact]
    public async Task Coming_up_next_is_said_again_for_a_new_round_after_a_restart_before_any_race()
    {
        // Before the night's first race.
        var (before, _) = Launch();

        before.Upcoming = () => (new[] { new UpcomingRound("Q1 Heat 1", "Box Stock", 8) }, TimeSpan.FromMinutes(4));

        var opening = await before.NextAsync();

        Assert.NotNull(opening);
        Assert.Equal(SegmentKind.ComingUpNext, opening!.Kind);

        // The laptop is closed and reopened between the heats and the
        // mains: a new director, tonight's spent facts restored.
        _clock.Advance(TimeSpan.FromMinutes(30));

        var (after, model) = Launch();

        after.Upcoming = () => (new[] { new UpcomingRound("A-Main", "Box Stock", 8) }, TimeSpan.FromMinutes(4));

        var said = await after.NextAsync();

        Assert.NotNull(said);
        Assert.Equal(SegmentKind.ComingUpNext, said!.Kind);
        Assert.Contains("A-Main", model.LastPrompt);
    }

    [Fact]
    public async Task The_same_round_is_still_not_announced_twice_across_that_restart()
    {
        var (before, _) = Launch();

        before.Upcoming = () => (new[] { new UpcomingRound("Q1 Heat 1", "Box Stock", 8) }, TimeSpan.FromMinutes(4));

        Assert.NotNull(await before.NextAsync());

        _clock.Advance(TimeSpan.FromMinutes(30));

        var (after, model) = Launch();

        after.Upcoming = () => (new[] { new UpcomingRound("Q1 Heat 1", "Box Stock", 8) }, TimeSpan.FromMinutes(4));

        Assert.Null(await after.NextAsync());
        Assert.Equal(0, model.Asked);
    }
}
