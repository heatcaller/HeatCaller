namespace HeatCaller.Tests;

/// <summary>
/// The rest of the voice speed plan's phases 2 and 3 (10 Nov 2026): shorter
/// wording when the track is busy, the winner named on the line, lost
/// personal bests said together, and streamed race calls.
/// </summary>
public sealed class VoiceSpeedPhase2And3Tests
{
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

    // ---------------------------------------------------------------- busy wording

    [Fact]
    public void Busy_wording_keeps_the_shorter_half_as_said()
    {
        var templates = new[]
        {
            "[cheerful] {driver} keeps improving - {time}.",
            "[cheerful] New personal best for {driver}. Smooth.",
            "[excited] {driver}!",
            "[cheerful] A personal best, smooth as chrome - {driver}, what a lap that was from start to finish.",
        };

        var shorter = BusyWording.ShorterHalf(templates);

        Assert.Equal(2, shorter.Count);
        Assert.Contains("[excited] {driver}!", shorter);
        Assert.DoesNotContain(templates[3], shorter);
    }

    [Fact]
    public void Busy_wording_never_empties_a_list()
    {
        Assert.Single(BusyWording.ShorterHalf(new[] { "Only one." }));
        Assert.Single(BusyWording.ShorterHalf(new[] { "Short.", "A good deal longer than that." }));
        Assert.Equal(2, BusyWording.ShorterHalf(new[] { "a", "bb", "ccc" }).Count);
    }

    [Fact]
    public void A_template_is_measured_as_it_will_be_said()
    {
        // Tags count for nothing; a time is a long phrase, a name a dozen letters.
        Assert.Equal(BusyWording.SpokenLength("Go!"), BusyWording.SpokenLength("[excited] Go!"));
        Assert.True(BusyWording.SpokenLength("{time}") > BusyWording.SpokenLength("{driver}"));
        Assert.True(BusyWording.SpokenLength("{driver}") > BusyWording.SpokenLength("{lap}"));
    }

    [Fact]
    public void It_is_busy_with_a_line_on_or_waiting_or_calls_coming_quickly()
    {
        Assert.True(BusyWording.IsBusy(speaking: true, waiting: false, recentCalls: 0));
        Assert.True(BusyWording.IsBusy(speaking: false, waiting: true, recentCalls: 0));
        Assert.True(BusyWording.IsBusy(speaking: false, waiting: false, recentCalls: BusyWording.CallsInWindow));
        Assert.False(BusyWording.IsBusy(speaking: false, waiting: false, recentCalls: BusyWording.CallsInWindow - 1));
    }

    [Fact]
    public void Busy_wording_spares_instructions_and_drops_the_optional_tail()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        Assert.Contains("var busy = !IsPriorityAnnouncement(type) && TrackIsBusy();", queue);
        Assert.Contains("candidates = BusyWording.ShorterHalf(candidates);", queue);
        Assert.Contains("autoTail = null;", queue);
    }

    // ---------------------------------------------------------------- the winner

    [Fact]
    public void The_winner_s_line_names_them_from_the_club_s_own_lines()
    {
        var defaults = VoiceProfile.CreateDefault().Events[nameof(AnnouncementEventType.WinnerCalled)].Templates;
        var line = AnnouncementService.WinnerLine(defaults, "Al gorerhythm");

        Assert.Contains("Al gorerhythm", line);
        Assert.DoesNotContain("{winner}", line);
        Assert.All(defaults, template => Assert.Contains("{winner}", template));

        // {driver} works too, as it does in every other call.
        Assert.Equal("Al wins!", AnnouncementService.WinnerLine(new[] { "{driver} wins!" }, "Al"));
    }

    [Fact]
    public void The_new_calls_have_rows_beside_the_calls_they_follow()
    {
        var rows = VoiceSettingsControl.AnnouncementTypes().ToList();

        Assert.Equal(rows.IndexOf(AnnouncementEventType.RaceCompleted) + 1, rows.IndexOf(AnnouncementEventType.WinnerCalled));
        Assert.Equal(rows.IndexOf(AnnouncementEventType.BestEverHere) + 1, rows.IndexOf(AnnouncementEventType.PersonalBestsTogether));
        Assert.Single(rows, t => t == AnnouncementEventType.WinnerCalled);
        Assert.Single(rows, t => t == AnnouncementEventType.PersonalBestsTogether);
    }

    [Fact]
    public void The_new_calls_have_default_lines_and_a_label()
    {
        var defaults = VoiceProfile.CreateDefault();

        foreach (var type in new[] { AnnouncementEventType.WinnerCalled, AnnouncementEventType.PersonalBestsTogether, AnnouncementEventType.BestEverHere })
        {
            Assert.True(defaults.Events[type.ToString()].Enabled);
            Assert.NotEmpty(defaults.Events[type.ToString()].Templates);
            Assert.NotEqual(type.ToString(), VoiceProfile.Describe(type).Label);
        }

        Assert.All(defaults.Events[nameof(AnnouncementEventType.PersonalBestsTogether)].Templates,
            template => Assert.Contains("{drivers}", template));
    }

    [Fact]
    public void Applying_an_older_style_keeps_the_newer_calls_as_this_PC_has_them()
    {
        var store = Source("ThemeStore.cs");
        var stamp = store.IndexOf("voiceCopy.TemplateVersion = VoiceSettingsStore.CurrentTemplateVersion;", StringComparison.Ordinal);
        var save = store.IndexOf("voiceStore.Save(voiceCopy);", stamp, StringComparison.Ordinal);

        Assert.Contains("foreach (var (key, config) in current.Events)", store[stamp..save]);
    }

    [Fact]
    public void The_winner_s_call_is_made_ready_on_the_last_lap_and_when_time_is_up()
    {
        var events = Source("AnnouncementService.RaceEvents.cs");

        var lastLap = events.IndexOf("public void NotifyLastLap(string transponderId)", StringComparison.Ordinal);
        var timeOver = events.IndexOf("public void NotifyRaceTimeOver()", StringComparison.Ordinal);
        Assert.Contains("PrepareTheWinnerCalls();", events[lastLap..(lastLap + 500)]);
        Assert.Contains("PrepareTheWinnerCalls();", events[timeOver..(timeOver + 800)]);

        var completed = events.IndexOf("public void NotifyRaceCompleted()", StringComparison.Ordinal);
        var quiet = events.IndexOf("BeginPostRaceQuiet();", completed, StringComparison.Ordinal);
        Assert.Contains("QueueTheWinnersCall();", events[completed..quiet]);
    }

    [Fact]
    public void The_winner_s_call_goes_first_and_survives_the_finish_clearing_the_decks()
    {
        var winner = Source("AnnouncementService.Winner.cs");

        Assert.Contains("_voiceQueue.Insert(0, queued);", winner);
        Assert.Contains("new QueuedVoiceItem(AnnouncementEventType.RaceCompleted,", winner);
        Assert.True(AnnouncementService.SurvivesAFlag(AnnouncementEventType.RaceCompleted));

        // Its render is handed over only as it is said, after the finish's clear-out.
        Assert.Contains("OfferRenderedAhead(key, render);", winner);
    }

    [Fact]
    public void The_winner_s_call_goes_with_the_finish_call()
    {
        var winner = Source("AnnouncementService.Winner.cs");

        // Both rows on: Race completed and Winner named.
        Assert.Contains("events.GetValueOrDefault(nameof(AnnouncementEventType.RaceCompleted)) is { } finish && CallOn(finish)", winner);
        Assert.Contains("events.GetValueOrDefault(nameof(AnnouncementEventType.WinnerCalled)) is { } winner && CallOn(winner)", winner);
        Assert.Contains("if (winner is null || IsPracticeMode || Muted || WinnerCallConfig() is not { } config)", winner);
        // Made ahead from the finish's own {winner} lines, or else the Winner named row (11 Nov 2026).
        Assert.Contains("if (IsPracticeMode || Muted || WinnerLines() is not { } lines", winner);
        Assert.Contains("return WinnerCallConfig() is { } winner ? (winner, winner.EffectiveTemplates, false) : null;", winner);
    }

    // ---------------------------------------------------------------- lost personal bests

    [Fact]
    public void Two_or_more_lost_personal_bests_still_inside_their_moment_are_said_together()
    {
        var lost = new[]
        {
            ("dee", 10_000L, "Dee Ploy", "Dee Ploy"),
            ("al", 9_000L, "Al gorerhythm", "Al Gorithim"),
            ("ana", 4_000L, "Annie Wayout", "Annie Wayout"),
        };

        var together = AnnouncementService.StillWorthCombining(lost, nowMs: 5_000);

        Assert.Equal(new[] { "al", "dee" }, together.Select(t => t.Item1));
    }

    [Fact]
    public void One_lost_personal_best_is_no_combined_call()
    {
        var lost = new[] { ("dee", 10_000L, "Dee Ploy", "Dee Ploy"), ("al", 1_000L, "Al", "Al") };

        Assert.Empty(AnnouncementService.StillWorthCombining(lost, nowMs: 5_000));
    }

    [Fact]
    public void A_driver_lost_twice_is_named_once_and_three_is_the_most()
    {
        var lost = new[]
        {
            // Dee's newer lap is the one kept - and it is the most pressing.
            ("dee", 9_500L, "Dee", "Dee"), ("dee", 8_000L, "Dee", "Dee"),
            ("al", 9_100L, "Al", "Al"), ("ana", 9_200L, "Ana", "Ana"), ("bo", 9_300L, "Bo", "Bo"),
        };

        var together = AnnouncementService.StillWorthCombining(lost, nowMs: 0);

        Assert.Equal(AnnouncementService.MostCombined, together.Count);
        Assert.Single(together, t => t.Item1 == "dee");
    }

    [Fact]
    public void Names_are_listed_the_way_they_are_said()
    {
        Assert.Equal("Dee", AnnouncementService.TheirNames(new[] { "Dee" }));
        Assert.Equal("Dee and Al", AnnouncementService.TheirNames(new[] { "Dee", "Al" }));
        Assert.Equal("Dee, Al and Ana", AnnouncementService.TheirNames(new[] { "Dee", "Al", "Ana" }));
    }

    [Fact]
    public void A_personal_best_lost_behind_a_line_is_remembered_and_offered_as_the_line_ends()
    {
        var density = Source("AnnouncementService.Density.cs");
        var still = density.IndexOf("still speaking the previous one.\");", StringComparison.Ordinal);
        Assert.Contains("NoteLostPersonalBest(transponder);", density[still..(still + 500)]);

        var queue = Source("AnnouncementService.VoiceQueue.cs");
        var held = queue.IndexOf("OfferTheHeldOverCall();", StringComparison.Ordinal);
        var combined = queue.IndexOf("OfferTheCombinedPersonalBests();", StringComparison.Ordinal);
        Assert.True(held > 0 && combined > held);
    }

    // ---------------------------------------------------------------- fastest ever here (phase 4)

    private static SessionRecord Race(string layout, params (string Transponder, long FastLapMs)[] rows) => new()
    {
        TrackLayoutName = layout,
        Results = rows.Select(r => new SessionResultEntry { Transponder = r.Transponder, FastLapMs = r.FastLapMs }).ToList(),
    };

    [Fact]
    public void A_driver_s_best_here_is_their_quickest_lap_on_this_layout_only()
    {
        var history = new[]
        {
            Race("Home Track", ("dee", 7_400), ("al", 7_100)),
            Race("home track", ("dee", 7_250)),
            Race("Away Track", ("dee", 6_000)),
            Race("Home Track", ("dee", 0)),
        };

        Assert.Equal(7_250, BestEverHere.PreviousBest(history, "Home Track", "dee"));
        Assert.Equal(7_100, BestEverHere.PreviousBest(history, "Home Track", "AL"));
    }

    [Fact]
    public void A_driver_new_to_the_layout_has_no_best_to_beat()
    {
        var history = new[] { Race("Away Track", ("dee", 6_000)) };

        Assert.Null(BestEverHere.PreviousBest(history, "Home Track", "dee"));
        Assert.Null(BestEverHere.PreviousBest(history, "", "dee"));
    }

    [Fact]
    public void Fastest_ever_here_is_asked_before_the_personal_best_and_replaces_it()
    {
        var events = Source("AnnouncementService.RaceEvents.cs");
        var notify = events.IndexOf("public void NotifyPersonalBestLap(string transponderId, long lapMs)", StringComparison.Ordinal);
        var ever = events.IndexOf("if (CallTheBestEverHere(transponderId, standing, lapMs))", notify, StringComparison.Ordinal);
        var personal = events.IndexOf("EnqueueVoice(AnnouncementEventType.PersonalBestLap, standing, null, lapMs);", notify, StringComparison.Ordinal);

        Assert.True(ever > notify && personal > ever);

        // And Race Control answers it from the saved races.
        Assert.Contains("_announcer.BestEverHereBefore = BestEverHereBefore;", Source("RaceControlControl.RaceEvents.cs"));
        Assert.Contains("_race.IsDemoMode", Source("RaceControlControl.BestEverHere.cs"));
    }

    [Fact]
    public void Fastest_ever_here_has_its_row_lines_and_a_lap_s_few_seconds()
    {
        var rows = VoiceSettingsControl.AnnouncementTypes().ToList();
        Assert.Equal(rows.IndexOf(AnnouncementEventType.PersonalBestLap) + 1, rows.IndexOf(AnnouncementEventType.BestEverHere));

        var config = VoiceProfile.CreateDefault().Events[nameof(AnnouncementEventType.BestEverHere)];
        Assert.True(config.Enabled);
        Assert.All(config.Templates, template => Assert.Contains("{driver}", template));

        Assert.True(AnnouncementService.IsLapBoundCall(AnnouncementEventType.BestEverHere));
        Assert.True(AnnouncementService.IsLapBoundCall(AnnouncementEventType.PersonalBestsTogether));
    }

    // ---------------------------------------------------------------- streaming

    [Fact]
    public void A_stream_plays_what_has_arrived_then_silence_until_it_is_complete()
    {
        var line = new StreamingVoiceLine(gain: 1f, leadingSilenceSeconds: 0);
        line.Add(new byte[] { 0x00, 0x40, 0x00, 0xC0 }); // +0.5, -0.5

        var buffer = new float[4];

        // Not complete: reads in full (silence after the two samples) so the bus keeps it.
        Assert.Equal(4, line.Read(buffer, 0, 4));
        Assert.Equal(0.5f, buffer[0], 3);
        Assert.Equal(-0.5f, buffer[1], 3);
        Assert.Equal(0f, buffer[2]);
        Assert.Equal(1, line.Underruns);

        // Complete and drained: reads short, and leaves the mix.
        line.Complete(trailingSilenceSeconds: 0);
        Assert.Equal(0, line.Read(buffer, 0, 4));
    }

    [Fact]
    public void A_sample_split_across_two_pieces_is_put_back_together()
    {
        var line = new StreamingVoiceLine(gain: 1f, leadingSilenceSeconds: 0);
        line.Add(new byte[] { 0x00 });
        line.Add(new byte[] { 0x40 });
        line.Complete(0);

        var buffer = new float[2];

        Assert.Equal(1, line.Read(buffer, 0, 2));
        Assert.Equal(0.5f, buffer[0], 3);
    }

    [Fact]
    public void A_stream_has_its_silences_and_its_gain()
    {
        var line = new StreamingVoiceLine(gain: 0.5f, leadingSilenceSeconds: 0.01);
        line.Add(new byte[] { 0x00, 0x40 });
        line.Complete(trailingSilenceSeconds: 0.01);

        var lead = (int)(StreamingVoiceLine.Format.SampleRate * 0.01);
        var buffer = new float[lead * 3];
        var read = line.Read(buffer, 0, buffer.Length);

        Assert.Equal(lead + 1 + lead, read);
        Assert.Equal(0f, buffer[0]);
        Assert.Equal(0.25f, buffer[lead], 3);
        Assert.Equal(TimeSpan.FromSeconds(1.0 / StreamingVoiceLine.Format.SampleRate), line.SpokenFor);
    }

    [Fact]
    public void A_voice_is_streamed_only_once_its_level_is_known()
    {
        VoiceLevelMemory.Forget();
        var profile = new VoiceProfile { Engine = VoiceEngine.ElevenLabs, VoiceId = "level-test", ModelId = "eleven_v4_turbo" };

        Assert.Null(VoiceLevelMemory.GainFor(profile));

        VoiceLevelMemory.Learn(profile, VoiceLevel.TargetDb - 6);
        var gain = VoiceLevelMemory.GainFor(profile)!.Value;
        Assert.InRange(gain, 1.9f, 2.1f);

        // Never more than MostGainDb either way, whatever one clip measured.
        VoiceLevelMemory.Forget();
        VoiceLevelMemory.Learn(profile, VoiceLevel.TargetDb - 60);
        Assert.InRange(VoiceLevelMemory.GainFor(profile)!.Value, 0f, (float)Math.Pow(10, VoiceLevelMemory.MostGainDb / 20) + 0.01f);

        VoiceLevelMemory.Forget();
    }

    [Fact]
    public void Streaming_is_off_unless_the_club_ticks_it_and_falls_back_to_whole()
    {
        Assert.False(new AppSettings().StreamLiveCalls);

        var streaming = Source("AnnouncementService.Streaming.cs");
        Assert.Contains("if (StreamLiveCalls?.Invoke() != true", streaming);
        Assert.Contains("|| voice is not null", streaming);
        Assert.Contains("VoiceLevelMemory.GainFor(profile) is not { } learned", streaming);

        var queue = Source("AnnouncementService.VoiceQueue.cs");
        Assert.Contains("if (WillStream(currentProfile, voice, text, out var streamGain)\n            && await TryStreamAndPlayAsync(", queue);
    }

    [Fact]
    public void A_streamed_line_is_checked_before_a_word_is_heard()
    {
        var streaming = Source("AnnouncementService.Streaming.cs");
        var lockAt = streaming.IndexOf("await _audioPlaybackLock.WaitAsync();", StringComparison.Ordinal);
        var playAt = streaming.IndexOf("await PlayStreamAsync(", StringComparison.Ordinal);

        foreach (var check in new[] { "IsSupersededByInterrupt()", "IsCutByCountdown()", "IsOvertakenOnTrack()", "WhyDequeuedLineIsStale()", "whyNotNow?.Invoke()", "Muted" })
        {
            var at = streaming.IndexOf(check, lockAt, StringComparison.Ordinal);
            Assert.True(at > lockAt && at < playAt, check);
        }
    }

    [Fact]
    public void The_stream_endpoint_is_asked_for_raw_PCM()
    {
        var client = Source("ElevenLabsClient.cs");

        Assert.Contains("url += $\"/stream?output_format={StreamingVoiceLine.ElevenLabsFormat}\";", client);
        Assert.Equal("pcm_22050", StreamingVoiceLine.ElevenLabsFormat);
        Assert.Equal(22_050, StreamingVoiceLine.Format.SampleRate);
    }
}
