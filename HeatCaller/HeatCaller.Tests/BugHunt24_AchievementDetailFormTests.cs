namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the badge window (AchievementDetailForm).
///
/// F012: the words under a holder's date counted 24-hour lumps, so a badge
/// earned at 9pm read "today" the next morning, under yesterday's date. Now
/// counted in club nights (6am to 6am).
///
/// F011: the holder list moved only on the mouse wheel, and kept a stale
/// scroll after the window was made taller. Now the keys scroll it and a
/// resize pulls the scroll back into range.
/// </summary>
public sealed class BugHunt24_AchievementDetailFormTests
{
    private static readonly TimeZoneInfo Club =
        TimeZoneInfo.CreateCustomTimeZone("Club", TimeSpan.FromHours(-5), "Club", "Club");

    private static DateTime Utc(int day, int hour, int minute = 0) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, day, hour, minute, 0, DateTimeKind.Unspecified), Club);

    [Fact]
    public void HolderAgo_BadgeFromLastNight_ReadsYesterdayTheNextMorning()
    {
        // Earned 9pm Saturday, looked at 10am Sunday: 13 hours, which the old
        // code called "today".
        Assert.Equal("yesterday", AchievementDetailForm.HolderAgo(Utc(21, 21), Utc(22, 10), Club));
    }

    [Fact]
    public void HolderAgo_BadgeFromTwoNightsBack_ReadsTwoDaysAgo()
    {
        // 9pm Friday to 6:30am Sunday is 33.5 hours, which the old code
        // called "yesterday".
        Assert.Equal("2 days ago", AchievementDetailForm.HolderAgo(Utc(20, 21), Utc(22, 6, 30), Club));
    }

    [Fact]
    public void HolderAgo_SameRaceNightAfterMidnight_ReadsToday()
    {
        Assert.Equal("today", AchievementDetailForm.HolderAgo(Utc(21, 21), Utc(22, 1), Club));
    }

    [Fact]
    public void HolderAgo_EarnedInTheFuture_ReadsToday()
    {
        Assert.Equal("today", AchievementDetailForm.HolderAgo(Utc(22, 10), Utc(21, 21), Club));
    }

    [Fact]
    public void ClampHolderScroll_PullsBackWhenTheListGetsTaller()
    {
        // 30 rows of 46: 1380 tall. Scrolled to the end of a 300-tall list...
        var atEnd = AchievementDetailForm.ClampHolderScroll(int.MaxValue / 2, 30, 46, 0, 300);
        Assert.Equal(1080, atEnd);

        // ...then made 500 tall: the last row now meets the bottom, no gap.
        Assert.Equal(880, AchievementDetailForm.ClampHolderScroll(atEnd, 30, 46, 0, 500));

        // Fewer rows than fit: no scroll at all.
        Assert.Equal(0, AchievementDetailForm.ClampHolderScroll(200, 3, 46, 0, 500));
    }

    [Fact]
    public void HolderScrollForKey_KeysReachTheHoldersBelowTheFirstScreenful()
    {
        Assert.Equal(46, AchievementDetailForm.HolderScrollForKey(Keys.Down, 0, 30, 46, 0, 300));
        Assert.Equal(254, AchievementDetailForm.HolderScrollForKey(Keys.PageDown, 0, 30, 46, 0, 300));
        Assert.Equal(1080, AchievementDetailForm.HolderScrollForKey(Keys.End, 0, 30, 46, 0, 300));
        Assert.Equal(0, AchievementDetailForm.HolderScrollForKey(Keys.Home, 1080, 30, 46, 0, 300));
        Assert.Equal(1034, AchievementDetailForm.HolderScrollForKey(Keys.Up, 1080, 30, 46, 0, 300));
        Assert.Equal(826, AchievementDetailForm.HolderScrollForKey(Keys.PageUp, 1080, 30, 46, 0, 300));

        // Never past either end.
        Assert.Equal(0, AchievementDetailForm.HolderScrollForKey(Keys.Up, 0, 30, 46, 0, 300));
        Assert.Equal(1080, AchievementDetailForm.HolderScrollForKey(Keys.Down, 1080, 30, 46, 0, 300));

        // Other keys are left alone.
        Assert.Null(AchievementDetailForm.HolderScrollForKey(Keys.A, 0, 30, 46, 0, 300));
    }
}
