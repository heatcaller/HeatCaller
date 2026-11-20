using System.Runtime.InteropServices;

namespace HeatCaller;

/// <summary>
/// AppUsage's link to the window (19 Nov 2026, usage totals round 1): the
/// 15-second clock, whether HeatCaller is in front, how long since the
/// keyboard or mouse was used, and which tab a page is on.
///
/// Counts nothing while <see cref="UsageSharing.IsOn"/> is false: a club that
/// unticked the box (or one outside the US that never ticked it) has no
/// totals kept at all, not merely none sent. Unticking it also forgets what
/// was already counted. Nothing at all in the rehearsal copy.
///
/// DIALOGS. While a window from LayoutAudit.ConvertedWindows is the one in
/// front, its time goes to a row of its own, "Dialog: <its name in that
/// list>" - never the window's own title, which can hold a driver's or a
/// track's name. Any other window's time stays with the page beneath it.
///
/// SENDING (round 3). On the first tick and every 6 hours after, the
/// finished days go to the club's relay (AppUsageSender), and are deleted
/// here once it has them.
///
/// TABS. A page's tab strips (TabControl and SegmentTabs) are found once,
/// the first time the page is shown, and watched from then on. The tab
/// counted is the selected one on the first strip that is visible - the
/// page's own, not one nested inside it.
/// </summary>
internal sealed class AppUsageTracker : IDisposable
{
    /// <summary>How often the clock gives time to the page on screen.</summary>
    internal const int TickMs = 15_000;

    /// <summary>How often the totals are written to disk.</summary>
    private static readonly TimeSpan SaveEvery = TimeSpan.FromMinutes(5);

    /// <summary>How often finished days are offered to the relay (the first try is on the first tick).</summary>
    internal static readonly TimeSpan SendEvery = TimeSpan.FromHours(6);

    private readonly AppUsage _usage;
    private readonly AppSettings _settings;
    private readonly Func<AppUsage.Context> _context;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = TickMs };
    private readonly Dictionary<Control, List<Control>> _strips = new();
    private string? _page;
    private Control? _pageControl;
    private DateTime _lastSave = DateTime.Now;
    private bool _wasOn;
    private DateTime _lastSendTry = DateTime.MinValue;
    private string? _dialog;
    private bool _sending;

    /// <summary>The window's tracker, for <see cref="Note"/>; null before the window is built and after it closes.</summary>
    private static AppUsageTracker? _current;

    /// <summary>
    /// Counts one of AppUsage.Events from anywhere - a button deep in a page
    /// or a dialog - through the main window's tracker. Nothing when there is
    /// none (a test's control on its own) or the box is off.
    /// </summary>
    internal static void Note(string name)
    {
        try
        {
            _current?.Count(name);
        }
        catch (Exception)
        {
            // Counting is never worth an error.
        }
    }

    internal AppUsageTracker(AppUsage usage, AppSettings settings, Func<AppUsage.Context> context)
    {
        _current = this;
        _usage = usage;
        _settings = settings;
        _context = context;
        _wasOn = IsOn;
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>The totals, for See what's sent.</summary>
    internal AppUsage Usage => _usage;

    private bool IsOn => !AppPaths.IsRehearsal && UsageSharing.IsOn(_settings);

    /// <summary>A sidebar page has come on screen (MainForm.ShowPage).</summary>
    internal void PageShown(string name, Control page)
    {
        _page = name;
        _pageControl = page;

        if (!_strips.ContainsKey(page))
        {
            var found = new List<Control>();
            FindStrips(page, found);
            _strips[page] = found;

            foreach (var strip in found)
            {
                switch (strip)
                {
                    case TabControl tabs:
                        tabs.SelectedIndexChanged += (_, _) => TabChanged(page);
                        break;
                    case SegmentTabs segments:
                        segments.SelectedIndexChanged += (_, _) => TabChanged(page);
                        break;
                }
            }
        }

        Enter();
    }

    /// <summary>Counts one of AppUsage.Events.</summary>
    internal void Count(string name)
    {
        if (IsOn)
        {
            _usage.Count(name, DateTime.Now);
        }
    }

    /// <summary>Writes what's counted (closing, or the regular save).</summary>
    internal void Save()
    {
        var now = DateTime.Now;

        if (IsOn)
        {
            _usage.Flush(now, _context());
        }

        _usage.Save(now);
        _lastSave = now;
    }

    private void TabChanged(Control page)
    {
        if (ReferenceEquals(page, _pageControl))
        {
            Enter();
        }
    }

    private void Enter()
    {
        if (!IsOn || _page is null || _pageControl is null)
        {
            return;
        }

        if (_dialog is { } dialog)
        {
            _usage.Enter(DialogPrefix + dialog, "", DateTime.Now, _context());
            return;
        }

        _usage.Enter(_page, CurrentTab(_pageControl), DateTime.Now, _context());
    }

    /// <summary>What a dialog's row is called before its name.</summary>
    internal const string DialogPrefix = "Dialog: ";

    /// <summary>
    /// The window in front's name on LayoutAudit.ConvertedWindows - its title
    /// exactly, or the title starting with the name then " (" or " - " - or
    /// null for the main window, no window, or one not on the list.
    /// </summary>
    internal static string? DialogName(Form? active)
    {
        if (active is null or MainForm)
        {
            return null;
        }

        var title = active.Text ?? "";

        return LayoutAudit.ConvertedWindows.FirstOrDefault(name =>
            name.Length > 0
            && (string.Equals(title, name, StringComparison.Ordinal)
                || title.StartsWith(name + " (", StringComparison.Ordinal)
                || title.StartsWith(name + " - ", StringComparison.Ordinal)));
    }

    private void Tick()
    {
        var on = IsOn;
        var now = DateTime.Now;

        if (!on)
        {
            if (_wasOn)
            {
                // Unticked: forget it all, at once and on disk.
                _usage.Clear();
                _usage.Save(now);
            }

            _wasOn = false;
            return;
        }

        if (!_wasOn)
        {
            _wasOn = true;
            Enter();
            return;
        }

        var active = Form.ActiveForm;
        var inFront = active is not null;
        _usage.Mark(now, inFront, inFront && IdleFor() < AppUsage.ActiveWithin, _context());

        // A dialog from the list came in front, or went: its own row from here.
        var dialog = inFront ? DialogName(active) : _dialog;

        if (!string.Equals(dialog, _dialog, StringComparison.Ordinal))
        {
            _dialog = dialog;
            Enter();
        }

        if (now - _lastSave >= SaveEvery)
        {
            _usage.Save(now);
            _lastSave = now;
        }

        if (!_sending && now - _lastSendTry >= SendEvery)
        {
            _lastSendTry = now;
            _ = SendFinishedDaysAsync(now);
        }
    }

    /// <summary>
    /// Offers the finished days to the relay (AppUsageSender), on this
    /// thread, and saves when any went. Never throws.
    /// </summary>
    private async Task SendFinishedDaysAsync(DateTime now)
    {
        var target = AppUsageSender.Target(_settings);
        var whyNot = AppUsageSender.WhyNot(IsOn, AppPaths.IsRehearsal || AppPaths.IsRedirected, Terms.IsAccepted(), target is not null);

        if (whyNot is not null || target is not { } relay)
        {
            return;
        }

        _sending = true;

        try
        {
            var (_, days) = await AppUsageSender.SendAsync(_usage, relay, now, UsageMeters.Http);

            if (days > 0)
            {
                _usage.Save(DateTime.Now);
                _lastSave = DateTime.Now;
                SessionLog.Write($"[usage] Sent {days} day(s) of usage totals to the club's relay.");
            }
        }
        catch (Exception ex)
        {
            SessionLog.Write($"[usage] Sending the usage totals failed ({ex.GetType().Name}); they will be tried again.");
        }
        finally
        {
            _sending = false;
        }
    }

    private string CurrentTab(Control page)
    {
        if (!_strips.TryGetValue(page, out var strips))
        {
            return "";
        }

        foreach (var strip in strips)
        {
            if (!strip.Visible)
            {
                continue;
            }

            switch (strip)
            {
                case TabControl { SelectedTab: { } tab }:
                    return tab.Text;
                case SegmentTabs segments when segments.Items.Count > 0:
                    return segments.Items[segments.SelectedIndex];
            }
        }

        return "";
    }

    /// <summary>Every tab strip on a page, outermost first (breadth first).</summary>
    private static void FindStrips(Control page, List<Control> found)
    {
        var queue = new Queue<Control>();
        queue.Enqueue(page);

        while (queue.Count > 0)
        {
            var control = queue.Dequeue();

            if (control != page && control is TabControl or SegmentTabs)
            {
                found.Add(control);
            }

            foreach (Control child in control.Controls)
            {
                queue.Enqueue(child);
            }
        }
    }

    /// <summary>How long since the keyboard or mouse was last used anywhere on this PC.</summary>
    private static TimeSpan IdleFor()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };

        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        // Both are milliseconds since Windows started, and wrap together at
        // 49.7 days - unsigned subtraction is right across the wrap.
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.Time));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    public void Dispose()
    {
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        _timer.Stop();
        _timer.Dispose();
    }
}
