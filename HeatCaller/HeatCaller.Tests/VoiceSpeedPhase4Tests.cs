namespace HeatCaller.Tests;

/// <summary>
/// Phase 4 of the voice speed plan (11 Nov 2026): a driver's own lap cue in
/// place of the shared chime, lap times said before the first corner and in
/// turn, the pass for the lead made ready while the cars are nose to tail,
/// and the gap for the lead called each lap at the end of a race.
/// </summary>
public sealed class VoiceSpeedPhase4Tests
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

    // ---------------------------------------------------------------- 1. your lap

    [Fact]
    public void A_driver_with_their_own_lap_beep_does_not_also_get_the_shared_chime()
    {
        var laps = Source("AnnouncementService.LapCalls.cs");
        var goodLap = laps.IndexOf("var cfg = sounds.Effects.GetValueOrDefault(nameof(SoundEffectKind.GoodLap));", StringComparison.Ordinal);
        var offered = laps.IndexOf("if (SoundCandidateCanPlay(SoundEffectKind.GoodLap, cfg))", goodLap, StringComparison.Ordinal);

        Assert.True(goodLap > 0 && offered > goodLap);
        Assert.Contains("if (LapBeeps?.ToneFor(transponder) is not null)", laps[goodLap..offered]);

        // The invalid-lap buzzer is not touched.
        Assert.Contains("SoundCandidateCanPlay(SoundEffectKind.InvalidLap, cfg)", laps);
    }

    [Fact]
    public void A_lap_time_is_said_before_the_first_corner_or_not_at_all()
    {
        Assert.Equal(AnnouncementService.LapTimeSayWithinMs, AnnouncementService.SayWithinMsFor(AnnouncementEventType.LapTime));
        Assert.InRange(AnnouncementService.LapTimeSayWithinMs, 1_500, 3_500);

        // Every other lap call keeps its six seconds.
        Assert.Equal(AnnouncementService.LapCallSayWithinMs, AnnouncementService.SayWithinMsFor(AnnouncementEventType.PersonalBestLap));

        // The queue and the timing both read it per call.
        var queue = Source("AnnouncementService.VoiceQueue.cs");
        Assert.Contains("LapSeenAt(transponder) + SayWithinMsFor(type)", queue);
        Assert.Contains("lapSayBy - SayWithinMsFor(item.Type)", queue);
    }

    [Fact]
    public void Among_equal_calls_the_driver_named_longest_ago_goes_first()
    {
        var calls = new[]
        {
            (Weight: 30, Who: "dee"),
            (Weight: 30, Who: "al"),
            (Weight: 90, Who: "terry"),
            (Weight: 30, Who: "ana"),
        };
        var named = new Dictionary<string, long> { ["dee"] = 5_000, ["al"] = 1_000 };

        var order = AnnouncementService.InTurn(calls, c => c.Weight, c => c.Who, t => named.TryGetValue(t, out var at) ? at : null)
            .Select(c => c.Who)
            .ToList();

        // The big call first; then Ana (never named), Al (long ago), Dee (just now).
        Assert.Equal(new[] { "terry", "ana", "al", "dee" }, order);
    }

    [Fact]
    public void A_driver_is_noted_as_named_only_when_their_line_is_heard()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        Assert.Contains("named = item.Transponder;", queue);
        Assert.Contains("if (timing.Played)\n                        {\n                            NoteNamed(named);", queue);
    }

    // ---------------------------------------------------------------- 2. the pass

    [Fact]
    public void A_preview_says_what_the_change_would_be_and_records_nothing()
    {
        var leads = new LeadTracker();
        leads.Observe("bill");
        leads.Observe("sam");   // change 1: Sam
        leads.Observe("bill");  // change 2: Bill back

        var preview = leads.Preview("sam");

        Assert.Equal(new LeadChange("sam", "bill", 3, 2, 2), preview);
        Assert.Equal("bill", leads.Leader);
        Assert.Equal(2, leads.Changes);

        // And the real change, when it comes, is the same.
        Assert.Equal(preview, leads.Observe("sam"));
    }

    [Fact]
    public void A_preview_for_someone_new_to_the_lead_counts_them_as_a_new_leader()
    {
        var leads = new LeadTracker();
        leads.Observe("bill");

        Assert.Equal(new LeadChange("ana", "bill", 1, 2, 1), leads.Preview("ana"));
        Assert.Null(leads.Preview("bill"));
        Assert.Null(new LeadTracker().Preview("bill"));
    }

    [Fact]
    public void The_pass_is_made_ready_from_the_same_words_the_call_builds()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        // A dry run of EnqueueVoice gives the words, queues nothing, gives
        // the sponsor's ration back - before EnqueueVoiceWork is reached.
        var dry = queue.IndexOf("if (_dryRun is { } dryRun)", StringComparison.Ordinal);
        var work = queue.IndexOf("var accepted = EnqueueVoiceWork(type, transponder,", StringComparison.Ordinal);
        Assert.True(dry > 0 && work > dry);
        Assert.Contains("releaseMention?.Invoke();", queue[dry..work]);

        // The template kept for the driver is the one the real call takes.
        Assert.Contains("var chosenTemplate = KeptTemplateFor(type, transponder, candidates)", queue);
    }

    [Fact]
    public void The_lead_change_takes_the_prediction_and_its_tail()
    {
        var laps = Source("AnnouncementService.LapCalls.cs");

        Assert.Contains("var predicted = TakeThePredictedPass(leader.Transponder, leadChange);", laps);
        Assert.Contains("? predicted?.Tail ?? LeadTracker.Tail(leadChange)", laps);
        Assert.Contains("PredictTheLeadChange(ordered);", laps);
    }

    [Fact]
    public void Passes_made_ready_are_capped_and_close()
    {
        Assert.InRange(AnnouncementService.MostPredictionsPerRace, 2, 10);
        Assert.InRange(AnnouncementService.PredictWithinMs, 200, 1_000);

        var density = Source("AnnouncementService.Density.cs");
        Assert.Contains("ForgetThePredictedPasses(newRace: false);", density);
        Assert.Contains("ForgetThePredictedPasses(newRace: true);", density);
    }

    // ---------------------------------------------------------------- 5. the gap

    [Theory]
    [InlineData(16, 20, false, false)]
    [InlineData(17, 20, false, true)]
    [InlineData(19, 20, false, true)]
    [InlineData(3, null, false, false)]
    [InlineData(3, null, true, true)]
    [InlineData(3, 0, true, true)]
    public void The_countdown_runs_in_the_last_laps_or_the_final_minute(int leaderLaps, int? lapLimit, bool finalMinute, bool runs)
    {
        Assert.Equal(runs, AnnouncementService.InTheFinalLaps(leaderLaps, lapLimit, finalMinute));
    }

    [Fact]
    public void The_countdown_has_its_row_lines_tag_and_a_short_deadline()
    {
        var rows = VoiceSettingsControl.AnnouncementTypes().ToList();
        Assert.Equal(rows.IndexOf(AnnouncementEventType.CloseBattle) + 1, rows.IndexOf(AnnouncementEventType.FinalLapsGap));

        var config = VoiceProfile.CreateDefault().Events[nameof(AnnouncementEventType.FinalLapsGap)];
        Assert.True(config.Enabled);
        Assert.All(config.Templates, template => Assert.Contains("{gap}", template));
        Assert.All(config.Templates, template => Assert.StartsWith(AudioTagTiers.Excited, template));
        Assert.Equal(AudioTagTiers.Excited, AudioTagTiers.For(AnnouncementEventType.FinalLapsGap));

        Assert.True(AnnouncementService.IsLapBoundCall(AnnouncementEventType.FinalLapsGap));
        Assert.Equal(AnnouncementService.LapTimeSayWithinMs, AnnouncementService.SayWithinMsFor(AnnouncementEventType.FinalLapsGap));
    }

    [Fact]
    public void The_countdown_is_told_the_lap_limit_and_the_final_minute()
    {
        Assert.Contains("_announcer.LapLimitProvider = () => _race.LapLimit;", Source("MainForm.cs"));

        var events = Source("AnnouncementService.RaceEvents.cs");
        var minute = events.IndexOf("public void NotifyFinalMinute(", StringComparison.Ordinal);
        var timeOver = events.IndexOf("public void NotifyRaceTimeOver()", StringComparison.Ordinal);
        Assert.Contains("NoteTheFinalMinute();", events[minute..(minute + 400)]);
        Assert.Contains("NoteTheFinalMinute();", events[timeOver..(timeOver + 1_000)]);

        Assert.Contains("CheckFinalLapsGap(ordered, standing);", Source("AnnouncementService.LapCalls.cs"));
        Assert.Contains("_inTheFinalMinute = false;", Source("AnnouncementService.Density.cs"));
    }
}
