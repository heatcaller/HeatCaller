using System.Diagnostics;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HeatCaller.Tests;

/// <summary>
/// WHY (25 Nov 2026, Asher: "speed up the animation"): Focus waited
/// 150-700 ms for each PrintWindow picture and then played for about 540 ms
/// after the view had changed. The pictures are now copied from the window's
/// own surface and every part is about 30% shorter.
/// </summary>
public sealed class FocusSpeedTests
{
    [Theory]
    [InlineData(true, 450)] // was 620
    [InlineData(false, 450)] // was 610
    public void Once_the_view_has_changed_the_rest_plays_quickly(bool entering, double most)
    {
        const double a = 500;
        var end = FocusChoreography.End(a, entering, foldParts: 8)!.Value + FocusChoreography.HandOverMs;

        Assert.InRange(end - a, 1, most);
        Assert.True(FocusChoreography.At(end, entering, a, 8).Done);
    }

    [Fact]
    public void Focus_on_its_own_clock_is_over_before_a_quick_view_change_arrives()
    {
        // The sidebar and the column's parts finish without waiting for anything.
        var folded = FocusChoreography.FoldStart + 7 * FocusChoreography.FoldStagger + FocusChoreography.FoldMs;

        Assert.True(FocusChoreography.SidebarMs <= 200);
        Assert.True(folded <= 320, $"the column folded at {folded} ms");
    }

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [Fact]
    public void The_picture_of_the_window_is_quick_and_the_same_as_PrintWindows_even_with_a_window_over_it()
    {
        var result = StaThread.Run(() =>
        {
            using var data = new TempAppData();
            new AppSettings { StartMaximized = false, HasCompletedFirstRun = true, HasSeenRaceDaySetup = true, AutoBackupEnabled = false, ConnectDecodersAtStartup = false }.Save();
            var window = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
            void Pump(int ms)
            {
                var until = Environment.TickCount64 + ms;
                while (Environment.TickCount64 < until)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
            }

            try
            {
                window.Show();
                Pump(2500);

                // A window over part of it, as the stage is over it for the picture after.
                using var cover = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    ShowInTaskbar = false,
                    BackColor = Color.Lime,
                    Bounds = window.RectangleToScreen(new Rectangle(100, 100, 400, 300)),
                };
                cover.Show();
                Pump(300);

                var take = typeof(MainForm).GetMethod("ClientPicture", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var best = long.MaxValue;
                Bitmap? quick = null;

                for (var i = 0; i < 3; i++)
                {
                    quick?.Dispose();
                    var sw = Stopwatch.StartNew();
                    quick = (Bitmap)take.Invoke(window, null)!;
                    best = Math.Min(best, sw.ElapsedMilliseconds);
                }

                using var slow = new Bitmap(window.ClientSize.Width, window.ClientSize.Height, PixelFormat.Format32bppRgb);
                using (var g = Graphics.FromImage(slow))
                {
                    var hdc = g.GetHdc();
                    PrintWindow(window.Handle, hdc, 0x1 | 0x2);
                    g.ReleaseHdc(hdc);
                }

                // Straight after PrintWindow, so nothing on the page has changed between the two.
                quick!.Dispose();
                quick = (Bitmap)take.Invoke(window, null)!;
                var differ = Differ(quick, slow);

                if (differ > 0)
                {
                    quick.Save(Path.Combine(Path.GetTempPath(), "focus-picture-quick.png"));
                    slow.Save(Path.Combine(Path.GetTempPath(), "focus-picture-slow.png"));
                }

                quick.Dispose();
                return (best, differ);
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(2), () => "focus picture");

        Assert.Equal(0, result.differ);
        Assert.True(result.best < 80, $"the picture took {result.best} ms");
    }

    // WHY (25 Nov 2026): moving the spare inbox into the dock was 260-460 ms
    // of each Focus - WinForms parks a part's window out of sight between its
    // old parent and its new one, and every part of the inbox was laid out
    // twice. Its parking place becomes the dock instead, so the inbox never
    // moves; and the start-up warming waits while Focus plays, so the picture
    // after is not queued behind it.
    [Fact]
    public void Focus_docks_the_spare_inbox_where_it_was_made_at_the_right_edge_beside_its_splitter_round_after_round()
    {
        var rounds = StaThread.Run(() =>
        {
            using var data = new TempAppData();
            new AppSettings { StartMaximized = false, HasCompletedFirstRun = true, HasSeenRaceDaySetup = true, AutoBackupEnabled = false, ConnectDecodersAtStartup = false }.Save();
            var window = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
            var seen = new List<string>();
            void Pump(int ms)
            {
                var until = Environment.TickCount64 + ms;
                while (Environment.TickCount64 < until)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
            }

            T Field<T>(string name) => (T)typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

            try
            {
                window.Show();
                Pump(2500);
                var page = Field<RaceControlControl>("_raceControlControl");

                for (var round = 0; round < 2; round++)
                {
                    window.MakeSpareInbox();
                    Pump(300);
                    var spare = Field<Form>("_spareInbox");
                    var parking = Field<Panel>("_spareParking");

                    page.FocusButton.PerformPress();
                    var until = Environment.TickCount64 + 8000;
                    while (typeof(MainForm).GetField("_focusStage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) is not null && Environment.TickCount64 < until)
                    {
                        Pump(10);
                    }

                    var inbox = Field<Form>("_inboxForm");
                    var dock = Field<Panel>("_inboxDock");
                    var split = Field<Splitter>("_inboxSplit");
                    var waited = int.Parse(window.LastArriveTimes.Split(',')[0].Replace("waited", string.Empty).Trim());
                    var emptyDocks = window.Controls.OfType<Panel>().Count(p => p.Dock == DockStyle.Right && p.Controls.Count == 0);

                    seen.Add(string.Join(" ", new[]
                    {
                        $"same inbox {ReferenceEquals(inbox, spare)}",
                        $"never moved {ReferenceEquals(inbox.Parent, parking)}",
                        $"parking is the dock {ReferenceEquals(dock, parking)}",
                        $"docked right {dock.Dock == DockStyle.Right && dock.Visible && dock.Right == window.ClientSize.Width}",
                        $"splitter beside it {split.Visible && split.Right == dock.Left}",
                        $"no empty dock {emptyDocks == 0}",
                        $"picture after not queued {waited < 150}",
                    }));

                    // Restore, and the inbox closed again for the next round's spare.
                    page.FocusButton.PerformPress();
                    until = Environment.TickCount64 + 8000;
                    while (typeof(MainForm).GetField("_focusStage", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window) is not null && Environment.TickCount64 < until)
                    {
                        Pump(10);
                    }

                    Field<Form?>("_inboxForm")?.Close();
                    Pump(300);
                }

                return seen;
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(3), () => "focus with a spare inbox");

        const string Right = "same inbox True never moved True parking is the dock True docked right True splitter beside it True no empty dock True picture after not queued True";
        Assert.Equal(new[] { Right, Right }, rounds);
    }

    private static int Differ(Bitmap a, Bitmap b)
    {
        Assert.Equal(a.Size, b.Size);
        var box = new Rectangle(Point.Empty, a.Size);
        var ra = a.LockBits(box, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        var rb = b.LockBits(box, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        var pa = new int[a.Width * a.Height];
        var pb = new int[pa.Length];
        Marshal.Copy(ra.Scan0, pa, 0, pa.Length);
        Marshal.Copy(rb.Scan0, pb, 0, pb.Length);
        a.UnlockBits(ra);
        b.UnlockBits(rb);

        var n = 0;
        for (var i = 0; i < pa.Length; i++)
        {
            if ((pa[i] & 0xFFFFFF) != (pb[i] & 0xFFFFFF))
            {
                n++;
            }
        }

        return n;
    }
}
