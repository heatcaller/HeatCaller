using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// MainForm's side of the online relay: publishing the roster, state,
/// standings, results and extras; check-in taps from the relay; racer PINs
/// and emailing them; and sign-ups.
///
/// Moved here unchanged from MainForm.cs on 26 Sep 2026, when the file was
/// split into parts. Fields with initializers stayed in MainForm.cs.
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// Hands the web server a fresh copy of the driver list and who is
    /// marked present.
    ///
    /// A copy, not the store. The server answers requests on thread-pool
    /// threads and CheckInStore is plain dictionaries and hash sets - it is
    /// only ever safe on this thread, so what crosses over is an immutable
    /// snapshot built here.
    /// </summary>
    private void PublishCheckInRoster()
    {
        // Stopped whichever way this was reached: a coalesced publish still
        // pending would only repeat this one.
        _checkInRosterCoalesce.Stop();

        static string NameOf(DriverProfile profile) =>
            string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;

        // All, not AllRaceable: ghosts are raceable, but nobody checks in a
        // ghost, and a list of real drivers with "Ghost of Christmas Past"
        // in it - on the LAN page and in the relay's roster - is a list
        // somebody has to explain.
        var roster = _profiles.All
            .OrderBy(NameOf, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new WebServerService.CheckInEntry(
                p.Transponder,
                NameOf(p),
                p.ClassName,
                _checkInStore.IsCheckedIn(p.Transponder)))
            .ToList();

        _webServer.PublishRoster(roster);
        PublishRosterToRelay(roster);
        PublishCheckInToRelay(roster);
    }

    /// <summary>
    /// A roster republish within a moment, coalescing a burst of check-ins
    /// into one. See _checkInRosterCoalesce.
    /// </summary>
    private void PublishCheckInRosterSoon()
    {
        if (!_checkInRosterCoalesce.Enabled)
        {
            _checkInRosterCoalesce.Start();
        }
    }

    /// <summary>
    /// The online check-in list. Sent whether check-in is on or off - off,
    /// it is an empty list, which is what takes the names back off the
    /// relay when a club switches it off.
    /// </summary>
    private void PublishCheckInToRelay(IReadOnlyList<WebServerService.CheckInEntry> roster)
    {
        if (!_settings.RelayEnabled)
        {
            return;
        }

        var current = _raceEvents.Current;
        var eventName = current.Id == RaceEventStore.LegacyEventId ? string.Empty : current.Name;

        SendToRelayIfChanged(RelayChannel.CheckIn, "the check-in list", () => RelayDocuments.CheckIn(
            roster, _profiles, _settings, eventName, _relayCheckInPhotos));
    }

    /// <summary>
    /// The check-in list sent again now, whether or not it looks changed -
    /// for a change the club is waiting to see work, like a new set of PINs.
    /// </summary>
    private void RepublishCheckInNow()
    {
        _lastRelayDocuments.Remove(RelayChannel.CheckIn);

        if (_webServer.IsRunning || _settings.RelayEnabled)
        {
            PublishCheckInRoster();
        }
    }

    /// <summary>
    /// Gives a racer added since the last tick their PIN, and forgets the
    /// PINs of racers deleted. Cheap - a dictionary check per racer - so it
    /// rides the roster's two-second tick; ProfileStore has no event to
    /// hang it on, and the half-dozen places that add a profile would each
    /// have to remember to call it.
    /// </summary>
    private void KeepRacerPinsInStep()
    {
        if (SecretRecovery.Pending || !_settings.RelayCheckInEnabled || !_settings.RelayRacerPinsEnabled)
        {
            return;
        }

        try
        {
            if (RacerPins.EnsurePins(_settings, _profiles))
            {
                _settings.Save();
            }
        }
        catch (Exception ex)
        {
            // Logged once per distinct fault, like a document that will not
            // build: this runs every two seconds.
            if (_relayBuildErrors.Add($"pins|{ex.Message}"))
            {
                OnClientRawMessage($"[pins] Could not give new racers a check-in PIN: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Personal PINs, kept up to date: a new set when the rotation says one
    /// is due, a PIN for anybody without one, then - when the club has
    /// asked for it - an email to every racer who has not been sent their
    /// current PIN. At startup, every ten minutes, and when the Club Website page
    /// changes them.
    ///
    /// Only while online check-in and personal PINs are both on: a PIN
    /// nobody can use is not worth emailing anybody.
    /// </summary>
    /// <returns>True when it changed the PINs and has already sent the check-in list again.</returns>
    private bool RunRacerPinsPass()
    {
        // Not while keys locked to another PC are still to be recovered:
        // the racers' current PINs are among them, and a set made now
        // would be emailed out and then replaced by the recovery.
        if (_closingDown || SecretRecovery.Pending || !_settings.RelayCheckInEnabled || !_settings.RelayRacerPinsEnabled)
        {
            return false;
        }

        var republished = false;

        // Never mid-race. A new set is for the start of a day, and a desk
        // queue of racers whose PINs had just stopped working, between two
        // heats, is the one moment it must not happen. The next pass after
        // the race picks it up.
        var raceLive = _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;

        try
        {
            // When racing last stopped, so a set that falls due at midnight
            // with the meeting still going waits for the track to go quiet
            // rather than changing every PIN between two heats. A session
            // saved without an end time counts from its start.
            var lastSessionEndUtc = _sessionHistory.All
                .Select(s => s.EndedAtUtc > s.StartedAtUtc ? s.EndedAtUtc : s.StartedAtUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            var rotated = !raceLive && RacerPins.RotateIfDue(
                _settings, _profiles, DateTime.UtcNow, lastSessionEndUtc == DateTime.MinValue ? (DateTime?)null : lastSessionEndUtc);
            var ensured = RacerPins.EnsurePins(_settings, _profiles);

            if (rotated || ensured)
            {
                // Published and emailed only once the PINs are actually on
                // disk. They live nowhere else - RacerPins is inside the
                // sealed blob in settings.json - so a failed write means the
                // set about to be emailed to sixty drivers will not survive
                // the next restart. It worked all evening and then every PIN
                // in every inbox stopped, with one console line hours earlier
                // as the only clue.
                if (!_settings.Save())
                {
                    OnClientRawMessage(
                        $"{AppTime.LogStampNow()}  [pins] New racer PINs could NOT be saved, so they have not been "
                        + "published or emailed. The PINs already in use are unchanged. Close anything that might "
                        + "have settings.json open and try again.");

                    return false;
                }

                RepublishCheckInNow();
                republished = true;
            }

            if (rotated)
            {
                OnClientRawMessage($"[pins] Made a new set of check-in PINs for {Plural.Of(_settings.RacerPins.Count, "racer")}.");
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[pins] Could not update the check-in PINs: {ex.Message}");
            return republished;
        }

        if (!raceLive)
        {
            EmailPinsAutomatically();
        }

        return republished;
    }

    /// <summary>
    /// Starts emailing every racer who is waiting for their current PIN, in
    /// the background, when the club has switched that on and set up its
    /// mailbox. Returns at once: the sending takes a second and a half per
    /// racer, and none of it is on this thread.
    /// </summary>
    private void EmailPinsAutomatically()
    {
        if (!_settings.RacerPinsEmailAutomatically
            || !PinMailer.IsConfigured(_settings)
            || PinMailer.IsSending
            || RelayCheckIn.CheckInPageUrl(_settings) is null)
        {
            return;
        }

        var pending = RacerPins.AwaitingEmail(_settings, _profiles)
            .Where(t => !_pinMailFailed.Contains($"{t}|{_settings.RacerPins[t]}"))
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        _ = EmailPinsInBackgroundAsync(pending);
    }

    private async Task EmailPinsInBackgroundAsync(IReadOnlyList<string> transponders)
    {
        try
        {
            // Called on the UI thread, and SendPinsAsync reads settings and
            // profiles before its first await - so all of that happens here,
            // on this thread. Only the sending happens elsewhere.
            var result = await PinMailer.SendPinsAsync(_settings, _profiles, transponders, null, _pinMailCancel.Token)
                .ConfigureAwait(false);

            // Settings are changed on the UI thread and nowhere else.
            TryPost(() => ApplyAutomaticPinMail(result));
        }
        catch (Exception ex)
        {
            // Fire and forget, so nothing else would ever see this.
            OnClientRawMessage($"[pins] Could not email check-in PINs: {ex.Message}");
        }
    }

    /// <summary>Records what an automatic PIN email run sent, and says so in the console in one line.</summary>
    private void ApplyAutomaticPinMail(MailResult result)
    {
        if (_closingDown)
        {
            return;
        }

        if (RacerPins.MarkEmailed(_settings, result.Sent) > 0)
        {
            _settings.Save();
        }

        foreach (var transponder in result.FailedTransponders)
        {
            if (_settings.RacerPins.TryGetValue(transponder, out var pin))
            {
                _pinMailFailed.Add($"{transponder}|{pin}");
            }
        }

        if (result.Cancelled)
        {
            return;
        }

        if (!result.Ok)
        {
            if (result.Message != _lastPinMailProblem)
            {
                _lastPinMailProblem = result.Message;
                OnClientRawMessage($"[pins] Could not email check-in PINs: {result.Message}");
            }

            if (result.Sent.Count == 0)
            {
                return;
            }
        }
        else
        {
            _lastPinMailProblem = null;
        }

        var line = $"[pins] Emailed {Plural.Of(result.Sent.Count, "racer")} their new PIN";

        if (result.Failures.Count > 0)
        {
            line += $"; {result.Failures.Count} could not be sent";
        }

        var noEmail = _profiles.All.Count(p => _settings.RacerPins.ContainsKey(p.Transponder) && RacerPins.EmailFor(p) is null);

        if (noEmail > 0)
        {
            line += $"; {noEmail} {(noEmail == 1 ? "has" : "have")} no email";
        }

        OnClientRawMessage(line + ".");
    }

    // ------------------------------------------------------ online sign-up

    /// <summary>Opens Online Sign-ups, or brings it forward if it is already open.</summary>
    private void ShowSignups()
    {
        if (_signupsForm is { IsDisposed: false } open)
        {
            open.Activate();
            return;
        }

        var form = new SignupsForm(_signupDesk, _settings, _profiles, _raceClasses, _rentals, _undo);
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_signupsForm, form))
            {
                _signupsForm = null;
            }

            form.Dispose();
        };

        _signupsForm = form;
        form.Show(this);
    }

    /// <summary>How many sign-ups are waiting, on the Entries button and in the sidebar.</summary>
    private void UpdateSignupCounts()
    {
        var waiting = _signupDesk.Waiting.Count;

        _checkInControl?.SetSignupCount(waiting);
        lstNav.SetBadge("Race Day", waiting);
    }

    /// <summary>
    /// A driver just approved from an online sign-up: their badge id, their
    /// own check-in PIN, the check-in list sent again with them in it, and
    /// the PIN emailed - whether or not the club emails PINs automatically,
    /// since the sign-up page told them it would come.
    ///
    /// Returns whether the PIN email was started, so RelaySignupDesk only
    /// tells the phone "your PIN is on its way" when it is.
    /// </summary>
    private bool GiveSignedUpRacerTheirPin(string transponder)
    {
        RebuildBadgeIds();
        _checkInControl?.RefreshAll();

        if (SecretRecovery.Pending || !_settings.RelayCheckInEnabled || !_settings.RelayRacerPinsEnabled)
        {
            RepublishCheckInNow();
            return false;
        }

        try
        {
            if (RacerPins.EnsurePins(_settings, _profiles))
            {
                _settings.Save();
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[signup] Could not give the new racer a check-in PIN: {ex.Message}");
        }

        RepublishCheckInNow();

        if (!_settings.RacerPins.ContainsKey(transponder)
            || RacerPins.EmailFor(_profiles.Get(transponder)) is null
            || !PinMailer.IsConfigured(_settings))
        {
            return false;
        }

        if (PinMailer.IsSending)
        {
            OnClientRawMessage("[signup] PINs are being emailed right now, so the new racer's PIN wasn't sent. Use Email PINs... on the Club Website page once that's done.");
            return false;
        }

        _ = EmailPinsInBackgroundAsync(new[] { transponder });
        return true;
    }

    /// <summary>
    /// Every driver's online id, recomputed. Cheap - an HMAC per driver -
    /// and only once a key exists: making one just to fill this map would
    /// be deciding, on the club's behalf, that it wants online check-in.
    /// </summary>
    private void RebuildBadgeIds()
    {
        var secret = _settings.RelayCheckInSecret;

        if (string.IsNullOrWhiteSpace(secret))
        {
            if (_relayBadgeIds.Count > 0)
            {
                _relayBadgeIds = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var profile in _profiles.AllRaceable)
        {
            if (!GhostService.IsGhost(profile.Transponder) && !string.IsNullOrWhiteSpace(profile.Transponder))
            {
                map[RelayCheckIn.IdFor(profile.Transponder, secret)] = profile.Transponder;
            }
        }

        _relayBadgeIds = map;
    }

    /// <summary>
    /// A driver checked in or out from their own phone. Applied exactly as
    /// a tap on the LAN check-in page is, so the Entries page, the roster
    /// and everything downstream cannot tell the two apart.
    /// </summary>
    /// <returns>
    /// True when the tap has been dealt with - applied, or deliberately
    /// ignored - and may be acknowledged to the relay. False only when it
    /// could not be dealt with, so the relay keeps it for next time.
    /// </returns>
    private bool ApplyRelayCheckIn(RelayCheckInTap tap)
    {
        try
        {
            if (!_relayBadgeIds.TryGetValue(tap.Id, out var transponder))
            {
                // The map is rebuilt every two seconds; a driver added in the
                // last two may simply not be in it yet.
                RebuildBadgeIds();

                if (!_relayBadgeIds.TryGetValue(tap.Id, out transponder))
                {
                    OnClientRawMessage("[relay] A check-in arrived for a driver this laptop does not recognise - their profile may have been deleted, or the tap came from a QR check-in or list from before QR check-ins were reissued.");
                    return true;
                }
            }

            var profile = _profiles.Get(transponder);
            var name = string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;

            // From another day: the relay keeps taps for twelve hours, and
            // a laptop that was off overnight would otherwise open the next
            // meeting by checking in everybody from the last one.
            if (DateTime.UtcNow - tap.AtUtc > TimeSpan.FromHours(12))
            {
                OnClientRawMessage($"[relay] Ignored an old check-{(tap.Wanted ? "in" : "out")} for {name} from {tap.AtUtc.ToLocalTime():g}.");
                return true;
            }

            // Overruled already. The relay is polled every few seconds, so a
            // tap can arrive after the desk has changed the same driver -
            // and applying it then would quietly undo the desk.
            if (_checkInChangedUtc.TryGetValue(transponder, out var changedUtc) && tap.AtUtc < changedUtc)
            {
                OnClientRawMessage($"[relay] Ignored a check-{(tap.Wanted ? "in" : "out")} for {name} from their phone - it was changed here since.");
                return true;
            }

            // The tap's own time, not now: a second tap made just after it,
            // collected in the same batch, must not look older than this.
            _checkInChangedUtc[transponder] = tap.AtUtc;

            if (_checkInStore.IsCheckedIn(transponder) == tap.Wanted)
            {
                return true;
            }

            _checkInStore.SetCheckedIn(transponder, tap.Wanted);

            OnClientRawMessage($"[relay] {name} checked {(tap.Wanted ? "in" : "out")} from their phone.");

            PublishCheckInRosterSoon();
            _checkInControl?.RefreshCheckInMarks();

            return true;
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not apply a check-in from a phone: {ex.Message}. It will be tried again.");
            return false;
        }
    }

    /// <summary>
    /// The relay's copy of the roster, which is NOT the one the local
    /// check-in page gets.
    ///
    /// Two differences, both deliberate. The LAN page keeps full names,
    /// because a driver standing at the track has to be able to find
    /// themselves in the list; the relay honours whatever the club chose.
    /// And no transponder ids cross: on a public address, an id that
    /// follows somebody from meeting to meeting, next to their name, is a
    /// tracking key nobody asked for. The page does not need it.
    /// </summary>
    private void PublishRosterToRelay(IReadOnlyList<WebServerService.CheckInEntry> roster)
    {
        try
        {
            var abbreviate = _settings.RelayAbbreviateNames;

            // The LAN roster names an unnamed driver by their transponder;
            // that stops here.
            var names = RelayPublisher.DisplayNames(
                roster.Select(d => RelayDocuments.PublicNameOrUnnamed(d.Name, d.Transponder)).ToList(),
                abbreviate);

            var document = JsonSerializer.Serialize(new
            {
                checkedIn = roster.Count(d => d.CheckedIn),
                total = roster.Count,
                drivers = roster.Select((d, i) => new
                {
                    name = names[i],
                    className = d.ClassName,
                    checkedIn = d.CheckedIn,
                }).ToList(),
            });

            // This runs every two seconds whether anything moved or not.
            // Sending an identical document that often is a POST a second
            // for nothing, all meeting, so it only goes when it changes.
            if (document == _lastRelayRoster)
            {
                return;
            }

            _lastRelayRoster = document;
            _relayPublisher.PublishSnapshot(RelayChannel.Roster, document);
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build the roster: {ex.Message}");
        }
    }

    /// <summary>
    /// A frame of the live race, once a second while one is happening.
    ///
    /// Idle time is skipped on purpose - a board sitting between heats
    /// does not need re-sending sixty times a minute - but the frame that
    /// records a session STOPPING does go, so the relay is left showing
    /// the result of the heat that just ended rather than the last moment
    /// of it running.
    /// </summary>
    private void PublishStateToRelay()
    {
        if (!_settings.RelayEnabled)
        {
            return;
        }

        var state = _race.State;
        var live = state is RaceState.Staging or RaceState.Countdown or RaceState.Racing;
        var changed = state != _lastRelayRaceState;

        _lastRelayRaceState = state;

        if (!live && !changed)
        {
            return;
        }

        try
        {
            _relayPublisher.PublishSnapshot(
                RelayChannel.State,
                _webServer.BuildStateJson(_settings.RelayAbbreviateNames, forRelay: true));
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build the live state: {ex.Message}");
        }
    }

    /// <summary>
    /// Championship standings, for every season, in one document.
    ///
    /// Every season rather than "the current one" because there is no such
    /// thing in this app's data: SeasonStore holds a list and nothing
    /// marks one as active. Inventing a current season here would be a
    /// guess that every later feature would then have to keep making the
    /// same way. Sending them all costs a few hundred bytes and lets the
    /// public page offer whichever one a club wants to show.
    /// </summary>
    private void PublishStandingsToRelay()
    {
        if (!_settings.RelayEnabled)
        {
            return;
        }

        try
        {
            var abbreviate = _settings.RelayAbbreviateNames;

            // Standings are keyed by name, and a car nobody named raced
            // under its transponder number - so any "name" that is one of
            // those numbers is replaced before it goes out.
            var transponderNames = TransponderNamesInHistory();

            var document = JsonSerializer.Serialize(new
            {
                seasons = _seasonStore.All.Select(season =>
                {
                    var table = SeasonStore.ComputeStandings(
                        season,
                        EventSpine.SessionsFor(season, _raceEvents, _rounds, _sessionHistory),
                        _profiles);

                    // Per season, because a season is the list a reader
                    // compares within - and shortened together, so two
                    // drivers never share a name in the same table.
                    var names = RelayPublisher.DisplayNames(
                        table.Select(entry => transponderNames.Contains((entry.DriverName ?? string.Empty).Trim())
                            ? RelayDocuments.UnnamedDriver
                            : entry.DriverName ?? string.Empty).ToList(),
                        abbreviate);

                    return new
                    {
                        id = season.Id,
                        name = season.Name,
                        standings = table.Select((entry, index) => new
                        {
                            pos = index + 1,
                            name = names[index],
                            points = entry.TotalPoints,
                            counted = entry.SessionsCounted,
                            wins = entry.Wins,
                            podiums = entry.Podiums,

                            // BestFinish starts at int.MaxValue to mean
                            // "no finish yet". Sent as null rather than as
                            // a two-billionth place.
                            best = entry.BestFinish == int.MaxValue ? (int?)null : entry.BestFinish,
                            dropped = entry.DroppedRounds,
                        }).ToList(),
                    };
                }).ToList(),
            });

            _relayPublisher.PublishSnapshot(RelayChannel.Standings, document);
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build standings: {ex.Message}");
        }
    }

    /// <summary>
    /// Every saved result whose driver "name" is only its transponder
    /// number - a car nobody had named when it raced. For keeping those
    /// numbers off the relay where a document has names but no
    /// transponders to compare them with.
    /// </summary>
    private HashSet<string> TransponderNamesInHistory()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in _sessionHistory.All)
        {
            foreach (var result in session.Results)
            {
                if (RelayDocuments.PublicNameOrUnnamed(result.DriverName, result.Transponder) == RelayDocuments.UnnamedDriver
                    && !string.IsNullOrWhiteSpace(result.DriverName))
                {
                    found.Add(result.DriverName.Trim());
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The schedule, badges and tracks pages' documents.
    ///
    /// Rides the roster's two-second tick. The schedule is cheap and is
    /// what a driver refreshes to see whether they are up, so it is built
    /// every tick; badges earned and the tracks involve reading files and
    /// drawing pictures, and change after a race rather than during one,
    /// so they are rebuilt every thirty seconds. Every one of them is only
    /// SENT when it differs from the last one that was.
    /// </summary>
    private void PublishExtrasToRelay()
    {
        if (!_settings.RelayEnabled)
        {
            return;
        }

        var generation = _relayPublisher.Generation;

        if (generation != _relayGeneration)
        {
            // A new club, a new address, or names switched to short form:
            // everything already sent was sent somewhere else, or said
            // somebody's name the club no longer wants said. Start again.
            _relayGeneration = generation;
            _lastRelayDocuments.Clear();
            _lastRelayRoster = string.Empty;
            _relayBuildErrors.Clear();
            _relayExtrasTick = 0;
            _relayCachesResetUtc = DateTime.UtcNow;
            _relayBadgesSentUtc = DateTime.MinValue;

            // Standings otherwise only go after a session is saved, which
            // would leave a newly set-up club with an empty standings page
            // until its next race.
            PublishStandingsToRelay();

            // And the day's results, which are otherwise sent once, as each
            // session is saved, from a queue held only in memory. A laptop
            // closed with results still queued - or pointed at a new relay
            // - lost them for good. Generation starts below the publisher's
            // own, so this runs on the first pass after startup too.
            RepublishRecentResults();
        }
        else if (DateTime.UtcNow - _relayCachesResetUtc > TimeSpan.FromMinutes(10))
        {
            // Every ten minutes, forget what was sent. A document counts as
            // sent the moment it is queued, not when the relay takes it, so
            // one it refused, or lost to a delete or a relay restart, would
            // otherwise stay missing until it next changed - which for the
            // tracks or the site look can be never.
            _relayCachesResetUtc = DateTime.UtcNow;
            _lastRelayDocuments.Remove(RelayChannel.Site);
            _lastRelayDocuments.Remove(RelayChannel.Programme);
            _lastRelayDocuments.Remove(RelayChannel.Achievements);
            _lastRelayDocuments.Remove(RelayChannel.Tracks);
            _lastRelayDocuments.Remove(RelayChannel.CheckIn);
            _lastRelayDocuments.Remove(RelayChannel.Manage);
            _lastRelayDocuments.Remove(RelayChannel.Signup);
            _lastRelayRoster = string.Empty;
        }

        var abbreviate = _settings.RelayAbbreviateNames;

        // The look first, so a club that has just picked a theme sees it on
        // the very next page load. Cheap: the logo is cached.
        SendToRelayIfChanged(RelayChannel.Site, "the site look", () => RelayDocuments.Site(_settings));

        // Settings-only and just as cheap as the look, so it goes every
        // tick rather than waiting for the throttled group below - a club
        // that has just switched the manager on, or changed its PIN,
        // reaches the relay within a couple of seconds rather than up to
        // thirty.
        SendToRelayIfChanged(RelayChannel.Manage, "the race-day manager", () => RelayDocuments.Manage(_settings));

        // Settings-only too: sign-up open or not, the classes, the invite
        // codes' hashes - so an invite just emailed works within seconds.
        // Only once a club has used it: a relay from before sign-up has no
        // such channel, and a club that never asked for sign-up should not
        // be told its relay "may need updating" because of it.
        if (_signupDesk.InUse)
        {
            SendToRelayIfChanged(RelayChannel.Signup, "driver sign-up", _signupDesk.Document);
        }

        SendToRelayIfChanged(RelayChannel.Programme, "the schedule", () => RelayDocuments.Programme(
            _daySchedule, _rounds, _raceEvents, _profiles, _settings.RelayPublishRoster, abbreviate));

        if (_relayExtrasTick++ % 15 != 0)
        {
            return;
        }

        SendToRelayIfChanged(RelayChannel.Achievements, "badges earned", () => RelayDocuments.Achievements(
            RelayAchievements(), _profiles, abbreviate));

        SendToRelayIfChanged(RelayChannel.Tracks, "the tracks", () => RelayDocuments.Tracks(
            _trackLayouts, _sessionHistory, _profiles, _pbResets, _raceEvents.Current.TrackLayoutName, abbreviate, _relayTrackImages));

        // The catalogue is fixed while the app runs, and it carries every
        // badge's artwork - so it is drawn and sent once, not twice a minute.
        // Once an hour it goes again regardless, for the same reason the
        // other documents are refreshed: once was once per process, and a
        // relay that lost it kept a badges page with no badges all day.
        if (!_lastRelayDocuments.ContainsKey(RelayChannel.Badges)
            || DateTime.UtcNow - _relayBadgesSentUtc > TimeSpan.FromHours(1))
        {
            _lastRelayDocuments.Remove(RelayChannel.Badges);
            _relayBadgesSentUtc = DateTime.UtcNow;
            SendToRelayIfChanged(RelayChannel.Badges, "the badge list", RelayDocuments.Badges);
        }
    }

    /// <summary>
    /// Earned badges for the relay, read from disk at most every five
    /// minutes - or on the next pass after a session is saved, which is
    /// when badges are awarded.
    ///
    /// It was a fresh load from disk on the UI thread every thirty seconds,
    /// all meeting, for a list that changes a few times a night. There is
    /// no change event on the store to wait for instead.
    /// </summary>
    private AchievementStore RelayAchievements()
    {
        if (_relayAchievements is null || DateTime.UtcNow - _relayAchievementsReadUtc > TimeSpan.FromMinutes(5))
        {
            _relayAchievements = AchievementStore.Load();
            _relayAchievementsReadUtc = DateTime.UtcNow;
        }

        return _relayAchievements;
    }

    /// <summary>
    /// Sends every session from the last thirty-six hours again.
    ///
    /// The relay keeps results by session id and takes a newer copy over
    /// an older one, so sending one it already has changes nothing - and
    /// sending one it lost, or never got, puts it back. Thirty-six hours
    /// covers a meeting that ran past midnight and a laptop reopened the
    /// next morning, without re-sending a whole season to a new relay.
    /// </summary>
    private void RepublishRecentResults()
    {
        try
        {
            var since = DateTime.UtcNow - TimeSpan.FromHours(36);

            var sent = _sessionHistory.All
                .Where(s => (s.EndedAtUtc > s.StartedAtUtc ? s.EndedAtUtc : s.StartedAtUtc) >= since)

                // Oldest first, so they land in the order they were raced.
                .OrderBy(RelayResultsIndex.EffectiveStart)
                .ToList();

            foreach (var session in sent)
            {
                PublishSessionToRelay(session);
            }

            // Once per year, after the results rather than with each of
            // them. A night of twenty heats is twenty result documents and
            // ONE index - sending the index after each would re-send the
            // whole year twenty times over a connection that has just come
            // back, which is the moment it can least afford it.
            foreach (var year in RelayResultsIndex.YearsOf(sent))
            {
                PublishResultsIndexForYear(year);
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not re-send recent results: {ex.Message}");
        }
    }

    private void SendToRelayIfChanged(RelayChannel channel, string what, Func<string> build)
    {
        try
        {
            var document = build();

            if (_lastRelayDocuments.TryGetValue(channel, out var last) && last == document)
            {
                return;
            }

            _lastRelayDocuments[channel] = document;
            _relayPublisher.PublishSnapshot(channel, document);
        }
        catch (Exception ex)
        {
            if (_relayBuildErrors.Add($"{channel}|{ex.Message}"))
            {
                OnClientRawMessage($"[relay] Could not build {what}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// A result status worth showing a spectator, or nothing.
    ///
    /// The app marks an ordinary finisher "active". That is a state this
    /// program tracks, not news - and published as-is it appeared under
    /// "Time" for every driver on the page.
    /// </summary>
    private static string PublishableStatus(string? status)
    {
        var text = (status ?? string.Empty).Trim();

        return text.Equals("active", StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
    }

    /// <summary>Republishes the index for the year one session belongs to. A demo race is not in it, so it changes nothing.</summary>
    private void PublishResultsIndexFor(SessionRecord session)
    {
        if (session.IsDemoSession())
        {
            return;
        }

        PublishResultsIndexForYear(RelayResultsIndex.YearOf(session));
    }

    /// <summary>
    /// Builds and publishes one year of the results index.
    ///
    /// Built from the whole history every time rather than patched: a
    /// patched index would drift from what was actually saved the first
    /// time a session was deleted or a result corrected outside the paths
    /// that call this, and an index that disagrees with the results is
    /// worse than no index - the page would offer a session the relay
    /// cannot hand back. A year of a busy club is a few hundred sessions,
    /// so building it costs nothing worth saving.
    /// </summary>
    private void PublishResultsIndexForYear(string year)
    {
        try
        {
            var json = RelayResultsIndex.Build(
                year,
                _sessionHistory.All,

                // A round belongs to a meeting, and a meeting usually to a
                // championship. The championship's name is what makes a
                // season picker worth having - one entry per season rather
                // than one per race night - so it is preferred, and the
                // meeting's own name is the fallback for a one-off.
                roundId => SeasonLabelFor(roundId),
                _settings.RelayAbbreviateNames,
                _settings.RelayResultsFromUtc);

            _relayPublisher.PublishResultsIndex(year, json);
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build the results index for {year}: {ex.Message}");
        }
    }

    /// <summary>The championship a round is part of, or the meeting's own name, or nothing.</summary>
    private string? SeasonLabelFor(string? roundId)
    {
        if (string.IsNullOrWhiteSpace(roundId))
        {
            return null;
        }

        var raceEvent = _raceEvents.EventForRound(roundId);

        if (raceEvent is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(raceEvent.ChampionshipId))
        {
            var season = _seasonStore.All.FirstOrDefault(s =>
                string.Equals(s.Id, raceEvent.ChampionshipId, StringComparison.Ordinal));

            if (season is not null && !string.IsNullOrWhiteSpace(season.Name))
            {
                return season.Name;
            }
        }

        return raceEvent.Id == RaceEventStore.LegacyEventId ? null : raceEvent.Name;
    }

    /// <summary>
    /// Publishes one finished session's results.
    ///
    /// Built here rather than inside RelayPublisher because the stores are
    /// only safe on this thread, and because the club's name setting has
    /// to be applied where the document is made - the publisher is
    /// deliberately blind to what it carries, so it could not apply it
    /// even if asked.
    ///
    /// Transponder ids are left out for the same reason as the roster.
    /// </summary>
    private void PublishSessionToRelay(SessionRecord session)
    {
        // A demo race is a rehearsal, not a result - it never goes on the
        // club's public results page.
        if (session.IsDemoSession())
        {
            return;
        }

        try
        {
            var abbreviate = _settings.RelayAbbreviateNames;

            // The whole field at once, so two drivers never arrive at the
            // same shortened name. One at a time turned "Ghost of
            // Christmas Past" and "Ghost of Christmas Present" into two
            // identical rows.
            //
            // A car nobody had named raced under its transponder number,
            // and that is what its result says its name is. Not online.
            var names = RelayPublisher.DisplayNames(
                session.Results.Select(r => RelayDocuments.PublicNameOrUnnamed(r.DriverName, r.Transponder)).ToList(),
                abbreviate);

            var document = JsonSerializer.Serialize(new
            {
                id = session.Id,
                type = session.SessionType.ToString(),
                label = session.Label,
                track = session.TrackLayoutName,
                roundId = session.RoundId,
                startedUtc = session.StartedAtUtc,
                endedUtc = session.EndedAtUtc,
                results = session.Results.Select((r, i) => new
                {
                    pos = r.Position,
                    name = names[i],
                    car = r.CarModel,
                    laps = r.Laps,
                    fastLapMs = r.FastLapMs,
                    elapsedMs = r.ElapsedMs,

                    // "active" is what this app calls a driver who simply
                    // raced. It is internal vocabulary, and published it
                    // filled the finishing-time column with the word
                    // "active" on every row. Only a real exception - a
                    // DNF, a disqualification - is worth a reader's
                    // attention, so only that is sent.
                    status = PublishableStatus(r.Status),
                }).ToList(),
            });

            // The effective start - the end, for a session saved without a
            // start - as the results index files it (see
            // RelayResultsIndex.EffectiveStart). The raw start of such a
            // session is DateTime.MinValue, which PublishResult's window
            // ignores, so the index then left out a session whose result
            // the relay had been given.
            _relayPublisher.PublishResult(session.Id, document, RelayResultsIndex.EffectiveStart(session));
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build the results document: {ex.Message}");
        }
    }
}
