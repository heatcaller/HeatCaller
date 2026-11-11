namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16 (10 Nov 2026): Delete on the Drivers page read only the
/// roster's highlighted rows, while the Table view, the drawer and the "..."
/// menu show the driver on the card. It deleted rows left highlighted in the
/// Card view, or did nothing when a search left no row highlighted.
/// </summary>
public sealed class BugHunt16_ProfileDeleteTargetsTests
{
    private static readonly DriverProfile Sam = new() { Transponder = "T1", DriverName = "Sam" };
    private static readonly DriverProfile Alex = new() { Transponder = "T2", DriverName = "Alex" };
    private static readonly DriverProfile Bo = new() { Transponder = "T3", DriverName = "Bo" };

    [Fact]
    public void The_table_deletes_only_the_driver_it_shows_even_with_rows_left_highlighted()
    {
        var targets = ProfileManagerControl.DeleteTargets(cardView: false, new[] { Sam, Alex, Bo }, Bo);

        Assert.Equal(new[] { Bo }, targets);
    }

    [Fact]
    public void A_search_that_hides_everyone_still_deletes_the_driver_on_the_card()
    {
        var targets = ProfileManagerControl.DeleteTargets(cardView: true, Array.Empty<DriverProfile>(), Sam);

        Assert.Equal(new[] { Sam }, targets);
    }

    [Fact]
    public void Several_rows_highlighted_in_the_card_view_are_all_deleted()
    {
        var targets = ProfileManagerControl.DeleteTargets(cardView: true, new[] { Sam, Alex, Bo }, Bo);

        Assert.Equal(new[] { Sam, Alex, Bo }, targets);
    }

    [Fact]
    public void Nothing_shown_means_nothing_to_delete()
    {
        Assert.Empty(ProfileManagerControl.DeleteTargets(cardView: false, Array.Empty<DriverProfile>(), null));
    }
}
