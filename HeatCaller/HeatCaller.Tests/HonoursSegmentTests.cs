namespace HeatCaller.Tests;

/// <summary>
/// The honours: the badges a race unlocked, read out as a segment of the
/// recap by a third voice (RaceHonours, and the sequence in
/// RaceControlControl.AfterRace that speaks it).
///
/// WHAT IS ACTUALLY BEING TESTED
///
/// Nothing here plays anything. AudioBus is silent for the whole run
/// (TestSetup) and none of this needs a sound card or a model: the segment
/// is three strings built out of a list of unlocks, and the hand-over is
/// two positions in that list. So what can be checked is exactly the part
/// that goes wrong quietly.
///
/// THE FOUR THINGS THAT MUST NOT REGRESS
///
///   1. NOTHING, WHERE THERE IS NOTHING. A throw to a third voice who then
///      has nothing to go over is the worst single thing this feature can
///      do, and a race unlocking no badges is the commonest case there is.
///
///   2. NOTHING IS SAID TWICE IN A NIGHT. The throw and the hand-back are
///      templated - that is what makes them survive the AI being off - and
///      the price of a template is repetition.
///
///   3. NO BADGE IS SAID TWICE EITHER. The segment REPLACES the individual
///      "Billy Bob has earned Nail-Biter" calls for that race. Both, and
///      the room hears every badge twice four minutes apart.
///
///   4. IT WORKS WITH THE MODEL UNREACHABLE. Nothing in the segment asks
///      the brain for anything, and the two ways out of the recap sequence
///      that have no recap at all still speak it.
/// </summary>
public sealed class HonoursSegmentTests
{
    /// <summary>Seeded, so a test that wants a particular phrasing twice can have it.</summary>
    private static ContinuityVoice Voice() => new(seed: 20261010);

    /// <summary>One badge, earned, by one driver - as AchievementEvaluator hands it over.</summary>
    private static AchievementUnlock Badge(string driver, string badge) =>
        new(
            driver,
            driver,
            new AchievementDefinition($"id-{badge}", badge, $"{badge}, earned.", AchievementCategory.Race, "star"),
            string.Empty);

    /// <summary>One race's badges, for the tests that are not about reading them.</summary>
    private static readonly AchievementUnlock[] OneBadge = { Badge("Billy Bob", "Nail-Biter") };

    private static int Occurrences(string text, string find)
    {
        var count = 0;
        var at = text.IndexOf(find, StringComparison.Ordinal);

        while (at >= 0)
        {
            count++;
            at = text.IndexOf(find, at + find.Length, StringComparison.Ordinal);
        }

        return count;
    }

    // ------------------------------------------------------------------
    // Nothing, where there is nothing
    // ------------------------------------------------------------------

    [Fact]
    public void A_race_that_unlocked_nothing_gets_no_segment_at_all()
    {
        // The whole house rule in one test. "And for tonight's honours,
        // over to Jane" followed by Jane having nothing to say is worse
        // than silence, and it is the case that would happen most often.
        Assert.Null(RaceHonours.Read(Array.Empty<AchievementUnlock>()));
        Assert.Null(RaceHonours.Read(null));

        Assert.Empty(RaceHonours.Segment(Array.Empty<AchievementUnlock>(), "Jane", Voice()));
        Assert.Empty(RaceHonours.Segment(null, "Jane", Voice()));
    }

    [Fact]
    public void A_badge_with_no_driver_or_no_name_is_not_read_out()
    {
        // "Billy Bob picks up" and "unlocks Nail-Biter" both read over a PA
        // as the app having broken rather than as somebody having won
        // something, so neither half is said without the other.
        Assert.Null(RaceHonours.Read(new[] { Badge("   ", "Nail-Biter") }));
        Assert.Null(RaceHonours.Read(new[] { Badge("Billy Bob", "   ") }));

        // And one good one among the bad still gets read.
        var read = RaceHonours.Read(new[] { Badge("  ", "Hat-Trick"), Badge("Jane Doe", "Personal Best") });

        Assert.NotNull(read);
        Assert.Equal(1, read!.Value.Badges);
        Assert.Contains("Jane Doe", read.Value.Said);
        Assert.DoesNotContain("Hat-Trick", read.Value.Said);
    }

    // ------------------------------------------------------------------
    // Who got what
    // ------------------------------------------------------------------

    [Fact]
    public void A_drivers_badges_are_one_thought_and_not_one_call_each()
    {
        // This is the whole difference between the segment and the
        // individual calls it replaces: two badges for one driver as two
        // announcements is the room hearing Billy Bob's name twice in six
        // seconds.
        var read = RaceHonours.Read(new[]
        {
            Badge("Billy Bob", "Nail-Biter"),
            Badge("Billy Bob", "Hat-Trick"),
            Badge("Jane Doe", "Personal Best"),
        });

        Assert.NotNull(read);
        Assert.Equal(3, read!.Value.Badges);
        Assert.Contains("Billy Bob picks up Nail-Biter and Hat-Trick.", read.Value.Said);
        Assert.Contains("Jane Doe takes Personal Best.", read.Value.Said);
        Assert.Equal(1, Occurrences(read.Value.Said, "Billy Bob"));
    }

    [Fact]
    public void Three_or_more_badges_for_one_driver_are_said_as_a_list_a_person_would_say()
    {
        var read = RaceHonours.Read(new[]
        {
            Badge("Billy Bob", "Nail-Biter"),
            Badge("Billy Bob", "Hat-Trick"),
            Badge("Billy Bob", "Clean Sweep"),
        });

        Assert.Contains("Nail-Biter, Hat-Trick and Clean Sweep", read!.Value.Said);
    }

    [Fact]
    public void The_same_three_words_are_not_used_for_every_driver_in_one_breath()
    {
        // What a room notices about a read is "picks up, picks up, picks
        // up, picks up" - fixed by varying the words WITHIN the sentence,
        // which a rotation keyed on the whole night cannot do.
        var read = RaceHonours.Read(new[]
        {
            Badge("Alice", "One"),
            Badge("Bob", "Two"),
            Badge("Carol", "Three"),
            Badge("Dave", "Four"),
        });

        Assert.NotNull(read);
        Assert.Contains("Alice picks up One.", read!.Value.Said);
        Assert.Contains("Bob takes Two.", read.Value.Said);
        Assert.Contains("Carol adds Three.", read.Value.Said);
        Assert.Contains("Dave collects Four.", read.Value.Said);
    }

    [Fact]
    public void A_big_night_is_capped_rather_than_read_out_as_a_list()
    {
        var crowd = Enumerable.Range(0, 7).Select(i => Badge($"Driver {i}", $"Badge {i}")).ToList();
        var read = RaceHonours.Read(crowd);

        Assert.NotNull(read);
        Assert.Equal(RaceHonours.MostDrivers, read!.Value.Badges);
        Assert.Contains("Driver 3", read.Value.Said);
        Assert.DoesNotContain("Driver 4", read.Value.Said);

        var hoarder = Enumerable.Range(0, 5).Select(i => Badge("Billy Bob", $"Badge {i}")).ToList();
        var second = RaceHonours.Read(hoarder);

        Assert.Equal(RaceHonours.MostEach, second!.Value.Badges);
        Assert.DoesNotContain("Badge 3", second.Value.Said);
    }

    [Fact]
    public void The_count_the_throw_promises_is_the_count_the_room_then_hears()
    {
        // Saying "seven to hand out" and then reading four is the one way
        // this segment could be caught lying by anybody listening, so the
        // read reports what it NAMES and not what the store holds.
        var crowd = Enumerable.Range(0, 7).Select(i => Badge($"Driver {i}", $"Badge {i}")).ToList();
        var turns = RaceHonours.Segment(crowd, "Jane", Voice());

        Assert.Equal(3, turns.Count);
        Assert.DoesNotContain("7 to hand out", turns[0].Line);
        Assert.Equal(RaceHonours.MostDrivers, Occurrences(turns[1].Line, "Driver "));
    }

    [Fact]
    public void One_badge_shared_by_two_drivers_is_one_honour_in_the_count()
    {
        // HEARD ON A CLUB NIGHT, 11 Oct 2026. The count was the number of
        // AWARDS - driver-and-badge pairs - where a room counts the number
        // of BADGES. The two differ whenever one badge goes to more than
        // one driver, and the date-based badges do that constantly:
        // Weekend Warrior and Back to Back fire for everybody who raced
        // both days. So the announcer promised three and the room then
        // heard two badge names, which is the one way this segment can be
        // caught lying by anybody listening.
        var read = RaceHonours.Read(new[]
        {
            Badge("Billy Bob", "Back to Back"),
            Badge("Billy Bob", "Weekend Warrior"),
            Badge("Asher", "Weekend Warrior"),
        });

        Assert.NotNull(read);

        // Two: Back to Back and Weekend Warrior. Three awards, two honours.
        Assert.Equal(2, read!.Value.Badges);

        // And the read itself is unchanged - it is still per driver, so
        // the shared badge is named against both of them. That is right:
        // Asher did win it.
        Assert.Contains("Billy Bob picks up Back to Back and Weekend Warrior.", read.Value.Said);
        Assert.Contains("Asher takes Weekend Warrior.", read.Value.Said);
        Assert.Equal(2, Occurrences(read.Value.Said, "Weekend Warrior"));

        // The throw promises what the room is about to hear.
        var turns = RaceHonours.Segment(
            new[]
            {
                Badge("Billy Bob", "Back to Back"),
                Badge("Billy Bob", "Weekend Warrior"),
                Badge("Asher", "Weekend Warrior"),
            },
            "Eric",
            Voice());

        Assert.Equal(3, turns.Count);
        Assert.DoesNotContain("3 to hand out", turns[0].Line);
    }

    [Fact]
    public void The_same_badge_for_everybody_is_still_one_honour()
    {
        // The limit of the rule above: a date-based badge on a six-car
        // grid is one honour, however many names it is read against.
        var everybody = Enumerable.Range(0, 6)
            .Select(i => Badge($"Driver {i}", "Weekend Warrior"))
            .ToList();

        var read = RaceHonours.Read(everybody);

        Assert.NotNull(read);
        Assert.Equal(1, read!.Value.Badges);

        // Capped at MostDrivers names, as any read is - the badge is still
        // on the others' records, it is just not read out.
        Assert.Equal(RaceHonours.MostDrivers, Occurrences(read.Value.Said, "Driver "));
    }

    // ------------------------------------------------------------------
    // The hand-over, which is the point
    // ------------------------------------------------------------------

    [Fact]
    public void The_segment_is_a_throw_a_read_and_a_hand_back_in_that_order()
    {
        var turns = RaceHonours.Segment(OneBadge, "Jane", Voice());

        Assert.Equal(3, turns.Count);

        // FromCoHost is "somebody else is saying this", not "the co-host
        // is" - see RaceHonours.Segment. The announcer throws, the reader
        // reads, the announcer comes back.
        Assert.False(turns[0].FromCoHost);
        Assert.True(turns[1].FromCoHost);
        Assert.False(turns[2].FromCoHost);

        Assert.Contains("Jane", turns[0].Line);
        Assert.Contains("Billy Bob", turns[1].Line);
        Assert.Contains("Jane", turns[2].Line);
        Assert.Contains("Nail-Biter", turns[1].Line);
    }

    [Fact]
    public void The_voice_changes_hands_twice_so_the_beat_lands_at_both()
    {
        // The beat and the nudge of the bed are what make a hand-over sound
        // like a hand-over rather than like one voice with a setting
        // changed halfway through - see CommentaryExchange.HandoffBeat. The
        // positions are worked out by the file whose business that is.
        var turns = RaceHonours.Segment(OneBadge, "Jane", Voice());

        Assert.Equal(new[] { 1, 2 }, CommentaryExchange.Handoffs(turns).ToArray());
    }

    [Fact]
    public void With_nobody_named_the_throw_is_not_a_hand_over_to_nobody()
    {
        // A format string handed a blank name produces "over to , who has
        // the honours", which is why the unnamed phrasings are their own
        // pool. Nothing here invents a name.
        var turns = RaceHonours.Segment(OneBadge, "   ", Voice());

        Assert.Equal(3, turns.Count);
        Assert.DoesNotContain("over to", turns[0].Line);
        Assert.DoesNotContain("  ", turns[0].Line);
        Assert.DoesNotContain(" .", turns[0].Line);

        // And nobody is thanked who was never named.
        Assert.DoesNotContain("Thank you,", turns[2].Line);
        Assert.DoesNotContain("  ", turns[2].Line);
    }

    // ------------------------------------------------------------------
    // Never the same words twice in a night
    // ------------------------------------------------------------------

    [Fact]
    public void Picking_the_lines_is_not_saying_them()
    {
        // The rotation is told at the point the words go out, which for
        // this segment is RaceControlControl.PlanHonours - see the note
        // there about why it is told at the pick and nowhere later.
        var voice = Voice();
        var turns = RaceHonours.Segment(OneBadge, "Jane", voice);

        Assert.Equal(3, turns.Count);
        Assert.Equal(0, voice.Heard);
        Assert.False(voice.AlreadyHeard(turns[0].Line));

        voice.Said(turns[0].Line);

        Assert.True(voice.AlreadyHeard(turns[0].Line));
    }

    [Fact]
    public void The_throw_and_the_hand_back_are_never_the_same_sentence_twice_in_a_night()
    {
        // Run the night out: every race that unlocks a badge gets a throw
        // and a hand-back nobody has heard yet, until there are none left -
        // and then the segment is dropped whole rather than half said.
        var voice = Voice();
        var throws = new HashSet<string>(StringComparer.Ordinal);
        var backs = new HashSet<string>(StringComparer.Ordinal);
        var races = 0;

        while (true)
        {
            var turns = RaceHonours.Segment(OneBadge, "Jane", voice);

            if (turns.Count == 0)
            {
                break;
            }

            Assert.Equal(3, turns.Count);
            Assert.True(throws.Add(turns[0].Line), $"The throw \"{turns[0].Line}\" was heard twice in one night.");
            Assert.True(backs.Add(turns[2].Line), $"The hand-back \"{turns[2].Line}\" was heard twice in one night.");

            // As PlanHonours does it: the announcer's own two turns are
            // remembered, and the read in the middle is not - it is built
            // from the badges and is different every race.
            voice.Said(turns[0].Line);
            voice.Said(turns[2].Line);

            races++;

            Assert.True(races < 60, "The rotation never runs out, so this test would never finish.");
        }

        // Sixteen is eight ways of throwing crossed with the two tails, and
        // four thanks crossed with four things to add on the way back. Any
        // fewer and a club with a badge in most of their races hears no
        // honours at all from somewhere in the middle of the evening, with
        // nothing on the page that could explain it.
        Assert.True(races >= 16, $"Only {races} races' worth of phrasings - the rotation runs dry on a club night.");
    }

    [Fact]
    public void A_new_race_day_starts_the_phrasings_again()
    {
        var voice = Voice();
        var races = 0;

        while (RaceHonours.Segment(OneBadge, "Jane", voice) is { Count: 3 } turns && races < 60)
        {
            voice.Said(turns[0].Line);
            voice.Said(turns[2].Line);
            races++;
        }

        Assert.Empty(RaceHonours.Segment(OneBadge, "Jane", voice));

        voice.NewMeeting("the-next-race-day");

        Assert.Equal(3, RaceHonours.Segment(OneBadge, "Jane", voice).Count);
    }

    // ------------------------------------------------------------------
    // With the model unreachable
    // ------------------------------------------------------------------

    [Fact]
    public void Nothing_in_the_honours_needs_the_model()
    {
        // The whole segment, built out of nothing but a list of unlocks and
        // a rotation: no CommentaryService, no brain, no allowance, no
        // network - which is why it is still there on the evening the AI is
        // switched off at the wall and the month's allowance has gone.
        var turns = RaceHonours.Segment(OneBadge, "Jane", Voice());

        Assert.Equal(3, turns.Count);
        Assert.All(turns, turn => Assert.False(string.IsNullOrWhiteSpace(turn.Line)));
    }

    // ------------------------------------------------------------------
    // What a crowd reads, and what a club ticks
    // ------------------------------------------------------------------

    [Fact]
    public void The_crowd_screen_has_its_own_name_for_the_honours()
    {
        Assert.Equal("Achievements Earned", ShowBlock.CaptionFor(SegmentKind.BadgesEarned));

        // One table, not two - OnAirTreatment.Words comes here. The Big
        // Screen's band and anything else asking the question get the same
        // answer, which they did not when there were two tables.
        Assert.Equal("Achievements Earned", OnAirTreatment.Words(SegmentKind.BadgesEarned));
        Assert.NotEqual(OnAirTreatment.Unnamed, OnAirTreatment.Words(SegmentKind.BadgesEarned));

        // And NOT the milestones segment's name. A crowd reading "Club
        // Honours" while a voice reads out tonight's badges would be told
        // the name of the other segment.
        Assert.NotEqual(ShowBlock.CaptionFor(SegmentKind.Honours), ShowBlock.CaptionFor(SegmentKind.BadgesEarned));

        // Every segment a club can put the music under has a name a crowd
        // can read, which is the rule the new one has to keep as well.
        Assert.All(ShowSegments.Candidates, kind => Assert.NotEqual(OnAirTreatment.Unnamed, OnAirTreatment.Words(kind)));
    }

    [Fact]
    public void The_honours_can_be_ticked_for_the_shows_music_like_any_other_segment()
    {
        Assert.Contains(SegmentKind.BadgesEarned, ShowSegments.Candidates);

        // By NAME in the saved setting, so inserting a member into
        // SegmentKind cannot re-label what a club ticked last season.
        var saved = ShowSegments.Save(new[] { SegmentKind.BadgesEarned });

        Assert.Contains("BadgesEarned", saved);
        Assert.Contains(SegmentKind.BadgesEarned, ShowSegments.Parse(saved));

        // Not ticked by default: a segment added later arrives without
        // music in front of it, which is the rule ShowSegments.Default is
        // written the way round it is for.
        Assert.DoesNotContain(SegmentKind.BadgesEarned, ShowSegments.Parse(null));

        // And the tick box has something to say on it - the page builds its
        // caption out of the budget's own table.
        Assert.Equal("Achievements earned", CommentaryBudget.Spec(SegmentKind.BadgesEarned).Title);
        Assert.False(string.IsNullOrWhiteSpace(CommentaryBudget.Spec(SegmentKind.BadgesEarned).What));
    }

    [Fact]
    public void The_honours_have_one_switch_and_only_one()
    {
        // Essential, and not because the segment is important: it is what
        // keeps the Commentary page from building a SECOND tick box for it
        // out of the budget's table, when it already has one in the
        // Broadcast card where the reader's name and voice are set. Two
        // switches for the same thing in different places is how a settings
        // page starts lying to people, which is what SetOff says.
        Assert.Equal(SegmentTier.Essential, CommentaryBudget.Spec(SegmentKind.BadgesEarned).Tier);

        var budget = new CommentaryBudget();

        budget.SetOff(SegmentKind.BadgesEarned, true);

        Assert.False(budget.IsOff(SegmentKind.BadgesEarned));
        Assert.DoesNotContain(SegmentKind.BadgesEarned, CommentaryBudget.ParseSwitches("BadgesEarned"));
    }

    [Fact]
    public void The_honours_cost_the_month_nothing()
    {
        // Nothing asks the brain for this one, so nothing reserves against
        // it - the same arrangement ContinuityShow.HandOver has, which
        // bills itself as "Coming Up" without taking a line off the night.
        // The row exists because the table insists every segment declare
        // what it costs.
        var spec = CommentaryBudget.Spec(SegmentKind.BadgesEarned);

        Assert.True(spec.Speaks);
        Assert.False(spec.CanBeExchange);
        Assert.Equal(TimeSpan.Zero, spec.MinGap);
    }

    // ------------------------------------------------------------------
    // Where it sits in the recap's sequence
    // ------------------------------------------------------------------

    /// <summary>
    /// The repo, found by walking up from the test binary to check.cmd -
    /// the same trick RecapBroadcastTests, NavRailTests and InstallerTests
    /// use.
    /// </summary>
    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    /// <summary>The recap sequence, read as text - see the note on the first test that does it.</summary>
    private static string AfterRaceSource => File.ReadAllText(
        Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "RaceControlControl.AfterRace.cs"));

    [Fact]
    public void The_honours_are_a_segment_of_the_recaps_show_and_come_before_the_interview()
    {
        // Pinned by reading the source rather than by driving the sequence,
        // which needs a model, a session and a sound device - the same
        // bargain RecapBroadcastTests strikes for the position of the
        // recap's own show. What is being held is an ORDER of three lines
        // in one method, which is exactly what reading the file can see.
        //
        // The order matters twice over. Inside the show, because that is
        // what makes the honours a segment of the recap rather than an
        // announcement with music behind it. In FRONT of the interview,
        // because the interview and the sign-off are written by the model
        // and may never arrive - and the badges were held back from the
        // individual calls on the strength of this running.
        var source = AfterRaceSource;

        var showOpened = source.IndexOf("opening = treatment.StartAsync(SegmentKind.RaceRecap", StringComparison.Ordinal);
        var honours = source.IndexOf("await SpeakHonoursAsync(honoursSegment, honoursLines, show, nextRace.Token);", StringComparison.Ordinal);
        // The recap show's interview is the Team Principal's, in the booth
        // (18 Oct 2026). The Technical Lead's pit lane word is at the flag,
        // before the break and outside the show, so it is not this one.
        var interview = source.IndexOf("await SpeakInterviewAsync(principalTask, nextRace.Token", StringComparison.Ordinal);

        Assert.True(showOpened > 0, "The recap no longer opens its show the way this test reads it.");
        Assert.True(honours > 0, "The recap sequence no longer speaks the honours the way this test reads it.");
        Assert.True(interview > 0, "The recap sequence no longer speaks the interview the way this test reads it.");

        Assert.True(
            honours > showOpened,
            "The honours are spoken before the recap's show is open, so they are an announcement rather than a segment of it.");

        Assert.True(
            interview > honours,
            "The honours now follow the booth interview, which can be skipped for being late - and then the badges are "
            + "lost, because they were held back from the individual calls. Put them back in front of it.");
    }

    [Fact]
    public void The_honours_still_go_out_on_the_two_nights_there_is_no_recap()
    {
        // The model unreachable, and the model with nothing to say. Both
        // return out of the sequence before the recap's show is ever
        // opened, and both have to speak the honours on the way - this is
        // the whole claim that the segment survives the AI being off.
        Assert.Equal(2, Occurrences(AfterRaceSource, "await HonoursOnTheirOwnAsync();"));
    }

    [Fact]
    public void A_badge_held_back_for_the_segment_is_not_also_called_out_on_its_own()
    {
        // The room must not hear every badge twice, four minutes apart.
        // AwardAchievements decides, and it decides by having a plan in
        // hand: with one, it returns before the individual calls.
        var source = AfterRaceSource;

        var decided = source.IndexOf("_honours = PlanHonours(unlocks);", StringComparison.Ordinal);
        var calledOut = source.IndexOf("_announcer.NotifyAchievement(unlock.Transponder, unlock.Badge.Name);", StringComparison.Ordinal);

        Assert.True(decided > 0, "AwardAchievements no longer builds the honours segment the way this test reads it.");
        Assert.True(calledOut > decided, "The individual badge calls no longer follow the decision the way this test reads it.");

        var returned = source.IndexOf("return;", decided, StringComparison.Ordinal);

        Assert.True(
            returned > decided && returned < calledOut,
            "Badges held back for the honours segment fall through to the individual calls as well, so the room hears "
            + "every badge twice.");
    }
}
