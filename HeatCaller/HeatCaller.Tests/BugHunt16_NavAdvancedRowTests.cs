namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16: a sidebar search in Simple mode that turns up only rows
/// under "In Advanced mode" must not highlight one of them - that showed the
/// hidden page with no warning, or left the Advanced mode settings row dead
/// to a click. See MainForm.NavRowToSelect.
/// </summary>
public sealed class BugHunt16_NavAdvancedRowTests
{
    [Fact]
    public void A_search_finding_only_Venues_in_Simple_mode_highlights_nothing()
    {
        var names = new[] { "Venues", SimpleMode.SearchRow };

        Assert.Null(MainForm.NavRowToSelect(names, keptShown: null, previouslySelected: "Drivers", showAll: false));
    }

    [Fact]
    public void A_search_finding_only_the_Advanced_settings_row_highlights_nothing()
    {
        var names = new[] { SimpleMode.SearchRow };

        Assert.Null(MainForm.NavRowToSelect(names, keptShown: null, previouslySelected: "Race Control", showAll: false));
    }

    [Fact]
    public void An_Advanced_row_picked_before_is_not_highlighted_again()
    {
        var names = new[] { "Venues", SimpleMode.SearchRow };

        Assert.Null(MainForm.NavRowToSelect(names, keptShown: null, previouslySelected: "Venues", showAll: false));
    }

    [Fact]
    public void A_page_in_the_results_is_picked_ahead_of_the_Advanced_rows()
    {
        var names = new[] { "Drivers", "Venues", SimpleMode.SearchRow };

        Assert.Equal("Drivers", MainForm.NavRowToSelect(names, keptShown: null, previouslySelected: "Classes", showAll: false));
    }

    [Fact]
    public void In_Advanced_mode_Venues_is_an_ordinary_page()
    {
        var names = new[] { "Venues", "Tracks" };

        Assert.Equal("Venues", MainForm.NavRowToSelect(names, keptShown: null, previouslySelected: "Classes", showAll: true));
    }

    [Fact]
    public void The_usual_order_still_holds()
    {
        var names = new[] { "Drivers", "Race Control", "Classes" };

        Assert.Equal("Classes", MainForm.NavRowToSelect(names, keptShown: "Classes", previouslySelected: "Drivers", showAll: false));
        Assert.Equal("Drivers", MainForm.NavRowToSelect(names, keptShown: null, previouslySelected: "Drivers", showAll: false));
        Assert.Equal("Race Control", MainForm.NavRowToSelect(names, keptShown: null, previouslySelected: "Venues", showAll: false));
    }
}
