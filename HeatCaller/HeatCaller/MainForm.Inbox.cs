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

    /// <summary>Opens the inbox on a chip, or brings the open one forward on it.</summary>
    internal void ShowInbox(InboxFilter filter)
    {
        if (_inboxForm is { IsDisposed: false } open)
        {
            // Activate alone leaves a minimised window minimised - and the
            // button looking as if it did nothing.
            if (open.WindowState == FormWindowState.Minimized)
            {
                open.WindowState = FormWindowState.Normal;
            }

            open.SetFilter(filter);
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

            form.Dispose();
        };

        _inboxForm = form;
        form.SetFilter(filter);
        form.Show(this);
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
