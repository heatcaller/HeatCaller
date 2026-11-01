namespace HeatCaller;

/// <summary>
/// A driver's report card, as MainForm gathers it (22 Oct 2026) - see
/// <see cref="CoachReport"/>, which does all the arithmetic. This file only
/// fetches what it is worked out from: the saved sessions, the setup change
/// logged tonight for the car the driver ran, and the names other drivers
/// go by on the page it is for.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// The report card for one driver tonight, a line at a time - empty
    /// when they have not run tonight, or when anything it reads cannot be
    /// read. Never throws: a report card is a courtesy, and the callers are
    /// the night publisher on the filing path and the phone page.
    /// </summary>
    /// <param name="transponder">The driver's transponder.</param>
    /// <param name="publicNames">
    /// True for a page that goes online (the driver's own night on the
    /// relay): the other driver named in the benchmark line is named the
    /// way the club's public pages name people, shortened when "Shorten
    /// published names" is ticked. The driver's own night is private to
    /// them; the person they are being compared with is not the reader.
    /// </param>
    /// <param name="includeSetup">
    /// False for the phone coach page: anybody on the club's Wi-Fi can open
    /// any driver's card there, and what a driver changed on their car is
    /// theirs to share. The setup line goes only on their own night (keyed
    /// by their badge) and on the club's own screens.
    /// </param>
    /// <param name="night">The night to report on - tonight when null.</param>
    internal IReadOnlyList<string> CoachReportFor(string transponder, bool publicNames, bool includeSetup = true, DateTime? night = null)
    {
        try
        {
            var history = _sessionHistory.All;
            var thisNight = TonightAtTheTrack.NightOf(DateTime.Now);
            var reported = (night ?? thisNight).Date;

            return CoachReport.Lines(new CoachReport.Input(
                transponder,
                history,
                reported,
                utc => TonightAtTheTrack.NightOf(utc.ToLocalTime()),
                NamerFor(history, reported, publicNames),
                includeSetup ? CoachSetupChange(transponder, reported) : null,
                utc => utc.ToLocalTime().ToString("t", System.Globalization.CultureInfo.CurrentCulture),
                reported == thisNight ? "tonight" : "that night"));
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// How another driver is named on the card. On the club's own screens,
    /// by the name on their profile; online, through the same shortening the
    /// public results use, applied to everybody who raced tonight at once so
    /// two Sams stay told apart (see RelayPublisher.DisplayNames).
    /// </summary>
    private Func<string, string> NamerFor(IReadOnlyList<SessionRecord> history, DateTime night, bool publicNames)
    {
        string Full(string transponder) =>
            _profiles.Get(transponder)?.DriverName is { Length: > 0 } named ? named : string.Empty;

        if (!publicNames)
        {
            return Full;
        }

        var everyone = history
            .Where(s => TonightAtTheTrack.NightOf(s.StartedAtUtc.ToLocalTime()) == night)
            .SelectMany(s => s.Results)
            .Select(r => (r.Transponder ?? string.Empty).Trim())
            .Where(t => t.Length > 0 && !GhostService.IsGhost(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var shortened = RelayPublisher.DisplayNames(
            everyone.Select(t => RelayDocuments.PublicNameOrUnnamed(Full(t), t)).ToList(),
            _settings.RelayAbbreviateNames,
            everyone.Select(t => (string?)t).ToList());

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < everyone.Count; i++)
        {
            map[everyone[i]] = shortened[i];
        }

        return transponder => map.TryGetValue(transponder, out var name) ? name : string.Empty;
    }

    /// <summary>
    /// The newest setup logged THAT NIGHT for a car this driver was in on the
    /// day - their own, or one lent to them for the race day - with what
    /// changed from the setup before it on that car. Null when nothing was
    /// logged tonight: a setup from last week is not a change they can see
    /// in tonight's laps.
    /// </summary>
    private CoachReport.SetupChange? CoachSetupChange(string transponder, DateTime night)
    {
        // WHY (22 Oct 2026): this always asked about the race day open NOW,
        // so a card opened from Race History for last Tuesday let tonight's
        // loan decide whose car it was - the borrower got the owner's
        // Tuesday setup change and the owner lost theirs. Now tonight asks
        // about the day in hand, and an earlier night asks about the race
        // day started that night (none = null, and DriverFor falls back to
        // the owner). A car remembers only one loan, so an old loan counts
        // only while the car still holds it - but tonight's never answers
        // for an earlier night. Same idea as RecapCarFactsFor.
        var eventId = night == TonightAtTheTrack.NightOf(DateTime.Now)
            ? _raceEvents.Active?.Id
            : _raceEvents.All
                .FirstOrDefault(e => !e.IsLegacy && TonightAtTheTrack.NightOf(e.StartedUtc.ToLocalTime()) == night)?.Id;

        var carIds = new HashSet<string>(
            _cars.All
                .Where(car => string.Equals(car.DriverFor(eventId), transponder, StringComparison.OrdinalIgnoreCase))
                .Select(car => car.Id),
            StringComparer.Ordinal);

        if (carIds.Count == 0)
        {
            return null;
        }

        var setups = _setupLog.All;

        var latest = setups
            .Where(entry => !string.IsNullOrEmpty(entry.CarId) && carIds.Contains(entry.CarId)
                            && TonightAtTheTrack.NightOf(entry.RecordedAtUtc.ToLocalTime()) == night)
            .OrderByDescending(entry => entry.RecordedAtUtc)
            .FirstOrDefault();

        if (latest is null)
        {
            return null;
        }

        // "Pinion 22 → 23, Front springs Soft → Medium" - or nothing when it
        // is the car's first setup, or nothing on the sheet changed.
        var changes = SetupDiff.SincePrevious(latest, setups, _chassisTemplates.Get)?
            .Where(change => change.OnSheet)
            .ToList();

        var what = changes is { Count: > 0 } ? SetupDiff.Describe(changes, max: 2) : null;

        return new CoachReport.SetupChange(latest.RecordedAtUtc, what);
    }

    /// <summary>
    /// Every driver who has run tonight, their card worked out again and
    /// handed to the phone coach page (WebServerService.PublishCoachCards).
    ///
    /// HERE, ON THE UI THREAD, and the page only ever reads the copy: the
    /// saved sessions, the Garage and the setup log are read on this thread
    /// everywhere else, and a request thread walking them while a race is
    /// filed is the "collection was modified" that blanked the live board
    /// once (see WebServerService.PublishState). Called when a race or a
    /// practice is filed - the only moment a card can change, apart from a
    /// setup logged, which the phone's card leaves out anyway - and once at
    /// start, so a laptop restarted mid-evening has them straight away.
    ///
    /// Full names and no setup line: the LAN pages name people the way the
    /// live board does, and any phone on the club's Wi-Fi can open any
    /// driver's card. See CoachReportFor's includeSetup.
    /// </summary>
    private void PublishCoachCards()
    {
        try
        {
            var tonight = TonightAtTheTrack.NightOf(DateTime.Now);

            var drivers = _sessionHistory.All
                .Where(s => !s.IsNotScored() && TonightAtTheTrack.NightOf(s.StartedAtUtc.ToLocalTime()) == tonight)
                .SelectMany(s => s.Results)
                .Select(r => (r.Transponder ?? string.Empty).Trim())
                .Where(t => t.Length > 0 && !GhostService.IsGhost(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var cards = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var who in drivers)
            {
                var card = CoachReportFor(who, publicNames: false, includeSetup: false);

                if (card.Count > 0)
                {
                    cards[who] = card;
                }
            }

            _webServer.PublishCoachCards(cards);
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[web] Could not work out the drivers' report cards: {ex.Message}");
        }
    }

    /// <summary>
    /// A target lap set or cleared on Race Control's Coach menu (22 Oct
    /// 2026) is put on the phone coach page straight away: the live state
    /// carries each driver's target (WebServerService.AddFollowing), and
    /// without this a new target only showed after the driver's next lap
    /// rebuilt the document. DriverTargets raises Changed on whichever
    /// thread set it, so the rebuild is marshalled here. Inline the state is
    /// only ever built on the UI thread; with timing on its own thread it is
    /// also built on the live-state worker (24 Oct 2026, timing layer, P8 -
    /// see MainForm.LiveState.cs), and the two builds take turns under
    /// WebServerService's _stateGate (see BuildAndPublish).
    /// Unsubscribed when the window closes, the event being static.
    /// </summary>
    private void WatchDriverTargets()
    {
        void Publish()
        {
            if (!IsDisposed)
            {
                _webServer.PublishState(force: true);
            }
        }

        void OnTargetsChanged()
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(Publish));
                }
                else
                {
                    Publish();
                }
            }
            catch (InvalidOperationException)
            {
                // The window is going - nothing left to publish to.
            }
        }

        DriverTargets.Changed += OnTargetsChanged;
        FormClosed += (_, _) => DriverTargets.Changed -= OnTargetsChanged;
    }

    /// <summary>
    /// Race Control's Coach menu: "Chase Sam's best (ghost)" (22 Oct 2026).
    /// The same as the Practice page's Chase a Best Lap for that driver - an
    /// own-best ghost put on, or brought up to their latest best, ghost
    /// racing switched on, and one Undo to put the ghosts back. Saving the
    /// ghosts puts it on track in a running practice (OnGhostStoreSaved).
    /// </summary>
    /// <returns>What happened, for the console.</returns>
    private string? ChaseOwnBestFromRaceControl(string transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder) || GhostService.IsGhost(transponder))
        {
            return null;
        }

        var profile = _profiles.Get(transponder);
        var before = _ghostStore.Snapshot();

        var (ghost, refreshed) = OwnBestGhost.ChaseFor(_ghostStore.Current, transponder, profile?.DriverName, profile?.CarModel);

        var store = _ghostStore;
        _undo.Capture(refreshed ? $"Bring {ghost.Name} up to date" : $"Chase {ghost.Name}", () => store.Restore(before));

        _ghostStore.Save();

        return refreshed
            ? $"{ghost.Name} brought up to their latest best - beat that."
            : $"{ghost.Name} is on: a ghost at their own best lap on this layout. It runs in practice and races while ghost racing is on.";
    }
}
