namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 22 (20 Nov 2026), the run sheet: the finals planned for a
/// class with none built yet, and the next race's card under a "drop"
/// preview.
/// </summary>
public sealed class BugHunt22_RunSheetTests
{
    private static readonly RaceFormat Format = new() { TimeLimitMinutes = 5 };
    private static readonly TimeSpan Five = TimeSpan.FromMinutes(5);

    private static int Races(List<NightPlan.UnbuiltMains> unbuilt) => unbuilt.Sum(u => u.Count);

    [Fact]
    public void A_plain_class_still_plans_one_main_per_heat_of_drivers()
    {
        var unbuilt = NightPlan.UnbuiltFor("Stock", new RaceClass { Name = "Stock" }, 18, 8, oneMainFewer: false, Format, Five);

        Assert.Equal(3, Races(unbuilt));
        Assert.Equal(2, Races(NightPlan.UnbuiltFor("Stock", new RaceClass { Name = "Stock" }, 18, 8, oneMainFewer: true, Format, Five)));
    }

    [Fact]
    public void An_invitational_with_a_shootout_plans_its_runs_and_every_main()
    {
        var raceClass = new RaceClass { Name = "Mod", Invitational = true, InvitationalMains = 5, PoleShootout = true, PoleShootoutDrivers = 5 };

        var unbuilt = NightPlan.UnbuiltFor("Mod", raceClass, 10, 10, oneMainFewer: false, Format, Five);

        Assert.Equal(2, unbuilt.Count);
        Assert.Equal(5, unbuilt[0].Count);                // the runs, first
        Assert.True(unbuilt[0].Length < Five);            // one car, a lap or three
        Assert.Equal(5, unbuilt[1].Count);                // the mains
        Assert.Equal(10, Races(unbuilt));
    }

    [Fact]
    public void An_a_main_in_legs_plans_each_leg()
    {
        var aOnly = new RaceClass { Name = "Stock", AMainLegs = 3 };
        var all = new RaceClass { Name = "Stock", AMainLegs = 3, LowerMainsInLegs = true };

        Assert.Equal(2 + 3, Races(NightPlan.UnbuiltFor("Stock", aOnly, 24, 8, false, Format, Five)));
        Assert.Equal(3 * 3, Races(NightPlan.UnbuiltFor("Stock", all, 24, 8, false, Format, Five)));
    }

    [Fact]
    public void The_next_race_dropped_in_a_preview_still_has_the_card()
    {
        // With the next race's own row hidden under the "cut" line, nothing
        // hosted Start, Call and Skip; the card now goes on a row of its own.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        var sheet = File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", "RaceProgramControl.RunSheet.cs"));

        Assert.Contains("&& !dropped.Contains(next.Id);", sheet);
    }
}
