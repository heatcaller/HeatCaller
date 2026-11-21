namespace HeatCaller.Tests;

/// <summary>
/// 20 Nov 2026, head-to-head baseline: once qualifying is over, Next Steps
/// seeds every class's finals before it offers to start the first final.
/// Seeding Mod used to make Mod's A-Main "next up", and Stock's finals
/// stayed unbuilt until it had run.
/// </summary>
public sealed class NextStepSeedsEveryClassTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static RoundDefinition Heat(string className, string label, bool run) => new()
    {
        ClassName = className,
        RoundLabel = label,
        Kind = RoundKind.Qualifier,
        AssignedTransponders = new List<string> { className + "1", className + "2" },
        LinkedSessionId = run ? Guid.NewGuid().ToString("N") : null,
    };

    private static RoundDefinition Main(string className) => new()
    {
        ClassName = className,
        RoundLabel = "A-Main",
        Kind = RoundKind.Main,
        MainTier = 0,
        AssignedTransponders = new List<string> { className + "1", className + "2" },
    };

    private static ProgramStep Step(params RoundDefinition[] rounds) =>
        ProgramNextStep.Evaluate(
            new RaceEvent { Name = "Race Day 20 Nov 2026" },
            rounds,
            new[] { new ProgramNextStep.RacingClass("Mod", 2), new ProgramNextStep.RacingClass("Stock", 2) },
            1,
            SessionHistoryStore.Load(),
            new ProgramNextStep.SetupFacts(2, 4, 4, false, 4));

    [Fact]
    public void After_one_class_is_seeded_the_next_step_seeds_the_other_before_any_final_starts()
    {
        var step = Step(Heat("Mod", "Q1 Heat 1", run: true), Heat("Stock", "Q1 Heat 1", run: true), Main("Mod"));

        Assert.Equal(ProgramStage.SeedMains, step.Stage);
        Assert.Equal("Stock", step.ClassName);
    }

    [Fact]
    public void With_every_class_seeded_the_first_final_is_next()
    {
        var step = Step(Heat("Mod", "Q1 Heat 1", run: true), Heat("Stock", "Q1 Heat 1", run: true), Main("Mod"), Main("Stock"));

        Assert.Equal(ProgramStage.RunNext, step.Stage);
        Assert.Equal("Mod", step.Round!.ClassName);
    }

    [Fact]
    public void A_heat_still_to_run_comes_before_another_class_s_seeding()
    {
        // Stock has qualified and has no finals, but Mod's heat is still to run: the heat first.
        var step = Step(Heat("Stock", "Q1 Heat 1", run: true), Heat("Mod", "Q1 Heat 1", run: false));

        Assert.Equal(ProgramStage.RunNext, step.Stage);
        Assert.Equal("Mod", step.Round!.ClassName);
    }

    [Fact]
    public void Once_a_final_has_run_a_class_with_no_finals_no_longer_holds_the_next_final()
    {
        // Bug hunt 23: Stock can't be seeded (or isn't getting finals tonight). The director
        // started Mod's B-Main from the program; the A-Main is next, not Stock's seeding again.
        var bMain = Main("Mod");
        bMain.RoundLabel = "B-Main";
        bMain.MainTier = 1;
        bMain.LinkedSessionId = Guid.NewGuid().ToString("N");

        var step = Step(Heat("Mod", "Q1 Heat 1", run: true), Heat("Stock", "Q1 Heat 1", run: true), bMain, Main("Mod"));

        Assert.Equal(ProgramStage.RunNext, step.Stage);
        Assert.Equal("A-Main", step.Round!.RoundLabel);
    }

    [Fact]
    public void With_every_final_run_the_class_with_none_is_offered_again()
    {
        var aMain = Main("Mod");
        aMain.LinkedSessionId = Guid.NewGuid().ToString("N");

        var step = Step(Heat("Mod", "Q1 Heat 1", run: true), Heat("Stock", "Q1 Heat 1", run: true), aMain);

        Assert.Equal(ProgramStage.SeedMains, step.Stage);
        Assert.Equal("Stock", step.ClassName);
    }
}
