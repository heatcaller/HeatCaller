namespace HeatCaller;

/// <summary>
/// "1 lap", not "1 laps".
///
/// The app counts things constantly - laps, rounds, drivers, sessions,
/// penalties, heats - and every one of those counts can legitimately be one.
/// A two-lap heat, a season that counts a single round, the first session
/// on a new track, the one driver still to be inspected. Written straight
/// into an interpolated string the noun is frozen at whatever the common
/// case happened to be when the line was typed, so those cases read "1
/// laps" on the board and, in the voice lines, say it out loud.
///
/// The alternative pattern - "(s)" on the end - is not a fix. It reads as
/// a form nobody proof-read, which is exactly what a results screen on a
/// projector should not look like.
///
/// Two methods rather than one because the count is not always next to its
/// noun: a spin box carries the number and a separate caption carries the
/// word.
/// </summary>
internal static class Plural
{
    /// <summary>The count and its noun together: "3 laps", "1 lap".</summary>
    internal static string Of(long count, string singular, string? plural = null) =>
        $"{count} {Word(count, singular, plural)}";

    /// <summary>
    /// Just the noun, for when the number is displayed somewhere else.
    ///
    /// Pass <paramref name="plural"/> for anything that does not simply
    /// take an s - "class"/"classes", "penalty"/"penalties".
    ///
    /// Minus one is singular as well: "-1 lap", the way it is said.
    /// </summary>
    internal static string Word(long count, string singular, string? plural = null) =>
        count is 1 or -1 ? singular : plural ?? singular + "s";
}
