namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16: a staggered start with no round borrows the last race's
/// finishing order as its grid. It used to take every driver on it - from a
/// race that could be last week's - so a car that was here waited through
/// the gap of every absent driver ahead of it, and the PA called "X, go!"
/// for drivers who were not at the track. Now only tonight's race, and only
/// the cars on it that are here.
/// </summary>
public sealed class BugHunt16_BorrowedGridTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 20, 0, 0, DateTimeKind.Utc);

    private static readonly string[] LastOrder = { "A", "B", "C", "D", "E", "F", "G", "H" };

    private static readonly HashSet<string> Tonight = new(StringComparer.OrdinalIgnoreCase) { "A", "D", "H", "N" };

    [Fact]
    public void Drivers_not_checked_in_or_heard_get_no_grid_slot()
    {
        var grid = RaceControlControl.BorrowedGridOfCarsHere(
            LastOrder, Now.AddMinutes(-20), Now,
            checkedIn: t => t is "A" or "D",
            heardLately: t => t is "H");

        Assert.Equal(new[] { "A", "D", "H" }, grid);
    }

    [Fact]
    public void With_only_the_decoder_the_grid_is_the_cars_it_has_heard()
    {
        var grid = RaceControlControl.BorrowedGridOfCarsHere(
            LastOrder, Now.AddMinutes(-20), Now,
            checkedIn: null,
            heardLately: Tonight.Contains);

        Assert.Equal(new[] { "A", "D", "H" }, grid);
    }

    [Fact]
    public void A_quiet_decoder_with_no_desk_keeps_the_whole_order()
    {
        var grid = RaceControlControl.BorrowedGridOfCarsHere(
            LastOrder, Now.AddMinutes(-20), Now,
            checkedIn: null,
            heardLately: _ => false);

        Assert.Equal(LastOrder, grid);
    }

    [Fact]
    public void A_race_from_last_week_is_no_grid_for_tonight()
    {
        var grid = RaceControlControl.BorrowedGridOfCarsHere(
            LastOrder, Now.AddDays(-7), Now,
            checkedIn: null, heardLately: null);

        Assert.Empty(grid);
    }

    [Fact]
    public void A_night_running_past_midnight_still_has_its_last_race()
    {
        var justBeforeMidnight = new DateTime(2026, 11, 10, 23, 50, 0, DateTimeKind.Utc);
        var grid = RaceControlControl.BorrowedGridOfCarsHere(
            LastOrder, justBeforeMidnight, justBeforeMidnight.AddMinutes(25),
            checkedIn: null, heardLately: null);

        Assert.Equal(LastOrder, grid);
    }

    [Fact]
    public void The_stagger_built_on_the_cut_grid_waits_for_nobody_who_is_absent()
    {
        var race = new RaceControlSession { StartType = RaceStartType.StaggeredGrid };
        var grid = RaceControlControl.BorrowedGridOfCarsHere(
            LastOrder, Now.AddMinutes(-20), Now,
            checkedIn: Tonight.Contains, heardLately: null);

        // As BuildStartSchedule numbers it: place * gap over the grid.
        const long gapMs = 5_000;
        var schedule = grid.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i * gapMs);
        race.SetReleaseSchedule(schedule);

        Assert.Equal(new[] { "A", "D", "H" }, race.ReleaseRoster());
        Assert.Equal(5_000, race.ReleaseOffsetMsFor("D"));
        Assert.Equal(10_000, race.ReleaseOffsetMsFor("H"));
        Assert.Equal(0, race.ReleaseOffsetMsFor("B"));
    }
}
