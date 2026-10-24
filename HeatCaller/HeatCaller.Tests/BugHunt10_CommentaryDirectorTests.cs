namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): the nights left in the month were counted on
/// the laptop's own date, once, when the race day opened - but the relay
/// keeps the club's month in UTC. A New York club on the 31st was paced
/// all evening as if it were the month's last night, after the relay had
/// already started the next month. Now counted on the relay's day, and
/// again when that month turns over mid-night.
/// </summary>
public sealed class BugHunt10_CommentaryDirectorTests
{
    [Fact]
    public void A_local_clock_is_counted_on_the_relays_utc_day()
    {
        var local = new DateTime(2026, 10, 31, 20, 30, 0, DateTimeKind.Local);

        Assert.Equal(local.ToUniversalTime(), CommentaryDirector.RelayDay(local));
        Assert.Equal(DateTimeKind.Utc, CommentaryDirector.RelayDay(local).Kind);
    }

    [Fact]
    public void A_clock_with_no_zone_is_taken_as_the_relays_day_already()
    {
        var fixedTime = new DateTime(2026, 10, 31, 23, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(fixedTime, CommentaryDirector.RelayDay(fixedTime));
    }

    [Fact]
    public void The_relays_month_turning_over_mid_night_counts_the_new_months_nights()
    {
        using var data = new TempAppData();
        var now = new DateTime(2026, 10, 31, 22, 30, 0, DateTimeKind.Utc);
        var settings = new AppSettings { CommentaryEnabled = true, CommentaryNightsPerMonth = 4 };
        var commentary = new CommentaryService(ProfileStore.Load(), settings);
        var director = new CommentaryDirector(commentary, settings, () => now);

        director.NewMeeting("day-1");
        director.MonthlyUsage(700, 1000);

        // The last day of October: this is the month's last night.
        Assert.Equal(1, director.Budget.NightsLeftThisMonth);

        // UTC midnight - 8pm in New York - and the relay's November begins.
        now = new DateTime(2026, 11, 1, 0, 10, 0, DateTimeKind.Utc);
        director.MonthlyUsage(5, 1000);

        Assert.Equal(4, director.Budget.NightsLeftThisMonth);
    }

    [Fact]
    public void The_same_month_does_not_recount_the_nights()
    {
        using var data = new TempAppData();
        var now = new DateTime(2026, 10, 22, 19, 0, 0, DateTimeKind.Utc);
        var settings = new AppSettings { CommentaryEnabled = true, CommentaryNightsPerMonth = 4 };
        var commentary = new CommentaryService(ProfileStore.Load(), settings);
        var director = new CommentaryDirector(commentary, settings, () => now);

        director.NewMeeting("day-1");
        var tonight = director.Budget.NightsLeftThisMonth;

        // Later in the same month the count stays where NewMeeting put it.
        now = new DateTime(2026, 10, 29, 19, 0, 0, DateTimeKind.Utc);
        director.MonthlyUsage(20, 400);

        Assert.Equal(tonight, director.Budget.NightsLeftThisMonth);
    }
}
