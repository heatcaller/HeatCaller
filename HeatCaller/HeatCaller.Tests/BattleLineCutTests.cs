using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// A battle line overtaken by the battle (1 Nov 2026). Asher: "during a AI
/// battle call out, sometimes the person behind gets ahead of the other
/// person but the ai script finishes betraying what's on the board." The
/// line now carries its two cars, and when the one it called behind gets
/// ahead it is cut off and the pass is called - for those two cars only,
/// and once. See AnnouncementService.BattleLines.
/// </summary>
public sealed class BattleLineCutTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly BattlePair SamChasingBill = new(AheadId: "102", BehindId: "101", Position: 1);

    private static RaceStanding Car(string transponder, int laps, long elapsedMs) => new()
    {
        Transponder = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        Status = "active",
        Id = transponder,
    };

    private static Func<string, RaceStanding?> Board(params RaceStanding[] cars) =>
        transponder => cars.FirstOrDefault(car => car.Transponder == transponder);

    // --- Who has passed whom ---------------------------------------------

    [Fact]
    public void The_chaser_has_passed_when_the_board_puts_them_in_front()
    {
        // Bill crossed first on lap 4, so Sam is still behind.
        Assert.False(AnnouncementService.HasPassed(SamChasingBill, Board(Car("102", 4, 40_000), Car("101", 4, 40_400))));

        // Sam crossed onto lap 5 first.
        Assert.True(AnnouncementService.HasPassed(SamChasingBill, Board(Car("102", 4, 40_000), Car("101", 5, 50_100))));

        // Same lap, Sam's crossing earlier - the leaderboard's own rule.
        Assert.True(AnnouncementService.HasPassed(SamChasingBill, Board(Car("102", 5, 50_300), Car("101", 5, 50_100))));
    }

    [Fact]
    public void A_car_the_board_does_not_know_never_counts_as_a_pass()
    {
        Assert.False(AnnouncementService.HasPassed(SamChasingBill, Board(Car("101", 9, 90_000))));
    }

    // --- Which lines the board overrules -----------------------------------

    [Fact]
    public void The_line_on_air_is_cut_when_its_two_cars_swap()
    {
        var onAir = new BattleLine(SamChasingBill);

        var verdict = AnnouncementService.JudgeBattleLines(onAir, [], Board(Car("102", 4, 40_000), Car("101", 5, 50_100)));

        Assert.Same(onAir, verdict.Cut);
        Assert.True(onAir.Overtaken);
    }

    [Fact]
    public void A_third_car_going_past_both_leaves_the_line_alone()
    {
        var onAir = new BattleLine(SamChasingBill);

        // Frank (103) is through into the lead, past both - but Bill is still ahead of Sam.
        var verdict = AnnouncementService.JudgeBattleLines(onAir, [],
            Board(Car("103", 5, 49_000), Car("102", 4, 40_000), Car("101", 4, 40_400)));

        Assert.Null(verdict.Cut);
        Assert.Empty(verdict.Stale);
        Assert.False(onAir.Overtaken);
    }

    [Fact]
    public void Another_pair_swapping_leaves_the_line_alone()
    {
        var onAir = new BattleLine(SamChasingBill);

        // Frank passes Jo for third. Sam and Bill are as they were.
        var verdict = AnnouncementService.JudgeBattleLines(onAir, [],
            Board(Car("102", 4, 40_000), Car("101", 4, 40_400), Car("104", 4, 41_000), Car("103", 4, 40_900)));

        Assert.Null(verdict.Cut);
        Assert.False(onAir.Overtaken);
    }

    [Fact]
    public void The_same_pass_is_only_called_once()
    {
        var onAir = new BattleLine(SamChasingBill);
        var passed = Board(Car("102", 4, 40_000), Car("101", 5, 50_100));

        Assert.NotNull(AnnouncementService.JudgeBattleLines(onAir, [], passed).Cut);

        // The next crossing - Bill onto lap 5 behind him - is the same pass.
        Assert.Null(AnnouncementService.JudgeBattleLines(onAir, [], Board(Car("102", 5, 50_600), Car("101", 5, 50_100))).Cut);
    }

    [Fact]
    public void A_waiting_line_about_the_swapped_pair_is_stale_and_one_about_others_is_kept()
    {
        var aboutSam = new BattleLine(SamChasingBill);
        var aboutFrank = new BattleLine(new BattlePair(AheadId: "104", BehindId: "103", Position: 3));

        var verdict = AnnouncementService.JudgeBattleLines(null, [aboutSam, aboutFrank],
            Board(Car("102", 4, 40_000), Car("101", 5, 50_100), Car("104", 4, 41_000), Car("103", 4, 41_500)));

        Assert.Null(verdict.Cut);
        Assert.Equal(new[] { aboutSam }, verdict.Stale);
        Assert.False(aboutFrank.Overtaken);
    }

    // --- The words ---------------------------------------------------------

    [Fact]
    public void The_pass_call_names_the_driver_and_the_place()
    {
        Assert.Equal("[shouting] And Sam takes the lead!", AnnouncementService.BattlePassWords("Sam", 1));
        Assert.Equal("[excited] And Sam is through, up into third!", AnnouncementService.BattlePassWords("Sam", 3));
    }

    [Fact]
    public void The_lead_pass_call_uses_the_leader_change_calls_own_words()
    {
        var shipped = VoiceProfile.CreateDefault().Events[nameof(AnnouncementEventType.LeaderChange)].EffectiveTemplates;

        Assert.Contains(shipped, line => AnnouncementService.BattlePassForTheLead.EndsWith(
            line.Replace("[shouting] ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal));
    }

    // --- In the announcer ---------------------------------------------------

    /// <summary>An announcer with every call and sound off, Sam (101), Bill (102) and Frank (103) on file, and what it puts on the screens.</summary>
    /// <param name="leaderChange">The Leader change call's one line, switched on - or null for it off.</param>
    /// <param name="closeBattle">The Close battle call's one line, switched on - or null for it off.</param>
    private static (AnnouncementService Announcer, ConcurrentQueue<string> Shown, ConcurrentQueue<string> Log) Announcer(string? leaderChange = null, string? closeBattle = null)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Bill" });
        profiles.Upsert(new DriverProfile { Transponder = "103", DriverName = "Frank" });

        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        if (leaderChange is not null)
        {
            voice.Current.Events[nameof(AnnouncementEventType.LeaderChange)] = new AnnouncementEventConfig { Enabled = true, Weight = 100, Templates = { leaderChange } };
        }

        if (closeBattle is not null)
        {
            voice.Current.Events[nameof(AnnouncementEventType.CloseBattle)] = new AnnouncementEventConfig { Enabled = true, Weight = 100, Templates = { closeBattle } };
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
    public void A_battle_line_is_cut_off_and_the_pass_called_when_its_two_cars_swap()
    {
        var (announcer, shown, log) = Announcer();

        // Bill leads Sam on lap 4, and the battle line about them is being said.
        announcer.OnStandingUpdated(Car("102", 4, 40_000));
        announcer.OnStandingUpdated(Car("101", 4, 40_400));
        announcer.PutBattleLineOnAir(SamChasingBill);

        // Sam crosses onto lap 5 first - he is through.
        announcer.OnStandingUpdated(Car("101", 5, 50_100));

        // Bill's crossing a moment later is the same pass.
        announcer.OnStandingUpdated(Car("102", 5, 50_600));

        // Nothing actually spoken in a test.
        announcer.Muted = true;

        Assert.Single(log, line => line.StartsWith("[voice] Battle line ", StringComparison.Ordinal)
            && line.Contains("Sam got past Bill", StringComparison.Ordinal));
        Assert.Equal(new[] { "And Sam takes the lead!" }, shown.ToList());
    }

    [Fact]
    public void A_battle_line_is_left_alone_when_a_third_car_passes_both()
    {
        var (announcer, shown, log) = Announcer();

        announcer.OnStandingUpdated(Car("102", 4, 40_000));
        announcer.OnStandingUpdated(Car("101", 4, 40_400));
        announcer.OnStandingUpdated(Car("103", 4, 41_000));
        announcer.PutBattleLineOnAir(SamChasingBill with { Position = 1 });

        // Frank laps quicker than both and takes the lead; Bill is still ahead of Sam.
        announcer.OnStandingUpdated(Car("103", 5, 49_000));

        announcer.Muted = true;

        Assert.DoesNotContain(log, line => line.StartsWith("[voice] Battle line ", StringComparison.Ordinal));
        Assert.Empty(shown);
    }

    [Fact]
    public void A_battle_line_already_cut_by_a_flag_or_reset_is_not_cut_again()
    {
        var (announcer, shown, log) = Announcer();

        announcer.OnStandingUpdated(Car("102", 4, 40_000));
        announcer.OnStandingUpdated(Car("101", 4, 40_400));
        announcer.PutBattleLineOnAir(SamChasingBill);

        // Everything cut - whatever is in the speaker now is not the battle line.
        announcer.SilenceNow("test");

        announcer.OnStandingUpdated(Car("101", 5, 50_100));
        announcer.Muted = true;

        Assert.DoesNotContain(log, line => line.StartsWith("[voice] Battle line ", StringComparison.Ordinal));
        Assert.DoesNotContain(shown, text => text.Contains("takes the lead", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_pass_for_the_lead_is_called_once_not_as_well_as_the_lead_change(bool battleLineOnAir)
    {
        var (announcer, shown, _) = Announcer(leaderChange: "{driver} now leads");

        announcer.OnStandingUpdated(Car("102", 4, 40_000));
        announcer.OnStandingUpdated(Car("101", 4, 40_400));

        if (battleLineOnAir)
        {
            announcer.PutBattleLineOnAir(SamChasingBill);
        }

        announcer.OnStandingUpdated(Car("101", 5, 50_100));

        // The lead change call waits in the per-lap batch before it decides.
        UntilSettled(announcer);

        announcer.Muted = true;

        if (battleLineOnAir)
        {
            Assert.Equal(new[] { "And Sam takes the lead!" }, shown.ToList());
        }
        else
        {
            // Without a battle line the ordinary call is said - so the
            // half above proves it was held back, not merely switched off.
            Assert.Equal(new[] { "Sam now leads" }, shown.ToList());
        }
    }

    // --- Wider: every call a position change can betray (3 Nov 2026) --------

    /// <summary>
    /// Waits until every lap call offered so far has been said or dropped
    /// (open items, 19 Nov 2026). It waited up to 3 s for the words it hoped
    /// for, and on a loaded machine the per-lap batch and the field window
    /// could take longer - the lead change test failed in the full suite.
    /// The 60 s only bounds a broken run; a working one settles in well
    /// under a second.
    /// </summary>
    private static void UntilSettled(AnnouncementService announcer)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!announcer.LapCallsSettledForTests && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }

        Assert.True(announcer.LapCallsSettledForTests, "the lap calls never settled");
    }

    [Fact]
    public void A_line_about_a_pass_is_judged_by_the_same_rule()
    {
        // Sam took the lead from Bill; Bill crosses onto the next lap first.
        var onAir = new BattleLine(BattlePair.Passed(aheadId: "101", behindId: "102", position: 1));

        var verdict = AnnouncementService.JudgeBattleLines(onAir, [], Board(Car("101", 5, 50_100), Car("102", 6, 60_000)));

        Assert.Same(onAir, verdict.Cut);
        Assert.False(onAir.Pair.CallsThePass);
        Assert.True(new BattlePair("102", "101", 1).CallsThePass);
    }

    [Fact]
    public void A_line_about_a_pass_that_is_undone_is_cut_off_with_no_pass_call()
    {
        var (announcer, shown, log) = Announcer();

        // Sam has just taken the lead from Bill, and the line saying so is on.
        announcer.OnStandingUpdated(Car("101", 5, 50_100));
        announcer.OnStandingUpdated(Car("102", 4, 40_000));
        announcer.PutBattleLineOnAir(BattlePair.Passed("101", "102", 1));

        // Bill is straight back past.
        announcer.OnStandingUpdated(Car("102", 6, 60_000));
        announcer.Muted = true;

        Assert.Single(log, line => line.StartsWith("[voice] Call ", StringComparison.Ordinal)
            && line.Contains("Bill got back ahead of Sam", StringComparison.Ordinal));
        Assert.Empty(shown);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_lead_change_undone_before_it_is_said_is_dropped_and_the_new_one_called(bool billIsStraightBack)
    {
        var (announcer, shown, log) = Announcer(leaderChange: "{driver} now leads");

        announcer.OnStandingUpdated(Car("102", 4, 40_000));
        announcer.OnStandingUpdated(Car("101", 4, 40_400));

        // Sam takes the lead - his call waits in the per-lap batch.
        announcer.OnStandingUpdated(Car("101", 5, 50_100));

        if (billIsStraightBack)
        {
            announcer.OnStandingUpdated(Car("102", 6, 60_000));
            UntilSettled(announcer);
        }
        else
        {
            UntilSettled(announcer);
        }

        announcer.Muted = true;

        if (billIsStraightBack)
        {
            // With the lead change count after it - Sam's lead still counts.
            Assert.Single(shown, text => text.StartsWith("Bill now leads", StringComparison.Ordinal));
            Assert.DoesNotContain(shown, text => text.Contains("Sam now leads", StringComparison.Ordinal));
            Assert.Contains(log, line => line.StartsWith("[voice] Dropped LeaderChange - the two cars it is about swapped", StringComparison.Ordinal));
        }
        else
        {
            // The control: the same lead change, left standing, is said.
            Assert.Equal(new[] { "Sam now leads" }, shown.ToList());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_close_battle_line_is_dropped_when_the_two_cars_swap_before_it_is_said(bool samGetsThrough)
    {
        var (announcer, shown, log) = Announcer(closeBattle: "{rival} is all over {driver} {battle}");

        // Bill and Sam four tenths apart on lap 3 - a close battle, offered
        // to Bill's per-lap batch.
        announcer.OnStandingUpdated(Car("102", 3, 30_000));
        announcer.OnStandingUpdated(Car("101", 3, 30_400));

        if (samGetsThrough)
        {
            announcer.OnStandingUpdated(Car("101", 4, 39_000));
        }

        // Waited out in full when it is dropped - long past the batch window.
        UntilSettled(announcer);
        announcer.Muted = true;

        if (samGetsThrough)
        {
            Assert.DoesNotContain(shown, text => text.Contains("all over", StringComparison.Ordinal));
            Assert.Contains(log, line => line.StartsWith("[voice] Dropped CloseBattle - the two cars it is about swapped", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains(shown, text => text.Contains("Sam is all over Bill", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Every_call_a_pass_can_betray_is_tagged_with_its_two_cars()
    {
        var laps = Source("AnnouncementService.LapCalls.cs");
        var director = Source("CommentaryDirector.cs");

        Assert.Contains("about: new BattlePair(front.Transponder, chaser.Transponder, i + 1)", laps);
        Assert.Contains("BattlePair.Passed(leader.Transponder, _currentLeaderTransponder, 1)", laps);
        Assert.Contains("BattlePair.Passed(currentlyAhead, overtaken,", laps);
        Assert.Contains("kind == SegmentKind.Horizon && horizonCalling is { } gap", director);

        // A call kept for the end of the last line keeps its tag when it comes back.
        Assert.Contains("using (AboutBattle(battle?.Pair))", Source("AnnouncementService.VoiceQueue.cs"));
    }

    private static string Source(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "check.cmd")))
        {
            folder = folder.Parent;
        }

        return File.ReadAllText(Path.Combine(
            folder?.FullName ?? throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory),
            "HeatCaller", "HeatCaller", fileName));
    }
}
