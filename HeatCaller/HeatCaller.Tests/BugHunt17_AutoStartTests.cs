namespace HeatCaller.Tests;

/// <summary>
/// 13 Nov 2026, Bug Hunt 17.
///
/// A race waiting for the time drivers were given (the night is running
/// early) showed "NEXT RACE AT 20:40" on the Big Screen, but the phones were
/// told there was no countdown at all for the whole wait: the phone's
/// countdown had no answer for that state.
/// </summary>
public sealed class BugHunt17_AutoStartTests
{
    [Fact]
    public void A_race_waiting_for_its_time_counts_down_on_the_phones_to_that_time()
    {
        var at = new DateTime(2026, 11, 13, 20, 40, 0, 250, DateTimeKind.Utc);

        var waiting = PhoneCountdown.For(AutoStartState.WaitingForTime, at);

        Assert.NotNull(waiting);
        Assert.Equal("counting", waiting!.State);
        Assert.Equal(new DateTime(2026, 11, 13, 20, 40, 0, DateTimeKind.Utc), waiting.AtUtc);
        Assert.Equal(PhoneCountdown.For(AutoStartState.Counting, at), waiting);
    }

    [Fact]
    public void With_no_start_time_there_is_still_nothing_to_count_to()
    {
        Assert.Null(PhoneCountdown.For(AutoStartState.WaitingForTime, null));
        Assert.Null(PhoneCountdown.For(AutoStartState.Off, DateTime.UtcNow));
    }
}
