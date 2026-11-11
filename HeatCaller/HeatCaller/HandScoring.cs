namespace HeatCaller;

/// <summary>
/// DECODER-DOWN MODE: SCORING A RACE BY HAND WHEN THE LOOP STOPS (11 Nov 2026).
///
/// Race-night build plan item 6.1. A decoder that drops off USB, or a loop
/// cable kicked out of it, used to leave the director two choices mid-race:
/// stop the race and rerun it - ten minutes the night does not have - or
/// keep the count with +1 Lap in the drawer, one car at a time, opening a
/// drawer, aiming at a button the width of a word, while eight cars come
/// past in four seconds. Nobody can do the second for a whole race, so in
/// practice it was always the first.
///
/// LiveTime and the paper-and-pencil clubs both answer this the same way:
/// a sheet with one box per car, and somebody ticking a box every time a
/// car goes past. This is that sheet. A full-screen grid, one big button
/// per car, on the desk, a tablet, or the race-day manager on a phone; a
/// tap is a lap.
///
/// WHY IT GOES THROUGH +1 LAP AND NOTHING NEW. A tap is exactly the
/// drawer's +1 Lap - RaceControlSession.AdjustLaps, wrapped in
/// EditStanding so Undo takes back just that tap. So everything +1 Lap
/// already gets right comes for free: a hand lap has no time, so it never
/// sets a best lap or a record; the lap limit still brings the checkered
/// out; and the saved result already marks the laps as counted by hand
/// ("12*", HandCountedLaps). Nothing about how the decoder's own crossings
/// are timed changes - if the decoder comes back, its crossings still count
/// as they always did, and the director just stops tapping.
///
/// WHY THE ANNOUNCER GOES QUIET ON LAPS. While the director has said the
/// loop can't be trusted, any crossing that does arrive - a flicker as the
/// cable is wiggled - is the one thing that must not be called: a lap time
/// across three laps of silence, "new fastest lap" off a phantom. So while
/// the mode is on, every lap call the loop would make is left unsaid
/// (AnnouncementService.WhileHandScoring). The board still takes the lap.
///
/// WHY IT IS OFFERED, NOT SWITCHED ON. TimingWatch already knows when laps
/// have stopped being counted - the decoder off, gone quiet, or no car
/// across the line for three laps. Those three, during a race, are the
/// moment to offer this; a single car not seen is a flat transponder, and
/// +1 Lap for that one car is the answer. But only a person can tell a dead
/// loop from a long red-flag-less crash, so it is offered with a button on
/// the Next Up bar, never switched on by itself.
///
/// It lasts for the race it was switched on in. The next race starts with
/// the decoder trusted again.
///
/// Pure: no store, no window, no thread - so the rules are tested here and
/// the desk, the tablet and the phone all read the same ones.
/// </summary>
public static class HandScoring
{
    /// <summary>The Next Up bar button that offers it.</summary>
    public const string OfferCaption = "Score by Hand";

    /// <summary>What the console and the room are told when it is switched on.</summary>
    public const string SpokenWhenOn = "Timing is down. Laps are being counted by hand.";

    /// <summary>
    /// Whether a TimingWatch problem means laps have stopped being counted
    /// for the whole field - the decoder off, gone quiet, or no crossings at
    /// all - rather than one car going missing. Keys are TimingWatch's own.
    /// </summary>
    public static bool MeansLapsAreNotCounted(string? timingProblemKey) =>
        timingProblemKey is "decoder-off" or "decoder-quiet" or "no-crossings";

    /// <summary>
    /// Whether to offer hand scoring now: a race running (not practice), the
    /// field's laps not being counted, and the mode not already on. A demo
    /// race never raises these problems, so it is never offered there -
    /// though it can be switched on by hand to try it.
    /// </summary>
    public static bool ShouldOffer(string? timingProblemKey, RaceState state, bool practice, bool alreadyOn) =>
        !alreadyOn
        && !practice
        && state == RaceState.Racing
        && MeansLapsAreNotCounted(timingProblemKey);

    /// <summary>
    /// Why hand scoring can't be switched on now, or null when it can. Only
    /// a race that is running: before the green there is nothing to count,
    /// and after the flag the saved result is corrected with Edit Result.
    /// </summary>
    public static string? StartRefusal(RaceState state, bool practice)
    {
        if (practice)
        {
            return "Hand scoring is for races - practice is running.";
        }

        return state == RaceState.Racing
            ? null
            : "Hand scoring needs a race that is running. After the flag, correct the saved result with Edit Result on Race History.";
    }

    /// <summary>
    /// Why a tap can't be counted, or null when it can. A car has to be on
    /// the board - it has crossed the line in this race, or was put there by
    /// a hand-released start - because a lap is added to a count, and a car
    /// with no count has nothing to add to.
    /// </summary>
    public static string? LapRefusal(RaceState state, bool practice, bool onBoard, string driverName)
    {
        if (StartRefusal(state, practice) is { } notNow)
        {
            return notNow;
        }

        return onBoard
            ? null
            : $"{driverName} isn't on the board in this race yet, so there is no count to add a lap to.";
    }

    /// <summary>The button on the grid that puts a missing car on it (11 Nov 2026).</summary>
    public const string AddCarCaption = "Add a Car...";

    /// <summary>
    /// A car that can be put on the hand-scoring grid: one the decoder never
    /// saw before it died. <paramref name="OnRoster"/> when it is in this
    /// round's line-up, the likely one.
    /// </summary>
    public sealed record Addable(string Transponder, string Name, bool OnRoster);

    /// <summary>
    /// The cars that can be added (11 Nov 2026): this round's roster first,
    /// in grid order, then every other known transponder by name - each only
    /// when it is not on the board already. A driver's alternate counts as
    /// them (<paramref name="mainOf"/>), so a driver is offered once and
    /// never beside the car they are already counted on. Ghosts never.
    /// </summary>
    public static IReadOnlyList<Addable> AddableCars(
        IEnumerable<string> roster,
        IEnumerable<string> known,
        IEnumerable<string> onBoard,
        Func<string, string?> nameOf,
        Func<string, string?> mainOf,
        Func<string, bool> isGhost)
    {
        string Main(string t) => (mainOf(t) ?? t).Trim();

        var taken = new HashSet<string>(onBoard.Select(Main), StringComparer.OrdinalIgnoreCase);
        var result = new List<Addable>();

        void Offer(string transponder, bool onRoster)
        {
            var id = Main(transponder);

            if (id.Length == 0 || isGhost(id) || !taken.Add(id))
            {
                return;
            }

            var name = nameOf(id);
            result.Add(new Addable(id, string.IsNullOrWhiteSpace(name) ? id : name.Trim(), onRoster));
        }

        foreach (var t in roster)
        {
            Offer(t, true);
        }

        var before = result.Count;

        foreach (var t in known)
        {
            Offer(t, false);
        }

        result.Sort(before, result.Count - before, Comparer<Addable>.Create((a, b) =>
        {
            var byName = StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name);
            return byName != 0 ? byName : StringComparer.Ordinal.Compare(a.Transponder, b.Transponder);
        }));

        return result;
    }

    /// <summary>
    /// Why a car can't be added to the grid now, or null when it can: hand
    /// scoring for a running race, before the checkered (a car with no
    /// crossing could never take the flag), and a car not on the board.
    /// </summary>
    public static string? AddRefusal(RaceState state, bool practice, bool finishing, bool onBoard, bool ghost, string driverName)
    {
        if (StartRefusal(state, practice) is { } notNow)
        {
            return notNow;
        }

        if (finishing)
        {
            return "The checkered is out - a car can't join the race now. Add its laps afterwards with Edit Result on Race History.";
        }

        if (ghost)
        {
            return "A ghost car counts its own laps.";
        }

        return onBoard ? $"{driverName} is on the board already - tap their button to count a lap." : null;
    }

    /// <summary>
    /// A second +1 for the same car this soon after the first is a finger
    /// bouncing on a touch screen, or two people counting the same car -
    /// never a lap: no RC car laps a track in a second.
    /// </summary>
    public const long DoubleTapMs = 1000;

    /// <summary>
    /// Whether a +1 at <paramref name="nowMs"/> is the same lap as the one
    /// at <paramref name="lastTapMs"/> (a monotonic clock, in ms), and is
    /// dropped. A clock that has gone backwards is never a double tap.
    /// </summary>
    public static bool IsDoubleTap(long? lastTapMs, long nowMs) =>
        lastTapMs is { } last && nowMs >= last && nowMs - last < DoubleTapMs;

    /// <summary>One car's button.</summary>
    /// <param name="Transponder">The standing's transponder.</param>
    /// <param name="Name">Who is driving, or the transponder when nobody is named.</param>
    /// <param name="Laps">Laps on the board now.</param>
    /// <param name="ByHand">How many of them were counted by hand.</param>
    /// <param name="StartPosition">Grid slot, 0 when not known - what keeps the buttons still.</param>
    public sealed record Car(string Transponder, string Name, int Laps, int ByHand, int StartPosition)
    {
        /// <summary>"12" or "12*" - the same mark the results carry.</summary>
        public string LapsText => HandCountedLaps.LapsText(Laps, ByHand);
    }

    /// <summary>
    /// The cars to show, in an order that NEVER changes as laps are added.
    ///
    /// Not the running order: a button that moves the moment it is tapped
    /// sends the next tap to the wrong car, and the director is looking at
    /// the track, not the screen. Grid slot first (the order they lined up
    /// in, which the director can see on track), then anyone with no slot by
    /// name. Ghost cars are left out - they lap themselves.
    /// </summary>
    public static IReadOnlyList<Car> CarsFor(
        IEnumerable<RacerStanding> standings,
        Func<string, string?> nameOf,
        Func<string, bool> isGhost)
    {
        return standings
            .Where(s => !isGhost(s.TransponderId))
            .Select(s =>
            {
                var name = nameOf(s.TransponderId);

                return new Car(
                    s.TransponderId,
                    string.IsNullOrWhiteSpace(name) ? s.TransponderId : name.Trim(),
                    s.Laps,
                    HandCountedLaps.During(s),
                    s.StartPosition);
            })
            .OrderBy(c => c.StartPosition > 0 ? 0 : 1)
            .ThenBy(c => c.StartPosition)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Transponder, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// How many columns and rows of buttons fill an area best: the shape
    /// that makes the smallest side of a button as big as it can be, with a
    /// button about twice as wide as it is tall (a name over a number) as
    /// the ideal. Ties go to fewer columns, so a short field reads down.
    /// </summary>
    public static (int Columns, int Rows) GridShape(int cars, int width, int height)
    {
        if (cars <= 0)
        {
            return (1, 1);
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        var best = (Columns: 1, Rows: cars);
        var bestScore = double.MinValue;

        for (var columns = 1; columns <= cars; columns++)
        {
            var rows = (cars + columns - 1) / columns;

            // A column more than the cars need leaves a column empty.
            if ((rows - 1) * columns >= cars)
            {
                continue;
            }

            var score = Math.Min(width / (double)columns / 2.0, height / (double)rows);

            if (score > bestScore + 0.0001)
            {
                bestScore = score;
                best = (columns, rows);
            }
        }

        return best;
    }

    // ------------------------------------------------------------------
    //  Afterwards: typing a whole result's lap counts in Edit Result
    // ------------------------------------------------------------------

    /// <summary>
    /// The laps a result shows for a correction: what was raced, plus laps
    /// added, less any lap penalty - never below none. ResultEditor's own
    /// sum (a DQ aside, which counts no laps whatever is typed).
    /// </summary>
    public static int TotalLaps(int racedLaps, int addedLaps, int lapPenalty) =>
        Math.Max(0, racedLaps + addedLaps - Math.Max(0, lapPenalty));

    /// <summary>
    /// The Added Laps that make a result show <paramref name="wantedTotal"/>
    /// laps - the other way round from <see cref="TotalLaps"/>, so a lap
    /// count read off a hand-scoring sheet can be typed straight in, and
    /// the lap penalty already given still stands on top of it.
    /// </summary>
    public static int AddedLapsFor(int racedLaps, int lapPenalty, int wantedTotal) =>
        Math.Max(0, wantedTotal) - racedLaps + Math.Max(0, lapPenalty);

    /// <summary>
    /// Reads a lap count typed into the Laps column: a whole number from
    /// none up, or null for anything else.
    /// </summary>
    public static int? ParseLapCount(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();

        return int.TryParse(trimmed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var laps)
            ? laps
            : null;
    }
}
