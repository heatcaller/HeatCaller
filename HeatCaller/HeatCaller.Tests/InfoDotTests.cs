namespace HeatCaller.Tests;

/// <summary>
/// The "i" circles at the foot of Race Control's board (25 Nov 2026, asked
/// for by Asher: "for all the tool tips here can we put them behind a small
/// circle with an i in it?").
///
/// The old behaviour: the pills, the format line, its pen and Focus each
/// popped a paragraph up whenever the mouse crossed them on the way to a
/// press, and nothing showed the help was there. Now each part's words are
/// in a small badge on its top right-hand corner, opened and closed by a
/// press ("if you click it it should bring up the tip and if you click again
/// it kills the tip"), and the parts themselves are quiet.
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
    public void A_circle_carries_its_words_for_a_screen_reader_and_pops_nothing_up_on_hover()
    {
        StaThread.Run(() =>
        {
            using var dot = new InfoDot { TipText = "Lit: every race starts with the warm-up flag." };

            // WHY (25 Nov 2026, Asher: "if you click it it should bring up the
            // tip and if you click again it kills the tip"): opened by a press,
            // not by hovering - a hover tip would open again under the mouse
            // straight after a second press closed it.
            Assert.Equal(string.Empty, dot.Tip.GetToolTip(dot));
            Assert.Equal("Lit: every race starts with the warm-up flag.", dot.AccessibleDescription);
            Assert.True(AppTheme.IsKept(dot.Tip), "the circle's tip is one the app keeps showing");
            Assert.False(dot.TabStop);
            return 0;
        }, TimeSpan.FromSeconds(30), () => "a circle");
    }

    [Fact]
    public void A_press_opens_the_tip_a_second_press_closes_it_and_opening_another_closes_the_first()
    {
        StaThread.Run(() =>
        {
            using var host = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false };
            using var a = new InfoDot { TipText = "On Air" };
            using var b = new InfoDot { TipText = "Warm-up" };
            host.Controls.Add(a);
            host.Controls.Add(b);
            host.Show();

            var down = new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0);
            var invoke = typeof(Control).GetMethod("OnMouseDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

            // On the press of the button, not its release.
            invoke.Invoke(a, new object[] { down });
            Assert.True(a.IsOpen, "the first press opens it");

            invoke.Invoke(a, new object[] { down });
            Assert.False(a.IsOpen, "the second press closes it");

            a.Toggle();
            b.Toggle();
            Assert.False(a.IsOpen, "opening another circle closes the first");
            Assert.True(b.IsOpen);

            // A right-click is not a press of the circle.
            invoke.Invoke(b, new object[] { new MouseEventArgs(MouseButtons.Right, 1, 5, 5, 0) });
            Assert.True(b.IsOpen);

            b.Visible = false;
            Assert.False(b.IsOpen, "hidden, it closes");
            return 0;
        }, TimeSpan.FromSeconds(30), () => "a circle's press");
    }

    [Fact]
    public void Each_badge_is_on_its_part_s_top_right_corner_and_takes_no_room_in_the_row()
    {
        var result = StaThread.Run(() =>
        {
            (Control[] parts, InfoDot[] dots, Label label, BoardFootRow row) Make(bool badges, Form host)
            {
                var a = new Panel { Size = new Size(100, 40) };
                var b = new Panel { Size = new Size(110, 40) };
                var c = new Panel { Size = new Size(130, 40) };
                var label = new Label { Text = "12 laps · all away together" };
                var pen = new Panel { Size = new Size(34, 34) };
                var focus = new Panel { Size = new Size(160, 44) };
                var dots = new[] { new InfoDot(), new InfoDot(), new InfoDot(), new InfoDot(), new InfoDot() };

                var row = badges
                    ? new BoardFootRow(new Control[] { a, b, c }, label, pen, focus, dots[..3], dots[3], dots[4]) { Width = 720 }
                    : new BoardFootRow(new Control[] { a, b, c }, label, pen, focus) { Width = 720 };
                host.Controls.Add(row);
                row.PerformLayout();
                return (new Control[] { a, b, c, pen, focus }, dots, label, row);
            }

            using var host = new Form { Width = 1400 };
            _ = host.Handle;
            var with = Make(badges: true, host);
            var without = Make(badges: false, host);

            for (var i = 0; i < 5; i++)
            {
                var part = with.parts[i].Bounds;
                var dot = with.dots[i].Bounds;

                Assert.True(dot.IntersectsWith(part), $"badge {i} is on its part");
                Assert.True(dot.Right <= part.Right + dot.Width / 2, $"badge {i} hangs no further than half over its part's right-hand edge");
                Assert.True(dot.Top < part.Top && dot.Top >= 0 && dot.Bottom < part.Top + part.Height / 2, $"badge {i} stands above its part's top edge and is not cut off");
                Assert.True(dot.Left + dot.Width / 2 > part.Left + part.Width * 3 / 4, $"badge {i} is on the right-hand corner of its part");
                Assert.Same(with.parts[i], with.dots[i].Over);
                Assert.True(with.row.Controls.GetChildIndex(with.dots[i]) < with.row.Controls.GetChildIndex(with.parts[i]), $"badge {i} is drawn over its part");
            }

            // At 720 wide (a 1280 window beside the clock) the format line
            // keeps every pixel it had without badges.
            Assert.Equal(without.label.Width, with.label.Width);
            Assert.True(with.label.Width >= 48, "the format line still shows");
            Assert.Equal(with.row.ClientSize.Width, with.parts[4].Right);
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
