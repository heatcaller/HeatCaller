using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Driver sign-up online, on the laptop's side: the invites, the sign-ups
/// waiting for the desk, and what happens when the desk answers one.
///
/// A new driver is invited (an emailed code, or one read out at the desk),
/// signs up on the club's sign-up page from their own phone, and waits. The
/// relay holds the sign-up until the desk answers it; RelayPublisher
/// collects every one waiting and hands the list here. Approving one makes
/// the driver's profile through ProfileStore.TryAdd - the desk's own add
/// path, which refuses a transponder somebody already has - asks whether to
/// put them in today's event, checks them in if so, gives them their own
/// check-in PIN and emails it, and tells their phone.
///
/// Nothing is taken off the relay by collecting: a sign-up goes only when
/// the desk answers it, so a laptop that closes mid-way loses nothing, and
/// an answer made while the relay is out of reach is sent when it is back.
/// If the relay offers a sign-up again that this laptop has already
/// answered - the answer was lost - the same answer goes again, from the
/// invite's own record.
///
/// A sign-up made with an invite this laptop has no record of is never
/// answered automatically: it waits, marked as such (see IsFromUnknownInvite).
/// The invite may be another laptop's publishing as the same club, or one
/// from a signups.json that could not be read - and a "no" is final on the
/// relay, so guessing wrong would throw away a real sign-up.
///
/// UI thread only, like the stores it works on.
/// </summary>
internal sealed class RelaySignupDesk
{
    private readonly AppSettings _settings;
    private readonly ProfileStore _profiles;
    private readonly CheckInStore _checkIns;
    private readonly RaceEventStore _events;
    private readonly RaceClassStore _classes;
    private readonly RentalTransponderStore _rentals;
    private readonly RelayPublisher _publisher;
    private readonly Action<string> _log;

    private List<RelaySignupRequest> _waiting = new();

    /// <summary>Sign-ups already mentioned in the console, so each is announced once.</summary>
    private readonly HashSet<string> _announced = new(StringComparer.OrdinalIgnoreCase);

    public RelaySignupDesk(
        AppSettings settings,
        ProfileStore profiles,
        CheckInStore checkIns,
        RaceEventStore events,
        RaceClassStore classes,
        RentalTransponderStore rentals,
        RelayPublisher publisher,
        Action<string> log)
    {
        _settings = settings;
        _profiles = profiles;
        _checkIns = checkIns;
        _events = events;
        _classes = classes;
        _rentals = rentals;
        _publisher = publisher;
        _log = log;

        Store = SignupStore.Load();
        _publisher.SignupsWanted = Store.Enabled;
    }

    public SignupStore Store { get; }

    /// <summary>
    /// Checks a driver in as the desk does - MainForm's ApplyPhoneCheckIn,
    /// which also times the change against phone taps and sends the list
    /// again. Set by the host.
    /// </summary>
    public Func<string, bool, bool>? CheckIn { get; set; }

    /// <summary>
    /// A driver was just added from a sign-up: give them their check-in PIN
    /// and email it. Set by the host. Returns whether a PIN email has been
    /// started for them - which decides what their phone is told.
    /// </summary>
    public Func<string, bool>? RacerAdded { get; set; }

    /// <summary>The waiting list or the invites changed - for the Entries button, the sidebar and an open Sign-ups window.</summary>
    public event Action? Changed;

    /// <summary>Sign-ups waiting for the desk, oldest first.</summary>
    public IReadOnlyList<RelaySignupRequest> Waiting => _waiting;

    /// <summary>
    /// Why sign-up isn't working, or null. Includes the relay refusing the
    /// sign-up document itself - while it does, every invite's link opens a
    /// page that doesn't know the code.
    /// </summary>
    public string? Problem => _publisher.SignupProblem;

    public DateTime? LockedUntilUtc => _publisher.RelaySignupLockedUntilUtc;

    private string Club => _settings.RelayClub.Trim().ToLowerInvariant();

    /// <summary>
    /// Whether this club has ever used sign-up - switched it on, or made an
    /// invite. Until then there is nothing to publish. Never while
    /// signups.json could not be read: a document from this session's empty
    /// store would take every real invite's code off the relay.
    /// </summary>
    public bool InUse => !Store.LoadedUnreadable && (Store.Enabled || Store.All.Count > 0);

    /// <summary>Why invites can't be made and sign-up can't be switched on this session, or null. See SignupStore.LoadedUnreadable.</summary>
    public string? Held => Store.LoadedUnreadable
        ? "The sign-up invites file (signups.json) couldn't be read when HeatCaller started - another program may have had it open. "
          + "Nothing here can be changed until HeatCaller is restarted and can read it, so the invites in it aren't lost. "
          + "Close anything that might have it open (a backup or sync tool is the usual cause) and restart HeatCaller."
        : null;

    public void SetEnabled(bool on)
    {
        // Refused while held - see InUse. The Sign-ups window says why.
        if (Store.LoadedUnreadable)
        {
            return;
        }

        Store.Enabled = on;
        Store.Save();
        _publisher.SignupsWanted = on;
        Changed?.Invoke();
    }

    // ---------------------------------------------------------- the relay

    /// <summary>
    /// The signup document for the relay: open or not, the classes a new
    /// driver can pick - today's event's, or every class when it names none
    /// - and the hash of every invite still usable.
    /// </summary>
    public string Document()
    {
        var current = _events.Current;
        var eventName = current.Id == RaceEventStore.LegacyEventId ? string.Empty : current.Name;

        var classes = current.ClassNames.Count > 0
            ? current.ClassNames.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : _classes.All.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();

        return Store.Document(_settings, eventName, classes);
    }

    /// <summary>
    /// Every sign-up the relay is holding, as of its latest answer. Those
    /// this laptop has already answered are answered again; the rest are
    /// the waiting list.
    /// </summary>
    public void Collected(IReadOnlyList<RelaySignupRequest> items)
    {
        var club = Club;
        var waiting = new List<RelaySignupRequest>();

        foreach (var item in items.OrderBy(i => i.SubmittedUtc))
        {
            var invite = Store.ByHash(item.Hash, club);

            if (invite is null)
            {
                // Not an invite this laptop knows - see the class summary.
                // Left waiting on the relay, unanswered, and shown to the
                // desk as such; turning it down is the desk's call.
                waiting.Add(item);

                if (_announced.Add(item.Identity))
                {
                    // No name - see A35-2 (17 Oct 2026, Bug Hunt 8) at Reject.
                    _log($"[signup] Someone signed up online with an invite this laptop doesn't know - made on another laptop, "
                        + "or lost from this one. It's waiting under Online Sign-ups on Race Day → Who's Racing.");
                }

                continue;
            }

            if (invite.State == SignupInviteState.Cancelled)
            {
                Answer(item.Hash, approved: false, "That invite was canceled. Ask at the timing desk.", checkedIn: false, driver: null);
                continue;
            }

            if (invite.State == SignupInviteState.Approved)
            {
                SendApproved(invite);
                continue;
            }

            if (invite.State == SignupInviteState.Rejected)
            {
                Answer(item.Hash, approved: false, invite.AnswerMessage, checkedIn: false, driver: null);
                continue;
            }

            waiting.Add(item);

            // By the relay's own time for it, not SubmittedUtc - see
            // RelaySignupRequest.SubmittedAtRaw.
            if (_announced.Add(item.Identity))
            {
                var what = item.ClassName.Length > 0 ? $" for {item.ClassName}" : string.Empty;
                _log($"[signup] A new driver signed up online{what}. Approve them under Online Sign-ups on Race Day → Who's Racing.");
            }
        }

        var changed = waiting.Count != _waiting.Count
            || waiting.Zip(_waiting).Any(pair => !pair.First.SameSignup(pair.Second));

        _waiting = waiting;

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Whether a waiting sign-up was made with an invite this laptop has no
    /// record of (see the class summary). It can be turned down here, but
    /// not approved: approving needs the invite.
    /// </summary>
    public bool IsFromUnknownInvite(RelaySignupRequest request) => Store.ByHash(request.Hash, Club) is null;

    /// <summary>Tells a phone what the desk decided. Queued in the publisher, which posts it until the relay takes it.</summary>
    private void Answer(string hash, bool approved, string message, bool checkedIn, object? driver)
    {
        var json = JsonSerializer.Serialize(new
        {
            h = hash,
            state = approved ? "approved" : "rejected",
            message,
            checkedIn,
            driver,
        });

        _publisher.AnswerSignup(hash, json);
    }

    /// <summary>An approval, sent (or sent again) from the invite's own record and the driver as they are now.</summary>
    private void SendApproved(SignupInvite invite)
    {
        // Only the driver whose OWN transponder this is. Get also follows an
        // alternate transponder to its owner, so once the approved driver
        // was deleted and their old transponder became somebody else's
        // second car, a re-sent answer used to hand this phone that other
        // driver's name and working check-in link. A club rental's own
        // profile ("Rental R4") isn't the person who signed up either, and
        // its check-in link would check in whoever has the unit next.
        var profile = _profiles.Get(invite.Transponder) is { IsRental: false } found
            && string.Equals(found.Transponder, invite.Transponder, StringComparison.OrdinalIgnoreCase)
                ? found
                : null;
        object? driver = null;

        if (profile is not null && _settings.RelayCheckInEnabled && !string.IsNullOrWhiteSpace(_settings.RelayCheckInSecret))
        {
            driver = new
            {
                id = RelayCheckIn.IdFor(profile.Transponder, _settings.RelayCheckInSecret),
                name = NameOf(profile),
                className = profile.ClassName,
                number = profile.RaceNumber,
            };
        }

        Answer(invite.Hash, approved: true, invite.AnswerMessage, invite.AnswerCheckedIn, driver);
    }

    // ------------------------------------------------------------ answers

    /// <summary>The event a newly approved driver can be put in, or null when there is no real event open - only today's check-in sheet.</summary>
    public RaceEvent? EventForEntry()
    {
        var current = _events.Current;
        return current.Id == RaceEventStore.LegacyEventId ? null : current;
    }

    /// <summary>Whether putting a driver of this class in today's event means adding the class to it.</summary>
    public bool ClassNeedsAdding(string className) =>
        EventForEntry() is { } current
        && current.ClassNames.Count > 0
        && className.Trim().Length > 0
        && !current.ClassNames.Contains(className.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Approves a sign-up: adds the driver - or, when a club rental was
    /// picked, lends it to them by name instead, with no profile of their
    /// own - puts them in today's event and checks them in when asked to,
    /// gives them their PIN and tells their phone. False, with the
    /// reason, when the driver could not be added - their transponder is
    /// somebody else's, most likely - and nothing has changed.
    /// </summary>
    public bool Approve(RelaySignupRequest request, DriverProfile profile, string? rentalUnit, bool enterEvent, out string refusal)
    {
        var invite = Store.ByHash(request.Hash, Club);

        if (invite is null)
        {
            refusal = "This sign-up used an invite this laptop doesn't know - one made on another laptop, or lost from this one. "
                + "Approve it on the laptop that sent the invite, or turn it down here.";
            return false;
        }

        if (invite.State != SignupInviteState.Open)
        {
            refusal = "That sign-up has already been dealt with.";
            return false;
        }

        var name = NameOf(profile);

        // Noted as each step is taken, for UndoApprove.
        string? rentalLent = null;
        DateTime? rentalOutUtc = null;
        string? eventWithClassAdded = null;
        string? classAdded = null;

        // The transponder they race on today: their own, or the club's unit.
        var transponder = profile.Transponder;
        var profileAdded = false;

        if (!string.IsNullOrWhiteSpace(rentalUnit))
        {
            // Lent by name only, as at the counter - no profile for them on
            // the club's unit. One used to be added, keyed on the rental's
            // transponder: every later borrower's laps were then announced
            // and published under this driver's name, the next sign-up lent
            // that unit was refused as a clash, and this driver was kept out
            // of records for good. Refused before anything changes when the
            // unit can't be lent, since without the loan nothing would
            // record who has it.
            if (_rentals.Get(rentalUnit) is not { } unit || !_rentals.CheckOut(unit.Transponder, name, string.Empty))
            {
                refusal = $"Rental {rentalUnit} can't be lent out - it may be out already, or retired. Check the Rentals page, then approve them again.";
                return false;
            }

            transponder = unit.Transponder;
            rentalLent = unit.Transponder;
            rentalOutUtc = unit.CheckedOutUtc;
        }
        else
        {
            if (!_profiles.TryAdd(profile, out refusal))
            {
                return false;
            }

            profileAdded = true;
        }

        refusal = string.Empty;

        var checkedIn = false;

        if (enterEvent)
        {
            var current = EventForEntry();

            if (current is not null && ClassNeedsAdding(profile.ClassName))
            {
                current.ClassNames.Add(profile.ClassName.Trim());
                _events.Upsert(current);
                eventWithClassAdded = current.Id;
                classAdded = profile.ClassName.Trim();
                _log($"[signup] Added {profile.ClassName.Trim()} to {current.DisplayName} for an online sign-up.");
            }

            CheckIn?.Invoke(transponder, true);
            checkedIn = _checkIns.IsCheckedIn(transponder);
        }

        var wasAnsweredUtc = invite.AnsweredUtc;
        var wasTransponder = invite.Transponder;
        var wasAnswerCheckedIn = invite.AnswerCheckedIn;

        invite.State = SignupInviteState.Approved;
        invite.AnsweredUtc = DateTime.UtcNow;
        invite.Transponder = transponder;
        invite.AnswerCheckedIn = checkedIn;

        // Decided by what the PIN step actually did, not guessed from the
        // settings beforehand: it can find the mail already busy, or give
        // no PIN at all, and the phone was then promised an email that
        // never came.
        // The approval is written down first, so a PIN step that throws
        // can never leave an approved invite that only lives in memory.
        // WHY (23 Oct 2026, bug hunt 10): the save's result used to be
        // ignored. With signups.json held by a sync or backup tool the
        // approval lived only in memory, yet the phone was still told it
        // was in and the relay kept that answer - after a restart the
        // invite was back to Open with no record of who it became, and
        // could never be approved again. Now a failed save puts every step
        // back here (as UndoApprove does) and nothing goes to the relay.
        // No new code: the relay hasn't been answered for this one.
        if (!Store.Save())
        {
            invite.State = SignupInviteState.Open;
            invite.AnsweredUtc = wasAnsweredUtc;
            invite.Transponder = wasTransponder;
            invite.AnswerCheckedIn = wasAnswerCheckedIn;

            RollBack(new ApprovalReceipt(invite.Id, name, transponder, rentalLent, rentalOutUtc,
                eventWithClassAdded, classAdded, checkedIn, _checkIns.CurrentEventId, profileAdded));

            _log("[signup] Couldn't save approving an online sign-up, so it was put back and nothing was sent.");
            refusal = "The approval couldn't be saved, so nothing was changed or sent. "
                + "Close anything that might have signups.json open (a backup or sync tool is the usual cause) and try again.";
            return false;
        }

        bool pinComing;
        try
        {
            // A PIN belongs to a profile, and a driver on a rental has none
            // of their own to give one to.
            pinComing = profileAdded && (RacerAdded?.Invoke(transponder) ?? false);
        }
        catch (Exception ex)
        {
            _log($"[signup] Could not give an approved online sign-up their check-in PIN: {ex.Message}");
            pinComing = false;
        }

        invite.AnswerMessage = pinComing ? "Your own check-in PIN is on its way by email." : string.Empty;

        // WHY (23 Oct 2026, bug hunt 10): this save adds only the message;
        // the approval itself is on disk already. A failure is said in the
        // console rather than ignored - the next signups.json save writes it.
        if (!Store.Save())
        {
            _log("[signup] Couldn't save the message for an approved online sign-up - it is kept and saved with the next change.");
        }

        SendApproved(invite);

        _waiting.RemoveAll(w => string.Equals(w.Hash, request.Hash, StringComparison.OrdinalIgnoreCase));
        _log($"[signup] Approved an online sign-up{(checkedIn ? " and checked them in" : string.Empty)}"
            + (rentalLent is null
                ? "."
                : $", lending them rental {rentalLent}. They have no profile of their own until they bring their own transponder - its laps go under the rental's name."));

        LastApproval = new ApprovalReceipt(invite.Id, name, transponder, rentalLent, rentalOutUtc,
            eventWithClassAdded, classAdded, checkedIn, _checkIns.CurrentEventId, profileAdded);

        Changed?.Invoke();
        return true;
    }

    // --------------------------------------------------------------- undo
    //
    // What the phone was told can't be taken back: an answer goes to the
    // relay within seconds, and the relay keeps it against that invite's
    // code (for as long as it keeps any sign-up, two weeks) and refuses any
    // new sign-up made with the code - "The timing desk has already
    // answered this sign-up." There is no way to withdraw an answer from
    // the relay. So each undo here puts things back on THIS laptop, and an
    // invite whose sign-up was answered is opened again under a NEW code
    // (see Reopen): reopening it under the old one used to tell the desk
    // the code worked again when the relay would never take it. The console
    // says what the phone was told, and that the new code has to reach them.

    /// <summary>What one approval did, for UndoApprove.</summary>
    public sealed record ApprovalReceipt(
        string InviteId,
        string Name,
        string Transponder,
        string? RentalUnit,
        DateTime? RentalOutUtc,
        string? EventWithClassAdded,
        string? ClassAdded,
        bool CheckedIn,
        string CheckInEventId,
        bool ProfileAdded = true);

    /// <summary>What the last successful Approve did - read straight after it, for the undo.</summary>
    public ApprovalReceipt? LastApproval { get; private set; }

    /// <summary>The invite a waiting sign-up was made with, or null when this laptop doesn't know it.</summary>
    public SignupInvite? InviteFor(RelaySignupRequest request) => Store.ByHash(request.Hash, Club);

    /// <summary>
    /// Undoes an approval here: the driver's check-in, their rental loan,
    /// the class added to the event for them (unless somebody else in that
    /// class is checked in there now), and their profile go; the invite is
    /// open again. Their phone has already been told they're in, and any
    /// PIN email has gone - neither can be recalled, and the log says so.
    /// Their PIN is taken back by the next PIN check, as for any deleted
    /// driver. Throws when the sign-up has changed since.
    /// </summary>
    public void UndoApprove(ApprovalReceipt receipt)
    {
        if (Store.Get(receipt.InviteId) is not { State: SignupInviteState.Approved } invite
            || !string.Equals(invite.Transponder, receipt.Transponder, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{receipt.Name}'s sign-up has changed since, so it was left as it is.");
        }

        // Reopened (and saved) first: Reopen throws when signups.json can't
        // be written (Bug hunt 11, 2026-11-05), and the approval's
        // check-in, rental and profile must then stay as the invite says.
        Reopen(invite, newCode: true);

        RollBack(receipt);

        _log($"[signup] Undid approving an online sign-up. Their phone was already told they're in"
            + " - that can't be taken back, so let them know in person. " + NewCodeNote);
    }

    /// <summary>
    /// Puts back on this laptop what one approval did - check-in, rental
    /// loan, profile, the class added to the event - but not the invite
    /// itself. Shared by UndoApprove and by Approve when its save fails
    /// (23 Oct 2026, bug hunt 10).
    /// </summary>
    private void RollBack(ApprovalReceipt receipt)
    {
        var transponder = receipt.Transponder;

        // Checked out first, through the desk's own path, while the profile
        // it looks up is still there. Only on the sheet they were checked
        // in on - after a switch of race day there is nothing on show to undo.
        if (receipt.CheckedIn
            && string.Equals(_checkIns.CurrentEventId, receipt.CheckInEventId, StringComparison.Ordinal)
            && _checkIns.IsCheckedIn(transponder))
        {
            if (CheckIn is not null)
            {
                CheckIn(transponder, false);
            }
            else
            {
                _checkIns.SetCheckedIn(transponder, false);
            }
        }

        if (receipt.RentalUnit is { } unit && receipt.RentalOutUtc is { } outAt && !_rentals.UndoCheckOut(unit, outAt))
        {
            _log($"[signup] Rental {unit} has changed hands since that sign-up was approved, so it was left as it is.");
        }

        // Only a profile this approval added: one lent a rental added none,
        // and the unit's own "Rental R4" profile must stay.
        if (receipt.ProfileAdded
            && _profiles.Get(transponder) is { } profile
            && string.Equals(profile.Transponder, transponder, StringComparison.OrdinalIgnoreCase))
        {
            _profiles.Delete(transponder);
        }

        if (receipt.EventWithClassAdded is { } eventId && receipt.ClassAdded is { } className && _events.Get(eventId) is { } raceEvent)
        {
            var othersInClass = _checkIns.CheckedInAt(eventId).Any(t =>
                !string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_profiles.Get(t)?.ClassName?.Trim(), className, StringComparison.OrdinalIgnoreCase));

            if (!othersInClass && raceEvent.ClassNames.RemoveAll(c => string.Equals(c?.Trim(), className, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                _events.Upsert(raceEvent);
            }
        }
    }

    /// <summary>Undoes turning a sign-up down, here: the invite is open again, under a new code. The phone has been told no, and the relay keeps that answer against the old code, so they sign up again with the new one.</summary>
    public void UndoReject(string inviteId, string name)
    {
        if (Store.Get(inviteId) is not { State: SignupInviteState.Rejected } invite)
        {
            throw new InvalidOperationException($"{name}'s invite has changed since, so it was left as it is.");
        }

        Reopen(invite, newCode: true);

        _log($"[signup] Undid turning down an online sign-up. Their phone was already told no - that can't be taken back. " + NewCodeNote);
    }

    /// <summary>
    /// Undoes cancelling an invite, here: it is open again. With no sign-up
    /// made with it yet, the relay has no answer for its code and the same
    /// code works again. A sign-up already turned down by the cancel has to
    /// be made again, and under a new code - the relay keeps that "no"
    /// against the old one.
    /// </summary>
    public void UndoCancel(string inviteId, bool signupWasTurnedDown)
    {
        if (Store.Get(inviteId) is not { State: SignupInviteState.Cancelled } invite)
        {
            throw new InvalidOperationException("that invite has changed since, so it was left as it is.");
        }

        // Always under a new code. Besides the sign-up the cancel itself
        // turned down, one that reached the relay just before the cancel -
        // or before the republished document dropped the code - is answered
        // "That invite was cancelled" when it is collected (Collected), and
        // the relay then refuses the old code for good. The desk can't
        // always know that happened, so the old code is never promised to
        // work again.
        Reopen(invite, newCode: true);

        // Not ForText: its note is whatever the desk typed, often a name -
        // see A35-2 (17 Oct 2026, Bug Hunt 8) at Reject.
        _log("[signup] Undid canceling an invite."
            + (signupWasTurnedDown
                ? " The sign-up already made with it was told it was canceled - that can't be taken back, so they need to sign up again. "
                : " ")
            + NewCodeNote);
    }

    /// <summary>What the console says after an undo that gave the invite a new code. The code itself is not logged: the log goes into support bundles.</summary>
    private const string NewCodeNote =
        "Their old invite code can't be used again (the club website keeps the answer it was given), so the invite has a new code: "
        + "send it to them with Email Again, or read it out with Show Code, under Online Sign-ups.";

    /// <summary>
    /// An invite back to unanswered, saved, and republished with the next
    /// sign-up document. With newCode, under a fresh code (SignupStore.NewCode)
    /// - for an invite whose sign-up the relay has had an answer for, since
    /// the relay refuses the old code from then on and only the new hash in
    /// the next document is open to a sign-up.
    /// </summary>
    private void Reopen(SignupInvite invite, bool newCode)
    {
        var was = (invite.Code, invite.Club, invite.Salt, invite.EmailedUtc, invite.State, invite.AnsweredUtc,
            invite.Transponder, invite.AnswerMessage, invite.AnswerCheckedIn, invite.ExpiresUtc);

        if (newCode)
        {
            Store.NewCode(invite, _settings);

            // A new code is a new invite to them, so it gets an invite's
            // time. The sign-up can be answered after the invite's own two
            // weeks ran out (the relay holds it for its own two weeks), and
            // the invite then came back already expired: never published,
            // while the console said to send the new code (Bug hunt 17).
            var fresh = DateTime.UtcNow + SignupStore.InviteLifetime;

            if (invite.ExpiresUtc < fresh)
            {
                invite.ExpiresUtc = fresh;
            }
        }

        invite.State = SignupInviteState.Open;
        invite.AnsweredUtc = null;
        invite.Transponder = string.Empty;
        invite.AnswerMessage = string.Empty;
        invite.AnswerCheckedIn = false;

        // WHY (2026-11-05, Bug hunt 11): the save's result used to be
        // ignored, as Approve's was before bug hunt 10. With signups.json
        // held by a sync or backup tool the new code lived only in memory,
        // was published and could be emailed - and after a restart the
        // invite was back under its old code, so the code sent never
        // worked. Now everything is put back and the undo reports it failed.
        if (!Store.Save())
        {
            (invite.Code, invite.Club, invite.Salt, invite.EmailedUtc, invite.State, invite.AnsweredUtc,
                invite.Transponder, invite.AnswerMessage, invite.AnswerCheckedIn, invite.ExpiresUtc) = was;

            throw new InvalidOperationException("the invite couldn't be saved, so it was left as it was. "
                + "Close anything that might have signups.json open (a backup or sync tool is the usual cause) and try again.");
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Turns a sign-up down, with a line for their phone. False, with
    /// nothing changed or sent, when the invite's new state couldn't be saved.
    /// </summary>
    public bool Reject(RelaySignupRequest request, string message)
    {
        var text = string.IsNullOrWhiteSpace(message)
            ? "The timing desk couldn't take this sign-up."
            : message.Trim();

        if (Store.ByHash(request.Hash, Club) is { State: SignupInviteState.Open } invite)
        {
            var wasAnsweredUtc = invite.AnsweredUtc;
            var wasMessage = invite.AnswerMessage;

            invite.State = SignupInviteState.Rejected;
            invite.AnsweredUtc = DateTime.UtcNow;
            invite.AnswerMessage = text;

            // WHY (2026-11-05, Bug hunt 11): as Approve (bug hunt 10). The
            // save's result used to be ignored, and the "no" went to the
            // relay anyway - which then refuses the code for good - while
            // the next launch read the invite back as Open. Now a failed
            // save puts the invite back, the sign-up stays waiting, and
            // nothing is sent.
            if (!Store.Save())
            {
                invite.State = SignupInviteState.Open;
                invite.AnsweredUtc = wasAnsweredUtc;
                invite.AnswerMessage = wasMessage;
                _log("[signup] Couldn't save turning down an online sign-up, so it is still waiting and nothing was sent. "
                    + "Close anything that might have signups.json open (a backup or sync tool is the usual cause) and try again.");
                Changed?.Invoke();
                return false;
            }
        }

        Answer(request.Hash, approved: false, text, checkedIn: false, driver: null);
        _waiting.RemoveAll(w => string.Equals(w.Hash, request.Hash, StringComparison.OrdinalIgnoreCase));
        // NO NAMES IN THE LOG (17 Oct 2026, Bug Hunt 8, A35-2): the session log
        // goes into support bundles, and the Redactor knows only Driver Hub
        // profiles - a sign-up turned down, still waiting, on a rental or
        // undone has none, so its name went out in plain text.
        _log("[signup] Turned down an online sign-up.");
        Changed?.Invoke();
        return true;
    }

    // ------------------------------------------------------------ invites

    /// <summary>
    /// New invites, one per address (or one with none, for somebody at the
    /// desk). Saved; the relay has them within a couple of seconds. Throws
    /// InvalidOperationException, with the reason to show, when they could
    /// not be saved - and then none is kept, published or sent.
    /// </summary>
    public IReadOnlyList<SignupInvite> CreateInvites(IEnumerable<(string Email, string Note)> people)
    {
        if (Held is { } held)
        {
            throw new InvalidOperationException(held);
        }

        var made = people.Select(p => Store.Create(_settings, p.Email, p.Note)).ToList();

        // Taken back out when they could not be saved. Kept in memory, they
        // were emailed and published straight away, and the next launch
        // read the file without them: every code just sent stopped working.
        if (!Store.Save())
        {
            foreach (var invite in made)
            {
                Store.Remove(invite);
            }

            throw new InvalidOperationException("The invites couldn't be saved, so none were made or sent. "
                + "Close anything that might have signups.json open (a backup or sync tool is the usual cause) and try again.");
        }

        Changed?.Invoke();
        return made;
    }

    /// <summary>
    /// Emails these invites, and records which went. Owned here rather
    /// than by the Sign-ups window: closing the window used to cancel the
    /// batch half-way, leaving some of the people just invited with no
    /// email - and the ones that had gone not marked as sent. The desk
    /// lives as long as the app, so the batch finishes and is recorded
    /// whether or not the window is still open to show the result.
    ///
    /// Call on the UI thread (PinMailer reads settings before its first
    /// await, and this marks the invites after it, back on this thread).
    /// </summary>
    public async Task<MailResult> EmailInvitesAsync(IReadOnlyList<SignupInvite> invites, IProgress<string>? progress)
    {
        MailResult result;

        try
        {
            result = await PinMailer.SendInvitesAsync(_settings, invites, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Said here as well as to the caller, which may be a window
            // that has closed since.
            _log($"[signup] Couldn't email the invites: {ex.Message}");
            throw;
        }

        MarkEmailed(result);

        // For when the window that asked has been closed: somebody should
        // still be able to see how it went.
        _log($"[signup] {result.Message}"
            + (result.Failures.Count > 0 ? $" {Plural.Of(result.Failures.Count, "invite")} couldn't be emailed." : string.Empty));

        return result;
    }

    /// <summary>Records which invites an email run sent.</summary>
    public void MarkEmailed(MailResult result)
    {
        var any = false;

        foreach (var sent in result.Sent)
        {
            // Only with the code that went, and while it is still open. A
            // batch copies every code before its first email, so an invite
            // cancelled, or given a new code by an undo, part-way through
            // was marked emailed - and listed as sent a code it never had.
            if (Store.Get(sent.Transponder) is { State: SignupInviteState.Open } invite
                && string.Equals(invite.Code, sent.Pin, StringComparison.Ordinal))
            {
                invite.EmailedUtc = DateTime.UtcNow;
                any = true;
            }
        }

        if (any)
        {
            Store.Save();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Cancels an invite that has not been used. A sign-up already made with
    /// it is turned down. False, with nothing changed or sent, when it isn't
    /// open or the cancel couldn't be saved.
    /// </summary>
    public bool Cancel(SignupInvite invite)
    {
        if (invite.State != SignupInviteState.Open)
        {
            return false;
        }

        var wasAnsweredUtc = invite.AnsweredUtc;

        invite.State = SignupInviteState.Cancelled;
        invite.AnsweredUtc = DateTime.UtcNow;

        // WHY (2026-11-05, Bug hunt 11): as Reject. A cancel that couldn't be
        // saved came back Open after a restart and its code worked again,
        // while a sign-up made with it had already been told it was canceled.
        if (!Store.Save())
        {
            invite.State = SignupInviteState.Open;
            invite.AnsweredUtc = wasAnsweredUtc;
            _log("[signup] Couldn't save canceling an invite, so it is still open and nothing was sent. "
                + "Close anything that might have signups.json open (a backup or sync tool is the usual cause) and try again.");
            Changed?.Invoke();
            return false;
        }

        var waiting = _waiting.FirstOrDefault(w => string.Equals(w.Hash, invite.Hash, StringComparison.OrdinalIgnoreCase));

        if (waiting is not null)
        {
            Answer(waiting.Hash, approved: false, "That invite was canceled. Ask at the timing desk.", checkedIn: false, driver: null);
            _waiting.Remove(waiting);
        }

        Changed?.Invoke();
        return true;
    }

    private static string NameOf(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();
}
