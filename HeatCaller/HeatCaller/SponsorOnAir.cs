namespace HeatCaller;

/// <summary>
/// Whether a sponsor's spoken read is going out right now, and what the
/// crowd screen should show for the length of it.
///
/// WHY THE READ WAS THE ONE PART OF THE ROTATION WITH NOTHING TO LOOK AT
/// (12 Oct 2026, asked for 10 Oct)
///
/// A sponsor's slide is a picture. A sponsor's video is a picture that
/// moves. A sponsor's READ - the few sentences they paid to have said over
/// the PA - put its text on the announcement banner and left the crowd
/// screen on the standings, so the one part of the rotation that costs the
/// announcer airtime was the one part with no image behind it. Forty
/// seconds of a sponsor being talked about, over a leaderboard.
///
/// WHY A DEADLINE AND NOT A STOP SIGNAL
///
/// SpeakReminder is fire and forget: the words go on the voice queue and
/// nothing comes back when they are done. A picture waiting for a signal
/// that never arrives is a picture left on a crowd screen for the rest of
/// the night, which is worse than no feature. So the read's own spoken
/// length sets a deadline, and everything here is "has that passed yet".
/// <see cref="End"/> exists for the paths that DO know - a read abandoned
/// with the show, the window closing - and nothing depends on being
/// called.
///
/// WHY STATIC, like CommercialBreak and ShowFloor beside it: the Big
/// Screen is a window that may not be open, may be on another monitor, and
/// is built long before or long after the announcer. A read happening is a
/// fact about the night rather than a message between two objects, and
/// every other such fact in this app is held exactly here.
/// </summary>
internal static class SponsorOnAir
{
    /// <summary>
    /// How much longer than the words themselves the picture stays up.
    ///
    /// Two seconds. The spoken length is an estimate from a word count
    /// (see SponsorCopy.SpokenLength) and a voice that reads a shade
    /// slower than the estimate would otherwise finish its last sentence
    /// to a leaderboard. Erring long costs a sponsor nothing; erring short
    /// cuts their picture off mid-sentence.
    /// </summary>
    private static readonly TimeSpan Overhang = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The longest a read may hold the screen, whatever it was told.
    ///
    /// Forty seconds, against a read capped at twenty-five. A backstop
    /// against a spoken length worked out from copy that was somehow
    /// enormous - the one failure here that a crowd would actually see.
    /// </summary>
    private static readonly TimeSpan Longest = TimeSpan.FromSeconds(40);

    private static volatile string? _picture;
    private static volatile string _name = string.Empty;
    private static long _untilTicks;

    /// <summary>
    /// A read is starting: hold the screen for about as long as it takes
    /// to say.
    /// </summary>
    /// <param name="pictureFileName">The sponsor's picture, or null when they have none - see <see cref="SponsorName"/>.</param>
    /// <param name="sponsorName">Their name as the Sponsors page has it, which is what the screen shows when there is no picture.</param>
    /// <param name="spokenFor">Roughly how long the words run.</param>
    internal static void Begin(string? pictureFileName, string? sponsorName, TimeSpan spokenFor)
    {
        var hold = spokenFor + Overhang;

        if (hold > Longest)
        {
            hold = Longest;
        }

        if (hold <= TimeSpan.Zero)
        {
            return;
        }

        _picture = string.IsNullOrWhiteSpace(pictureFileName) ? null : pictureFileName;
        _name = (sponsorName ?? string.Empty).Trim();

        Interlocked.Exchange(ref _untilTicks, (DateTime.UtcNow + hold).Ticks);
    }

    /// <summary>
    /// Takes it off early - a read abandoned with its show, or the Big
    /// Screen closing. Safe to call when nothing is on.
    /// </summary>
    internal static void End() => Interlocked.Exchange(ref _untilTicks, 0);

    /// <summary>Whether a read is on air right now.</summary>
    internal static bool Showing => DateTime.UtcNow.Ticks < Interlocked.Read(ref _untilTicks);

    /// <summary>
    /// The picture to show, or null when the sponsor has none and the
    /// screen should show <see cref="SponsorName"/> instead.
    ///
    /// Null also when nothing is on air at all, so a caller that forgets
    /// to ask <see cref="Showing"/> first cannot put a stale picture up.
    /// </summary>
    internal static string? PictureFileName => Showing ? _picture : null;

    /// <summary>
    /// Whose read it is, as the Sponsors page spells it. Shown on the
    /// screen when they have no picture - which is the whole of the
    /// no-picture answer: a club that has not uploaded one still gets
    /// their sponsor's name in front of the room while it is being read
    /// out, rather than the standings.
    /// </summary>
    internal static string SponsorName => Showing ? _name : string.Empty;

    /// <summary>For tests, which must not inherit a deadline from the one before.</summary>
    internal static void ResetForTests()
    {
        _picture = null;
        _name = string.Empty;
        Interlocked.Exchange(ref _untilTicks, 0);
    }
}
