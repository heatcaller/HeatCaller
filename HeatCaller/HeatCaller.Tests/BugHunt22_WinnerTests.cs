using System.Collections;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 22 (19 Nov 2026): a car shown the black flag still has its laps
/// counted, so it could lead the announcer's order to the flag and be named
/// the winner while the result put it last, disqualified.
/// </summary>
public sealed class BugHunt22_WinnerTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static FieldInfo Field(string name) => typeof(AnnouncementService).GetField(name, Private)!;

    private static MethodInfo Method(string name) => typeof(AnnouncementService).GetMethod(name, Private)!;

    private static AnnouncementService AtTheFlag(bool leaderBlackFlagged)
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
        standings["201"] = new RaceStanding { Transponder = "201", Name = "Mo", Laps = 11, ElapsedMs = 79_000, BlackFlagged = leaderBlackFlagged };
        standings["202"] = new RaceStanding { Transponder = "202", Name = "Sam", Laps = 10, ElapsedMs = 82_000 };
        Field("_currentLeaderTransponder").SetValue(announcer, "201");

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
    public void A_black_flagged_car_in_front_at_the_flag_is_not_named_the_winner()
    {
        Assert.Equal("Victory for Sam!", Assert.Single(QueuedAtTheFlag(AtTheFlag(leaderBlackFlagged: true))));
    }

    [Fact]
    public void A_leader_not_black_flagged_is_still_named()
    {
        Assert.Equal("Victory for Mo!", Assert.Single(QueuedAtTheFlag(AtTheFlag(leaderBlackFlagged: false))));
    }

    [Fact]
    public void The_announcer_is_told_of_a_black_flag_at_once_and_at_every_crossing()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        var root = Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller");
        var warnings = File.ReadAllText(Path.Combine(root, "RaceControlWarnings.cs"));
        var events = File.ReadAllText(Path.Combine(root, "RaceControlControl.RaceEvents.cs"));

        Assert.Contains("_announcer.OnStandingUpdated(AdjustedStandingFor(standing));", warnings);
        Assert.Equal(2, events.Split("BlackFlagged = standing.IsBlackFlagged,").Length - 1);
    }
}
