namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), F053: Hold must stop the clock while a race
/// waits for the time drivers were given, as it does for the gap. The old
/// code let the wait run down under a hold, so the marks were said for a
/// held race and Resume after the promised time started it at once.
/// </summary>
public sealed class BugHunt24_AutoStartTests
{
    private static readonly DateTime Flag = new(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<string> Nobody = Array.Empty<string>();

    private static DateTime At(double seconds) => Flag.AddSeconds(seconds);

    private static AutoStartClock WaitingForItsTime(double notBefore = 480)
    {
        var clock = new AutoStartClock();
        clock.Open("7", "heat2", Flag, TimeSpan.FromSeconds(150));
        clock.NotBefore(At(notBefore));
        return clock;
    }

    [Fact]
    public void BugHunt24_A_hold_while_waiting_for_its_time_stops_the_clock()
    {
        var clock = WaitingForItsTime();

        Assert.Equal(TimeSpan.FromSeconds(80), clock.Remaining(At(400)));
        clock.Hold(At(400));

        // While held the start slides a second per second, so no mark comes due.
        Assert.Equal(TimeSpan.FromSeconds(80), clock.Remaining(At(450)));
        Assert.Equal(At(530), clock.StartsAtUtc(At(450)));
        Assert.Equal(At(580), clock.StartsAtUtc(At(500)));
        Assert.Equal(AutoStartState.Held, clock.Step(At(500), false, Nobody));

        // Race Control sets the promise again every second.
        clock.NotBefore(At(480));
        clock.Resume(At(600));

        Assert.Equal(TimeSpan.FromSeconds(80), clock.Remaining(At(600)));
        Assert.Equal(AutoStartState.WaitingForTime, clock.Step(At(600), false, Nobody));
        Assert.Equal(AutoStartState.WaitingForTime, clock.Step(At(679), false, Nobody));
        Assert.Equal(AutoStartState.Start, clock.Step(At(680), false, Nobody));
    }

    [Fact]
    public void BugHunt24_A_short_hold_while_waiting_keeps_the_time_that_was_left()
    {
        var clock = WaitingForItsTime();

        clock.Hold(At(400));
        clock.Resume(At(420));

        Assert.Equal(TimeSpan.FromSeconds(80), clock.Remaining(At(420)));
        Assert.True(clock.StartsAtUtc(At(420)) >= At(480));
    }

    [Fact]
    public void BugHunt24_A_hold_on_a_late_night_still_counts_the_gap_as_before()
    {
        // The promise is earlier than the gap: the gap is the longer wait.
        var clock = WaitingForItsTime(notBefore: 60);

        clock.Hold(At(10));
        clock.Resume(At(20));

        Assert.Equal(At(160), clock.StartsAtUtc(At(30)));
        Assert.Equal(AutoStartState.Counting, clock.Step(At(30), false, Nobody));
        Assert.Equal(AutoStartState.Start, clock.Step(At(160), false, Nobody));
    }

    [Fact]
    public void BugHunt24_Start_now_during_a_held_wait_still_starts_at_once()
    {
        var clock = WaitingForItsTime();

        clock.Hold(At(400));
        clock.StartNow();

        Assert.Equal(AutoStartState.Start, clock.Step(At(410), false, Nobody));
    }
}
