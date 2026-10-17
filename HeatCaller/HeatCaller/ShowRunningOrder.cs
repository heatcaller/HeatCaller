namespace HeatCaller;

/// <summary>
/// What kind of show this one is - where it sits in the evening, and so
/// how much of a front it gets.
///
/// WHY A SHOW NEEDS TO KNOW THIS
///
/// Every show used to be the same show. The opener from the top, the bed
/// up, the voice over the tail, the ending. Heard once that is a broadcast
/// coming on air; heard eleven times in an evening - which is what a club
/// with race intros ticked actually gets - it is a jingle, and the room
/// stops hearing it some time around the third heat. No broadcast plays its
/// full theme every quarter of an hour. It plays it once at the top, and
/// after that it plays the signature: the back half of the same music, in
/// and out, because the audience has already been told what programme they
/// are listening to.
///
/// So a show is now billed. The first one of the night gets the whole
/// thing. The ones in the body of the evening come back in on the opener's
/// ring-out, which is the same music at a quarter of the length. One after
/// a long quiet spell re-establishes the theme, because by then the room
/// has had time to forget it. And the sign-off is played out - the bed held
/// up on its own for a beat after the last word, so the night ends on
/// music rather than on the announcer stopping.
/// </summary>
internal enum ShowCue
{
    /// <summary>The first show of the night. The opener in full - nobody has heard it yet.</summary>
    TopOfTheNight,

    /// <summary>
    /// A show in the body of the evening, close behind the last one. In on
    /// the opener's ring-out and straight to the voice: the audience knows
    /// the music, and what they want from it is "we're back", not the
    /// whole theme again.
    /// </summary>
    Rejoin,

    /// <summary>
    /// A show after a long quiet spell - a tea break, a long delay, the
    /// second leg of a two-part meeting. The theme is re-established,
    /// because by now it has been long enough that a re-entry sting lands
    /// on nothing.
    /// </summary>
    BackAfterABreak,

    /// <summary>
    /// The way out. The opener in full and then the bed held up on its own
    /// after the last word, so the evening finishes on music.
    /// </summary>
    Playout,
}

/// <summary>
/// What just happened, in the two facts that are worth a bigger open.
///
/// WHY THE SHOW SHOULD KNOW
///
/// The show plays the same whatever the night did. A main decided by four
/// thousandths of a second and a walkover get the same front, the same
/// length of music and the same ending - and the facts to tell them apart
/// have been sitting in the results all along. A broadcast would not throw
/// that away: the open before the big one is a beat longer and the music
/// holds a beat longer after the last word, and nobody in the room could
/// tell you why that race felt like the one that mattered.
///
/// HOW MUCH IT IS ALLOWED TO CHANGE
///
/// Two numbers, each a fraction of a second - see ShowBilling.BiggerAir and
/// ShowBilling.LongerLift. Not the cue, not which part of the opener plays,
/// not the piece of music, not whether there is a sting. The reason is the
/// same reason the rejoin is only a shortening: a club hears their own show
/// eleven times an evening, and a shape that visibly LURCHES does not read
/// as production, it reads as a fault - and the first thing a club does
/// about a fault is turn the feature off. Six tenths of a second on a
/// twenty second show is heard as "they took a breath before that one" by
/// anyone and as nothing at all by everyone else, which is exactly the
/// right amount for a thing nobody asked for.
///
/// NOTHING IS INVENTED. Every field here is a number the app already holds
/// and already says out loud somewhere - the margin at the line that
/// CommentaryService.DescribeMargin reads, the smallest margin of the night
/// that CommentaryShowFacts.SignOff reads, and the track's own lap record
/// (TrackLayout.RecordLapMs). A night with nothing measured is
/// <see cref="None"/> and the show is the show it always was.
/// </summary>
/// <param name="WinningMarginSeconds">
/// How much the winner won by, at the line, in seconds - the same number
/// CommentaryService.DescribeMargin describes, and
/// NightDone.ClosestFinishSeconds for a sign-off. Null when there was
/// nothing to measure: one finisher, no times, or a caller with no race in
/// front of it. Null is NOT zero - zero would make every show without a
/// race the most dramatic one of the night.
/// </param>
/// <param name="LapsDown">
/// How many laps behind the second-place car finished. Anything but zero
/// takes the margin out of it altogether, however small the seconds look:
/// a car a lap down can cross the line a tenth after the winner, and the
/// recap that called that a photo finish is the bug
/// CommentaryService.DescribeMargin's lap-difference rule exists for.
/// </param>
/// <param name="TrackRecord">
/// Whether the track's own lap record went in this race. The one fact that
/// is worth a bigger open on its own merits - a record stands until it
/// does not, and a club that breaks theirs on a Tuesday deserves the show
/// to notice.
/// </param>
internal readonly record struct ShowDrama(double? WinningMarginSeconds, int LapsDown, bool TrackRecord)
{
    /// <summary>
    /// Nothing measured, nothing to act on - the show every show was before
    /// this existed. The default of the struct, so a caller that leaves the
    /// argument out gets it without having to know it is there.
    /// </summary>
    internal static readonly ShowDrama None = default;

    /// <summary>
    /// What one race's finish was worth, for a recap.
    /// </summary>
    /// <param name="marginSeconds">The winning margin at the line, or null when there was nothing to measure.</param>
    /// <param name="lapsDown">How many laps down second place finished - see <see cref="LapsDown"/>.</param>
    /// <param name="trackRecord">Whether the track's lap record went in this one.</param>
    internal static ShowDrama Finish(double? marginSeconds, int lapsDown = 0, bool trackRecord = false) =>
        new(marginSeconds, lapsDown, trackRecord);

    /// <summary>
    /// What the night was worth, for the sign-off.
    ///
    /// Reads the one fact the night already keeps for exactly this purpose -
    /// the smallest winning margin of the evening, which the sign-off is
    /// about to read out anyway. Zero means nothing was measured (see
    /// NightDone), which is why it becomes null and not a margin of nothing.
    /// </summary>
    internal static ShowDrama Night(NightDone night) =>
        new(night.ClosestFinishSeconds > 0 ? night.ClosestFinishSeconds : null, 0, false);

    /// <summary>
    /// Whether this one gets the bigger open.
    ///
    /// ONE STEP, NOT A SCALE. A dial from nothing to everything would give a
    /// club four lengths of the same show and no way to tell which one they
    /// were hearing or why - and the whole case for this feature is that it
    /// is not noticed as a feature. Either the night did something worth a
    /// beat or it did not.
    ///
    /// THE THRESHOLD IS NOT A NEW ONE. CommentaryShowFacts.CloseFinishSeconds
    /// is already what this app means by a close finish: it is the margin
    /// the sign-off will say "the closest finish all night" about, and the
    /// same half second DescribeMargin calls a photo finish. A third number
    /// here would be a third answer to one question, and the show would
    /// have swelled for a race the words were about to call comfortable.
    /// </summary>
    internal bool Notable =>
        TrackRecord
        || (LapsDown == 0
            && WinningMarginSeconds is { } margin
            && margin > 0
            && margin <= CommentaryShowFacts.CloseFinishSeconds);
}

/// <summary>
/// One show's billing: the production decisions that make this show
/// different from the last one, as numbers the staging can act on.
///
/// Held as a record rather than as a pile of switches inside ShowTreatment
/// so that the whole production - all four shapes, side by side - reads as
/// one table in <see cref="For"/>, and so a test can ask what the show was
/// going to do without listening to it.
///
/// EVERYTHING HERE DEGRADES. Each number is a length of time or a flag,
/// and every one of them is meaningful with no opener, with no bed, and
/// with no model behind the words - which is the state a good many clubs
/// run in. A show with neither piece of music never gets this far: see
/// ShowTreatment.OpenShowAsync, which returns null before anything is
/// billed.
/// </summary>
/// <param name="Cue">Where in the evening this show sits.</param>
/// <param name="EnterOpenerLate">
/// Whether to come in on the opener's ring-out instead of playing it from
/// the top - see <see cref="OpenerFrom"/>.
/// </param>
/// <param name="AirSeconds">
/// A beat of silence in front of the opener. The club's music is silenced
/// the instant a show opens (ShowOnAir), so without this the playlist
/// stops dead and the opener lands in the same sample - which reads as the
/// music having cut out rather than as a station going live. Carried as
/// silence at the head of the opener's own provider rather than as a wait,
/// so it is sample-exact and costs the staging no extra await.
/// </param>
/// <param name="BedAloneSeconds">
/// How long the bed has to itself before the voice, for a club with a bed
/// and no opener. Without it the first word starts in the same instant as
/// a bed fading up from nothing, and the two smother each other. This is
/// the only number here that is a real wait, and it is short on purpose:
/// a race intro's show is awaited by the staging chain.
/// </param>
/// <param name="LiftSeconds">
/// How long the bed is brought up on its own once the read is over, before
/// the ending starts - the oldest punctuation in radio. The bed coming up
/// as the talk stops is what tells a room that something was a programme
/// rather than an announcement. Zero means the show ends as it always did.
/// </param>
/// <param name="PlayoutSeconds">
/// Extra bed, on top of the lift, for the end of the night. Only the
/// sign-off gets this: it is the one show where the music finishing IS the
/// point, and a couple of seconds of it is the difference between a night
/// that ended and a night that stopped.
/// </param>
/// <param name="BiggerOpen">
/// Whether the night earned this one a beat more at both ends - see
/// <see cref="ShowDrama"/>. Nothing in the staging reads this: the beat is
/// already IN <see cref="AirSeconds"/> and <see cref="LiftSeconds"/> by the
/// time a billing is handed out, which is the point of folding it into the
/// numbers rather than adding a switch for the staging to honour. It is
/// here so the developer console can say why a show was a shade longer, and
/// so a test can tell a bigger open from a club who simply set a longer
/// one.
/// </param>
internal sealed record ShowBilling(
    ShowCue Cue,
    bool EnterOpenerLate,
    double AirSeconds,
    double BedAloneSeconds,
    double LiftSeconds,
    double PlayoutSeconds,
    bool BiggerOpen)
{
    /// <summary>
    /// The extra beat of air in front of the opener when the night earned it
    /// - see <see cref="ShowDrama"/>.
    ///
    /// A quarter of a second. Long enough that the open does not start on
    /// the same edge as every other night's, short enough that nobody can
    /// name what is different. It is also time the club's music is silenced
    /// with nothing playing, which is why it is not half a second.
    /// </summary>
    internal const double BiggerAir = 0.25;

    /// <summary>
    /// The extra beat the bed is held up for after the last word when the
    /// night earned it - see <see cref="ShowDrama"/>.
    ///
    /// The larger of the two, because this is the end a room actually hears:
    /// the music holding a moment longer after "and that is the closest
    /// finish we have had all season" is the oldest trick in broadcasting
    /// and the only one here that is ever consciously noticed. Still a third
    /// of a second.
    /// </summary>
    internal const double LongerLift = 0.35;

    /// <summary>
    /// How much of the opener has to be left after its ring-out starts
    /// before coming in late is worth doing.
    ///
    /// Deliberately the sum of ShowTreatment's own two guards - the voice
    /// must have some opener alone in front of it and some left to talk
    /// over - because a late entry that cannot satisfy both is not a
    /// shorter version of the theme, it is a hit with the hit cut off. A
    /// club whose opener is a one-second stab therefore always hears the
    /// whole stab, which is also the right answer: there was nothing to
    /// shorten.
    /// </summary>
    private const double LeastLeftToEnterLate = 1.3;

    /// <summary>
    /// The four shapes, side by side.
    ///
    /// The numbers are small on purpose. Every one of them is time the
    /// club's music is silenced and - for a race intro's show - time the
    /// staging chain spends waiting, so each has to earn itself. The air
    /// and the lift are the two that do most of the work, and neither is
    /// more than a beat.
    /// </summary>
    /// <param name="cue">Where in the evening this show sits.</param>
    /// <param name="drama">
    /// What the night just did, where the caller has a race or a night to
    /// read it off - see <see cref="ShowDrama"/>. Left out means
    /// <see cref="ShowDrama.None"/>, which is every show this feature ever
    /// played before today.
    /// </param>
    internal static ShowBilling For(ShowCue cue, ShowDrama drama = default)
    {
        // The full front: the biggest air, the whole opener, the longest
        // lift. Also what anything unrecognised falls back to - a cue from
        // a newer build read back by an older one should sound like the
        // thing this feature started as, not like nothing.
        var top = new ShowBilling(ShowCue.TopOfTheNight, false, 0.40, 0.50, 1.00, 0, false);

        var billing = cue switch
        {
            // Brisk at both ends. Coming back from a break is not an
            // occasion, and lingering over it is what made the old shape
            // tiring by the third heat.
            ShowCue.Rejoin => new ShowBilling(cue, true, 0.15, 0.20, 0.70, 0, false),

            // Between the two: the theme is worth re-establishing, but the
            // room has already met it tonight.
            ShowCue.BackAfterABreak => new ShowBilling(cue, false, 0.30, 0.40, 0.90, 0, false),

            // The one show that is allowed to take its time.
            ShowCue.Playout => new ShowBilling(cue, false, 0.40, 0.50, 1.20, 1.60, false),

            _ => top,
        };

        // THE BIGGER OPEN, folded into the two numbers the staging already
        // reads. Deliberately not a switch for the staging to honour: a
        // third thing ShowTreatment has to remember to act on is a third
        // way for the club's music not to come back, which is the one fault
        // in this area that costs a club their evening. A longer beat of
        // air and a longer lift are a longer beat of air and a longer lift
        // whoever is reading them.
        //
        // Added to whatever the cue was, never replacing it - so a rejoin
        // with a photo finish behind it is still a rejoin, and still comes
        // in on the ring-out. The night may make a show a shade bigger; it
        // may not make it a different show.
        if (!drama.Notable)
        {
            return billing;
        }

        return billing with
        {
            BiggerOpen = true,
            AirSeconds = billing.AirSeconds + BiggerAir,
            LiftSeconds = billing.LiftSeconds + LongerLift,
        };
    }

    /// <summary>
    /// Which frame of the opener this show starts playing it at. Zero -
    /// the top - unless this is a show that comes in late and the piece
    /// has enough ring-out to come in on.
    /// </summary>
    internal int OpenerFrom(ShowClip opener) => EnterOpenerLate ? EntersLateAt(opener) : 0;

    /// <summary>
    /// Where a late entry lands: the point the opener stops hitting and
    /// starts ringing out, which is exactly what ShowShape measures and
    /// exactly where ShowTreatment already brings the voice in on a full
    /// open. The two agreeing is the whole trick - the back of the piece
    /// is the part the room recognises, because on a full open it is the
    /// part they heard the announcer over.
    ///
    /// Zero when there is too little left to be heard as anything, so the
    /// whole piece is played instead. Also used by the Commentary page to
    /// tell a club what their own file is going to do, which is otherwise
    /// unknowable: this is measured, not set.
    /// </summary>
    internal static int EntersLateAt(ShowClip opener)
    {
        var ringOut = Math.Clamp(opener.Shape.LoopEnd, 0, opener.Frames);
        var left = opener.Frames - ringOut;

        return left >= (int)(LeastLeftToEnterLate * opener.SampleRate) ? ringOut : 0;
    }
}

/// <summary>
/// The evening's running order: what has already gone out, and therefore
/// what this next show should be.
///
/// WHY AN INSTANCE AND NOT A STATIC
///
/// ShowOnAir is static because the thing it talks to - the club's music
/// player - lives several levels up in MainForm and has no way to be
/// handed anything. This has the opposite shape: it is only ever read by
/// the one ShowTreatment that stages every show (see
/// AnnouncerControl.ShowMusicTreatment, which hands the same instance to
/// Race Control). Static state here would buy nothing and cost what
/// ShowOnAir.ResetForTests exists to undo - a test that staged a show
/// leaving every test after it in the middle of somebody else's evening.
/// An instance is fresh per treatment and so fresh per test.
///
/// LOCKED ANYWAY. One treatment, but two callers: the announcer screen's
/// director stages the set pieces and Race Control stages the intros and
/// the recaps, and those are separate chains that have overlapped before
/// (see ShowSpeaking for what that cost last time). The lock is a handful
/// of instructions once per show.
/// </summary>
internal sealed class ShowRunningOrder
{
    /// <summary>
    /// How long a quiet stretch has to be before the next show
    /// re-establishes the theme rather than just rejoining.
    ///
    /// Twenty minutes is about a tea break, a battery charge or a long
    /// delay - and comfortably longer than the gap between two heats,
    /// which is the gap this must NOT treat as a break. A club running
    /// back to back gets rejoins all evening, which is the point.
    /// </summary>
    internal static readonly TimeSpan AfterABreak = TimeSpan.FromMinutes(20);

    private readonly object _gate = new();

    /// <summary>When the last show opened. MinValue until one has.</summary>
    private DateTime _lastShowUtc = DateTime.MinValue;

    private int _shows;

    /// <summary>How many shows have opened tonight. For the test run.</summary>
    internal int ShowsTonight
    {
        get
        {
            lock (_gate)
            {
                return _shows;
            }
        }
    }

    /// <summary>
    /// What the next show should be, without recording it - see
    /// <see cref="Went"/> for why the two are apart.
    /// </summary>
    /// <param name="kind">
    /// The segment being staged, where the caller knows it. Null is
    /// perfectly serviceable: the night's own shape carries everything
    /// except the two cues that are about a segment rather than about a
    /// time of night.
    /// </param>
    /// <param name="rejoinAllowed">
    /// The club's setting (AppSettings.ShowRejoinEnabled). Read fresh each
    /// time rather than held, because a club turning it off halfway
    /// through an evening means the next show, not the next night. It only
    /// takes the SHORTENED opener away - a rejoin still runs brisk at both
    /// ends, because that part is about where the show sits and not about
    /// how much music a club wants to hear.
    /// </param>
    /// <param name="drama">
    /// What the thing this show is about actually did, where the caller has
    /// a race or a night to read it off - see <see cref="ShowDrama"/>. Left
    /// out means None, which is every show before today.
    ///
    /// BESIDE THE CLOCK AND UNDER IT. The clock decides which SHOW this is
    /// and the night only decides whether that show gets a beat more at
    /// each end: a photo finish cannot promote a rejoin into a full open,
    /// because the room hearing the whole theme twice in two minutes is a
    /// worse fault than a thrilling race going unmarked. See
    /// ShowBilling.For.
    /// </param>
    internal ShowBilling Billing(SegmentKind? kind, DateTime nowUtc, bool rejoinAllowed, ShowDrama drama = default)
    {
        // NOT IN FRONT OF A RACE. A race intro's show is AWAITED by the
        // staging chain that is about to call the next race to the grid
        // (RaceControlControl.SpeakRoundIntro), so every tenth of a second
        // added here is a tenth the race is called later - and a race call
        // always wins. The beat is for the shows that have nothing waiting
        // behind them, which is every other one.
        //
        // Belt and braces: the caller has a race's facts at exactly the
        // moment it is staging the intro for the NEXT race, which is the
        // easiest wiring mistake to make in this area and the hardest to
        // hear.
        var earned = kind == SegmentKind.RaceIntro ? ShowDrama.None : drama;

        var billing = ShowBilling.For(Cue(kind, nowUtc), earned);

        return rejoinAllowed ? billing : billing with { EnterOpenerLate = false };
    }

    private ShowCue Cue(SegmentKind? kind, DateTime nowUtc)
    {
        // The two the clock cannot work out.
        //
        // A sign-off is the end of the night whenever it is read, and a
        // cold open is the top of one even when a club spent the half hour
        // before it setting their levels with Play It - those test presses
        // are shows as far as the clock is concerned, and letting them
        // turn the real cold open into a rejoin would cost a club the one
        // full opener of the evening that everybody is listening for.
        if (kind == SegmentKind.SignOffNumbers)
        {
            return ShowCue.Playout;
        }

        if (kind == SegmentKind.ColdOpen)
        {
            return ShowCue.TopOfTheNight;
        }

        lock (_gate)
        {
            if (_lastShowUtc == DateTime.MinValue)
            {
                return ShowCue.TopOfTheNight;
            }

            // A clock that has gone backwards - an NTP correction mid
            // evening - reads as no time having passed, so the next show
            // is a rejoin. The conservative way round: an extra rejoin is
            // a shorter opener, where an extra full open is the theme
            // playing twice in two minutes.
            return nowUtc - _lastShowUtc >= AfterABreak ? ShowCue.BackAfterABreak : ShowCue.Rejoin;
        }
    }

    /// <summary>
    /// Records that a show actually went out, which is what the next one's
    /// billing is worked out from.
    ///
    /// APART FROM <see cref="Billing"/> ON PURPOSE. A show can be billed
    /// and then never heard - the start cancelled inside the opener when
    /// the next race is called to the grid, or the audio device gone. If
    /// billing recorded it, that unheard show would spend the night's one
    /// full opener and the show after it would come in late on a theme the
    /// room had never been given. So ShowTreatment calls this at the point
    /// it hands the run back, and a start that fell over leaves the
    /// evening where it was: the next show plays the whole thing, which is
    /// never the wrong answer, only sometimes a generous one.
    /// </summary>
    internal void Went(DateTime nowUtc)
    {
        lock (_gate)
        {
            _shows++;
            _lastShowUtc = nowUtc;
        }
    }

    /// <summary>
    /// A fresh evening: the next show is the top of the night again.
    ///
    /// Nothing in the app calls this yet - there is no seam from the race
    /// day being opened down to the treatment, and the twenty minute rule
    /// above covers the ordinary case of a club setting levels and then
    /// racing. Here, tested, and called by ShowTreatment.NewNight, so that
    /// seam is one line when somebody wants it.
    /// </summary>
    internal void NewNight()
    {
        lock (_gate)
        {
            _shows = 0;
            _lastShowUtc = DateTime.MinValue;
        }
    }
}
