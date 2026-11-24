namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the commentary director.
///
/// A callback about an earlier call could go out as a whole segment by
/// itself, under another segment's name. A throw to pit lane that came back
/// in one piece was read in one voice. And the qualifying report's pole fact
/// was keyed on the last race rather than the class, so a second class
/// reported in the same gap lost its pole.
/// </summary>
public sealed class BugHunt24_CommentaryDirectorTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    // A callback is never a segment by itself
    // ------------------------------------------------------------------

    private static bool IsCallback(string? key) => key is not null && key.StartsWith("promise:", StringComparison.Ordinal);

    private static readonly FactBlock UpNext = new("upnext:race-1", "Coming up next is the Stock A-Main.");

    private static readonly FactBlock Callback = new("promise:carl-dave", "The fight between Carl and Dave was called on air earlier tonight.", "pair:carl:dave");

    [Fact]
    public void A_segment_whose_own_facts_are_all_spent_is_not_sent_out_with_the_callback_alone()
    {
        var said = new SaidTonight();
        said.Mark(UpNext.Key);

        var pack = CommentaryDirector.PackOwnFactsFirst(new[] { UpNext, Callback }, said, IsCallback);

        Assert.True(pack.Empty);
    }

    [Fact]
    public void A_cooled_own_fact_is_said_rather_than_swapped_for_a_fresh_callback()
    {
        var said = new SaidTonight();
        var own = new FactBlock("form:stock:1", "Sam has won three of the four heats.", "driver:A");

        // Sam was the subject of the segment just spoken, so his theme is cooled.
        said.Mark(new PackedFacts("Sam set the fast lap.", new[] { "record:1" }, 0) { Themes = new[] { "driver:A" } });

        var pack = CommentaryDirector.PackOwnFactsFirst(new[] { own, Callback }, said, IsCallback);

        Assert.False(pack.Empty);
        Assert.Contains(own.Key, pack.Keys);
        Assert.DoesNotContain(Callback.Key, pack.Keys);
    }

    [Fact]
    public void A_segment_with_its_own_fact_left_still_carries_the_callback()
    {
        var said = new SaidTonight();

        var pack = CommentaryDirector.PackOwnFactsFirst(new[] { UpNext, Callback }, said, IsCallback);

        Assert.Equal(new[] { UpNext.Key, Callback.Key }, pack.Keys);
    }

    [Fact]
    public void The_budget_does_not_count_a_callback_as_something_new_to_say()
    {
        var director = Source("CommentaryDirector.cs");

        Assert.Contains(".Where(block => !Promises.IsPayOffKey(block.Key))", director);
        Assert.Contains("var pack = PackOwnFactsFirst(blocks, Said, Promises.IsPayOffKey);", director);
    }

    // ------------------------------------------------------------------
    // The pit lane is a two-hander or nothing
    // ------------------------------------------------------------------

    [Fact]
    public void A_pit_lane_that_comes_back_in_one_piece_is_not_asked_for_again_in_one_voice()
    {
        Assert.False(CommentaryDirector.OneVoiceWhenTheTwoHanderFails(SegmentKind.PitLaneLive));

        // Every other kind still falls back to one voice.
        Assert.True(CommentaryDirector.OneVoiceWhenTheTwoHanderFails(SegmentKind.BattleWatch));
        Assert.True(CommentaryDirector.OneVoiceWhenTheTwoHanderFails(SegmentKind.QualifyingReport));

        var director = Source("CommentaryDirector.cs");
        var drop = director.IndexOf("if (!OneVoiceWhenTheTwoHanderFails(kind))", StringComparison.Ordinal);
        var oneVoice = director.IndexOf("came back in one piece rather than two - saying it in one voice.", StringComparison.Ordinal);

        Assert.True(drop > 0);
        Assert.True(oneVoice > drop);
    }

    // ------------------------------------------------------------------
    // Each class's qualifying report keeps its own pole
    // ------------------------------------------------------------------

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

        director.NewMeeting("day-1");
        director.MonthlyUsage(0, 400);
        director.NightShape(10, 30);

        return (director, model);
    }

    private static IReadOnlyList<QualifyingLine> StockQualifying() => new[]
    {
        new QualifyingLine("A", "Sam", 1, 9.512),
        new QualifyingLine("B", "Bill", 2, 9.800),
    };

    private static IReadOnlyList<QualifyingLine> ModQualifying() => new[]
    {
        new QualifyingLine("C", "Cara", 1, 8.912),
        new QualifyingLine("D", "Dev", 2, 9.100),
    };

    [Fact]
    public async Task A_second_class_reported_in_the_same_gap_still_leads_with_its_pole()
    {
        var (director, model) = Launch();

        director.RaceStarted("mod-heat-3", "Mod");
        director.RaceFinished();

        director.ClassBetweenRaces("Stock");
        director.Qualifying = StockQualifying;

        var stock = await director.NextAsync();

        Assert.NotNull(stock);
        Assert.Equal(SegmentKind.QualifyingReport, stock!.Kind);
        Assert.Contains("takes pole in Stock", model.LastPrompt);

        // Still the same gap - no green in between - and the Mod main is
        // picked in the Round box instead.
        _clock.Advance(TimeSpan.FromMinutes(5));
        director.ClassBetweenRaces("Mod");
        director.Qualifying = ModQualifying;

        var mod = await director.NextAsync();

        Assert.NotNull(mod);
        Assert.Equal(SegmentKind.QualifyingReport, mod!.Kind);
        Assert.Contains("takes pole in Mod", model.LastPrompt);
    }

    [Fact]
    public void The_qualifying_report_key_tells_two_classes_apart_on_the_same_night()
    {
        var stock = CommentaryReportFacts.QualifyingReport(StockQualifying(), "Stock", CommentaryDirector.QualifyingReportKey("day-1", "Stock"));
        var mod = CommentaryReportFacts.QualifyingReport(ModQualifying(), "Mod", CommentaryDirector.QualifyingReportKey("day-1", "Mod"));

        Assert.NotEqual(stock[0].Key, mod[0].Key);
        Assert.Equal(CommentaryDirector.QualifyingReportKey("day-1", "Stock"), CommentaryDirector.QualifyingReportKey("day-1", "Stock"));
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }
}
