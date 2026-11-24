using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// ShowWithoutFlash (24 Nov 2026, plan in plans/native-scrollbar-flash.md):
/// painting held through a change and always given back. A window left with
/// painting off never draws again, so that is what these check - the sweep
/// in NativeScrollBarFlashTests checks the flash itself.
/// </summary>
public sealed class ShowWithoutFlashTests
{
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    private static (Form Form, Panel Panel) Window()
    {
        var form = new Form { StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-20000, -20000), ShowInTaskbar = false };
        var panel = new Panel { Dock = DockStyle.Fill };
        form.Controls.Add(panel);
        form.Show();
        Application.DoEvents();
        return (form, panel);
    }

    [Fact]
    public void Painting_is_held_during_the_change_and_given_back_after_it()
    {
        var seen = StaThread.Run(() =>
        {
            var (form, panel) = Window();

            using (form)
            {
                var during = true;
                ShowWithoutFlash.Hold(panel, () => during = IsWindowVisible(panel.Handle));
                return (During: during, After: IsWindowVisible(panel.Handle));
            }
        }, TimeSpan.FromSeconds(30), () => "holding a panel");

        Assert.False(seen.During, "held: Windows treats it as hidden from painting");
        Assert.True(seen.After, "and drawn again after");
    }

    [Fact]
    public void Painting_is_given_back_when_the_change_throws()
    {
        var after = StaThread.Run(() =>
        {
            var (form, panel) = Window();

            using (form)
            {
                Assert.Throws<InvalidOperationException>(() => ShowWithoutFlash.Hold(panel, () => throw new InvalidOperationException()));
                return IsWindowVisible(panel.Handle);
            }
        }, TimeSpan.FromSeconds(30), () => "a change that throws");

        Assert.True(after);
    }

    [Fact]
    public void A_container_the_change_hid_stays_hidden()
    {
        var seen = StaThread.Run(() =>
        {
            var (form, panel) = Window();

            using (form)
            {
                ShowWithoutFlash.Hold(panel, () => panel.Visible = false);
                return (Window: IsWindowVisible(panel.Handle), WinForms: panel.Visible);
            }
        }, TimeSpan.FromSeconds(30), () => "a change that hides its container");

        Assert.False(seen.Window);
        Assert.False(seen.WinForms);
    }

    [Fact]
    public void A_tab_change_holds_the_tab_controls_parent_and_gives_it_back()
    {
        var seen = StaThread.Run(() =>
        {
            var (form, panel) = Window();

            using (form)
            {
                var tabs = new TabControl { Dock = DockStyle.Fill };
                var first = new TabPage("One");
                var second = new TabPage("Two");
                tabs.TabPages.Add(first);
                tabs.TabPages.Add(second);
                panel.Controls.Add(tabs);
                ShowWithoutFlash.HoldTabChanges(tabs);
                Application.DoEvents();

                var during = true;
                second.VisibleChanged += (_, _) => during = IsWindowVisible(second.Handle);
                tabs.SelectedTab = second;

                for (var i = 0; i < 10; i++)
                {
                    Application.DoEvents();
                }

                return (During: during, After: IsWindowVisible(panel.Handle) && IsWindowVisible(second.Handle));
            }
        }, TimeSpan.FromSeconds(30), () => "changing a held tab control's tab");

        Assert.False(seen.During, "the new tab is shown while its tab control's parent is held");
        Assert.True(seen.After, "and everything drawn again once the change has settled");
    }

    [Fact]
    public void Every_themed_tab_control_holds_its_tab_changes()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        var theme = File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", "AppTheme.cs"));
        var style = theme[theme.IndexOf("public static void StyleTabControl(TabControl tabs)", StringComparison.Ordinal)..];

        Assert.True(style.IndexOf("ShowWithoutFlash.HoldTabChanges(tabs);", StringComparison.Ordinal) < style.IndexOf("tabs.DrawMode", StringComparison.Ordinal));
    }
}
