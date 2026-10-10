namespace HeatCaller.Tests;

/// <summary>
/// The club's voice-ID, and the night shaping the show.
///
/// WHAT IS ACTUALLY BEING TESTED. AudioBus is silent for the whole run
/// (TestSetup) and nothing here renders a voice, so none of this can be
/// listened to. What can be checked is everything that decides what a room
/// would have heard: the words the voice-ID comes down to and the key a
/// recording of it is kept under, how often across an evening it goes out,
/// which voice a take was made in - and, for the night shaping the show,
/// that a thrilling race moves the production by a fraction of a second and
/// changes nothing else about it at all. That last one is the point of this
/// file: a feature whose whole case is that it is never noticed as a
/// feature needs a test that says how little it is allowed to do.
/// </summary>
public sealed class ShowIdentTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Ident = "You're listening to Thunder Valley Raceway";

    // --- The line itself -------------------------------------------------------------------

    [Fact]
    public void A_typed_voice_id_comes_down_to_one_line()
    {
        // Pasted out of a document, which is how a club will get it in
        // there - and the text IS the key a recording is kept under
        // (StingerLibrary.KeyFor), so a stray newline would be a different
        // line from the one that was recorded, and a club would have paid
        // for a take that never played again.
        var line = ShowIdent.Line("  You're listening\r\n to   Thunder Valley\tRaceway. ")!;

        Assert.Equal("You're listening to Thunder Valley Raceway.", line);
    }

    [Fact]
    public void Braces_come_out_so_a_voice_id_can_always_be_recorded()
    {
        // A brace is this app's mark for something filled in at race time,
        // and RecordableLines refuses any line holding one - so a club who
        // typed a placeholder would have had a voice-ID that could never be
        // recorded and nothing anywhere to tell them why.
        var line = ShowIdent.Line("You're listening to {Thunder Valley} Raceway")!;

        Assert.Equal(Ident, line);
        Assert.DoesNotContain("{", line);
        Assert.Contains(line, StingerLibrary.RecordableLines(Voice(VoiceEngine.ElevenLabs, "Ivy"), line));
    }

    [Fact]
    public void Nothing_typed_is_nothing_said()
    {
        Assert.Null(ShowIdent.Line(null));
        Assert.Null(ShowIdent.Line("   "));

        // No letter or digit in it anywhere: a second of the announcer
        // clearing its throat is worse than no voice-ID at all.
        Assert.Null(ShowIdent.Line("- - -"));

        Assert.Null(ShowIdent.Item(null, _ => { }));
        Assert.Null(ShowIdent.Item("  ", _ => { }));
    }

    [Fact]
    public void A_voice_id_longer_than_a_sentence_is_turned_down_and_said_so()
    {
        var tooLong = new string('a', ShowIdent.LongestLine + 1);

        Assert.Null(ShowIdent.Line(tooLong));
        Assert.True(ShowIdent.TooLong(tooLong));

        // And the club is told WHICH of the two reasons nothing is said -
        // Line cannot tell them, because both come back null.
        Assert.Contains("one sentence", ShowIdent.Reading(tooLong, Voice(VoiceEngine.Kokoro, string.Empty), Library()));

        // Exactly at the limit is fine: the box on the Commentary page
        // takes the same number as its MaxLength, so a club types right up
        // to it and stops.
        Assert.NotNull(ShowIdent.Line(new string('a', ShowIdent.LongestLine)));
        Assert.False(ShowIdent.TooLong(new string('a', ShowIdent.LongestLine)));

        // Blank is not "too long", it is "not typed", and the two get
        // different sentences.
        Assert.False(ShowIdent.TooLong("   "));
        Assert.Contains("Type one line", ShowIdent.Reading(null, Voice(VoiceEngine.Kokoro, string.Empty), Library()));
    }

    [Fact]
    public void The_voice_id_is_continuity_so_a_tight_night_drops_it_and_not_the_race()
    {
        // The whole of why ShowItem.Bridge and not Spoken. Continuity is
        // the first thing a block's fitting drops when the gap is short
        // (ShowBlock.Fit), which is exactly the right answer on a night
        // running late: the two seconds come off the station introducing
        // itself, never off the racing.
        var item = ShowIdent.Item(Ident, _ => { })!;

        Assert.True(item.IsContinuity);
        Assert.Null(item.Kind);
        Assert.False(item.Silent);
        Assert.False(item.PunctuateBefore, "a sting on top of the opener is two flourishes in the same second");
    }

    [Fact]
    public void The_voice_id_says_its_cleaned_line_and_only_then_counts_as_gone_out()
    {
        var said = new List<string>();
        var went = 0;

        var item = ShowIdent.Item("  You're listening to   Thunder Valley Raceway  ", said.Add, () => went++)!;

        Assert.Equal(0, went);

        Assert.Single(item.Parts).Say();

        // The words that go out are the words the fitting reckoned the
        // length from - ShowUtterance.Words says they have to be the same
        // text, or a block's fitting is measuring something else.
        Assert.Equal(Ident, Assert.Single(said));
        Assert.Equal(Ident, Assert.Single(item.Parts).Words);
        Assert.Equal(1, went);
    }

    [Fact]
    public void A_voice_queue_that_fell_over_does_not_spend_the_nights_voice_id()
    {
        // The telling comes AFTER the say. A line that never reached the
        // announcer did not go out, and the next show with room should
        // still be able to introduce the station.
        var went = 0;

        var item = ShowIdent.Item(
            Ident,
            _ => throw new InvalidOperationException("the voice queue fell over"),
            () => went++)!;

        Assert.Throws<InvalidOperationException>(() => Assert.Single(item.Parts).Say());
        Assert.Equal(0, went);
    }

    // --- Recording it, and the voice it was recorded in ------------------------------------

    [Fact]
    public void The_voice_id_is_one_more_recordable_line_and_not_a_second_store()
    {
        var voice = Voice(VoiceEngine.ElevenLabs, "Ivy");
        voice.Events[AnnouncementEventType.RaceStarted.ToString()] =
            new AnnouncementEventConfig { Enabled = true, Templates = new List<string> { "GREEN FLAG - WE ARE RACING" } };

        var lines = StingerLibrary.RecordableLines(voice, Ident);

        Assert.Equal(2, lines.Count);

        // Last, so a club reading "1 of 2 voice lines recorded" sees the
        // race lines where they always were with the voice-ID on the end,
        // rather than everything shifted along by one.
        Assert.Equal("GREEN FLAG - WE ARE RACING", lines[0]);
        Assert.Equal(Ident, lines[1]);

        // And with no voice-ID typed, nothing changes for every club that
        // has not typed one.
        Assert.Equal(StingerLibrary.RecordableLines(voice), StingerLibrary.RecordableLines(voice, null));
    }

    [Fact]
    public void A_voice_id_word_for_word_a_race_line_is_not_listed_twice()
    {
        var voice = Voice(VoiceEngine.ElevenLabs, "Ivy");
        voice.Events[AnnouncementEventType.RaceStarted.ToString()] =
            new AnnouncementEventConfig { Enabled = true, Templates = new List<string> { "WE ARE RACING" } };

        // Same words, different spacing and case - the key ignores both, so
        // listing it twice would charge a club twice for one take.
        Assert.Equal("WE ARE RACING", Assert.Single(StingerLibrary.RecordableLines(voice, "we   are racing")));
    }

    [Fact]
    public void A_recording_made_for_the_voice_id_is_found_under_its_own_line()
    {
        var library = Library();

        library.Save(Ident, new byte[] { 1, 2, 3 }, "Ivy", "Pit Lane");

        var entry = ShowIdent.Recorded(Ident, library)!;

        Assert.Equal("Ivy", entry.VoiceName);
        Assert.Equal(StingerLibrary.KeyFor(Ident), entry.Key);

        // Found from what the club TYPED as well as from the cleaned line:
        // the page reads the setting, not a cleaned copy of it.
        Assert.NotNull(ShowIdent.Recorded("  You're listening to Thunder Valley Raceway ", library));

        Assert.Equal(new byte[] { 1, 2, 3 }, library.TryLoad(Ident)!);
    }

    [Fact]
    public void A_voice_id_recorded_in_a_voice_the_club_has_since_changed_is_still_played_and_said_so()
    {
        // THE FAILURE MODE. A take from March under April's show: one
        // sentence in the old voice and the rest of the night in the new
        // one, and nothing anywhere saying why.
        //
        // It still plays. That is StingerLibrary's whole contract, and
        // holding this one line back would be a second, contradictory
        // policy for the same store - and would mean a club who changed
        // voice lost their voice-ID silently, which is a worse version of
        // the same complaint. So the club is TOLD, and one press of
        // Re-record all on Soundscapes puts it right.
        var library = Library();

        library.Save(Ident, new byte[] { 9 }, StingerLibrary.VoiceNameFor(Voice(VoiceEngine.ElevenLabs, "Ivy")), "Pit Lane");

        var nowUsing = Voice(VoiceEngine.ElevenLabs, "Brian");

        Assert.True(ShowIdent.InAnotherVoice(Ident, nowUsing, library));

        var reading = ShowIdent.Reading(Ident, nowUsing, library);

        Assert.Contains("Ivy", reading);
        Assert.Contains("Brian", reading);
        Assert.Contains("plays as recorded", reading);

        // The item is still made: the line goes out, in the recorded voice,
        // as it has every night since March.
        Assert.NotNull(ShowIdent.Item(Ident, _ => { }));
    }

    [Fact]
    public void The_same_voice_with_stray_spacing_is_not_a_voice_change()
    {
        // The names come out of boxes a club typed into, and "Ivy " is not
        // a new voice. Anything else different IS one.
        var library = Library();

        library.Save(Ident, new byte[] { 9 }, " ivy ", "Pit Lane");

        Assert.False(ShowIdent.InAnotherVoice(Ident, Voice(VoiceEngine.ElevenLabs, "Ivy"), library));
        Assert.Contains("recorded in", ShowIdent.Reading(Ident, Voice(VoiceEngine.ElevenLabs, "Ivy"), library));
    }

    [Fact]
    public void With_nothing_recorded_and_nothing_to_record_with_the_voice_id_is_still_said()
    {
        // A club with no ElevenLabs key, no relay and no recording. Kokoro
        // is the free voice and the fallback for everything, so the line is
        // spoken live on this PC, every night, with nothing billed - and
        // the voice-ID is the one segment where that costs nothing at all,
        // because the words never change and so there is nothing to write.
        var library = Library();
        var free = Voice(VoiceEngine.Kokoro, string.Empty);

        Assert.Null(ShowIdent.Recorded(Ident, library));
        Assert.False(ShowIdent.InAnotherVoice(Ident, free, library));
        Assert.NotNull(ShowIdent.Item(Ident, _ => { }));

        var reading = ShowIdent.Reading(Ident, free, library);

        Assert.Contains("said live", reading);
        Assert.Contains("Kokoro", reading);
    }

    [Fact]
    public void Nothing_anywhere_in_the_voice_ids_words_names_the_retired_windows_voice()
    {
        // Retired on 10 Oct 2026. Kokoro is now both the free voice a club
        // can pick and the voice that speaks when the chosen one cannot, so
        // there is no second free voice to name. Every state a club can
        // reach is swept, so a sentence added to the table later cannot
        // quietly bring it back.
        var library = Library();
        var readings = new List<string>();

        foreach (var engine in new[] { VoiceEngine.Kokoro, VoiceEngine.ElevenLabs, VoiceEngine.HeatCaller })
        {
            var voice = Voice(engine, "Ivy");

            readings.Add(ShowIdent.Reading(null, voice, library));
            readings.Add(ShowIdent.Reading(new string('a', ShowIdent.LongestLine + 1), voice, library));
            readings.Add(ShowIdent.Reading(Ident, voice, library));
            readings.Add(StingerLibrary.VoiceNameFor(voice));
        }

        library.Save(Ident, new byte[] { 9 }, "Some Other Voice", "Pit Lane");
        readings.Add(ShowIdent.Reading(Ident, Voice(VoiceEngine.ElevenLabs, "Ivy"), library));

        foreach (var reading in readings)
        {
            Assert.DoesNotContain("Windows", reading, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SAPI", reading, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_recording_voice_name_has_one_spelling_for_every_engine()
    {
        // ONE TABLE, compared with SameVoice and nothing else. Two
        // spellings of the same voice would have read as a voice change on
        // every single night, which is the one thing this is read for.
        Assert.Equal("Ivy", StingerLibrary.VoiceNameFor(Voice(VoiceEngine.ElevenLabs, "Ivy")));
        Assert.Equal("your ElevenLabs voice", StingerLibrary.VoiceNameFor(Voice(VoiceEngine.ElevenLabs, string.Empty)));
        Assert.Equal("the Kokoro voice", StingerLibrary.VoiceNameFor(Voice(VoiceEngine.Kokoro, "ignored")));

        var relay = Voice(VoiceEngine.HeatCaller, string.Empty);

        Assert.Equal("the HeatCaller voice", StingerLibrary.VoiceNameFor(relay));

        relay.HeatCallerVoiceName = "Clubhouse";

        Assert.Equal("the HeatCaller voice (Clubhouse)", StingerLibrary.VoiceNameFor(relay));

        Assert.True(StingerLibrary.SameVoice("Ivy", " ivy "));
        Assert.False(StingerLibrary.SameVoice("Ivy", "Brian"));
        Assert.False(StingerLibrary.SameVoice(null, "Ivy"));
        Assert.True(StingerLibrary.SameVoice(null, "   "));
    }

    // --- How often it goes out -------------------------------------------------------------

    [Fact]
    public void The_top_of_the_night_carries_the_voice_id()
    {
        var order = new ShowRunningOrder();

        Assert.True(order.IdentDue(SegmentKind.ColdOpen, At(19, 30)));
        Assert.Equal(0, order.IdentsTonight);
    }

    [Fact]
    public void A_rejoin_never_carries_it()
    {
        // The rejoin is the show that exists BECAUSE the room has already
        // been told. Introducing the station in front of every heat is the
        // exact mistake the rejoin was written to undo.
        var order = new ShowRunningOrder();

        order.Went(At(19, 30));
        order.IdentWent(At(19, 30));

        Assert.False(order.IdentDue(null, At(19, 38)));
    }

    [Fact]
    public void A_race_intro_never_carries_it_however_long_the_room_has_been_quiet()
    {
        // A race intro's show is awaited by the chain that is about to call
        // the next race to the grid, so two seconds of station
        // identification would come off the race. A race call always wins.
        var order = new ShowRunningOrder();

        Assert.False(order.IdentDue(SegmentKind.RaceIntro, At(19, 30)));

        order.Went(At(19, 30));

        Assert.False(order.IdentDue(SegmentKind.RaceIntro, At(21, 0)));
    }

    [Fact]
    public void After_a_long_quiet_spell_it_comes_round_again_but_not_before_the_floor()
    {
        var order = new ShowRunningOrder();

        order.Went(At(19, 0));
        order.IdentWent(At(19, 0));

        // Twenty five minutes later. The clock calls that a break, because
        // it is longer than AfterABreak - but it is a club with a slow
        // turnaround, not a new audience, and the voice-ID is held.
        Assert.False(order.IdentDue(null, At(19, 25)));

        // And once the floor has passed, the room really has turned over.
        Assert.True(order.IdentDue(null, At(19, 0) + ShowRunningOrder.IdentApart));
    }

    [Fact]
    public void A_club_with_a_slow_turnaround_is_not_introduced_all_evening()
    {
        // THE FAILURE THE FLOOR IS FOR. Twenty five minutes between heats
        // reads as BackAfterABreak every single time, so without a floor
        // this evening would have introduced the station eight times -
        // which is a jingle, which is the thing this feature is not.
        var order = new ShowRunningOrder();
        var start = At(19, 0);

        for (var i = 0; i < 8; i++)
        {
            var now = start + TimeSpan.FromMinutes(25 * i);

            if (order.IdentDue(null, now) && ShowIdent.Item(Ident, _ => { }, () => order.IdentWent(now)) is { } ident)
            {
                // Said, which is the only thing that counts as gone out.
                Assert.Single(ident.Parts).Say();
            }

            order.Went(now);
        }

        // Nearly three hours of racing: the top of the night, and three
        // more as the floor came round.
        Assert.Equal(4, order.IdentsTonight);
    }

    [Fact]
    public void The_sign_off_always_carries_it()
    {
        // The last thing said should be whose night it was, and a short
        // meeting would otherwise end without ever naming the club.
        var order = new ShowRunningOrder();

        order.Went(At(19, 30));
        order.IdentWent(At(19, 30));

        Assert.True(order.IdentDue(SegmentKind.SignOffNumbers, At(19, 35)));
    }

    [Fact]
    public void Being_asked_for_is_not_going_out()
    {
        // Apart for Billing's own reason: a block whose gap is tight drops
        // its continuity, and the voice-ID IS continuity. If being asked
        // for counted, a club with tight gaps would spend the night's one
        // voice-ID on a show that never said it, and the next show - the
        // one with room - would be told the station was introduced already.
        var order = new ShowRunningOrder();

        Assert.True(order.IdentDue(null, At(19, 30)));
        Assert.True(order.IdentDue(null, At(19, 31)));
        Assert.Equal(0, order.IdentsTonight);

        order.IdentWent(At(19, 31));

        Assert.False(order.IdentDue(null, At(19, 32)));
        Assert.Equal(1, order.IdentsTonight);
    }

    [Fact]
    public void A_fresh_evening_introduces_the_station_again()
    {
        var order = new ShowRunningOrder();

        order.Went(At(19, 0));
        order.IdentWent(At(19, 0));

        Assert.False(order.IdentDue(null, At(19, 25)));

        order.NewNight();

        Assert.Equal(0, order.IdentsTonight);
        Assert.True(order.IdentDue(null, At(19, 26)));
    }

    [Fact]
    public void A_clock_that_went_backwards_does_not_lock_the_voice_id_out_for_the_evening()
    {
        // An NTP correction mid evening. It reads as "too soon", which it
        // stops being once the clock catches up - and the sign-off carries
        // the voice-ID whatever the clock says.
        var order = new ShowRunningOrder();

        order.IdentWent(At(20, 0));

        Assert.False(order.IdentDue(null, At(19, 0)));
        Assert.True(order.IdentDue(SegmentKind.SignOffNumbers, At(19, 0)));
        Assert.True(order.IdentDue(null, At(20, 0) + ShowRunningOrder.IdentApart));
    }

    // --- What the night is allowed to change ----------------------------------------------

    [Fact]
    public void A_night_with_nothing_measured_is_the_show_it_always_was()
    {
        foreach (var cue in Enum.GetValues<ShowCue>())
        {
            Assert.Equal(ShowBilling.For(cue), ShowBilling.For(cue, ShowDrama.None));
            Assert.False(ShowBilling.For(cue, ShowDrama.None).BiggerOpen);

            // Null is NOT a margin of nothing: zero would have made every
            // show with no race in front of it the most dramatic of the
            // night.
            Assert.Equal(ShowBilling.For(cue), ShowBilling.For(cue, ShowDrama.Finish(null)));
            Assert.Equal(ShowBilling.For(cue), ShowBilling.For(cue, ShowDrama.Finish(0d)));
        }
    }

    [Fact]
    public void A_photo_finish_is_worth_a_beat_at_each_end_and_nothing_else()
    {
        // Four thousandths of a second, and the whole of what the night may
        // do about it.
        var plain = ShowBilling.For(ShowCue.TopOfTheNight);
        var big = ShowBilling.For(ShowCue.TopOfTheNight, ShowDrama.Finish(0.004));

        Assert.True(big.BiggerOpen);
        Assert.Equal(plain.AirSeconds + ShowBilling.BiggerAir, big.AirSeconds, 6);
        Assert.Equal(plain.LiftSeconds + ShowBilling.LongerLift, big.LiftSeconds, 6);

        // A show whose shape visibly LURCHES reads as a fault and not as
        // production, so everything that would be heard as a different show
        // is untouched.
        Assert.Equal(plain.Cue, big.Cue);
        Assert.Equal(plain.EnterOpenerLate, big.EnterOpenerLate);
        Assert.Equal(plain.BedAloneSeconds, big.BedAloneSeconds, 6);
        Assert.Equal(plain.PlayoutSeconds, big.PlayoutSeconds, 6);
    }

    [Fact]
    public void The_whole_of_the_drama_is_under_a_second()
    {
        // Named, so nobody raises these to "make it land" without meeting
        // the argument: six tenths on a twenty second show is heard as
        // "they took a breath before that one" by anyone who is listening
        // and as nothing at all by everybody else, which is the right
        // amount for a thing nobody asked for.
        Assert.True(
            ShowBilling.BiggerAir + ShowBilling.LongerLift < 1.0,
            "the bigger open has to stay a beat, not a different front");

        foreach (var cue in Enum.GetValues<ShowCue>())
        {
            var plain = ShowBilling.For(cue);
            var big = ShowBilling.For(cue, ShowDrama.Finish(0.004));

            var grown = big.AirSeconds - plain.AirSeconds
                + (big.LiftSeconds - plain.LiftSeconds)
                + (big.PlayoutSeconds - plain.PlayoutSeconds)
                + (big.BedAloneSeconds - plain.BedAloneSeconds);

            Assert.Equal(ShowBilling.BiggerAir + ShowBilling.LongerLift, grown, 6);
        }
    }

    [Fact]
    public void A_rejoin_with_a_thriller_behind_it_is_still_a_rejoin()
    {
        // The night may make a show a shade bigger; it may not make it a
        // different show. The room hearing the whole theme twice in two
        // minutes is a worse fault than a thrilling race going unmarked.
        var big = ShowBilling.For(ShowCue.Rejoin, ShowDrama.Finish(0.01));

        Assert.Equal(ShowCue.Rejoin, big.Cue);
        Assert.True(big.EnterOpenerLate, "a photo finish must not promote a rejoin into a full open");
    }

    [Fact]
    public void A_walkover_gets_the_show_a_walkover_always_got()
    {
        Assert.False(ShowDrama.Finish(3.2).Notable);
        Assert.False(ShowDrama.Finish(0.6).Notable);
        Assert.False(ShowBilling.For(ShowCue.Rejoin, ShowDrama.Finish(3.2)).BiggerOpen);
    }

    [Fact]
    public void Close_enough_is_the_same_half_second_the_rest_of_the_app_means_by_it()
    {
        // NOT A NEW THRESHOLD. CommentaryShowFacts.CloseFinishSeconds is
        // already what this app calls a close finish: it is the margin the
        // sign-off reads out as "the closest finish all night" and the one
        // CommentaryService.DescribeMargin calls a photo finish. A third
        // number here and the show would have swelled for a race the words
        // were about to call comfortable.
        var close = CommentaryShowFacts.CloseFinishSeconds;

        Assert.True(ShowDrama.Finish(close).Notable);
        Assert.False(ShowDrama.Finish(close + 0.01).Notable);
    }

    [Fact]
    public void A_lapped_second_place_was_never_a_close_finish()
    {
        // A car a lap down can cross the line a tenth after the winner. The
        // recap that called that a photo finish is the bug DescribeMargin's
        // lap-difference rule exists for, and the show must not make it
        // again in another place.
        Assert.False(ShowDrama.Finish(0.1, lapsDown: 1).Notable);
        Assert.True(ShowDrama.Finish(0.1).Notable);
    }

    [Fact]
    public void A_track_record_is_worth_it_on_its_own()
    {
        // A record stands until it does not, whatever the margin was.
        Assert.True(ShowDrama.Finish(null, trackRecord: true).Notable);
        Assert.True(ShowDrama.Finish(9.9, lapsDown: 3, trackRecord: true).Notable);
    }

    [Fact]
    public void The_sign_off_reads_its_drama_off_the_night_the_club_already_keeps()
    {
        // The one fact the night keeps for exactly this - the smallest
        // winning margin of the evening, which the sign-off is about to
        // read out anyway.
        var close = new NightDone(12, 400, ClosestFinishSeconds: 0.06, ClosestFinishWinner: "Sam");
        var dull = new NightDone(12, 400, ClosestFinishSeconds: 2.4, ClosestFinishWinner: "Sam");
        var nothingMeasured = new NightDone(12, 400);

        Assert.True(ShowDrama.Night(close).Notable);
        Assert.False(ShowDrama.Night(dull).Notable);
        Assert.False(ShowDrama.Night(nothingMeasured).Notable);

        Assert.True(ShowBilling.For(ShowCue.Playout, ShowDrama.Night(close)).BiggerOpen);

        // Not even the sign-off's own extra bed, which is the one number
        // this could plausibly have reached for and the one a club would
        // notice getting longer.
        Assert.Equal(
            ShowBilling.For(ShowCue.Playout).PlayoutSeconds,
            ShowBilling.For(ShowCue.Playout, ShowDrama.Night(close)).PlayoutSeconds,
            6);
    }

    [Fact]
    public void The_running_order_will_not_let_the_night_delay_a_race()
    {
        // Belt and braces for the easiest wiring mistake in this area: a
        // caller holding the LAST race's facts at the moment it stages the
        // intro for the next one.
        var order = new ShowRunningOrder();
        var thriller = ShowDrama.Finish(0.004);

        var intro = order.Billing(SegmentKind.RaceIntro, At(19, 30), rejoinAllowed: true, thriller);

        Assert.False(intro.BiggerOpen);
        Assert.Equal(ShowBilling.For(ShowCue.TopOfTheNight).AirSeconds, intro.AirSeconds, 6);

        // Every other show does take it.
        Assert.True(order.Billing(SegmentKind.RaceRecap, At(19, 30), rejoinAllowed: true, thriller).BiggerOpen);
    }

    [Fact]
    public void A_club_who_turned_the_rejoin_off_still_gets_their_beat()
    {
        // Turning the rejoin off only takes the SHORTENED opener away. The
        // two decisions are about different things and must not cancel each
        // other out.
        var order = new ShowRunningOrder();

        order.Went(At(19, 30));

        var billing = order.Billing(null, At(19, 38), rejoinAllowed: false, ShowDrama.Finish(0.004));

        Assert.Equal(ShowCue.Rejoin, billing.Cue);
        Assert.False(billing.EnterOpenerLate);
        Assert.True(billing.BiggerOpen);
    }

    [Fact]
    public void A_club_with_a_bed_and_no_opener_is_left_exactly_as_they_were()
    {
        // The bed-alone beat is the one number the billing varies for them,
        // and it is a REAL wait in front of a read the staging chain awaits
        // - so the night does not touch it.
        foreach (var cue in Enum.GetValues<ShowCue>())
        {
            Assert.Equal(
                ShowBilling.For(cue).BedAloneSeconds,
                ShowBilling.For(cue, ShowDrama.Finish(0.004)).BedAloneSeconds,
                6);
        }
    }

    // --- Helpers ---------------------------------------------------------------------------

    /// <summary>A time tonight, as the running order is handed them - UTC, because that is what it is given.</summary>
    private static DateTime At(int hour, int minute) => new(2026, 10, 10, hour, minute, 0, DateTimeKind.Utc);

    /// <summary>
    /// A library on a folder of this test's own.
    ///
    /// StingerLibrary.Shared settles its folder once for the life of the
    /// process, which is no use to a test that needs takes of its own - and
    /// a folder of its own rather than the club's "VoiceLines", so nothing
    /// here can meet an index.json another test left behind.
    /// </summary>
    private static StingerLibrary Library() =>
        StingerLibrary.ForTests(Path.Combine(AppPaths.Root, "VoiceLines-ident"), TimeSpan.Zero);

    /// <summary>
    /// A voice profile on one engine. The name is the ElevenLabs voice's,
    /// which is the only one of the three that comes off VoiceName - see
    /// StingerLibrary.VoiceNameFor.
    /// </summary>
    private static VoiceProfile Voice(VoiceEngine engine, string name) => new()
    {
        Engine = engine,
        VoiceName = engine == VoiceEngine.ElevenLabs ? name : string.Empty,
    };
}
