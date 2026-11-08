namespace HeatCaller.Tests;

/// <summary>
/// The Desk button on Race Control's flag row (7 Nov 2026): cyan, with a red
/// badge counting the drivers' requests waiting, on the row only while any
/// are, opening Driver Requests when clicked - and the row re-spacing itself
/// as it comes and goes.
/// </summary>
public sealed class DeskButtonTests
{
    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(12, "12")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    public void The_badge_says_how_many_and_caps_at_99_plus(int count, string said)
    {
        Assert.Equal(said, RaceControlControl.DeskBadgeText(count));
    }

    [Fact]
    public void It_is_cyan_with_a_red_badge()
    {
        Assert.Equal(Color.FromArgb(0x2E, 0xE6, 0xFF), RaceControlControl.DeskCyan);
        Assert.True(RaceControlControl.DeskBadgeRed.R > 200 && RaceControlControl.DeskBadgeRed.G < 80);
    }

    [Fact]
    public void A_click_opens_Driver_Requests_and_the_count_follows_the_desk()
    {
        Assert.Contains("raceControl.DeskRequested += ShowDriverRequests;", Source("MainForm.Pages.cs"), StringComparison.Ordinal);

        var tick = Source("MainForm.DriverRequests.cs");
        var set = tick.IndexOf("_raceControlControl?.SetDeskWaiting(store.WaitingCount);", StringComparison.Ordinal);
        var unchanged = tick.IndexOf("if (version == _driverRequestsVersionShown", StringComparison.Ordinal);

        Assert.True(set > 0 && unchanged > set,
            "The Desk button's count is set after the roster tick's \"nothing changed\" return, so it can miss its first tick.");

        Assert.Contains("_btnReset, _btnBarDesk);", Source("RaceControlControl.Layout.cs"), StringComparison.Ordinal);
    }

    // Bug hunt 14: Driver Requests minimised, then Desk clicked - Activate()
    // alone left it minimised and the click did nothing visible.
    [Theory]
    [InlineData("MainForm.DriverRequests.cs", "private void ShowDriverRequests()")]
    [InlineData("MainForm.Relay.cs", "private void ShowSignups()")]
    public void Bringing_the_window_forward_restores_it_from_minimised(string file, string method)
    {
        var source = Source(file);
        var start = source.IndexOf(method, StringComparison.Ordinal);
        Assert.True(start > 0, method);

        var body = source[start..source.IndexOf("open.Activate();", start, StringComparison.Ordinal)];
        Assert.Contains("open.WindowState = FormWindowState.Normal;", body, StringComparison.Ordinal);
    }

    private sealed class Held(float? progress) : IShownHold
    {
        public string Doing => "throw the red flag";

        public float? Progress { get; } = progress;
    }

    // Bug hunt 14: a request arriving while Red, Checkered or Reset was held
    // re-centred the row under the cursor, which called the hold off.
    [Fact]
    public void The_row_is_not_laid_out_again_while_a_hold_is_under_way()
    {
        Assert.True(RaceControlControl.DeskMayMoveRow(Array.Empty<IShownHold>()));
        Assert.True(RaceControlControl.DeskMayMoveRow(new IShownHold[] { new Held(null), new Held(null) }));
        Assert.False(RaceControlControl.DeskMayMoveRow(new IShownHold[] { new Held(null), new Held(0.4f) }));

        var desk = Source("RaceControlControl.Desk.cs");
        var set = desk[desk.IndexOf("internal void SetDeskWaiting(int count)", StringComparison.Ordinal)..desk.IndexOf("private bool _deskMoveOwed;", StringComparison.Ordinal)];
        Assert.DoesNotContain("Rearrange()", set, StringComparison.Ordinal);
        Assert.Contains("MoveDeskIfOwed();", set, StringComparison.Ordinal);

        // Every hold on the row tries again as it is let go.
        Assert.Contains("hold.Changed += _ => MoveDeskIfOwed();", Source("RaceControlControl.Morph.cs"), StringComparison.Ordinal);
        Assert.Contains("skip.Changed += _ => MoveDeskIfOwed();", Source("RaceControlControl.BetweenRaces.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void On_a_wide_row_the_buttons_shift_along_to_make_room_and_the_row_keeps_its_height()
    {
        var seen = ShowAndHide(1600);

        Assert.All(seen.WithDesk, b => Assert.False(b.IntersectsWith(seen.DeskBox), $"{b} overlaps the Desk button at {seen.DeskBox}"));
        Assert.NotEqual(seen.Before, seen.WithDesk);
        Assert.Equal(seen.Before[0].Top, seen.DeskBox.Top);
        Assert.Equal(seen.HeightBefore, seen.HeightWith);

        Assert.Equal(seen.Before, seen.After);
        Assert.Equal(seen.HeightBefore, seen.HeightAfter);
    }

    [Fact]
    public void On_a_narrow_row_it_goes_on_a_line_of_its_own_and_the_row_grows_then_shrinks_back()
    {
        var seen = ShowAndHide(640);

        Assert.All(seen.WithDesk, b => Assert.False(b.IntersectsWith(seen.DeskBox), $"{b} overlaps the Desk button at {seen.DeskBox}"));
        Assert.True(seen.HeightWith >= seen.HeightBefore);

        Assert.Equal(seen.Before, seen.After);
        Assert.Equal(seen.HeightBefore, seen.HeightAfter);
    }

    private sealed record Seen(
        Rectangle[] Before, int HeightBefore, Rectangle[] WithDesk, Rectangle DeskBox, int HeightWith, Rectangle[] After, int HeightAfter);

    private static Seen ShowAndHide(int width)
    {
        return StaThread.Run(() =>
        {
            using var window = new QuietWindow();
            var page = new Panel { Bounds = new Rectangle(0, 0, width, 400) };
            window.Controls.Add(page);

            var stack = new PageStack();
            page.Controls.Add(stack);

            // The racing row - five - and the Desk button, hidden.
            var racing = new Button[]
            {
                new FlagButton { Text = "Green" },
                new FlagButton { Text = "Yellow" },
                new FlagButton { Text = "Red" },
                new FlagButton { Chequered = true, Text = "Checkered" },
                new Button { Text = "Reset" },
            };

            var desk = new FlagButton { Text = "Desk", FlagColour = RaceControlControl.DeskCyan, Lit = true, Visible = false };
            var bar = new ButtonBar(tight: true, sameWidth: true).With(racing.Append(desk).ToArray());
            var row = new FlagRow(bar, "Race Control", new HoldStrip());

            stack.Add(new Section()).Add(row, gapBelow: 0);
            AppTheme.ApplyRecursively(page);

            window.Show();
            StaThread.Settle();

            // Where each button is on the ROW - the bar itself is what moves
            // to keep the buttons centred.
            Rectangle OnRow(Control b) => row.RectangleToClient(b.RectangleToScreen(b.ClientRectangle));
            Rectangle[] Places() => racing.Select(OnRow).ToArray();

            var before = Places();
            var heightBefore = row.Height;

            desk.Visible = true;
            row.Rearrange();
            StaThread.Settle();

            var withDesk = Places();
            var deskBox = OnRow(desk);
            var heightWith = row.Height;

            desk.Visible = false;
            row.Rearrange();
            StaThread.Settle();

            return new Seen(before, heightBefore, withDesk, deskBox, heightWith, Places(), row.Height);
        }, TimeSpan.FromMinutes(1), () => $"the Desk button on the flag row at {width}px");
    }

    private sealed class QuietWindow : Form
    {
        public QuietWindow()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Opacity = 0d;
            Size = new Size(200, 200);
            Font = AppTheme.BodyFont;
            BackColor = AppTheme.Background;
        }

        protected override bool ShowWithoutActivation => true;
    }
}
