using System.Globalization;
using System.Net;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// The switch-over (19 Oct 2026): when the relay's commentary lines for the
/// month are used up, the model on this PC writes instead - even with "Let
/// the model on this PC write when the relay can't" unticked - and the
/// relay is not asked line after line just to refuse. It is back in front
/// as soon as the usage store shows lines again.
/// </summary>
public sealed class CommentarySwitchOverTests : IDisposable
{
    // Declared first: the relay client asks RestoredSettings, which reads
    // a file where this points.
    private readonly TempAppData _data = new();
    private readonly HttpClient _realHttp = RelayCommentaryClient.Http;

    private static readonly DateTime Resets = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string SpentBody =
        "{\"ok\":false,\"refusal\":\"allowance\",\"error\":\"This club has used its commentary allowance for the month.\","
        + "\"used\":400,\"allowance\":400,\"resetsAt\":\"2026-11-01T00:00:00.000Z\"}";

    private const string RelayLine = "Side by side into the hairpin, and neither one is lifting!";

    private DateTime _now = new(2026, 10, 19, 20, 0, 0, DateTimeKind.Utc);

    public CommentarySwitchOverTests()
    {
        // The store and the client read one clock: this test's. Left on the
        // real one, the store judged the test's spent October by today's
        // date - fine until the real clock passed 1 November, after which it
        // called it "last month's figure" and three of these failed on every
        // run (1 Nov 2026).
        UsageMeters.Shared.UseClockForTests(() => _now);
        UsageMeters.Shared.Forget(MeterKind.Commentary);
    }

    public void Dispose()
    {
        RelayCommentaryClient.Http = _realHttp;

        // The store is one per process, so a spent month would follow this
        // class into the next test.
        UsageMeters.Shared.Forget(MeterKind.Commentary);
        UsageMeters.Shared.UseClockForTests(null);
        _data.Dispose();
    }

    // ------------------------------------------------------------------
    // The relay client and the pair
    // ------------------------------------------------------------------

    [Fact]
    public async Task An_allowance_refusal_sends_the_next_line_to_this_pc_without_asking_the_relay_again()
    {
        var relay = Serving(HttpStatusCode.TooManyRequests, SpentBody);
        var client = Client();
        var local = new Brain("Written on this PC.");
        var brain = new FallbackCommentaryBrain(client, local, onAnyFailure: false);

        var first = await brain.GenerateAsync("system", "facts", 40);

        Assert.Equal("Written on this PC.", first);
        Assert.Equal(1, relay.Asked);
        Assert.True(client.MonthSpent);
        Assert.Equal(400, client.MonthAllowance);
        Assert.True(UsageMeters.Shared.Get(MeterKind.Commentary)?.Spent);

        var second = await brain.GenerateAsync("system", "facts", 40);

        Assert.Equal("Written on this PC.", second);
        Assert.Equal(1, relay.Asked);
        Assert.Equal(2, local.Asked);
        Assert.True(brain.TakingOver);
    }

    [Fact]
    public async Task The_relay_writes_again_as_soon_as_the_store_shows_lines_left()
    {
        var relay = Serving(HttpStatusCode.TooManyRequests, SpentBody);
        var client = Client();
        var local = new Brain("Written on this PC.");
        var brain = new FallbackCommentaryBrain(client, local, onAnyFailure: false);

        await brain.GenerateAsync("system", "facts", 40);
        Assert.True(client.MonthSpent);

        // A top-up, heard by the "This month" refresh.
        UsageMeters.Shared.Report(MeterKind.Commentary, 400, 450, Resets, topUp: 50);
        relay.Answer(HttpStatusCode.OK, $"{{\"ok\":true,\"text\":\"{RelayLine}\",\"used\":401,\"allowance\":450}}");

        Assert.False(client.MonthSpent);

        var line = await brain.GenerateAsync("system", "facts", 40);

        Assert.Equal(RelayLine, line);
        Assert.Equal(2, relay.Asked);
        Assert.Equal(1, local.Asked);
        Assert.False(brain.TakingOver);
    }

    [Fact]
    public async Task One_line_goes_through_to_a_spent_relay_each_half_hour()
    {
        var relay = Serving(HttpStatusCode.TooManyRequests, SpentBody);
        var client = Client();
        var brain = new FallbackCommentaryBrain(client, new Brain("Written on this PC."), onAnyFailure: false);

        await brain.GenerateAsync("system", "facts", 40);
        _now += TimeSpan.FromMinutes(29);
        await brain.GenerateAsync("system", "facts", 40);

        Assert.Equal(1, relay.Asked);

        _now += TimeSpan.FromMinutes(2);
        await brain.GenerateAsync("system", "facts", 40);
        await brain.GenerateAsync("system", "facts", 40);

        Assert.Equal(2, relay.Asked);
    }

    [Fact]
    public async Task The_relay_is_asked_again_once_the_month_has_reset()
    {
        var relay = Serving(HttpStatusCode.TooManyRequests, SpentBody);
        var client = Client();
        var brain = new FallbackCommentaryBrain(client, new Brain("Written on this PC."), onAnyFailure: false);

        await brain.GenerateAsync("system", "facts", 40);
        Assert.True(client.MonthSpent);

        _now = Resets.AddMinutes(1);

        Assert.False(client.MonthSpent);

        await brain.GenerateAsync("system", "facts", 40);

        Assert.Equal(2, relay.Asked);
    }

    [Fact]
    public async Task The_console_is_told_once_and_calmly()
    {
        Serving(HttpStatusCode.TooManyRequests, SpentBody);
        var client = Client();
        client.LocalTakesOver = () => true;
        var brain = new FallbackCommentaryBrain(client, new Brain("Written on this PC."), onAnyFailure: false);
        var said = new List<string>();
        brain.StatusMessage += said.Add;

        await brain.GenerateAsync("system", "facts", 40);
        await brain.GenerateAsync("system", "facts", 40);
        await brain.GenerateAsync("system", "facts", 40);

        var spent = said.Where(line => line.Contains("relay lines are used up", StringComparison.Ordinal)).ToList();

        Assert.Single(spent);
        Assert.Equal("[brain] This month's relay lines are used up - the model on this PC is writing until 1 Nov.", spent[0]);
        Assert.DoesNotContain(said, line => line.Contains("wouldn't write that one", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_no_model_here_the_console_says_normal_announcements_carry_on()
    {
        Serving(HttpStatusCode.TooManyRequests, SpentBody);
        var client = Client();
        client.LocalTakesOver = () => false;
        var said = new List<string>();
        client.StatusMessage += said.Add;

        Assert.Null(await client.GenerateAsync("system", "facts", 40));

        Assert.Contains("[brain] This month's relay lines are used up - normal announcements carry on until 1 Nov.", said);
    }

    [Fact]
    public async Task Unticked_falls_back_for_a_spent_month_but_not_for_any_other_failure()
    {
        var relay = Serving(HttpStatusCode.BadGateway, "{\"ok\":false,\"refusal\":\"upstream\",\"error\":\"The commentary brain couldn't write that one.\",\"used\":12,\"allowance\":400}");
        var local = new Brain("Written on this PC.");
        var unticked = new FallbackCommentaryBrain(Client(), local, onAnyFailure: false);

        Assert.Null(await unticked.GenerateAsync("system", "facts", 40));
        Assert.Equal(0, local.Asked);

        relay.Answer(HttpStatusCode.TooManyRequests, SpentBody);

        Assert.Equal("Written on this PC.", await unticked.GenerateAsync("system", "facts", 40));
        Assert.Equal(1, local.Asked);
    }

    [Fact]
    public async Task Ticked_still_falls_back_for_any_failure()
    {
        Serving(HttpStatusCode.BadGateway, "{\"ok\":false,\"refusal\":\"upstream\",\"error\":\"The commentary brain couldn't write that one.\",\"used\":12,\"allowance\":400}");
        var local = new Brain("Written on this PC.");
        var ticked = new FallbackCommentaryBrain(Client(), local);

        Assert.Equal("Written on this PC.", await ticked.GenerateAsync("system", "facts", 40));
        Assert.Equal(1, local.Asked);
    }

    [Fact]
    public async Task A_bare_429_is_the_rate_limit_not_a_spent_month()
    {
        var relay = Serving(HttpStatusCode.TooManyRequests, "{\"ok\":false,\"error\":\"Too many lines at once. Wait a moment and try again.\"}");
        var client = Client();
        var local = new Brain("Written on this PC.");
        var unticked = new FallbackCommentaryBrain(client, local, onAnyFailure: false);

        Assert.Null(await unticked.GenerateAsync("system", "facts", 40));
        Assert.False(client.MonthSpent);

        await unticked.GenerateAsync("system", "facts", 40);

        Assert.Equal(2, relay.Asked);
        Assert.Equal(0, local.Asked);
    }

    [Fact]
    public async Task Colour_and_flourish_stay_relay_only_while_the_month_is_spent()
    {
        Serving(HttpStatusCode.TooManyRequests, SpentBody);
        var local = new Brain("Written on this PC.");
        var brain = new FallbackCommentaryBrain(Client(), local, onAnyFailure: false);

        try
        {
            FallbackCommentaryBrain.RelayOnly = true;

            Assert.Null(await brain.GenerateAsync("system", "facts", 40));
            Assert.Equal(0, local.Asked);
        }
        finally
        {
            FallbackCommentaryBrain.RelayOnly = false;
        }
    }

    [Fact]
    public async Task Test_connection_still_asks_a_spent_relay_and_finds_a_top_up()
    {
        var relay = Serving(HttpStatusCode.TooManyRequests, SpentBody);
        var client = Client();

        await client.GenerateAsync("system", "facts", 40);
        Assert.True(client.MonthSpent);

        relay.Answer(HttpStatusCode.OK, "{\"ok\":true,\"text\":\"OK\",\"used\":401,\"allowance\":450}");

        Assert.Equal("OK", await client.TestAsync());
        Assert.Equal(2, relay.Asked);
        Assert.False(client.MonthSpent);
    }

    // ------------------------------------------------------------------
    // The budget
    // ------------------------------------------------------------------

    [Fact]
    public void The_budget_stops_refusing_core_for_the_month_once_this_pc_is_writing()
    {
        var budget = SpentNight();

        Assert.Equal(BudgetRefusal.TheMonthsAllowance, budget.Peek(SegmentKind.BattleWatch, "Stock").Why);

        budget.LocalModelTakesOver = () => true;

        var verdict = budget.Peek(SegmentKind.BattleWatch, "Stock");

        Assert.True(verdict.Allowed);
        Assert.Equal(BrainFallback.LocalModel, verdict.Fallback);
        Assert.True(budget.LocalModelWriting);
    }

    [Fact]
    public void The_budget_still_refuses_relay_only_tiers_while_this_pc_is_writing()
    {
        var budget = SpentNight();
        budget.LocalModelTakesOver = () => true;

        var verdict = budget.Peek(SegmentKind.FormGuide, "Stock");

        Assert.False(verdict.Allowed);
        Assert.Equal(BudgetRefusal.TheMonthsAllowance, verdict.Why);
    }

    [Fact]
    public void The_budget_keeps_its_other_gates_while_this_pc_is_writing()
    {
        var budget = SpentNight();
        budget.LocalModelTakesOver = () => true;

        var verdict = budget.Reserve(SegmentKind.BattleWatch, "Stock");
        Assert.True(verdict.Allowed);
        budget.Settle(verdict.Ticket);

        // Straight after a spoken line: the gap each segment keeps still holds.
        Assert.Equal(BudgetRefusal.TooSoonAfterTheLast, budget.Peek(SegmentKind.BattleWatch, "Stock").Why);
    }

    [Fact]
    public void Describe_says_the_model_on_this_pc_is_writing()
    {
        var budget = SpentNight();

        Assert.DoesNotContain(CommentaryBudget.LocalWritingSentence, budget.Describe(), StringComparison.Ordinal);

        budget.LocalModelTakesOver = () => true;

        Assert.Equal(
            "Tonight: 0 lines written. This month's relay lines are used up - the model on this PC is writing tonight's commentary.",
            budget.Describe());
    }

    [Fact]
    public void Lines_this_pc_wrote_for_the_spent_month_leave_a_top_up_for_tonight()
    {
        var clock = new DateTime(2026, 10, 19, 19, 0, 0);
        var local = true;

        CommentaryBudget Night()
        {
            var night = new CommentaryBudget(() => clock);
            night.Restore(null, "day-1");
            night.MonthlyUsage(400, 400);
            night.NightsLeftThisMonth = 1;
            night.NightShape(30, 30);
            night.LocalModelTakesOver = () => local;
            return night;
        }

        var budget = Night();
        var untouched = Night();

        // Three live lines written on this PC while the month was spent.
        for (var i = 0; i < 3; i++)
        {
            var verdict = budget.Reserve(SegmentKind.BattleWatch, "Stock");
            Assert.True(verdict.Allowed);
            budget.Settle(verdict.Ticket);
            clock = clock.AddMinutes(5);
        }

        Assert.Equal(3, budget.SpentTonight);

        // Then a top-up of 50 lands, and the relay writes again.
        local = false;
        budget.MonthlyUsage(400, 450);
        untouched.MonthlyUsage(400, 450);

        // The local lines cost no relay lines, so tonight's share of the
        // top-up is all still there - as if they had never been written.
        Assert.Equal(untouched.Headroom, budget.Headroom);
        Assert.True(budget.Headroom > 0);
        Assert.Equal(3, budget.SpentTonight);
    }

    [Fact]
    public void A_spent_figure_without_a_reset_date_does_not_hold_into_the_next_month()
    {
        var client = Client();

        // An older relay's figure, with no resetsAt, heard in the last
        // minute of October UTC - 7:59pm in New York.
        _now = new DateTime(2026, 10, 31, 23, 59, 0, DateTimeKind.Utc);
        UsageMeters.Shared.Report(MeterKind.Commentary, 400, 400, null);

        Assert.True(client.MonthSpent);

        // The relay's months turn at midnight UTC.
        _now = new DateTime(2026, 11, 1, 0, 0, 1, DateTimeKind.Utc);
        Assert.False(client.MonthSpent);
    }

    [Theory]
    [InlineData("2026-11-01T00:00:00.000Z")] // the 1st at midnight UTC: a US club's evening of the 31st
    [InlineData("2027-01-01T00:00:00.000Z")] // across the year end
    [InlineData("2027-03-01T00:00:00.000Z")] // after a 28-day February
    [InlineData("2028-03-01T00:00:00.000Z")] // after a leap-year February
    [InlineData("2027-07-01T00:00:00.000Z")] // after a 30-day month, in summer time
    public async Task A_spent_month_holds_right_up_to_its_reset_and_not_a_moment_after(string resetsAt)
    {
        var resets = DateTime.Parse(resetsAt, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        var spent = SpentBody.Replace("2026-11-01T00:00:00.000Z", resetsAt, StringComparison.Ordinal);

        var relay = Serving(HttpStatusCode.TooManyRequests, spent);
        var client = Client();
        var local = new Brain("Written on this PC.");
        var brain = new FallbackCommentaryBrain(client, local, onAnyFailure: false);

        // The last race night of the month, two hours before the reset.
        _now = resets.AddHours(-2);

        Assert.Equal("Written on this PC.", await brain.GenerateAsync("system", "facts", 40));
        Assert.True(client.MonthSpent);
        Assert.Equal(400, client.MonthAllowance);

        _now = resets.AddSeconds(-1);
        Assert.True(client.MonthSpent);

        // The new month: back to the relay at once, not after a probe.
        _now = resets;
        relay.Answer(HttpStatusCode.OK, $"{{\"ok\":true,\"text\":\"{RelayLine}\",\"used\":1,\"allowance\":400}}");

        Assert.False(client.MonthSpent);
        Assert.Equal(RelayLine, await brain.GenerateAsync("system", "facts", 40));
        Assert.Equal(2, relay.Asked);
        Assert.Equal(1, local.Asked);
        Assert.False(client.MonthSpent);
    }

    [Fact]
    public void The_budget_is_unchanged_while_the_month_has_lines()
    {
        var budget = new CommentaryBudget(() => new DateTime(2026, 10, 19, 19, 0, 0));
        budget.Restore(null, "day-1");
        budget.MonthlyUsage(390, 400);
        budget.NightsLeftThisMonth = 4;
        budget.NightShape(30, 30);
        budget.LocalModelTakesOver = () => true;

        // Below the reserve but not spent: the relay is still writing, so
        // its lines are still rationed for intros and recaps.
        Assert.Equal(BudgetRefusal.TheMonthsAllowance, budget.Peek(SegmentKind.BattleWatch, "Stock").Why);
        Assert.False(budget.LocalModelWriting);
    }

    // ------------------------------------------------------------------
    // Test Connection's words
    // ------------------------------------------------------------------

    [Fact]
    public void Test_connection_reads_as_a_calm_sentence_when_the_month_is_spent()
    {
        Assert.Equal(
            ("This month's 400 relay lines are used up - the model on this PC is writing instead.", true),
            CommentaryControl.SpentTestResult(400, writtenHere: true, modelHere: true));

        Assert.Equal(
            ("This month's 400 relay lines are used up - no model is installed on this PC, so normal announcements carry on.", false),
            CommentaryControl.SpentTestResult(400, writtenHere: false, modelHere: false));

        Assert.StartsWith(
            "This month's relay lines are used up",
            CommentaryControl.SpentTestResult(null, writtenHere: true, modelHere: true).Text,
            StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------

    private RelayCommentaryClient Client() => new(new AppSettings
    {
        RelayBaseUrl = "http://localhost:8787",
        RelayClub = "testclub",
        RelayToken = "club-token",
    })
    {
        UtcNow = () => _now,
    };

    /// <summary>A night whose relay month is spent: 400 of 400.</summary>
    private static CommentaryBudget SpentNight()
    {
        var budget = new CommentaryBudget(() => new DateTime(2026, 10, 19, 19, 0, 0));

        budget.Restore(null, "day-1");
        budget.MonthlyUsage(400, 400);
        budget.NightsLeftThisMonth = 4;
        budget.NightShape(30, 30);

        return budget;
    }

    private static FakeRelay Serving(HttpStatusCode status, string body)
    {
        var relay = new FakeRelay(status, body);
        RelayCommentaryClient.Http = new HttpClient(relay);
        return relay;
    }

    /// <summary>The relay's brain/write, answering whatever it was last told to and counting the asks.</summary>
    private sealed class FakeRelay(HttpStatusCode status, string body) : HttpMessageHandler
    {
        private HttpStatusCode _status = status;
        private string _body = body;
        private int _asked;

        public int Asked => Volatile.Read(ref _asked);

        public void Answer(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _asked);

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }

    /// <summary>The model on this PC, as far as the pair can tell: answers every line with the same words.</summary>
    private sealed class Brain(string answer) : ICommentaryBrain
    {
        private int _asked;

        public int Asked => Volatile.Read(ref _asked);

        public event Action<string>? StatusMessage;

        public string Describe => "the built-in model";

        public Task<string?> GenerateAsync(
            string systemPrompt,
            string userPrompt,
            int maxTokens,
            double temperature = 0.7,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            Interlocked.Increment(ref _asked);
            StatusMessage?.Invoke(string.Empty);
            return Task.FromResult<string?>(answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("OK");
    }
}
