namespace HeatCaller;

/// <summary>
/// One thing one voice says inside an item - the unit the staging actually
/// queues.
///
/// WHY AN ITEM IS A LIST OF THESE AND NOT A STRING
///
/// A two-hander is already split into turns on its A:/B: tags long before
/// it gets here (CommentaryExchange), and the show used to hand the pair
/// over as one block of text - which is how an exchange came out sounding
/// concatenated rather than directed. Held as parts, the block can put a
/// beat and a nudge of the bed at the point the voice changes hands, which
/// is the whole difference between two people talking and one person
/// reading out a transcript of two people talking.
///
/// An ordinary one-voice item is one part, so there is one shape to run
/// rather than two.
/// </summary>
/// <param name="Words">
/// What is said, for the estimate only. The words that actually go out are
/// whatever <paramref name="Say"/> queues; this is what the fitting reckons
/// the length from, so it has to be the same text.
/// </param>
/// <param name="FromCoHost">
/// True when the co-host says this part. The block puts a beat in wherever
/// this changes between two parts - see CommentaryExchange.HandoffBeat.
/// </param>
/// <param name="Say">
/// Queues it with the announcer. Called on whichever thread is running the
/// block, once, and must not wait for the words to be spoken: the block
/// waits for that itself, which is what lets it put punctuation between two
/// items instead of letting the voice queue run them together.
/// </param>
internal sealed record ShowUtterance(string Words, bool FromCoHost, Action Say);

/// <summary>
/// One item in a show's running order: a segment, or a line of continuity
/// between two of them.
///
/// WHAT AN ITEM IS FOR
///
/// A show used to wrap exactly one segment - open, say one thing, end -
/// which is a jingle round an announcement. A block is open, item,
/// punctuation, item, ending, with the bed running under all of it, and an
/// item is what goes in that list. Each one carries its own words, its own
/// way of saying them, and an estimate of how long it will take, because the
/// gap a block is given is the one thing that decides how much of it is
/// heard - see <see cref="ShowBlock.Fit"/>.
///
/// Built through the three factories rather than a constructor, so that what
/// a caller is making - a segment, a bridge, a two-hander - is the name of
/// the thing they call.
/// </summary>
internal sealed class ShowItem
{
    private ShowItem(
        IReadOnlyList<ShowUtterance> parts,
        SegmentKind? kind,
        bool continuity,
        bool punctuateBefore,
        TimeSpan? estimate)
    {
        Parts = parts;
        Kind = kind;
        IsContinuity = continuity;
        PunctuateBefore = punctuateBefore;
        Estimate = estimate ?? Reckon(parts);
    }

    /// <summary>What is said, in order - see <see cref="ShowUtterance"/>. Never empty.</summary>
    internal IReadOnlyList<ShowUtterance> Parts { get; }

    /// <summary>
    /// Which segment this item is, where it is one. Null for a continuity
    /// line and for anything the caller has no segment for; nothing in the
    /// staging needs it, and it is here because the thing that settles a
    /// segment's budget does.
    /// </summary>
    internal SegmentKind? Kind { get; }

    /// <summary>
    /// True for a bridging line - "still to come, the A-main" - rather than
    /// a segment in its own right.
    ///
    /// It changes what gets DROPPED. A bridge is only worth saying when
    /// there is something on the other side of it, so one whose segment did
    /// not fit goes with it, and one left at the end of a block is taken off
    /// - see <see cref="ShowBlock.Fit"/>. A bridge also never gets the
    /// first-item exemption there: a block that opened the music to promise
    /// something it is not going to say is worse than no block.
    /// </summary>
    internal bool IsContinuity { get; }

    /// <summary>
    /// Whether a mark goes in front of this item - a sting, or a swell of
    /// the bed where the club has no sting file.
    ///
    /// Asked for per item rather than put between every pair, because
    /// punctuation that happens every time stops being punctuation. Ignored
    /// in front of the FIRST item that goes out: the opener is the mark in
    /// front of a block's first word, and a sting on top of it is two
    /// flourishes in the same second.
    /// </summary>
    internal bool PunctuateBefore { get; }

    /// <summary>
    /// How long this is reckoned to take to say, handed in or worked out.
    ///
    /// Worked out through SponsorCopy.SpokenLength, which is the same
    /// estimate the paid sales reads are fitted to a gap with. Deliberately
    /// the same one and not a second answer: it is already on the safe side
    /// (a hundred and fifty words a minute is an unhurried read), and the
    /// cost of being wrong is identical in both places - an estimate that
    /// runs under the real thing is a read spilling into the next race.
    /// </summary>
    internal TimeSpan Estimate { get; }

    /// <summary>True for a two-hander: more than one part, in more than one voice.</summary>
    internal bool IsExchange => Parts.Count > 1 && Parts.Any(p => p.FromCoHost != Parts[0].FromCoHost);

    /// <summary>
    /// True when there is nothing to say: an exchange that came back
    /// unsplittable, or a line the writer left empty.
    ///
    /// <see cref="ShowBlock.Fit"/> leaves these out rather than running
    /// them. A block that ran one would wait out its pacing for silence and
    /// then count it as an item that went out, which is the worst of both -
    /// a gap in the programme AND a segment the next one gets no room for.
    /// Judged on the WORDS and not on the estimate, because the estimate is
    /// what a caller may hand in and the words are what is actually said.
    /// </summary>
    internal bool Silent => Parts.All(p => p.Words.Length == 0);

    /// <summary>
    /// An ordinary segment, said in one voice.
    /// </summary>
    /// <param name="words">What is said - the estimate is taken off this.</param>
    /// <param name="say">Queues it; see <see cref="ShowUtterance.Say"/>.</param>
    /// <param name="kind">Which segment it is, where the caller knows.</param>
    /// <param name="punctuateBefore">Whether a mark goes in front of it - see <see cref="PunctuateBefore"/>.</param>
    /// <param name="estimate">How long it will take, where the caller knows better than the reckoning.</param>
    internal static ShowItem Spoken(
        string? words,
        Action say,
        SegmentKind? kind = null,
        bool punctuateBefore = false,
        TimeSpan? estimate = null) =>
        new(
            new[] { new ShowUtterance(words ?? string.Empty, false, say) },
            kind,
            continuity: false,
            punctuateBefore,
            estimate);

    /// <summary>
    /// A line of continuity between two segments - see
    /// <see cref="IsContinuity"/> for what that changes.
    /// </summary>
    internal static ShowItem Bridge(
        string? words,
        Action say,
        bool punctuateBefore = false,
        TimeSpan? estimate = null) =>
        new(
            new[] { new ShowUtterance(words ?? string.Empty, false, say) },
            kind: null,
            continuity: true,
            punctuateBefore,
            estimate);

    /// <summary>
    /// A two-hander, as CommentaryExchange split it: one part per turn, in
    /// whichever voice the turn belongs to.
    ///
    /// <paramref name="say"/> is called once per turn, in order, and gets
    /// the turn so the caller can pick the voice - which is the caller's
    /// business, because the co-host voice belongs to the announcer's
    /// settings and not to the staging.
    /// </summary>
    internal static ShowItem Exchange(
        IReadOnlyList<ExchangeTurn> turns,
        Action<ExchangeTurn> say,
        SegmentKind? kind = null,
        bool punctuateBefore = false,
        TimeSpan? estimate = null)
    {
        // An exchange that came back unsplittable is an empty list
        // (CommentaryExchange.Split). One part with no words is the honest
        // shape for that, and <see cref="Silent"/> is what keeps the
        // fitting from running it.
        var parts = turns.Count == 0
            ? new[] { new ShowUtterance(string.Empty, false, () => { }) }
            : turns.Select(turn => new ShowUtterance(turn.Line, turn.FromCoHost, () => say(turn))).ToArray();

        return new ShowItem(parts, kind, continuity: false, punctuateBefore, estimate);
    }

    /// <inheritdoc cref="Estimate"/>
    private static TimeSpan Reckon(IReadOnlyList<ShowUtterance> parts)
    {
        var spoken = TimeSpan.Zero;

        for (var i = 0; i < parts.Count; i++)
        {
            spoken += SponsorCopy.SpokenLength(parts[i].Words);

            // The beat at the hand-over is part of what the item costs: it
            // is real time on air, and a block fitted without it overruns
            // its gap by one beat per change of voice.
            if (i > 0 && parts[i].FromCoHost != parts[i - 1].FromCoHost)
            {
                spoken += CommentaryExchange.HandoffBeat;
            }
        }

        return spoken;
    }
}

/// <summary>
/// What a block costs beyond the words themselves, so the fitting can take
/// it off the gap before it starts counting items in.
/// </summary>
/// <param name="Music">
/// Every second between the block opening and the club's music coming back
/// that is not a word being said: the beat of air, the opener in front of
/// the voice, the lift at the end and the bed's own ending. Zero for a block
/// with no music, which costs nothing at either end.
/// </param>
/// <param name="BetweenItems">
/// What the staging spends settling between two items - see
/// ShowTreatment.Pacing. Small, and paid once per item after the first.
/// </param>
/// <param name="Punctuation">
/// What one mark costs: the sting's own length, a swell of the bed, or the
/// beat of silence a block with no files at all can still manage.
/// </param>
internal sealed record ShowBlockCost(TimeSpan Music, TimeSpan BetweenItems, TimeSpan Punctuation)
{
    /// <summary>
    /// Nothing but the words. For a test about the fitting itself, where
    /// the staging's own costs would only be noise in the arithmetic.
    /// </summary>
    internal static readonly ShowBlockCost None = new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
}

/// <summary>
/// How much of a running order the gap had room for, and what came off it.
/// </summary>
/// <param name="Items">What goes out, in order. Empty when nothing does.</param>
/// <param name="Dropped">
/// What does not, in the order it was asked for. These are DROPPED and not
/// deferred: see <see cref="ShowBlock"/> for why nothing here is ever
/// queued for the next gap.
/// </param>
/// <param name="Spent">What <see cref="Items"/> is reckoned to take, the staging's own costs included.</param>
internal sealed record ShowFit(IReadOnlyList<ShowItem> Items, IReadOnlyList<ShowItem> Dropped, TimeSpan Spent)
{
    /// <summary>True when the whole running order fitted.</summary>
    internal bool Whole => Dropped.Count == 0;
}

/// <summary>
/// How a block went.
/// </summary>
/// <param name="WithMusic">
/// Whether there was an opener or a bed under it. False is not a failure -
/// it is a club with no files in, whose block ran as spoken items with the
/// punctuation it could manage.
/// </param>
/// <param name="Said">How many items actually went out.</param>
/// <param name="Dropped">How many did not - the gap's doing, or the race's.</param>
/// <param name="Abandoned">True when a race moved on and the block was cut off mid-item.</param>
/// <param name="Took">How long the whole thing was on air, for the console line.</param>
internal sealed record ShowBlockResult(bool WithMusic, int Said, int Dropped, bool Abandoned, TimeSpan Took)
{
    /// <summary>Nothing went out at all - the gap was too short, or there was nothing to say.</summary>
    internal static readonly ShowBlockResult Nothing = new(false, 0, 0, false, TimeSpan.Zero);

    /// <summary>True when the room heard something.</summary>
    internal bool WentOut => Said > 0;
}

/// <summary>
/// A show's running order: several items in order, with the bed running
/// under all of them and the club's music silenced ONCE for the whole thing.
///
/// WHAT THIS REPLACES
///
/// A show wrapped exactly one segment. Open, say one thing, end. That is a
/// jingle round an announcement, and everything that would make it a
/// programme - continuity between items, punctuation, a directed hand-over
/// between two voices - had nowhere to live. A block is the list, and
/// ShowTreatment.RunBlockAsync is the production: the opener, then item,
/// mark, item, mark, item, then the lift and the ending.
///
/// IT IS BUILT TO FIT A GAP, AND IT DROPS WHAT IT CANNOT FIT
///
/// Ninety seconds of programme between two heats is good. The same ninety
/// seconds with the next race twenty seconds away is a disaster, and no
/// amount of good writing saves it. So a block is told the gap it has and
/// <see cref="Fit"/> decides how much of it is heard - in whole items, never
/// half of one, exactly as SponsorCopy.Fit does it in whole sentences for a
/// paid read, and for the same reason: half a thing said out loud over a PA
/// sounds like a fault.
///
/// WHAT IS DROPPED IS DROPPED
///
/// A sales read picks up where it left off in the sponsor's next gap,
/// because the copy is still true next time. A running order is not: "still
/// to come, the A-main" said after the A-main has started is worse than
/// silence, and so is the second half of a form guide for a race that is
/// already over. Nothing a block drops is ever queued for later. That is the
/// one place this deliberately does NOT follow the sponsor copy's thinking.
/// </summary>
internal sealed class ShowBlock
{
    /// <summary>
    /// The longest a block may run, whatever gap it is told about.
    ///
    /// A cap and not a formality. The gap a caller knows about is a
    /// prediction - the next race goes when Race Control says so - and
    /// every second of a block is a second the club's playlist is silenced
    /// and the voice queue is full of things that are not race calls. A
    /// minute and a half is two or three segments with continuity between
    /// them, which is a programme; past that it is a podcast over the top of
    /// a race meeting.
    /// </summary>
    internal static readonly TimeSpan LongestBlock = TimeSpan.FromSeconds(90);

    /// <summary>
    /// The shortest gap worth opening a block into at all. Under this
    /// nothing goes out: no music, no items, nothing said.
    ///
    /// Longer than SponsorCopy.ShortestUsefulGap, because a block pays for
    /// its own staging before a word is said - the air, the opener, and the
    /// bed's ending on the way out. A block in less than this is the music
    /// and no programme, which is the jingle this feature exists to stop
    /// being.
    /// </summary>
    internal static readonly TimeSpan ShortestUsefulGap = TimeSpan.FromSeconds(12);

    /// <summary>
    /// Kept back out of every gap, on top of the music's own cost.
    ///
    /// The estimate under this is words a minute (SponsorCopy.SpokenLength),
    /// and a synthetic voice reading a name it finds hard, or a club with the
    /// voice speed turned down, takes longer than words a minute says. The
    /// thing on the other side of the gap is a race start, so the margin goes
    /// the generous way: a block that finishes three seconds early is nothing,
    /// where one that finishes three seconds late is talking over the horn.
    /// </summary>
    internal static readonly TimeSpan Headroom = TimeSpan.FromSeconds(3);

    /// <summary>
    /// "There is no race anywhere near this" - the cold open, the sign-off,
    /// a block between two classes. Not actually unlimited: see
    /// <see cref="LongestBlock"/>, which still applies.
    /// </summary>
    internal static readonly TimeSpan NoLimit = TimeSpan.MaxValue;

    /// <summary>
    /// How long the bed is held up for when a swell of it is standing in for
    /// a sting - see ShowTreatment and ShowRun.Swell.
    ///
    /// Long enough to read as a deliberate mark rather than as the duck
    /// letting go, short enough that it is punctuation and not an interlude.
    /// </summary>
    internal static readonly TimeSpan Swell = TimeSpan.FromSeconds(1.1);

    /// <summary>
    /// The mark a block with NO music at all can still manage: a beat of
    /// silence.
    ///
    /// It is not a poor relation. A pause is what a reader uses where a
    /// writer would use a paragraph break, and a club with no files in -
    /// which is a good many of them - gets a block that is paced rather than
    /// a run-on sentence. Slightly longer than the settle between two
    /// ordinary items, so the two are told apart.
    /// </summary>
    internal static readonly TimeSpan DryBeat = TimeSpan.FromSeconds(0.7);

    /// <summary>
    /// The longest a sting may hold a block up, whatever a club dropped in
    /// as one.
    ///
    /// The file is theirs and nothing checks what is in it, so "sting.mp3"
    /// may turn out to be a thirty-second piece of music. Past this the
    /// block carries on over the top of it, which is what a sting is for
    /// anyway.
    /// </summary>
    internal static readonly TimeSpan LongestSting = TimeSpan.FromSeconds(4);

    /// <summary>
    /// How far over the room the first segment may run and still be let out
    /// - see the one rule in <see cref="Fit"/> that is not about room.
    ///
    /// The exemption is there so a club whose segments happen to run a few
    /// seconds longer than tonight's gaps still hears one. It is NOT there
    /// to let a ninety-second form guide out into a twenty-second gap, which
    /// is the exact disaster this class exists to prevent - so an item in a
    /// different league from the room is dropped like any other, and only
    /// one that nearly fitted is let out.
    ///
    /// Eight seconds is the longest overrun this feature is prepared to
    /// cause, and even that is bounded on the other side: a race going to
    /// the grid abandons the block mid-item (ShowTreatment.AbandonBlock),
    /// and a flag or a lap call goes in front of whatever of it has not been
    /// spoken. SponsorCopy's own exemption needs no such bound because it
    /// hands out one SENTENCE at a time; an item here is all or nothing.
    /// </summary>
    internal static readonly TimeSpan Overrun = TimeSpan.FromSeconds(8);

    internal ShowBlock(IEnumerable<ShowItem> items) => Items = items.ToArray();

    /// <summary>The running order as the caller wants it. What is heard is <see cref="Fit"/>'s answer.</summary>
    internal IReadOnlyList<ShowItem> Items { get; }

    /// <summary>
    /// Which segment the block counts as, for the running order's billing -
    /// a sign-off is played out, a cold open is the top of the night. Null
    /// is serviceable: the night's own shape carries everything else. See
    /// ShowRunningOrder.
    /// </summary>
    internal SegmentKind? Kind { get; init; }

    /// <summary>
    /// What is on air, in words a crowd can read - "Race Recap", "Cold
    /// Open". Handed to ShowOnAir.Begin, which is where the Big Screen reads
    /// it from. Null takes <see cref="CaptionFor"/>'s answer for
    /// <see cref="Kind"/>.
    /// </summary>
    internal string? Caption { get; init; }

    /// <summary>
    /// How long the block has before the next race. <see cref="NoLimit"/>
    /// when there is no race near it - see there.
    /// </summary>
    internal TimeSpan Gap { get; init; } = NoLimit;

    /// <summary>
    /// As much of <paramref name="wanted"/> as <paramref name="gap"/> has
    /// room for, in whole items and in the order it was asked for.
    ///
    /// THE ONE RULE THAT IS NOT ABOUT ROOM: the first SEGMENT goes out even
    /// when it does not quite fit - within <see cref="Overrun"/> - so long
    /// as the gap is at least <see cref="ShortestUsefulGap"/>. This is
    /// SponsorCopy.Fit's own rule and it is here for a related reason: a
    /// block that silenced the club's music, played an opener and then said
    /// nothing is the worst thing this can do, and a club whose segments
    /// happen to run four seconds longer than tonight's gaps would otherwise
    /// never hear one. Bounded, unlike the sponsor's, because that one hands
    /// out a sentence at a time and an item here is all or nothing.
    ///
    /// Everything after the first segment has to fit. The first item that
    /// does not closes the block - the running order is an ORDER, so a later
    /// item is not more deserving than the one in front of it, and
    /// reshuffling a programme to fill a gap is how a running order stops
    /// being one.
    ///
    /// WHAT COMES OFF FIRST
    ///
    /// Continuity. A bridge has to fit on its own merits - it gets no
    /// exemption - and one that does not is simply not said, while the
    /// segments either side of it are still tried. A bridge left at the END
    /// of what fitted is taken back off, because a bridge with nothing on
    /// the far side of it promises something the block will not say.
    /// </summary>
    internal static ShowFit Fit(IEnumerable<ShowItem> wanted, TimeSpan gap, ShowBlockCost cost)
    {
        var asked = wanted as IReadOnlyList<ShowItem> ?? wanted.ToList();

        if (asked.Count == 0 || gap < ShortestUsefulGap)
        {
            return new ShowFit(Array.Empty<ShowItem>(), asked, TimeSpan.Zero);
        }

        // Compared rather than Math.Min'd so NoLimit, which is
        // TimeSpan.MaxValue, cannot overflow the subtraction below - the
        // same care SponsorCopy.Fit takes with the same value.
        var room = gap >= LongestBlock ? LongestBlock : gap;

        room -= cost.Music + Headroom;

        // Which of the asked-for items are going out, by position: a running
        // order may hold the same item twice, so nothing here compares items
        // to each other.
        var going = new bool[asked.Count];
        var spent = new TimeSpan[asked.Count];

        var total = TimeSpan.Zero;
        var taken = 0;
        var anySegment = false;
        var closed = false;

        for (var i = 0; i < asked.Count; i++)
        {
            if (closed)
            {
                continue;
            }

            var item = asked[i];

            if (item.Silent)
            {
                // Nothing to say - see ShowItem.Silent. Left out WITHOUT
                // closing the block: the items round it are still worth
                // having, and a segment the model declined to write is not
                // a reason to drop the rest of the programme.
                continue;
            }

            // What this one costs WHERE IT SITS. The settle and the mark are
            // both nothing in front of the first item that goes out: the
            // opener is the punctuation there.
            var first = taken == 0;

            var ask = item.Estimate
                + (first ? TimeSpan.Zero : cost.BetweenItems)
                + (!first && item.PunctuateBefore ? cost.Punctuation : TimeSpan.Zero);

            // The first SEGMENT goes out though it does not quite fit - see
            // the remarks, and Overrun for how far "not quite" goes. A
            // bridge never gets that, and nor does anything once a segment
            // has gone out.
            var exempt = !anySegment && !item.IsContinuity && ask <= room + Overrun;

            if (!exempt && total + ask > room)
            {
                if (item.IsContinuity)
                {
                    // A bridge that will not fit is left out and nothing
                    // else changes - what it was bridging is still worth
                    // having, and the next item is tried as though the
                    // bridge had never been asked for.
                    continue;
                }

                closed = true;
                continue;
            }

            going[i] = true;
            spent[i] = ask;
            total += ask;
            taken++;

            anySegment |= !item.IsContinuity;
        }

        // The bridge at the end, off again - see the remarks. A loop and not
        // one check: two bridges in a row is a running order a caller is
        // entitled to write.
        for (var i = asked.Count - 1; i >= 0; i--)
        {
            if (!going[i])
            {
                continue;
            }

            if (!asked[i].IsContinuity)
            {
                break;
            }

            going[i] = false;
            total -= spent[i];
        }

        var items = new List<ShowItem>();
        var dropped = new List<ShowItem>();

        for (var i = 0; i < asked.Count; i++)
        {
            (going[i] ? items : dropped).Add(asked[i]);
        }

        // Nothing but bridges fitted, so nothing goes out: the music would
        // have opened to say "still to come" and then stopped.
        return items.Count == 0
            ? new ShowFit(Array.Empty<ShowItem>(), asked, TimeSpan.Zero)
            : new ShowFit(items, dropped, total);
    }

    /// <summary>
    /// What to put on the crowd's screen for a segment, in their words
    /// rather than the settings page's.
    ///
    /// Its own small table and not CommentaryBudget.Spec(kind).Title, which
    /// is written for a tick box and is plural for it - "Race recaps" on a
    /// screen in front of a room reads as a heading for a list. Singular,
    /// title case, and short enough to be read at the back of a hall.
    ///
    /// Null where there is nothing worth putting up, which ShowOnAir takes
    /// as "a show with nothing to call itself", and which is the honest
    /// answer for a segment the crowd has no name for.
    /// </summary>
    /// <summary>
    /// What a show calls itself, in words a crowd can read off a screen at
    /// the back of a hall.
    ///
    /// THE ONLY TABLE OF THESE. It was briefly two: this one, and
    /// OnAirTreatment.Words, written the same afternoon by somebody wiring
    /// the Big Screen - and they disagreed on seven of the eleven segments,
    /// so the crowd would have read one set of names and anything else
    /// asking the question would have got the other. OnAirTreatment.Words
    /// now comes here. The wording is that one's, because it follows the
    /// app's own page names ("Club Honours", "Qualifying Report"), which is
    /// the house rule for anything a club reads.
    ///
    /// Null for a kind that is not a show, which is most of them - a battle
    /// call is not a programme.
    /// </summary>
    internal static string? CaptionFor(SegmentKind? kind) => kind switch
    {
        SegmentKind.ColdOpen => "Cold Open",
        SegmentKind.RaceIntro => "Round Intro",
        SegmentKind.RaceRecap => "Race Recap",
        SegmentKind.SignOffNumbers => "Sign-Off",
        SegmentKind.MidMeetingStandings => "Standings So Far",
        SegmentKind.ChampionshipCheck => "Championship Check",
        SegmentKind.Honours => "Club Honours",
        SegmentKind.QualifyingReport => "Qualifying Report",
        SegmentKind.FormGuide => "Form Guide",
        SegmentKind.GridHistory => "The Grid",
        SegmentKind.ThisDayInHistory => "This Day At The Track",
        SegmentKind.MechanicInterview => "In The Pits",
        SegmentKind.ComingUpNext => "Coming Up",
        _ => null,
    };
}

/// <summary>
/// How fast a block moves between the things it says.
///
/// WHY THE BLOCK WAITS AT ALL
///
/// Queueing two items one after the other does not pace them: the
/// announcer's queue speaks them back to back, so a mark queued between them
/// lands after both and a beat of silence lands nowhere. A block therefore
/// says one thing, waits for the room to have heard it, and only then
/// punctuates and goes on. That wait is the whole reason a block sounds like
/// a programme rather than a paragraph.
///
/// WHY IT IS SETTABLE
///
/// Only for the test run. AudioBus is silent for the whole suite and nothing
/// in a test queues a real voice, so <see cref="StartsWithin"/> is spent in
/// full on every item - and a test about which items fitted would be sitting
/// out seconds of pacing to find out. Nothing in the app ever changes it;
/// see ShowTreatment.Pacing.
/// </summary>
/// <param name="StartsWithin">
/// How long the announcer is given to actually START saying what was just
/// queued. A line has to be fetched or synthesised first, and that is not
/// instant; past this the block takes it that the line was dropped or that
/// the announcer is muted, and moves on rather than waiting for a voice that
/// is not coming.
/// </param>
/// <param name="Settle">
/// How long the announcer has to have been quiet before an item counts as
/// said. Short - this is the gap between two items of one programme, not the
/// end of the read, which ShowTreatment.Quiet is about.
/// </param>
/// <param name="Longest">
/// The longest any one item may hold the block up. A voice queue that jams
/// must not leave the bed playing and the club's music silenced.
/// </param>
/// <param name="Tick">How often the two waits above look. Polled for the reason ShowTreatment's own wait is: a stretch of quiet is not an edge.</param>
internal sealed record ShowPacing(TimeSpan StartsWithin, TimeSpan Settle, TimeSpan Longest, TimeSpan Tick)
{
    /// <summary>What a block runs at in a hall.</summary>
    internal static readonly ShowPacing OnAir = new(
        TimeSpan.FromSeconds(2.5),
        TimeSpan.FromSeconds(0.45),
        TimeSpan.FromSeconds(75),
        TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// The same shape, in milliseconds instead of seconds - see the note on
    /// this type. Nothing in the app uses it.
    /// </summary>
    internal static readonly ShowPacing ForTests = new(
        TimeSpan.FromMilliseconds(60),
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromMilliseconds(10));
}
