using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// THE WHITE SCROLL BAR THAT FLASHED ON A FIRST VISIT (24 Nov 2026, Asher:
/// "theres a lot of times i see a native scroll bar when first clicking onto
/// a screen ... only the first time it loads"; plan in
/// plans/native-scrollbar-flash.md).
///
/// Builds the main window off screen, runs the startup warm-up the real app
/// runs, then visits every page, every tab of every tab control, the Race
/// Day Setup strip and Driver Hub's card tabs, each for the first time. A
/// message hook on the window's thread sees every frame paint and every
/// paint of a grid's own scroll bar, and notes each one where Windows draws
/// its own scroll bar on screen with no slim bar (ThinScrollBar) over it.
/// The flash lasts one paint, so nothing but a hook can see it: a look at
/// the window between steps always finds it fine.
///
/// A paint made while painting is held (WM_SETREDRAW off on a parent) never
/// reaches the screen, and Windows says so: the window is not visible then
/// (IsWindowVisible). Those are left out, which is what lets the fix -
/// holding painting through a first show - pass.
/// </summary>
public sealed class NativeScrollBarFlashTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>
    /// With the startup warm-up, as on an idle PC - and without it, as on a
    /// busy one, where the warm-up gives up after a slow step (WarmStepTooSlow)
    /// and every tab has its windows made as it is first picked. That second
    /// case is what the tab-change hold is for: with the warm-up alone, a
    /// build without that hold failed one run in three; without the warm-up,
    /// every run (24 Nov 2026 lookback).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void No_native_scroll_bar_shows_uncovered_on_a_first_visit_to_any_page_or_tab(bool warmUp)
    {
        Seed();

        var found = StaThread.Run(() =>
        {
            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-20000, -20000) };
            using var watch = new NativeBarWatch();

            try
            {
                window.Show();
                window.ClientSize = new System.Drawing.Size(1280, 800);
                StaThread.Settle();

                // The warm-up the real window runs from Shown (LayoutCheckMode
                // skips it): without it pages flash that a user never sees flash.
                var warmed = !warmUp;

                if (warmUp)
                {
                    typeof(MainForm).GetMethod("WarmPagesInTurn", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, new object[] { new Action<TimeSpan, string, string?>((_, _, _) => warmed = true) });
                }

                var until = DateTime.UtcNow.AddMinutes(3);
                while (!warmed && DateTime.UtcNow < until)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }

                Pump();

                watch.Start();
                Sweep(window, watch);
                return watch.Found.ToList();
            }
            finally
            {
                watch.Stop();
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(8), () => "sweeping every page and tab for an uncovered native scroll bar");

        Assert.True(found.Count(f => !f.StartsWith("    ", StringComparison.Ordinal)) == 0, "A native scroll bar showed with no slim bar over it:\n" + string.Join("\n", found));
    }

    /// <summary>
    /// The windows the app opens on their own - the Inbox, Mission Control,
    /// the Announcer screen, Broadcast, Big Screen and Photo Finish - each
    /// opened for the first time and gone through: their tabs, the Inbox's
    /// filters, and the Inbox docked and made a window again (24 Nov 2026
    /// lookback: the page sweep never reached them).
    /// </summary>
    [Fact]
    public void No_native_scroll_bar_shows_uncovered_when_a_window_is_first_opened()
    {
        Seed();

        var found = StaThread.Run(() =>
        {
            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-20000, -20000) };
            using var watch = new NativeBarWatch();

            try
            {
                window.Show();
                window.ClientSize = new System.Drawing.Size(1280, 800);
                StaThread.Settle();
                Pump();
                watch.Start();

                var raceControl = (RaceControlControl)typeof(MainForm).GetField("_raceControlControl", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var opens = new (string Name, Action Open)[]
                {
                    ("Inbox", () => window.ShowInbox(InboxFilter.All)),
                    ("Mission Control", window.ShowMissionControl),
                    ("Announcer screen", () => Call(window, "OpenAnnouncerWindow")),
                    ("Broadcast", () => Call(raceControl, "ShowBroadcastWindow")),
                    ("Big Screen", raceControl.TriggerOpenBigScreen),
                    ("Photo Finish", window.ShowPhotoFinish),
                };

                foreach (var (name, open) in opens)
                {
                    var before = Application.OpenForms.Cast<Form>().ToHashSet();
                    watch.Where = name;
                    open();
                    Pump();

                    foreach (var opened in Application.OpenForms.Cast<Form>().Where(f => !before.Contains(f)).ToList())
                    {
                        // Off screen, as the main window is, before anything is picked in it.
                        opened.Location = new System.Drawing.Point(-20000, -20000);
                        SweepInside(opened, name, watch);
                    }

                    if (name == "Inbox")
                    {
                        foreach (var filter in Enum.GetValues<InboxFilter>())
                        {
                            watch.Where = "Inbox > " + filter;
                            window.ShowInbox(filter);
                            Pump();
                        }

                        watch.Where = "Inbox docked";
                        window.DockInboxFromRaceControl(true);
                        Pump();
                        watch.Where = "Inbox a window again";
                        window.DockInboxFromRaceControl(false);
                        Pump();
                    }
                }

                return watch.Found.ToList();
            }
            finally
            {
                watch.Stop();

                foreach (var open in Application.OpenForms.Cast<Form>().Where(f => !ReferenceEquals(f, window)).ToList())
                {
                    open.Close();
                }

                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(5), () => "opening each window for an uncovered native scroll bar");

        Assert.True(found.Count(f => !f.StartsWith("    ", StringComparison.Ordinal)) == 0, "A native scroll bar showed with no slim bar over it:\n" + string.Join("\n", found));
    }

    /// <summary>
    /// Practice's Expand and Restore (24 Nov 2026, Asher: "when restoring the
    /// screen on practice I see native scroll bars flash during the
    /// animation"): setting the fold up lays the page out at its start and
    /// its end, and at its end the page's scrolling panel turned Windows' own
    /// bar on, drawn on screen. Pressed twice each way, nothing flashes.
    /// </summary>
    [Fact]
    public void Practice_s_Expand_and_Restore_show_no_native_scroll_bar()
    {
        Seed();

        var found = StaThread.Run(() =>
        {
            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-20000, -20000) };
            using var watch = new NativeBarWatch();

            try
            {
                window.Show();
                window.ClientSize = new System.Drawing.Size(1280, 800);
                StaThread.Settle();
                typeof(MainForm).GetMethod("GoToPage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { "Practice" });
                Pump();

                var page = All(window).OfType<PracticeModeControl>().First();
                var expand = (Button)typeof(PracticeModeControl).GetField("_btnExpand", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

                watch.Start();

                foreach (var round in new[] { "first", "second" })
                {
                    watch.Where = "Practice, Expand, " + round + " time";
                    expand.PerformClick();
                    Pump();
                    watch.Where = "Practice, Restore, " + round + " time";
                    expand.PerformClick();
                    Pump();
                }

                return watch.Found.ToList();
            }
            finally
            {
                watch.Stop();
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(3), () => "Practice's Expand and Restore");

        Assert.True(found.Count(f => !f.StartsWith("    ", StringComparison.Ordinal)) == 0, "A native scroll bar showed with no slim bar over it:\n" + string.Join("\n", found));
    }

    private static void Call(object on, string method) =>
        on.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(on, null);

    /// <summary>
    /// Enough to make the lists scroll: forty drivers. Or, with
    /// HC_FLASH_DATA set to a data folder (a copy of %AppData%HeatCaller),
    /// that club's own files - which is how the Sounds and Race Analytics
    /// cases in the plan were found. Its JSON files and photo folders are
    /// copied into the test's own folder; the original is never written.
    /// </summary>
    private void Seed()
    {
        if (Environment.GetEnvironmentVariable("HC_FLASH_DATA") is { Length: > 0 } from && Directory.Exists(from))
        {
            foreach (var file in Directory.GetFiles(from, "*.json"))
            {
                File.Copy(file, _data.PathOf(Path.GetFileName(file)), overwrite: true);
            }

            foreach (var dir in new[] { "DriverPhotos", "CarPhotos", "PhonePhotos", "Sponsors" })
            {
                if (!Directory.Exists(Path.Combine(from, dir)))
                {
                    continue;
                }

                Directory.CreateDirectory(_data.PathOf(dir));

                foreach (var file in Directory.GetFiles(Path.Combine(from, dir)))
                {
                    File.Copy(file, Path.Combine(_data.PathOf(dir), Path.GetFileName(file)), overwrite: true);
                }
            }

            var settings = AppSettings.Load();
            settings.StartMaximized = false;
            settings.AutoBackupEnabled = false;
            settings.ConnectDecodersAtStartup = false;
            settings.Save();
            return;
        }

        new AppSettings
        {
            StartMaximized = false,
            HasCompletedFirstRun = true,
            HasSeenRaceDaySetup = true,
            AutoBackupEnabled = false,
            ConnectDecodersAtStartup = false,
            LastDriverProfileTransponder = "1000",
        }.Save();

        var profiles = ProfileStore.Load();

        for (var i = 0; i < 40; i++)
        {
            profiles.Upsert(new DriverProfile { Transponder = (1000 + i).ToString(), DriverName = "Driver " + i, RaceNumber = i.ToString() });
        }
    }

    private static void Pump()
    {
        // Past two ticks of the slim bars' 150 ms timer, so a step starts
        // with every bar where it settles.
        for (var i = 0; i < 40; i++)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void Sweep(MainForm window, NativeBarWatch watch)
    {
        var go = typeof(MainForm).GetMethod("GoToPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var navs = (System.Collections.IEnumerable)typeof(MainForm).GetField("_navItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

        foreach (var nav in navs.Cast<object>().ToList())
        {
            var name = (string)nav.GetType().GetProperty("Name")!.GetValue(nav)!;
            var page = (Control)nav.GetType().GetProperty("Page")!.GetValue(nav)!;

            watch.Where = name;
            go.Invoke(window, new object[] { name });
            Pump();

            SweepInside(page, name, watch);
        }
    }

    /// <summary>Every tab of every tab control in <paramref name="root"/>, and what is inside each; then every strip of tabs drawn by the app itself.</summary>
    private static void SweepInside(Control root, string where, NativeBarWatch watch)
    {
        foreach (var tabs in Outermost<TabControl>(root))
        {
            if (!tabs.Visible)
            {
                continue;
            }

            var first = tabs.SelectedTab;

            foreach (TabPage tab in tabs.TabPages)
            {
                if (ReferenceEquals(tab, first))
                {
                    continue;
                }

                watch.Where = where + " > " + tab.Text;
                tabs.SelectedTab = tab;
                Pump();
                SweepInside(tab, watch.Where, watch);
            }

            if (first is not null && !first.IsDisposed)
            {
                watch.Where = where + " > " + first.Text;
                tabs.SelectedTab = first;
                Pump();
                SweepInside(first, watch.Where, watch);
            }
        }

        // The app's own tab strips: Race Day's Setup (SegmentTabs) and the
        // driver's card on Driver Hub (UnderlineTabs).
        foreach (var strip in All(root).Where(c => c.Visible && c.GetType().Name is "SegmentTabs" or "UnderlineTabs").ToList())
        {
            var index = strip.GetType().GetProperty(strip.GetType().Name == "SegmentTabs" ? "SelectedIndex" : "Selected", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
            var count = strip.GetType().Name == "SegmentTabs"
                ? ((IReadOnlyList<string>)strip.GetType().GetProperty("Items")!.GetValue(strip)!).Count
                : 6;
            var start = (int)index.GetValue(strip)!;

            for (var i = 0; i < count; i++)
            {
                if (i == start)
                {
                    continue;
                }

                watch.Where = $"{where} > {strip.GetType().Name} {i}";

                try
                {
                    index.SetValue(strip, i);
                }
                catch (TargetInvocationException)
                {
                    // A strip with fewer tabs than asked (Driver Hub without Batteries).
                    break;
                }

                Pump();
            }

            index.SetValue(strip, start);
            Pump();
        }
    }

    private static IEnumerable<T> Outermost<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T found)
            {
                yield return found;
            }
            else
            {
                foreach (var inner in Outermost<T>(child))
                {
                    yield return inner;
                }
            }
        }
    }

    internal static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));

    /// <summary>
    /// The hook: every WM_NCPAINT, and every WM_PAINT of a grid's own scroll
    /// bar, sent on this thread while it runs.
    /// </summary>
    internal sealed class NativeBarWatch : IDisposable
    {
        private const int WhCallWndProc = 4;
        private const uint WmNcPaint = 0x0085;
        private const uint WmPaint = 0x000F;
        private const int GwlStyle = -16;
        private const int WsHScroll = 0x00100000;
        private const int WsVScroll = 0x00200000;

        private readonly HookProc _proc;
        private IntPtr _hook;

        public NativeBarWatch() => _proc = OnMessage;

        /// <summary>Where the sweep is - written into each finding.</summary>
        public string Where { get; set; } = string.Empty;

        /// <summary>Each finding once, in the order seen, each followed by where it was painted from.</summary>
        public List<string> Found { get; } = new();

        private readonly HashSet<string> _seen = new();

        public void Start() => _hook = SetWindowsHookEx(WhCallWndProc, _proc, IntPtr.Zero, GetCurrentThreadId());

        public void Stop()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        public void Dispose() => Stop();

        private IntPtr OnMessage(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0)
            {
                var message = Marshal.PtrToStructure<CwpStruct>(lParam);

                if (message.Message is WmNcPaint or WmPaint)
                {
                    try
                    {
                        Check(message.Hwnd, message.Message);
                    }
                    catch (Exception ex)
                    {
                        Note($"{Where}: the check itself failed - {ex.Message}");
                    }
                }
            }

            return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
        }

        private void Check(IntPtr hwnd, uint message)
        {
            // Not on screen: hidden, or under a parent whose painting is held.
            if (!IsWindowVisible(hwnd) || Control.FromHandle(hwnd) is not { IsDisposed: false } control)
            {
                return;
            }

            if (control.GetType().Name == "ThinScrollBar")
            {
                return;
            }

            Control target;
            bool vertical, horizontal;

            if (control is ScrollBar bar && control.Parent is DataGridView grid)
            {
                target = grid;
                vertical = bar is VScrollBar;
                horizontal = bar is HScrollBar;
            }
            else if (message == WmNcPaint)
            {
                target = control;
                var style = GetWindowLong(hwnd, GwlStyle);
                vertical = (style & WsVScroll) != 0;
                horizontal = (style & WsHScroll) != 0;
            }
            else
            {
                return;
            }

            if (vertical && !Covered(target, horizontal: false))
            {
                Note($"{Where}: {Describe(target)} vertical");
            }

            if (horizontal && !Covered(target, horizontal: true))
            {
                Note($"{Where}: {Describe(target)} horizontal");
            }
        }

        /// <summary>A finding, and - the first time - what was running when it painted, so a failure says where to look.</summary>
        private void Note(string what)
        {
            if (!_seen.Add(what))
            {
                return;
            }

            Found.Add(what);

            var frames = new System.Diagnostics.StackTrace().GetFrames()
                .Select(frame => frame.GetMethod())
                .Where(method => method?.DeclaringType is { } type
                    && type != typeof(NativeBarWatch)
                    && (type.Namespace == "HeatCaller" || type.Namespace?.StartsWith("System.Windows.Forms", StringComparison.Ordinal) == true))
                .Select(method => method!.DeclaringType!.Name + "." + method.Name)
                .Where(name => !name.StartsWith("NativeWindow.", StringComparison.Ordinal) && !name.Contains("WndProc", StringComparison.Ordinal))
                .Take(24);

            Found.Add("    painted from " + string.Join(" < ", frames));
        }

        /// <summary>A slim bar for this control and direction, on screen and over the native one.</summary>
        private static bool Covered(Control target, bool horizontal)
        {
            foreach (var bar in Bars(target))
            {
                var kind = bar.GetType();
                var barHorizontal = (bool)kind.GetField("_horizontal", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bar)!;

                if (barHorizontal != horizontal || !bar.IsHandleCreated || !IsWindowVisible(bar.Handle))
                {
                    continue;
                }

                // Over the strip the native bar is drawn in, as Windows has it now.
                var screen = target.RectangleToScreen(new System.Drawing.Rectangle(System.Drawing.Point.Empty, target.Size));
                var thickness = horizontal ? SystemInformation.HorizontalScrollBarHeight : SystemInformation.VerticalScrollBarWidth;
                var strip = horizontal
                    ? new System.Drawing.Rectangle(screen.Left, screen.Bottom - thickness, screen.Width, thickness)
                    : new System.Drawing.Rectangle(screen.Right - thickness, screen.Top, thickness, screen.Height);
                var over = bar.RectangleToScreen(bar.ClientRectangle);
                var middle = horizontal
                    ? new System.Drawing.Point(strip.Left + strip.Width / 2, strip.Top + strip.Height / 2)
                    : new System.Drawing.Point(strip.Left + strip.Width / 2, strip.Top + strip.Height / 2);

                if (over.Contains(middle))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<Control> Bars(Control target)
        {
            var host = target.Parent;

            while (host is TableLayoutPanel or FlowLayoutPanel)
            {
                host = host.Parent;
            }

            if (host is null)
            {
                yield break;
            }

            foreach (Control child in host.Controls)
            {
                if (child.GetType().Name == "ThinScrollBar"
                    && ReferenceEquals(child.GetType().GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(child), target))
                {
                    yield return child;
                }
            }
        }

        private static string Describe(Control target)
        {
            var path = new List<string>();

            for (var at = target; at is not null && path.Count < 4; at = at.Parent)
            {
                path.Add(at.GetType().Name + (string.IsNullOrEmpty(at.Name) ? string.Empty : "#" + at.Name));
            }

            return string.Join(" in ", path) + $" at {target.Width}x{target.Height}";
        }

        private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct CwpStruct
        {
            public IntPtr LParam;
            public IntPtr WParam;
            public uint Message;
            public IntPtr Hwnd;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
    }
}
