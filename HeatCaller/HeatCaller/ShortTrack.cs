namespace HeatCaller;

/// <summary>
/// The warning for a short track (9 Nov 2026). Where laps average under 8
/// seconds the announcer can't keep up: a line takes two or three seconds
/// to say, so routine calls are skipped behind the one playing, a call can
/// be a lap old when it is heard, and the commentary can mix up lap times
/// that sit close together. Seen on the 5-6 s demo heat of 8 Nov 2026; the
/// 8 seconds is a margin over that. Timing and scoring are not affected.
///
/// The same limit is in the beta terms (section 1) and the user guide
/// (announcer/voice.md, "On a short track").
/// </summary>
internal static class ShortTrack
{
    /// <summary>Laps averaging under this many seconds get the warning.</summary>
    public const double UnderSeconds = 8.0;

    /// <summary>
    /// The lap a layout is judged by: the median of its real laps raced
    /// since its racing line last changed (RcpLapModel.Raced, demo races
    /// left out), or else the average typed on Track Layouts. Null for no
    /// layout, or one with neither.
    /// </summary>
    public static double? LapFor(TrackLayout? track, IEnumerable<SessionRecord>? saved)
    {
        if (track is null)
        {
            return null;
        }

        if (saved is not null && RcpLapModel.Raced(saved, track.Name, track.Design?.RouteChangedUtc) is { } raced)
        {
            return raced.Seconds;
        }

        return track.AverageLapSeconds is > 0 ? track.AverageLapSeconds : null;
    }

    /// <summary>Whether a lap is short enough to warn about.</summary>
    public static bool IsShort(double? lapSeconds) => lapSeconds is > 0 and < UnderSeconds;

    /// <summary>The warning for a lap, or null when it is long enough (or unknown).</summary>
    public static string? Warning(double? lapSeconds) =>
        IsShort(lapSeconds)
            ? $"Short laps ({lapSeconds!.Value:0.0}s here): under {UnderSeconds:0} seconds a lap, the announcer skips some calls and can fall a lap behind. Timing and results aren't affected."
            : null;

    /// <summary>The longest the warning can be, for a label that keeps its height.</summary>
    public static string LongestWarning => Warning(UnderSeconds - 0.05)!;
}
