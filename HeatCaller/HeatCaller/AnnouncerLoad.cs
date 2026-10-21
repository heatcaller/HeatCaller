namespace HeatCaller;

/// <summary>
/// How much the announcer tried to say this race, and how much of it got
/// out.
///
/// WHY THERE IS A COUNTER AT ALL (14 Oct 2026)
///
/// Asked for from the floor, in these words: "I didn't know how many are
/// being dropped as I don't have a visual."
///
/// The lines were there. Every dropped call has written itself to the
/// console since the drop-while-speaking rule was first put in -
/// "[voice] Dropped LapCompleted announcement - still speaking the
/// previous one" - and about twenty of them came out of one eight-lap demo
/// with three cars in it. Nobody reads the console on a race night. The
/// person it would have told is standing at a microphone watching Race
/// Control, and from there the announcer sounded busy, which is exactly
/// what it sounds like when it is throwing most of its work away.
///
/// So the count is kept here, where the strip and the Commentary page can
/// both read it, and the console lines stay as they are. A fault nobody
/// can see is a fault nobody reports, and this one went unreported through
/// every night the app has ever run.
///
/// WHAT THE FOUR NUMBERS MEAN, which matters because three of them are
/// nearly the same thing and are not:
///
///  - Offered is every routine call that reached the voice queue's gate.
///  - Accepted is the ones that were taken to be said.
///  - Held is the ones <see cref="AnnouncerDensity"/> kept back on purpose
///    to leave a gap. These are the GOOD kind of unsaid: the club asked
///    for them not to be said.
///  - Dropped is the ones lost because something was already being
///    spoken. These are the BAD kind, and the number the report above was
///    asking for.
///
/// Held and Dropped are reported separately for that reason. A club that
/// turns the pacing up and watches Held rise and Dropped fall is watching
/// the dial work. A club that sees Dropped rise is watching the announcer
/// run out of room, and the answer is a wider gap rather than a quieter
/// one - which is a different control from the chattiness dial, and the
/// whole confusion the report came out of.
///
/// ARITHMETIC THAT HAS TO ADD UP. Offered == Accepted + Held + Dropped,
/// always, and <see cref="Balances"/> is that sentence as a test. A
/// counter whose parts do not sum to its whole is worse than no counter:
/// the club reads it, believes it, and tunes a dial against a number that
/// is lying.
///
/// NOT WRITTEN DOWN AND NOT KEPT. It is per race, it is in memory, and a
/// restart loses it - the same reasoning NightPromises gives for itself.
/// Last race's load is a thing to glance at between races; a figure from
/// a race three weeks ago is a statistic, and the app does not need
/// another one.
/// </summary>
internal sealed class AnnouncerLoad
{
    private int _offered;
    private int _accepted;
    private int _held;
    private int _dropped;

    /// <summary>Every routine call that reached the gate this race.</summary>
    internal int Offered => Volatile.Read(ref _offered);

    /// <summary>The ones taken to be said. Not a promise they were heard - a flag can still clear the queue.</summary>
    internal int Accepted => Volatile.Read(ref _accepted);

    /// <summary>The ones the pacing dial held back on purpose.</summary>
    internal int Held => Volatile.Read(ref _held);

    /// <summary>The ones lost behind a line already being spoken.</summary>
    internal int Dropped => Volatile.Read(ref _dropped);

    /// <summary>
    /// Everything that was offered and not said, however it went. What the
    /// strip shows, because somebody glancing at Race Control wants one
    /// number rather than a breakdown - the breakdown is on the Commentary
    /// page.
    /// </summary>
    internal int Unsaid => Held + Dropped;

    /// <summary>
    /// Counted on every thread the timing loop and the voice queue run on,
    /// so every one of these is interlocked. The four Note methods are the
    /// only way the numbers move.
    /// </summary>
    internal void NoteOffered() => Interlocked.Increment(ref _offered);

    internal void NoteAccepted() => Interlocked.Increment(ref _accepted);

    internal void NoteHeld(UnsaidLine? line = null)
    {
        Interlocked.Increment(ref _held);
        Keep(line);
    }

    internal void NoteDropped(UnsaidLine? line = null)
    {
        Interlocked.Increment(ref _dropped);
        Keep(line);
    }

    // ------------------------------------------------------------------
    //  Each line, not only the count (20 Oct 2026)
    // ------------------------------------------------------------------
    //
    // ASKED FOR: click the lost count on Race Control and see every line
    // that wasn't said, stacked up, with what it was and why it went. A
    // number says the announcer is running out of room; the list says
    // which calls it is losing and to what - "a lap time, lost behind the
    // race recap" is something a club can act on, and "17 lost" is not.
    //
    // Kept alongside the counters rather than instead of them, so the
    // arithmetic above is untouched: a line with no detail (a caller that
    // passes none) still counts, it just isn't listed.

    /// <summary>The most lines one race keeps - a long night of a busy dial is a few hundred at most, and the window only needs the shape of it.</summary>
    internal const int MostKept = 300;

    private readonly object _linesGate = new();
    private readonly List<UnsaidLine> _lines = new();
    private int _linesVersion;

    /// <summary>Every line not said this race that came with its details, oldest first.</summary>
    internal IReadOnlyList<UnsaidLine> Lines
    {
        get
        {
            lock (_linesGate)
            {
                return _lines.ToArray();
            }
        }
    }

    /// <summary>Bumped on every line kept and on every reset, so a window can tell cheaply whether to redraw.</summary>
    internal int LinesVersion => Volatile.Read(ref _linesVersion);

    private void Keep(UnsaidLine? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_linesGate)
        {
            _lines.Add(line);

            if (_lines.Count > MostKept)
            {
                _lines.RemoveAt(0);
            }

            _linesVersion++;
        }
    }

    /// <summary>
    /// Whether the parts sum to the whole - see the arithmetic note on the
    /// class. Read under no lock, so a true answer is only guaranteed when
    /// nothing is being counted; that is fine for what it is for, which is
    /// a test and a diagnostic rather than a decision.
    /// </summary>
    internal bool Balances => Offered == Accepted + Held + Dropped;

    /// <summary>A new race. Everything goes.</summary>
    internal void Reset()
    {
        Interlocked.Exchange(ref _offered, 0);
        Interlocked.Exchange(ref _accepted, 0);
        Interlocked.Exchange(ref _held, 0);
        Interlocked.Exchange(ref _dropped, 0);

        lock (_linesGate)
        {
            _lines.Clear();
            _linesVersion++;
        }
    }

    /// <summary>
    /// What Race Control's announcer strip puts beside the buttons, or
    /// NULL when there is nothing to say.
    ///
    /// Null rather than a nought for the reason the notes and votes counts
    /// beside it are: "0 held" on a quiet practice session reads as a
    /// fault, and a row on the busiest screen in the app does not get to
    /// spend space saying nothing happened.
    ///
    /// DROPPED LEADS WHERE THERE IS ANY, because that is the number the
    /// person holding the microphone needs: held is the dial doing its
    /// job, dropped is the announcer losing lines. A race with both says
    /// the dropped figure and nothing else, so the one word on the strip
    /// is never the reassuring one.
    /// </summary>
    internal string? Badge
    {
        get
        {
            var dropped = Dropped;

            if (dropped > 0)
            {
                return $"{dropped} lost";
            }

            var held = Held;

            return held > 0 ? $"{held} held" : null;
        }
    }

    /// <summary>
    /// Whether the strip should show the badge in the look's accent - the
    /// same rule the notes count goes by, so an eye on the leaderboard
    /// still catches it.
    ///
    /// Only for DROPPED. Held is the club's own setting working and must
    /// not be lit like a warning, or the first thing a club does after
    /// turning the pacing up is turn it back down.
    /// </summary>
    internal bool BadgeIsAWarning => Dropped > 0;

    /// <summary>
    /// The sentence the Commentary page puts under the pacing dial, in
    /// whole calls - which is the part a club can hear.
    ///
    /// Says what it MEANS and not only the figures: a club reading
    /// "74 held" has to be told that is the dial working, or it reads as
    /// the app losing three quarters of the show.
    /// </summary>
    internal string Report()
    {
        var offered = Offered;

        if (offered == 0)
        {
            return "Nothing has been offered yet this race, so there is nothing to show. Run a race and this "
                 + "says how many timing calls the announcer tried to make and how many got out.";
        }

        var said = $"Of {Plural.Of(offered, "timing call")} offered this race, {Accepted} went out";

        var held = Held;
        var dropped = Dropped;

        if (held > 0)
        {
            said += $", {held} {(held == 1 ? "was" : "were")} held back to leave a gap";
        }

        if (dropped > 0)
        {
            said += $", and {dropped} {(dropped == 1 ? "was" : "were")} lost behind a line already being spoken";
        }

        said += ".";

        if (dropped > 0)
        {
            said += " Lines being LOST is the announcer running out of room rather than the dial working - a "
                  + "wider gap gives it somewhere to put them.";
        }
        else if (held > 0)
        {
            said += " Nothing was lost, which is what this dial is for: everything unsaid was unsaid on "
                  + "purpose.";
        }

        return said;
    }
}

/// <summary>Whether a line went unsaid on purpose (the pacing dial) or was lost.</summary>
internal enum UnsaidKind
{
    /// <summary>Lost: something else was being said, or the moment passed.</summary>
    Lost,

    /// <summary>Held back on purpose by the pacing dial, to leave a gap.</summary>
    Held,
}

/// <summary>
/// One timing call that was not said - what Race Control's lost-lines window
/// lists (20 Oct 2026).
/// </summary>
/// <param name="AtUtc">When it was turned away.</param>
/// <param name="Kind">Lost or held.</param>
/// <param name="Call">The kind of call, as the Voice page names it - "Lap time", "Fast lap".</param>
/// <param name="Driver">Who it was about, or null for a call about nobody in particular.</param>
/// <param name="Words">What it would have said, delivery tags taken off, or null when the words weren't kept.</param>
/// <param name="Why">Why it went, in a sentence.</param>
internal sealed record UnsaidLine(DateTime AtUtc, UnsaidKind Kind, string Call, string? Driver, string? Words, string Why);
