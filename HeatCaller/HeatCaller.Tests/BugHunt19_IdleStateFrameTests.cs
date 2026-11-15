namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19 (14 Nov 2026) #12, the docked live video between races.
///
/// The relay shows the club's stream as live only while the race state is
/// under two minutes old, and the laptop sent no state at all while idle, so
/// every race break of over two minutes told the phones "The stream has
/// stopped" and tore their player down. An idle board now goes once a minute.
/// </summary>
public sealed class BugHunt19_IdleStateFrameTests
{
    [Fact]
    public void An_idle_board_still_goes_once_a_minute()
    {
        const long sent = 1_000_000;

        Assert.False(MainForm.StateFrameDue(live: false, changed: false, sent + 30_000, sent));
        Assert.True(MainForm.StateFrameDue(live: false, changed: false, sent + MainForm.IdleStateEveryMs, sent));
        Assert.True(MainForm.IdleStateEveryMs < 120_000, "well inside the relay's two minutes");
    }

    [Fact]
    public void A_race_on_or_a_change_still_goes_every_tick()
    {
        const long sent = 1_000_000;

        Assert.True(MainForm.StateFrameDue(live: true, changed: false, sent + 1000, sent));
        Assert.True(MainForm.StateFrameDue(live: false, changed: true, sent + 1000, sent));
    }
}
