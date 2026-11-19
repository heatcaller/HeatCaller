namespace HeatCaller.Tests;

/// <summary>
/// The row the sidebar highlights after a rebuild - see MainForm.NavRowToSelect.
/// What was left of bug hunt 16's tests once Simple mode, and its "In Advanced
/// mode" rows, were taken out (18 Nov 2026).
/// </summary>
public sealed class NavRowToSelectTests
{
    [Fact]
    public void Venues_is_an_ordinary_page()
    {
        var names = new[] { "Venues", "Tracks" };

        Assert.Equal("Venues", MainForm.NavRowToSelect(names, keptShown: null, previouslySelected: "Classes"));
    }

    [Fact]
    public void The_usual_order_holds()
    {
        var names = new[] { "Drivers", "Race Control", "Classes" };

        Assert.Equal("Classes", MainForm.NavRowToSelect(names, keptShown: "Classes", previouslySelected: "Drivers"));
        Assert.Equal("Drivers", MainForm.NavRowToSelect(names, keptShown: null, previouslySelected: "Drivers"));
        Assert.Equal("Race Control", MainForm.NavRowToSelect(names, keptShown: null, previouslySelected: "Venues"));
    }

    [Fact]
    public void An_empty_list_falls_back_to_Race_Control()
    {
        Assert.Equal("Race Control", MainForm.NavRowToSelect(Array.Empty<string>(), keptShown: null, previouslySelected: null));
    }
}
