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
            form.Dispose();
        };

        form.DockWanted += docked => HostInbox(form, docked);

        _inboxForm = form;
        form.SetFilter(filter);
        HostInbox(form, _settings.InboxDocked);
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

        if (docked)
        {
            if (!form.TopLevel && form.Parent == _inboxDock)
            {
                return;
            }

            EnsureInboxDock();
            form.Hide();
            _inboxLeast = form.MinimumSize;
            form.MinimumSize = Size.Empty;
            form.TopLevel = false;
            form.FormBorderStyle = FormBorderStyle.None;
            form.Dock = DockStyle.Fill;
            _inboxDock!.Controls.Add(form);
            _inboxSplit!.Visible = true;
            _inboxDock.Visible = true;
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

        form.ShowDocked(docked);
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
