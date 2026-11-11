namespace HeatCaller;

/// <summary>What the automatic start between races is doing this second.</summary>
public enum AutoStartState
{
    /// <summary>Not running: switched off, no race just finished, or nothing next to start.</summary>
    Off,

    /// <summary>Counting down to the next race's start.</summary>
    Counting,

    /// <summary>The race director pressed Hold. The clock stands still until Resume.</summary>
    Held,

    /// <summary>Time's up, but the announcer is still talking (the recap show). Starts the moment it stops.</summary>
    WaitingForFloor,

    /// <summary>Time's up, and a car in the next race hasn't been heard by the loop. Waiting for the director to say start without it.</summary>
    WaitingForCars,

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
    private bool _started;

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
        _started = false;
        _startNow = false;

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
        _started = false;
    }

    /// <summary>When the race will start, or null when the gap isn't open. Includes the time held so far and any minutes pushed back.</summary>
    public DateTime? StartsAtUtc(DateTime nowUtc, int pushedBackMinutes = 0)
    {
        if (!IsOpen)
        {
            return null;
        }

        var held = _heldFor + (_heldSinceUtc is { } since ? nowUtc - since : TimeSpan.Zero);

        return _openedUtc + _gap + _rest + held + TimeSpan.FromMinutes(Math.Max(0, pushedBackMinutes));
    }

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

    /// <summary>
    /// What to do this second.
    /// </summary>
    /// <param name="nowUtc">Now.</param>
    /// <param name="floorBusy">The announcer is talking (the recap show): a start waits for it.</param>
    /// <param name="notSeen">Drivers in the next race the loop hasn't heard. Looked at only from <see cref="LookForCarsFrom"/> before the start.</param>
    /// <param name="pushedBackMinutes">Minutes the director pushed the race back (Call Up's +1 min).</param>
    public AutoStartState Step(DateTime nowUtc, bool floorBusy, IReadOnlyList<string> notSeen, int pushedBackMinutes = 0)
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
            return AutoStartState.Counting;
        }

        if (floorBusy)
        {
            return AutoStartState.WaitingForFloor;
        }

        if (NotSeen.Count > 0 && !_carsApproved)
        {
            return AutoStartState.WaitingForCars;
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
    public static string Words(AutoStartState state, TimeSpan? remaining, IReadOnlyList<string> notSeen, IReadOnlyList<string> resting)
    {
        var rest = resting.Count > 0 ? $" (rest for {Names(resting)})" : string.Empty;

        return state switch
        {
            AutoStartState.Counting when remaining is { } left => $"starts in {Clock(left)}{rest}",
            AutoStartState.Held => "start held - press Resume",
            AutoStartState.WaitingForFloor => "starts when the announcer finishes",
            AutoStartState.WaitingForCars => $"waiting for {Names(notSeen)} - not seen by the loop",
            AutoStartState.Start => "starting",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// The Big Screen's countdown band between races: "NEXT RACE IN 1:42",
    /// "NEXT RACE ON HOLD", "NEXT RACE SOON". Empty when the clock is off.
    /// A missing car is not put on the room's screen by name.
    /// </summary>
    public static string Band(AutoStartState state, TimeSpan? remaining) => state switch
    {
        AutoStartState.Counting when remaining is { } left => $"NEXT RACE IN {Clock(left)}",
        AutoStartState.Held => "NEXT RACE ON HOLD",
        AutoStartState.WaitingForFloor or AutoStartState.WaitingForCars or AutoStartState.Start => "NEXT RACE SOON",
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
    /// </summary>
    public static List<string> BackToBack(IEnumerable<string>? justRan, IEnumerable<string>? next)
    {
        if (justRan is null || next is null)
        {
            return new List<string>();
        }

        var ran = justRan.Where(t => !string.IsNullOrWhiteSpace(t)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return next
            .Where(t => !string.IsNullOrWhiteSpace(t) && ran.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Whether the automatic start is off tonight: switched off, or turned off for this night only.</summary>
    public static bool OffTonight(bool switchedOn, string? offForNight, string tonight) =>
        !switchedOn || string.Equals(offForNight, tonight, StringComparison.Ordinal);
}
