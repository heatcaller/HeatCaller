namespace HeatCaller.Tests;

/// <summary>
/// The "i" circles at the foot of Race Control's board (25 Nov 2026, asked
/// for by Asher: "for all the tool tips here can we put them behind a small
/// circle with an i in it?").
///
/// The old behaviour: the pills, the format line, its pen and Focus each
/// popped a paragraph up whenever the mouse crossed them on the way to a
/// press, and nothing showed the help was there. Now each part's words are
/// in a small circle beside it, and the parts themselves are quiet.
/// </summary>
public sealed class InfoDotTests
{
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.True(dir is not null, "Couldn't find the repo root");
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }

    [Fact]
    public void A_circle_carries_its_words_for_the_mouse_and_for_a_screen_reader()
    {
        StaThread.Run(() =>
        {
            using var dot = new InfoDot { TipText = "Lit: every race starts with the warm-up flag." };

            Assert.Equal("Lit: every race starts with the warm-up flag.", dot.Tip.GetToolTip(dot));
            Assert.Equal("Lit: every race starts with the warm-up flag.", dot.AccessibleDescription);
            Assert.True(AppTheme.IsKept(dot.Tip), "the circle's tip is one the app keeps showing");
            Assert.False(dot.TabStop);
            return 0;
        }, TimeSpan.FromSeconds(30), () => "a circle");
    }

    [Fact]
    public void Each_circle_sits_just_after_its_part_Focus_s_at_the_right_hand_edge()
    {
        var result = StaThread.Run(() =>
        {
            using var host = new Form { Width = 1400 };
            var a = new Panel { Size = new Size(100, 40) };
            var b = new Panel { Size = new Size(110, 40) };
            var c = new Panel { Size = new Size(130, 40) };
            var label = new Label { Text = "12 laps · all away together" };
            var pen = new Panel { Size = new Size(34, 34) };
            var focus = new Panel { Size = new Size(160, 44) };
            var dots = new[] { new InfoDot(), new InfoDot(), new InfoDot() };
            var penDot = new InfoDot();
            var focusDot = new InfoDot();

            var row = new BoardFootRow(new Control[] { a, b, c }, label, pen, focus, dots, penDot, focusDot) { Width = 1200 };
            host.Controls.Add(row);
            _ = host.Handle;
            row.PerformLayout();

            var gap = UiLayout.AtDpi(InfoDot.GapAt100);
            var parts = new Control[] { a, b, c };

            for (var i = 0; i < 3; i++)
            {
                Assert.Equal(parts[i].Right + gap, dots[i].Left);
                Assert.True(i == 2 || dots[i].Right < parts[i + 1].Left, $"circle {i} runs into the next pill");
            }

            Assert.Equal(pen.Right + gap, penDot.Left);
            Assert.True(penDot.Right < focus.Left, "the format's circle runs into Focus");
            Assert.Equal(focus.Right + gap, focusDot.Left);
            Assert.Equal(row.ClientSize.Width, focusDot.Right);
            Assert.True(label.Right <= pen.Left, "the format line runs under its pen");
            return 0;
        }, TimeSpan.FromSeconds(30), () => "the row");

        Assert.Equal(0, result);
    }

    [Fact]
    public void The_foot_of_the_board_has_a_circle_for_each_part_and_the_parts_pop_nothing_up()
    {
        var layout = Source("RaceControlControl.Layout.cs");
        Assert.Contains("leftDots: new InfoDot?[] { _infoOnAir, _infoWarmUp, _infoAutoStart }, penDot: _infoFormat, rightDot: _btnFocus.Info", layout, StringComparison.Ordinal);

        var onAir = Source("RaceControlControl.OnAir.cs");
        var autoStart = Source("RaceControlControl.AutoStart.cs");
        var nextUp = Source("RaceControlControl.NextUp.cs");

        Assert.Contains("_infoOnAir.TipText =", onAir, StringComparison.Ordinal);
        Assert.Contains("_infoWarmUp.TipText =", onAir, StringComparison.Ordinal);
        Assert.Contains("_infoAutoStart.TipText =", autoStart, StringComparison.Ordinal);
        Assert.Contains("_infoFormat.TipText =", nextUp, StringComparison.Ordinal);

        Assert.DoesNotContain("SetToolTip(_chkStartWithOnAir", onAir, StringComparison.Ordinal);
        Assert.DoesNotContain("SetToolTip(_swWarmUpFirst", onAir, StringComparison.Ordinal);
        Assert.DoesNotContain("SetToolTip(_swAutoStart", autoStart, StringComparison.Ordinal);
        Assert.DoesNotContain("SetToolTip(_lblFormat", nextUp, StringComparison.Ordinal);
        Assert.DoesNotContain("SetToolTip(this", Source("FormatPenSquare.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("SetToolTip(this", Source("FocusSplitButton.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void Focus_puts_its_words_in_its_circle_as_the_view_changes()
    {
        StaThread.Run(() =>
        {
            using var focus = new FocusSplitButton();
            Assert.StartsWith("Focus:", focus.Info.TipText, StringComparison.Ordinal);

            focus.ShowState(focused: true, wide: false, inboxDocked: false);
            Assert.StartsWith("Restore:", focus.Info.TipText, StringComparison.Ordinal);
            return 0;
        }, TimeSpan.FromSeconds(30), () => "Focus");
    }
}
