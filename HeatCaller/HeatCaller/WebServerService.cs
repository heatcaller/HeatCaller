using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// A small embedded web server serving pages on the local network:
///
///   /        - live results, read-only. Point a browser or an OBS browser
///              source at this for a spectator display or stream overlay.
///   /remote  - race control from a phone, so whoever's running the race
///              can throw flags while standing at the track instead of
///              being tethered to the laptop.
///   /checkin - sign-on from a phone. Drivers find their own name and tap
///              it, which is the half of check-in that does not need
///              somebody standing at the laptop doing it for them. Since
///              26 Oct 2026 each tap needs that driver's own personal
///              PIN (see _racerPinChecks).
///   /manage  - the race-day manager: set up the day, entries and
///              check-in, the programme, race control and results, from a
///              phone on the track's own Wi-Fi. One JSON endpoint
///              (/api/manage) behind the manager PIN; see RaceDayService
///              and raceday-protocol.md for what it can do.
///
/// Built on HttpListener (part of .NET itself) rather than pulling in a
/// full web framework - the surface here is a handful of endpoints, and
/// avoiding another dependency keeps the app's footprint small.
///
/// IMPORTANT: binding to a network-visible address on Windows normally
/// requires either administrator rights or a one-time URL reservation.
/// Start() surfaces that clearly rather than failing silently - see the
/// UrlReservationHint below.
/// </summary>
public sealed partial class WebServerService
{
    private readonly RaceTiming _race;
    private readonly ProfileStore _profiles;
    private readonly DayScheduleStore? _schedule;
    private readonly SponsorStore? _sponsors;
    private readonly AppSettings? _settings;
    private readonly KpiTracker _kpiTracker = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    /// <summary>Raised for log-worthy events (started, stopped, errors) so the host can surface them in the console.</summary>
    public event Action<string>? StatusMessage;

    /// <summary>
    /// Carries out a race-control action the remote page asked for, and says
    /// what became of it. The host supplies this rather than this service
    /// calling RaceControlSession directly, because those actions have to run
    /// on the UI thread and go through the same paths as the on-screen
    /// buttons (which also handle things like cancelling a pending
    /// auto-start).
    ///
    /// The second argument is "force": the phone has asked its user whether
    /// to stage over a running race and been told yes. Without it - which is
    /// how every press arrives the first time - a stage or countdown over a
    /// running race is answered with that question (409 with "confirm")
    /// rather than run, and the page sends it again with force=1 on a yes.
    /// See HandleCommand.
    ///
    /// The third is the race that yes was about - the session generation the
    /// question carried, sent back by the page as raceGeneration - or null
    /// from a page that does not send it, which is trusted as before. A yes
    /// about one race must not abandon another that went green meanwhile.
    ///
    /// Called on the request's own thread-pool thread (see DispatchRequest),
    /// never the UI thread, and it may block for a few seconds: the host
    /// hands the command to the UI thread and waits for Race Control to
    /// finish with it, and the phone's answer waits with it.
    ///
    /// It replaces CommandReceived and CommandAccepted, which could only say
    /// whether a command had been handed to the UI thread. The phone was told
    /// "GREEN sent" for a command Race Control then refused - Stage during
    /// practice, a checkered with no race running - and whoever was holding
    /// it had no way to know.
    /// </summary>
    public Func<string, bool, int?, RemoteCommandOutcome>? RunCommand { get; set; }

    /// <summary>
    /// The race Race Control has in hand - "Q2 Heat 1 (Stock Buggy)" - for
    /// the phone remote to say what its buttons are about to act on (27 Sep
    /// 2026). Asked on the UI thread as the LAN copy of the live state is
    /// built. Never in the relay's copy.
    /// </summary>
    public Func<string>? RaceInHand { get; set; }

    /// <summary>
    /// The races still to run, in order, for phones following a driver -
    /// when is their next race and where do they grid (PhoneFollow). Asked
    /// on the UI thread as the LAN copy of the live state is built. Never in
    /// the relay's public copy - only, per badge, in the relay's private
    /// driverlive (22 Oct 2026, driver experience), which is reachable with
    /// that driver's own badge alone.
    /// </summary>
    public Func<IReadOnlyList<UpcomingRace>>? UpcomingRaces { get; set; }

    /// <summary>
    /// The live track map - the line and every car on it - for the Live
    /// Results page's Track page. Asked on the UI thread as the LAN copy of
    /// the live state is built. Never in the relay's copy.
    /// </summary>
    public Func<(TrackLine? Line, IReadOnlyList<TrackPositions.Car> Cars)>? TrackMap { get; set; }

    /// <summary>
    /// The championship as it would stand if the race on track ended now
    /// (ChampionshipLive), for the Live Results page - null when no race that
    /// counts towards one is on. Asked on the UI thread as the LAN copy of
    /// the live state is built. Never in the relay's copy.
    /// </summary>
    public Func<IfItEndedNow?>? IfItEndedNow { get; set; }

    /// <summary>
    /// When the decoder last heard a transponder, and how well - "good",
    /// "weak" or "unknown" - or null when it has not been heard lately (22
    /// Oct 2026, driver experience, item 4). For the follow view's "Check my
    /// transponder": the driver drives past the line and the phone says it
    /// was heard. Set by MainForm.WiFiExtras from TransponderWatch, which
    /// has its own lock. Asked on the UI thread as the LAN copy of the live
    /// state is built, for the people on the follow list only. Never in the
    /// relay's copy.
    /// </summary>
    public Func<string, (DateTime SeenUtc, string Signal)?>? SightingOf { get; set; }

    /// <summary>
    /// The last few lines the announcer said, oldest first, for the
    /// captions strip on the OBS overlay and the broadcast graphics with
    /// ?captions=1 (22 Oct 2026, driver experience, item 9). Set by
    /// MainForm.WiFiExtras from CaptionLog.Shared. The text as heard, full
    /// names and all: the LAN pages are in the room, where the PA has
    /// already said it. Never in the relay's copy - that has its own,
    /// redacted, captions document.
    /// </summary>
    public Func<IReadOnlyList<CaptionLine>>? Captions { get; set; }

    /// <summary>Lines of captions the LAN state carries: the strip shows the newest, and four covers a page that missed a poll or two.</summary>
    internal const int CaptionsInState = 4;

    /// <summary>The most penalty lines one driver's follow card carries - a night of them is still a phone screen.</summary>
    internal const int PenaltyLinesPerDriver = 12;

    /// <summary>Tonight's penalty lines, by transponder - replaced whole by PublishPenalties, never changed in place.</summary>
    private volatile IReadOnlyDictionary<string, IReadOnlyList<string>> _penalties =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Tonight's penalty lines, by transponder - "Q1 Heat 2: +5 s - jump
    /// start" - for the follow card on the Live Results page (22 Oct 2026,
    /// driver experience, item 1). Called on the UI thread when a race is
    /// filed or corrected (MainForm.RaceFiledWiFiExtras), the same way as
    /// PublishCoachCards. The dictionary is copied, so the caller is free to
    /// reuse its own.
    ///
    /// THE REASONS ARE PRIVATE everywhere but here and the driver's own
    /// night (see PenaltyWords): the follow view is in the room, where the
    /// PA has already said them. They go into the LAN copy of the live state
    /// only, under the roster's opaque id, and never into the relay's.
    /// </summary>
    public void PublishPenalties(IReadOnlyDictionary<string, IReadOnlyList<string>> byTransponder)
    {
        var copy = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (who, lines) in byTransponder)
        {
            var kept = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

            if (!string.IsNullOrWhiteSpace(who) && kept.Length > 0)
            {
                // The latest are the ones the driver is asking about.
                copy[who.Trim()] = kept.Skip(Math.Max(0, kept.Length - PenaltyLinesPerDriver)).ToArray();
            }
        }

        _penalties = copy;
    }

    /// <summary>
    /// A time as the pages read it - "2026-10-22T19:42:00.000Z", always UTC
    /// and always the invariant culture (22 Oct 2026, driver experience). A
    /// time whose kind nobody set is taken to be UTC already, which is what
    /// every caller here hands over.
    /// </summary>
    internal static string IsoUtc(DateTime time)
    {
        var utc = time.Kind switch
        {
            DateTimeKind.Utc => time,
            DateTimeKind.Local => time.ToUniversalTime(),
            _ => DateTime.SpecifyKind(time, DateTimeKind.Utc),
        };

        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }

    private volatile bool _hostReady;

    /// <summary>
    /// Whether the host window is up and able to act on a command or a
    /// check-in. Set by the host once it has been shown, cleared as it
    /// closes.
    ///
    /// Until then a request would be answered "ok" and then dropped on the
    /// floor, because there is no UI thread yet to hand it to - a phone
    /// saying "GREEN sent" for a flag that never flew. Refusing with a 503
    /// tells the truth instead.
    /// </summary>
    public bool HostReady
    {
        get => _hostReady;
        set => _hostReady = value;
    }

    /// <summary>One driver as the check-in page sees them.</summary>
    public sealed record CheckInEntry(string Transponder, string Name, string ClassName, bool CheckedIn);

    /// <summary>
    /// The roster as the check-in page is served it: each driver under an
    /// opaque id rather than their transponder, and the way back from one
    /// to the other.
    ///
    /// The ids exist because the roster is readable by any phone on the
    /// Wi-Fi, and a transponder number next to a name is a tracking key -
    /// it follows somebody from meeting to meeting, and it is also exactly
    /// what a spoofed decoder pass needs. An HMAC under a key made fresh
    /// every time the server starts says nothing about the transponder and
    /// cannot be carried from one meeting to the next.
    /// </summary>
    private sealed record RosterView(
        IReadOnlyList<CheckInEntry> Entries,
        IReadOnlyList<string> Ids,
        IReadOnlyDictionary<string, CheckInEntry> ById);

    private static readonly RosterView EmptyRoster = new(
        Array.Empty<CheckInEntry>(),
        Array.Empty<string>(),
        new Dictionary<string, CheckInEntry>(StringComparer.Ordinal));

    /// <summary>
    /// The roster the check-in page serves, published by the host.
    ///
    /// A SNAPSHOT rather than a reference to CheckInStore, and this is the
    /// important part: requests are handled on thread-pool threads while
    /// the app edits the same stores on the UI thread. CheckInStore is
    /// backed by plain dictionaries and sets - reading one while the UI
    /// thread adds to it is a data race, and the fact that it would
    /// usually get away with it is exactly what makes that kind of bug
    /// turn up once, at an event, and never in testing.
    ///
    /// Publishing an immutable list instead means the only thing shared
    /// across threads is a reference, and swapping a reference is atomic.
    /// </summary>
    private volatile IReadOnlyList<CheckInEntry> _roster = Array.Empty<CheckInEntry>();

    /// <summary>The roster under its opaque ids. Rebuilt whenever the roster or the key changes; see RosterView.</summary>
    private volatile RosterView _rosterView = EmptyRoster;

    /// <summary>The key the roster ids are made with. Replaced on every Start, so an id seen at one meeting means nothing at the next.</summary>
    private volatile byte[] _rosterKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Raised when a phone asks to check somebody in or out. Handled by the
    /// host on the UI thread, for the same reason RunCommand is - the
    /// stores belong to that thread.
    /// </summary>
    public event Action<string, bool>? CheckInRequested;

    /// <summary>
    /// Held while _roster and _rosterView are REPLACED - never while they
    /// are read, which stays a plain read of one reference. Replacing
    /// happens from two threads: the host publishing from the UI thread,
    /// and AssumeCheckIn on a request thread, and without this AssumeCheckIn
    /// could check "nobody has published since" and then write its guess
    /// over a roster the host published in between.
    /// </summary>
    private readonly object _rosterGate = new();

    /// <summary>Hands the check-in page a fresh roster. Called from the UI thread.</summary>
    public void PublishRoster(IReadOnlyList<CheckInEntry> roster)
    {
        lock (_rosterGate)
        {
            _roster = roster;
            _rosterView = BuildRosterView(roster, _rosterKey);
        }
    }

    private static RosterView BuildRosterView(IReadOnlyList<CheckInEntry> roster, byte[] key)
    {
        var ids = new List<string>(roster.Count);
        var byId = new Dictionary<string, CheckInEntry>(StringComparer.Ordinal);

        foreach (var entry in roster)
        {
            var id = RosterId(key, entry.Transponder);
            ids.Add(id);

            // A collision in 64 bits is not going to happen on a club
            // roster, but if it did, the first driver keeps the id rather
            // than a tap on one checking in the other.
            byId.TryAdd(id, entry);
        }

        return new RosterView(roster, ids, byId);
    }

    /// <summary>
    /// The key the public board's row tags are made with (29 Oct 2026,
    /// spoken laps on My Night). Its own key, not the roster's, so a tag on
    /// the public board can never be matched against a check-in id. Made
    /// once per run, so a driver's tag holds all evening.
    /// </summary>
    private readonly byte[] _relayRowKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// The tag a car's row carries on the relay's public board: sixteen hex
    /// characters that say nothing about the transponder. The driver's own
    /// night page gets the same tags in their private live entry
    /// (DriverLive), which is how it finds their row on the board without
    /// going by name. Null for a blank transponder.
    /// </summary>
    public string? RelayRowTag(string? transponder) =>
        string.IsNullOrWhiteSpace(transponder) ? null : RosterId(_relayRowKey, transponder);

    /// <summary>Sixteen hex characters of HMAC. Case and spacing in the transponder do not change it.</summary>
    private static string RosterId(byte[] key, string transponder)
    {
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(transponder.Trim().ToUpperInvariant()));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    public bool IsRunning => _listener?.IsListening == true;
    public int Port { get; private set; }

    /// <summary>
    /// The PIN the remote-control page needs. Required: with none set, or
    /// one shorter than four digits, the remote refuses every command -
    /// see IsUsableRemotePin.
    /// </summary>
    public string Pin
    {
        get => _pin;
        set => _pin = PinChanged(_pin, value, _remotePinLockout);
    }

    private string _pin = string.Empty;

    /// <summary>
    /// A new PIN for one of the three pages: stored, and when it really is
    /// different, the phones trusted under the old one stop being trusted.
    ///
    /// Trust (see PinLockout) means "this phone knew the PIN", and lets it
    /// guess past the whole-PIN lockout. A phone that knew the OLD PIN - a
    /// marshal who has handed the remote back, the reason the PIN was
    /// changed - kept that for twelve hours after the change, with nothing
    /// but its own ten-a-go limit between it and the new one. The settings
    /// are applied again on every save, so only a real change forgets.
    /// </summary>
    private static string PinChanged(string current, string? value, PinLockout lockout)
    {
        var next = value ?? string.Empty;

        if (!string.Equals(current, next, StringComparison.Ordinal))
        {
            lockout.ForgetTrusted();
        }

        return next;
    }

    /// <summary>
    /// The PIN the check-in page needs, if any. Separate from the remote's
    /// on purpose: this one is written on the wall for drivers, and a
    /// driver who knew the remote PIN could throw a red flag from the pits.
    /// Empty means anybody on the Wi-Fi can open the list. It no longer
    /// checks anybody in on its own: each tap also needs that driver's own
    /// PIN (26 Oct 2026 - see _racerPinChecks).
    /// </summary>
    public string CheckInPin
    {
        get => _checkInPin;
        set => _checkInPin = PinChanged(_checkInPin, value, _checkInPinLockout);
    }

    private string _checkInPin = string.Empty;

    // WHY (26 Oct 2026, asked for by Asher: "is there a reason the phone
    // sign in page you can sign in/out anyone you want without a pin?"):
    // there was a reason, and it no longer holds. /checkin was built as an
    // open sign-on sheet - "drivers find their own name and tap it" - with
    // the check-in PIN above as a PIN for the WALL, shared by every driver
    // and blank (open to anybody on the Wi-Fi) unless the club set one. So
    // anybody on the track's Wi-Fi could check any driver in or out. Now
    // every check-in or check-out from that page needs THAT driver's own
    // personal PIN - the six digits RacerPins gives each racer and the club
    // emails them - checked here, on the laptop, before anything changes.
    // The wall PIN still decides who can open the list at all; the desk
    // itself and the race-day manager (/manage, behind the manager PIN) are
    // staff's way to check anybody in, and are unchanged.

    /// <summary>
    /// Each racer's personal PIN as a keyed hash, by transponder - never the
    /// PIN itself. Published by the host from the UI thread (see
    /// PublishRacerPins) and swapped whole, like the roster, so a request
    /// thread only ever reads one immutable reference.
    /// </summary>
    private volatile IReadOnlyDictionary<string, byte[]> _racerPinChecks =
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The key the racer PIN checks are made with: made fresh every run and never saved, so a check means nothing outside this process.</summary>
    private readonly byte[] _racerPinKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Hands the check-in page each racer's personal PIN, by transponder.
    /// Called from the UI thread with settings.RacerPins (empty when racer
    /// PINs are off). Kept only as a keyed hash: the server never holds a
    /// copy of a PIN, and nothing it writes or logs can carry one.
    /// </summary>
    public void PublishRacerPins(IReadOnlyDictionary<string, string>? pinsByTransponder)
    {
        var checks = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var (transponder, pin) in pinsByTransponder ?? new Dictionary<string, string>())
        {
            var key = (transponder ?? string.Empty).Trim();
            var value = (pin ?? string.Empty).Trim();

            if (key.Length > 0 && value.Length > 0)
            {
                checks[key] = RacerPinCheck(_racerPinKey, value);
            }
        }

        _racerPinChecks = checks;
    }

    private static byte[] RacerPinCheck(byte[] key, string pin) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(pin.Trim()));

    /// <summary>What became of a racer PIN offered with a check-in tap. See CheckRacerPin.</summary>
    internal enum RacerPinVerdict
    {
        /// <summary>The driver's own PIN: the tap may go on.</summary>
        Accepted,
        /// <summary>
        /// Not their PIN - or no PIN sent, or a driver with no personal PIN
        /// at all, which are answered exactly the same (see CheckRacerPin).
        /// Counted against this device and against every device together.
        /// </summary>
        Wrong,
        /// <summary>This device has had its ten wrong PINs in five minutes.</summary>
        TooManyFromDevice,
        /// <summary>Too many wrong racer PINs from everybody at once - nobody's is taken for five minutes.</summary>
        LockedOut,
    }

    /// <summary>
    /// Whether <paramref name="offered"/> is the personal PIN of the driver
    /// with this transponder - the server-side check every phone tap on
    /// /checkin has to pass. Ten wrong from one device in five minutes and
    /// that device waits; a hundred from everybody and racer PINs are refused
    /// for five minutes (see _racerPinLockout). Compared in constant time, as
    /// keyed hashes. Internal for the tests.
    /// </summary>
    internal RacerPinVerdict CheckRacerPin(string device, string transponder, string? offered)
    {
        var pin = (offered ?? string.Empty).Trim();

        // WHY (26 Oct 2026, review): "no PIN sent" and "this driver has no
        // personal PIN" used to be answered first, uncounted and each in
        // words of its own - so a phone past the wall PIN (blank by default)
        // could learn for free which drivers have a PIN. Now every request
        // is counted first, and all three refusals are one: Wrong. The page
        // asks for a PIN on it either way.
        //
        // Counted BEFORE the comparison and handed back only when right, as
        // PinAccepted does, so a burst of guesses is held exactly as a
        // trickle is.
        var who = "racer|" + device;

        if (!_pinFailures.TryReserve(who, out var ticket))
        {
            return RacerPinVerdict.TooManyFromDevice;
        }

        if (!_racerPinLockout.TryReserve(device, out var lockoutTicket))
        {
            _pinFailures.Release(who, ticket);
            return RacerPinVerdict.LockedOut;
        }

        // A driver with no PIN is compared against a check nothing matches,
        // so they take the same path - and much the same time - as a wrong
        // PIN for a driver who has one.
        var known = _racerPinChecks.TryGetValue((transponder ?? string.Empty).Trim(), out var expected);
        var offeredCheck = RacerPinCheck(_racerPinKey, pin);

        if (known && expected is not null && pin.Length > 0 && CryptographicOperations.FixedTimeEquals(offeredCheck, expected))
        {
            _pinFailures.Release(who, ticket);

            // Forgiven, not trusted: knowing your OWN PIN says nothing about
            // anybody else's, so it must not let this phone guess past the
            // lockout the way a known wall or remote PIN does.
            _racerPinLockout.Forgive(lockoutTicket);
            return RacerPinVerdict.Accepted;
        }

        if (_racerPinLockout.ConfirmWrong())
        {
            try
            {
                StatusMessage?.Invoke($"[web] WARNING: {_racerPinLockout.MaxWrong} wrong personal check-in PINs in five minutes - somebody may be trying "
                    + "to guess one. Phones can't check anybody in or out for five minutes; the desk still can.");
            }
            catch
            {
                // A request thread; the log must not be what fails it.
            }
        }

        return RacerPinVerdict.Wrong;
    }

    /// <summary>
    /// The racer PIN check for one tap, answered on the request when it is
    /// refused. The PIN comes in its own X-Racer-Pin header - never the
    /// address, where it would sit in browser history - and is never kept.
    /// </summary>
    private bool RacerPinAccepted(HttpListenerContext context, CheckInEntry driver)
    {
        var verdict = CheckRacerPin(ClientKey(context), driver.Transponder, context.Request.Headers["X-Racer-Pin"]);

        if (verdict == RacerPinVerdict.Accepted)
        {
            return true;
        }

        var (status, error) = verdict switch
        {
            RacerPinVerdict.TooManyFromDevice => (429, "Too many wrong PINs. Wait a few minutes."),
            RacerPinVerdict.LockedOut => (429, "Too many wrong check-in PINs on this Wi-Fi. Phones can't check in for five minutes - check in at the desk."),
            // One answer for a wrong PIN, no PIN, and a driver with no PIN
            // at all - see CheckRacerPin. The hint covers the last of them
            // without saying which it was.
            _ => (403, "That isn't this driver's check-in PIN. No personal PIN? Check in at the desk."),
        };

        context.Response.StatusCode = status;
        WriteJson(context, JsonSerializer.Serialize(new { ok = false, racerPin = true, error }));
        return false;
    }

    /// <summary>
    /// Whether a PIN is good enough to guard the remote: four or more
    /// digits and nothing else. Digits because the page asks for it with a
    /// phone's number pad; four because anything shorter falls to the
    /// wrong-PIN limit in an afternoon.
    /// </summary>
    public static bool IsUsableRemotePin(string? pin) =>
        pin is { Length: >= 4 } && pin.All(char.IsAsciiDigit);

    /// <summary>
    /// The PIN /manage needs. Separate from both the others: it can touch
    /// entries, the programme and saved results, not just throw a flag or
    /// sign somebody in, so it is checked against
    /// <see cref="RaceDayService.IsUsableManagerPin"/> - six digits or
    /// more - rather than the remote's four. Empty means the race-day
    /// manager is not usable at all, the same as the remote with no PIN.
    /// </summary>
    public string ManagerPin
    {
        get => _managerPin;
        set => _managerPin = PinChanged(_managerPin, value, _managerPinLockout);
    }

    private string _managerPin = string.Empty;

    /// <summary>
    /// The race-day manager itself, handed over by MainForm once the
    /// stores and controls it needs all exist. Null until then, and
    /// briefly on shutdown - see HandleManage.
    /// </summary>
    public RaceDayService? Manager { get; set; }

    public WebServerService(RaceTiming race, ProfileStore profiles,
        DayScheduleStore? schedule = null, SponsorStore? sponsors = null, AppSettings? settings = null)
    {
        _race = race;
        _profiles = profiles;
        _schedule = schedule;
        _sponsors = sponsors;
        _settings = settings;
    }

    /// <remarks>
    /// The reservation is written as an SDDL string rather than
    /// "user=Everyone": that name is translated on non-English Windows
    /// ("Jeder", "Tout le monde"), and the English one then fails with a
    /// confusing error on exactly the laptops where nobody can read it.
    /// WD is the well-known SID for Everyone in every language.
    ///
    /// The firewall rule is the second half nobody mentions. Without it the
    /// reservation succeeds, the server starts, and every phone still times
    /// out - Windows asks about it once, and a click on the wrong button
    /// blocks it silently for good. Private profile only: the venue's Wi-Fi
    /// set as a private network is where this belongs, and a public hotspot
    /// is not.
    /// </remarks>
    public static string UrlReservationHint =>
        "Windows blocked the app from accepting connections from other devices.\r\n\r\n" +
        "Fix it once with either of these:\r\n\r\n" +
        "  \u2022 Run HeatCaller as administrator, or\r\n" +
        "  \u2022 Open an admin Command Prompt and run:\r\n" +
        "      netsh http add urlacl url=http://+:{PORT}/ sddl=D:(A;;GX;;;WD)\r\n\r\n" +
        "The second option is the better one - it's a one-time setup and " +
        "doesn't require running the whole app elevated afterward.\r\n\r\n" +
        "If phones still can't connect after that, allow the port through the firewall " +
        "(same admin Command Prompt, and set the venue's Wi-Fi as a Private network):\r\n" +
        "      netsh advfirewall firewall add rule name=\"HeatCaller web {PORT}\" dir=in action=allow " +
        "protocol=TCP localport={PORT} profile=private";

    public bool Start(int port, out string? error)
    {
        Stop();

        Port = port;

        // Held in a local until it has started. A listener that failed to
        // start is closed here rather than left for the collector, which
        // would otherwise hold the port's registration until it got round
        // to it - and the next Start, on the same port, then fails too.
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://+:{port}/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            CloseQuietly(listener);
            error = ex.ErrorCode == 5
                ? UrlReservationHint.Replace("{PORT}", port.ToString())
                : $"Couldn't start the web server on port {port}: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            CloseQuietly(listener);
            error = $"Couldn't start the web server on port {port}: {ex.Message}";
            return false;
        }

        var cts = new CancellationTokenSource();

        _listener = listener;
        _cts = cts;

        // A fresh start is a fresh slate: somebody locked out by mistyping
        // is let back in, and the roster ids from the last run stop working.
        _pinFailures.Clear();
        _remotePinLockout.Clear();
        _checkInPinLockout.Clear();
        _managerPinLockout.Clear();
        _racerPinLockout.Clear();
        _checkInWrites.Clear();
        _checkInWritesAll.Clear();
        _commands.Clear();
        lock (_rosterGate)
        {
            _rosterKey = RandomNumberGenerator.GetBytes(32);
            _rosterView = BuildRosterView(_roster, _rosterKey);
        }

        // Before the listener, so the very first request already has a
        // document to serve rather than an empty one. Start is called from
        // the UI thread, which is the only thread allowed to build it.
        PublishState(force: true);

        // The listener and token handed over as they are NOW. The loop used
        // to read the fields, and a Stop then Start while it was between
        // two requests left the old loop serving from the new listener.
        _ = Task.Run(() => ListenLoopAsync(listener, cts.Token));

        StatusMessage?.Invoke(
            $"[web] Server started on port {port}. Live results: {LocalUrl()}  "
            + $"Remote: {LocalUrl()}remote  Check-in: {LocalUrl()}checkin");

        if (!IsUsableRemotePin(Pin))
        {
            StatusMessage?.Invoke(Pin.Length == 0
                ? "[web] The phone remote is locked: it needs a PIN of four or more digits (Local Screens page)."
                : "[web] The phone remote is locked: its PIN must be four or more digits and nothing else (Local Screens page).");
        }

        // The other half of CheckInLockedReason: said at the start, loudly,
        // because a server started automatically shows no warning dialog.
        if (CheckInClosedReason is { } locked)
        {
            StatusMessage?.Invoke("[web] " + locked);
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Why the check-in pages are closed although no check-in PIN is set -
    /// or null when a blank PIN means what it says, open to anybody on the
    /// Wi-Fi.
    ///
    /// A blank check-in PIN is a choice a club can make. A check-in PIN that
    /// is blank because the laptop could not unseal its saved secrets - a
    /// backup restored onto a new laptop - is not, and the two used to look
    /// the same here: the remote and the manager, which need a PIN, failed
    /// closed, while check-in quietly opened to everyone, so nobody noticed
    /// (25 Sep security audit). Until the secrets are unlocked or a PIN is
    /// entered again, check-in stays shut and says why.
    ///
    /// <paramref name="pinNeedsEntering"/> alone (AppSettings.
    /// CheckInPinNeedsEntering, with the secrets no longer locked) means the
    /// backup's keys were given up on, so there is nothing left to unlock.
    /// It used to say to unlock them with the backup password all the same,
    /// on every phone and at every server start, until a PIN was set.
    /// </summary>
    internal static string? CheckInLockedReason(bool secretsLocked, string checkInPin, bool pinNeedsEntering = false) =>
        !string.IsNullOrEmpty(checkInPin)
            ? null
            : secretsLocked
                ? "Check-in is closed: the check-in PIN saved on this laptop couldn't be read (usually a backup restored onto another PC). "
                  + "Unlock the saved PINs with the backup password, or set the check-in PIN again on the Local Screens page."
                : pinNeedsEntering
                    ? "Check-in is closed until a check-in PIN is set again on the Local Screens page (the one from the old PC couldn't be recovered)."
                    : null;

    /// <summary>
    /// CheckInLockedReason for this server's settings and check-in PIN, as
    /// they stand. Goes by CheckInPinNeedsEntering, not just SecretsLocked:
    /// the lock is cleared by giving up on the backup's keys, or by any
    /// other secret being saved, and check-in then opened to everybody on
    /// the Wi-Fi though nobody had set its PIN again.
    /// </summary>
    internal string? CheckInClosedReason =>
        CheckInLockedReason(_settings?.SecretsLocked == true, CheckInPin, _settings?.CheckInPinNeedsEntering == true);

    /// <summary>Answers a check-in request refused by CheckInLockedReason, and says whether it may go on.</summary>
    private bool CheckInOpen(HttpListenerContext context)
    {
        if (CheckInClosedReason is not { } reason)
        {
            return true;
        }

        context.Response.StatusCode = 503;
        WriteJson(context, JsonSerializer.Serialize(new { ok = false, error = reason }));
        return false;
    }

    private static void CloseQuietly(HttpListener listener)
    {
        try
        {
            listener.Close();
        }
        catch
        {
            // It never started; there is nothing more to release.
        }
    }

    public void Stop()
    {
        var listener = _listener;
        var cts = _cts;

        _listener = null;
        _cts = null;

        try
        {
            cts?.Cancel();
            if (listener?.IsListening == true)
            {
                listener.Stop();
            }
            listener?.Close();
        }
        catch
        {
            // Shutting down - nothing useful to do if this throws.
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <summary>Best-guess URL other devices on the same network should use.</summary>
    public string LocalUrl()
    {
        var ip = GetLocalIPv4() ?? "localhost";
        return $"http://{ip}:{Port}/";
    }

    /// <summary>
    /// The address phones on the same Wi-Fi should use.
    ///
    /// The network adapters are asked first. The old way - a UDP socket
    /// "connected" towards a public address, which makes Windows pick the
    /// interface it would route through - needs a default gateway to have
    /// anything to route through, and a race-day network is often a
    /// travel router with no internet behind it. There it failed, and
    /// every poster and URL on the Local Screens page said "localhost",
    /// which no phone can reach. It is still the fallback, because on a
    /// laptop with several adapters it is the best tie-breaker there is.
    /// </summary>
    public static string? GetLocalIPv4()
    {
        try
        {
            System.Net.IPAddress? withGateway = null;
            System.Net.IPAddress? withoutGateway = null;

            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up
                    || nic.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Loopback
                        or System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var properties = nic.GetIPProperties();
                var hasGateway = properties.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !g.Address.Equals(System.Net.IPAddress.Any));

                foreach (var unicast in properties.UnicastAddresses)
                {
                    var address = unicast.Address;

                    if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
                        || !IsPrivateIPv4(address))
                    {
                        continue;
                    }

                    if (hasGateway)
                    {
                        withGateway ??= address;
                    }
                    else
                    {
                        withoutGateway ??= address;
                    }
                }
            }

            // With several private adapters up (Wi-Fi plus a VM switch,
            // say), the one with a gateway is the one the route agrees
            // with - ask the route first in that case, then fall back.
            if (withGateway is not null)
            {
                return RoutedIPv4() ?? withGateway.ToString();
            }

            if (withoutGateway is not null)
            {
                return withoutGateway.ToString();
            }
        }
        catch
        {
            // Adapter enumeration can fail on a locked-down machine; the
            // routing trick below still has a chance.
        }

        return RoutedIPv4();
    }

    /// <summary>10/8, 172.16/12 and 192.168/16 - the addresses a venue router hands out.</summary>
    private static bool IsPrivateIPv4(IPAddress address)
    {
        var b = address.GetAddressBytes();

        return b.Length == 4
               && (b[0] == 10
                   || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                   || (b[0] == 192 && b[1] == 168));
    }

    private static string? RoutedIPv4()
    {
        try
        {
            // Opening a UDP socket toward a public address doesn't actually
            // send anything, but it makes the OS pick the interface it would
            // route through - which is the address other devices on the LAN
            // can reach, rather than a loopback or virtual adapter.
            using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            return (socket.LocalEndPoint as IPEndPoint)?.Address.ToString();
        }
        catch
        {
            return null;
        }
    }

    private async Task ListenLoopAsync(HttpListener listener, CancellationToken token)
    {
        // Failures in a row, for the backoff below. Any accepted request
        // puts it back to zero.
        var failuresInARow = 0;

        while (!token.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
                failuresInARow = 0;
            }
            catch (ObjectDisposedException)
            {
                return; // listener closed by Stop
            }
            catch (Exception ex)
            {
                // Only a stopped listener ends the loop. Any failure used to,
                // and this is the ONLY loop accepting requests: one phone
                // dropping its connection at the wrong moment (http.sys
                // reports that as an error here, 1229 and friends) left the
                // server listening, the Local Screens page saying "Running",
                // and every phone in the building timing out until somebody
                // restarted it.
                if (token.IsCancellationRequested || !listener.IsListening)
                {
                    return;
                }

                failuresInARow++;

                try
                {
                    LogRequestFailure($"Couldn't accept a connection: {ex.Message}");
                }
                catch
                {
                    // The log must never be what stops the server.
                }

                // One failure is a phone that went away; carry straight on.
                // Several in a row is something wrong with the listener
                // itself, and retrying flat out would spin a core and fill
                // the log - so wait, a little longer each time, up to two
                // seconds.
                if (failuresInARow > 1)
                {
                    var wait = TimeSpan.FromMilliseconds(Math.Min(2000, 50 << Math.Min(failuresInARow, 6)));

                    try
                    {
                        await Task.Delay(wait, token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }

                continue;
            }

            try
            {
                DispatchRequest(context, token);
            }
            catch
            {
                // Handing a request to the thread pool does not fail in any
                // way worth stopping the server for. If it ever did, this
                // one request is lost and the next is still accepted.
            }
        }
    }

    /// <summary>
    /// Handles one request on the thread pool. Everything it can throw is
    /// caught inside, so no request can take the accept loop down with it.
    /// </summary>
    private void DispatchRequest(HttpListenerContext context, CancellationToken token)
    {
        _ = Task.Run(() =>
        {
            try
            {
                HandleRequest(context);
            }
            catch (Exception ex)
            {
                // Answer, and close - FIRST. A request that threw used
                // to be left open - never answered, never closed - so
                // the phone that asked hung until its browser gave up.
                // On a page that polls every second that is a board
                // that freezes, not one that skips a frame. The log
                // line used to come before this, and a log handler that
                // threw meant the phone never heard back at all.
                try
                {
                    context.Response.StatusCode = 500;
                    context.Response.Close();
                }
                catch
                {
                    // Headers already sent, or the client already went.
                    // Either way there is nothing left to tell it.
                }

                try
                {
                    LogRequestFailure(ex.Message);
                }
                catch
                {
                    // A request thread is no place to let the log take
                    // the process down.
                }
            }
        }, token);
    }

    private readonly object _failureLogGate = new();
    private string? _lastFailure;
    private DateTime _lastFailureLoggedUtc = DateTime.MinValue;
    private int _failuresSinceLogged;

    /// <summary>
    /// One line per distinct failure every ten seconds, with a count of how
    /// many were swallowed. The pages poll once a second from every phone in
    /// the building, so one fault in the state document used to be twenty
    /// identical console lines a second - burying everything else in the
    /// log, on the one night somebody needed to read it.
    /// </summary>
    private void LogRequestFailure(string message)
    {
        string line;

        lock (_failureLogGate)
        {
            var now = DateTime.UtcNow;

            if (message == _lastFailure && now - _lastFailureLoggedUtc < TimeSpan.FromSeconds(10))
            {
                _failuresSinceLogged++;
                return;
            }

            // The count belongs to the message it was counting, so it is
            // only reported when that same message comes round again.
            line = message == _lastFailure && _failuresSinceLogged > 0
                ? $"[web] Request failed: {message} (and {_failuresSinceLogged} more times since it was last shown)"
                : $"[web] Request failed: {message}";

            _lastFailure = message;
            _lastFailureLoggedUtc = now;
            _failuresSinceLogged = 0;
        }

        StatusMessage?.Invoke(line);
    }

    /// <summary>
    /// Whether a request was addressed to this laptop by a name that can
    /// only mean this laptop.
    ///
    /// The defence against DNS rebinding. A web page on some other site
    /// can point a name it controls at this laptop's address, and a phone
    /// on the club Wi-Fi that opens that page would then let it read the
    /// roster and send commands as though it were one of these pages. What
    /// gives it away is the Host header, which carries the name the browser
    /// used: an IP address, "localhost", this PC's own name, or a name that
    /// only exists on a local network is fine - a public domain never is.
    /// </summary>
    internal static bool IsLocalHost(string? hostHeader)
    {
        if (string.IsNullOrWhiteSpace(hostHeader))
        {
            return false;
        }

        var host = hostHeader.Trim();

        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']');

            if (close < 0)
            {
                return false;
            }

            host = host[1..close];
        }
        else if (host.IndexOf(':') is var colon and >= 0)
        {
            host = host[..colon];
        }

        host = host.TrimEnd('.').ToLowerInvariant();

        if (host.Length == 0)
        {
            return false;
        }

        if (IPAddress.TryParse(host, out _) || host == "localhost"
            || host == Environment.MachineName.ToLowerInvariant())
        {
            return true;
        }

        // A single-label name ("timing") and the local-only suffixes are
        // names a router or mDNS hands out on the club's own network, and
        // that nobody on the internet can register.
        return !host.Contains('.')
               || host.EndsWith(".local", StringComparison.Ordinal)
               || host.EndsWith(".lan", StringComparison.Ordinal)
               || host.EndsWith(".home", StringComparison.Ordinal)
               || host.EndsWith(".internal", StringComparison.Ordinal)
               || host.EndsWith(".localdomain", StringComparison.Ordinal)
               || host.EndsWith(".home.arpa", StringComparison.Ordinal);
    }

    /// <summary>
    /// On every answer: nothing may frame these pages (a remote laid under
    /// a decoy page is a remote somebody else is pressing), nothing is
    /// cached, and a response is only ever what its type says it is.
    /// </summary>
    private static void AddSecurityHeaders(HttpListenerResponse response)
    {
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["X-Frame-Options"] = "DENY";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Cache-Control"] = "no-store";

        // The pages are self-contained - inline script and style, images
        // from this server or data: - so this costs them nothing and takes
        // away everything else: a name that slipped past the escaping could
        // not load a script from elsewhere, or send the roster or a PIN
        // anywhere but back here. 'unsafe-inline' is needed by the pages'
        // own inline script and handlers; nothing here uses eval.
        response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; "
            + "img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'";
    }

    /// <summary>
    /// A request that changes something - a flag, a check-in - has to be a
    /// POST carrying a header of this app's own.
    ///
    /// That header is the whole CSRF defence. A GET that throws a red flag
    /// can be fired by any web page a phone on the club Wi-Fi happens to
    /// open, with nothing more than an image tag pointing at this laptop.
    /// A custom header is something no other site's page can add without
    /// first asking this server's permission - and this server never
    /// gives it, because it never answers that question.
    /// </summary>
    private static bool IsAppWrite(HttpListenerContext context)
    {
        var request = context.Request;

        if (string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)
            && request.Headers["X-HeatCaller"] == "1")
        {
            return true;
        }

        context.Response.StatusCode = 405;
        WriteJson(context, "{\"ok\":false,\"error\":\"This page is out of date - reload it and try again.\"}");
        return false;
    }

    /// <summary>
    /// Counts attempts per client inside a sliding window, and says when a
    /// client has had its share.
    ///
    /// Check and record happen under ONE lock, as one step. They used to be
    /// two - "has this phone had ten wrong PINs?" and, later, "note another
    /// wrong one" - and a script firing fifty guesses at once got all fifty
    /// past the first question before any of them reached the second.
    /// </summary>
    internal sealed class AttemptLimiter
    {
        private readonly int _max;
        private readonly TimeSpan _window;
        private readonly int _capacity;
        private readonly Dictionary<string, List<DateTime>> _attempts = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        public AttemptLimiter(int max, TimeSpan window, int capacity)
        {
            _max = max;
            _window = window;
            _capacity = capacity;
        }

        /// <summary>
        /// Records an attempt and returns true, or returns false without
        /// recording when the client is already at its limit.
        /// <paramref name="ticket"/> identifies the attempt for Release.
        /// </summary>
        public bool TryReserve(string who, out DateTime ticket)
        {
            var now = DateTime.UtcNow;
            ticket = now;

            lock (_gate)
            {
                if (_attempts.TryGetValue(who, out var times))
                {
                    times.RemoveAll(t => now - t > _window);

                    if (times.Count >= _max)
                    {
                        return false;
                    }
                }
                else
                {
                    MakeRoom(now);
                    times = new List<DateTime>();
                    _attempts[who] = times;
                }

                times.Add(now);
                return true;
            }
        }

        /// <summary>Takes back an attempt that turned out to be fine - a right PIN is not a failure.</summary>
        public void Release(string who, DateTime ticket)
        {
            lock (_gate)
            {
                if (_attempts.TryGetValue(who, out var times))
                {
                    times.Remove(ticket);

                    if (times.Count == 0)
                    {
                        _attempts.Remove(who);
                    }
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _attempts.Clear();
            }
        }

        /// <summary>
        /// Keeps the table bounded without ever forgetting everybody at
        /// once. It used to be cleared wholesale at a thousand entries -
        /// which meant a script could wipe its own record, and everybody
        /// else's, by arriving from a thousand and one addresses. Now the
        /// expired go first, and only then the longest-quiet.
        /// </summary>
        private void MakeRoom(DateTime now)
        {
            if (_attempts.Count < _capacity)
            {
                return;
            }

            foreach (var key in _attempts.Keys.ToList())
            {
                var times = _attempts[key];
                times.RemoveAll(t => now - t > _window);

                if (times.Count == 0)
                {
                    _attempts.Remove(key);
                }
            }

            while (_attempts.Count >= _capacity)
            {
                var quietest = _attempts.MinBy(pair => pair.Value.Count == 0 ? DateTime.MinValue : pair.Value.Max()).Key;
                _attempts.Remove(quietest);
            }
        }
    }

    /// <summary>
    /// Ten wrong PINs from one device in five minutes, and it waits.
    /// Without this a four-digit PIN is ten thousand requests - about a
    /// minute's work for a script on the same Wi-Fi.
    /// </summary>
    private readonly AttemptLimiter _pinFailures = new(10, TimeSpan.FromMinutes(5), 1000);

    /// <summary>
    /// Thirty check-in changes a minute from one device. A desk tablet
    /// signing a queue in is nowhere near it; a script toggling somebody in
    /// and out to hammer the laptop's disk is stopped well short.
    /// </summary>
    private readonly AttemptLimiter _checkInWrites = new(30, TimeSpan.FromMinutes(1), 1000);

    /// <summary>
    /// Check-in changes from every device together: 120 a minute. Two a
    /// second is a queue at the desk moving faster than any real one; the
    /// ceiling is there for addresses that keep changing. See
    /// ReserveCheckInWrite.
    /// </summary>
    private readonly AttemptLimiter _checkInWritesAll = new(120, TimeSpan.FromMinutes(1), 1);

    /// <summary>
    /// The race-control buttons' brake: one command at a time, and so many
    /// a minute per device and from all of them together. See
    /// RemoteCommandGate, HandleCommand and - for the Race tab's flags on
    /// /manage, which share it - HandleManageAsync.
    /// </summary>
    private readonly RemoteCommandGate _commands = new();

    /// <summary>
    /// The longest anything holds <see cref="_commands"/>: three seconds,
    /// the same bound MainForm's RunCommand puts on /api/command. /manage's
    /// race.flag lets go of the gate after this too, rather than holding it
    /// for HandleJsonAsync's ten-plus-sixty-second timeouts.
    /// </summary>
    private static readonly TimeSpan CommandGateHold = TimeSpan.FromSeconds(3);

    /// <summary>
    /// <see cref="RemoteCommandGate.TryBegin"/>, except that a RED or YELLOW
    /// is never turned away just because some OTHER command is in progress:
    /// it waits its turn - at most <see cref="CommandGateHold"/> and a
    /// little - and then goes. A marshal's RED used to be refused "NOT sent"
    /// while a GREEN or a Race-tab flag was still being carried out, and if
    /// that one then timed out neither flag reached the track. The same flag
    /// already going out is still answered at once (two marshals pressing RED
    /// together is one red flag), and the per-minute counts still apply.
    /// Called on a request thread, never the UI thread.
    /// </summary>
    private RemoteCommandGate.Answer BeginCommand(string device, string command, out string? running)
    {
        var answer = _commands.TryBegin(device, command, out running);

        if (answer != RemoteCommandGate.Answer.Busy || command is not ("RED" or "YELLOW"))
        {
            return answer;
        }

        var giveUp = DateTime.UtcNow + CommandGateHold + TimeSpan.FromSeconds(2);

        while (answer == RemoteCommandGate.Answer.Busy
            && !string.Equals(running, command, StringComparison.Ordinal)
            && DateTime.UtcNow < giveUp)
        {
            Thread.Sleep(50);
            answer = _commands.TryBegin(device, command, out running);
        }

        return answer;
    }

    /// <summary>
    /// Who a request is from, for counting: the device's own address.
    ///
    /// IPv6 addresses are counted whole. They used to be counted by /64,
    /// on the theory that one /64 is one device - true on the internet,
    /// false on a club's Wi-Fi, where every phone shares the network's /64
    /// and every phone's link-local fe80:: address is in the same one. One
    /// phone mistyping ten times then locked out every other IPv6 phone in
    /// the building. A device that keeps changing its address to dodge the
    /// count is what the whole-PIN limit (PinLockout) is for. An IPv4
    /// address that arrives dressed as IPv6 (::ffff:192.168.1.20) is the
    /// same phone as the plain one and is counted with it.
    /// </summary>
    private static string ClientKey(HttpListenerContext context)
    {
        var address = context.Request.RemoteEndPoint?.Address;

        if (address is null)
        {
            return "unknown";
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // The scope (%12) is the laptop's own interface number, not
            // part of who is asking; the bytes alone are the address.
            return Convert.ToHexString(address.GetAddressBytes());
        }

        return address.ToString();
    }

    /// <summary>
    /// A limit on wrong guesses at one PIN from everybody at once, on top
    /// of the per-device one.
    ///
    /// The per-device limit counts by address, and a device can change its
    /// address - a laptop script can give itself dozens of IPv4 aliases,
    /// and an IPv6 phone can make up a new address whenever it likes. At
    /// ten guesses per address, a four-digit remote PIN falls to a few
    /// hundred of them. So wrong guesses are also counted for the PIN as a
    /// whole: fifty in five minutes for the remote (two hundred for the
    /// check-in page's PIN), from anywhere, and that page stops
    /// taking PINs for five minutes.
    ///
    /// Except from a device that has got this PIN right recently. It
    /// already knows the PIN, so it cannot be the one guessing it, and the
    /// race director's phone keeps working while somebody else's script is
    /// being kept out. The people it does stop are those who have not yet
    /// typed the PIN in, for five minutes, which is the price of the four
    /// digits holding.
    ///
    /// How many wrong guesses that takes is set per PIN (maxWrong) - see
    /// the two instances below for why they differ.
    /// </summary>
    private sealed class PinLockout
    {
        private const int MaxTrusted = 1000;
        private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan TrustFor = TimeSpan.FromHours(12);

        private readonly List<DateTime> _attempts = new();
        private readonly Dictionary<string, DateTime> _trusted = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private DateTime _lockedUntilUtc = DateTime.MinValue;
        private readonly int _maxWrong;

        public PinLockout(int maxWrong)
        {
            _maxWrong = maxWrong;
        }

        /// <summary>How many wrong guesses in five minutes lock this PIN. For the console warning.</summary>
        public int MaxWrong => _maxWrong;

        /// <summary>
        /// Counts an attempt as wrong until Release says otherwise, and
        /// returns true - or returns false, counting nothing, when this PIN
        /// is locked and <paramref name="who"/> is not a device that knows
        /// it. Counted before the comparison, for the same reason the
        /// per-device limit is: a burst of simultaneous guesses must not
        /// all get past the check before any of them is counted.
        /// </summary>
        public bool TryReserve(string who, out DateTime ticket)
        {
            var now = DateTime.UtcNow;
            ticket = now;

            lock (_gate)
            {
                _attempts.RemoveAll(t => now - t > Window);

                var trusted = _trusted.TryGetValue(who, out var knewItUtc) && now - knewItUtc <= TrustFor;

                if (!trusted && (now < _lockedUntilUtc || _attempts.Count >= _maxWrong))
                {
                    return false;
                }

                _attempts.Add(now);
                return true;
            }
        }

        /// <summary>The attempt was the right PIN: not a wrong guess, and the device is trusted from now on.</summary>
        public void Release(string who, DateTime ticket)
        {
            lock (_gate)
            {
                _attempts.Remove(ticket);

                if (!_trusted.ContainsKey(who) && _trusted.Count >= MaxTrusted)
                {
                    // Bounded like the other tables: the one trusted
                    // longest ago goes first.
                    var oldest = _trusted.MinBy(pair => pair.Value).Key;
                    _trusted.Remove(oldest);
                }

                _trusted[who] = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// The attempt was right, so it is not counted as a wrong guess - but
        /// the device is NOT trusted for it (26 Oct 2026). For racer PINs,
        /// where knowing one PIN proves nothing about the others.
        /// </summary>
        public void Forgive(DateTime ticket)
        {
            lock (_gate)
            {
                _attempts.Remove(ticket);
            }
        }

        /// <summary>
        /// The attempt was wrong, and stays counted. True when it is the one
        /// that locks the PIN, so the caller can say so once in the console.
        /// </summary>
        public bool ConfirmWrong()
        {
            var now = DateTime.UtcNow;

            lock (_gate)
            {
                _attempts.RemoveAll(t => now - t > Window);

                if (_attempts.Count < _maxWrong || now < _lockedUntilUtc)
                {
                    return false;
                }

                // A fresh count once the lock lifts, rather than the lock
                // lifting straight into a full count.
                _lockedUntilUtc = now + LockFor;
                _attempts.Clear();
                return true;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _attempts.Clear();
                _trusted.Clear();
                _lockedUntilUtc = DateTime.MinValue;
            }
        }

        /// <summary>The PIN has changed: nobody knows the new one yet. The count and any lock stay as they are.</summary>
        public void ForgetTrusted()
        {
            lock (_gate)
            {
                _trusted.Clear();
            }
        }
    }

    /// <summary>
    /// The whole-PIN limit for the phone remote's PIN. See PinLockout.
    /// Kept strict: this PIN throws flags, and nobody but the race
    /// director's own phone should ever be sending it.
    /// </summary>
    private readonly PinLockout _remotePinLockout = new(50);

    /// <summary>
    /// The whole-PIN limit for the check-in page's PIN, counted apart from
    /// the remote's, and four times as generous. This PIN is written on the
    /// wall for drivers by design, so it guards far less - and the honest
    /// mistakes against it come in crowds: a QR poster at the drivers'
    /// briefing, fifty phones opening the page at once, some of them still
    /// remembering last meeting's PIN. At fifty, those alone locked
    /// check-in for everybody who had not yet typed the PIN in.
    /// </summary>
    private readonly PinLockout _checkInPinLockout = new(200);

    /// <summary>
    /// The whole-PIN limit for the race-day manager's PIN, counted apart
    /// from the other two. Kept as strict as the remote's: this PIN can
    /// touch entries, the programme and saved results, so it deserves the
    /// same suspicion as one that can throw a flag.
    /// </summary>
    private readonly PinLockout _managerPinLockout = new(50);

    /// <summary>
    /// The whole-PIN limit for racers' personal PINs on /checkin (26 Oct
    /// 2026): a hundred wrong in five minutes, from everybody together, and
    /// no phone may check anybody in or out for five minutes. Between the
    /// remote's fifty and the wall PIN's two hundred: a racer PIN is six
    /// digits and only ever typed by its own driver, so honest mistakes come
    /// one at a time, not in crowds. Unlike the other three it never trusts
    /// a device (see CheckRacerPin) - a driver who knows their own PIN is
    /// no less a stranger to everybody else's.
    /// </summary>
    private readonly PinLockout _racerPinLockout = new(100);

    /// <summary>
    /// Compares a PIN without saying anything about it through timing.
    /// Both sides are hashed first, so the comparison is always of two
    /// 32-byte values - comparing the raw strings let the time taken reveal
    /// the PIN's length, since unequal lengths were rejected at once.
    /// </summary>
    private static bool PinMatches(string offered, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(offered)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    /// <summary>
    /// Checks the PIN a request carries, in its X-Pin header - never the
    /// address, where it would sit in every browser's history.
    ///
    /// <paramref name="expected"/> is the PIN for THIS page: the remote and
    /// the check-in page each have their own, and each has its own count
    /// of wrong guesses (<paramref name="scope"/>), so a driver fumbling
    /// the check-in PIN does not lock the race director out of the remote.
    ///
    /// <paramref name="required"/> is for the remote and the race-day
    /// manager: with no usable PIN set they refuse outright rather than
    /// letting anybody on the Wi-Fi throw flags or touch the programme.
    /// Check-in stays open without one, as it always has.
    ///
    /// <paramref name="isUsable"/> is what "usable" means for this scope -
    /// <see cref="IsUsableRemotePin"/> (four digits or more) unless the
    /// caller passes something stricter, such as the manager's six.
    /// </summary>
    private bool PinAccepted(
        HttpListenerContext context, string expected, bool required, string scope, Func<string?, bool>? isUsable = null)
    {
        isUsable ??= IsUsableRemotePin;

        // What this scope is called back to the person holding the phone,
        // for the three messages below that name it.
        var label = scope switch
        {
            "remote" => "remote",
            "manager" => "manager",
            _ => "check-in",
        };

        if (required && !isUsable(expected))
        {
            context.Response.StatusCode = 403;
            WriteJson(context, string.IsNullOrEmpty(expected)
                ? $"{{\"ok\":false,\"error\":\"The {label} needs a PIN. Set one on the laptop's Local Screens page first.\"}}"
                : $"{{\"ok\":false,\"error\":\"The {label} PIN on the laptop is too short or has something other than digits. Fix it on the Local Screens page.\"}}");
            return false;
        }

        if (string.IsNullOrEmpty(expected))
        {
            return true;
        }

        var offered = context.Request.Headers["X-Pin"] ?? string.Empty;

        // No PIN at all is not a guess - it is a phone that has not been
        // given one yet, usually the check-in page opening for the first
        // time. It is answered before either limit is touched: counted,
        // fifty phones opening the page from the poster at the drivers'
        // briefing locked check-in for everybody. An empty PIN can never
        // match a set one, so saying so tells nobody anything.
        if (offered.Length == 0)
        {
            context.Response.StatusCode = 403;
            WriteJson(context, $"{{\"ok\":false,\"pin\":true,\"needPin\":true,\"error\":\"Enter the {label} PIN.\"}}");
            return false;
        }

        var device = ClientKey(context);
        var who = scope + "|" + device;
        var lockout = scope switch
        {
            "remote" => _remotePinLockout,
            "manager" => _managerPinLockout,
            _ => _checkInPinLockout,
        };

        // Counted as a failure BEFORE the comparison, and handed back only
        // if it was right - so a burst of simultaneous guesses is limited
        // exactly as a slow trickle of them is.
        if (!_pinFailures.TryReserve(who, out var ticket))
        {
            context.Response.StatusCode = 429;
            WriteJson(context, "{\"ok\":false,\"error\":\"Too many wrong PINs. Wait a few minutes.\"}");
            return false;
        }

        // Then against the PIN as a whole - see PinLockout. Refused here, it
        // was never a guess, so this device's own count gets it back.
        if (!lockout.TryReserve(device, out var lockoutTicket))
        {
            _pinFailures.Release(who, ticket);
            context.Response.StatusCode = 429;
            WriteJson(context, "{\"ok\":false,\"error\":\"Too many wrong PINs on this Wi-Fi. The PIN is not being accepted for five minutes.\"}");
            return false;
        }

        if (PinMatches(offered, expected))
        {
            _pinFailures.Release(who, ticket);
            lockout.Release(device, lockoutTicket);
            return true;
        }

        if (lockout.ConfirmWrong())
        {
            try
            {
                StatusMessage?.Invoke(scope switch
                {
                    "remote" => $"[web] WARNING: {lockout.MaxWrong} wrong remote PINs in five minutes - somebody may be trying to guess it. "
                        + "The remote PIN is refused for five minutes, except on phones that already entered it. "
                        + "Consider a longer PIN on the Local Screens page.",
                    "manager" => $"[web] WARNING: {lockout.MaxWrong} wrong manager PINs in five minutes - somebody may be trying to guess it. "
                        + "The manager PIN is refused for five minutes, except on phones that already entered it. "
                        + "Consider a longer PIN on the Local Screens page.",
                    _ => $"[web] WARNING: {lockout.MaxWrong} wrong check-in PINs in five minutes. "
                        + "The check-in PIN is refused for five minutes, except on phones that already entered it.",
                });
            }
            catch
            {
                // A request thread; the log must not be what fails it.
            }
        }

        context.Response.StatusCode = 403;
        WriteJson(context, "{\"ok\":false,\"pin\":true,\"error\":\"Wrong PIN\"}");
        return false;
    }

    /// <summary>
    /// Refuses a command or a check-in while the laptop cannot act on it.
    /// See HostReady.
    /// </summary>
    private bool HostAccepting(HttpListenerContext context)
    {
        if (HostReady)
        {
            return true;
        }

        context.Response.StatusCode = 503;
        WriteJson(context, "{\"ok\":false,\"error\":\"Laptop still starting - try again in a moment.\"}");
        return false;
    }

    private void HandleRequest(HttpListenerContext context)
    {
        AddSecurityHeaders(context.Response);

        if (!IsLocalHost(context.Request.Headers["Host"]))
        {
            context.Response.StatusCode = 421;
            WriteText(context, "This address is not served here.");
            return;
        }

        var path = context.Request.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant() ?? "/";

        switch (path)
        {
            case "":
            case "/":
                WriteHtml(context, BuildLiveResultsPage());
                break;
            case "/remote":
                WriteHtml(context, BuildRemotePage());
                break;
            case "/checkin":
                WriteHtml(context, BuildCheckInPage());
                break;
            case "/manage":
                WriteHtml(context, BuildManagePage());
                break;
            case "/api/manage":
                // HandleRequest runs on its own thread-pool thread per
                // request (see DispatchRequest) with no synchronization
                // context to deadlock against, so waiting here for the
                // async manager call keeps this method's contract - answer
                // written before it returns - the same for every case in
                // this switch.
                HandleManageAsync(context).GetAwaiter().GetResult();
                break;
            case "/api/roster":
                // Behind the check-in PIN when there is one: the list of who
                // is here is exactly what that PIN is protecting.
                if (CheckInOpen(context) && PinAccepted(context, CheckInPin, required: false, scope: "checkin"))
                {
                    WriteJson(context, BuildRosterJson());
                }

                break;
            case "/api/checkin":
                HandleCheckIn(context);
                break;
            case "/api/state":
                // The published copy, never a fresh build. See PublishState.
                //
                // No PIN and no gate, by design: it is the spectator board.
                // So nothing in it may be worth a PIN - every id in it is
                // the roster's own opaque one rather than a transponder,
                // for the reason AddFollowing gives.
                WriteJson(context, _publishedState);
                break;
            case "/api/look":
                // Which look the pages would be built with now - see
                // LookWatchScript. Kept out of /api/state on purpose: that
                // document is also what the relay publishes.
                WriteJson(context, "{\"v\":\"" + CurrentPageLook().Fingerprint + "\"}");
                break;
            case "/api/command":
                HandleCommand(context);
                break;
            case "/api/sponsor":
                HandleSponsorImage(context);
                break;
            case "/logo.png":
                HandleLogo(context);
                break;
            case "/shoutout":
                // The room's way into the broadcast - see
                // WebServerService.Notes. Served even when the club has it
                // switched off, with the page saying so, because a club
                // that printed the address on a poster should get an
                // explanation rather than a 404.
                WriteHtml(context, BuildNotePage());
                break;
            case "/api/note":
                HandleClubNote(context);
                break;
            case "/vote":
                // The room's vote on who wins - see
                // WebServerService.Vote. Served even when the club has it
                // switched off, with the page saying no vote is running,
                // for the same reason /shoutout is.
                WriteHtml(context, BuildVotePage());
                break;
            case "/api/vote/ballot":
                HandleVoteBallot(context);
                break;
            case "/api/vote":
                HandleRoomVote(context);
                break;
            case "/coach":
                // One driver's laps, trend and report card on their own
                // phone - see WebServerService.Coach. No PIN: it shows only
                // what the spectator board already does, and the card it
                // adds leaves out anything private (22 Oct 2026).
                WriteHtml(context, BuildCoachPage());
                break;
            case "/api/coach":
                HandleCoachCard(context);
                break;
            case KeepAwake.Address:
                // The silent video that keeps a phone awake on the pit
                // board, where the browser will not offer a wake lock to a
                // plain-http page - see KeepAwake (22 Oct 2026). No PIN: it
                // is the same few bytes for everybody.
                HandleKeepAwake(context);
                break;
            case "/broadcast":
                // The broadcast graphics for OBS - see WebServerService.Camera.
                WriteHtml(context, BuildBroadcastPage());
                break;
            case "/camera":
            case "/api/camera/shots":
            case "/api/camera/frame":
                // The photo-finish camera, on this PC only - see WebServerService.Camera.
                HandleCamera(context, path);
                break;
            default:
                context.Response.StatusCode = 404;
                WriteText(context, "Not found");
                break;
        }
    }

    private void HandleCommand(HttpListenerContext context)
    {
        if (!IsAppWrite(context) || !PinAccepted(context, Pin, required: true, scope: "remote")
            || !HostAccepting(context))
        {
            return;
        }

        var query = context.Request.QueryString;
        var action = query["action"];

        var allowed = new[] { "stage", "countdown", "green", "yellow", "red", "checkered", "reset", "skip", "holdtalk", "callup" };
        if (action is null || !allowed.Contains(action))
        {
            context.Response.StatusCode = 400;
            WriteJson(context, "{\"ok\":false,\"error\":\"Unknown action\"}");
            return;
        }

        var force = query["force"] == "1";

        // The race the phone's yes was about, when the page says: the
        // question below carries the race it was asked about, and the page
        // sends that back with force=1. Optional - an older page's force=1
        // is taken as before.
        int? shownRace = int.TryParse(query["raceGeneration"], System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var parsedRace)
            ? parsedRace
            : null;

        // Read once, from this request thread; the host checks again on the
        // UI thread before acting.
        var raceNow = _race.SessionGeneration;

        // A yes about a race that is no longer the one on track is no yes
        // for this one: asked again, about this one.
        var confirmedHere = force && (shownRace is not { } shown || shown == raceNow);

        // Staging or counting down over a running race wipes its standings,
        // and a button on a phone in a pocket is one brush away from it. The
        // page sends every press without force=1 first; over a running race
        // it is answered with this question, which the page puts to its user,
        // and it sends the command again with force=1 on a yes. The page used
        // to ask first, from the state it last saw - and practice, which runs
        // in the Racing state too, was asked about a race that wasn't there.
        // Not for practice: Race Control refuses Stage and Countdown then,
        // and says why. Read from a request thread, but these are single
        // reads, and the host checks again on the UI thread before acting.
        if (!confirmedHere && (action is "stage" or "countdown") && _race.State == RaceState.Racing && !_race.IsPracticeMode)
        {
            context.Response.StatusCode = 409;
            WriteJson(context, JsonSerializer.Serialize(new
            {
                ok = false,
                confirm = true,
                error = _race.IsDemoMode
                    ? "A demo is running - this would end it."
                    : "A race is running - this would wipe its standings.",
                raceGeneration = raceNow,
            }));
            return;
        }

        // Read once: the host can set or clear it from its own thread while
        // this request is on its way through.
        var run = RunCommand;

        if (run is null)
        {
            context.Response.StatusCode = 503;
            WriteJson(context, "{\"ok\":false,\"error\":\"Laptop still starting - try again in a moment.\"}");
            return;
        }

        var name = action.ToUpperInvariant();
        var confirmed = force ? " (confirmed over a running race)" : string.Empty;

        // Here, just before Race Control is asked - the checks above cost
        // the laptop nothing, and the question over a running race is free
        // to ask. There used to be no limit at all: see RemoteCommandGate.
        switch (BeginCommand(ClientKey(context), name, out var running))
        {
            case RemoteCommandGate.Answer.Busy:
                context.Response.StatusCode = 503;
                context.Response.AddHeader("Retry-After", "1");
                WriteJson(context, JsonSerializer.Serialize(new
                {
                    ok = false,
                    error = $"NOT sent - the laptop is still carrying out {running}. "
                        + "Check the flag, then press again if it is still needed.",
                }));
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [web] Remote command: {action}{confirmed} - turned away, {running} was still being carried out.");
                return;

            case RemoteCommandGate.Answer.TooMany:
                context.Response.StatusCode = 429;
                context.Response.AddHeader("Retry-After", "60");
                WriteJson(context, "{\"ok\":false,\"error\":\"NOT sent - too many commands in the last minute. Wait a minute, and check the laptop.\"}");
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [web] Remote command: {action}{confirmed} - turned away, too many commands in the last minute.");
                return;
        }

        RemoteCommandOutcome outcome;

        try
        {
            // On this request's own thread, and it can take a few seconds:
            // the host waits for Race Control to finish with the command, so
            // the phone can be told what it did. See RunCommand.
            outcome = run(action, force, shownRace);
        }
        catch (Exception ex)
        {
            // Whether it ran before it failed is not something this can
            // know, so the phone is told neither - only to look.
            context.Response.StatusCode = 500;
            WriteJson(context, JsonSerializer.Serialize(new
            {
                ok = false,
                error = "The laptop hit a problem with that command. Check the laptop before trying again.",
            }));
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [web] Remote command: {action}{confirmed} - failed on the laptop ({ex.Message}). The phone was told to check the laptop.");
            return;
        }
        finally
        {
            // Whatever became of it, this phone is about to be answered -
            // the next command may go.
            _commands.End();
        }

        // The phone used to be answered {"ok":true} - shown as "GREEN sent" -
        // as soon as the command had been handed to the UI thread, whatever
        // Race Control did with it next, so a command it refused still read
        // as sent. Each answer now says what became of it, in Race Control's
        // own words; a host that gave none gets a plain sentence for the
        // phone rather than a blank line. The page puts the command's name
        // in front of a refusal or a wait itself ("GREEN not done: ..."), so
        // those sentences do not repeat it.
        var said = string.IsNullOrWhiteSpace(outcome.Message) ? null : outcome.Message.Trim();

        int status;
        string answer;
        string logged;

        switch (outcome.Status)
        {
            case RemoteCommandStatus.Done:
                said ??= $"{name} done.";
                status = 200;
                answer = JsonSerializer.Serialize(new { ok = true, message = said });
                logged = $"done. The phone was told \"{said}\"";
                break;

            case RemoteCommandStatus.Refused:
                // 409 like the "a race is running" answer above, but never
                // with "confirm": that flag is how the page tells the two
                // apart. That one asks the phone a question; this one is
                // final, and the phone shows the command as NOT done.
                said ??= "Race Control did not do it - check the laptop to see why.";
                status = 409;
                answer = JsonSerializer.Serialize(new { ok = false, refused = true, error = said });
                logged = $"refused. The phone was told \"{said}\"";
                break;

            case RemoteCommandStatus.Waiting:
                // It reached Race Control, which had not finished with it
                // when the phone had to be answered - usually a question on
                // the laptop. Sent, then, but what happens is up to the laptop.
                said ??= "Race Control has not finished with it - check the laptop.";
                status = 202;
                answer = JsonSerializer.Serialize(new { ok = true, waiting = true, message = said });
                logged = $"still waiting on the laptop. The phone was told \"{said}\"";
                break;

            case RemoteCommandStatus.NeedsConfirm:
                // A race went green while the command was on its way: the
                // same question the check above asks, so the page asks its
                // user and sends it again with force=1 on a yes.
                said ??= "A race is running - this would wipe its standings.";
                status = 409;
                answer = JsonSerializer.Serialize(new { ok = false, confirm = true, error = said, raceGeneration = _race.SessionGeneration });
                logged = $"needs the phone's yes - a race is running. The phone was asked \"{said}\"";
                break;

            case RemoteCommandStatus.NotDelivered:
                said ??= "The laptop could not take that command just now - it was NOT sent. Try again.";
                status = 503;
                answer = JsonSerializer.Serialize(new { ok = false, error = said });
                logged = $"the laptop could not take it just then. The phone was told \"{said}\"";
                break;

            default:
                // An answer this version does not know. Claimed neither way.
                said = "The laptop did not say what became of that command. Check the laptop before trying again.";
                status = 500;
                answer = JsonSerializer.Serialize(new { ok = false, error = said });
                logged = $"race control answered {outcome.Status}, which this version does not know. The phone was told to check the laptop.";
                break;
        }

        // Answered first and logged after, so the log can never be what
        // keeps the phone from hearing back.
        context.Response.StatusCode = status;
        WriteJson(context, answer);
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [web] Remote command: {action}{confirmed} - {logged}");
    }

    private string BuildRosterJson()
    {
        // Read once into a local: the field can be swapped by the UI thread
        // between two reads, and a list that changed halfway through being
        // serialised is the kind of fault nobody ever reproduces.
        var view = _rosterView;
        var roster = view.Entries;

        return JsonSerializer.Serialize(new
        {
            // An opaque id, never the transponder - see RosterView.
            drivers = roster.Select((d, i) => new
            {
                t = view.Ids[i],
                n = d.Name,
                c = d.ClassName,
                @in = d.CheckedIn,
            }),
            checkedIn = roster.Count(d => d.CheckedIn),
            total = roster.Count,
        });
    }

    /// <summary>
    /// Takes one check-in write slot for this request - this phone's share,
    /// and everybody's together - or answers 429 and returns false.
    ///
    /// The all-devices ceiling is the backstop the per-device count needs:
    /// the count is by address, an address is cheap to change on a venue's
    /// Wi-Fi, and with a blank check-in PIN there is no PIN limit behind it.
    /// Every accepted change is a flushed write on the UI thread, so a
    /// script toggling somebody in and out from a few dozen addresses
    /// starved the race tick (25 Sep security audit).
    /// </summary>
    private bool ReserveCheckInWrite(HttpListenerContext context)
    {
        var device = ClientKey(context);

        if (!_checkInWrites.TryReserve(device, out var ticket))
        {
            context.Response.StatusCode = 429;
            WriteJson(context, "{\"ok\":false,\"error\":\"Too many changes from this phone. Wait a minute.\"}");
            return false;
        }

        if (!_checkInWritesAll.TryReserve("all", out _))
        {
            // Not this phone's doing - its own count is given back.
            _checkInWrites.Release(device, ticket);
            context.Response.StatusCode = 429;
            WriteJson(context, "{\"ok\":false,\"error\":\"The laptop is taking a lot of check-ins right now. Try again in a minute, or check in at the desk.\"}");
            return false;
        }

        return true;
    }

    private void HandleCheckIn(HttpListenerContext context)
    {
        if (!IsAppWrite(context) || !CheckInOpen(context)
            || !PinAccepted(context, CheckInPin, required: false, scope: "checkin")
            || !HostAccepting(context))
        {
            return;
        }

        var query = context.Request.QueryString;

        var raw = (query["t"] ?? string.Empty).Trim();

        if (raw.Length == 0)
        {
            context.Response.StatusCode = 400;
            WriteJson(context, "{\"ok\":false,\"error\":\"No driver\"}");
            return;
        }

        var view = _rosterView;

        // A row on the page sends the opaque id it was given. A phone that
        // managed to read a printed badge sends the badge payload as it
        // was scanned - the MZRD: form or an online badge link - and that
        // is unwrapped here rather than in the page, which keeps one
        // definition of what a badge is: the same one the USB scanner on
        // the Entries page uses. A bare transponder number is no longer
        // accepted; no page this server serves is given one as an id any
        // more. That claim was wrong for a while: /api/state, which needs
        // no PIN, keyed its rows, its map cars and its follow list by
        // transponder until 9 Oct 2026 - see AddFollowing.
        // Set once this request has used up a write slot, so a badge that
        // turns out to be a real change is not charged twice.
        var reserved = false;

        if (!view.ById.TryGetValue(raw.ToLowerInvariant(), out var known))
        {
            // A badge payload carries the transponder itself - the durable
            // number the opaque ids exist to keep off the wire. Answered
            // "not on this list" or "already so" for free, it was an oracle:
            // probe a number, meeting after meeting, and learn whether that
            // car is here tonight (25 Sep security audit). Every answer to
            // this path now costs a slot, whatever it turns out to be.
            if (!ReserveCheckInWrite(context))
            {
                return;
            }

            reserved = true;

            var transponder = QrBadgeRenderer.ParsePayload(raw);

            known = string.IsNullOrWhiteSpace(transponder)
                ? null
                : view.Entries.FirstOrDefault(d =>
                    string.Equals(d.Transponder, transponder, StringComparison.OrdinalIgnoreCase));
        }

        if (known is null)
        {
            context.Response.StatusCode = 404;
            WriteJson(context, "{\"ok\":false,\"error\":\"Not a driver on this list\"}");
            return;
        }

        // That driver's own PIN, before anything is changed or even said
        // about them (26 Oct 2026 - see _racerPinChecks): the wall PIN
        // above only opens the list, and a QR badge carries the transponder,
        // which is no secret. Before "already so", too, or that answer would
        // tell a stranger whether somebody is here.
        if (!RacerPinAccepted(context, known))
        {
            return;
        }

        var wanted = !string.Equals(query["state"], "out", StringComparison.OrdinalIgnoreCase);

        // Already so: nothing to do, and nothing to write. A double tap, or
        // a badge scanned twice, used to be a disk write and a rebuild of
        // the Entries page each time.
        if (known.CheckedIn == wanted)
        {
            WriteJson(context, "{\"ok\":true}");
            return;
        }

        if (!reserved && !ReserveCheckInWrite(context))
        {
            return;
        }

        var handler = CheckInRequested;

        if (handler is null)
        {
            context.Response.StatusCode = 503;
            WriteJson(context, "{\"ok\":false,\"error\":\"Laptop still starting - try again in a moment.\"}");
            return;
        }

        handler(known.Transponder, wanted);
        AssumeCheckIn(view, known, wanted);
        StatusMessage?.Invoke($"[web] {known.Name} checked {(wanted ? "in" : "out")} from a phone.");

        // Optimistic: the change is applied on the UI thread a moment from
        // now, and the page's next poll is what confirms it. Saying "ok"
        // here means "accepted", which is the only honest thing a
        // fire-and-forget endpoint can say.
        WriteJson(context, "{\"ok\":true}");
    }

    /// <summary>
    /// Marks a driver in or out in this server's own copy of the roster, as
    /// the host is about to in its store.
    ///
    /// The "already so" check above reads this copy, and the host's
    /// republish can be a moment behind. Without this, a quick in-then-out
    /// from one phone found the copy still saying "out" and answered the
    /// out as already done - while the store had them in. The host's next
    /// publish replaces this with the real thing either way.
    /// </summary>
    private void AssumeCheckIn(RosterView view, CheckInEntry known, bool wanted)
    {
        // Under the gate, so the check and the replace are one step - see
        // _rosterGate.
        lock (_rosterGate)
        {
            var current = _rosterView;

            // Only on the copy this request read, or on one made from it by
            // another phone's tap a moment ago - those share its id list,
            // and building on them keeps both taps rather than letting the
            // second quietly undo the first. Anything else is a roster the
            // host has published since, which is better than this guess.
            if (!ReferenceEquals(current.Ids, view.Ids))
            {
                return;
            }

            var index = -1;

            for (var i = 0; i < view.Entries.Count; i++)
            {
                if (ReferenceEquals(view.Entries[i], known))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0 || index >= current.Entries.Count)
            {
                return;
            }

            var entries = current.Entries.ToList();
            entries[index] = entries[index] with { CheckedIn = wanted };

            var byId = new Dictionary<string, CheckInEntry>(StringComparer.Ordinal);

            for (var i = 0; i < entries.Count; i++)
            {
                byId.TryAdd(current.Ids[i], entries[i]);
            }

            _roster = entries;
            _rosterView = new RosterView(entries, current.Ids, byId);
        }
    }

    /// <summary>
    /// The most a /manage request's body is read before it is refused. Well
    /// above RaceDayService.MaxRequestChars (64K characters, checked once
    /// the body is parsed) - this is the transport's own, cheaper backstop
    /// so nobody has to buffer an unbounded body to find out it is too big.
    /// </summary>
    private const int MaxManageBodyBytes = 256 * 1024;

    /// <summary>
    /// The race-day manager's one JSON endpoint. Thin on purpose - the only
    /// thing this method decides is who may ask (the CSRF header, the
    /// manager PIN, a body that isn't absurd, and for race.flag the same
    /// RemoteCommandGate as /api/command); what a request means,
    /// whether it needs a yes first, and whether the laptop is too busy for
    /// it are all RaceDayService.HandleJsonAsync's job, and it always
    /// answers with well-formed JSON, never an exception - so whatever it
    /// comes back with is written straight to the phone.
    /// </summary>
    private async Task HandleManageAsync(HttpListenerContext context)
    {
        if (!IsAppWrite(context)
            || !PinAccepted(context, ManagerPin, required: true, scope: "manager", isUsable: RaceDayService.IsUsableManagerPin)
            || !HostAccepting(context))
        {
            return;
        }

        // Set once, by MainForm, before the window is ever shown - see
        // WireRaceDayService - so this is really a guard against a future
        // caller that starts the server without it, not something a phone
        // should ever see in practice.
        if (Manager is not { } manager)
        {
            context.Response.StatusCode = 503;
            WriteJson(context, "{\"ok\":false,\"error\":\"The race-day manager isn't ready yet - try again in a moment.\"}");
            return;
        }

        string? body;

        try
        {
            body = await ReadCappedBodyAsync(context.Request, MaxManageBodyBytes).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or HttpListenerException)
        {
            // The phone went away mid-upload - not worth a 500 and a log
            // line, since there is nobody left to read either. Aborted
            // rather than just left, so the listener lets go of the request.
            try
            {
                context.Response.Abort();
            }
            catch
            {
                // Already gone.
            }

            return;
        }

        if (body is null)
        {
            context.Response.StatusCode = 413;
            WriteJson(context, "{\"ok\":false,\"error\":\"That request was too big.\"}");
            return;
        }

        // A flag from the Race tab goes through the same gate as the flag
        // remote's (/api/command): one command at a time across both pages,
        // and the same per-device and all-devices counts. This endpoint used
        // to have none. Each race.flag is answered "sent" at once and posts
        // Race Control's trigger to the UI thread unconditionally, so a
        // marshal tapping RED five times on laggy Wi-Fi got five red flags,
        // five announcements and five forced journal writes mid-race - and a
        // script with the manager PIN could queue hundreds ahead of the race
        // tick, the very flood RemoteCommandGate closed on /api/command. The
        // rest of /manage is left as it was: those ops change the program or
        // results, not the race on track, and RaceDayService checks them.
        var (isFlag, flagName, requestId) = ManageFlagRequest(body);
        var gated = false;

        if (isFlag)
        {
            switch (BeginCommand(ClientKey(context), flagName, out var running))
            {
                case RemoteCommandGate.Answer.Busy:
                    context.Response.StatusCode = 503;
                    context.Response.AddHeader("Retry-After", "1");
                    WriteJson(context, JsonSerializer.Serialize(new
                    {
                        id = requestId,
                        ok = false,
                        error = $"NOT sent - the laptop is still carrying out {running}. "
                            + "Check the flag, then press again if it is still needed.",
                    }));
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [web] Race-day manager flag: {flagName} - turned away, {running} was still being carried out.");
                    return;

                case RemoteCommandGate.Answer.TooMany:
                    context.Response.StatusCode = 429;
                    context.Response.AddHeader("Retry-After", "60");
                    WriteJson(context, JsonSerializer.Serialize(new
                    {
                        id = requestId,
                        ok = false,
                        error = "NOT sent - too many commands in the last minute. Wait a minute, and check the laptop.",
                    }));
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [web] Race-day manager flag: {flagName} - turned away, too many commands in the last minute.");
                    return;
            }

            gated = true;
        }

        // The server's own stop token: if the laptop stops the web server
        // mid-request, this unblocks rather than riding out its own
        // ten-to-seventy-second timeouts for an answer nobody can send any
        // more.
        Task<string> pending;

        try
        {
            // Inside the try: reading the token of a server being stopped
            // can throw, and the gate must still be let go.
            pending = manager.HandleJsonAsync(body, RaceDayChannel.WiFi, _cts?.Token ?? CancellationToken.None);

            // Held until the op has run on the UI thread (which posts the
            // flag's trigger there before it answers, so the next command's
            // op lands behind it) - but no longer than CommandGateHold. It
            // used to be held for the whole of HandleJsonAsync, up to ten
            // seconds and then sixty more on a busy laptop, and a RED from
            // the flag remote in that window was refused. Now, as on
            // /api/command, a laptop that is not keeping up gets at most one
            // flag every three seconds.
            if (gated)
            {
                await Task.WhenAny(pending, Task.Delay(CommandGateHold)).ConfigureAwait(false);
            }
        }
        finally
        {
            // Exactly once - End does not know whose command it is ending,
            // so letting go again later could open the gate under the next
            // one.
            if (gated)
            {
                _commands.End();
            }
        }

        var answer = await pending.ConfigureAwait(false);

        WriteJson(context, answer);
    }

    /// <summary>
    /// Whether a /manage body asks for race.flag, and if so the flag as the
    /// phone shows it ("RED") and the request's id to echo back. Only a peek
    /// for the gate - anything unreadable is left for RaceDayService to turn
    /// down in its own words.
    /// </summary>
    internal static (bool IsFlag, string Name, string? Id) ManageFlagRequest(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("op", out var op) || op.ValueKind != JsonValueKind.String
                || op.GetString()?.Trim() != "race.flag")
            {
                return (false, string.Empty, null);
            }

            string? id = null;

            if (root.TryGetProperty("id", out var idValue))
            {
                id = idValue.ValueKind switch
                {
                    JsonValueKind.String => idValue.GetString(),
                    JsonValueKind.Number => idValue.GetRawText(),
                    _ => null,
                };

                if (id is { Length: > 100 })
                {
                    id = id[..100];
                }
            }

            var name = root.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object
                && args.TryGetProperty("flag", out var flag) && flag.ValueKind == JsonValueKind.String
                ? (flag.GetString() ?? string.Empty).Trim().ToUpperInvariant()
                : string.Empty;

            // Counted even with no readable flag: it is still a race.flag
            // request, and a junk one should not be a way round the count.
            return (true, name.Length is > 0 and <= 20 ? name : "a flag", id);
        }
        catch (JsonException)
        {
            return (false, string.Empty, null);
        }
    }

    /// <summary>
    /// Reads a request body up to <paramref name="maxBytes"/> and stops,
    /// returning null rather than the (start of a) body that turns out to
    /// be bigger. Nothing before /manage ever read a request body in this
    /// server, so there was no existing helper to share.
    /// </summary>
    private static async Task<string?> ReadCappedBodyAsync(HttpListenerRequest request, int maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var stream = request.InputStream;

        while (true)
        {
            var read = await stream.ReadAsync(chunk, 0, chunk.Length).ConfigureAwait(false);

            if (read == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, read);

            if (buffer.Length > maxBytes)
            {
                return null;
            }
        }

        // Always UTF-8 regardless of what the request claims: every caller
        // is this page's own fetch(), whose body is JSON.stringify's output,
        // which is UTF-8 whatever charset (if any) the browser happens to
        // put on the Content-Type header.
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    /// <summary>
    /// A sponsor slide's bytes, read from disk once and kept until the file
    /// changes.
    ///
    /// Every phone and TV showing the sponsors fetches the current slide on
    /// every rotation, and each fetch read the whole file again - a 12 MB
    /// photo is a large-object allocation per request, and twenty screens
    /// at once kept the garbage collector busy enough to make the race tick
    /// late (25 Sep security audit). Kept by path and checked against the
    /// file's size and time, so a slide replaced under the same name is read
    /// afresh. Bounded: past the budget the cache starts again rather than
    /// growing with however many slides the club adds.
    /// </summary>
    private byte[]? SlideBytes(string path)
    {
        long length;
        DateTime written;

        try
        {
            var info = new FileInfo(path);

            if (!info.Exists)
            {
                return null;
            }

            length = info.Length;
            written = info.LastWriteTimeUtc;
        }
        catch
        {
            return null;
        }

        lock (_slideGate)
        {
            if (_slides.TryGetValue(path, out var kept) && kept.Length == length && kept.Written == written)
            {
                return kept.Bytes;
            }
        }

        if (SponsorStore.ReadSlide(path) is not { } bytes)
        {
            return null;
        }

        lock (_slideGate)
        {
            if (_slides.TryGetValue(path, out var old))
            {
                _slideBytes -= old.Bytes.Length;
            }

            if (_slideBytes + bytes.Length > SlideCacheBudget)
            {
                _slides.Clear();
                _slideBytes = 0;
            }

            _slides[path] = (length, written, bytes);
            _slideBytes += bytes.Length;
        }

        return bytes;
    }

    /// <summary>The most sponsor-slide bytes SlideBytes keeps at once.</summary>
    private const long SlideCacheBudget = 64L * 1024 * 1024;

    private readonly object _slideGate = new();
    private readonly Dictionary<string, (long Length, DateTime Written, byte[] Bytes)> _slides = new(StringComparer.OrdinalIgnoreCase);
    private long _slideBytes;

    private void HandleSponsorImage(HttpListenerContext context)
    {
        var indexText = context.Request.QueryString["i"];

        // Read ONCE into a local. The list belongs to the UI thread, which
        // can add or remove a slide between a Count and an indexer - and
        // this runs on a request thread, where that lands as an unhandled
        // IndexOutOfRange rather than a 404.
        List<string>? slides;

        try
        {
            slides = _sponsors?.SlideFileNames.ToList();
        }
        catch
        {
            // The list changed while it was being copied, because the
            // operator is editing the slides. One missed image is the right
            // outcome; an unhandled exception on a request thread is not.
            slides = null;
        }

        if (slides is null || !int.TryParse(indexText, out var index)
            || index < 0 || index >= slides.Count)
        {
            context.Response.StatusCode = 404;
            WriteText(context, "Not found");
            return;
        }

        var path = _sponsors!.ResolvePath(slides[index]);
        if (path is null)
        {
            context.Response.StatusCode = 404;
            WriteText(context, "Not found");
            return;
        }

        // A video is streamed off disk, never cached. SlideBytes keeps up to
        // 64 MB of pictures in memory because a picture is small and is
        // asked for once per phone per turn; a single ad can be bigger than
        // that whole budget on its own, and every phone in the building asks
        // for it at once.
        if (SponsorMedia.IsVideoName(slides[index]))
        {
            HandleSponsorVideo(context, path);
            return;
        }

        // Set once the image starts going out. From then on a 500 cannot be
        // sent - see the catches below.
        var sending = false;

        try
        {
            // Only a slide the Sponsors page would take in, sized from its
            // header (see SponsorStore.ReadSlide). This used to read the whole
            // file for every request whatever its size - a planted file of
            // any length, pulled into memory once per phone per page load.
            if (SlideBytes(path) is not { } bytes)
            {
                context.Response.StatusCode = 404;
                WriteText(context, "Not found");
                return;
            }

            // From what the bytes are rather than the file's extension, since
            // these pages tell the phone not to guess (nosniff): a PNG saved
            // as .jpg would otherwise not be shown at all.
            context.Response.ContentType = ImageProbe.IsPng(bytes) ? "image/png"
                : bytes is [0x47, 0x49, 0x46, 0x38, ..] ? "image/gif"
                : bytes is [0x42, 0x4D, ..] ? "image/bmp"
                : "image/jpeg";
            context.Response.ContentLength64 = bytes.Length;
            sending = true;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }
        catch when (sending)
        {
            // WHY (7 Nov 2026, Bug Hunt 13): nearly always a phone or TV
            // that moved on, or dropped off the Wi-Fi, partway through a
            // big slide. The headers have gone, so a 500 and a body now
            // would throw again - and that reached the console as a
            // "Request failed" line for an ordinary disconnect, over and
            // over with enough screens. Let go of it instead, the way
            // HandleSponsorVideo does.
            try
            {
                context.Response.Abort();
            }
            catch
            {
                // Already gone.
            }
        }
        catch
        {
            // Before anything went out - the slide could not be read - so
            // there is still time to say so.
            context.Response.StatusCode = 500;
            WriteText(context, "Error");
        }
    }

    /// <summary>
    /// Sends a sponsor video, streamed from disk in chunks and with Range
    /// requests answered.
    ///
    /// Range matters: a &lt;video&gt; element in Safari will not play at all
    /// from a server that answers every request with the whole file, and
    /// Chrome asks for a range as soon as anything seeks. Only a single
    /// "bytes=from-to" range is handled, which is all a browser sends for
    /// ordinary playback - a multipart range gets the whole file, which is a
    /// legal answer to a range request as long as the status says 200.
    ///
    /// Nothing is held in memory: an 80 KB buffer walks the file, so twenty
    /// phones streaming the same ad cost twenty buffers rather than twenty
    /// copies.
    /// </summary>
    private void HandleSponsorVideo(HttpListenerContext context, string path)
    {
        const int ChunkBytes = 80 * 1024;

        // OPENED BEFORE ANYTHING IS SENT, on its own. A video removed or
        // locked after ResolvePath found it used to throw inside the catch
        // below, which said nothing and closed nothing - the phone hung
        // until its browser gave up. Now it is told there is no such file.
        FileStream file;

        try
        {
            file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch
        {
            context.Response.StatusCode = 404;
            WriteText(context, "Not found");
            return;
        }

        try
        {
            using var held = file;

            var length = file.Length;
            var from = 0L;
            var to = length - 1;

            // The browser tells the phone not to guess at a type (nosniff),
            // so this has to be right. From the extension, which AddVideo
            // vouched for against the file's own header before it was
            // stored - see SponsorMedia.
            context.Response.ContentType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp4" or ".m4v" => "video/mp4",
                ".mov" => "video/quicktime",
                ".webm" => "video/webm",
                ".mkv" => "video/x-matroska",
                ".avi" => "video/x-msvideo",
                ".wmv" => "video/x-ms-wmv",
                _ => "application/octet-stream",
            };

            context.Response.Headers["Accept-Ranges"] = "bytes";

            if (TryReadRange(context.Request.Headers["Range"], length, out var wantedFrom, out var wantedTo))
            {
                from = wantedFrom;
                to = wantedTo;

                context.Response.StatusCode = 206;
                context.Response.Headers["Content-Range"] = $"bytes {from}-{to}/{length}";
            }

            var sending = to - from + 1;
            context.Response.ContentLength64 = sending;
            file.Seek(from, SeekOrigin.Begin);

            var buffer = new byte[ChunkBytes];
            var left = sending;

            while (left > 0)
            {
                var read = file.Read(buffer, 0, (int)Math.Min(buffer.Length, left));

                if (read <= 0)
                {
                    // The file was truncated under us - stop rather than
                    // spin. The browser sees a short body and gives up on
                    // this slide, which is the same outcome as a 404.
                    break;
                }

                context.Response.OutputStream.Write(buffer, 0, read);
                left -= read;
            }

            context.Response.OutputStream.Close();
        }
        catch
        {
            // Nearly always the phone navigating away mid-stream, which
            // closes the connection under the write above. Nothing to say
            // and nowhere to say it - and setting a status now would throw
            // again, because the headers went out with the first chunk.
            // But the response is still let go of: aborted, so the
            // connection and the listener's hold on this request are
            // released rather than left open.
            try
            {
                context.Response.Abort();
            }
            catch
            {
                // Already gone.
            }
        }
    }

    /// <summary>
    /// A single "bytes=from-to" Range header, held to the file's length.
    /// False when there is no range, when it isn't one this understands, or
    /// when it asks for nothing inside the file - in each of which cases the
    /// caller sends the whole thing.
    /// </summary>
    internal static bool TryReadRange(string? header, long length, out long from, out long to)
    {
        from = 0;
        to = length - 1;

        if (length <= 0 || string.IsNullOrWhiteSpace(header)
            || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var span = header["bytes=".Length..].Trim();

        // A multipart range ("bytes=0-99,200-299") is answered with the
        // whole file instead. Browsers only send one for playback.
        if (span.Contains(','))
        {
            return false;
        }

        var dash = span.IndexOf('-');
        if (dash < 0)
        {
            return false;
        }

        var left = span[..dash].Trim();
        var right = span[(dash + 1)..].Trim();

        if (left.Length == 0)
        {
            // "bytes=-500": the LAST 500 bytes, not the first.
            if (!long.TryParse(right, out var fromEnd) || fromEnd <= 0)
            {
                return false;
            }

            from = Math.Max(0, length - fromEnd);
            to = length - 1;
            return true;
        }

        if (!long.TryParse(left, out from) || from < 0 || from >= length)
        {
            from = 0;
            return false;
        }

        if (right.Length == 0)
        {
            // "bytes=500-": from there to the end.
            to = length - 1;
            return true;
        }

        if (!long.TryParse(right, out to) || to < from)
        {
            from = 0;
            to = length - 1;
            return false;
        }

        // An end past the file is clamped rather than refused, which is what
        // the HTTP spec asks for.
        to = Math.Min(to, length - 1);
        return true;
    }

    /// <summary>
    /// The club logo for the pages' header bar. Only ever the logo file the
    /// app made (ClubLogo.PngBytes checks the stored name and that it really
    /// is a PNG), so a settings file naming some other file cannot turn this
    /// into a way to read it. Sent as image/png because nosniff means a
    /// browser takes the type at its word. Any query string (the pages add
    /// ?v= so a new logo is never mistaken for the old one) is ignored.
    /// </summary>
    private void HandleLogo(HttpListenerContext context)
    {
        var bytes = _settings is null ? null : ClubLogo.PngBytes(_settings.RelayLogoFile);

        if (bytes is null)
        {
            context.Response.StatusCode = 404;
            WriteText(context, "Not found");
            return;
        }

        context.Response.ContentType = "image/png";
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }

    /// <summary>
    /// /keepawake.mp4: the pit board's keep-awake video (22 Oct 2026, driver
    /// experience, item 10), with Range answered because Safari asks for one
    /// before it will play anything - see KeepAwake.Answer.
    /// </summary>
    private static void HandleKeepAwake(HttpListenerContext context)
    {
        var (status, contentRange, body) = KeepAwake.Answer(context.Request.Headers["Range"]);

        try
        {
            context.Response.StatusCode = status;
            context.Response.ContentType = KeepAwake.ContentType;
            context.Response.Headers["Accept-Ranges"] = "bytes";

            if (contentRange is not null)
            {
                context.Response.Headers["Content-Range"] = contentRange;
            }

            context.Response.ContentLength64 = body.Length;
            context.Response.OutputStream.Write(body, 0, body.Length);
            context.Response.OutputStream.Close();
        }
        catch
        {
            // The phone went away mid-answer - see HandleSponsorVideo.
            try
            {
                context.Response.Abort();
            }
            catch
            {
                // Already gone.
            }
        }
    }

    /// <summary>
    /// The name to show for a transponder, or - when nobody has named it -
    /// the raw id on the LAN pages and "Unnamed driver" on the relay copy.
    /// The LAN pages are only reachable from the venue's Wi-Fi, and the
    /// person reading them may be the one holding that transponder; the
    /// relay is the whole internet, where a number next to a car is a
    /// tracking key.
    /// </summary>
    private string DisplayNameFor(string transponder, bool publicCopy = false)
    {
        // Names, not the store: threaded this may be off the UI thread (24
        // Oct 2026, timing layer, P8 - see WebServerService.LiveInputs).
        var profile = Names.Get(transponder);

        if (!string.IsNullOrWhiteSpace(profile?.DriverName))
        {
            return profile!.DriverName;
        }

        return publicCopy ? RelayDocuments.UnnamedDriver : transponder;
    }

    /// <summary>Guards a build of the live-state document. See BuildStateJson.</summary>
    private readonly object _stateGate = new();

    /// <summary>
    /// The last state document built - on the UI thread, or threaded by
    /// MainForm.LiveState off it - for request threads to serve.
    ///
    /// Volatile, and replaced whole: a reader gets either the previous
    /// document or the new one, never half of either.
    /// </summary>
    private volatile string _publishedState = "{}";

    /// <summary>
    /// Rebuilds the document /api/state serves. Inline, UI thread only.
    /// Threaded (timing on its own thread), from MainForm.LiveState's
    /// worker off the UI as well - see the remarks.
    ///
    /// Requests used to build it themselves, on a thread-pool thread. That
    /// meant every phone in the building was walking the race session's
    /// standings several times a second while the lap pipeline was adding
    /// to them - and the standings are a plain dictionary, so
    /// "Collection was modified" was one lap away at any moment. What a
    /// spectator saw was the page going blank mid-race, which is the one
    /// time anybody is looking at it.
    ///
    /// Building on a timer instead costs a document nobody reads when the
    /// page is closed, and the page polls once a second anyway, so nothing
    /// on screen is any staler than it was.
    /// </summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P8): built on the UI thread, the
    /// document froze whenever the window did - and with timing on its own
    /// thread the race goes on regardless, so every phone and the relay
    /// showed a race that had stopped. Threaded, MainForm builds it from a
    /// thread-pool timer instead (MainForm.LiveState): the race is read from
    /// the timing thread, the profiles from their frozen view, and what only
    /// the window can read from what it last captured (CaptureUiSide and the
    /// frozen feeds). Built and published inside _stateGate (BuildAndPublish),
    /// so a forced publish from the window and the worker's never land out
    /// of order. Inline it is the same build on the UI thread as before.
    /// </remarks>
    public void PublishState(bool force = false)
    {
        if (!force && !IsRunning)
        {
            return;
        }

        try
        {
            BuildAndPublish();
        }
        catch (Exception ex)
        {
            // Kept, rather than thrown into a UI timer where it would take
            // the app down. The page goes on serving the last good copy.
            StatusMessage?.Invoke($"[web] Couldn't refresh the live state: {ex.Message}");
        }
    }

    private AchievementStore? _achievements;
    private DateTime _achievementsReadUtc = DateTime.MinValue;
    private (string Path, DateTime WriteUtc, long Length) _achievementsStamp;
    private bool _achievementsLoadFailed;
    private DateTime _achievementsLoadUtc;
    private int _achievementsFailedLoads;

    /// <summary>
    /// How long to wait before reading an unreadable achievements file
    /// again (22 Oct 2026, known issues): 15 seconds after the first
    /// failure, doubling to the old 5 minutes. A backup tool's lock is
    /// usually gone in seconds, and the pages showed no badges at all for
    /// the whole 5 minutes after one locked read.
    /// </summary>
    internal static TimeSpan AchievementRetryAfter(int failedLoads)
    {
        if (failedLoads <= 1)
        {
            return TimeSpan.FromSeconds(15);
        }

        var seconds = 15.0 * Math.Pow(2, Math.Min(failedLoads - 1, 10));
        return TimeSpan.FromSeconds(Math.Min(seconds, 300));
    }

    /// <summary>
    /// Earned badges, re-read from disk at most every few seconds.
    ///
    /// Loading them per request was tolerable when a request was the only
    /// thing asking. The document is built on a timer now, and reading a
    /// file from the UI thread twice a second is how a race night acquires
    /// a stutter. A badge awarded between reads appears within a few
    /// seconds, which is soon enough for a spectator page.
    /// </summary>
    private AchievementStore EarnedBadges()
    {
        if (_achievements is null || DateTime.UtcNow - _achievementsReadUtc > TimeSpan.FromSeconds(5))
        {
            // WHY (20 Oct 2026): this used to reload every 5 s regardless.
            // A damaged achievements.json does not throw - Load() keeps a
            // full copy of it aside and puts a storage warning on the
            // console, then hands back an empty store - so a truncated file
            // made a new "achievements.json.unreadable-*" copy and a new
            // console line every 5 s all night, on the UI thread. Now the
            // file is only re-read when it has actually changed (its path,
            // write time or length differ), and a load that failed is
            // retried on a short clock that backs off to 5 minutes (for a
            // file a backup tool had locked - AchievementRetryAfter). A healthy file is still picked up within 5 s of a write.
            var path = Path.Combine(AppPaths.Root, "achievements.json");
            (string Path, DateTime WriteUtc, long Length) stamp = (path, DateTime.MinValue, 0);

            try
            {
                var fi = new FileInfo(path);

                if (fi.Exists)
                {
                    stamp = (path, fi.LastWriteTimeUtc, fi.Length);
                }
            }
            catch
            {
                // Couldn't look at it: treat it as missing; Load() decides.
            }

            // The retry is timed from the last real load
            // (_achievementsLoadUtc), not from the last look at the file, so
            // the look itself still happens at most every 5 s even while a
            // load is failing.
            var retryFailed = _achievementsLoadFailed
                && DateTime.UtcNow - _achievementsLoadUtc > AchievementRetryAfter(_achievementsFailedLoads);

            if (_achievements is null || stamp != _achievementsStamp || retryFailed)
            {
                var loaded = AchievementStore.Load();

                // A LOCKED READ KEEPS THE LAST GOOD COPY (22 Oct 2026): the
                // badges did not go anywhere because a backup tool held the
                // file for a moment, so the pages go on showing them.
                _achievements = loaded.LoadedUnreadable && _achievements is { LoadedUnreadable: false } lastGood
                    ? lastGood
                    : loaded;
                _achievementsStamp = stamp;
                // Only a file that could not be read at all (LoadedUnreadable:
                // locked or refused) is retried on the backing-off clock. A file
                // that read but would not parse is left until it changes:
                // retrying it would only keep another copy of it every 5
                // minutes, and any rewrite moves the stamp anyway. Nor is a
                // healthy "[]" a failure (20 Oct 2026).
                _achievementsLoadFailed = loaded.LoadedUnreadable;
                _achievementsFailedLoads = _achievementsLoadFailed ? _achievementsFailedLoads + 1 : 0;
                _achievementsLoadUtc = DateTime.UtcNow;
            }

            _achievementsReadUtc = DateTime.UtcNow;
        }

        return _achievements;
    }

    /// <summary>
    /// The live race as one JSON document - the same one the results page
    /// and the OBS overlay are served.
    ///
    /// Serialised, because building it MUTATES state: KpiTracker.Observe
    /// records per-driver history as it goes. Requests already arrive on
    /// thread-pool threads, so two phones polling at once have always been
    /// able to interleave inside it; the relay publisher now calls this
    /// from the UI thread as well, which turns a race that was unlikely
    /// into one that happens every second. The lock is uncontended a
    /// handful of times a second and costs nothing measurable.
    ///
    /// It does NOT make reading the race session safe - that belongs to
    /// the UI thread and this has always read it from elsewhere. The fix
    /// for that is to build here once per tick and serve a published
    /// snapshot, the way the roster already works, which is a bigger
    /// change than this one.
    /// </summary>
    /// <param name="abbreviateNames">Shorten driver names to a first name and an initial. For the public relay copy only.</param>
    /// <param name="forRelay">
    /// The public relay copy. No transponder number leaves in it: an unnamed
    /// driver is "Unnamed driver" rather than their id, and the Transponder
    /// column is dropped whatever columns the club picked for the LAN page.
    /// </param>
    public string BuildStateJson(bool abbreviateNames = false, bool forRelay = false)
    {
        lock (_stateGate)
        {
            return BuildStateJsonCore(abbreviateNames, forRelay);
        }
    }

    private string BuildStateJsonCore(bool abbreviateNames, bool forRelay)
    {
        // WHY (24 Oct 2026, timing layer, P8): threaded this may run off the
        // UI thread (MainForm.LiveState). What only the window may read comes
        // from its last capture (ui - see WebServerService.LiveInputs), and
        // the race is read in one trip to the timing thread, from one moment
        // (view). Inline both are read here and now, on the UI thread, at
        // the same points as always: ui is today's reads, and view is null.
        var ui = CurrentUiSide();
        var view = _race.IsThreaded ? _race.ReadLiveView() : null;

        var ordered = view?.Ordered ?? _race.GetOrderedStandings();

        // Decided here because here is where the whole field is. Two
        // drivers whose names differ only in their last word would
        // otherwise be shortened, separately, into the same name. The
        // relay copy always gets a map, shortened or not, because the map
        // is also what keeps an unnamed driver's id out of the Driver
        // column.
        var shortened = abbreviateNames || forRelay ? ShortNames(ordered, abbreviateNames, forRelay) : null;

        // Session-wide fastest lap, so the page can highlight whoever holds it.
        //
        // Never a ghost's while there is a real car out - the rule the race's
        // own fastest lap follows (RaceControlSession.ProcessLapEvent). A
        // Quick Rival laps faster than its driver by design, and its lap
        // used to be highlighted here as the session's quickest, with every
        // real driver's Best Pace Gap measured from a lap nobody drove,
        // while the PA called a real driver's lap as the fastest. A field of
        // nothing but ghosts still shows its own best.
        var realCarsRacing = ordered.Any(s => !Ghosts.IsGhost(s.TransponderId));
        var bestLapMs = ordered.Where(s => s.FastestLapMs > 0 && !(realCarsRacing && Ghosts.IsGhost(s.TransponderId)))
            .Select(s => s.FastestLapMs)
            .DefaultIfEmpty(0)
            .Min();

        _kpiTracker.Observe(ordered, view?.Generation ?? _race.SessionGeneration);

        var columns = RelayColumns.Resolve(ui.WebKpis, ui.RelayKpis, forRelay);

        var context = new KpiContext
        {
            Ordered = ordered,
            Profiles = Names,
            Tracker = _kpiTracker,
            FastestOverall = bestLapMs,
            StreakThreshold = view?.StreakThreshold ?? _race.StreakThreshold,
            SectorLengthsFeet = view is null ? _race.SectorLengthsFeet : view.SectorLengthsFeet,

            // Re-read per request rather than held: the web server and Race
            // Control are separate objects, and a badge awarded between
            // requests has to show up without restarting the server.
            Achievements = EarnedBadges(),
            DisplayNames = shortened,
            PublicCopy = forRelay,
        };

        // Rows are emitted as parallel arrays of cell values matching the
        // column list, so the page can render whatever selection is active
        // without knowing anything about individual KPIs.
        var standings = ordered.Select((s, i) => new
        {
            cells = columns.Select(k =>
            {
                var value = k.Compute(s, i, context);
                return new { text = value.Text, emphasis = value.Emphasis.ToString().ToLowerInvariant() };
            }).ToList(),

            // The chart pages need the shape of a driver's race, not just
            // the current numbers. Sent alongside the cells rather than from
            // a second endpoint so a phone on club wifi makes one request a
            // second, not two.
            //
            // The start lap is dropped here rather than on the page: it runs
            // from the green to the line and isn't a lap time, and left in
            // it drags the first point of every line off the bottom of the
            // chart and flattens everything after it.
            // The same name the Driver column shows, from the same map.
            name = shortened is not null && shortened.TryGetValue(s.TransponderId, out var shortName)
                ? shortName
                : DisplayNameFor(s.TransponderId, forRelay),
            laps = RaceGaps.FlyingLaps(s).ToList(),
            best = s.FastestLapMs,
        }).ToList();

        var onDeck = ui.OnDeck;

        var document = new
        {
            state = (view?.State ?? _race.State).ToString(),
            flag = (view?.Flag ?? _race.Flag).ToString(),
            countdown = view is null ? _race.GetCountdownText() : view.Countdown,
            onDeck,
            columns = columns.Select(k => k.Label).ToList(),
            sponsorCount = ui.SponsorCount,
            sponsorLeaderboardSecs = ui.SponsorLeaderboardSecs,
            sponsorSlideSecs = ui.SponsorSlideSecs,
            sponsorPauseDuringRace = ui.SponsorPauseDuringRace,

            // Which slots are videos, so a page knows to use its <video>
            // element rather than its <img> - indexes into the same list
            // /api/sponsor?i= serves. Sent as indexes rather than a kind per
            // slide because almost every club's list is all pictures, and
            // then this is an empty array. Worked out in UiSideNow.
            sponsorVideos = ui.SponsorVideos,
            standings,
        };

        // The relay's copy keeps the format the relay knows, with two things
        // added to each row (29 Oct 2026, spoken laps on My Night):
        //  - "tag", the car's opaque row tag (RelayRowTag), so a driver's own
        //    night page can find their row without going by name. Never on a
        //    ghost, which is nobody's car;
        //  - "ahead", the gap to the car in front in the board's own words
        //    ("+0.41", "+1L"), so the gap can be read out even when the club
        //    doesn't show an Interval column. Anyone can already work it out
        //    from the board. Not on the leader.
        //  - "lap", how many times the car has crossed the line, the start lap
        //    included - its lap times, not Laps, which a penalty can move
        //    (3 Nov 2026, Asher: "add Lap 1"). "laps" leaves the start lap
        //    out - it runs from the green and isn't a lap time - so without
        //    this a phone can't tell a car that has done its first lap from
        //    one still on the grid, and never says "Lap 1".
        //  - "leader", the gap to P1 in the same words as "ahead" (3 Nov
        //    2026, Asher: "time to P1"), so a phone can say "four seconds
        //    off the lead" whatever columns the club shows. Not on the
        //    leader. The gap to the car behind needs nothing new: it is the
        //    next row's "ahead".
        if (forRelay)
        {
            var relayNode = JsonSerializer.SerializeToNode(document)!.AsObject();

            if (relayNode["standings"] is System.Text.Json.Nodes.JsonArray rows)
            {
                for (var i = 0; i < ordered.Count && i < rows.Count; i++)
                {
                    if (rows[i] is not System.Text.Json.Nodes.JsonObject row)
                    {
                        continue;
                    }

                    var car = ordered[i];

                    if (!Ghosts.IsGhost(car.TransponderId) && RelayRowTag(car.TransponderId) is { } tag)
                    {
                        row["tag"] = tag;
                    }

                    row["lap"] = car.LapTimesMs.Count;

                    if (i > 0 && RaceGaps.Format(RaceGaps.Between(car, ordered[i - 1])) is var ahead && ahead != "-")
                    {
                        row["ahead"] = ahead;
                    }

                    if (i > 0 && RaceGaps.Format(RaceGaps.Between(car, ordered[0])) is var leader && leader != "-")
                    {
                        row["leader"] = leader;
                    }
                }
            }

            // WHAT THE * MEANS (7 Nov 2026, known issues): the Laps column
            // reads "12*" for a count with laps race control counted by hand,
            // and the relay draws its own pages, so it is told there is a
            // mark to explain and writes the legend itself. Only when the
            // column is on the board and somebody's count carries the mark.
            if (columns.Any(k => k.Key == "laps") && ordered.Any(s => HandCountedLaps.During(s) > 0))
            {
                relayNode["handCounted"] = true;
            }

            // A timed race's length in seconds (5 Nov 2026, Asher: "on flag
            // card on relay make a progress bar for a timed race as well as
            // the lap number race"). The countdown says only the time left
            // ("07:30 REMAINING"); with the length too the flag card can fill
            // a bar from it, as it does from "5 LAPS TO GO". Not for a lap
            // race, whose countdown is laps and wins (GetCountdownText).
            if (_race.LapLimit is null && _race.TimeLimit is { } timeLimit && timeLimit > TimeSpan.Zero)
            {
                relayNode["raceSecs"] = (int)Math.Round(timeLimit.TotalSeconds);
            }

            // The live track map on the relay's Live tab (4 Nov 2026, Asher:
            // "the live track map should be on the live tab"). Guarded: the
            // live state must not go stale for the sake of a picture.
            try
            {
                AddRelayTrackMap(relayNode);
            }
            catch
            {
                relayNode["map"] = null;
            }

            return relayNode.ToJsonString();
        }

        // The LAN copy also says which race Race Control has in hand, for
        // the phone remote. Guarded: the live state must not go stale for
        // the sake of a heading.
        string race;

        try
        {
            race = RaceInHand?.Invoke() ?? string.Empty;
        }
        catch
        {
            race = string.Empty;
        }

        var node = JsonSerializer.SerializeToNode(document)!.AsObject();
        node["race"] = race;

        // 22 Oct 2026 (driver experience, item 3): the laptop's own clock,
        // so a phone works out "about 7 min" and "seen 4 s ago" against the
        // time the ETAs and sightings were measured by, not its own clock,
        // which may be minutes out.
        node["now"] = IsoUtc(DateTime.UtcNow);

        // 22 Oct 2026 (driver experience, item 9): the last few lines the
        // announcer said, for the captions strip (?captions=1). Left out
        // when nothing has been said. Guarded: the board must not go stale
        // for the sake of a caption.
        try
        {
            var said = Captions?.Invoke() ?? Array.Empty<CaptionLine>();
            var last = said.Skip(Math.Max(0, said.Count - CaptionsInState)).ToList();

            if (last.Count > 0)
            {
                node["captions"] = new System.Text.Json.Nodes.JsonArray(last
                    .Select(l => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
                    {
                        ["n"] = l.N,
                        ["t"] = l.Text,
                    })
                    .ToArray());
            }
        }
        catch
        {
            // The board without the captions, rather than no board.
        }

        // 22 Oct 2026: the key to the * a lap count carries when race
        // control counted some of it by hand (HandCountedLaps). LAN copy
        // only - the relay's document keeps the format the relay knows.
        if (ordered.Any(s => HandCountedLaps.During(s) > 0))
        {
            node["byHand"] = HandCountedLaps.Legend;
        }

        // The key the ids below are made with, read once for the whole
        // document. The board, the map and the follow list all have to call
        // one driver by one id or the page cannot match them up, and a Start
        // between two reads of the key would key them differently.
        var rosterKey = _rosterKey;

        // Following a driver (PhoneFollow): each row's opaque id, so the
        // page can pick out the one being followed; their card - position,
        // laps, last lap, the gaps either side; when each driver's next race
        // is; and who can be followed. Guarded like the heading above: the
        // board must not go stale for the sake of the extras.
        try
        {
            AddFollowing(node, ordered, rosterKey);
        }
        catch
        {
            // The board without the follow view, rather than no board.
        }

        try
        {
            AddTrackMap(node, rosterKey);
        }
        catch
        {
            // The board without the track map, rather than no board.
        }

        try
        {
            AddIfItEndedNow(node);
        }
        catch
        {
            // The board without the championship, rather than no board.
        }

        try
        {
            AddBroadcast(node, ordered, rosterKey, ui, view);
        }
        catch
        {
            // The board without the broadcast extras, rather than no board.
        }

        return node.ToJsonString();
    }

    /// <summary>
    /// Adds the live track map to the LAN copy of the live state: the racing
    /// line (in tiles - the page fits it to the screen), where the split
    /// loops are, and each car's place on it with its pace, so the page can
    /// move the dots smoothly between its once-a-second polls. Null when the
    /// layout being raced on has no plan to draw. See TrackPositions.
    ///
    /// Each car is named by the roster's opaque id rather than by its
    /// transponder, for the reason AddFollowing gives.
    /// </summary>
    private void AddTrackMap(System.Text.Json.Nodes.JsonObject node, byte[] rosterKey)
    {
        var (line, cars) = TrackMap?.Invoke() ?? (null, Array.Empty<TrackPositions.Car>());

        if (line is null)
        {
            node["map"] = null;
            return;
        }

        static double Round(float value) => Math.Round(value, 3);

        var points = new System.Text.Json.Nodes.JsonArray(line.Points
            .Select(p => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonArray(Round(p.X), Round(p.Y)))
            .ToArray());

        var splits = new System.Text.Json.Nodes.JsonArray(line.SplitAt
            .Select(f => (System.Text.Json.Nodes.JsonNode?)Round(f))
            .ToArray());

        var placed = new System.Text.Json.Nodes.JsonArray(cars
            .Select(c => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = RosterId(rosterKey, c.Transponder),
                ["pos"] = c.Position,
                ["f"] = Round(c.Fraction),
                ["rate"] = Math.Round(c.Rate, 5),
                ["cap"] = Round(c.Cap),
                ["stop"] = c.Stopped,
                ["where"] = c.Where,

                // The best-lap ghost: where it would be on its best lap, and
                // how fast that ghost goes round. -1 for none.
                ["g"] = Round(c.Ghost),
                ["gr"] = Math.Round(c.GhostRate, 5),
            })
            .ToArray());

        node["map"] = new System.Text.Json.Nodes.JsonObject
        {
            ["pts"] = points,
            ["splits"] = splits,
            ["cars"] = placed,
        };
    }

    /// <summary>
    /// The most points of the track's line the relay copy carries. The
    /// frame goes up once a second while a race is on, so a plan drawn with
    /// thousands of points is thinned to this many, evenly along the line,
    /// keeping its first and last (the finish line, start and end).
    /// </summary>
    internal const int RelayMapPoints = 240;

    /// <summary>
    /// The live track map for the relay copy of the state (4 Nov 2026): the
    /// same line, split loops and cars as the Wi-Fi copy's "map"
    /// (<see cref="AddTrackMap"/>), with three differences that the public
    /// internet asks for.
    ///
    /// EACH CAR BY ITS ROW'S TAG, never its transponder or the roster's id -
    /// the same opaque <see cref="RelayRowTag"/> the board row carries, so the
    /// page names a dot from its row and nothing new about a car goes out.
    /// A car with no tag is left off the map, as it has none on its row.
    ///
    /// NO GHOSTS: a ghost has no row on the relay's board either.
    ///
    /// A THINNER LINE: see <see cref="RelayMapPoints"/>.
    /// </summary>
    private void AddRelayTrackMap(System.Text.Json.Nodes.JsonObject node)
    {
        var (line, cars) = TrackMap?.Invoke() ?? (null, Array.Empty<TrackPositions.Car>());

        if (line is null)
        {
            node["map"] = null;
            return;
        }

        static double Round(float value) => Math.Round(value, 3);

        var source = line.Points;
        var points = new System.Text.Json.Nodes.JsonArray();

        foreach (var p in ThinnedLine(source.Count, RelayMapPoints).Select(i => source[i]))
        {
            points.Add(new System.Text.Json.Nodes.JsonArray(Round(p.X), Round(p.Y)));
        }

        var splits = new System.Text.Json.Nodes.JsonArray(line.SplitAt
            .Select(f => (System.Text.Json.Nodes.JsonNode?)Round(f))
            .ToArray());

        var placed = new System.Text.Json.Nodes.JsonArray();

        foreach (var c in cars)
        {
            if (Ghosts.IsGhost(c.Transponder) || RelayRowTag(c.Transponder) is not { } tag)
            {
                continue;
            }

            placed.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["tag"] = tag,
                ["pos"] = c.Position,
                ["f"] = Round(c.Fraction),
                ["rate"] = Math.Round(c.Rate, 5),
                ["cap"] = Round(c.Cap),
                ["stop"] = c.Stopped,
            });
        }

        node["map"] = new System.Text.Json.Nodes.JsonObject
        {
            ["pts"] = points,
            ["splits"] = splits,
            ["cars"] = placed,
        };
    }

    /// <summary>
    /// Which of <paramref name="count"/> points to keep so no more than
    /// <paramref name="most"/> remain: evenly spaced indexes, the first and
    /// the last always among them. Every index when there are few enough.
    /// </summary>
    internal static IReadOnlyList<int> ThinnedLine(int count, int most)
    {
        if (count <= 0)
        {
            return Array.Empty<int>();
        }

        if (count <= most || most < 2)
        {
            return Enumerable.Range(0, count).ToList();
        }

        var kept = new List<int>(most);

        for (var k = 0; k < most; k++)
        {
            var index = (int)Math.Round(k * (count - 1) / (double)(most - 1));

            if (kept.Count == 0 || kept[^1] != index)
            {
                kept.Add(index);
            }
        }

        return kept;
    }

    /// <summary>
    /// Adds the championship as it would stand if the race ended now: its
    /// name, the headline, and the top of the table with each move. Null
    /// when no race that counts towards one is on.
    /// </summary>
    private void AddIfItEndedNow(System.Text.Json.Nodes.JsonObject node)
    {
        if (IfItEndedNow?.Invoke() is not { } table)
        {
            node["champ"] = null;
            return;
        }

        node["champ"] = new System.Text.Json.Nodes.JsonObject
        {
            ["season"] = table.SeasonName,
            ["headline"] = table.Headline,
            ["rows"] = new System.Text.Json.Nodes.JsonArray(table.Rows
                .Select(r => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
                {
                    ["pos"] = r.Position,
                    ["name"] = r.DriverName,
                    ["pts"] = r.Points,
                    ["move"] = ChampionshipLive.Arrow(r).Trim(),
                    ["racing"] = r.InThisRace,
                })
                .ToArray()),
        };
    }

    /// <summary>
    /// Adds what a phone following one driver needs to the LAN copy of the
    /// live state: an id on each row, a card per car on the board, the
    /// call-ups for the races still to run, and everyone who can be
    /// followed, by name. See PhoneFollow.
    ///
    /// Every id here is the roster's own opaque id (RosterId), not the
    /// driver's transponder. It was the transponder until 9 Oct 2026, and
    /// because /api/state is served to any phone on the club Wi-Fi with no
    /// PIN and no gate, the people list below handed out a transponder next
    /// to a full name, for everyone on the board and everyone gridded in
    /// every race still to come - the exact mapping /api/roster spends the
    /// check-in PIN and these same ids to keep off the wire (see RosterView
    /// for why that mapping matters). The pages only ever match one id in a
    /// document against another in the same document, so which id it is
    /// makes no difference to them.
    /// </summary>
    private void AddFollowing(System.Text.Json.Nodes.JsonObject node, IReadOnlyList<RacerStanding> ordered, byte[] rosterKey)
    {
        if (node["standings"] is System.Text.Json.Nodes.JsonArray rows)
        {
            for (var i = 0; i < rows.Count && i < ordered.Count; i++)
            {
                if (rows[i] is System.Text.Json.Nodes.JsonObject row)
                {
                    row["id"] = RosterId(rosterKey, ordered[i].TransponderId);

                    // Said outright, because the broadcast page used to tell
                    // a ghost from a real car by the GHOST- on its id and an
                    // opaque id has no prefix to read. Without it a Quick
                    // Rival's lap takes the fastest-lap graphic on the
                    // stream, which is the one thing a ghost's lap must
                    // never do. Only on a ghost's row: nearly every row is
                    // a real car.
                    if (Ghosts.IsGhost(ordered[i].TransponderId))
                    {
                        row["ghost"] = true;
                    }
                }
            }
        }

        var cards = new System.Text.Json.Nodes.JsonObject();

        // Each car's best lap too, for the lap times spoken in a follower's
        // earbuds ("best lap") and the broadcast graphics.
        var bestOf = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // And each car's standing, for the warnings and penalty time race
        // control has given it this race (22 Oct 2026, driver experience,
        // item 1). Read here, on the UI thread, like everything else in it.
        var standingOf = new Dictionary<string, RacerStanding>(StringComparer.OrdinalIgnoreCase);

        foreach (var standing in ordered)
        {
            bestOf.TryAdd(standing.TransponderId, standing.FastestLapMs);
            standingOf.TryAdd(standing.TransponderId, standing);
        }

        foreach (var (id, card) in PhoneFollow.Cards(ordered))
        {
            // Under the opaque id, like the rows. bestOf stays keyed by
            // transponder: it is read here and never leaves this method.
            cards[RosterId(rosterKey, id)] = new System.Text.Json.Nodes.JsonObject
            {
                ["pos"] = card.Position,
                ["laps"] = card.Laps,
                ["last"] = card.LastLapMs,
                ["best"] = bestOf.GetValueOrDefault(id),
                ["ahead"] = card.Ahead,
                ["behind"] = card.Behind,
            };

            // The target lap race control set for them, for the phone coach
            // page (22 Oct 2026) - only when there is one. See TargetLapMsFor.
            if (TargetLapMsFor(id) is > 0 and var target)
            {
                cards[RosterId(rosterKey, id)]!["target"] = target;
            }

            // This race's warnings and penalty time, under the call-up on
            // the follow card (22 Oct 2026, driver experience, item 1) -
            // only when there are any, so a clean card is as it was. The
            // seconds to a tenth, so a 2.5 s penalty does not read as 3.
            if (standingOf.TryGetValue(id, out var raced))
            {
                if (raced.Warnings.Count > 0)
                {
                    cards[RosterId(rosterKey, id)]!["warn"] = raced.Warnings.Count;
                }

                if (raced.PenaltyMs > 0)
                {
                    cards[RosterId(rosterKey, id)]!["penS"] = Math.Round(raced.PenaltyMs / 1000.0, 1);
                }
            }
        }

        node["cards"] = cards;

        IReadOnlyList<UpcomingRace> upcoming;

        try
        {
            upcoming = UpcomingRaces?.Invoke() ?? Array.Empty<UpcomingRace>();
        }
        catch
        {
            upcoming = Array.Empty<UpcomingRace>();
        }

        var callUps = new System.Text.Json.Nodes.JsonObject();

        foreach (var (id, callUp) in PhoneFollow.CallUps(upcoming))
        {
            var call = new System.Text.Json.Nodes.JsonObject
            {
                ["race"] = callUp.Race,
                ["away"] = callUp.Away,
                ["slot"] = callUp.Slot,
                ["now"] = callUp.OnTrack,
                ["words"] = PhoneFollow.Words(callUp),
            };

            // 22 Oct 2026 (driver experience, item 3): staged - go to the
            // driver stand - and when the race is expected to start, for
            // "about 7 min (19:42)". Only when they apply, so a call-up
            // without them is as it was.
            if (callUp.Staged)
            {
                call["staged"] = true;
            }

            if (callUp.EtaUtc is { } eta)
            {
                call["eta"] = IsoUtc(eta);
            }

            callUps[RosterId(rosterKey, id)] = call;
        }

        node["callups"] = callUps;

        // Everyone on the board or due in a race to come, by name - the
        // page's "Follow" list.
        var followable = ordered.Select(s => s.TransponderId)
            .Concat(upcoming.SelectMany(r => r.Grid))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var people = followable
            .Select(id => (Id: RosterId(rosterKey, id), Name: DisplayNameFor(id, false)))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject { ["id"] = p.Id, ["name"] = p.Name })
            .ToArray();

        node["people"] = new System.Text.Json.Nodes.JsonArray(people);

        // 22 Oct 2026 (driver experience): the follow card's extras, for the
        // people above only - nobody can follow anyone else, so nobody else
        // needs them. Each under the same opaque id, and each left out when
        // there is nothing to say.
        // Each guarded on its own: a sighting that cannot be read must not
        // take the follow list it is added to down with it.
        try
        {
            AddSightings(node, followable, rosterKey);
        }
        catch
        {
            // The follow view without the transponder check.
        }

        try
        {
            AddPenalties(node, followable, rosterKey);
        }
        catch
        {
            // The follow view without tonight's penalties.
        }
    }

    /// <summary>
    /// "seen": when each followable driver's transponder was last heard -
    /// seconds ago, by the laptop's clock - and how well (22 Oct 2026,
    /// driver experience, item 4). The "Check my transponder" button on the
    /// follow card watches for a sighting newer than the press. Nothing
    /// new is recorded for it: every pass is already (TransponderWatch).
    /// Ghosts are never heard, so never here.
    /// </summary>
    private void AddSightings(System.Text.Json.Nodes.JsonObject node, IReadOnlyList<string> followable, byte[] rosterKey)
    {
        if (SightingOf is not { } sightingOf)
        {
            return;
        }

        var seen = new System.Text.Json.Nodes.JsonObject();
        var now = DateTime.UtcNow;

        foreach (var transponder in followable)
        {
            if (Ghosts.IsGhost(transponder) || sightingOf(transponder) is not { } sighting)
            {
                continue;
            }

            seen[RosterId(rosterKey, transponder)] = new System.Text.Json.Nodes.JsonObject
            {
                ["ago"] = Math.Max(0, (int)(now - sighting.SeenUtc).TotalSeconds),
                ["sig"] = sighting.Signal is "good" or "weak" ? sighting.Signal : "unknown",
            };
        }

        if (seen.Count > 0)
        {
            node["seen"] = seen;
        }
    }

    /// <summary>
    /// "pens": each followable driver's penalty lines from tonight's filed
    /// races - "Q1 Heat 2: +5 s - jump start" - as PublishPenalties last
    /// had them (22 Oct 2026, driver experience, item 1).
    /// </summary>
    private void AddPenalties(System.Text.Json.Nodes.JsonObject node, IReadOnlyList<string> followable, byte[] rosterKey)
    {
        var penalties = _penalties;

        if (penalties.Count == 0)
        {
            return;
        }

        var pens = new System.Text.Json.Nodes.JsonObject();

        foreach (var transponder in followable)
        {
            if (penalties.TryGetValue(transponder.Trim(), out var lines) && lines.Count > 0)
            {
                pens[RosterId(rosterKey, transponder)] = new System.Text.Json.Nodes.JsonArray(lines
                    .Select(l => (System.Text.Json.Nodes.JsonNode?)l)
                    .ToArray());
            }
        }

        if (pens.Count > 0)
        {
            node["pens"] = pens;
        }
    }

    /// <summary>
    /// Shortened names for everyone on track, keyed by transponder.
    ///
    /// Keyed by transponder rather than by name because two drivers can
    /// share a name; a transponder is the one thing that cannot collide.
    /// </summary>
    private IReadOnlyDictionary<string, string> ShortNames(IReadOnlyList<RacerStanding> ordered, bool abbreviate, bool publicCopy)
    {
        var full = ordered.Select(s => DisplayNameFor(s.TransponderId, publicCopy)).ToList();

        // Initials-only is for the copy bound for the relay, matched by
        // transponder too; the Wi-Fi pages are in the room and keep the
        // names everyone there hears on the PA (design 1.4.7; 22 Oct 2026,
        // driver experience).
        var chosen = publicCopy
            ? RelayPublisher.DisplayNames(full, abbreviate, ordered.Select(s => (string?)s.TransponderId).ToList())
            : RelayPublisher.DisplayNamesForTheRoom(full, abbreviate);

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < ordered.Count; i++)
        {
            // The placeholder for a car with no name is not a name, and
            // shortened to "Unnamed d" it reads like one.
            map[ordered[i].TransponderId] = full[i] == RelayDocuments.UnnamedDriver ? full[i] : chosen[i];
        }

        return map;
    }

    private static void WriteHtml(HttpListenerContext context, string html)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        WriteBody(context, html);
    }

    private static void WriteJson(HttpListenerContext context, string json)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        WriteBody(context, json);
    }

    private static void WriteText(HttpListenerContext context, string text)
    {
        context.Response.ContentType = "text/plain; charset=utf-8";
        WriteBody(context, text);
    }

    private static void WriteBody(HttpListenerContext context, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        context.Response.ContentLength64 = bytes.Length;
        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
        context.Response.OutputStream.Close();
    }

    // --- Pages -------------------------------------------------------------
    // Kept as self-contained strings with inline CSS/JS rather than separate
    // asset files, so there's nothing extra to copy alongside the .exe and
    // no chance of the app running without its own web assets present.

    /// <summary>
    /// What a page takes from the crowd-screen look (Themes → Looks), worked
    /// out once per request so a change shows on the next page load:
    /// Scheme is "light" or "dark" (the html class and color-scheme), Root
    /// the :root block of colour variables every page's CSS is written
    /// against, ThemeColor the phone's address-bar colour, and Logo the
    /// header's logo image, or empty. Fingerprint is twelve hex digits
    /// standing for all of that together: a page carries the one it was
    /// built with, and /api/look answers with the current one, so a page
    /// nobody reloads (an OBS source, a TV) can tell it is out of date.
    /// </summary>
    private sealed record PageLook(string Scheme, string Root, string ThemeColor, string Logo)
    {
        public string Fingerprint { get; } = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(Scheme + "|" + Root + "|" + ThemeColor + "|" + Logo)))
            [..12].ToLowerInvariant();
    }

    /// <summary>
    /// Keeps a page on the current crowd-screen look without anyone touching
    /// it. The pages are built with the look baked in, which is right for a
    /// phone that reloads all the time and wrong for an OBS browser source or
    /// a TV left on Live Results all day: a new look or logo never reached
    /// them. Every five seconds this asks /api/look for the fingerprint the
    /// page would be built with now, and reloads when it differs from the one
    /// on the body tag (the address, ?overlay=1 included, is kept).
    ///
    /// Not while somebody is using the page: a focused box, or whatever the
    /// page's own lookBusy() says (the check-in page: a name half-searched,
    /// the camera open) holds the reload until it is idle again. A failed or
    /// odd answer is ignored rather than acted on, so the laptop being away
    /// can never set a page reloading over and over.
    /// </summary>
    private const string LookWatchScript = """
        (function(){
          var built = document.body.getAttribute('data-look') || '';
          var stale = false;
          function busy(){
            var a = document.activeElement;
            if (a && /^(INPUT|TEXTAREA|SELECT)$/.test(a.tagName)) { return true; }
            try { if (typeof lookBusy === 'function' && lookBusy()) { return true; } } catch(e) {}
            return false;
          }
          async function check(){
            if (!stale) {
              try {
                var r = await fetch('/api/look', { cache: 'no-store' });
                if (!r.ok) { return; }
                var d = await r.json();
                if (!d || typeof d.v !== 'string' || !/^[0-9a-f]{12}$/.test(d.v) || d.v === built) { return; }
                stale = true;
              } catch(e) { return; }
            }
            if (!busy()) { location.reload(); }
          }
          if (/^[0-9a-f]{12}$/.test(built)) { setInterval(check, 5000); }
        })();
        """;

    /// <summary>
    /// Builds the PageLook for this request. Runs on a request thread:
    /// Looks.ForDisplays only reads (the custom-look list is swapped whole,
    /// never changed in place) and ClubLogo's answers are cached under a
    /// lock. Every value that goes into the page is either one of the look's
    /// colours - always #RRGGBB, see ResolvedLook.Hex - an rgba() made here
    /// from numbers, or a logo name that passed ClubLogo.IsLogoName, so
    /// nothing a settings file says can break out of the CSS or the markup.
    /// </summary>
    private PageLook CurrentPageLook()
    {
        var look = _settings is null ? AppTheme.Active : Looks.ForDisplays(_settings);
        var hex = look.Hex;
        var light = look.Light;

        var vars = new StringBuilder(":root{");

        void Var(string name, string value) =>
            vars.Append("--").Append(name).Append(':').Append(value).Append(';');

        // The same eleven, under the same names, as the online pages - so a
        // stylesheet rule reads the same in either place.
        Var("bg", hex.Bg);
        Var("surface", hex.Surface);
        Var("row-alt", hex.RowAlt);
        Var("primary", hex.Primary);
        Var("on-primary", hex.OnPrimary);
        Var("accent", hex.Accent);
        Var("text", hex.Text);
        Var("muted", hex.Muted);
        Var("border", hex.Border);
        Var("good", hex.Good);
        Var("bad", hex.Bad);

        // Worked out here rather than with color-mix() in the page: an
        // older phone browser that doesn't know color-mix() would drop the
        // whole rule and leave the colour missing.
        //   hair   - the line between table rows, a shade quieter than a border
        //   raised - a chip, an idle dot, the edge of a box you type in
        //   soft   - chart names and legends: between hint and text
        Var("hair", Looks.ToHex(Looks.Mix(look.Border, look.Background, 0.3)));
        Var("raised", Looks.ToHex(Looks.Mix(look.Surface, look.Text, light ? 0.12 : 0.14)));
        Var("soft", Looks.ToHex(Looks.Mix(look.Muted, look.Text, 0.45)));

        // The leader's row, and a driver who is checked in: a wash of the
        // primary colour, a little stronger on a light page where a faint
        // tint vanishes into white.
        Var("p1-tint", Rgba(look.Primary, light ? "0.09" : "0.06"));
        Var("on-tint", Rgba(look.Primary, light ? "0.13" : "0.10"));
        Var("on-edge", Looks.ToHex(Looks.Mix(look.Surface, look.Primary, 0.45)));

        // OBS overlay: the look's own background, see-through, so the text
        // colours still have what they were chosen against behind them.
        Var("glass-bg", Rgba(look.Background, "0.86"));
        Var("glass-cell", Rgba(look.Background, "0.78"));
        Var("glass-p1", Rgba(Looks.Mix(look.Background, look.Primary, 0.12), "0.82"));
        Var("glass-surface", Rgba(look.Surface, "0.86"));

        // HeatCaller's sun in the footer: the look's primary, or on Synthwave
        // (Classic's look) the yellow-to-orange sunset - see HeatCallerMark.IsSunset.
        Var("hcsun", HeatCallerMark.IsSunset(look.Primary) ? HeatCallerMark.SunsetFill : "currentColor");

        var logo = string.Empty;

        // Read once: the setting can be changed on the UI thread while this
        // page is being built.
        var file = _settings?.RelayLogoFile;

        if (_settings is not null && _settings.ShowLogoOnScreens && ClubLogo.PngBytes(file) is not null)
        {
            // The header bar is the page background, so that is what the
            // logo has to stand out from.
            var plate = ClubLogo.PlateFor(file, look.Background);

            if (plate is Color plateColor)
            {
                Var("plate", Looks.ToHex(plateColor));
            }

            // ?v= the file's name: each import gets a new one, so a phone
            // can never show last season's logo from its own cache. alt is
            // empty because the page title follows it.
            logo = "<img class=\"logo" + (plate is null ? string.Empty : " plate")
                   + "\" src=\"/logo.png?v=" + WebUtility.UrlEncode(file) + "\" alt=\"\">";
        }

        var scheme = light ? "light" : "dark";
        vars.Append("color-scheme:").Append(scheme).Append(";}");

        return new PageLook(scheme, vars.ToString(), hex.Bg, logo);
    }

    /// <summary>rgba() text from a colour and an alpha written the CSS way, so no culture's decimal comma can creep in.</summary>
    private static string Rgba(Color color, string alpha) =>
        "rgba(" + color.R + "," + color.G + "," + color.B + "," + alpha + ")";

    // Every colour here is a variable from the page's :root block (see
    // CurrentPageLook), so the look decides them all.
    private const string SharedStyle = """
        * { box-sizing: border-box; }
        :root { --cond: 'Bahnschrift','Segoe UI Variable Display','Roboto Condensed','Helvetica Neue',system-ui,sans-serif; }
        body { margin:0; font-family: 'Segoe UI', system-ui, sans-serif; background:var(--bg); color:var(--text); }
        h1 { margin:0; font-family:var(--cond); font-stretch:condensed; font-size:22px;
             font-weight:700; letter-spacing:.14em; display:flex; align-items:center; gap:12px; }
        /* The club logo, left of the page title. Sized for a phone first,
           then bigger on a laptop, TV or stream where it is seen from further
           away. Square because the logo file is square. */
        .logo { width:40px; height:40px; flex:0 0 auto; object-fit:contain; border-radius:8px; }
        .logo.plate { background:var(--plate); padding:3px; }
        @media (min-width:700px) { .logo { width:56px; height:56px; border-radius:10px; } }
        @media (min-width:1100px) { .logo { width:72px; height:72px; border-radius:12px; } }
        .bar { background:var(--bg); padding:15px 18px; display:flex; justify-content:space-between;
               align-items:center; border-bottom:1px solid var(--border); }
        .state { font-family:var(--cond); font-stretch:condensed; font-weight:700; color:var(--primary);
                 font-size:17px; letter-spacing:.1em; }
        .countdown { font-family:var(--cond); font-stretch:condensed; font-size:30px; color:var(--text);
                     font-weight:700; line-height:1; }
        table { width:100%; border-collapse:collapse; }
        th { background:var(--bg); color:var(--muted); text-align:left; padding:9px 12px;
             font-size:10px; font-weight:700; letter-spacing:.16em; text-transform:uppercase;
             border-bottom:1px solid var(--border); }
        td { padding:13px 12px; border-bottom:1px solid var(--hair); font-size:21px;
             font-family:var(--cond); font-stretch:condensed; font-weight:600; }
        .pos { width:56px; font-weight:700; color:var(--muted); font-size:25px; }
        tr.p1 td { background:var(--p1-tint); }
        tr.p1 .pos { color:var(--primary); }
        .empty { padding:40px; text-align:center; color:var(--muted); }
        .handnote { padding:6px 12px; color:var(--muted); font-size:.85em; }

        /* --- Swipe deck ---
           One horizontal scroller with snap points rather than a JS carousel:
           the browser then owns the physics, so the flick feels native and
           keeps working if the script stalls mid-race. */
        .deck { display:flex; overflow-x:auto; scroll-snap-type:x mandatory;
                -webkit-overflow-scrolling:touch; scrollbar-width:none; }
        .deck::-webkit-scrollbar { display:none; }
        .page { flex:0 0 100%; scroll-snap-align:start; min-width:100%; }
        .dots { display:flex; gap:8px; justify-content:center; padding:10px 0 14px;
                background:var(--bg); position:sticky; bottom:0; }
        .dot { width:8px; height:8px; border-radius:50%; background:var(--raised); transition:background .2s; }
        .dot.on { background:var(--primary); }
        .dots { border-top:1px solid var(--border); }
        .ptitle { padding:14px 18px 6px; font-size:10px; font-weight:700; letter-spacing:.16em;
                  color:var(--muted); text-transform:uppercase; }
        canvas { width:100%; display:block; }
        .legend { display:flex; flex-wrap:wrap; gap:10px; padding:8px 18px 16px; font-size:13px; }
        .legend span { display:flex; align-items:center; gap:5px; color:var(--soft); }
        .swatch { width:12px; height:3px; border-radius:2px; }
        """
        // HeatCaller's sign-off at the foot of every page (31 Oct 2026),
        // the relay pages' footer mark - see HeatCallerMark.PageFooterHtml.
        + HeatCallerMark.PageFooterStyle;

    // Internal rather than private so the tests can read its script (BugHunt4WebTests).
    internal string BuildLiveResultsPage()
    {
        var look = CurrentPageLook();

        return $$"""
        <!DOCTYPE html><html class="{{look.Scheme}}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="{{look.Scheme}}"><meta name="theme-color" content="{{look.ThemeColor}}">
        <title>Live Results</title><style>{{look.Root}}{{SharedStyle}}
        /* Highlight is the accent, as on the Big Screen and the online
           pages; good is the look's own green. */
        .good { color:var(--good); font-weight:700; }
        .highlight { color:var(--accent); font-weight:700; }
        .bad { color:var(--bad); font-weight:700; }
        .pen { color:var(--bad); font-size:12px; margin-left:6px; }
        .ondeck { background:var(--surface); color:var(--accent); padding:11px 18px; font-weight:700;
                  border-bottom:1px solid var(--border); font-size:15px; }
        #sponsor { display:none; position:fixed; inset:0; background:var(--bg); z-index:99;
                   align-items:center; justify-content:center; }
        #sponsor img, #sponsor video { max-width:92%; max-height:92%; object-fit:contain; }
        /* Overlay mode for OBS: transparent background and no chrome, so it
           composites cleanly over a video feed instead of covering it. */
        body.overlay { background:transparent; }
        body.overlay .bar { background:var(--glass-bg); }
        body.overlay td { background:var(--glass-cell) !important; }
        body.overlay tr.p1 td { background:var(--glass-p1) !important; }
        body.overlay th { background:var(--glass-bg); }
        body.overlay .ondeck { background:var(--glass-surface); }
        body.overlay #sponsor { display:none !important; }
        /* Following one driver: a picker, their card, and the call-up for
           their next race - which flashes when it is next. */
        .follow { background:var(--surface); border-bottom:1px solid var(--border); padding:10px 18px; }
        .follow select { font:inherit; font-size:15px; padding:6px 8px; border-radius:8px; max-width:100%;
                         background:var(--bg); color:var(--text); border:1px solid var(--border); }
        .fcard { display:none; margin-top:10px; align-items:baseline; gap:16px; flex-wrap:wrap; }
        .fcard .fpos { font-size:44px; font-weight:800; color:var(--accent); line-height:1; }
        .fcard .fname { font-size:20px; font-weight:700; }
        .fcard .fstat { color:var(--soft); font-size:15px; }
        .fcard .fstat b { color:var(--text); }
        .callup { display:none; margin-top:10px; padding:12px 14px; border-radius:10px; font-weight:800;
                  font-size:18px; background:var(--bg); border:2px solid var(--border); }
        .callup.next { border-color:var(--accent); color:var(--accent); animation:callpulse 1.1s ease-in-out infinite; }
        .callup.now { border-color:var(--good); color:var(--good); }
        @keyframes callpulse { 0%,100% { box-shadow:0 0 0 0 transparent; } 50% { box-shadow:0 0 0 6px var(--accent); } }
        /* 22 Oct 2026 (driver experience): when the race should start, the
           penalties race control gave tonight and why, and the transponder
           check - all under the call-up. */
        .callup .eta { display:block; margin-top:4px; font-size:15px; font-weight:600; }
        .fpens { display:none; margin-top:10px; font-size:14px; color:var(--soft); line-height:1.45; }
        .flost { display:none; margin-top:8px; font-size:14px; color:var(--soft); line-height:1.45; }
        .fpens b { color:var(--bad); }
        .fpens ul { margin:4px 0 0; padding-left:18px; }
        .tcheck { display:none; margin-top:10px; }
        .tcheck .speak { margin-left:0; }
        .tsay { display:block; margin-top:6px; font-size:15px; font-weight:700; color:var(--soft); }
        .tsay.ok { color:var(--good); } .tsay.bad { color:var(--bad); }
        /* Captions (?captions=1): the line the announcer just said, for
           anyone who can't hear the PA - on the wall, or over the stream. */
        #captions { display:none; position:fixed; left:50%; bottom:52px; transform:translateX(-50%); z-index:98;
                    width:max-content; max-width:min(92vw, 1000px); padding:10px 18px; border-radius:10px;
                    background:var(--glass-bg); color:var(--text); font-size:22px; font-weight:600; line-height:1.3;
                    text-align:center; transition:opacity .6s; }
        #captions.on { display:block; }
        #captions.fade { opacity:0; }
        tr.me td { outline:2px solid var(--accent); outline-offset:-2px; }
        body.overlay .follow { display:none; }
        /* Lap times read out on the phone, for earbuds. */
        .speak { font:inherit; font-size:14px; margin-left:8px; padding:6px 10px; border-radius:8px; cursor:pointer;
                 background:var(--bg); color:var(--soft); border:1px solid var(--border); }
        .speak.on { color:var(--accent); border-color:var(--accent); font-weight:700; }
        /* If it ended now: the championship with this race counted as it stands. */
        .champ { display:none; background:var(--surface); border-bottom:1px solid var(--border); padding:8px 18px; font-size:14px; }
        .champ .chead { font-weight:800; color:var(--accent); letter-spacing:.04em; }
        .champ .crow { display:inline-block; margin:4px 14px 0 0; color:var(--soft); }
        .champ .crow b { color:var(--text); }
        .champ .crow.racing b { color:var(--accent); }
        .champ .up { color:var(--good); } .champ .down { color:var(--bad); }
        body.overlay .champ { display:none !important; }
        </style></head><body data-look="{{look.Fingerprint}}">
        <div class="bar"><h1>{{look.Logo}}LIVE RESULTS</h1>
          <div><span class="state" id="state">-</span> &nbsp; <span class="countdown" id="countdown"></span></div>
        </div>
        <div class="ondeck" id="ondeck" style="display:none"></div>
        <div class="champ" id="champ"></div>
        <div class="follow" id="follow">
          <select id="followPick"><option value="">Follow a driver...</option></select>
          <div class="flost" id="flost">Your driver isn't on the list now. If you are racing tonight, pick your driver again above.</div>
          <button type="button" class="speak" id="speakBtn" style="display:none">Lap times in my ear: off</button>
          <div class="fcard" id="fcard"><span class="fpos" id="fpos"></span>
            <span><span class="fname" id="fname"></span><br><span class="fstat" id="fstat"></span></span></div>
          <div class="callup" id="callup"></div>
          <div class="fpens" id="fpens"></div>
          <div class="tcheck" id="tcheck"><button type="button" class="speak" id="tcheckBtn">Check my transponder</button>
            <span class="tsay" id="tsay" aria-live="polite"></span></div>
        </div>

        <div class="deck" id="deck">
          <div class="page">
            <table><thead><tr id="head"></tr></thead><tbody id="rows"></tbody></table>
            <div class="empty" id="empty">Waiting for lap data...</div>
            <div class="handnote" id="handnote" style="display:none"></div>
          </div>
          <div class="page">
            <div class="ptitle">Lap times</div>
            <canvas id="lapChart" height="300"></canvas>
            <div class="legend" id="lapLegend"></div>
          </div>
          <div class="page">
            <div class="ptitle">Best lap &amp; consistency</div>
            <canvas id="paceChart" height="300"></canvas>
            <div class="legend"><span>Bar length is the best lap - shorter is quicker. The lighter tail is lap-time spread.</span></div>
          </div>
          <div class="page">
            <div class="ptitle">Track map</div>
            <canvas id="mapChart" height="340"></canvas>
            <div class="legend" id="mapLegend"><span>Draw the racing line and the finish line on this layout's plan (Track Designer) to see the cars go round.</span></div>
          </div>
        </div>
        <div class="dots"><i class="dot on"></i><i class="dot"></i><i class="dot"></i><i class="dot"></i></div>

        <div id="captions" aria-live="polite"></div>
        <div id="sponsor"><img id="sponsorImg" src=""><video id="sponsorVid" muted playsinline preload="auto" style="display:none"></video></div>
        {{HeatCallerMark.PageFooterHtml}}
        <script>
        // Everything below builds rows with innerHTML, and a driver name is
        // whatever somebody typed into a profile. Without this, a driver
        // called "Ash <3 Racing" rendered as "Ash " - the rest swallowed as
        // a tag - and a name containing a script tag ran on every phone on
        // the club wifi and on the OBS overlay. The check-in page in this
        // same file uses textContent and was never exposed; these pages
        // build markup, so they escape instead.
        function esc(v){ return String(v==null?'':v).replace(/[&<>"']/g, function(c){
          return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]; }); }
        const overlay = new URLSearchParams(location.search).has('overlay');
        if (overlay) document.body.classList.add('overlay');

        let sponsorCount = 0, sponsorIdx = -1, showingSponsor = false, sinceSwap = 0;
        // Which slots are videos (indexes into the same list /api/sponsor
        // serves). Empty for the club whose slides are all pictures.
        let sponsorVideos = [];
        // Timings come from the server on every poll rather than being fixed
        // here, so edits on the Sponsors page take effect on already-open
        // pages without needing a refresh or a server restart.
        let leaderboardSecs = 60, slideSecs = 8, pauseDuringRace = true;

        // One poll at a time, and none left hanging: on poor Wi-Fi an older
        // answer arriving after a newer one read a lap out twice and buzzed
        // twice, and hung requests used up the phone's connections.
        let ticking = false;
        async function tick(){
          if (ticking) return;
          ticking = true;
          const stop = new AbortController(), timer = setTimeout(() => stop.abort(), 3000);
          try{
            const r = await fetch('/api/state', { signal: stop.signal }); const d = await r.json();
            takeClock(d.now);
            showCaptions(d.captions);
            document.getElementById('state').textContent = d.state + ' - ' + d.flag + ' flag';
            document.getElementById('countdown').textContent = d.countdown || '';

            const od = document.getElementById('ondeck');
            if (d.onDeck) { od.textContent = 'ON DECK:  ' + d.onDeck; od.style.display = 'block'; }
            else { od.style.display = 'none'; }

            document.getElementById('head').innerHTML =
              (d.columns||[]).map((c,i) => `<th class="${i===0?'pos':''}">${esc(c)}</th>`).join('');
            document.getElementById('rows').innerHTML = d.standings.map((s,ri) =>
              `<tr class="${ri===0?'p1':''}${s.id && s.id===following?' me':''}">` + s.cells.map((c,i) =>
                `<td class="${i===0?'pos':''} ${c.emphasis!=='none'?esc(c.emphasis):''}">${esc(c.text)}</td>`
              ).join('') + '</tr>').join('');
            document.getElementById('empty').style.display = d.standings.length ? 'none' : 'block';

            // What a * after a lap count means, when one is on the board.
            const hn = document.getElementById('handnote');
            if (d.byHand) { hn.textContent = d.byHand; hn.style.display = 'block'; }
            else { hn.style.display = 'none'; }

            showFollowing(d);
            takeMap(d.map);
            showChamp(d.champ);

            window.lastRows = d.standings;
            drawLaps(d.standings);
            drawPace(d.standings);

            sponsorCount = d.sponsorCount || 0;
            sponsorVideos = d.sponsorVideos || [];
            leaderboardSecs = d.sponsorLeaderboardSecs || 60;
            slideSecs = d.sponsorSlideSecs || 8;
            pauseDuringRace = d.sponsorPauseDuringRace !== false;
            if (!overlay) rotateSponsors(d.state);
          }catch(e){}
          finally { clearTimeout(timer); ticking = false; }
        }

        function rotateSponsors(state){
          sponsorState = state;
          if (sponsorCount === 0) { hideSponsor(); return; }
          sinceSwap++;
          if (showingSponsor) {
            // Not over a race that has gone green, a video included: one
            // started during staging or the countdown used to play on over
            // the opening laps, as nothing here asked again once it was up.
            // The Big Screen stops its video when the race is called.
            if (pauseDuringRace && state === 'Racing') { hideSponsor(); return; }
            // A video holds the page until it ends (sponsorVid.onended),
            // exactly as it holds the Big Screen - the slide time is for
            // still pictures.
            if (!showingVideo && sinceSwap >= slideSecs) hideSponsor();
          } else if (sinceSwap >= leaderboardSecs && !(pauseDuringRace && state === 'Racing')) {
            sponsorIdx = (sponsorIdx + 1) % sponsorCount;

            if (sponsorVideos.indexOf(sponsorIdx) >= 0) { startSponsorVideo(); return; }
            // Put up only once it has arrived (sponsorImg.onload). The overlay
            // used to go up as the slide was asked for, and a slide missing
            // or too big to send - answered 404 - covered the standings with
            // a blank screen for its whole turn, where the Big Screen skips it.
            // The count starts again here, so the next slide isn't asked for
            // while this one loads - only, a whole turn later, in place of
            // one that never came.
            loadingSponsor = true; sinceSwap = 0;
            sponsorImg.src = '/api/sponsor?i=' + sponsorIdx + '&t=' + Date.now();
          }
        }
        const sponsorImg = document.getElementById('sponsorImg');
        let loadingSponsor = false, sponsorState = '';
        sponsorImg.onload = function(){
          if (!loadingSponsor) return;
          loadingSponsor = false; sinceSwap = 0;
          // Not over a race that went green while it loaded.
          if (pauseDuringRace && sponsorState === 'Racing') return;
          document.getElementById('sponsor').style.display = 'flex';
          showingSponsor = true;
        };
        // Skipped: the board stays up for another full turn, as on the Big Screen.
        sponsorImg.onerror = function(){
          if (!loadingSponsor) return;
          loadingSponsor = false; sinceSwap = 0;
        };

        // --- Video slides ---------------------------------------------------
        //
        // ALWAYS MUTED here, and not a setting. Every browser refuses to
        // autoplay sound on a page the viewer hasn't interacted with, and a
        // phone that silently refused to start would simply show no ad at
        // all - so a muted video that plays beats an unmuted one that
        // doesn't. The Sponsors page's sound setting is about the Big
        // Screen, which is HeatCaller's own window and has no such rule.
        //
        // This is also why the phones don't need the music muted: there is
        // no sound coming out of them to be talked over.
        const sponsorVid = document.getElementById('sponsorVid');
        let showingVideo = false;

        function startSponsorVideo(){
          loadingSponsor = true; sinceSwap = 0;
          sponsorVid.src = '/api/sponsor?i=' + sponsorIdx + '&t=' + Date.now();
          // load() rather than waiting: a src set on an element that already
          // played keeps the old buffered data on some browsers.
          try { sponsorVid.load(); } catch(e){}
        }

        // Up only once there are frames to show, the same rule the picture
        // follows - a video that 404s or won't decode never covers the
        // standings.
        sponsorVid.oncanplay = function(){
          if (!loadingSponsor) return;
          loadingSponsor = false; sinceSwap = 0;
          if (pauseDuringRace && sponsorState === 'Racing') {
            // Let go of, not just left hidden: with its src still set it
            // would carry on buffering the whole file - see hideSponsor.
            try { sponsorVid.pause(); sponsorVid.removeAttribute('src'); sponsorVid.load(); } catch(e){}
            return;
          }
          sponsorImg.style.display = 'none';
          sponsorVid.style.display = 'block';
          document.getElementById('sponsor').style.display = 'flex';
          showingSponsor = true; showingVideo = true;
          const playing = sponsorVid.play();
          // Refused anyway (an older iOS, a battery-saving mode): treated as
          // a slide that didn't arrive rather than left as a frozen frame.
          if (playing && playing.catch) playing.catch(function(){ hideSponsor(); });
        };

        sponsorVid.onended = function(){ hideSponsor(); };
        sponsorVid.onerror = function(){
          if (!loadingSponsor) { hideSponsor(); return; }
          loadingSponsor = false; sinceSwap = 0;
        };

        // Enough hues to tell a full grid apart, spaced around the wheel so
        // neighbouring cars never get neighbouring colours. These are the
        // drivers' colours, not the look's, so they stay the same whatever
        // look is chosen - except that on a light page each is swapped for a
        // deeper shade of the same hue: a 2px lime or yellow line on white
        // is all but invisible.
        const LIGHT = document.documentElement.classList.contains('light');
        const COLOURS = LIGHT
          ? ['#4D7C0F','#0E7490','#C2410C','#15803D','#7E22CE','#A16207','#B91C1C','#047857','#1D4ED8','#BE185D']
          : ['#C8F031','#22D3EE','#FF9F45','#4ADE80','#B06CFF','#FFE05C','#F0483E','#7CFF5C','#5C9DFF','#FF9ED2'];

        // The chart's own ink - axes, labels, names - from the look, read
        // from the page's colour variables so the canvas matches the table.
        const CSSV = getComputedStyle(document.documentElement);
        function ink(name, fallback){ return (CSSV.getPropertyValue(name) || '').trim() || fallback; }
        const INK = { text: ink('--text','#F2F4F7'), muted: ink('--muted','#5A6478'),
                      soft: ink('--soft','#A8B0C0'), line: ink('--border','#161B24') };

        // Canvases are sized in CSS pixels but drawn in device pixels -
        // without this a chart is a blurry mess on any phone, which is all
        // of them.
        // The height each chart was laid out with (its height attribute),
        // kept from the first draw: the attribute is the drawing size after
        // that, and dividing it by the screen's pixel ratio again shrank a
        // chart to a third of its height on a phone.
        function prep(cv){
          const dpr = window.devicePixelRatio || 1;
          if (!cv.dataset.h) cv.dataset.h = cv.getAttribute('height') || '300';
          const w = cv.clientWidth, h = +cv.dataset.h;
          cv.style.height = h + 'px';
          cv.width = w * dpr; cv.height = h * dpr;
          const g = cv.getContext('2d');
          g.setTransform(dpr,0,0,dpr,0,0);
          g.clearRect(0,0,w,h);
          return {g,w,h};
        }

        function axes(g,w,h,pad){
          g.strokeStyle = INK.line; g.lineWidth = 1;
          g.beginPath(); g.moveTo(pad,8); g.lineTo(pad,h-pad); g.lineTo(w-8,h-pad); g.stroke();
        }

        function drawLaps(rows){
          const cv = document.getElementById('lapChart');
          const runners = (rows||[]).filter(r => r.laps && r.laps.length > 1).slice(0,10);
          const {g,w,h} = prep(cv);
          const legend = document.getElementById('lapLegend');

          if (!runners.length){
            legend.innerHTML = '';
            g.fillStyle = INK.muted; g.font = '14px system-ui';
            g.fillText('Two laps needed before there is a shape to draw.', 16, h/2);
            return;
          }

          const pad = 44;
          const all = runners.flatMap(r => r.laps);
          let lo = Math.min(...all), hi = Math.max(...all);

          // A flat race would otherwise divide by zero and draw nothing.
          if (hi - lo < 1) { hi = lo + 1; }

          const maxLap = Math.max(...runners.map(r => r.laps.length));
          const x = i => pad + (maxLap<2?0:(i/(maxLap-1))*(w-pad-12));
          const y = v => 8 + (1-(v-lo)/(hi-lo))*(h-pad-8);

          axes(g,w,h,pad);
          g.fillStyle = INK.muted; g.font = '11px system-ui';
          g.fillText((hi/1000).toFixed(2)+'s', 4, 16);
          g.fillText((lo/1000).toFixed(2)+'s', 4, h-pad+4);

          runners.forEach((r,ri) => {
            g.strokeStyle = COLOURS[ri % COLOURS.length];
            g.lineWidth = 2; g.beginPath();
            r.laps.forEach((v,i) => i ? g.lineTo(x(i),y(v)) : g.moveTo(x(i),y(v)));
            g.stroke();
          });

          legend.innerHTML = runners.map((r,ri) =>
            `<span><i class="swatch" style="background:${COLOURS[ri % COLOURS.length]}"></i>${esc(r.name)}</span>`).join('');
        }

        function drawPace(rows){
          const cv = document.getElementById('paceChart');
          const runners = (rows||[]).filter(r => r.best > 0).slice(0,10);
          const {g,w,h} = prep(cv);

          if (!runners.length){
            g.fillStyle = INK.muted; g.font = '14px system-ui';
            g.fillText('No lap times yet.', 16, h/2);
            return;
          }

          const pad = 8, labelW = 96;
          const slowest = Math.max(...runners.map(r => r.best));
          const rowH = Math.min(30, (h - pad*2) / runners.length);

          runners.forEach((r,ri) => {
            const top = pad + ri*rowH;
            const len = (r.best/slowest) * (w - labelW - 60);

            g.fillStyle = INK.soft; g.font = '12px system-ui';
            g.fillText(r.name.slice(0,13), 8, top + rowH*0.65);

            g.fillStyle = COLOURS[ri % COLOURS.length];
            g.fillRect(labelW, top + 5, len, rowH-12);

            // Spread drawn as a paler tail off the bar: a driver can be
            // quick and scattered, and the bar alone can't say that.
            const laps = r.laps || [];
            if (laps.length > 2){
              const mean = laps.reduce((a,b)=>a+b,0)/laps.length;
              const sd = Math.sqrt(laps.reduce((a,b)=>a+(b-mean)*(b-mean),0)/laps.length);
              const tail = Math.min((sd/slowest) * (w - labelW - 60) * 3, w - labelW - len - 60);
              g.globalAlpha = 0.32;
              g.fillRect(labelW + len, top + 5, Math.max(0,tail), rowH-12);
              g.globalAlpha = 1;
            }

            g.fillStyle = INK.text; g.font = '12px system-ui';
            g.fillText((r.best/1000).toFixed(3), labelW + len + 6, top + rowH*0.65);
          });
        }

        // Dots follow the scroller rather than driving it, so they stay
        // honest if the user half-swipes and lets go.
        const deck = document.getElementById('deck');
        deck.addEventListener('scroll', () => {
          const i = Math.round(deck.scrollLeft / deck.clientWidth);
          document.querySelectorAll('.dot').forEach((d,n) => d.classList.toggle('on', n===i));
        }, {passive:true});

        window.addEventListener('resize', () => { if (window.lastRows) { drawLaps(window.lastRows); drawPace(window.lastRows); } });

        // --- The track map ----------------------------------------------
        // Each car is placed where the laptop last worked it out to be, then
        // moved on at its usual pace between polls - held short of the next
        // loop, as the laptop holds it - so the dots glide rather than hop
        // once a second. A car that seems to have stopped is red and pulses.
        let map = null, mapAt = 0;

        function takeMap(m){
          map = m || null; mapAt = performance.now();
          if (map) {
            const stopped = (map.cars || []).filter(c => c.stop);
            document.getElementById('mapLegend').innerHTML = stopped.length
              ? stopped.map(c => `<span style="color:var(--bad)">${esc(nameOf(c.id))} may have stopped ${esc(c.where)}</span>`).join('')
              : '<span>Each dot is a car, its number its place in the race.' +
                (following ? ' The dotted ring is your driver on their best lap of the race.' : '') + '</span>';
          }
        }

        // --- If it ended now ---------------------------------------------
        function showChamp(c){
          const el = document.getElementById('champ');
          if (!c || !c.rows || !c.rows.length){ el.style.display = 'none'; return; }
          el.innerHTML = `<div class="chead">IF IT ENDED NOW - ${esc(c.headline)}</div>` + c.rows.slice(0, 6).map(r => {
            const cls = r.move.startsWith('\u25B2') ? 'up' : r.move.startsWith('\u25BC') ? 'down' : '';
            return `<span class="crow${r.racing ? ' racing' : ''}">${r.pos}. <b>${esc(r.name)}</b> ${r.pts}` +
              (r.move ? ` <span class="${cls}">${esc(r.move)}</span>` : '') + '</span>';
          }).join('');
          el.style.display = 'block';
        }

        function nameOf(id){
          const p = (window.people || []).find(x => x.id === id);
          return p ? p.name : id;
        }

        function pointAt(pts, cum, f){
          f = f - Math.floor(f);
          const want = f * cum[cum.length-1];
          for (let i = 1; i < pts.length; i++){
            if (cum[i] >= want){
              const seg = cum[i] - cum[i-1], t = seg <= 0 ? 0 : (want - cum[i-1]) / seg;
              return [pts[i-1][0] + (pts[i][0]-pts[i-1][0])*t, pts[i-1][1] + (pts[i][1]-pts[i-1][1])*t];
            }
          }
          return pts[pts.length-1];
        }

        function drawMap(){
          const cv = document.getElementById('mapChart');
          if (!cv || !cv.clientWidth) return;
          const {g,w,h} = prep(cv);
          if (!map || !map.pts || map.pts.length < 2){
            g.fillStyle = INK.muted; g.font = '14px system-ui';
            g.fillText('No track plan for this layout.', 16, h/2);
            return;
          }
          const pts = map.pts, cum = [0];
          for (let i = 1; i < pts.length; i++) cum.push(cum[i-1] + Math.hypot(pts[i][0]-pts[i-1][0], pts[i][1]-pts[i-1][1]));
          const xs = pts.map(p => p[0]), ys = pts.map(p => p[1]);
          const minX = Math.min(...xs), minY = Math.min(...ys);
          const spanX = Math.max(0.01, Math.max(...xs) - minX), spanY = Math.max(0.01, Math.max(...ys) - minY);
          const pad = 22, k = Math.min((w-2*pad)/spanX, (h-2*pad)/spanY);
          const ox = (w - spanX*k)/2 - minX*k, oy = (h - spanY*k)/2 - minY*k;
          const P = p => [ox + p[0]*k, oy + p[1]*k];

          g.lineJoin = 'round'; g.strokeStyle = INK.line; g.lineWidth = 12;
          g.beginPath(); pts.forEach((p,i) => { const q = P(p); i ? g.lineTo(q[0],q[1]) : g.moveTo(q[0],q[1]); }); g.stroke();

          const mark = (f, label, colour) => {
            const a = P(pointAt(pts, cum, f)), b = P(pointAt(pts, cum, f + 0.005));
            const dx = b[0]-a[0], dy = b[1]-a[1], len = Math.hypot(dx,dy) || 1, nx = -dy/len*12, ny = dx/len*12;
            g.strokeStyle = colour; g.lineWidth = 3;
            g.beginPath(); g.moveTo(a[0]-nx, a[1]-ny); g.lineTo(a[0]+nx, a[1]+ny); g.stroke();
            g.fillStyle = colour; g.font = 'bold 11px system-ui'; g.fillText(label, a[0]+nx*1.3, a[1]+ny*1.3);
          };
          mark(0, 'S/F', INK.text);
          (map.splits || []).forEach((f,i) => mark(f, 'S' + (i+1), INK.soft));

          const secs = (performance.now() - mapAt) / 1000, pulse = (1 - Math.cos(performance.now()/1100*2*Math.PI))/2;

          // The followed driver's best-lap ghost: a dotted ring where they
          // would be on their best lap of the race. Ahead of it, a PB lap.
          const mine = (map.cars || []).find(c => c.id === following && !c.stop && c.g >= 0);
          if (mine){
            const q = P(pointAt(pts, cum, Math.min(1, mine.g + mine.gr * secs)));
            g.setLineDash([3, 4]); g.strokeStyle = COLOURS[(mine.pos-1) % COLOURS.length]; g.lineWidth = 2.5;
            g.beginPath(); g.arc(q[0], q[1], 17, 0, Math.PI*2); g.stroke(); g.setLineDash([]);
          }

          [...(map.cars || [])].sort((a,b) => b.pos - a.pos).forEach(c => {
            const f = c.stop ? c.f : Math.min(c.cap, c.f + c.rate * secs);
            const q = P(pointAt(pts, cum, f));
            const r = c.id === following ? 11 : 9;
            if (c.stop){
              g.strokeStyle = 'rgba(239,68,68,' + (0.9 - pulse*0.6) + ')'; g.lineWidth = 3;
              g.beginPath(); g.arc(q[0], q[1], r * (1.6 + pulse*1.4), 0, Math.PI*2); g.stroke();
            }
            g.fillStyle = c.stop ? '#EF4444' : COLOURS[(c.pos-1) % COLOURS.length];
            g.beginPath(); g.arc(q[0], q[1], r, 0, Math.PI*2); g.fill();
            if (c.id === following){ g.strokeStyle = INK.text; g.lineWidth = 2; g.stroke(); }
            g.fillStyle = c.stop ? '#fff' : '#000'; g.font = 'bold 11px system-ui'; g.textAlign = 'center'; g.textBaseline = 'middle';
            g.fillText(c.stop ? '!' : String(c.pos), q[0], q[1] + 0.5);
            g.textAlign = 'start'; g.textBaseline = 'alphabetic';
          });
        }

        // About ten frames a second, and only while the map page is the one showing.
        setInterval(() => {
          const i = Math.round(deck.scrollLeft / Math.max(1, deck.clientWidth));
          if (i === 3) drawMap();
        }, 100);

        // --- Following one driver ---------------------------------------
        // Remembered on this phone, so it is still following them after a
        // reload. Picking a driver is also the tap that lets the page buzz
        // and beep later: phones only allow either after a touch.
        const FOLLOW_KEY = 'hcFollow';
        let following = '';
        try { following = localStorage.getItem(FOLLOW_KEY) || ''; } catch(e) {}
        let lastCallKey = '', audio = null;

        const pick = document.getElementById('followPick');
        pick.addEventListener('change', () => {
          following = pick.value;
          try { localStorage.setItem(FOLLOW_KEY, following); } catch(e) {}
          wakeAudio();
          lastCallKey = '';
          tick();
        });

        // The beep's player, made - or woken - by a tap: phones only allow
        // sound after one, and after a reload there has been none yet.
        function wakeAudio(){
          try {
            audio = audio || new (window.AudioContext || window.webkitAudioContext)();
            if (audio.state !== 'running') audio.resume();
          } catch(e) {}
        }

        function fmt(ms){ return ms > 0 ? (ms/1000).toFixed(3) : '-'; }

        function showFollowing(d){
          // The picker, filled again only when the list of people changes.
          const people = d.people || [];
          window.people = people;
          const sig = people.map(p => p.id).join('|');
          if (pick.dataset.sig !== sig){
            pick.innerHTML = '<option value="">Follow a driver...</option>' + people.map(p =>
              `<option value="${esc(p.id)}">${esc(p.name)}</option>`).join('');
            pick.dataset.sig = sig;
          }
          pick.value = people.some(p => p.id === following) ? following : '';

          // WHY (23 Oct 2026, bug hunt 10): a restart of the web server (the
          // only way to change a PIN) gives everybody new ids, and a phone
          // still holding the old one showed nothing while its driver raced.
          // A pick nobody on the list matches now says to pick again. It is
          // kept, not wiped - as the coach page does.
          const lost = !!following && people.length > 0 && !people.some(p => p.id === following);
          document.getElementById('flost').style.display = lost ? 'block' : 'none';

          const card = (d.cards || {})[following];
          const fc = document.getElementById('fcard');
          if (following && card){
            const who = people.find(p => p.id === following);
            document.getElementById('fpos').textContent = 'P' + card.pos;
            document.getElementById('fname').textContent = who ? who.name : following;
            document.getElementById('fstat').innerHTML =
              `Lap <b>${card.laps}</b> &nbsp; Last <b>${fmt(card.last)}</b><br>` +
              `Ahead <b>${esc(card.ahead)}</b> &nbsp; Behind <b>${esc(card.behind)}</b>`;
            fc.style.display = 'flex';
          } else {
            fc.style.display = 'none';
          }

          speakLap(card);

          const call = (d.callups || {})[following];
          const cu = document.getElementById('callup');
          if (following && call){
            cu.textContent = call.words.toUpperCase() + ' - ' + call.race + ' - grid ' + call.slot;
            // About when it starts, by the laptop's clock (22 Oct 2026).
            const eta = etaWords(call);
            if (eta){ const e = document.createElement('span'); e.className = 'eta'; e.textContent = eta; cu.appendChild(e); }
            // Staged is the race on track, but it is the one moment the
            // driver has to move, so it pulses like "next" rather than
            // sitting green like "on track".
            cu.className = 'callup ' + (call.staged ? 'next' : call.now ? 'now' : call.away === 0 ? 'next' : '');
            cu.style.display = 'block';

            // Buzz as their race becomes the next one - and again when it
            // is staged (22 Oct 2026, driver experience): the key changes at
            // both, and at nothing in between, because a race that goes on
            // track keeps the same race and an away of 0.
            const key = call.race + '|' + call.away + '|' + (call.staged ? 'staged' : '');
            if (lastCallKey && lastCallKey !== key && (call.staged || (!call.now && call.away === 0))) buzz();
            lastCallKey = key;
          } else {
            cu.style.display = 'none';
            lastCallKey = lastCallKey || 'none';
          }

          showPens(d, card);
          showCheck(d);
        }

        // --- The laptop's clock (22 Oct 2026, driver experience) ----------
        // Every "about 7 min" and "seen at" is worked out against the time
        // the laptop says it is, carried on each poll as "now": a phone's
        // own clock can be minutes out, and the ETAs and sightings were all
        // measured by the laptop's.
        let skew = 0;
        function takeClock(now){
          const t = Date.parse(now || '');
          if (!isNaN(t)) skew = t - Date.now();
        }
        function serverNow(){ return Date.now() + skew; }
        function clockAt(ms){
          try { return new Date(ms).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' }); }
          catch(e) { return ''; }
        }

        // "about 7 min (19:42)" - only for a race still to come. A race
        // already on track or staged has its own words.
        function etaWords(call){
          if (!call || !call.eta || call.now || call.staged) return '';
          const at = Date.parse(call.eta);
          if (isNaN(at)) return '';
          const mins = Math.round((at - serverNow()) / 60000);
          return (mins >= 1 ? 'about ' + mins + ' min' : 'any minute now') + ' (' + clockAt(at) + ')';
        }

        // --- Penalties (22 Oct 2026, driver experience) -------------------
        // This race's warnings and penalty time as they stand, then each
        // penalty from tonight's filed races with race control's reason.
        // In the room only: the PA has already said them, and the club's
        // online pages carry the numbers alone.
        function showPens(d, card){
          const box = document.getElementById('fpens');
          const lines = following ? ((d.pens || {})[following] || []) : [];
          const now = [];
          if (following && card){
            if (card.warn > 0) now.push(card.warn + (card.warn === 1 ? ' warning' : ' warnings'));
            if (card.penS > 0) now.push('+' + card.penS + ' s penalty');
          }
          if (!now.length && !lines.length){ box.style.display = 'none'; box.innerHTML = ''; return; }
          box.innerHTML = (now.length ? 'This race: <b>' + esc(now.join(', ')) + '</b>' : '') +
            (lines.length ? (now.length ? '<br>' : '') + 'Tonight:<ul>' + lines.map(l => '<li>' + esc(l) + '</li>').join('') + '</ul>' : '');
          box.style.display = 'block';
        }

        // --- Check my transponder (22 Oct 2026, driver experience) --------
        // Press, drive past the line, and the phone says it was heard and
        // how well. Nothing special happens on the laptop: every pass is
        // already recorded, so this only watches "seen" for one newer than
        // the press.
        const CHECK_MS = 3 * 60 * 1000;
        const tcheckBtn = document.getElementById('tcheckBtn'), tsay = document.getElementById('tsay');
        let checkFor = '', checkFrom = 0;

        function checkSay(text, cls){ tsay.textContent = text; tsay.className = 'tsay' + (cls ? ' ' + cls : ''); }

        tcheckBtn.addEventListener('click', () => {
          wakeAudio();
          checkFor = following; checkFrom = serverNow();
          checkSay('Drive past the line now - watching for it...', '');
        });

        function showCheck(d){
          document.getElementById('tcheck').style.display = following ? 'block' : 'none';
          if (!checkFor) return;
          if (checkFor !== following){ checkFor = ''; checkSay('', ''); return; }
          const docNow = isNaN(Date.parse(d.now || '')) ? serverNow() : Date.parse(d.now);
          const s = (d.seen || {})[following];
          if (s && docNow - s.ago * 1000 > checkFrom){
            checkFor = '';
            const at = docNow - s.ago * 1000;
            if (s.sig === 'weak') checkSay('Seen at ' + clockAt(at) + ', signal weak - check it is mounted where the loop can see it, and its battery.', 'bad');
            else checkSay('Seen at ' + clockAt(at) + (s.sig === 'good' ? ', signal good' : ''), 'ok');
            buzz();
            return;
          }
          if (serverNow() - checkFrom > CHECK_MS){
            checkFor = '';
            checkSay('Not seen. Check it is switched on and charged, and mounted with a clear view of the track - then try again.', 'bad');
          }
        }

        // --- Captions (?captions=1, 22 Oct 2026, driver experience) -------
        // The line the announcer has just said, at the bottom of the page,
        // fading after eight seconds. Only a line said while the page is
        // open: the first look takes what is there as already said, so an
        // OBS source reloaded mid-race does not put up an old line.
        const captionsOn = new URLSearchParams(location.search).get('captions') === '1';
        const CAPTION_MS = 8000;
        let captionSeen = -1, captionTimer = 0;
        function showCaptions(list){
          if (!captionsOn) return;
          const lines = Array.isArray(list) ? list : [];
          const newest = lines.length ? lines[lines.length - 1] : null;
          if (captionSeen < 0){ captionSeen = newest ? newest.n : 0; return; }
          // A different number rather than a bigger one: the count starts
          // again when HeatCaller does, and a page left open must not go
          // quiet until it catches up.
          if (!newest || newest.n === captionSeen) return;
          captionSeen = newest.n;
          const el = document.getElementById('captions');
          el.textContent = newest.t;
          el.classList.remove('fade'); el.classList.add('on');
          clearTimeout(captionTimer);
          captionTimer = setTimeout(() => el.classList.add('fade'), CAPTION_MS);
        }

        // --- Lap times in your ear ---------------------------------------
        // Each new lap of the driver being followed, read out by the phone's
        // own voice: "Lap 7, 19.42, best lap, P2, 0.4 seconds behind". On
        // with the button, remembered on this phone. Turning it on speaks
        // once straight away, which is what lets a phone speak later.
        const SPEAK_KEY = 'hcSpeak';
        const speakBtn = document.getElementById('speakBtn');
        let speaking = false, unlocked = false;
        try { speaking = localStorage.getItem(SPEAK_KEY) === '1'; } catch(e) {}
        let spokenFor = '', spokenLap = -1;
        const canSpeak = 'speechSynthesis' in window;

        function sayAloud(text){
          try {
            window.speechSynthesis.cancel();
            const u = new SpeechSynthesisUtterance(text);
            u.rate = 1.1;
            window.speechSynthesis.speak(u);
          } catch(e) {}
        }

        // A phone only lets a page speak once it has been tapped since it
        // loaded - so after a reload with this on, it waits for a tap.
        function showSpeak(){
          speakBtn.style.display = canSpeak && following ? 'inline-block' : 'none';
          speakBtn.textContent = 'Lap times in my ear: ' + (!speaking ? 'off' : unlocked ? 'on' : 'tap to resume');
          speakBtn.classList.toggle('on', speaking);
        }

        // A click, not a touch going down: iPhones only count a finished tap
        // as one that lets a page speak or play sound.
        document.addEventListener('click', e => {
          wakeAudio();
          if (unlocked || e.target === speakBtn) return;
          unlocked = true;
          if (speaking) sayAloud(' ');
          showSpeak();
        }, true);

        // Not reloaded for a change of look while it is talking.
        function lookBusy(){ return speaking; }

        speakBtn.addEventListener('click', () => {
          // On, but waiting for a tap since a reload: this tap resumes it.
          if (speaking && !unlocked){
            unlocked = true; spokenFor = '';
            sayAloud('Lap times on'); showSpeak();
            return;
          }
          unlocked = true;
          speaking = !speaking;
          try { localStorage.setItem(SPEAK_KEY, speaking ? '1' : '0'); } catch(e) {}
          if (speaking) sayAloud('Lap times on'); else { try { window.speechSynthesis.cancel(); } catch(e) {} }
          spokenFor = '';
          showSpeak();
        });

        function gapWords(gap){
          const laps = /^\+(\d+)L$/.exec(gap || '');
          if (laps) return laps[1] + (laps[1] === '1' ? ' lap' : ' laps') + ' behind';
          const secs = /^\+(\d+([.,]\d+)?)$/.exec(gap || '');
          return secs ? secs[1].replace(',', '.') + ' seconds behind' : '';
        }

        function speakLap(card){
          showSpeak();
          if (!speaking || !unlocked || !following || !card) return;

          // A new driver, or a new race: start counting from here, quietly.
          if (spokenFor !== following || card.laps < spokenLap){
            spokenFor = following; spokenLap = card.laps;
            return;
          }

          if (card.laps <= spokenLap || !(card.last > 0)) return;
          spokenLap = card.laps;

          const parts = ['Lap ' + card.laps, (card.last / 1000).toFixed(2)];
          if (card.best > 0 && card.last <= card.best && card.laps > 1) parts.push('best lap');
          parts.push('P' + card.pos);
          const gap = gapWords(card.ahead);
          if (gap) parts.push(gap);
          sayAloud(parts.join(', '));
        }

        function buzz(){
          try { if (navigator.vibrate) navigator.vibrate([250,120,250,120,400]); } catch(e) {}
          try {
            if (!audio) return;
            if (audio.state !== 'running') audio.resume();
            [0, 0.25].forEach(at => {
              const o = audio.createOscillator(), g = audio.createGain();
              o.frequency.value = 880; o.connect(g); g.connect(audio.destination);
              g.gain.setValueAtTime(0.2, audio.currentTime + at);
              g.gain.exponentialRampToValueAtTime(0.001, audio.currentTime + at + 0.2);
              o.start(audio.currentTime + at); o.stop(audio.currentTime + at + 0.22);
            });
          } catch(e) {}
        }

        function hideSponsor(){
          document.getElementById('sponsor').style.display = 'none';
          showingSponsor = false; loadingSponsor = false; sinceSwap = 0;

          // The video is stopped and let go of, not just hidden: a hidden
          // <video> with a src keeps the connection open and keeps
          // buffering, and over a race night that is one stream per slide
          // per phone, all of them live at once.
          if (showingVideo) {
            showingVideo = false;
            try { sponsorVid.pause(); sponsorVid.removeAttribute('src'); sponsorVid.load(); } catch(e){}
            sponsorVid.style.display = 'none';
            sponsorImg.style.display = '';
          }
        }
        tick(); setInterval(tick, 1000);
        </script><script>{{LookWatchScript}}</script></body></html>
        """;
    }

    /// <summary>
    /// Sign-on from a phone: the driver finds their own name and taps it.
    ///
    /// A LIST rather than a camera, and that is a decision rather than a
    /// shortcut. This server speaks plain HTTP on a LAN address, and every
    /// current browser refuses getUserMedia outside a secure context - so a
    /// camera button on this page would be a button that does nothing on
    /// every phone at the track. The scan path that DOES work is the USB
    /// scanner on the Entries page, where somebody is standing anyway.
    ///
    /// The camera is still wired up behind a feature check, so it appears
    /// by itself if this is ever served over https, and stays invisible
    /// rather than broken until then.
    ///
    /// Internal rather than private so the tests can read its script
    /// (BugHunt4WebTests).
    /// </summary>
    internal string BuildCheckInPage()
    {
        var look = CurrentPageLook();

        return $$"""
        <!DOCTYPE html><html class="{{look.Scheme}}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="{{look.Scheme}}"><meta name="theme-color" content="{{look.ThemeColor}}">
        <title>Check In</title><style>{{look.Root}}{{SharedStyle}}
        .wrap { padding:14px; max-width:560px; margin:0 auto; }
        .pin, .find { width:100%; padding:13px; font-size:17px; border-radius:8px;
                      border:1px solid var(--raised); background:var(--surface); color:var(--text); margin-bottom:10px; }
        .msg { text-align:center; padding:8px; font-weight:700; min-height:20px; font-size:15px; }
        .row { width:100%; display:flex; align-items:center; justify-content:space-between;
               gap:10px; padding:15px 14px; margin:7px 0; border:1px solid var(--raised);
               border-radius:10px; background:var(--surface); color:var(--text); text-align:left;
               font-family:inherit; cursor:pointer; }
        .row.on { background:var(--on-tint); border-color:var(--on-edge); }
        .row:active { opacity:.7; }
        .lt { display:flex; flex-direction:column; gap:3px; min-width:0; }
        .nm { font-size:18px; font-weight:700; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
        .cl { font-size:12px; color:var(--muted); letter-spacing:.06em; text-transform:uppercase; }
        .chip { flex:0 0 auto; font-size:11px; font-weight:700; letter-spacing:.14em;
                padding:6px 10px; border-radius:20px; background:var(--raised); color:var(--muted); }
        .row.on .chip { background:var(--primary); color:var(--on-primary); }
        /* Centred explicitly. A phone browser with a large accessibility
           text size, or a button that ends up taller than one line, does
           not reliably centre a button's label on its own - and a caption
           sitting against one edge of a big coloured block is the first
           thing anyone notices. */
        .cam { display:none; width:100%; padding:14px; margin-bottom:10px; border:none;
               border-radius:10px; background:var(--primary); color:var(--on-primary); font-size:16px; font-weight:700;
               font-family:inherit; text-align:center; line-height:1.25;
               align-items:center; justify-content:center; }
        .cam.on { display:flex; }
        #camwrap { display:none; margin-bottom:10px; }
        #vid { width:100%; border-radius:10px; background:#000; }
        </style></head><body data-look="{{look.Fingerprint}}">
        <div class="bar"><h1>{{look.Logo}}CHECK IN</h1><span class="state" id="count">-</span></div>
        <div class="wrap">
          <input class="pin" id="pin" placeholder="Check-in PIN (leave blank if none)" inputmode="numeric">
          <button class="cam" id="cam">SCAN A QR CHECK-IN</button>
          <div id="camwrap"><video id="vid" playsinline muted></video>
            <button class="cam on" id="camclose" style="background:#4a4a4a;color:#fff">CLOSE CAMERA</button></div>
          <input class="find" id="find" placeholder="Find your name" autocomplete="off">
          <div class="msg" id="msg"></div>
          <div id="list"></div>
        </div>
        {{HeatCallerMark.PageFooterHtml}}
        <script>
        // Everything below builds rows with innerHTML, and a driver name is
        // whatever somebody typed into a profile. Without this, a driver
        // called "Ash <3 Racing" rendered as "Ash " - the rest swallowed as
        // a tag - and a name containing a script tag ran on every phone on
        // the club wifi and on the OBS overlay. The check-in page in this
        // same file uses textContent and was never exposed; these pages
        // build markup, so they escape instead.
        function esc(v){ return String(v==null?'':v).replace(/[&<>"']/g, function(c){
          return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]; }); }
        var pinBox = document.getElementById('pin');
        var find = document.getElementById('find');
        var list = document.getElementById('list');
        var msg = document.getElementById('msg');
        var data = [];
        // A key of its own. The remote and this page used to share one, so
        // a phone that had ever opened the remote handed its PIN to this
        // page - and a driver borrowing that phone could read it back.
        try { pinBox.value = localStorage.getItem('mzrd_checkin_pin') || ''; } catch(e) {}
        // The PIN that was last refused. Polling stops while the box still
        // holds it: every refused poll is a wrong guess to the laptop, and
        // a page left open with a stale PIN would lock its own phone out.
        var badPin = null;
        // Whether this check-in has a PIN, as of when the page was served.
        // With one set, an empty box is never sent: the page asks for the
        // PIN instead. The laptop does not count an empty PIN as a wrong
        // guess any more, but there is no point asking a question whose
        // answer is already known on every poll.
        var pinNeeded = {{(string.IsNullOrEmpty(CheckInPin) ? "false" : "true")}};
        // When an empty box last checked whether a PIN is still needed -
        // see load(). Starts now: the page was just served with the
        // answer, so the first look is thirty seconds away, not at once.
        var lastEmptyProbe = Date.now();
        pinBox.addEventListener('change', function(){
          try { localStorage.setItem('mzrd_checkin_pin', pinBox.value); } catch(e) {}
          badPin = null;
          load();
        });
        find.addEventListener('input', render);
        function say(text, good){
          msg.textContent = text;
          msg.style.color = good ? 'var(--good)' : 'var(--bad)';
        }
        function render(){
          var term = find.value.trim().toLowerCase();
          list.innerHTML = '';
          var shown = data.filter(function(d){
            return !term || (d.n + ' ' + (d.c||'')).toLowerCase().indexOf(term) >= 0;
          });
          if (!shown.length) {
            var e = document.createElement('div');
            e.className = 'empty';
            e.textContent = data.length ? 'Nobody matches that.' : 'No drivers on the list yet.';
            list.appendChild(e);
            return;
          }
          shown.forEach(function(d){
            var row = document.createElement('button');
            row.className = 'row' + (d.in ? ' on' : '');
            var left = document.createElement('span'); left.className = 'lt';
            var nm = document.createElement('span'); nm.className = 'nm'; nm.textContent = d.n;
            var cl = document.createElement('span'); cl.className = 'cl'; cl.textContent = d.c || 'No class';
            left.appendChild(nm); left.appendChild(cl);
            var chip = document.createElement('span'); chip.className = 'chip';
            chip.textContent = d.in ? 'HERE' : 'TAP';
            row.appendChild(left); row.appendChild(chip);
            row.onclick = function(){ toggle(d); };
            list.appendChild(row);
          });
        }
        function askForPin(){
          data = [];
          render();
          say('Enter the check-in PIN above', false);
        }
        // One look at a time, and given up on after five seconds: a new one
        // every four seconds whether or not the last had come back piled up
        // on weak Wi-Fi, and a tap then waited behind them.
        var loading = false;
        async function load(){
          if (loading) { return; }
          if (badPin !== null && pinBox.value === badPin) { return; }
          if (pinNeeded && pinBox.value === '') {
            askForPin();
            // Once every thirty seconds it looks again, in case the laptop
            // has since cleared the PIN - otherwise this page would ask for
            // a PIN that no longer exists until somebody reloaded it. An
            // empty PIN is not counted as a guess, so the look costs
            // nothing but the request.
            if (Date.now() - lastEmptyProbe < 30000) { return; }
            lastEmptyProbe = Date.now();
          }
          loading = true;
          // The PIN this look sends. The box can change while it is out,
          // and the look the change asks for is then dropped (one at a
          // time, above). A refusal used to be put down to the box as it
          // was when the answer came - the right PIN, just typed, refused
          // and never tried until the page was reloaded. Now it is the sent
          // PIN's, and a box changed meanwhile is looked at once this is back.
          var sentPin = pinBox.value;
          var stop = new AbortController(), timer = setTimeout(function(){ stop.abort(); }, 5000);
          try {
            var r = await fetch('/api/roster', { headers: { 'X-Pin': sentPin }, signal: stop.signal });
            var d = await r.json();
            // No PIN typed, and the laptop wants one (set since this page
            // loaded, perhaps): ask for it, and stop sending the empty box.
            if (!r.ok && d.needPin) { pinNeeded = true; askForPin(); return; }
            if (r.ok && pinBox.value === '') { pinNeeded = false; }
            if (!r.ok) {
              if (r.status === 403 || r.status === 429) { badPin = sentPin; }
              // Locked out rather than wrong: the lock lifts by itself, so
              // polling resumes by itself a minute later.
              if (r.status === 429) { setTimeout(function(){ badPin = null; }, 60000); }
              data = [];
              render();
              say(d.error || 'Enter the check-in PIN', false);
              return;
            }
            data = d.drivers || [];
            document.getElementById('count').textContent = d.checkedIn + ' / ' + d.total + ' HERE';
            render();
          } catch(e) {}
          finally {
            clearTimeout(timer);
            loading = false;
            if (pinBox.value !== sentPin) { setTimeout(load, 0); }
          }
        }
        // WHY (26 Oct 2026, asked for by Asher: "is there a reason the phone
        // sign in page you can sign in/out anyone you want without a pin?"):
        // every check-in or check-out from this page needs THAT driver's own
        // personal PIN, and the laptop refuses the tap without it. Asked for
        // on every tap and never kept - not in storage, not in the address;
        // it goes once, in its own header, to the laptop and nowhere else.
        function askRacerPin(name, want){
          var p = '';
          try { p = window.prompt((name ? name + "'s" : 'Your') + ' own check-in PIN, to check ' + (name ? 'them' : 'you') + ' ' + want + ':') || ''; } catch(e) { p = ''; }
          p = String(p).trim();
          return p === '' ? null : p;
        }
        async function toggle(d){
          // Out asks first. A tap on the wrong row while scrolling used to
          // sign somebody off without a word, and they found out when
          // their heat went without them.
          if (d.in && !confirm('Check ' + d.n + ' out?')) { return; }
          var want = d.in ? 'out' : 'in';
          var racerPin = askRacerPin(d.n, want);
          if (racerPin === null) { say('Not changed - ' + d.n + "'s own check-in PIN is needed.", false); return; }
          d.in = !d.in;
          render();
          try {
            var r = await fetch('/api/checkin?t=' + encodeURIComponent(d.t) + '&state=' + want,
                              { method: 'POST', headers: { 'X-HeatCaller': '1', 'X-Pin': pinBox.value, 'X-Racer-Pin': racerPin } });
            var j = await r.json();
            if (!j.ok) { d.in = !d.in; render(); say(j.error || 'Failed', false); return; }
            say(d.n + (d.in ? ' checked in' : ' checked out'), true);
          } catch(e) { d.in = !d.in; render(); say('Connection lost', false); }
          setTimeout(load, 500);
        }
        async function send(payload){
          var racerPin = askRacerPin('', 'in');
          if (racerPin === null) { say('Not changed - your own check-in PIN is needed.', false); return; }
          try {
            var r = await fetch('/api/checkin?t=' + encodeURIComponent(payload) + '&state=in',
                              { method: 'POST', headers: { 'X-HeatCaller': '1', 'X-Pin': pinBox.value, 'X-Racer-Pin': racerPin } });
            var j = await r.json();
            say(j.ok ? 'Checked in' : (j.error || 'Failed'), j.ok);
          } catch(e) { say('Connection lost', false); }
          setTimeout(load, 500);
        }
        var camBtn = document.getElementById('cam');
        var camWrap = document.getElementById('camwrap');
        if (window.isSecureContext && ('BarcodeDetector' in window) && navigator.mediaDevices) {
          camBtn.classList.add('on');
          camBtn.onclick = scan;
        }
        async function scan(){
          var v = document.getElementById('vid');
          try {
            var stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: 'environment' } });
            v.srcObject = stream;
            camWrap.style.display = 'block';
            await v.play();
            var det = new BarcodeDetector({ formats: ['qr_code'] });
            var stop = function(){
              stream.getTracks().forEach(function(t){ t.stop(); });
              camWrap.style.display = 'none';
            };
            document.getElementById('camclose').onclick = stop;
            var loop = async function(){
              if (camWrap.style.display === 'none') { return; }
              try {
                var codes = await det.detect(v);
                if (codes.length) { stop(); send(codes[0].rawValue); return; }
              } catch(e) {}
              requestAnimationFrame(loop);
            };
            loop();
          } catch(e) { say('Camera not available', false); }
        }
        // Holds back the page's look-change reload (LookWatchScript) while a
        // driver is part way through: a name typed into the search, or the
        // camera open. (A box with the cursor in it - the PIN being typed -
        // counts as busy too; once entered, the PIN is saved and survives one.)
        function lookBusy(){
          return find.value.trim() !== '' || camWrap.style.display === 'block';
        }
        load(); setInterval(load, 4000);
        </script><script>{{LookWatchScript}}</script></body></html>
        """;
    }

    // Internal rather than private so the tests can read its script (BugHunt4WebTests).
    internal string BuildRemotePage()
    {
        var look = CurrentPageLook();

        return $$"""
        <!DOCTYPE html><html class="{{look.Scheme}}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="{{look.Scheme}}"><meta name="theme-color" content="{{look.ThemeColor}}">
        <title>Race Remote</title><style>{{look.Root}}{{SharedStyle}}
        .wrap { padding:16px; max-width:520px; margin:0 auto; }
        /* STAGE and COUNTDOWN in a plain button from the look - never its
           primary colour, which in some looks is green or red and would pass
           for a flag at a glance. The flag buttons keep their own flag
           colours whatever the look: those are signals, not decoration. */
        button { width:100%; padding:20px; margin:8px 0; font-size:19px; font-weight:700;
                 border:1px solid var(--border); border-radius:10px; color:var(--text); background:var(--raised);
                 cursor:pointer; font-family:inherit; text-align:center; line-height:1.2; }
        button:active { opacity:.7; }
        .green,.yellow,.red,.checkered,.reset { color:#fff; border-color:transparent; }
        .green{background:#1f9d55}.yellow{background:#c99700}.red{background:#cc2e4d}
        .checkered{background:#4a4a4a}.reset{background:#7a2233}
        .pin { width:100%; padding:14px; font-size:17px; border-radius:8px; border:1px solid var(--raised);
               background:var(--surface); color:var(--text); margin-bottom:10px; }
        /* What became of the last button pressed. Two lines are kept for it
           whether or not it has anything to say: the laptop's answer can run
           to two now, and a line that grew when the answer came in would move
           every button under the thumb about to press the next one. */
        .msg { display:flex; align-items:center; justify-content:center; text-align:center;
               padding:3px 4px; font-weight:700; line-height:1.2; min-height:calc(2.4em + 6px); }
        .msg .why { font-weight:400; }
        .grid2 { display:grid; grid-template-columns:1fr 1fr; gap:8px; }
        /* The between-race talk (31 Oct 2026): Skip, Hold Talk and Call Up,
           as the laptop's flag row has them after the flag. */
        .grid3 { display:grid; grid-template-columns:1fr 1fr 1fr; gap:8px; }
        .grid3 button { padding:16px 6px; font-size:16px; }
        /* Which race the buttons act on (27 Sep 2026). One line, always
           there - a line that appeared, or grew, would move every button
           under the thumb. A long name ends in an ellipsis instead. */
        .race { display:flex; align-items:baseline; gap:8px; padding:2px 2px 12px; min-height:1.4em; }
        .race .lbl { flex:0 0 auto; font-size:11px; font-weight:700; letter-spacing:.09em; color:var(--muted); }
        .race .name { flex:1 1 auto; min-width:0; font-size:18px; font-weight:700;
                      overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
        </style></head><body data-look="{{look.Fingerprint}}">
        <div class="bar"><h1>{{look.Logo}}RACE REMOTE</h1><span class="state" id="state">-</span></div>
        <div class="wrap">
          <div class="race"><span class="lbl">CONTROLLING</span><span class="name" id="race">-</span></div>
          <input class="pin" id="pin" placeholder="Remote PIN (set on the laptop)" inputmode="numeric">
          <div class="msg" id="msg" role="status" aria-live="polite"></div>
          <button onclick="go('stage')">STAGE</button>
          <button onclick="go('countdown')">COUNTDOWN</button>
          <button class="green" onclick="go('green')">GREEN - START</button>
          <div class="grid2">
            <button class="yellow" onclick="go('yellow')">YELLOW</button>
            <button class="red" onclick="go('red')">RED</button>
          </div>
          <button class="checkered" onclick="go('checkered')">CHECKERED</button>
          <button class="reset" onclick="if(confirm('Reset the race?'))go('reset')">RESET</button>
          <div class="race" style="padding-top:14px"><span class="lbl">BETWEEN RACES</span></div>
          <div class="grid3">
            <button onclick="go('skip')">SKIP</button>
            <button onclick="go('holdtalk')">HOLD TALK</button>
            <button onclick="go('callup')">CALL UP</button>
          </div>
          <table style="margin-top:18px"><thead><tr id="head"></tr></thead>
          <tbody id="rows"></tbody></table>
        </div>
        {{HeatCallerMark.PageFooterHtml}}
        <script>
        // Everything below builds rows with innerHTML, and a driver name is
        // whatever somebody typed into a profile. Without this, a driver
        // called "Ash <3 Racing" rendered as "Ash " - the rest swallowed as
        // a tag - and a name containing a script tag ran on every phone on
        // the club wifi and on the OBS overlay. The check-in page in this
        // same file uses textContent and was never exposed; these pages
        // build markup, so they escape instead.
        function esc(v){ return String(v==null?'':v).replace(/[&<>"']/g, function(c){
          return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]; }); }
        const pinBox = document.getElementById('pin');
        // Wrapped: storage throws outright in some private-browsing modes,
        // and an exception here stopped the whole script - every button on
        // the remote dead, with nothing on screen to say why. Its own key,
        // too: the check-in page must never be able to read this one.
        try { pinBox.value = localStorage.getItem('mzrd_remote_pin') || ''; } catch(e) {}
        pinBox.addEventListener('change', () => { try { localStorage.setItem('mzrd_remote_pin', pinBox.value); } catch(e) {} });
        // What the laptop said about the last button pressed. It used to say
        // "GREEN sent" as soon as the command reached the laptop, and a
        // command Race Control then refused - Stage during practice, a
        // checkered with no race running - still read as sent. The laptop now
        // waits for Race Control and answers with what it did: done (200),
        // refused (409 with "refused"), or sent but still waiting on the
        // laptop (202). Each press takes a number and only the latest may
        // write the line: an answer can take a few seconds, and a slow one
        // must not land over a newer press and describe the wrong button.
        let lastPress = 0;
        // A bold verdict and, after it, the laptop's reason in plain type:
        // "CHECKERED not done: No race is running.". Built with textContent,
        // like everything else the laptop says.
        function show(press, verdict, reason, tone){
          if (press !== lastPress) { return; }
          const msg = document.getElementById('msg');
          const line = document.createElement('span');
          line.textContent = verdict;
          if (reason) {
            const why = document.createElement('span');
            why.className = 'why';
            why.textContent = ' ' + reason;
            line.appendChild(why);
          }
          msg.textContent = '';
          msg.appendChild(line);
          msg.style.color = tone === 'good' ? 'var(--good)' : tone === 'bad' ? 'var(--bad)'
                          : tone === 'wait' ? 'var(--text)' : 'var(--muted)';
        }
        // Every press goes to the laptop as it is. Over a running race the
        // laptop answers with a question (409 with "confirm"), which is put
        // to whoever pressed, and on a yes the command goes again with
        // force=1. The page used to ask first, from the state it had last
        // polled - and during practice, which runs in the Racing state too,
        // it asked about a race that wasn't there, then the laptop refused.
        // The yes carries the race the question was about (raceGeneration),
        // so it can only abandon that race - if another has gone green since,
        // the laptop asks again.
        async function go(a, force, race){
          const name = ({ holdtalk: 'HOLD TALK', callup: 'CALL UP' })[a] || a.toUpperCase();
          const press = ++lastPress;
          show(press, 'Sending ' + name + '…', '', 'busy');
          let r, d = null;
          // Given up on after twelve seconds - the laptop answers within about
          // eight (BeginCommand and RunCommand) - so a press stuck behind a
          // dead connection says so, rather than "Sending" until the Wi-Fi
          // comes back and then going late.
          const stop = new AbortController(), timer = setTimeout(() => stop.abort(), 12000);
          try{
            r = await fetch('/api/command?action=' + a + (force ? '&force=1' : '')
                            + (force && typeof race === 'number' ? '&raceGeneration=' + race : ''),
                            { method: 'POST', headers: { 'X-HeatCaller': '1', 'X-Pin': pinBox.value }, signal: stop.signal });
          }catch(e){
            clearTimeout(timer);
            // It may have got there before the connection went.
            show(press, 'Connection lost.', 'Check the laptop to see whether ' + name + ' went.', 'bad');
            return;
          }
          try { d = await r.json(); } catch(e) {}
          clearTimeout(timer);
          if (!d) { show(press, 'No answer this page can read.', 'Check the laptop to see whether ' + name + ' went.', 'bad'); return; }
          // A race is running: ask, and send it again on a yes. A yes that
          // comes back with the question again is asked again only when the
          // question is about a different race from the one just agreed to -
          // a new race went green while the first question was open - so the
          // marshal gets to say yes or no about the race now on track. The
          // same race again can't happen from the laptop, but if it did,
          // asking again would only loop, so that falls through to the error
          // below. A question with no race number is never re-sent forced,
          // since a forced press with no number would abandon whatever race
          // is on track without anyone being asked about it.
          const askedAgain = force && typeof d.raceGeneration === 'number' && d.raceGeneration !== race;
          if (!d.ok && d.confirm && (!force || askedAgain)) {
            if (confirm((d.error || 'A race is running.') + ' Carry on?')) { go(a, true, d.raceGeneration); }
            else { show(press, name + ' canceled.', 'Nothing was changed.', 'busy'); }
            return;
          }
          if (d.ok && d.waiting) { show(press, name + ' sent:', d.message || 'waiting on the laptop.', 'wait'); }
          else if (d.ok) { show(press, d.message || (name + ' done.'), '', 'good'); }
          else if (d.refused) { show(press, name + ' not done:', d.error || '', 'bad'); }
          else { show(press, name + ':', d.error || 'Failed', 'bad'); }
        }
        // One poll at a time, and none left hanging, as on the live results
        // page: a new one every second and a half whether or not the last
        // came back used up the phone's connections to the laptop on weak
        // Wi-Fi, and a flag pressed then waited behind them.
        let ticking = false;
        async function tick(){
          if (ticking) return;
          ticking = true;
          const stop = new AbortController(), timer = setTimeout(() => stop.abort(), 3000);
          try{
            const r = await fetch('/api/state', { signal: stop.signal }); const d = await r.json();
            document.getElementById('state').textContent = d.state + ' - ' + d.flag;
            // As text, never markup: a race name is whatever was typed.
            const race = document.getElementById('race');
            race.textContent = d.race || '-';
            race.title = d.race || '';
            document.getElementById('head').innerHTML =
              (d.columns||[]).map((c,i) => `<th class="${i===0?'pos':''}">${esc(c)}</th>`).join('');
            document.getElementById('rows').innerHTML = d.standings.map(s =>
              '<tr>' + s.cells.map((c,i) => `<td class="${i===0?'pos':''}">${esc(c.text)}</td>`).join('') + '</tr>').join('');
          }catch(e){}
          finally { clearTimeout(timer); ticking = false; }
        }
        tick(); setInterval(tick, 1500);
        </script><script>{{LookWatchScript}}</script></body></html>
        """;
    }

    /// <summary>
    /// The race-day manager's page: five tabs (Today, Entries, Program,
    /// Race, Results) built entirely from RaceDayService's ops through the
    /// one /api/manage endpoint - see HandleManageAsync. This transport is
    /// always Wi-Fi, so race control always shows, but the script still
    /// asks hello rather than assuming, the same as a relay-served copy of
    /// this page would have to. See raceday-protocol.md for every op this
    /// script calls and what each answers.
    ///
    /// Internal rather than private so the tests can read its script
    /// (BugHunt4WebTests).
    /// </summary>
    internal string BuildManagePage()
    {
        var look = CurrentPageLook();

        return $$"""
        <!DOCTYPE html><html class="{{look.Scheme}}"><head>
        <meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <meta name="color-scheme" content="{{look.Scheme}}"><meta name="theme-color" content="{{look.ThemeColor}}">
        <title>Race Day Manager</title><style>{{look.Root}}{{SharedStyle}}
        .wrap { padding:14px 14px 60px; max-width:640px; margin:0 auto; }
        .pin { width:100%; padding:13px; font-size:17px; border-radius:8px; border:1px solid var(--raised);
               background:var(--surface); color:var(--text); margin-bottom:10px; }
        .find { width:100%; padding:13px; font-size:16px; border-radius:8px; border:1px solid var(--raised);
                background:var(--surface); color:var(--text); margin-bottom:10px; }
        /* The status line, as a toast pinned to the foot of the screen - over
           the page, never part of its layout. It used to be a line above the
           tabs: every message pushed the tab bar down (a long one by three
           lines), and one that came while a long list was scrolled was off
           the top of the screen where nobody saw it. It never takes a tap. */
        .msg { position:fixed; left:12px; right:12px; bottom:14px; z-index:5; max-width:560px; margin:0 auto;
               padding:11px 14px; border-radius:10px; background:var(--surface); border:1px solid var(--border);
               box-shadow:0 6px 22px rgba(0,0,0,.3); text-align:center; font-weight:700; font-size:15px;
               pointer-events:none; }
        .msg:empty { display:none; }

        .tabs { display:flex; background:var(--surface); border-bottom:1px solid var(--border);
                overflow-x:auto; position:sticky; top:0; z-index:2; }
        .navbtn { flex:1 0 auto; padding:13px 8px; background:none; border:none; color:var(--muted);
                  font-weight:700; font-size:11px; letter-spacing:.07em; text-transform:uppercase;
                  cursor:pointer; font-family:inherit; border-bottom:2px solid transparent; white-space:nowrap; }
        .navbtn.on { color:var(--primary); border-bottom-color:var(--primary); }

        .row { width:100%; display:flex; align-items:center; justify-content:space-between;
               gap:10px; padding:13px 14px; margin:7px 0; border:1px solid var(--raised);
               border-radius:10px; background:var(--surface); color:var(--text); text-align:left;
               font-family:inherit; cursor:pointer; }
        .row.static { cursor:default; }
        .row:not(.static):active { opacity:.7; }
        .row.on { background:var(--on-tint); border-color:var(--on-edge); }
        .lt { display:flex; flex-direction:column; gap:3px; min-width:0; }
        .nm { font-size:16px; font-weight:700; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
        .cl { font-size:12px; color:var(--muted); letter-spacing:.05em; }
        .rt { display:flex; align-items:center; gap:8px; flex:0 0 auto; }
        .packline { display:flex; align-items:center; justify-content:space-between; gap:10px;
                    margin:-3px 4px 9px; font-size:12px; color:var(--muted); letter-spacing:.05em; }
        .packline select { min-height:40px; max-width:60%; font-size:16px; }

        .chip { flex:0 0 auto; font-size:11px; font-weight:700; letter-spacing:.09em;
                padding:6px 10px; border-radius:20px; background:var(--raised); color:var(--muted);
                border:none; font-family:inherit; cursor:pointer; }
        .row.on .chip { background:var(--primary); color:var(--on-primary); }
        .chip.go { background:var(--primary); color:var(--on-primary); }
        .chip.status-onTrack { background:var(--bad); color:#fff; }
        .chip.status-next { background:var(--primary); color:var(--on-primary); }
        .chip.status-recorded { background:var(--good); color:#0a2e17; }
        /* A result row's winner: a small caption over the name, in plain
           text. It used to be a grey pill like a status chip, which read as
           a button - and with no winner it was an empty pill. */
        .win { display:flex; flex-direction:column; align-items:flex-end; gap:2px; min-width:0; text-align:right; }
        .win .wl { font-size:10px; font-weight:700; letter-spacing:.09em; text-transform:uppercase; color:var(--muted); }
        .win .wn { font-size:14px; font-weight:700; max-width:100%; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }

        .kv { display:flex; justify-content:space-between; align-items:baseline; gap:10px;
              padding:9px 0; border-bottom:1px solid var(--hair); font-size:15px; }
        .kv .lbl { color:var(--muted); font-size:11px; text-transform:uppercase; letter-spacing:.06em; }
        .kv b { font-weight:700; }
        .sub { font-size:12px; color:var(--muted); padding:0 0 9px; border-bottom:1px solid var(--hair); margin-bottom:4px; }
        .banner { margin:10px 0; padding:12px 14px; border-radius:10px; background:var(--on-tint); border:1px solid var(--on-edge); }
        .empty { padding:24px; text-align:center; color:var(--muted); }

        .btn { display:inline-block; padding:13px 16px; margin:4px 6px 4px 0; font-size:15px; font-weight:700;
               border:1px solid var(--border); border-radius:10px; color:var(--text); background:var(--raised);
               cursor:pointer; font-family:inherit; text-align:center; }
        .btn:active { opacity:.7; }
        .btn.small { padding:9px 14px; font-size:13px; margin:4px 6px 4px 0; }
        .btn.danger { background:#7a2233; color:#fff; border-color:transparent; }
        .btn.wide { display:block; width:100%; margin:8px 0; }
        .btn.green,.btn.yellow,.btn.red,.btn.checkered { color:#fff; border-color:transparent; }
        .btn.green { background:#1f9d55; } .btn.yellow { background:#c99700; }
        .btn.red { background:#cc2e4d; } .btn.checkered { background:#4a4a4a; }
        .grid2 { display:grid; grid-template-columns:1fr 1fr; gap:8px; }

        .rowActions { display:flex; gap:8px; margin-top:10px; flex-wrap:wrap; align-items:center; }
        /* The Program tab's toolbar: the class picker full width on a line of
           its own, and its two buttons sharing the line under it half and
           half, tall enough to hit with a thumb. They used to share one
           wrapping row, and on a phone Bump Ups fell onto a line by itself.
           16px in the picker, because iOS zooms the page into any smaller. */
        .progTools { display:grid; grid-template-columns:1fr 1fr; gap:8px; margin-top:10px; }
        .progTools select { grid-column:1 / -1; width:100%; min-height:44px; font-size:16px; }
        .progTools .btn { width:100%; min-height:44px; margin:0; }
        .detail { margin-top:14px; padding:14px; border:1px solid var(--border); border-radius:10px; background:var(--surface); }
        .detail h3 { margin:0 0 10px; font-size:16px; }
        .detail h4 { margin:14px 0 6px; font-size:11px; text-transform:uppercase; letter-spacing:.06em; color:var(--muted); }

        select { font-family:inherit; font-size:14px; padding:10px; border-radius:8px;
                 border:1px solid var(--raised); background:var(--surface); color:var(--text); }
        label.chk { display:flex; align-items:center; gap:8px; font-size:14px; color:var(--muted); margin:10px 0; }
        </style></head><body data-look="{{look.Fingerprint}}">
        <div class="bar"><h1>{{look.Logo}}MANAGE</h1></div>
        <div class="wrap" style="padding-bottom:0">
          <input class="pin" id="pin" type="password" placeholder="Manager PIN" inputmode="numeric" pattern="[0-9]*" autocomplete="off">
          <div class="msg" id="msg" role="status" aria-live="polite"></div>
        </div>
        <div class="tabs">
          <button class="navbtn on" id="nav-today" data-tab="today">Today</button>
          <button class="navbtn" id="nav-entries" data-tab="entries">Entries</button>
          <button class="navbtn" id="nav-program" data-tab="program">Program</button>
          <button class="navbtn" id="nav-race" data-tab="race">Race</button>
          <button class="navbtn" id="nav-results" data-tab="results">Results</button>
        </div>
        <div class="wrap">
          <div id="tab-today">
            <div id="todayBody"></div>
            <button class="btn wide" id="btnNewDay">New Race Day…</button>
            <button class="btn wide" id="btnBuildQualifying">Build Qualifying</button>
            <div class="sub" id="undoList">Nothing to undo.</div>
          </div>

          <div id="tab-entries" style="display:none">
            <input class="find" id="entrySearch" placeholder="Find a driver" autocomplete="off">
            <div id="entryList"></div>
            <button class="btn wide" id="btnAddDriver">Add Driver…</button>
          </div>

          <div id="tab-program" style="display:none">
            <div class="progTools">
              <select id="selClass"></select>
              <button class="btn small" id="btnSeedMains">Seed Mains</button>
              <button class="btn small" id="btnBumpUps">Bump Ups</button>
            </div>
            <div id="programList"></div>
            <div id="heatDetail" class="detail" style="display:none"></div>
          </div>

          <div id="tab-race" style="display:none">
            <div class="kv"><span class="lbl">State</span><b id="raceState">-</b></div>
            <button class="btn wide" id="btnStartNext">Start Next</button>
            <button class="btn wide" id="btnFlag-stage">STAGE</button>
            <button class="btn wide" id="btnFlag-countdown">COUNTDOWN</button>
            <button class="btn wide green" id="btnFlag-green">GREEN - START</button>
            <div class="grid2">
              <button class="btn yellow" id="btnFlag-yellow">YELLOW</button>
              <button class="btn red" id="btnFlag-red">RED</button>
            </div>
            <button class="btn wide checkered" id="btnFlag-checkered">CHECKERED</button>
            <button class="btn wide" id="btnReset">RESET</button>
          </div>

          <div id="tab-results" style="display:none">
            <label class="chk"><input type="checkbox" id="resultsTodayOnly" checked> Today's races only</label>
            <div id="resultsList"></div>
            <div id="resultDetail" class="detail" style="display:none"></div>
          </div>
        </div>
        {{HeatCallerMark.PageFooterHtml}}
        <script>
        function esc(v){ return String(v==null?'':v).replace(/[&<>"']/g, function(c){
          return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]; }); }
        var pinBox = document.getElementById('pin');
        try { pinBox.value = localStorage.getItem('mzrd_manage_pin') || ''; } catch(e) {}
        // Whether the laptop last turned the PIN down as wrong, so the line
        // goes on saying so once the box has been emptied - see sayFailure.
        var pinWasWrong = false;
        // Tried as soon as it has been typed (the box loses the cursor, or
        // Enter), rather than whenever the next poll came round. After Enter
        // the cursor is let go of too, which puts the keyboard away and lets
        // the tab fill in - neither happens while the box has it.
        pinBox.addEventListener('change', function(){
          if (document.activeElement === pinBox) { pinBox.blur(); }
          pinWasWrong = false;
          try { localStorage.setItem('mzrd_manage_pin', pinBox.value); } catch(e) {}
          loadHello().then(function(){ refreshActive(); });
        });
        // Empties the box and the copy this phone keeps. For a PIN the laptop
        // says is wrong, and for a manager the laptop has switched off: the
        // PIN is no use either way. A wrong one used to stay in the box, in
        // clear, and go out again with every poll - each one a wrong guess
        // to the laptop - until this phone had locked itself out.
        function forgetPin(){
          pinBox.value = '';
          try { localStorage.removeItem('mzrd_manage_pin'); } catch(e) {}
        }
        var msgBox = document.getElementById('msg');
        var msgTimer = null;
        // What the status line is showing: '' (nothing); 'done', a success,
        // which goes by itself after four seconds; 'error', something the
        // laptop turned down, which stays until the next action; or 'gate',
        // the laptop not reached at all (the PIN, the connection), which
        // stays while that lasts and goes as soon as an answer gets through.
        // A tab change clears any of them. The line used to keep whatever
        // it was last given for ever: a green "Checked in." still there on
        // Program and Results, and "Enter the manager PIN." still showing
        // after the PIN had been entered and the page had filled in.
        var msgKind = '';
        function say(text, good, kind){
          clearTimeout(msgTimer);
          msgTimer = null;
          msgBox.textContent = text || '';
          msgBox.style.color = good ? 'var(--good)' : 'var(--bad)';
          msgKind = text ? (kind || (good ? 'done' : 'error')) : '';
          if (text && good) { msgTimer = setTimeout(function(){ say(''); }, 4000); }
        }
        // A failed answer. Anything but a 200 is the laptop not reached
        // (a PIN, the connection, the laptop starting up); a 200 that says
        // no is the laptop turning the request down.
        function sayFailure(d, fallback){
          var text = d.error || fallback || 'Failed';
          if (d.needPin && pinWasWrong) { text = 'Wrong PIN. Enter the manager PIN again.'; }
          say(text, false, d._status === 200 ? 'error' : 'gate');
        }
        // How long a read that refreshes the page is waited for: past the ten
        // seconds the laptop takes to say it couldn't get to a request
        // (RaceDayService.AnswerTimeout).
        var READ_WAIT = 15000;
        // waitMs gives up on the request after that long - for those reads
        // only. Never for a change: one can take over a minute on the laptop,
        // and "Connection lost" while it went through would invite a second go.
        function call(op, args, confirmToken, waitMs){
          var body = { op: op, args: args || {}, confirm: confirmToken || null };
          // The PIN as it was sent: an answer about it is only acted on while
          // the box still holds it, never over a PIN typed in since.
          var sentPin = pinBox.value;
          var stop = new AbortController(), timer = waitMs ? setTimeout(function(){ stop.abort(); }, waitMs) : 0;
          return fetch('/api/manage', { method: 'POST', headers: { 'X-HeatCaller': '1', 'X-Pin': sentPin, 'Content-Type': 'application/json' }, body: JSON.stringify(body), signal: stop.signal })
            .then(function(r){ return r.json().then(function(d){ d._status = r.status; return d; }); })
            .catch(function(){ return { ok: false, error: 'Connection lost' }; })
            .then(function(d){ clearTimeout(timer); checkGate(d, sentPin); return d; });
        }
        function checkGate(d, sentPin){
          if (d._status === 200) {
            if (msgKind === 'gate') { say(''); }
            return;
          }
          // A 403 that is not "enter the PIN": the PIN was wrong (d.pin), or
          // the laptop has no usable manager PIN - the manager is switched
          // off there. See forgetPin.
          if (d._status === 403 && !d.needPin && sentPin !== '' && pinBox.value === sentPin) {
            forgetPin();
            pinWasWrong = !!d.pin;
          }
        }
        // The shared two-step confirm flow every Change/RaceControl op uses (see
        // raceday-protocol.md section 3): call once, and if the laptop asks a
        // question, show it with confirm() and call again with the token it gave.
        // A transport never needs to know in advance which ops ask - and args is
        // reused untouched on the second call, since the laptop binds the token to
        // the exact arguments it was asked about.
        function doOp(op, args, onDone){
          // A new action: whatever the line said about the last one is over.
          say('');
          call(op, args).then(function(d){ handleOpResult(op, args, d, onDone); });
        }
        function handleOpResult(op, args, d, onDone){
          if (d.needsConfirm) {
            if (confirm(d.message || 'Are you sure?')) {
              call(op, args, d.confirmToken).then(function(d2){ finishOp(d2, onDone); });
            }
            return;
          }
          finishOp(d, onDone);
        }
        function finishOp(d, onDone){
          if (!d.ok) { sayFailure(d); return; }
          say(d.message || 'Done', true);
          if (onDone) { onDone(d); }
        }
        function fieldBusy(){
          var a = document.activeElement;
          return !!(a && /^(INPUT|TEXTAREA|SELECT)$/.test(a.tagName));
        }

        // --- Tabs --------------------------------------------------------------
        var TABS = ['today', 'entries', 'program', 'race', 'results'];
        var active = 'today';
        var caps = { raceControl: true, allowResultEditing: false };
        function showTab(name){
          active = name;
          say('');
          TABS.forEach(function(t){
            var page = document.getElementById('tab-' + t);
            var nav = document.getElementById('nav-' + t);
            if (page) { page.style.display = (t === name) ? 'block' : 'none'; }
            if (nav) { nav.className = 'navbtn' + (t === name ? ' on' : ''); }
          });
          refreshActive();
        }
        function refreshActive(){
          if (fieldBusy()) { return; }
          if (active === 'today') { return refreshToday(); }
          else if (active === 'entries') { return refreshEntries(); }
          else if (active === 'program') { return refreshProgram(); }
          else if (active === 'race') { return refreshRace(); }
          else if (active === 'results') { return refreshResults(); }
        }
        document.querySelectorAll('.navbtn').forEach(function(btn){
          btn.addEventListener('click', function(){ showTab(btn.getAttribute('data-tab')); });
        });

        function loadHello(){
          return call('hello', {}, null, READ_WAIT).then(function(d){
            if (!d.ok) { sayFailure(d, 'Could not reach the laptop'); return d; }
            caps.raceControl = !!d.data.raceControl;
            caps.allowResultEditing = !!d.data.allowResultEditing;
            var navRace = document.getElementById('nav-race');
            if (navRace) { navRace.style.display = caps.raceControl ? '' : 'none'; }
            if (!caps.raceControl && active === 'race') { showTab('today'); }
            // Whether a given RESULT can be corrected is per-session (a demo or
            // practice never can) and is decided by results.get's own canEdit -
            // see renderResultDetail. caps.allowResultEditing is only the club-wide
            // switch, kept here so a future tab can read it without another round
            // trip.
            return d;
          });
        }

        // --- Today ---------------------------------------------------------------
        // Shape (RaceDayOpsToday.TodaySummary): { raceDay, classes:[{name,drivers,
        // checkedIn}], counts:{drivers,checkedIn,rounds,recorded}, race:{state,flag,
        // ...}, nextStep:{headline,detail,...}, nextRound, runSheetNext, undo,
        // allowResultEditing, busy }.
        function refreshToday(){
          return call('today.summary', {}, null, READ_WAIT).then(function(d){
            if (!d.ok) { sayFailure(d); return; }
            renderToday(d.data);
          });
        }
        function renderToday(d){
          var box = document.getElementById('todayBody');
          var day = d.raceDay;
          var rows = [];

          rows.push('<div class="kv"><span class="lbl">Race day</span><b>' + esc(day ? day.name : 'None open') + '</b></div>');
          rows.push('<div class="kv"><span class="lbl">Classes racing</span><b>'
            + esc(((day && day.classNames) || []).join(', ') || 'None') + '</b></div>');

          var checkedIn = 0, entered = 0, perClass = [];
          // Number() and esc() on counts too, as the relay's copy of this
          // page does: a string where a number was expected would otherwise
          // be markup, and += with one in it builds a string, not a sum.
          (d.classes || []).forEach(function(c){
            checkedIn += Number(c.checkedIn) || 0;
            entered += Number(c.drivers) || 0;
            perClass.push(c.name + ' ' + c.checkedIn + '/' + c.drivers);
          });
          rows.push('<div class="kv"><span class="lbl">Checked in</span><b>' + esc(checkedIn) + ' / ' + esc(entered) + '</b></div>');
          if (perClass.length > 1) {
            rows.push('<div class="sub">' + esc(perClass.join(', ')) + '</div>');
          }

          var counts = d.counts || {};
          rows.push('<div class="kv"><span class="lbl">Rounds recorded</span><b>' + esc(counts.recorded || 0) + ' / ' + esc(counts.rounds || 0) + '</b></div>');
          rows.push('<div class="kv"><span class="lbl">Laptop</span><b>' + (d.busy ? 'Busy with a question' : 'Ready') + '</b></div>');

          if (d.nextStep && (d.nextStep.headline || d.nextStep.detail)) {
            rows.push('<div class="banner"><b>' + esc(d.nextStep.headline || '') + '</b>'
              + (d.nextStep.detail ? '<br>' + esc(d.nextStep.detail) : '') + '</div>');
          }

          box.innerHTML = rows.join('');

          var undoBox = document.getElementById('undoList');
          var pending = d.undo || [];
          undoBox.textContent = pending.length ? ('Undo stack (on the laptop only): ' + pending.join(' • ')) : 'Nothing to undo.';
        }
        function newDay(){
          var name = prompt('Name for the new race day (blank = today\'s date):', '');
          if (name === null) { return; }
          doOp('day.create', { name: name }, refreshToday);
        }
        document.getElementById('btnNewDay').addEventListener('click', newDay);
        document.getElementById('btnBuildQualifying').addEventListener('click', function(){
          doOp('program.buildQualifying', {}, function(){ refreshToday(); if (active === 'program') { refreshProgram(); } });
        });

        // --- Entries ---------------------------------------------------------------
        var entriesData = [];
        function refreshEntries(){
          var q = document.getElementById('entrySearch').value.trim();
          return call('drivers.search', { query: q, limit: 200 }, null, READ_WAIT).then(function(d){
            if (!d.ok) { sayFailure(d); return; }
            entriesData = d.data.drivers || [];
            renderEntries();
          });
        }
        function renderEntries(){
          var list = document.getElementById('entryList');
          list.innerHTML = '';
          if (!entriesData.length) {
            list.innerHTML = '<div class="empty">No drivers match.</div>';
            return;
          }
          entriesData.forEach(function(row){
            var el = document.createElement('button');
            el.className = 'row' + (row.checkedIn ? ' on' : '');
            el.innerHTML =
              '<span class="lt"><span class="nm">' + esc(row.name) + '</span>' +
              '<span class="cl">' + esc(row.className || 'No class') + (row.car ? ' • ' + esc(row.car) : '') + '</span></span>' +
              '<span class="chip">' + (row.checkedIn ? 'HERE' : 'TAP') + '</span>';
            el.addEventListener('click', function(){ toggleCheckIn(row); });
            list.appendChild(el);
            addPackLine(list, row);
          });
        }
        // The pack in the driver's car - what each race they run is recorded
        // on. Only for a driver with packs to choose from (theirs or the
        // club's): driver.setPack, as Use This One on Driver Hub does.
        function addPackLine(list, row){
          var packs = row.packs || [];
          if (!packs.length) { return; }
          var line = document.createElement('div');
          line.className = 'packline';
          var caption = document.createElement('span');
          caption.textContent = 'Pack in the car';
          var sel = document.createElement('select');
          var opts = ['<option value="">None</option>'];
          packs.forEach(function(p){
            opts.push('<option value="' + esc(p.id) + '"' + (p.id === row.packId ? ' selected' : '') + '>'
              + esc(p.label) + (p.club ? ' (club)' : '') + '</option>');
          });
          sel.innerHTML = opts.join('');
          sel.value = row.packId || '';
          sel.addEventListener('change', function(){
            // doOp, less its confirm (this op never asks), so that a refusal
            // puts the list back on the pack that is really in the car.
            var args = { driverId: row.id, packId: sel.value };
            say('');
            call('driver.setPack', args).then(function(d){
              handleOpResult('driver.setPack', args, d, refreshEntries);
              if (!d.ok) { sel.value = row.packId || ''; }
            });
          });
          line.appendChild(caption);
          line.appendChild(sel);
          list.appendChild(line);
        }
        function toggleCheckIn(row){
          doOp('checkin.set', { driverId: row.id, checkedIn: !row.checkedIn }, refreshEntries);
        }
        document.getElementById('entrySearch').addEventListener('input', refreshEntries);
        document.getElementById('btnAddDriver').addEventListener('click', function(){
          var transponder = prompt('Transponder number:');
          if (!transponder) { return; }
          var name = prompt('Driver name (optional):', '') || '';
          var className = prompt('Class (optional):', '') || '';
          doOp('driver.add', { transponder: transponder, name: name, className: className }, refreshEntries);
        });

        // --- Program -----------------------------------------------------------------
        // Shape (RaceDayOpsProgram): program.list -> {classes:[string], rounds:[
        // RoundRow]}; RoundRow has {id,label,className,drivers,status}. program.heat
        // -> {round: RoundRow, locked, drivers:[in-heat], available:[not yet in]}.
        var programRounds = [];
        var programClasses = [];
        function refreshProgram(){
          return call('program.list', {}, null, READ_WAIT).then(function(d){
            if (!d.ok) { sayFailure(d); return; }
            programRounds = d.data.rounds || [];
            programClasses = d.data.classes || [];
            renderClassPicker();
            renderProgram();
          });
        }
        function renderClassPicker(){
          var sel = document.getElementById('selClass');
          var current = sel.value;
          sel.innerHTML = '';
          programClasses.forEach(function(name){
            var opt = document.createElement('option');
            opt.value = name; opt.textContent = name;
            sel.appendChild(opt);
          });
          if (programClasses.indexOf(current) >= 0) { sel.value = current; }
        }
        function statusWord(r){
          if (r.status === 'onTrack') { return 'ON TRACK'; }
          if (r.status === 'next') { return 'NEXT'; }
          if (r.status === 'recorded') { return 'DONE'; }
          return 'SCHEDULED';
        }
        function renderProgram(){
          var list = document.getElementById('programList');
          list.innerHTML = '';
          if (!programRounds.length) {
            list.innerHTML = '<div class="empty">Nothing on the program yet.</div>';
            return;
          }
          programRounds.forEach(function(r){
            var el = document.createElement('div');
            el.className = 'row roundRow';

            var left = document.createElement('span'); left.className = 'lt';
            var nm = document.createElement('span'); nm.className = 'nm'; nm.textContent = r.label;
            var cl = document.createElement('span'); cl.className = 'cl';
            cl.textContent = r.className + ' • ' + r.drivers + ' driver' + (r.drivers === 1 ? '' : 's');
            left.appendChild(nm); left.appendChild(cl);
            el.appendChild(left);

            var right = document.createElement('span'); right.className = 'rt';

            if (caps.raceControl && (r.status === 'scheduled' || r.status === 'next')) {
              var startBtn = document.createElement('button');
              startBtn.className = 'chip go'; startBtn.textContent = 'START';
              startBtn.addEventListener('click', function(ev){
                ev.stopPropagation();
                doOp('race.startRound', { roundId: r.id }, function(){
                  refreshProgram();
                  if (active === 'race') { refreshRace(); }
                });
              });
              right.appendChild(startBtn);
            }

            var chip = document.createElement('span');
            chip.className = 'chip status-' + r.status;
            chip.textContent = statusWord(r);
            right.appendChild(chip);

            el.appendChild(right);
            el.addEventListener('click', function(){ openHeat(r.id); });
            list.appendChild(el);
          });
        }
        function openHeat(roundId){
          call('program.heat', { roundId: roundId }).then(function(d){
            if (!d.ok) { sayFailure(d); return; }
            renderHeat(d.data);
          });
        }
        function driverLine(entry, extra){
          var row = document.createElement('div'); row.className = 'row static';
          var left = document.createElement('span'); left.className = 'lt';
          var nm = document.createElement('span'); nm.className = 'nm'; nm.textContent = entry.name;
          var cl = document.createElement('span'); cl.className = 'cl';
          cl.textContent = entry.checkedIn ? 'checked in' : 'not checked in';
          left.appendChild(nm); left.appendChild(cl);
          row.appendChild(left);
          if (extra) { row.appendChild(extra); }
          return row;
        }
        function renderHeat(d){
          var box = document.getElementById('heatDetail');
          box.innerHTML = '';

          var h3 = document.createElement('h3');
          h3.textContent = d.round.label + ' — ' + d.round.className;
          box.appendChild(h3);

          if (d.locked) {
            var lockedNote = document.createElement('div');
            lockedNote.className = 'sub';
            lockedNote.textContent = 'This round has been run, so its drivers are locked.';
            box.appendChild(lockedNote);
          }

          (d.drivers || []).forEach(function(entry){
            var btn = null;
            if (!d.locked) {
              btn = document.createElement('button');
              btn.className = 'chip'; btn.textContent = 'Remove';
              btn.addEventListener('click', function(){
                doOp('program.moveDriver', { driverId: entry.id, fromRoundId: d.round.id },
                  function(){ openHeat(d.round.id); refreshProgram(); });
              });
            }
            box.appendChild(driverLine(entry, btn));
          });

          if (!(d.drivers || []).length) {
            var noneRow = document.createElement('div'); noneRow.className = 'empty';
            noneRow.textContent = 'Nobody is in this heat yet.';
            box.appendChild(noneRow);
          }

          if (!d.locked && (d.available || []).length) {
            var h4 = document.createElement('h4'); h4.textContent = 'Add to this heat';
            box.appendChild(h4);

            d.available.forEach(function(entry){
              var btn = document.createElement('button');
              btn.className = 'chip go'; btn.textContent = 'Add';
              btn.addEventListener('click', function(){
                doOp('program.moveDriver', { driverId: entry.id, toRoundId: d.round.id },
                  function(){ openHeat(d.round.id); refreshProgram(); });
              });
              box.appendChild(driverLine(entry, btn));
            });
          }

          var actions = document.createElement('div'); actions.className = 'rowActions';

          var btnDelete = document.createElement('button');
          btnDelete.className = 'btn small danger'; btnDelete.textContent = 'Delete round';
          btnDelete.addEventListener('click', function(){ deleteRound(d.round.id); });
          actions.appendChild(btnDelete);

          var btnReorder = document.createElement('button');
          btnReorder.className = 'btn small'; btnReorder.textContent = 'Reorder…';
          btnReorder.addEventListener('click', function(){ showReorderPicker(d.round.id); });
          actions.appendChild(btnReorder);

          box.appendChild(actions);
          box.style.display = 'block';
        }
        function deleteRound(roundId){
          doOp('program.deleteRound', { roundId: roundId }, function(){
            document.getElementById('heatDetail').style.display = 'none';
            refreshProgram();
          });
        }
        function showReorderPicker(roundId){
          var round = programRounds.find(function(r){ return r.id === roundId; });
          if (!round) { return; }

          var others = programRounds.filter(function(r){ return r.className === round.className && r.id !== roundId; });

          var wrap = document.createElement('div'); wrap.className = 'rowActions';
          var sel = document.createElement('select');

          var optEnd = document.createElement('option');
          optEnd.value = ''; optEnd.textContent = 'End of the program';
          sel.appendChild(optEnd);

          others.forEach(function(r){
            var opt = document.createElement('option');
            opt.value = r.id; opt.textContent = 'Before ' + r.label;
            sel.appendChild(opt);
          });

          var go = document.createElement('button');
          go.className = 'btn small'; go.textContent = 'Move here';
          go.addEventListener('click', function(){
            doOp('program.moveRound', { roundId: roundId, beforeRoundId: sel.value || null }, function(){
              document.getElementById('heatDetail').style.display = 'none';
              refreshProgram();
            });
          });

          wrap.appendChild(sel);
          wrap.appendChild(go);
          document.getElementById('heatDetail').appendChild(wrap);
        }
        document.getElementById('btnSeedMains').addEventListener('click', function(){
          var className = document.getElementById('selClass').value;
          if (!className) { say('No class to seed mains for', false); return; }
          doOp('program.seedMains', { className: className }, refreshProgram);
        });
        document.getElementById('btnBumpUps').addEventListener('click', function(){
          var className = document.getElementById('selClass').value;
          if (!className) { say('No class to bump up', false); return; }
          doOp('program.bumpUps', { className: className }, refreshProgram);
        });

        // --- Race (Wi-Fi only) ----------------------------------------------------
        // today.summary's race block: {state, flag, raceGeneration, practice,
        // demo, roundId, roundLabel, className} - there is no separate "race
        // state" op.
        // The race last shown here, sent with every flag: a stage or countdown
        // that would abandon a running race is only taken as about THAT race,
        // and the laptop says to look again if another has taken its place.
        var shownRaceGeneration = null;
        function refreshRace(){
          return call('today.summary', {}, null, READ_WAIT).then(function(d){
            if (!d.ok) { return; }
            var race = d.data.race || {};
            shownRaceGeneration = typeof race.raceGeneration === 'number' ? race.raceGeneration : null;
            var label = (race.state || 'idle').toUpperCase() + (race.flag && race.flag !== 'none' ? ' – ' + race.flag.toUpperCase() : '');
            if (race.roundLabel) { label += '  (' + race.roundLabel + (race.className ? ', ' + race.className : '') + ')'; }
            if (d.data.busy) { label += '  • laptop busy'; }
            document.getElementById('raceState').textContent = label;
          });
        }
        function flag(name){
          var args = { flag: name };
          if (shownRaceGeneration !== null) { args.raceGeneration = shownRaceGeneration; }
          doOp('race.flag', args, refreshRace);
        }
        document.getElementById('btnStartNext').addEventListener('click', function(){
          doOp('race.startNext', {}, refreshRace);
        });
        ['stage', 'countdown', 'green', 'yellow', 'red', 'checkered'].forEach(function(name){
          var btn = document.getElementById('btnFlag-' + name);
          if (btn) { btn.addEventListener('click', function(){ flag(name); }); }
        });
        document.getElementById('btnReset').addEventListener('click', function(){
          if (confirm('Reset the race? The board is cleared.')) { flag('reset'); }
        });

        // --- Results -----------------------------------------------------------------
        // results.list -> {sessions:[SessionRow]}; SessionRow has {id,label,
        // roundLabel,className,corrected,winner}. results.get -> {results:[ResultRow],
        // canEdit}; ResultRow has {position,id,name,laps,outcome,addedLaps}.
        var resultsData = [];
        function refreshResults(){
          var todayOnly = document.getElementById('resultsTodayOnly').checked;
          return call('results.list', { todayOnly: todayOnly, limit: 50 }, null, READ_WAIT).then(function(d){
            if (!d.ok) { sayFailure(d); return; }
            resultsData = d.data.sessions || [];
            renderResults();
          });
        }
        function renderResults(){
          var list = document.getElementById('resultsList');
          list.innerHTML = '';
          if (!resultsData.length) {
            list.innerHTML = '<div class="empty">No results yet.</div>';
            return;
          }
          resultsData.forEach(function(s){
            var el = document.createElement('button');
            el.className = 'row';
            el.innerHTML =
              '<span class="lt"><span class="nm">' + esc(s.label || s.roundLabel || 'Race') + '</span>' +
              '<span class="cl">' + esc(s.className || '') + (s.corrected ? ' • corrected' : '') + '</span></span>' +
              (s.winner ? '<span class="win"><span class="wl">Winner</span><span class="wn">' + esc(s.winner) + '</span></span>' : '');
            el.addEventListener('click', function(){ openResult(s.id); });
            list.appendChild(el);
          });
        }
        var currentResult = null;
        function openResult(sessionId){
          call('results.get', { sessionId: sessionId }).then(function(d){
            if (!d.ok) { sayFailure(d); return; }
            currentResult = { id: sessionId, canEdit: d.data.canEdit, rows: d.data.results };
            renderResultDetail(d.data);
          });
        }
        function renderResultDetail(d){
          var box = document.getElementById('resultDetail');
          box.innerHTML = '';

          var h3 = document.createElement('h3'); h3.textContent = 'Result';
          box.appendChild(h3);

          (d.results || []).forEach(function(r){
            var row = document.createElement('div'); row.className = 'kv';
            var left = document.createElement('span');
            left.textContent = 'P' + r.position + ' ' + r.name;
            var right = document.createElement('b');
            right.textContent = r.laps + ' laps' + (r.outcome ? ' • ' + r.outcome : '');
            row.appendChild(left); row.appendChild(right);
            box.appendChild(row);
          });

          var actions = document.createElement('div'); actions.className = 'rowActions';

          var btnPublish = document.createElement('button');
          btnPublish.className = 'btn small'; btnPublish.textContent = 'Publish again';
          btnPublish.addEventListener('click', publishResult);
          actions.appendChild(btnPublish);

          if (d.canEdit) {
            var btnEdit = document.createElement('button');
            btnEdit.className = 'btn small'; btnEdit.textContent = 'Edit result…';
            btnEdit.addEventListener('click', openEditResult);
            actions.appendChild(btnEdit);
          }

          box.appendChild(actions);
          box.style.display = 'block';
        }
        function publishResult(){
          if (!currentResult) { return; }
          doOp('results.publish', { sessionId: currentResult.id });
        }
        function openEditResult(){
          if (!currentResult) { return; }
          var reason = prompt('Reason for the correction (required):', '');
          if (!reason) { return; }
          var changes = [];
          for (var i = 0; i < currentResult.rows.length; i++) {
            var r = currentResult.rows[i];
            // A total, as the laptop takes it (ResultAdjust). This used to
            // call 0 no change, and a 1 typed for one more lap was read as
            // the one lap the driver already had.
            //
            // A whole number only, asked again until it is one: parseInt read
            // "two" or a slip of the thumb as 0 and quietly took every added
            // lap away. Negative is allowed - laps taken off.
            var addedLaps = null;
            for (;;) {
              var typed = prompt('Total laps added for ' + r.name + ' (now ' + (r.addedLaps || 0) + '; 0 = none):', r.addedLaps || 0);
              if (typed === null) { return; }
              if (/^\s*-?[0-9]{1,4}\s*$/.test(typed)) { addedLaps = parseInt(typed, 10); break; }
              alert('"' + typed + '" is not a whole number of laps. Type a number such as 0, 1 or -1.');
            }
            var outcome = prompt('Outcome for ' + r.name + ' (blank = finished, or DNF / DQ):', r.outcome || '');
            if (outcome === null) { return; }
            // Only the rows that changed, so a row nobody touched is never
            // sent back as an edit of itself.
            if (addedLaps === (r.addedLaps || 0) && (outcome || '') === (r.outcome || '')) { continue; }
            changes.push({ driverId: r.id, addedLaps: addedLaps, outcome: outcome || '' });
          }
          if (changes.length === 0) { alert('Nothing was changed.'); return; }
          doOp('result.adjust', { sessionId: currentResult.id, reason: reason, changes: changes },
            function(){ openResult(currentResult.id); refreshResults(); });
        }
        document.getElementById('resultsTodayOnly').addEventListener('change', refreshResults);

        // --- Boot ------------------------------------------------------------------
        loadHello().then(function(){ refreshActive(); });
        // Never while the PIN box has the cursor: a half-typed PIN used to go
        // to the laptop with the next poll, and each was counted there as a
        // wrong guess. Hello first and the tab after it, as at the start, so
        // a PIN turned down by the one is not sent again by the other.
        //
        // And one poll at a time. A new one every four seconds whether or not
        // the last had come back piled up on weak Wi-Fi, used up the phone's
        // connections to the laptop, and a Race-tab flag then waited behind
        // them. A poll's reads give up after READ_WAIT, so a hung one holds
        // the next back only that long.
        var polling = false;
        setInterval(function(){
          if (document.activeElement === pinBox || polling) { return; }
          polling = true;
          loadHello().then(function(){ return refreshActive(); })
            .then(function(){ polling = false; }, function(){ polling = false; });
        }, 4000);
        function lookBusy(){
          return fieldBusy()
            || document.getElementById('heatDetail').style.display === 'block'
            || document.getElementById('resultDetail').style.display === 'block';
        }
        </script><script>{{LookWatchScript}}</script></body></html>
        """;
    }
}
