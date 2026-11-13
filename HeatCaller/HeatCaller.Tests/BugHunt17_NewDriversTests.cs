namespace HeatCaller.Tests;

/// <summary>
/// A first night runs past midnight (13 Nov 2026, bug hunt 17).
///
/// "First night" was judged by the calendar date, so a newcomer checked in
/// at 19:30 stopped being one at 00:00: the desk's badge went, and a first
/// race staged after midnight got no welcome. It is the 6am-to-6am night
/// now, the same one the once-a-night welcome key already used.
/// </summary>
public sealed class BugHunt17_NewDriversTests
{
    private static DriverProfile SeenAt(DateTime local) =>
        new() { Transponder = "1", FirstSeenUtc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime() };

    [Fact]
    public void A_newcomer_checked_in_in_the_evening_is_still_on_their_first_night_after_midnight()
    {
        var newcomer = SeenAt(new DateTime(2026, 11, 14, 19, 30, 0));
        var afterMidnight = new DateTime(2026, 11, 15, 0, 5, 0);

        Assert.True(NewDrivers.FirstNight(newcomer, afterMidnight));
        Assert.True(NewDrivers.IsNew(newcomer, afterMidnight));
        Assert.Equal(" · first night", NewDrivers.Badge(newcomer, afterMidnight));
    }

    [Fact]
    public void The_next_evening_is_not_their_first_night()
    {
        var newcomer = SeenAt(new DateTime(2026, 11, 14, 19, 30, 0));

        Assert.False(NewDrivers.FirstNight(newcomer, new DateTime(2026, 11, 15, 19, 0, 0)));
        Assert.Equal(string.Empty, NewDrivers.Badge(newcomer, new DateTime(2026, 11, 15, 19, 0, 0)));
    }

    [Fact]
    public void A_profile_made_after_midnight_belongs_to_the_night_before_not_the_evening_after()
    {
        var lateArrival = SeenAt(new DateTime(2026, 11, 15, 0, 30, 0));

        Assert.True(NewDrivers.FirstNight(lateArrival, new DateTime(2026, 11, 15, 1, 0, 0)));
        Assert.False(NewDrivers.FirstNight(lateArrival, new DateTime(2026, 11, 15, 19, 0, 0)));
    }
}
