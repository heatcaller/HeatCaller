namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The app's message box (AppDialog), as it opens for the messages that
    /// test it hardest: one line, a long question with three buttons, a
    /// title longer than any message, no icon, and a message too long for a
    /// small screen, whose text has to scroll with the buttons staying.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> MessageWindows() => new (string, Func<Form>)[]
    {
        ("Message", () => AppDialog.BuildForLayoutCheck(
            "Select a driver first.",
            "No driver selected",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information)),

        ("Message (question, three buttons)", () => AppDialog.BuildForLayoutCheck(
            "Qualifier 2 Heat 3 is still running - 4 of 8 cars have laps, and the clock says 3:12 to go.\r\n\r\n"
            + "Yes saves it as it stands and marks it unfinished, so its laps count for nothing until you say so.\r\n"
            + "No throws it away, and the heat can be run again from the start.\r\n"
            + "Cancel goes back to the race.",
            "Stop the race?",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question)),

        ("Message (error, long title)", () => AppDialog.BuildForLayoutCheck(
            "Close the other program and press Connect again.",
            "The decoder could not be opened on COM7 because another program on this laptop is already using that port",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error)),

        // The warning before Advanced mode (1 Nov 2026): as it opens between
        // races, and with the race line under it while one is running.
        ("Turn on Advanced mode?", () => new AdvancedModeForm(raceRunning: false)),
        ("Turn on Advanced mode? (race running)", () => new AdvancedModeForm(raceRunning: true)),

        // Restore default settings (round 4): between races, and greyed with
        // its line while one runs. Put back earlier settings: ten copies, the
        // most kept, and none yet.
        ("Restore default settings?", () => new RestoreDefaultsForm(raceRunning: false)),
        ("Restore default settings? (race running)", () => new RestoreDefaultsForm(raceRunning: true)),
        ("Put back earlier settings", () => new PutBackSettingsForm(
            Enumerable.Range(0, SettingsRestore.CopiesKept)
                .Select(i => ($"settings {i}.zip", new DateTime(2026, 11, 1, 21, 30, 0).AddDays(-i * 3)))
                .ToList(),
            raceRunning: false)),
        ("Put back earlier settings (none yet)", () => new PutBackSettingsForm(new List<(string, DateTime)>(), raceRunning: false)),

        // The manager PIN before Advanced mode (round 5).
        ("Manager PIN", () => new ManagerPinForm("482913")),

        ("Message (no icon)", () => AppDialog.BuildForLayoutCheck(
            "Every driver ticked here is added to the round, in the order they are ticked.",
            string.Empty,
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.None)),

        ("Message (very long)", () => AppDialog.BuildForLayoutCheck(
            string.Join("\r\n", Enumerable.Range(1, 40).Select(i =>
                $"Alexandria Montgomery-Fitzwilliam {i} has no transponder number, so their laps cannot be counted.")),
            "Drivers without a transponder",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning)),
    };
}
