namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), #29: "Qualifiers 1 min shorter" counted a
/// minute for every qualifier whose slot - staging and countdown included,
/// and 5 minutes for a lap race - was 3 minutes or more, though the change
/// (NightPlan.Shortened) does nothing to a lap race and never takes a time
/// limit below 3 minutes. A night of 3-minute or lap-count heats was offered
/// a saving that ticking the fix could not deliver.
/// </summary>
public sealed class BugHunt24_NightPlanTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);

    private static RaceFormat Timed(int minutes, int staging = 30, int countdown = 10) =>
        new() { TimeLimitMinutes = minutes, StagingSeconds = staging, CountdownSeconds = countdown };

    private static RoundDefinition Heat(string label, RaceFormat format, string className = "Stock") => new()
    {
        RoundLabel = label,
        ClassName = className,
        Kind = RoundKind.Qualifier,
        FormatOverride = format,
    };

    /// <summary>Six heats, Q2 and Q3, all to one format.</summary>
    private static RoundDefinition[] SixHeats(RaceFormat format) => new[]
    {
        Heat("Q2 Heat 1", format), Heat("Q2 Heat 2", format), Heat("Q2 Heat 3", format),
        Heat("Q3 Heat 1", format), Heat("Q3 Heat 2", format), Heat("Q3 Heat 3", format),
    };

    /// <summary>A plan well past an out-by five minutes from now, carrying each heat's format.</summary>
    private static NightPlan.Projection LatePlan(IReadOnlyList<RoundDefinition> rounds)
    {
        var appDefault = Timed(5);
        RaceFormat FormatOf(RoundDefinition r) => NightPlan.FormatOf(r, null, appDefault);

        return NightPlan.For(
            rounds,
            r => NightPlan.LengthOf(FormatOf(r)),
            null, null, null, null,
            Now.AddMinutes(5).ToLocalTime().TimeOfDay,
            Now,
            formatOf: FormatOf)!;
    }

    private static NightPlan.Fix? ShorterQualifiers(NightPlan.Projection plan) =>
        plan.Fixes.FirstOrDefault(f => f.Kind == NightPlan.FixKind.ShorterQualifiers);

    [Fact]
    public void Three_minute_qualifiers_are_not_offered_a_minute_shorter()
    {
        // A 3:40 slot, but the time limit is already as short as it goes.
        var plan = LatePlan(SixHeats(Timed(3)));

        Assert.NotNull(plan.Over);
        Assert.Null(ShorterQualifiers(plan));
    }

    [Fact]
    public void Lap_count_qualifiers_are_not_offered_a_minute_shorter()
    {
        var plan = LatePlan(SixHeats(new RaceFormat { TimeLimitMinutes = 0 }));

        Assert.NotNull(plan.Over);
        Assert.Null(ShorterQualifiers(plan));
    }

    [Fact]
    public void Two_minute_heats_with_a_minute_of_staging_are_not_offered_a_minute_shorter()
    {
        var plan = LatePlan(SixHeats(Timed(2, staging: 50, countdown: 10)));

        Assert.NotNull(plan.Over);
        Assert.Null(ShorterQualifiers(plan));
    }

    [Fact]
    public void A_mixed_night_counts_only_the_heats_that_can_lose_a_minute()
    {
        var five = Timed(5);
        var three = Timed(3);

        var rounds = new[]
        {
            Heat("Q2 Heat 1", five), Heat("Q2 Heat 2", five), Heat("Q2 Heat 3", three, "Mod"),
            Heat("Q3 Heat 1", five), Heat("Q3 Heat 2", five), Heat("Q3 Heat 3", three, "Mod"),
        };

        var fix = ShorterQualifiers(LatePlan(rounds));

        Assert.NotNull(fix);
        Assert.Equal(TimeSpan.FromMinutes(4), fix!.Saves);
        Assert.Equal("run qualifiers a minute shorter to save 4 min", fix.Text);
    }

    [Fact]
    public void Once_the_heats_are_down_to_three_minutes_the_fix_is_no_longer_offered()
    {
        var five = Timed(5);

        // Used once: 5 -> 4, still offered.
        var four = NightPlan.Shortened(five, 1)!;
        Assert.Equal(TimeSpan.FromMinutes(6), ShorterQualifiers(LatePlan(SixHeats(four)))!.Saves);

        // Used twice: 4 -> 3, nothing more to take off.
        var three = NightPlan.Shortened(four, 1)!;
        Assert.Equal(3, three.TimeLimitMinutes);
        Assert.Null(ShorterQualifiers(LatePlan(SixHeats(three))));
    }

    [Fact]
    public void FormatOf_chooses_the_format_as_Race_Control_does()
    {
        var appDefault = Timed(5);
        var classFormat = Timed(6);
        var qualifierFormat = Timed(4);
        var raceClass = new RaceClass { Name = "Stock", Format = classFormat, QualifierFormat = qualifierFormat };

        var heat = new RoundDefinition { RoundLabel = "Q1 Heat 1", ClassName = "Stock", Kind = RoundKind.Qualifier };
        var main = new RoundDefinition { RoundLabel = "A-Main", ClassName = "Stock", Kind = RoundKind.Main };

        Assert.Same(qualifierFormat, NightPlan.FormatOf(heat, raceClass, appDefault));
        Assert.Same(classFormat, NightPlan.FormatOf(main, raceClass, appDefault));
        Assert.Same(appDefault, NightPlan.FormatOf(heat, null, appDefault));

        var overridden = Timed(7);
        heat.FormatOverride = overridden;
        Assert.Same(overridden, NightPlan.FormatOf(heat, raceClass, appDefault));
    }

    [Fact]
    public void A_heat_with_no_format_given_keeps_the_old_slot_test()
    {
        var slot = new NightPlan.Slot(null, "Q1 Heat 1", "Stock", RoundKind.Qualifier, TimeSpan.FromMinutes(5), Now);

        Assert.Equal(TimeSpan.FromMinutes(1), NightPlan.QualifierMinuteSaves(slot));
        Assert.Equal(TimeSpan.Zero, NightPlan.QualifierMinuteSaves(slot with { Format = Timed(3) }));
    }
}
