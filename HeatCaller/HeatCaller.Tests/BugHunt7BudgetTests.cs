namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7, the commentary budget: the saved night read back on every
/// tick, tonight's lines charged twice, the budget changed from two threads
/// at once, and a refused colour line written by Ollama anyway.
/// </summary>
public sealed class BugHunt7BudgetTests
{
    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 13, 19, 0, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    /// <summary>A night as the director opens it: restored first, then the relay's month, then the programme.</summary>
    private static CommentaryBudget Opened(Clock clock, string? saved = null)
    {
        var budget = new CommentaryBudget(clock.Read);

        budget.Restore(saved, "day-1");
        budget.MonthlyUsage(0, 400);
        budget.NightsLeftThisMonth = 4;
        budget.NightShape(30, 30);

        return budget;
    }

    // ------------------------------------------------------------------
    // Restore on the half-second tick
    // ------------------------------------------------------------------

    [Fact]
    public void A_restore_on_every_tick_does_not_lift_the_per_class_cap()
    {
        var clock = new Clock();
        var budget = Opened(clock);

        var first = budget.Reserve(SegmentKind.BattleWatch, "Stock");
        Assert.True(first.Allowed);
        budget.Settle(first.Ticket);

        // What the director saved after that first line was spoken.
        var saved = budget.Save();
        var said = 1;

        for (var i = 0; i < 20; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(11));

            var verdict = budget.Reserve(SegmentKind.BattleWatch, "Stock");

            // The host's tick, landing while the line is being written.
            budget.Restore(saved, "day-1");

            if (!verdict.Allowed)
            {
                break;
            }

            budget.Settle(verdict.Ticket);
            said++;
        }

        Assert.Equal(CommentaryBudget.Spec(SegmentKind.BattleWatch).MaxPerClass, said);
    }

    [Fact]
    public void A_restore_on_the_same_day_does_not_put_tonight_s_spend_back_to_the_last_save()
    {
        var clock = new Clock();
        var budget = Opened(clock);

        var saved = budget.Save();

        for (var i = 0; i < 5; i++)
        {
            var verdict = budget.Reserve(SegmentKind.RaceIntro);
            Assert.True(verdict.Allowed);
            budget.Settle(verdict.Ticket);
        }

        budget.Restore(saved, "day-1");

        Assert.Equal(5, budget.SpentTonight);
    }

    [Fact]
    public void The_first_restore_of_a_day_already_opened_still_picks_the_night_up()
    {
        var clock = new Clock();
        var before = Opened(clock);

        for (var i = 0; i < 3; i++)
        {
            var verdict = before.Reserve(SegmentKind.RaceIntro);
            before.Settle(verdict.Ticket);
        }

        var after = new CommentaryBudget(clock.Read);

        after.NewMeeting("day-1");
        after.Restore(before.Save(), "day-1");

        Assert.Equal(3, after.SpentTonight);
    }

    [Fact]
    public void A_new_day_is_still_restored_after_the_last_one_was()
    {
        var clock = new Clock();
        var budget = Opened(clock);

        var verdict = budget.Reserve(SegmentKind.RaceIntro);
        budget.Settle(verdict.Ticket);

        budget.Restore(budget.Save(), "day-2");

        // Last night's spend is not tonight's.
        Assert.Equal(0, budget.SpentTonight);
    }

    // ------------------------------------------------------------------
    // Tonight's lines are charged once
    // ------------------------------------------------------------------

    [Fact]
    public void Tonight_s_lines_come_off_the_headroom_once_not_twice()
    {
        var clock = new Clock();
        var budget = Opened(clock);

        // 400 over four nights is a hundred.
        Assert.Equal(100.0, budget.NightAllotment, 3);

        for (var used = 0; used < 50; used++)
        {
            var verdict = budget.Reserve(SegmentKind.RaceIntro);
            Assert.True(verdict.Allowed);

            // The relay counts the line before its answer comes back,
            // which is before the director settles it.
            budget.MonthlyUsage(used + 1, 400);
            budget.Settle(verdict.Ticket);
        }

        Assert.Equal(100.0, budget.NightAllotment, 3);
        Assert.Equal(50.0, budget.Headroom, 3);
    }

    [Fact]
    public void Another_machine_s_lines_tonight_still_come_off_the_allotment()
    {
        var clock = new Clock();
        var budget = Opened(clock);

        // Twenty lines written by somebody else on the same club tonight.
        budget.MonthlyUsage(20, 400);

        Assert.Equal(95.0, budget.NightAllotment, 3);
    }

    [Fact]
    public void A_night_picked_up_after_a_restart_is_not_charged_twice_either()
    {
        var clock = new Clock();
        var before = Opened(clock);

        for (var used = 0; used < 10; used++)
        {
            var verdict = before.Reserve(SegmentKind.RaceIntro);
            before.MonthlyUsage(used + 1, 400);
            before.Settle(verdict.Ticket);
        }

        var after = new CommentaryBudget(clock.Read);

        after.Restore(before.Save(), "day-1");
        after.NightsLeftThisMonth = 4;
        after.NightShape(30, 30);

        // The relay's first answer after the restart already counts the
        // ten lines this night has spent.
        after.MonthlyUsage(10, 400);

        Assert.Equal(100.0, after.NightAllotment, 3);
        Assert.Equal(90.0, after.Headroom, 3);
    }

    // ------------------------------------------------------------------
    // Two threads at once
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_budget_can_be_written_from_the_pool_while_the_tick_reads_it()
    {
        var clock = new Clock();
        var budget = Opened(clock);
        var lines = CommentaryBudget.Spec(SegmentKind.RaceIntro).Lines;
        const int each = 2000;

        var writer = Task.Run(() =>
        {
            for (var i = 0; i < each; i++)
            {
                var verdict = budget.Reserve(SegmentKind.RaceIntro, $"class-{i % 7}");
                budget.Settle(verdict.Ticket, spoken: false);
            }
        });

        var tick = Task.Run(() =>
        {
            for (var i = 0; i < each; i++)
            {
                budget.Restore(null, "day-1");
                budget.RestoreSwitches(i % 2 == 0 ? "ColdOpen" : "ColdOpen,FormGuide");
                _ = budget.Describe();
                _ = budget.Headroom;
                _ = budget.SwitchedOff.Count;
                _ = budget.Save();
            }
        });

        await Task.WhenAll(writer, tick);

        Assert.Equal(0, budget.InFlight);
        Assert.Equal(each * lines, budget.SpentTonight);
    }

    // ------------------------------------------------------------------
    // A colour line the relay refuses is not handed to Ollama
    // ------------------------------------------------------------------

    private sealed class Brain : ICommentaryBrain
    {
        private readonly string? _answer;

        public Brain(string? answer) => _answer = answer;

        public int Asked { get; private set; }

        public event Action<string>? StatusMessage;

        public string Describe => "a test brain";

        public Task<string?> GenerateAsync(
            string systemPrompt,
            string userPrompt,
            int maxTokens,
            double temperature = 0.7,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            Asked++;

            // Keeps the compiler happy about an event nothing subscribes to.
            StatusMessage?.Invoke(string.Empty);

            return Task.FromResult(_answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    [Fact]
    public async Task A_relay_only_line_is_not_written_by_the_second_brain()
    {
        var relay = new Brain(null);
        var local = new Brain("Written on the laptop.");
        var brain = new FallbackCommentaryBrain(relay, local);

        try
        {
            FallbackCommentaryBrain.RelayOnly = true;

            var line = await brain.GenerateAsync("system", "user", 40);

            Assert.Null(line);
            Assert.Equal(1, relay.Asked);
            Assert.Equal(0, local.Asked);
        }
        finally
        {
            FallbackCommentaryBrain.RelayOnly = false;
        }
    }

    [Fact]
    public async Task Any_other_line_still_falls_back_to_the_second_brain()
    {
        var relay = new Brain(null);
        var local = new Brain("Written on the laptop.");
        var brain = new FallbackCommentaryBrain(relay, local);

        var line = await brain.GenerateAsync("system", "user", 40);

        Assert.Equal("Written on the laptop.", line);
        Assert.Equal(1, local.Asked);
    }
}
