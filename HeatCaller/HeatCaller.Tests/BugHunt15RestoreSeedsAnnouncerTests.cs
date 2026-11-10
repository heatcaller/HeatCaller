using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15, B5 (9 Nov 2026): undoing Stage over a running race put the
/// race back on the board but left the announcer's running order empty -
/// Staging had reset it. After the green the first car over was the whole
/// field to the announcer, so the real leader crossing next was called as
/// taking the lead and the lapped cars were called lapped again. The undo
/// (and the Reset undo and the crash restore) now load the restored
/// standings with AnnouncementService.SeedStandings, which makes no call.
/// </summary>
public sealed class BugHunt15RestoreSeedsAnnouncerTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>A car with equal lap times making up its race time.</summary>
    private static RaceStanding Car(string transponder, int laps, long elapsedMs)
    {
        var lapTimes = new long[laps];
        for (var i = 0; i < laps; i++)
        {
            lapTimes[i] = i < laps - 1 ? elapsedMs / laps : elapsedMs - (elapsedMs / laps * (laps - 1));
        }

        return new RaceStanding
        {
            Transponder = transponder,
            Id = transponder,
            Laps = laps,
            ElapsedMs = elapsedMs,
            Status = "active",
            LapTimesMs = lapTimes,
        };
    }

    private static (AnnouncementService Announcer, ConcurrentQueue<string> Shown) Announcer()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Bill" });

        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        voice.Current.Events[nameof(AnnouncementEventType.LeaderChange)] =
            new AnnouncementEventConfig { Enabled = true, Weight = 100, Templates = { "{driver} now leads" } };
        voice.Current.Events[nameof(AnnouncementEventType.Lapped)] =
            new AnnouncementEventConfig { Enabled = true, Weight = 100, Templates = { "{driver} has been lapped" } };

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        var announcer = new AnnouncementService(profiles, voice, sounds);
        var shown = new ConcurrentQueue<string>();
        announcer.AnnouncementTextReady += shown.Enqueue;

        return (announcer, shown);
    }

    /// <summary>
    /// After the restart: Sam, a lap down, crosses first, then Bill, the
    /// leader. Neither is a lead change nor a new lapping.
    /// </summary>
    private static void RestartCrossings(AnnouncementService announcer)
    {
        announcer.OnStandingUpdated(Car("101", 12, 130_000));
        announcer.OnStandingUpdated(Car("102", 13, 131_000));
    }

    [Fact]
    public void A_seeded_race_makes_no_lead_change_or_lapped_call_on_the_first_crossings()
    {
        var (announcer, shown) = Announcer();

        // Bill leads on lap 12; Sam is a lap down on 11.
        announcer.SeedStandings([Car("102", 12, 120_000), Car("101", 11, 121_000)]);

        Assert.Equal("102", announcer.Leads.Leader);
        Assert.Equal(0, announcer.Leads.Changes);

        RestartCrossings(announcer);
        Thread.Sleep(900);

        Assert.DoesNotContain(shown, text => text.Contains("now leads", StringComparison.Ordinal));
        Assert.DoesNotContain(shown, text => text.Contains("has been lapped", StringComparison.Ordinal));
        Assert.Equal(0, announcer.Leads.Changes);
    }

    [Fact]
    public void Without_the_seed_the_same_crossings_make_a_false_lead_change_call()
    {
        // The fault, for contrast: the order rebuilt one crossing at a time.
        var (announcer, shown) = Announcer();

        RestartCrossings(announcer);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline && !shown.Any(text => text.Contains("now leads", StringComparison.Ordinal)))
        {
            Thread.Sleep(50);
        }

        Assert.Contains(shown, text => text.Contains("Bill now leads", StringComparison.Ordinal));
    }
}
