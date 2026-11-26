namespace HeatCaller;

/// <summary>
/// How much ROOM the announcer leaves between the calls it makes off the
/// timing loop.
///
/// WHY THIS EXISTS WHEN <see cref="ShowChatter"/> ALREADY DID (14 Oct 2026)
///
/// Reported from the floor, with the console log to go with it: an
/// eight-lap demo with THREE cars in it produced about twenty
/// "Dropped LapCompleted announcement - still speaking the previous one"
/// lines, and the club had already turned the chattiness dial down looking
/// for the control that would stop it. The dial was not the control, and
/// the reason is worth writing down because it is not obvious from either
/// class on its own.
///
/// ShowChatter decides WHICH calls are eligible and HOW MANY a single car
/// may make on a single lap. Both of those are per-driver questions:
/// <see cref="ShowChatter.ScaledPerLap"/> scales
/// VoiceProfile.MaxAnnouncementsPerLap, and the batch that cap is applied
/// to is keyed by TRANSPONDER - see AnnouncementService's
/// _pendingPerLapBatches. So three cars at a cap of two are six calls a
/// lap, ten cars are twenty, and every one of them is inside the cap the
/// club set. Nothing in the app had an opinion about the FIELD.
///
/// What then happened to those six or twenty calls is the second half of
/// it. They arrived at EnqueueVoiceWork, which dropped any routine line
/// that turned up while something was already being said - first come,
/// first served, no ranking, nothing counted anywhere a club could see it.
/// The announcer therefore talked continuously and the thing it talked
/// about was whichever car happened to cross the loop at the moment the
/// previous sentence ended. A lead change could lose its slot to a lap
/// time; neither the club nor the room had any way of knowing it had.
///
/// So this is the missing dial, and it is a dial about the field: the
/// SHORTEST GAP the announcer will leave between one routine call and the
/// next, whoever they are about.
///
/// THE SHAPE COMES OFF THE STATURE, THE VOLUME OFF THE DIAL, which is the
/// arrangement ShowChatter has with CommentaryBudget.AirtimeCap and it is
/// here for the same reason: the quiet-practice-loud-main curve is right
/// for every club, and where it sits is taste. See
/// <see cref="BaseGapSeconds"/> and <see cref="GapSeconds"/>.
///
/// WHAT IT CANNOT DO. It cannot touch an instruction. A flag, a
/// countdown, a green, a grid call, a driver's release, the finish, a
/// badge, "last lap", "one minute to go", a track record and the running
/// order all go straight past it - the same two lists EnqueueVoiceWork
/// already keeps (IsPriorityAnnouncement and IsWorthWaitingFor), asked
/// rather than copied. A dial about pacing that can swallow a yellow is
/// not a pacing dial, it is a bug.
///
/// AND IT CANNOT HOLD BACK THE BIGGEST CALL IN THE SET. See
/// <see cref="WorthBreakingIn"/>: a call that outweighs the one the gap is
/// protecting speaks anyway. A gap that silenced a lead change because a
/// lap time went out two seconds earlier would have replaced a known fault
/// with a worse one.
///
/// THE DEFAULT IS NOT "NO CHANGE", WHICH IS A DEPARTURE AND DELIBERATE.
/// Every other dial in this layer ships on the setting an existing club
/// already had, so nobody's evening changes under them. This one ships on
/// <see cref="Balanced"/>, because "no change" here means the twenty
/// dropped lines in the report above, and a room losing most of what the
/// announcer tried to say is not a taste anybody chose. The old behaviour
/// is still on the dial - it is <see cref="WallToWall"/>, the last stop -
/// so a club that wants it back has it one click away and can read on the
/// page what it means.
/// </summary>
internal static class AnnouncerDensity
{
    /// <summary>
    /// One stop on the dial.
    /// </summary>
    /// <param name="Key">What is stored in settings. Never changes once shipped, so the wording below can.</param>
    /// <param name="Name">What the club picks it by on the Commentary page.</param>
    /// <param name="Blurb">One line, in a club's words rather than a writer's.</param>
    /// <param name="Scale">
    /// What the race's own gap is multiplied by - bigger is quieter, which
    /// is the opposite of ShowChatter's Scale and the reason the two are
    /// not one record. Nought means NO GAP AT ALL rather than a very short
    /// one: see <see cref="WallToWall"/>.
    /// </param>
    internal sealed record Level(string Key, string Name, string Blurb, double Scale);

    /// <summary>
    /// The pacing this ships on - see the departure note at the top of
    /// this class for why it is this one and not the old behaviour.
    /// </summary>
    internal static readonly Level Balanced = new(
        "balanced",
        "Balanced",
        "A beat between calls, so each one lands.",
        1.0);

    /// <summary>
    /// The announcer exactly as it behaved before this dial existed: no
    /// field-wide gap at all, every call offered the moment it arrives,
    /// and whatever turns up mid-sentence lost.
    ///
    /// ON THE DIAL ON PURPOSE. A club that liked the old density, or that
    /// runs one car at a time and never had a collision in the first
    /// place, should not have to be told its only option is a gap it does
    /// not want. The blurb says what it costs, which is the part the
    /// report at the top of this class shows a club cannot work out for
    /// itself.
    /// </summary>
    internal static readonly Level WallToWall = new(
        "walltowall",
        "Wall to wall",
        "No gap at all - as the announcer was before this dial. On a full field most calls will be lost "
        + "behind the one being spoken.",
        0.0);

    /// <summary>
    /// The five stops, quietest first, which is also the order they are
    /// offered in - so the dial reads left to right like a volume control,
    /// the same way <see cref="ShowChatter.All"/> does.
    /// </summary>
    internal static readonly IReadOnlyList<Level> All = new[]
    {
        new Level(
            "roomy",
            "Room to breathe",
            "Long gaps. The announcer picks its moments and says nothing in between.",
            1.6),

        new Level(
            "spacious",
            "Spacious",
            "A clear pause after each call - for a small room, or a PA that carries.",
            1.3),

        Balanced,

        new Level(
            "busy",
            "Busy",
            "Barely a breath between calls. For a big field with a crowd in.",
            0.6),

        WallToWall,
    };

    /// <summary>
    /// The longest gap any stop may produce. Twenty seconds is a third of
    /// a short heat spent silent, and past that the number has stopped
    /// being pacing and become a mute switch - which a club has elsewhere,
    /// on every tick box on the Voice page.
    /// </summary>
    internal const double LongestGap = 20;

    /// <summary>
    /// And the shortest a stop that HAS a gap may produce. One second,
    /// because the floor exists to stop a small scale rounding down into
    /// nought and silently turning the whole gate off - which would read
    /// in the log as pacing that was switched on and doing nothing.
    /// <see cref="WallToWall"/> turns it off by saying so.
    /// </summary>
    internal const double ShortestGap = 1;

    /// <summary>
    /// A weight at or above which a call is never held for pacing - see
    /// <see cref="WorthBreakingIn"/>.
    ///
    /// A hundred is the shipped weight of a change of lead, which is the
    /// one thing in the per-event set that is always the most interesting
    /// sentence available: if the lead of the race has changed hands, that
    /// is what the room wants said, and a gap protecting somebody's lap
    /// time from two seconds ago is not a reason to lose it.
    /// </summary>
    internal const int Commanding = 100;

    /// <summary>
    /// How far a call has to outweigh the one the gap is protecting before
    /// it breaks in anyway.
    ///
    /// Twenty-five, against the shipped weights, means a fast lap (90)
    /// breaks in over a lap count (40) or a lap time (30), and a lap time
    /// does not break in over a lap count. That is the right way round: the
    /// gap exists to stop the SMALL calls filling the air, and the small
    /// calls are exactly the ones within a few points of each other.
    /// </summary>
    internal const int UpgradeBy = 25;

    /// <summary>
    /// The stop a stored key means, or <see cref="Balanced"/> when it is
    /// blank, unknown, or from a newer version of the app.
    ///
    /// Blank is Balanced rather than the old behaviour, for the reason the
    /// departure note at the top gives: blank is every club that has never
    /// opened this page, and they are the clubs the report came from.
    /// </summary>
    internal static Level For(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Balanced;
        }

        var wanted = key.Trim();

        foreach (var level in All)
        {
            if (string.Equals(level.Key, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return level;
            }
        }

        return Balanced;
    }

    /// <summary>
    /// The gap a race of this stature wants before the dial is applied, in
    /// seconds.
    ///
    /// THE SHAPE IS THE SAME SHAPE AirtimeCap HAS, upside down: practice
    /// is background and gets the most room, the heats are held back, and
    /// the top main is where the night spends what it saved. A room that
    /// hears the announcer working hardest in the A-main learns which race
    /// counts without being told, which is the whole argument
    /// <see cref="RaceStature"/> makes for itself.
    ///
    /// UNKNOWN IS PACED AS A HEAT, which is a second departure from
    /// RaceStature.Unknown's rule that a club with no programme keeps
    /// exactly what it had. It is deliberate for the same reason the
    /// default is: a club with no programme is a club running freeform
    /// practice with whoever turned up, which is the busiest loop in the
    /// app and the case the report came from. Giving it no pacing would
    /// leave the fault in place precisely where it bites hardest.
    /// </summary>
    internal static double BaseGapSeconds(RaceStature stature)
    {
        var gap = stature.Billing switch
        {
            RaceBilling.Practice => 12.0,
            RaceBilling.Heat => 8.0,
            RaceBilling.LesserMain => 6.0,
            RaceBilling.TopMain => 4.0,

            // Unknown, and anything a later version adds that this build
            // has not heard of - see the note above.
            _ => 8.0,
        };

        // THE LAST RACE OF THE NIGHT, for the reason AirtimeCap adds two
        // segments to it: everyone still in the hall is watching this one
        // and there is no later race to save the air for. A second and a
        // half off the gap rather than a flat figure, so the curve is kept
        // - the last heat of the night is still roomier than a top main.
        return stature.LastOfTheNight ? Math.Max(2.0, gap - 1.5) : gap;
    }

    /// <summary>
    /// The shortest gap the announcer will leave between routine calls, in
    /// seconds - or NOUGHT for <see cref="WallToWall"/>, which means no
    /// gate at all rather than a gap of nothing.
    ///
    /// The floor is only applied to a stop that has a gap, so a future
    /// scale small enough to round to nought cannot quietly become
    /// Wall to wall - see <see cref="ShortestGap"/>.
    /// </summary>
    internal static double GapSeconds(RaceStature stature, string? key)
    {
        var scale = For(key).Scale;

        if (scale <= 0)
        {
            return 0;
        }

        return Math.Clamp(BaseGapSeconds(stature) * scale, ShortestGap, LongestGap);
    }

    /// <summary>
    /// Whether a call arriving inside the gap is big enough to be said
    /// anyway.
    /// </summary>
    /// <param name="weight">The arriving call's Weight, off the Voice page.</param>
    /// <param name="weightBeingProtected">
    /// The Weight of the last routine call that was accepted - the one the
    /// gap is currently holding the air for.
    /// </param>
    internal static bool WorthBreakingIn(int weight, int weightBeingProtected) =>
        weight >= Commanding || weight >= weightBeingProtected + UpgradeBy;

    /// <summary>
    /// How long the announcer collects the field's calls before it says
    /// the best of them - see the look-ahead note on
    /// AnnouncementService.OfferToTheField.
    ///
    /// ON TOP OF the per-lap batch's own window, so a call is queued about a
    /// third of a second after the crossing that raised it. Two cars crossing
    /// within a fraction of a second of each other is exactly the case where
    /// the order they reached the loop in says nothing about which of them
    /// is the story.
    ///
    /// Not stretched by later arrivals, the way the per-lap batch is: a
    /// window that restarts on every crossing never closes on a full field.
    ///
    /// 200 ms since 10 Nov 2026 (voice speed plan, phase 1), from 350, with
    /// the per-lap batch down to 120 ms: the "second of device warm-up"
    /// above is 0.15 s now, so the two windows together were most of the
    /// wait before a call was even queued. Two cars a fifth of a second
    /// apart are still weighed together.
    /// </summary>
    internal static readonly TimeSpan FieldWindow = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How long a big call refused because the announcer was mid-sentence
    /// is kept to be said once the sentence ends - see
    /// <see cref="HeldOver{T}"/>.
    ///
    /// Four seconds. Long enough to outlast a lap call or a lap time, which
    /// is what a lead change usually loses its slot to; short enough that
    /// "Sam takes the lead" is still news when it is said. A call kept
    /// longer than this is dropped, because a lead change announced after
    /// the lead has changed back is worse than one never announced.
    /// </summary>
    internal static readonly TimeSpan HoldOverFor = TimeSpan.FromSeconds(4);

    /// <summary>
    /// How the Commentary page says what is set: the stop's own words and
    /// then the real gaps it buys, worked out from the tables above rather
    /// than written down - so a change to either cannot leave this line
    /// lying. The same rule <see cref="ShowChatter.Describe"/> goes by.
    /// </summary>
    internal static string Describe(string? key)
    {
        var level = For(key);

        if (level.Scale <= 0)
        {
            return level.Blurb
                 + " Nothing is held back for pacing, and nothing is counted as held - which is the state "
                 + "the dropped-call count on Race Control was added to make visible.";
        }

        // WHY (25 Nov 2026, Asher: "fix the app text too"): the line gave the gap for
        // each kind of race and listed the calls that skip the gap, which is
        // how the patent applications' pacing claim reads. It now gives the
        // gap a club hears in a heat, and says flags are always called.
        var heat = GapSeconds(new RaceStature(RaceBilling.Heat, false), key);

        return $"{level.Blurb} About {Gap(heat)} between calls. Flag calls are always said.";
    }

    /// <summary>
    /// A gap in the words a club would use for it rather than to two
    /// decimal places - "four seconds", "six and a half seconds".
    /// </summary>
    internal static string Gap(double seconds)
    {
        var halves = Math.Round(Math.Max(0, seconds) * 2, MidpointRounding.AwayFromZero);
        var whole = (int)(halves / 2);
        var half = (halves % 2) != 0;

        if (whole == 0)
        {
            return half ? "half a second" : "no time at all";
        }

        var number = half ? $"{whole} and a half" : whole.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return $"{number} {(whole == 1 && !half ? "second" : "seconds")}";
    }

    /// <summary>
    /// How long since the last call, in words, for a line held back
    /// because that time was shorter than <paramref name="gapSeconds"/>.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): the held-line reason put both the
    /// gap and the time since the last call through <see cref="Gap"/>,
    /// which rounds to the NEAREST half second. A call 7.8 seconds after
    /// the last one on an 8 second gap therefore read "leaves 8 seconds
    /// between calls, and the last one was only 8 seconds before" - a
    /// reason that contradicts itself - and two calls in the same burst
    /// read "only no time at all before". This rounds DOWN to the half
    /// second, says "under half a second" rather than "no time at all",
    /// and says "just under" the gap if the two would still read the same.
    /// <see cref="Gap"/> itself is left alone: the Commentary page's
    /// summary wants the nearest half second.
    /// </summary>
    internal static string GapSince(double sinceSeconds, double gapSeconds)
    {
        var floored = Math.Floor(Math.Max(0, sinceSeconds) * 2) / 2;

        if (floored <= 0)
        {
            return "under half a second";
        }

        var words = Gap(floored);
        var gapWords = Gap(gapSeconds);

        return words == gapWords ? "just under " + gapWords : words;
    }
}

/// <summary>
/// ONE call kept back because the announcer was mid-sentence, to be said
/// the moment the sentence ends - the half of the field-wide look-ahead
/// that <see cref="AnnouncerDensity.WorthBreakingIn"/> could not do.
///
/// WHY (15 Oct 2026)
///
/// The pacing gap converts losses into holds while the speaker is FREE.
/// While it is busy nothing could be done: a lap time two seconds into
/// its sentence and a change of lead arriving behind it, and the lead
/// change was simply lost - "Dropped LeaderChange announcement - still
/// speaking the previous one". The biggest call in the set lost to the
/// smallest, by arriving second.
///
/// ONE SLOT, NOT A QUEUE, and that is the whole design. A queue of held
/// calls is the backlog the drop-while-speaking rule was written to
/// prevent - "three lap streak, four lap streak, five lap streak" read out
/// long after the car had moved on. A single slot keeps exactly one thing:
/// the most important call that arrived while the speaker was busy, and
/// only if it clearly outweighs what was being said (the same test
/// WorthBreakingIn makes). A bigger one arriving replaces it; anything
/// smaller is lost as before.
///
/// AND IT GOES STALE. Kept past <see cref="AnnouncerDensity.HoldOverFor"/>
/// it is dropped rather than said, because a held call is a statement
/// about a moment, and the moment passes.
///
/// Pure: a payload, weights and an injectable clock. Thread-safe, because
/// the timing loop offers and the voice loop takes.
/// </summary>
internal sealed class HeldOver<T> where T : class
{
    private readonly object _lock = new();
    private readonly Func<DateTime> _clock;

    private T? _held;
    private int _weight;
    private DateTime _at;

    /// <remarks>
    /// By default on TimingClock (21 Nov 2026), not the wall clock, so a
    /// kept call's few seconds are not stretched or cut by the PC's clock
    /// being set. Expressed as a DateTime so a test can still hand in its own.
    /// </remarks>
    public HeldOver(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.MinValue.AddMilliseconds(Math.Max(0, TimingClock.NowMs)));

    /// <summary>Whether a call is being kept. For the tests and the console.</summary>
    public bool IsHolding
    {
        get
        {
            lock (_lock)
            {
                return _held is not null;
            }
        }
    }

    /// <summary>
    /// Offers a call refused because the speaker was busy.
    /// </summary>
    /// <param name="weightBeingSpoken">The Weight of the line on the speaker now.</param>
    /// <param name="displaced">
    /// A call this one pushed out of the slot, if it did - the caller counts
    /// it as lost, because it now will be.
    /// </param>
    /// <returns>
    /// True if this call is now being kept. False means it was not worth
    /// keeping - not enough bigger than the line being spoken, or not
    /// bigger than the call already kept - and is lost.
    /// </returns>
    public bool Offer(T call, int weight, int weightBeingSpoken, out T? displaced)
    {
        displaced = null;

        if (!AnnouncerDensity.WorthBreakingIn(weight, weightBeingSpoken))
        {
            return false;
        }

        lock (_lock)
        {
            // A kept call that has gone stale is no rival to a fresh one,
            // whatever it weighed - and is lost either way.
            var fresh = _held is not null && _clock() - _at <= AnnouncerDensity.HoldOverFor;

            if (fresh && weight <= _weight)
            {
                return false;
            }

            displaced = _held;
            _held = call;
            _weight = weight;
            _at = _clock();

            return true;
        }
    }

    /// <summary>
    /// The call to say now the speaker is free, or null - and the stale one
    /// that was thrown away instead, so the caller can count it.
    /// </summary>
    public T? Take(out T? stale)
    {
        stale = null;

        lock (_lock)
        {
            if (_held is not { } call)
            {
                return null;
            }

            var age = _clock() - _at;

            _held = null;

            if (age > AnnouncerDensity.HoldOverFor)
            {
                stale = call;

                return null;
            }

            return call;
        }
    }

    /// <summary>A flag, a countdown or a new race: whatever was kept is no longer true. Returns it, for counting.</summary>
    public T? Clear()
    {
        lock (_lock)
        {
            var was = _held;
            _held = null;

            return was;
        }
    }

    /// <summary>
    /// The call being kept, still fresh, without taking it - so its words
    /// can be rendered while the line it waits on plays (10 Nov 2026). Null
    /// when nothing is kept or what is kept has gone stale.
    /// </summary>
    public T? Peek()
    {
        lock (_lock)
        {
            return _held is { } call && _clock() - _at <= AnnouncerDensity.HoldOverFor ? call : null;
        }
    }
}
