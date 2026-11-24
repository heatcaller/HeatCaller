namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the Handicaps page's Suggest from History.
///
/// Suggestions are always time credits. In a class that counts lap credit
/// (or has handicaps off) the status line still said "Applied 3 suggested
/// handicaps.", though the seconds change no result.
/// </summary>
public sealed class BugHunt24_HandicapControlTests
{
    [Fact]
    public void Suggested_time_credits_in_a_time_credit_class_still_say_applied()
    {
        Assert.Equal("Applied 2 suggested handicaps.",
            HandicapControl.SuggestionsAppliedWords(2, HandicapMode.TimeCredit));
        Assert.Equal("Applied 1 suggested handicap.",
            HandicapControl.SuggestionsAppliedWords(1, HandicapMode.TimeCredit));
    }

    [Fact]
    public void Suggested_time_credits_in_a_lap_credit_class_say_they_do_not_change_results()
    {
        var words = HandicapControl.SuggestionsAppliedWords(3, HandicapMode.LapCredit);

        Assert.DoesNotContain("Applied", words);
        Assert.Contains("3 suggested time credits", words);
        Assert.Contains("lap credit", words);
        Assert.Contains("do not change", words);
    }

    [Fact]
    public void Suggested_time_credits_with_handicaps_off_say_they_do_not_change_results()
    {
        var words = HandicapControl.SuggestionsAppliedWords(1, HandicapMode.None);

        Assert.DoesNotContain("Applied", words);
        Assert.Contains("1 suggested time credit.", words);
        Assert.Contains("off", words);
        Assert.Contains("do not change", words);
    }
}
