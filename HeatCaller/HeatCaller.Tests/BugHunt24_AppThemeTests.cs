using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, finding F050: a flag button clicked with the mouse is meant
/// to let go of the focus so a stray Space or Enter cannot press it again.
/// The check compared the form's ActiveControl to the button, but inside a
/// page (a UserControl, as Race Control is) the form's ActiveControl is the
/// page, so the button kept the focus.
/// </summary>
public sealed class BugHunt24_AppThemeTests
{
    private sealed record Rig(Form Host, Button Flag, TextBox Other);

    private static Rig Build(bool insidePage)
    {
        var host = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000),
            ClientSize = new Size(400, 200),
        };

        var flag = new Button { Text = "Yellow", Location = new Point(10, 10), Size = new Size(100, 40) };
        var other = new TextBox { Location = new Point(10, 80), Width = 100 };

        AppTheme.ReleaseFocusAfterClick(flag);

        if (insidePage)
        {
            var page = new UserControl { Dock = DockStyle.Fill };
            page.Controls.Add(flag);
            page.Controls.Add(other);
            host.Controls.Add(page);
        }
        else
        {
            host.Controls.Add(flag);
            host.Controls.Add(other);
        }

        host.Show();
        StaThread.Settle();

        flag.Focus();
        StaThread.Settle();

        return new Rig(host, flag, other);
    }

    private static void Raise(Control control, string method, EventArgs args) =>
        typeof(Control)
            .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic, null, [args.GetType()], null)!
            .Invoke(control, [args]);

    private static void MouseClick(Button button)
    {
        Raise(button, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0));
        button.PerformClick();
    }

    private static T OnUiThread<T>(Func<T> body) =>
        StaThread.Run(body, TimeSpan.FromMinutes(1), () => "bug hunt 24, flag button focus");

    [Fact]
    public void BugHunt24_MouseClickedFlagInsideAPage_LetsGoOfFocus()
    {
        var (before, after) = OnUiThread(() =>
        {
            var rig = Build(insidePage: true);

            try
            {
                var had = rig.Flag.Focused;
                MouseClick(rig.Flag);
                StaThread.Settle();
                return (had, rig.Flag.Focused);
            }
            finally
            {
                rig.Host.Dispose();
            }
        });

        Assert.True(before, "the test could not put the focus on the button to start with");
        Assert.False(after);
    }

    [Fact]
    public void BugHunt24_MouseClickedFlagDirectlyOnTheForm_StillLetsGoOfFocus()
    {
        var (before, after) = OnUiThread(() =>
        {
            var rig = Build(insidePage: false);

            try
            {
                var had = rig.Flag.Focused;
                MouseClick(rig.Flag);
                StaThread.Settle();
                return (had, rig.Flag.Focused);
            }
            finally
            {
                rig.Host.Dispose();
            }
        });

        Assert.True(before, "the test could not put the focus on the button to start with");
        Assert.False(after);
    }

    [Fact]
    public void BugHunt24_KeyboardPressedFlagInsideAPage_KeepsFocus()
    {
        var (before, after) = OnUiThread(() =>
        {
            var rig = Build(insidePage: true);

            try
            {
                var had = rig.Flag.Focused;
                Raise(rig.Flag, "OnKeyDown", new KeyEventArgs(Keys.Space));
                rig.Flag.PerformClick();
                StaThread.Settle();
                return (had, rig.Flag.Focused);
            }
            finally
            {
                rig.Host.Dispose();
            }
        });

        Assert.True(before, "the test could not put the focus on the button to start with");
        Assert.True(after);
    }
}
