namespace HeatCaller.Tests;

/// <summary>
/// The number check on the AI's lines (8 Nov 2026): every figure in a line
/// must be in its facts, or the sum or difference of two of them, or the
/// line is dropped. See CommentaryNumberCheck.
/// </summary>
public sealed class CommentaryNumberCheckTests
{
    private static List<double> Values(string text, bool forFacts = false) =>
        CommentaryNumberCheck.FiguresIn(text, forFacts).Select(f => Math.Round(f.Value, 3)).ToList();

    // ------------------------------------------------------------------
    // Reading figures
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("a 5.42 for Sam", 5.42)]
    [InlineData("Bill is 1.1s back", 1.1)]
    [InlineData("12 laps", 12)]
    [InlineData("a 5.425s lap", 5.425)]
    [InlineData("now in P4", 4)]
    [InlineData("forty percent of the room", 40)]
    [InlineData("40% of the room", 40)]
    [InlineData("nine laps left", 9)]
    [InlineData("NINE laps left", 9)]
    [InlineData("forty-two laps", 42)]
    [InlineData("forty two laps", 42)]
    [InlineData("rated one hundred and fifty", 150)]
    [InlineData("seven-tenths a lap", 0.7)]
    [InlineData("three tenths", 0.3)]
    [InlineData("a tenth", 0.1)]
    [InlineData("four hundredths", 0.04)]
    [InlineData("two thousandths", 0.002)]
    [InlineData("half a second", 0.5)]
    [InlineData("a third of a second", 0.333)]
    [InlineData("a quarter of a second", 0.25)]
    [InlineData("two thirds of a second", 0.667)]
    [InlineData("a second and a half", 1.5)]
    [InlineData("two and a half seconds", 2.5)]
    [InlineData("a five forty-two", 5.42)]
    [InlineData("a five four twenty-five", 5.425)]
    [InlineData("a six oh nine", 6.09)]
    [InlineData("a five oh oh six", 5.006)]
    [InlineData("a five flat", 5)]
    [InlineData("a twelve thirty-four", 12.34)]
    [InlineData("a twelve three forty-one", 12.341)]
    [InlineData("a four point nine", 4.9)]
    [InlineData("point three back", 0.3)]
    [InlineData("five point four two", 5.42)]
    [InlineData("SEVEN-TENTHS", 0.7)]
    public void Reads_the_figure(string text, double expected)
    {
        Assert.Equal(new[] { Math.Round(expected, 3) }, Values(text));
    }

    [Theory]
    [InlineData("one of the best")]
    [InlineData("two cars in it")]
    [InlineData("zero chance")]
    [InlineData("a second clear")]
    [InlineData("just over a second")]
    [InlineData("his third win")]
    [InlineData("the 3rd heat")]
    [InlineData("the twenty-first lap")]
    [InlineData("since 2019")]
    [InlineData("a B6.4 and an RC8")]
    [InlineData("4WD on a 1/8 track")]
    [InlineData("at 7:30")]
    [InlineData("halfway through")]
    [InlineData("half the field")]
    [InlineData("a third straight win")]
    [InlineData("driving at ten-tenths")]
    [InlineData("oh what a move")]
    [InlineData("first and second")]
    public void Never_a_figure(string text)
    {
        Assert.Empty(Values(text));
    }

    [Fact]
    public void Reads_every_figure_in_a_line_and_keeps_them_apart_at_punctuation()
    {
        // "five, twelve" is two numbers, not a lap time.
        Assert.Equal(new[] { 5.0, 12.0 }, Values("five, twelve"));
        Assert.Equal(new[] { 1.1, 0.7, 9.0 }, Values("by 1.1s - seven-tenths a lap - NINE laps left."));
    }

    [Fact]
    public void Facts_in_milliseconds_back_the_seconds_too()
    {
        Assert.Equal(new[] { 6039, 6.039 }, Values("last lap 6039 ms", forFacts: true));
        Assert.Equal(new[] { 6753, 6.753 }, Values("last lap 6753ms", forFacts: true));
    }

    [Fact]
    public void Facts_keep_what_a_line_leaves_out()
    {
        Assert.Equal(new[] { 1.0, 2.0 }, Values("one or two", forFacts: true));
        Assert.Equal(new[] { 3.0 }, Values("P3", forFacts: true));
        Assert.Contains(90.0, Values("1:30 left", forFacts: true));
        Assert.Contains(1.5, Values("1:30 left", forFacts: true));
    }

    [Fact]
    public void Facts_already_in_words_are_read_the_same_way()
    {
        // RaceNumbers.LapSpoken writes some facts in words already.
        Assert.Equal(new[] { 5.42 }, Values(RaceNumbers.LapSpoken(5420), forFacts: true));
        Assert.Equal(new[] { 6.935 }, Values(RaceNumbers.LapSpoken(6935), forFacts: true));
        Assert.Equal(new[] { 5.06 }, Values(RaceNumbers.LapSpoken(5060), forFacts: true));
        Assert.Equal(new[] { 5.006 }, Values(RaceNumbers.LapSpoken(5006), forFacts: true));
    }

    [Fact]
    public void The_figure_is_reported_as_the_line_said_it()
    {
        Assert.Equal("four point nine", CommentaryNumberCheck.Unsupported("a four point nine!", "lap 5.425s"));
        Assert.Equal("seven-tenths", CommentaryNumberCheck.Unsupported("seven-tenths clear", "gap 0.2s"));
    }

    // ------------------------------------------------------------------
    // The rule - the cases the check was built for
    // ------------------------------------------------------------------

    private const string RaceFacts =
        "Terry leads Dee by 1.1s. Terry's last lap 6039 ms; Dee's last lap 6753 ms. "
        + "The race is to 12 laps and Terry is on lap 3.";

    private const string LapFacts = "Sam has just set 5.425s, the fastest lap of the race. His previous best was 5.743s.";

    [Fact]
    public void Gap_rate_and_laps_left_worked_out_from_the_facts_pass()
    {
        // Just over a second for 1.1; seven-tenths for 6.753 - 6.039; nine
        // for 12 - 3.
        Assert.Null(CommentaryNumberCheck.Unsupported(
            "Terry's out front by just over a second but Dee's losing seven-tenths a lap and there's NINE laps left",
            RaceFacts));
    }

    [Fact]
    public void A_lap_called_the_way_it_is_said_passes()
    {
        // 5.42 for 5.425; a third for 5.743 - 5.425 = 0.318.
        Assert.Null(CommentaryNumberCheck.Unsupported(
            "RIPS it with a five forty-two - nearly a third of a second better", LapFacts));
    }

    [Theory]
    [InlineData("RIPS it with a four point nine!", "four point nine")]
    [InlineData("Sam's got eleven laps left to do it", "eleven")]
    public void A_figure_nobody_gave_it_fails(string line, string figure)
    {
        Assert.Equal(figure, CommentaryNumberCheck.Unsupported(line, LapFacts));
    }

    [Theory]
    [InlineData("Terry's in front with eleven laps left")]
    [InlineData("Terry's on lap five")]
    [InlineData("Terry's ten laps from home")]
    [InlineData("Dee's losing eight tenths a lap")]
    public void Invented_figures_fail_against_the_race_facts(string line)
    {
        Assert.NotNull(CommentaryNumberCheck.Unsupported(line, RaceFacts));
    }

    [Theory]
    [InlineData("Terry by 1.1 seconds", null)]
    [InlineData("Terry by 1.12 seconds", null)]    // within 0.05
    [InlineData("Terry by 1.2 seconds", "1.2")]    // 0.1 out, and more than 5% of anything near it
    [InlineData("on a six oh four", null)]         // 6.04 for 6.039
    [InlineData("on a six seventy-five", null)]    // 6.75 for 6.753
    public void Decimals_are_backed_within_tolerance(string line, string? figure)
    {
        Assert.Equal(figure, CommentaryNumberCheck.Unsupported(line, RaceFacts));
    }

    [Fact]
    public void Five_percent_of_a_lap_time_is_inside_the_tolerance()
    {
        // Pinned so a change to the tolerance is a decision, not an
        // accident: 6.50 is 0.253 from 6.753, inside 5% of it (0.338).
        Assert.Null(CommentaryNumberCheck.Unsupported("on a six fifty", RaceFacts));
        Assert.NotNull(CommentaryNumberCheck.Unsupported("on a six twenty", "lap 6.753s"));
    }

    [Fact]
    public void Whole_numbers_must_match_exactly_or_be_a_fact_rounded()
    {
        const string facts = "Sam leads Bill by 2.8s with 4 laps to go.";

        Assert.Null(CommentaryNumberCheck.Unsupported("about three seconds in it with four to go", facts));
        Assert.Equal("five", CommentaryNumberCheck.Unsupported("five laps to go", facts));

        // Sums and differences back a whole number only when they are one:
        // 4 + 2.8 is 6.8, which is not "seven".
        Assert.Equal("seven", CommentaryNumberCheck.Unsupported("seven laps to go", facts));
    }

    [Fact]
    public void A_sum_backs_a_figure_too()
    {
        Assert.Null(CommentaryNumberCheck.Unsupported("fifteen laps in all", "10 laps done, 5 to go"));
    }

    [Fact]
    public void A_half_is_backed_by_what_rounds_to_it()
    {
        // RaceNumbers.GapSpoken calls 1.38 "a second and a half".
        Assert.Null(CommentaryNumberCheck.Unsupported("a second and a half up the road", "gap 1.38s"));
        Assert.NotNull(CommentaryNumberCheck.Unsupported("a second and a half up the road", "gap 1.1s"));
    }

    [Fact]
    public void Numbers_inside_a_name_from_the_facts_are_not_claims()
    {
        const string facts = "Sam (TLR 22 5.0) leads Lucky Seven by 0.3s.";

        Assert.Null(CommentaryNumberCheck.Unsupported("Sam's TLR 22 5.0 has three tenths on Lucky Seven twelve laps in", facts + " Lap 12."));
        Assert.Null(CommentaryNumberCheck.Unsupported("Lucky Seven is three tenths back", facts));
    }

    [Fact]
    public void A_class_name_with_a_number_in_it_is_not_a_claim()
    {
        const string facts = "Box Stock heat 3 of the 17.5 Stock class. Sam leads by 0.3s.";

        Assert.Null(CommentaryNumberCheck.Unsupported("The 17.5 Stock boys are three tenths apart", facts));
        Assert.Null(CommentaryNumberCheck.Unsupported("In seventeen five stock, Sam by three tenths", facts));
    }

    [Fact]
    public void A_line_with_no_figures_passes_and_a_blank_one_too()
    {
        Assert.Null(CommentaryNumberCheck.Unsupported("What a fight out there!", "gap 0.3s"));
        Assert.Null(CommentaryNumberCheck.Unsupported(string.Empty, "gap 0.3s"));
        Assert.Null(CommentaryNumberCheck.Unsupported(null, null));
    }

    [Fact]
    public void A_year_in_words_is_let_go()
    {
        Assert.Null(CommentaryNumberCheck.Unsupported("the best lap of twenty twenty-six", "lap 5.425s"));
    }

    [Fact]
    public void Audio_tags_are_not_read()
    {
        Assert.Null(CommentaryNumberCheck.Unsupported("[excited] three tenths!", "gap 0.3s"));
    }

    [Fact]
    public void Facts_markers_are_found_in_a_prompt()
    {
        Assert.Equal("\ngap 0.3s\n", CommentaryNumberCheck.FactsIn("<facts>\ngap 0.3s\n</facts>\n\nWrite two sentences."));
        Assert.Equal("no markers", CommentaryNumberCheck.FactsIn("no markers"));
    }

    [Fact]
    public void A_whole_night_of_facts_does_not_hang_or_throw()
    {
        var facts = string.Join(" ", Enumerable.Range(1, 2000).Select(i => $"lap {i} at {5 + (i % 1000) / 1000.0:0.000}s"));

        Assert.Null(CommentaryNumberCheck.Unsupported("a five forty-two", facts));
    }
}

/// <summary>
/// The number check where a written segment comes back to the director, and
/// on the CommentaryService paths that are not the director's.
/// </summary>
public sealed class CommentaryNumberCheckDirectorTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 11, 8, 19, 0, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;
    }

    private sealed class Brain : ICommentaryBrain
    {
        private readonly Clock _clock;

        public Brain(Clock clock) => _clock = clock;

        public string? Answer { get; set; }

        public int Asked { get; private set; }

        public string? LastFacts { get; private set; }

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
            LastFacts = userPrompt;
            _clock.Advance(TimeSpan.FromSeconds(2));
            StatusMessage?.Invoke(string.Empty);

            return Task.FromResult(Answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private (CommentaryDirector Director, CommentaryService Commentary, Brain Model, List<string> Console) Rig()
    {
        var clock = new Clock();
        var settings = new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 40 };
        var model = new Brain(clock);
        var commentary = new CommentaryService(ProfileStore.Load(), settings) { Client = model };
        var director = new CommentaryDirector(commentary, settings, () => clock.Now);
        var console = new List<string>();

        director.Status += console.Add;
        director.NameFor = id => id == "A" ? "Sam" : "Bill";
        director.NewMeeting("day-1");
        director.MonthlyUsage(0, 400);
        director.NightShape(10, 30);
        director.RaceStarted("heat-1", "Box Stock");

        // A fight: Bill within 0.3s of Sam for half a minute.
        for (var i = 0; i < 6; i++)
        {
            director.Standings(new[] { new PairGap("A", "B", 1, 0.3, 0) });
            clock.Advance(TimeSpan.FromSeconds(5));
        }

        return (director, commentary, model, console);
    }

    [Fact]
    public async Task A_battle_call_with_an_invented_figure_is_dropped_and_released()
    {
        var (director, commentary, model, console) = Rig();
        model.Answer = "Bill's a four point nine back and closing on Sam!";

        Assert.Null(await director.NextAsync());

        Assert.Equal(1, model.Asked);

        // Written, so charged - the relay kept it - and nothing left in flight.
        Assert.Equal(1, director.Budget.SpentTonight);
        Assert.Equal(0, director.Budget.InFlight);
        Assert.Equal(0, director.Said.Count);
        Assert.Contains(console, line => line.Contains("it says four point nine, which is not in its facts"));

        // Never recorded, so Repeat does not offer it.
        Assert.Null(commentary.LastLine);
    }

    [Fact]
    public async Task A_battle_call_whose_figures_are_in_its_facts_is_said()
    {
        var (director, _, model, console) = Rig();
        model.Answer = "Sam's hanging on by three tenths - Bill is RIGHT there!";

        Assert.NotNull(await director.NextAsync());

        Assert.Equal(1, director.Said.Count);
        Assert.DoesNotContain(console, line => line.Contains("which is not in its facts"));
    }

    [Fact]
    public async Task A_night_recap_with_an_invented_figure_is_dropped_and_said_in_the_log()
    {
        var (_, commentary, model, _) = Rig();
        var log = new List<string>();
        commentary.Log = log.Add;
        model.Answer = "Sam won all eleven heats tonight.";

        var facts = new PackedFacts("Sam won 4 heats tonight. Bill won 2.", Array.Empty<string>(), 0);

        Assert.Null(await commentary.BuildNightRecapAsync(facts));
        Assert.Contains(log, line => line.Contains("Dropped the night recap - it says eleven"));

        model.Answer = "Sam won four heats tonight, Bill two.";
        Assert.Equal(model.Answer, await commentary.BuildNightRecapAsync(facts));
    }

    [Fact]
    public async Task The_race_s_own_lap_count_backs_a_line_as_its_facts_do()
    {
        var (_, commentary, model, _) = Rig();
        commentary.RaceLimitsNow = () => (12, null);
        model.Answer = "Twelve laps of this and Sam's hanging on by three tenths!";

        var facts = new PackedFacts("Bill has been within 0.30s of Sam for 30 seconds, fighting for the lead.", Array.Empty<string>(), 0);

        Assert.NotNull(await commentary.BuildSegmentAsync(SegmentKind.BattleWatch, facts));
    }
}
