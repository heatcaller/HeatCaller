namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026), the commentary engine: a segment's sponsor
/// credit was counted for the sponsor report the moment the line was
/// written. The director and the announcer can still drop the line after
/// that - Hold pressed, the next race called to the grid, the moment gone -
/// so the report showed a "presented by" nobody heard, and the ration stayed
/// spent, holding the sponsor's credit off the announcer's own calls. Now the
/// count waits for the line to be said, and a dropped line gives the ration back.
/// </summary>
public sealed class BugHunt11_CommentaryEngineTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public async Task A_sponsored_segment_dropped_by_the_hold_mid_write_is_not_counted_and_gives_the_ration_back()
    {
        var rig = new Rig();
        AFight(rig);

        rig.Model.WhileWriting = () => rig.Director.Held = true;

        Assert.Null(await rig.Director.NextAsync());
        Assert.Equal(1, rig.Model.Asked);
        Assert.Equal(1, rig.Taken);
        Assert.Equal(0, rig.Counted);
        Assert.Equal(1, rig.Released);
    }

    [Fact]
    public async Task A_sponsored_segment_the_announcer_drops_after_it_is_written_is_not_counted()
    {
        var rig = new Rig();
        rig.Director.CommitWhenSpoken = true;
        AFight(rig);

        var said = await rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.Contains("Joe's Hobby Shop", said!.Text);
        Assert.Equal(0, rig.Counted);

        // The next race was called to the grid before it could be said.
        rig.Director.Dropped(said);

        Assert.Equal(0, rig.Counted);
        Assert.Equal(1, rig.Released);

        // A second answer for the same segment does nothing.
        rig.Director.Spoken(said);
        Assert.Equal(0, rig.Counted);
    }

    [Fact]
    public async Task A_sponsored_segment_that_is_said_is_counted_once_and_keeps_the_ration()
    {
        var rig = new Rig();
        rig.Director.CommitWhenSpoken = true;
        AFight(rig);

        var said = await rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.Equal(0, rig.Counted);

        rig.Director.Spoken(said);

        Assert.Equal(1, rig.Counted);
        Assert.Equal(0, rig.Released);

        rig.Director.Dropped(said);
        Assert.Equal(0, rig.Released);
    }

    [Fact]
    public async Task Without_waiting_for_the_word_a_written_segment_is_counted_as_before()
    {
        var rig = new Rig();
        AFight(rig);

        Assert.NotNull(await rig.Director.NextAsync());
        Assert.Equal(1, rig.Counted);
        Assert.Equal(0, rig.Released);
    }

    /// <summary>One fight, nose to tail for long enough to be worth calling - BugHunt7CommentaryServiceTests' own.</summary>
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
        public DateTime Now { get; private set; } = new(2026, 11, 5, 19, 0, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    /// <summary>A brain the test drives.</summary>
    private sealed class Brain : ICommentaryBrain
    {
        private readonly Clock _clock;

        public Brain(Clock clock) => _clock = clock;

        public int Asked { get; private set; }

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
            _clock.Advance(TimeSpan.FromSeconds(2));
            WhileWriting?.Invoke();
            StatusMessage?.Invoke(string.Empty);

            return Task.FromResult<string?>("They are nose to tail out there!");
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    /// <summary>A director with a fight to call and a sponsor who presents it.</summary>
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

        public int Taken { get; private set; }

        public int Counted { get; private set; }

        public int Released { get; private set; }

        public Rig()
        {
            Model = new Brain(Clock);

            Commentary = new CommentaryService(ProfileStore.Load(), Settings)
            {
                Client = Model,
                SegmentSponsorTakenWithRelease = _ =>
                {
                    Taken++;
                    return ("Joe's Hobby Shop", () => Counted++, () => Released++);
                },
            };

            Director = new CommentaryDirector(Commentary, Settings, Clock.Read);
            Director.NameFor = id => id == "A" ? "Sam" : "Bill";

            Director.NewMeeting("day-1");
            Director.MonthlyUsage(0, 400);
            Director.NightShape(10, 30);
            Director.RaceStarted("heat-1", "Box Stock");
        }
    }
}
