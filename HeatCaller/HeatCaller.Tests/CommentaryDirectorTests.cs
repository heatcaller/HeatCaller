namespace HeatCaller.Tests;

/// <summary>
/// The director: the order the budget and the facts are spent in.
///
/// Getting this order wrong is silent, which is why it is tested harder
/// than anything else in the layer. A line that cost the club its
/// allowance and was never spoken leaves no trace; nor does the best fact
/// of the night being burned on a request that timed out. Both show up
/// weeks later as "it has gone quiet" and neither is traceable from a log.
/// </summary>
public sealed class CommentaryDirectorTests : IDisposable
{
    private readonly Rig _rig = new();

    public void Dispose() => _rig.Dispose();

    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 10, 8, 19, 0, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    /// <summary>A brain the test drives: what it writes, how long it takes, and whether it fails.</summary>
    private sealed class Brain : ICommentaryBrain
    {
        private readonly Clock _clock;

        public Brain(Clock clock) => _clock = clock;

        public string? Answer { get; set; } = "They are nose to tail out there!";

        public TimeSpan Takes { get; set; } = TimeSpan.FromSeconds(2);

        public Exception? Throws { get; set; }

        public int Asked { get; private set; }

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
            _clock.Advance(Takes);

            if (Throws is { } ex)
            {
                return Task.FromException<string?>(ex);
            }

            // Keeps the compiler happy about an event nothing subscribes to.
            StatusMessage?.Invoke(string.Empty);

            return Task.FromResult(Answer);
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private sealed class Rig : IDisposable
    {
        /// <summary>A folder of its own: the director saves the night's spend as it goes.</summary>
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

        /// <summary>Puts a fight on track that has lasted long enough to be worth calling.</summary>
        public void AFight()
        {
            for (var i = 0; i < 6; i++)
            {
                Director.Standings(new[] { new PairGap("A", "B", 1, 0.3, 0) });
                Clock.Advance(TimeSpan.FromSeconds(5));
            }
        }

        public void Dispose() => _data.Dispose();
    }

    // ------------------------------------------------------------------
    // The night's shape
    //
    // Unwired, this is silent and expensive: the budget sits at its
    // opening float all evening and the later segments starve, with
    // nothing in any log to say why the second half of the night went
    // quiet.
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_programme_s_progress_is_read_and_handed_to_the_budget()
    {
        _rig.Director.NightProgress = () => (20, 30);

        await _rig.Director.NextAsync();

        Assert.Equal(20, _rig.Director.Budget.RoundsRun);
        Assert.Equal(30, _rig.Director.Budget.RoundsTotal);
    }

    [Fact]
    public async Task The_programme_is_not_counted_on_every_tick()
    {
        // NextAsync runs twice a second all night and counting the rounds
        // walks a store, where what it finds can only change when a race
        // is filed - minutes apart.
        var counted = 0;

        _rig.Director.NightProgress = () =>
        {
            counted++;
            return (1, 30);
        };

        for (var i = 0; i < 10; i++)
        {
            await _rig.Director.NextAsync();
            _rig.Clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        Assert.Equal(1, counted);

        // Past the throttle, it looks again.
        _rig.Clock.Advance(TimeSpan.FromSeconds(10));
        await _rig.Director.NextAsync();

        Assert.Equal(2, counted);
    }

    [Fact]
    public async Task A_programme_reader_that_throws_does_not_stop_the_segments()
    {
        _rig.Director.NightProgress = () => throw new InvalidOperationException("the round store is being saved");

        _rig.AFight();

        var said = await _rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.Contains(_rig.Console, line => line.Contains("progress", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Without_a_reader_the_budget_keeps_what_it_was_told()
    {
        // The rig sets 10 of 30 by hand. Nothing should undo that just
        // because no reader is wired.
        Assert.Null(_rig.Director.NightProgress);

        await _rig.Director.NextAsync();

        Assert.Equal(10, _rig.Director.Budget.RoundsRun);
        Assert.Equal(30, _rig.Director.Budget.RoundsTotal);
    }

    // ------------------------------------------------------------------
    // The happy path
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_fight_worth_calling_is_written_spent_and_marked()
    {
        _rig.AFight();

        var said = await _rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.Equal(SegmentKind.BattleWatch, said!.Kind);
        Assert.Equal("They are nose to tail out there!", said.Text);
        Assert.False(said.IsExchange);

        Assert.Equal(1, _rig.Director.Budget.SpentTonight);
        Assert.Equal(1, _rig.Director.Said.Count);
        Assert.Equal(0, _rig.Director.Budget.InFlight);
    }

    [Fact]
    public async Task Nothing_happening_costs_nothing_and_asks_nothing()
    {
        Assert.Null(await _rig.Director.NextAsync());

        Assert.Equal(0, _rig.Model.Asked);
        Assert.Equal(0, _rig.Director.Budget.SpentTonight);
        Assert.Equal(0, _rig.Director.Said.Count);
    }

    // ------------------------------------------------------------------
    // Giving things back
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_write_that_failed_gives_the_budget_back_and_marks_no_facts()
    {
        _rig.Model.Answer = null;
        _rig.AFight();

        Assert.Null(await _rig.Director.NextAsync());

        Assert.Equal(1, _rig.Model.Asked);
        Assert.Equal(0, _rig.Director.Budget.SpentTonight);
        Assert.Equal(0, _rig.Director.Said.Count);
        Assert.Equal(0, _rig.Director.Budget.InFlight);
    }

    [Fact]
    public async Task A_brain_that_throws_gives_the_budget_back_rather_than_taking_the_race_down()
    {
        _rig.Model.Throws = new InvalidOperationException("the relay fell over");
        _rig.AFight();

        Assert.Null(await _rig.Director.NextAsync());

        Assert.Equal(0, _rig.Director.Budget.SpentTonight);
        Assert.Equal(0, _rig.Director.Budget.InFlight);
        Assert.Contains(_rig.Console, line => line.Contains("the relay fell over"));
    }

    [Fact]
    public async Task A_line_that_came_back_after_the_moment_had_gone_is_not_said_and_is_not_charged()
    {
        // The battle call's whole point is that it is true right now. One
        // that took half a minute to write is describing a race that has
        // moved on, and reading it out is worse than silence.
        _rig.Model.Takes = TimeSpan.FromSeconds(40);
        _rig.AFight();

        Assert.Null(await _rig.Director.NextAsync());

        Assert.Equal(1, _rig.Model.Asked);
        Assert.Equal(0, _rig.Director.Budget.SpentTonight);
        Assert.Equal(0, _rig.Director.Said.Count);
        Assert.Contains(_rig.Console, line => line.Contains("after the moment had gone"));
    }

    // ------------------------------------------------------------------
    // Not saying it twice
    // ------------------------------------------------------------------

    [Fact]
    public async Task The_same_fight_is_one_story_and_not_one_a_tick()
    {
        _rig.AFight();

        Assert.NotNull(await _rig.Director.NextAsync());

        // Still nose to tail, still the same fight.
        _rig.AFight();

        Assert.Null(await _rig.Director.NextAsync());
        Assert.Equal(1, _rig.Model.Asked);
    }

    [Fact]
    public async Task A_refusal_from_the_budget_never_reaches_the_model()
    {
        _rig.Director.Budget.SetOff(SegmentKind.BattleWatch, true);
        _rig.AFight();

        Assert.Null(await _rig.Director.NextAsync());

        Assert.Equal(0, _rig.Model.Asked);
        Assert.Contains(_rig.Console, line => line.Contains("switched off"));
    }

    [Fact]
    public async Task The_console_says_why_it_went_quiet()
    {
        // A month nearly spent: everything but the essentials is held.
        _rig.Director.MonthlyUsage(395, 400);
        _rig.AFight();

        Assert.Null(await _rig.Director.NextAsync());

        Assert.Equal(0, _rig.Model.Asked);
        Assert.Contains(_rig.Console, line => line.Contains("Skipped battle watch"));
    }

    // ------------------------------------------------------------------
    // Which segment goes first
    // ------------------------------------------------------------------

    [Fact]
    public async Task While_a_race_is_running_only_the_live_segments_are_tried()
    {
        _rig.Director.Upcoming = () => (new[] { new UpcomingRound("Q2 Heat 1", "Box Stock", 8) }, TimeSpan.FromMinutes(4));

        // Nothing live is happening, and "coming up next" is a between
        // races line - saying it over a running race is the wrong moment.
        Assert.Null(await _rig.Director.NextAsync());
        Assert.Equal(0, _rig.Model.Asked);

        _rig.Director.RaceFinished();

        var said = await _rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.Equal(SegmentKind.ComingUpNext, said!.Kind);
    }

    [Fact]
    public async Task A_record_lap_is_called_once_the_race_has_some_laps_in_it()
    {
        // Warm the race up past the laps where "fastest so far" is silly,
        // then a lap that beats them all.
        for (var i = 0; i < 4; i++)
        {
            _rig.Director.Lap($"warm-{i}", 20.0 + i);
        }

        _rig.Director.Lap("A", 12.0);

        var said = await _rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.Equal(SegmentKind.PaceNote, said!.Kind);
    }

    [Fact]
    public async Task When_two_are_ready_the_shorter_lived_one_is_said_first()
    {
        _rig.AFight();

        for (var i = 0; i < 4; i++)
        {
            _rig.Director.Lap($"warm-{i}", 20.0 + i);
        }

        _rig.Director.Lap("A", 12.0);

        // A battle call stops being true inside twenty seconds; a record
        // lap has thirty. Whichever is about to go off goes first, and
        // the other is still there afterwards.
        var first = await _rig.Director.NextAsync();

        Assert.NotNull(first);
        Assert.Equal(SegmentKind.BattleWatch, first!.Kind);

        // The record lap is not spoken straight over the top of it: the
        // budget keeps a gap after every spoken line, and says so.
        Assert.Null(await _rig.Director.NextAsync());
        Assert.Contains(_rig.Console, line => line.Contains("since the last line"));
        Assert.Equal(1, _rig.Model.Asked);

        // But its gap is shorter than its life, so once the gap has passed
        // it is still there to be said.
        _rig.Clock.Advance(CommentaryBudget.Spec(SegmentKind.PaceNote).MinGap);

        var second = await _rig.Director.NextAsync();

        Assert.NotNull(second);
        Assert.Equal(SegmentKind.PaceNote, second!.Kind);
    }

    [Fact]
    public async Task A_record_lap_nobody_could_afford_in_time_is_dropped_rather_than_kept()
    {
        for (var i = 0; i < 4; i++)
        {
            _rig.Director.Lap($"warm-{i}", 20.0 + i);
        }

        _rig.Director.Lap("A", 12.0);

        // Half a minute of the announcer being busy. By now it is not news.
        _rig.Clock.Advance(TimeSpan.FromSeconds(45));

        Assert.Null(await _rig.Director.NextAsync());
        Assert.Equal(0, _rig.Model.Asked);
    }

    // ------------------------------------------------------------------
    // Two voices
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_club_with_a_co_host_gets_the_fight_as_a_two_hander()
    {
        _rig.Settings.CommentaryCoHost = true;
        _rig.Settings.CommentaryCoHostName = "Dave";
        _rig.Model.Answer = "A: Bill is all over him.\nB: And he still cannot get by.";

        _rig.AFight();

        var said = await _rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.True(said!.IsExchange);
        Assert.Equal(2, said.Turns.Count);
        Assert.False(said.Turns[0].FromCoHost);
        Assert.True(said.Turns[1].FromCoHost);
        Assert.Contains("Dave: And he still cannot get by.", said.Text);

        // One request for the pair, not one each.
        Assert.Equal(1, _rig.Model.Asked);
        Assert.Equal(1, _rig.Director.Budget.SpentTonight);
    }

    [Fact]
    public async Task An_exchange_the_model_would_not_split_is_said_in_one_voice_rather_than_lost()
    {
        _rig.Settings.CommentaryCoHost = true;
        _rig.Settings.CommentaryCoHostName = "Dave";
        _rig.Model.Answer = "They are nose to tail and neither will give an inch.";

        _rig.AFight();

        var said = await _rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.False(said!.IsExchange);
        Assert.Equal(2, _rig.Model.Asked);
        Assert.Contains(_rig.Console, line => line.Contains("one voice"));
    }

    // ------------------------------------------------------------------
    // The sponsors, which are not on the ladder
    // ------------------------------------------------------------------

    [Fact]
    public void A_sponsor_read_never_touches_the_budget_or_the_model()
    {
        // A month entirely spent. The sponsor still gets their read,
        // because it is something the club sold rather than colour.
        _rig.Director.MonthlyUsage(400, 400);
        _rig.Director.SponsorList = () => new[] { new SponsorSlot("hobby", "Clarke and Sons") };

        var read = _rig.Director.NextSponsorRead(SponsorMoment.Opening);

        Assert.NotNull(read);
        Assert.Contains("Clarke and Sons", read!.Text);
        Assert.Equal(0, _rig.Model.Asked);
        Assert.Equal(0, _rig.Director.Budget.SpentTonight);
    }

    [Fact]
    public void A_club_that_has_sold_nothing_hears_nothing()
    {
        Assert.Null(_rig.Director.NextSponsorRead(SponsorMoment.Opening));

        _rig.Director.SponsorList = () => Array.Empty<SponsorSlot>();

        Assert.Null(_rig.Director.NextSponsorRead(SponsorMoment.Opening));
    }

    // ------------------------------------------------------------------
    // What has not been fed yet
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_segment_with_no_reader_behind_it_stays_quiet_rather_than_inventing_half()
    {
        _rig.Director.RaceFinished();

        // No GridHistory, no Qualifying, no Championship, no NightAhead.
        Assert.Null(await _rig.Director.NextAsync());
        Assert.Equal(0, _rig.Model.Asked);
    }

    [Fact]
    public async Task A_reader_that_is_wired_fires_and_the_rest_stay_quiet()
    {
        _rig.Director.RaceFinished();
        _rig.Director.Championship = () => new[]
        {
            new ChampionshipStanding("A", "Sam", PositionNow: 1, PositionBefore: 2, Points: 88, BehindLeader: 0),
            new ChampionshipStanding("B", "Bill", PositionNow: 2, PositionBefore: 1, Points: 85, BehindLeader: 3),
        };

        var said = await _rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.Equal(SegmentKind.ChampionshipCheck, said!.Kind);
    }

    // ------------------------------------------------------------------
    // Across the night
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_new_race_is_a_clean_sheet_for_the_detectors_but_not_for_the_night()
    {
        _rig.AFight();

        Assert.NotNull(await _rig.Director.NextAsync());
        Assert.Equal(1, _rig.Director.Budget.SpentTonight);

        // The main goes out minutes after the heat, well clear of the
        // gap the budget keeps between spoken lines.
        _rig.Clock.Advance(TimeSpan.FromMinutes(5));
        _rig.Director.RaceStarted("a-main", "Box Stock");

        // The same two drivers fighting again is a fresh story, and the
        // cooldown from the heat does not follow them into the main.
        _rig.AFight();

        Assert.NotNull(await _rig.Director.NextAsync());
        Assert.Equal(2, _rig.Director.Budget.SpentTonight);
        Assert.Equal(2, _rig.Director.Said.Count);
    }

    [Fact]
    public async Task Closing_the_laptop_mid_evening_does_not_refill_the_night()
    {
        _rig.AFight();

        Assert.NotNull(await _rig.Director.NextAsync());

        var spent = _rig.Director.Budget.SpentTonight;

        // A second director over the same settings, as a restart gives.
        var back = new CommentaryDirector(_rig.Commentary, _rig.Settings, _rig.Clock.Read);

        back.NewMeeting("day-1");

        Assert.Equal(spent, back.Budget.SpentTonight);
    }

    // ------------------------------------------------------------------
    // The horizon - what is still to be decided (15 Oct 2026)
    // ------------------------------------------------------------------

    /// <summary>A gap coming down two tenths a lap, with the laps running out.</summary>
    private void AGapComingDown(bool estimated = false)
    {
        var gaps = new[] { 1.0, 0.8, 0.6 };

        for (var lap = 0; lap < gaps.Length; lap++)
        {
            _rig.Director.Standings(
                new[] { new PairGap("A", "B", 1, gaps[lap], 0) },
                leaderLaps: 10 + lap,
                new RoadLeft(5 - lap, estimated));

            _rig.Clock.Advance(TimeSpan.FromSeconds(6));
        }
    }

    [Fact]
    public async Task A_gap_coming_down_with_laps_left_is_pointed_at()
    {
        AGapComingDown();

        var said = await _rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.Equal(SegmentKind.Horizon, said!.Kind);
    }

    [Fact]
    public async Task The_horizon_is_the_facts_and_the_cooldown_once_it_is_said()
    {
        AGapComingDown();

        Assert.NotNull(_rig.Director.Horizons.Pick());

        await _rig.Director.NextAsync();

        // Said, so the same pair is left alone for the cooldown - one gap
        // coming down over six laps is one line, not six.
        Assert.Null(_rig.Director.Horizons.Pick());
        Assert.Equal(1, _rig.Director.Budget.SpentTonight);
    }

    [Fact]
    public async Task With_no_road_left_nothing_is_said_about_the_future()
    {
        for (var lap = 0; lap < 3; lap++)
        {
            // A race with neither a lap limit nor a clock: no road, so no
            // horizon, however fast the gap is coming down.
            _rig.Director.Standings(new[] { new PairGap("A", "B", 1, 1.0 - (lap * 0.2), 0) }, 10 + lap, null);
            _rig.Clock.Advance(TimeSpan.FromSeconds(6));
        }

        Assert.Null(_rig.Director.Horizons.Pick());

        var said = await _rig.Director.NextAsync();

        Assert.True(said is null || said.Kind != SegmentKind.Horizon);
    }

    [Fact]
    public void The_horizon_is_live_one_voice_and_short_lived()
    {
        var spec = CommentaryBudget.Spec(SegmentKind.Horizon);

        Assert.True(CommentaryDirector.InRace(SegmentKind.Horizon));
        Assert.Equal(SegmentTier.Core, spec.Tier);

        // Never a two-hander: a second voice reacting to "three laps to
        // find half a second" reaches for "and he'll do it".
        Assert.False(spec.CanBeExchange);
        Assert.True(spec.NeedsBy <= TimeSpan.FromSeconds(20), "a rate read on lap six is wrong by lap nine");
    }

    [Fact]
    public void A_new_race_clears_the_horizon_with_the_other_detectors()
    {
        AGapComingDown();

        _rig.Director.RaceStarted("heat-2", "Box Stock");

        Assert.Equal(0, _rig.Director.Horizons.Watching);
    }

    [Fact]
    public void The_horizon_s_facts_are_a_sum_and_never_a_forecast()
    {
        var horizon = new Horizon("A", "B", 1, 0.6, 0.2, 4, 0.8, HorizonKind.WithinReach, 3);

        var fact = CommentaryFacts.Horizon(horizon, id => id == "A" ? "Sam" : "Bill", "heat-1");

        Assert.Contains("Bill is 0.60s behind Sam, fighting for the lead", fact.Text);
        Assert.Contains("if the rate holds", fact.Text);

        foreach (var forecast in new[] { "will win", "will catch", "going to", "is set to" })
        {
            Assert.DoesNotContain(forecast, fact.Text, StringComparison.OrdinalIgnoreCase);
        }

        // Themed on the pair, which is what makes it a promise the night
        // can come back to.
        Assert.Equal(FactTheme.Rivalry("A", "B"), fact.Theme);
    }

    [Fact]
    public void An_estimated_road_is_given_to_the_writer_as_one()
    {
        var horizon = new Horizon("A", "B", 2, 0.6, 0.2, 4, 0.8, HorizonKind.WithinReach, 3, Estimated: true);

        var fact = CommentaryFacts.Horizon(horizon, id => id == "A" ? "Sam" : "Bill", "heat-1");

        Assert.Contains("About 4 laps left, going by the clock.", fact.Text);
        Assert.Contains("fighting for 2nd", fact.Text);
    }

    [Fact]
    public async Task A_record_lap_is_said_before_a_horizon_that_is_ready_at_the_same_time()
    {
        // The horizon has the shortest life of the live calls, and sorted
        // by deadline alone it took the slot every time - a record lap
        // behind it could run out of time and be thrown away.
        for (var i = 0; i < 4; i++)
        {
            _rig.Director.Lap($"warm-{i}", 20.0 + i);
        }

        _rig.Director.Lap("A", 12.0);
        AGapComingDown();

        var said = await _rig.Director.NextAsync();

        Assert.NotNull(said);
        Assert.Equal(SegmentKind.PaceNote, said!.Kind);
    }
}
