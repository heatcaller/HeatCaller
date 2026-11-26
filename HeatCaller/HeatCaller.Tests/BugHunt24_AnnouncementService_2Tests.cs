using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the announcer, second group:
/// a driver name saved with two spaces was never shortened for the cloud
/// voice, the captions or listen-along; a winner's line with a placeholder
/// besides {winner} was said with the braces in; a streamed line dropped for
/// a mute stayed on the screens; the winner's call and the class winners'
/// call never reached the screens; a rivalry line was thrown away by an
/// unrelated lead change; and a gap in a streamed HeatCaller-voice line
/// blamed ElevenLabs.
/// </summary>
public sealed class BugHunt24_AnnouncementService_2Tests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static FieldInfo Field(string name) => typeof(AnnouncementService).GetField(name, Private)!;

    private static MethodInfo Method(string name) => typeof(AnnouncementService).GetMethod(name, Private)!;

    // ------------------------------------------------- names with a doubled space

    [Fact]
    public void A_name_saved_with_two_spaces_is_shortened_in_a_tidied_line()
    {
        var map = AnnouncementService.ShortNameMap(new[] { "Mia  Kent", "Sam Parker" });

        Assert.Equal("Mia K takes the lead", AnnouncementService.ShortenNames("Mia Kent takes the lead", map));
    }

    [Fact]
    public void A_name_saved_with_a_tab_is_shortened_too()
    {
        var map = AnnouncementService.ShortNameMap(new[] { "Mia\tKent", "Sam Parker" });

        Assert.Equal("Mia K wins", AnnouncementService.ShortenNames("Mia Kent wins", map));
    }

    [Fact]
    public void A_line_that_keeps_a_doubled_space_is_still_shortened()
    {
        var map = AnnouncementService.ShortNameMap(new[] { "Mia Kent", "Sam Parker" });

        Assert.Equal("Mia K wins", AnnouncementService.ShortenNames("Mia  Kent wins", map));
    }

    [Fact]
    public void The_voice_says_the_first_name_of_a_name_saved_with_two_spaces()
    {
        var map = AnnouncementService.SpokenNameMap(new[] { (Driver: "Mia  Kent", Spoken: string.Empty) });

        Assert.Equal("Mia takes the lead", AnnouncementService.ShortenNames("Mia Kent takes the lead", map));
    }

    [Fact]
    public void The_public_pages_shorten_a_driver_saved_with_two_spaces()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "301", DriverName = "Mia  Kent" });
        profiles.Upsert(new DriverProfile { Transponder = "302", DriverName = "Sam Parker" });

        var announcer = new AnnouncementService(profiles, VoiceSettingsStore.Load(), SoundEffectsStore.Load())
        {
            Muted = true,
            ShortenNamesForCloudVoices = () => true,
        };

        Assert.Equal("Mia K wins", announcer.TextForPublicPages("Mia Kent wins"));
        Assert.DoesNotContain("Kent", announcer.TextForCloudVoice("Mia Kent wins"), StringComparison.Ordinal);
    }

    // ------------------------------------------------------ the winner's line

    [Fact]
    public void A_winner_line_with_another_placeholder_drops_it_with_its_lead_in()
    {
        Assert.Equal("Checkered flag - Dee Ploy takes it!",
            AnnouncementService.WinnerLine(new[] { "Checkered flag in {class} - {winner} takes it!" }, "Dee Ploy"));
        Assert.Equal("Dee Ploy wins it!",
            AnnouncementService.WinnerLine(new[] { "{winner} wins it in the {car}!" }, "Dee Ploy"));

        var team = AnnouncementService.WinnerLine(new[] { "{WINNER} of {team} wins!" }, "Dee Ploy");
        Assert.Contains("Dee Ploy", team, StringComparison.Ordinal);
        Assert.DoesNotContain("{", team, StringComparison.Ordinal);
        Assert.DoesNotContain("}", team, StringComparison.Ordinal);
    }

    [Fact]
    public void A_winner_line_with_only_the_name_is_left_as_it_was()
    {
        Assert.Equal("[shouting] Done and dusted - Dee Ploy takes it!!",
            AnnouncementService.WinnerLine(new[] { "[shouting] Done and dusted - {winner} takes it!!" }, "Dee Ploy"));
        Assert.Equal("Victory for Dee Ploy!", AnnouncementService.WinnerLine(new[] { "Victory for {driver}!" }, "Dee Ploy"));
    }

    // ------------------------------------------- the winner's call on the screens

    /// <summary>An announcer at the flag, Mo (Stock) leading Sam (Mod), with the queue held as the finish call holds it.</summary>
    private static (AnnouncementService Announcer, ConcurrentQueue<string> Shown) AtTheFlag(bool sharedHeat)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "201", DriverName = "Mo" });
        profiles.Upsert(new DriverProfile { Transponder = "202", DriverName = "Sam" });

        var voice = VoiceSettingsStore.Load();
        voice.Current.Events[nameof(AnnouncementEventType.RaceCompleted)].Enabled = true;
        voice.Current.Events[nameof(AnnouncementEventType.WinnerCalled)].Enabled = true;
        voice.Current.Events[nameof(AnnouncementEventType.WinnerCalled)].Templates = new List<string> { "Victory for {winner}!" };

        var announcer = new AnnouncementService(profiles, voice, SoundEffectsStore.Load());

        if (sharedHeat)
        {
            var classes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["201"] = "Stock", ["202"] = "Mod" };
            announcer.SharedHeatClasses = () => classes;
        }

        var standings = (Dictionary<string, RaceStanding>)Field("_standings").GetValue(announcer)!;
        standings["201"] = new RaceStanding { Transponder = "201", Name = "Mo", Laps = 10, ElapsedMs = 80_000 };
        standings["202"] = new RaceStanding { Transponder = "202", Name = "Sam", Laps = 10, ElapsedMs = 82_000 };
        Field("_currentLeaderTransponder").SetValue(announcer, "201");

        Method("BeginFinishCallHold").Invoke(announcer, null);

        var shown = new ConcurrentQueue<string>();
        announcer.AnnouncementTextReady += shown.Enqueue;

        return (announcer, shown);
    }

    /// <summary>The winner's call queued, and each item's work taken off the queue in order.</summary>
    private static List<Func<Task>> QueuedWork(AnnouncementService announcer)
    {
        Method("QueueTheWinnersCall").Invoke(announcer, null);

        var queue = (IList)Field("_voiceQueue").GetValue(announcer)!;
        var work = new List<Func<Task>>();

        lock (Field("_voiceQueueLock").GetValue(announcer)!)
        {
            foreach (var item in queue)
            {
                work.Add((Func<Task>)item!.GetType().GetProperty("Work")!.GetValue(item)!);
            }

            queue.Clear();
        }

        return work;
    }

    [Fact]
    public async Task The_winners_call_reaches_the_screens_as_it_is_said()
    {
        var (announcer, shown) = AtTheFlag(sharedHeat: false);
        var work = Assert.Single(QueuedWork(announcer));

        // Not while the finish call is still being said.
        Assert.Empty(shown);

        // Muted so nothing is rendered - the words go up as the line is taken.
        announcer.Muted = true;
        await work().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(new[] { "Victory for Mo!" }, shown.ToList());
    }

    [Fact]
    public async Task The_class_winners_call_reaches_the_screens_after_the_winners()
    {
        var (announcer, shown) = AtTheFlag(sharedHeat: true);
        var work = QueuedWork(announcer);

        Assert.Equal(2, work.Count);
        Assert.Empty(shown);

        announcer.Muted = true;

        foreach (var line in work)
        {
            await line().WaitAsync(TimeSpan.FromSeconds(10));
        }

        Assert.Equal(new[] { "Victory for Mo!", "Mo wins Stock, Sam wins Mod." }, shown.ToList());
    }

    // --------------------------------------------------- the streamed mute drop

    [Fact]
    public void A_streamed_line_dropped_for_a_mute_is_taken_back_off_the_screens()
    {
        var streaming = Source("AnnouncementService.Streaming.cs");
        var muted = streaming.IndexOf("drop = \"the announcer was muted before it spoke\";", StringComparison.Ordinal);
        var dropped = streaming.IndexOf("if (drop is not null)", muted, StringComparison.Ordinal);

        Assert.True(muted > 0 && dropped > muted);
        Assert.Contains("WithdrawAnnouncementText(_dequeuedFreshness.Value.Shown);", streaming[muted..dropped], StringComparison.Ordinal);
    }

    // ------------------------------------------------------ the rivalry line

    [Fact]
    public void A_rivalry_line_is_stamped_as_a_pass_for_position_not_a_lead_change()
    {
        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load()) { Muted = true };

        var battle = announcer.TemplateFactsFor(AnnouncementEventType.CloseBattle)!;
        Assert.Null(AnnouncementService.WhyNoLongerTrue(battle, battle with { LeadChanges = battle.LeadChanges + 1 }));

        var lead = announcer.TemplateFactsFor(AnnouncementEventType.LeaderChange)!;
        Assert.Equal("the lead has changed hands since it was written",
            AnnouncementService.WhyNoLongerTrue(lead, lead with { LeadChanges = lead.LeadChanges + 1 }));

        var lapCalls = Source("AnnouncementService.LapCalls.cs");
        var start = lapCalls.IndexOf("private void FireRivalryLine(", StringComparison.Ordinal);
        var end = lapCalls.IndexOf("private void HandleGatePassed(", start, StringComparison.Ordinal);

        Assert.True(start > 0 && end > start);
        Assert.Contains("facts: TemplateFactsFor(AnnouncementEventType.CloseBattle)", lapCalls[start..end], StringComparison.Ordinal);
    }

    // ------------------------------------------------- the streamed gap warning

    [Fact]
    public void A_gap_in_a_streamed_heatcaller_voice_line_does_not_blame_elevenlabs()
    {
        var words = AnnouncementService.StreamGapWords("Sam wins", 2, heatCaller: true);

        Assert.Contains("ran ahead of the HeatCaller voice 2 times", words, StringComparison.Ordinal);
        Assert.DoesNotContain("ElevenLabs", words, StringComparison.Ordinal);

        Assert.Contains("ran ahead of ElevenLabs 1 time", AnnouncementService.StreamGapWords("Sam wins", 1, heatCaller: false), StringComparison.Ordinal);
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
