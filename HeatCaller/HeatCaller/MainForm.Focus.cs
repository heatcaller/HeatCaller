using System.Drawing.Imaging;

namespace HeatCaller;

/// <summary>
/// FOCUS (24 Nov 2026, asked for by Asher: "I want to have one button press
/// that expands the leaderboard and loads in the docked inbox with a click.
/// that should be animated in a way that looks like it costs $10,000";
/// option B of six, plans/race-control-foot-plan.md).
///
/// Race Control's Focus button narrows the sidebar, folds the column under
/// the clock and docks the inbox - opening it if it is closed - in one
/// press, and Restore puts back the view as it was (FocusView has the
/// rules). Its menu's Wide board only and Inbox only are what Expand and
/// the dock square did, sliding as they did.
///
/// Focus and Restore play on the FocusStage: a picture of the window now is
/// put over it, the window is changed underneath in one go, a picture of it
/// after is taken, and the stage plays from one to the other
/// (FocusChoreography). With Windows' "Show animations" off, or no window to
/// play over, it simply changes.
/// </summary>
public partial class MainForm
{
    /// <summary>The view before Focus, while it is still focused - see FocusView.Kept.</summary>
    private FocusViewState? _focusBefore;

    /// <summary>The saved "keep the inbox docked" choice before Focus, put back by Restore - see FocusView.DockSavedAfterRestore.</summary>
    private bool _focusDockSavedBefore;

    /// <summary>Set while Focus changes the view itself, so each step is not taken for a change by hand.</summary>
    private bool _applyingFocus;

    /// <summary>The stage while Focus or Restore plays.</summary>
    private FocusStage? _focusStage;

    private InboxPlace InboxPlaceNow() =>
        _inboxForm is not { IsDisposed: false } ? InboxPlace.Closed
        : InboxDockedOnScreen ? InboxPlace.Docked
        : InboxPlace.Window;

    private FocusViewState FocusViewNow() => new(_navIconsOnly, _settings.RaceControlSideFolded, InboxPlaceNow());

    /// <summary>
    /// The view has changed - the sidebar, the clock column or the inbox:
    /// what Focus remembers is dropped once the view is no longer focused,
    /// and the button's caption and menu follow.
    /// </summary>
    internal void NoteFocusView()
    {
        if (_applyingFocus || _raceControlControl is null)
        {
            return;
        }

        var now = FocusViewNow();
        _focusBefore = FocusView.Kept(_focusBefore, now);

        // No longer focused, by Restore or by hand: the dock goes back to the
        // club's own width, the splitter's.
        if (_focusNarrowedDock && !FocusView.IsFocused(now))
        {
            _focusNarrowedDock = false;

            if (_inboxDock is { } dock)
            {
                var least = UiLayout.AtDpi(360);
                dock.Width = Math.Clamp(_settings.InboxDockWidth, least, Math.Max(least, ClientSize.Width / 2));
            }
        }

        _raceControlControl.ShowFocusState(now);
    }

    /// <summary>Focus narrowed the dock to its widest for Focus; put back once the view is not focused.</summary>
    private bool _focusNarrowedDock;

    /// <summary>Focus, Restore, Wide board only or Inbox only, from Race Control.</summary>
    internal void FocusFromRaceControl(FocusAsk ask)
    {
        if (_closingDown || _focusStage is not null)
        {
            return;
        }

        var now = FocusViewNow();
        var focused = FocusView.IsFocused(now);
        var (target, before) = FocusView.After(ask, now, _focusBefore);

        if (ask == FocusAsk.Focus && !focused)
        {
            _focusDockSavedBefore = _settings.InboxDocked;
        }

        _focusBefore = before;

        if (ask != FocusAsk.Focus)
        {
            ApplyFocusView(target, slide: true, restoring: false);
            NoteFocusView();
            return;
        }

        OnClientRawMessage($"{AppTime.LogStampNow()}  [view] {(focused ? "Restore: the board and the inbox back as they were" : "Focus: the wide board and the inbox docked")}.");

        if (!TryPlayFocusStage(target, entering: !focused))
        {
            ApplyFocusView(target, slide: SystemInformation.UIEffectsEnabled && !LayoutCheckMode, restoring: focused);
            NoteFocusView();
        }
    }

    /// <summary>
    /// Changes the view to <paramref name="target"/>: the sidebar, the clock
    /// column, then the inbox. <paramref name="slide"/>: the sidebar and the
    /// column slide as they do by hand; false, at once (under the stage).
    /// </summary>
    /// <summary>How long after the press the last Focus stage was up and moving - for the log and the tests.</summary>
    internal long LastStageUpMs { get; private set; } = -1;

    /// <summary>Where the time to put the last stage up went: the picture before, the pieces, the stage's own start.</summary>
    internal string LastStageStartTimes { get; private set; } = string.Empty;

    /// <summary>How long each step of the last ApplyFocusView took - for the log and the tests.</summary>
    internal string LastFocusStepTimes { get; private set; } = string.Empty;

    private void ApplyFocusView(FocusViewState target, bool slide, bool restoring)
    {
        _applyingFocus = true;
        var steps = System.Diagnostics.Stopwatch.StartNew();
        var times = new List<string>();
        void Step(string what)
        {
            times.Add($"{what} {steps.ElapsedMilliseconds}");
            steps.Restart();
        }

        // Under the stage, the window and Race Control are laid out once, at
        // the end, rather than at each of the three steps - and (25 Nov 2026)
        // painted once, by the picture after, rather than by each step: the
        // sidebar, the column and the inbox each drew the whole window, which
        // nobody could see. See ShowWithoutFlash.HoldWhole.
        IDisposable? paint = null;
        IDisposable? body = null;

        if (!slide)
        {
            paint = ShowWithoutFlash.HoldWhole(this);
            SuspendLayout();
            _raceControlControl.SuspendLayout();
            body = _raceControlControl.HoldBodyLayout();
        }

        try
        {
            if (_navIconsOnly != target.NavNarrowed)
            {
                if (slide)
                {
                    SlideNavIconsOnly(target.NavNarrowed);
                }
                else
                {
                    SetNavIconsOnly(target.NavNarrowed);
                }
            }

            Step("sidebar");

            _raceControlControl.SetSideFolded(target.SideFolded, slide);
            Step("column");

            // Focused, the inbox docks no wider than the mockup's, so the board
            // comes out wider than it was - see FocusView.FocusedInboxWidth. Set
            // before the inbox goes in, so it is laid out once, at that width.
            if (FocusView.IsFocused(target))
            {
                EnsureInboxDock();

                if (_inboxDock is { } dock)
                {
                    var width = FocusView.FocusedInboxWidth(dock.Width, UiLayout.AtDpi(FocusView.FocusedInboxWidest), UiLayout.AtDpi(360));

                    if (dock.Width != width)
                    {
                        dock.Width = width;
                        _focusNarrowedDock = true;
                    }
                }
            }

            Step("dock width");

            if (InboxPlaceNow() != target.Inbox)
            {
                switch (target.Inbox)
                {
                    case InboxPlace.Docked:
                        DockInboxFromRaceControl(true);
                        break;

                    case InboxPlace.Window:
                        DockInboxFromRaceControl(false);
                        break;

                    default:
                        _inboxForm?.Close();
                        break;
                }
            }

            Step("inbox");

            if (restoring)
            {
                var saved = FocusView.DockSavedAfterRestore(target.Inbox, _focusDockSavedBefore);

                if (_settings.InboxDocked != saved)
                {
                    _settings.InboxDocked = saved;
                    _settings.Save();
                }
            }
        }
        finally
        {
            if (!slide)
            {
                _raceControlControl.ResumeLayout(true);
                ResumeLayout(true);

                // The clock column and the board once, at their final size.
                body?.Dispose();
                paint?.Dispose();
            }

            Step("layout");
            LastFocusStepTimes = string.Join(", ", times);

            _applyingFocus = false;
        }
    }

    /// <summary>
    /// Focus or Restore on the stage. False when it cannot be played - the
    /// window not on screen, Windows' animations off, the layout test, or no
    /// picture to be had - and nothing has been changed.
    /// </summary>
    private bool TryPlayFocusStage(FocusViewState target, bool entering)
    {
        if (LayoutCheckMode || !IsHandleCreated || !Visible || WindowState == FormWindowState.Minimized
            || !SystemInformation.UIEffectsEnabled || ClientSize.Width < 200 || ClientSize.Height < 200)
        {
            return false;
        }

        var timing = System.Diagnostics.Stopwatch.StartNew();

        List<(object Key, bool Folds, Rectangle At)> piecesBefore = new();
        FocusScene? scene = null;
        long pictureAt = 0, piecesAt = 0;

        // Up and moving at once, on its own thread - see FocusStage. Its
        // window is made while the picture before is taken here.
        var stage = FocusStage.Begin(RectangleToScreen(ClientRectangle), entering,
            toMain: action => BeginInvoke(action),
            done: () => _focusStage = null,
            take: () =>
            {
                var before = ClientPicture();
                pictureAt = timing.ElapsedMilliseconds;

                if (before is null)
                {
                    return null;
                }

                // Where things are now, and the sidebar's width after - its
                // two widths are fixed, so the stage can close it before the
                // change - and every piece of the page where it is now: each
                // card glides from here once its place after is known.
                var navFrom = SidebarEdge();
                var navTo = navFrom == 0 ? 0 : OnClient(pnlSidebar).Left + (target.NavNarrowed ? SidebarNarrowWidth : SidebarWidth);
                piecesBefore = FocusPiecesNow();
                piecesAt = timing.ElapsedMilliseconds;
                scene = new FocusScene(
                    NavFrom: navFrom,
                    NavTo: navTo,
                    Button: OnClient(_raceControlControl.FocusButton),
                    Pieces: FocusElastic.Pieces(piecesBefore, after: null),
                    Inbox: entering ? Rectangle.Empty : InboxOnClient(),
                    BoardTop: Rectangle.Empty);

                return (before, scene);
            });

        if (stage is null || scene is null)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [view] Focus played without its animation - the stage could not be put up.");
            return false;
        }

        var start = scene;
        _focusStage = stage;
        var upAt = timing.ElapsedMilliseconds;
        LastStageUpMs = upAt;
        LastStageStartTimes = $"picture {pictureAt}, pieces {piecesAt - pictureAt}, stage {upAt - piecesAt}";

        // Underneath, in one go, while the stage plays.
        ApplyFocusView(target, slide: false, restoring: !entering);
        NoteFocusView();
        var changedAt = timing.ElapsedMilliseconds;

        // Once the message loop has laid the window out: the picture after.
        BeginInvoke(new Action(() =>
        {
            if (_closingDown)
            {
                stage.CallOff();
                return;
            }

            RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow);

            var after = ClientPicture();

            if (after is null)
            {
                stage.CallOff();
                return;
            }

            stage.Arrive(after, start with
            {
                Pieces = FocusElastic.Pieces(piecesBefore, FocusPiecesNow()),
                Inbox = entering ? InboxOnClient() : start.Inbox,
                BoardTop = entering ? BoardTopOnClient() : Rectangle.Empty,
            });

            // How long each part took, for a slow press to be found in the log.
            OnClientRawMessage($"{AppTime.LogStampNow()}  [view] {(entering ? "Focus" : "Restore")} timings: stage up {upAt} ms, view changed {changedAt} ms ({LastFocusStepTimes}), picture after {timing.ElapsedMilliseconds} ms.");
        }));

        return true;
    }

    /// <summary>
    /// The page's pieces where they are now, for the Focus stage: the strip
    /// above Race Control with the page's title, then Race Control's own
    /// (RaceControlControl.FocusPieces). Hidden ones are there with an empty
    /// place, so a piece that goes or comes is matched to itself.
    /// </summary>
    private List<(object Key, bool Folds, Rectangle At)> FocusPiecesNow()
    {
        var pieces = new List<(object Key, bool Folds, Rectangle At)>();

        if (_raceControlControl.Parent is { } page && OnClient(page) is { IsEmpty: false } p
            && OnClient(_raceControlControl) is { IsEmpty: false } rc && rc.Top > p.Top)
        {
            pieces.Add((page, false, new Rectangle(p.Left, p.Top, p.Width, rc.Top - p.Top)));
        }

        foreach (var (piece, folds) in _raceControlControl.FocusPieces())
        {
            pieces.Add((piece, folds, OnClient(piece)));
        }

        return pieces;
    }

    /// <summary>The right-hand edge of the sidebar, in the client area - 0 with it hidden (Race Mode).</summary>
    private int SidebarEdge() => pnlSidebar.Visible && pnlSidebar.Width > 0 ? OnClient(pnlSidebar).Right : 0;

    /// <summary>The docked inbox and its splitter, in the client area - empty with nothing docked.</summary>
    private Rectangle InboxOnClient()
    {
        if (_inboxDock is not { Visible: true } dock)
        {
            return Rectangle.Empty;
        }

        var area = OnClient(dock);

        if (_inboxSplit is { Visible: true } split)
        {
            area = Rectangle.Union(area, OnClient(split));
        }

        return area;
    }

    /// <summary>The top edge of the leaderboard's card, in the client area.</summary>
    private Rectangle BoardTopOnClient()
    {
        var board = _raceControlControl.BoardScreenBounds;

        if (board.IsEmpty)
        {
            return Rectangle.Empty;
        }

        var onClient = RectangleToClient(board);
        return new Rectangle(onClient.Left, onClient.Top, onClient.Width, 1);
    }

    private Rectangle OnClient(Control control) =>
        control.IsHandleCreated && control.Visible ? RectangleToClient(control.RectangleToScreen(control.ClientRectangle)) : Rectangle.Empty;

    /// <summary>
    /// The client area as it is drawn now, every window in it - taken with
    /// PrintWindow from what Windows has composed, so it is right even with
    /// the stage over it. Null when it cannot be had.
    /// </summary>
    private Bitmap? ClientPicture()
    {
        const uint PW_CLIENTONLY = 0x1;
        const uint PW_RENDERFULLCONTENT = 0x2;

        Bitmap? picture = null;

        try
        {
            picture = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppRgb);

            using var g = Graphics.FromImage(picture);
            var hdc = g.GetHdc();
            bool ok;

            try
            {
                ok = PrintWindow(Handle, hdc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }

            if (!ok)
            {
                picture.Dispose();
                return null;
            }

            return picture;
        }
        catch
        {
            picture?.Dispose();
            return null;
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
