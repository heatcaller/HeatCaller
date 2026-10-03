namespace HeatCaller;

/// <summary>
/// One pass over the split loop, as the app understands it.
/// </summary>
/// <param name="Transponder">The split transponder the split decoder read.</param>
/// <param name="Point">Which split loop.</param>
/// <param name="Driver">The driver it is fitted to (their main transponder), or null when it is nobody's.</param>
/// <param name="CarId">The car it is fitted to (Car.Id), or empty.</param>
/// <param name="AppMs">When the car crossed the split loop, on the app's clock (TimingClock), calibration applied.</param>
/// <param name="LineMs">
/// The same moment on the LINE decoder's clock - the clock the race's laps
/// are on - or null while the two clocks can't be related yet (an EasyLap
/// line whose heartbeat hasn't been heard for a few seconds).
/// </param>
/// <param name="RawLineMs">LineMs before the Line Up calibration (SplitLoopOffsetMs) - what Line Up measures from.</param>
public sealed record SplitPass(
    string Transponder,
    TimingPoint Point,
    string? Driver,
    string CarId,
    long AppMs,
    long? LineMs,
    long? RawLineMs);

/// <summary>
/// The split loop: every pass the split decoder reads comes here instead of
/// to the race. It works out whose transponder it was and when, on the line
/// decoder's clock, the car crossed - and keeps the last few minutes of
/// them, for Line Up and for adding a split transponder by driving over
/// the loop.
///
/// Nothing here can touch a lap. Laps, minimum lap time, jump starts and
/// the finish are the line decoder's alone (RaceControlSession); a split
/// loop that drops out, misreads or reads a stranger leaves the race exactly
/// as it would have been without it.
///
/// UI thread only (MainForm posts every pass here).
/// </summary>
public sealed class SplitLoop
{
    /// <summary>How many recent passes are kept.</summary>
    private const int Kept = 300;

    /// <summary>How long a transponder nobody has stays on the "seen at the split" list.</summary>
    private static readonly long UnknownKeptMs = 10 * 60_000;

    private readonly ProfileStore _profiles;
    private readonly List<SplitPass> _recent = new();
    private readonly Dictionary<string, long> _unknown = new(StringComparer.OrdinalIgnoreCase);

    public SplitLoop(ProfileStore profiles)
    {
        _profiles = profiles;
    }

    /// <summary>
    /// The split decoder's pass time on the app's clock (TimingClock): its
    /// own clock mapped across for an EasyLap, or when it arrived for an
    /// I-Lap, which times its passes that way. Null while that can't be
    /// known. Set by MainForm, which has the decoders.
    /// </summary>
    public Func<DirectLapEvent, long?>? AppTimeOf { get; set; }

    /// <summary>
    /// An app time (TimingClock) on the LINE decoder's clock - the identity
    /// for an I-Lap line, the clock map for an EasyLap one. Null while the
    /// two can't be related. Set by MainForm.
    /// </summary>
    public Func<long, long?>? LineTimeOf { get; set; }

    /// <summary>
    /// How much later the split loop reads a car than the line would at the
    /// same spot, in milliseconds - measured by Line Up and taken off every
    /// split time. Set by MainForm from the settings.
    /// </summary>
    public Func<int>? OffsetMs { get; set; }

    /// <summary>Raised for every pass, once it is understood.</summary>
    public event Action<SplitPass>? PassRecorded;

    /// <summary>For the console.</summary>
    public event Action<string>? Log;

    /// <summary>When the last pass arrived (TimingClock), or null before any has.</summary>
    public long? LastPassAt { get; private set; }

    /// <summary>The passes from the last few minutes, oldest first.</summary>
    public IReadOnlyList<SplitPass> Recent => _recent;

    /// <summary>Forgets the passes kept - for a new connection of the split decoder.</summary>
    public void Clear()
    {
        _recent.Clear();
        _unknown.Clear();
        LastPassAt = null;
    }

    /// <summary>
    /// The transponders read at the split in the last ten minutes that
    /// nobody has as a split transponder, the most recent first - for Add
    /// Split Transponder, so a car driven over the loop fills itself in.
    /// </summary>
    public IReadOnlyList<string> UnknownRecently(long nowMs)
    {
        foreach (var gone in _unknown.Where(u => nowMs - u.Value > UnknownKeptMs).Select(u => u.Key).ToList())
        {
            _unknown.Remove(gone);
        }

        // Taken since it was read: no longer unknown.
        foreach (var taken in _unknown.Keys.Where(id => _profiles.SplitOwnerOf(id) is not null).ToList())
        {
            _unknown.Remove(taken);
        }

        return _unknown.OrderByDescending(u => u.Value).Select(u => u.Key).ToList();
    }

    /// <summary>A pass from the split decoder. Returns what it was understood as.</summary>
    public SplitPass Record(DirectLapEvent pass)
    {
        var id = pass.TransponderId?.Trim() ?? string.Empty;
        var arrived = pass.ReceivedMs != 0 ? pass.ReceivedMs : TimingClock.NowMs;

        // When the split decoder's own time can't be placed yet (an EasyLap
        // whose clock is still being learned), neither can the line time:
        // when it merely arrived is late by the decoder's own delay, and
        // Line Up and the splits must never be given a guess.
        var appKnown = AppTimeOf is null ? arrived : AppTimeOf(pass);
        var app = appKnown ?? arrived;
        var offset = OffsetMs?.Invoke() ?? 0;
        var rawLine = appKnown is null ? null : LineTimeOf?.Invoke(app);

        var owner = _profiles.SplitOwnerOf(id);
        var point = pass.Point == TimingPoint.Line ? TimingPoint.Split1 : pass.Point;

        var understood = new SplitPass(
            id,
            point,
            owner?.Driver.Transponder,
            owner?.Entry.CarId ?? string.Empty,
            app - offset,
            rawLine - offset,
            rawLine);

        LastPassAt = arrived;
        _recent.Add(understood);

        if (_recent.Count > Kept)
        {
            _recent.RemoveRange(0, _recent.Count - Kept);
        }

        if (owner is null)
        {
            _unknown[id] = arrived;
        }

        Log?.Invoke(Describe(understood, owner?.Driver));
        PassRecorded?.Invoke(understood);
        return understood;
    }

    private static string Describe(SplitPass pass, DriverProfile? driver)
    {
        var who = driver is null
            ? "nobody's split transponder yet (add it on the driver's Transponders tab)"
            : string.IsNullOrWhiteSpace(driver.DriverName) ? driver.Transponder : driver.DriverName.Trim();

        var when = pass.LineMs is { } line
            ? $"line clock {line}ms"
            : "line clock not known yet";

        return $"[split] {SplitLoopNames.Of(pass.Point)}: {pass.Transponder} = {who}, {when}";
    }
}

/// <summary>What the split loops are called on screen and in the console.</summary>
public static class SplitLoopNames
{
    public static string Of(TimingPoint point) => point switch
    {
        TimingPoint.Line => "Start/finish",
        TimingPoint.Split1 => "Split 1",
        TimingPoint.Split2 => "Split 2",
        TimingPoint.Split3 => "Split 3",
        _ => point.ToString(),
    };
}
