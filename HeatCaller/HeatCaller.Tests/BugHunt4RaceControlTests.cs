using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Race Control fixes from the 4 Oct 2026 bug hunt: a Quick Race or a re-run
/// restaged before its green losing what kind of race it was, a yellow in
/// practice that nothing could clear, the automatic start collapsing to an
/// instant with the announcer muted, and a re-run brought back from the crash
/// journal as an ordinary freeform race.
/// </summary>
public sealed class BugHunt4RaceControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- Stage pressed again during a start -----------------------------------------------

    [Fact]
    public void Stage_pressed_again_during_a_Quick_Races_countdown_keeps_it_a_Quick_Race()
    {
        // The yellow out during Quick Race's countdown, and Stage pressed to
        // restart it, as the log says to.
        var kind = RaceControlControl.RaceKindAfterStage(
            RaceState.Countdown, tookOverDemo: false, isQuick: true, isRerun: false, rerunPending: false);

        Assert.Equal((true, false), kind);
    }

    [Fact]
    public void Stage_pressed_again_during_a_reruns_staging_keeps_it_a_rerun()
    {
        // The first Stage took the marker up, so nothing is pending now.
        var kind = RaceControlControl.RaceKindAfterStage(
            RaceState.Staging, tookOverDemo: false, isQuick: false, isRerun: true, rerunPending: false);

        Assert.Equal((false, true), kind);
    }

    [Fact]
    public void A_Stage_between_races_starts_the_new_race_unmarked()
    {
        Assert.Equal((false, false), RaceControlControl.RaceKindAfterStage(
            RaceState.Finished, tookOverDemo: false, isQuick: true, isRerun: true, rerunPending: false));

        Assert.Equal((false, false), RaceControlControl.RaceKindAfterStage(
            RaceState.Idle, tookOverDemo: false, isQuick: true, isRerun: true, rerunPending: false));
    }

    [Fact]
    public void A_rerun_the_host_asked_for_is_taken_up_by_the_Stage_that_begins_it()
    {
        Assert.Equal((false, true), RaceControlControl.RaceKindAfterStage(
            RaceState.Idle, tookOverDemo: false, isQuick: false, isRerun: false, rerunPending: true));
    }

    [Fact]
    public void A_Stage_over_a_running_race_is_a_new_race()
    {
        // Undo puts the abandoned race's kind back - see CaptureAbandonedRaceUndo.
        Assert.Equal((false, false), RaceControlControl.RaceKindAfterStage(
            RaceState.Racing, tookOverDemo: false, isQuick: true, isRerun: true, rerunPending: false));

        Assert.Equal((false, true), RaceControlControl.RaceKindAfterStage(
            RaceState.Racing, tookOverDemo: false, isQuick: true, isRerun: false, rerunPending: true));
    }

    [Fact]
    public void A_Stage_that_takes_over_a_demos_countdown_is_a_new_race()
    {
        Assert.Equal((false, false), RaceControlControl.RaceKindAfterStage(
            RaceState.Countdown, tookOverDemo: true, isQuick: true, isRerun: true, rerunPending: false));
    }

    // --- Yellow during practice ------------------------------------------------------------

    [Fact]
    public void A_yellow_from_the_phone_is_refused_while_practice_runs()
    {
        Assert.Equal("Practice is running - stop it on the Practice page first.",
            RaceDayService.PracticeRefusal("yellow", practiceRunning: true));

        // Between races a yellow still goes.
        Assert.Null(RaceDayService.PracticeRefusal("yellow", practiceRunning: false));
    }

    /// <summary>
    /// The whole way through the race-day manager. Windows only: the service
    /// answers on a window's thread, and a window can't be made on Linux.
    /// </summary>
    [Fact]
    public async Task The_race_day_manager_refuses_a_yellow_during_practice_and_leaves_the_flag_alone()
    {
        using var window = new Control();
        _ = window.Handle;

        var race = new RaceControlSession { IsPracticeMode = true };

        var phone = new RaceDayService(new RaceDayServiceHost
        {
            UiThread = window,
            Settings = new AppSettings(),
            Undo = new UndoService(),
            Race = race,
            Events = RaceEventStore.Load(),
            Classes = RaceClassStore.Load(),
            Profiles = ProfileStore.Load(),
            Rounds = RoundDefinitionStore.Load(),
            History = SessionHistoryStore.Load(),
            CheckIns = CheckInStore.Load(),
            Tech = TechInspectionStore.Load(),
            Schedule = DayScheduleStore.Load(),

            // Refused before either is reached.
            Program = null!,
            RaceControl = null!,

            ActiveRound = () => null,
            StartRound = (_, _, _) => "Not in this test.",
            SetCheckedIn = (_, _) => false,
            RepublishSession = _ => { },
            RefreshDesk = () => { },
            Log = _ => { },
        });

        var request = JsonSerializer.Serialize(new { op = "race.flag", args = new { flag = "yellow" } });
        var json = await phone.HandleJsonAsync(request, RaceDayChannel.WiFi, CancellationToken.None);

        using var answer = JsonDocument.Parse(json);
        Assert.False(answer.RootElement.GetProperty("ok").GetBoolean(), json);
        Assert.Equal("Practice is running - stop it on the Practice page first.", answer.RootElement.GetProperty("error").GetString());
        Assert.NotEqual(FlagState.Yellow, race.Flag);
    }

    // --- The automatic start with the announcer muted --------------------------------------

    [Fact]
    public void Muted_a_step_of_the_automatic_start_lasts_its_set_length()
    {
        Assert.Equal(TimeSpan.FromSeconds(15), RaceControlControl.StartStepFloor(muted: true, TimeSpan.Zero, 15));
        Assert.Equal(TimeSpan.FromSeconds(11), RaceControlControl.StartStepFloor(muted: true, TimeSpan.FromSeconds(4), 15));
    }

    [Fact]
    public void Muted_a_step_that_has_already_lasted_its_length_moves_straight_on()
    {
        Assert.Equal(TimeSpan.Zero, RaceControlControl.StartStepFloor(muted: true, TimeSpan.FromSeconds(20), 15));
        Assert.Equal(TimeSpan.Zero, RaceControlControl.StartStepFloor(muted: true, TimeSpan.Zero, -3));
    }

    [Fact]
    public void With_the_announcer_on_the_start_still_follows_the_audio_alone()
    {
        Assert.Equal(TimeSpan.Zero, RaceControlControl.StartStepFloor(muted: false, TimeSpan.Zero, 15));
    }

    // --- A re-run in the crash journal -----------------------------------------------------

    private static void Cross(RaceControlSession race, string car, uint atMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = atMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

    private static RaceControlSession RaceWithLaps()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);

        Cross(race, "AAAAAA", 5_000);
        Cross(race, "AAAAAA", 25_000);
        Cross(race, "AAAAAA", 45_000);

        Assert.True(race.GetOrderedStandings().Sum(s => s.Laps) > 0);
        return race;
    }

    [Fact]
    public void A_rerun_left_in_the_crash_journal_comes_back_as_a_rerun()
    {
        var race = RaceWithLaps();

        // A re-run races with the Round box on freeform, so there is no round
        // to file it under - only the name, which is the recorded heat's own.
        new RaceJournal().Record(race, DateTime.UtcNow, string.Empty, force: true,
            roundId: null, raceLabel: "Stock - Q1 Heat 1", isRerun: true);

        var entry = RaceJournal.Unfinished();

        Assert.NotNull(entry);
        Assert.True(entry.IsRerun);
    }

    [Fact]
    public void Any_other_race_in_the_crash_journal_is_not_a_rerun()
    {
        new RaceJournal().Record(RaceWithLaps(), DateTime.UtcNow, string.Empty, force: true,
            roundId: null, raceLabel: "Stock - Q1 Heat 1");

        var entry = RaceJournal.Unfinished();

        Assert.NotNull(entry);
        Assert.False(entry.IsRerun);
    }

    [Fact]
    public void A_journal_written_before_reruns_were_stored_reads_as_not_a_rerun()
    {
        var entry = JsonSerializer.Deserialize<RaceJournal.Entry>("{\"State\":3,\"RaceLabel\":\"Stock - Q1 Heat 1\"}");

        Assert.NotNull(entry);
        Assert.False(entry.IsRerun);
    }
}
