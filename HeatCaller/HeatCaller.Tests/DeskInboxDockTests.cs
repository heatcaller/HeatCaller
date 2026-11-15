using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// The Desk Inbox docked in the main window (15 Nov 2026, Asher: "fix them
/// all" - the plan's docking): a tick on the inbox asks its host to move it
/// into a panel down the right of the main window, beside Race Control, with
/// a splitter, and back out again. And Who's Racing's buttons say they open
/// the inbox.
/// </summary>
public sealed class DeskInboxDockTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
    }

    [Fact]
    public void The_tick_saves_the_choice_and_asks_the_host_to_move_it()
    {
        StaThread.Run(() =>
        {
            var settings = new AppSettings();
            using var inbox = new DeskInboxForm(null, ProfileStore.Load(), null, null, null, null, null, null, settings, null, null, null);

            var asked = new List<bool>();
            inbox.DockWanted += asked.Add;

            var tick = (CheckBox)typeof(DeskInboxForm).GetField("_chkPinned", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inbox)!;
            Assert.Equal("Dock in the main window", tick.Text);
            Assert.False(tick.Checked);

            tick.Checked = true;
            Assert.True(settings.InboxDocked);
            Assert.Equal(new[] { true }, asked);

            // The host saying so does not ask again.
            inbox.ShowDocked(true);
            Assert.Equal(new[] { true }, asked);

            tick.Checked = false;
            Assert.False(settings.InboxDocked);
            Assert.Equal(new[] { true, false }, asked);

            return true;
        }, TimeSpan.FromSeconds(30), () => "the inbox's dock tick");
    }

    [Fact]
    public void Docked_it_is_a_child_of_a_right_hand_panel_with_a_splitter_and_back_out_it_is_a_window_again()
    {
        var host = Source("MainForm.Inbox.cs");

        Assert.Contains("form.DockWanted += docked => HostInbox(form, docked);", host, StringComparison.Ordinal);
        Assert.Contains("HostInbox(form, _settings.InboxDocked);", host, StringComparison.Ordinal);
        Assert.Contains("form.TopLevel = false;", host, StringComparison.Ordinal);
        Assert.Contains("form.Dock = DockStyle.Fill;", host, StringComparison.Ordinal);
        Assert.Contains("form.TopLevel = true;", host, StringComparison.Ordinal);

        // Added after the content and the sidebar, so it docks first, at the right.
        var split = host.IndexOf("Controls.Add(_inboxSplit);", StringComparison.Ordinal);
        var dock = host.IndexOf("Controls.Add(_inboxDock);", StringComparison.Ordinal);
        Assert.True(split > 0 && dock > split);
        Assert.Contains("new Splitter { Dock = DockStyle.Right", host, StringComparison.Ordinal);

        // Its width is the club's, kept as the splitter moves.
        Assert.Contains("_settings.InboxDockWidth = dock.Width;", host, StringComparison.Ordinal);
        Assert.Equal(520, new AppSettings().InboxDockWidth);
    }

    [Fact]
    public void Who_s_Racing_s_buttons_say_they_open_the_inbox()
    {
        var entries = Source("CheckInControl.cs");

        Assert.Contains("\"Inbox: Sign-ups ({Math.Min(waiting, 99)})\" : \"Inbox: Sign-ups\"", entries, StringComparison.Ordinal);
        Assert.Contains("\"Inbox: Requests ({Math.Min(waiting, 99)})\" : \"Inbox: Requests\"", entries, StringComparison.Ordinal);
        Assert.Contains("Text = \"Inbox: Sign-ups\"", Source("CheckInControl.Desk.cs"), StringComparison.Ordinal);
    }
}
