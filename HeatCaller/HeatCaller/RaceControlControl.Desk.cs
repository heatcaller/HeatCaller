using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// THE DESK BUTTON ON THE FLAG ROW (7 Nov 2026, asked for by Asher: "add a
/// flag button in cyan that pops up when the desk has something to respond
/// to. have a red badge in it indicating how many. automatically adjust the
/// spacing the flag button row when it appears and disappears ... have it
/// open the window where you can take action when you click").
///
/// Drivers' requests from their phones wait for the desk (Driver Requests),
/// and the only sign of them was a count on Race Day → Who's Racing - a page
/// away from where the race director is all night. So while any are waiting,
/// a cyan Desk button stands on the flag row with the number in a red badge,
/// and a click opens Driver Requests. With none waiting it is not there.
///
/// Not one of the race's buttons (RaceBar): it follows the desk, not the
/// race, so the morphing bar never shows or hides it. When it comes and goes
/// the row is laid out again (FlagRow.Rearrange), so the buttons re-centre
/// and, where it makes the row wrap, the card grows or shrinks with it.
/// </summary>
public partial class RaceControlControl
{
    /// <summary>The cyan the Desk button wears - On Air's live cyan.</summary>
    internal static readonly Color DeskCyan = Color.FromArgb(0x2E, 0xE6, 0xFF);

    /// <summary>The red of the count badge.</summary>
    internal static readonly Color DeskBadgeRed = Color.FromArgb(0xE5, 0x39, 0x35);

    /// <summary>Desk: opens Driver Requests. Only on the row while requests are waiting - see SetDeskWaiting.</summary>
    private readonly FlagButton _btnBarDesk = new()
    {
        // "Requests" since 14 Nov 2026 (Asher) - it was "Desk".
        Text = "Requests",
        FlagColour = DeskCyan,
        FlagText = Color.FromArgb(8, 28, 36),
        Lit = true,
        Visible = false,
        AccessibleName = "Requests",
    };

    /// <summary>How many requests the badge shows - 0 while the button is off the row.</summary>
    private int _deskWaiting;

    /// <summary>Desk on the flag row was clicked: the host opens Driver Requests.</summary>
    internal event Action? DeskRequested;

    /// <summary>The Desk button - for the tests.</summary>
    internal Button DeskButton => _btnBarDesk;

    /// <summary>What the badge says - for the tests.</summary>
    internal int DeskWaiting => _deskWaiting;

    /// <summary>Wires the Desk button. Called once, by BuildLayout, after the bar is made.</summary>
    private void BuildDeskButton()
    {
        _btnBarDesk.Click += (_, _) => DeskRequested?.Invoke();
        _btnBarDesk.Overlay = PaintDeskBadge;
    }

    /// <summary>
    /// How many requests are waiting at the desk. On the UI thread - the
    /// host's roster tick. Shows the button with its badge while any are
    /// waiting and takes it off the row when none are, laying the row out
    /// again either way. Does nothing when the number has not changed.
    /// </summary>
    internal void SetDeskWaiting(int count)
    {
        count = Math.Max(0, count);

        if (count == _deskWaiting || IsDisposed || Disposing)
        {
            return;
        }

        var wasShown = _deskWaiting > 0;
        _deskWaiting = count;

        _btnBarDesk.AccessibleDescription = count == 0
            ? null
            : count == 1 ? "One driver request waiting" : $"{count} driver requests waiting";

        _btnBarDesk.Invalidate();

        if (wasShown != count > 0)
        {
            _deskMoveOwed = true;
            MoveDeskIfOwed();
        }
    }

    /// <summary>The Desk button came or went while a hold was under way, and is still to be put on or taken off the row.</summary>
    private bool _deskMoveOwed;

    /// <summary>
    /// Puts the Desk button on the row, or takes it off, once nothing on the
    /// row is being held. Laying the row out moves the buttons along - under
    /// a held Red, Checkered or Reset that is the cursor leaving the button,
    /// which calls the hold off. So it waits for the let-go: every hold's
    /// Changed runs this again.
    /// </summary>
    private void MoveDeskIfOwed()
    {
        if (!_deskMoveOwed || IsDisposed || Disposing || !DeskMayMoveRow(_holds))
        {
            return;
        }

        _deskMoveOwed = false;

        var show = _deskWaiting > 0;

        if (_btnBarDesk.Visible != show)
        {
            _btnBarDesk.Visible = show;
            _flagRow?.Rearrange();
        }
    }

    /// <summary>Whether the row may be laid out again now: nothing on it is being held.</summary>
    internal static bool DeskMayMoveRow(IEnumerable<IShownHold> holds) => holds.All(h => h.Progress is null);

    /// <summary>The badge's words: the count, or "99+" past that.</summary>
    internal static string DeskBadgeText(int count) => count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The red badge at the button's top right, with the count in white.</summary>
    private void PaintDeskBadge(Graphics g, Rectangle bounds, Color ink, Color? halo)
    {
        if (_deskWaiting <= 0)
        {
            return;
        }

        var text = DeskBadgeText(_deskWaiting);
        var font = AppTheme.MicroFont;
        var size = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        // A circle for one digit, a pill for more - never taller than the
        // button can hold with a little room above and below.
        var height = Math.Min(Math.Max(size.Height + 2, 14), Math.Max(10, bounds.Height - 6));
        var width = Math.Max(height, size.Width + height / 2);
        var badge = new Rectangle(bounds.Right - width - 4, bounds.Top + 3, width, height);

        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var path = AppTheme.RoundedRect(badge, height / 2))
        using (var fill = new SolidBrush(DeskBadgeRed))
        {
            g.FillPath(fill, path);
        }

        g.Restore(saved);

        TextRenderer.DrawText(g, text, font, badge, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
            | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
}
