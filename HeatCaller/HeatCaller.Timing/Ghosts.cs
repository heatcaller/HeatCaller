namespace HeatCaller;

/// <summary>
/// Whether a transponder belongs to a ghost car rather than real hardware.
///
/// This check used to live on GhostService, which is the ghost SIMULATOR:
/// 700-odd lines that need a ProfileStore, a SessionHistoryStore, a
/// TrackLayoutStore and a PersonalBestResetStore before it will even
/// construct. The timing code only ever wanted the one string test -
/// Sectors leaves ghosts out of session bests, RaceControlSession and its
/// lap handling keep them out of the things that have to be real - and
/// asking for it dragged the whole simulator and four stores in behind it.
///
/// It lives here so the timing side can answer the question on its own, as
/// the timing code is gradually moved out into a layer of its own.
/// GhostService.IsGhost forwards to this, so the forty-odd callers that go
/// through the simulator are untouched and there is still exactly one
/// definition of what a ghost transponder looks like.
/// </summary>
public static class Ghosts
{
    /// <summary>
    /// The prefix every ghost transponder carries (GhostCar.Transponder
    /// mints them). A real transponder is digits, so nothing a decoder can
    /// read will ever collide with one.
    /// </summary>
    public const string Prefix = "GHOST-";

    /// <summary>Whether a transponder belongs to a ghost rather than real hardware.</summary>
    public static bool IsGhost(string? transponder) =>
        transponder is not null && transponder.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The session's fastest lap for a board to show: ghosts left out while
    /// any real car is in the standings, as the race's own fastest lap
    /// leaves them out (RaceControlSession). 0 before anyone has one. A field
    /// of nothing but ghosts still shows its own best.
    ///
    /// Each board worked it out for itself from every row, so a ghost's
    /// generated lap was shown as the session's fastest - on the Big Screen,
    /// the Race Control board, the stat strip and Practice - while the call
    /// and the saved result named a real driver's.
    /// </summary>
    public static long FastestLapMs(IEnumerable<RacerStanding> standings)
    {
        var list = standings as IReadOnlyCollection<RacerStanding> ?? standings.ToList();
        var realCarsRacing = list.Any(s => !IsGhost(s.TransponderId));

        return list
            .Where(s => s.FastestLapMs > 0 && !(realCarsRacing && IsGhost(s.TransponderId)))
            .Select(s => s.FastestLapMs)
            .DefaultIfEmpty(0)
            .Min();
    }
}
