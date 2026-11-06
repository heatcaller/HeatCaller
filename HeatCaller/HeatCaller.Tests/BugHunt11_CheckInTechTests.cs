using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026), check-in and tech: the Big Screen's grid roll
/// call must not count a pass made in the race that just finished, the rule
/// Mission Control already keeps (MainForm.MissionControl).
/// </summary>
public sealed class BugHunt11_CheckInTechTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    [Fact]
    public void The_roll_call_starts_at_the_grace_or_the_last_finish_whichever_is_later()
    {
        var now = new DateTime(2026, 11, 5, 20, 0, 0, DateTimeKind.Utc);

        // Nothing finished yet: the grace alone.
        Assert.Equal(now - RollCall.Grace, RollCall.Since(now, null));

        // Finished long ago: the grace.
        Assert.Equal(now - RollCall.Grace, RollCall.Since(now, now.AddMinutes(-10)));

        // Finished 30 seconds ago, staged now: from the finish, not before it.
        Assert.Equal(now.AddSeconds(-30), RollCall.Since(now, now.AddSeconds(-30)));
    }

    [Fact]
    public void A_pass_in_the_heat_before_does_not_make_the_next_grid_heard_on_the_big_screen()
    {
        var seen = StaThread.Run(() =>
        {
            var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());
            var race = new RaceTiming(new RaceControlSession());
            var watch = new TransponderWatch();

            using var form = new BigScreenForm(race, announcer, ProfileStore.Load(),
                watch: watch, roster: () => new[] { "T1" });

            // Heat 1: T1's last pass, then its transponder dies on the last lap.
            race.Stage();
            race.GreenFlag(race.EstimatedDeviceTimeNowMs);
            watch.Record(new DirectLapEvent { TransponderId = "T1", SignalHits = 8 }, DateTime.UtcNow.AddSeconds(-5));
            race.CheckeredFlag();
            var finished = race.State == RaceState.Finished;

            // Heat 2 staged straight away - well inside the two-minute grace.
            race.Stage();
            UpdateRollCall(form);
            var beforeHeard = Entries(form).Single().State;

            // A pass after the finish is proof, and the card turns.
            watch.Record(new DirectLapEvent { TransponderId = "T1", SignalHits = 8 }, DateTime.UtcNow.AddSeconds(1));
            UpdateRollCall(form);
            var afterHeard = Entries(form).Single().State;

            form.Close();
            return (finished, beforeHeard, afterHeard);
        }, TimeSpan.FromSeconds(60), () => "the Big Screen's roll call across a finish");

        Assert.True(seen.finished);
        Assert.Equal(RollCall.State.Waiting, seen.beforeHeard);
        Assert.Equal(RollCall.State.Heard, seen.afterHeard);
    }

    private static void UpdateRollCall(BigScreenForm form) =>
        typeof(BigScreenForm).GetMethod("UpdateRollCall", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);

    private static IReadOnlyList<RollCall.Entry> Entries(BigScreenForm form)
    {
        var board = typeof(BigScreenForm).GetField("_rollCall", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        return (IReadOnlyList<RollCall.Entry>)typeof(RollCallBoard).GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(board)!;
    }
}
