namespace HeatCaller.Tests;

/// <summary>
/// A first night runs past midnight (13 Nov 2026, bug hunt 17).
///
/// "First night" was judged by the calendar date, so a newcomer checked in
/// at 19:30 stopped being one at 00:00: the desk's badge went, and a first
/// race staged after midnight got no welcome. It is the 6am-to-6am night
/// now, the same one the once-a-night welcome key already used.
///
/// Since bug hunt 21 a first night is judged by the filed results, not by
/// when the profile was made (see NewDrivers.FirstNight); the night still
/// runs 6am to 6am.
/// </summary>
public sealed class BugHunt17_NewDriversTests
{
    private static readonly DriverProfile Newcomer = new() { Transponder = "1" };

    private static SessionRecord Raced(string transponder, DateTime local) => new()
    {
        SessionType = RaceSessionType.Race,
        StartedAtUtc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime(),
        Results = new List<SessionResultEntry> { new() { Position = 1, Transponder = transponder, Laps = 10 } },
    };

    /// <summary>A club with four earlier nights on file - enough to say who is new - and the newcomer's one race.</summary>
    private static List<SessionRecord> History(DateTime newcomerRaced)
    {
        var history = Enumerable.Range(1, 4).Select(week => Raced("99", new DateTime(2026, 11, 14, 19, 0, 0).AddDays(-7 * week))).ToList();
        history.Add(Raced("1", newcomerRaced));
        return history;
    }

    private static NewDrivers.RacedBefore At(List<SessionRecord> history, DateTime nowLocal) => NewDrivers.RacedBefore.From(history, nowLocal);

    [Fact]
    public void A_newcomer_checked_in_in_the_evening_is_still_on_their_first_night_after_midnight()
    {
        var history = History(new DateTime(2026, 11, 14, 19, 30, 0));
        var afterMidnight = At(history, new DateTime(2026, 11, 15, 0, 5, 0));

        Assert.True(NewDrivers.FirstNight(Newcomer, afterMidnight));
        Assert.True(NewDrivers.IsNew(Newcomer, afterMidnight));
        Assert.Equal(" · first night", NewDrivers.Badge(Newcomer, afterMidnight));
    }

    [Fact]
    public void The_next_evening_is_not_their_first_night()
    {
        var nextEvening = At(History(new DateTime(2026, 11, 14, 19, 30, 0)), new DateTime(2026, 11, 15, 19, 0, 0));

        Assert.False(NewDrivers.FirstNight(Newcomer, nextEvening));
        Assert.Equal(string.Empty, NewDrivers.Badge(Newcomer, nextEvening));
    }

    [Fact]
    public void A_race_after_midnight_belongs_to_the_night_before_not_the_evening_after()
    {
        var history = History(new DateTime(2026, 11, 15, 0, 30, 0));

        Assert.True(NewDrivers.FirstNight(Newcomer, At(history, new DateTime(2026, 11, 15, 1, 0, 0))));
        Assert.False(NewDrivers.FirstNight(Newcomer, At(history, new DateTime(2026, 11, 15, 19, 0, 0))));
    }
}
