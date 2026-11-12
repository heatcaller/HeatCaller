using System.Runtime.InteropServices;

namespace HeatCaller.Tests;

/// <summary>
/// The wheel over a multiline box on a scrolling page (WheelPassThrough,
/// ControlSkins.cs). The Website page's first-night guide and Club updates
/// boxes kept every wheel turn, so the page stopped scrolling as soon as the
/// pointer reached them (11 Nov 2026). A box nobody is working in hands the
/// turn to the page; one that is being worked in - here a read-only one, which
/// counts as worked in without needing the keyboard focus a hidden test window
/// cannot be given - scrolls itself until it reaches its end, and then the
/// page carries on.
/// </summary>
public sealed class WheelPassThroughTests
{
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
    private const int Notch = 120;

    private static volatile string _now = "starting";

    [Fact]
    public void TheWheelOverABoxScrollsThePageUnlessTheBoxHasSomewhereToGo()
    {
        var problems = StaThread.Run(Check, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, "The wheel went to the wrong place:\n" + string.Join("\n", problems));
    }

    private static List<string> Check()
    {
        var problems = new List<string>();

        using var window = new QuietWindow { Size = new Size(500, 400) };

        var page = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        window.Controls.Add(page);

        var text = string.Join(Environment.NewLine, Enumerable.Range(1, 80).Select(i => $"Line {i} of the club's news"));

        TextBox Box(int top, bool readOnly) => new()
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            ReadOnly = readOnly,
            Text = text,
            Bounds = new Rectangle(10, top, 400, 120),
        };

        var typing = Box(300, readOnly: false);
        var log = Box(700, readOnly: true);

        page.Controls.Add(typing);
        page.Controls.Add(log);
        page.Controls.Add(new Panel { Bounds = new Rectangle(10, 1400, 10, 10) });

        // As a real page gets it, once built.
        AppTheme.ApplyRecursively(window);

        window.Show();
        StaThread.Settle();

        _now = "an editable box nobody is typing in";
        page.AutoScrollPosition = new Point(0, 200);
        StaThread.Settle();

        var before = page.VerticalScroll.Value;
        Wheel(typing, -Notch);

        if (page.VerticalScroll.Value <= before)
        {
            problems.Add($"{_now}: wheel down left the page at {page.VerticalScroll.Value} (was {before})");
        }

        if (FirstLine(typing) != 0)
        {
            problems.Add($"{_now}: the box scrolled itself, to line {FirstLine(typing)}");
        }

        _now = "a read-only box with lines below";
        page.AutoScrollPosition = new Point(0, 500);
        StaThread.Settle();

        before = page.VerticalScroll.Value;
        Wheel(log, -Notch);

        if (FirstLine(log) == 0)
        {
            problems.Add($"{_now}: wheel down did not scroll the box");
        }

        if (page.VerticalScroll.Value != before)
        {
            problems.Add($"{_now}: the page moved too, {before} to {page.VerticalScroll.Value}");
        }

        _now = "a read-only box back at its top";
        Wheel(log, Notch * 10);
        StaThread.Settle();

        if (FirstLine(log) != 0)
        {
            problems.Add($"{_now}: the box did not get back to its top (line {FirstLine(log)})");
        }

        before = page.VerticalScroll.Value;
        Wheel(log, Notch);

        if (page.VerticalScroll.Value >= before)
        {
            problems.Add($"{_now}: wheel up left the page at {page.VerticalScroll.Value} (was {before})");
        }

        return problems;
    }

    private static void Wheel(Control box, int delta)
    {
        var screen = box.PointToScreen(new Point(box.Width / 2, box.Height / 2));
        var wParam = (IntPtr)(delta << 16);
        var lParam = (IntPtr)((screen.Y << 16) | (screen.X & 0xFFFF));

        SendMessage(box.Handle, WM_MOUSEWHEEL, wParam, lParam);
        StaThread.Settle();
    }

    private static int FirstLine(TextBox box) => (int)SendMessage(box.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private sealed class QuietWindow : Form
    {
        public QuietWindow()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Opacity = 0d;
            Font = AppTheme.BodyFont;
            BackColor = AppTheme.Background;
        }

        protected override bool ShowWithoutActivation => true;
    }
}
