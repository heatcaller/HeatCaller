namespace HeatCaller;

/// <summary>
/// WHAT THE DESK HEARS ABOUT A SWAP MADE ON PHONES (13 Nov 2026).
///
/// WHY: with "Marshal swaps from phones need the desk's OK" off, two drivers
/// agree a swap between themselves and the program follows. That is routine
/// and the director doesn't need interrupting for it - a beep per swap gets
/// ignored, and then the one that matters is missed too. What the director
/// needs is to know who is on each corner when they look up (Race Control's
/// marshal banner says "Sam for Max"), and to be told, unmissably, only when
/// a swap can leave a corner wrong:
///
///  - LATE: the race is on track, its marshals have been called or are being
///    checked, or it starts within <see cref="LateWithin"/>. The old marshal
///    may already be walking out, or the new one may not know where to go.
///  - FAILED: the asked driver said yes but the swap couldn't be made (the
///    race was re-seeded and they're driving in it now, say). Both drivers
///    think it's sorted; the original marshal is still on the post.
///
/// A swap made earlier than that goes in the log and the Answered list only.
///
/// Pure, so it is tested without a form.
/// </summary>
public static class MarshalSwapNotices
{
    /// <summary>A swap made this close to the start of its race is a late one.</summary>
    public static readonly TimeSpan LateWithin = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Why a swap for a race is late, as the end of a sentence ("with the race
    /// on track"), or null when it isn't. Checked in this order: on track,
    /// called, being checked, about to start. Only the next race can be any
    /// but the first.
    /// </summary>
    /// <param name="onTrack">The race is the one loaded and running now.</param>
    /// <param name="isNext">The race is the next one to start.</param>
    /// <param name="marshalsCalled">The announcer has already called its marshals to their posts.</param>
    /// <param name="marshalsBeingChecked">Race Control's "marshals in place?" ask is up for it.</param>
    /// <param name="untilStart">Time left on auto start's countdown to it, or null with no countdown.</param>
    public static string? Lateness(bool onTrack, bool isNext, bool marshalsCalled, bool marshalsBeingChecked, TimeSpan? untilStart)
    {
        if (onTrack)
        {
            return "with the race on track";
        }

        if (!isNext)
        {
            return null;
        }

        if (marshalsCalled)
        {
            return "after its marshals were called";
        }

        if (marshalsBeingChecked)
        {
            return "while its marshals were being checked";
        }

        if (untilStart is { } left && left <= LateWithin)
        {
            return left <= TimeSpan.Zero ? "at the start" : $"{Minutes(left)} before the start";
        }

        return null;
    }

    /// <summary>"Sam P. is covering Max P.'s post (Turn 3) in Q2 Heat 3" - what a swap did, for the log and a notice.</summary>
    public static string Made(string coverName, string ownerName, string post, string roundTitle) =>
        $"{Name(coverName)} is covering {Possessive(Name(ownerName))} post{PostPart(post)} in {roundTitle}";

    /// <summary>The Race Control notice for a late swap: "Late marshal swap: Sam P. is covering ... - made 1 minute before the start. Make sure Sam P. knows."</summary>
    public static string LateNotice(string made, string lateness, string coverName) =>
        $"Late marshal swap: {made} - made {lateness}. Make sure {Name(coverName)} knows where to go.";

    /// <summary>
    /// The Race Control notice for a yes that couldn't be made: "Marshal swap
    /// didn't go through: Sam P. said yes to covering Max P. (Turn 3) in Q2
    /// Heat 3, but Q2 Heat 3 has already run. Max P. is still on the post."
    /// </summary>
    public static string FailedNotice(string coverName, string ownerName, string post, string roundTitle, string why)
    {
        var reason = (why ?? string.Empty).Trim().TrimEnd('.');

        return $"Marshal swap didn't go through: {Name(coverName)} said yes to covering {Name(ownerName)}{PostPart(post)} in {roundTitle}, "
               + $"but {(reason.Length == 0 ? "it couldn't be made" : LowerFirst(reason))}. {Name(ownerName)} is still on the post.";
    }

    /// <summary>"Sam for Max" - a covered post's marshal on the banner; the plain name when nobody is covered.</summary>
    public static string BannerName(string name, string? coveringFor) =>
        string.IsNullOrWhiteSpace(coveringFor) ? name : $"{name} for {coveringFor.Trim()}";

    private static string Name(string name) => string.IsNullOrWhiteSpace(name) ? "a driver" : name.Trim();

    private static string Possessive(string name) => name.EndsWith('s') ? name + "'" : name + "'s";

    private static string PostPart(string post) => string.IsNullOrWhiteSpace(post) ? string.Empty : $" ({post.Trim()})";

    private static string LowerFirst(string text) =>
        // "That race isn't..." reads on as "that race isn't..."; "Q2 Heat 3
        // has already run" and "HeatCaller doesn't..." keep their capitals.
        text.Length > 1 && char.IsUpper(text[0]) && char.IsLower(text[1]) && !text.StartsWith("HeatCaller", StringComparison.Ordinal)
            ? char.ToLowerInvariant(text[0]) + text[1..]
            : text;

    private static string Minutes(TimeSpan left)
    {
        var seconds = (int)Math.Ceiling(left.TotalSeconds);

        if (seconds < 60)
        {
            return seconds == 1 ? "1 second" : $"{seconds} seconds";
        }

        var minutes = (int)Math.Ceiling(left.TotalMinutes);
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }
}
