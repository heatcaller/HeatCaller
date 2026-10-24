namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The two small windows Race Control opens from its leaderboard: the
    /// seconds its Penalty... asks for (RaceControlControl.TimePenaltyForm),
    /// which shows the same whoever it is for; and the reason its warnings
    /// list's "Other reason..." asks for (RaceControlControl.WarningReasonForm),
    /// titled with the driver's name - the longest name there is, and a short
    /// one. Neither needs a race on the board to be built.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> RaceControlPopupWindows() => new (string, Func<Form>)[]
    {
        ("Time penalty", () => new RaceControlControl.TimePenaltyForm()),
        ("Warning for", () => new RaceControlControl.WarningReasonForm(Drivers(12)[11].DriverName)),
        ("Warning for (short name)", () => new RaceControlControl.WarningReasonForm(Drivers(1)[0].DriverName)),

        // Coach (name) > Target lap... (22 Oct 2026), titled with the
        // driver's name - the longest there is.
        ("Target lap for", () => new RaceControlControl.TargetLapForm(Drivers(12)[11].DriverName, 9_200)),
    };
}
