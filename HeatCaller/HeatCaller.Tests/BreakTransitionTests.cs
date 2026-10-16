namespace HeatCaller.Tests;

/// <summary>
/// What the room hears at the edges of a commercial break.
///
/// Mostly a file about NOT adding a second mark. Both edges are already
/// marked in words and deliberately - the teaser going in, the recap's own
/// "we're back live here at the RC track" coming back - so the interesting
/// question is the one case where neither of those is there.
/// </summary>
public sealed class BreakTransitionTests
{
    [Fact]
    public void A_break_that_did_its_job_needs_nothing_said_coming_back()
    {
        // The recap is written and about to play, and the recap opens by
        // saying the club is back live at the track. A welcome-back in
        // front of that is two returns for one break.
        Assert.Null(BreakTransition.ComingBack(BreakEnding.RecapReady));
    }

    [Fact]
    public void A_break_the_next_race_ended_needs_nothing_either()
    {
        // What follows is a grid call, which is an instruction to drivers.
        // The break's own note says adverts over the next countdown are
        // worse than no adverts at all; a line over it is the same fault
        // one step earlier.
        Assert.Null(BreakTransition.ComingBack(BreakEnding.NextRaceCalled));
    }

    [Fact]
    public void A_break_still_running_says_nothing_because_it_has_not_come_back()
    {
        Assert.Null(BreakTransition.ComingBack(BreakEnding.Running));
    }

    [Fact]
    public void The_two_endings_that_leave_the_room_hanging_get_a_line()
    {
        // Neither of these has the recap ready, so the "we're back live"
        // that normally covers the return has not been written yet. The
        // sponsors stop and the room is left with silence and no idea
        // whether anything else is coming.
        foreach (var ending in new[] { BreakEnding.Ceiling, BreakEnding.EverySponsorShown })
        {
            var said = BreakTransition.ComingBack(ending);

            Assert.False(string.IsNullOrWhiteSpace(said), $"{ending} leaves the room with nothing");
        }
    }

    [Fact]
    public void The_line_promises_nothing_about_the_recap()
    {
        // A break that hit its ceiling is a break where the recap may never
        // arrive. "Here's how that one unfolded" would be the announcer
        // undertaking something it cannot deliver - the rule NightPromises
        // states for this whole layer. It says where the room is and stops.
        var said = BreakTransition.ComingBack(BreakEnding.Ceiling)!;

        foreach (var promise in new[] { "recap", "unfolded", "coming up", "in a moment", "shortly", "stay" })
        {
            Assert.DoesNotContain(promise, said, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Every_ending_is_answered_rather_than_falling_through()
    {
        // A missing word must never be the thing that takes a recap down -
        // the rule WhyTheBreakEnded goes by for the same enum.
        foreach (var ending in Enum.GetValues<BreakEnding>())
        {
            BreakTransition.ComingBack(ending);

            Assert.False(
                string.IsNullOrWhiteSpace(BreakTransition.Describe(ending, goingIn: false, hadSting: true)),
                $"{ending} has no console line");

            Assert.False(
                string.IsNullOrWhiteSpace(BreakTransition.Describe(ending, goingIn: true, hadSting: true)),
                $"{ending} has no console line going in");
        }
    }

    // ------------------------------------------------------------------
    //  The sting
    // ------------------------------------------------------------------

    [Fact]
    public void Going_in_is_always_marked()
    {
        // A break starting is always a transition, whichever way it later
        // ends - and at the moment it starts, which ending it will have is
        // not yet known.
        foreach (var ending in Enum.GetValues<BreakEnding>())
        {
            Assert.True(BreakTransition.StingMarks(ending, goingIn: true));
        }
    }

    [Fact]
    public void Coming_back_is_marked_only_where_nothing_else_marks_it()
    {
        // Asked of ComingBack rather than kept as a second list: a sting in
        // front of the recap's own opener is the doubling-up that rule
        // exists to prevent, and two rules that could disagree about it
        // would be one too many.
        Assert.False(BreakTransition.StingMarks(BreakEnding.RecapReady, goingIn: false));
        Assert.False(BreakTransition.StingMarks(BreakEnding.NextRaceCalled, goingIn: false));

        Assert.True(BreakTransition.StingMarks(BreakEnding.Ceiling, goingIn: false));
        Assert.True(BreakTransition.StingMarks(BreakEnding.EverySponsorShown, goingIn: false));
    }

    [Fact]
    public void The_sting_and_the_line_never_disagree_about_a_boundary()
    {
        // The one invariant worth asserting across both: coming back, a
        // sting plays exactly when a line is said. Either both mark the
        // return or neither does.
        foreach (var ending in Enum.GetValues<BreakEnding>())
        {
            Assert.Equal(
                BreakTransition.ComingBack(ending) is not null,
                BreakTransition.StingMarks(ending, goingIn: false));
        }
    }

    [Fact]
    public void A_boundary_sting_invents_no_level_of_its_own()
    {
        // The first draft of this class carried a StingLevel constant that
        // scaled the club's setting down by a fifth, on the reasoning that
        // a mark landing into a gap reads as louder. AppSettings.ShowStingLevel
        // says the opposite and has actually been listened to: the
        // in-block sting is "a shade under" the opener BECAUSE it lands on
        // a running bed "where the opener lands on nothing". A break edge
        // lands on nothing too.
        //
        // So the constant went, and a boundary sting plays at the level the
        // club set. Asserted as an absence, because the fault would be
        // somebody reintroducing a second level nobody chose.
        Assert.DoesNotContain(
            "StingLevel",
            typeof(BreakTransition).GetFields(
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static)
                .Select(one => one.Name));
    }

    // ------------------------------------------------------------------
    //  What the console says
    // ------------------------------------------------------------------

    [Fact]
    public void A_club_with_no_sting_is_told_that_is_why_the_break_was_unmarked()
    {
        // "The sting is the one part whose absence is not an absence" - so
        // a club hearing nothing has to be able to read why, rather than
        // reporting a fault.
        var said = BreakTransition.Describe(BreakEnding.Running, goingIn: true, hadSting: false);

        Assert.Contains("no show sting", said, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_boundary_that_was_marked_says_so_with_the_reason_it_ended()
    {
        var said = BreakTransition.Describe(BreakEnding.Ceiling, goingIn: false, hadSting: true);

        Assert.Contains("as long as a break may", said);
        Assert.Contains(BreakTransition.ComingBack(BreakEnding.Ceiling)!, said);
    }

    [Fact]
    public void A_boundary_nothing_marked_says_what_marked_it_instead()
    {
        // The console has to account for the silence, or a club reads it as
        // the sting having failed.
        Assert.Contains(
            "recap's own opener",
            BreakTransition.Describe(BreakEnding.RecapReady, goingIn: false, hadSting: true));

        Assert.Contains(
            "grid call",
            BreakTransition.Describe(BreakEnding.NextRaceCalled, goingIn: false, hadSting: true));
    }
}
