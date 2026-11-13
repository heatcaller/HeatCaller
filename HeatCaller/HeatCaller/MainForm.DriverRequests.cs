namespace HeatCaller;

/// <summary>
/// Drivers' own requests on the Main Window (22 Oct 2026, driver
/// experience): the store and the desk made at start-up, the relay publisher
/// told where to file what it collects, the phone's answers handed to the
/// driver's live document (_askAnswersFor, read by MainForm.DriverLive), and
/// the Driver Requests button on Who's Racing kept up to date on the roster
/// tick.
///
/// Wired before the pages are built (see the hooks in MainForm.cs), so the
/// Entries page's button is joined up on the first roster tick that finds it.
/// </summary>
public partial class MainForm
{
    private DriverRequestStore? _driverRequests;
    private DriverRequestDesk? _driverRequestDesk;
    private DriverRequestsForm? _driverRequestsForm;

    /// <summary>Whether the Entries page's button has been joined to the window yet.</summary>
    private bool _driverRequestsButtonWired;

    /// <summary>The store's version last shown on the button, so the tick does nothing while nothing changed.</summary>
    private int _driverRequestsVersionShown;

    /// <summary>Whether the button was last shown or hidden, so a change of the setting is picked up.</summary>
    private bool _driverRequestsShownAs;

    /// <summary>The Driver Hub's Setups tab, redrawn when a phone files a setup change. Set as the page is built.</summary>
    private Action? _driverHubSetupsRefresh;

    /// <summary>A phone filed a setup change: the hub shows it at once if it is open on that driver.</summary>
    private void SetupLogChangedByPhone()
    {
        try
        {
            _driverHubSetupsRefresh?.Invoke();
        }
        catch
        {
            // The setup is filed; the hub shows it next time it is opened.
        }
    }

    /// <summary>Requests already mentioned in the console, so each is announced once.</summary>
    private HashSet<string>? _driverRequestsAnnounced;

    partial void WireDriverRequests()
    {
        var store = DriverRequestStore.Load();

        _driverRequests = store;
        _driverRequestDesk = new DriverRequestDesk(
            store,
            _settings,
            _profiles,
            _batteryPacks,
            _sessionHistory,
            _undo,
            OnClientRawMessage,

            // Read on the UI thread; the map is swapped whole (RebuildBadgeIds).
            badge => _relayBadgeIds.TryGetValue(badge, out var transponder) ? transponder : null)
        {
            RelayProblem = () => _relayPublisher.DriverRequestsProblem,

            // A setup change from a phone files itself into the Garage's log
            // (4 Nov 2026) - see DriverRequestDesk.FileSetup.
            Setups = _setupLog,
            Cars = _cars,
            EventId = () => _raceEvents.Active?.Id,
            TrackName = () => RunningTrackName(_settings.LastRaceControlTrackName),

            // Their packs, cars, photos and sheets from the phone (5 Nov
            // 2026) - see DriverRequests.Garage.
            ChassisTypes = _chassisTypes,
            Sheets = _chassisTemplates,

            // "I'm coming" from the Me page (12 Nov 2026, setup plan C1).
            PreEntries = _preEntries,
            AnnouncedNight = () => _settings.NextNight,

            // More time, or skip my race, from a driver's phone - done in
            // Race Control once the desk approves (11 Nov 2026).
            RaceAsk = (kind, transponder, name) => _raceControlControl is not { } raceControl
                ? "Race Control isn't open, so nothing was changed."
                : kind == DriverAsk.Late
                    ? raceControl.GiveDriverMoreTime(transponder, name)
                    : raceControl.SkipDriverInNextRace(transponder, name),
        };

        // Marshals from their phones (12 Nov 2026, setup plan step 8).
        WireMarshalsFromPhones(_driverRequestDesk);

        _driverRequestDesk.GarageChangedByPhone += () =>
        {
            SetupLogChangedByPhone();
            GarageChanged();
        };

        // Filed on the publisher's own thread - the store is thread-safe.
        _relayPublisher.TonightsDriverRequests = () => store;

        // For the phone: what the desk has answered (driverlive asks).
        _askAnswersFor = badge => store.AnswersFor(badge, DateTime.UtcNow);

        // Those already waiting from before a restart aren't new.
        _driverRequestsAnnounced = new HashSet<string>(store.Waiting.Select(r => r.Id), StringComparer.Ordinal);
        _driverRequestsVersionShown = -1;

        if (store.LoadedUnreadable)
        {
            OnClientRawMessage("[driver requests] driver-requests.json couldn't be read, so drivers' requests are left on the club's website "
                + "until HeatCaller is restarted and can read it. Close anything that might have it open (a backup or sync tool is the usual cause).");
        }
    }

    partial void RosterTickDriverRequests()
    {
        if (_driverRequests is not { } store || _closingDown)
        {
            return;
        }

        if (!_driverRequestsButtonWired && _checkInControl is { } entries)
        {
            entries.DriverRequestsRequested += ShowDriverRequests;
            _driverRequestsButtonWired = true;
            _driverRequestsVersionShown = -1;
        }

        // Packs, cars, photos and setups from phones are applied by
        // themselves (5 Nov 2026), so they never wait on the desk's list.
        // Guarded: the roster tick must run.
        try
        {
            _driverRequestDesk?.FileWaitingWrites();
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[driver requests] A change from a phone couldn't be applied: {ex.Message}");
        }

        // The Desk button on Race Control's flag row (7 Nov 2026) - every
        // tick, before the "nothing changed" return below: it does nothing
        // itself while the count is the same, and it must catch the first
        // tick after Race Control is built.
        _raceControlControl?.SetDeskWaiting(store.WaitingCount);

        var shown = _settings.RelayDriverRequests || store.WaitingCount > 0;
        var version = store.Version;

        if (version == _driverRequestsVersionShown && shown == _driverRequestsShownAs)
        {
            return;
        }

        _driverRequestsVersionShown = version;
        _driverRequestsShownAs = shown;

        var waiting = store.Waiting;
        _checkInControl?.SetDriverRequestCount(waiting.Count, shown);

        // A console line when new ones arrive. No names: the session log
        // goes into support bundles (as RelaySignupDesk).
        var fresh = waiting.Count(r => _driverRequestsAnnounced!.Add(r.Id));

        if (fresh > 0)
        {
            OnClientRawMessage($"[driver requests] {(fresh == 1 ? "A driver has" : $"{fresh} drivers have")} sent a request from their phone. "
                + "Approve or decline it under Driver Requests on Race Day → Who's Racing.");
        }
    }

    /// <summary>Opens Driver Requests, or brings it forward if it is already open - as Online Sign-ups.</summary>
    private void ShowDriverRequests()
    {
        if (_driverRequestDesk is not { } desk)
        {
            return;
        }

        if (_driverRequestsForm is { IsDisposed: false } open)
        {
            // Activate alone leaves a minimised window minimised - and the
            // Desk click looking as if it did nothing.
            if (open.WindowState == FormWindowState.Minimized)
            {
                open.WindowState = FormWindowState.Normal;
            }

            open.Activate();
            return;
        }

        var form = new DriverRequestsForm(desk, _profiles, text => _announcer.SpeakTest(text, _voiceStore.Current.Speed), _checkInStore);
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_driverRequestsForm, form))
            {
                _driverRequestsForm = null;
            }

            form.Dispose();
        };

        _driverRequestsForm = form;
        form.Show(this);
    }
}
