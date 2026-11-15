namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19 (14 Nov 2026) #8: a driver seated on their second car's
/// transponder taps "Skip me" (or "Need 5 minutes"), the desk says yes, and
/// nothing happens - the phone asks by the main transponder, and the grid
/// holds the alternate one.
/// </summary>
public sealed class BugHunt19SkipAlternateTransponderTests
{
    // ALT555 is Max's second car; 555 is his main transponder.
    private static string? MainOf(string id) =>
        string.Equals(id, "ALT555", StringComparison.OrdinalIgnoreCase) ? "555" : null;

    [Fact]
    public void A_driver_on_their_second_cars_transponder_is_found_by_their_main_one()
    {
        var grid = new List<string> { "101", "ALT555", "202" };

        Assert.Equal("ALT555", RaceControlControl.SeatOn(grid, "555", MainOf));
    }

    [Fact]
    public void The_id_itself_is_found_first_whatever_the_case()
    {
        Assert.Equal("alt555", RaceControlControl.SeatOn(new List<string> { "101", "alt555" }, "ALT555", MainOf));
        Assert.Equal("555", RaceControlControl.SeatOn(new List<string> { "ALT555", "555" }, "555", MainOf));
    }

    [Fact]
    public void Someone_not_on_the_grid_is_not_found()
    {
        var grid = new List<string> { "101", "ALT555" };

        Assert.Null(RaceControlControl.SeatOn(grid, "303", MainOf));
        Assert.Null(RaceControlControl.SeatOn(grid, " ", MainOf));
        Assert.Null(RaceControlControl.SeatOn(new List<string> { "101" }, "555", MainOf));
    }

    [Fact]
    public void Skip_and_more_time_look_for_the_driver_by_any_of_their_transponders()
    {
        var skip = Source("RaceControlControl.SkipDns.cs");
        var autoStart = Source("RaceControlControl.AutoStart.cs");

        Assert.Contains("&& SeatOf(r, transponder) is not null);", skip, StringComparison.Ordinal);
        Assert.Contains("transponder = SeatOf(race, transponder) ?? transponder;", skip, StringComparison.Ordinal);
        Assert.Contains("SeatOn(race.AssignedTransponders, transponder, _profiles.PrimaryForAlternate)", skip, StringComparison.Ordinal);

        Assert.Contains("if (next is null || SeatOf(next, transponder) is null)", autoStart, StringComparison.Ordinal);
        var seat = autoStart.IndexOf("transponder = SeatOf(theirs, transponder) ?? transponder;", StringComparison.Ordinal);
        var owe = autoStart.IndexOf("_moreTimeOwed.Owe(theirs.Id, transponder, name, LateMinutes)", StringComparison.Ordinal);
        Assert.True(seat > 0 && owe > seat, "Minutes owed must be kept under the id the driver is on the grid by.");
    }

    private static string Source(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "check.cmd")))
        {
            folder = folder.Parent;
        }

        Assert.True(folder is not null, "No folder with check.cmd in it above " + AppContext.BaseDirectory);

        return File.ReadAllText(Path.Combine(folder!.FullName, "HeatCaller", "HeatCaller", fileName)).Replace("\r\n", "\n");
    }
}
