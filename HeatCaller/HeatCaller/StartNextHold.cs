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
///
/// LET GO EARLY, NOTHING HAPPENS (23 Nov 2026, Asher: "when clicking and
/// holding the start button on race day if you release it prior to it
/// starting as a demo it should go back to it's unpressed state and not the
/// popup window"). A press let go before the demo started used to count as
/// a click, however long it had been held, so it started the race for real
/// and the start's popup came up under the director's hand. Now only a
/// press let go within <see cref="ClickAtMost"/> is a click. One held longer
/// and let go before the demo is a hold called off: nothing happens and the
/// button comes back up. Space and Enter press no mouse, so they are always
/// a click.
/// </summary>
internal sealed class HeldClickFilter
{
    /// <summary>The longest a press can be held and still be a click - a click is well under this, and the hold's bar is a third of the way along by then.</summary>
    internal static readonly TimeSpan ClickAtMost = TimeSpan.FromMilliseconds(300);

    private readonly Func<long> _now;
    private bool _held;
    private long? _downAt;

    public HeldClickFilter(Func<long>? now = null) => _now = now ?? (() => Environment.TickCount64);

    /// <summary>The last press is over: the next click is a click, whatever happened to that one.</summary>
    public void Pressed()
    {
        _held = false;
        _downAt = null;
    }

    /// <summary>The mouse is pressed on the button while its hold is on offer: from now, holding is timed.</summary>
    public void Down()
    {
        _held = false;
        _downAt = _now();
    }

    /// <summary>The hold on this press has done its job.</summary>
    public void Held() => _held = true;

    /// <summary>
    /// True when the click should do the button's own job; false for the
    /// let-go of a completed hold, and for a hold let go before it was done
    /// that was held for longer than a click.
    /// </summary>
    public bool ClickActs()
    {
        var acts = !_held && !(_downAt is { } down && _now() - down >= (long)ClickAtMost.TotalMilliseconds);
        _held = false;
        _downAt = null;
        return acts;
    }
}
