using System.Collections.Concurrent;
using System.Globalization;

namespace HeatCaller;

/// <summary>
/// A tone at the line for a driver being coached with "Tones instead of
/// words" (22 Oct 2026) - the coaching said in pitch rather than in words.
/// </summary>
public enum CoachTone
{
    /// <summary>Quicker than the target. A single high note.</summary>
    Faster,

    /// <summary>Within a twentieth of the target, or level at the split. A single middle note.</summary>
    OnTarget,

    /// <summary>Slower than the target, or than their best. A single low note.</summary>
    Slower,

    /// <summary>A new best lap of the session. Two notes, rising - the one shape that is never anything else.</summary>
    NewBest,
}

/// <summary>
/// THE TARGET LAP EACH DRIVER IS COACHED TO (22 Oct 2026), set on Race
/// Control's right-click (Coach > Target lap...).
///
/// Kept per driver for as long as HeatCaller is open - the night - and
/// gone when it closes. Not with the race: a target is a goal for the
/// evening's practice, set once and chased over several runs, and losing it
/// at every Reset would mean typing it again before every run. Not saved to
/// disk either: next week's target is next week's, and a stale one quietly
/// coaching a driver to last month's pace is worse than none.
///
/// Read by anything that shows a driver their target - the phone coach page
/// among them - through <see cref="TargetMs"/>.
/// </summary>
internal static class DriverTargets
{
    private static readonly ConcurrentDictionary<string, long> Targets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised (on whichever thread set it) when any driver's target is set or cleared.</summary>
    public static event Action? Changed;

    /// <summary>The target lap set for this driver tonight, in milliseconds, or null when there is none.</summary>
    public static long? TargetMs(string? transponder) =>
        !string.IsNullOrWhiteSpace(transponder) && Targets.TryGetValue(transponder, out var ms) ? ms : null;

    /// <summary>Every driver with a target tonight, by transponder.</summary>
    public static IReadOnlyDictionary<string, long> All() => new Dictionary<string, long>(Targets, StringComparer.OrdinalIgnoreCase);

    /// <summary>Sets a driver's target. Under a second is not a lap anybody races, and is refused.</summary>
    internal static bool Set(string transponder, long targetMs)
    {
        if (string.IsNullOrWhiteSpace(transponder) || targetMs < 1000)
        {
            return false;
        }

        Targets[transponder] = targetMs;
        Changed?.Invoke();
        return true;
    }

    /// <summary>Clears a driver's target. True when there was one.</summary>
    internal static bool Clear(string transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder) || !Targets.TryRemove(transponder, out _))
        {
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>Forgets every target - for the tests.</summary>
    internal static void ClearAll()
    {
        Targets.Clear();
        Changed?.Invoke();
    }
}

/// <summary>
/// The arithmetic and the words of the coaching (22 Oct 2026): the target
/// lap's "two tenths off", the consistency block's spread, and which tone a
/// lap earns. Pure, so the tests can hold every case to its words.
/// </summary>
internal static class CoachingWords
{
    /// <summary>Within this of the target is on it - the decoder is good to a few hundredths, and "off by nothing" is not a sentence.</summary>
    internal const long OnTargetMs = 50;

    /// <summary>
    /// A lap more than this many times the driver's best is a spin, a stop
    /// or a missed read - not pace to coach. The same half-again the split
    /// loop uses for a read it can believe (RaceControlControl.ToTheSplit).
    /// </summary>
    internal const double OffLapFactor = 1.5;

    /// <summary>
    /// "On target", "two tenths off", "a tenth under" - a lap against the
    /// target. Lower case, as a value; the line capitalises what it opens.
    /// </summary>
    internal static string PaceSpoken(long lapMs, long targetMs)
    {
        var delta = lapMs - targetMs;

        if (Math.Abs(delta) < OnTargetMs)
        {
            return "on target";
        }

        // WHY (8 Nov 2026): to the tenth PaceShown gives, not GapSpoken's
        // halves - "a second and a half off" under "+1.3s" was the voice
        // getting the gap wrong. See RaceNumbers.GapInTenths.
        var gap = RaceNumbers.GapInTenths(RaceNumbers.TenthsOf(delta));

        return delta > 0 ? $"{gap} off" : $"{gap} under";
    }

    /// <summary>
    /// The gap to the own-best ghost on track (22 Oct 2026): "two tenths
    /// behind your ghost", "a tenth ahead of your ghost", or "level with
    /// your ghost" inside a twentieth. <paramref name="gapMs"/> is positive
    /// when the driver is behind - see OwnBestGhost.GapToGhostMs.
    /// </summary>
    internal static string GhostGapSpoken(long gapMs)
    {
        if (Math.Abs(gapMs) < OnTargetMs)
        {
            return "level with your ghost";
        }

        // WHY (8 Nov 2026): to the tenth GhostGapShown gives - see PaceSpoken.
        var gap = RaceNumbers.GapInTenths(RaceNumbers.TenthsOf(gapMs));

        return gapMs > 0 ? $"{gap} behind your ghost" : $"{gap} ahead of your ghost";
    }

    /// <summary>The same, on a screen: "level", "+0.2s", "-0.1s" (behind is plus).</summary>
    // The spoken form's rounding (8 Nov 2026), so the two agree at every tenth.
    internal static string GhostGapShown(long gapMs) =>
        Math.Abs(gapMs) < OnTargetMs
            ? "level"
            : (gapMs > 0 ? "+" : "-") + RaceNumbers.TenthsShown(RaceNumbers.TenthsOf(gapMs));

    /// <summary>The same, on a screen: "on target", "+0.2s", "-0.1s".</summary>
    internal static string PaceShown(long lapMs, long targetMs)
    {
        var delta = lapMs - targetMs;

        if (Math.Abs(delta) < OnTargetMs)
        {
            return "on target";
        }

        // The spoken form's rounding (8 Nov 2026), so the two agree at every tenth.
        return (delta > 0 ? "+" : "-") + RaceNumbers.TenthsShown(RaceNumbers.TenthsOf(delta));
    }

    /// <summary>
    /// Whether a lap is one to coach on: a real time, and not half as long
    /// again as the driver's best (or, before they have one, the target).
    /// With nothing to measure it against, any real time is.
    /// </summary>
    internal static bool IsCleanLap(long lapMs, long referenceMs) =>
        lapMs > 0 && (referenceMs <= 0 || lapMs <= referenceMs * OffLapFactor);

    /// <summary>The laps to coach on, in order: real times, none half as long again as the best of them.</summary>
    internal static List<long> CleanLaps(IEnumerable<long> lapsMs)
    {
        var real = lapsMs.Where(ms => ms > 0).ToList();

        if (real.Count == 0)
        {
            return real;
        }

        var best = real.Min();

        return real.Where(ms => IsCleanLap(ms, best)).ToList();
    }

    /// <summary>The slowest of a block less the quickest - how far apart the laps were.</summary>
    internal static long Spread(IReadOnlyCollection<long> block) =>
        block.Count < 2 ? 0 : block.Max() - block.Min();

    /// <summary>
    /// The consistency line's words for a block of <paramref name="laps"/>
    /// clean laps <paramref name="spreadMs"/> apart, against the block
    /// before when there was one:
    ///
    ///   within a tenth and a half: "Last five within a tenth - that's your rhythm"
    ///   within four tenths:        "Last five within three tenths"
    ///   wider:                     "Spread's six tenths over the last five - settle it down"
    ///
    /// with ", tighter than the five before" or ", looser than the five
    /// before" when it moved by a tenth or more. Lower case first, as a
    /// value; the line capitalises it.
    /// </summary>
    internal static string ConsistencySpoken(int laps, long spreadMs, long? previousSpreadMs)
    {
        var count = RaceNumbers.Words(laps);
        var gap = SpreadSpoken(spreadMs);

        string said;

        if (spreadMs <= 150)
        {
            said = $"last {count} within {gap} - that's your rhythm";
        }
        else if (spreadMs <= 400)
        {
            said = $"last {count} within {gap}";
        }
        else
        {
            said = $"spread's {gap} over the last {count} - settle it down";
        }

        if (previousSpreadMs is { } before && Math.Abs(spreadMs - before) >= 100)
        {
            said += spreadMs < before ? $", tighter than the {count} before" : $", looser than the {count} before";
        }

        return said;
    }

    /// <summary>A spread as said: "a tenth" at the least, since "within nothing" is not something a driver can do.</summary>
    // WHY (8 Nov 2026): to the tenth SpreadShown gives, not GapSpoken's
    // halves past a second - see PaceSpoken.
    internal static string SpreadSpoken(long spreadMs) =>
        spreadMs < 100 ? "a tenth" : RaceNumbers.GapInTenths(RaceNumbers.TenthsOf(spreadMs));

    /// <summary>The spread on a screen: "0.3s".</summary>
    // The spoken form's rounding (8 Nov 2026).
    internal static string SpreadShown(long spreadMs) =>
        RaceNumbers.TenthsShown(RaceNumbers.TenthsOf(Math.Max(0, spreadMs)));

    /// <summary>
    /// The tone a lap earns: a new best first, whatever the target; then
    /// against the target when there is one - quicker, on it, or slower;
    /// then, without a target, level with or slower than their best (a lap
    /// quicker than the best IS a new best). Null when there is nothing to
    /// measure against yet, or the lap is one not to coach on.
    /// </summary>
    internal static CoachTone? ToneFor(long lapMs, long bestBeforeMs, long? targetMs)
    {
        var reference = targetMs ?? bestBeforeMs;

        if (lapMs <= 0 || reference <= 0 || !IsCleanLap(lapMs, bestBeforeMs > 0 ? bestBeforeMs : reference))
        {
            return null;
        }

        if (bestBeforeMs > 0 && lapMs < bestBeforeMs)
        {
            return CoachTone.NewBest;
        }

        var delta = lapMs - reference;

        if (Math.Abs(delta) < OnTargetMs)
        {
            return CoachTone.OnTarget;
        }

        return delta < 0 ? CoachTone.Faster : CoachTone.Slower;
    }

    /// <summary>The tone at the split, from the split coach's up-or-down: up, level, or down.</summary>
    internal static CoachTone ToneAtSplit(long deltaMs) =>
        Math.Abs(deltaMs) < OnTargetMs ? CoachTone.OnTarget : deltaMs < 0 ? CoachTone.Faster : CoachTone.Slower;

    /// <summary>
    /// Reads a target typed as seconds - "9.2", "9.20", "9,20" - into
    /// milliseconds, or null when it is not a lap time (under a second, or
    /// over ten minutes).
    /// </summary>
    internal static long? ParseTarget(string? text)
    {
        var cleaned = (text ?? string.Empty).Trim().TrimEnd('s', 'S').Trim().Replace(',', '.');

        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        var ms = (long)Math.Round(seconds * 1000m, MidpointRounding.AwayFromZero);

        return ms is >= 1000 and <= 600_000 ? ms : null;
    }
}
