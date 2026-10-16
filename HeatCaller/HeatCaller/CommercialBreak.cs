namespace HeatCaller;

/// <summary>
/// Why a commercial break is over, or that it is not.
///
/// An answer rather than a bool, because the four endings are not
/// interchangeable: three of them are the break doing its job and one is
/// the night having moved on without it, they read differently in the
/// console, and which one happened is the only thing worth knowing about a
/// break that went wrong. See <see cref="CommercialBreak.Over"/>.
/// </summary>
internal enum BreakEnding
{
    /// <summary>Not over - the sponsors keep the screen.</summary>
    Running,

    /// <summary>
    /// The next race has been called to the grid. First of all of them and
    /// not subject to the floor: adverts still running over the next
    /// countdown are worse than no adverts at all.
    /// </summary>
    NextRaceCalled,

    /// <summary>The break has run as long as one may - see <see cref="CommercialBreak.Longest"/>.</summary>
    Ceiling,

    /// <summary>The recap is written AND rendered, which is what the break was covering.</summary>
    RecapReady,

    /// <summary>
    /// The rotation has been round every sponsor the club has. The break
    /// ends rather than playing them again - see
    /// <see cref="CommercialBreak.ShownEverything"/>.
    /// </summary>
    EverySponsorShown,
}

/// <summary>
/// Says when the club is in a commercial break after a race, so the Big
/// Screen can hand its rotation over to the sponsors for the duration and
/// the on-air band can say what is on.
///
/// WHY THERE IS A BREAK AT ALL (10 Oct 2026)
///
/// The recap takes the model time to write - rehearsal 5 had it take 41.6s
/// and 37.5s - and the teaser ("give us a second and we'll tell you how
/// that one unfolded") was all there was to cover it. A station covers that
/// wait with the adverts, which is also what the club was paid for, so the
/// after-race running order is now: finish call, badge calls, teaser,
/// COMMERCIAL BREAK, recap show, honours, interview, sign-off. The break is
/// exactly as long as the model needed. See
/// RaceControlControl.HoldTheCommercialBreakAsync, which is the one thing
/// that starts and ends one.
///
/// WHY STATIC, AND WHY NOT AN EVENT
///
/// Static for exactly the reason AdBreak and ShowOnAir are: the break is
/// run by Race Control's after-race sequence on a background task, and the
/// Big Screen is a separate window - built by Race Control AND by Practice
/// Mode, each already passing a dozen optional arguments - several levels
/// away. Threading a "the adverts are on" callback down that chain would
/// mean changing both call sites and both constructors to carry something
/// neither window has an opinion about. Here the sequence says what is
/// happening and the screen, which owns the rotation, decides what to do
/// about it.
///
/// Deliberately NOT an event, which is the one way this differs from those
/// two. A subscriber would be a static event holding a window for the life
/// of the process, and it would be called inside <see cref="Gate"/> - where
/// the one rule is that nothing may block. The Big Screen already asks
/// ShowOnAir whether a show is on twice a second and makes the band agree
/// (BigScreenForm.UpdateOnAir), for the stated reason that furniture with
/// exactly one route off it stays on the moment that route is missed. The
/// break is asked on the same tick, in the same place, and nothing is
/// remembered between calls that could go stale.
///
/// WHAT THIS DOES NOT TOUCH
///
/// The club's music, and that is a decision rather than an oversight. A
/// break of still slides has no sound of its own, and a station plays a bed
/// under its slides - silencing the music here would turn the 40 seconds
/// this exists to fill into 40 seconds of dead air. A sponsor VIDEO with
/// sound still quiets the music exactly as it always did, counted by
/// AdBreak, which the rotation drives per video and not per break. The paid
/// sales reads ride those tails unchanged - see
/// CommentaryDirector.NextAdBreakRead.
///
/// WHAT GOES WRONG IF A BREAK IS NEVER ENDED
///
/// The rotation runs one slide into the next for as long as this says a
/// break is on, so a break left open would keep the standings off the
/// crowd's wall for the rest of the evening and leave the band reading
/// COMMERCIAL BREAK over every heat. And it gets worse than furniture: a
/// club with video sponsors would have the rotation start another video
/// every pass, AdBreak would quiet the club's music under each one, and the
/// night ends up in exactly the place AdBreak's own notes were written
/// about. So the count is the same shape as theirs - counted once, ended
/// once, never below zero - and the one caller holds it in a try/finally.
/// </summary>
internal static class CommercialBreak
{
    /// <summary>
    /// What the on-air band says while the break is on, in the words the
    /// club owner used for it. The band upper-cases and folds it to one
    /// line itself - see OnAirTreatment.Caption.
    ///
    /// A plain constant and NOT a SegmentKind with a caption in
    /// ShowBlock.CaptionFor. That table is the crowd's names for things the
    /// brain writes; an advert break is written by a sponsor, costs no
    /// lines, sits on no tier and is never offered the show's music. A
    /// SegmentKind for it would need a SegmentSpec to go with it
    /// (CommentaryBudgetTests holds AllSpecs to the length of the enum), and
    /// that would put a tick box for the adverts in the middle of the
    /// Commentary page's list of things the model says.
    /// </summary>
    internal const string OnAirCaption = "Commercial Break";

    /// <summary>
    /// The shortest a break may be, however quickly the recap arrives.
    ///
    /// A recap can be in hand almost at once - a club running a big model on
    /// a box in the corner, or one whose last race asked much the same
    /// question - and with no floor the break would be the crowd's screen
    /// flicking to a sponsor and back inside a second. That reads as a fault
    /// rather than as a decision, and it is worth nothing at all to the
    /// sponsor who paid for it.
    ///
    /// Eight seconds is one turn of the rotation at SponsorSettings'
    /// SlideSeconds default, so the shortest break is one whole slide held
    /// still; and it is longer than SponsorCopy.ShortestUsefulGap, which is
    /// the shortest gap a paid read is worth starting into at all - so a
    /// read the break earns has somewhere to go.
    /// </summary>
    internal static readonly TimeSpan Shortest = TimeSpan.FromSeconds(8);

    /// <summary>
    /// The longest a break may run before it ends and the sequence goes back
    /// to simply waiting for the model.
    ///
    /// The break is here to cover the writing, so it has to outlast a slow
    /// night: the worst the model managed in rehearsal 5 (10 Oct 2026) was
    /// 41.6s to write and render, and a ceiling anywhere near that would cut
    /// short exactly the nights this was asked for. Ninety seconds is more
    /// than twice it, and it is the same ninety that
    /// RaceControlControl.RecapLineLimit and
    /// CommentaryDirector.QuietAfterRead both settled on for the same
    /// reason: longer than anything that legitimately takes this long,
    /// shorter than a heat.
    ///
    /// What happens after it is deliberately NOTHING. The adverts stop, the
    /// standings go back up, and the room waits for the recap exactly as it
    /// did before there was a break - and the recap still goes out when it
    /// arrives. The ceiling is here so a model that has hung cannot go on
    /// selling a sponsor impressions nobody bought and keep the crowd's
    /// screen off the board all evening; it is not a deadline for the recap,
    /// which has its own.
    /// </summary>
    internal static readonly TimeSpan Longest = TimeSpan.FromSeconds(90);

    /// <summary>
    /// How often the break looks at whether it is over. The Big Screen's own
    /// rotation tick, so the two never disagree by more than one of them;
    /// and polled rather than awaited for the reason ShowTreatment's own
    /// wait is polled - the end of a break is a stretch of state rather than
    /// an edge, and four separate things can bring it about.
    /// </summary>
    internal static readonly TimeSpan Look = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Whether the break is over, and why - the whole of the decision, as
    /// one function of four facts and no clock of its own.
    ///
    /// PURE, AND HERE, for the reason OnAirTreatment.Showing and
    /// RollCall.Showing are: the sequence that drives this needs a model, a
    /// race, a sound device and about three quarters of a minute to say any
    /// of it out loud, and the floor and the ceiling are the two things in
    /// this feature a club would actually notice being wrong. Driven
    /// directly, every case is a line in a test.
    ///
    /// THE ORDER OF THE FOUR IS THE DESIGN.
    ///
    /// The next race first, and ahead of the floor: a race called to the
    /// grid abandons the break at once, however short it has been.
    ///
    /// The ceiling second, and also ahead of the floor, which costs nothing
    /// because the ceiling is far longer than the floor - but it says
    /// plainly that nothing can hold the adverts on past it.
    ///
    /// Then THE FLOOR, as the one condition on both of the endings that are
    /// the break doing its job. A recap that arrives in a second does not
    /// get to cut the advert off, and nor does a club with one slide.
    /// </summary>
    /// <param name="on">How long the break has been running.</param>
    /// <param name="recapReady">The recap has been written AND rendered - or has failed, which is just as much a reason to stop.</param>
    /// <param name="shownEverything">The rotation has been round every sponsor - see <see cref="ShownEverything"/>.</param>
    /// <param name="nextRaceCalled">The next race has been staged, counted down or started.</param>
    internal static BreakEnding Over(TimeSpan on, bool recapReady, bool shownEverything, bool nextRaceCalled, bool videoOnScreen = false)
    {
        if (nextRaceCalled)
        {
            return BreakEnding.NextRaceCalled;
        }

        if (on >= Longest)
        {
            return BreakEnding.Ceiling;
        }

        if (on < Shortest)
        {
            return BreakEnding.Running;
        }

        // AN ADVERT ON THE SCREEN KEEPS THE BREAK (11 Oct 2026). Above the
        // ceiling check, so a stalled video still cannot hold the recap -
        // and below the next-race check, because a grid forming beats a
        // sponsor every time. See VideoOnScreen.
        if (videoOnScreen)
        {
            return BreakEnding.Running;
        }

        if (recapReady)
        {
            return BreakEnding.RecapReady;
        }

        return shownEverything ? BreakEnding.EverySponsorShown : BreakEnding.Running;
    }

    /// <summary>
    /// How long a Big Screen's report that it could carry a break is good
    /// for. The window says so on its own half-second tick, so this is six
    /// of them: long enough that a UI thread busy drawing a finished board
    /// does not lose the club its break, short enough that a screen closed
    /// during the finish call is not still counted when the teaser ends.
    ///
    /// A heartbeat rather than a count the window puts up on open and takes
    /// down on close, which is what AdBreak does for a video. A count can be
    /// got wrong in one direction only by leaking high - and a break held
    /// open against a screen that is not there would delay every recap of
    /// the night by the floor with nothing on the wall to show for it. A
    /// report that stops arriving cannot leak.
    /// </summary>
    private static readonly TimeSpan ScreenReportGoodFor = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Serialises the count, the caption's conditions and the screen's
    /// report together, for the reason AdBreak's own gate gives: a Begin on
    /// one thread and an End on another could otherwise be counted in one
    /// order and seen in the other.
    ///
    /// Nothing is raised from inside it - see the note on the class - so
    /// unlike AdBreak's and ShowOnAir's, no handler can block in here and
    /// nothing in the app can deadlock on it. Behind each member is a
    /// compare and a field read.
    /// </summary>
    private static readonly object Gate = new();

    private static int _running;

    /// <summary>How many breaks have begun this run - see <see cref="BreaksBegun"/>.</summary>
    private static int _breaksBegun;

    /// <summary>Paid reads being spoken inside the break right now - see <see cref="ReadOnAir"/>.</summary>
    private static int _readsOnAir;

    /// <summary>
    /// How many commercial breaks have begun since the app started. The
    /// commentary director compares it with the count at the last flag: a
    /// break that has run since is where tonight's paid read went, so no
    /// other read follows it (16 Oct 2026 - see
    /// CommentaryDirector.ReadsBelongHere).
    /// </summary>
    internal static int BreaksBegun
    {
        get
        {
            lock (Gate)
            {
                return _breaksBegun;
            }
        }
    }

    /// <summary>
    /// Whether a sponsor's copy is being read inside the break right now
    /// (16 Oct 2026). The Big Screen holds a still slide rather than start
    /// a video under it - two adverts over each other sell neither - and
    /// the break does not end beneath it. See
    /// RaceControlControl.ReadTheSponsorsInTheBreakAsync.
    /// </summary>
    internal static bool ReadOnAir
    {
        get
        {
            lock (Gate)
            {
                return _readsOnAir > 0;
            }
        }
    }

    /// <summary>A paid read has started inside the break. Pair with <see cref="ReadEnds"/>, from a finally.</summary>
    internal static void ReadBegins()
    {
        lock (Gate)
        {
            _readsOnAir++;
        }
    }

    /// <summary>The paid read inside the break is over. Never counts below zero.</summary>
    internal static void ReadEnds()
    {
        lock (Gate)
        {
            if (_readsOnAir > 0)
            {
                _readsOnAir--;
            }
        }
    }

    /// <summary>
    /// True once the rotation has been round every one of the club's slides
    /// during this break - see <see cref="EverySponsorShown"/>. Cleared by
    /// <see cref="Begin"/>, so it is always about the break that is on.
    /// </summary>
    private static bool _shownEverything;

    /// <summary>
    /// When a Big Screen last said it could carry a break, from
    /// Environment.TickCount64 - monotonic, so a club PC whose clock is
    /// corrected mid-evening cannot make a stale report look fresh.
    /// Negative until one ever has.
    /// </summary>
    private static long _screenSaidAt = -1;

    /// <summary>When a Big Screen last said a sponsor video was on its screen - see <see cref="VideoOnScreen"/>.</summary>
    private static long _videoSaidAt = -1;

    /// <summary>Whether the club is in a commercial break right now.</summary>
    internal static bool Running
    {
        get
        {
            lock (Gate)
            {
                return _running > 0;
            }
        }
    }

    /// <summary>
    /// Whether the rotation has already been round every slide the club
    /// has. The break ENDS on this rather than starting again: looping the
    /// rotation would sell a sponsor impressions nobody bought, and holding
    /// is what a station does. What fills the rest of the wait is the quiet
    /// before the recap's own opener, exactly as it was before there was a
    /// break at all.
    ///
    /// False between breaks, so the sequence can read it without asking
    /// whether one is on.
    /// </summary>
    internal static bool ShownEverything
    {
        get
        {
            lock (Gate)
            {
                return _running > 0 && _shownEverything;
            }
        }
    }

    /// <summary>
    /// Whether a Big Screen has said lately that it could carry a break -
    /// open, with the club's slides switched on and at least one of them to
    /// show. See <see cref="ScreenReportGoodFor"/>.
    ///
    /// The one gate that keeps this feature additive. A club with no
    /// sponsor slides, with them switched off, or with no Big Screen open
    /// has nothing to put in a break, and a break with nothing in it is
    /// silence added in front of the recap - which is a regression and not
    /// a feature. Asked at the moment the break would start rather than at
    /// the flag, because a volunteer opening the screen during the finish
    /// call should get their adverts.
    /// </summary>
    internal static bool SponsorScreenUp
    {
        get
        {
            lock (Gate)
            {
                return _screenSaidAt >= 0
                       && Environment.TickCount64 - _screenSaidAt <= (long)ScreenReportGoodFor.TotalMilliseconds;
            }
        }
    }

    /// <summary>
    /// A Big Screen saying it could carry a break right now. Called from the
    /// window's own half-second tick, on the UI thread, and only while the
    /// club's slides are switched on and there is at least one to show.
    /// </summary>
    internal static void SponsorScreenReady()
    {
        lock (Gate)
        {
            _screenSaidAt = Environment.TickCount64;
        }
    }

    /// <summary>
    /// Whether a sponsor's VIDEO is on the crowd's screen right now.
    ///
    /// ADDED 11 Oct 2026, to stop a break ending underneath an advert. The
    /// break's floor is eight seconds - one turn of the rotation - and it
    /// ends at the next look once the recap is written. A video is almost
    /// always longer than that, so a break that reached one would hand the
    /// screen back part way through it and the room would get half an
    /// advert, or none of it: the club sold a thirty-second spot and the
    /// crowd saw eight seconds of it under a recap.
    ///
    /// Reported on the window's own tick rather than counted up and down,
    /// for the reason <see cref="SponsorScreenUp"/> states: a report that
    /// stops arriving is safe, where a count got wrong in the wrong
    /// direction would hold the sponsors over the crowd's screen.
    ///
    /// NOT A WAY ROUND THE CEILING. <see cref="Longest"/> still ends the
    /// break - a stalled video must not keep the recap off the air.
    /// </summary>
    internal static bool VideoOnScreen
    {
        get
        {
            lock (Gate)
            {
                return _videoSaidAt >= 0
                       && Environment.TickCount64 - _videoSaidAt <= (long)ScreenReportGoodFor.TotalMilliseconds;
            }
        }
    }

    /// <summary>
    /// A Big Screen saying a sponsor's video is on its screen right now.
    /// Called from the window's own half-second tick, on the UI thread.
    /// </summary>
    internal static void SponsorVideoOnScreen()
    {
        lock (Gate)
        {
            _videoSaidAt = Environment.TickCount64;
        }
    }

    /// <summary>
    /// The rotation has been round every slide the club has. Said by the
    /// Big Screen, which is the only thing that knows - the sequence knows
    /// how long it has been waiting and nothing about what is on the wall.
    ///
    /// Harmless when no break is on and harmless twice: two screens each
    /// running their own rotation both say it, within a slide of each other,
    /// and the first one is the answer.
    /// </summary>
    internal static void EverySponsorShown()
    {
        lock (Gate)
        {
            if (_running > 0)
            {
                _shownEverything = true;
            }
        }
    }

    /// <summary>
    /// The break is on. Pair every call with <see cref="End"/>, from a
    /// finally - see the note on the class for what a break left open costs.
    /// </summary>
    internal static void Begin()
    {
        lock (Gate)
        {
            if (_running++ == 0)
            {
                _breaksBegun++;

                // Cleared on the way in rather than on the way out, so the
                // break that is starting cannot inherit the last one's
                // answer - a second race's break would otherwise end at its
                // floor having shown nothing.
                _shownEverything = false;
            }
        }
    }

    /// <summary>
    /// The break is over - the recap is in hand, the ceiling was reached,
    /// the sponsors have all been shown, or the next race was called to the
    /// grid. Called from a finally as well as at the natural end, and never
    /// counts below zero.
    /// </summary>
    internal static void End()
    {
        lock (Gate)
        {
            if (_running == 0)
            {
                return;
            }

            if (--_running == 0)
            {
                _shownEverything = false;
            }
        }
    }

    /// <summary>
    /// Back to no break and no screen, for the test run.
    ///
    /// Static state outlives a test, and a test that began a break without
    /// ending one would hold the sponsors over the crowd's screen in every
    /// test that followed - and in a real app run started by the layout
    /// tests in the same process. The bug AdBreak.ResetForTests,
    /// ShowOnAir.ResetForTests and OnAirTreatment.ResetForTests all exist
    /// for.
    /// </summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _running = 0;
            _readsOnAir = 0;
            _shownEverything = false;
            _screenSaidAt = -1;
            _videoSaidAt = -1;
        }
    }
}
