namespace HeatCaller;

/// <summary>
/// LAPS COUNTED BY HAND, SAID WHEREVER A RESULT IS SHOWN (22 Oct 2026).
///
/// When a transponder dies mid-race the race director keeps the count with
/// +1 Lap (the drawer, or the keyboard), and after the race Edit Result can
/// add a lap a missed crossing cost. Both are right to do, and both change
/// the result - and until now nothing in the result said so. A result built
/// partly by hand has to be trusted AND be questionable: the driver who
/// finished a lap behind is owed the knowledge that the lap in front of him
/// was counted by a person, not a loop.
///
/// So a lap count with any of those laps in it carries a mark - "12*" - and
/// every place that shows the mark says what it means. The number itself is
/// unchanged and the scoring is untouched: this is a label, not a rule.
/// </summary>
public static class HandCountedLaps
{
    /// <summary>The mark after a lap count that includes laps counted by hand.</summary>
    public const string Mark = "*";

    /// <summary>What the mark means, for a key under a table.</summary>
    public const string Legend = "* includes laps counted by hand";

    /// <summary>
    /// Laps a car has in its count with no crossing behind them: added with
    /// +1 Lap during the race. AdjustLaps keeps a hand lap as a count with no
    /// time, and takes those off first, so the difference between the count
    /// and the timed laps is exactly the laps still standing that were added
    /// by hand.
    /// </summary>
    public static int During(RacerStanding standing) =>
        Math.Max(0, standing.Laps - standing.LapTimesMs.Count);

    /// <summary>
    /// The laps in a saved result counted by hand: those added during the
    /// race, plus any Edit Result added afterwards. A lap Edit Result took
    /// away comes off the hand-counted ones first - the order the race
    /// itself takes laps off in (RaceControlSession.AdjustLaps) - never
    /// below none, and never more than the laps the result now has, so a
    /// disqualified or lap-penalised result cannot read "0*".
    /// </summary>
    public static int In(SessionResultEntry result) =>
        Math.Clamp(Math.Max(0, result.LapsByHand) + result.AddedLaps, 0, Math.Max(0, result.Laps));

    /// <summary>"12", or "12*" when any of the twelve were counted by hand.</summary>
    public static string LapsText(int laps, int byHand) =>
        byHand > 0 ? laps + Mark : laps.ToString();

    /// <summary>"1 lap counted by hand", "3 laps counted by hand", or empty.</summary>
    public static string Note(int byHand) =>
        byHand <= 0 ? string.Empty : byHand == 1 ? "1 lap counted by hand" : $"{byHand} laps counted by hand";
}
