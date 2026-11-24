using System.Globalization;

namespace HeatCaller;

/// <summary>
/// The Pole Shootout (19 Nov 2026, race-night build plan row 14b).
///
/// WHAT IT IS: after qualifying, the class's top N qualifiers (5 unless the
/// class says otherwise) each go out ALONE, one car at a time, slowest of
/// the N first and the top qualifier last. Each run is an out lap and then
/// one timed lap (1 to 3 if the class says so; the best of them counts).
/// The fastest run takes pole. The N fill the front of the A-main grid in
/// shootout order; everybody else keeps their qualifying place behind them.
/// A run with no time - a crash on the out lap, a car that never crossed -
/// keeps the driver's own qualifying place among the N, behind every driver
/// who did set a time.
///
/// HOW IT IS RUN: each run is a one-driver round of its own kind
/// (RoundKind.Shootout) on the programme, ahead of the class's mains, with
/// a format of its own (RunFormat): each car's clock from its own first
/// crossing, so the out lap costs nothing, and a lap limit one more than
/// the timed laps, because the first crossing is itself counted as a lap.
/// Race Control runs it like any race. When the last run is filed, the
/// A-main's grid is put in shootout order (Grid).
///
/// Pure: handed the rounds and each run's recorded result, so every rule
/// here is a table test. Named "Pole Shootout", always - not any other
/// series' name for it.
/// </summary>
public static class PoleShootout
{
    /// <summary>The format's name, as the class editor, the programme and the docs say it.</summary>
    public const string Name = "Pole Shootout";

    public const int MinDrivers = 2;
    public const int MaxDrivers = 10;
    public const int DefaultDrivers = 5;
    public const int MaxTimedLaps = 3;

    /// <summary>What a run is called on the programme: "Pole Shootout Run 2".</summary>
    public static string RunLabel(int run) => $"{Name} Run {run}";

    /// <summary>The class's shootout size, kept inside its limits and never more than an A-main holds.</summary>
    public static int DriversFor(RaceClass? raceClass) =>
        Math.Min(Math.Clamp(raceClass?.PoleShootoutDrivers ?? DefaultDrivers, MinDrivers, MaxDrivers), Math.Max(MinDrivers, raceClass?.MainSize ?? 8));

    /// <summary>The class's timed laps per run, 1 to 3.</summary>
    public static int TimedLapsFor(RaceClass? raceClass) => Math.Clamp(raceClass?.PoleShootoutLaps ?? 1, 1, MaxTimedLaps);

    /// <summary>
    /// A run's format: the class's own format with the length and the start
    /// changed - every car's clock from its own first crossing (the out lap
    /// is free), a lap limit of the timed laps plus that first crossing, and
    /// a time limit only as a stop for a car that never comes round.
    /// <para>A class with no format of its own (null) follows the app
    /// default, so <paramref name="appDefault"/> is what the run starts from
    /// then; the built-in values only when neither is given.</para>
    /// </summary>
    public static RaceFormat RunFormat(RaceFormat? classFormat, int timedLaps, RaceFormat? appDefault = null)
    {
        var laps = Math.Clamp(timedLaps, 1, MaxTimedLaps);

        // WHY (23 Nov 2026, bug hunt 24): a class that follows the app default
        // has a null Format, and this used to fall straight to the built-in
        // values (3 s min lap, automatic 15 s + 5 s start), not the club's own
        // default - so at a club with a lower min lap no timed lap counted and
        // nobody took pole, while the same class's heats and mains raced fine.
        var format = (classFormat ?? appDefault ?? new RaceFormat()).Clone();

        format.StartType = RaceStartType.Qualifier;
        format.LapLimit = laps + 1;
        format.TimeLimitMinutes = laps + 2;
        format.WarmUpSeconds = 0;

        return format;
    }

    /// <summary>Who runs the shootout: the top <paramref name="drivers"/> of the qualifying order, best first.</summary>
    public static List<string> Field(IReadOnlyList<string> qualifyingOrder, int drivers) =>
        qualifyingOrder
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(0, drivers))
            .ToList();

    /// <summary>
    /// The runs for a field (best qualifier first), in running order: the
    /// slowest of the field first, the top qualifier last. Fewer than two
    /// drivers is no shootout at all, and builds nothing.
    /// <para><paramref name="leftOut"/>: drivers the director took out of the
    /// shootout by deleting their run (bug hunt 24 #269). They get no run;
    /// the others keep their own qualifying places, and the runs remember
    /// the whole field and who was left out, so the next Refresh sees
    /// nothing has changed.</para>
    /// </summary>
    public static List<RoundDefinition> BuildRuns(string className, IReadOnlyList<string> field, int timedLaps, RaceFormat? classFormat, string trackLayoutName, RaceFormat? appDefault = null, IReadOnlyCollection<string>? leftOut = null)
    {
        var runs = new List<RoundDefinition>();
        var skip = new HashSet<string>(leftOut ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

        // Each driver who runs, with their place in the whole field (1 = top qualifier).
        var running = field
            .Select((t, i) => (Transponder: t, Place: i + 1))
            .Where(d => !skip.Contains(d.Transponder))
            .ToList();

        if (running.Count < MinDrivers)
        {
            return runs;
        }

        var kept = skip.Count == 0 ? null : skip.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

        for (var run = 1; run <= running.Count; run++)
        {
            var driver = running[running.Count - run];

            runs.Add(new RoundDefinition
            {
                ClassName = className,
                RoundLabel = RunLabel(run),
                Kind = RoundKind.Shootout,
                ShootoutRun = run,
                ShootoutRunCount = running.Count,
                ShootoutQualifyingPlace = driver.Place,
                TrackLayoutName = trackLayoutName,
                AssignedTransponders = new List<string> { driver.Transponder },
                FormatOverride = RunFormat(classFormat, timedLaps, appDefault),

                // The field the runs were built for, so Refresh can tell a
                // hand change from a change in qualifying (bug hunt 24).
                ShootoutField = field.ToList(),
                ShootoutLeftOut = kept?.ToList(),
            });
        }

        return runs;
    }

    /// <summary>
    /// The driver a run stands for: the qualifier in its place in the field
    /// it was built for - even when the desk has since given the run to
    /// somebody else - or, on runs saved before the field was kept, the
    /// driver in it.
    /// </summary>
    public static string SlotDriver(RoundDefinition run) =>
        run.ShootoutField is { } built && run.ShootoutQualifyingPlace >= 1 && run.ShootoutQualifyingPlace <= built.Count
            ? built[run.ShootoutQualifyingPlace - 1]
            : run.AssignedTransponders.FirstOrDefault() ?? string.Empty;

    /// <summary>Everyone the director has left out of the shootout, from any of its runs.</summary>
    public static List<string> LeftOut(IEnumerable<RoundDefinition> runs) =>
        runs.SelectMany(r => r.ShootoutLeftOut ?? new List<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Writes onto the runs still on the programme that the
    /// <paramref name="deleted"/> runs were taken off it by hand: their
    /// drivers are left out, and the shootout has as many runs as are left,
    /// so it still finishes and still sets the front of the A-Main.
    /// Called with no deleted runs after an undo puts some back: who is left
    /// out and the run count are worked out again from the runs there now,
    /// so two deletes undone in either order both come out right. Returns
    /// the runs it changed, for the caller to save.
    /// </summary>
    /// <remarks>
    /// WHY (24 Nov 2026, bug hunt 24 #269): the runs left used to go on
    /// counting a deleted run, so Refresh read the gap as a change in
    /// qualifying and built the run again after every filed race - and,
    /// once a run had been raced, the shootout could never be complete,
    /// so nobody was called on pole.
    /// </remarks>
    public static List<RoundDefinition> LeaveOut(IReadOnlyList<RoundDefinition> runsLeft, IEnumerable<RoundDefinition> deleted)
    {
        var changed = new List<RoundDefinition>();

        if (runsLeft.Count == 0)
        {
            return changed;
        }

        var stillRunning = runsLeft.Select(SlotDriver).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var leftOut = LeftOut(runsLeft)
            .Concat(deleted.Select(SlotDriver))
            .Where(t => !string.IsNullOrWhiteSpace(t) && !stillRunning.Contains(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var run in runsLeft)
        {
            var was = run.ShootoutLeftOut ?? new List<string>();

            if (run.ShootoutRunCount == runsLeft.Count && was.SequenceEqual(leftOut, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            run.ShootoutRunCount = runsLeft.Count;
            run.ShootoutLeftOut = leftOut.Count == 0 ? null : leftOut.ToList();
            changed.Add(run);
        }

        return changed;
    }

    /// <summary>What Refresh found.</summary>
    public enum RefreshResult
    {
        /// <summary>The runs already hold the right drivers.</summary>
        Unchanged,

        /// <summary>The drivers were moved between the runs to follow the new qualifying order.</summary>
        Updated,

        /// <summary>The field is a different size now: the runs have to be built again.</summary>
        Rebuild,

        /// <summary>A run has been raced already, so nothing is changed.</summary>
        Frozen,
    }

    /// <summary>
    /// Keeps the class's unrun shootout runs in step with qualifying - a
    /// result corrected, or a late heat filed, after they were built. Only
    /// while NO run has been raced: once one has, the shootout is under way
    /// and the drivers stay as they are (Frozen). A field of a different
    /// size can't be fixed in place (Rebuild): the caller builds them again.
    /// </summary>
    public static RefreshResult Refresh(IReadOnlyList<RoundDefinition> runs, IReadOnlyList<string> field)
    {
        if (runs.Count == 0)
        {
            return field.Count >= MinDrivers ? RefreshResult.Rebuild : RefreshResult.Unchanged;
        }

        if (runs.Any(r => r.IsRecorded))
        {
            return RefreshResult.Frozen;
        }

        // WHY (23 Nov 2026, bug hunt 24): qualifying is what the runs follow,
        // not the runs themselves. When the field is the one they were built
        // for, nothing in qualifying moved, so a run the director gave
        // another driver by hand stands. This used to read that hand change
        // as a new qualifying order and put the qualifier back after every
        // filed race. Runs built before the field was kept fall through to
        // the old comparison.
        //
        // WHY (24 Nov 2026, bug hunt 24 #269): a run the director deleted is
        // written onto the runs left (LeaveOut), so the field is the same
        // field less the drivers left out. That used to read as a field of
        // another size and build the deleted run again after every filed
        // race, "for the new qualifying order". A run that is missing with
        // nobody recorded as left out is still built again, as before.
        var leftOut = LeftOutOf(runs, field);
        var running = field.Count - leftOut.Count;

        if (runs.Count == running
            && runs.Any(r => r.ShootoutField is { } built && built.SequenceEqual(field, StringComparer.OrdinalIgnoreCase)))
        {
            return RefreshResult.Unchanged;
        }

        if (leftOut.Count > 0)
        {
            // Runs saved before the field was kept: nothing changed while
            // they still hold the field less the drivers left out, in order.
            var expected = field.Where(t => !leftOut.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
            var holding = runs.OrderBy(r => r.ShootoutQualifyingPlace).Select(r => r.AssignedTransponders.FirstOrDefault() ?? string.Empty).ToList();

            if (runs.All(r => r.ShootoutField is null) && holding.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            {
                return RefreshResult.Unchanged;
            }

            // Qualifying really changed. The runs have gaps where the
            // deleted ones were, so they can't be moved in place: built
            // again, still without the drivers left out (BuildRuns' leftOut).
            return RefreshResult.Rebuild;
        }

        if (runs.Count != field.Count)
        {
            return RefreshResult.Rebuild;
        }

        var changed = false;

        foreach (var run in runs.OrderBy(r => r.ShootoutRun))
        {
            var place = field.Count - run.ShootoutRun + 1;

            if (place < 1 || place > field.Count)
            {
                return RefreshResult.Rebuild;
            }

            var driver = field[place - 1];

            if (run.AssignedTransponders.Count != 1
                || !string.Equals(run.AssignedTransponders[0], driver, StringComparison.OrdinalIgnoreCase)
                || run.ShootoutQualifyingPlace != place)
            {
                run.AssignedTransponders = new List<string> { driver };
                run.ShootoutQualifyingPlace = place;
                run.ShootoutRunCount = field.Count;
                changed = true;
            }
        }

        if (changed)
        {
            // The field the runs now follow (bug hunt 24), on every run.
            foreach (var run in runs)
            {
                run.ShootoutField = field.ToList();
            }
        }

        return changed ? RefreshResult.Updated : RefreshResult.Unchanged;
    }

    /// <summary>The drivers left out of the shootout who are in <paramref name="field"/> - the ones a rebuild must not give a run.</summary>
    public static List<string> LeftOutOf(IEnumerable<RoundDefinition> runs, IReadOnlyList<string> field) =>
        LeftOut(runs).Where(t => field.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// A run's time: the best of its timed laps, with any time penalty the
    /// driver was given added; 0 for no time (no timed lap, or a DQ). The
    /// first crossing is the start of the clock, not a lap (StartSlots).
    /// </summary>
    public static long RunTimeMs(SessionResultEntry result, int timedLaps)
    {
        if (result.Outcome == ResultEditor.Dq)
        {
            return 0;
        }

        long best;

        if (result.LapTimesMs.Count > 0)
        {
            var flying = result.LapTimesMs
                .Skip(result.StartSlots(result.LapTimesMs))
                .Take(Math.Clamp(timedLaps, 1, MaxTimedLaps))
                .Where(ms => ms > 0)
                .ToList();

            if (flying.Count == 0)
            {
                return 0;
            }

            best = flying.Min();
        }
        else if (result.Laps > 1 && result.FastLapMs > 0)
        {
            best = result.FastLapMs;
        }
        else
        {
            return 0;
        }

        return best + Math.Max(0, result.RacePenaltyMs) + Math.Max(0, result.TimePenaltyMs);
    }

    /// <summary>One driver's run: their time (0 for none), the qualifying place they came in with, and the run it was.</summary>
    public sealed record RunTime(string Transponder, string DriverName, int QualifyingPlace, long TimeMs, int Run, bool Raced);

    /// <summary>The shootout so far: the order (pole first), how many runs are in and how many there are.</summary>
    public sealed record Standing(List<RunTime> Order, int RunsDone, int Runs)
    {
        /// <summary>Every run is in.</summary>
        public bool Complete => Runs > 0 && RunsDone >= Runs;

        /// <summary>Who has pole: the top of the order, when they set a time.</summary>
        public RunTime? Pole => Order.Count > 0 && Order[0].TimeMs > 0 ? Order[0] : null;
    }

    /// <summary>
    /// The shootout's order from the runs raced so far. A driver with a time
    /// is ranked on it (a dead heat to the better qualifier); a driver whose
    /// run is in with no time goes behind every time, in qualifying order;
    /// a run not yet raced is left off. <paramref name="justRun"/> is a run
    /// just finished and not yet linked to its round.
    /// </summary>
    public static Standing Standings(
        IReadOnlyList<RoundDefinition> runs,
        Func<string, SessionRecord?> sessionById,
        int timedLaps,
        Func<string, string?>? primaryForAlternate = null,
        (RoundDefinition Round, SessionRecord Result)? justRun = null)
    {
        var times = new List<RunTime>();
        var done = 0;

        foreach (var run in runs.OrderBy(r => r.ShootoutRun))
        {
            SessionRecord? result = justRun is { } just && string.Equals(just.Round.Id, run.Id, StringComparison.Ordinal)
                ? just.Result
                : run.LinkedSessionId is { } id ? sessionById(id) : null;

            if (result is null)
            {
                continue;
            }

            done++;

            var driver = run.AssignedTransponders.FirstOrDefault() ?? string.Empty;
            string Key(string t) => primaryForAlternate?.Invoke(t) ?? t;

            // The driver's own row - under their main transponder or a spare.
            var row = result.Results
                .Where(r => !string.IsNullOrWhiteSpace(r.Transponder) && !GhostService.IsGhost(r.Transponder))
                .FirstOrDefault(r => string.Equals(Key(r.Transponder), Key(driver), StringComparison.OrdinalIgnoreCase))
                ?? (string.IsNullOrWhiteSpace(driver)
                    ? result.Results.FirstOrDefault(r => !GhostService.IsGhost(r.Transponder))
                    : null);

            var who = string.IsNullOrWhiteSpace(driver) ? row?.Transponder ?? string.Empty : Key(driver);
            var name = row is { DriverName.Length: > 0 } ? row.DriverName : who;

            times.Add(new RunTime(who, name, run.ShootoutQualifyingPlace, row is null ? 0 : RunTimeMs(row, timedLaps), run.ShootoutRun, true));
        }

        var order = times
            .OrderBy(t => t.TimeMs > 0 ? 0 : 1)
            .ThenBy(t => t.TimeMs > 0 ? t.TimeMs : 0)
            .ThenBy(t => t.QualifyingPlace)
            .ToList();

        // How many runs the shootout has, from the runs themselves: a caller
        // holding only the runs raced so far (the event report, which sees
        // only its own sessions) must not take those as all of them and
        // call a half-run shootout complete, with a pole (bug hunt 22).
        var runCount = runs.Count == 0 ? 0 : Math.Max(runs.Count, runs.Max(r => r.ShootoutRunCount));

        return new Standing(order, done, runCount);
    }

    /// <summary>
    /// The A-main grid with the shootout at the front: the shootout's
    /// drivers in shootout order, then everyone else in the order they
    /// already had. A shootout driver no longer in that main (moved by hand)
    /// is not put back into it.
    /// </summary>
    public static List<string> Grid(IReadOnlyList<string> grid, Standing shootout, Func<string, string?>? primaryForAlternate = null)
    {
        string Key(string t) => primaryForAlternate?.Invoke(t) ?? t;

        var front = new List<string>();

        foreach (var run in shootout.Order)
        {
            var inGrid = grid.FirstOrDefault(t => string.Equals(Key(t), Key(run.Transponder), StringComparison.OrdinalIgnoreCase));

            if (inGrid is not null && !front.Contains(inGrid, StringComparer.OrdinalIgnoreCase))
            {
                front.Add(inGrid);
            }
        }

        return front.Concat(grid.Where(t => !front.Contains(t, StringComparer.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>
    /// The class's shootout runs among <paramref name="classRounds"/> (one
    /// race day's), in running order. Where a day has more than one set -
    /// built again - the set built last.
    /// </summary>
    public static List<RoundDefinition> RunsOf(IEnumerable<RoundDefinition> classRounds, string className) =>
        classRounds
            .Where(r => r.Kind == RoundKind.Shootout
                        && r.ShootoutRun > 0
                        && string.Equals(r.ClassName, className, StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.ShootoutRun)
            .Select(g => g.MaxBy(r => r.CreatedAtUtc)!)
            .OrderBy(r => r.ShootoutRun)
            .ToList();

    /// <summary>
    /// The mains whose grid the shootout sets: the class's A-main still to
    /// run - every leg of one run in legs (they share a grid), or an
    /// Invitational's first main - or an untiered main when the class runs a
    /// single final. A main already raced is left alone.
    /// </summary>
    public static List<RoundDefinition> GridTargets(IEnumerable<RoundDefinition> classRounds, string className)
    {
        var mains = classRounds
            .Where(r => r.Kind == RoundKind.Main
                        && !r.IsRecorded
                        && string.Equals(r.ClassName, className, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var invitational = mains.Where(r => r.InvitationalMain == 1).ToList();

        if (invitational.Count > 0)
        {
            return invitational;
        }

        var top = mains.Where(r => r.InvitationalMain == 0 && r.MainTier == 0).ToList();

        return top.Count > 0 ? top : mains.Where(r => r.InvitationalMain == 0 && r.MainTier < 0).ToList();
    }

    /// <summary>"12.345" - a run's time as it is shown and said.</summary>
    public static string TimeWords(long ms) => (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>
    /// The call after one run: "Sam, 12.345 - quickest so far." / "Sam,
    /// 12.901 - 3rd so far." / "Sam, no time." Short: the next car is
    /// already on its way out.
    /// </summary>
    public static string RunCall(string spokenName, Standing standing, string transponder)
    {
        var name = spokenName.Trim();
        var index = standing.Order.FindIndex(t => string.Equals(t.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

        if (index < 0 || standing.Order[index].TimeMs <= 0)
        {
            return $"{name}, no time.";
        }

        var time = TimeWords(standing.Order[index].TimeMs);

        if (standing.Complete)
        {
            return $"{name}, {time}.";
        }

        return index == 0
            ? $"{name}, {time} - quickest so far."
            : $"{name}, {time} - {AMainLegs.Ordinal(index + 1)} so far.";
    }

    /// <summary>The pole call once the last run is in: "Sam takes pole in the Pole Shootout, 12.345."</summary>
    public static string PoleCall(string spokenName, long timeMs) =>
        $"{spokenName.Trim()} takes pole in the {Name}, {TimeWords(timeMs)}.";

    /// <summary>The recap's fact line for the finished shootout: the order and the times.</summary>
    public static string Fact(Standing standing, Func<RunTime, string> nameOf)
    {
        var lines = standing.Order.Select((t, i) => $"{i + 1}. {nameOf(t)} {(t.TimeMs > 0 ? TimeWords(t.TimeMs) : "no time")}");

        return $"That was the last run of the {Name}, where the top {AMainLegs.Words(standing.Runs)} qualifiers each ran alone "
               + $"against the clock. The order, which is the front of the A-Main grid: {string.Join(", ", lines)}."
               + (standing.Pole is { } pole ? $" {nameOf(pole)} has pole." : string.Empty);
    }

    /// <summary>How a run with no time is placed, for the results and the docs.</summary>
    public const string NoTimeWords =
        "Fastest run takes pole. A run with no time keeps the driver's qualifying place among the shootout drivers, behind everyone who set a time.";
}
