namespace HeatCaller.Tests;

/// <summary>
/// WHAT A SEGMENT UNDERTOOK, so a later one can come back to it (12 Oct
/// 2026) - see <see cref="NightPromises"/>.
///
/// WHAT THESE TESTS ARE MOSTLY ABOUT
///
/// Not the callback working. The callback working is two sentences of
/// string building and the first test covers it. What the rest of this
/// file is about is the ways a callback can be a LIE, because this is the
/// one class in the commentary layer that keeps a record of the broadcast
/// itself rather than deriving everything from the club's saved results -
/// and every lie available to it is quiet.
///
/// THE TEST WORTH READING FIRST is
/// <see cref="A_pace_note_is_never_paid_off_by_its_own_race"/>. A note
/// called on lap five saying Carl has just gone quickest, paid off by the
/// moment that says Carl set the fastest lap of that race, is the
/// announcer congratulating itself on reading out its own line - and it
/// would have happened on every single pace note, because they are the
/// same lap. It is the fault the whole green-flag rule exists for, it was
/// found by simulating the ledger rather than by reading it, and the first
/// draft of the class shipped it.
/// </summary>
public sealed class NightPromisesTests
{
    private static readonly DateTime Green = new(2026, 10, 12, 19, 0, 0, DateTimeKind.Utc);

    private const string Carl = "111";
    private const string Dave = "222";
    private const string Sam = "333";

    /// <summary>The names a club would have on file.</summary>
    private static string? NameFor(string transponder) => transponder switch
    {
        Carl => "Carl Dodd",
        Dave => "Dave Bellamy",
        Sam => "Sam Teague",
        _ => null,
    };

    /// <summary>
    /// A pack as the director would hand one over - the text is never
    /// read by this class, only the themes are.
    /// </summary>
    private static PackedFacts Pack(params string[] themes) =>
        new("some fact or other", new[] { "fact:1" }, 0) { Themes = themes };

    /// <summary>
    /// One moment, with the two times that matter kept apart: the green
    /// flag is what a promise is tested against and the flag is what
    /// NightMoments orders by.
    /// </summary>
    private static NightMoments.Moment Moment(
        NightMoments.Kind what,
        string transponder,
        DateTime green,
        string sessionId = "race-2",
        string beaten = "",
        long figureMs = 24_113) =>
        new(
            what,
            sessionId,
            "A-Main",
            transponder,
            NameFor(transponder) ?? string.Empty,
            Lap: 4,
            FigureMs: figureMs,
            Places: 5,
            Beaten: beaten,
            AtUtc: green.AddMinutes(8),
            GreenAtUtc: green);

    // ------------------------------------------------------------------
    //  It works
    // ------------------------------------------------------------------

    [Fact]
    public void A_driver_the_broadcast_pointed_at_is_come_back_to()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);

        var block = ledger.PayOff(
            new[] { Moment(NightMoments.Kind.LastLapLead, Carl, Green.AddMinutes(10)) },
            atAir.AddMinutes(14));

        Assert.NotNull(block);
        Assert.Contains("Carl Dodd was called on air earlier tonight", block!.Text);
        Assert.Contains("took the lead on the last lap", block.Text);

        // Themed on the subject, so the next segment does not also land on
        // Carl - the rule FactBlock.Theme states for colour.
        Assert.Equal(FactTheme.Driver(Carl), block.Theme);
    }

    [Fact]
    public void A_fight_names_both_sides_and_credits_whichever_delivered()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(
            SegmentKind.BattleWatch,
            Pack(FactTheme.Rivalry(Carl, Dave)),
            NameFor,
            bothVoices: true,
            atAir);

        // The moment is about the OTHER side of the fight, which is the
        // case the pay-off has to get right rather than the easy one.
        var block = ledger.PayOff(
            new[] { Moment(NightMoments.Kind.PhotoFinish, Dave, Green.AddMinutes(10), beaten: Carl, figureMs: 613) },
            atAir.AddMinutes(14));

        Assert.NotNull(block);
        Assert.Contains("The fight between", block!.Text);
        Assert.Contains("Carl Dodd", block.Text);
        Assert.Contains("Dave Bellamy won", block.Text);

        // Both voices were on it, which is an observation - the segment
        // went out as an exchange - and not a claim about what either of
        // them said. See the class summary on NightPromises.
        Assert.Contains("talked over by both voices", block.Text);
    }

    [Fact]
    public void One_voice_is_said_to_be_one_voice()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(SegmentKind.PaceNote, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);

        var block = ledger.PayOff(
            new[] { Moment(NightMoments.Kind.Charge, Carl, Green.AddMinutes(10)) },
            atAir.AddMinutes(14));

        Assert.NotNull(block);
        Assert.Contains("called on air", block!.Text);
        Assert.DoesNotContain("both voices", block.Text);
    }

    // ------------------------------------------------------------------
    //  The ways it would lie
    // ------------------------------------------------------------------

    /// <summary>
    /// THE ONE. See the class summary.
    ///
    /// The note goes out four minutes into a race that is still running.
    /// That race is then saved, and its fastest lap is the very lap the
    /// note was about. A moment's AtUtc is the FLAG, so from the ledger's
    /// side the race finished after the promise and looks like a later
    /// race - which is why the green flag is the test and nothing else
    /// would have done.
    /// </summary>
    [Fact]
    public void A_pace_note_is_never_paid_off_by_its_own_race()
    {
        var ledger = new NightPromises();

        // Green at 19:00, the note at 19:04, the flag at 19:08.
        var atAir = Green.AddMinutes(4).ToLocalTime();

        ledger.Note(SegmentKind.PaceNote, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);

        var ownRace = Moment(NightMoments.Kind.FastestLap, Carl, Green, sessionId: "race-1");

        Assert.Null(ledger.PayOff(new[] { ownRace }, atAir.AddMinutes(6)));

        // And the next race, which started after the note, does pay it off
        // - so the refusal above is the green flag doing its job and not
        // the ledger being broken.
        var nextRace = Moment(NightMoments.Kind.FastestLap, Carl, Green.AddMinutes(12), sessionId: "race-2");

        Assert.NotNull(ledger.PayOff(new[] { nextRace }, atAir.AddMinutes(16)));
    }

    [Fact]
    public void A_result_with_no_start_time_pays_nothing_off()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);

        var handEntered = Moment(NightMoments.Kind.Charge, Carl, default);

        Assert.Null(ledger.PayOff(new[] { handEntered }, atAir.AddMinutes(10)));
    }

    /// <summary>
    /// A fight needs both its names. The fallback that was there first
    /// dropped to the one-driver wording, which opened with the subject's
    /// name and then described whatever the moment was about - two
    /// different drivers in one sentence joined by "earlier tonight".
    /// </summary>
    [Fact]
    public void A_fight_whose_second_driver_has_no_name_is_dropped()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        // 999 is on no club profile, so there is no second name.
        ledger.Note(
            SegmentKind.BattleWatch,
            Pack(FactTheme.Rivalry(Carl, "999")),
            NameFor,
            bothVoices: false,
            atAir);

        Assert.Equal(1, ledger.Noted);
        Assert.Null(ledger.PayOff(
            new[] { Moment(NightMoments.Kind.Charge, Carl, Green.AddMinutes(10)) },
            atAir.AddMinutes(14)));
    }

    [Fact]
    public void A_driver_with_no_name_is_never_promised()
    {
        var ledger = new NightPromises();

        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver("999")), NameFor, bothVoices: false, Green.ToLocalTime());

        Assert.Equal(0, ledger.Noted);
        Assert.Equal(0, ledger.Held);
    }

    [Fact]
    public void A_promise_goes_stale_and_is_not_paid_off_late()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);

        var later = atAir + NightPromises.StaysWorthPaying + TimeSpan.FromMinutes(1);

        Assert.Null(ledger.PayOff(new[] { Moment(NightMoments.Kind.LastLapLead, Carl, Green.AddMinutes(10)) }, later));
        Assert.Equal(0, ledger.Held);
        Assert.Equal(1, ledger.Lapsed);
    }

    /// <summary>
    /// The callback is not retired by being OFFERED, because most offers
    /// are made to segments the budget never lets through. It is retired by
    /// the mark, exactly as every other fact in this app is.
    /// </summary>
    [Fact]
    public void A_callback_offered_and_not_spoken_is_still_waiting()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);

        var moments = new[] { Moment(NightMoments.Kind.LastLapLead, Carl, Green.AddMinutes(10)) };
        var first = ledger.PayOff(moments, atAir.AddMinutes(14));

        Assert.NotNull(first);
        Assert.Equal(0, ledger.Paid);

        var again = ledger.PayOff(moments, atAir.AddMinutes(15));

        Assert.NotNull(again);
        Assert.Equal(first!.Key, again!.Key);

        ledger.MarkPaid(first.Key);

        Assert.Equal(1, ledger.Paid);
        Assert.Null(ledger.PayOff(moments, atAir.AddMinutes(16)));
    }

    /// <summary>
    /// A callback is itself a segment about a driver, so it files a fresh
    /// promise - which is right, and must not become a loop. It cannot: the
    /// new promise was made after that race went green, so paying it off
    /// needs a race that has not been run yet.
    /// </summary>
    [Fact]
    public void A_callback_cannot_pay_itself_off_again()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(SegmentKind.PaceNote, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);

        var second = new[] { Moment(NightMoments.Kind.LastLapLead, Carl, Green.AddMinutes(10), sessionId: "race-2") };
        var first = ledger.PayOff(second, atAir.AddMinutes(14));

        Assert.NotNull(first);
        ledger.MarkPaid(first!.Key);

        // The callback goes out. Its pack is themed on Carl, so a fresh
        // promise about Carl is filed - at 19:14, after race two went
        // green at 19:10.
        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir.AddMinutes(14));

        Assert.Null(ledger.PayOff(second, atAir.AddMinutes(15)));

        var third = new[] { Moment(NightMoments.Kind.LapRecord, Carl, Green.AddMinutes(20), sessionId: "race-3") };

        Assert.NotNull(ledger.PayOff(third, atAir.AddMinutes(24)));
    }

    // ------------------------------------------------------------------
    //  The subjects, read back out of the themes
    // ------------------------------------------------------------------

    [Fact]
    public void The_subjects_are_the_drivers_and_the_fights_and_nothing_else()
    {
        var subjects = NightPromises.SubjectsIn(Pack(
            FactTheme.Driver(Carl),
            FactTheme.Rivalry(Dave, Sam),
            FactTheme.Venue("Winter Indoor"),
            FactTheme.Field("Box Stock"),
            FactTheme.PastDay(3)));

        Assert.Equal(2, subjects.Count);
        Assert.Contains(subjects, one => one.A == Carl && one.B is null);

        // Minted either way round and read back the way FactTheme.Pair
        // sorts them, which is what makes one fight one theme.
        Assert.Contains(subjects, one => one.B is not null
            && (one.A == Dave || one.A == Sam)
            && (one.B == Dave || one.B == Sam));
    }

    [Fact]
    public void A_pair_that_two_splits_both_explain_is_no_subject_at_all()
    {
        // A transponder with a plus in it. "12+34" against "5678" and "12"
        // against "34+5678" are the same string and the same theme, so the
        // honest answer is that the subject is unknown.
        var subjects = NightPromises.SubjectsIn(Pack(FactTheme.Rivalry("12+34", "5678")));

        Assert.Empty(subjects);
    }

    [Fact]
    public void A_theme_with_no_subject_in_it_is_skipped()
    {
        Assert.Empty(NightPromises.SubjectsIn(Pack("driver:", "driver", "rivalry:", "rivalry:abc", "rivalry:+x")));
    }

    [Fact]
    public void The_same_driver_pointed_at_twice_is_one_thing_to_come_back_to()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);
        ledger.Note(SegmentKind.PaceNote, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir.AddMinutes(3));

        Assert.Equal(2, ledger.Noted);
        Assert.Equal(1, ledger.Held);

        // Replaced rather than lapsed - the older one went because the
        // newer is the one the room heard, not because the clock took it.
        Assert.Equal(0, ledger.Lapsed);
    }

    [Fact]
    public void The_ledger_holds_only_so_many_and_drops_the_oldest()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        for (var at = 0; at < NightPromises.MostHeld + 6; at++)
        {
            var who = "d" + at.ToString(System.Globalization.CultureInfo.InvariantCulture);

            ledger.Note(
                SegmentKind.FormGuide,
                Pack(FactTheme.Driver(who)),
                _ => "Driver " + who,
                bothVoices: false,
                atAir.AddSeconds(at));
        }

        Assert.Equal(NightPromises.MostHeld, ledger.Held);
        Assert.Equal(NightPromises.MostHeld + 6, ledger.Noted);
        Assert.Equal(6, ledger.Lapsed);
    }

    [Fact]
    public void A_new_race_day_clears_the_lot()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.NewMeeting("2026-10-12");
        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);

        Assert.Equal(1, ledger.Held);

        ledger.NewMeeting("2026-10-19");

        Assert.Equal(0, ledger.Held);
        Assert.Equal(0, ledger.Noted);
        Assert.Null(ledger.Report());
    }

    [Fact]
    public void The_same_race_day_twice_keeps_what_is_open()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.NewMeeting("2026-10-12");
        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);
        ledger.NewMeeting("2026-10-12");

        Assert.Equal(1, ledger.Held);
    }

    // ------------------------------------------------------------------
    //  The report
    // ------------------------------------------------------------------

    [Fact]
    public void A_night_that_pointed_at_nobody_reports_nothing()
    {
        Assert.Null(new NightPromises().Report());
    }

    [Fact]
    public void The_report_counts_what_was_pointed_at_and_what_was_come_back_to()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);
        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Dave)), NameFor, bothVoices: false, atAir);

        var block = ledger.PayOff(
            new[] { Moment(NightMoments.Kind.LastLapLead, Carl, Green.AddMinutes(10)) },
            atAir.AddMinutes(14));

        Assert.NotNull(block);
        ledger.MarkPaid(block!.Key);

        var report = ledger.Report();

        Assert.NotNull(report);
        Assert.Contains("2 subjects", report!);
        Assert.Contains("came back to 1", report);
        Assert.Contains("1 still open", report);
    }

    [Fact]
    public void A_pay_off_key_is_known_to_be_one()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);

        var block = ledger.PayOff(
            new[] { Moment(NightMoments.Kind.LastLapLead, Carl, Green.AddMinutes(10)) },
            atAir.AddMinutes(14));

        Assert.NotNull(block);
        Assert.True(ledger.IsPayOffKey(block!.Key));
        Assert.False(ledger.IsPayOffKey("pb:carl:2026-10-12-r4"));
        Assert.False(ledger.IsPayOffKey(null));
        Assert.False(ledger.IsPayOffKey(string.Empty));
    }

    /// <summary>
    /// A promise made in the first heat and a promise made in the A-main
    /// are two things the night can come back to, so they cannot share a
    /// key - SaidTonight would spend the second before it was ever offered.
    /// </summary>
    [Fact]
    public void Two_promises_about_one_driver_have_two_keys()
    {
        var first = new NightPromises();
        var second = new NightPromises();
        var atAir = Green.ToLocalTime();

        first.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);
        second.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir.AddMinutes(20));

        var moments = new[] { Moment(NightMoments.Kind.LastLapLead, Carl, Green.AddMinutes(25)) };

        var one = first.PayOff(moments, atAir.AddMinutes(20));
        var two = second.PayOff(moments, atAir.AddMinutes(30));

        Assert.NotNull(one);
        Assert.NotNull(two);
        Assert.NotEqual(one!.Key, two!.Key);
    }

    /// <summary>
    /// The strongest MOMENT wins rather than the oldest promise, because
    /// NightMoments.Over hands them over strongest first and a callback is
    /// only worth the airtime if what it pays off is.
    /// </summary>
    [Fact]
    public void The_strongest_moment_decides_which_promise_is_paid()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);
        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Dave)), NameFor, bothVoices: false, atAir.AddMinutes(1));

        // As Over would order them: the lead change first.
        var moments = new[]
        {
            Moment(NightMoments.Kind.LastLapLead, Dave, Green.AddMinutes(10)),
            Moment(NightMoments.Kind.FastestLap, Carl, Green.AddMinutes(10)),
        };

        var block = ledger.PayOff(moments, atAir.AddMinutes(14));

        Assert.NotNull(block);
        Assert.Contains("Dave Bellamy", block!.Text);
        Assert.DoesNotContain("Carl Dodd", block.Text);
    }

    [Fact]
    public void A_pack_with_no_themes_promises_nothing()
    {
        var ledger = new NightPromises();

        ledger.Note(SegmentKind.FormGuide, Pack(), NameFor, bothVoices: false, Green.ToLocalTime());
        ledger.Note(SegmentKind.FormGuide, null, NameFor, bothVoices: false, Green.ToLocalTime());

        Assert.Equal(0, ledger.Noted);
    }

    [Fact]
    public void Nothing_is_paid_off_on_a_night_with_no_moments()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);

        Assert.Null(ledger.PayOff(Array.Empty<NightMoments.Moment>(), atAir.AddMinutes(5)));
        Assert.Null(ledger.PayOff(null, atAir.AddMinutes(5)));

        // And the promise is still open, because nothing happened to it.
        Assert.Equal(1, ledger.Held);
    }

    [Fact]
    public void A_moment_about_somebody_else_pays_nothing_off()
    {
        var ledger = new NightPromises();
        var atAir = Green.ToLocalTime();

        ledger.Note(SegmentKind.FormGuide, Pack(FactTheme.Driver(Carl)), NameFor, bothVoices: false, atAir);

        Assert.Null(ledger.PayOff(
            new[] { Moment(NightMoments.Kind.LastLapLead, Sam, Green.AddMinutes(10)) },
            atAir.AddMinutes(14)));
    }
}
