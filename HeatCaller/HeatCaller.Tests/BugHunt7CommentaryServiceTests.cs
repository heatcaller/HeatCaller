namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, the commentary service. What the hunt found:
///
///  - The night's recap, a driver's night and the night before were written
///    under the live talking-point timeout (20 s by default), so a small
///    local model timed out on every one of them.
///  - An intro written ahead, and a segment the director dropped after
///    writing it, were both recorded as the last line - so Repeat on the
///    Announcer page could read over the PA a line nobody had heard.
///  - The history facts an intro or a recap marked were only saved when a
///    director segment happened to follow, so a restart brought them back.
///  - A driver's name in capitals was not counted as shouting, so it went to
///    the voice in capitals.
///  - The intro's rivalry lines were built from every assigned driver while
///    the facts only name the first eight.
///  - A held intro survived a corrected or later result, and a write still
///    in flight when another result came in was held anyway.
/// </summary>
public sealed class BugHunt7CommentaryServiceTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    //  The long pieces' timeout
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_long_pieces_are_not_written_under_the_live_timeout()
    {
        var model = new Brain { Answer = "What a night it was." };
        var desk = new CommentaryService(ProfileStore.Load(), new AppSettings { CommentaryEnabled = true }) { Client = model };
        var facts = new PackedFacts("Heat 1: Sam won.", new[] { "night:heat-1" }, 0);

        await desk.BuildNightRecapAsync(facts);
        Assert.NotNull(model.Timeout);
        Assert.True(model.Timeout >= TimeSpan.FromMinutes(3));

        model.Timeout = null;
        await desk.BuildDriverCutAsync(facts);
        Assert.True(model.Timeout >= TimeSpan.FromMinutes(3));

        model.Timeout = null;
        await desk.BuildNightBeforeAsync(facts);
        Assert.True(model.Timeout >= TimeSpan.FromMinutes(3));
    }

    // ------------------------------------------------------------------
    //  The last line is a line somebody heard
    // ------------------------------------------------------------------

    [Fact]
    public async Task An_intro_written_ahead_is_not_the_last_line_until_it_is_spoken()
    {
        var model = new Brain { Answer = "Sam and Bill line up for the A-main." };
        var desk = new CommentaryService(ProfileStore.Load(), new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60 }) { Client = model };

        var line = await desk.BuildRoundIntroAsync(AMain("sam", "bill"), remember: false);

        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.Null(desk.LastLine);
        Assert.Null(desk.LastLineKind);

        desk.IntroSpoken(line);

        Assert.Equal(line, desk.LastLine);
        Assert.Equal("Intro", desk.LastLineKind);
    }

    [Fact]
    public async Task An_intro_written_at_staging_is_still_recorded_as_before()
    {
        var model = new Brain { Answer = "Sam and Bill line up for the A-main." };
        var desk = new CommentaryService(ProfileStore.Load(), new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60 }) { Client = model };

        var line = await desk.BuildRoundIntroAsync(AMain("sam", "bill"));

        Assert.False(string.IsNullOrWhiteSpace(line));
        Assert.Equal(line, desk.LastLine);
        Assert.Equal("Intro", desk.LastLineKind);
    }

    [Fact]
    public async Task A_segment_dropped_because_the_hold_came_on_mid_write_is_not_the_last_line()
    {
        var rig = new Rig();
        AFight(rig);

        rig.Model.WhileWriting = () => rig.Director.Held = true;

        Assert.Null(await rig.Director.NextAsync());
        Assert.Equal(1, rig.Model.Asked);
        Assert.Null(rig.Commentary.LastLine);
    }

    [Fact]
    public async Task A_segment_that_came_back_after_its_moment_is_not_the_last_line()
    {
        var rig = new Rig();
        AFight(rig);

        // A battle call is worth saying for twenty seconds.
        rig.Model.Takes = TimeSpan.FromSeconds(30);

        Assert.Null(await rig.Director.NextAsync());
        Assert.Contains(rig.Console, line => line.Contains("came back after the moment had gone"));
        Assert.Null(rig.Commentary.LastLine);
    }

    [Fact]
    public void Forgetting_a_line_only_clears_it_while_it_is_still_the_one_held()
    {
        var desk = new CommentaryService(ProfileStore.Load(), new AppSettings());

        desk.IntroSpoken("A later line.");
        desk.ForgetLine("An earlier line.");

        Assert.Equal("A later line.", desk.LastLine);

        desk.ForgetLine("A later line.");

        Assert.Null(desk.LastLine);
        Assert.Null(desk.LastLineKind);
    }

    // ------------------------------------------------------------------
    //  The history facts are saved when they are marked
    // ------------------------------------------------------------------

    [Fact]
    public void Marking_history_tells_the_host_so_it_can_save_and_nothing_marked_tells_it_nothing()
    {
        var desk = new CommentaryService(ProfileStore.Load(), new AppSettings());
        var said = new SaidTonight();
        said.NewMeeting("2026-10-13");

        var told = 0;
        desk.HistorySaid = said;
        desk.HistoryMarked = () => told++;

        desk.HistorySpoken(null);
        desk.HistorySpoken(new PackedFacts(string.Empty, Array.Empty<string>(), 0));

        Assert.Equal(0, told);

        desk.HistorySpoken(new PackedFacts("Billy's first ever start.", new[] { "hist:billy:debut" }, 0));

        Assert.Equal(1, told);
        Assert.True(said.Spent("hist:billy:debut"));
    }

    // ------------------------------------------------------------------
    //  A name in capitals
    // ------------------------------------------------------------------

    [Fact]
    public void A_name_in_capitals_comes_back_as_the_facts_spell_it()
    {
        var notes = new List<string>();

        var text = CommentaryService.TidyIntro(
            "Here comes ASHER, and FRANK is right behind!", "Q3 Heat 1", new[] { "Asher", "Frank" }, 45, notes);

        Assert.Equal("Here comes Asher, and Frank is right behind!", text);
        Assert.NotEmpty(notes);
    }

    [Fact]
    public void A_name_written_as_an_ordinary_word_is_left_alone()
    {
        var notes = new List<string>();
        const string line = "Sam will start on pole tonight.";

        Assert.Equal(line, CommentaryService.TidyIntro(line, "Q3 Heat 1", new[] { "Sam", "Will" }, 45, notes));
        Assert.Empty(notes);
    }

    // ------------------------------------------------------------------
    //  The rivalries are the eight drivers' rivalries
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_rivalry_between_the_ninth_and_tenth_drivers_does_not_reach_the_intro()
    {
        var profiles = ProfileStore.Load();
        var drivers = new[] { "Ann", "Bob", "Cat", "Dan", "Eve", "Fay", "Gus", "Hal", "Ivy", "Jay" };

        for (var i = 0; i < drivers.Length; i++)
        {
            profiles.Upsert(new DriverProfile { Transponder = "t" + (i + 1), DriverName = drivers[i] });
        }

        var rivalries = RivalryStore.Load();
        rivalries.Upsert(new RivalryLine { DriverA = "Ivy", DriverB = "Jay" });

        var model = new Brain { Answer = "Here they come." };
        var desk = new CommentaryService(profiles, new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 60 }, rivalries)
        {
            Client = model,
        };

        var round = AMain(Enumerable.Range(1, 10).Select(i => "t" + i).ToArray());

        await desk.BuildRoundIntroAsync(round);

        Assert.DoesNotContain("Known rivalry", model.Prompt);
        Assert.False(desk.HasRivalryAmong(round.AssignedTransponders));

        // And one among the eight still does.
        rivalries.Upsert(new RivalryLine { DriverA = "Ann", DriverB = "Bob" });

        await desk.BuildRoundIntroAsync(round);

        Assert.Contains("Known rivalry", model.Prompt);
        Assert.True(desk.HasRivalryAmong(round.AssignedTransponders));
    }

    // ------------------------------------------------------------------
    //  A held intro and a result that changed
    // ------------------------------------------------------------------

    [Fact]
    public void A_result_filed_or_corrected_throws_the_held_intro_away()
    {
        var at = new DateTime(2026, 10, 13, 20, 0, 0, DateTimeKind.Local);
        var grid = new[] { "111", "222" };
        var ahead = new WrittenAhead();

        var asOf = ahead.ResultsChanged();
        Assert.True(ahead.Hold("r5", grid, "Bill leads the title by four.", null, at, asOf));

        ahead.ResultsChanged();

        Assert.Equal(WrittenAhead.Why.NothingHeld, ahead.TakeFor("r5", grid, null, at.AddMinutes(1)).Why);
        Assert.Equal(1, ahead.Wasted);
        Assert.Contains("a result was filed or corrected after it was written", ahead.Report()!);
    }

    [Fact]
    public void A_write_overtaken_by_another_result_is_not_held()
    {
        var at = new DateTime(2026, 10, 13, 20, 0, 0, DateTimeKind.Local);
        var grid = new[] { "111", "222" };
        var ahead = new WrittenAhead();

        var first = ahead.ResultsChanged();
        var second = ahead.ResultsChanged();

        // The first write comes back after the second filing.
        Assert.False(ahead.Hold("r5", grid, "stale words", null, at, first));
        Assert.Equal(string.Empty, ahead.HoldingFor);
        Assert.Equal(1, ahead.Written);
        Assert.Equal(1, ahead.Wasted);

        Assert.True(ahead.Hold("r5", grid, "fresh words", null, at, second));

        var answer = ahead.TakeFor("r5", grid, null, at.AddMinutes(1));

        Assert.Equal(WrittenAhead.Why.Used, answer.Why);
        Assert.Equal("fresh words", answer.Line);
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    private static RoundDefinition AMain(params string[] roster) => new()
    {
        RoundLabel = "A-Main",
        ClassName = "Box Stock",
        Kind = RoundKind.Main,
        MainTier = 0,
        AssignedTransponders = roster.ToList(),
    };

    /// <summary>One fight, nose to tail for long enough to be worth calling - BugHunt5CommentaryTests' own.</summary>
    private static void AFight(Rig rig)
    {
        for (var i = 0; i < 6; i++)
        {
            rig.Director.Standings(new[] { new PairGap("A", "B", 1, 0.3, 0) });
            rig.Clock.Advance(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 13, 19, 0, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    /// <summary>A brain the test drives, which keeps what it was asked with.</summary>
    private sealed class Brain : ICommentaryBrain
    {
        private readonly Clock? _clock;

        public Brain(Clock? clock = null) => _clock = clock;

        public string? Answer { get; set; } = "They are nose to tail out there!";

        public TimeSpan Takes { get; set; } = TimeSpan.FromSeconds(2);

        public int Asked { get; private set; }

        public string Prompt { get; private set; } = string.Empty;

        public TimeSpan? Timeout { get; set; }

        /// <summary>Run inside the request, as the hold button is.</summary>
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
            Prompt = userPrompt;
            Timeout = timeout;
            _clock?.Advance(Takes);

            WhileWriting?.Invoke();

            // Keeps the compiler happy about an event nothing subscribes to.
            StatusMessage?.Invoke(string.Empty);

            return Task.FromResult(Answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    /// <summary>A director with a fight to call, as BugHunt5CommentaryTests builds one.</summary>
    private sealed class Rig
    {
        public Clock Clock { get; } = new();

        public AppSettings Settings { get; } = new()
        {
            CommentaryEnabled = true,
            CommentaryMaxWords = 40,
        };

        public Brain Model { get; }

        public CommentaryService Commentary { get; }

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
    }
}
