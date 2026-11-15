namespace HeatCaller;

/// <summary>
/// The drivers' live evening and their own details, on the relay
/// (22 Oct 2026, driver experience, items 2-4, 8 and 12-14).
///
/// Two private snapshots, keyed by badge id, built by <see cref="DriverLive"/>
/// from what this laptop already knows and sent the way every other
/// snapshot is (SendToRelayIfChanged - only when the string changed):
///
///  - driverlive: next race, marshal duty, transponder last heard, checked
///    in, first night, the desk's answers. Built every ten seconds, and at
///    once (on the next two-second tick) when the race changes state or a
///    race is filed. Its "at" is to the minute, so it is also sent once a
///    minute when nothing else changed: the relay stops serving it after
///    thirty minutes without one.
///  - driverme: each driver's own details for the "Your details" form.
///    Sent only when it changes.
///
/// GATED like the rest of the drivers' nights. driverlive needs the relay,
/// drivers' nights and a check-in key; driverme also needs online check-in
/// and driver requests. Closed, nothing is sent - the relay refuses both
/// with a 403 while its switch is off, and takes them down itself when it
/// is switched off - and what was last sent is forgotten, so reopening
/// sends both again rather than trusting a copy the relay threw away.
///
/// Fields here have no initializers: this is a part file (see MainForm.cs),
/// so the fixed intervals are properties rather than static fields.
/// UI thread throughout, except where marked.
/// </summary>
public partial class MainForm
{
    /// <summary>How often driverlive is rebuilt when nothing is happening.</summary>
    private static TimeSpan DriverLiveEvery => TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long after driver requests open before driverme is first sent.
    /// The relay refuses it (403) until it has been told requests are open,
    /// and that is told on the requests pass, after the snapshots - so a
    /// copy sent the same moment would be refused, and a refused snapshot
    /// holds up every snapshot behind it while the publisher backs off.
    /// </summary>
    private static TimeSpan DriverMeSettleFor => TimeSpan.FromSeconds(20);

    /// <summary>
    /// While driverme is new on the relay, its last copy is forgotten this
    /// often, so one the relay refused while it was still closed is sent
    /// again within seconds rather than at the ten-minute refresh.
    /// </summary>
    private static TimeSpan DriverMeEarlyForgetEvery => TimeSpan.FromSeconds(30);

    /// <summary>How long "new on the relay" lasts - see DriverMeEarlyForgetEvery.</summary>
    private static TimeSpan DriverMeEarlyFor => TimeSpan.FromMinutes(2);

    /// <summary>
    /// After that, how often driverme's last copy is forgotten anyway - the
    /// ten minutes PublishExtrasToRelay gives every settings-only document,
    /// for the reason it gives: queued is not taken.
    /// </summary>
    private static TimeSpan DriverMeForgetEvery => TimeSpan.FromMinutes(10);

    /// <summary>How long a driver's first-night answer is kept before it is worked out again.</summary>
    private static TimeSpan FirstNightKeptFor => TimeSpan.FromMinutes(10);

    /// <summary>How long the list of who raced tonight is kept, when no race has been filed meanwhile.</summary>
    private static TimeSpan RacedTonightKeptFor => TimeSpan.FromMinutes(1);

    /// <summary>When driverlive was last built.</summary>
    private DateTime _driverLiveBuiltUtc;

    /// <summary>When driverme was last built.</summary>
    private DateTime _driverMeBuiltUtc;

    /// <summary>The race state driverlive was last built for - a change rebuilds it at once.</summary>
    private RaceState _driverLiveRaceState;

    /// <summary>
    /// The drivers' requests store's version driverlive was last built for
    /// (11 Nov 2026): the desk answering a request rebuilds it at once, so
    /// the driver's phone hears back on the next tick, not up to 10s later.
    /// </summary>
    private int _driverLiveAsksVersion = -1;

    /// <summary>
    /// Rebuild on the next tick: a race filed, or the race changed state.
    /// Set from whichever thread raised that, hence volatile.
    /// </summary>
    private volatile bool _driverLiveDirty;

    /// <summary>The list of who raced tonight wants reading again - a race was filed. Any thread.</summary>
    private volatile bool _driverLiveRacedStale;

    /// <summary>Whether driverlive's gate was open on the last tick.</summary>
    private bool _driverLiveOpen;

    /// <summary>Whether driverme's gate was open on the last tick.</summary>
    private bool _driverMeOpen;

    /// <summary>When driverme's gate last opened.</summary>
    private DateTime _driverMeOpenedUtc;

    /// <summary>When driverme's last copy was last forgotten.</summary>
    private DateTime _driverMeForgotUtc;

    /// <summary>
    /// RelayPublisher.DriverAsksOpenedCount as last seen. The relay blanks
    /// driverme whenever requests close, and a relay that lost its switch is
    /// told "open" again by the publisher - so a move here means the copy
    /// last sent is gone and must be sent again (22 Oct 2026, driver
    /// experience).
    /// </summary>
    private int _driverMeAsksOpenedSeen;

    /// <summary>Who raced tonight (main transponders), and when that was read.</summary>
    private List<string>? _driverLiveRaced;
    private DateTime _driverLiveRacedReadUtc;

    /// <summary>First-night answers by main transponder, for the night they were worked out on.</summary>
    private Dictionary<string, bool>? _driverFirstNights;
    private DateTime _driverFirstNightsNight;
    private DateTime _driverFirstNightsReadUtc;

    /// <summary>The badge map inverted (main transponder to badge id), and the map it was made from.</summary>
    private Dictionary<string, string>? _driverLiveBadgeOf;
    private Dictionary<string, string>? _driverLiveBadgeOfSource;

    /// <summary>
    /// Once, at start-up. Only an event to listen to: the pages (Race
    /// Control, where the races ahead come from) are not built yet, so
    /// everything else waits for the roster tick.
    /// </summary>
    partial void WireDriverLive()
    {
        // Any thread: only a flag. Staged, green, finished - each changes
        // what a phone should say, so the next tick rebuilds at once rather
        // than up to ten seconds later.
        _race.StateChanged += _ => _driverLiveDirty = true;
        _driverLiveDirty = true;
    }

    /// <summary>A race was filed or corrected: who raced tonight changed, and so may everybody's next race.</summary>
    partial void RaceFiledDriverLive(SessionRecord session)
    {
        // Possibly off the UI thread (SessionAdded): flags only, and the
        // roster tick does the work.
        _driverLiveRacedStale = true;
        _driverLiveDirty = true;
    }

    /// <summary>Every two seconds, on the UI thread, after the other relay documents.</summary>
    partial void RosterTickDriverLive()
    {
        try
        {
            var now = DateTime.UtcNow;

            // R1's rule (22 Oct 2026): the relay throws a closed switch's
            // document away, so a copy "already sent" before it closed is
            // not on the relay when it reopens. Forgotten on every change
            // either way - closing too, so nothing sent while closed (and
            // refused) can count as sent either.
            var liveOpen = DriverLiveIsOpen();

            if (liveOpen != _driverLiveOpen)
            {
                _driverLiveOpen = liveOpen;
                _lastRelayDocuments.Remove(RelayChannel.DriverLive);
                _driverLiveDirty = true;

                if (!liveOpen)
                {
                    // A copy still queued would only be refused (22 Oct
                    // 2026, driver experience).
                    _relayPublisher.DropQueuedSnapshot(RelayChannel.DriverLive);
                }
            }

            var meOpen = liveOpen && DriverMeIsOpen();

            if (meOpen != _driverMeOpen)
            {
                _driverMeOpen = meOpen;
                _lastRelayDocuments.Remove(RelayChannel.DriverMe);
                _driverMeOpenedUtc = now;
                _driverMeForgotUtc = now;
                _driverMeBuiltUtc = DateTime.MinValue;

                if (!meOpen)
                {
                    _relayPublisher.DropQueuedSnapshot(RelayChannel.DriverMe);
                }
            }

            // The relay was told "open" again - by a switch ticked, or after
            // it refused driverme as closed - so what it held was blanked.
            var asksOpened = _relayPublisher.DriverAsksOpenedCount;

            if (asksOpened != _driverMeAsksOpenedSeen)
            {
                _driverMeAsksOpenedSeen = asksOpened;
                _lastRelayDocuments.Remove(RelayChannel.DriverMe);
            }

            if (!liveOpen)
            {
                return;
            }

            // Race Control is built after the wiring; until it is, there is
            // no running order to read.
            if (_raceControlControl is null)
            {
                return;
            }

            if (_race.State != _driverLiveRaceState)
            {
                _driverLiveRaceState = _race.State;
                _driverLiveDirty = true;
            }

            if (_driverRequests is { } requests && requests.Version != _driverLiveAsksVersion)
            {
                _driverLiveAsksVersion = requests.Version;
                _driverLiveDirty = true;
            }

            if (_driverLiveDirty || now - _driverLiveBuiltUtc >= DriverLiveEvery || now < _driverLiveBuiltUtc)
            {
                _driverLiveDirty = false;
                _driverLiveBuiltUtc = now;
                SendToRelayIfChanged(RelayChannel.DriverLive, "the drivers' live evening", BuildDriverLiveDocument);
            }

            // Each driver's garage goes with their details (5 Nov 2026) -
            // see MainForm.DriverGarage.
            TickDriverGarage(now, meOpen && now - _driverMeOpenedUtc >= DriverMeSettleFor);

            if (!meOpen || now - _driverMeOpenedUtc < DriverMeSettleFor)
            {
                return;
            }

            var forgetEvery = now - _driverMeOpenedUtc < DriverMeEarlyFor ? DriverMeEarlyForgetEvery : DriverMeForgetEvery;

            if (now - _driverMeForgotUtc >= forgetEvery || now < _driverMeForgotUtc)
            {
                _driverMeForgotUtc = now;
                _lastRelayDocuments.Remove(RelayChannel.DriverMe);
            }

            if (!_lastRelayDocuments.ContainsKey(RelayChannel.DriverMe)
                || now - _driverMeBuiltUtc >= DriverLiveEvery
                || now < _driverMeBuiltUtc)
            {
                _driverMeBuiltUtc = now;
                SendToRelayIfChanged(RelayChannel.DriverMe, "the drivers' own details", BuildDriverMeDocument);
            }
        }
        catch (Exception ex)
        {
            // The roster tick carries check-in; nothing here may stop it.
            if (_relayBuildErrors.Add($"driverlive|{ex.Message}"))
            {
                OnClientRawMessage($"[relay] Could not update the drivers' live pages: {ex.Message}");
            }
        }
    }

    /// <summary>Drivers' live pages may be sent: the relay, drivers' nights and a check-in key (design 1.3).</summary>
    private bool DriverLiveIsOpen() =>
        _settings.RelayEnabled
        && _settings.PublishDriverNightsToTheRelay
        && !string.IsNullOrWhiteSpace(_settings.RelayCheckInSecret);

    /// <summary>
    /// Drivers' own details may be sent too: online check-in (the relay
    /// knows a driver from the check-in roster) and driver requests.
    /// </summary>
    private bool DriverMeIsOpen() =>
        DriverLiveIsOpen()
        && _settings.RelayCheckInEnabled
        && _settings.RelayDriverRequests;

    /// <summary>driverlive, from Race Control's running order, the marshal rota, check-in, tonight's races, the transponder watch and the desk.</summary>
    private string BuildDriverLiveDocument()
    {
        var nowUtc = DateTime.UtcNow;
        var nowLocal = DateTime.Now;
        var badgeOf = DriverLiveBadgeMap();

        var upcoming = _raceControlControl.UpcomingRaces(DriverLive.UpcomingCount);

        var duties = _rounds is null
            ? Array.Empty<MarshalAssigner.MarshalDuty>()
            : MarshalAssigner.DutiesForDay(_rounds, _raceEvents, _trackLayouts, _profiles);

        var asksFor = _askAnswersFor;

        return DriverLive.Document(new DriverLive.LiveInputs
        {
            NowUtc = nowUtc,
            Upcoming = upcoming,
            Duties = duties,
            RacedTonight = DriverLiveRacedTonight(nowUtc),
            CheckedIn = _checkInStore.SnapshotCheckedIn(),

            // Everyone whose badge sent a request in the last day (design
            // 1.2), not every profile: the store says which badges, and the
            // badge map says whose they are (22 Oct 2026, driver experience).
            AskCandidates = asksFor is null || _driverRequests is null
                ? Array.Empty<string>()
                : DriverLiveAskCandidates(_driverRequests, nowUtc),
            ProfileOf = t => _profiles.Get(t),
            BadgeOf = t => badgeOf.TryGetValue(t, out var badge) ? badge : null,
            // WHY (23 Oct 2026, bug hunt 10): the driver's own page reports the last pass, as Check my transponder does.
            SeenOf = t => _transponderWatch.Of(t) is { } sighting
                ? new DriverLive.Seen(sighting.LastSeenUtc, sighting.LastSignalKnown, sighting.LastPassWeak)
                : null,
            FirstNight = t => DriverIsOnFirstNight(t, nowUtc, nowLocal),
            AsksOf = asksFor,

            // 29 Oct 2026, spoken laps on My Night: the driver's row tags on
            // the public board, and their target laps, so their own page
            // can read their laps out.
            RowTagOf = _webServer.RelayRowTag,
            TargetOf = DriverTargets.TargetMs,

            // 12 Nov 2026, setup plan step 8: marshals in place and swaps
            // from their phones, and their own entry money.
            MarshalIn = _raceControlControl.MarshalIsIn,
            CoverOffers = _driverRequests is null
                ? Array.Empty<MarshalSwaps.Offer>()
                : MarshalSwaps.Offers(_driverRequests.All, nowUtc),
            MainOfBadge = badge => _relayBadgeIds.TryGetValue(badge, out var transponder) ? transponder : null,
            MoneyOf = DriverLiveMoneyOf,
        });
    }

    /// <summary>A driver's own entry fee tonight, paid or owing, for their phone - null with no race day open or no fee (setup plan step 8).</summary>
    private DeskMoney.PhoneMoney? DriverLiveMoneyOf(string main)
    {
        if (_raceEvents.Active is not { IsLegacy: false } day || _profiles.Get(main) is not { } profile)
        {
            return null;
        }

        // With the classes' own fees, as the desk works it out - see PhoneMoneyFor.
        return CheckInControl.PhoneMoneyFor(
            profile, day, DeskMoney.ClassLookup(_raceClasses.All), _payments.Get(day.Id, profile.Transponder), _raceClasses.All);
    }

    /// <summary>The transponders behind every badge with a request to hear back about - see DriverRequestStore.BadgesWithAnswers.</summary>
    private List<string> DriverLiveAskCandidates(DriverRequestStore store, DateTime nowUtc)
    {
        var badges = _relayBadgeIds;
        var candidates = new List<string>();

        foreach (var badge in store.BadgesWithAnswers(nowUtc))
        {
            if (badges.TryGetValue(badge, out var transponder) && !string.IsNullOrWhiteSpace(transponder))
            {
                candidates.Add(transponder);
            }
        }

        return candidates;
    }

    /// <summary>driverme: every driver who has a badge, the checked-in first, then by name.</summary>
    private string BuildDriverMeDocument()
    {
        var badgeOf = DriverLiveBadgeMap();
        var here = new HashSet<string>(_checkInStore.SnapshotCheckedIn(), StringComparer.OrdinalIgnoreCase);

        var profiles = _profiles.AllRaceable
            .OrderByDescending(p => here.Contains(p.Transponder))
            .ThenBy(p => p.DriverName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return DriverLive.MeDocument(
            profiles,
            t => badgeOf.TryGetValue(t, out var badge) ? badge : null,
            DriverLivePacksOf,
            CrewForPhoneNow());
    }

    /// <summary>A driver's packs for the phone - the same choices Race Control's pack picker offers.</summary>
    private DriverLive.PackView DriverLivePacksOf(DriverProfile profile)
    {
        var key = BatteryPackStore.DriverKey(_profiles, profile.Transponder);
        var current = _batteryPacks.CurrentPackId(key);

        var choices = _batteryPacks.ChoicesFor(profile, current)
            .Select(pack => new DriverLive.PackChoice(pack.Id, pack.DisplayName))
            .ToList();

        return new DriverLive.PackView(current, choices);
    }

    /// <summary>
    /// _relayBadgeIds turned round: main transponder to badge id. That map
    /// is rebuilt every two seconds and replaced whole, so this is only
    /// worked out again when it has been.
    /// </summary>
    private Dictionary<string, string> DriverLiveBadgeMap()
    {
        var source = _relayBadgeIds;

        if (_driverLiveBadgeOf is not null && ReferenceEquals(source, _driverLiveBadgeOfSource))
        {
            return _driverLiveBadgeOf;
        }

        var inverted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (badge, transponder) in source)
        {
            if (!string.IsNullOrWhiteSpace(transponder))
            {
                inverted[transponder.Trim()] = badge;
            }
        }

        _driverLiveBadgeOf = inverted;
        _driverLiveBadgeOfSource = source;

        return inverted;
    }

    /// <summary>
    /// Everybody who raced tonight, read again when a race is filed or a
    /// minute has gone by (the night turns at 6am, and a race deleted in
    /// Race History raises nothing).
    /// </summary>
    private List<string> DriverLiveRacedTonight(DateTime nowUtc)
    {
        if (_driverLiveRaced is null
            || _driverLiveRacedStale
            || nowUtc - _driverLiveRacedReadUtc >= RacedTonightKeptFor
            || nowUtc < _driverLiveRacedReadUtc)
        {
            _driverLiveRacedStale = false;
            _driverLiveRacedReadUtc = nowUtc;
            _driverLiveRaced = RacesTonight()
                .Where(race => race is not null && !race.IsNotScored())
                .SelectMany(DriversIn)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return _driverLiveRaced;
    }

    /// <summary>
    /// Whether tonight is this driver's first night (DriverFirsts). Kept per
    /// driver for the night: their history before tonight does not change
    /// because they raced tonight, and reading the whole of it for every
    /// driver every ten seconds is the UI thread's time for nothing. Kept
    /// ten minutes, for a history imported or deleted meanwhile.
    /// </summary>
    private bool DriverIsOnFirstNight(string transponder, DateTime nowUtc, DateTime nowLocal)
    {
        var night = TonightAtTheTrack.NightOf(nowLocal);

        if (_driverFirstNights is null
            || _driverFirstNightsNight != night
            || nowUtc - _driverFirstNightsReadUtc >= FirstNightKeptFor
            || nowUtc < _driverFirstNightsReadUtc)
        {
            _driverFirstNights = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            _driverFirstNightsNight = night;
            _driverFirstNightsReadUtc = nowUtc;
        }

        if (!_driverFirstNights.TryGetValue(transponder, out var first))
        {
            // WHY (23 Oct 2026, bug hunt 10): this checked the main
            // transponder only, so a driver whose earlier nights were filed
            // under another transponder of theirs (one that raced before it
            // was given to them) got firstNight here while their night page
            // said not - and the phone shows the guide if either says so.
            // Now it uses the night page's rule: every transponder of
            // theirs, retired ones too, has to be new. Still kept per main.
            var theirs = (_profiles.Get(transponder)?.AllTransponderIds ?? Array.Empty<string>())
                .Append(transponder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            first = theirs.All(id => DriverFirsts.IsFirstNight(_sessionHistory.All, id, nowLocal));
            _driverFirstNights[transponder] = first;
        }

        return first;
    }
}
