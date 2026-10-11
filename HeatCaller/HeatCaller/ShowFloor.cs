namespace HeatCaller;

/// <summary>
/// Who has the broadcast floor: one show at a time, across the whole app.
///
/// WHY THIS EXISTS
///
/// Two separate chains stage shows through the same ShowTreatment and the
/// same announcer, and until this existed nothing held them apart:
///
///   * Race Control's post-flag sequence - teaser, recap, honours, the
///     mechanic, the sign-off - which runs as one long fire-and-forget task
///     (see SpeakRaceSummary).
///   * The announcer screen's director, which asks for a segment twice a
///     second and runs whatever comes back as a block of its own (see
///     AnnouncerControl.SpeakNextSegmentAsync).
///
/// On the night of 11 Oct 2026 both ran at once. Race Control opened the
/// recap's show at 2:34:45 - opener, then an 87s bed looping under it - and
/// nine seconds later the director independently decided the night's
/// sign-off numbers were due and opened a SECOND show on top: another
/// opener, another bed, both summed by AudioBus, which mixes additively and
/// has no opinion about it. The hall heard two pieces of music over one
/// voice, and the programme signed off before the honours it had not read
/// out yet.
///
/// ShowOnAir could not prevent it. It is a reference COUNTER, written so
/// the club's music stays out of the way until the last show ends - two
/// shows count to two there quite happily, and its own comment says it
/// assumes "nothing runs two shows at once" rather than enforcing it. This
/// is the thing that enforces it.
///
/// WHY A CLAIM RATHER THAN A LOCK
///
/// The two chains are not peers. Race Control's sequence is the programme -
/// a race has just finished and the room is waiting to be told about it -
/// and it runs for minutes, across several awaits, with its own
/// cancellation when the next race is called. The director's segments are
/// filler for the gaps between races, and a segment not said is unspent:
/// skipping one costs the club nothing, where interrupting a recap costs it
/// the race. So the floor is CLAIMED for the length of a sequence and the
/// filler simply stands down while it is held, rather than either side
/// blocking on the other.
///
/// WHY A CEILING ON A CLAIM
///
/// The worst outcome in this area is a claim that is never released: the
/// director would be silent for the rest of the evening and nobody would
/// know why. So a claim older than <see cref="Longest"/> is treated as
/// gone, and the breaking of it is reported. That is the same bias
/// ShowOnAir takes with its count - when in doubt, let the audio back.
/// </summary>
internal static class ShowFloor
{
    /// <summary>Serialises the holder and the time it was taken together.</summary>
    private static readonly object Gate = new();

    /// <summary>
    /// How long a claim is honoured before it is assumed to have been
    /// dropped by something that failed without a finally.
    ///
    /// Race Control's sequence is the long one and it is bounded: the
    /// recap's own lines are waited on for at most RecapLineLimit each, the
    /// follow-ons for FollowOnWait, and the tail for a minute. Six minutes
    /// is past all of that with room to spare, and still short enough that
    /// a night does not lose its filler.
    /// </summary>
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(6);

    private static string? _who;
    private static DateTime _takenUtc;

    /// <summary>
    /// Reports a claim being broken because it went stale, for the
    /// developer console. A broken claim is a bug somewhere else, and this
    /// is the only place it can be noticed.
    ///
    /// A SETTABLE SINK rather than an event, which is the one thing about
    /// this class that is not copied from ShowOnAir. This is static state
    /// and Race Control is rebuilt whenever the window is - an event would
    /// collect a subscriber per rebuild and report a broken claim four
    /// times on a long evening. Assignment cannot.
    /// </summary>
    internal static Action<string>? Status { get; set; }

    /// <summary>
    /// Whether anything holds the floor right now. False once a claim has
    /// gone stale, which also reports it.
    /// </summary>
    internal static bool Busy => HeldBy is not null;

    /// <summary>
    /// What holds the floor, in words for the console - "the race recap
    /// sequence". Null when the floor is free.
    /// </summary>
    internal static string? HeldBy
    {
        get
        {
            lock (Gate)
            {
                if (_who is null)
                {
                    return null;
                }

                if (DateTime.UtcNow - _takenUtc < Longest)
                {
                    return _who;
                }

                var stale = _who;

                _who = null;

                // Outside nothing: this runs under Gate, and the handler is
                // a console line. Kept short for that reason - see
                // ShowOnAir.Gate, which this follows.
                Report($"[show] The broadcast floor was still claimed by {stale} after "
                    + $"{Longest.TotalMinutes:0} minutes, so it has been let go. "
                    + "Something ended without releasing it.");

                return null;
            }
        }
    }

    /// <summary>
    /// Takes the floor for <paramref name="who"/>, or answers false when
    /// something else already has it.
    ///
    /// Pair every true with <see cref="Release"/>, from a finally. A false
    /// is not a failure and needs no handling beyond not starting a show:
    /// the caller is the filler, and filler stands down.
    /// </summary>
    internal static bool TryClaim(string who)
    {
        // Read first, so a stale claim is cleared before this asks.
        _ = HeldBy;

        lock (Gate)
        {
            if (_who is not null)
            {
                return false;
            }

            _who = string.IsNullOrWhiteSpace(who) ? "a show" : who.Trim();
            _takenUtc = DateTime.UtcNow;

            return true;
        }
    }

    /// <summary>
    /// Gives the floor back. Safe to call twice, safe from any thread, and
    /// safe when something else has since taken it - only the holder's own
    /// release counts, so a stale claim that was broken and then re-taken
    /// is not released out from under its new owner.
    /// </summary>
    internal static void Release(string who)
    {
        lock (Gate)
        {
            if (_who is not null && string.Equals(_who, who?.Trim(), StringComparison.Ordinal))
            {
                _who = null;
            }
        }
    }

    private static void Report(string line)
    {
        try
        {
            Status?.Invoke(line);
        }
        catch
        {
            // A console line is not worth taking down whatever was starting
            // or ending a show - the same reason ShowOnAir swallows its own.
        }
    }

    /// <summary>
    /// Back to a free floor, for the test run. Static state outlives a
    /// test, and a test that claimed without releasing would silence the
    /// director in every test after it.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _who = null;
        }
    }
}
