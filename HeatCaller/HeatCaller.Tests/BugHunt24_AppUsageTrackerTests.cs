namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), usage totals: a try that could not send (the
/// relay didn't answer, or it isn't ready yet) must be tried again within
/// minutes, not after the full 6 hours - a laptop that joins the track
/// hotspot a minute after starting used to send nothing that night.
/// </summary>
public sealed class BugHunt24_AppUsageTrackerTests
{
    [Fact]
    public void A_try_that_went_waits_the_full_six_hours()
    {
        Assert.Equal(TimeSpan.FromHours(6), AppUsageTracker.NextSendWait(AppUsageSender.Outcome.Sent, 0));
        Assert.Equal(TimeSpan.FromHours(6), AppUsageTracker.NextSendWait(AppUsageSender.Outcome.NothingWaiting, 0));
    }

    [Fact]
    public void A_try_the_relay_did_not_answer_is_tried_again_in_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), AppUsageTracker.NextSendWait(AppUsageSender.Outcome.TryLater, 1));
    }

    [Fact]
    public void A_try_that_was_not_allowed_yet_is_tried_again_in_five_minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), AppUsageTracker.NextSendWait(null, 1));
    }

    [Fact]
    public void Failures_in_a_row_wait_longer_but_never_more_than_an_hour()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), AppUsageTracker.NextSendWait(AppUsageSender.Outcome.TryLater, 2));
        Assert.Equal(TimeSpan.FromMinutes(20), AppUsageTracker.NextSendWait(AppUsageSender.Outcome.TryLater, 3));
        Assert.Equal(TimeSpan.FromMinutes(40), AppUsageTracker.NextSendWait(AppUsageSender.Outcome.TryLater, 4));
        Assert.Equal(TimeSpan.FromHours(1), AppUsageTracker.NextSendWait(AppUsageSender.Outcome.TryLater, 5));
        Assert.Equal(TimeSpan.FromHours(1), AppUsageTracker.NextSendWait(null, 50));
    }
}
