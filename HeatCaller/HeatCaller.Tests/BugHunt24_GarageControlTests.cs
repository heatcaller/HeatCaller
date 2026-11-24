namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the garage on a driver's page: showing nobody
/// does not tell the director to click a greyed-out Add a Car.
/// </summary>
public sealed class BugHunt24_GarageControlTests
{
    // ------------------------------------------------------------ #178 no driver picked

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 1)]
    public void Drivers_page_showing_nobody_asks_for_a_driver_not_add_a_car(int allCars, int clubCars)
    {
        var words = GarageControl.EmptyWallWords(forOneDriver: true, shownKey: null, allCars, clubCars, includeRetired: false);

        Assert.Equal("Pick a driver to see their cars.", words);
        Assert.DoesNotContain("Add a Car", words);
    }

    [Fact]
    public void Drivers_page_for_a_driver_with_no_cars_still_says_add_a_car()
    {
        Assert.Equal("No cars yet. Click Add a Car, and it appears here.",
            GarageControl.EmptyWallWords(true, "T1", 2, 0, false));
    }

    [Fact]
    public void Drivers_page_showing_everyone_or_the_club_keeps_its_words()
    {
        Assert.Equal("No cars yet. Click Add a Car, and it appears here.",
            GarageControl.EmptyWallWords(true, GarageControl.EveryoneKey, 0, 0, false));
        Assert.Equal("Every car in the garage is retired.",
            GarageControl.EmptyWallWords(true, GarageControl.EveryoneKey, 4, 0, false));
        Assert.Equal("The club has no cars of its own yet. Click Add a Car, and it appears here.",
            GarageControl.EmptyWallWords(true, GarageControl.ClubKey, 4, 0, false));
        Assert.Equal("The club's own cars are all retired.",
            GarageControl.EmptyWallWords(true, GarageControl.ClubKey, 4, 2, false));
    }

    [Fact]
    public void Garage_page_words_are_unchanged()
    {
        Assert.Equal("No cars yet. Add one, and it appears here under its class.",
            GarageControl.EmptyWallWords(false, null, 0, 0, false));
        Assert.Equal("No cars to show - pick another in Show, or tick Include retired.",
            GarageControl.EmptyWallWords(false, null, 3, 0, false));
        Assert.Equal("No cars to show - pick another in Show.",
            GarageControl.EmptyWallWords(false, null, 3, 0, true));
    }
}
