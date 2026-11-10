using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// Lines no longer true by the time they would be heard (8 Nov 2026). Asher,
/// after a demo heat on a 5.5-second track: "I thought I heard some times
/// called inaccurately ... it's very important to people trusting the
/// timing." Every lap was timed right. What was said was not: a fastest-lap
/// call heard three laps late, an AI line about a 5.42 heard a second after a
/// 4.936, and the 4.936's own call still queued behind AI lines at the flag.
/// See AnnouncementService.Freshness.
/// </summary>
public sealed class StaleCallsAtTheSpeakerTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static RaceFactsStamp Facts(long race = 1, long fastest = 3, long leads = 1, int leaderLaps = 5, long atMs = 100_000, long? sayBy = null) =>
        new(race, fastest, leads, leaderLaps, atMs, sayBy);

    // --- The rule ---------------------------------------------------------

    [Fact]
    public void A_line_is_still_true_while_nothing_it_was_written_against_has_moved()
    {
        var then = Facts(sayBy: 130_000);

        Assert.Null(AnnouncementService.WhyNoLongerTrue(then, then with { AtMs = 110_000 }));
    }

    [Fact]
    public void A_quicker_lap_makes_a_line_untrue()
    {
        // "RIPS it with a five forty-two" - and Terry had just done a 4.936.
        var why = AnnouncementService.WhyNoLongerTrue(Facts(), Facts(fastest: 4));

        Assert.Equal("a quicker lap has been set since it was written", why);
    }

    [Fact]
    public void A_new_leader_makes_a_line_untrue()
    {
        Assert.Equal(
            "the lead has changed hands since it was written",
            AnnouncementService.WhyNoLongerTrue(Facts(), Facts(leads: 2)));
    }

    [Fact]
    public void Another_lap_by_the_leader_makes_a_line_untrue()
    {
        // "Out front by just over a second ... NINE laps left", heard a lap
        // later with the gap at two seconds and eight to go.
        Assert.Equal(
            "the leader has started another lap since it was written",
            AnnouncementService.WhyNoLongerTrue(Facts(leaderLaps: 3), Facts(leaderLaps: 4)));

        Assert.Equal(
            "the leader has done 3 more laps since it was written",
            AnnouncementService.WhyNoLongerTrue(Facts(leaderLaps: 3), Facts(leaderLaps: 6)));
    }

    [Fact]
    public void A_new_race_makes_a_line_untrue()
    {
        Assert.Equal("the race it was about is over", AnnouncementService.WhyNoLongerTrue(Facts(), Facts(race: 2)));

        // A reset that put the lap count back, with the race counter missed.
        Assert.Equal("the race it was about is over", AnnouncementService.WhyNoLongerTrue(Facts(leaderLaps: 7), Facts(leaderLaps: 1)));
    }

    [Fact]
    public void A_line_past_its_own_deadline_is_untrue()
    {
        var why = AnnouncementService.WhyNoLongerTrue(Facts(sayBy: 120_000), Facts(atMs: 122_500));

        Assert.Equal("its moment passed 2.5s ago", why);
    }

    [Fact]
    public void A_quicker_lap_is_named_before_a_lap_by_the_leader()
    {
        // The reason that matters most to the room is the one in the log.
        Assert.Equal(
            "a quicker lap has been set since it was written",
            AnnouncementService.WhyNoLongerTrue(Facts(), Facts(fastest: 4, leaderLaps: 6)));
    }

    // --- How long each kind of AI line lasts (8 Nov 2026, second pass) -------

    [Fact]
    public void A_record_lap_line_is_not_made_untrue_by_laps_or_a_new_leader()
    {
        var then = Facts(leaderLaps: 3) with { LeaderLapsAllowed = null, LeadMatters = false };

        Assert.Null(AnnouncementService.WhyNoLongerTrue(then, Facts(leaderLaps: 9, leads: 4)));

        // A quicker lap still does.
        Assert.NotNull(AnnouncementService.WhyNoLongerTrue(then, Facts(leaderLaps: 4, fastest: 4)));
    }

    [Fact]
    public void A_fight_further_back_outlives_one_lap_by_the_leader_but_not_two()
    {
        var then = Facts(leaderLaps: 3) with { LeaderLapsAllowed = 2, LeadMatters = false };

        Assert.Null(AnnouncementService.WhyNoLongerTrue(then, Facts(leaderLaps: 4, leads: 2)));
        Assert.Equal("the leader has done 2 more laps since it was written",
            AnnouncementService.WhyNoLongerTrue(then, Facts(leaderLaps: 5)));
    }

    [Theory]
    [InlineData(SegmentKind.PaceNote, 0, null, false)]
    [InlineData(SegmentKind.RecordWatch, 0, null, false)]
    [InlineData(SegmentKind.Horizon, 0, 1, true)]
    [InlineData(SegmentKind.BattleWatch, 1, 1, true)]
    [InlineData(SegmentKind.BattleWatch, 3, 2, false)]
    public void Each_kind_of_in_race_line_gets_its_own_life(SegmentKind kind, int battleFor, int? lapsAllowed, bool leadMatters)
    {
        var battle = battleFor > 0 ? new Battle("A", "B", battleFor, 6, TimeSpan.FromSeconds(30), 0.2, 0.3, 0) : null;

        Assert.Equal(lapsAllowed, CommentaryDirector.LeaderLapsAllowedFor(kind, battle));
        Assert.Equal(leadMatters, CommentaryDirector.LeadMattersFor(kind, battle));
    }

    // --- Screens show the real name ------------------------------------------

    [Fact]
    public void The_screens_get_the_real_name_back_from_the_voices_spelling()
    {
        var profiles = new[]
        {
            new DriverProfile { Transponder = "1", DriverName = "Ella Vator", AnnouncerName = "Ela vayter" },
            new DriverProfile { Transponder = "2", DriverName = "Al Gorithim", AnnouncerName = "Yo yo yo" },
            new DriverProfile { Transponder = "3", DriverName = "Terry Byte" },
        };

        Assert.Equal("Ella Vator finds another gear - 7.253s.",
            AnnouncementService.WithScreenNames("Ela vayter finds another gear - 7.253s.", profiles));
        Assert.Equal("Al Gorithim beats their own time, Terry Byte leads.",
            AnnouncementService.WithScreenNames("Yo yo yo beats their own time, Terry Byte leads.", profiles));
    }

    [Fact]
    public void Only_whole_words_are_given_back()
    {
        var profiles = new[] { new DriverProfile { Transponder = "1", DriverName = "Maxine Power", AnnouncerName = "Max" } };

        Assert.Equal("Maximum attack from Maxine Power!",
            AnnouncementService.WithScreenNames("Maximum attack from Max!", profiles));
    }

    [Fact]
    public void A_hand_correction_is_not_a_crossing()
    {
        Assert.True(AnnouncementService.IsHandAdjustment(new RaceStanding { Transponder = "1", Status = "adjusted" }));
        Assert.False(AnnouncementService.IsHandAdjustment(new RaceStanding { Transponder = "1", Status = "active" }));
    }

    [Fact]
    public void The_feed_marks_a_line_that_was_dropped_unheard()
    {
        Assert.Equal("13:11:37   Best lap - Terry Byte   (not said - out of date)",
            AnnouncerControl.FeedLineNotSaid("13:11:37   Best lap - Terry Byte"));
    }

    [Fact]
    public void Dropped_lines_are_taken_back_off_the_screens_and_the_order_is_checked_after_rendering()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        Assert.Contains("WithdrawAnnouncementText(shown);", queue, StringComparison.Ordinal);
        Assert.Contains("WithdrawAnnouncementText(_dequeuedFreshness.Value.Shown);", queue, StringComparison.Ordinal);
        Assert.Contains("_announcer.AnnouncementTextWithdrawn += OnAnnouncementTextWithdrawn;", Source("BigScreenForm.cs"), StringComparison.Ordinal);
        Assert.Contains("announcer.AnnouncementTextWithdrawn += OnAnnouncementTextWithdrawn;", Source("AnnouncerControl.cs"), StringComparison.Ordinal);
        Assert.Contains("\"the order moved while it was being rendered\"", Source("AnnouncementService.LapCalls.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_battle_pass_call_has_the_few_seconds_every_lap_call_has()
    {
        Assert.Contains("SayByMs: LapSeenAt(behindId) + LapCallSayWithinMs", Source("AnnouncementService.BattleLines.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_scheduled_line_no_longer_bins_the_lap_calls()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        Assert.Contains("&& !(type == AnnouncementEventType.Reminder && IsLapBoundCall(item.Type))", queue, StringComparison.Ordinal);
        Assert.Contains("waiting.Facts is not null || waiting.Type == AnnouncementEventType.Reminder", queue, StringComparison.Ordinal);
    }

    // --- Which calls are about the lap just done ----------------------------

    [Theory]
    [InlineData(AnnouncementEventType.FastLap)]
    [InlineData(AnnouncementEventType.PersonalBestLap)]
    [InlineData(AnnouncementEventType.LapTime)]
    [InlineData(AnnouncementEventType.LapCompleted)]
    [InlineData(AnnouncementEventType.LeaderChange)]
    [InlineData(AnnouncementEventType.CloseBattle)]
    [InlineData(AnnouncementEventType.Streak)]
    [InlineData(AnnouncementEventType.Lapped)]
    [InlineData(AnnouncementEventType.LastLap)]
    [InlineData(AnnouncementEventType.SplitTime)]
    public void A_call_about_the_lap_just_done_has_a_few_seconds_to_be_said(AnnouncementEventType type)
    {
        Assert.True(AnnouncementService.IsLapBoundCall(type));
    }

    [Theory]
    [InlineData(AnnouncementEventType.Reminder)]
    [InlineData(AnnouncementEventType.PositionReport)]
    [InlineData(AnnouncementEventType.TrackRecord)]
    [InlineData(AnnouncementEventType.AchievementUnlocked)]
    [InlineData(AnnouncementEventType.DriverRelease)]
    [InlineData(AnnouncementEventType.JumpStart)]
    [InlineData(AnnouncementEventType.FinalMinute)]
    [InlineData(AnnouncementEventType.RaceCompleted)]
    [InlineData(AnnouncementEventType.YellowFlag)]
    public void Instructions_flags_facts_and_the_running_order_are_not_held_to_it(AnnouncementEventType type)
    {
        Assert.False(AnnouncementService.IsLapBoundCall(type));
    }

    [Fact]
    public void The_window_is_a_few_seconds()
    {
        Assert.InRange(AnnouncementService.LapCallSayWithinMs, 3_000, 8_000);
    }

    // --- Where a lap call goes in the queue ---------------------------------

    [Fact]
    public void A_lap_call_goes_in_front_of_the_first_waiting_ai_line()
    {
        // [lap call, AI, lap call, AI] - in front of the first AI line, behind what was ahead of it.
        Assert.Equal(1, AnnouncementService.LapCallSlot(new[] { false, true, false, true }));
    }

    [Fact]
    public void A_lap_call_goes_to_the_back_when_no_ai_line_is_waiting()
    {
        Assert.Equal(3, AnnouncementService.LapCallSlot(new[] { false, false, false }));
        Assert.Equal(0, AnnouncementService.LapCallSlot(Array.Empty<bool>()));
    }

    // --- The announcer keeps the race as an AI line would read it -------------

    private static RaceStanding Car(string transponder, int laps, long elapsedMs) => new()
    {
        Transponder = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        Status = "active",
        Id = transponder,
    };

    private static (AnnouncementService Announcer, ConcurrentQueue<string> Shown, ConcurrentQueue<string> Log) Announcer(params AnnouncementEventType[] on)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Terry" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Dee" });

        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        foreach (var type in on)
        {
            voice.Current.Events[type.ToString()].Enabled = true;
            voice.Current.Events[type.ToString()].EnabledInPractice = true;
        }

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        var announcer = new AnnouncementService(profiles, voice, sounds);
        var shown = new ConcurrentQueue<string>();
        var log = new ConcurrentQueue<string>();
        announcer.AnnouncementTextReady += shown.Enqueue;
        announcer.StatusMessage += log.Enqueue;

        return (announcer, shown, log);
    }

    [Fact]
    public void The_announcer_counts_the_leaders_laps_and_the_lead_changes()
    {
        var (announcer, _, _) = Announcer();
        announcer.Muted = true;

        announcer.OnStandingUpdated(Car("101", 3, 16_000));
        announcer.OnStandingUpdated(Car("102", 3, 17_000));

        var before = announcer.RaceFactsNow();
        Assert.Equal(3, before.LeaderLaps);

        // Dee crossing behind Terry changes nothing an AI line would read.
        announcer.OnStandingUpdated(Car("102", 3, 17_000));
        Assert.Null(AnnouncementService.WhyNoLongerTrue(before, announcer.RaceFactsNow()));

        // Terry starts lap 5.
        announcer.OnStandingUpdated(Car("101", 4, 21_500));
        Assert.Equal("the leader has started another lap since it was written",
            AnnouncementService.WhyNoLongerTrue(before, announcer.RaceFactsNow()));

        // Dee two laps on in one crossing - through into the lead.
        var beforePass = announcer.RaceFactsNow();
        announcer.OnStandingUpdated(Car("102", 5, 25_000));
        Assert.Equal("the lead has changed hands since it was written",
            AnnouncementService.WhyNoLongerTrue(beforePass, announcer.RaceFactsNow()));
    }

    [Fact]
    public void A_split_call_is_timed_from_the_split_not_from_the_line_crossed_seven_seconds_before()
    {
        // 9 Nov 2026, Bug hunt 15, B7: a loop halfway round a 14-second lap
        // is crossed 7s after the line - past a deadline counted from the
        // line, so every split call was dropped before it was queued.
        var (announcer, shown, log) = Announcer(AnnouncementEventType.SplitTime);

        // Terry crossed the line 7s ago.
        announcer.LapRaisedAtMs = TimingClock.NowMs - 7_000;
        announcer.OnStandingUpdated(Car("101", 3, 16_000));
        announcer.LapRaisedAtMs = null;

        // And the split loop now.
        announcer.NotifySplitTime(Car("101", 3, 16_000), "the split", 7_000);
        announcer.Muted = true;

        Assert.DoesNotContain(log, line => line.Contains("Dropped SplitTime", StringComparison.Ordinal));
        Assert.Contains(shown, line => line.Contains("Terry", StringComparison.Ordinal) || line.Contains("7.00", StringComparison.Ordinal));
    }

    [Fact]
    public void A_new_race_moves_the_facts_on()
    {
        var (announcer, _, _) = Announcer();
        announcer.Muted = true;

        announcer.OnStandingUpdated(Car("101", 9, 50_000));
        var lastRace = announcer.RaceFactsNow();

        announcer.ResetSession();

        Assert.Equal("the race it was about is over",
            AnnouncementService.WhyNoLongerTrue(lastRace, announcer.RaceFactsNow()));
    }

    [Fact]
    public void An_ai_line_the_race_has_already_overtaken_is_not_queued_or_shown()
    {
        var (announcer, shown, log) = Announcer();

        announcer.OnStandingUpdated(Car("101", 3, 16_000));
        var written = announcer.RaceFactsNow();
        announcer.OnStandingUpdated(Car("101", 4, 21_500));

        announcer.SpeakReminder("Terry's out front by just over a second!", showOnScreens: true, facts: written);
        announcer.Muted = true;

        Assert.Contains(log, line => line.StartsWith("[voice] Dropped an AI line before it was queued - the leader has started another lap", StringComparison.Ordinal));
        Assert.Empty(shown);
    }

    // --- The director: the race moving while a line is written ---------------

    private sealed class Brain : ICommentaryBrain
    {
        public Action? During { get; set; }

        public int Asked { get; private set; }

        public event Action<string>? StatusMessage;

        public string Describe => "the test";

        public Task<string?> GenerateAsync(string systemPrompt, string userPrompt, int maxTokens, double temperature = 0.7, CancellationToken ct = default, TimeSpan? timeout = null)
        {
            Asked++;
            var during = During;
            During = null;
            during?.Invoke();
            StatusMessage?.Invoke(string.Empty);
            return Task.FromResult<string?>("They are nose to tail out there!");
        }

        public Task WarmUpAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<string?> TestAsync(CancellationToken ct = default) => Task.FromResult<string?>("ok");
    }

    private (CommentaryDirector Director, Brain Model, List<string> Console) DirectorInAFight()
    {
        var now = new DateTime(2026, 11, 8, 13, 11, 0, DateTimeKind.Unspecified);
        var model = new Brain();
        var settings = new AppSettings { CommentaryEnabled = true, CommentaryMaxWords = 40 };
        var commentary = new CommentaryService(ProfileStore.Load(), settings) { Client = model };
        var director = new CommentaryDirector(commentary, settings, () => now);
        var console = new List<string>();

        director.Status += console.Add;
        director.NameFor = id => id == "A" ? "Terry" : "Dee";
        director.NewMeeting("day-1");
        director.MonthlyUsage(0, 400);
        director.NightShape(10, 30);
        director.RaceStarted("heat-1", "Stock");

        for (var i = 0; i < 6; i++)
        {
            director.Standings(new[] { new PairGap("A", "B", 1, 0.3, 0) });
            now += TimeSpan.FromSeconds(5);
        }

        return (director, model, console);
    }

    [Fact]
    public async Task An_in_race_line_whose_race_moved_on_while_it_was_written_is_dropped()
    {
        var (director, model, console) = DirectorInAFight();
        var race = Facts(leaderLaps: 3, atMs: 50_000);
        director.RaceFactsNow = () => race;

        // Terry starts another lap while the relay writes it.
        model.During = () => race = race with { LeaderLaps = 4, AtMs = 52_000 };

        Assert.Null(await director.NextAsync());

        Assert.Equal(1, model.Asked);
        Assert.Equal(0, director.Budget.InFlight);
        Assert.Equal(0, director.Said.Count);
        Assert.Contains(console, line => line.Contains("the leader has started another lap since it was written", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_in_race_line_goes_out_with_the_race_it_was_written_against()
    {
        var (director, _, _) = DirectorInAFight();
        var race = Facts(leaderLaps: 3, atMs: 50_000);
        director.RaceFactsNow = () => race;

        var said = await director.NextAsync();

        Assert.NotNull(said);
        Assert.NotNull(said!.Facts);
        Assert.Equal(3, said.Facts!.LeaderLaps);

        // With its own deadline, counted from when it was asked for.
        var needsBy = (long)CommentaryBudget.Spec(said.Kind).NeedsBy.TotalMilliseconds;
        Assert.Equal(50_000 + needsBy, said.Facts.SayByMs);
    }

    [Fact]
    public async Task Without_the_race_to_read_a_line_goes_out_unstamped_as_before()
    {
        var (director, _, _) = DirectorInAFight();

        var said = await director.NextAsync();

        Assert.NotNull(said);
        Assert.Null(said!.Facts);
    }

    // --- The source: the checks are where the line is about to be heard ------

    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", file));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Couldn't find the repo root (check.cmd).");
    }

    [Fact]
    public void A_waiting_line_is_judged_as_it_comes_off_the_queue_and_again_once_rendered()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        // Off the queue, before any render.
        Assert.Contains("var stale = work is null ? null : WhyStale(sayBy, facts);", queue, StringComparison.Ordinal);

        // After the render and the speaker's lock.
        Assert.Contains("if (WhyDequeuedLineIsStale() is { } stale)", queue, StringComparison.Ordinal);

        // A lap call gets its deadline when none is given.
        Assert.Contains("sayByMs ??= IsLapBoundCall(type) ? LapSeenAt(transponder) + LapCallSayWithinMs : null;", queue, StringComparison.Ordinal);

        // An AI line no longer bins the lap calls ahead of it.
        Assert.Contains("if (isPriority && facts is null)", queue, StringComparison.Ordinal);
    }

    [Fact]
    public void A_prepared_ai_turn_is_judged_once_it_has_the_speaker()
    {
        Assert.Contains("if (WhyDequeuedLineIsStale() is { } stale)", Source("AnnouncementService.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_hands_the_director_the_race_and_the_announcer_the_lines_facts()
    {
        var host = Source("AnnouncerControl.Director.cs");

        Assert.Contains("_director.RaceFactsNow = _announcer.RaceFactsNow;", host, StringComparison.Ordinal);
        Assert.Contains("facts: said.Facts", host, StringComparison.Ordinal);
    }

    // --- Playing a segment again (8 Nov 2026) -----------------------------

    private static SpokenSegment InRace(RaceFactsStamp? facts) =>
        new(SegmentKind.Horizon, "Dave is a second behind Carl, with nine laps left.", Array.Empty<ExchangeTurn>()) { Facts = facts };

    [Fact]
    public void An_in_race_segment_the_race_has_moved_past_is_not_played_again()
    {
        var why = RaceControlControl.WhyNotToReplay(InRace(Facts()), Facts(leaderLaps: 8));

        Assert.Equal("the leader has done 3 more laps since it was written", why);
    }

    [Fact]
    public void An_in_race_segment_still_true_may_be_played_again()
    {
        var then = Facts(sayBy: 130_000);

        Assert.Null(RaceControlControl.WhyNotToReplay(InRace(then), then with { AtMs = 110_000 }));
    }

    [Fact]
    public void A_segment_from_between_races_is_always_true_to_play_again()
    {
        Assert.Null(RaceControlControl.WhyNotToReplay(InRace(null), Facts(race: 9, leaderLaps: 0)));
    }

    [Fact]
    public void A_replay_carries_its_facts_to_the_speaker_and_says_why_it_will_not()
    {
        var replay = Source("RaceControlControl.Broadcast.cs");

        Assert.Contains("WhyNotToReplay(last, _announcer.RaceFactsNow())", replay, StringComparison.Ordinal);
        Assert.Contains("[show] Not playing {named} again - {untrue}.", replay, StringComparison.Ordinal);
        Assert.Contains("_announcer.SpeakReminder(last.Text, showOnScreens: true, onAir: named, facts: last.Facts);", replay, StringComparison.Ordinal);
        Assert.Contains("facts: last.Facts);", replay, StringComparison.Ordinal);
    }
}
