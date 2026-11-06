namespace HeatCaller.Tests;

/// <summary>
/// 6 Nov 2026, Bug hunt 11b: CommentaryContinuity's "still to come" checked
/// its two-hour cap on the raw estimate and then said it rounded, so 119.6
/// minutes went out as "about 120 minutes".
/// </summary>
public sealed class BugHunt11b_ContinuityEstimateTests
{
    private static readonly IReadOnlyList<UpcomingRound> NextUp = new[]
    {
        new UpcomingRound("A-Main", "Box Stock", 10),
    };

    private static List<string> EveryLine(TimeSpan inAbout)
    {
        var voice = new ContinuityVoice(seed: 1704);
        var heard = new List<string>();

        for (var i = 0; i < 40; i++)
        {
            if (CommentaryContinuity.StillToCome(NextUp, inAbout, voice) is not { } line)
            {
                break;
            }

            voice.Said(line);
            heard.Add(line);
        }

        return heard;
    }

    [Fact]
    public void An_estimate_that_rounds_to_two_hours_is_not_said()
    {
        var heard = EveryLine(TimeSpan.FromMinutes(119.6));

        Assert.NotEmpty(heard);
        Assert.DoesNotContain(heard, line => line.Contains("minute", StringComparison.Ordinal));
    }

    [Fact]
    public void An_estimate_just_under_the_cap_is_still_said_as_rounded()
    {
        var heard = EveryLine(TimeSpan.FromMinutes(119.4));

        Assert.Contains(heard, line => line.Contains("119 minutes", StringComparison.Ordinal));
    }
}
