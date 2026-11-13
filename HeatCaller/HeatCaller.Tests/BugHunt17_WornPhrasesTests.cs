namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): the stock-phrase watch matched anywhere in a
/// line, so "attention" was noted as "tension" and "prehistoric" as
/// "historic" - and the prompt then told the model to avoid words nobody
/// had worn out. A watched phrase now has to begin a word.
/// </summary>
public sealed class BugHunt17_WornPhrasesTests : IDisposable
{
    public BugHunt17_WornPhrasesTests() => WornPhrases.Forget();

    public void Dispose() => WornPhrases.Forget();

    [Theory]
    [InlineData("Pay attention to the back of the field.", "tension")]
    [InlineData("A prehistoric pace from the back row.", "historic")]
    [InlineData("That is an understatement.", "statement")]
    [InlineData("The champion fired it down the inside.", "on fire")]
    public void A_phrase_found_only_inside_another_word_is_not_noted(string line, string phrase)
    {
        WornPhrases.Note(line);

        Assert.DoesNotContain("\"" + phrase, WornPhrases.PromptLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_phrase_that_begins_a_word_is_still_noted_as_it_was_heard()
    {
        WornPhrases.Note("He dominated that one, and the tension was real.");

        var prompt = WornPhrases.PromptLine();

        Assert.Contains("\"dominated\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"tension\"", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_real_use_later_in_the_line_is_found_past_one_inside_a_word()
    {
        WornPhrases.Note("Pay attention - the tension is building.");

        Assert.Contains("\"tension\"", WornPhrases.PromptLine(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_phrase_at_the_very_start_or_after_punctuation_counts()
    {
        WornPhrases.Note("Showdown time. A nail-biter, (incredible) stuff.");

        var prompt = WornPhrases.PromptLine();

        Assert.Contains("\"showdown\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"nail-biter\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"incredible\"", prompt, StringComparison.Ordinal);
    }
}
