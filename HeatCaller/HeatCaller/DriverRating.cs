namespace HeatCaller;

/// <summary>
/// One driver's race as the rating reads it.
/// </summary>
/// <param name="Id">Whatever identifies them to the caller - a transponder.</param>
/// <param name="Position">Where they finished among the real cars, 1-based.</param>
/// <param name="StartPosition">Where they started, 1-based, or 0 when the grid was not known.</param>
/// <param name="Laps">Laps completed.</param>
/// <param name="LapsLed">Laps completed while leading (RacerStanding.LapsLed).</param>
/// <param name="BestLapMs">Their best lap, or 0 for none.</param>
/// <param name="Warnings">Warnings race control gave them this race.</param>
/// <param name="Disqualified">Marked DQ after the race: rated nothing.</param>
public readonly record struct RatingInput(
    string Id,
    int Position,
    int StartPosition,
    int Laps,
    int LapsLed,
    long BestLapMs,
    int Warnings = 0,
    bool Disqualified = false);

/// <summary>
/// A driver rating for one race, 0 to 150 - HeatCaller's version of the
/// rating NASCAR's loop data produces, built only from what a club's timing
/// actually records.
///
/// WHAT IS IN IT, AND THE WEIGHTS (the most a part can give, out of 150)
///
///   Finish            55   1st gets all of it, last gets none, evenly between.
///   Win               5    For winning, on top - so a win always outrates a second.
///   Laps led          25   The share of the winner's laps they led.
///   Pace              25   Their best lap against the race's best: the race's best
///                          gets all of it, 15% slower gets none.
///   Fastest lap       10   For holding the race's fastest lap.
///   Distance          15   The share of the winner's laps they completed.
///   Places gained     15   Half of it for holding their grid spot; up to all of it
///                          for coming through the whole field, down to none for
///                          dropping through it. Half when the grid was not known,
///                          so a race without one neither helps nor hurts.
///   Warnings          -5 each, never below zero.
///
/// A perfect race - from last on the grid to a win, leading every lap, with
/// the fastest lap - is 150. Leading flag to flag from pole with the fastest
/// lap is 142.5, because nobody came through anything.
///
/// WHY NOT AVERAGE RUNNING POSITION, which NASCAR's rating leans on hardest:
/// the running order BETWEEN crossings is never stored (see
/// SessionResultEntry.LapsLed), so a rating that claimed it would be
/// inventing it. Laps led is the one in-race figure counted honestly at each
/// crossing, and it is here.
///
/// Pure, so the same race always rates the same - from the live standings
/// at the flag and from the saved result years later.
/// </summary>
public static class DriverRating
{
    public const double Max = 150;

    private const double FinishWeight = 55;
    private const double WinBonus = 5;
    private const double LapsLedWeight = 25;
    private const double PaceWeight = 25;
    private const double FastestLapBonus = 10;
    private const double DistanceWeight = 15;
    private const double GainedWeight = 15;
    private const double PerWarning = 5;

    /// <summary>How far off the race's best lap stops counting for anything - 15% slower.</summary>
    private const double PaceFloor = 0.85;

    /// <summary>
    /// Every driver's rating for one race, by id. Empty for a race of fewer
    /// than two cars, where finishing first says nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, double> ForRace(IReadOnlyList<RatingInput> field)
    {
        var ratings = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        if (field is null || field.Count < 2)
        {
            return ratings;
        }

        var size = field.Count;
        var winnerLaps = Math.Max(1, field.Max(f => f.Laps));
        var raceBest = field.Where(f => f.BestLapMs > 0 && !f.Disqualified).Select(f => f.BestLapMs).DefaultIfEmpty(0).Min();

        foreach (var car in field)
        {
            if (string.IsNullOrWhiteSpace(car.Id) || ratings.ContainsKey(car.Id))
            {
                continue;
            }

            ratings[car.Id] = Rate(car, size, winnerLaps, raceBest);
        }

        return ratings;
    }

    private static double Rate(RatingInput car, int size, int winnerLaps, long raceBest)
    {
        if (car.Disqualified)
        {
            return 0;
        }

        var position = Math.Clamp(car.Position, 1, size);
        var span = size - 1.0;

        var finish = (size - position) / span * FinishWeight;
        var win = position == 1 && car.Laps > 0 ? WinBonus : 0;
        var led = Math.Clamp(car.LapsLed / (double)winnerLaps, 0, 1) * LapsLedWeight;

        var pace = 0.0;
        var fastest = 0.0;

        if (raceBest > 0 && car.BestLapMs > 0)
        {
            var ratio = raceBest / (double)car.BestLapMs;
            pace = Math.Clamp((ratio - PaceFloor) / (1 - PaceFloor), 0, 1) * PaceWeight;
            fastest = car.BestLapMs == raceBest ? FastestLapBonus : 0;
        }

        var distance = Math.Clamp(car.Laps / (double)winnerLaps, 0, 1) * DistanceWeight;

        var gained = car.StartPosition > 0
            ? (Math.Clamp((car.StartPosition - position) / span, -1, 1) + 1) / 2 * GainedWeight
            : GainedWeight / 2;

        var total = finish + win + led + pace + fastest + distance + gained - PerWarning * Math.Max(0, car.Warnings);

        return Math.Round(Math.Clamp(total, 0, Max), 1, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Every real car's rating in a saved race, by transponder. Ghosts are
    /// left out of the field - they are not racing anybody - and places are
    /// counted among the real cars, the way DriverStats counts them. Empty for
    /// practice and for a demo, which have no result to rate.
    /// </summary>
    public static IReadOnlyDictionary<string, double> ForSession(SessionRecord session)
    {
        if (session is null || session.SessionType != RaceSessionType.Race || session.IsDemoSession())
        {
            return new Dictionary<string, double>();
        }

        var real = session.Results
            .Where(e => !string.IsNullOrWhiteSpace(e.Transponder) && !GhostService.IsGhost(e.Transponder))
            .OrderBy(e => e.Position)
            .ToList();

        // Grid places among the real cars too, or a ghost on the grid would
        // count as a place gained by everyone who started behind it.
        var gridOrder = GridSlots(real.Where(e => e.StartPosition > 0).OrderBy(e => e.StartPosition).Select(e => e.Transponder));

        var field = real.Select((e, i) => new RatingInput(
                e.Transponder,
                i + 1,
                gridOrder.GetValueOrDefault(e.Transponder),
                Math.Max(0, e.Laps),
                Math.Max(0, e.LapsLed),
                e.FastLapMs,
                e.Warnings?.Count ?? 0,
                string.Equals(e.Outcome, "DQ", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        return ForRace(field);
    }

    /// <summary>
    /// The live race's ratings, by transponder, from the standings in board
    /// order - for the podium graphic at the flag. Ghosts are left out, as in
    /// <see cref="ForSession"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, double> ForStandings(IReadOnlyList<RacerStanding> ordered)
    {
        var real = (ordered ?? Array.Empty<RacerStanding>())
            .Where(s => !string.IsNullOrWhiteSpace(s.TransponderId) && !GhostService.IsGhost(s.TransponderId))
            .ToList();

        var gridOrder = GridSlots(real.Where(s => s.StartPosition > 0).OrderBy(s => s.StartPosition).Select(s => s.TransponderId));

        var field = real.Select((s, i) => new RatingInput(
                s.TransponderId,
                i + 1,
                gridOrder.GetValueOrDefault(s.TransponderId),
                Math.Max(0, s.Laps),
                Math.Max(0, s.LapsLed),
                s.FastestLapMs,
                s.Warnings.Count))
            .ToList();

        return ForRace(field);
    }

    /// <summary>Grid slots 1, 2, 3... in the order given - the first time an id is met, should a result hold it twice.</summary>
    private static Dictionary<string, int> GridSlots(IEnumerable<string> inGridOrder)
    {
        var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var id in inGridOrder)
        {
            slots.TryAdd(id, slots.Count + 1);
        }

        return slots;
    }

    /// <summary>"128.4" - one decimal, invariant, the way a rating is printed.</summary>
    public static string Format(double rating) =>
        rating.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
}
