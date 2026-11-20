using System.Collections.Concurrent;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 22 (20 Nov 2026): a car shown the black flag keeps its laps, so
/// it stayed the announcer's leader - gaps were given to it, position reports
/// named it, and the car behind taking the lead was never called. Asher's
/// pick (B): it leaves the order quietly, as a hand correction does - no
/// "takes the lead" for the flag, and it is in no report, gap or battle line
/// from then on.
/// </summary>
public sealed class BugHunt22_BlackFlagOrderTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static RaceStanding Car(string transponder, int laps, long elapsedMs, bool blackFlagged = false, bool adjusted = false) => new()
    {
        Transponder = transponder,
        Id = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        BlackFlagged = blackFlagged,
        Status = adjusted ? "adjusted" : "active",
    };

    /// <summary>Every call and sound off but the lead change; Sam (101), Bill (102) and Jo (103) on file.</summary>
    private static (AnnouncementService Announcer, ConcurrentQueue<string> Shown, ConcurrentQueue<string> Log) Announcer()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Bill" });
        profiles.Upsert(new DriverProfile { Transponder = "103", DriverName = "Jo" });

        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        voice.Current.Events[nameof(AnnouncementEventType.LeaderChange)] =
            new AnnouncementEventConfig { Enabled = true, Weight = 100, Templates = { "{driver} now leads" } };

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

    private static void LetTheBatchesSettle() => Thread.Sleep(900);

    private static void UntilShown(ConcurrentQueue<string> shown, string words)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !shown.Any(text => text.Contains(words, StringComparison.Ordinal)))
        {
            Thread.Sleep(50);
        }
    }

    private static List<string> RunningOrder(AnnouncementService announcer)
    {
        var method = typeof(AnnouncementService).GetMethod("RunningOrderNow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var rows = (System.Collections.IEnumerable)method.Invoke(announcer, new object[] { 5, false })!;

        return rows.Cast<object>()
            .Select(row => ((RaceStanding)row.GetType().GetProperty("Standing")!.GetValue(row)!).Transponder)
            .ToList();
    }

    [Fact]
    public void The_black_flag_moves_the_lead_quietly_and_the_next_real_pass_is_still_called()
    {
        var (announcer, shown, _) = Announcer();

        // Bill leads Sam, then Jo, on lap 6.
        announcer.OnStandingUpdated(Car("102", 6, 60_000));
        announcer.OnStandingUpdated(Car("101", 6, 61_000));
        announcer.OnStandingUpdated(Car("103", 6, 61_500));

        // Black flag for Bill - Race Control hands it over as an adjustment.
        announcer.OnStandingUpdated(Car("102", 6, 60_000, blackFlagged: true, adjusted: true));
        LetTheBatchesSettle();

        Assert.Empty(shown);
        Assert.Equal("101", announcer.Leads.Leader);
        Assert.Equal(0, announcer.Leads.Changes);

        // Bill keeps lapping before he comes in, and Sam crosses in front of
        // Jo: nothing to call - Bill is not the leader, and Sam took nothing.
        announcer.OnStandingUpdated(Car("102", 7, 70_000, blackFlagged: true));
        announcer.OnStandingUpdated(Car("101", 7, 71_000));
        LetTheBatchesSettle();

        Assert.Empty(shown);
        Assert.Equal(new[] { "101", "103" }, RunningOrder(announcer));

        // The control: Jo really passing Sam is called.
        announcer.OnStandingUpdated(Car("103", 8, 79_000));
        UntilShown(shown, "now leads");
        announcer.Muted = true;

        Assert.Single(shown, text => text.StartsWith("Jo now leads", StringComparison.Ordinal));
        Assert.Equal(1, announcer.Leads.Changes);
    }

    [Fact]
    public void A_battle_line_about_the_flagged_car_is_cut_without_a_pass_call()
    {
        var (announcer, shown, log) = Announcer();

        announcer.OnStandingUpdated(Car("102", 4, 40_000));
        announcer.OnStandingUpdated(Car("101", 4, 40_400));
        announcer.PutBattleLineOnAir(new BattlePair(AheadId: "102", BehindId: "101", Position: 1));

        announcer.OnStandingUpdated(Car("102", 4, 40_000, blackFlagged: true, adjusted: true));
        LetTheBatchesSettle();
        announcer.Muted = true;

        Assert.Single(log, line => line.StartsWith("[voice] Battle line ", StringComparison.Ordinal)
            && line.Contains("black flag", StringComparison.Ordinal));
        Assert.Empty(shown);
    }
}
