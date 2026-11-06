namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11b, CommentaryFacts.ComingUpNext. What the hunt found:
///
///  - The estimate was gated on the RAW minutes (1 to under 120) and then
///    rounded for the sentence, so 1m10s read "about 1 minutes away" and
///    119.6 minutes rounded up to "about 120 minutes" - past the two-hour
///    cap the gate was there to enforce.
///  - A one-car round was listed as "1 cars".
///
/// CommentaryContinuity.Ahead already avoided both; these pin the fact
/// builder to the same rules.
/// </summary>
public sealed class BugHunt11b_ComingUpNextTests
{
    private static readonly UpcomingRound[] Main = { new("A-Main", "Box Stock", 10) };

    [Fact]
    public void A_minute_and_a_bit_is_not_read_as_1_minutes()
    {
        var text = CommentaryFacts.ComingUpNext(Main, TimeSpan.FromSeconds(70))!.Text;

        Assert.DoesNotContain("1 minutes", text);
        Assert.DoesNotContain("away", text);
    }

    [Fact]
    public void An_estimate_that_rounds_to_two_hours_is_past_the_cap()
    {
        var text = CommentaryFacts.ComingUpNext(Main, TimeSpan.FromMinutes(119.6))!.Text;

        Assert.DoesNotContain("120 minutes", text);
        Assert.DoesNotContain("away", text);
    }

    [Fact]
    public void A_one_car_round_is_not_1_cars()
    {
        var text = CommentaryFacts.ComingUpNext(new[] { new UpcomingRound("Q3 Heat 4", "Open", 1) })!.Text;

        Assert.DoesNotContain("1 cars", text);
        Assert.Contains("Q3 Heat 4 (Open)", text);
    }

    [Fact]
    public void An_ordinary_gap_reads_exactly_as_it_always_did()
    {
        var block = CommentaryFacts.ComingUpNext(new[]
        {
            new UpcomingRound("Q3 Heat 1", "Box Stock", 8),
            new UpcomingRound("Q3 Heat 2", "Open", 7),
        }, TimeSpan.FromMinutes(4.2));

        Assert.Equal(
            "Coming up: Q3 Heat 1 (Box Stock), 8 cars; then Q3 Heat 2 (Open), 7 cars. The next one is about 4 minutes away.",
            block!.Text);
    }

    [Fact]
    public void Just_under_the_cap_still_says_its_estimate()
    {
        var text = CommentaryFacts.ComingUpNext(Main, TimeSpan.FromMinutes(119.4))!.Text;

        Assert.Contains("about 119 minutes away", text);
    }
}
