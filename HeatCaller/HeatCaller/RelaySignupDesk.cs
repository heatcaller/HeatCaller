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

    /// <summary>Whether this club has ever used sign-up - switched it on, or made an invite. Until then there is nothing to publish.</summary>
    public bool InUse => Store.Enabled || Store.All.Count > 0;

    public void SetEnabled(bool on)
    {
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
                    _log($"[signup] {item.Name} signed up online with an invite this laptop doesn't know - made on another laptop, "
                        + "or lost from this one. It's waiting under Online Sign-ups on Race Day → Who's Racing.");
                }

                continue;
            }

            if (invite.State == SignupInviteState.Cancelled)
            {
                Answer(item.Hash, approved: false, "That invite was cancelled. Ask at the timing desk.", checkedIn: false, driver: null);
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
                _log($"[signup] {item.Name} signed up online{what}. Approve them under Online Sign-ups on Race Day → Who's Racing.");
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
        var profile = _profiles.Get(invite.Transponder);
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
    /// Approves a sign-up: adds the driver, lends them a club rental if one
    /// was picked, puts them in today's event and checks them in when asked
    /// to, gives them their PIN and tells their phone. False, with the
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

        if (!_profiles.TryAdd(profile, out refusal))
        {
            return false;
        }

        var name = NameOf(profile);

        // Noted as each step is taken, for UndoApprove.
        string? rentalLent = null;
        DateTime? rentalOutUtc = null;
        string? eventWithClassAdded = null;
        string? classAdded = null;

        if (!string.IsNullOrWhiteSpace(rentalUnit))
        {
            if (_rentals.CheckOut(rentalUnit, name, string.Empty))
            {
                rentalLent = rentalUnit;
                rentalOutUtc = _rentals.Get(rentalUnit)?.CheckedOutUtc;
            }
            else
            {
                _log($"[signup] {name} was added, but rental {rentalUnit} could not be lent out - it may be out already. Check the Rentals page.");
            }
        }

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
                _log($"[signup] Added {profile.ClassName.Trim()} to {current.DisplayName} for {name}.");
            }

            CheckIn?.Invoke(profile.Transponder, true);
            checkedIn = _checkIns.IsCheckedIn(profile.Transponder);
        }

        invite.State = SignupInviteState.Approved;
        invite.AnsweredUtc = DateTime.UtcNow;
        invite.Transponder = profile.Transponder;
        invite.AnswerCheckedIn = checkedIn;

        // Decided by what the PIN step actually did, not guessed from the
        // settings beforehand: it can find the mail already busy, or give
        // no PIN at all, and the phone was then promised an email that
        // never came.
        // The approval is written down first, so a PIN step that throws
        // can never leave an approved invite that only lives in memory.
        Store.Save();
        bool pinComing;
        try
        {
            pinComing = RacerAdded?.Invoke(profile.Transponder) ?? false;
        }
        catch (Exception ex)
        {
            _log($"[signup] Could not give {name} their check-in PIN: {ex.Message}");
            pinComing = false;
        }

        invite.AnswerMessage = pinComing ? "Your own check-in PIN is on its way by email." : string.Empty;
        Store.Save();

        SendApproved(invite);

        _waiting.RemoveAll(w => string.Equals(w.Hash, request.Hash, StringComparison.OrdinalIgnoreCase));
        _log($"[signup] Approved {name}'s online sign-up{(checkedIn ? " and checked them in" : string.Empty)}.");

        LastApproval = new ApprovalReceipt(invite.Id, name, profile.Transponder, rentalLent, rentalOutUtc,
            eventWithClassAdded, classAdded, checkedIn, _checkIns.CurrentEventId);

        Changed?.Invoke();
        return true;
    }

    // --------------------------------------------------------------- undo
    //
    // What the phone was told can't be taken back: an answer goes to the
    // relay within seconds, and the relay then forgets the sign-up. So each
    // undo here puts things back on THIS laptop - the invite open again, so
    // its code works again - and says in the console what the phone was told.

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
        string CheckInEventId);

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
            _log($"[signup] Rental {unit} has changed hands since {receipt.Name} was approved, so it was left as it is.");
        }

        if (_profiles.Get(transponder) is { } profile && string.Equals(profile.Transponder, transponder, StringComparison.OrdinalIgnoreCase))
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

        Reopen(invite);

        _log($"[signup] Undid approving {receipt.Name}. Their phone was already told they're in"
            + " - that can't be taken back, so let them know in person. Their invite code works again.");
    }

    /// <summary>Undoes turning a sign-up down, here: the invite is open again. The phone has been told no, and the relay has let the sign-up go - they sign up again with the same code.</summary>
    public void UndoReject(string inviteId, string name)
    {
        if (Store.Get(inviteId) is not { State: SignupInviteState.Rejected } invite)
        {
            throw new InvalidOperationException($"{name}'s invite has changed since, so it was left as it is.");
        }

        Reopen(invite);

        _log($"[signup] Undid turning down {name}. Their phone was already told no - that can't be taken back. "
            + "Their invite code works again, so they can sign up again with it.");
    }

    /// <summary>Undoes cancelling an invite, here: its code works again. A sign-up already turned down by the cancel has to be made again.</summary>
    public void UndoCancel(string inviteId, bool signupWasTurnedDown)
    {
        if (Store.Get(inviteId) is not { State: SignupInviteState.Cancelled } invite)
        {
            throw new InvalidOperationException("that invite has changed since, so it was left as it is.");
        }

        Reopen(invite);

        _log($"[signup] Undid cancelling the invite for {invite.ForText}. Its code works again."
            + (signupWasTurnedDown ? " The sign-up already made with it was told it was cancelled - that can't be taken back, so they need to sign up again." : string.Empty));
    }

    /// <summary>An invite back to unanswered, saved, and republished with the next sign-up document.</summary>
    private void Reopen(SignupInvite invite)
    {
        invite.State = SignupInviteState.Open;
        invite.AnsweredUtc = null;
        invite.Transponder = string.Empty;
        invite.AnswerMessage = string.Empty;
        invite.AnswerCheckedIn = false;
        Store.Save();
        Changed?.Invoke();
    }

    /// <summary>Turns a sign-up down, with a line for their phone.</summary>
    public void Reject(RelaySignupRequest request, string message)
    {
        var text = string.IsNullOrWhiteSpace(message)
            ? "The timing desk couldn't take this sign-up."
            : message.Trim();

        if (Store.ByHash(request.Hash, Club) is { State: SignupInviteState.Open } invite)
        {
            invite.State = SignupInviteState.Rejected;
            invite.AnsweredUtc = DateTime.UtcNow;
            invite.AnswerMessage = text;
            Store.Save();
        }

        Answer(request.Hash, approved: false, text, checkedIn: false, driver: null);
        _waiting.RemoveAll(w => string.Equals(w.Hash, request.Hash, StringComparison.OrdinalIgnoreCase));
        _log($"[signup] Turned down {request.Name}'s online sign-up.");
        Changed?.Invoke();
    }

    // ------------------------------------------------------------ invites

    /// <summary>New invites, one per address (or one with none, for somebody at the desk). Saved; the relay has them within a couple of seconds.</summary>
    public IReadOnlyList<SignupInvite> CreateInvites(IEnumerable<(string Email, string Note)> people)
    {
        var made = people.Select(p => Store.Create(_settings, p.Email, p.Note)).ToList();

        Store.Save();
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
            if (Store.Get(sent.Transponder) is { } invite)
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

    /// <summary>Cancels an invite that has not been used. A sign-up already made with it is turned down.</summary>
    public void Cancel(SignupInvite invite)
    {
        if (invite.State != SignupInviteState.Open)
        {
            return;
        }

        invite.State = SignupInviteState.Cancelled;
        invite.AnsweredUtc = DateTime.UtcNow;
        Store.Save();

        var waiting = _waiting.FirstOrDefault(w => string.Equals(w.Hash, invite.Hash, StringComparison.OrdinalIgnoreCase));

        if (waiting is not null)
        {
            Answer(waiting.Hash, approved: false, "That invite was cancelled. Ask at the timing desk.", checkedIn: false, driver: null);
            _waiting.Remove(waiting);
        }

        Changed?.Invoke();
    }

    private static string NameOf(DriverProfile profile) =>
        string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();
}
