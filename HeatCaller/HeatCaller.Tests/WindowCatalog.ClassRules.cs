namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// Two of a class's rules, from its editor (RaceClassEditForm). The
    /// warning ladder (WarningLadderForm) as a class with none of its own
    /// opens it - the default ladder, its fifth rung "(end)" - and with all
    /// five rungs set, every action among them and a penalty at the most
    /// seconds the box takes. And the sanctioning presets
    /// (SanctioningPresetForm) for a class that follows the app default,
    /// which puts the note about that under the presets, and for a class
    /// with a format of its own, which leaves the note out.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> ClassRuleWindows() => new (string, Func<Form>)[]
    {
        ("Warnings", () => new WarningLadderForm(null, "Warnings - Stock Buggy")),

        ("Warnings (five rungs, every action)", () => new WarningLadderForm(
            new List<WarningStep>
            {
                new WarningStep(WarningAction.Warning),
                new WarningStep(WarningAction.TimePenalty, 5),
                new WarningStep(WarningAction.TimePenalty, 600),
                new WarningStep(WarningAction.Warning),
                new WarningStep(WarningAction.BlackFlag),
            },
            "Warnings - Stock Buggy")),

        ("Sanctioning preset", () => new SanctioningPresetForm(
            new RaceClass { Name = "Stock Buggy" },
            RaceFormat.FromSettings(new AppSettings()))),

        ("Sanctioning preset (a format of its own)", () => new SanctioningPresetForm(
            new RaceClass { Name = "Stock Buggy", Format = RaceFormat.FromSettings(new AppSettings()) },
            RaceFormat.FromSettings(new AppSettings()))),
    };
}
