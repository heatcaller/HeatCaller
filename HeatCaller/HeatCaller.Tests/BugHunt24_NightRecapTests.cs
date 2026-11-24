namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the recap show takes the model's own goodbye
/// off the end before adding the sign-off, and its test for "is this a
/// goodbye" was a plain substring match - so a real closing sentence that
/// only looked like one ("from Megan", "a good night of racing",
/// "back next time") was cut from the read.
/// </summary>
public sealed class BugHunt24_NightRecapTests
{
    private const string SignOff = "I'm Frankie. See you next time.";

    private static string Read(string script) =>
        string.Join(" ", NightRecap.Chunks(script, SignOff));

    [Theory]
    [InlineData("Carl took the A-main from Megan by two tenths.")]
    [InlineData("It was a good night of racing at Riverside.")]
    [InlineData("Dave will want that one back next time.")]
    public void BugHunt24_NightRecap_a_closing_sentence_that_only_looks_like_a_goodbye_is_kept(string closing)
    {
        var read = Read("Bill led every lap of the B-main. " + closing);

        Assert.Contains(closing, read, StringComparison.Ordinal);
        Assert.EndsWith(SignOff, read, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Good night, everyone.")]
    [InlineData("That's all from me.")]
    [InlineData("That's it from me.")]
    [InlineData("See you all soon.")]
    [InlineData("Next time, then.")]
    public void BugHunt24_NightRecap_a_real_goodbye_is_still_taken_off_before_the_sign_off(string goodbye)
    {
        var read = Read("Bill led every lap of the B-main. " + goodbye);

        Assert.DoesNotContain(goodbye, read, StringComparison.Ordinal);
        Assert.Contains("Bill led every lap of the B-main.", read, StringComparison.Ordinal);
        Assert.EndsWith(SignOff, read, StringComparison.Ordinal);
    }

    [Fact]
    public void BugHunt24_NightRecap_a_piece_that_is_only_a_goodbye_keeps_it()
    {
        var read = Read("Good night.");

        Assert.StartsWith("Good night.", read, StringComparison.Ordinal);
    }
}
