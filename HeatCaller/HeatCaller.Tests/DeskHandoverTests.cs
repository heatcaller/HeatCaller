namespace HeatCaller.Tests;

/// <summary>
/// Handing the desk over (12 Nov 2026, setup plan D8): what the desk view
/// lets through, and the wiring that keeps it to Who's Racing. See MainForm.DeskView.
/// </summary>
public sealed class DeskHandoverTests
{
    [Theory]
    [InlineData(Keys.A)]
    [InlineData(Keys.D7)]
    [InlineData(Keys.Enter)]
    [InlineData(Keys.Tab)]
    [InlineData(Keys.Back)]
    [InlineData(Keys.A | Keys.Shift)]
    [InlineData(Keys.C | Keys.Control)]
    [InlineData(Keys.V | Keys.Control)]
    [InlineData(Keys.Z | Keys.Control)]
    public void Typing_and_the_editing_shortcuts_go_through(Keys key)
    {
        Assert.True(MainForm.DeskViewAllowsKey(key));
    }

    [Theory]
    [InlineData(Keys.F6)]
    [InlineData(Keys.F11)]
    [InlineData(Keys.F1)]
    [InlineData(Keys.S | Keys.Shift | Keys.Control)]
    [InlineData(Keys.S | Keys.Control)]
    [InlineData(Keys.F4 | Keys.Alt)]
    [InlineData(Keys.D1 | Keys.Alt)]
    public void Function_keys_and_other_shortcuts_do_nothing(Keys key)
    {
        Assert.False(MainForm.DeskViewAllowsKey(key));
    }

    [Fact]
    public void The_desk_view_is_wired_to_every_way_off_Who_s_Racing()
    {
        var navigation = Source("MainForm.Navigation.cs");
        var keyboard = Source("MainForm.Keyboard.cs");
        var pages = Source("MainForm.Pages.cs");
        var desk = Source("MainForm.DeskView.cs");

        Assert.Contains("if (DeskViewRefusesPage(name))", navigation);
        Assert.Contains("if (_deskOnly && !DeskViewAllowsKey(keyData))", keyboard);
        Assert.Contains("if (!DeskViewAllowsTab(e.TabPage, subPageEntries))", pages);
        Assert.Contains("checkIn.DeskViewRequested += StartDeskView;", pages);
        Assert.Contains("protected override void OnFormClosing(FormClosingEventArgs e)", desk);
    }

    [Fact]
    public void The_first_run_wizard_and_Who_s_Racing_read_a_list_the_same_way()
    {
        Assert.Contains("EntryImportFlow.FromFile(this)", Source("CheckInControl.Entries.cs"));
        Assert.Contains("EntryImportFlow.FromFile(this)", Source("FirstRunWizard.Import.cs"));
        Assert.Contains("EntryImportFlow.Preview(this, list", Source("FirstRunWizard.Import.cs"));
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
