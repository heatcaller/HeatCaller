namespace HeatCaller;

/// <summary>What the automatic start between races is doing this second.</summary>
public enum AutoStartState
{
    /// <summary>Not running: switched off, no race just finished, or nothing next to start.</summary>
    Off,

    /// <summary>Counting down to the next race's start.</summary>
    Counting,

    /// <summary>
    /// Counting down, but to the time drivers were given less two minutes
    /// rather than the gap (12 Nov 2026, setup plan step 8 - StartPromise):
    /// the night is running early, and the race waits for its time. Start
    /// now still starts it.
    /// </summary>
    WaitingForTime,

    /// <summary>The race director pressed Hold. The clock stands still until Resume.</summary>
    Held,

    /// <summary>Time's up, but the announcer is still talking (the recap show). Starts the moment it stops.</summary>
    WaitingForFloor,

    /// <summary>Time's up, and a car in the next race hasn't been heard by the loop. Waiting for the director to say start without it.</summary>
    WaitingForCars,

    /// <summary>
    /// Time's up, the club waits for its marshals (12 Nov 2026, setup plan D4),
    /// and nobody has said they're in place yet. Waiting for Marshals in place.
    /// </summary>
    WaitingForMarshals,

    /// <summary>Start the next race now. Given once per gap.</summary>
    Start,
}

/// <summary>
/// THE AUTOMATIC START BETWEEN RACES (11 Nov 2026, race-night build plan,
/// Phase 1 - "keep the night moving").
///
/// WHY: dead time between heats is the biggest complaint about a race night,
/// and every serious rival (LiveTime's Time Table, RCM, Everlaps' Auto panel)
/// counts down a fixed gap after each race and starts the next one by
/// itself. HeatCaller called the next race up but still waited for somebody
/// to press Start Next.
///
/// THE RULE, as decided on 11 Nov 2026:
///  - On by default once there is a program. After each race's checkered
///    flag the clock counts down the gap (2:30 unless set otherwise), plus
///    any minutes pushed back (+1 min), plus a rest for a driver who raced
///    the race just run (<see cref="RestForBackToBack"/>).
///  - The call-ups and countdown marks are timed off this start, so the room
///    hears "one minute" a minute before the start rather than before a run
///    sheet time that may not exist.
///  - At zero, a car in the next race that the loop hasn't heard holds the
///    start until the race director approves starting without it. A car
///    turning up in the meantime clears it by itself.
///  - Hold stops the clock; Resume carries on from where it stood. Start now
///    starts at once. "Off for tonight" stops it until tomorrow's racing.
///  - Never while the announcer is mid-recap: it waits for the floor.
///
/// Pure (no timers, no window), so the whole of it is tested without a
/// race. Race Control ticks it once a second from the gap's own tick.
/// </summary>
public sealed class AutoStartClock
{
    /// <summary>The gap when nobody has chosen one: the middle of ROAR's 2-5 minutes, and LiveRC's own advice.</summary>
    public static readonly TimeSpan DefaultGap = TimeSpan.FromSeconds(150);

    /// <summary>The extra wait before a race with a driver who raced the race just run.</summary>
    public static readonly TimeSpan RestForBackToBack = TimeSpan.FromMinutes(2);

    /// <summary>How long before the start the cars are looked for, so a missing one can be called in time.</summary>
    public static readonly TimeSpan LookForCarsFrom = TimeSpan.FromSeconds(45);

    private string? _gapKey;
    private DateTime _openedUtc;
    private TimeSpan _gap;
    private TimeSpan _rest;
    private TimeSpan _heldFor;
    private DateTime? _heldSinceUtc;
    private bool _startNow;
    private bool _carsApproved;
    private bool _marshalsConfirmed;
    private bool _started;
    private DateTime? _notBeforeUtc;

    /// <summary>
    /// The earliest the race may start (12 Nov 2026, setup plan step 8): its
    /// promised time less StartPromise.Early, or null for no such limit.
    /// </summary>
    public DateTime? NotBeforeUtc => _notBeforeUtc;

    /// <summary>
    /// Sets the earliest the race may start - see StartPromise.NotBefore.
    /// Race Control sets it every second, so a promise made or a setting
    /// changed mid-gap takes effect at once. Null lifts it.
    /// </summary>
    public void NotBefore(DateTime? utc)
    {
        if (IsOpen)
        {
            _notBeforeUtc = utc;
        }
    }

    /// <summary>How long before the start the marshals can be said to be in place, so a confirmed race never stops at zero.</summary>
    public static readonly TimeSpan AskForMarshalsFrom = TimeSpan.FromSeconds(60);

    /// <summary>True once somebody has said the next race's marshals are in place.</summary>
    public bool MarshalsConfirmed => _marshalsConfirmed;

    /// <summary>
    /// Marshals who said from their phone they're at their post (12 Nov 2026,
    /// setup plan step 8), by round. Not cleared by <see cref="Open"/> or
    /// <see cref="Close"/>: a marshal says so before the gap opens.
    /// </summary>
    public MarshalPosts Posts { get; } = new();

    /// <summary>The next race this gap is counting down to, or null when it isn't open.</summary>
    public string? RoundId { get; private set; }

    /// <summary>Drivers in the next race the loop hasn't heard, as last given to <see cref="Step"/>.</summary>
    public IReadOnlyList<string> NotSeen { get; private set; } = Array.Empty<string>();

    /// <summary>Who the rest is for, when there is one: the drivers who raced the race just run.</summary>
    public IReadOnlyList<string> Resting { get; private set; } = Array.Empty<string>();

    /// <summary>True while Hold is on.</summary>
    public bool IsHeld => _heldSinceUtc is not null;

    /// <summary>True once the director has said start without the cars not seen.</summary>
    public bool CarsApproved => _carsApproved;

    /// <summary>True while a gap is being counted.</summary>
    public bool IsOpen => _gapKey is not null;

    /// <summary>
    /// Opens the gap after a race, or keeps the one open. <paramref name="gapKey"/>
    /// is the finished race (its session generation); a new key, or a
    /// different next race, starts the count again from
    /// <paramref name="finishedUtc"/>.
    /// </summary>
    /// <param name="gapKey">Which race just finished.</param>
    /// <param name="roundId">The race the count is for.</param>
    /// <param name="finishedUtc">When the gap started: the checkered flag.</param>
    /// <param name="gap">The gap to count.</param>
    /// <param name="resting">Drivers in the next race who raced the race just run: they get <see cref="RestForBackToBack"/>.</param>
    public void Open(string gapKey, string roundId, DateTime finishedUtc, TimeSpan gap, IReadOnlyList<string>? resting = null)
    {
        if (_gapKey == gapKey && RoundId == roundId)
        {
            _gap = gap;
            return;
        }

        var sameGap = _gapKey == gapKey;

        _gapKey = gapKey;
        RoundId = roundId;
        _gap = gap;
        Resting = resting ?? Array.Empty<string>();
        _rest = Resting.Count > 0 ? RestForBackToBack : TimeSpan.Zero;
        NotSeen = Array.Empty<string>();
        _carsApproved = false;
        _marshalsConfirmed = false;
        _started = false;
        _startNow = false;
        _notBeforeUtc = null;

        // A different next race in the same gap (the director moved the
        // order) keeps the clock where it was and any hold: only a new gap
        // starts from the flag again.
        if (!sameGap)
        {
            _openedUtc = finishedUtc;
            _heldFor = TimeSpan.Zero;
            _heldSinceUtc = null;
        }
    }

    /// <summary>Closes the gap: a race went out (by any route), or the night moved on.</summary>
    public void Close()
    {
        _gapKey = null;
        RoundId = null;
        NotSeen = Array.Empty<string>();
        Resting = Array.Empty<string>();
        _heldSinceUtc = null;
        _heldFor = TimeSpan.Zero;
        _startNow = false;
        _carsApproved = false;
        _marshalsConfirmed = false;
        _started = false;
        _notBeforeUtc = null;
    }

    /// <summary>
    /// When the race will start, or null when the gap isn't open. Includes
    /// the time held so far and any minutes pushed back - and never before
    /// <see cref="NotBeforeUtc"/>, the time drivers were given less two
    /// minutes, so the call-ups and the countdown marks wait with it.
    /// </summary>
    public DateTime? StartsAtUtc(DateTime nowUtc, int pushedBackMinutes = 0)
    {
        if (GapStartsAtUtc(nowUtc, pushedBackMinutes) is not { } gapStart)
        {
            return null;
        }

        return _notBeforeUtc is { } notBefore && notBefore > gapStart && !_startNow ? notBefore : gapStart;
    }

    /// <summary>When the gap alone would start the race - the flag, the gap, the rest, the hold and the +1 mins - or null when it isn't open.</summary>
    public DateTime? GapStartsAtUtc(DateTime nowUtc, int pushedBackMinutes = 0)
    {
        if (!IsOpen)
        {
            return null;
        }

        var held = _heldFor + (_heldSinceUtc is { } since ? nowUtc - since : TimeSpan.Zero);

        return _openedUtc + _gap + _rest + held + TimeSpan.FromMinutes(Math.Max(0, pushedBackMinutes));
    }

    /// <summary>
    /// How much longer than the gap the race waits for its time (12 Nov
    /// 2026, setup plan step 8) - zero when the gap is the longer, or there
    /// is no promise. For the gap's log (GapNoteKind.WaitedForPromise).
    /// </summary>
    public TimeSpan WaitForTime(DateTime nowUtc, int pushedBackMinutes = 0) =>
        StartsAtUtc(nowUtc, pushedBackMinutes) is { } at && GapStartsAtUtc(nowUtc, pushedBackMinutes) is { } gap && at > gap
            ? at - gap
            : TimeSpan.Zero;

    /// <summary>How long to the start, never below zero, or null when the gap isn't open.</summary>
    public TimeSpan? Remaining(DateTime nowUtc, int pushedBackMinutes = 0) =>
        StartsAtUtc(nowUtc, pushedBackMinutes) is { } at
            ? (at > nowUtc ? at - nowUtc : TimeSpan.Zero)
            : null;

    /// <summary>Hold: the clock stands still until <see cref="Resume"/>.</summary>
    public void Hold(DateTime nowUtc)
    {
        if (IsOpen && _heldSinceUtc is null)
        {
            _heldSinceUtc = nowUtc;
        }
    }

    /// <summary>Carries on from where Hold stopped it.</summary>
    public void Resume(DateTime nowUtc)
    {
        if (_heldSinceUtc is { } since)
        {
            _heldFor += nowUtc - since;
            _heldSinceUtc = null;
        }
    }

    /// <summary>Start as soon as the floor is free and the cars allow, whatever the clock says.</summary>
    public void StartNow()
    {
        if (IsOpen)
        {
            _startNow = true;
            _heldSinceUtc = null;
        }
    }

    /// <summary>The director said start without the cars not seen.</summary>
    public void ApproveMissingCars()
    {
        if (IsOpen)
        {
            _carsApproved = true;
        }
    }

    /// <summary>The marshals are in place for the next race (setup plan D4), from the banner.</summary>
    public void ConfirmMarshals()
    {
        if (IsOpen)
        {
            _marshalsConfirmed = true;
        }
    }

    /// <summary>
    /// What to do this second.
    /// </summary>
    /// <param name="nowUtc">Now.</param>
    /// <param name="floorBusy">The announcer is talking (the recap show): a start waits for it.</param>
    /// <param name="notSeen">Drivers in the next race the loop hasn't heard. Looked at only from <see cref="LookForCarsFrom"/> before the start.</param>
    /// <param name="pushedBackMinutes">Minutes the director pushed the race back (Call Up's +1 min).</param>
    /// <param name="waitForMarshals">
    /// The club waits for its marshals and the next race has some (setup plan
    /// D4): at zero, the start waits for <see cref="ConfirmMarshals"/>. Start
    /// now doesn't wait, because pressing it says the director has looked.
    /// </param>
    public AutoStartState Step(DateTime nowUtc, bool floorBusy, IReadOnlyList<string> notSeen, int pushedBackMinutes = 0, bool waitForMarshals = false)
    {
        if (!IsOpen || _started)
        {
            return AutoStartState.Off;
        }

        if (IsHeld)
        {
            return AutoStartState.Held;
        }

        var remaining = Remaining(nowUtc, pushedBackMinutes)!.Value;

        if (remaining <= LookForCarsFrom || _startNow)
        {
            NotSeen = notSeen;
        }

        if (remaining > TimeSpan.Zero && !_startNow)
        {
            return WaitForTime(nowUtc, pushedBackMinutes) > TimeSpan.Zero
                ? AutoStartState.WaitingForTime
                : AutoStartState.Counting;
        }

        if (floorBusy)
        {
            return AutoStartState.WaitingForFloor;
        }

        if (NotSeen.Count > 0 && !_carsApproved)
        {
            return AutoStartState.WaitingForCars;
        }

        if (waitForMarshals && !_marshalsConfirmed && !_startNow)
        {
            return AutoStartState.WaitingForMarshals;
        }

        _started = true;
        return AutoStartState.Start;
    }

    /// <summary>A gap as the Next Up bar shows it: "2:30", "0:05".</summary>
    public static string Clock(TimeSpan remaining)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    /// <summary>
    /// What the Next Up bar says after the next race's name, for each state:
    /// "starts in 1:42", "held - press Resume", "waiting for Frank's car".
    /// Empty when the clock is off.
    /// </summary>
    /// <param name="startsAtUtc">When it starts, for "waits for its time - starts at 20:40". Null leaves the time out.</param>
    public static string Words(AutoStartState state, TimeSpan? remaining, IReadOnlyList<string> notSeen, IReadOnlyList<string> resting, DateTime? startsAtUtc = null)
    {
        var rest = resting.Count > 0 ? $" (rest for {Names(resting)})" : string.Empty;

        return state switch
        {
            AutoStartState.Counting when remaining is { } left => $"starts in {Clock(left)}{rest}",
            AutoStartState.WaitingForTime when remaining is { } left => startsAtUtc is { } at
                ? $"waiting for its time - starts at {AppTime.TimeOfDay(at.ToLocalTime())} (in {Clock(left)}), the time drivers were given"
                : $"waiting for its time - starts in {Clock(left)}",
            AutoStartState.Held => "start held - press Resume",
            AutoStartState.WaitingForFloor => "starts when the announcer finishes",
            AutoStartState.WaitingForCars => $"waiting for {Names(notSeen)} - not seen by the loop",
            AutoStartState.WaitingForMarshals => "waiting for the marshals - press Marshals in place",
            AutoStartState.Start => "starting",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The Big Screen's countdown band between races: "NEXT RACE IN 1:42",
    /// "NEXT RACE ON HOLD", "NEXT RACE SOON". Empty when the clock is off.
    /// A missing car is not put on the room's screen by name. A race waiting
    /// for its time says the time (12 Nov 2026, setup plan step 8): "NEXT
    /// RACE AT 20:40", which is what the pits want to know.
    /// </summary>
    public static string Band(AutoStartState state, TimeSpan? remaining, DateTime? startsAtUtc = null) => state switch
    {
        AutoStartState.Counting when remaining is { } left => $"NEXT RACE IN {Clock(left)}",
        AutoStartState.WaitingForTime when startsAtUtc is { } at => $"NEXT RACE AT {AppTime.TimeOfDay(at.ToLocalTime())}",
        AutoStartState.WaitingForTime when remaining is { } left => $"NEXT RACE IN {Clock(left)}",
        AutoStartState.Held => "NEXT RACE ON HOLD",
        AutoStartState.WaitingForFloor or AutoStartState.WaitingForCars or AutoStartState.WaitingForMarshals or AutoStartState.Start => "NEXT RACE SOON",
        _ => string.Empty,
    };

    /// <summary>Names as said: "Frank", "Frank and Sam", "Frank, Sam and 2 more".</summary>
    public static string Names(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        3 => $"{names[0]}, {names[1]} and {names[2]}",
        _ => $"{names[0]}, {names[1]} and {names.Count - 2} more",
    };

    /// <summary>
    /// Drivers in the next race who raced the race just run, for the rest
    /// guard - by transponder, case-blind. Empty when either is unknown.
    /// <paramref name="mainOf"/> (ProfileStore.PrimaryForAlternate) matches
    /// a driver seated on their second car's transponder in one of the two
    /// races (bug hunt 20) - the two-class driver swapping cars is the one
    /// the rest is for. Returns the ids as the next race's grid has them.
    /// </summary>
    public static List<string> BackToBack(IEnumerable<string>? justRan, IEnumerable<string>? next, Func<string, string?>? mainOf = null)
    {
        if (justRan is null || next is null)
        {
            return new List<string>();
        }

        string Driver(string t) => mainOf?.Invoke(t.Trim()) ?? t.Trim();

        var ran = justRan.Where(t => !string.IsNullOrWhiteSpace(t)).Select(Driver).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return next
            .Where(t => !string.IsNullOrWhiteSpace(t) && ran.Contains(Driver(t)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Whether the automatic start is off tonight: switched off, or turned off for this night only.</summary>
    public static bool OffTonight(bool switchedOn, string? offForNight, string tonight) =>
        !switchedOn || string.Equals(offForNight, tonight, StringComparison.Ordinal);
}

/// <summary>
/// Marshals who said from their own phone that they're at their post (12 Nov
/// 2026, setup plan step 8), round by round.
///
/// WHY: Marshals in place (setup plan D4) was one button for the whole race -
/// the desk looking down the track and pressing it, or the deputy's remote.
/// The marshals themselves know first, and a desk that can read "2 of 3 in:
/// Hugh Turn, Rhoda Rage; waiting for Axel Grease" knows who to shout for
/// instead of counting heads. When every marshal of the next race has said
/// so, the start stops waiting by itself; the desk's Marshals in place still
/// opens it whatever the phones say.
///
/// Kept by round, not by gap: the "I'm at my post" button is on the phone
/// while the race before is still running, before Auto start has opened the
/// gap it would otherwise be kept in. Only the last <see cref="RoundsKept"/>
/// rounds are remembered - a night is a few dozen races.
/// </summary>
public sealed class MarshalPosts
{
    /// <summary>How many rounds' confirmations are kept, oldest forgotten first.</summary>
    public const int RoundsKept = 32;

    private readonly Dictionary<string, HashSet<string>> _in = new(StringComparer.Ordinal);
    private readonly List<string> _order = new();

    /// <summary>A marshal (by main transponder) is at their post for a round. True the first time.</summary>
    public bool In(string roundId, string marshal)
    {
        if (string.IsNullOrWhiteSpace(roundId) || string.IsNullOrWhiteSpace(marshal))
        {
            return false;
        }

        if (!_in.TryGetValue(roundId, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _in[roundId] = set;
            _order.Add(roundId);

            while (_order.Count > RoundsKept)
            {
                _in.Remove(_order[0]);
                _order.RemoveAt(0);
            }
        }

        return set.Add(marshal.Trim());
    }

    /// <summary>Whether this marshal has said they're at their post for this round.</summary>
    public bool IsIn(string? roundId, string? marshal) =>
        roundId is not null && marshal is not null && _in.TryGetValue(roundId, out var set) && set.Contains(marshal.Trim());

    /// <summary>For each marshal given, whether they're in - in the same order.</summary>
    public IReadOnlyList<bool> Arrived(string roundId, IReadOnlyList<string> marshals) =>
        marshals.Select(m => IsIn(roundId, m)).ToList();

    /// <summary>True when there are marshals and every one of them has said they're at their post.</summary>
    public bool AllIn(string roundId, IReadOnlyList<string> marshals) =>
        marshals.Count > 0 && marshals.All(m => IsIn(roundId, m));
}

/// <summary>
/// The next race's start as the phones count it down (12 Nov 2026, setup plan
/// step 8): <paramref name="AtUtc"/> while counting, and the state in one of
/// three words - "counting", "held" or "soon".
/// </summary>
public sealed record PhoneCountdown(DateTime? AtUtc, string State)
{
    /// <summary>
    /// What the Live and Schedule pages are told between races - the Big
    /// Screen band's three states (<see cref="AutoStartClock.Band"/>), with
    /// the start time instead of the time left, so a phone counts it down
    /// itself between two of the laptop's publishes. Null with the clock off.
    /// To the second, so the same count is the same document. A race waiting
    /// for the time drivers were given counts down to that time, as the band
    /// says it ("NEXT RACE AT 20:40").
    /// </summary>
    public static PhoneCountdown? For(AutoStartState state, DateTime? startsAtUtc) => state switch
    {
        AutoStartState.Counting or AutoStartState.WaitingForTime when startsAtUtc is { } at => new PhoneCountdown(
            new DateTime(at.Ticks - at.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc), "counting"),
        AutoStartState.Held => new PhoneCountdown(null, "held"),
        AutoStartState.WaitingForFloor or AutoStartState.WaitingForCars or AutoStartState.WaitingForMarshals or AutoStartState.Start
            => new PhoneCountdown(null, "soon"),
        _ => null,
    };

    /// <summary>A short key that changes exactly when the phones need telling again. Empty for none.</summary>
    public static string KeyOf(PhoneCountdown? countdown) =>
        countdown is null
            ? string.Empty
            : countdown.State + "|" + (countdown.AtUtc?.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
}
