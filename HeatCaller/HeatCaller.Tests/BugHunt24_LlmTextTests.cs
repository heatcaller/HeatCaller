namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): a cut-off answer that stops right after the
/// point of a number is not kept as a whole sentence, so half a lap time is
/// never spoken.
/// </summary>
public sealed class BugHunt24_LlmTextTests
{
    [Fact]
    public void A_cut_off_answer_ending_digit_then_full_stop_falls_back_to_the_sentence_before()
    {
        var kept = LlmText.UpToLastWholeSentence("Sam Kerr leads the way tonight. He puts in a best lap of 12.");

        Assert.Equal("Sam Kerr leads the way tonight.", kept);
    }

    [Fact]
    public void A_cut_off_answer_whose_only_sentence_ends_digit_then_full_stop_gives_nothing()
    {
        Assert.Null(LlmText.UpToLastWholeSentence("Sam Kerr puts in a best lap of 12."));
    }

    [Fact]
    public void A_decimal_inside_a_finished_sentence_is_still_kept_whole()
    {
        const string text = "Sam Kerr puts in a best lap of 12.48 seconds. Great drive.";

        Assert.Equal(text, LlmText.UpToLastWholeSentence(text));
    }

    [Fact]
    public void A_sentence_ending_on_a_word_after_a_decimal_is_unchanged()
    {
        const string text = "Sam Kerr wins the A main by 4.63 seconds.";

        Assert.Equal(text, LlmText.UpToLastWholeSentence(text));
    }
}
