namespace HeatCaller.Tests;

/// <summary>
/// A meeting that has not happened, previewed (12 Oct 2026) - see
/// <see cref="NightBefore"/>.
///
/// WHAT THIS FILE IS ABOUT
///
/// One thing: a preview is a PLAN, and every sentence has to say so. Every
/// other piece in this layer is written from saved results - things that
/// happened, to people who were there. This one is written from an entry
/// list, which is what the club INTENDS, and three drivers will scratch
/// before the first heat.
///
/// So most of what is tested here is wording, and that is not fussiness:
/// a fact block is data the model describes, and a model handed "nineteen
/// cars" writes "nineteen cars will be out" however the instruction is
/// phrased. Handed "nineteen cars are on the entry list", it writes that.
///
/// The test worth reading first is
/// <see cref="Four_heats_of_the_same_ten_drivers_is_ten_entries"/>. It is
/// the one real piece of arithmetic in the file and the one a reader would
/// get wrong: adding the rosters up would have the club advertising forty
/// cars for a ten-car class.
/// </summary>
public sealed class NightBeforeTests
{
    private static readonly DateTime When = new(2026, 11, 7, 10, 15, 0, DateTimeKind.Local);

    /// <summary>One round of the programme, as a club sets it up before anybody races.</summary>
    private static RoundDefinition Round(string className, string label, params string[] grid) =>
        new()
        {
            ClassName = className,
            RoundLabel = label,
            Kind = RoundKind.Qualifier,
            AssignedTransponders = grid.ToList(),
        };

    private static VenueRecord Record() =>
        new("Winter Indoor", 8.412, "Bill Hargrove", "in March", RacesOnLayout: 40, RacesSince: 12, SetTonight: false);

    private static TitleStakes Stakes(bool settled = false) =>
        new(
            "Winter Series",
            RoundsCounted: 4,
            RoundsLeft: 2,
            LeaderName: "Bill Hargrove",
            LeaderId: "111",
            LeaderPoints: 148,
            LeadBy: 12,
            CannotBeCaught: settled,
            StillInIt: settled ? Array.Empty<string>() : new[] { "Bill Hargrove", "Sam Teague" });

    private static string TextOf(PackedFacts facts) => facts.Text;

    // ------------------------------------------------------------------
    //  It is a plan, and it says so
    // ------------------------------------------------------------------

    [Fact]
    public void The_programme_is_offered_as_a_plan_and_never_as_a_promise()
    {
        var facts = NightBefore.FactsFor(
            "Winter Round 5",
            When,
            "10:15",
            new[] { Round("Box Stock", "Heat 1", "111", "222", "333", "444") },
            venue: null,
            stakes: null);

        var said = TextOf(facts);

        Assert.Contains("Winter Round 5 is planned for", said);
        Assert.Contains("first race due out at 10:15", said);
        Assert.Contains("down to run", said);
        Assert.Contains("That is the plan rather than a promise", said);

        // The words that would turn it into a report.
        Assert.DoesNotContain("will be out", said);
        Assert.DoesNotContain("goes out at", said);
    }

    [Fact]
    public void An_entry_list_is_never_offered_as_a_field()
    {
        var facts = NightBefore.FactsFor(
            "Winter Round 5",
            When,
            "10:15",
            new[] { Round("Box Stock", "Heat 1", "111", "222", "333", "444", "555") },
            venue: null,
            stakes: null);

        var said = TextOf(facts);

        Assert.Contains("5 cars on the entry list for Box Stock", said);
        Assert.Contains("An entry list is not a result sheet", said);
    }

    /// <summary>THE ONE. See the class summary.</summary>
    [Fact]
    public void Four_heats_of_the_same_ten_drivers_is_ten_entries()
    {
        var grid = new[] { "111", "222", "333", "444", "555", "666", "777", "888", "999", "1010" };

        var facts = NightBefore.FactsFor(
            "Winter Round 5",
            When,
            "10:15",
            new[]
            {
                Round("Box Stock", "Heat 1", grid),
                Round("Box Stock", "Heat 2", grid),
                Round("Box Stock", "Heat 3", grid),
                Round("Box Stock", "Heat 4", grid),
            },
            venue: null,
            stakes: null);

        var said = TextOf(facts);

        Assert.Contains("10 cars on the entry list for Box Stock", said);
        Assert.DoesNotContain("40 cars", said);

        // And the programme's own count is still four races, because four
        // races is what is down to run.
        Assert.Contains("4 races down to run", said);
    }

    [Fact]
    public void A_ghost_is_not_an_entry()
    {
        // Four real cars and two pace references. Ghosts are not entries,
        // for the reason CommentaryReaders.Field gives: a count of cars is
        // a claim about cars with people behind them.
        var facts = NightBefore.FactsFor(
            "Club Night",
            When,
            "10:15",
            new[] { Round("Stock", "Heat 1", "111", "222", "333", "444", "GHOST-pace", "GHOST-target") },
            venue: null,
            stakes: null);

        Assert.Contains("4 cars on the entry list for Stock", TextOf(facts));
    }

    [Fact]
    public void A_class_with_barely_anybody_entered_is_not_advertised()
    {
        var facts = NightBefore.FactsFor(
            "Club Night",
            When,
            "10:15",
            new[]
            {
                Round("Box Stock", "Heat 1", "111", "222", "333", "444"),
                Round("Mod", "Heat 1", "777", "888"),
            },
            venue: null,
            stakes: null);

        var said = TextOf(facts);

        Assert.Contains("entry list for Box Stock", said);

        // Two entries is below LeastEntered, so no count is stated - a
        // club advertising that nobody is coming to its Mod class is worse
        // than saying nothing about it.
        Assert.DoesNotContain("entry list for Mod", said);

        // The class is still named in the programme, because the programme
        // does have Mod races down to run. That is not a contradiction: the
        // races exist, the count is what is not worth stating.
        Assert.Contains("Mod", said);
    }

    [Fact]
    public void Classes_are_pluralised_as_classes()
    {
        var facts = NightBefore.FactsFor(
            "Club Night",
            When,
            "10:15",
            new[]
            {
                Round("Box Stock", "Heat 1", "111", "222", "333", "444"),
                Round("Mod", "Heat 1", "777", "888", "999", "1010"),
            },
            venue: null,
            stakes: null);

        var said = TextOf(facts);

        Assert.Contains("2 classes", said);
        Assert.DoesNotContain("2 classs", said);
    }

    [Fact]
    public void One_class_and_one_race_read_as_one_of_each()
    {
        var facts = NightBefore.FactsFor(
            "Club Night",
            When,
            "10:15",
            new[] { Round("Stock", "Heat 1", "111", "222", "333", "444") },
            venue: null,
            stakes: null);

        var said = TextOf(facts);

        Assert.Contains("1 race down to run", said);
        Assert.Contains("1 class", said);
    }

    // ------------------------------------------------------------------
    //  What it will not claim
    // ------------------------------------------------------------------

    [Fact]
    public void A_day_with_no_date_claims_none()
    {
        var facts = NightBefore.FactsFor(
            "Club Night",
            default,
            firstRace: null,
            new[] { Round("Stock", "Heat 1", "111", "222", "333", "444") },
            venue: null,
            stakes: null);

        var said = TextOf(facts);

        Assert.Contains("is planned.", said);
        Assert.DoesNotContain("planned for", said);
        Assert.DoesNotContain("due out at", said);
    }

    [Fact]
    public void A_day_with_no_name_is_not_given_one()
    {
        var facts = NightBefore.FactsFor(
            null,
            When,
            "10:15",
            new[] { Round("Stock", "Heat 1", "111", "222", "333", "444") },
            venue: null,
            stakes: null);

        Assert.Contains("The club's next race day is planned for", TextOf(facts));
    }

    /// <summary>
    /// A club's own first-race time goes out exactly as they typed it.
    /// Tidying "TBD" into something definite would be this file inventing
    /// a certainty the club did not express.
    /// </summary>
    [Fact]
    public void A_first_race_time_is_said_as_the_club_wrote_it()
    {
        var facts = NightBefore.FactsFor(
            "Club Night",
            When,
            "TBD",
            new[] { Round("Stock", "Heat 1", "111", "222", "333", "444") },
            venue: null,
            stakes: null);

        Assert.Contains("first race due out at TBD", TextOf(facts));
    }

    [Fact]
    public void An_empty_programme_is_nothing_to_preview()
    {
        Assert.True(NightBefore.FactsFor("Club Night", When, "10:15", Array.Empty<RoundDefinition>(), null, null).Empty);
        Assert.True(NightBefore.FactsFor("Club Night", When, "10:15", null, null, null).Empty);
    }

    // ------------------------------------------------------------------
    //  The facts that cannot go stale
    // ------------------------------------------------------------------

    [Fact]
    public void The_venue_record_is_carried_because_it_is_about_the_past()
    {
        var facts = NightBefore.FactsFor(
            "Club Night",
            When,
            "10:15",
            new[] { Round("Stock", "Heat 1", "111", "222", "333", "444") },
            Record(),
            stakes: null);

        var said = TextOf(facts);

        Assert.Contains("outright lap record on Winter Indoor", said);
        Assert.Contains("8.412s", said);
        Assert.Contains("Bill Hargrove", said);
    }

    [Fact]
    public void A_record_with_nobody_holding_it_is_left_out()
    {
        var facts = NightBefore.FactsFor(
            "Club Night",
            When,
            "10:15",
            new[] { Round("Stock", "Heat 1", "111", "222", "333", "444") },
            new VenueRecord("Winter Indoor", 8.412, string.Empty, "in March", 40, 12, false),
            stakes: null);

        Assert.DoesNotContain("outright lap record", TextOf(facts));
    }

    [Fact]
    public void What_the_title_has_riding_on_it_is_carried()
    {
        var facts = NightBefore.FactsFor(
            "Winter Round 5",
            When,
            "10:15",
            new[] { Round("Box Stock", "Heat 1", "111", "222", "333", "444") },
            venue: null,
            Stakes());

        var said = TextOf(facts);

        Assert.Contains("Winter Series", said);
        Assert.Contains("Bill Hargrove", said);
        Assert.Contains("Sam Teague", said);
    }

    // ------------------------------------------------------------------
    //  The entry list, for the stakes to be read against
    // ------------------------------------------------------------------

    [Fact]
    public void Everybody_entered_in_anything_is_on_the_entry_list()
    {
        var entered = NightBefore.EnteredIn(new[]
        {
            Round("Box Stock", "Heat 1", "111", "222"),
            Round("Box Stock", "Heat 2", "333"),

            // A contender entered only in the second heat is still entered.
            Round("Mod", "Heat 1", "444", "111"),
        });

        Assert.Equal(new[] { "111", "222", "333", "444" }, entered);
    }

    [Fact]
    public void The_entry_list_drops_ghosts_and_blanks_and_keeps_each_car_once()
    {
        var entered = NightBefore.EnteredIn(new[]
        {
            Round("Stock", "Heat 1", "111", "  ", "GHOST-pace", "111", " 222 "),
        });

        Assert.Equal(new[] { "111", "222" }, entered);
    }

    [Fact]
    public void No_programme_is_an_empty_entry_list()
    {
        Assert.Empty(NightBefore.EnteredIn(null));
        Assert.Empty(NightBefore.EnteredIn(Array.Empty<RoundDefinition>()));
    }

    // ------------------------------------------------------------------
    //  The piece itself
    // ------------------------------------------------------------------

    [Fact]
    public void It_is_the_same_length_whatever_the_clubs_line_limit_is()
    {
        // The prompt builder ADDS its argument to the club's own limit, so
        // what goes in is the difference - the same arithmetic as the
        // night's recap, and for its reasons.
        Assert.Equal(NightBefore.Words - 45, NightBefore.ExtraWordsOver(45));
        Assert.Equal(NightBefore.Words, NightBefore.ExtraWordsOver(0));

        // A club that has asked for longer lines than the whole preview
        // keeps them: they asked for long writing outright.
        Assert.Equal(0, NightBefore.ExtraWordsOver(NightBefore.Words + 100));

        // A nonsense limit is treated as none at all, so the preview gets
        // its whole allowance rather than nothing.
        Assert.Equal(NightBefore.Words, NightBefore.ExtraWordsOver(-10));
    }

    /// <summary>
    /// The clause no other instruction in this app has, and the reason
    /// this one is written out rather than borrowed.
    /// </summary>
    [Fact]
    public void The_instruction_says_it_is_a_plan_and_forbids_predicting()
    {
        var told = NightBefore.Instruction;

        Assert.Contains("HAS NOT HAPPENED YET", told);
        Assert.Contains("A PLAN, NOT A RESULT", told);
        Assert.Contains("entered rather than racing", told);
        Assert.Contains("due out rather than", told);
        Assert.Contains("past tense", told);

        // No predicting, and no inventing a grid that does not exist.
        Assert.Contains("Do not predict who will win", told);
        Assert.Contains("do not exist yet", told);

        // And the closing rule every piece in this layer carries.
        Assert.Contains("must come from the facts above", told);
        Assert.Contains("rather than filling it out", told);
    }

    [Fact]
    public void What_replaces_a_preview_once_the_meeting_starts_is_nothing()
    {
        // Empty rather than dropped - the rule RelayPublisher already
        // learned for a withdrawn roster.
        //
        // Assert.Empty rather than Assert.Equal against string.Empty:
        // both sides of that comparison are constants, so the analyser
        // cannot tell which was meant to be the expected one and says so
        // (xUnit2000). This asks the question directly.
        Assert.Empty(NightBefore.Withdrawn);
    }

    [Fact]
    public void It_splits_the_way_the_recap_does()
    {
        Assert.Empty(NightBefore.Chunks(null));
        Assert.Empty(NightBefore.Chunks("   "));

        var chunks = NightBefore.Chunks("One sentence. Then another one.");

        Assert.NotEmpty(chunks);
    }

    [Fact]
    public void It_is_called_the_night_before()
    {
        Assert.Equal("The Night Before", NightBefore.Label);
    }

    // ------------------------------------------------------------------
    //  THE DOCUMENT THE CLUB'S PAGE RECEIVES (13 Oct 2026)
    //
    //  Four names, agreed with previewHtml in relay/src/pages.ts and
    //  understood by nothing in between - the relay does not read a
    //  club's payload. So this half of the agreement is held here and the
    //  other half by relay/test/nightbefore.test.ts, and a rename that
    //  misses one of them is a club's front page drawing nothing.
    // ------------------------------------------------------------------

    /// <summary>A race day as the programme has it: Saturday 17 October 2026, first race at a quarter past ten.</summary>
    private static readonly DateTime RaceDay = new(2026, 10, 17, 0, 0, 0, DateTimeKind.Local);

    private static readonly TimeSpan QuarterPastTen = new(10, 15, 0);

    [Fact]
    public void The_document_carries_the_words_a_title_a_when_and_an_expiry()
    {
        var json = NightBefore.Document("Friday Club Night", RaceDay, QuarterPastTen, "Nineteen cars are entered.");

        using var doc = System.Text.Json.JsonDocument.Parse(json);

        Assert.Equal("Nineteen cars are entered.", doc.RootElement.GetProperty("text").GetString());
        Assert.Equal("Friday Club Night", doc.RootElement.GetProperty("title").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("when").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("until").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("kicker").GetString()));
    }

    [Fact]
    public void A_day_with_no_name_still_gets_a_heading_rather_than_a_blank()
    {
        foreach (var nameless in new[] { null, string.Empty, "   " })
        {
            var json = NightBefore.Document(nameless, RaceDay, QuarterPastTen, "Something to say.");

            using var doc = System.Text.Json.JsonDocument.Parse(json);

            Assert.Equal("Next race day", doc.RootElement.GetProperty("title").GetString());
        }
    }

    [Fact]
    public void A_model_that_wrote_nothing_takes_the_old_preview_down()
    {
        // NOT a heading over an empty card, and not the old preview left
        // standing either. A failed write is a withdrawal.
        foreach (var nothing in new[] { null, string.Empty, "   \n  " })
        {
            Assert.Equal(NightBefore.WithdrawnDocument, NightBefore.Document("Friday", RaceDay, QuarterPastTen, nothing));
        }
    }

    [Fact]
    public void The_withdrawal_is_a_document_with_no_words_rather_than_no_document()
    {
        // THE WHOLE REASON Withdrawn EXISTS. There is no route that
        // deletes a snapshot, and there should not be: the last thing sent
        // would stand on the relay for good, because nothing newer would
        // ever replace it.
        using var doc = System.Text.Json.JsonDocument.Parse(NightBefore.WithdrawnDocument);

        Assert.Equal(NightBefore.Withdrawn, doc.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void A_preview_stands_until_the_meeting_would_be_over()
    {
        var until = NightBefore.UntilUtc(RaceDay, QuarterPastTen);

        // Six hours after a quarter past ten, in local time, expressed as
        // UTC - so the comparison is made in UTC too rather than against a
        // local clock.
        var expected = DateTime.SpecifyKind(
            RaceDay.Date + QuarterPastTen + NightBefore.StandsFor, DateTimeKind.Local).ToUniversalTime();

        Assert.Equal(expected, until);
        Assert.Equal(DateTimeKind.Utc, until.Kind);
    }

    [Fact]
    public void A_day_that_has_not_said_when_it_starts_keeps_its_preview_all_day()
    {
        // THE BUG THIS PREVENTS, and it would have hit the morning the
        // preview is most wanted. Six hours from an unset time is six
        // hours from midnight, so a preview for a ten o'clock start would
        // have come down at six that morning.
        var until = NightBefore.UntilUtc(RaceDay, null);

        var sixInTheMorning = DateTime.SpecifyKind(
            RaceDay.Date + TimeSpan.FromHours(6), DateTimeKind.Local).ToUniversalTime();

        Assert.True(until > sixInTheMorning, "a preview must not come down on the morning of the meeting");

        var endOfTheDay = DateTime.SpecifyKind(RaceDay.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();

        Assert.Equal(endOfTheDay, until);
    }

    [Fact]
    public void An_evening_meeting_keeps_its_preview_past_midnight()
    {
        // A seven o'clock club night finishes at half nine or ten, and the
        // backstop has to be after that or it takes the preview down
        // mid-meeting on a night the laptop never came up.
        var until = NightBefore.UntilUtc(RaceDay, new TimeSpan(19, 0, 0));

        Assert.True(
            until > DateTime.SpecifyKind(RaceDay.Date + new TimeSpan(22, 0, 0), DateTimeKind.Local).ToUniversalTime(),
            "an evening meeting's preview must outlast the meeting");
    }

    [Fact]
    public void A_date_read_as_local_rather_than_as_UTC()
    {
        // The reason SpecifyKind is in UntilUtc. A race day's date and
        // first-race time are local by definition; read as UTC they would
        // put the expiry four or five hours early on this side of the
        // Atlantic, and the other way in Europe. Unspecified alone could
        // not catch that - ToUniversalTime reads Unspecified as local with
        // or without SpecifyKind - so a date marked Utc is checked too, and
        // every answer against the local clock worked out separately.
        var expected = TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, 10, 17, 10, 15, 0, DateTimeKind.Unspecified) + NightBefore.StandsFor,
            TimeZoneInfo.Local);

        foreach (var kind in new[] { DateTimeKind.Local, DateTimeKind.Unspecified, DateTimeKind.Utc })
        {
            var day = new DateTime(2026, 10, 17, 0, 0, 0, kind);

            Assert.Equal(expected, NightBefore.UntilUtc(day, QuarterPastTen));
        }
    }

    [Fact]
    public void The_when_line_says_the_day_and_the_time()
    {
        var line = NightBefore.WhenLine(RaceDay, QuarterPastTen);

        // The day NUMBER and the shape of the line, not the weekday's
        // name: this is written in the machine's own culture, and a test
        // that asserted "Saturday" would fail on a club PC set to French
        // while the page was perfectly correct.
        Assert.Contains("17", line);
        Assert.Contains("from", line);
    }

    [Fact]
    public void The_when_line_leaves_the_time_out_when_the_day_has_not_set_one()
    {
        var line = NightBefore.WhenLine(RaceDay, null);

        Assert.DoesNotContain("from", line);
        Assert.Contains("17", line);
    }

    [Fact]
    public void A_model_that_ignored_the_word_count_cannot_fill_a_club_front_page()
    {
        var runOn = string.Join("\n\n", Enumerable.Repeat(new string('x', 500), 40));

        var json = NightBefore.Document("Friday", RaceDay, QuarterPastTen, runOn);

        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var text = doc.RootElement.GetProperty("text").GetString();

        Assert.NotNull(text);
        Assert.True(text!.Length <= NightBefore.MostChars, $"{text.Length} characters went to the page");
    }

    [Fact]
    public void An_over_long_preview_is_cut_at_a_paragraph_rather_than_mid_sentence()
    {
        // This is the version strangers read. A preview that stops mid-word
        // reads as a club whose website is broken.
        var runOn = string.Join("\n\n", Enumerable.Repeat(new string('x', 500), 40));

        var json = NightBefore.Document("Friday", RaceDay, QuarterPastTen, runOn);

        using var doc = System.Text.Json.JsonDocument.Parse(json);

        var text = doc.RootElement.GetProperty("text").GetString();

        Assert.NotNull(text);

        // Whole blocks only: every piece is the full 500 and none is a
        // part of one.
        Assert.All(
            text!.Split("\n\n", StringSplitOptions.RemoveEmptyEntries),
            piece => Assert.Equal(500, piece.Trim().Length));
    }

    [Fact]
    public void A_preview_that_fits_is_left_exactly_as_the_model_wrote_it()
    {
        const string written = "Nineteen cars.\n\nThe record has stood since March.";

        var json = NightBefore.Document("Friday", RaceDay, QuarterPastTen, written);

        using var doc = System.Text.Json.JsonDocument.Parse(json);

        Assert.Equal(written, doc.RootElement.GetProperty("text").GetString());
    }
}
