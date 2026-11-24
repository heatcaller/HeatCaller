namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): a hyphenated count in a typed notice or a
/// model-written line was turned into a lap time and read as one.
/// </summary>
public sealed class BugHunt24_RaceNumbersTests
{
    [Theory]
    [InlineData("five-four-three-two-one, go!")]
    [InlineData("four-three-two-one")]
    [InlineData("One-two-three-four, here we go")]
    [InlineData("three-two-one-zero and they're away")]
    public void A_hyphenated_count_is_said_as_written(string line)
    {
        Assert.Equal(line, RaceNumbers.DigitsFromSpelledTimes(line));
        Assert.Equal(line, AnnouncementService.SpellOutTimesForSpeech(line));
    }

    [Theory]
    [InlineData("with a twelve-nine-four-five, proving", "with a 12.945s, proving")]
    [InlineData("a twelve-three-four-five", "a 12.345s")]
    [InlineData("a five-four-three-one lap", "a 5.431s lap")]
    public void A_spelled_time_that_is_not_a_count_still_goes_back_to_figures(string written, string expected)
    {
        Assert.Equal(expected, RaceNumbers.DigitsFromSpelledTimes(written));
    }

    [Fact]
    public void A_spelled_time_never_starts_after_a_hyphen()
    {
        const string line = "go-thirteen fifty-six-nine";

        Assert.Equal(line, RaceNumbers.DigitsFromSpelledTimes(line));
    }

    [Theory]
    [InlineData("four", "-three-two-one", true)]
    [InlineData("one", "-two-three-four", true)]
    [InlineData("six", "-five-four-three", true)]
    [InlineData("five", "-four-three-one", false)]
    [InlineData("twelve", "-thirteen-fourteen-fifteen", false)]
    [InlineData("twelve", "-three-four-five", false)]
    [InlineData("one", "-one-one-one", false)]
    public void IsPlainCount_spots_a_count_by_ones(string whole, string digits, bool count)
    {
        Assert.Equal(count, RaceNumbers.IsPlainCount(whole, digits));
    }
}
