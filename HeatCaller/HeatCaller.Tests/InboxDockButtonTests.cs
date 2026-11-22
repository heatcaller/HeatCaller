using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Dock Inbox on Race Control (22 Nov 2026, asked for by Asher: "make a
/// button next to expand on race control to dock the inbox"): at the foot
/// of the board beside Expand, it docks the inbox - opening it if it is
/// closed - and then reads Undock Inbox, which makes it a window again.
/// </summary>
public sealed class InboxDockButtonTests
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

    [Fact]
    public void It_reads_what_a_press_would_do()
    {
        Assert.Equal("Dock Inbox", InboxDockButton.Caption(dockedOnScreen: false));
        Assert.Equal("Undock Inbox", InboxDockButton.Caption(dockedOnScreen: true));
    }

    // Closed, or in a window of its own: a press docks it. Docked: a press undocks it.
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void A_press_docks_it_unless_it_is_docked_on_screen(bool open, bool inItsOwnWindow, bool docks)
    {
        var onScreen = InboxDockButton.DockedOnScreen(open, inItsOwnWindow);

        Assert.Equal(docks, InboxDockButton.DockOnPress(onScreen));
    }

    // A docked inbox that was closed leaves the saved choice on. The button
    // goes by what is on screen, so it never offers to undock nothing.
    [Fact]
    public void A_closed_inbox_is_never_docked_on_screen_whatever_was_saved()
    {
        Assert.False(InboxDockButton.DockedOnScreen(open: false, inItsOwnWindow: false));
        Assert.Equal("Dock Inbox", InboxDockButton.Caption(InboxDockButton.DockedOnScreen(open: false, inItsOwnWindow: false)));
    }

    [Fact]
    public void It_stands_beside_Expand_at_the_foot_of_the_board()
    {
        Assert.Contains("_btnFormat, _btnExpandBoard, _btnDockInbox);", Source("RaceControlControl.Layout.cs"), StringComparison.Ordinal);

        var desk = Source("RaceControlControl.Desk.cs");
        Assert.Contains("DockInboxRequested?.Invoke(InboxDockButton.DockOnPress(_inboxDockedOnScreen));", desk, StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_saves_the_choice_then_moves_or_opens_the_inbox_and_tells_the_button()
    {
        Assert.Contains("_raceControlControl.DockInboxRequested += DockInboxFromRaceControl;", Source("MainForm.Pages.cs"), StringComparison.Ordinal);

        var host = Source("MainForm.Inbox.cs");
        var press = host[host.IndexOf("internal void DockInboxFromRaceControl(bool dock)", StringComparison.Ordinal)..];

        // Saved before the window moves, so the inbox's own tick does not ask again.
        var saved = press.IndexOf("_settings.InboxDocked = dock;", StringComparison.Ordinal);
        var opened = press.IndexOf("ShowInbox(InboxFilter.All);", StringComparison.Ordinal);
        var moved = press.IndexOf("HostInbox(open, dock);", StringComparison.Ordinal);
        Assert.True(saved > 0 && opened > saved && moved > opened);

        // A minimised window is put back before it goes in the dock.
        Assert.Contains("open.WindowState = FormWindowState.Normal;", press, StringComparison.Ordinal);

        // The caption follows every move, and the inbox closing.
        Assert.Contains("form.ShowDocked(docked);\n        ShowInboxDockedOnRaceControl();", host, StringComparison.Ordinal);
        Assert.Contains("HideInboxDock();\n            ShowInboxDockedOnRaceControl();", host, StringComparison.Ordinal);
    }

    [Fact]
    public void It_is_a_picture_named_by_its_tooltip_and_the_name_changes_when_told()
    {
        var desk = Source("RaceControlControl.Desk.cs");

        Assert.Contains("AccessibleName = InboxDockButton.Caption(dockedOnScreen: false),", desk, StringComparison.Ordinal);
        Assert.Contains("var caption = InboxDockButton.Caption(dockedOnScreen);", desk, StringComparison.Ordinal);
        Assert.Contains("_dockInboxTip.SetToolTip(_btnDockInbox, InboxDockButton.Tip(dockedOnScreen));", desk, StringComparison.Ordinal);

        Assert.StartsWith("Dock Inbox - ", InboxDockButton.Tip(dockedOnScreen: false), StringComparison.Ordinal);
        Assert.StartsWith("Undock Inbox - ", InboxDockButton.Tip(dockedOnScreen: true), StringComparison.Ordinal);
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    // The real page in the real window: every press, and what the button reads after it.
    [Fact]
    public void On_the_page_a_press_docks_it_the_next_undocks_it_and_closing_it_puts_the_caption_back()
    {
        var seen = StaThread.Run(() =>
        {
            using var data = new TempAppData();

            new AppSettings
            {
                StartMaximized = false,
                HasCompletedFirstRun = true,
                HasSeenRaceDaySetup = true,
                AutoBackupEnabled = false,
                ConnectDecodersAtStartup = false,
            }.Save();

            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };

            try
            {
                window.Show();
                StaThread.Settle();

                var page = Field<RaceControlControl>(window, "_raceControlControl");
                var button = page.DockInboxButton;
                var expand = Field<Button>(page, "_btnExpandBoard");
                var format = Field<Button>(page, "_btnFormat");
                DeskInboxForm? Inbox() => (DeskInboxForm?)typeof(MainForm).GetField("_inboxForm", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);

                var log = new List<string>();
                void Note(string step) => log.Add($"{step}: \"{button.AccessibleName}\" open={Inbox() is { IsDisposed: false }} window={Inbox()?.TopLevel} saved={AppSettings.Load().InboxDocked} square={button.Width == button.Height && button.Text.Length == 0}");

                // WHY (22 Nov 2026): in words ("Dock Inbox") the button needed 120 pixels where a
                // 1280-wide window leaves the row 46, and dropped to a second row - so it is a picture.
                window.SizeContentForLayoutCheck(new Size(1280, 800));
                StaThread.Settle();
                var oneRow = button.Parent == expand.Parent && button.Top == expand.Top && format.Top == expand.Top && button.Left > expand.Right - 1;

                var row = "bar " + button.Parent!.Width + ": " + string.Join(" | ", button.Parent.Controls.Cast<Control>().Select(c => $"{c.Text} L{c.Left} T{c.Top} W{c.Width}"));
                Note("start");

                button.PerformClick();
                StaThread.Settle();
                Note("pressed closed");

                button.PerformClick();
                StaThread.Settle();
                Note("pressed docked");

                // Minimised in its own window, a press still docks it, and not minimised.
                Inbox()!.WindowState = FormWindowState.Minimized;
                button.PerformClick();
                StaThread.Settle();
                Note("pressed minimised");
                var state = Inbox()!.WindowState;

                // Closed while docked: the choice stays saved, the button offers to dock again.
                Inbox()!.Close();
                StaThread.Settle();
                Note("closed docked");

                return (oneRow, row, state, log);
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(2), () => "Race Control's Dock Inbox button");

        Assert.True(seen.oneRow, "Format, Expand and Dock Inbox share one row at 1280 wide - " + seen.row);
        Assert.Equal(FormWindowState.Normal, seen.state);
        Assert.Equal(new[]
        {
            "start: \"Dock Inbox\" open=False window= saved=False square=True",
            "pressed closed: \"Undock Inbox\" open=True window=False saved=True square=True",
            "pressed docked: \"Dock Inbox\" open=True window=True saved=False square=True",
            "pressed minimised: \"Undock Inbox\" open=True window=False saved=True square=True",
            "closed docked: \"Dock Inbox\" open=False window= saved=True square=True",
        }, seen.log);
    }
}
