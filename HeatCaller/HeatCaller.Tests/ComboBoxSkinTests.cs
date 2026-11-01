using System.Runtime.InteropServices;

namespace HeatCaller.Tests;

/// <summary>
/// An editable drop-down (one you can type in, like Venue and Track layout
/// on Race Day &gt; Setup) keeps the theme's colours in its text box when it
/// is switched off. Asher, 1 Nov: after deleting a race day the page showed
/// the old "Before events" entry, which can't be edited, and both boxes went
/// bright white - a switched-off text box asks its drop-down for its colours
/// with a message nothing was answering, so Windows used its own white.
/// </summary>
public class ComboBoxSkinTests
{
    private const int WM_CTLCOLORSTATIC = 0x0138;
    private const int WM_CTLCOLOREDIT = 0x0133;

    private string _now = "starting";

    [Fact]
    public void ASwitchedOffTypedDropDownKeepsTheThemeInEveryLook()
    {
        var problems = StaThread.Run(() =>
        {
            var found = new List<string>();

            try
            {
                foreach (var look in Looks.All)
                {
                    _now = look.Name;
                    AppTheme.UseLook(look);

                    using var window = new OffScreenWindow { ClientSize = new Size(400, 120) };
                    var venue = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 250, Location = new Point(10, 10) };
                    venue.Items.AddRange(new object[] { "Home track", "Club hall" });
                    window.Controls.Add(venue);
                    AppTheme.StyleComboBox(venue);

                    window.Show();
                    StaThread.Settle();

                    // Switched on: WinForms answers the text box with the
                    // combo's own BackColor, which the theme set to Surface.
                    Check(found, venue, WM_CTLCOLOREDIT, venue.BackColor, $"{look.Name}, switched on");

                    // Switched off and empty, as on a closed day.
                    venue.Text = string.Empty;
                    venue.Enabled = false;
                    StaThread.Settle();
                    Check(found, venue, WM_CTLCOLORSTATIC, AppTheme.Background, $"{look.Name}, switched off");

                    // And the field the skin draws round it is the same colour.
                    if (ComboBoxSkin.FieldFill(venue).ToArgb() != AppTheme.Background.ToArgb())
                    {
                        found.Add($"{look.Name}, switched off: the field is {ComboBoxSkin.FieldFill(venue)}, not the page's background");
                    }

                    window.Close();
                }
            }
            finally
            {
                // Back to the look every other test paints with.
                AppTheme.UseLook(Looks.FamilyBase(Look.FamilyLight));
            }

            return found;
        }, TimeSpan.FromMinutes(2), () => _now);

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>
    /// Asks the drop-down for its text box's colours, as the text box itself
    /// does before it paints, and checks the background it is told to use.
    /// </summary>
    private static void Check(List<string> found, ComboBox combo, int message, Color expected, string where)
    {
        var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf<COMBOBOXINFO>() };

        if (!GetComboBoxInfo(combo.Handle, ref info) || info.hwndItem == IntPtr.Zero)
        {
            found.Add($"{where}: the drop-down has no text box to ask about");
            return;
        }

        using var picture = new Bitmap(8, 8);
        using var g = Graphics.FromImage(picture);
        var hdc = g.GetHdc();

        try
        {
            var brush = SendMessage(combo.Handle, message, hdc, info.hwndItem);
            var background = GetBkColor(hdc);

            if (brush == IntPtr.Zero)
            {
                found.Add($"{where}: nothing answered - Windows paints the box in its own colour");
            }

            if (background != ColorTranslator.ToWin32(expected))
            {
                found.Add($"{where}: the box is painted {ColorTranslator.FromWin32(background)}, not {expected}");
            }
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COMBOBOXINFO
    {
        public int cbSize;
        public RECT rcItem;
        public RECT rcButton;
        public int stateButton;
        public IntPtr hwndCombo;
        public IntPtr hwndItem;
        public IntPtr hwndList;
    }

    [DllImport("user32.dll")]
    private static extern bool GetComboBoxInfo(IntPtr hWnd, ref COMBOBOXINFO info);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern int GetBkColor(IntPtr hdc);

    /// <summary>A window shown off screen, invisible and never activated, as the layout pieces' tests use.</summary>
    private sealed class OffScreenWindow : Form
    {
        public OffScreenWindow()
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
