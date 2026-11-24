namespace HeatCaller;

/// <summary>
/// The Pole Shootout and the Invitational (Reedy-style format) on the
/// programme (19 Nov 2026, race-night build plan row 14b): built with the
/// class's finals (SeedMainsFor), and kept in step as the night goes on
/// (KeepFinalsFormatsInStep) - the shootout's runs following qualifying
/// until one is raced, the A-main's grid put in shootout order once the
/// last run is in, and each Invitational main gridded on the points so far
/// once the main before it is filed.
/// </summary>
public sealed partial class RaceProgramControl
{
    /// <summary>
    /// What a bump-up from <paramref name="feeder"/> reads: its result, or a
    /// lower main run in legs' overall result (19 Nov 2026) - see
    /// MainsBuilder.FeederResults. Only asked once DescribeBumpUpBlocker has
    /// passed, so every leg is in.
    /// </summary>
    private SessionRecord FeederResultsFor(RoundDefinition feeder, IReadOnlyList<RoundDefinition> mains) =>
        MainsBuilder.FeederResults(feeder, mains, _sessionHistory.Get, ClassByName(feeder.ClassName)?.AMainLegsCounted ?? 0, _profiles.PrimaryForAlternate)!;

    /// <summary>A feeder as the bump-up question names it: its label, or "the B-Main overall" for a main run in legs.</summary>
    private static string FeederName(RoundDefinition feeder) =>
        feeder.MainLeg > 0 ? $"the {feeder.TierLetter}-Main overall" : feeder.RoundLabel;

    /// <summary>The class's Pole Shootout runs on tonight's programme, in running order.</summary>
    private List<RoundDefinition> ShootoutRunsToday(string className) =>
        PoleShootout.RunsOf(DayRoundsForClass(className), className);

    /// <summary>The Invitational's field in seed order - see Invitational.Field.</summary>
    private List<string> InvitationalField(string className, RaceClass raceClass, IReadOnlyList<string> ranked) =>
        Invitational.Field(
            ranked,
            RosterFor(className).Select(p => p.Transponder).ToList(),
            raceClass.InvitationalSeed,
            raceClass.InvitationalFieldLimit);

    /// <summary>The build question for an Invitational: who is in it, seeded how, and how it is scored.</summary>
    private string InvitationalQuestion(string className, RaceClass raceClass, IReadOnlyList<string> field, bool qualified)
    {
        var mains = Invitational.MainsFor(raceClass);
        var counting = Invitational.CountingFor(raceClass);
        var seededBy = qualified
            ? "seeded by qualifying"
            : raceClass.InvitationalSeed is { Count: > 0 } ? "seeded by the class's seed order" : "seeded in roster order (no qualifying, and no seed order set)";

        var grid = string.Join("\r\n", field.Select((t, i) => $"   {i + 1}. {(_profiles.Get(t)?.DriverName is { Length: > 0 } name ? name : t)}"));

        return $"Build the {Invitational.Name} for \"{className}\": {Plural.Of(field.Count, "driver")}, {seededBy}, in "
               + $"{Plural.Of(mains, "main")}?\r\n\r\nMain 1's grid:\r\n{grid}"
               + $"\r\n\r\nEvery driver races every main. A finish scores {Invitational.ScoringWords(raceClass)}, the best {counting} "
               + $"of {mains} count and fewest points wins. {Invitational.TieBreakWords} "
               + (raceClass.InvitationalInvertGrid
                   ? "After main 1 the grid is inverted: the points leader starts at the back."
                   : "After main 1 each grid is set by the points so far, the leader at the front.");
    }

    /// <summary>The build question's line about the class's Pole Shootout, or nothing when it has none.</summary>
    private string ShootoutNote(string className, RaceClass? raceClass, IReadOnlyList<string> ranked)
    {
        if (raceClass is not { PoleShootout: true })
        {
            return string.Empty;
        }

        if (ShootoutRunsToday(className).Any(r => r.IsRecorded))
        {
            return $"\r\n\r\nThe {PoleShootout.Name} is under way and is kept as it is; its result sets the front of the new A-Main's grid.";
        }

        var field = PoleShootout.Field(ranked, PoleShootout.DriversFor(raceClass));

        if (field.Count < PoleShootout.MinDrivers)
        {
            return $"\r\n\r\nNo {PoleShootout.Name}: fewer than {PoleShootout.MinDrivers} drivers have qualified.";
        }

        var laps = PoleShootout.TimedLapsFor(raceClass);

        return $"\r\n\r\n{PoleShootout.Name} first: the top {field.Count} run alone, slowest first, an out lap and "
               + (laps == 1 ? "one timed lap" : $"{laps} timed laps (best counts)")
               + ". The fastest takes pole and the shootout sets the front of the A-Main's grid.";
    }

    /// <summary>
    /// Keeps tonight's Pole Shootouts and Invitationals in step with the
    /// results - every class, or just <paramref name="onlyClass"/>. Called
    /// once a race is filed, once a result is corrected, and after the
    /// finals are built. Never throws: a fault is put in the status line,
    /// and the night goes on with the grids as they were.
    /// </summary>
    public void KeepFinalsFormatsInStep(string? onlyClass = null)
    {
        if (ActiveDay is null)
        {
            return;
        }

        var classNames = DayRounds()
            .Where(r => r.Kind is RoundKind.Shootout || r.InvitationalMain > 0 || r.Kind == RoundKind.Main)
            .Select(r => r.ClassName)
            .Where(c => onlyClass is null || string.Equals(c, onlyClass, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var className in classNames)
        {
            try
            {
                var raceClass = ClassByName(className);

                KeepShootoutRunsInStep(className, raceClass);
                SetShootoutGrid(className, raceClass);
                SetInvitationalGrids(raceClass, DayRoundsForClass(className));
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"The {PoleShootout.Name} or Invitational grids for {className} could not be brought up to date: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// The shootout's runs follow qualifying until one of them has been
    /// raced: a corrected result or a late heat moves the drivers between
    /// the runs, and a field of a different size builds them again in the
    /// same place on the programme.
    /// </summary>
    private void KeepShootoutRunsInStep(string className, RaceClass? raceClass)
    {
        var runs = ShootoutRunsToday(className);

        if (runs.Count == 0 || raceClass is not { PoleShootout: true } || OnTrackRound(runs) is not null)
        {
            return;
        }

        var field = PoleShootout.Field(SeedingOrder(className, raceClass), PoleShootout.DriversFor(raceClass));

        switch (PoleShootout.Refresh(runs, field))
        {
            case PoleShootout.RefreshResult.Updated:
                foreach (var run in runs)
                {
                    _rounds.Update(run);
                }

                _lblStatus.Text = $"{PoleShootout.Name} for {className} follows the new qualifying order.";
                break;

            // WHY (24 Nov 2026, bug hunt 24 #269): the drivers the director
            // left out by deleting their run stay out of a rebuild, and are
            // carried on to the new runs. Built from the whole field, they
            // came back. And when too few would be left to run, the runs
            // are left as they are rather than all taken away.
            case PoleShootout.RefreshResult.Rebuild
                when field.Count - PoleShootout.LeftOutOf(runs, field).Count >= PoleShootout.MinDrivers:
            {
                var leftOut = PoleShootout.LeftOut(runs);

                // In the place the old runs had: in front of whatever came after them.
                var lastOrder = runs.Max(r => r.Order);
                var after = _rounds.All.FirstOrDefault(r => r.Order > lastOrder && runs.All(x => x.Id != r.Id))?.Id;

                foreach (var run in runs)
                {
                    _rounds.Delete(run.Id);
                    _events?.RemoveRound(run.Id);
                }

                // WHY (23 Nov 2026, bug hunt 24): with the app default, as the
                // first build has it - a class following the app default has
                // a null Format, and a rebuild raced under the built-in 3 s
                // min lap and automatic start instead of the club's default.
                var built = PoleShootout.BuildRuns(className, field, PoleShootout.TimedLapsFor(raceClass), raceClass.Format, DayTrack(), RaceFormat.FromSettings(_settings), leftOut);

                foreach (var run in built)
                {
                    _rounds.Add(run);
                }

                _events?.AddRoundsToActive(built.Select(r => r.Id));
                _rounds.MoveBefore(built.Select(r => r.Id).ToList(), after);
                _lblStatus.Text = $"{PoleShootout.Name} for {className} built again for the new qualifying order: {Plural.Of(built.Count, "run")}.";
                break;
            }
        }
    }

    /// <summary>
    /// Before <paramref name="removed"/> are deleted from the programme: for
    /// each Pole Shootout one of them is a run of, writes onto the runs
    /// left that its driver is left out and how many runs there are now
    /// (PoleShootout.LeaveOut). Returns what the director is told, or
    /// nothing when no shootout run is among them (or none of its runs is left).
    /// </summary>
    /// <remarks>
    /// WHY (24 Nov 2026, bug hunt 24 #269): without it the deleted run came
    /// back after the next filed race, with "built again for the new
    /// qualifying order" though qualifying had not changed.
    /// </remarks>
    private string LeaveOutOfShootouts(IReadOnlyList<RoundDefinition> removed)
    {
        var ids = removed.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var words = new List<string>();

        foreach (var className in removed.Where(r => r.Kind == RoundKind.Shootout).Select(r => r.ClassName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var runs = ShootoutRunsToday(className);
            var deleted = runs.Where(r => ids.Contains(r.Id)).ToList();
            var left = runs.Where(r => !ids.Contains(r.Id)).ToList();

            if (deleted.Count == 0 || left.Count == 0)
            {
                continue;
            }

            foreach (var run in PoleShootout.LeaveOut(left, deleted))
            {
                _rounds.Update(run);
            }

            var names = deleted
                .Select(PoleShootout.SlotDriver)
                .Where(t => t.Length > 0)
                .Select(t => _profiles.Get(t)?.DriverName is { Length: > 0 } name ? name : t)
                .ToList();

            words.Add($"{(names.Count == 0 ? "That driver is" : string.Join(" and ", names) + (names.Count == 1 ? " is" : " are"))} left out of the {PoleShootout.Name} for {className}. "
                      + (left.Count == 1 ? "The one run left sets" : $"The {left.Count} runs left set") + " the front of the A-Main.");
        }

        return string.Join(" ", words);
    }

    /// <summary>
    /// After an undo has put deleted rounds back: each Pole Shootout one of
    /// them was a run of counts its runs and who is left out again, from the
    /// runs there now. Not when the shootout has been built again since -
    /// the run put back is then not one of its runs, and the new runs
    /// already know who is left out.
    /// </summary>
    private void RecountShootoutsAfterUndo(IReadOnlyList<RoundDefinition> restored)
    {
        var ids = restored.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var className in restored.Where(r => r.Kind == RoundKind.Shootout).Select(r => r.ClassName).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var runs = ShootoutRunsToday(className);

            if (!runs.Any(r => ids.Contains(r.Id)))
            {
                continue;
            }

            foreach (var run in PoleShootout.LeaveOut(runs, Array.Empty<RoundDefinition>()))
            {
                _rounds.Update(run);
            }
        }
    }

    /// <summary>Once every run is in, the A-Main's grid in shootout order - once, so a change made by hand afterwards stands.</summary>
    private void SetShootoutGrid(string className, RaceClass? raceClass)
    {
        var runs = ShootoutRunsToday(className);

        if (runs.Count == 0 || runs.Any(r => !r.IsRecorded))
        {
            return;
        }

        var standing = PoleShootout.Standings(runs, _sessionHistory.Get, PoleShootout.TimedLapsFor(raceClass), _profiles.PrimaryForAlternate);

        foreach (var main in PoleShootout.GridTargets(DayRoundsForClass(className), className).Where(m => !m.ShootoutGridSet))
        {
            main.AssignedTransponders = PoleShootout.Grid(main.AssignedTransponders, standing, _profiles.PrimaryForAlternate);
            main.ShootoutGridSet = true;
            _rounds.Update(main);
            _lblStatus.Text = $"{main.RoundLabel} for {className}: grid set by the {PoleShootout.Name}.";
        }
    }

    /// <summary>Each Invitational main after one that has been filed: gridded on the points so far, once.</summary>
    private void SetInvitationalGrids(RaceClass? raceClass, IReadOnlyList<RoundDefinition> classRounds)
    {
        foreach (var filed in classRounds.Where(r => r is { Kind: RoundKind.Main, InvitationalMain: > 0, IsRecorded: true }).OrderBy(r => r.InvitationalMain))
        {
            var series = Invitational.SeriesOf(filed, classRounds);

            if (Invitational.NextToGrid(filed, series, RoundOnTrackId?.Invoke()) is not { } next)
            {
                continue;
            }

            var table = Invitational.Standings(
                series.Where(r => r.InvitationalMain < next.InvitationalMain).ToList(),
                _sessionHistory.Get,
                raceClass,
                _profiles.PrimaryForAlternate,
                t => _profiles.Get(t)?.DriverName ?? t);

            if (table is null)
            {
                continue;
            }

            var again = next.InvitationalGridSet;

            if (!Invitational.SetGrid(next, Invitational.NextGrid(table, next.AssignedTransponders, raceClass?.InvitationalInvertGrid ?? false, _profiles.PrimaryForAlternate)))
            {
                continue;
            }

            _rounds.Update(next);
            _lblStatus.Text = again
                ? $"{next.RoundLabel}: grid set again from the corrected points."
                : $"{next.RoundLabel}: grid set by the points so far{(raceClass?.InvitationalInvertGrid == true ? ", inverted" : string.Empty)}.";
        }
    }
}
