namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), RaceControlControl.DriverHistory.cs.
///
/// BACK ON FORM, SWITCHED OFF, USED UP "QUICKER THAN LAST TIME OUT". Race
/// Control answered "back on form" without knowing the call was unticked,
/// and marked the driver as told for last time out too. The announcer threw
/// the answer away, and the call that was on was never made for that driver
/// all night. Race Control is now told which calls are on.
///
/// Source checks, as DriverHistoryCallsTests': the page cannot be built in a
/// test.
/// </summary>
public sealed class BugHunt17_RaceControlControl_DriverHistoryTests
{
    [Fact]
    public void A_call_that_is_off_is_neither_answered_nor_marked_as_told()
    {
        var history = Source("RaceControlControl.DriverHistory.cs");

        var form = history.IndexOf("if (on(AnnouncementEventType.BackOnForm)", StringComparison.Ordinal);
        var formMarked = history.IndexOf("_toldTonight.Add($\"{tonight}|form|{transponder}\")", StringComparison.Ordinal);
        var last = history.IndexOf("if (on(AnnouncementEventType.BeatLastTimeOut)", StringComparison.Ordinal);
        var lastMarked = history.LastIndexOf("_toldTonight.Add($\"{tonight}|last|{transponder}\")", StringComparison.Ordinal);

        Assert.True(form > 0, "Back on form must be asked for only when it is on.");
        Assert.True(formMarked > form, "Back on form is marked as told only after it is known to be on.");
        Assert.True(last > formMarked, "Last time out is still tried after back on form.");
        Assert.True(lastMarked > last, "Last time out is marked as told only after it is known to be on.");
    }

    [Fact]
    public void The_announcer_tells_Race_Control_which_calls_are_on()
    {
        var announcer = Source("AnnouncementService.DriverHistory.cs");

        Assert.Contains("LapAgainstHistory?.Invoke(transponderId, lapMs, On)", announcer, StringComparison.Ordinal);
    }

    private static string Source(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "check.cmd")))
        {
            folder = folder.Parent;
        }

        Assert.True(folder is not null, "No folder with check.cmd in it above " + AppContext.BaseDirectory);

        return string.Join(
            "\n",
            File.ReadAllText(Path.Combine(folder!.FullName, "HeatCaller", "HeatCaller", fileName))
                .Replace("\r\n", "\n")
                .Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
    }
}
