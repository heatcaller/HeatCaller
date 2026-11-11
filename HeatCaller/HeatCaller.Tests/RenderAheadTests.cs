namespace HeatCaller.Tests;

/// <summary>
/// The next line renders while this one plays (10 Nov 2026, voice speed
/// plan, phase 2). The slowest calls in the Phase 1 race were ready to go
/// when the speaker came free and then spent 0.6-1 s rendering in silence.
/// </summary>
public sealed class RenderAheadTests
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

    private static VoiceProfile ElevenLabs(double speed = 1.0) =>
        new() { Engine = VoiceEngine.ElevenLabs, VoiceId = "v1", ModelId = "eleven_v4_turbo", Speed = speed };

    [Fact]
    public void The_same_words_in_the_same_settings_have_the_same_key()
    {
        Assert.Equal(
            AnnouncementService.RenderAheadKey(ElevenLabs(), "Dee Ploy, six eight seven!"),
            AnnouncementService.RenderAheadKey(ElevenLabs(), "Dee Ploy, six eight seven!"));
    }

    [Fact]
    public void Other_words_or_another_speed_are_another_line()
    {
        var key = AnnouncementService.RenderAheadKey(ElevenLabs(), "Dee Ploy, six eight seven!");

        Assert.NotEqual(key, AnnouncementService.RenderAheadKey(ElevenLabs(), "Dee Ploy, six eight eight!"));
        Assert.NotEqual(key, AnnouncementService.RenderAheadKey(ElevenLabs(speed: 1.1), "Dee Ploy, six eight seven!"));
    }

    [Fact]
    public void Another_engine_or_Kokoro_voice_is_another_line()
    {
        // The saved-line key is made for ElevenLabs takes and leaves these
        // out; an early Kokoro render must never be played as ElevenLabs'.
        var kokoroA = new VoiceProfile { Engine = VoiceEngine.Kokoro, KokoroVoiceName = "bm_daniel" };
        var kokoroB = new VoiceProfile { Engine = VoiceEngine.Kokoro, KokoroVoiceName = "af_heart" };

        Assert.NotEqual(AnnouncementService.RenderAheadKey(kokoroA, "Go!"), AnnouncementService.RenderAheadKey(kokoroB, "Go!"));
        Assert.NotEqual(AnnouncementService.RenderAheadKey(kokoroA, "Go!"), AnnouncementService.RenderAheadKey(ElevenLabs(), "Go!"));
    }

    [Fact]
    public void A_kept_call_can_be_seen_without_taking_it()
    {
        var now = new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);
        var held = new HeldOver<string>(() => now);

        Assert.Null(held.Peek());

        Assert.True(held.Offer("lead change", AnnouncerDensity.Commanding, 0, out _));

        Assert.Equal("lead change", held.Peek());
        Assert.Equal("lead change", held.Peek());
        Assert.True(held.IsHolding);
        Assert.Equal("lead change", held.Take(out _));
    }

    [Fact]
    public void A_kept_call_that_has_gone_stale_is_not_worth_rendering()
    {
        var now = new DateTime(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);
        var held = new HeldOver<string>(() => now);
        held.Offer("lead change", AnnouncerDensity.Commanding, 0, out _);

        now += AnnouncerDensity.HoldOverFor + TimeSpan.FromMilliseconds(1);

        Assert.Null(held.Peek());
    }

    [Fact]
    public void A_line_takes_its_early_render_and_falls_back_to_rendering_now()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        Assert.Contains("var (synthesis, renderedAhead) = await RenderAheadOrNowAsync(withoutVoice, currentProfile, voice, text);", queue);
        Assert.DoesNotContain("var synthesis = await RenderCastLineAsync(withoutVoice, currentProfile, voice, text);", queue);

        var ahead = Source("AnnouncementService.RenderAhead.cs");
        Assert.Contains("return (await RenderCastLineAsync(withoutVoice, profile, voice, text), false);", ahead);
    }

    [Fact]
    public void The_early_render_builds_the_profile_the_line_will()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");
        var ahead = Source("AnnouncementService.RenderAhead.cs");

        // One profile builder for both.
        Assert.Contains("var currentProfile = ProfileForLine(_dequeuedCall.Value, deliveryOverride, speedOverride);", queue);
        Assert.Contains("var profile = ProfileForLine(type, null, speed);", ahead);

        // Then the model's tags, then the race's energy - SynthesizeAndPlayAsync's order.
        var sanitise = ahead.IndexOf("text = profile.SanitiseForModel(text);", StringComparison.Ordinal);
        var energy = ahead.IndexOf("profile = WithTheRacesEnergy(profile, text);", StringComparison.Ordinal);
        Assert.True(sanitise > 0 && energy > sanitise);
    }

    [Fact]
    public void A_lap_call_is_queued_with_its_words_to_render_ahead()
    {
        var queue = Source("AnnouncementService.VoiceQueue.cs");

        Assert.Contains("renderAhead: () => RenderLineAheadAsync(text, type, speed));", queue);

        // Kept for the end of a line, it keeps them - and so does its second offer.
        Assert.Contains("heldOver: true, lost: lost, routineWeight: routineWeight, sayByMs: sayByMs, facts: facts, renderAhead: renderAhead);", queue);
        Assert.Contains("RenderAhead = renderAhead,", queue);
        Assert.Contains("new HeldCall(type, holdOver, lost, transponder, words, renderAhead)", Source("AnnouncementService.Density.cs"));
    }

    [Fact]
    public void The_next_line_starts_rendering_as_each_line_is_about_to_be_heard()
    {
        Assert.Contains("RenderTheNextLineAhead();", Source("AnnouncementService.VoiceQueue.cs"));
        Assert.Contains("RenderTheNextLineAhead();", Source("AnnouncementService.cs"));
    }

    [Fact]
    public void A_call_queued_or_kept_while_a_line_speaks_starts_rendering_then()
    {
        // The first race with render ahead started it only as each line
        // began - before the calls made during that line existed - and
        // rendered nothing ahead at all.
        var queue = Source("AnnouncementService.VoiceQueue.cs");
        var density = Source("AnnouncementService.Density.cs");

        var signal = queue.IndexOf("_voiceQueueSignal.Release();\n\n        // Queued while a line speaks", StringComparison.Ordinal);
        Assert.True(signal > 0);
        Assert.Contains("RenderAheadIfALineIsSpeaking();", queue[signal..(signal + 300)]);

        var keeping = density.IndexOf("for the end of this line - it outranks what is being said.", StringComparison.Ordinal);
        Assert.True(keeping > 0);
        Assert.Contains("RenderAheadIfALineIsSpeaking();", density[keeping..(keeping + 400)]);
    }

    [Fact]
    public void Every_call_whose_words_are_fixed_when_queued_can_render_ahead()
    {
        // The lap calls, the longest streak, a battle, a rivalry, a race
        // event, a milestone and a release call said live. Not a streak
        // call, whose words are chosen as it is said (the run may have ended).
        var laps = Source("AnnouncementService.LapCalls.cs");

        Assert.Contains("renderAhead: () => RenderLineAheadAsync(text, AnnouncementEventType.StreakLead, speed)", laps);
        Assert.Contains("renderAhead: () => RenderLineAheadAsync(said, type, speed)", laps);
        Assert.Contains("renderAhead: () => RenderLineAheadAsync(text, AnnouncementEventType.LeaderChange, speed: null)", laps);
        Assert.Equal(2, Count(Source("AnnouncementService.RaceEvents.cs"), "renderAhead: () => RenderLineAheadAsync(text, type, speed)"));
        Assert.Contains("renderAhead: () => RenderLineAheadAsync(text, type, speed)", Source("AnnouncementService.DriverRelease.cs"));
    }

    [Fact]
    public void A_call_queued_in_front_does_not_throw_away_the_render_behind_it()
    {
        Assert.InRange(AnnouncementService.MostRenderedAhead, 2, 5);
    }

    private static int Count(string text, string what)
    {
        var count = 0;

        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    [Fact]
    public void Nothing_of_the_playing_line_reaches_the_early_render()
    {
        Assert.Contains("using (ExecutionContext.SuppressFlow())", Source("AnnouncementService.RenderAhead.cs"));
    }

    [Fact]
    public void A_flag_or_a_new_race_forgets_what_was_rendered_ahead()
    {
        var density = Source("AnnouncementService.Density.cs");
        var forget = density.IndexOf("private void ForgetTheHeldOverCall(bool count)", StringComparison.Ordinal);

        Assert.True(forget > 0);
        Assert.Contains("ForgetRenderedAhead();", density[forget..(forget + 400)]);
    }

    [Fact]
    public void The_race_summary_says_how_many_early_renders_were_used()
    {
        var tally = new VoiceTimingTally();
        var used = new VoiceLineTiming { Type = AnnouncementEventType.FastLap, QueuedAtMs = 0, TakenAtMs = 0, RenderedBy = "rendered ahead - ElevenLabs eleven_v4_turbo" };
        var rendered = new VoiceLineTiming { Type = AnnouncementEventType.FastLap, QueuedAtMs = 0, TakenAtMs = 0, RenderedBy = "ElevenLabs eleven_v4_turbo" };
        tally.Add(used);
        tally.Add(rendered);
        tally.NoteRenderedAhead();
        tally.NoteRenderedAhead();
        tally.NoteRenderedAhead();

        Assert.Contains("1 of 3 lines rendered ahead were used", tally.Summary(offered: 5, dropped: 1, held: 0));

        tally.Reset();
        Assert.Equal(0, tally.RenderedAhead);
    }

    [Fact]
    public void A_call_due_before_the_playing_line_can_end_is_not_rendered()
    {
        // A render nobody hears is a request paid for.
        Assert.Contains("!(front.SayByMs is { } sayBy && sayBy <= lineEndsNoSoonerThan)", Source("AnnouncementService.RenderAhead.cs"));
        Assert.InRange(AnnouncementService.PlayingLineLeastLeftMs, 1_000, 3_000);
    }
}
