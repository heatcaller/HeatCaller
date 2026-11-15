namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19, the race program:
///  - #28: on a night whose heats were never posted, a late check-in after
///    the first race was said to have come "after the heats were posted".
///    It now says "after racing started".
///  - #29: the rest-gap warning named heats without their class, so a
///    driver in two classes read "Q1 Heat 1 and Q1 Heat 1 back to back",
///    and a new clash with the same labels as an old one was passed as
///    already there.
/// </summary>
public sealed class BugHunt19ProgramTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void The_late_check_in_headline_only_says_posted_when_the_heats_were()
    {
        Assert.Equal("Max Power checked in after the heats were posted.", LateArrivals.Headline(new[] { "Max Power" }, clubNight: false, posted: true));
        Assert.Equal("Max Power checked in after racing started.", LateArrivals.Headline(new[] { "Max Power" }, clubNight: false, posted: false));
        Assert.Equal("2 drivers checked in after racing started.", LateArrivals.Headline(new[] { "Al", "Hugh" }, clubNight: true, posted: false));
        Assert.Equal("tonight's races were posted", LateArrivals.After(clubNight: true, posted: true, "tonight's"));
    }

    [Fact]
    public void A_late_check_in_after_a_race_on_a_night_never_posted_says_racing_started() => BugHunt17_RaceProgramControlTests.OnUiThread(() =>
    {
        using var night = new BugHunt17Night("Stock");
        night.Driver("1", "Max Power", "Stock");
        night.Driver("2", "Al Gorithim", "Stock");
        Assert.True(night.Program.BuildQualifying(string.Empty).Ok);

        // The first heat runs; Post the Heats was never pressed.
        var first = night.DayRounds().First(r => r.Kind != RoundKind.Practice);
        Assert.True(night.Rounds.MarkRecorded(first.Id, "session-1"));
        Assert.Null(night.Events.Active!.HeatsPostedUtc);

        night.Driver("5", "Axel Grease", "Stock");
        var step = night.Program.NextStep();

        Assert.Equal(ProgramStage.LateArrivals, step.Stage);
        Assert.Equal("Axel Grease checked in after racing started.", step.Headline);
    });

    [Fact]
    public void The_running_order_names_each_heats_class()
    {
        var stock = new RoundDefinition { Id = "s", ClassName = "Stock", RoundLabel = "Q1 Heat 1", Kind = RoundKind.Qualifier };
        var mod = new RoundDefinition { Id = "m", ClassName = "Mod", RoundLabel = "Q1 Heat 1", Kind = RoundKind.Qualifier };
        var order = RaceProgramControl.RunningOrder(new[] { stock, mod }, _ => new[] { "max" });

        Assert.Equal(new[] { "Stock Q1 Heat 1", "Mod Q1 Heat 1" }, order.Select(o => o.Label));

        var words = RestOrder.Warning(RestOrder.TooClose(order, 1), t => "Max Power", 1);
        Assert.StartsWith("Max Power would race in Stock Q1 Heat 1 and Mod Q1 Heat 1 back to back.", words);
    }

    [Fact]
    public void A_new_back_to_back_with_the_same_heat_labels_as_an_old_one_is_still_warned_about()
    {
        RoundDefinition Heat(string id, string className, string label) =>
            new() { Id = id, ClassName = className, RoundLabel = label, Kind = RoundKind.Qualifier };

        var stockQ1 = Heat("s1", "Stock", "Q1 Heat 1");
        var modQ1 = Heat("m1", "Mod", "Q1 Heat 1");
        var stockQ2 = Heat("s2", "Stock", "Q2 Heat 1");
        var modQ2 = Heat("m2", "Mod", "Q2 Heat 1");
        IReadOnlyCollection<string> Max(RoundDefinition _) => new[] { "max" };

        // Before: Mod Q1 Heat 1 straight before Stock Q2 Heat 1.
        var before = RaceProgramControl.RunningOrder(new[] { stockQ1, modQ1, stockQ2, modQ2 },
            r => r == stockQ1 || r == modQ2 ? Array.Empty<string>() : Max(r));
        // After: Stock Q1 Heat 1 straight before Mod Q2 Heat 1 - a different clash.
        var after = RaceProgramControl.RunningOrder(new[] { modQ1, stockQ2, stockQ1, modQ2 },
            r => r == modQ1 || r == stockQ2 ? Array.Empty<string>() : Max(r));

        Assert.Single(RestOrder.TooClose(before, 1));
        var tooClose = RestOrder.NewlyTooClose(before, after, 1);

        Assert.Single(tooClose);
        Assert.Equal("Stock Q1 Heat 1", tooClose[0].First);
        Assert.Equal("Mod Q2 Heat 1", tooClose[0].Second);
    }
}
