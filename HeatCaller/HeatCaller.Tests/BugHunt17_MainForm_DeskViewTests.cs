namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the desk view (MainForm.DeskView, MainForm.Keyboard): the
/// letter shortcuts, the app's Ctrl+Z and the buttons at the foot of the
/// sidebar still worked on a locked desk, and Alt+F4 closed the banner that
/// holds the only Unlock. These are window wiring, so the tests read the
/// source, as DeskHandoverTests does.
/// </summary>
public sealed class BugHunt17_MainForm_DeskViewTests
{
    [Fact]
    public void The_letter_shortcuts_do_nothing_on_a_locked_desk()
    {
        var keyboard = Source("MainForm.Keyboard.cs");
        var keyDown = keyboard[keyboard.IndexOf("private void MainForm_KeyDown(", StringComparison.Ordinal)..];

        var guard = keyDown.IndexOf("if (_deskOnly)", StringComparison.Ordinal);
        var firstShortcut = keyDown.IndexOf("IsTypingInTextField()", StringComparison.Ordinal);

        Assert.True(guard >= 0, "MainForm_KeyDown has no desk view guard.");
        Assert.True(guard < firstShortcut, "The desk view guard has to come before the shortcuts.");
    }

    [Fact]
    public void Ctrl_Z_is_not_the_app_s_undo_on_a_locked_desk()
    {
        Assert.Contains(
            "if (keyData == (Keys.Control | Keys.Z) && !_deskOnly && !FocusedControlHasOwnUndo())",
            Source("MainForm.Keyboard.cs"));
    }

    [Fact]
    public void The_foot_of_the_sidebar_is_off_while_locked_and_back_on_after()
    {
        var desk = Source("MainForm.DeskView.cs");

        Assert.Contains("pnlSidebarFooter.Enabled = false;", desk);
        Assert.Contains("btnCollapseNav.Enabled = false;", desk);
        Assert.Contains("pnlSidebarFooter.Enabled = true;", desk);
        Assert.Contains("btnCollapseNav.Enabled = true;", desk);

        // Every button and chip down there is inside that one panel.
        var designer = Source("MainForm.Designer.cs");

        Assert.Contains("pnlSidebarFooter.Controls.Add(btnUndo);", designer);
        Assert.Contains("pnlSidebarFooter.Controls.Add(btnManageNav);", designer);
        Assert.Contains("pnlSidebarFooter.Controls.Add(btnMuteToggle);", designer);
        Assert.Contains("pnlSidebarFooter.Controls.Add(_advancedChip);", Source("MainForm.SettingsMode.cs"));
        Assert.Contains("pnlSidebarFooter.Controls.Add(_usageChip);", Source("MainForm.Usage.cs"));
    }

    [Fact]
    public void The_banner_cannot_be_closed_while_the_desk_is_locked()
    {
        var desk = Source("MainForm.DeskView.cs");
        var banner = desk[desk.IndexOf("private void ShowDeskBanner()", StringComparison.Ordinal)..];

        Assert.Contains("banner.FormClosing += (_, e) =>", banner);
        Assert.Contains("if (_deskOnly && e.CloseReason == CloseReason.UserClosing)", banner);
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
