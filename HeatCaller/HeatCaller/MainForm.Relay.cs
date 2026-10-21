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

        if (!_racerPinHashesInPlace)
        {
            try
            {
                var missing = RelayDocuments.RacerPinHashesMissing(roster, _settings);

                if (missing.Count > 0)
                {
                    WarmRacerPinHashesThenPublish(missing);
                    return;
                }
            }
            catch (Exception ex)
            {
                // Nothing expected - it reads settings and may make the salt,
                // as CheckIn itself would - but if it ever throws, the list
                // must still go out, so the old in-place way takes over.
                _racerPinHashesInPlace = true;
                OnClientRawMessage($"[pins] Could not prepare the check-in PINs in the background, so they are prepared as the list is sent: {ex.Message}");
            }
        }

        var current = _raceEvents.Current;
        var eventName = current.Id == RaceEventStore.LegacyEventId ? string.Empty : current.Name;

        SendToRelayIfChanged(RelayChannel.CheckIn, "the check-in list", () => RelayDocuments.CheckIn(
            roster, _profiles, _settings, eventName, _relayCheckInPhotos));
    }

    /// <summary>
    /// True while racer PIN hashes are being made in the background, so a
    /// second roster tick does not start a second batch of the same work.
    /// Set on the UI thread; cleared from the background only when the
    /// window cannot take the post that would clear it, hence volatile.
    /// </summary>
    private volatile bool _racerPinHashesWarming;

    /// <summary>An automatic PIN email held back until the check-in list with the new PINs has been sent - see EmailPinsAutomatically. UI thread.</summary>
    private bool _emailPinsAfterWarm;

    /// <summary>
    /// Racers approved from an online sign-up whose PIN email is held back
    /// until the check-in list carrying their PIN has been sent - see
    /// GiveSignedUpRacerTheirPin and EmailSignedUpPinsHeldForWarm. By
    /// transponder. UI thread only.
    /// </summary>
    private readonly HashSet<string> _signupPinsAfterWarm = new(StringComparer.Ordinal);

    /// <summary>
    /// PIN emails started - here (EmailPinsInBackgroundAsync) or by Email
    /// PINs... on the Club Website page - whose results have not yet been
    /// recorded in RacerPinsEmailed. PinMailer.IsSending alone is not enough
    /// to know nobody is mid-send: it goes false the moment the sending
    /// ends, on a background thread, and the result is only recorded later,
    /// in a post to the UI thread. In between, a racer just emailed still
    /// looks unemailed, and EmailSignedUpPinsHeldForWarm would email them
    /// again. Interlocked, because a run that cannot post its result back
    /// has to be counted off from the background.
    /// </summary>
    private int _pinMailsUnrecorded;

    /// <summary>
    /// Set if making the hashes in the background ever fails: from then on
    /// CheckIn makes any it needs in place, as it always used to - slower,
    /// but the list still goes out.
    /// </summary>
    private bool _racerPinHashesInPlace;

    /// <summary>
    /// Makes the racer PIN hashes the check-in list needs on a background
    /// thread, then sends the list. Until then the relay keeps the list it
    /// already has, which after a restart carries the same hashes anyway.
    ///
    /// Each hash is a 100,000-round PBKDF2 (see RelayDocuments.RacerPinHashes),
    /// and building the list here on the UI thread used to make them all in
    /// place - at start-up, after "New PINs now", after a daily rotation -
    /// freezing race control and every check-in tap for several seconds.
    /// </summary>
    private void WarmRacerPinHashesThenPublish(IReadOnlyList<RelayDocuments.RacerPinHashJob> missing)
    {
        if (_racerPinHashesWarming)
        {
            // The batch already running ends by publishing again, and that
            // publish asks for whatever is still missing - this included.
            return;
        }

        _racerPinHashesWarming = true;

        // The same token that stops an automatic PIN email when the window
        // closes: canceled as the window closes and never disposed, so it is
        // safe to read from the background for as long as this runs.
        var cancel = _pinMailCancel.Token;

        _ = Task.Run(() =>
        {
            Exception? failed = null;

            try
            {
                RelayDocuments.WarmRacerPinHashes(missing, cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // Closing down: nothing left to publish to.
                _racerPinHashesWarming = false;
                return;
            }
            catch (Exception ex)
            {
                failed = ex;
            }

            var posted = TryPost(() =>
            {
                _racerPinHashesWarming = false;

                if (failed is not null)
                {
                    _racerPinHashesInPlace = true;
                    OnClientRawMessage($"[pins] Could not prepare the check-in PINs in the background, so they are prepared as the list is sent: {failed.Message}");
                }

                if (!_closingDown && _settings.RelayEnabled)
                {
                    PublishCheckInRoster();
                }

                // The email held back while the list was prepared, now the
                // relay has been sent the PINs it would carry.
                if (_emailPinsAfterWarm && !_closingDown)
                {
                    _emailPinsAfterWarm = false;
                    EmailPinsAutomatically();
                }

                // Likewise the PIN of any racer approved from an online
                // sign-up while this batch ran - see GiveSignedUpRacerTheirPin.
                if (!_closingDown)
                {
                    EmailSignedUpPinsHeldForWarm();
                }
            });

            if (!posted)
            {
                // No window to post to yet (or any more). Cleared here so the
                // next roster tick, once there is one, can try again - the
                // hashes made so far are kept, so it has less to do.
                _racerPinHashesWarming = false;
            }
        });
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
        // A sign-up racer's PIN email held back while another email ran -
        // see EmailSignedUpPinsHeldForWarm. The sends started here and on the
        // Club Website page retry it themselves when they end; this catches
        // the ones that say nothing when they do (an invite or test email).
        // Cheap when nobody is held: a count check.
        EmailSignedUpPinsHeldForWarm();

        if (SecretRecovery.Pending || !_settings.RelayCheckInEnabled || !_settings.RelayRacerPinsEnabled)
        {
            return;
        }

        try
        {
            var before = RacerPins.CaptureSet(_settings);

            // A PIN that only exists in memory must not be published or
            // emailed: after a restart it is made afresh, and the one the
            // racer was sent stops working. So a PIN that could not be saved
            // is taken back, and handed out again once settings.json can be
            // written (this runs every two seconds).
            if (RacerPins.EnsurePins(_settings, _profiles) && !_settings.Save())
            {
                RacerPins.PutBackExactly(_settings, before);

                if (_relayBuildErrors.Add("pins|not saved"))
                {
                    OnClientRawMessage("[pins] New racers' check-in PINs couldn't be saved to settings.json, so none were given out yet - they will be once it can be written.");
                }
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

            // Taken before either step touches the PINs, so a set that then
            // can't be saved is put back exactly as it was - see below.
            var before = RacerPins.CaptureSet(_settings);

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
                    // And taken back out of memory, as KeepRacerPinsInStep
                    // does. Left there, the unsaved set would go out anyway
                    // by the back door: the roster's two-second tick
                    // publishes whatever RacerPins holds (its own EnsurePins
                    // finds nobody missing, so it never reaches a save
                    // check), and the next pass here, seeing nothing to
                    // rotate, would email it. The old set is still what is
                    // on disk and on the relay, so it is the one to keep
                    // using; the next pass tries the rotation again.
                    RacerPins.PutBackExactly(_settings, before);

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
        // The new PINs' check-in list is still being prepared in the
        // background (WarmRacerPinHashesThenPublish), so the relay still
        // holds the old ones: a racer emailed now would try a PIN the relay
        // refuses. Sent once that list has gone - see there.
        if (_racerPinHashesWarming)
        {
            _emailPinsAfterWarm = true;
            return;
        }

        // WHY (20 Oct 2026): this used to check only PinMailer.IsSending. That
        // goes false before the send that just ended has recorded who it
        // reached (see _pinMailsUnrecorded), so in that gap every racer it
        // emailed still looked unemailed and got a second, identical PIN
        // email. Now it also waits for that result to be recorded; whoever
        // is still waiting is picked up by the next RunRacerPinsPass.
        if (!_settings.RacerPinsEmailAutomatically
            || !PinMailer.IsConfigured(_settings)
            || PinMailer.IsSending
            || Volatile.Read(ref _pinMailsUnrecorded) > 0
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
        // Counted until the result is recorded - see _pinMailsUnrecorded.
        // Every path below counts it off exactly once.
        Interlocked.Increment(ref _pinMailsUnrecorded);

        MailResult? result = null;

        try
        {
            // Called on the UI thread, and SendPinsAsync reads settings and
            // profiles before its first await - so all of that happens here,
            // on this thread. Only the sending happens elsewhere.
            result = await PinMailer.SendPinsAsync(_settings, _profiles, transponders, null, _pinMailCancel.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Fire and forget, so nothing else would ever see this.
            OnClientRawMessage($"[pins] Could not email check-in PINs: {ex.Message}");
        }

        var finished = result;

        // Settings are changed on the UI thread and nowhere else.
        var posted = TryPost(() =>
        {
            try
            {
                if (finished is not null)
                {
                    ApplyAutomaticPinMail(finished);
                }
            }
            finally
            {
                Interlocked.Decrement(ref _pinMailsUnrecorded);
            }

            // Any racer approved from an online sign-up who was held back
            // because this send was running, now it has ended and what it
            // sent is recorded - so a racer it already emailed is not
            // emailed again.
            EmailSignedUpPinsHeldForWarm();
        });

        if (!posted)
        {
            // No window to take it - closing down, or not shown yet. Counted
            // off here, or nothing held back would ever be sent again.
            Interlocked.Decrement(ref _pinMailsUnrecorded);
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
            var before = RacerPins.CaptureSet(_settings);

            // As KeepRacerPinsInStep: a PIN that could not be saved is taken
            // back rather than published and emailed - see there. Without it
            // the racer is not emailed below either.
            if (RacerPins.EnsurePins(_settings, _profiles) && !_settings.Save())
            {
                RacerPins.PutBackExactly(_settings, before);
                OnClientRawMessage("[signup] The new racer's check-in PIN couldn't be saved to settings.json, so it wasn't sent. It will be given out once the file can be written.");
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[signup] Could not give the new racer a check-in PIN: {ex.Message}");
        }

        RepublishCheckInNow();

        // CanSendPinsNow, not just IsConfigured: true here becomes "your PIN
        // is on its way by email" on the racer's phone, so it must only be
        // said when SendPinsAsync will really send - not with publishing
        // switched off (no check-in page to link to) or in a rehearsal.
        if (!_settings.RacerPins.ContainsKey(transponder)
            || RacerPins.EmailFor(_profiles.Get(transponder)) is null
            || !PinMailer.CanSendPinsNow(_settings))
        {
            return false;
        }

        // The list just asked for may not have gone yet: the racer PIN hashes
        // it needs are made in the background first (see
        // WarmRacerPinHashesThenPublish), and until that batch ends the relay
        // holds the list from before this racer had a PIN. Emailed now, they
        // could tap their PIN straight away and be refused. So, as
        // EmailPinsAutomatically does, the email waits for that batch and is
        // sent from its end - still true here, since it will go: if another
        // PIN email is running by then, it is held until that one ends
        // rather than dropped (see EmailSignedUpPinsHeldForWarm).
        if (_racerPinHashesWarming)
        {
            _signupPinsAfterWarm.Add(transponder);
            return true;
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
    /// Emails the PINs of racers approved from an online sign-up while the
    /// check-in list was being prepared, once it has been sent. UI thread.
    ///
    /// Called at the end of each background batch. If that end started
    /// another batch - a racer approved during the first one is not in it,
    /// so the publish it ends with finds their hash still missing - they
    /// wait for that one too.
    ///
    /// And held, not dropped, while any PIN email is running. That is the
    /// usual case, not a rare one: the batch's end starts the held-back
    /// automatic email (_emailPinsAfterWarm) just before calling this, and
    /// PinMailer takes its one-at-a-time lock before returning. The phone
    /// has already been told "your PIN is on its way", so they are tried
    /// again when that send ends and its result is recorded
    /// (EmailPinsInBackgroundAsync, and PinEmailFinished from the Club
    /// Website page), and on the roster's two-second tick for any other
    /// send - an invite or test email - that says nothing when it ends.
    ///
    /// It cannot email anybody twice: a racer that send reached is marked
    /// emailed before this runs and is left out below, and the set is
    /// emptied the moment anybody in it is sent to. Nor can it go round in a
    /// loop: each call either waits for something outside it to happen, or
    /// empties the set.
    /// </summary>
    private void EmailSignedUpPinsHeldForWarm()
    {
        if (_closingDown || _signupPinsAfterWarm.Count == 0 || _racerPinHashesWarming)
        {
            return;
        }

        if (PinMailer.IsSending || Volatile.Read(ref _pinMailsUnrecorded) > 0)
        {
            // Kept, and tried again when that send has ended - see above.
            return;
        }

        // Asked again now rather than trusting the moment of approval: the
        // racer may have been deleted, PINs switched off, or the mailbox
        // changed while the list was prepared. And left out when a send that
        // ran meanwhile already reached them (the automatic one takes every
        // racer awaiting their PIN, and Email PINs... takes everybody), or
        // already tried and failed with this same PIN - that failure has
        // been reported, and the director resends by hand.
        var due = _signupPinsAfterWarm
            .Where(t => _settings.RacerPins.TryGetValue(t, out var pin)
                        && !RacerPins.IsEmailed(_settings, t)
                        && !_pinMailFailed.Contains($"{t}|{pin}")
                        && RacerPins.EmailFor(_profiles.Get(t)) is not null)
            .ToList();

        _signupPinsAfterWarm.Clear();

        if (due.Count == 0)
        {
            return;
        }

        if (!PinMailer.CanSendPinsNow(_settings))
        {
            // Their phones were told the PIN was on its way, so the director
            // is told it isn't - quietly forgetting them would leave racers
            // waiting for an email that never comes.
            OnClientRawMessage($"[signup] The PIN email for {Plural.Of(due.Count, "newly approved racer")} couldn't be sent - the email or online check-in settings changed meanwhile. Use Email PINs... on the Club Website page once they're set up again.");
            return;
        }

        _ = EmailPinsInBackgroundAsync(due);
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
                    OnClientRawMessage("[relay] A check-in arrived for a driver this laptop does not recognize - their profile may have been deleted, or the tap came from a QR check-in or list from before QR check-ins were reissued.");
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

            // Not onto a closed day - see CheckInStore.ClosedCurrentDay.
            // Refused like the old and overruled taps above: said on the
            // console and acknowledged, so the relay does not keep offering
            // it, and the phone's next look at the roster shows it unchanged.
            if (CheckInStore.ClosedCurrentDay(_raceEvents) is { } closedDay)
            {
                OnClientRawMessage($"[relay] Refused a check-{(tap.Wanted ? "in" : "out")} for {name} from their phone - this day is closed (\"{closedDay.DisplayName}\"). Start tonight's race day, or reopen that one, first.");
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

            // Not published: the empty roster PublishSnapshot would put in
            // its place anyway, built here so the check below compares what
            // actually goes. Unticking the box then sends it once, taking
            // the roster already on the relay down, rather than the full
            // roster being "unchanged" and nothing going at all.
            var document = !_settings.RelayPublishRoster
                ? RelayPublisher.WithdrawnRoster
                : JsonSerializer.Serialize(new
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
            var document = BuildStandingsDocument();

            // Remembered as sent, so the thirty-second check in
            // PublishExtrasToRelay does not send the same table again.
            _lastRelayDocuments[RelayChannel.Standings] = document;
            _relayPublisher.PublishSnapshot(RelayChannel.Standings, document);
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build standings: {ex.Message}");
        }
    }

    /// <summary>
    /// The standings document PublishStandingsToRelay sends. Split out so
    /// PublishExtrasToRelay can rebuild it on its thirty-second pass and
    /// send it only when it has changed.
    /// </summary>
    private string BuildStandingsDocument()
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
                    // Only what is on disk - see SessionHistoryStore.Saved.
                    EventSpine.SessionsFor(season, _raceEvents, _rounds, _sessionHistory.SavedView()),
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

        return document;
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

            // Standings and the results index were left out, and both change
            // only when a session is saved or a championship edited - weeks,
            // between meetings. After a relay redeploy the rest came back
            // within ten minutes and the standings page and the whole
            // archive stayed empty. Standings are rebuilt on the throttled
            // pass anyway, so forgetting them costs one send. The index is
            // sent again for every year sent this run, and this year's
            // whatever happened - not every year of history, which is the
            // burst RepublishRecentResults was written to avoid.
            _lastRelayDocuments.Remove(RelayChannel.Standings);

            var years = new HashSet<string>(_relayIndexSent.Keys, StringComparer.Ordinal)
            {
                DateTime.Now.Year.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
            };

            foreach (var year in years)
            {
                PublishResultsIndexForYear(year);
            }
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

        SendToRelayIfChanged(RelayChannel.Achievements, "achievements earned", () => RelayDocuments.Achievements(
            RelayAchievements(), _profiles, abbreviate));

        // Standings and the results index otherwise only go when a session
        // is saved or a result corrected. Plenty else changes them without
        // either: a session deleted in Race History (and SessionHistoryStore
        // raises nothing for a delete), a whole race day deleted, and every
        // edit on the Championship page - the points scheme, races ticked
        // or unticked, Scoring Rules, points carried in, a championship
        // renamed or deleted. The laptop's own Standings grid recomputes at
        // once, so the public page went on showing a table, and listing a
        // session, that the laptop no longer had - until the next race was
        // saved, which between meetings can be weeks. Checking here, rather
        // than hooking each of those places, catches the ones nobody has
        // thought of yet too, the same reasoning as KeepRacerPinsInStep.
        SendToRelayIfChanged(RelayChannel.Standings, "standings", BuildStandingsDocument);
        RepublishChangedResultsIndexYears();

        SendToRelayIfChanged(RelayChannel.Tracks, "the tracks", () => RelayDocuments.Tracks(
            _trackLayouts, _sessionHistory.SavedView(), _profiles, _pbResets, _raceEvents.Current.TrackLayoutName, abbreviate, _relayTrackImages));

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
            SendToRelayIfChanged(RelayChannel.Badges, "the achievement list", () => RelayDocuments.Badges(
                _settings.RacingAt,
                RelayAchievements().All.SelectMany(d => d.Unlocked).Select(u => u.Id).ToHashSet(StringComparer.Ordinal)));
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

            // Saved, not All: a race whose filing failed was never sent by
            // SessionAdded, and is not to be sent this way either.
            //
            // A session corrected in that time counts as recent too, however
            // long ago it was raced, and so does one whose correction could
            // not be sent at all. A correction goes once, from the same
            // queue in memory, and a race only 36 hours old was the whole
            // test - so Saturday's heat corrected on Monday with the relay
            // asleep, the laptop then shut, was never sent again, and the
            // relay served the uncorrected result for good. Owed takes in
            // every result queued and never taken as well, so a night's
            // results lost with the queue at shutdown go now, whatever
            // their age (see RelayResultsIndex.Ledger.Owed).
            var sent = _sessionHistory.Saved
                .Where(s => (s.EndedAtUtc > s.StartedAtUtc ? s.EndedAtUtc : s.StartedAtUtc) >= since
                    || (s.Edits is { } edits && edits.Any(e => e.AtUtc >= since))
                    || _relayPublisher.ResultIsOwed(s.Id))

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

            // And every driver who raced tonight sent their night again,
            // for the reason ResendDriverNights gives: a night is queued
            // once, when the race it changed was filed, so a relay that was
            // down for an hour of racing leaves one out of date with
            // nothing else to put it right.
            ResendDriverNights();
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

    /// <summary>
    /// The non-demo session ids in each year of history, as last seen by
    /// RepublishChangedResultsIndexYears. Cheap to work out every thirty
    /// seconds, unlike the index itself, so a year's index is only rebuilt
    /// when the sessions in it have changed.
    /// </summary>
    private readonly Dictionary<string, string> _relayIndexSessionsSeen = new(StringComparer.Ordinal);

    /// <summary>The last index document sent for each year, so one rebuilt to the same bytes is not sent again.</summary>
    private readonly Dictionary<string, string> _relayIndexSent = new(StringComparer.Ordinal);

    /// <summary>
    /// Republishes the results index for every year whose sessions have
    /// changed since the last pass - in practice, a session deleted, which
    /// raises no event. See PublishExtrasToRelay.
    ///
    /// The first time a year is seen it is only noted, not sent: nothing
    /// is known yet about what the relay was given before this run, and
    /// sending every year of history at startup is what
    /// RepublishRecentResults was written to avoid. A year whose last
    /// session has been deleted is still checked, so its index goes out
    /// empty rather than going on listing that session.
    /// </summary>
    private void RepublishChangedResultsIndexYears()
    {
        try
        {
            // Saved, not All: a race Add could not write would otherwise
            // change the year's ids and send an index listing a result the
            // relay was never given - see SessionHistoryStore.Saved.
            //
            // And only those the relay holds, as the index itself lists. A
            // result re-sent from a phone (results.publish) or sent again
            // once publishing is back on changes the year's index without
            // changing its ids, and nothing else republished it - the
            // re-sent result could not be found from the archive.
            var now = _sessionHistory.Saved
                .Where(s => !s.IsDemoSession())
                .Where(s => _relayPublisher.ResultIsOnRelay(s.Id, RelayResultsIndex.EffectiveStart(s)))
                .GroupBy(s => RelayResultsIndex.YearOf(s), StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => string.Join("|", g.Select(s => s.Id).OrderBy(id => id, StringComparer.Ordinal)),
                    StringComparer.Ordinal);

            foreach (var year in now.Keys.Union(_relayIndexSessionsSeen.Keys, StringComparer.Ordinal).ToList())
            {
                var current = now.TryGetValue(year, out var ids) ? ids : string.Empty;

                if (!_relayIndexSessionsSeen.TryGetValue(year, out var seen))
                {
                    // Unless an index for it has gone this run. A result
                    // now joins the index only once the relay has taken it,
                    // which is after SessionAdded sent its year's index - so
                    // the first heat of a new year (or to a new relay) was
                    // first seen here already delivered, only noted, and
                    // left out of the index until another heat was raced.
                    if (!_relayIndexSent.ContainsKey(year))
                    {
                        _relayIndexSessionsSeen[year] = current;
                        continue;
                    }

                    seen = string.Empty; // never equal: a year only in `now` has sessions
                }

                if (seen == current)
                {
                    continue;
                }

                _relayIndexSessionsSeen[year] = current;

                // A session added already sent its year's index from
                // SessionAdded, and that was remembered; the rebuild here
                // then matches it and nothing goes twice.
                PublishResultsIndexForYear(year, onlyIfChanged: true);
            }
        }
        catch (Exception ex)
        {
            if (_relayBuildErrors.Add($"resultsindex|{ex.Message}"))
            {
                OnClientRawMessage($"[relay] Could not check the results index: {ex.Message}");
            }
        }
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
    private void PublishResultsIndexForYear(string year, bool onlyIfChanged = false)
    {
        try
        {
            var json = RelayResultsIndex.Build(
                year,

                // Only what is on disk - see SessionHistoryStore.Saved.
                _sessionHistory.Saved,

                // A round belongs to a meeting, and a meeting usually to a
                // championship. The championship's name is what makes a
                // season picker worth having - one entry per season rather
                // than one per race night - so it is preferred, and the
                // meeting's own name is the fallback for a one-off.
                roundId => SeasonLabelFor(roundId),
                _settings.RelayAbbreviateNames,

                // What the relay was given, not merely everything since the
                // first session it was given: one raced while publishing
                // was unticked sat inside that window, and the archive
                // offered it with no result behind it.
                from: null,
                onRelay: s => _relayPublisher.ResultIsOnRelay(s.Id, RelayResultsIndex.EffectiveStart(s)));

            if (onlyIfChanged && _relayIndexSent.TryGetValue(year, out var last) && last == json)
            {
                return;
            }

            _relayIndexSent[year] = json;
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
    /// <param name="firstPublish">The session has just been saved (SessionAdded). Every other caller is a re-send - see RelayResultsIndex.MayAlreadyBeOnRelay.</param>
    private void PublishSessionToRelay(SessionRecord session, bool firstPublish = false)
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

                    // Their best through each sector, with a split loop -
                    // null without, which the page shows as nothing.
                    sectorsMs = Sectors.Bests(r),
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
            //
            // Whether the relay may hold it already - corrected, or sent
            // before - so a re-send that cannot go is remembered as owed
            // rather than the session dropped from the index: the relay
            // still holds the copy from before.
            _relayPublisher.PublishResult(
                session.Id, document, RelayResultsIndex.EffectiveStart(session),
                correction: RelayResultsIndex.MayAlreadyBeOnRelay(session, firstPublish));
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build the results document: {ex.Message}");
        }
    }

    /// <summary>
    /// TONIGHT'S RACES, and the one answer to that question on this
    /// laptop.
    ///
    /// Through <see cref="TonightAtTheTrack"/> rather than filtered again
    /// here: its night runs 6am to 6am and it is the same answer every
    /// pace figure on the page is read from. A second answer to "which
    /// races were tonight" is the sort of thing that agrees all season and
    /// then disagrees about a race that started at five to midnight - which
    /// is why the announcer's own RacesTonight calls this rather than
    /// holding a copy of it.
    /// </summary>
    private IReadOnlyList<SessionRecord> RacesTonight()
    {
        if (_rounds is null)
        {
            return Array.Empty<SessionRecord>();
        }

        return TonightAtTheTrack
            .Races(_sessionHistory.All, id => _rounds.Get(id), DateTime.Now)
            .Select(one => one.Session)
            .ToList();
    }

    /// <summary>
    /// Every driver in a race just filed, sent their own night - see
    /// <see cref="DriverNight"/>.
    ///
    /// ONLY THE DRIVERS IN THAT RACE, which is both cheaper and more
    /// nearly correct than sending everybody. A night is built from
    /// DriverCut.FactsFor over the whole evening, so each document is
    /// already their night ENTIRE rather than one race of it - and the only
    /// documents a filed race can have changed are those of the drivers who
    /// were in it. A driver who raced heat one and went home has a correct
    /// night from the moment heat one was filed, and re-sending it after
    /// every heat afterwards would be twenty documents a race to say the
    /// same thing.
    /// </summary>
    private void PublishDriverNightsFor(SessionRecord session)
    {
        // A demo race is a rehearsal rather than a result, exactly as
        // PublishSessionToRelay has it: it never reaches the club's public
        // results and it must not reach a driver's night either.
        if (!session.IsDemoSession())
        {
            PublishNightsFor(DriversIn(session));
        }
    }

    /// <summary>
    /// Every driver who has raced tonight, sent their night again.
    ///
    /// FROM THE RE-SEND PASS ONLY, where the recent results go after a
    /// connection has come back. A night is queued once, when the race it
    /// changed was filed, and the queue drops its oldest when it fills - so
    /// a laptop shut with a full queue, or a relay that was down all
    /// evening, can leave a driver's night a race or two out of date with
    /// nothing to put it right. This is what puts it right, in the one
    /// place in this file that already exists for exactly that problem.
    ///
    /// EACH DRIVER ONCE across the whole evening, and not once per race
    /// they were in. The document is their night entire either way, so a
    /// driver in four heats sent four times is the same four kilobytes
    /// written four times into a queue five hundred deep that drops its
    /// oldest when it fills - which on the one pass that exists to catch up
    /// is the last thing worth spending it on.
    /// </summary>
    private void ResendDriverNights() =>
        PublishNightsFor(RacesTonight()
            .Where(race => race is not null && !race.IsDemoSession())
            .SelectMany(DriversIn)
            .Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// The documents themselves: one per driver, built from their whole
    /// evening and keyed by their badge id.
    ///
    /// A CAR NOBODY HAS NAMED GETS NOTHING. The results page publishes
    /// those under their transponder number, because a results table with
    /// a hole in it is worse. A document whose only subject is a number is
    /// not somebody's night, and the badge it would be keyed to is one
    /// nobody has been given.
    ///
    /// NOTHING HERE MAY THROW INTO THE FILING PATH. This runs from
    /// SessionHistoryStore.SessionAdded, on the thread that has just saved
    /// a race, and a driver's night is a courtesy. One profile that cannot
    /// be read, or a secret that has gone, leaves the result filed and
    /// published exactly as before this existed.
    /// </summary>
    private void PublishNightsFor(IEnumerable<string> drivers)
    {
        if (!_settings.PublishDriverNightsToTheRelay)
        {
            return;
        }

        try
        {
            // A club that never set up online check-in has no badges, so
            // there is no id to key a night to. Not a fault and not worth a
            // line in the console every race: the box cannot sensibly be
            // ticked without check-in, and the Club Website page is where
            // that belongs being said.
            var secret = _settings.RelayCheckInSecret;

            if (string.IsNullOrWhiteSpace(secret))
            {
                return;
            }

            var tonight = RacesTonight();

            if (tonight.Count == 0)
            {
                return;
            }

            var night = TonightAtTheTrack.NightOf(DateTime.Now);
            var dayName = _raceEvents.Active?.Name;
            var atUtc = DateTime.UtcNow;
            var sent = 0;

            foreach (var who in drivers)
            {
                // THE FULL NAME, not the club's abbreviated one. See the
                // note at the top of DriverNight: a results table is a
                // list of other people and shortening it is right there,
                // but a driver who opens their own night and reads
                // "B. Hargrove" has been shown a bug.
                var named = _profiles.Get(who)?.DriverName;

                if (string.IsNullOrWhiteSpace(named))
                {
                    continue;
                }

                var facts = DriverCut.FactsFor(who, named, tonight, _trackLayouts);
                var document = DriverNight.Document(named, night, dayName, facts, atUtc);

                if (document is null)
                {
                    continue;
                }

                if (_relayPublisher.PublishDriverNight(RelayCheckIn.IdFor(who, secret), document))
                {
                    sent++;
                }
            }

            if (sent > 0)
            {
                OnClientRawMessage($"[relay] {Plural.Of(sent, "driver")} had their own night published.");
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build the drivers' own nights: {ex.Message}");
        }
    }

    /// <summary>
    /// Everybody who really raced in one session: no ghosts, no blanks, and
    /// each transponder once however many rows carry it.
    /// </summary>
    private static IEnumerable<string> DriversIn(SessionRecord session) =>
        session.Results
            .Select(row => row.Transponder)
            .Where(who => !string.IsNullOrWhiteSpace(who) && !GhostService.IsGhost(who))
            .Select(who => who.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
}
