namespace HeatCaller;

/// <summary>What holding Start Next comes to - see <see cref="StartNextHold.Decide"/>.</summary>
internal enum StartNextHoldOutcome
{
    /// <summary>Run the next round as a demo.</summary>
    RunDemo,

    /// <summary>A session is under way: nothing is started over it.</summary>
    RaceUnderWay,

    /// <summary>On Air is opening a race already.</summary>
    OnAirOpening,

    /// <summary>The programme owes a step first (bump-ups, seeding the mains), so the next round's grid is not settled.</summary>
    StepOwed,

    /// <summary>The programme has nothing left to run.</summary>
    NothingNext,
}

/// <summary>
/// HOLD START NEXT FOR A DEMO RUN (11 Nov 2026). A click on the Next Up
/// bar's Start Next still starts the next race for real; pressed and held
/// for the second Red takes, it runs that same round as a demo instead - the
/// simulated race Demo Mode runs, with the round's own drivers, format and
/// show. The demo goes through MainForm's one demo path (the race
/// programme's Demo button uses it too), so it keeps every demo rule: it is
/// never filed against the round as a real result, and it never scores.
///
/// The decisions are here, with no window in them, so they can be tested.
/// </summary>
internal static class StartNextHold
{
    /// <summary>The words "Keep holding to …" finishes with on the strip under the flag row.</summary>
    public const string Doing = "run it as a demo";

    /// <summary>Start Next's tooltip.</summary>
    public const string Tip = "Click to start the next race. Hold for a demo run: press and hold for about a second "
        + "to run the same round as a demo instead - simulated laps, never filed as a result, never scored.";

    /// <summary>Start Next's description for a screen reader.</summary>
    public const string AccessibleDescription = "Click to start the next race; press and hold for about a second for a demo run of it.";

    /// <summary>
    /// What a full hold of Start Next does, in the order Start Next itself
    /// checks the same things: refused while a race is under way or On Air
    /// is opening one, and while a programme step is owed; otherwise the
    /// next round's demo, when there is a next round.
    /// </summary>
    public static StartNextHoldOutcome Decide(RaceState state, bool onAirRunning, bool stepOwed, bool hasNextRound)
    {
        if (state is not (RaceState.Idle or RaceState.Finished))
        {
            return StartNextHoldOutcome.RaceUnderWay;
        }

        if (onAirRunning)
        {
            return StartNextHoldOutcome.OnAirOpening;
        }

        if (stepOwed)
        {
            return StartNextHoldOutcome.StepOwed;
        }

        return hasNextRound ? StartNextHoldOutcome.RunDemo : StartNextHoldOutcome.NothingNext;
    }
}

/// <summary>
/// Lets a button's Click through unless a hold has just done its job on the
/// same press: a completed hold is let go over the button, and that let-go
/// is a click as far as Windows is concerned - it must not start the race
/// for real as well as the demo.
///
/// <see cref="Pressed"/> on every press (and once the let-go has been
/// handled) clears it, so a hold whose let-go never reached the button - a
/// dialog took the mouse - cannot swallow the next real click.
/// </summary>
internal sealed class HeldClickFilter
{
    private bool _held;

    /// <summary>A fresh press, or the last one is over: the next click is a click.</summary>
    public void Pressed() => _held = false;

    /// <summary>The hold on this press has done its job.</summary>
    public void Held() => _held = true;

    /// <summary>True when the click should do the button's own job; false for the let-go of a completed hold.</summary>
    public bool ClickActs()
    {
        var acts = !_held;
        _held = false;
        return acts;
    }
}
