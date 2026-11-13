namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, "last time out" on a meeting that runs past midnight.
///
/// WHY. Nights were told apart by the calendar date, so in a main started
/// after 00:00 the same evening's heats became "the previous night": a driver
/// was told they were quicker than last time out against a lap set two hours
/// earlier, and the once-a-night guard reset with the date. A night is now
/// the club's night, 6am to 6am, as it is everywhere else.
/// </summary>
public sealed class BugHunt17_DriverHistoryTests
{
    private const string Track = "Club Oval";
    private const string Dee = "T-DEE";

    private static SessionRecord Race(DateTime startedLocal, long bestMs) => new()
    {
        SessionType = RaceSessionType.Race,
        TrackLayoutName = Track,
        StartedAtUtc = DateTime.SpecifyKind(startedLocal, DateTimeKind.Local).ToUniversalTime(),
        Results = { new SessionResultEntry { Transponder = Dee, FastLapMs = bestMs, Laps = 20 } },
    };

    [Fact]
    public void After_midnight_it_is_still_the_night_the_meeting_started_on()
    {
        Assert.Equal(new DateOnly(2026, 11, 11), DriverHistory.NightOf(new DateTime(2026, 11, 11, 19, 0, 0)));
        Assert.Equal(new DateOnly(2026, 11, 11), DriverHistory.NightOf(new DateTime(2026, 11, 12, 0, 30, 0)));
        Assert.Equal(new DateOnly(2026, 11, 12), DriverHistory.NightOf(new DateTime(2026, 11, 12, 9, 0, 0)));
    }

    [Fact]
    public void Tonight_s_heats_before_midnight_are_not_last_time_out_in_a_main_after_it()
    {
        var history = new[]
        {
            Race(new DateTime(2026, 11, 4, 19, 0, 0), 7_100),   // last week
            Race(new DateTime(2026, 11, 11, 21, 30, 0), 6_900), // tonight, before midnight
            Race(new DateTime(2026, 11, 12, 0, 10, 0), 6_800),  // tonight, after it
        };

        var tonight = DriverHistory.NightOf(new DateTime(2026, 11, 12, 0, 30, 0));
        var nights = DriverHistory.NightsOn(history, Track, Dee, tonight);

        Assert.Single(nights);
        Assert.Equal(new DateOnly(2026, 11, 4), nights[0].Night);
        Assert.Equal(7_100, DriverHistory.LastTimeOut(nights));
    }

    [Fact]
    public void A_night_that_ran_past_midnight_is_one_night_when_it_is_looked_back_on()
    {
        var history = new[]
        {
            Race(new DateTime(2026, 11, 4, 22, 0, 0), 7_100),
            Race(new DateTime(2026, 11, 5, 0, 20, 0), 6_950),
        };

        var nights = DriverHistory.NightsOn(history, Track, Dee, new DateOnly(2026, 11, 11));

        Assert.Single(nights);
        Assert.Equal(6_950, nights[0].BestMs);
    }
}
