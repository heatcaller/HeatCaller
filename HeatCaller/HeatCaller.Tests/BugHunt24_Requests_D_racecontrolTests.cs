namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, follow-up requests (bundle D_racecontrol): changes other
/// groups' fixes needed in Race Control, the announcer and the pages beside
/// them to reach the director. The lead change tally put back by an undo
/// (F025) and the Sound Effects page's failed-write wording are pinned as
/// behaviour; the wiring that can't be driven without a window or a sound
/// card is pinned in the source, in the style of the other bug hunt tests.
/// </summary>
public sealed class BugHunt24_Requests_D_racecontrolTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- F025: the lead change count survives an undone Stage or Reset -------------

    private static RaceStanding Car(string transponder, int laps, long elapsedMs) => new()
    {
        Transponder = transponder,
        Id = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        Status = "active",
    };

    private static AnnouncementService Announcer()
    {
        var profiles = ProfileStore.Load();
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

        return new AnnouncementService(profiles, voice, sounds);
    }

    [Fact]
    public void BugHunt24_A_race_put_back_with_its_tally_keeps_its_lead_change_count()
    {
        var announcer = Announcer();
        var before = new Dictionary<string, int> { ["101"] = 2, ["102"] = 2, ["103"] = 1 };

        announcer.SeedStandings([Car("102", 12, 120_000), Car("101", 12, 121_000)], (4, before));

        Assert.Equal("102", announcer.Leads.Leader);
        Assert.Equal(4, announcer.Leads.Changes);
        Assert.Equal(3, announcer.Leads.Leaders);

        // The next change is the race's fifth, not its first.
        var next = announcer.Leads.Observe("101");

        Assert.NotNull(next);
        Assert.Equal(5, next!.Number);
        Assert.Equal(3, next.TimesLed);
    }

    [Fact]
    public void BugHunt24_A_race_put_back_without_a_tally_keeps_the_count_as_it_was()
    {
        var announcer = Announcer();

        announcer.SeedStandings([Car("102", 12, 120_000), Car("101", 11, 121_000)]);

        Assert.Equal("102", announcer.Leads.Leader);
        Assert.Equal(0, announcer.Leads.Changes);
    }

    [Fact]
    public void BugHunt24_Each_undo_of_a_running_race_takes_the_lead_tally_and_hands_it_back()
    {
        foreach (var file in new[] { "RaceControlControl.StartSequence.cs", "RaceControlControl.cs", "RaceControlControl.Layout.cs" })
        {
            var source = Source(file);
            var taken = source.IndexOf("var leadsBefore = _announcer.Leads.Snapshot();", StringComparison.Ordinal);
            var given = source.IndexOf("SeedAnnouncerFromRace(leadsBefore);", StringComparison.Ordinal);

            Assert.True(taken >= 0, $"{file} no longer takes the lead change tally with the undo snapshot.");
            Assert.True(given > taken, $"{file} no longer hands the lead change tally back to the announcer.");
        }

        Assert.Contains("_announcer.SeedStandings(_race.GetOrderedStandings()", Source("RaceControlControl.RaceEvents.cs"));
        Assert.Contains(".ToList(), leads);", Source("RaceControlControl.RaceEvents.cs"));
    }

    // --- Sound Effects: a write that failed does not say Saved ---------------------

    [Fact]
    public void BugHunt24_A_sounds_save_that_could_not_be_written_says_so()
    {
        var line = SoundEffectsControl.SavedStatus(notWritten: false, "19:02:11", races: 9, practice: 7, writeFailed: true);

        Assert.StartsWith("Not saved", line);
        Assert.Contains("couldn't write the sounds file (sound-effects.json)", line);
        Assert.Contains("disk has room", line);
        Assert.DoesNotContain("Saved 19:02:11", line);
    }

    [Fact]
    public void BugHunt24_A_sounds_file_unreadable_at_startup_keeps_its_own_wording()
    {
        var line = SoundEffectsControl.SavedStatus(notWritten: true, "19:02:11", races: 9, practice: 7, writeFailed: true);

        Assert.Contains("couldn't be read when HeatCaller started", line);
    }

    [Fact]
    public void BugHunt24_The_sound_effects_page_reads_what_the_save_answered()
    {
        var source = Source("SoundEffectsControl.cs");

        Assert.Contains("var written = _store.Save(profile);", source);
        Assert.Contains("writeFailed: !written", source);
    }

    // --- Pacing: the time since the last call reads shorter than the gap ---------

    [Fact]
    public void BugHunt24_A_held_line_gives_the_time_since_the_last_call_rounded_down()
    {
        var source = Source("AnnouncementService.Density.cs");

        Assert.DoesNotContain("AnnouncerDensity.Gap(since.TotalSeconds)", source);
        Assert.Equal(2, Count(source, "AnnouncerDensity.GapSince(since.TotalSeconds, gap)"));
    }

    // --- Lap beeps: a ghost that is not announced does not beep -----------------

    [Fact]
    public void BugHunt24_A_ghost_that_is_not_announced_plays_no_lap_beep()
    {
        var source = Source("AnnouncementService.LapCalls.cs");
        var play = source.IndexOf("ToneGenerator.PlayDriverLapAsync(toneIndex, beepVolume)", StringComparison.Ordinal);

        Assert.True(play >= 0, "The driver's lap beep is no longer played from the lap calls.");

        var condition = source[source.LastIndexOf("if (", play, StringComparison.Ordinal)..play];

        Assert.Contains("!MutedGhost(transponder)", condition);
        Assert.Contains("LapBeeps?.ToneFor(transponder)", condition);
    }

    // --- Between races ------------------------------------------------------------

    [Fact]
    public void BugHunt24_Call_Up_after_a_skipped_call_up_is_not_worded_as_a_reminder()
    {
        var source = Source("RaceControlControl.BetweenRaces.cs");

        Assert.Contains("var repeat = Gap.CallUpSaid;", source);
        Assert.DoesNotContain("var repeat = Gap.CallUps > 0;", source);
        Assert.Contains("Gap.SaidMark(mark, now, facts.DueUtc);", source);
    }

    // --- Times on the Announcer and Big Screen ----------------------------------

    [Fact]
    public void BugHunt24_On_deck_times_are_shown_in_the_clubs_clock()
    {
        Assert.Contains("DayScheduleControl.ShownTime(next.ScheduledTime)", Source("AnnouncerControl.cs"));
        Assert.Contains("DayScheduleControl.ShownTime(nextUp.ScheduledTime)", Source("BigScreenForm.cs"));
    }

    // --- Invitational status line -----------------------------------------------

    [Fact]
    public void BugHunt24_The_invitational_status_line_names_the_main_just_run()
    {
        var source = Source("RaceControlControl.FinalsFormats.cs");

        Assert.Contains("after main {table.MainNumber} of {table.Mains}", source);
        Assert.DoesNotContain("after main {table.MainsRun} of", source);
    }

    // --- Packs: a pack removed while the list was open ---------------------------

    [Fact]
    public void BugHunt24_A_pack_removed_while_its_list_was_open_is_said_and_nothing_changes()
    {
        var hub = Source("DriverBatteriesPanel.cs");
        Assert.Contains("if (!BatteryActions.TagSession(_store, _undo, session.Id, session.Label, key, DriverName, chosen?.Id, PackTagSource.DriverHub, intoCar: false))", hub);
        Assert.Contains("\"That pack was removed while this was open, so nothing was changed.\", \"Pack removed\"", hub);

        var raceControl = Source("RaceControlControl.Packs.cs");
        Assert.Contains("if (!BatteryActions.TagSession(store, _undo, session.Id, session.Label, key, name, packId, PackTagSource.RaceControl, intoCar: true))", raceControl);
        Assert.Contains("that pack was removed while the menu was open, so nothing was changed.", raceControl);
    }

    // --- The sponsor's picture comes down when the break's read is over ----------

    [Fact]
    public void BugHunt24_The_breaks_sponsor_read_takes_its_picture_down_when_it_is_over()
    {
        var source = Source("RaceControlControl.AfterRace.cs");
        var start = source.IndexOf("private async Task ReadTheSponsorsInTheBreakAsync(", StringComparison.Ordinal);
        var end = source.IndexOf("// ==================== Broadcast imaging", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start);

        var method = source[start..end];

        Assert.Contains("stillSpeaking = !await _announcer.WaitForAudioAsync(SpokenWait(), nextRace);", method);
        Assert.Contains("if (putPictureUp && !stillSpeaking)", method);
        Assert.Contains("SponsorOnAir.End();", method);
    }

    // --- The record watch forgets a lap taken off ---------------------------------

    [Fact]
    public void BugHunt24_A_lap_taken_off_is_forgotten_by_the_record_watch_too()
    {
        var source = Source("AnnouncerControl.Director.cs");
        var director = source.IndexOf("director.ForgetLaps(standing.TransponderId, surviving);", StringComparison.Ordinal);
        var watch = source.IndexOf("watch.Forget(standing.TransponderId, surviving);", StringComparison.Ordinal);

        Assert.True(director >= 0 && watch > director);
    }

    // --- The recorded pieces say whether they were saved ------------------------

    [Fact]
    public void BugHunt24_Each_recorded_piece_reports_whether_its_recorder_started()
    {
        var source = Source("AnnouncerControl.Director.cs");

        Assert.Contains("public bool LastPieceWasSaved { get; private set; }", source);
        Assert.Equal(3, Count(source, "LastPieceWasSaved = false;"));
        Assert.Equal(3, Count(source, "LastPieceWasSaved = recording is not null;"));
    }

    // --- F087: the intro and the recap say how many nights their history covers --

    [Fact]
    public void BugHunt24_The_intro_and_the_recap_histories_carry_their_nights_on_file()
    {
        var source = Source("RaceControlControl.AfterRace.cs");

        Assert.Contains("NightsOnFile = CommentaryReaders.NightsOnFile(round?.ClassName, _sessionHistory, _rounds, exceptSessionId: record.Id),", source);
        Assert.Contains("NightsOnFile = CommentaryReaders.NightsOnFile(round.ClassName, _sessionHistory, _rounds),", source);
    }

    // --- F304: the stream's sponsor logo time survives a restart ----------------

    [Fact]
    public void BugHunt24_The_stream_sponsor_time_is_put_back_and_kept_with_the_settings()
    {
        var source = Source("AnnouncerControl.cs");
        var restored = source.IndexOf("SponsorStream.Restore(settings.SponsorStreamNight, DateTime.UtcNow);", StringComparison.Ordinal);
        var kept = source.IndexOf("settings.SponsorStreamNight = saved;", StringComparison.Ordinal);

        Assert.True(restored >= 0, "Tonight's stream sponsor time is no longer put back at startup.");
        Assert.True(kept > restored, "Tonight's stream sponsor time is no longer kept in the settings.");
        Assert.Contains("SponsorStream.Changed = saved =>", source);
    }

    // --- F083: a binned battle line hands its two cars to the director ----------

    [Fact]
    public void BugHunt24_A_battle_line_binned_before_a_word_names_its_fight_to_the_director()
    {
        Assert.Contains("Director?.BattleUnheard(pair.AheadId, pair.BehindId);", Source("AnnouncerControl.Director.cs"));
    }

    private static int Count(string source, string text)
    {
        var count = 0;
        for (var at = source.IndexOf(text, StringComparison.Ordinal); at >= 0; at = source.IndexOf(text, at + text.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }
}
