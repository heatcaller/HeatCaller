namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the follow-up wiring in the main window that
/// other fixes asked for: an abandoned heat's start stamp (F110), a closed
/// day's heats (F228), a heat warming up counting as on track (F254), the
/// crowd's opening mood (F017), the failed update's window (F051), automatic
/// backups kept on this PC (F058), Forget's older transponders (F119), the
/// retired-photo sweep (F124), the garage setups count (F129), the level
/// check during a race (F175), Driver Hub's check-ins (F202), Mission
/// Control's second-car transponders (F269), the too-big garage named
/// (F278), the restored standby address (F287) and the saved-piece answer.
/// </summary>
public sealed class BugHunt24_Requests_B_mainformTests
{
    private static string Source(string file) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", file));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found.");
    }

    /// <summary>The text of a method, from its signature to the next one of the same kind.</summary>
    private static string Slice(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"\"{from}\" has moved - this test needs its bounds updated.");

        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"\"{to}\" not found after \"{from}\" - this test needs its bounds updated.");

        return source[start..end];
    }

    // ------------------------------------------------------------ F110

    [Fact]
    public void An_abandoned_heat_loses_its_start_stamp_and_undo_puts_the_same_one_back()
    {
        var started = new DateTime(2026, 11, 23, 19, 42, 0, DateTimeKind.Utc);
        var stamps = new Dictionary<string, DateTime> { ["heat-3"] = started };

        DateTime? Clear(string id) => stamps.Remove(id, out var was) ? was : null;
        void Mark(string id, DateTime at) => stamps.TryAdd(id, at);

        var putBack = MainForm.ClearStartOfAbandonedRound("heat-3", Clear, Mark);

        Assert.NotNull(putBack);
        Assert.False(stamps.ContainsKey("heat-3"));

        putBack!();

        Assert.Equal(started, stamps["heat-3"]);
    }

    [Fact]
    public void A_freeform_race_or_a_heat_with_no_stamp_leaves_nothing_to_put_back()
    {
        var cleared = 0;
        DateTime? Clear(string id)
        {
            cleared++;
            return null;
        }

        Assert.Null(MainForm.ClearStartOfAbandonedRound(null, Clear, (_, _) => { }));
        Assert.Equal(0, cleared);

        Assert.Null(MainForm.ClearStartOfAbandonedRound("heat-3", Clear, (_, _) => { }));
        Assert.Equal(1, cleared);
    }

    [Fact]
    public void Race_Control_tells_the_host_before_capturing_and_puts_the_stamp_back_before_the_race()
    {
        var body = Slice(Source("RaceControlControl.StartSequence.cs"),
            "private void CaptureAbandonedRaceUndo(", "private sealed class AbandonedRaceUndo");

        var asks = body.IndexOf("RaceAbandoned?.Invoke()", StringComparison.Ordinal);
        var snapshot = body.IndexOf("_race.CreateSnapshot()", StringComparison.Ordinal);
        var putBack = body.IndexOf("putBackHostSchedule();", StringComparison.Ordinal);
        var restore = body.IndexOf("_race.RestoreSnapshotAfterGap(", StringComparison.Ordinal);

        Assert.True(asks >= 0 && asks < snapshot, "The host is no longer told before the abandon is captured.");
        Assert.True(putBack >= 0 && putBack < restore,
            "The run sheet's stamp must be back before the race is restored, or its Racing handler stamps it with now.");
    }

    [Fact]
    public void The_host_wires_the_abandon_to_the_run_sheet_and_tracks_the_round_at_each_green()
    {
        var audio = Source("MainForm.Audio.cs");

        Assert.Contains("_raceControlControl.RaceAbandoned = () =>", audio);
        Assert.Contains("_daySchedule.ClearStarted, _daySchedule.MarkStarted", audio);
        Assert.Contains("_roundRacingId = !_race.IsPracticeMode && !_race.IsDemoMode ? _activeRound?.Id : null;", audio);
        Assert.Contains("WireRaceAbandonedSchedule();", Source("MainForm.Pages.cs"));
    }

    // ------------------------------------------------------------ F228

    [Fact]
    public void A_heat_of_a_closed_day_is_refused_with_the_days_name()
    {
        var closed = new RaceEvent { Name = "Thursday Club Night", FinishedUtc = new DateTime(2026, 11, 16, 23, 0, 0, DateTimeKind.Utc) };

        var refusal = MainForm.ClosedDayRefusal(closed);

        Assert.NotNull(refusal);
        Assert.Contains("\"Thursday Club Night\" is closed", refusal);
        Assert.Contains("reopen it on Race Day", refusal);
    }

    [Fact]
    public void A_heat_of_an_open_day_the_legacy_holder_or_no_day_is_not_refused()
    {
        Assert.Null(MainForm.ClosedDayRefusal(new RaceEvent { Name = "Tonight" }));
        Assert.Null(MainForm.ClosedDayRefusal(new RaceEvent { IsLegacy = true, FinishedUtc = DateTime.UtcNow }));
        Assert.Null(MainForm.ClosedDayRefusal(null));
    }

    [Fact]
    public void Starting_a_round_refuses_a_closed_day_before_anything_is_touched()
    {
        var body = Slice(Source("MainForm.RaceSetup.cs"), "private string? StartRoundNow(", "private bool ApplyPhoneCheckIn(");

        var refuses = body.IndexOf("ClosedDayRefusal(_raceEvents.EventForRound(round.Id))", StringComparison.Ordinal);
        var demo = body.IndexOf("StopDemoForTakeover(", StringComparison.Ordinal);
        var apply = body.IndexOf("ApplyRoundToSessionSetup(round);", StringComparison.Ordinal);

        Assert.True(refuses >= 0, "StartRoundNow no longer refuses a closed day's heat.");
        Assert.True(refuses < demo && refuses < apply, "The closed-day refusal must come before the demo is ended or the round applied.");
    }

    [Fact]
    public void The_Round_box_lists_no_programme_rounds_on_a_closed_day()
    {
        var body = Slice(Source("MainForm.RaceSetup.cs"), "private void RefreshActiveRoundCombo(string? selectRoundId)", "private void CmbActiveRound_SelectedIndexChanged(");

        Assert.Contains("var closedDay = CheckInStore.ClosedCurrentDay(_raceEvents);", body);
        Assert.Contains("if (closedDay is null)", body);
    }

    // ------------------------------------------------------------ F254

    [Theory]
    [InlineData(RaceState.Idle, FlagState.None, null)]
    [InlineData(RaceState.Idle, FlagState.WarmUp, "heat-2")]
    [InlineData(RaceState.Staging, FlagState.None, "heat-2")]
    [InlineData(RaceState.Racing, FlagState.Green, "heat-2")]
    [InlineData(RaceState.Finished, FlagState.WarmUp, "heat-2")]
    public void A_heat_warming_up_counts_as_on_track(RaceState state, FlagState flag, string? expected)
    {
        Assert.Equal(expected, MainForm.RoundOnTrack(state, flag, "heat-2"));
    }

    [Fact]
    public void The_Program_tab_and_the_race_day_deleter_both_ask_RoundOnTrack()
    {
        var pages = Source("MainForm.Pages.cs");

        Assert.Contains("raceProgram.RoundOnTrackId = () => RoundOnTrack(_race.State, _race.Flag, _activeRound?.Id);", pages);
        Assert.Contains("RaceDayDeleter.RoundOnTrackId = () => RoundOnTrack(_race.State, _race.Flag, _activeRound?.Id);", pages);
    }

    // ------------------------------------------------------------ F017

    [Fact]
    public void The_session_crowd_is_given_the_races_mood_before_it_opens()
    {
        var body = Slice(Source("MainForm.Audio.cs"), "private void StartSessionAudio()", "private void StopMusicAndCrowd(");

        var mood = body.IndexOf("_ambience.SetOpeningMood(AmbienceMoodFor(_race.State, _race.Flag));", StringComparison.Ordinal);
        var start = body.IndexOf("_ambience.Start(ignoreEnabledSetting: true);", StringComparison.Ordinal);

        Assert.True(mood >= 0 && mood < start, "The crowd must be given the race's mood before it is started.");
    }

    [Fact]
    public void The_unmute_gives_the_crowd_the_races_mood_only_when_it_was_missed()
    {
        var body = Slice(Source("MainForm.Audio.cs"), "private void SetAllSoundMuted(bool muted)", "UpdateMuteButtonText();");

        var missed = body.IndexOf("if (_ambienceMoodMissed)", StringComparison.Ordinal);
        var mood = body.IndexOf("_ambience.SetOpeningMood(AmbienceMoodFor(_race.State, _race.Flag));", StringComparison.Ordinal);
        var start = body.IndexOf("_ambience.Start(ignoreEnabledSetting: true);", StringComparison.Ordinal);

        Assert.True(missed >= 0 && missed < mood && mood < start);
    }

    // ------------------------------------------------------------ F051

    [Fact]
    public void A_failed_update_is_told_in_a_window()
    {
        var body = Slice(Source("MainForm.cs"), "AppUpdates.TellInstallFailed = words => TryAsk(", "AppUpdates.BackUp = () =>");

        Assert.Contains("AppDialog.Show(this, words, \"Update not installed\", MessageBoxButtons.OK, MessageBoxIcon.Warning);", body);
        Assert.Contains("whenGone: false", body);
    }

    // ------------------------------------------------------------ F058

    [Fact]
    public void A_backup_kept_on_this_PC_is_told_apart_from_the_folder_asked_for()
    {
        var asked = Path.Combine(Path.GetTempPath(), "HeatCaller Backups");
        var local = Path.Combine(Path.GetTempPath(), "HeatCaller Backups (this PC only)");

        Assert.True(MainForm.BackupWentElsewhere(asked, local));
    }

    [Fact]
    public void A_trailing_backslash_or_a_change_of_case_is_the_same_folder()
    {
        var asked = Path.Combine(Path.GetTempPath(), "HeatCaller Backups");

        Assert.False(MainForm.BackupWentElsewhere(asked + Path.DirectorySeparatorChar, asked));
        Assert.False(MainForm.BackupWentElsewhere(asked.ToUpperInvariant(), asked));
    }

    [Fact]
    public void The_automatic_backup_prunes_where_the_backup_went_with_the_clubs_keep()
    {
        var body = Slice(Source("MainForm.cs"), "private void CheckAutoBackup()", "internal static bool BackupWentElsewhere(");

        Assert.Contains("var wentTo = Path.GetDirectoryName(path) ?? folder;", body);
        Assert.Contains("BackupService.PruneOldBackups(wentTo, keep);", body);
        Assert.DoesNotContain("BackupService.PruneOldBackups(folder, keep);", body);
        Assert.Contains("until a backup password is set", body);
    }

    // ------------------------------------------------- F119, F124, F129

    [Fact]
    public void Forget_republishes_the_older_transponders_it_renamed_results_on()
    {
        Assert.Contains("profile.AllTransponderIds.Concat(outcome.AlsoRepublish).ToList()", Source("MainForm.Forget.cs"));
    }

    [Fact]
    public void Start_up_sweeps_the_retired_driver_photos()
    {
        var body = Slice(Source("MainForm.Pages.cs"), "private void WireUndoButton()", "_undo.UndoFailed +=");

        Assert.Contains("UndoHoldingFolder.Prune();", body);
        Assert.Contains("DriverPhotos.PruneRetiredNow();", body);
    }

    [Fact]
    public void The_driver_card_is_given_the_garage()
    {
        var body = Slice(Source("MainForm.Pages.cs"), "var driverStatsSources = new DriverStatsSources", "var driverGarage = new DriverGarageSources");

        Assert.Contains("Cars = _cars,", body);
    }

    // ------------------------------------------------------- F175, F202

    [Fact]
    public void The_mixer_can_ask_the_race_before_its_level_check_speaks()
    {
        Assert.Contains("mixerControl.RaceStateNow = () => _race.State;", Source("MainForm.Pages.cs"));
    }

    [Fact]
    public void Driver_Hub_is_redrawn_after_phone_and_race_day_manager_check_ins()
    {
        Assert.Contains("_driverHubCheckInsRefresh = driverProfiles.RefreshCheckIns;", Source("MainForm.Pages.cs"));

        var phone = Slice(Source("MainForm.RaceSetup.cs"), "private bool ApplyPhoneCheckIn(", "private void SetCheckedInFromWizard(");
        Assert.Contains("RefreshDriverHubCheckIns();", phone);

        var relay = Source("MainForm.Relay.cs");
        var marks = relay.IndexOf("_checkInControl?.RefreshCheckInMarks();", StringComparison.Ordinal);
        var hub = relay.IndexOf("RefreshDriverHubCheckIns();", StringComparison.Ordinal);
        Assert.True(marks >= 0 && hub > marks, "A relay phone check-in no longer redraws Driver Hub.");
    }

    // ------------------------------------------------- F269, F278, F287

    [Fact]
    public void Mission_Control_looks_a_grid_transponder_up_the_way_the_watch_files_it()
    {
        Assert.Contains("_transponderWatch.Of(_profiles.PrimaryForAlternate(t) ?? t)", Source("MainForm.MissionControl.cs"));
    }

    [Fact]
    public void A_too_big_garage_is_named_on_the_console()
    {
        var source = Source("MainForm.DriverGarage.cs");

        Assert.Contains("!RelayPublisher.GarageFits(json) && _garageTooBigSaid.Add(badge)", source);
        Assert.Contains("$\"{name}'s garage\"", source);
        Assert.Contains("_garageOwner[badge] = profile.DriverName;", source);
    }

    [Fact]
    public void A_held_standby_address_sends_no_address_and_no_PIN()
    {
        var body = Slice(Source("MainForm.Standby.cs"), "private StandbyMirror.Target StandbyTarget()", "private void ApplyStandbySettings()");

        var held = body.IndexOf("RestoredSettings.StandbyHeldProblem() is { } held", StringComparison.Ordinal);
        var pin = body.IndexOf("_settings.StandbyPin", StringComparison.Ordinal);

        Assert.True(held >= 0 && held < pin, "The held check must come before the PIN is read.");
        Assert.Contains("new StandbyMirror.Target(null, string.Empty, held)", body);
    }

    // ------------------------------------------------------------ Recording

    [Fact]
    public void The_Recording_page_asks_the_announcer_whether_the_piece_was_saved()
    {
        Assert.Contains("recording.PieceWasSaved = () => announcer.LastPieceWasSaved;", Source("MainForm.Commentary.cs"));
    }
}
