namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11b (6 Nov 2026): the lines the model on this PC wrote while
/// the relay's month was spent were kept out of tonight's relay charge in
/// memory only. After a same-day restart they came back as relay lines,
/// so once the relay had lines again (a top-up, or its UTC month turning
/// over) the night's pacing was wrong. They are now saved with the night.
/// </summary>
public sealed class BugHunt11b_CommentaryBudgetTests
{
    private const int FreeLines = 3;

    private DateTime _clock = new(2026, 11, 6, 19, 0, 0, DateTimeKind.Unspecified);

    private CommentaryBudget Night(string? saved, bool local)
    {
        var night = new CommentaryBudget(() => _clock);

        night.Restore(saved, "day-1");
        night.MonthlyUsage(400, 400);
        night.NightsLeftThisMonth = 2;
        night.NightShape(30, 30);
        night.LocalModelTakesOver = () => local;

        return night;
    }

    /// <summary>A night that wrote <see cref="FreeLines"/> lines on this PC while the relay's month was spent.</summary>
    private CommentaryBudget SpentFree()
    {
        var budget = Night(null, local: true);

        for (var i = 0; i < FreeLines; i++)
        {
            var verdict = budget.Reserve(SegmentKind.BattleWatch, "Stock");
            Assert.True(verdict.Allowed);
            budget.Settle(verdict.Ticket);
            _clock = _clock.AddMinutes(5);
        }

        Assert.Equal(FreeLines, budget.SpentTonight);
        return budget;
    }

    [Fact]
    public void Free_local_lines_are_still_free_after_a_same_day_restart()
    {
        var before = SpentFree();
        var saved = before.Save();

        // The app restarts on the same race day, and a top-up of 50 lands.
        var after = new CommentaryBudget(() => _clock);
        after.Restore(saved, "day-1");
        after.MonthlyUsage(400, 450);
        after.NightsLeftThisMonth = 2;
        after.NightShape(30, 30);

        before.LocalModelTakesOver = () => false;
        before.MonthlyUsage(400, 450);

        var untouched = Night(null, local: false);
        untouched.MonthlyUsage(400, 450);

        Assert.Equal(FreeLines, after.SpentTonight);

        // The local lines cost no relay lines, before the restart or after it.
        Assert.Equal(untouched.Headroom, before.Headroom, 3);
        Assert.Equal(before.Headroom, after.Headroom, 3);
        Assert.Equal(before.NightAllotment, after.NightAllotment, 3);
    }

    [Fact]
    public void A_night_saved_before_free_lines_were_kept_still_loads_with_none_free()
    {
        const string oldFile = """{"DayId":"day-1","Spent":3,"HighWater":0,"PerMeeting":{"BattleWatch":3},"PerClass":{}}""";
        const string withNoneFree = """{"DayId":"day-1","Spent":3,"HighWater":0,"PerMeeting":{"BattleWatch":3},"PerClass":{},"Free":0}""";

        var old = new CommentaryBudget(() => _clock);
        old.Restore(oldFile, "day-1");
        old.MonthlyUsage(400, 450);
        old.NightsLeftThisMonth = 2;
        old.NightShape(30, 30);

        var explicitNone = new CommentaryBudget(() => _clock);
        explicitNone.Restore(withNoneFree, "day-1");
        explicitNone.MonthlyUsage(400, 450);
        explicitNone.NightsLeftThisMonth = 2;
        explicitNone.NightShape(30, 30);

        Assert.Equal(3, old.SpentTonight);
        Assert.Equal(explicitNone.Headroom, old.Headroom, 3);
        Assert.Equal(explicitNone.NightAllotment, old.NightAllotment, 3);

        // All three charged as relay lines, as before this fix.
        var fresh = new CommentaryBudget(() => _clock);
        fresh.Restore(null, "day-1");
        fresh.MonthlyUsage(400, 450);
        fresh.NightsLeftThisMonth = 2;
        fresh.NightShape(30, 30);

        Assert.True(old.Headroom < fresh.Headroom);
    }
}
