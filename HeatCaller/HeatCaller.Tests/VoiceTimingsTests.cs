namespace HeatCaller.Tests;

/// <summary>
/// Where a line's time goes between its lap and the room (10 Nov 2026,
/// voice speed plan, phase 0). Every later change to the queue is judged
/// against these numbers, so the sums have to be right.
/// </summary>
public sealed class VoiceTimingsTests
{
    private static VoiceLineTiming Line(long? lapSeen = 1_000, long queued = 1_650, long taken = 1_700,
        AnnouncementEventType type = AnnouncementEventType.PersonalBestLap) =>
        new() { Type = type, LapSeenAtMs = lapSeen, QueuedAtMs = queued, TakenAtMs = taken };

    /// <summary>A line rendered in 0.8 s and played: 2 s of words, 0.15 s lead, 0.6 s tail.</summary>
    private static VoiceLineTiming Played(long lapSeen = 1_000, long renderMs = 800, long speechMs = 2_000, string by = "ElevenLabs eleven_v4_turbo")
    {
        var line = Line(lapSeen, lapSeen + 650, lapSeen + 700);
        var renderStart = lapSeen + 700;

        line.RenderStarted(renderStart);
        line.RenderEnded(renderStart + renderMs);
        line.RenderedBy = by;
        line.Characters = 52;
        line.GotTheSpeaker(renderStart + renderMs + 10);
        line.LeadMs = 150;
        line.SpeechMs = speechMs;
        line.TailMs = 600;
        line.FirstPulled(renderStart + renderMs + 20);
        line.DoneAtMs = renderStart + renderMs + 20 + 150 + speechMs + 600 + 350;

        return line;
    }

    [Fact]
    public void A_played_lap_call_adds_up_from_its_lap_to_its_first_word()
    {
        var line = Played();

        Assert.True(line.Played);
        Assert.Equal(650, line.BatchedMs);
        Assert.Equal(50, line.WaitedMs);
        Assert.Equal(800, line.RenderMs);

        // Pulled at 2520; heard after its 150 ms lead and the device's read-ahead.
        Assert.Equal(2_520 + 150 + VoiceLineTiming.DeviceAheadMs, line.WordsHeardAtMs);
        Assert.Equal(1_520 + 150 + VoiceLineTiming.DeviceAheadMs, line.LapToWordsMs);

        // Taken at 1700, done at 2520 + 150 + 2000 + 600 + 350.
        Assert.Equal(3_920, line.SpeakerHeldMs);
    }

    [Fact]
    public void The_device_read_ahead_is_the_buffer_less_the_one_playing()
    {
        // The same sum the level meters make (AudioLevels.Feeder).
        Assert.Equal(AudioBus.OutputLatencyMs - AudioBus.OutputLatencyMs / AudioBus.OutputBuffers, VoiceLineTiming.DeviceAheadMs);
    }

    [Fact]
    public void A_call_that_is_not_about_a_lap_has_no_lap_to_word_time()
    {
        var line = Line(lapSeen: null, type: AnnouncementEventType.PositionReport);
        line.FirstPulled(5_000);

        Assert.Null(line.BatchedMs);
        Assert.Null(line.LapToWordsMs);
        Assert.NotNull(line.WordsHeardAtMs);
    }

    [Fact]
    public void A_line_that_never_played_is_not_said()
    {
        var line = Line();
        line.RenderStarted(1_700);
        line.RenderEnded(2_600);
        line.DroppedBecause = "it was 0.3s past the latest it could still be said";
        line.DoneAtMs = 2_610;

        Assert.False(line.Played);
        Assert.Null(line.WordsHeardAtMs);
        Assert.Null(line.LapToWordsMs);
        Assert.Contains("not said (it was 0.3s past the latest it could still be said)", line.Describe());
    }

    [Fact]
    public void Only_the_first_stamp_of_each_step_counts()
    {
        // A piece of work that renders twice keeps its first render, and the
        // audio thread's later reads do not move the first.
        var line = Line();
        line.RenderStarted(2_000);
        line.RenderStarted(9_000);
        line.RenderEnded(2_800);
        line.RenderEnded(9_800);
        line.GotTheSpeaker(2_900);
        line.GotTheSpeaker(9_900);
        line.FirstPulled(3_000);
        line.FirstPulled(10_000);

        Assert.Equal(2_000, line.RenderStartMs);
        Assert.Equal(800, line.RenderMs);
        Assert.Equal(2_900, line.SpeakerAtMs);
        Assert.Equal(3_000, line.FirstPulledMs);
    }

    [Fact]
    public void The_console_line_says_where_the_time_went()
    {
        var text = Played().Describe();

        Assert.StartsWith("[timing] PersonalBestLap: lap to first word ", text);
        Assert.Contains("batched 0.65s", text);
        Assert.Contains("waited 0.05s", text);
        Assert.Contains("render 0.80s (ElevenLabs eleven_v4_turbo, 52 chars)", text);
        Assert.Contains("to speaker 0.01s", text);
        Assert.Contains("speaker held 3.92s for 2.00s of words", text);
    }

    [Fact]
    public void A_line_rendered_before_it_was_queued_is_said_without_a_render()
    {
        // A recap or a two-hander's turn (SpeakPrepared), or a release call:
        // these were logged as "not said" in the first baseline race, though
        // the room heard every one.
        var line = Line(lapSeen: null, type: AnnouncementEventType.Reminder);
        line.RenderedBy = "prepared ahead";
        line.Characters = 120;
        line.GotTheSpeaker(1_800);
        line.LeadMs = 150;
        line.SpeechMs = 8_000;
        line.FirstPulled(1_810);
        line.DoneAtMs = 10_500;

        var text = line.Describe();

        Assert.True(line.Played);
        Assert.Null(line.RenderMs);
        Assert.StartsWith("[timing] Reminder: said - ", text);
        Assert.Contains("prepared ahead (120 chars)", text);
        Assert.Contains("for 8.00s of words", text);
    }

    [Fact]
    public void Percentiles_are_nearest_rank()
    {
        long[] values = { 900, 100, 500, 300, 700 };

        Assert.Equal(500, VoiceTimingTally.Percentile(values, 50));
        Assert.Equal(900, VoiceTimingTally.Percentile(values, 90));
        Assert.Equal(100, VoiceTimingTally.Percentile(values, 1));
        Assert.Equal(42, VoiceTimingTally.Percentile(new long[] { 42 }, 90));
    }

    [Fact]
    public void A_race_with_no_lines_has_no_summary()
    {
        Assert.Null(new VoiceTimingTally().Summary(offered: 10, dropped: 3, held: 1));
    }

    [Fact]
    public void The_race_summary_counts_what_was_said_and_how_quickly()
    {
        var tally = new VoiceTimingTally();
        tally.Add(Played(lapSeen: 1_000, renderMs: 700));
        tally.Add(Played(lapSeen: 10_000, renderMs: 900));
        tally.Add(Played(lapSeen: 20_000, renderMs: 0, by: "a saved take"));

        var dropped = Line();
        dropped.DroppedBecause = "muted";
        dropped.DoneAtMs = dropped.TakenAtMs;
        tally.Add(dropped);

        var text = tally.Summary(offered: 40, dropped: 12, held: 3)!;

        Assert.StartsWith("[timing] This race: 3 of 4 lines taken were said", text);
        Assert.Contains("over 3", text);

        // The saved take is not an ElevenLabs render, so only two count.
        Assert.Contains("ElevenLabs rendered in 0.70s (median), 0.90s (90%), over 2", text);
        Assert.Contains("12 of 40 routine calls lost behind a line being spoken, 3 held by the pacing dial", text);
        Assert.Contains("words were ", text);
    }

    [Fact]
    public void A_reset_starts_the_race_again()
    {
        var tally = new VoiceTimingTally();
        tally.Add(Played());
        tally.Reset();

        Assert.Empty(tally.Lines);
    }

    [Fact]
    public void A_long_night_keeps_only_the_newest_lines()
    {
        var tally = new VoiceTimingTally();

        for (var i = 0; i < VoiceTimingTally.MostKept + 5; i++)
        {
            tally.Add(Line(queued: i, taken: i));
        }

        Assert.Equal(VoiceTimingTally.MostKept, tally.Lines.Count);
        Assert.Equal(5, tally.Lines[0].QueuedAtMs);
    }
}
