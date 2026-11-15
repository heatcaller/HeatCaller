namespace HeatCaller;

/// <summary>
/// The next race's start as Race Control has it, for the night plan
/// (12 Nov 2026).
///
/// WHY: an approved "Need 5 minutes" from a driver's phone, Call Up's
/// "+1 min" and Hold all move the next race, but only Auto start's own
/// countdown knew. The night plan, which every "about 20:42" comes from
/// (the program, the phones' Schedule, the Big Screen's UP NEXT, the
/// night's finish), worked from the last finish plus the night's usual
/// gap. So the countdown said 7:00 while the schedule still said the
/// race was off in two. The plan now starts the next race when Race
/// Control will start it, and moves everything after it to match.
/// </summary>
public partial class RaceControlControl
{
    /// <summary>
    /// What the night plan needs to know about the next race. RoundId is the
    /// race. StartsAtUtc is when it goes green by Auto start (the Start press
    /// plus warm-up, staging and countdown - bug hunt 21), or null with Auto
    /// start off or not counting. PushedBackMinutes is how far the desk has
    /// put it back, which counts when there's no Auto start time.
    /// </summary>
    public sealed record NextStartFact(string RoundId, DateTime? StartsAtUtc, int PushedBackMinutes);

    private NextStartFact? _nextStart;

    /// <summary>The start NextStartMoved was last raised for - what the program last drew. UI thread.</summary>
    private NextStartFact? _nextStartTold;

    /// <summary>
    /// The next race's start between races, or null while a race is on (the
    /// plan has the race on track then). Written on the UI thread each gap
    /// tick, read from the publisher's thread.
    /// </summary>
    public NextStartFact? NextStartForPlan => Volatile.Read(ref _nextStart);

    /// <summary>Raised when the next race's start moves, so the program can redraw its times.</summary>
    public event Action? NextStartMoved;

    private void NoteNextStart(DateTime now, GapFacts facts)
    {
        var fact = facts.NextId is { } id
            // To the green, as every other clock counts (bug hunt 21).
            ? new NextStartFact(id, AutoGreenAtUtc(id, now), Gap.PushedBackMinutes)
            : null;

        Volatile.Write(ref _nextStart, fact);

        if (MovedSinceTold(ref _nextStartTold, fact))
        {
            NextStartMoved?.Invoke();
        }
    }

    private void ForgetNextStart()
    {
        Volatile.Write(ref _nextStart, null);
        _nextStartTold = null;
    }

    /// <summary>
    /// Whether the start has moved from the one last told, which becomes this
    /// one when it has. Measured from the last one told, not from the tick
    /// before (bug hunt 17): a Hold slides the start a second a tick, never
    /// half a minute between two ticks, so the program was never redrawn
    /// however long the hold ran.
    /// </summary>
    internal static bool MovedSinceTold(ref NextStartFact? told, NextStartFact? now)
    {
        if (!Moved(told, now))
        {
            return false;
        }

        told = now;
        return true;
    }

    /// <summary>
    /// A move worth redrawing for: another race, another push-back, or Auto
    /// start's time more than half a minute out from before (the count's own
    /// second-by-second wobble isn't).
    /// </summary>
    internal static bool Moved(NextStartFact? was, NextStartFact? now)
    {
        if (was is null || now is null)
        {
            return was is not null || now is not null;
        }

        if (was.RoundId != now.RoundId || was.PushedBackMinutes != now.PushedBackMinutes)
        {
            return true;
        }

        return (was.StartsAtUtc, now.StartsAtUtc) switch
        {
            (null, null) => false,
            ({ } a, { } b) => (a - b).Duration() > TimeSpan.FromSeconds(30),
            _ => true,
        };
    }
}
