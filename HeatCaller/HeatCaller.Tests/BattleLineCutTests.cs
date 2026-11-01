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
        Assert.Equal("[excited] And Sam takes the lead!", AnnouncementService.BattlePassWords("Sam", 1));
        Assert.Equal("[excited] And Sam is through, up into third!", AnnouncementService.BattlePassWords("Sam", 3));
    }

    [Fact]
    public void The_lead_pass_call_uses_the_leader_change_calls_own_words()
    {
        var shipped = VoiceProfile.CreateDefault().Events[nameof(AnnouncementEventType.LeaderChange)].EffectiveTemplates;

        Assert.Contains(shipped, line => AnnouncementService.BattlePassForTheLead.EndsWith(
            line.Replace("[excited] ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal));
    }

    // --- In the announcer ---------------------------------------------------

    /// <summary>An announcer with every call and sound off, Sam (101), Bill (102) and Frank (103) on file, and what it puts on the screens.</summary>
    /// <param name="leaderChange">The Leader change call's one line, switched on - or null for it off.</param>
    private static (AnnouncementService Announcer, ConcurrentQueue<string> Shown, ConcurrentQueue<string> Log) Announcer(string? leaderChange = null)
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
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !shown.Any(text => text.Contains("now leads", StringComparison.Ordinal)))
        {
            Thread.Sleep(50);
        }

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
}
