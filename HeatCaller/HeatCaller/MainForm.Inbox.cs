namespace HeatCaller;

/// <summary>
/// The Desk Inbox's one window (15 Nov 2026) - see <see cref="DeskInboxForm"/>.
/// Every button that opened Driver Requests, the club notes, the room vote
/// or Online Sign-ups opens it, on that button's chip; Race Control's Inbox
/// button on the flag row shows how many are waiting, red while any of them
/// holds up the next race.
/// </summary>
public partial class MainForm
{
    private DeskInboxForm? _inboxForm;
    private bool _inboxRaceHooked;

    /// <summary>The docked inbox's place down the right of the main window, and the splitter that sizes it - made the first time it docks.</summary>
    private Panel? _inboxDock;
    private Splitter? _inboxSplit;

    /// <summary>The window's own least size, kept while it is docked (where the dock's width decides).</summary>
    private Size _inboxLeast;

    /// <summary>Opens the inbox on a chip, or brings the open one forward on it.</summary>
    internal void ShowInbox(InboxFilter filter)
    {
        if (_inboxForm is { IsDisposed: false } open)
        {
            open.SetFilter(filter);

            if (!open.TopLevel)
            {
                // Docked: it is already on screen beside Race Control.
                open.Focus();
                return;
            }

            // Activate alone leaves a minimised window minimised - and the
            // button looking as if it did nothing.
            if (open.WindowState == FormWindowState.Minimized)
            {
                open.WindowState = FormWindowState.Normal;
            }

            open.Activate();
            return;
        }

        // The spare, already made and laid out in the hidden dock, when there
        // is one - see FocusView.SpareInboxWanted.
        if (TakeSpareInbox() is { } spare)
        {
            _inboxForm = spare;
            spare.Resting = false;
            HostInbox(spare, _settings.InboxDocked);

            // After it is on screen: the spare was shown once already, hidden,
            // so opening on the vote waits for nothing - SetFilter opens the
            // vote's card itself once the window is visible.
            spare.SetFilter(filter);
            return;
        }

        var form = BuildInbox();
        _inboxForm = form;
        form.SetFilter(filter);
        HostInbox(form, _settings.InboxDocked);
    }

    /// <summary>A new inbox, wired to the main window: what happens when it is closed, and its tick asking to be docked or not.</summary>
    private DeskInboxForm BuildInbox()
    {
        var form = new DeskInboxForm(
            _driverRequestDesk,
            _profiles,
            text => _announcer.SpeakTest(text, _voiceStore.Current.Speed),
            _checkInStore,
            _clubNotes,
            _roomVote,
            () => _raceControlControl?.CurrentVoteTarget(),
            _signupDesk,
            _settings,
            _raceClasses,
            _rentals,
            _undo);

        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_inboxForm, form))
            {
                _inboxForm = null;
            }

            HideInboxDock();
            ShowInboxDockedOnRaceControl();
            form.Dispose();

            // The next one made ready, once things are quiet.
            WantSpareInbox();
        };

        form.DockWanted += docked => HostInbox(form, docked);
        return form;
    }

    /// <summary>The spare inbox, made and laid out out of sight in the hidden dock - see FocusView.SpareInboxWanted.</summary>
    private DeskInboxForm? _spareInbox;

    /// <summary>The spare's own least size, kept from before it went into the dock.</summary>
    private Size _spareLeast;

    /// <summary>Tries now and then to make the spare, until there is one or none is wanted.</summary>
    private System.Windows.Forms.Timer? _spareTimer;

    /// <summary>The spare, made ready. For tests.</summary>
    internal bool SpareInboxReady => _spareInbox is { IsDisposed: false };

    /// <summary>
    /// Asks for a spare inbox to be made the next time it is quiet - checked
    /// every two seconds, so a race on the track is waited out.
    /// </summary>
    internal void WantSpareInbox()
    {
        if (_closingDown || IsDisposed || LayoutCheckMode || SpareInboxReady)
        {
            return;
        }

        _spareTimer ??= new System.Windows.Forms.Timer { Interval = 2000 };
        _spareTimer.Tick -= OnSpareTimerTick;
        _spareTimer.Tick += OnSpareTimerTick;
        _spareTimer.Start();
    }

    private void OnSpareTimerTick(object? sender, EventArgs e)
    {
        var raceOnTrack = _race.IsPracticeMode || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;

        if (_closingDown || IsDisposed || SpareInboxReady)
        {
            _spareTimer?.Stop();
            return;
        }

        // An inbox open: the spare is made after it is closed (its FormClosed asks again).
        if (_inboxForm is { IsDisposed: false })
        {
            _spareTimer?.Stop();
            return;
        }

        // Not yet - a race on, or somebody at the keyboard: tried again next tick.
        if (FocusView.SpareInboxWanted(SpareInboxReady, inboxOpen: false, raceOnTrack, _closingDown, AppUsageTracker.IdleFor()))
        {
            _spareTimer?.Stop();
            MakeSpareInbox();
        }
    }

    /// <summary>Where the spare waits: off the left of the window's client area, so never drawn, and switched off, so never tabbed into.</summary>
    private Panel? _spareParking;

    /// <summary>
    /// The spare made now: built as a docked inbox is, at the dock's size,
    /// and shown in its parking place - which makes its windows and lays it
    /// out - with nothing on screen changing. Its refresh then rests until it
    /// is used.
    ///
    /// Not in the hidden dock itself: inside a hidden parent WinForms counts
    /// every part as hidden too, and made 2 of the inbox's 89 parts and laid
    /// none of them out - the press was as slow as without a spare. Parked
    /// in a panel that is shown but placed outside the window's client area,
    /// all of it is made and laid out, and none of it is drawn.
    /// </summary>
    internal void MakeSpareInbox()
    {
        if (_closingDown || IsDisposed || !IsHandleCreated || SpareInboxReady || _inboxForm is { IsDisposed: false })
        {
            return;
        }

        var took = System.Diagnostics.Stopwatch.StartNew();
        var form = BuildInbox();

        try
        {
            var least = UiLayout.AtDpi(360);
            var size = new Size(
                FocusView.FocusedInboxWidth(_settings.InboxDockWidth, UiLayout.AtDpi(FocusView.FocusedInboxWidest), least),
                Math.Max(UiLayout.AtDpi(400), ClientSize.Height));

            if (_spareParking is not { IsDisposed: false })
            {
                _spareParking = new Panel { Enabled = false, TabStop = false };
                Controls.Add(_spareParking);
            }

            _spareParking.Bounds = new Rectangle(new Point(-size.Width - UiLayout.AtDpi(10000), 0), size);

            _spareLeast = form.MinimumSize;
            form.ShowDocked(true);
            PutInDock(form, _spareParking);
            form.Show();
            form.Resting = true;
            _spareInbox = form;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            // No spare, then: the next inbox is made when it is asked for, as before.
            _spareParking?.Controls.Remove(form);
            form.Dispose();
            return;
        }

        OnClientRawMessage($"{AppTime.LogStampNow()}  [view] A spare inbox made ready out of sight in {took.ElapsedMilliseconds} ms, so the next Focus or Dock Inbox only has to show it.");
    }

    /// <summary>The spare, handed over and forgotten - null when there is none.</summary>
    private DeskInboxForm? TakeSpareInbox()
    {
        var spare = _spareInbox;
        _spareInbox = null;

        if (spare is not { IsDisposed: false } || _spareParking is null || spare.Parent != _spareParking)
        {
            return null;
        }

        // Out of its parking place - its windows are kept, as any part's are
        // when it changes parent - with its own least size back, which
        // HostInbox keeps for when it is a window of its own.
        spare.Hide();
        _spareParking.Controls.Remove(spare);
        spare.MinimumSize = _spareLeast;
        return spare;
    }

    /// <summary>
    /// Puts the inbox in the dock down the right of the main window, or back
    /// in a window of its own. The same window either way - its list, its
    /// selection and anything half-typed stay as they are.
    /// </summary>
    private void HostInbox(DeskInboxForm form, bool docked)
    {
        if (form.IsDisposed || _closingDown)
        {
            return;
        }

        // WHY (24 Nov 2026, scroll bar flash lookback): the dock taking its
        // room, or giving it back, lays the whole window out again on
        // screen - Race Control narrower, the inbox at the dock's width -
        // and each turned Windows' own white scroll bar on for a frame
        // before the slim bars covered it. Held through the move, and drawn
        // once - see ShowWithoutFlash.
        ShowWithoutFlash.Hold(this, () => MoveInbox(form, docked));

        form.ShowDocked(docked);
        ShowInboxDockedOnRaceControl();
    }

    /// <summary>The move itself, for HostInbox.</summary>
    private void MoveInbox(DeskInboxForm form, bool docked)
    {
        if (docked)
        {
            if (!form.TopLevel && form.Parent == _inboxDock)
            {
                return;
            }

            EnsureInboxDock();
            _inboxLeast = form.MinimumSize;
            PutInDock(form, _inboxDock!);
            _inboxSplit!.Visible = true;
            _inboxDock!.Visible = true;
            form.Show();
        }
        else
        {
            if (!form.TopLevel)
            {
                form.Hide();
                _inboxDock?.Controls.Remove(form);
                HideInboxDock();
                form.Dock = DockStyle.None;
                form.TopLevel = true;
                form.FormBorderStyle = FormBorderStyle.Sizable;
                form.MinimumSize = _inboxLeast;
            }

            if (!form.Visible)
            {
                form.Show(this);
            }
        }
    }

    /// <summary>
    /// The inbox, hidden, made a part of <paramref name="dock"/> that fills
    /// it. The caller shows it.
    ///
    /// WHY (23 Nov 2026, Asher: "when redocking a floating inbox it doesn't
    /// go to the proper format"): a maximised inbox window went into the
    /// dock still maximised, and a maximised window keeps its full-screen
    /// size whatever it is docked in - 1516 wide in a 520 dock, so it kept
    /// the wide three-column layout and ran off the right of the window.
    /// Only Race Control's Dock Inbox button put the window back to normal
    /// first; the tick at the inbox's foot did not. Both come through here
    /// now, and it is put back to normal (from minimised too) before it
    /// moves.
    /// </summary>
    internal static void PutInDock(Form form, Control dock)
    {
        if (form.TopLevel && form.WindowState != FormWindowState.Normal)
        {
            form.WindowState = FormWindowState.Normal;
        }

        form.Hide();
        form.MinimumSize = Size.Empty;
        form.TopLevel = false;
        form.FormBorderStyle = FormBorderStyle.None;
        form.Dock = DockStyle.Fill;
        dock.Controls.Add(form);
    }

    /// <summary>The inbox is open and in the dock, not a window of its own.</summary>
    private bool InboxDockedOnScreen =>
        _inboxForm is { IsDisposed: false } open && InboxDockButton.DockedOnScreen(open: true, inItsOwnWindow: open.TopLevel);

    /// <summary>
    /// Race Control's Dock Inbox button reads Undock Inbox while the inbox is
    /// docked on screen - and Practice's (24 Nov 2026), the same button.
    /// </summary>
    private void ShowInboxDockedOnRaceControl()
    {
        _raceControlControl?.ShowInboxDocked(InboxDockedOnScreen);
        _practiceControl?.ShowInboxDocked(InboxDockedOnScreen);

        // And Race Control's Focus button (24 Nov 2026) - see MainForm.Focus.
        NoteFocusView();
    }

    /// <summary>
    /// Race Control's Dock Inbox button (22 Nov 2026, asked for by Asher):
    /// docks the inbox - opening it if it is closed - or makes it a window
    /// of its own again. The same saved choice as the tick at the inbox's
    /// foot, set before the window moves so the tick, following the window,
    /// has nothing left to ask for.
    /// </summary>
    internal void DockInboxFromRaceControl(bool dock)
    {
        if (_closingDown)
        {
            return;
        }

        if (_settings.InboxDocked != dock)
        {
            _settings.InboxDocked = dock;
            _settings.Save();
        }

        if (_inboxForm is not { IsDisposed: false } open)
        {
            // Closed: it opens where the choice just saved says.
            ShowInbox(InboxFilter.All);
            return;
        }

        // A minimised or maximised window is put back to normal on its way
        // into the dock - see PutInDock.
        HostInbox(open, dock);
    }

    /// <summary>
    /// The dock, made once. Added after the content and the sidebar, so it
    /// docks first: the rightmost strip, then the splitter, then the sidebar
    /// and the page in what is left.
    /// </summary>
    private void EnsureInboxDock()
    {
        if (_inboxDock is not null)
        {
            return;
        }

        var least = UiLayout.AtDpi(360);
        _inboxSplit = new Splitter { Dock = DockStyle.Right, Width = UiLayout.AtDpi(5), BackColor = AppTheme.Border, Visible = false, MinExtra = UiLayout.AtDpi(480), MinSize = least };
        _inboxDock = new Panel
        {
            Dock = DockStyle.Right,
            Width = Math.Clamp(_settings.InboxDockWidth, least, Math.Max(least, ClientSize.Width / 2)),
            BackColor = AppTheme.Background,
            Visible = false,
        };

        var dock = _inboxDock;
        _inboxSplit.SplitterMoved += (_, _) =>
        {
            _settings.InboxDockWidth = dock.Width;
            _settings.Save();
        };

        Controls.Add(_inboxSplit);
        Controls.Add(_inboxDock);
    }

    private void HideInboxDock()
    {
        if (_inboxDock is not null)
        {
            _inboxDock.Visible = false;
        }

        if (_inboxSplit is not null)
        {
            _inboxSplit.Visible = false;
        }
    }

    /// <summary>
    /// The count on Race Control's Inbox button: every request, club note
    /// and sign-up waiting, red while any of them holds up the next race.
    /// On the roster tick, on the UI thread.
    /// </summary>
    private void UpdateInboxBadge()
    {
        if (_closingDown)
        {
            return;
        }

        if (!_inboxRaceHooked)
        {
            _inboxRaceHooked = true;

            // Later (S) puts an item off until the race on now is over.
            _race.RaceCompleted += () =>
            {
                try
                {
                    if (_inboxForm is { IsDisposed: false } inbox)
                    {
                        inbox.BeginInvoke(inbox.RaceOver);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Closed between the check and the call.
                }
            };
        }

        IEnumerable<DriverRequest> requests = (IEnumerable<DriverRequest>?)_driverRequests?.Waiting ?? Array.Empty<DriverRequest>();
        var urgent = requests.Any(r => DeskInbox.RaceBlockingKind(r.Kind)
            || (r.Kind == DriverAsk.Query && _driverRequestDesk?.InProtestWindow(r) == true));
        var total = requests.Count() + _clubNotes.Counts().ToApprove + _signupDesk.Waiting.Count;

        _raceControlControl?.SetDeskWaiting(total, urgent);
    }
}
