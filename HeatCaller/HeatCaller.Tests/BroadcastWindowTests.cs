using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The Broadcast window (25 Oct 2026): Race Control's row of broadcast
/// controls - Hold, Play again, Skip, Notes, Votes - moved into a window of
/// its own, opened from a Broadcast button under Mission Control, with what
/// the announcer is doing said in full beside them.
/// </summary>
public sealed class BroadcastWindowTests
{
    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    [Fact]
    public void The_budget_line_loses_its_own_Tonight_since_the_caption_says_it()
    {
        Assert.Equal("22 lines written.", BroadcastWindow.Tonight("Tonight: 22 lines written."));
        Assert.Equal("1 line written.", BroadcastWindow.Tonight("Tonight: 1 line written."));
        Assert.Equal("22 of about 40 lines - everything running.", BroadcastWindow.Tonight("Tonight: 22 of about 40 lines - everything running."));

        // Anything else is left as it came.
        Assert.Equal("Something else.", BroadcastWindow.Tonight("Something else."));
        Assert.Equal("Tonight: ", BroadcastWindow.Tonight("Tonight: "));
    }

    [Fact]
    public void The_window_holds_Race_Control_s_own_row_and_its_heading_says_what_the_row_would()
    {
        var seen = StaThread.Run(() =>
        {
            var strip = new AnnouncerStrip();
            using var window = new BroadcastWindow(strip);

            var settings = new AppSettings();
            var director = new CommentaryDirector(new CommentaryService(ProfileStore.Load(), settings), settings);

            strip.Show(director, true, speaking: false);
            window.ShowState(director, true, speaking: false, muted: false, notesWaiting: 0, votesIn: 0, load: null,
                recapRunning: false, replaying: false, momentHeld: false);

            var said = Field<Label>(strip, "_said");
            var heading = Field<Label>(window, "_lblOnAir");

            // In the window - the same control, not a copy.
            var inWindow = strip.FindForm() == window;

            return (inWindow, strip.ShowsSaying, saidText: said.Text, heading: heading.Text);
        }, TimeSpan.FromSeconds(30), () => "building the Broadcast window");

        Assert.True(seen.inWindow);
        Assert.False(seen.ShowsSaying);

        // The row's own label is kept up to date for the heading to read.
        Assert.False(string.IsNullOrEmpty(seen.saidText));
        Assert.Equal(seen.saidText, seen.heading);
    }

    [Fact]
    public void A_row_that_does_not_show_its_label_hides_it_at_once_and_keeps_its_buttons()
    {
        // On a row with no window round it, Visible reads each control's own
        // setting - in a window never shown, every child reads false.
        var seen = StaThread.Run(() =>
        {
            var settings = new AppSettings();
            var director = new CommentaryDirector(new CommentaryService(ProfileStore.Load(), settings), settings);

            using var strip = new AnnouncerStrip();
            strip.Show(director, true, speaking: false);
            var before = Field<Label>(strip, "_said").Visible;

            strip.ShowsSaying = false;
            var at = Field<Label>(strip, "_said").Visible;

            strip.Show(director, true, speaking: false);
            var after = Field<Label>(strip, "_said").Visible;
            var hold = Field<Button>(strip, "_hold").Visible;

            return (before, at, after, hold);
        }, TimeSpan.FromSeconds(30), () => "drawing the announcer's row");

        Assert.True(seen.before);
        Assert.False(seen.at);
        Assert.False(seen.after);
        Assert.True(seen.hold);
    }

    [Fact]
    public void Disposing_the_window_lets_go_of_Race_Control_s_row()
    {
        var seen = StaThread.Run(() =>
        {
            using var strip = new AnnouncerStrip();
            var window = new BroadcastWindow(strip);
            window.Dispose();

            return (strip.IsDisposed, strip.Parent is null, strip.ShowsSaying);
        }, TimeSpan.FromSeconds(30), () => "disposing the Broadcast window");

        Assert.False(seen.IsDisposed);
        Assert.True(seen.Item2);
        Assert.True(seen.ShowsSaying);
    }

    [Fact]
    public void The_flag_keys_pressed_in_the_window_reach_Race_Control()
    {
        var seen = StaThread.Run(() =>
        {
            var keys = new List<Keys>();
            using var strip = new AnnouncerStrip();
            using var window = new BroadcastWindow(strip, key => { keys.Add(key); return true; });

            var process = typeof(BroadcastWindow).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var msg = new Message();

            foreach (var key in new[] { Keys.F4, Keys.F4 | Keys.Shift, Keys.A })
            {
                process.Invoke(window, new object[] { msg, key });
            }

            return keys;
        }, TimeSpan.FromSeconds(30), () => "pressing keys in the Broadcast window");

        // F4 only: not with Shift held, as MainForm takes them, and not letters.
        Assert.Equal(new[] { Keys.F4 }, seen);
    }

    [Fact]
    public void Switched_off_it_says_so_rather_than_showing_last_night_s_readings()
    {
        var heading = StaThread.Run(() =>
        {
            var strip = new AnnouncerStrip();
            using var window = new BroadcastWindow(strip);

            strip.Show(null, false, speaking: false);
            window.ShowState(null, false, speaking: false, muted: false, notesWaiting: 0, votesIn: 0, load: null,
                recapRunning: false, replaying: false, momentHeld: false);

            return Field<Label>(window, "_lblOnAir").Text;
        }, TimeSpan.FromSeconds(30), () => "building the Broadcast window");

        Assert.Equal("The announcer is off", heading);
    }

    [Fact]
    public void Restyled_the_row_s_buttons_grow_with_their_captions_again()
    {
        // The theme pass turns AutoSize off on a button it thinks too short,
        // and the row is styled before it was ever laid out - every button
        // was left at a new button's 75px, "Replay Moment" cut in half.
        var seen = StaThread.Run(() =>
        {
            var strip = new AnnouncerStrip();
            using var window = new BroadcastWindow(strip);

            return new[] { "_hold", "_replay", "_replayMoment", "_skip", "_notes", "_votes" }
                .Select(name => Field<Button>(strip, name))
                .Select(b => (b.Text, b.AutoSize, b.AutoSizeMode))
                .ToList();
        }, TimeSpan.FromSeconds(30), () => "building the Broadcast window");

        Assert.All(seen, b =>
        {
            Assert.True(b.AutoSize, b.Text);
            Assert.Equal(AutoSizeMode.GrowAndShrink, b.AutoSizeMode);
        });
    }

    [Fact]
    public void Race_Control_opens_it_from_a_Broadcast_button_and_the_two_above_lose_Open()
    {
        var source = Source("RaceControlControl.Layout.cs");

        Assert.Contains("_btnBigScreen.Text = \"Big Screen\";", source);
        Assert.Contains("_btnAnnouncer.Text = \"Announcer Screen\";", source);
        Assert.DoesNotContain("\"Open Big Screen\"", source);
        Assert.DoesNotContain("\"Open Announcer Screen\"", source);

        // Under Mission Control, in the column of things that open a window.
        var missionControl = source.IndexOf("body.AddSide(btnMissionControl);", StringComparison.Ordinal);
        var broadcast = source.IndexOf("body.AddSide(_btnBroadcast);", StringComparison.Ordinal);
        Assert.True(missionControl > 0 && broadcast > missionControl);

        // And the row is no longer on the page itself.
        Assert.DoesNotContain("_page.Add(_announcerStrip", source);
    }

    /// <summary>The folder the repo is checked out in, found by the file that is only ever at its root (as ShowOnScreenTests finds it).</summary>
    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));
}
