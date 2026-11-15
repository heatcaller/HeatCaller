namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19 (14 Nov 2026), #21, #22 and #44.
///
/// #21: the night pace report named the after-Q1 break twice when a held Q1
/// heat ran after Q2 Heat 1, so the second gap's real cause ("idle
/// director") was hidden.
///
/// #22: "finals 2 min shorter" claimed the full 2 minutes for every main
/// whose slot (staging and countdown included) was 5 minutes or more, though
/// the cut never takes a time limit below 3 minutes and does nothing to a
/// lap race.
///
/// #44: a second "We're done after this round" offered the finals cuts
/// again with savings, though a cut already chosen changes nothing.
/// </summary>
public sealed class BugHunt19NightPaceAndFinalsCutTests
{
    private static readonly DateTime Seven = new(2026, 11, 7, 19, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double minutes) => Seven.AddMinutes(minutes);

    private static SessionRecord Race(string id, double start) => new()
    {
        Id = "s-" + id,
        SessionType = RaceSessionType.Race,
        Label = id,
        RoundId = id,
        StartedAtUtc = At(start),
        EndedAtUtc = At(start + 5),
    };

    private static RoundDefinition Round(string id, string label) => new()
    {
        Id = id,
        RoundLabel = label,
        ClassName = "Stock",
        Kind = RoundKind.Qualifier,
        CreatedAtUtc = At(-120),
    };

    [Fact]
    public void A_held_heat_run_late_does_not_name_the_planned_break_a_second_time()
    {
        var rounds = new[]
        {
            Round("q1h1", "Q1 Heat 1"), Round("q1h2", "Q1 Heat 2"), Round("q1h3", "Q1 Heat 3"),
            Round("q2h1", "Q2 Heat 1"), Round("q1h4", "Q1 Heat 4"), Round("q2h2", "Q2 Heat 2"),
        };

        // Q1 Heat 4 held and run after Q2 Heat 1. The break falls before Q2
        // Heat 1 (a 13-minute gap); the 11-minute gap before Q2 Heat 2 is
        // nobody pressing anything.
        var races = new[]
        {
            Race("q1h1", 0), Race("q1h2", 8), Race("q1h3", 16),
            Race("q2h1", 34), Race("q1h4", 42), Race("q2h2", 58),
        };

        var breaks = new[] { new PlannedBreak { AfterRound = 1, Minutes = 10 } };
        var pace = NightPace.For(races, rounds, setGap: TimeSpan.FromSeconds(150), breaks: breaks)!;

        var toQ2Heat1 = pace.LongestGaps.Single(g => g.After.Id == "s-q2h1");
        var toQ2Heat2 = pace.LongestGaps.Single(g => g.After.Id == "s-q2h2");

        Assert.Equal(new[] { "a planned break (10 min)" }, toQ2Heat1.Causes);
        Assert.Equal(new[] { "idle director" }, toQ2Heat2.Causes);
    }

    private static NightPlan.Projection Plan(params (string Class, RaceFormat Format)[] mains)
    {
        var slots = mains
            .Select((m, i) => new NightPlan.Slot(null, "Main", m.Class, RoundKind.Main, NightPlan.LengthOf(m.Format), At(i * 10)) { Format = m.Format })
            .ToList();

        return new NightPlan.Projection(slots, slots[^1].EndUtc, TimeSpan.FromMinutes(3), false, At(0), null, Array.Empty<NightPlan.Fix>());
    }

    private static RaceFormat Timed(int minutes) => new() { TimeLimitMinutes = minutes, StagingSeconds = 120, CountdownSeconds = 10 };

    [Fact]
    public void Shorter_finals_count_only_what_the_cut_really_takes_off()
    {
        // A 3-minute main with staging is a 5:10 slot, but stays 3 minutes.
        Assert.False(NightPlan.FinalsCuts(Plan(("Stock", Timed(3))))!.CanShorten);

        // A lap race has no time limit to shorten.
        Assert.False(NightPlan.FinalsCuts(Plan(("Stock", new RaceFormat { TimeLimitMinutes = 0 })))!.CanShorten);

        // A 4-minute main loses 1 minute, a 6-minute main 2.
        Assert.Equal(TimeSpan.FromMinutes(1), NightPlan.FinalsCuts(Plan(("Stock", Timed(4))))!.ShorterSaves);
        Assert.Equal(TimeSpan.FromMinutes(3), NightPlan.FinalsCuts(Plan(("Stock", Timed(4)), ("Mod", Timed(6))))!.ShorterSaves);
    }

    [Fact]
    public void The_plan_carries_each_unbuilt_mains_format_to_the_cut()
    {
        var plan = NightPlan.For(
            Array.Empty<RoundDefinition>(),
            _ => TimeSpan.FromMinutes(5),
            new[] { new NightPlan.UnbuiltMains("Stock", 2, NightPlan.LengthOf(Timed(3)), Timed(3)) },
            null, null, null, null, At(0))!;

        Assert.All(plan.Slots, s => Assert.Equal(3, s.Format!.TimeLimitMinutes));
    }

    [Fact]
    public void A_cut_already_chosen_tonight_is_not_offered_again()
    {
        // Stock already shortened by 2 tonight: its slots are 4-minute mains now.
        var plan = Plan(("Stock", Timed(6)), ("Stock", Timed(6)));

        var again = NightPlan.FinalsCuts(plan, minutesShorterTonight: NightPlan.FinalsShorterBy, oneMainFewerTonight: true)!;

        Assert.False(again.CanShorten);
        Assert.False(again.CanDropOne);
        Assert.Empty(again.FewerClasses);

        // Not chosen yet: both are offered.
        var first = NightPlan.FinalsCuts(plan)!;
        Assert.Equal(TimeSpan.FromMinutes(4), first.ShorterSaves);
        Assert.True(first.CanDropOne);
    }
}
