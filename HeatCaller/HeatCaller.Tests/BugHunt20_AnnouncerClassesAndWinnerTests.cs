using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 20, the announcer.
///
/// In a heat two classes share, the battle pass call, the ruling's pass call
/// and the battle line said the place in the whole heat, not in the class,
/// and the end-of-race gap call measured the overall second against the
/// overall leader across classes. A ghost in front at the flag was named the
/// winner, and so was the first car home in a class scored on handicap. And
/// a fast lap dropped because someone went quicker stayed on the screens.
/// </summary>
public sealed class BugHunt20_AnnouncerClassesAndWinnerTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static FieldInfo Field(string name) => typeof(AnnouncementService).GetField(name, Private)!;

    private static MethodInfo Method(string name) => typeof(AnnouncementService).GetMethod(name, Private)!;

    // Frank runs Mod; Sam and Bill run Stock.
    private static readonly Dictionary<string, string> ModAndStock = new(StringComparer.OrdinalIgnoreCase)
    {
        ["101"] = "Stock",
        ["102"] = "Stock",
        ["103"] = "Mod",
    };

    private static RaceStanding Car(string transponder, int laps, long elapsedMs) => new()
    {
        Transponder = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        Status = "active",
        Id = transponder,
    };

    /// <summary>An announcer with every call and sound off, Sam (101), Bill (102) and Frank (103) on file, in a shared heat.</summary>
    private static (AnnouncementService Announcer, ConcurrentQueue<string> Shown, ConcurrentQueue<string> Log) Announcer()
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

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        var announcer = new AnnouncementService(profiles, voice, sounds) { SharedHeatClasses = () => ModAndStock };
        var shown = new ConcurrentQueue<string>();
        var log = new ConcurrentQueue<string>();
        announcer.AnnouncementTextReady += shown.Enqueue;
        announcer.StatusMessage += log.Enqueue;

        return (announcer, shown, log);
    }

    // --------------------------------------------------------- the pass calls

    [Fact]
    public void The_pass_calls_can_give_the_place_in_a_class()
    {
        Assert.Equal("[excited] And Sam is through, up into second in Stock!", AnnouncementService.BattlePassWords("Sam", 2, "Stock"));
        Assert.Equal("[shouting] And Sam takes the lead in Stock!", AnnouncementService.BattlePassWords("Sam", 1, "Stock"));
        Assert.Equal("[shouting] And with that ruling, Sam takes the lead in Stock!", AnnouncementService.RulingPassWords("Sam", 1, "Stock"));
        Assert.Equal("[excited] And with that ruling, Sam is up into third in Stock!", AnnouncementService.RulingPassWords("Sam", 3, "Stock"));

        // A one-class race is worded as before.
        Assert.Equal("[excited] And Sam is through, up into third!", AnnouncementService.BattlePassWords("Sam", 3));
    }

    [Fact]
    public void A_pass_in_a_shared_heat_is_called_by_the_place_in_the_class()
    {
        var (announcer, shown, log) = Announcer();

        // Frank (Mod) leads; Bill and Sam fight for the Stock lead behind.
        announcer.OnStandingUpdated(Car("103", 5, 45_000));
        announcer.OnStandingUpdated(Car("102", 4, 40_000));
        announcer.OnStandingUpdated(Car("101", 4, 40_400));
        announcer.PutBattleLineOnAir(new BattlePair(AheadId: "102", BehindId: "101", Position: 2));

        // Sam gets by: second overall, first in Stock.
        announcer.OnStandingUpdated(Car("101", 5, 50_100));
        announcer.Muted = true;

        Assert.Single(log, line => line.StartsWith("[voice] Battle line ", StringComparison.Ordinal));
        Assert.Equal(new[] { "And Sam takes the lead in Stock!" }, shown.ToList());
    }

    [Fact]
    public void A_pass_between_two_classes_of_a_shared_heat_is_not_called()
    {
        var (announcer, shown, log) = Announcer();

        // Frank (Mod) leads Sam (Stock), and a line about them is on.
        announcer.OnStandingUpdated(Car("103", 4, 40_000));
        announcer.OnStandingUpdated(Car("101", 4, 40_400));
        announcer.PutBattleLineOnAir(new BattlePair(AheadId: "103", BehindId: "101", Position: 1));

        announcer.OnStandingUpdated(Car("101", 5, 50_100));
        announcer.Muted = true;

        // Cut off - it is untrue now - but no "takes the lead" between cars not racing each other.
        Assert.Single(log, line => line.StartsWith("[voice] Battle line ", StringComparison.Ordinal));
        Assert.DoesNotContain(shown, text => text.Contains("takes the lead", StringComparison.Ordinal));
    }

    // ----------------------------------------------------- the final laps gap

    [Fact]
    public void The_final_laps_gap_in_a_shared_heat_is_the_fight_for_the_lead_of_the_class()
    {
        var (announcer, _, _) = Announcer();

        var ordered = new List<RaceStanding> { Car("103", 10, 80_000), Car("102", 10, 80_400), Car("101", 10, 80_700) };
        var fight = (IReadOnlyList<RaceStanding>)Method("TheLeadFightFor").Invoke(announcer, new object[] { ordered, ordered[2] })!;

        // Sam is measured against Bill, the Stock leader - not against Frank.
        Assert.Equal(new[] { "102", "101" }, fight.Select(s => s.Transponder));

        announcer.SharedHeatClasses = () => null;
        var whole = (IReadOnlyList<RaceStanding>)Method("TheLeadFightFor").Invoke(announcer, new object[] { ordered, ordered[1] })!;

        Assert.Equal(new[] { "103", "102", "101" }, whole.Select(s => s.Transponder));
    }

    // ----------------------------------------------------- the winner at the flag

    /// <summary>An announcer at the flag of a race whose leader is given, with the queue held as the finish call holds it.</summary>
    private static AnnouncementService AtTheFlag(string leader)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "201", DriverName = "Mo" });
        profiles.Upsert(new DriverProfile { Transponder = "202", DriverName = "Sam" });

        var voice = VoiceSettingsStore.Load();
        voice.Current.Events[nameof(AnnouncementEventType.RaceCompleted)].Enabled = true;
        voice.Current.Events[nameof(AnnouncementEventType.WinnerCalled)].Enabled = true;
        voice.Current.Events[nameof(AnnouncementEventType.WinnerCalled)].Templates = new List<string> { "Victory for {winner}!" };

        var announcer = new AnnouncementService(profiles, voice, SoundEffectsStore.Load());

        var standings = (Dictionary<string, RaceStanding>)Field("_standings").GetValue(announcer)!;
        standings["GHOST-1"] = new RaceStanding { Transponder = "GHOST-1", Name = "Ghost Sam", Laps = 11, ElapsedMs = 79_000 };
        standings["201"] = new RaceStanding { Transponder = "201", Name = "Mo", Laps = 10, ElapsedMs = 80_000 };
        standings["202"] = new RaceStanding { Transponder = "202", Name = "Sam", Laps = 10, ElapsedMs = 82_000 };
        Field("_currentLeaderTransponder").SetValue(announcer, leader);

        Method("BeginFinishCallHold").Invoke(announcer, null);

        return announcer;
    }

    private static List<string?> QueuedAtTheFlag(AnnouncementService announcer)
    {
        Method("QueueTheWinnersCall").Invoke(announcer, null);

        var queue = (IList)Field("_voiceQueue").GetValue(announcer)!;
        var queued = new List<string?>();

        lock (Field("_voiceQueueLock").GetValue(announcer)!)
        {
            foreach (var item in queue)
            {
                queued.Add((string?)item!.GetType().GetProperty("ShownText")!.GetValue(item));
            }

            queue.Clear();
        }

        return queued;
    }

    [Fact]
    public void A_ghost_in_front_at_the_flag_is_not_named_the_winner()
    {
        var words = Assert.Single(QueuedAtTheFlag(AtTheFlag("GHOST-1")));

        Assert.Equal("Victory for Mo!", words);
    }

    [Fact]
    public void A_real_leader_is_still_named()
    {
        Assert.Equal("Victory for Mo!", Assert.Single(QueuedAtTheFlag(AtTheFlag("201"))));
    }

    [Fact]
    public void A_class_scored_on_handicap_names_no_winner_at_the_flag()
    {
        var announcer = AtTheFlag("201");
        IReadOnlyCollection<string>? asked = null;
        announcer.ScoredOnHandicap = cars => { asked = cars; return true; };

        Assert.Empty(QueuedAtTheFlag(announcer));

        // Asked about the real cars only.
        Assert.NotNull(asked);
        Assert.DoesNotContain("GHOST-1", asked!);

        var finish = new AnnouncementEventConfig { Enabled = true, Templates = new List<string> { "Checkered flag - {winner} takes it!" } };
        Assert.False((bool)Method("TrySpeakTheFinishWithTheWinner").Invoke(announcer, new object[] { finish })!);
    }

    [Fact]
    public void A_class_not_scored_on_handicap_still_names_the_winner()
    {
        var announcer = AtTheFlag("201");
        announcer.ScoredOnHandicap = _ => false;

        Assert.Equal("Victory for Mo!", Assert.Single(QueuedAtTheFlag(announcer)));
    }

    [Fact]
    public void MainForm_tells_the_announcer_when_the_race_is_scored_on_handicap()
    {
        var main = Source("MainForm.cs");

        Assert.Contains("_announcer.ScoredOnHandicap = transponders =>", main, StringComparison.Ordinal);
        Assert.Contains("_race.StartType != RaceStartType.StaggeredHandicap", main, StringComparison.Ordinal);
    }

    // ----------------------------------------------- a line the race made untrue

    [Fact]
    public void A_line_dropped_by_its_own_test_is_taken_back_off_the_screens()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");
        var notNow = queue.IndexOf("if (notNow is not null)", StringComparison.Ordinal);
        var muted = queue.IndexOf("if (honourMute && Muted)", notNow, StringComparison.Ordinal);

        Assert.True(notNow > 0 && muted > notNow);
        Assert.Contains("WithdrawAnnouncementText(_dequeuedFreshness.Value.Shown);", queue[notNow..muted], StringComparison.Ordinal);

        var streaming = Source("AnnouncementService.Streaming.cs");
        var invoked = streaming.IndexOf("drop = whyNotNow?.Invoke();", StringComparison.Ordinal);
        var mute = streaming.IndexOf("if (drop is null && honourMute && Muted)", invoked, StringComparison.Ordinal);

        Assert.True(invoked > 0 && mute > invoked);
        Assert.Contains("WithdrawAnnouncementText(_dequeuedFreshness.Value.Shown);", streaming[invoked..mute], StringComparison.Ordinal);
    }

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
