namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16: renaming the track layout selected in Race Control's Track
/// box rebuilt the box, which kept its selection by the OLD name - no longer
/// in the list - and so fell to "(none)". The race was then filed against
/// no track. The box now follows the selected layout by its Id.
/// </summary>
public sealed class BugHunt16_TrackRenameKeepsSelectionTests
{
    private static TrackLayout Layout(string id, string name) => new() { Id = id, Name = name };

    [Fact]
    public void A_renamed_selected_layout_is_kept_under_its_new_name()
    {
        // The box showed "(none)", "Club Oval", "Indoor"; "Club Oval" was picked.
        var shownIds = new List<string> { "a", "b" };
        var nowStored = new[] { Layout("a", "Club Oval 2026"), Layout("b", "Indoor") };

        var keep = MainForm.TrackNameToKeep("Club Oval", 1, shownIds, nowStored);

        Assert.Equal("Club Oval 2026", keep);
    }

    [Fact]
    public void A_selection_survives_a_new_layout_sorting_in_ahead_of_it()
    {
        var shownIds = new List<string> { "b" };
        var nowStored = new[] { Layout("a", "Arena"), Layout("b", "Indoor") };

        Assert.Equal("Indoor", MainForm.TrackNameToKeep("Indoor", 1, shownIds, nowStored));
    }

    [Fact]
    public void A_deleted_layout_and_none_fall_back_to_the_shown_name()
    {
        var shownIds = new List<string> { "a" };
        var nowStored = Array.Empty<TrackLayout>();

        Assert.Equal("Club Oval", MainForm.TrackNameToKeep("Club Oval", 1, shownIds, nowStored));
        Assert.Equal("(none)", MainForm.TrackNameToKeep("(none)", 0, shownIds, nowStored));
        Assert.Null(MainForm.TrackNameToKeep(null, -1, shownIds, nowStored));
    }

    [Fact]
    public void Before_the_first_fill_the_shown_name_is_used()
    {
        // No ids recorded yet (the box was never filled by the refresh).
        var nowStored = new[] { Layout("a", "Club Oval") };

        Assert.Equal("Club Oval", MainForm.TrackNameToKeep("Club Oval", 1, new List<string>(), nowStored));
    }
}
