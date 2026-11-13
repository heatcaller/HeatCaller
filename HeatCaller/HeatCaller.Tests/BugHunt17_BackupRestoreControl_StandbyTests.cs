namespace HeatCaller.Tests;

/// <summary>
/// 13 Nov 2026, Bug Hunt 17.
///
/// Take Over asked "The newest copy is 12 min old (0 files)" whenever the
/// standby's HeatCaller had been restarted since its last copy - which is the
/// night the timing PC dies and the standby is opened to take over. The copy
/// on disk was whole; the count was simply not known yet. Take Over now
/// counts the copy itself, and a count that still isn't known is left out.
/// </summary>
public sealed class BugHunt17_BackupRestoreControl_StandbyTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_count_that_is_not_known_is_left_out_rather_than_said_as_no_files()
    {
        var question = WarmStandby.TakeOverQuestion(Now.AddMinutes(-12), Now, null, 0);

        Assert.Contains("The newest copy is 12 min old. Anything the timing PC saved after that", question, StringComparison.Ordinal);
        Assert.DoesNotContain("0 files", question, StringComparison.Ordinal);
    }

    [Fact]
    public void A_known_count_is_still_said()
    {
        var question = WarmStandby.TakeOverQuestion(Now.AddMinutes(-12), Now, "TIMING-PC", 18);

        Assert.Contains("12 min old (18 files). Anything TIMING-PC saved", question, StringComparison.Ordinal);
    }
}
