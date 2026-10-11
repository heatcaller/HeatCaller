namespace HeatCaller.Tests;

/// <summary>
/// The show as a BLOCK: several items in order, with punctuation between
/// them, fitted to the gap it was given, and the club's music counted down
/// on every way out.
///
/// WHAT IS ACTUALLY BEING TESTED. AudioBus is silent for the whole run
/// (TestSetup), so nothing here can be listened to. What can be checked is
/// the production's decisions - which items fitted the gap, which came off
/// first, where a mark went, where the bed went for one - and above all the
/// thing the fifth bug hunt found twice in a week: that the counter which
/// silences the club's playlist comes back down on every single path out of
/// a block, including the paths nobody meant to take.
///
/// The pieces are written here as wav files, as the other show tests do it,
/// so each test says in its own body what shape of music it is about and
/// none of this depends on an mp3 decoder.
/// </summary>
public sealed class ShowBlockTests : IDisposable
{
    private readonly TempAppData _data = new();

    /// <summary>What this test subscribed to ShowOnAir, to be taken off again - see Edges.</summary>
    private readonly List<Watcher> _watching = new();

    /// <summary>Low on purpose, as in ShowAudioTests: every piece below is seconds long, and this keeps the arrays small.</summary>
    private const int Rate = 8000;

    public ShowBlockTests()
    {
        ShowOnAir.ResetForTests();
        ShowMusic.Reload();
    }

    public void Dispose()
    {
        // All three are static, so a test that left a show open, a loaded
        // piece of audio or a handler behind would reach into every test
        // after it - and into a real app run started by the layout test in
        // the same process.
        foreach (var watcher in _watching)
        {
            ShowOnAir.PlayingChanged -= watcher.Saw;
        }

        ShowOnAir.ResetForTests();
        ShowMusic.Reload();
        _data.Dispose();
    }

    // --- Fitting a running order to a gap ---------------------------------------------------

    [Fact]
    public void A_whole_running_order_goes_out_when_nothing_is_near_it()
    {
        // The cold open and the sign-off: no race anywhere, so the only cap
        // is the block's own.
        var fit = ShowBlock.Fit(new[] { Item(4), Item(4), Item(4) }, ShowBlock.NoLimit, ShowBlockCost.None);

        Assert.Equal(3, fit.Items.Count);
        Assert.Empty(fit.Dropped);
        Assert.True(fit.Whole);
    }

    [Fact]
    public void A_running_order_too_long_for_the_gap_is_cut_at_a_whole_item()
    {
        // The whole point. Ninety seconds of programme between two heats is
        // good; the same ninety seconds with the next race twenty seconds
        // away is a disaster, and the answer is never to cut an item in half
        // - SponsorCopy.Fit takes the same view of a sentence.
        var fit = ShowBlock.Fit(
            new[] { Item(8), Item(8), Item(8) },
            TimeSpan.FromSeconds(25),
            ShowBlockCost.None);

        // 25 less the headroom leaves 22, which is two of them.
        Assert.Equal(2, fit.Items.Count);
        Assert.Single(fit.Dropped);
        Assert.False(fit.Whole);
    }

    [Fact]
    public void What_is_dropped_comes_off_the_END_of_the_running_order()
    {
        // A running order is an ORDER. Reshuffling one to fill a gap is how
        // it stops being one, so the tail goes and the front stays.
        var first = Item(8);
        var second = Item(8);
        var third = Item(8);

        var fit = ShowBlock.Fit(new[] { first, second, third }, TimeSpan.FromSeconds(25), ShowBlockCost.None);

        Assert.Same(first, fit.Items[0]);
        Assert.Same(second, fit.Items[1]);
        Assert.Same(third, Assert.Single(fit.Dropped));
    }

    [Fact]
    public void The_first_segment_goes_out_even_when_it_does_not_quite_fit()
    {
        // SponsorCopy.Fit's one rule that is not about room, and it is here
        // for a related reason: a club whose segments happen to run a few
        // seconds longer than tonight's gaps would otherwise never hear a
        // block at all, and would never be told why.
        var fit = ShowBlock.Fit(new[] { Item(18) }, TimeSpan.FromSeconds(16), ShowBlockCost.None);

        Assert.Single(fit.Items);
        Assert.Empty(fit.Dropped);
    }

    [Fact]
    public void An_item_in_a_different_league_from_the_gap_is_dropped_rather_than_overrunning()
    {
        // Where the exemption stops. Letting a ninety-second item out into a
        // twenty-second gap is the exact disaster the fitting exists to
        // prevent, and "the first one goes out anyway" must not become a way
        // of causing it. See ShowBlock.Overrun.
        var fit = ShowBlock.Fit(new[] { Item(90) }, TimeSpan.FromSeconds(20), ShowBlockCost.None);

        Assert.Empty(fit.Items);
        Assert.Single(fit.Dropped);
    }

    [Fact]
    public void A_gap_too_short_to_be_worth_a_block_gets_nothing_at_all()
    {
        // Not "the music and no programme" - nothing. A block in the last
        // breath before a start is the jingle this feature exists to stop
        // being.
        var fit = ShowBlock.Fit(
            new[] { Item(2), Item(2) },
            ShowBlock.ShortestUsefulGap - TimeSpan.FromSeconds(1),
            ShowBlockCost.None);

        Assert.Empty(fit.Items);
        Assert.Equal(2, fit.Dropped.Count);
    }

    [Fact]
    public void A_block_never_runs_past_its_own_cap_however_long_the_gap()
    {
        // The gap a caller knows about is a prediction, and an hour of
        // nothing is not a licence to broadcast for an hour.
        var many = Enumerable.Range(0, 40).Select(_ => Item(10)).ToArray();

        var fit = ShowBlock.Fit(many, ShowBlock.NoLimit, ShowBlockCost.None);

        Assert.True(
            fit.Spent <= ShowBlock.LongestBlock,
            $"the block came to {fit.Spent.TotalSeconds:0}s, past its own {ShowBlock.LongestBlock.TotalSeconds:0}s cap");

        Assert.True(fit.Dropped.Count > 0);
    }

    // --- Continuity: what comes off first ---------------------------------------------------

    [Fact]
    public void A_bridge_left_at_the_end_of_a_block_is_taken_back_off()
    {
        // "Still to come, the A-main" as the last thing in the block
        // promises something the block is not going to say, which is worse
        // than no bridge at all.
        var segment = Item(5);
        var bridge = Bridge(2);

        var fit = ShowBlock.Fit(new[] { segment, bridge }, ShowBlock.NoLimit, ShowBlockCost.None);

        Assert.Same(segment, Assert.Single(fit.Items));
        Assert.Same(bridge, Assert.Single(fit.Dropped));
    }

    [Fact]
    public void Two_bridges_at_the_end_both_come_off()
    {
        // A running order a caller is entitled to write, so the trim is a
        // loop and not one check.
        var segment = Item(5);

        var fit = ShowBlock.Fit(new[] { segment, Bridge(2), Bridge(2) }, ShowBlock.NoLimit, ShowBlockCost.None);

        Assert.Same(segment, Assert.Single(fit.Items));
        Assert.Equal(2, fit.Dropped.Count);
    }

    [Fact]
    public void A_bridge_whose_segment_did_not_fit_goes_with_it()
    {
        // The bridge fitted on its own and the thing it was bridging to did
        // not. Saying it anyway would have the block end on a promise.
        var fit = ShowBlock.Fit(
            new[] { Item(8), Bridge(2), Item(40) },
            TimeSpan.FromSeconds(25),
            ShowBlockCost.None);

        Assert.Single(fit.Items);
        Assert.Equal(2, fit.Dropped.Count);
    }

    [Fact]
    public void A_bridge_that_will_not_fit_does_not_close_the_block_behind_it()
    {
        // A bridge is not a segment: one that cannot be afforded is simply
        // not said, and what it was bridging is still worth having.
        var first = Item(6);
        var last = Item(4);

        var fit = ShowBlock.Fit(
            new[] { first, Bridge(40), last },
            TimeSpan.FromSeconds(30),
            ShowBlockCost.None);

        Assert.Equal(2, fit.Items.Count);
        Assert.Same(first, fit.Items[0]);
        Assert.Same(last, fit.Items[1]);
        Assert.Single(fit.Dropped);
    }

    [Fact]
    public void A_running_order_of_nothing_but_continuity_goes_out_as_nothing()
    {
        // The music would have opened to say "still to come" and then
        // stopped.
        var fit = ShowBlock.Fit(new[] { Bridge(2), Bridge(2) }, ShowBlock.NoLimit, ShowBlockCost.None);

        Assert.Empty(fit.Items);
        Assert.Equal(2, fit.Dropped.Count);
    }

    [Fact]
    public void An_item_with_nothing_to_say_is_left_out_and_the_rest_still_goes()
    {
        // An exchange the model wrote as a paragraph comes back unsplittable
        // (CommentaryExchange.Split returns nothing), and a block that ran
        // it would wait out its pacing for silence and then count it as a
        // segment the next one gets no room for.
        var empty = ShowItem.Exchange(Array.Empty<ExchangeTurn>(), (_, _) => { });
        var real = Item(4);

        var fit = ShowBlock.Fit(new[] { empty, real }, ShowBlock.NoLimit, ShowBlockCost.None);

        Assert.Same(real, Assert.Single(fit.Items));
        Assert.Same(empty, Assert.Single(fit.Dropped));
    }

    // --- What the staging costs the gap -----------------------------------------------------

    [Fact]
    public void The_music_s_own_time_comes_off_the_gap_before_the_items_do()
    {
        // Every second of the air, the opener and the bed's ending is a
        // second the club's playlist is silenced and nothing is being said.
        var order = new[] { Item(8), Item(8) };

        var dry = ShowBlock.Fit(order, TimeSpan.FromSeconds(25), ShowBlockCost.None);

        var dressed = ShowBlock.Fit(
            order,
            TimeSpan.FromSeconds(25),
            new ShowBlockCost(TimeSpan.FromSeconds(8), TimeSpan.Zero, TimeSpan.Zero));

        Assert.Equal(2, dry.Items.Count);
        Assert.Single(dressed.Items);
    }

    [Fact]
    public void The_mark_in_front_of_an_item_is_counted_into_what_it_costs()
    {
        // A sting is time on air. A block fitted without it overruns its gap
        // by one sting per mark, which is the arithmetic going the one way
        // it must not.
        var plain = new[] { Item(8), Item(8) };
        var marked = new[] { Item(8), Item(8, punctuate: true) };

        var cost = new ShowBlockCost(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(4));

        Assert.Equal(2, ShowBlock.Fit(plain, TimeSpan.FromSeconds(22), cost).Items.Count);
        Assert.Single(ShowBlock.Fit(marked, TimeSpan.FromSeconds(22), cost).Items);
    }

    [Fact]
    public void A_mark_in_front_of_the_first_item_costs_nothing_because_the_opener_is_the_mark()
    {
        // A sting on top of the opener is two flourishes in the same second,
        // so the staging does not play one there - and the fitting must not
        // charge for one either. Shown as an asymmetry, because that is what
        // it is: the SAME item fits at the front of the order and does not
        // fit behind something.
        var cost = new ShowBlockCost(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(4));

        var gap = TimeSpan.FromSeconds(14);

        Assert.Single(ShowBlock.Fit(new[] { Item(18, punctuate: true) }, gap, cost).Items);

        // Behind a one-second item it is charged for its mark, and 18 plus 4
        // is past even what the first-segment exemption allows.
        var behind = ShowBlock.Fit(new[] { Item(1), Item(18, punctuate: true) }, gap, cost);

        Assert.Single(behind.Items);
        Assert.Equal(TimeSpan.FromSeconds(1), behind.Spent);
    }

    [Fact]
    public void An_item_is_reckoned_at_the_same_speed_a_sponsor_s_read_is()
    {
        // Deliberately the same estimate and not a second answer: it is
        // already on the safe side, and the cost of being wrong is identical
        // in both places.
        const string words = "The A-main is next, and it is the one everybody came for.";

        Assert.Equal(
            SponsorCopy.SpokenLength(words),
            ShowItem.Spoken(words, () => { }).Estimate);
    }

    [Fact]
    public void A_caller_who_knows_better_than_the_reckoning_can_say_so()
    {
        var told = TimeSpan.FromSeconds(12);

        Assert.Equal(told, ShowItem.Spoken("Two words", () => { }, estimate: told).Estimate);
    }

    // --- The two-voice hand-over ------------------------------------------------------------

    [Fact]
    public void A_two_hander_costs_its_words_and_a_beat_at_the_hand_over()
    {
        // The beat is real time on air, so it is counted rather than quietly
        // spent - a block fitted without it overruns by one beat per change
        // of voice.
        var turns = new[]
        {
            new ExchangeTurn(false, "Sam has been within four tenths of Bill for six laps."),
            new ExchangeTurn(true, "And neither of them has put a wheel wrong."),
        };

        var item = ShowItem.Exchange(turns, (_, _) => { });

        var words = SponsorCopy.SpokenLength(turns[0].Line) + SponsorCopy.SpokenLength(turns[1].Line);

        Assert.Equal(words + CommentaryExchange.HandoffBeat, item.Estimate);
        Assert.True(item.IsExchange);
    }

    [Fact]
    public void A_one_voice_item_has_no_hand_over_to_pay_for()
    {
        var item = ShowItem.Spoken("One voice, one line.", () => { });

        Assert.Equal(SponsorCopy.SpokenLength("One voice, one line."), item.Estimate);
        Assert.False(item.IsExchange);
    }

    [Fact]
    public void The_hand_over_is_found_where_the_voice_changes_and_nowhere_else()
    {
        var turns = new[]
        {
            new ExchangeTurn(false, "First."),
            new ExchangeTurn(true, "Second."),
            new ExchangeTurn(true, "Still me."),
            new ExchangeTurn(false, "Back to you."),
        };

        Assert.Equal(new[] { 1, 3 }, CommentaryExchange.Handoffs(turns));
    }

    [Fact]
    public void One_voice_all_the_way_through_hands_over_to_nobody()
    {
        var turns = new[] { new ExchangeTurn(false, "One."), new ExchangeTurn(false, "Two.") };

        Assert.Empty(CommentaryExchange.Handoffs(turns));
    }

    [Fact]
    public void The_nudge_at_a_hand_over_is_nothing_like_the_lift_at_the_end()
    {
        // The lift means "the talking has stopped". A room given that in the
        // middle of a two-hander hears the segment end and then start again,
        // so the hand-over's nudge is a hair and the lift is a move.
        Assert.True(CommentaryExchange.HandoffNudge > 1f, "a nudge of nothing is not a nudge");
        Assert.True(
            CommentaryExchange.HandoffNudge < 1.3f,
            $"{CommentaryExchange.HandoffNudge:0.00} is a lift, not a hand-over - the room will hear the segment end");
    }

    // --- The bed as punctuation -------------------------------------------------------------

    [Fact]
    public async Task The_bed_swells_as_the_mark_for_a_club_with_no_sting()
    {
        // The club this matters most to: one bed, no sting file, which is
        // most of them. A swell has to work on its own merits rather than as
        // a poor relation of the sting.
        var clip = Clip("bed.wav", Join(Music(8), FadeOut(Music(2))));
        var bed = new ShowBed(clip);
        var gain = new SmoothGainSampleProvider(bed, 0f) { Target = 0.22f };

        ShowOnAir.Begin("Race Recap");

        var run = new ShowRun(
            Announcer(), _ => { }, new ShowSpeaking(), bed, gain, Task.CompletedTask, clip.Length, 0.22f, null);

        var underTheRead = run.BedTargetForTests;

        Assert.True(run.Swell(), "there was a bed, so there was something to swell");
        Assert.True(
            run.BedTargetForTests > underTheRead,
            "the bed did not come up for the mark - a block with no sting has nothing to punctuate with");

        run.Settle();

        Assert.Equal(underTheRead, run.BedTargetForTests, 3);

        await run.FinishAsync(cutShort: true);

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_show_with_no_bed_has_nothing_to_swell_and_says_so()
    {
        // A club with an opener and no bed. The block punctuates with a beat
        // of silence instead, which is what the false is for.
        ShowOnAir.Begin();

        var run = new ShowRun(
            Announcer(), _ => { }, new ShowSpeaking(), null, null, Task.CompletedTask, TimeSpan.Zero, 0.22f, null);

        Assert.False(run.Swell());
        Assert.False(run.Nudge());

        await run.FinishAsync();

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task The_swell_between_items_is_smaller_than_the_lift_at_the_end()
    {
        var clip = Clip("bed.wav", Join(Music(8), FadeOut(Music(2))));
        var bed = new ShowBed(clip);
        var gain = new SmoothGainSampleProvider(bed, 0f) { Target = 0.22f };

        ShowOnAir.Begin();

        var run = new ShowRun(
            Announcer(),
            _ => { },
            new ShowSpeaking(),
            bed,
            gain,
            Task.CompletedTask,
            clip.Length,
            0.22f,
            null,
            ShowBilling.For(ShowCue.TopOfTheNight));

        run.Swell();

        var swelled = run.BedTargetForTests;

        run.Nudge();

        var nudged = run.BedTargetForTests;

        run.Settle();

        await run.FinishAsync();

        Assert.True(
            run.BedTargetForTests > swelled,
            $"the mark's swell reached {swelled:0.000} and the ending's lift only {run.BedTargetForTests:0.000} - "
            + "a mark in the middle of a block must not sound like the end of one");

        Assert.True(nudged < swelled, "a hand-over is a hair, a mark is a move");

        Assert.False(ShowOnAir.Playing);
    }

    // --- A real block, staged ---------------------------------------------------------------

    [Fact]
    public async Task A_block_of_three_items_is_one_show_and_one_count()
    {
        // The whole limit being removed. Three items used to be three shows:
        // an opener in front of each, the playlist counted up and down three
        // times, and nowhere for anything to live in between.
        var treatment = Treatment(withOpener: true);

        var edges = Edges();

        var said = new List<string>();

        var result = await treatment.RunBlockAsync(Block(said, "one", "two", "three"));

        Assert.True(result.WithMusic);
        Assert.Equal(3, result.Said);
        Assert.Equal(new[] { "one", "two", "three" }, said);

        Assert.Equal(new[] { true, false }, edges.Seen);
        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_block_says_its_items_in_the_order_the_running_order_gave_them()
    {
        var treatment = Treatment(withOpener: false);

        var said = new List<string>();

        await treatment.RunBlockAsync(Block(said, "opening", "the recap", "still to come"));

        Assert.Equal(new[] { "opening", "the recap", "still to come" }, said);
        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task What_is_on_air_is_captioned_in_words_a_crowd_can_read()
    {
        // The Big Screen reads it off ShowOnAir while the show is on, so it
        // has to be set by the time the handler is told the show started.
        var treatment = Treatment(withOpener: false);

        string? onAir = null;

        void Watch(bool playing)
        {
            if (playing)
            {
                onAir = ShowOnAir.Caption;
            }
        }

        ShowOnAir.PlayingChanged += Watch;

        try
        {
            var said = new List<string>();

            await treatment.RunBlockAsync(
                new ShowBlock(new[] { ShowItem.Spoken("the recap", () => said.Add("the recap")) })
                {
                    Kind = SegmentKind.RaceRecap,
                });
        }
        finally
        {
            ShowOnAir.PlayingChanged -= Watch;
        }

        Assert.Equal("Race Recap", onAir);
        Assert.Null(ShowOnAir.Caption);
        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public void Every_segment_a_club_can_score_has_something_to_put_on_the_screen()
    {
        // A caption of null is a show with nothing to call itself, which is
        // fine in general and wrong for the eleven a club can actually tick:
        // those are the ones a crowd sees.
        foreach (var kind in ShowSegments.Candidates)
        {
            Assert.False(
                string.IsNullOrWhiteSpace(ShowBlock.CaptionFor(kind)),
                $"{kind} can be given the show treatment, so it needs words a crowd can read");
        }
    }

    [Fact]
    public async Task A_club_with_no_files_at_all_still_gets_the_block()
    {
        // No opener, no bed, no sting, no model, no budget. The programme is
        // the items and their order; the music is only how it is dressed.
        var treatment = Treatment(withOpener: false, withBed: false);

        var said = new List<string>();

        var result = await treatment.RunBlockAsync(Block(said, "one", "two"));

        Assert.False(result.WithMusic);
        Assert.Equal(2, result.Said);
        Assert.Equal(new[] { "one", "two" }, said);

        // And nothing was ever counted, so the club's playlist was never
        // silenced for a show that had no music to silence it for.
        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_club_who_turned_the_music_off_still_gets_the_block()
    {
        var treatment = Treatment(withOpener: true, theme: false);

        var said = new List<string>();

        var result = await treatment.RunBlockAsync(Block(said, "one", "two"));

        Assert.False(result.WithMusic);
        Assert.Equal(2, result.Said);
        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_gap_too_short_for_a_block_plays_nothing_and_says_nothing()
    {
        // Not "the music and no programme" - nothing at all. The club's
        // playlist is never even silenced, which is the whole of why the
        // gap is checked before anything is opened.
        var treatment = Treatment(withOpener: true);

        var said = new List<string>();

        var edges = Edges();

        var tight = await treatment.RunBlockAsync(
            new ShowBlock(new[] { ShowItem.Spoken("the recap", () => said.Add("the recap")) })
            {
                Gap = ShowBlock.ShortestUsefulGap - TimeSpan.FromSeconds(1),
            });

        Assert.False(tight.WentOut);
        Assert.Equal(1, tight.Dropped);
        Assert.Empty(said);

        Assert.Empty(edges.Seen);
        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_block_abandoned_mid_item_drops_the_rest_and_never_says_them()
    {
        // The race moved on. A "still to come" said after the race started
        // is worse than silence, so the unsaid items are DROPPED and never
        // queued for later.
        var treatment = Treatment(withOpener: false);

        var said = new List<string>();

        var order = new ShowBlock(new[]
        {
            // The first item abandons the block from inside itself, which is
            // the race-state watcher's own shape: something else notices and
            // calls in while an item is being said.
            ShowItem.Spoken("one", () =>
            {
                said.Add("one");
                treatment.AbandonBlock();
            }),
            ShowItem.Spoken("two", () => said.Add("two")),
            ShowItem.Spoken("three", () => said.Add("three")),
        });

        var result = await treatment.RunBlockAsync(order);

        Assert.True(result.Abandoned);

        // The first was queued, so the room heard it. The other two are
        // DROPPED and never queued for later: a "still to come" said after
        // the race started is worse than silence.
        Assert.Equal(new[] { "one" }, said);
        Assert.Equal(1, result.Said);
        Assert.Equal(2, result.Dropped);

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_block_the_next_race_cancels_gives_the_club_s_music_back()
    {
        // Race Control's own token, which is the seam that already exists.
        var treatment = Treatment(withOpener: false);

        using var cts = new CancellationTokenSource();

        var said = new List<string>();

        var order = new ShowBlock(new[]
        {
            ShowItem.Spoken("one", () =>
            {
                said.Add("one");
                cts.Cancel();
            }),
            ShowItem.Spoken("two", () => said.Add("two")),
        });

        var result = await treatment.RunBlockAsync(order, cts.Token);

        Assert.True(result.Abandoned);
        Assert.DoesNotContain("two", said);
        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task An_item_that_throws_does_not_take_the_block_or_the_music_down()
    {
        var treatment = Treatment(withOpener: false);

        var said = new List<string>();

        var order = new ShowBlock(new[]
        {
            ShowItem.Spoken("one", () => throw new InvalidOperationException("the voice queue fell over")),
            ShowItem.Spoken("two", () => said.Add("two")),
        });

        var result = await treatment.RunBlockAsync(order);

        // The one that threw did not go out; the one after it did.
        Assert.Equal(1, result.Said);
        Assert.Equal(new[] { "two" }, said);
        Assert.False(result.Abandoned);

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task Abandoning_when_no_block_is_running_does_nothing_at_all()
    {
        // Several race-state changes arrive together - a stop, then a flag,
        // then a reset - and most of them find nothing to abandon.
        var treatment = Treatment(withOpener: false);

        treatment.AbandonBlock();
        treatment.AbandonBlock();

        var said = new List<string>();

        var result = await treatment.RunBlockAsync(Block(said, "one"));

        Assert.Equal(1, result.Said);

        treatment.AbandonBlock();
        treatment.AbandonBlock();

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_block_handed_nothing_opens_nothing()
    {
        var treatment = Treatment(withOpener: true);

        var result = await treatment.RunBlockAsync(new ShowBlock(Array.Empty<ShowItem>()));

        Assert.False(result.WentOut);
        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task Every_way_a_block_can_end_gives_the_club_s_music_back()
    {
        // The one thing in this area that must never go wrong. A stuck
        // counter means the club's playlist never comes back for the rest of
        // the night, which is worse than anything else on this page - and
        // the fifth bug hunt found two separate ways into it in one week.
        var treatment = Treatment(withOpener: true);

        var said = new List<string>();

        // 1. The natural end.
        Assert.True((await treatment.RunBlockAsync(Block(said, "a", "b"))).WentOut);
        Assert.False(ShowOnAir.Playing);

        // 2. Abandoned from inside an item, which is the race-state
        // watcher's shape.
        var abandoned = await treatment.RunBlockAsync(new ShowBlock(new[]
        {
            ShowItem.Spoken("c", () => treatment.AbandonBlock()),
            ShowItem.Spoken("d", () => said.Add("d")),
        }));

        Assert.True(abandoned.Abandoned);
        Assert.False(ShowOnAir.Playing);

        // 3. Cancelled by the caller's own token before it begins, so the
        // show is called off on its way in.
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();

            var called = await treatment.RunBlockAsync(Block(said, "e"), cts.Token);

            Assert.False(called.WentOut);
        }

        Assert.False(ShowOnAir.Playing);

        // 4. Every item throws, so nothing is said and the music still comes
        // back.
        var broken = await treatment.RunBlockAsync(new ShowBlock(new[]
        {
            ShowItem.Spoken("f", () => throw new InvalidOperationException("nothing works tonight")),
        }));

        Assert.Equal(0, broken.Said);
        Assert.False(ShowOnAir.Playing);

        // 5. A gap too short to open one into, so nothing is counted at all.
        var tight = await treatment.RunBlockAsync(new ShowBlock(new[] { ShowItem.Spoken("g", () => said.Add("g")) })
        {
            Gap = TimeSpan.FromSeconds(2),
        });

        Assert.False(tight.WentOut);
        Assert.False(ShowOnAir.Playing);

        // 6. And no music at all, which never counts it in the first place.
        foreach (var file in Directory.GetFiles(ShowMusic.Folder))
        {
            File.Delete(file);
        }

        ShowMusic.Reload();

        var bare = await treatment.RunBlockAsync(Block(said, "h"));

        Assert.False(bare.WithMusic);
        Assert.True(bare.WentOut);
        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_two_hander_inside_a_block_is_said_a_turn_at_a_time()
    {
        // Each turn queued on its own, so the beat at the hand-over is a
        // real gap rather than a delay between two things the voice queue
        // then speaks back to back.
        var treatment = Treatment(withOpener: false);

        var turns = new[]
        {
            new ExchangeTurn(false, "Sam has been within four tenths of Bill for six laps."),
            new ExchangeTurn(true, "And neither of them has put a wheel wrong."),
        };

        var spoken = new List<string>();

        var result = await treatment.RunBlockAsync(new ShowBlock(new[]
        {
            ShowItem.Exchange(turns, (turn, _) => spoken.Add(turn.Line)),
        }));

        Assert.Equal(turns.Select(t => t.Line), spoken);
        Assert.Equal(1, result.Said);
        Assert.False(ShowOnAir.Playing);
    }

    // --- The club's third file --------------------------------------------------------------

    [Fact]
    public void A_sting_is_chosen_and_kept_beside_the_opener_and_the_bed()
    {
        var chosen = Wav(Path.Combine(_data.Folder, "chosen-sting.wav"), Music(1.5));

        var music = new ShowMusic(new AppSettings());

        Assert.True(music.Install(chosen, ShowPart.Sting, out var problem), problem ?? "it would not install");
        Assert.NotNull(music.StingPath);

        // Copied in under the well-known name rather than pointed at, so a
        // backup carries it to the next laptop - see ShowMusic.Install.
        Assert.Equal("sting.wav", Path.GetFileName(music.StingPath!));
        Assert.Contains("sting.wav", music.Describe(ShowPart.Sting));
    }

    [Fact]
    public void The_page_says_what_marks_the_breaks_when_there_is_no_sting()
    {
        // Measured rather than set, like everything else on that page: a
        // club has no other way to know that leaving the sting out costs
        // them nothing.
        ShowMusicFile("bed.wav", Join(Music(8), FadeOut(Music(2))));

        Assert.Contains("swell of the bed", new ShowMusic(new AppSettings()).Describe(ShowPart.Sting));
    }

    [Fact]
    public void The_page_says_so_when_there_is_no_bed_to_swell_either()
    {
        Assert.Contains("beat of silence", new ShowMusic(new AppSettings()).Describe(ShowPart.Sting));
    }

    [Fact]
    public void A_sting_longer_than_the_show_will_wait_out_is_said_to_be_so()
    {
        // Nothing checks what is in a club's file, and "sting.mp3" has
        // turned out to be a whole chorus before now.
        ShowMusicFile("sting.wav", Music(ShowBlock.LongestSting.TotalSeconds + 4));

        var said = new ShowMusic(new AppSettings()).Describe(ShowPart.Sting);

        Assert.Contains("carries on over it", said);
    }

    [Fact]
    public void A_sting_removed_leaves_the_opener_and_the_bed_where_they_were()
    {
        var opener = ShowMusicFile("opener.wav", Join(Music(8), FadeOut(Music(2))));
        var bed = ShowMusicFile("bed.wav", Join(Music(8), FadeOut(Music(2))));

        ShowMusicFile("sting.wav", Music(1.5));

        var music = new ShowMusic(new AppSettings());

        Assert.True(music.Remove(ShowPart.Sting));

        Assert.Null(music.StingPath);
        Assert.Equal(opener, music.OpenerPath);
        Assert.Equal(bed, music.BedPath);
    }

    // --- Staging a block for a test ---------------------------------------------------------

    /// <summary>
    /// A treatment with the club's music in place, on this test's own data
    /// folder, and the block's pacing in milliseconds rather than seconds.
    ///
    /// The pacing matters here. Nothing in a test queues a real voice, so
    /// every item would spend the whole of ShowPacing.OnAir.StartsWithin
    /// waiting for a voice that is never coming - which is seconds per item
    /// to find out which items fitted. See ShowPacing.
    /// </summary>
    private ShowTreatment Treatment(bool withOpener, bool withBed = true, bool theme = true)
    {
        if (withBed)
        {
            ShowMusicFile("bed.wav", Join(Music(8), FadeOut(Music(2))));
        }

        if (withOpener)
        {
            // Eight seconds of body and a two-second fade, which is the
            // shape ShowAudioTests proves the measurement reads properly.
            ShowMusicFile("opener.wav", Join(Music(8), FadeOut(Music(2))));
        }

        return new ShowTreatment(
            new AppSettings
            {
                ShowThemeEnabled = theme,

                // Set rather than measured, only so these tests do not sit
                // out the eight seconds the measured entry would be on a
                // piece this shape - exactly as ShowRunningOrderTests does
                // it, and for the same reason.
                ShowVoiceEntersMs = 200,
            },
            Announcer())
        {
            Pacing = ShowPacing.ForTests,
        };
    }

    /// <summary>An announcer the runs below can unsubscribe their ducking from. Nothing asks it to speak.</summary>
    private static AnnouncementService Announcer() =>
        new(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

    /// <summary>
    /// A running order of plain items, each of which writes its own name
    /// into <paramref name="said"/> when it is queued - which is how a test
    /// with no audio anywhere can tell what the room would have heard, and
    /// in what order.
    /// </summary>
    private static ShowBlock Block(List<string> said, params string[] items) =>
        new(items.Select(name => ShowItem.Spoken(name, () => said.Add(name))).ToArray());

    /// <summary>An item of roughly <paramref name="seconds"/> of words, for the fitting tests.</summary>
    private static ShowItem Item(double seconds, bool punctuate = false) =>
        ShowItem.Spoken("filler", () => { }, punctuateBefore: punctuate, estimate: TimeSpan.FromSeconds(seconds));

    /// <inheritdoc cref="Item"/>
    private static ShowItem Bridge(double seconds) =>
        ShowItem.Bridge("still to come", () => { }, estimate: TimeSpan.FromSeconds(seconds));

    /// <summary>
    /// Watches every change of "the show's music is playing", so a test can
    /// say how many times the club's playlist was silenced and given back.
    /// Two entries - true then false - is one show.
    /// </summary>
    private Watcher Edges()
    {
        var watcher = new Watcher();

        ShowOnAir.PlayingChanged += watcher.Saw;

        // Taken off in Dispose: a static event left holding a test's own
        // list would be called by every test after it.
        _watching.Add(watcher);

        return watcher;
    }

    /// <summary>What ShowOnAir told the world, in order - see Edges.</summary>
    private sealed class Watcher
    {
        private readonly List<bool> _seen = new();

        public IReadOnlyList<bool> Seen
        {
            get
            {
                lock (_seen)
                {
                    return _seen.ToArray();
                }
            }
        }

        public void Saw(bool playing)
        {
            lock (_seen)
            {
                _seen.Add(playing);
            }
        }
    }

    private static float[] Music(double seconds)
    {
        var frames = (int)(seconds * Rate);
        var samples = new float[frames];

        // Fixed seed: a test that measures must not depend on the draw.
        var wobble = new Random(7);

        for (var i = 0; i < frames; i++)
        {
            var level = 0.45 * (0.9 + 0.2 * wobble.NextDouble());
            samples[i] = (float)(level * Math.Sin(2 * Math.PI * 220 * i / Rate) * Math.Sqrt(2));
        }

        return samples;
    }

    private static float[] FadeOut(float[] piece)
    {
        var faded = new float[piece.Length];

        for (var i = 0; i < piece.Length; i++)
        {
            faded[i] = piece[i] * (1f - (float)i / piece.Length);
        }

        return faded;
    }

    private static float[] Join(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    private ShowClip Clip(string name, float[] samples)
    {
        var clip = ShowClip.Load(Wav(Path.Combine(AppPaths.Root, name), samples), out var problem);

        Assert.Null(problem);

        return clip!;
    }

    /// <summary>A piece in the club's own show music folder, where the treatment looks for it.</summary>
    private string ShowMusicFile(string name, float[] samples)
    {
        Directory.CreateDirectory(ShowMusic.Folder);

        return Wav(Path.Combine(ShowMusic.Folder, name), samples);
    }

    /// <summary>
    /// Writes a 16-bit PCM wav, by hand rather than through NAudio - a wav
    /// header is nine lines, and a test that writes its own file cannot be
    /// confused by what a writer decided to put in one.
    /// </summary>
    private static string Wav(string path, float[] samples)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var file = File.Create(path);
        using var write = new BinaryWriter(file);

        var bytes = samples.Length * 2;

        write.Write("RIFF".ToCharArray());
        write.Write(36 + bytes);
        write.Write("WAVE".ToCharArray());
        write.Write("fmt ".ToCharArray());
        write.Write(16);
        write.Write((short)1);
        write.Write((short)1);
        write.Write(Rate);
        write.Write(Rate * 2);
        write.Write((short)2);
        write.Write((short)16);
        write.Write("data".ToCharArray());
        write.Write(bytes);

        foreach (var sample in samples)
        {
            write.Write((short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue));
        }

        return path;
    }
}
