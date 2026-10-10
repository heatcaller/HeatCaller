namespace HeatCaller.Tests;

/// <summary>
/// Continuity: the bridging lines, the rotation that keeps them from
/// becoming six words a crowd stops hearing, and the running orders that
/// put two or three things inside one show.
///
/// WHAT IS ACTUALLY BEING TESTED
///
/// Nothing here plays anything. AudioBus is silent for the whole run
/// (TestSetup) and none of this needs a sound card: a continuity line is a
/// string built out of the programme, and a running order is a list of
/// items with a gap written on it. So what can be checked is exactly the
/// part that goes wrong quietly - what gets said, what never gets said, and
/// what happens when the app does not know something.
///
/// THE THREE THINGS THAT MUST NOT REGRESS
///
///   1. NOTHING IS INVENTED. A "still to come" for a round that is not on
///      the programme is a promise made over a PA in the club's own voice
///      that the evening then breaks. Every method that could name a round
///      has a test here for the case where there is no round to name.
///
///   2. NOTHING IS SAID TWICE. Templated lines are the whole point of
///      continuity - they survive the AI being off and the allowance being
///      spent - and the price of a template is repetition.
///
///   3. A STRANDED BRIDGE IS NEVER HEARD. "Still to come, the A-main" with
///      nothing after it is worse than silence, and ShowBlock.Fit is what
///      takes it off. The running orders here are shaped so that it can.
/// </summary>
public sealed class ContinuityTests : IDisposable
{
    /// <summary>A folder of its own: the programme store saves itself as rounds are added.</summary>
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>What the programme has left, for the tests that are not about reading it.</summary>
    private static readonly IReadOnlyList<UpcomingRound> NextUp = new[]
    {
        new UpcomingRound("A-Main", "Box Stock", 10),
    };

    // ------------------------------------------------------------------
    // The lines
    // ------------------------------------------------------------------

    [Fact]
    public void A_look_back_names_the_round_that_has_just_run_and_whose_it_was()
    {
        var line = Line(CommentaryContinuity.LookBack(new RoundJustRun("B-Main", "Box Stock"), Voice()));

        Assert.Contains("the B-Main for Box Stock", line);
    }

    [Fact]
    public void A_main_is_called_the_main_and_a_heat_is_not_called_the_heat()
    {
        // Grammar read off the label the club typed, not a fact worked out
        // about the round. "The A-Main" is how a main is spoken; "the Heat
        // 1" is how a machine speaks.
        var main = Line(CommentaryContinuity.LookBack(new RoundJustRun("A-Main", "Open"), Voice()));
        var heat = Line(CommentaryContinuity.LookBack(new RoundJustRun("Heat 1", "Open"), Voice()));

        Assert.Contains("the A-Main for Open", main);
        Assert.Contains("Heat 1 for Open", heat);
        Assert.DoesNotContain("the Heat 1", heat);
    }

    [Fact]
    public void A_round_with_no_class_against_it_is_named_on_its_own()
    {
        // A club night that files nothing against a class. The line is
        // shorter; it is not wrong.
        var line = Line(CommentaryContinuity.LookBack(new RoundJustRun("Heat 4", string.Empty), Voice()));

        Assert.Contains("Heat 4", line);
        Assert.DoesNotContain(" for ", line);
    }

    [Fact]
    public void Nothing_is_looked_back_on_when_nothing_has_been_filed()
    {
        Assert.Null(CommentaryContinuity.LookBack(null, Voice()));
    }

    [Fact]
    public void A_round_with_no_label_is_never_named()
    {
        // "That was round four" would be this file inventing what the club
        // calls it, so it says nothing at all.
        Assert.Null(CommentaryContinuity.LookBack(new RoundJustRun("   ", "Open"), Voice()));
    }

    [Fact]
    public void A_still_to_come_names_what_is_next_on_the_programme()
    {
        var line = Line(CommentaryContinuity.StillToCome(NextUp, null, Voice()));

        Assert.Contains("the A-Main for Box Stock", line);
    }

    [Fact]
    public void Nothing_is_promised_when_the_programme_has_nothing_left()
    {
        // The whole house rule in one test. A "still to come" for a round
        // that is not in the programme is the worst thing this could ship.
        Assert.Null(CommentaryContinuity.StillToCome(Array.Empty<UpcomingRound>(), null, Voice()));
        Assert.Null(CommentaryContinuity.StillToCome(null, TimeSpan.FromMinutes(4), Voice()));
        Assert.Null(CommentaryContinuity.FirstUp(Array.Empty<UpcomingRound>(), null, Voice()));
    }

    [Fact]
    public void The_estimate_is_only_said_where_the_host_knows_it()
    {
        // Handed in, never worked out - how long a club night runs against
        // its schedule is WhereTimeGoes's question, and a bridge that
        // guessed at it would be inventing the one number an announcer gets
        // held to.
        var voice = Voice();

        for (var i = 0; i < 12; i++)
        {
            if (CommentaryContinuity.StillToCome(NextUp, null, voice) is not { } line)
            {
                break;
            }

            voice.Said(line);

            Assert.DoesNotContain("minute", line);
        }
    }

    [Fact]
    public void A_race_a_minute_off_is_never_announced_as_one_minutes_away()
    {
        // The singular trap. A special case for it would be a sentence
        // nobody ever reads back, so under two minutes the estimate is
        // simply left off - the room can see the grid forming anyway.
        var line = Line(CommentaryContinuity.StillToCome(NextUp, TimeSpan.FromSeconds(70), Voice()));

        Assert.DoesNotContain("minute", line);
    }

    [Fact]
    public void Every_way_of_saying_what_is_next_is_used_once_and_then_it_goes_quiet()
    {
        // Six openings, by four tails - the bare sentence, three estimates
        // and the car count - is thirty ways to say one thing. All thirty
        // go out, each exactly once, and then the line is not said again
        // tonight rather than said again in words already heard.
        var voice = Voice();
        var heard = new List<string>();

        for (var i = 0; i < 40; i++)
        {
            if (CommentaryContinuity.StillToCome(NextUp, TimeSpan.FromMinutes(4), voice) is not { } line)
            {
                break;
            }

            voice.Said(line);
            heard.Add(line);
        }

        // Counted into a local first: an Assert.Equal whose right-hand side
        // is a collection's own size is what the xunit analysers object to.
        var distinct = heard.Distinct(StringComparer.Ordinal).Count();

        Assert.Equal(30, heard.Count);
        Assert.Equal(heard.Count, distinct);
        Assert.Contains(heard, line => line.Contains("4 minutes", StringComparison.Ordinal));
        Assert.Contains(heard, line => line.Contains("10 cars", StringComparison.Ordinal));
    }

    [Fact]
    public void A_label_somebody_typed_badly_is_cleaned_before_it_is_said()
    {
        // Every label and class name in here was typed by somebody. These
        // lines do not reach a model, so an injected instruction is not the
        // risk - but a round label with a line break in it is read out over
        // a PA exactly as typed, and Field is already the one guard.
        var line = Line(CommentaryContinuity.LookBack(
            new RoundJustRun("A-Main\n<tag>", "Box\tStock"),
            Voice()));

        Assert.DoesNotContain("\n", line);
        Assert.DoesNotContain("\t", line);
        Assert.DoesNotContain("<", line);
        Assert.Contains("the A-Main tag for Box Stock", line);
    }

    [Fact]
    public void A_hand_back_and_a_close_of_the_night_both_have_words_of_their_own()
    {
        Assert.NotNull(CommentaryContinuity.BackShortly(Voice()));
        Assert.Contains("tonight", Line(CommentaryContinuity.ThatIsTheLot(Voice())));
    }

    // ------------------------------------------------------------------
    // Variety, and never the same words twice
    // ------------------------------------------------------------------

    [Fact]
    public void Every_phrasing_is_heard_before_any_is_heard_twice()
    {
        // A bag, not a dice. A dice rolls the same number twice often
        // enough that a crowd notices, and "it is random" is no comfort to
        // the club who heard it.
        var voice = Voice();
        var ran = new RoundJustRun("B-Main", "Open");
        var heard = new List<string>();

        for (var i = 0; i < 5; i++)
        {
            var line = Line(CommentaryContinuity.LookBack(ran, voice));

            voice.Said(line);
            heard.Add(line);
        }

        var distinct = heard.Distinct(StringComparer.Ordinal).Count();

        Assert.Equal(heard.Count, distinct);
        Assert.Null(CommentaryContinuity.LookBack(ran, voice));
    }

    [Fact]
    public void Picking_a_line_is_not_saying_it()
    {
        // A bridge the gap turned out to have no room for was never heard,
        // and burning a phrasing on it would be the same quiet loss
        // SaidTonight.Mark is written the long way round to avoid. So the
        // rotation is told at the point the words go to the announcer.
        var voice = Voice();
        var ran = new RoundJustRun("B-Main", "Open");

        var first = Line(CommentaryContinuity.LookBack(ran, voice));

        Assert.Equal(0, voice.Heard);
        Assert.False(voice.AlreadyHeard(first));

        voice.Said(first);

        Assert.Equal(1, voice.Heard);
        Assert.True(voice.AlreadyHeard(first));
    }

    [Fact]
    public void The_rotation_survives_the_tick_that_asks_for_a_new_meeting_twice_a_second()
    {
        // The bug of the day, twice over. NewMeeting is called from the
        // host's half-second tick, not once a night - anything cleared
        // there unguarded is cleared sixty times a minute, and a rotation
        // reshuffled that often always hands out whatever a fresh shuffle
        // starts with. The club would get the same six words all evening
        // with nothing in any log to say why.
        var voice = Voice();

        voice.NewMeeting("day-1");

        var line = Line(CommentaryContinuity.ThatIsTheLot(voice));

        voice.Said(line);

        for (var i = 0; i < 60; i++)
        {
            voice.NewMeeting("day-1");
        }

        Assert.True(voice.AlreadyHeard(line));
        Assert.Equal(1, voice.Heard);
    }

    [Fact]
    public void A_new_night_is_last_night_s_lines_again()
    {
        var voice = Voice();

        voice.NewMeeting("day-1");

        var line = Line(CommentaryContinuity.ThatIsTheLot(voice));

        voice.Said(line);
        voice.NewMeeting("day-2");

        Assert.False(voice.AlreadyHeard(line));
        Assert.Equal(0, voice.Heard);
    }

    [Fact]
    public void The_director_starts_the_continuity_s_night_with_the_rest_of_the_night()
    {
        // Wired into the same NewMeeting as the budget and the facts, and
        // guarded the same way - otherwise the rotation would be the one
        // piece of the layer that the tick could wipe.
        var settings = new AppSettings();
        var director = new CommentaryDirector(new CommentaryService(ProfileStore.Load(), settings), settings);

        director.NewMeeting("day-1");

        var line = Line(CommentaryContinuity.ThatIsTheLot(director.Continuity));

        director.Continuity.Said(line);

        for (var i = 0; i < 60; i++)
        {
            director.NewMeeting("day-1");
        }

        Assert.True(director.Continuity.AlreadyHeard(line));

        director.NewMeeting("day-2");

        Assert.False(director.Continuity.AlreadyHeard(line));
    }

    [Fact]
    public void The_night_around_a_block_comes_back_empty_when_nothing_is_wired()
    {
        // A half-fed segment that invents the other half is worse than one
        // that stays quiet, so an unwired reader is "nothing known" and
        // every bridge that would have used it is simply not said.
        var settings = new AppSettings();
        var director = new CommentaryDirector(new CommentaryService(ProfileStore.Load(), settings), settings);

        var facts = director.ContinuityNow();

        Assert.Null(facts.JustRan);
        Assert.Empty(facts.Upcoming);
        Assert.Null(facts.InAbout);
    }

    [Fact]
    public void A_reader_that_throws_is_nothing_to_say_and_not_a_silent_announcer()
    {
        // The rule ReadNightProgress and Gather already follow: a store
        // caught mid-save must not take the announcer down with it.
        var settings = new AppSettings();
        var director = new CommentaryDirector(new CommentaryService(ProfileStore.Load(), settings), settings);

        var console = new List<string>();

        director.Status += console.Add;
        director.JustRan = () => throw new InvalidOperationException("mid-save");
        director.Upcoming = () => throw new InvalidOperationException("mid-save");

        var facts = director.ContinuityNow();

        Assert.Null(facts.JustRan);
        Assert.Empty(facts.Upcoming);
        Assert.Equal(2, console.Count);
    }

    // ------------------------------------------------------------------
    // The running orders
    // ------------------------------------------------------------------

    [Fact]
    public void Between_races_a_segment_is_bridged_at_both_ends()
    {
        var order = Order(Segment(SegmentKind.RaceRecap, "and what a finish that was"), Between());

        Assert.Equal(3, order.Items.Count);

        // The look-back goes in FRONT: a broadcast says what it was and
        // then talks about it.
        Assert.True(order.Items[0].IsContinuity);
        Assert.Contains("the B-Main for Box Stock", Words(order.Items[0]));

        Assert.Equal(SegmentKind.RaceRecap, order.Items[1].Kind);
        Assert.False(order.Items[1].IsContinuity);

        Assert.Contains("the A-Main for Box Stock", Words(order.Items[2]));

        // The one mark in the block, and it is the gear change: the evening
        // stops being about the race that has run and starts being about
        // the next one.
        Assert.True(order.Items[2].PunctuateBefore);
        Assert.False(order.Items[1].PunctuateBefore);

        Assert.Equal(SegmentKind.RaceRecap, order.Kind);
    }

    [Fact]
    public void The_gap_a_block_is_fitted_to_is_the_time_until_the_next_race()
    {
        // The one thing that makes a block fit between two heats instead of
        // running into the start of one.
        Assert.Equal(TimeSpan.FromMinutes(4), Order(Segment(SegmentKind.RaceRecap, "the recap"), Between()).Gap);

        // And where the host does not know, the block's own ninety-second
        // cap and the race-state watcher are what bound it.
        var unknown = new ContinuityFacts(new RoundJustRun("B-Main", "Box Stock"), NextUp, null);

        Assert.Equal(ShowBlock.NoLimit, Order(Segment(SegmentKind.RaceRecap, "the recap"), unknown).Gap);
    }

    [Fact]
    public void The_cold_open_looks_forward_and_never_back()
    {
        // Nothing has run. An opener that said "that was" would be lying in
        // its first breath.
        var order = Order(Segment(SegmentKind.ColdOpen, "good evening and welcome"), Between());

        Assert.Equal(2, order.Items.Count);
        Assert.Equal(SegmentKind.ColdOpen, order.Items[0].Kind);
        Assert.False(order.Items[0].IsContinuity);

        Assert.Contains("the A-Main for Box Stock", Words(order.Items[1]));
        Assert.DoesNotContain("Still to come", Words(order.Items[1]));
    }

    [Fact]
    public void The_coming_up_segment_is_not_followed_by_the_line_it_already_is()
    {
        // That segment IS the still-to-come, written by the model from the
        // same rounds. Saying both would be the same thing twice in a row
        // in two voices.
        var order = Order(Segment(SegmentKind.ComingUpNext, "coming up, the A-Main"), Between());

        Assert.Equal(2, order.Items.Count);
        Assert.True(order.Items[0].IsContinuity);
        Assert.Equal(SegmentKind.ComingUpNext, order.Items[1].Kind);
    }

    [Fact]
    public void The_sign_off_closes_the_night_and_nothing_else_does()
    {
        // "That is the lot for tonight" only where the app has already
        // decided the night is over. A programme with nothing unrun in it
        // is not the same thing: a club adds a bonus main after tea, and an
        // announcer who has closed the evening has to take it back.
        var over = new ContinuityFacts(new RoundJustRun("A-Main", "Box Stock"), Array.Empty<UpcomingRound>(), null);

        var signOff = Order(Segment(SegmentKind.SignOffNumbers, "that is 412 laps tonight"), over);

        Assert.Equal(3, signOff.Items.Count);
        Assert.Contains("tonight", Words(signOff.Items[2]));

        var recap = Order(Segment(SegmentKind.RaceRecap, "and what a finish"), over);

        Assert.Equal(2, recap.Items.Count);
        Assert.True(recap.Items[0].IsContinuity);
        Assert.Equal(SegmentKind.RaceRecap, recap.Items[1].Kind);
    }

    [Fact]
    public void Back_in_a_moment_stands_in_where_the_programme_cannot_name_the_next_race()
    {
        // A freeform race, or one of the many clubs who never enter a
        // programme at all. It is true and it promises nothing.
        var freeform = new ContinuityFacts(
            new RoundJustRun("Heat 3", "Open"),
            Array.Empty<UpcomingRound>(),
            TimeSpan.FromMinutes(3));

        var order = Order(Segment(SegmentKind.RaceRecap, "the recap"), freeform);

        Assert.Equal(3, order.Items.Count);
        Assert.DoesNotContain("Heat", Words(order.Items[2]));
    }

    [Fact]
    public void Nothing_to_bridge_to_is_the_single_segment_path_s_job()
    {
        // Null is not a failure and it is not rare. The caller then uses
        // RunAsync, which hands the words back when there is no music -
        // the fallback a block cannot offer, because a block speaks its
        // items itself either way.
        Assert.Null(ContinuityShow.Around(
            Segment(SegmentKind.RaceRecap, "the recap"),
            ContinuityFacts.Unknown,
            Voice(),
            Nothing,
            NothingTurn,
            NothingLine));
    }

    [Fact]
    public void A_two_hander_goes_in_as_a_two_hander_so_the_block_can_pace_it()
    {
        // Queued as one lump it came out sounding concatenated. As parts,
        // the block puts a beat and a nudge of the bed at the change of
        // voice, which is the difference between two people talking and one
        // voice reading out a transcript of two people talking.
        var said = new SpokenSegment(SegmentKind.RaceRecap, "Sam held on. He did, just.", new[]
        {
            new ExchangeTurn(false, "Sam held on."),
            new ExchangeTurn(true, "He did, just."),
        });

        var item = Order(said, Between()).Items[1];

        Assert.True(item.IsExchange);
        Assert.Equal(2, item.Parts.Count);
        Assert.Equal("Sam held on.", item.Parts[0].Words);
        Assert.True(item.Parts[1].FromCoHost);
    }

    [Fact]
    public void A_line_is_marked_as_heard_at_the_point_it_goes_to_the_announcer()
    {
        var voice = Voice();
        var queued = new List<string>();

        var order = ContinuityShow.Around(
            Segment(SegmentKind.RaceRecap, "the recap"),
            Between(),
            voice,
            () => queued.Add("the recap"),
            NothingTurn,
            queued.Add);

        Assert.NotNull(order);
        Assert.Equal(0, voice.Heard);

        foreach (var part in order.Items.SelectMany(item => item.Parts))
        {
            part.Say();
        }

        Assert.Equal(3, queued.Count);
        Assert.Equal("the recap", queued[1]);
        Assert.Equal(2, voice.Heard);
    }

    // ------------------------------------------------------------------
    // A stranded bridge is never heard
    // ------------------------------------------------------------------

    [Fact]
    public void A_look_back_is_dropped_with_the_segment_it_was_introducing()
    {
        // The running order is a look-back, a segment far too long for the
        // gap, and a hand-over. The segment closes the block, the hand-over
        // never gets a chance - and the look-back, now at the end of what
        // fitted, is taken back off. The room hears nothing rather than
        // "that was the B-main" and then the club's playlist.
        var order = Order(Segment(SegmentKind.RaceRecap, ManyWords(300)), Between());

        var fit = ShowBlock.Fit(order.Items, TimeSpan.FromSeconds(14), ShowBlockCost.None);

        Assert.Empty(fit.Items);
        Assert.Equal(3, fit.Dropped.Count);
    }

    [Fact]
    public void A_hand_over_of_two_bridges_would_fit_perfectly_and_say_nothing()
    {
        // Why the forward half of a hand-over is a spoken item and not a
        // bridge. Written as two bridges it passes the fitting and then
        // comes back out of it empty, which is the sort of thing that only
        // shows up as "it went quiet" weeks later.
        var bothBridges = new[]
        {
            ShowItem.Bridge("That was the B-Main for Box Stock.", Nothing),
            ShowItem.Bridge("Still to come, the A-Main for Box Stock.", Nothing),
        };

        Assert.Empty(ShowBlock.Fit(bothBridges, TimeSpan.FromSeconds(40), ShowBlockCost.None).Items);
    }

    [Fact]
    public void A_hand_over_needs_both_ends_or_it_is_not_a_hand_over()
    {
        var ran = new RoundJustRun("B-Main", "Box Stock");

        Assert.Null(ContinuityShow.HandOver(new ContinuityFacts(null, NextUp, null), Voice(), NothingLine));
        Assert.Null(ContinuityShow.HandOver(
            new ContinuityFacts(ran, Array.Empty<UpcomingRound>(), null),
            Voice(),
            NothingLine));
    }

    [Fact]
    public void A_hand_over_is_a_programme_with_no_segment_in_it_and_it_survives_the_fitting()
    {
        // The order a club hears on the evening the allowance has run out
        // and the AI is switched off at the wall. No model, no budget, and
        // still two sentences and a beat between them.
        var block = ContinuityShow.HandOver(Between(), Voice(), NothingLine);

        Assert.NotNull(block);
        Assert.Equal(2, block.Items.Count);
        Assert.True(block.Items[0].IsContinuity);
        Assert.False(block.Items[1].IsContinuity);
        Assert.Equal(SegmentKind.ComingUpNext, block.Kind);
        Assert.Equal(TimeSpan.FromMinutes(4), block.Gap);

        var fit = ShowBlock.Fit(block.Items, TimeSpan.FromSeconds(25), ShowBlockCost.None);

        Assert.Equal(2, fit.Items.Count);
        Assert.Empty(fit.Dropped);
    }

    [Fact]
    public void A_gap_too_short_for_a_block_hears_no_continuity_at_all()
    {
        // Under ShowBlock.ShortestUsefulGap nothing goes out - no music, no
        // items, nothing said. A bridge into eight seconds would be a
        // promise said over the top of a countdown.
        var block = ContinuityShow.HandOver(Between(), Voice(), NothingLine);

        Assert.NotNull(block);
        Assert.Empty(ShowBlock.Fit(block.Items, TimeSpan.FromSeconds(8), ShowBlockCost.None).Items);
    }

    // ------------------------------------------------------------------
    // Reading the night off the club's own stores
    // ------------------------------------------------------------------

    [Fact]
    public void What_is_still_to_come_is_read_off_the_programme_in_running_order()
    {
        var rounds = RoundDefinitionStore.Load();

        rounds.Add(new RoundDefinition { ClassName = "Box Stock", RoundLabel = "Q1 Heat 1", AssignedTransponders = { "A", "B" } });
        rounds.Add(new RoundDefinition { ClassName = "Box Stock", RoundLabel = "Q1 Heat 2" });
        rounds.Add(new RoundDefinition { ClassName = "Open", RoundLabel = "A-Main" });

        var (next, inAbout) = CommentaryReaders.Upcoming(rounds, inAbout: TimeSpan.FromMinutes(4));

        Assert.Equal(3, next.Count);
        Assert.Equal("Q1 Heat 1", next[0].Label);
        Assert.Equal("Box Stock", next[0].ClassName);
        Assert.Equal(2, next[0].Entries);

        // A main built from the heats has no roster until the heats are in,
        // and is still a round that is still to come.
        Assert.Equal(0, next[2].Entries);
        Assert.Equal(TimeSpan.FromMinutes(4), inAbout);
    }

    [Fact]
    public void A_round_already_run_and_the_round_in_hand_are_both_left_out()
    {
        // The round in hand matters more than it looks. A round is only
        // "recorded" once its race has been FILED, so between the flag and
        // the filing the race just watched is still the first unrecorded
        // round on the programme - and "still to come, Heat 2" about the
        // heat the room has just seen is the worst thing a bridge can do.
        var rounds = RoundDefinitionStore.Load();

        var first = new RoundDefinition { ClassName = "Open", RoundLabel = "Heat 1" };
        var second = new RoundDefinition { ClassName = "Open", RoundLabel = "Heat 2" };
        var third = new RoundDefinition { ClassName = "Open", RoundLabel = "Heat 3" };

        rounds.Add(first);
        rounds.Add(second);
        rounds.Add(third);
        rounds.MarkRecorded(first.Id, "session-1");

        var (next, _) = CommentaryReaders.Upcoming(rounds, skipRoundId: second.Id);

        Assert.Equal("Heat 3", Assert.Single(next).Label);
    }

    [Fact]
    public void A_round_the_club_never_named_is_never_promised()
    {
        var rounds = RoundDefinitionStore.Load();

        rounds.Add(new RoundDefinition { ClassName = "Open", RoundLabel = "   " });
        rounds.Add(new RoundDefinition { ClassName = "Open", RoundLabel = "Heat 2" });

        var (next, _) = CommentaryReaders.Upcoming(rounds);

        Assert.Equal("Heat 2", Assert.Single(next).Label);
    }

    [Fact]
    public void Only_tonight_s_rounds_are_promised()
    {
        // Scoped to the race day in hand, as the qualifying report learned
        // to be on 25 Sep: unscoped it would promise a round from a meeting
        // three weeks ago.
        var rounds = RoundDefinitionStore.Load();

        var tonight = new RoundDefinition { ClassName = "Open", RoundLabel = "Heat 1" };
        var lastWeek = new RoundDefinition { ClassName = "Open", RoundLabel = "Heat 9" };

        rounds.Add(tonight);
        rounds.Add(lastWeek);

        var (next, _) = CommentaryReaders.Upcoming(rounds, Scope(tonight.Id));

        Assert.Equal("Heat 1", Assert.Single(next).Label);
    }

    [Fact]
    public void What_has_just_run_is_the_newest_race_filed_against_a_round()
    {
        // Read from what has been filed and not from the programme's order:
        // a club runs races out of order all evening, so "the one before
        // the next one" is not an answer.
        var rounds = RoundDefinitionStore.Load();

        var heat = new RoundDefinition { ClassName = "Box Stock", RoundLabel = "Q2 Heat 1" };
        var main = new RoundDefinition { ClassName = "Box Stock", RoundLabel = "B-Main" };

        rounds.Add(heat);
        rounds.Add(main);

        var history = SessionHistoryStore.FromSessions(new[]
        {
            Filed(main.Id, Now.AddMinutes(-2)),
            Filed(heat.Id, Now.AddMinutes(-14)),
        });

        var ran = CommentaryReaders.JustRan(rounds, history, now: Now);

        Assert.NotNull(ran);
        Assert.Equal("B-Main", ran.Value.Label);
        Assert.Equal("Box Stock", ran.Value.ClassName);
    }

    [Fact]
    public void A_race_from_before_the_break_is_not_what_has_just_run()
    {
        // A club breaks for tea and comes back an hour later. The first
        // thing the announcer says must not be about the race before the
        // sandwiches - and on a night abandoned after one heat, the next
        // day must not open with "that was Q1 Heat 1" because that is
        // still the newest thing filed.
        var rounds = RoundDefinitionStore.Load();

        var heat = new RoundDefinition { ClassName = "Open", RoundLabel = "Heat 1" };

        rounds.Add(heat);

        var history = SessionHistoryStore.FromSessions(new[] { Filed(heat.Id, Now.AddMinutes(-90)) });

        Assert.Null(CommentaryReaders.JustRan(rounds, history, now: Now));
    }

    [Fact]
    public void A_race_filed_against_another_race_day_is_not_tonight_s()
    {
        var rounds = RoundDefinitionStore.Load();

        var heat = new RoundDefinition { ClassName = "Open", RoundLabel = "Heat 1" };
        var other = new RoundDefinition { ClassName = "Open", RoundLabel = "Heat 9" };

        rounds.Add(heat);
        rounds.Add(other);

        var history = SessionHistoryStore.FromSessions(new[] { Filed(other.Id, Now.AddMinutes(-2)) });

        Assert.Null(CommentaryReaders.JustRan(rounds, history, Scope(heat.Id), Now));
    }

    [Fact]
    public void Practice_and_a_race_with_no_round_behind_it_are_nothing_to_look_back_on()
    {
        var rounds = RoundDefinitionStore.Load();

        var heat = new RoundDefinition { ClassName = "Open", RoundLabel = "Heat 1" };

        rounds.Add(heat);

        var practice = SessionHistoryStore.FromSessions(new[]
        {
            Filed(heat.Id, Now.AddMinutes(-2), RaceSessionType.Practice),
        });

        Assert.Null(CommentaryReaders.JustRan(rounds, practice, now: Now));

        var freeform = SessionHistoryStore.FromSessions(new[] { Filed(null, Now.AddMinutes(-2)) });

        Assert.Null(CommentaryReaders.JustRan(rounds, freeform, now: Now));
    }

    [Fact]
    public void The_two_readers_together_make_a_hand_over()
    {
        // End to end, with no model and no allowance anywhere near it:
        // the club's own stores in, and a programme out.
        var rounds = RoundDefinitionStore.Load();

        var heat = new RoundDefinition { ClassName = "Box Stock", RoundLabel = "B-Main" };
        var main = new RoundDefinition { ClassName = "Box Stock", RoundLabel = "A-Main", AssignedTransponders = { "A", "B" } };

        rounds.Add(heat);
        rounds.Add(main);
        rounds.MarkRecorded(heat.Id, "session-1");

        var history = SessionHistoryStore.FromSessions(new[] { Filed(heat.Id, Now.AddMinutes(-1)) });

        var facts = new ContinuityFacts(
            CommentaryReaders.JustRan(rounds, history, now: Now),
            CommentaryReaders.Upcoming(rounds, inAbout: TimeSpan.FromMinutes(5)).Rounds,
            TimeSpan.FromMinutes(5));

        var block = ContinuityShow.HandOver(facts, Voice(), NothingLine);

        Assert.NotNull(block);
        Assert.Contains("the B-Main for Box Stock", Words(block.Items[0]));
        Assert.Contains("the A-Main for Box Stock", Words(block.Items[1]));
    }

    // ------------------------------------------------------------------
    // Scaffolding
    // ------------------------------------------------------------------

    /// <summary>The evening these tests are set on. UTC, as the filed records are.</summary>
    private static readonly DateTime Now = new(2026, 10, 10, 19, 30, 0, DateTimeKind.Utc);

    /// <summary>
    /// Seeded, so a test that wants the same phrasing twice gets it.
    /// Nothing in the app passes a seed - a club's evening should not open
    /// with the same words as every other club's.
    /// </summary>
    private static ContinuityVoice Voice() => new(seed: 1704);

    /// <summary>An ordinary between-races night: a B-main just run, an A-main to come, four minutes off.</summary>
    private static ContinuityFacts Between() =>
        new(new RoundJustRun("B-Main", "Box Stock"), NextUp, TimeSpan.FromMinutes(4));

    /// <summary>A one-voice segment the director has had written.</summary>
    private static SpokenSegment Segment(SegmentKind kind, string text) =>
        new(kind, text, Array.Empty<ExchangeTurn>());

    /// <summary>The running order for a segment, which these tests always expect to get.</summary>
    private static ShowBlock Order(SpokenSegment said, ContinuityFacts facts)
    {
        var order = ContinuityShow.Around(said, facts, Voice(), Nothing, NothingTurn, NothingLine);

        Assert.NotNull(order);

        return order;
    }

    /// <summary>What an item says, as one piece of text.</summary>
    private static string Words(ShowItem item) => string.Join(" ", item.Parts.Select(part => part.Words));

    /// <summary>
    /// Enough words to be sure of blowing a short gap. The estimate is
    /// SponsorCopy.SpokenLength, which is a hundred and fifty words a
    /// minute - see ShowItem.Estimate.
    /// </summary>
    private static string ManyWords(int howMany) => string.Join(" ", Enumerable.Repeat("word", howMany));

    /// <summary>A round id as the host scopes a race day's rounds.</summary>
    private static IReadOnlySet<string> Scope(params string[] ids) => ids.ToHashSet(StringComparer.Ordinal);

    /// <summary>A race the club has filed, with a result in it.</summary>
    private static SessionRecord Filed(string? roundId, DateTime endedUtc, RaceSessionType type = RaceSessionType.Race) =>
        new()
        {
            SessionType = type,
            RoundId = roundId,
            StartedAtUtc = endedUtc.AddMinutes(-5),
            EndedAtUtc = endedUtc,
            Results = { new SessionResultEntry { Position = 1, Transponder = "A", DriverName = "Sam" } },
        };

    /// <summary>A line the test does not care about being said.</summary>
    private static void Nothing()
    {
    }

    /// <inheritdoc cref="Nothing"/>
    private static void NothingTurn(ExchangeTurn turn)
    {
    }

    /// <inheritdoc cref="Nothing"/>
    private static void NothingLine(string line)
    {
    }

    /// <summary>A line that must have been said, as a non-null string.</summary>
    private static string Line(string? line)
    {
        Assert.NotNull(line);

        return line;
    }
}
