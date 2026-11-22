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
    public void The_button_on_the_page_changes_its_caption_when_told()
    {
        var desk = Source("RaceControlControl.Desk.cs");

        Assert.Contains("private readonly Button _btnDockInbox = new() { Text = InboxDockButton.Caption(dockedOnScreen: false) };", desk, StringComparison.Ordinal);
        Assert.Contains("var caption = InboxDockButton.Caption(dockedOnScreen);", desk, StringComparison.Ordinal);
    }
}
