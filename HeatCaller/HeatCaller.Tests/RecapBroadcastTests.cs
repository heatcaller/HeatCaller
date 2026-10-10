namespace HeatCaller.Tests;

/// <summary>
/// The recap as a broadcast (27 Sep 2026): the mechanic's interview split
/// between the two voices (CommentaryService.ParseInterview), a race named
/// as a heat or a main (DescribeKind), and the mechanic's voice standing in
/// for the announcer's for a line (VoiceProfile.CloneWithVoice).
///
/// The mechanic used to have a Windows voice among its per-engine voices.
/// That voice was retired on 10 Oct 2026, so what is checked here is the
/// same rule over the voices that are left: only what was picked for the
/// mechanic replaces the announcer's, and everything else is kept.
/// </summary>
public sealed class RecapBroadcastTests
{
    private static readonly string[] Names = { "Asher", "Wrench", "Audi R8" };

    // --- The interview ------------------------------------------------------------------

    [Fact]
    public void An_interview_in_the_form_asked_for_splits_into_its_exchanges()
    {
        // As LocalLlmClient hands it over: the lines joined into one, and the
        // leading "HOST:" taken off as though it were a label on the answer.
        const string text =
            "Wrench, what a drive from Asher! How did the car feel? MECHANIC: It was on rails - he never put a wheel wrong. "
            + "HOST: And what did you change? MECHANIC: We went up a tooth on the pinion, to 23T, and it paid off on the straight.";

        var interview = CommentaryService.ParseInterview(text, "Wrench", Names);

        Assert.NotNull(interview);
        Assert.Equal(2, interview!.Count);
        Assert.Equal("Wrench, what a drive from Asher! How did the car feel?", interview[0].Question);
        Assert.Equal("It was on rails - he never put a wheel wrong.", interview[0].Answer);
        Assert.Equal("And what did you change?", interview[1].Question);
        Assert.Equal("We went up a tooth on the pinion, to 23T, and it paid off on the straight.", interview[1].Answer);
    }

    [Fact]
    public void Labels_in_bold_in_any_case_or_by_the_mechanics_name_are_read()
    {
        const string text =
            "**Host:** Wrench, great win! **Wrench:** Thanks - the car was perfect. host : What changed? mechanic: Nothing, we left it alone.";

        var interview = CommentaryService.ParseInterview(text, "Wrench", Names);

        Assert.Equal(2, interview!.Count);
        Assert.Equal("Wrench, great win!", interview[0].Question);
        Assert.Equal("Thanks - the car was perfect.", interview[0].Answer);
        Assert.Equal("What changed?", interview[1].Question);
        Assert.Equal("Nothing, we left it alone.", interview[1].Answer);
    }

    [Fact]
    public void The_mechanics_name_mid_sentence_is_not_taken_for_the_mechanic_speaking()
    {
        const string text = "HOST: Over to you, Wrench: how did it feel out there? MECHANIC: Like it was on rails all night.";

        var exchange = Assert.Single(CommentaryService.ParseInterview(text, "Wrench", Names)!);

        Assert.Equal("Over to you, Wrench: how did it feel out there?", exchange.Question);
        Assert.Equal("Like it was on rails all night.", exchange.Answer);
    }

    [Fact]
    public void A_preamble_before_the_first_host_line_is_dropped()
    {
        const string text = "Here is the interview. HOST: Wrench, how was it? MECHANIC: Brilliant, from start to finish.";

        var exchange = Assert.Single(CommentaryService.ParseInterview(text, "Wrench", Names)!);

        Assert.Equal("Wrench, how was it?", exchange.Question);
        Assert.Equal("Brilliant, from start to finish.", exchange.Answer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("What a race that was, and what a drive from Asher!")]
    [InlineData("MECHANIC: We changed nothing at all. HOST: Great stuff, thanks.")]
    [InlineData("HOST: Wrench, how was it? HOST: And the car today?")]
    [InlineData("HOST: Wrench? MECHANIC: Great.")]
    public void An_interview_that_cant_be_split_into_two_voices_is_not_used(string text)
    {
        Assert.Null(CommentaryService.ParseInterview(text, "Wrench", Names));
    }

    [Fact]
    public void A_broken_exchange_ends_the_interview_and_no_more_than_two_are_kept()
    {
        const string broken = "HOST: How was it out there? MECHANIC: Smooth all the way round. HOST: And the car? HOST: Anything else?";
        Assert.Single(CommentaryService.ParseInterview(broken, "Wrench", Names)!);

        const string three =
            "HOST: First question here? MECHANIC: First answer here. HOST: Second question here? MECHANIC: Second answer here. "
            + "HOST: Third question here? MECHANIC: Third answer here.";
        Assert.Equal(2, CommentaryService.ParseInterview(three, string.Empty, Names)!.Count);
    }

    [Fact]
    public void A_shouted_question_is_calmed_and_a_long_answer_cut_to_whole_sentences()
    {
        const string sentence = "We tuned it all week and it paid off.";
        var longAnswer = string.Join(" ", Enumerable.Repeat(sentence, 8));

        var exchange = Assert.Single(CommentaryService.ParseInterview(
            $"HOST: WRENCH, THAT WAS AN INCREDIBLE DRIVE BY ASHER! MECHANIC: {longAnswer}", "Wrench", Names)!);

        Assert.Equal("Wrench, that was an incredible drive by Asher!", exchange.Question);

        // Nine words a sentence, and 45 words with the usual fifth of slack
        // is 54: six whole sentences.
        Assert.Equal(string.Join(" ", Enumerable.Repeat(sentence, 6)), exchange.Answer);
    }

    // --- Heat or main -------------------------------------------------------------------

    [Theory]
    [InlineData(RoundKind.Qualifier, -1, false, "qualifying heat")]
    [InlineData(RoundKind.Qualifier, -1, true, null)]
    [InlineData(RoundKind.Main, 0, false, "the A-Main, the top final of the class")]
    [InlineData(RoundKind.Main, 1, true, "the B-Main")]
    [InlineData(RoundKind.Main, -1, false, "a main")]
    [InlineData(RoundKind.Practice, -1, false, "practice session")]
    public void A_race_is_named_a_heat_or_a_main(RoundKind kind, int tier, bool clubNight, string? expected)
    {
        Assert.Equal(expected, CommentaryService.DescribeKind(new RoundDefinition { Kind = kind, MainTier = tier }, clubNight));
    }

    [Fact]
    public void A_freeform_race_is_neither()
    {
        Assert.Null(CommentaryService.DescribeKind(null, clubNight: false));
    }

    // --- The mechanic's voice -------------------------------------------------------------

    [Fact]
    public void The_mechanics_voice_replaces_only_what_was_picked_for_it()
    {
        var announcer = new VoiceProfile
        {
            Engine = VoiceEngine.ElevenLabs,
            ApiKey = "key",
            VoiceId = "announcer-id",
            VoiceName = "Adam",
            KokoroVoiceName = "am_michael",
            KokoroStandsIn = false,
            Stability = 0.3,
            Style = 0.4,
        };

        // Only an ElevenLabs voice picked for the mechanic: no HeatCaller
        // voice, no Kokoro voice.
        var mechanic = new SecondVoice("mechanic-id", "Bill", string.Empty, string.Empty, string.Empty);

        var copy = announcer.CloneWithVoice(mechanic);

        Assert.Equal("mechanic-id", copy.VoiceId);
        Assert.Equal("Bill", copy.VoiceName);

        // Not picked, so the announcer's: the Kokoro voice, the stand-in
        // choice, the key, the delivery.
        Assert.Equal("am_michael", copy.KokoroVoiceName);
        Assert.False(copy.KokoroStandsIn);
        Assert.Equal("key", copy.ApiKey);
        Assert.Equal(0.3, copy.Stability);
        Assert.Equal(0.4, copy.Style);
        Assert.Equal(VoiceEngine.ElevenLabs, copy.EffectiveEngine);

        // A copy - the saved profile still speaks as the announcer.
        Assert.Equal("announcer-id", announcer.VoiceId);

        Assert.True(mechanic.IsSetFor(VoiceEngine.ElevenLabs));
        Assert.False(mechanic.IsSetFor(VoiceEngine.Kokoro));
        Assert.False(mechanic.IsSetFor(VoiceEngine.HeatCaller));
    }

    [Fact]
    public void A_kokoro_voice_picked_for_the_mechanic_replaces_the_announcers()
    {
        var announcer = new VoiceProfile
        {
            Engine = VoiceEngine.Kokoro,
            KokoroVoiceName = "am_michael",
        };

        var mechanic = new SecondVoice(
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, "bf_emma");

        var copy = announcer.CloneWithVoice(mechanic);

        Assert.Equal("bf_emma", copy.KokoroVoiceName);
        Assert.Equal(VoiceEngine.Kokoro, copy.EffectiveEngine);

        // The announcer still speaks as itself.
        Assert.Equal("am_michael", announcer.KokoroVoiceName);
        Assert.True(mechanic.IsSetFor(VoiceEngine.Kokoro));
    }

    [Fact]
    public void A_mechanics_voice_left_null_by_a_hand_edited_settings_file_reads_as_none()
    {
        var settings = new AppSettings { MechanicElevenLabsVoiceId = null!, MechanicKokoroVoiceName = "af_heart" };

        Assert.False(settings.MechanicVoice.IsSetFor(VoiceEngine.ElevenLabs));
        Assert.True(settings.MechanicVoice.IsSetFor(VoiceEngine.Kokoro));
    }
}
