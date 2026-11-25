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

    // Race Control's square went on 24 Nov 2026: docking the inbox there is
    // Focus, or "Inbox only" on Focus's menu - see FocusOnRaceControlTests.
    // Practice keeps the square.
    [Fact]
    public void The_square_docks_by_the_rules_and_Race_Control_docks_through_Focus()
    {
        Assert.Contains("Click += (_, _) => DockRequested?.Invoke(InboxDockButton.DockOnPress(_dockedOnScreen));", Source("DockInboxSquare.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("DockInboxSquare", Source("RaceControlControl.Desk.cs").Replace("(DockInboxSquare)", string.Empty), StringComparison.Ordinal);
        Assert.Contains("DockInboxFromRaceControl(true);", Source("MainForm.Focus.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_host_saves_the_choice_then_moves_or_opens_the_inbox_and_tells_the_button()
    {
        // Through Focus since 24 Nov 2026 - see FocusOnRaceControlTests.
        Assert.Contains("_raceControlControl.FocusRequested += FocusFromRaceControl;", Source("MainForm.Pages.cs"), StringComparison.Ordinal);

        var host = Source("MainForm.Inbox.cs");
        var press = host[host.IndexOf("internal void DockInboxFromRaceControl(bool dock)", StringComparison.Ordinal)..];

        // Saved before the window moves, so the inbox's own tick does not ask again.
        var saved = press.IndexOf("_settings.InboxDocked = dock;", StringComparison.Ordinal);
        var opened = press.IndexOf("ShowInbox(InboxFilter.All);", StringComparison.Ordinal);
        var moved = press.IndexOf("HostInbox(open, dock);", StringComparison.Ordinal);
        Assert.True(saved > 0 && opened > saved && moved > opened);

        // A minimised or maximised window is put back before it goes in the
        // dock - on the way through HostInbox, for the inbox's own tick too
        // (23 Nov 2026, see DeskInboxDockTests).
        Assert.Contains("PutInDock(form, _inboxDock!);", host, StringComparison.Ordinal);
        Assert.Contains("form.WindowState = FormWindowState.Normal;", host, StringComparison.Ordinal);

        // The caption follows every move, and the inbox closing.
        Assert.Contains("form.ShowDocked(docked);\n        ShowInboxDockedOnRaceControl();", host, StringComparison.Ordinal);
        Assert.Contains("HideInboxDock();\n            ShowInboxDockedOnRaceControl();", host, StringComparison.Ordinal);
    }

    [Fact]
    public void It_is_a_picture_named_by_its_tooltip_and_the_name_changes_when_told()
    {
        var square = Source("DockInboxSquare.cs");

        Assert.Contains("AccessibleName = InboxDockButton.Caption(dockedOnScreen: false);", square, StringComparison.Ordinal);
        Assert.Contains("var caption = InboxDockButton.Caption(dockedOnScreen);", square, StringComparison.Ordinal);
        Assert.Contains("_tip.SetToolTip(this, InboxDockButton.Tip(dockedOnScreen));", square, StringComparison.Ordinal);

        Assert.StartsWith("Dock Inbox - ", InboxDockButton.Tip(dockedOnScreen: false), StringComparison.Ordinal);
        Assert.StartsWith("Undock Inbox - ", InboxDockButton.Tip(dockedOnScreen: true), StringComparison.Ordinal);
    }

    // The real-window test of Race Control's square (every press, and what it
    // read after it) is FocusOnRaceControlTests' since 24 Nov 2026, through
    // Focus's "Inbox only".
}
