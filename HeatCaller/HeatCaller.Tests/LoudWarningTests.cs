namespace HeatCaller.Tests;

/// <summary>
/// The decoder-off warning, after rehearsal 4: said before the green as well
/// as during the race, and shown on Race Control as a pulsing alarm rather
/// than a line of red text.
/// </summary>
public sealed class LoudWarningTests
{
    [Theory]
    [InlineData(RaceState.Staging)]
    [InlineData(RaceState.Countdown)]
    [InlineData(RaceState.Racing)]
    public void An_unplugged_decoder_is_said_once_a_session_is_on_its_way(RaceState state)
    {
        var problem = TimingWatch.DecoderOffBeforeTheGreen(state, demo: false, decoderConnected: false);

        Assert.Equal("decoder-off", problem?.Key);
    }

    [Fact]
    public void Before_the_green_it_says_to_connect_before_the_green()
    {
        var problem = TimingWatch.DecoderOffBeforeTheGreen(RaceState.Staging, demo: false, decoderConnected: false);

        Assert.Contains("before the green", problem!.Text);
    }

    [Theory]
    [InlineData(RaceState.Idle)]
    [InlineData(RaceState.Finished)]
    public void Setting_up_with_the_decoder_in_its_box_is_not_a_problem(RaceState state) =>
        Assert.Null(TimingWatch.DecoderOffBeforeTheGreen(state, demo: false, decoderConnected: false));

    [Fact]
    public void A_demo_needs_no_decoder() =>
        Assert.Null(TimingWatch.DecoderOffBeforeTheGreen(RaceState.Staging, demo: true, decoderConnected: false));

    [Fact]
    public void A_connected_decoder_is_never_a_problem() =>
        Assert.Null(TimingWatch.DecoderOffBeforeTheGreen(RaceState.Countdown, demo: false, decoderConnected: true));

    [Fact]
    public void The_alarm_swells_smoothly_between_its_deepest_and_brightest()
    {
        Assert.Equal(0, NextUpBar.PulseAt(0), 3);
        Assert.Equal(1, NextUpBar.PulseAt(550), 3);
        Assert.Equal(0, NextUpBar.PulseAt(1100), 3);

        for (var ms = 0L; ms < 5000; ms += 7)
        {
            var pulse = NextUpBar.PulseAt(ms);
            Assert.InRange(pulse, 0, 1);

            // A swell, not a flash: never more than a small step between
            // frames 40ms apart.
            Assert.True(Math.Abs(NextUpBar.PulseAt(ms + 40) - pulse) < 0.15, $"jumped at {ms}ms");
        }
    }

    [Fact]
    public void The_bar_is_an_alarm_only_while_there_is_a_timing_problem()
    {
        var alarming = StaThread.Run(() =>
        {
            using var bar = new NextUpBar();
            var before = bar.Alarming;

            bar.ShowInfo(new NextUpBar.Info("Q1", "Q2", "Day", false, "The decoder isn't connected"));
            var during = bar.Alarming;

            bar.ShowInfo(new NextUpBar.Info("Q1", "Q2", "Day", false));
            var after = bar.Alarming;

            return (before, during, after);
        }, TimeSpan.FromSeconds(30), () => "showing the bar");

        Assert.Equal((false, true, false), alarming);
    }
}

/// <summary>Click-and-drag selection: which rows a sweep covers (DragSelect).</summary>
public sealed class DragSelectTests
{
    [Fact]
    public void A_sweep_down_covers_the_anchor_to_the_pointer() =>
        Assert.Equal(new[] { 2, 3, 4 }, Enumerable.Range(0, 8).Where(i => DragSelect.Covers(i, 2, 4, null)));

    [Fact]
    public void A_sweep_up_covers_the_same_way() =>
        Assert.Equal(new[] { 2, 3, 4 }, Enumerable.Range(0, 8).Where(i => DragSelect.Covers(i, 4, 2, null)));

    [Fact]
    public void A_ctrl_sweep_keeps_what_was_already_selected() =>
        Assert.Equal(new[] { 0, 3, 4, 7 },
            Enumerable.Range(0, 8).Where(i => DragSelect.Covers(i, 3, 4, new HashSet<int> { 0, 7 })));
}
