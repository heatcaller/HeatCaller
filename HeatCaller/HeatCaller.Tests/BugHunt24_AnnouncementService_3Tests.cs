using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the announcer, third group:
/// a newer position report binned the correction for the one just cut off,
/// so the lead change was never called; a black-flagged car was named its
/// class's winner; a winner's finish line whose early render failed was
/// never made again, so the flag went silent; two drivers sharing an
/// announcer name showed the first one's real name for both; a spent
/// HeatCaller allowance passed saved takes over with no Kokoro to speak
/// instead; and the console said Kokoro had taken over at 100% when it had
/// not.
/// </summary>
public sealed class BugHunt24_AnnouncementService_3Tests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static FieldInfo Field(string name) => typeof(AnnouncementService).GetField(name, Private)!;

    private static MethodInfo Method(string name) => typeof(AnnouncementService).GetMethod(name, Private)!;

    // ------------------------------------- a correction and the next report

    [Fact]
    public void A_newer_position_report_does_not_bin_a_waiting_correction()
    {
        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

        var itemType = typeof(AnnouncementService).GetNestedType("QueuedVoiceItem", BindingFlags.NonPublic)!;
        Func<Task> nothing = () => Task.CompletedTask;

        // The correction, queued under the new leader - the car the next
        // report is queued under too.
        var correction = Activator.CreateInstance(itemType,
            AnnouncementEventType.PositionReport, "201", nothing, "Position report", null, null, null, "Hold on - Sam takes the lead!")!;
        itemType.GetProperty("IsCorrection")!.SetValue(correction, true);

        var queue = (IList)Field("_voiceQueue").GetValue(announcer)!;

        // Held throughout, so the voice loop takes nothing while we look.
        lock (Field("_voiceQueueLock").GetValue(announcer)!)
        {
            queue.Clear();
            queue.Add(correction);

            var accepted = (bool)Method("EnqueueVoiceWork").Invoke(announcer, new object?[]
            {
                AnnouncementEventType.PositionReport, "201", nothing, null, "Position report",
                false, null, null, null, TimingClock.NowMs + 60_000, null, null,
            })!;

            Assert.True(accepted);

            var waiting = queue.Cast<object>().ToList();
            queue.Clear();

            Assert.Equal(2, waiting.Count);
            Assert.Same(correction, waiting[0]);
            Assert.False((bool)itemType.GetProperty("IsCorrection")!.GetValue(waiting[1])!);
        }
    }

    // --------------------------------------- a black flag and the class winners

    /// <summary>A shared heat at the flag, Mo (Stock) black-flagged but ahead on laps.</summary>
    private static AnnouncementService SharedHeatWithABlackFlag(bool anotherStockCar)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "201", DriverName = "Mo" });
        profiles.Upsert(new DriverProfile { Transponder = "202", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "203", DriverName = "Al" });

        var voice = VoiceSettingsStore.Load();
        voice.Current.Events[nameof(AnnouncementEventType.RaceCompleted)].Enabled = true;
        voice.Current.Events[nameof(AnnouncementEventType.WinnerCalled)].Enabled = true;
        voice.Current.Events[nameof(AnnouncementEventType.WinnerCalled)].Templates = new List<string> { "Victory for {winner}!" };

        var announcer = new AnnouncementService(profiles, voice, SoundEffectsStore.Load());

        var classes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["201"] = "Stock", ["202"] = "Mod" };

        if (anotherStockCar)
        {
            classes["203"] = "Stock";
        }

        announcer.SharedHeatClasses = () => classes;

        var standings = (Dictionary<string, RaceStanding>)Field("_standings").GetValue(announcer)!;
        standings["201"] = new RaceStanding { Transponder = "201", Name = "Mo", Laps = 11, ElapsedMs = 80_000, BlackFlagged = true };
        standings["202"] = new RaceStanding { Transponder = "202", Name = "Sam", Laps = 10, ElapsedMs = 82_000 };

        if (anotherStockCar)
        {
            standings["203"] = new RaceStanding { Transponder = "203", Name = "Al", Laps = 10, ElapsedMs = 83_000 };
        }

        Field("_currentLeaderTransponder").SetValue(announcer, "201");

        return announcer;
    }

    private static string? ClassWinnersShown(AnnouncementService announcer)
    {
        var item = Method("TheClassWinnersCall").Invoke(announcer, null);

        return item is null ? null : (string?)item.GetType().GetProperty("ShownText")!.GetValue(item);
    }

    [Fact]
    public void A_black_flagged_car_is_not_named_its_classs_winner()
    {
        var shown = ClassWinnersShown(SharedHeatWithABlackFlag(anotherStockCar: true));

        Assert.Equal("Sam wins Mod, Al wins Stock.", shown);
    }

    [Fact]
    public void A_class_whose_only_car_was_black_flagged_has_no_winner_to_name()
    {
        Assert.Null(ClassWinnersShown(SharedHeatWithABlackFlag(anotherStockCar: false)));
    }

    // ------------------------------------ the winner's finish line made again

    [Fact]
    public async Task An_early_render_that_failed_is_made_again()
    {
        var again = 0;

        var result = await AnnouncementService.MadeAgainIfItFailed(
            Task.FromException<int>(new HttpRequestException("429")),
            () => { again++; return Task.FromResult(7); });

        Assert.Equal(7, result);
        Assert.Equal(1, again);
    }

    [Fact]
    public async Task A_cancelled_early_render_is_made_again()
    {
        var result = await AnnouncementService.MadeAgainIfItFailed(
            Task.FromCanceled<int>(new CancellationToken(canceled: true)),
            () => Task.FromResult(8));

        Assert.Equal(8, result);
    }

    [Fact]
    public async Task An_early_render_that_worked_is_used_as_it_is()
    {
        var again = 0;

        var result = await AnnouncementService.MadeAgainIfItFailed(
            Task.FromResult(3),
            () => { again++; return Task.FromResult(9); });

        Assert.Equal(3, result);
        Assert.Equal(0, again);
    }

    [Fact]
    public async Task A_second_failure_is_the_takes_own()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => AnnouncementService.MadeAgainIfItFailed(
            Task.FromException<int>(new HttpRequestException("500")),
            () => Task.FromException<int>(new TimeoutException())));
    }

    [Fact]
    public void The_finish_with_the_winner_makes_a_failed_early_render_again()
    {
        var winner = Source("AnnouncementService.Winner.cs");
        var start = winner.IndexOf("private bool TrySpeakTheFinishWithTheWinner(", StringComparison.Ordinal);
        var end = winner.IndexOf("SpeakFlagTake(AnnouncementEventType.RaceCompleted, text, take, profile, config, how);", start, StringComparison.Ordinal);

        Assert.True(start > 0 && end > start);
        Assert.Contains("MadeAgainIfItFailed(early, () => MakeItNow())", winner[start..end], StringComparison.Ordinal);
    }

    // ---------------------------------------- a nickname two drivers share

    [Fact]
    public void A_nickname_two_drivers_share_is_left_as_said()
    {
        var profiles = new[]
        {
            new DriverProfile { Transponder = "1", DriverName = "Robert Hale", AnnouncerName = "Junior" },
            new DriverProfile { Transponder = "2", DriverName = "Daniel Ortiz", AnnouncerName = "Junior" },
            new DriverProfile { Transponder = "3", DriverName = "Ella Vader", AnnouncerName = "Ela vayter" },
        };

        Assert.Equal("Junior takes the lead from Ella Vader",
            AnnouncementService.WithScreenNames("Junior takes the lead from Ela vayter", profiles));
    }

    [Fact]
    public void A_nickname_on_two_copies_of_one_driver_still_shows_the_real_name()
    {
        var profiles = new[]
        {
            new DriverProfile { Transponder = "1", DriverName = "Robert Hale", AnnouncerName = "Junior" },
            new DriverProfile { Transponder = "2", DriverName = "Robert Hale", AnnouncerName = "Junior" },
        };

        Assert.Equal("Robert Hale takes the lead",
            AnnouncementService.WithScreenNames("Junior takes the lead", profiles));
    }

    // --------------------------------- the HeatCaller allowance and no Kokoro

    [Fact]
    public void A_spent_heatcaller_allowance_keeps_saved_takes_when_kokoro_cannot_speak()
    {
        var heatCaller = new VoiceProfile { Engine = VoiceEngine.HeatCaller, HeatCallerVoice = "clubhouse" };

        Assert.True(AnnouncementService.PassesOverSavedHeatCaller(heatCaller, consentWithheld: false, allowanceSpent: true, kokoroReady: true));
        Assert.False(AnnouncementService.PassesOverSavedHeatCaller(heatCaller, consentWithheld: false, allowanceSpent: true, kokoroReady: false));
        Assert.False(AnnouncementService.PassesOverSavedHeatCaller(heatCaller, consentWithheld: false, allowanceSpent: false, kokoroReady: true));

        // A consent taken back passes them over whatever else is true.
        Assert.True(AnnouncementService.PassesOverSavedHeatCaller(heatCaller, consentWithheld: true, allowanceSpent: false, kokoroReady: false));

        // Not the rule for the other voices.
        Assert.False(AnnouncementService.PassesOverSavedHeatCaller(
            new VoiceProfile { Engine = VoiceEngine.ElevenLabs }, consentWithheld: true, allowanceSpent: true, kokoroReady: true));
    }

    [Fact]
    public void The_heatcaller_saved_line_lookup_asks_whether_kokoro_can_speak()
    {
        var voice = Source("AnnouncementService.HeatCallerVoice.cs");

        Assert.Contains(
            "if (!PassesOverSavedHeatCaller(profile, HeatCallerConsentWithheld(profile), allowanceSpent, KokoroSpeech.IsAvailable)",
            voice, StringComparison.Ordinal);
        Assert.Contains(
            "return PassesOverSavedHeatCaller(profile, consentWithheld: false, allowanceSpent, KokoroSpeech.IsAvailable);",
            voice, StringComparison.Ordinal);
    }

    // ---------------------------------- the 100% message with no stand-in

    [Fact]
    public void The_takeover_is_said_only_when_kokoro_really_takes_over()
    {
        var (said, words) = AnnouncementService.MeterEdgeWords(wasSaid: false, spent: true, standingIn: true);
        Assert.True(said);
        Assert.Contains("the free Kokoro voice is speaking from here", words, StringComparison.Ordinal);

        // At 100% with nothing standing in: nothing changed, nothing said.
        Assert.Equal((false, (string?)null), AnnouncementService.MeterEdgeWords(wasSaid: false, spent: true, standingIn: false));

        // The stand-in unticked at 100%: said truthfully.
        (said, words) = AnnouncementService.MeterEdgeWords(wasSaid: true, spent: true, standingIn: false);
        Assert.False(said);
        Assert.Contains("no longer standing in", words, StringComparison.Ordinal);
        Assert.DoesNotContain("speaking from here", words, StringComparison.Ordinal);

        // Under 100% again, as before.
        (said, words) = AnnouncementService.MeterEdgeWords(wasSaid: true, spent: false, standingIn: true);
        Assert.False(said);
        Assert.Contains("under 100% again", words, StringComparison.Ordinal);

        Assert.Equal((true, (string?)null), AnnouncementService.MeterEdgeWords(wasSaid: true, spent: true, standingIn: true));
        Assert.Equal((false, (string?)null), AnnouncementService.MeterEdgeWords(wasSaid: false, spent: false, standingIn: true));
    }

    [Fact]
    public void At_100_percent_with_the_stand_in_off_the_console_does_not_say_kokoro_took_over()
    {
        var voices = VoiceSettingsStore.Load();
        voices.Current.Engine = VoiceEngine.ElevenLabs;
        voices.Current.KokoroStandsIn = false;

        var announcer = new AnnouncementService(ProfileStore.Load(), voices, SoundEffectsStore.Load())
        {
            ElevenLabsMeterSpent = () => true,
        };

        var log = new ConcurrentQueue<string>();
        announcer.StatusMessage += log.Enqueue;

        var usedUp = (bool)Method("ElevenLabsAllowanceUsedUp").Invoke(announcer, new object[] { voices.Current })!;

        Assert.True(usedUp);
        Assert.DoesNotContain(log, line => line.Contains("speaking from here", StringComparison.Ordinal)
            || line.Contains("held back", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------- helpers

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
}
