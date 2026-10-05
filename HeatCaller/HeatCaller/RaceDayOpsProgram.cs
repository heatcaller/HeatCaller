namespace HeatCaller;

/// <summary>
/// The race-day manager's programme ops: the day's rounds and who is in
/// them, building qualifying, seeding the mains, bump-ups, moving a driver
/// between heats, and reordering or deleting a round. See
/// raceday-protocol.md.
///
/// Every one of these goes through the Program tab's own methods
/// (RaceProgramControl's Plan.../apply pairs) and HeatBalanceForm's move
/// rules - the phone never has a second copy of any programme logic. What
/// the desk confirms, the phone confirms, with the same words.
/// </summary>
public sealed partial class RaceDayService
{
    private const string PhoneUndo = "Phone: ";

    private void RegisterProgramOps()
    {
        Register("program.list", OpKind.Read, ProgramList);
        Register("program.heat", OpKind.Read, ProgramHeat);
        Register("program.standings", OpKind.Read, ProgramStandings);
        Register("program.buildQualifying", OpKind.Change, ProgramBuildQualifying);
        Register("program.seedMains", OpKind.Change, ProgramSeedMains);
        Register("program.bumpUps", OpKind.Change, ProgramBumpUps);
        Register("program.skipBumpUps", OpKind.Change, ProgramSkipBumpUps);
        Register("program.moveDriver", OpKind.Change, ProgramMoveDriver);
        Register("program.moveRound", OpKind.Change, ProgramMoveRound);
        Register("program.deleteRound", OpKind.Change, ProgramDeleteRound);
    }

    /// <summary>A round as the phone lists it. Status is worked out against the whole day, as the Program tab's grid does.</summary>
    private object RoundRow(RoundDefinition round, int position, string? nextId, IReadOnlyList<RoundDefinition> classRounds)
    {
        var onTrack = RoundOnTrackId;

        var status = round.IsRecorded ? "recorded"
            : string.Equals(round.Id, onTrack, StringComparison.Ordinal) ? "onTrack"
            : string.Equals(round.Id, nextId, StringComparison.Ordinal) ? "next"
            : "scheduled";

        return new
        {
            id = round.Id,
            position,
            label = round.RoundLabel,
            className = round.ClassName,
            kind = Camel(round.Kind.ToString()),
            tier = round.TierLetter.Length > 0 ? round.TierLetter : null,
            drivers = round.AssignedTransponders.Count,
            track = round.TrackLayoutName,
            status,
            sessionId = round.LinkedSessionId,
            bumpUpCount = round.BumpUpCount,
            bumpUpsApplied = round.BumpUpsApplied,

            // Which qualifying round a heat belongs to ("Q2"), "" for the one
            // unnumbered round, null for anything that is not a qualifier -
            // read exactly as the Move Drivers form reads it. A key for
            // grouping, never a caption: on a club night the heat is called
            // "Race 2" and its key is still "Q2". Show `label`.
            qualifyingRound = HeatBalanceForm.RoundKeyFor(round, classRounds.Select(r => (RoundDefinition?)r)),
        };
    }

    private List<RoundDefinition> TodaysRoundsForClass(string className) =>
        _host.Program.TodaysRounds()
            .Where(r => string.Equals(r.ClassName, className, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private OpResult ProgramList(OpContext context)
    {
        var filter = context.Text("className")?.Trim() ?? string.Empty;
        var day = _host.Program.TodaysRounds();
        var next = day.FirstOrDefault(r => !r.IsRecorded);
        var step = _host.Program.NextStep();

        var byClass = day
            .GroupBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RoundDefinition>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        var rows = new List<object>();
        var position = 1;

        foreach (var round in day)
        {
            var at = position++;

            if (filter.Length > 0 && !string.Equals(round.ClassName, filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.Add(RoundRow(round, at, next?.Id, byClass[round.ClassName]));
        }

        return OpResult.Read(new
        {
            raceDay = _host.Program.HasRaceDay,
            onTrackRoundId = RoundOnTrackId,
            nextRoundId = next?.Id,
            nextStep = new
            {
                stage = Camel(step.Stage.ToString()),
                headline = step.Headline,
                detail = step.Detail,
                action = step.ActionCaption,
                roundId = step.Round?.Id,
                className = step.ClassName,
            },
            classes = _host.Program.TodaysClassNames(),
            rounds = rows,
        });
    }

    private RoundDefinition RequireTodaysRound(OpContext context, string argName)
    {
        var id = context.RequireText(argName);

        return _host.Program.TodaysRounds().FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.Ordinal))
               ?? throw new OpRefused("That round isn't on today's program any more - reload it.");
    }

    private OpResult ProgramHeat(OpContext context)
    {
        var round = RequireTodaysRound(context, "roundId");
        var day = _host.Program.TodaysRounds().ToList();
        var next = day.FirstOrDefault(r => !r.IsRecorded);
        var classRounds = TodaysRoundsForClass(round.ClassName);

        var drivers = round.AssignedTransponders
            .Select(t =>
            {
                var profile = _host.Profiles.Get(t);

                return new
                {
                    id = DriverIdFor(profile?.Transponder ?? t),
                    // Channel-aware: an unnamed car's transponder is not
                    // sent through the relay in full (see ShownName).
                    name = ShownName(profile?.DriverName, profile?.Transponder ?? t, context.Channel),
                    className = profile?.ClassName ?? string.Empty,
                    inClass = profile is not null && profile.RunsClass(round.ClassName),
                    checkedIn = _host.CheckIns.IsCheckedIn(profile?.Transponder ?? t),
                    tech = profile is null ? null : TechView(profile),
                };
            })
            .ToList();

        // Who could be put in: the class's roster for today, less anyone
        // already in this heat - the "not in a heat" column of the form.
        var inHeat = round.AssignedTransponders.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var available = _host.Program.ClassRoster(round.ClassName)
            .Where(p => !inHeat.Contains(p.Transponder))
            .Select(p => new
            {
                id = DriverIdFor(p.Transponder),
                name = ShownName(p.DriverName, p.Transponder, context.Channel),
                checkedIn = _host.CheckIns.IsCheckedIn(p.Transponder),
                inHeats = classRounds
                    .Where(r => r.AssignedTransponders.Contains(p.Transponder, StringComparer.OrdinalIgnoreCase))
                    .Select(r => r.RoundLabel)
                    .ToList(),
            })
            .ToList();

        return OpResult.Read(new
        {
            round = RoundRow(round, day.FindIndex(r => r.Id == round.Id) + 1, next?.Id, classRounds),
            locked = round.IsRecorded,
            drivers,
            available,
        });
    }

    /// <summary>Where qualifying stands for a class - the same figures the Standings button shows (QualifyingCalculator.Compute over today's rounds).</summary>
    private OpResult ProgramStandings(OpContext context)
    {
        var className = context.RequireText("className");
        var raceClass = _host.Classes.All.FirstOrDefault(c => string.Equals(c.Name, className, StringComparison.OrdinalIgnoreCase));

        // Said, rather than an empty table that reads as "nobody has qualified".
        if (!_host.Program.HasRaceDay)
        {
            throw new OpRefused(RaceProgramControl.NoRaceDayStandings);
        }

        var entries = QualifyingCalculator.Compute(
            raceClass?.Name ?? className, _host.Rounds, _host.History, _host.Profiles, raceClass, _host.Program.QualifyingRoundsCounted());

        var method = raceClass?.QualifyingMethod ?? QualifyingMethod.TotalLaps;

        return OpResult.Read(new
        {
            className = raceClass?.Name ?? className,
            method = Camel(method.ToString()),
            consecutiveLaps = raceClass?.ConsecutiveLapCount ?? 3,
            standings = entries.Select((e, i) => new
            {
                position = i + 1,
                id = DriverIdFor(e.Transponder),
                name = ShownName(e.DriverName, e.Transponder, context.Channel),
                laps = e.BestLaps,
                timeMs = e.BestElapsedMs,
                fastLapMs = e.BestFastLapMs,
                consecutiveMs = e.BestConsecutiveMs,
                heatsRun = e.HeatsRun,
                heatsCounted = e.HeatsCounted,
                from = e.SourceRoundLabel,
            }).ToList(),
        });
    }

    private OpResult ProgramBuildQualifying(OpContext context)
    {
        RefuseWhileLive("build the program");

        var plan = _host.Program.PlanQualifyingBuild();

        if (!plan.Ok)
        {
            throw new OpRefused(plan.Message);
        }

        if (!context.Confirmed)
        {
            return OpResult.Ask(plan.Message);
        }

        var done = _host.Program.BuildQualifying(PhoneUndo);

        return done.Ok
            ? OpResult.Done(done.Message, done.Message)
            : OpResult.Refuse(done.Message);
    }

    private string RequireClassName(OpContext context)
    {
        var wanted = context.RequireText("className");

        return _host.Classes.All.FirstOrDefault(c => string.Equals(c.Name, wanted, StringComparison.OrdinalIgnoreCase))?.Name
               ?? throw new OpRefused($"There is no class called \"{wanted}\".");
    }

    private OpResult ProgramSeedMains(OpContext context)
    {
        var className = RequireClassName(context);

        RefuseWhileLive("seed the mains");

        var plan = _host.Program.PlanSeedMains(className);

        if (!plan.Ok)
        {
            throw new OpRefused(plan.Message);
        }

        if (!context.Confirmed)
        {
            return OpResult.Ask(plan.Message);
        }

        var done = _host.Program.SeedMainsFor(className, PhoneUndo);

        return done.Ok
            ? OpResult.Done(done.Message, done.Message)
            : OpResult.Refuse(done.Message);
    }

    /// <summary>
    /// A class's bump-ups: whichever main is ready. When everybody who
    /// earned a place is in it already, the one thing left is to mark it
    /// done - which the desk does after telling you, and the phone does
    /// after asking.
    /// </summary>
    private OpResult ProgramBumpUps(OpContext context)
    {
        var className = RequireClassName(context);

        // The drivers it would move, named as the phone's channel may see
        // them - see ShownPromotionName.
        var plan = _host.Program.PlanBumpUps(className, p => ShownPromotionName(p, _host.Profiles, context.Channel));

        if (!plan.Ok || plan.Target is not { } target)
        {
            throw new OpRefused(plan.Message);
        }

        if (string.Equals(target.Id, RoundOnTrackId, StringComparison.Ordinal))
        {
            throw new OpRefused($"{target.RoundLabel} is on track - bump-ups have to happen before it runs.");
        }

        if (!context.Confirmed)
        {
            return plan.NothingToMove
                ? OpResult.Ask(plan.Message.Replace(" are marked as done,", " will be marked as done,", StringComparison.Ordinal))
                : OpResult.Ask(plan.Message);
        }

        if (plan.NothingToMove)
        {
            _host.Program.SkipBumpUps(target, PhoneUndo);

            return OpResult.Done($"Marked bump-ups into {target.RoundLabel} done (nobody to move)",
                $"Bump-ups for {target.RoundLabel} are marked as done.");
        }

        var done = _host.Program.ApplyBumpUpsInto(target, PhoneUndo);

        return done.Ok
            ? OpResult.Done(done.Message, done.Message)
            : OpResult.Refuse(done.Message);
    }

    /// <summary>The desk's "No - skip bump-ups and carry on": marks the ready main's bump-ups done without moving anybody.</summary>
    private OpResult ProgramSkipBumpUps(OpContext context)
    {
        var className = RequireClassName(context);
        var plan = _host.Program.PlanBumpUps(className);

        if (!plan.Ok || plan.Target is not { } target)
        {
            throw new OpRefused(plan.Message);
        }

        if (!context.Confirmed)
        {
            return OpResult.Ask($"Skip bump-ups for {target.RoundLabel} and carry on? Nobody is moved up.");
        }

        _host.Program.SkipBumpUps(target, PhoneUndo);

        return OpResult.Done($"Skipped bump-ups into {target.RoundLabel}", $"Skipped bump-ups into {target.RoundLabel}.");
    }

    /// <summary>
    /// Moves one driver: from a heat to another heat of the same class, into
    /// a heat from nowhere (fromRoundId missing), or out of a heat
    /// (toRoundId missing). The Move Drivers Between Heats form's rules: a
    /// heat that has been run is locked, and nobody is in two heats of one
    /// qualifying round (HeatBalanceForm.DescribeMoveBlocker). Refused for
    /// the heat on track. Emptying a heat is asked about, as the form asks.
    /// </summary>
    private OpResult ProgramMoveDriver(OpContext context)
    {
        var from = context.Has("fromRoundId") ? RequireTodaysRound(context, "fromRoundId") : null;
        var to = context.Has("toRoundId") ? RequireTodaysRound(context, "toRoundId") : null;

        if (from is null && to is null)
        {
            throw new OpRefused("Say which heat to move the driver from, to, or both.");
        }

        if (from is not null && to is not null)
        {
            if (from.Id == to.Id)
            {
                throw new OpRefused("That's the same heat.");
            }

            if (!string.Equals(from.ClassName, to.ClassName, StringComparison.OrdinalIgnoreCase))
            {
                throw new OpRefused("Drivers can only be moved between heats of the same class.");
            }
        }

        foreach (var round in new[] { from, to })
        {
            if (round is null)
            {
                continue;
            }

            if (round.IsRecorded)
            {
                throw new OpRefused($"{round.RoundLabel} has been run, so its drivers are locked.");
            }

            if (string.Equals(round.Id, RoundOnTrackId, StringComparison.Ordinal))
            {
                throw new OpRefused($"{round.RoundLabel} is on track - change its drivers when it has finished.");
            }
        }

        var className = (from ?? to)!.ClassName;

        // The driver is found among the heat they are leaving first - a car
        // with no profile can still be in a heat - then among the drivers
        // on file.
        var transponder = (from is not null ? TransponderForDriverId(context.Text("driverId"), from.AssignedTransponders) : null)
                          ?? TransponderForDriverId(context.Text("driverId"))
                          ?? throw new OpRefused("That driver isn't on the laptop any more - reload the list.");

        if (from is not null && !from.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase))
        {
            throw new OpRefused($"{ShownNameOfTransponder(transponder, context.Channel)} isn't in {from.RoundLabel}.");
        }

        var classRounds = TodaysRoundsForClass(className);

        if (to is not null)
        {
            var heats = classRounds
                .Select(r => new HeatBalanceForm.HeatState(r, r.IsRecorded, r.AssignedTransponders))
                .ToList();

            // The refusal goes back to the phone, so it names drivers as the
            // phone's channel may see them - see ShownNameOf.
            if (HeatBalanceForm.DescribeMoveBlocker(
                    heats, from, to, transponder, _host.Program.HasRaceDay, t => ShownNameOfTransponder(t, context.Channel)) is { } refusal)
            {
                throw new OpRefused(refusal);
            }
        }

        var changes = new List<HeatBalanceForm.RosterChange>();

        if (from is not null)
        {
            var before = from.AssignedTransponders.ToList();
            var after = before.Where(t => !string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase)).ToList();

            if (after.Count == 0 && !context.Confirmed)
            {
                return OpResult.Ask($"{from.RoundLabel} will have no drivers left. Move {ShownNameOfTransponder(transponder, context.Channel)} anyway?");
            }

            changes.Add(new HeatBalanceForm.RosterChange(from.Id, before, after));
        }

        if (to is not null)
        {
            var before = to.AssignedTransponders.ToList();

            // At the back, as the form puts a driver dropped at the end.
            changes.Add(new HeatBalanceForm.RosterChange(to.Id, before, before.Append(transponder).ToList()));
        }

        var done = _host.Program.ApplyRosterChanges(className, changes, PhoneUndo);

        if (!done.Ok)
        {
            return OpResult.Refuse(done.Message);
        }

        // Said twice: in full on the laptop's console, and to the phone as
        // its channel may see the driver - an unnamed car's whole
        // transponder went back through the relay here. See ShownNameOf.
        string Said(string name) => (from, to) switch
        {
            ({ } a, { } b) => $"Moved {name} from {a.RoundLabel} to {b.RoundLabel}",
            ({ } a, null) => $"Took {name} out of {a.RoundLabel}",
            (null, { } b) => $"Put {name} in {b.RoundLabel}",
            _ => $"Moved {name}",
        };

        return OpResult.Done(
            $"{Said(NameOfTransponder(transponder))} ({className})",
            $"{Said(ShownNameOfTransponder(transponder, context.Channel))}.");
    }

    /// <summary>
    /// Moves a round in the running order, in front of another (or to the
    /// end). No confirmation - the same as a drag on the Program tab - and,
    /// like the drag, an undo entry on the laptop that puts the running
    /// order back (RaceProgramControl.MoveRoundsBefore).
    /// </summary>
    private OpResult ProgramMoveRound(OpContext context)
    {
        var round = RequireTodaysRound(context, "roundId");
        var before = context.Has("beforeRoundId") ? RequireTodaysRound(context, "beforeRoundId") : null;

        if (before is not null && before.Id == round.Id)
        {
            return OpResult.Read(new { changed = false });
        }

        // Asked for the end of TODAY's programme: in front of whatever
        // follows today's last round in the whole running order - the
        // Program tab's AnchorFor does the same for a drop past the last row.
        string? anchor = before?.Id;

        if (before is null)
        {
            var all = _host.Rounds.All.ToList();
            var last = _host.Program.TodaysRounds().LastOrDefault();
            var at = last is null ? -1 : all.FindIndex(r => r.Id == last.Id);

            anchor = at >= 0 && at + 1 < all.Count ? all[at + 1].Id : null;
        }

        _host.Program.MoveRoundsBefore(new[] { round.Id }, anchor);

        return OpResult.Done(
            before is null ? $"Moved {round.RoundLabel} ({round.ClassName}) to the end of the program" : $"Moved {round.RoundLabel} ({round.ClassName}) in front of {before.RoundLabel}",
            "Moved.",
            new { changed = true });
    }

    private OpResult ProgramDeleteRound(OpContext context)
    {
        var round = RequireTodaysRound(context, "roundId");

        if (string.Equals(round.Id, RoundOnTrackId, StringComparison.Ordinal))
        {
            throw new OpRefused($"{round.RoundLabel} is on track - its result would have nothing to be saved against. Delete it when it has finished.");
        }

        if (!context.Confirmed)
        {
            return OpResult.Ask($"Delete round \"{round.RoundLabel}\" ({round.ClassName}) from the program?\r\n\r\nThis does not delete any recorded session history.");
        }

        var done = _host.Program.DeleteRounds(new[] { round }, PhoneUndo);

        return done.Ok
            ? OpResult.Done($"Deleted {round.RoundLabel} ({round.ClassName}) from the program", done.Message)
            : OpResult.Refuse(done.Message);
    }
}
