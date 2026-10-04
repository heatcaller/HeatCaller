namespace HeatCaller.Tests;

/// <summary>
/// The round intro held to its rules after the model has written it
/// (CommentaryService.TidyIntro). Rehearsal 3's intro for Q3 Heat 1 opened
/// with a whole sentence in capitals, ran to 80 words against a 45-word
/// limit, and called the race "Heat 3".
/// </summary>
public sealed class RoundIntroTidyTests
{
    private const string Label = "Q3 Heat 1";

    private static readonly string[] Names = { "Asher", "Frank", "Test Class", Label, "Drift 1.0" };

    /// <summary>The line rehearsal 3 spoke, word for word.</summary>
    private const string Rehearsal3Intro =
        "IT'S HEAT 3 OF THE TEST CLASS ROUND HERE AT DRIFT 1.0 AND WE'VE GOT A BARN BURNER OF A RIVALRY ON OUR HANDS! "
        + "The intense battle between ASHER and FRANK is about to reach NEW HEIGHTS! "
        + "These two drivers have been going at it all heat, and with the Test Class intensity, this one's gonna be a WILD RIDE! "
        + "Asher and Frank are lined up and ready to take on the Drift 1.0 track - only one can take the checkered flag! "
        + "Let's see who's gonna come out on top!";

    [Fact]
    public void The_rehearsal_3_intro_comes_out_calm_short_and_named_right()
    {
        var notes = new List<string>();

        var text = CommentaryService.TidyIntro(Rehearsal3Intro, Label, Names, maxWords: 45, notes);

        Assert.Equal(
            "It's Q3 Heat 1 of the Test Class round here at Drift 1.0 and we've got a barn burner of a rivalry on our hands! "
            + "The intense battle between Asher and Frank is about to reach new heights!",
            text);

        Assert.Equal(3, notes.Count);
    }

    [Fact]
    public void A_short_word_in_capitals_on_its_own_is_left_alone()
    {
        // Since rehearsal 4 a word of four letters or more in capitals is
        // shouting even on its own (the voice reads it that way); a short
        // one is as likely initials - "RC", "GP" - and stays.
        var notes = new List<string>();
        const string line = "Sam's hanging on by three tenths in the RC final - Bill is right there!";

        Assert.Equal(line, CommentaryService.TidyIntro(line, Label, Names, 45, notes));
        Assert.Empty(notes);
    }

    [Fact]
    public void A_long_word_in_capitals_on_its_own_is_calmed()
    {
        var notes = new List<string>();

        Assert.Equal(
            "Sam's hanging on by three tenths - Bill is right there!",
            CommentaryService.TidyIntro("Sam's hanging on by three tenths - Bill is RIGHT there!", Label, Names, 45, notes));
    }

    [Fact]
    public void A_single_letter_inside_shouting_is_lowered_with_it()
    {
        var notes = new List<string>();

        Assert.Equal(
            "What a race, a barn burner of a final!",
            CommentaryService.TidyIntro("WHAT A RACE, A BARN BURNER OF A FINAL!", Label, Names, 45, notes));
    }

    [Fact]
    public void A_shouted_I_stays_a_capital()
    {
        var notes = new List<string>();

        var text = CommentaryService.TidyIntro("I THINK I CAN SEE THEM NOW, folks.", Label, Names, 45, notes);

        Assert.Equal("I think I can see them now, folks.", text);
    }

    [Fact]
    public void The_right_heat_is_not_touched_and_a_wrong_one_is_replaced_whole()
    {
        var notes = new List<string>();

        Assert.Equal("Welcome to Q3 Heat 1!", CommentaryService.TidyIntro("Welcome to Q3 Heat 1!", Label, Names, 45, notes));
        Assert.Empty(notes);

        // With its qualifier in front, so the correction is not "Q3 Q3 Heat 1".
        Assert.Equal("Here comes Q3 Heat 1!", CommentaryService.TidyIntro("Here comes Q3 Heat 2!", Label, Names, 45, notes));
    }

    [Fact]
    public void A_label_with_no_heat_number_is_not_checked()
    {
        var notes = new List<string>();

        Assert.Equal("The heat 2 winners line up!", CommentaryService.TidyIntro("The heat 2 winners line up!", "A-Main", Names, 45, notes));
        Assert.Empty(notes);
    }

    [Fact]
    public void An_intro_whose_first_sentence_is_already_too_long_is_dropped()
    {
        var notes = new List<string>();
        var rambling = string.Join(" ", Enumerable.Repeat("and on", 40)) + ".";

        Assert.Null(CommentaryService.TidyIntro(rambling, Label, Names, 45, notes));
        Assert.Single(notes);
    }

    [Fact]
    public void A_few_words_over_the_limit_is_kept_whole()
    {
        var notes = new List<string>();

        // 50 words: over 45, inside the slack. Each sentence starts with a
        // capital already, so nothing else is changed either.
        var sentence = "Go" + string.Concat(Enumerable.Repeat(" go", 24)) + ".";
        var line = sentence + " " + sentence;

        Assert.Equal(line, CommentaryService.TidyIntro(line, Label, Names, 45, notes));
        Assert.Empty(notes);
    }
}
