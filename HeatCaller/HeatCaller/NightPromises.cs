namespace HeatCaller;

/// <summary>
/// What a segment UNDERTOOK, so a later one can come back to it.
///
/// WHY THIS IS A STORE WHEN NightMoments IS A READER
///
/// NightMoments derives everything it knows from the saved result, which
/// is why it can be asked about a night months after the night happened.
/// This cannot. What the broadcast pointed the room at is not in the
/// result and is not recoverable from it: a race where Carl and Dave spent
/// nine laps a tenth apart looks the same on the sheet whether anybody
/// mentioned it or not. So the one thing the result cannot hold is the one
/// thing kept here, and nothing else is.
///
/// WHAT A PROMISE IS, AND THE LINE THIS CLASS WILL NOT CROSS
///
/// A promise is a SUBJECT the broadcast pointed at. It is not a
/// prediction, and there is deliberately no field on it for what was
/// claimed.
///
/// "Watch Carl in the second half of this one" is not falsifiable. "Carl
/// will win" is, and this app must never say it. The distinction is the
/// whole reason a forward-looking segment is allowed at all - see
/// NightThread, which states the same rule for the night's storyline: the
/// SHAPE is invented, the claim inside it is not.
///
/// So a promise records who it was about and nothing more. The pay-off is
/// whatever actually happened to that subject, read out of NightMoments,
/// which is arithmetic over the club's own saved result. A promise the
/// night had nothing to say about is dropped in silence rather than paid
/// off wrongly.
///
/// AND WHY IT DOES NOT RECORD WHAT THE VOICES SAID (12 Oct 2026)
///
/// The first draft of the pay-off read "the two voices did not agree about
/// this earlier tonight". It was cut, and the reason is worth keeping
/// because it is the same reason three times over in this layer.
///
/// We know a segment went out as a two-hander, and we know CoHostStance
/// gave the co-host a brief that tells it to disagree. We do NOT know that
/// it did. A brief is an instruction to a model, not an observation of
/// one, and "they disagreed" built from an instruction is exactly the
/// invented claim this class exists to refuse - the more so because a club
/// that heard the two of them agree about Carl and then heard the recap
/// say they had argued about him would be hearing the announcer lie about
/// the last five minutes of their own evening.
///
/// What IS true is that both voices were on it. So the pay-off says that,
/// and nothing about the position either of them took. The stance brief is
/// still in force when the callback is written, so a sceptical co-host
/// comes back to it sceptically without anybody having had to record that
/// it would.
///
/// For the same reason there is no second log of what was said anywhere
/// here. Read RaceHighlights' opening paragraph: everything in this layer
/// is arithmetic on the record the app already keeps, plus - in this one
/// class and only this one - the fact that a segment about a named subject
/// went to air.
///
/// LIVES FOR ONE NIGHT AND IS NOT WRITTEN DOWN
///
/// SaidTonight survives a restart and says why. This does not, and the
/// difference is not an oversight. A spent fact is spent for the evening
/// whatever happens to the laptop - re-introducing every driver after the
/// break is a fault a room hears. An unpaid promise is the opposite: it is
/// a thing to come back to in the next few minutes, and a callback to a
/// fight the room heard called forty minutes and one laptop lid ago is
/// worse than no callback. So it is in memory, it lapses, and a restart
/// loses it on purpose.
/// </summary>
internal sealed class NightPromises
{
    /// <summary>
    /// How many promises are held at once.
    ///
    /// Small, and a cap rather than a target. A night's worth of segments
    /// would run to a hundred of these and the room can be reminded of
    /// perhaps two; everything past that is a queue that never drains and
    /// whose head is the stalest thing in it. When it is full the OLDEST
    /// is dropped, for the same reason: a promise nobody has got round to
    /// paying off in two dozen segments is not going to land now.
    /// </summary>
    internal const int MostHeld = 24;

    /// <summary>
    /// How long a promise is worth coming back to.
    ///
    /// Twenty-five minutes, which is two or three races at a club meeting.
    /// The figure is doing one job: keeping a callback inside the stretch
    /// of the evening the room still has in its head. "We were talking
    /// about that fight earlier" lands when earlier was two races ago and
    /// is a non-sequitur when earlier was before the break.
    /// </summary>
    internal static readonly TimeSpan StaysWorthPaying = TimeSpan.FromMinutes(25);

    /// <summary>
    /// One subject the broadcast pointed at.
    /// </summary>
    /// <param name="From">Which segment did the pointing. For the console and for nothing else - the pay-off never names it, because a room does not think in segment kinds.</param>
    /// <param name="Subject">The transponder the segment's facts were about.</param>
    /// <param name="Name">That driver, as the club calls them, cleaned once here so the pay-off cannot be the thing that finds a name empty.</param>
    /// <param name="Against">The second transponder where the subject was a fight, or null where it was one driver.</param>
    /// <param name="AgainstName">The second driver's name. Empty where the result had none; see PayOff for what happens then.</param>
    /// <param name="BothVoices">The segment went out as a two-hander. See the class summary for what this is and is NOT allowed to mean.</param>

    /// <param name="At">
    /// When it went to air, by the DIRECTOR's clock - which is local time,
    /// not UTC, because that is the clock the whole commentary layer is
    /// paced on.
    ///
    /// THE FIELD THAT MAKES THE PAY-OFF HONEST, and it does it by being
    /// compared with the race's GREEN FLAG. A pace note saying Carl has
    /// just gone quickest, paid off by the moment that says Carl set the
    /// fastest lap, is the announcer congratulating itself on reading out
    /// its own line - and it would happen on every single pace note,
    /// because they are the same lap. So a promise is only paid off by a
    /// race that had not STARTED when the promise went to air. See PayOff.
    ///
    /// THE CONVERSION IS EXPLICIT AND IT HAS TO BE. A moment's times come
    /// off the saved session and are genuinely UTC; this comes off
    /// DateTime.Now, whose Kind is Local. Subtracting one from the other
    /// raw would be right on a laptop in London in January and an hour out
    /// in July, and the symptom would be a callback paying itself off out
    /// of its own race for half the year - silent, seasonal, and
    /// impossible to reproduce in the clubhouse where it happened. So
    /// PayOff converts, once, and says so.
    ///
    /// The LAPSE never crosses the two clocks: it compares this field with
    /// another reading of the same clock.
    /// </param>
    internal sealed record Promise(
        SegmentKind From,
        string Subject,
        string Name,
        string? Against,
        string? AgainstName,
        bool BothVoices,
        DateTime At);

    private readonly object _gate = new();
    private readonly List<Promise> _held = new();
    private string _dayId = string.Empty;
    private int _noted;
    private int _paid;
    private int _lapsed;

    /// <summary>A new race day. Last week's promises were paid off or were not, and either way they are gone.</summary>
    internal void NewMeeting(string? dayId)
    {
        lock (_gate)
        {
            var now = dayId ?? string.Empty;

            if (string.Equals(_dayId, now, StringComparison.Ordinal))
            {
                return;
            }

            _dayId = now;
            _held.Clear();
            _noted = 0;
            _paid = 0;
            _lapsed = 0;
        }
    }

    /// <summary>How many are waiting to be come back to.</summary>
    internal int Held
    {
        get
        {
            lock (_gate)
            {
                return _held.Count;
            }
        }
    }

    /// <summary>How many subjects the broadcast has pointed at tonight.</summary>
    internal int Noted
    {
        get
        {
            lock (_gate)
            {
                return _noted;
            }
        }
    }

    /// <summary>How many were come back to.</summary>
    internal int Paid
    {
        get
        {
            lock (_gate)
            {
                return _paid;
            }
        }
    }

    /// <summary>How many went past StaysWorthPaying, or were pushed out by MostHeld, without the night having anything to say about them.</summary>
    internal int Lapsed
    {
        get
        {
            lock (_gate)
            {
                return _lapsed;
            }
        }
    }

    /// <summary>
    /// Records what a segment pointed at, AFTER it has actually gone to
    /// air - the same rule as SaidTonight.Mark and for the same reason. A
    /// segment written and then dropped pointed the room at nothing, and a
    /// callback to it would be the announcer referring to a line nobody
    /// heard.
    /// </summary>
    /// <param name="from">Which segment.</param>
    /// <param name="pack">The facts it went out with. Its THEMES are where the subjects come from - see SubjectsIn.</param>
    /// <param name="nameFor">Transponder to the name the club uses, as everything else in this layer takes it.</param>
    /// <param name="bothVoices">It went out as a two-hander.</param>
    /// <param name="at">Now, by the director's clock. See Promise.At.</param>
    internal void Note(
        SegmentKind from,
        PackedFacts? pack,
        Func<string, string?>? nameFor,
        bool bothVoices,
        DateTime at)
    {
        var subjects = SubjectsIn(pack);

        if (subjects.Count == 0)
        {
            return;
        }

        var names = nameFor ?? (_ => null);

        lock (_gate)
        {
            foreach (var (a, b) in subjects)
            {
                var named = CommentaryFacts.Clean(names(a));

                // No name, no promise. The pay-off is a sentence with a
                // person in it; "the car we mentioned earlier" is not a
                // callback, it is a riddle.
                if (named.Length == 0)
                {
                    continue;
                }

                // ONE PROMISE PER SUBJECT. A driver the broadcast has
                // pointed at three times in ten minutes is one thing to
                // come back to, not three, and three callbacks about Carl
                // is the repetition this whole layer was written against.
                // The newest wins, because the newest is the one the room
                // heard most recently; so the old one goes rather than
                // being counted as lapsed.
                _held.RemoveAll(one =>
                    string.Equals(one.Subject, a, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(one.Against ?? string.Empty, b ?? string.Empty, StringComparison.OrdinalIgnoreCase));

                _held.Add(new Promise(
                    from,
                    a,
                    named,
                    b,
                    b is null ? null : CommentaryFacts.Clean(names(b)),
                    bothVoices,
                    at));

                _noted++;
            }

            // The oldest go when it is full - see MostHeld.
            while (_held.Count > MostHeld)
            {
                _held.RemoveAt(0);
                _lapsed++;
            }
        }
    }

    /// <summary>
    /// Takes back the fight promise a battle watch filed for this pair -
    /// either order - because its line was thrown away before a word of it
    /// was heard. Not counted as lapsed: it was never made.
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): a battle watch is settled as said when
    /// it is queued, so a line the voice then binned unheard still left a
    /// promise behind, and a later callback told the room the fight "was
    /// called on air earlier tonight" about a line nobody heard.
    /// </remarks>
    internal void Withdraw(string aId, string bId)
    {
        if (string.IsNullOrWhiteSpace(aId) || string.IsNullOrWhiteSpace(bId))
        {
            return;
        }

        bool Is(string? one, string other) => string.Equals(one ?? string.Empty, other, StringComparison.OrdinalIgnoreCase);

        lock (_gate)
        {
            var gone = _held.RemoveAll(one =>
                one.From == SegmentKind.BattleWatch
                && ((Is(one.Subject, aId) && Is(one.Against, bId)) || (Is(one.Subject, bId) && Is(one.Against, aId))));

            _noted = Math.Max(0, _noted - gone);
        }
    }

    /// <summary>
    /// The one promise the night has something to say about, as a fact
    /// block, or null.
    ///
    /// ONE, not all of them. This block rides along inside a segment that
    /// is mostly about something else, exactly as CommentaryShowFacts.
    /// ThreadSoFar does, and two callbacks in one line is a segment about
    /// the broadcast rather than about the racing.
    ///
    /// THE STRONGEST MOMENT WINS rather than the oldest promise, because
    /// NightMoments.Over already hands them over strongest first and a
    /// callback is only worth the airtime if what it pays off is. A
    /// last-lap lead change beats a fastest lap whichever was promised
    /// first.
    ///
    /// Reads the ledger and does not change it. The promise is retired by
    /// the block being SPOKEN - its key goes to SaidTonight with the rest
    /// of the pack, so a callback written and then dropped is still waiting
    /// to be made, which is the same discipline every other fact in this
    /// app is under. The one thing this does write is the lapse count, for
    /// promises the clock has taken out of reach.
    /// </summary>
    /// <param name="moments">Tonight's moments, strongest first, as NightMoments.Over hands them over.</param>
    /// <param name="now">Now, by the director's clock - the same clock Note was given. See Promise.At.</param>
    internal FactBlock? PayOff(IReadOnlyList<NightMoments.Moment>? moments, DateTime now)
    {
        if (moments is null || moments.Count == 0)
        {
            return null;
        }

        List<Promise> live;

        lock (_gate)
        {
            var gone = _held.RemoveAll(one => now - one.At > StaysWorthPaying);
            _lapsed += gone;

            live = new List<Promise>(_held);
        }

        if (live.Count == 0)
        {
            return null;
        }

        foreach (var moment in moments)
        {
            foreach (var promise in live)
            {
                // ONCE, HERE, AND SAID OUT LOUD. The promise's clock is
                // local and the moment's is UTC - see Promise.At for what
                // comparing them raw would have cost.
                var promiseAtUtc = promise.At.ToUniversalTime();

                // THE RACE HAD NOT STARTED WHEN THE PROMISE WAS MADE.
                //
                // This is the check that stops a pace note being paid off
                // by its own lap, and the green flag is the only time that
                // answers it: a moment's AtUtc is the FLAG, so the race a
                // note was called during finished after the note and looks
                // from here like a later race. See Promise.At.
                //
                // A green flag of default is a result with no start time
                // saved on it, and it gets no callback rather than a
                // guessed one.
                //
                // ONE EXCEPTION, AND ONLY ONE (15 Oct 2026): a HORIZON may
                // be paid off by its own race's FINISH. A horizon is said
                // during a race about something that race has still to
                // decide - "three laps to find half a second" - and the
                // only honest pay-off for it is how that race ended. The
                // finish moments (the lead changing on the last lap, a
                // photo finish) are at the flag, so they are after the
                // horizon by construction - it is only ever said with road
                // left - and the self-congratulation the rule above exists
                // to stop cannot happen: the horizon pointed at a gap, and
                // the moment is what the gap became. Every other moment in
                // that race - a fastest lap, a lead change mid-race - may
                // have been BEFORE the horizon, and stays out.
                var laterRace = moment.GreenAtUtc != default && moment.GreenAtUtc > promiseAtUtc;

                if (!laterRace && !PaidOffByItsOwnFinish(promise, moment, promiseAtUtc))
                {
                    continue;
                }

                // A FIGHT NEEDS BOTH ITS NAMES. The callback names the
                // two of them - "the fight between Carl and Dave" - and a
                // result saved without a name on one row leaves nothing to
                // put in the second place. Written as a skip rather than
                // as a fallback sentence because the fallback was wrong:
                // dropping to the one-driver wording would open with the
                // subject's name and then describe whatever the moment was
                // about, which on a moment about the OTHER side of the
                // fight is two different drivers in one sentence joined by
                // "earlier tonight". A promise that cannot be paid off
                // properly is dropped in silence, which is this class's
                // rule everywhere else.
                if (promise.Against is { Length: > 0 }
                    && CommentaryFacts.Clean(promise.AgainstName).Length == 0)
                {
                    continue;
                }

                var subject = string.Equals(moment.Transponder, promise.Subject, StringComparison.OrdinalIgnoreCase);
                var against = promise.Against is { Length: > 0 } other
                    && string.Equals(moment.Transponder, other, StringComparison.OrdinalIgnoreCase);

                if (!subject && !against)
                {
                    continue;
                }

                return Block(promise, moment);
            }
        }

        return null;
    }

    /// <summary>
    /// Whether a moment from the race a promise was made DURING may pay it
    /// off - true only for a horizon, only for a finish moment, and only
    /// for a race whose flag fell after the promise was made. See the
    /// exception in PayOff.
    /// </summary>
    private static bool PaidOffByItsOwnFinish(Promise promise, NightMoments.Moment moment, DateTime promiseAtUtc) =>
        promise.From == SegmentKind.Horizon
        && moment.What is NightMoments.Kind.LastLapLead or NightMoments.Kind.PhotoFinish
        && moment.GreenAtUtc != default
        && moment.GreenAtUtc <= promiseAtUtc
        && moment.AtUtc > promiseAtUtc;

    /// <summary>
    /// Marks a promise paid - called when the callback has actually gone
    /// out, never before.
    ///
    /// Separate from PayOff for the reason every mark in this layer is
    /// separate from the thing it marks: PayOff is asked speculatively,
    /// once per segment per pass, and most of those segments are never
    /// spoken. A promise retired at the moment it was offered would be
    /// retired by a segment the budget refused.
    /// </summary>
    internal void MarkPaid(string? key)
    {
        if (key is not { Length: > 0 })
        {
            return;
        }

        lock (_gate)
        {
            var gone = _held.RemoveAll(one => string.Equals(KeyFor(one), key, StringComparison.Ordinal));

            _paid += gone;
        }
    }

    /// <summary>True when this key is one of ours, so the director knows a spoken pack carried a callback.</summary>
    internal bool IsPayOffKey(string? key) =>
        key is { Length: > 0 } text && text.StartsWith(KeyPrefix, StringComparison.Ordinal);

    /// <summary>
    /// What the night's own report says about the promises, or null.
    ///
    /// NULL rather than a line of noughts on a night nothing was pointed
    /// at, which is NightAccount.Report's own rule and worth keeping:
    /// a report that lists what did not happen is a report nobody reads
    /// twice. A club that heard no callbacks and wants to know why reads
    /// the account above it, where the segments that were held are
    /// counted - a night with no promises is a night with few segments,
    /// and that is the figure that explains it.
    /// </summary>
    internal string? Report()
    {
        lock (_gate)
        {
            if (_noted == 0)
            {
                return null;
            }

            return $"Pointed the room at {Plural.Of(_noted, "subject")}, came back to "
                + $"{_paid.ToString(System.Globalization.CultureInfo.InvariantCulture)}, "
                + $"let {_lapsed.ToString(System.Globalization.CultureInfo.InvariantCulture)} go by, "
                + $"{_held.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} still open.";
        }
    }

    /// <summary>
    /// What a spoken pack was ABOUT, read back out of its own themes.
    ///
    /// FactTheme mints "driver:1234" and "rivalry:1234+5678", and this is
    /// the only place that reads one. That is a deliberate round trip
    /// rather than a shortcut: the alternative is the director handing a
    /// subject over from each case arm of Candidates, which is twenty
    /// places that have to remember to, and a kind added later would
    /// silently never make a promise - the fault CommentaryDirector's own
    /// switch was restructured to stop.
    ///
    /// A PAIR IS VERIFIED BY BEING MINTED AGAIN, AND ONLY WHERE ONE SPLIT
    /// DOES IT. FactTheme.Rivalry sorts its two sides, so the string cannot
    /// simply be split on the plus - a wrong pair is a callback about two
    /// drivers who were never mentioned together. So every split is tried
    /// and the pair is taken only when EXACTLY ONE of them mints back to
    /// the same theme.
    ///
    /// Exactly one rather than the first one, and that is not caution for
    /// its own sake: a transponder with a plus in it makes two splits that
    /// both mint back correctly. "12+34" against "5678" and "12" against
    /// "34+5678" are the same string and the same theme, and nothing in
    /// the theme can tell them apart - so the honest answer is that this
    /// pack's subject is unknown, and an unknown subject is no promise.
    /// Transponders are numbers in every timing system this app reads, so
    /// the case is theoretical; the branch is here because a theoretical
    /// wrong name over a PA is still a wrong name over a PA.
    ///
    /// THE OTHER THREE THEMES ARE NOT SUBJECTS. A venue, a field and a
    /// past day are things rather than people, and a callback is "we were
    /// talking about Carl earlier" - there is nothing to come back to in
    /// having mentioned how many cars were in the B-main.
    /// </summary>
    internal static IReadOnlyList<(string A, string? B)> SubjectsIn(PackedFacts? pack)
    {
        var found = new List<(string A, string? B)>();

        if (pack is null || pack.Empty)
        {
            return found;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var theme in pack.Themes)
        {
            if (theme is not { Length: > 0 } tag || !seen.Add(tag))
            {
                continue;
            }

            if (tag.StartsWith(DriverTag, StringComparison.Ordinal))
            {
                var who = tag.Substring(DriverTag.Length).Trim();

                if (who.Length > 0)
                {
                    found.Add((who, null));
                }

                continue;
            }

            if (!tag.StartsWith(RivalryTag, StringComparison.Ordinal))
            {
                continue;
            }

            var pair = tag.Substring(RivalryTag.Length);
            var splits = 0;
            (string A, string B) only = (string.Empty, string.Empty);

            for (var at = pair.IndexOf('+'); at > 0; at = pair.IndexOf('+', at + 1))
            {
                var a = pair.Substring(0, at);
                var b = pair.Substring(at + 1);

                if (b.Length > 0 && string.Equals(FactTheme.Rivalry(a, b), tag, StringComparison.Ordinal))
                {
                    splits++;
                    only = (a, b);
                }
            }

            if (splits == 1)
            {
                found.Add((only.A, only.B));
            }
        }

        return found;
    }

    /// <summary>What the prefix of every pay-off key is. See KeyFor.</summary>
    private const string KeyPrefix = "promise:";

    /// <summary>FactTheme.Driver's prefix, so this class reads back what that class mints.</summary>
    private const string DriverTag = "driver:";

    /// <summary>FactTheme.Rivalry's prefix. See SubjectsIn for why the pair is not simply split.</summary>
    private const string RivalryTag = "rivalry:";

    /// <summary>
    /// A promise's key.
    ///
    /// The time it was made is in it, which is what makes it the identity
    /// of THIS promise rather than of its subject. Carl pointed at in the
    /// first heat and again in the A-main are two things the night can come
    /// back to, and a key that collapsed them would let SaidTonight spend
    /// the second one before it was offered.
    /// </summary>
    private static string KeyFor(Promise promise) =>
        KeyPrefix
        + promise.At.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ":" + promise.Subject
        + (promise.Against is { Length: > 0 } other ? "+" + other : string.Empty);

    /// <summary>
    /// The callback, as a fact.
    ///
    /// Plain declarative sentences and no steer, because the system prompt
    /// says everything between the fact markers is data to describe and
    /// never an instruction - so a fact block that tried to tell the model
    /// what not to claim would be read out as part of the race. The guard
    /// is in what the sentence SAYS rather than in an aside attached to it:
    /// it states that the subject was on air and what has since happened,
    /// and there is nothing in it to overstate.
    ///
    /// THEMED ON THE SUBJECT, the same way round as ThreadSoFar and for
    /// the same reason: this is colour inside somebody else's segment, so
    /// cooling everything else about that driver for a segment or two is
    /// exactly right. The room has just been reminded about Carl; the next
    /// line should not also be about Carl.
    /// </summary>
    private static FactBlock Block(Promise promise, NightMoments.Moment moment)
    {
        // WHICHEVER OF THE TWO THE MOMENT IS ABOUT, by the moment's own
        // name rather than the one stored with the promise. They are the
        // same driver; the moment's is read from the race that has just
        // been saved, which is the later of the two records.
        var did = CommentaryFacts.Clean(moment.DriverName);

        if (did.Length == 0)
        {
            // The result this moment came from had no name on that row.
            // The promise's own copy is the fallback, and for the second
            // side of a fight only when it has one - otherwise the first
            // side's name is wrong but present, which is worse than the
            // first side's name being right.
            var stored = string.Equals(moment.Transponder, promise.Subject, StringComparison.OrdinalIgnoreCase)
                ? promise.Name
                : CommentaryFacts.Clean(promise.AgainstName);

            did = stored.Length > 0 ? stored : promise.Name;
        }

        // WHO THE OPENING NAMES. A fight names both sides; one driver
        // names themselves. PayOff has already refused a fight whose
        // second name the result did not save, so the pair branch here can
        // rely on having two - and the fallback below names the driver the
        // MOMENT is about rather than the promise's first side, so a
        // promise that somehow reached here without its pair cannot open
        // with one driver's name and then describe another's race.
        var second = CommentaryFacts.Clean(promise.AgainstName);
        string opening;

        if (promise.Against is { Length: > 0 } && second.Length > 0)
        {
            opening = promise.BothVoices
                ? $"The fight between {promise.Name} and {second} was talked over by both voices earlier tonight."
                : $"The fight between {promise.Name} and {second} was called on air earlier tonight.";
        }
        else
        {
            opening = promise.BothVoices
                ? $"{did} was talked over by both voices earlier tonight."
                : $"{did} was called on air earlier tonight.";
        }

        var since = DriverCut.Describe(moment, did);

        return new FactBlock(
            KeyFor(promise),
            since.Length > 0 ? opening + " " + since : opening,
            promise.Against is { Length: > 0 } pairedWith
                ? FactTheme.Rivalry(promise.Subject, pairedWith)
                : FactTheme.Driver(promise.Subject));
    }
}
