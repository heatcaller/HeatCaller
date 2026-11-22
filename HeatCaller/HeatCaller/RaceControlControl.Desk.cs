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

    /// <summary>The red of the count badge - while anything waiting holds up the next race.</summary>
    internal static readonly Color DeskBadgeRed = Color.FromArgb(0xE5, 0x39, 0x35);

    /// <summary>The badge with nothing race-blocking waiting: dark on the cyan, so red still means "now".</summary>
    internal static readonly Color DeskBadgeCalm = Color.FromArgb(0x10, 0x2A, 0x43);

    /// <summary>Inbox: opens the Desk Inbox. Only on the row while anything is waiting - see SetDeskWaiting.</summary>
    private readonly FlagButton _btnBarDesk = new()
    {
        // "Inbox" since 15 Nov 2026 (Asher: "change the button on race
        // control to read Inbox") - "Requests" from 14 Nov, "Desk" before.
        Text = "Inbox",
        FlagColour = DeskCyan,
        FlagText = Color.FromArgb(8, 28, 36),
        Lit = true,
        Visible = false,
        AccessibleName = "Inbox",
    };

    /// <summary>Whether anything waiting holds up the next race - the badge is red then.</summary>
    private bool _deskUrgent;

    /// <summary>How many requests the badge shows - 0 while the button is off the row.</summary>
    private int _deskWaiting;

    /// <summary>Inbox on the flag row was clicked: the host opens the Desk Inbox.</summary>
    internal event Action? DeskRequested;

    /// <summary>The announcer strip's Notes or Votes was clicked: the host opens the Desk Inbox on that chip.</summary>
    internal event Action<InboxFilter>? InboxRequested;

    /// <summary>
    /// DOCK INBOX (22 Nov 2026, asked for by Asher: "make a button next to
    /// expand on race control to dock the inbox"). At the foot of the board,
    /// beside Expand: one press puts the inbox in the dock down the right of
    /// the main window - opening it if it is closed - and it then reads
    /// Undock Inbox, which makes it a window of its own again. Before, it
    /// could only be docked from the tick at the inbox's own foot, and the
    /// flag row's Inbox button is only there while something is waiting.
    /// The rules are in <see cref="InboxDockButton"/>.
    ///
    /// A PICTURE, NOT WORDS (22 Nov 2026, Asher's pick of three): the row
    /// it stands on was made tight to stay "on one level" (15 Nov), and at
    /// a 1280-wide window it has 46 pixels left where "Dock Inbox" needs
    /// 120 - in words the button dropped to a second row and the board lost
    /// a driver's row. So it is a square with a window drawn in it, a panel
    /// down its right-hand side - filled while the inbox is docked - and
    /// the words are its tooltip and its name for a screen reader.
    /// </summary>
    private readonly Button _btnDockInbox = new()
    {
        Text = string.Empty,
        AccessibleName = InboxDockButton.Caption(dockedOnScreen: false),
    };

    private readonly ToolTip _dockInboxTip = new();

    /// <summary>Dock Inbox was pressed: the host docks the inbox (true) or makes it a window of its own again (false).</summary>
    internal event Action<bool>? DockInboxRequested;

    /// <summary>The inbox is docked on screen now - told by MainForm whenever it moves, opens or closes.</summary>
    private bool _inboxDockedOnScreen;

    /// <summary>The Dock Inbox button - for the tests.</summary>
    internal Button DockInboxButton => _btnDockInbox;

    /// <summary>The inbox docked, undocked or closed, from anywhere - for Dock Inbox's caption.</summary>
    internal void ShowInboxDocked(bool dockedOnScreen)
    {
        _inboxDockedOnScreen = dockedOnScreen;

        var caption = InboxDockButton.Caption(dockedOnScreen);

        if (_btnDockInbox.AccessibleName != caption)
        {
            _btnDockInbox.AccessibleName = caption;
            _dockInboxTip.SetToolTip(_btnDockInbox, InboxDockButton.Tip(dockedOnScreen));
            _btnDockInbox.Invalidate();
        }
    }

    /// <summary>Wires Dock Inbox. Called once, by BuildLayout, before the button goes on its row.</summary>
    private void BuildDockInboxButton()
    {
        _btnDockInbox.Click += (_, _) => DockInboxRequested?.Invoke(InboxDockButton.DockOnPress(_inboxDockedOnScreen));
        _btnDockInbox.Paint += (_, e) => PaintDockInboxIcon(e.Graphics);

        AppTheme.StyleToolTip(_dockInboxTip, keep: true);
        _dockInboxTip.SetToolTip(_btnDockInbox, InboxDockButton.Tip(dockedOnScreen: false));
    }

    /// <summary>
    /// Dock Inbox as wide as it is tall. Its row sizes a button to its
    /// caption, and this one has none - left to the row it is a sliver. So
    /// whenever the row has sized it (on going in, and at a new text size)
    /// it is squared off again.
    /// </summary>
    private void SquareDockInboxButton()
    {
        if (_btnDockInbox.Width != _btnDockInbox.Height)
        {
            _btnDockInbox.Width = _btnDockInbox.Height;
        }
    }

    /// <summary>The picture: a window with a panel down its right-hand side, filled while the inbox is docked there.</summary>
    private void PaintDockInboxIcon(Graphics g)
    {
        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var width = Math.Min(UiLayout.AtDpi(18), _btnDockInbox.Width - UiLayout.AtDpi(12));
        var height = width * 3 / 4;
        var box = new Rectangle((_btnDockInbox.Width - width) / 2, (_btnDockInbox.Height - height) / 2, width, height);
        var split = box.Right - width * 2 / 5;
        var ink = _btnDockInbox.Enabled ? _btnDockInbox.ForeColor : AppTheme.TextMuted;

        using (var pen = new Pen(ink, UiLayout.AtDpi(1) * 1.5f))
        using (var path = AppTheme.RoundedRect(box, UiLayout.AtDpi(2)))
        {
            if (_inboxDockedOnScreen)
            {
                using var fill = new SolidBrush(ink);
                var clip = g.Clip;
                g.SetClip(new Rectangle(split, box.Top, box.Right - split + 1, box.Height + 1));
                g.FillPath(fill, path);
                g.Clip = clip;
            }

            g.DrawPath(pen, path);
            g.DrawLine(pen, split, box.Top, split, box.Bottom);
        }

        g.Restore(saved);
    }

    /// <summary>The badge's colour: red while anything waiting holds up the next race, dark otherwise.</summary>
    internal static Color DeskBadgeColour(bool urgent) => urgent ? DeskBadgeRed : DeskBadgeCalm;

    /// <summary>Whether the badge is red - for the tests.</summary>
    internal bool DeskUrgent => _deskUrgent;

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
    internal void SetDeskWaiting(int count, bool urgent = false)
    {
        count = Math.Max(0, count);
        urgent = urgent && count > 0;

        if ((count == _deskWaiting && urgent == _deskUrgent) || IsDisposed || Disposing)
        {
            return;
        }

        var wasShown = _deskWaiting > 0;
        _deskWaiting = count;
        _deskUrgent = urgent;

        _btnBarDesk.AccessibleDescription = count == 0
            ? null
            : (count == 1 ? "One thing waiting in the inbox" : $"{count} things waiting in the inbox")
              + (urgent ? ", holding up the next race" : string.Empty);

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
        using (var fill = new SolidBrush(DeskBadgeColour(_deskUrgent)))
        {
            g.FillPath(fill, path);
        }

        g.Restore(saved);

        TextRenderer.DrawText(g, text, font, badge, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
            | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
}
