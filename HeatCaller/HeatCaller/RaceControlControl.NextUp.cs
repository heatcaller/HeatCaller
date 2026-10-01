namespace HeatCaller;

/// <summary>
/// What runs next: the race format in force, the tech check before a start,
/// the Next Up bar, reseeding, the readiness check, and auto-advance.
///
/// Moved here unchanged from RaceControlControl.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class RaceControlControl
{
    private RaceClass? ClassFor(RoundDefinition? round) =>
        round is null || string.IsNullOrEmpty(round.ClassName)
            ? null
            : _raceClasses.All.FirstOrDefault(c =>
                string.Equals(c.Name, round.ClassName, StringComparison.OrdinalIgnoreCase));

    // ==================== Tech warning ====================

    /// <summary>
    /// The Tech Log to check a heat against.
    ///
    /// The shared store when the host passed one - that is the copy the
    /// Tech Log page writes to and the event binding keeps pointed at the
    /// current event. Without it, a fresh copy is read from disk (every
    /// inspection is saved the moment it is recorded) and pointed at the
    /// current event by hand, so the warning still works rather than
    /// silently never firing.
    /// </summary>
    private TechInspectionStore TechStoreForCheck()
    {
        if (_techInspections is not null)
        {
            return _techInspections;
        }

        var fresh = TechInspectionStore.Load();
        fresh.CurrentEventId = _events?.CurrentId ?? RaceEventStore.LegacyEventId;
        return fresh;
    }

    /// <summary>
    /// Whether scrutineering is actually being run for this class.
    ///
    /// "Warn about uninspected drivers" is on by default for every class,
    /// including all the ones at clubs that never run tech - and there,
    /// every heat would open with a list of the whole field. So a class
    /// counts as running tech when it has a checklist (an empty one is
    /// documented as "tech isn't run for this class"), or when somebody has
    /// recorded an inspection for it that could still clear a car here -
    /// at this race day, or recently (TechInspectionStore.NoRaceDayWindow)
    /// when it has none - the Tech Log
    /// also allows a plain pass/fail with no checklist.
    /// </summary>
    private static bool TechIsRunFor(RaceClass raceClass, TechInspectionStore tech)
    {
        if (raceClass.TechCheckItems.Count > 0)
        {
            return true;
        }

        bool SameClass(TechInspection i) =>
            string.Equals(i.ClassName, raceClass.Name, StringComparison.OrdinalIgnoreCase);

        // The legacy holder is every inspection from before events existed,
        // months of them - one pass-or-fail from last spring must not switch
        // the warning on today. Only a real event spans more than a day.
        var atThisEvent = !string.Equals(tech.CurrentEventId, RaceEventStore.LegacyEventId, StringComparison.Ordinal)
                          && tech.ForEvent(tech.CurrentEventId).Any(SameClass);

        // Otherwise, by the same rule as TechInspectionStore.Latest - which
        // IsClearedToday asks - an inspection with no event, or filed under
        // the legacy holder, counts while it is recent. This asked for
        // today's calendar date instead, so a club night running past
        // midnight switched the warning off while the clearances it relies
        // on were still good, and one from another race day, which clears
        // nobody here, could switch it on.
        var recentFrom = DateTime.UtcNow - TechInspectionStore.NoRaceDayWindow;
        var recentHere = tech.All.Any(i => SameClass(i)
                                           && i.InspectedAtUtc >= recentFrom
                                           && (string.IsNullOrEmpty(i.EventId)
                                               || string.Equals(i.EventId, RaceEventStore.LegacyEventId, StringComparison.Ordinal)
                                               || string.Equals(i.EventId, tech.CurrentEventId, StringComparison.Ordinal)));

        return atThisEvent || recentHere;
    }

    /// <summary>
    /// Drivers on a round's roster who are not cleared by tech, as
    /// "name - not inspected" / "name - Failed" lines. Empty when everyone
    /// is cleared, or when the check does not apply: no round, no roster,
    /// a class with "Warn about uninspected drivers" off, or a class that
    /// is not running tech at all.
    ///
    /// "Cleared" is exactly the Tech Log's own test (TechInspectionStore
    /// .IsClearedToday, class-scoped), so this and the Tech Log's
    /// Outstanding list can never disagree about who still needs doing.
    /// </summary>
    private List<string> UninspectedDrivers(RoundDefinition? round, TechInspectionStore tech)
    {
        var lines = new List<string>();

        if (round is null || ClassFor(round) is not { WarnOnUninspected: true } raceClass)
        {
            return lines;
        }

        if (!TechIsRunFor(raceClass, tech))
        {
            return lines;
        }

        var roster = round.AssignedTransponders
            .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var transponder in roster)
        {
            if (tech.IsClearedToday(transponder, raceClass.Name))
            {
                continue;
            }

            var profileName = _profiles.Get(transponder)?.DriverName;
            var name = string.IsNullOrWhiteSpace(profileName) ? transponder : profileName;
            var state = tech.Latest(transponder, raceClass.Name) is { } latest ? latest.ResultText : "not inspected";

            lines.Add($"{name} - {state}");
        }

        return lines;
    }

    /// <summary>
    /// Says, in the Race Control log, who in the heat being started has not
    /// passed tech.
    ///
    /// A warning and never a block, as the class setting promises: the
    /// race director decides who races, and a scrutineer running behind
    /// must not stop an event. Deliberately not a dialog either - this runs
    /// as the heat is staged, often inside the automatic Stage, Countdown,
    /// Green sequence, and a box waiting for OK would hold that sequence
    /// up. The same count is shown ahead of time on the Next Up bar.
    ///
    /// Skipped for practice and demo races, which are not heats.
    /// </summary>
    private void WarnAboutUninspectedDrivers()
    {
        if (_race.IsPracticeMode || _race.IsDemoMode)
        {
            return;
        }

        var round = _activeRound?.Invoke();

        if (round is null)
        {
            return;
        }

        List<string> lines;

        try
        {
            lines = UninspectedDrivers(round, TechStoreForCheck());
        }
        catch (Exception ex)
        {
            // A warning must never be the reason a heat fails to start.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [tech] Could not check the Scrutineering log for this heat: {ex.Message}");
            return;
        }

        if (lines.Count == 0)
        {
            return;
        }

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [tech] WARNING: {Plural.Of(lines.Count, "driver")} in {RoundTitle(round)} "
            + $"{(lines.Count == 1 ? "has" : "have")} not passed tech today: {string.Join("; ", lines)}. "
            + "The heat is not held up - record their inspection on Race Day \u2192 Who's Racing.");
    }

    /// <summary>Round override, else class format, else app default - and which of those it was, for the chip.</summary>
    private RaceFormat EffectiveFormat(out string source)
    {
        var round = _activeRound?.Invoke();

        if (round?.FormatOverride is { } overridden)
        {
            source = $"{round.RoundLabel} override";
            return overridden;
        }

        // A qualifier runs under the class's qualifier format when it has
        // one; everything else - and a qualifier without one - runs under
        // the class format. Both fall through to the app default.
        if (round?.Kind == RoundKind.Qualifier && ClassFor(round)?.QualifierFormat is { } qualifierFormat)
        {
            source = $"{round.ClassName} qualifiers";
            return qualifierFormat;
        }

        if (ClassFor(round)?.Format is { } classFormat)
        {
            source = $"{round!.ClassName} class";
            return classFormat;
        }

        source = "app default";
        return RaceFormat.FromSettings(_settings);
    }

    /// <summary>
    /// Re-resolves the format and pushes it into the session.
    ///
    /// The session is only written between races. Mid-race, the chip still
    /// updates - so the operator can see what the NEXT race will run
    /// under - but the race being run keeps the rules it started with;
    /// changing the lap limit under a moving field is not a feature.
    /// </summary>
    /// <param name="startingNewRace">
    /// True from Stage, Countdown and a first Green: a new race is about to
    /// begin, so its format is written whatever the state - including over
    /// a race the person has just agreed to abandon. Without this the
    /// session kept whatever the last writer left in it, and after a
    /// practice (which clears the limits) the next race ran with no lap or
    /// time limit at all.
    /// </param>
    private void ApplyEffectiveFormat(bool startingNewRace = false)
    {
        _fmt = EffectiveFormat(out var source);

        if (startingNewRace || _race.State is RaceState.Idle or RaceState.Finished)
        {
            _race.LapLimit = _fmt.LapLimit == 0 ? null : _fmt.LapLimit;
            _race.TimeLimit = _fmt.TimeLimitMinutes == 0 ? null : TimeSpan.FromMinutes(_fmt.TimeLimitMinutes);
            _race.MinLapTimeMs = _fmt.MinLapTimeMs;
            _race.StreakWindowPercent = _fmt.StreakWindowPercent;
            _race.StreakFasterLapBreaks = _fmt.StreakFasterBreaks;
            _race.StartType = _fmt.StartType;
            _race.StaggerClockMode = _fmt.StaggerClockMode;
            _race.FinishOnNextCrossing = _fmt.FinishOnNextCrossing;

            // The format has just been written in whole, so there is no
            // handicap-start override left to put back.
            _clockBeforeHandicapOverride = null;
        }

        _lblFormat.Text = $"Format ({source}):  {_fmt.Describe()}";
    }

    /// <summary>For MainForm to call when the round selection changes - the format follows the round.</summary>
    public void RefreshFormat() => ApplyEffectiveFormat();

    // ==================== The Next Up bar ====================

    /// <summary>The program's not-yet-run rounds in running order, scoped to the current event when there is a real one.</summary>
    private List<RoundDefinition> OrderedProgram()
    {
        if (_rounds is null)
        {
            return new List<RoundDefinition>();
        }

        var all = _rounds.All;

        // A real current event bounds the day; the legacy holder does not -
        // on it, the whole program is the day, which is how the app worked
        // before events existed.
        if (_events?.Current is { IsLegacy: false } current)
        {
            var members = new HashSet<string>(current.RoundIds, StringComparer.Ordinal);
            return all.Where(r => members.Contains(r.Id)).ToList();
        }

        return all.ToList();
    }

    /// <summary>
    /// What runs after the round in hand: the first not-yet-recorded round
    /// that isn't the one already selected. Null when the program is done
    /// or there is no program.
    /// </summary>
    private RoundDefinition? NextRound()
    {
        var active = _activeRound?.Invoke();

        return OrderedProgram()
            .FirstOrDefault(r => !r.IsRecorded && !string.Equals(r.Id, active?.Id, StringComparison.Ordinal));
    }

    private static string RoundTitle(RoundDefinition round) =>
        string.IsNullOrWhiteSpace(round.ClassName)
            ? round.RoundLabel
            : $"{round.RoundLabel} ({round.ClassName})";

    /// <summary>
    /// The race these controls act on, for the phone remote (27 Sep 2026):
    /// "Q2 Heat 1 (Stock Buggy)", a race name typed in, "Practice", or
    /// "Freeform race" with neither. The round is the one the race is being
    /// run for - the Round box's, or a restored race's. UI thread.
    /// </summary>
    public string RaceInHandTitle()
    {
        if (_race.IsPracticeMode)
        {
            return "Practice";
        }

        var round = RoundForThisRace();
        var typed = _txtSessionLabel.Text.Trim();

        var title = typed.Length > 0
            ? round is not null
              && !string.IsNullOrWhiteSpace(round.ClassName)
              && !typed.Contains(round.ClassName.Trim(), StringComparison.OrdinalIgnoreCase)
                ? $"{typed} ({round.ClassName.Trim()})"
                : typed
            : round is not null ? RoundTitle(round) : "Freeform race";

        return _race.IsDemoMode ? $"Demo: {title}" : title;
    }

    private void UpdateNextUpBar()
    {
        var program = OrderedProgram();
        var active = _activeRound?.Invoke();

        // --- NOW ---
        var running = active is null
            ? (string.IsNullOrWhiteSpace(_txtSessionLabel.Text) ? "freeform session" : _txtSessionLabel.Text)
            : RoundTitle(active);

        var now = $"{running} · {_race.State.ToString().ToLowerInvariant()}";

        // --- NEXT ---
        var next = program.FirstOrDefault(r => !r.IsRecorded && !string.Equals(r.Id, active?.Id, StringComparison.Ordinal));
        var warning = false;
        string nextText;

        if (next is null)
        {
            nextText = program.Count == 0
                ? "no program - Quick Race is one press away"
                : "program complete";
        }
        else
        {
            var roster = next.AssignedTransponders;

            var missing = _checkIns is null
                ? 0
                : roster.Count(t => !_checkIns.IsCheckedIn(t));

            // Only from the shared Tech Log: this runs every second, and the
            // fallback in TechStoreForCheck reads the file from disk.
            var notThroughTech = _techInspections is null
                ? 0
                : UninspectedDrivers(next, _techInspections).Count;

            warning = missing > 0 || notThroughTech > 0;

            var parts = $"{RoundTitle(next)} · {Plural.Of(roster.Count, "driver")}";

            if (missing > 0)
            {
                parts += $" · {missing} not checked in";
            }

            if (notThroughTech > 0)
            {
                parts += $" · {notThroughTech} not through tech";
            }

            // The convention every club already runs: the heat before you
            // marshals your heat. Said here so nobody has to be found and
            // told - the bar IS the telling.
            var index = program.FindIndex(r => string.Equals(r.Id, next.Id, StringComparison.Ordinal));

            if (index > 0)
            {
                parts += $" · marshals: {program[index - 1].RoundLabel}";
            }

            nextText = parts;
        }

        // --- DAY ---
        var total = program.Count;
        var done = program.Count(r => r.IsRecorded);
        var day = total == 0 ? string.Empty : $"{done}/{total} recorded";

        // Drift, when the run sheet gave this round a parseable time.
        // Freeform times ("~10:15", "TBD") simply produce no drift figure,
        // which is the right amount of certainty about them.
        if (next is not null && _daySchedule is not null)
        {
            var line = _daySchedule.All.FirstOrDefault(e =>
                string.Equals(e.RoundId, next.Id, StringComparison.Ordinal));

            // Shared with the Timetable's own reading rather than worked
            // out again here. This copy took the slot on today's date
            // flat, so once a club night ran past midnight a 23:55 heat
            // reached at 00:10 read as "1425 min ahead" - and the
            // Timetable, three feet away on the same laptop, said 15
            // behind. See ScheduleDrift.MinutesOff.
            if (line is not null && ScheduleDrift.MinutesOff(line.ScheduledTime, DateTime.Now) is { } driftMinutes)
            {
                if (driftMinutes >= 3)
                {
                    day += $" · {driftMinutes} min behind";
                }
                else if (driftMinutes <= -3)
                {
                    day += $" · {-driftMinutes} min ahead";
                }
            }
        }

        // --- Is anything owed before the next race can go out? ---
        //
        // The rule lives in ProgramNextStep, which puts bump-ups ahead of
        // running the next round and says why: "a bump-up changes who is
        // in the main above, so doing it after that main has gone out is
        // not a late correction - it is too late, full stop."
        //
        // This bar did not know. It offered Start Next on the A-Main, with
        // no warning, the instant the B-Main was recorded - while Today
        // and Heats & Mains both sat holding "Apply Bump-Ups". The one
        // screen the director actually watches was the one screen that
        // could get it wrong. Asking the same authority is the fix; the
        // bar still decides nothing.
        var idle = _race.State is RaceState.Idle or RaceState.Finished;

        if (!idle)
        {
            // Nothing is owed while a race is on: the button says "Race
            // Running" either way, and this is the busiest the app gets.
            _barStep = null;
        }
        else if (DateTime.UtcNow - _barStepReadUtc >= TimeSpan.FromSeconds(1))
        {
            _barStepReadUtc = DateTime.UtcNow;

            _barStep = NextStepProvider?.Invoke() is { } step
                       && step.Stage is ProgramStage.BumpUps or ProgramStage.SeedMains
                ? step
                : null;
        }

        if (_barStep is { } owed)
        {
            warning = true;
            nextText = owed.Headline + "  ·  " + owed.ActionCaption.TrimEnd('.');
        }

        _nextUpBar.ShowInfo(new NextUpBar.Info(now, nextText, day, warning, TimingProblem?.Invoke() ?? string.Empty));

        // While a session is under way the button starts nothing - that is
        // deliberate, the next heat must not be launchable by a stray
        // click; the checkered flag or Reset in Race Control comes first.
        // So the caption says what is true instead of promising a start:
        // "Race Running", greyed, until the session is over.
        var racing = !idle;

        _btnStartNext.Text = racing
            ? ProgramCaptions.RaceRunning
            : _barStep?.ActionCaption ?? ProgramCaptions.StartNext;

        _btnStartNext.Enabled = !racing && (_barStep is not null || next is not null);
    }

    /// <summary>
    /// Rebuilds the rosters of every not-yet-run qualifier from who is
    /// checked in RIGHT NOW - the one-press answer to a no-show or a late
    /// entry, instead of editing heats by hand while the room waits.
    ///
    /// Recorded rounds are never touched, and mains are left alone too:
    /// they are seeded from qualifying results, which re-seeding cannot
    /// improve on.
    /// </summary>
    private void ReseedRemaining()
    {
        if (_rounds is null || _checkIns is null)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [day] Redo Remaining Heats needs the program and check-in stores - not available here.");
            return;
        }

        var pending = OrderedProgram()
            .Where(r => r.Kind == RoundKind.Qualifier && !r.IsRecorded)
            .ToList();

        if (pending.Count == 0)
        {
            AppDialog.Show(FindForm(), "Every heat on the program has already been run, so there is nothing to redo.",
                "Nothing left to redo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = AppDialog.Show(FindForm(),
            $"Rebuild the rosters of {Plural.Of(pending.Count, "not-yet-run qualifier heat")} from who is checked in right now? "
            + "Recorded heats and mains are untouched. Undo puts the old rosters back.",
            "Redo remaining heats", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        // The old rosters, captured before anything moves, so one Undo puts
        // the whole re-seed back rather than one heat of it.
        var before = pending.ToDictionary(r => r.Id, r => r.AssignedTransponders.ToList(), StringComparer.Ordinal);

        _undo.Capture("Redo the remaining heats", () =>
        {
            foreach (var round in pending)
            {
                if (before.TryGetValue(round.Id, out var old))
                {
                    round.AssignedTransponders = old;
                    _rounds.Update(round);
                }
            }
        });

        var touched = 0;
        var skipped = new List<string>();

        foreach (var classGroup in pending.GroupBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase))
        {
            var roster = _profiles.All
                .Where(p => string.Equals(p.ClassName, classGroup.Key, StringComparison.OrdinalIgnoreCase)
                            && _checkIns.IsCheckedIn(p.Transponder))
                .Select(p => p.Transponder)
                .ToList();

            if (roster.Count == 0)
            {
                skipped.Add(classGroup.Key);
                continue;
            }

            // Heats regrouped by the round they belong to - "Q1 Heat 2"
            // groups under "Q1", a hand-made "Heat 2" under the blank
            // prefix - so the per-round rotation survives the re-seed.
            var groups = classGroup
                .GroupBy(r =>
                {
                    var cut = r.RoundLabel.IndexOf("Heat", StringComparison.OrdinalIgnoreCase);
                    return cut < 0 ? r.RoundLabel : r.RoundLabel[..cut].Trim();
                })
                .ToList();

            var groupIndex = 0;

            foreach (var group in groups)
            {
                var heats = group.ToList();
                var heatSize = (int)Math.Ceiling(roster.Count / (double)heats.Count);
                var shift = roster.Count == 0 ? 0 : groupIndex * heatSize % roster.Count;
                var rotated = roster.Skip(shift).Concat(roster.Take(shift)).ToList();

                var buckets = Enumerable.Range(0, heats.Count).Select(_ => new List<string>()).ToList();

                for (var i = 0; i < rotated.Count; i++)
                {
                    buckets[i % heats.Count].Add(rotated[i]);
                }

                for (var i = 0; i < heats.Count; i++)
                {
                    heats[i].AssignedTransponders = buckets[i];
                    _rounds.Update(heats[i]);
                    touched++;
                }

                groupIndex++;
            }
        }

        var note = $"{AppTime.LogStampNow()}  [day] Rebuilt {Plural.Of(touched, "heat")} from the live check-in sheet.";

        if (skipped.Count > 0)
        {
            note += $" Skipped {string.Join(", ", skipped)} - nobody checked in.";
        }

        StatusMessage?.Invoke(note);
        UpdateNextUpBar();
    }

    /// <summary>
    /// Opens the editor on the format that is actually in force, at its
    /// source: the round's override if that is what is running, else the
    /// class's format, else the app default. Editing what you can see
    /// beats a menu asking which of three formats you meant.
    /// </summary>
    private void EditFormat()
    {
        var round = _activeRound?.Invoke();

        if (round?.FormatOverride is not null && _rounds is not null)
        {
            using var overrideForm = new RaceFormatForm(
                round.FormatOverride, $"Race Format - {round.RoundLabel} (round override)", allowUseDefault: true,
                fallback: RaceFormat.FromSettings(_settings));

            if (overrideForm.ShowDialog(FindForm()) == DialogResult.OK)
            {
                // The old override, for Undo - put back onto the round as it
                // is on file then, by id, so an undo from further back does
                // not bring back a stale copy of the rest of the round.
                RoundDefinitionStore rounds = _rounds;
                var roundId = round.Id;
                var oldOverride = round.FormatOverride?.Clone();
                var newOverride = overrideForm.Result;

                _undo.Capture($"Edit race format for {round.RoundLabel}", () =>
                {
                    if (rounds.Get(roundId) is not { } current)
                    {
                        throw new InvalidOperationException("that round has been deleted since. Nothing was changed.");
                    }

                    if (!ReferenceEquals(current.FormatOverride, newOverride))
                    {
                        throw new InvalidOperationException(
                            $"{current.RoundLabel}'s format has been changed again since, and undoing this now would undo that too. Nothing was changed.");
                    }

                    current.FormatOverride = oldOverride;
                    rounds.Update(current);
                    ReapplyFormatAfterUndo();
                });

                round.FormatOverride = newOverride;
                _rounds.Update(round);
                ApplyEffectiveFormat();
            }

            return;
        }

        if (ClassFor(round) is { Format: not null } cls)
        {
            using var classForm = new RaceFormatForm(cls.Format, $"Race Format - {cls.Name}", allowUseDefault: true,
                fallback: RaceFormat.FromSettings(_settings));

            if (classForm.ShowDialog(FindForm()) == DialogResult.OK)
            {
                // As for the round above: only the format goes back, onto
                // the class as it is on file then.
                var classes = _raceClasses;
                var classId = cls.Id;
                var oldFormat = cls.Format?.Clone();
                var newFormat = classForm.Result;

                _undo.Capture($"Edit race format for {cls.Name}", () =>
                {
                    if (classes.Get(classId) is not { } current)
                    {
                        throw new InvalidOperationException("that class has been deleted since. Nothing was changed.");
                    }

                    if (!ReferenceEquals(current.Format, newFormat))
                    {
                        throw new InvalidOperationException(
                            $"{current.Name}'s format has been changed again since, and undoing this now would undo that too. Nothing was changed.");
                    }

                    current.Format = oldFormat;
                    classes.Upsert(current);
                    ReapplyFormatAfterUndo();
                });

                cls.Format = newFormat;
                _raceClasses.Upsert(cls);
                ApplyEffectiveFormat();
            }

            return;
        }

        using var defaultForm = new RaceFormatForm(
            RaceFormat.FromSettings(_settings), "App Default Race Format", allowUseDefault: false);

        if (defaultForm.ShowDialog(FindForm()) == DialogResult.OK && defaultForm.Result is { } edited)
        {
            var settings = _settings;
            var oldDefault = RaceFormat.FromSettings(settings);

            _undo.Capture("Edit the app default race format", () =>
            {
                oldDefault.SaveTo(settings);
                settings.Save();
                ReapplyFormatAfterUndo();
            });

            edited.SaveTo(_settings);
            _settings.Save();
            ApplyEffectiveFormat();
        }
    }

    /// <summary>
    /// Puts the page's format (and an idle race's numbers) back in line
    /// after an undone format edit, exactly as the edit itself did.
    /// ApplyEffectiveFormat already leaves a race under way alone.
    /// </summary>
    private void ReapplyFormatAfterUndo()
    {
        if (!IsDisposed)
        {
            ApplyEffectiveFormat();
        }
    }


    /// <summary>
    /// Whether finishing a race should arm the next one.
    ///
    /// The open race day's own answer, falling back to the club-wide
    /// setting for a day that never gave one - which is every day written
    /// before this moved onto the race day. See RaceEvent.AutoAdvanceOn.
    /// </summary>
    private bool AutoAdvanceWanted =>
        _events?.Active is { IsLegacy: false } day
            ? day.AutoAdvanceOn(_settings.AutoAdvanceRounds)
            : _settings.AutoAdvanceRounds;

    /// <summary>Which race to reach for - the day's, or the club's when the day never said.</summary>
    private string AutoAdvanceScope =>
        _events?.Active is { IsLegacy: false } day
            ? day.AutoAdvanceScopeOr(_settings.AutoAdvanceScope)
            : _settings.AutoAdvanceScope;

    /// <summary>The class the scope is pinned to, read together with the scope above.</summary>
    private string AutoAdvancePinnedClass =>
        _events?.Active is { IsLegacy: false } day
            ? day.AutoAdvanceClassOr(_settings.AutoAdvanceClassName)
            : _settings.AutoAdvanceClassName;

    /// <summary>Which round a finished race should complete when none was picked. See RoundPicker.</summary>
    private RoundDefinition? PickNextRound(IReadOnlyList<RacerStanding> ordered) =>
        _rounds is null
            ? null
            : RoundPicker.Next(
                _rounds,
                _events,
                AutoAdvanceScope,
                AutoAdvancePinnedClass,
                ordered.Select(s => _profiles.Get(s.TransponderId)?.ClassName),
                message => StatusMessage?.Invoke(message));


    /// <summary>
    /// Names the saved session. Uses the round's label when the race is tied
    /// to one, so the session pickers elsewhere show "Heat 1" instead of a
    /// generic "Race" - without that there's no way to match a qualifying
    /// figure back to the heat that produced it.
    /// </summary>
    private string BuildSessionLabel(RoundDefinition? round)
    {
        var typed = _txtSessionLabel.Text.Trim();

        var baseLabel = !string.IsNullOrWhiteSpace(typed)
            ? typed
            : !string.IsNullOrWhiteSpace(round?.RoundLabel)
                ? round!.RoundLabel
                : "Race";

        return _currentRaceIsDemo ? $"{SessionRecord.DemoLabelPrefix} {baseLabel}" : baseLabel;
    }

    /// <summary>
    /// Opens the pre-race check.
    ///
    /// The roster is the selected round's if there is one, because that is
    /// the list somebody has already agreed to. Failing that it is whoever
    /// is already on the board, which is at least a list of real drivers
    /// rather than every profile ever saved - checking a car that is not in
    /// this race and reporting it missing would train people to ignore this
    /// within an evening.
    /// </summary>
    private void ShowReadinessCheck()
    {
        if (_transponderWatch is null)
        {
            AppDialog.Show(this,
                "Nothing is listening to the decoder, so there is nothing to check against.",
                "Pre-race check", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return;
        }

        var round = _activeRound?.Invoke();

        List<string> roster;
        string source;

        if (round is { AssignedTransponders.Count: > 0 })
        {
            roster = round.AssignedTransponders.ToList();
            source = string.IsNullOrWhiteSpace(round.RoundLabel) ? "round roster" : $"roster for {round.RoundLabel}";
        }
        else
        {
            roster = _race.GetOrderedStandings().Select(s => s.TransponderId).ToList();
            source = "drivers already on the board";
        }

        using var form = new ReadinessCheckForm(roster, _transponderWatch, _profiles, source);
        form.ShowDialog(this);
    }
}
