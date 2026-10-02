namespace HeatCaller.Tests;

/// <summary>
/// "Race days attended" counts only race days that still exist. A deleted
/// day's check-in sheet is kept so an undo can bring it back, but it must not
/// count towards a driver's attendance while the day is gone.
/// </summary>
public sealed class BugHuntCheckInTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_deleted_days_sheet_is_not_counted_when_the_caller_says_it_is_gone()
    {
        var checkIns = CheckInStore.Load();

        checkIns.CurrentEventId = "day-1";
        checkIns.SetCheckedIn("T1", true);

        checkIns.CurrentEventId = "day-2";
        checkIns.SetCheckedIn("T1", true);

        // day-2 has been deleted; only day-1 still exists.
        var live = new HashSet<string> { "day-1" };

        Assert.Equal(1, checkIns.EventsAttended("T1", live.Contains));

        // The sheet itself is still there for an undo to bring back.
        Assert.Contains("T1", checkIns.CheckedInAt("day-2"));
    }

    [Fact]
    public void Without_an_event_check_every_sheet_still_counts()
    {
        var checkIns = CheckInStore.Load();

        checkIns.CurrentEventId = "day-1";
        checkIns.SetCheckedIn("T1", true);

        checkIns.CurrentEventId = "day-2";
        checkIns.SetCheckedIn("T1", true);

        Assert.Equal(2, checkIns.EventsAttended("T1"));
    }
}
