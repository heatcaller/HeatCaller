namespace HeatCaller.Tests;

/// <summary>
/// 23 Nov 2026, bug hunt 24: seeding a class with Pole Shootout on puts its
/// shootout runs ahead of its mains. Next Steps only looked for a main being
/// next before seeding the other classes, so the shootout runs went out with
/// another class's finals still unbuilt.
/// </summary>
public sealed class BugHunt24_ProgramNextStepTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static RoundDefinition Heat(string className, bool run) => new()
    {
        ClassName = className,
        RoundLabel = "Q1 Heat 1",
        Kind = RoundKind.Qualifier,
        AssignedTransponders = new List<string> { className + "1", className + "2" },
        LinkedSessionId = run ? Guid.NewGuid().ToString("N") : null,
    };

    private static RoundDefinition ShootoutRun(string className, int number, bool run) => new()
    {
        ClassName = className,
        RoundLabel = $"Pole Shootout Run {number}",
        Kind = RoundKind.Shootout,
        AssignedTransponders = new List<string> { className + number },
        LinkedSessionId = run ? Guid.NewGuid().ToString("N") : null,
    };

    private static RoundDefinition AMain(string className) => new()
    {
        ClassName = className,
        RoundLabel = "A-Main",
        Kind = RoundKind.Main,
        MainTier = 0,
        AssignedTransponders = new List<string> { className + "1", className + "2" },
    };

    private static ProgramStep Step(params RoundDefinition[] rounds) =>
        ProgramNextStep.Evaluate(
            new RaceEvent { Name = "Race Day 23 Nov 2026" },
            rounds,
            new[] { new ProgramNextStep.RacingClass("Mod", 2), new ProgramNextStep.RacingClass("Stock", 2) },
            1,
            SessionHistoryStore.Load(),
            new ProgramNextStep.SetupFacts(2, 4, 4, false, 4));

    [Fact]
    public void With_a_shootout_run_next_the_other_class_is_seeded_first()
    {
        var step = Step(
            Heat("Mod", run: true), Heat("Stock", run: true),
            ShootoutRun("Mod", 1, run: false), ShootoutRun("Mod", 2, run: false), AMain("Mod"));

        Assert.Equal(ProgramStage.SeedMains, step.Stage);
        Assert.Equal("Stock", step.ClassName);
    }

    [Fact]
    public void Once_a_shootout_run_has_raced_the_next_run_is_offered_not_the_seeding()
    {
        // The director has gone on: a class that can't be seeded must not hold the banner.
        var step = Step(
            Heat("Mod", run: true), Heat("Stock", run: true),
            ShootoutRun("Mod", 1, run: true), ShootoutRun("Mod", 2, run: false), AMain("Mod"));

        Assert.Equal(ProgramStage.RunNext, step.Stage);
        Assert.Equal("Pole Shootout Run 2", step.Round!.RoundLabel);
    }

    [Fact]
    public void With_every_class_seeded_the_shootout_run_is_next()
    {
        var step = Step(
            Heat("Mod", run: true), Heat("Stock", run: true),
            ShootoutRun("Mod", 1, run: false), AMain("Mod"), AMain("Stock"));

        Assert.Equal(ProgramStage.RunNext, step.Stage);
        Assert.Equal("Pole Shootout Run 1", step.Round!.RoundLabel);
    }
}
