using System.Collections;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The winner first, then the finish call (24 Nov 2026, asked for by Asher:
/// "I want the winner announced played first and then the race completed
/// call after it"). It was the finish call ("That's the finish!") and then
/// the name ("Victory for Mo!"). Now, with the Winner named row on, the
/// name goes out straight after the fanfare, in the finish call's place, and
/// the finish call's own words are first in the queue behind it.
/// </summary>
public sealed class WinnerBeforeTheFinishTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static FieldInfo Field(string name) => typeof(AnnouncementService).GetField(name, Private)!;

    private static MethodInfo Method(string name) => typeof(AnnouncementService).GetMethod(name, Private)!;

    private static AnnouncementService AtTheFlag(bool winnerRowOn)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "201", DriverName = "Mo" });
        profiles.Upsert(new DriverProfile { Transponder = "202", DriverName = "Sam" });

        var voice = VoiceSettingsStore.Load();
        var finish = voice.Current.Events[nameof(AnnouncementEventType.RaceCompleted)];
        finish.Enabled = true;
        finish.Templates = new List<string> { "That's the finish!" };

        var winner = voice.Current.Events[nameof(AnnouncementEventType.WinnerCalled)];
        winner.Enabled = winnerRowOn;
        winner.Templates = new List<string> { "Victory for {winner}!" };

        var announcer = new AnnouncementService(profiles, voice, SoundEffectsStore.Load());

        var standings = (Dictionary<string, RaceStanding>)Field("_standings").GetValue(announcer)!;
        standings["201"] = new RaceStanding { Transponder = "201", Name = "Mo", Laps = 11, ElapsedMs = 79_000 };
        standings["202"] = new RaceStanding { Transponder = "202", Name = "Sam", Laps = 10, ElapsedMs = 82_000 };
        Field("_currentLeaderTransponder").SetValue(announcer, "201");

        return announcer;
    }

    /// <summary>What NotifyRaceCompleted does with the voice, without the fanfare: the finish call, then the winner's call.</summary>
    private static (List<string> Shown, List<string?> Queued) TheFlag(AnnouncementService announcer)
    {
        var shown = new List<string>();
        announcer.AnnouncementTextReady += shown.Add;
        Method("BeginFinishCallHold").Invoke(announcer, null);

        var queue = (IList)Field("_voiceQueue").GetValue(announcer)!;
        var queued = new List<string?>();

        // The queue's lock held throughout (it is re-entrant, so the calls
        // below still queue), so the voice loop cannot take and say a line
        // before it is read here.
        lock (Field("_voiceQueueLock").GetValue(announcer)!)
        {
            var enqueue = Method("EnqueueVoice");
            var args = enqueue.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
            args[0] = AnnouncementEventType.RaceCompleted;
            enqueue.Invoke(announcer, args);

            Method("QueueTheWinnersCall").Invoke(announcer, null);

            foreach (var item in queue)
            {
                queued.Add((string?)item!.GetType().GetProperty("ShownText")!.GetValue(item));
            }

            queue.Clear();
        }

        return (shown, queued);
    }

    [Fact]
    public void The_winner_is_said_at_the_flag_and_the_finish_call_is_queued_after_it()
    {
        var (shown, queued) = TheFlag(AtTheFlag(winnerRowOn: true));

        // The name is the flag call: on the screens at once.
        Assert.Equal("Victory for Mo!", Assert.Single(shown));

        // The finish call next, and the name not queued a second time.
        Assert.Equal("That's the finish!", Assert.Single(queued));
    }

    [Fact]
    public void With_the_winner_row_off_the_finish_call_goes_as_before()
    {
        var (shown, queued) = TheFlag(AtTheFlag(winnerRowOn: false));

        Assert.Equal("That's the finish!", Assert.Single(shown));
        Assert.Empty(queued);
    }

    [Fact]
    public void A_finish_line_that_names_the_winner_is_still_one_line()
    {
        var announcer = AtTheFlag(winnerRowOn: true);
        var voice = (VoiceSettingsStore)typeof(AnnouncementService).GetField("_voiceStore", Private)!.GetValue(announcer)!;
        voice.Current.Events[nameof(AnnouncementEventType.RaceCompleted)].Templates = new List<string> { "Checkered flag - {winner} takes it!" };

        var (shown, queued) = TheFlag(announcer);

        Assert.Equal("Checkered flag - Mo takes it!", Assert.Single(shown));
        Assert.Empty(queued);
    }
}
