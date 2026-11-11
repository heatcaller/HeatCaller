namespace HeatCaller.Tests;

/// <summary>
/// The four fixes from the 2 Nov 2026 demo night (3 Nov 2026): a battle
/// watch thrown away unheard gives its turn back, a recap's capitals and
/// digit-by-digit times are put right, the cold open stays out of the recap
/// show, and the sign-off does not promise the race that just finished.
/// </summary>
public sealed class RecapAndBattleFixTests
{
    // ------------------------------------------------------------------
    // A battle watch nobody heard
    // ------------------------------------------------------------------

    [Fact]
    public void An_unheard_segment_gives_its_turn_back()
    {
        var budget = new CommentaryBudget();
        budget.NewMeeting("day-1");

        var limit = CommentaryBudget.Spec(SegmentKind.BattleWatch).MaxPerClass;

        for (var i = 0; i < limit; i++)
        {
            var verdict = budget.Reserve(SegmentKind.BattleWatch, "Stock");
            Assert.True(verdict.Allowed, $"battle watch {i}: {verdict.Reason}");
            budget.Settle(verdict.Ticket, spoken: false);
        }

        Assert.False(budget.Peek(SegmentKind.BattleWatch, "Stock").Allowed);

        // One of them was thrown away before a word was heard.
        budget.GiveBackTurn(SegmentKind.BattleWatch, "Stock");

        Assert.True(budget.Peek(SegmentKind.BattleWatch, "Stock").Allowed);
    }

    [Fact]
    public void Giving_back_a_turn_never_goes_below_nought()
    {
        var budget = new CommentaryBudget();
        budget.NewMeeting("day-1");

        budget.GiveBackTurn(SegmentKind.ColdOpen, "Stock");

        Assert.True(Write(budget, SegmentKind.ColdOpen));
        Assert.False(budget.Peek(SegmentKind.ColdOpen).Allowed);
    }

    private static bool Write(CommentaryBudget budget, SegmentKind kind)
    {
        var verdict = budget.Reserve(kind);

        if (!verdict.Allowed)
        {
            return false;
        }

        budget.Settle(verdict.Ticket);

        return true;
    }

    // ------------------------------------------------------------------
    // Times read out a digit at a time
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("Three-point-three-zero-four back to second place", "3.304s back to second place")]
    [InlineData("a thirteen-point-nine-eight-zero!", "a 13.980s!")]
    [InlineData("an eleven-point-two-six-four second gap", "an 11.264s gap")]
    [InlineData("the fastest lap of the day with a twelve-nine-four-five, proving", "the fastest lap of the day with a 12.945s, proving")]
    [InlineData("at a thirteen fifty-six-nine - flying", "at a 13.569s - flying")]
    public void A_time_spelled_digit_by_digit_goes_back_to_figures(string written, string expected)
    {
        Assert.Equal(expected, RaceNumbers.DigitsFromSpelledTimes(written));
    }

    [Theory]
    [InlineData("a one-two finish for the team")]
    [InlineData("three point five clear")]
    [InlineData("thirteen point nine eight")]
    [InlineData("a twelve thirty-four")]
    [InlineData("Twenty-four laps led")]
    public void Ordinary_words_are_left_alone(string written)
    {
        Assert.Equal(written, RaceNumbers.DigitsFromSpelledTimes(written));
    }

    // 10 Nov 2026, bug hunt 16: a heat's run time from the intros, which
    // the model is told to copy word for word, was put back into figures
    // and then read as a lap - "five minutes and two three fifteen".
    [Theory]
    [InlineData(302_315)]
    [InlineData(48_512)]
    [InlineData(60_001)]
    public void A_qualifying_run_time_in_the_intros_is_said_as_written(long ms)
    {
        var line = $"Sam Parker takes pole with thirty-four laps in {DriverIntros.DurationSpoken(ms)}.";

        Assert.Equal(line, RaceNumbers.DigitsFromSpelledTimes(line));
        Assert.Equal(line, AnnouncementService.SpellOutTimesForSpeech(line));
    }

    [Fact]
    public void The_voice_no_longer_reads_the_digits_out()
    {
        var spoken = AnnouncementService.SpellOutTimesForSpeech("Three-point-three-zero-four back to second place");

        Assert.DoesNotContain("zero-four", spoken, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("3.304", spoken, StringComparison.Ordinal);
        Assert.Contains("back to second place", spoken, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // Capitals in the recap
    // ------------------------------------------------------------------

    [Fact]
    public void A_shouted_recap_is_put_back_into_normal_case_with_the_names_kept()
    {
        var calmed = CommentaryService.CalmRecap(
            "WELCOME BACK TO HEAT CALLER RACING! I'm Brick Hammerstone. Five lead changes! FIVE! Al Gorithim wins.",
            new[] { "Heat Caller Racing", "Al Gorithim" });

        Assert.Equal(
            "Welcome back to Heat Caller Racing! I'm Brick Hammerstone. Five lead changes! Five! Al Gorithim wins.",
            calmed);
    }

    [Fact]
    public void A_recap_without_shouting_is_left_exactly_as_written()
    {
        const string written = "Al Gorithim took the win in the Orange Porsche 911 GTR.";

        Assert.Equal(written, CommentaryService.CalmRecap(written, new[] { "Al Gorithim" }));
    }

    // ------------------------------------------------------------------
    // The cold open in the recap show
    // ------------------------------------------------------------------

    [Fact]
    public void Only_the_cold_open_is_kept_out_of_the_recap_show()
    {
        Assert.True(CommentaryDirector.OpensTheNight(SegmentKind.ColdOpen));
        Assert.False(CommentaryDirector.OpensTheNight(SegmentKind.ComingUpNext));
        Assert.False(CommentaryDirector.OpensTheNight(SegmentKind.FormGuide));
    }

    // ------------------------------------------------------------------
    // The sign-off's next race
    // ------------------------------------------------------------------

    private static SessionRecord Ran(string label) => new() { Label = label };

    [Fact]
    public void A_sign_off_promising_the_race_just_run_is_caught()
    {
        var line = "Al Gorithim takes the heat! Stick with us as we roll straight into Q2 Heat 1 next!";

        Assert.True(CommentaryService.NamesTheRaceJustRun(line, Ran("Stock Q2 Heat 1"), null));
        Assert.True(CommentaryService.NamesTheRaceJustRun(line, Ran("Stock Q2 Heat 1"), "Stock Q3 Heat 1"));
    }

    [Fact]
    public void A_sign_off_naming_the_real_next_race_is_kept()
    {
        Assert.False(CommentaryService.NamesTheRaceJustRun(
            "That's Q2 Heat 1 done - up next, Q3 Heat 1!", Ran("Stock Q2 Heat 1"), "Q3 Heat 1"));

        Assert.False(CommentaryService.NamesTheRaceJustRun(
            "Stick around for Q2 Heat 10!", Ran("Stock Q2 Heat 1"), "Q2 Heat 10"));

        Assert.False(CommentaryService.NamesTheRaceJustRun(
            "That's all for this one - see you next time!", Ran("Stock Q2 Heat 1"), null));
    }
}
