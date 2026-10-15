namespace HeatCaller;

/// <summary>
/// The SOUND at the edges of a commercial break - the one part of the
/// boundary that was never there.
///
/// WHAT WAS ALREADY THERE, AND IS NOT BEING REBUILT (15 Oct 2026)
///
/// Both edges of a break are already marked, in words, and deliberately -
/// read HoldTheCommercialBreakAsync's own note: "the teaser is the line
/// INTO the break - 'give us a second and we'll tell you how that one
/// unfolded' is a station going to the adverts - and the recap is the one
/// coming back from it, which is what it already says ('and we're back
/// live here at the RC track')."
///
/// So there is no missing welcome-back line in the ordinary case, and
/// adding one would be two returns for one break. What is missing is the
/// PUNCTUATION: a station marks a boundary with a sound as well as a
/// sentence, and on the PA the break began and ended with nothing but
/// speech either side of a silence.
///
/// THE SPONSORS' SCREEN IS NOT THE PA, which is why this is a class of its
/// own rather than a field on CommercialBreak. That one answers "are we in
/// a break" for the Big Screen's rotation; this one answers "what does the
/// room hear at the edges of it".
///
/// IT ADDS NO NEW AUDIO ASSET. A bumper of its own would be another file
/// for a club to record, another row on the Music page and another thing
/// to go missing. The club's existing SHOW STING - the mark ShowTreatment
/// already puts between two items of a block - is the right sound for this
/// and every club that has set one already has it. A club with no sting
/// hears the break exactly as it does now, which is the rule ShowTreatment
/// states for the sting generally: "the sting is the one part whose
/// absence is not an absence".
///
/// AND THE ONE CASE THAT IS GENUINELY UNCOVERED is what
/// <see cref="ComingBack"/> is for.
/// </summary>
internal static class BreakTransition
{
    /// <summary>
    /// How loud the sting is at a break boundary, as a fraction of the
    /// club's show level.
    ///
    /// Slightly under the sting inside a block. A mark between two items
    /// of a show lands between two things the room is already listening
    /// to; this one lands into a gap, where the same level reads as louder
    /// than it is.
    /// </summary>
    internal const float StingLevel = 0.8f;

    /// <summary>
    /// What the announcer says coming back out of a break, or NULL when
    /// something else already says it - which is most of the time.
    ///
    /// THE RULE, and it is the reason this is worth a test. The four
    /// endings of a break are not interchangeable, and two of them are
    /// already covered by a line that exists:
    ///
    ///  - <see cref="BreakEnding.RecapReady"/> is the break doing exactly
    ///    its job. The recap is written and about to play, and the recap
    ///    itself opens by saying the club is back live at the track. A
    ///    second welcome-back in front of that is two returns for one
    ///    break, which is what makes a broadcast sound padded rather than
    ///    produced.
    ///
    ///  - <see cref="BreakEnding.NextRaceCalled"/> is the night having
    ///    moved on without the break. What follows is a grid call - an
    ///    instruction to drivers - and nothing belongs in front of that.
    ///    The break's own note says adverts over the next countdown are
    ///    worse than no adverts at all; a line over it is the same fault
    ///    one step earlier.
    ///
    /// The other two are the uncovered case, and they are uncovered
    /// precisely BECAUSE the recap is what normally covers it:
    ///
    ///  - <see cref="BreakEnding.Ceiling"/> - the break ran as long as it
    ///    may and the recap still is not ready.
    ///  - <see cref="BreakEnding.EverySponsorShown"/> - the rotation ran
    ///    out of sponsors to show.
    ///
    /// In both, the sponsors stop, the recap's "we're back live" has not
    /// been written yet, and the room is left with silence and no idea
    /// whether anything else is coming. That is the one case where saying
    /// something beats saying nothing.
    ///
    /// IT PROMISES NOTHING ABOUT THE RECAP, deliberately. A break that hit
    /// its ceiling is a break where the recap may never arrive, so "here's
    /// how that one unfolded" would be the announcer undertaking something
    /// it cannot deliver - the rule NightPromises states for this whole
    /// layer. It says where the room is and stops.
    /// </summary>
    internal static string? ComingBack(BreakEnding ending) => ending switch
    {
        BreakEnding.Ceiling or BreakEnding.EverySponsorShown => "And we're back at the track.",
        _ => null,
    };

    /// <summary>
    /// Whether the sting marks this boundary at all.
    ///
    /// Going IN, always - a break starting is always a transition.
    ///
    /// Coming BACK, only when nothing else does, which is the same
    /// question <see cref="ComingBack"/> answers and is asked of it rather
    /// than kept as a second list: a sting in front of a recap's own opener
    /// is the doubling-up that method exists to prevent, and two rules that
    /// could disagree about it would be one rule too many.
    /// </summary>
    internal static bool StingMarks(BreakEnding ending, bool goingIn) =>
        goingIn || ComingBack(ending) is not null;

    /// <summary>
    /// The console line for a boundary, so a club reading the log at
    /// midnight can account for a sting it heard - or for one it did not.
    /// </summary>
    internal static string Describe(BreakEnding ending, bool goingIn, bool hadSting)
    {
        if (goingIn)
        {
            return hadSting
                ? "Commercial break - sting in, the sponsors have the crowd's screen."
                : "Commercial break - no show sting set, so the break starts unmarked.";
        }

        var said = ComingBack(ending);

        if (said is null)
        {
            return ending switch
            {
                BreakEnding.RecapReady =>
                    "Break over - the recap's own opener marks the way back, so nothing is added in front of it.",
                BreakEnding.NextRaceCalled =>
                    "Break over - the next race has been called, so the grid call marks the way back.",
                _ => "Break over.",
            };
        }

        return hadSting
            ? $"Break over ({Why(ending)}) - sting and \"{said}\" to mark the way back."
            : $"Break over ({Why(ending)}) - \"{said}\" to mark the way back; no show sting set.";
    }

    private static string Why(BreakEnding ending) => ending switch
    {
        BreakEnding.Ceiling => "it ran as long as a break may",
        BreakEnding.EverySponsorShown => "every sponsor has been shown",
        BreakEnding.RecapReady => "the recap is ready",
        BreakEnding.NextRaceCalled => "the next race was called",
        _ => "still running",
    };
}
