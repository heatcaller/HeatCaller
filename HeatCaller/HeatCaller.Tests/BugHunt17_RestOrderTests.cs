namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: the "too close together" question before a change by hand
/// only named clashes the night didn't already have - and told them apart
/// by driver and the two races alone. A drag that made a clash the director
/// had lived with WORSE (one race between down to back to back) kept the
/// same driver and races, so it saved without a word.
/// </summary>
public sealed class BugHunt17_RestOrderTests
{
    private static (string Label, IReadOnlyCollection<string> Drivers) Race(string label, params string[] drivers) => (label, drivers);

    [Fact]
    public void A_clash_the_change_makes_closer_is_warned_about()
    {
        // Two races off asked for. Max already has only one between.
        var before = new[] { Race("Heat 1", "max"), Race("Heat 2", "sam"), Race("Heat 3", "max") };

        // Dragged so Heat 3 follows Heat 1: back to back now.
        var after = new[] { Race("Heat 1", "max"), Race("Heat 3", "max"), Race("Heat 2", "sam") };

        var tooClose = Assert.Single(RestOrder.NewlyTooClose(before, after, 2));

        Assert.Equal("max", tooClose.Transponder);
        Assert.Equal(0, tooClose.RacesBetween);
    }

    [Fact]
    public void A_clash_left_as_close_or_made_less_close_is_still_not_the_change_s_fault()
    {
        var backToBack = new[] { Race("Heat 1", "max"), Race("Heat 3", "max"), Race("Heat 2", "sam"), Race("Heat 4", "kim") };
        var oneBetween = new[] { Race("Heat 1", "max"), Race("Heat 2", "sam"), Race("Heat 3", "max"), Race("Heat 4", "kim") };

        // Something else moved; Max's clash is as it was.
        var kimFirst = new[] { Race("Heat 4", "kim"), Race("Heat 1", "max"), Race("Heat 3", "max"), Race("Heat 2", "sam") };
        Assert.Empty(RestOrder.NewlyTooClose(backToBack, kimFirst, 2));

        // Still short of two races off, but better than it was.
        Assert.Empty(RestOrder.NewlyTooClose(backToBack, oneBetween, 2));
    }
}
