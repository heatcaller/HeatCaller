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
    internal static bool TechIsRunFor(RaceClass raceClass, TechInspectionStore tech)
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
    /// Whether the check-in desk and the phone judge a driver's tech in this
    /// class (TechInspectionStore.LatestForDriver): it runs tech, it is
    /// racing tonight when the race day names its classes, and it is the
    /// class picked in the desk's Class: filter when one is.
    ///
    /// They used to ask TechIsRunFor alone, true for any class with a
    /// checklist whether it was racing or not. A driver cleared in the one
    /// class on tonight's race day still read as not inspected, because of
    /// a second class that was not racing, and stayed on the desk's "not
    /// through tech" list even with that one class picked - while Race
    /// Control, which only judges a heat's own class, let them go.
    /// </summary>
    internal static bool DeskJudgesTechFor(string className, IEnumerable<RaceClass> classes, TechInspectionStore tech,
        RaceEvent? day, string? classFilter = null) =>
        (classFilter is null || string.Equals(className, classFilter, StringComparison.OrdinalIgnoreCase))
        && (day is not { IsLegacy: false, ClassNames.Count: > 0 } || day.ClassNames.Contains(className, StringComparer.OrdinalIgnoreCase))
        && classes.FirstOrDefault(c => string.Equals(c.Name, className, StringComparison.OrdinalIgnoreCase)) is { } raceClass
        && TechIsRunFor(raceClass, tech);

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
        if (round is not { } heat)
        {
            return new List<string>();
        }

        return UninspectedDriversOf(
            heat,
            _raceClasses.All,
            tech,
            t => MixedHeats.ClassOfDriver(heat, t, _profiles),
            t => _profiles.Get(t)?.DriverName);
    }

    /// <summary>
    /// The rule behind <see cref="UninspectedDrivers"/>, away from the
    /// control: each car on the roster is judged in its own class of the
    /// round (classOfDriver), with that class's "Warn about uninspected
    /// drivers" and whether it runs tech at all.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): this judged every car against the
    /// heat's main class. In a shared heat (Stock + Mod) a Mod car passed
    /// under Mod was listed "not inspected" against Stock, and when Stock
    /// had the warning off or ran no tech the Mod cars were never checked.
    /// For a one-class round every car's class is the round's class, so
    /// nothing changes there.
    /// </summary>
    internal static List<string> UninspectedDriversOf(RoundDefinition round, IEnumerable<RaceClass> classes, TechInspectionStore tech,
        Func<string, string> classOfDriver, Func<string, string?> nameOf)
    {
        var lines = new List<string>();
        var all = classes.ToList();

        // Per class name, the class to judge in, or null when its cars are not checked. Cached because the bar asks every second.
        var judged = new Dictionary<string, RaceClass?>(StringComparer.OrdinalIgnoreCase);

        RaceClass? JudgedClass(string className)
        {
            if (!judged.TryGetValue(className, out var raceClass))
            {
                raceClass = all.FirstOrDefault(c => string.Equals(c.Name, className, StringComparison.OrdinalIgnoreCase)) is { WarnOnUninspected: true } found
                            && TechIsRunFor(found, tech)
                    ? found
                    : null;
                judged[className] = raceClass;
            }

            return raceClass;
        }

        var roster = round.AssignedTransponders
            .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var transponder in roster)
        {
            var className = classOfDriver(transponder);

            if (string.IsNullOrEmpty(className) || JudgedClass(className) is not { } raceClass)
            {
                continue;
            }

            if (tech.IsClearedToday(transponder, raceClass.Name))
            {
                continue;
            }

            var profileName = nameOf(transponder);
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
        // A demo re-running a round already run takes that round's format
        // too (18 Oct 2026) - see ShowRound.
        var round = ShowRound();

        return FormatFor(round, ClassFor(round), () => RaceFormat.FromSettings(_settings), out source);
    }

    /// <summary>
    /// The format a round runs under: its override, else (for a qualifier)
    /// the class's qualifier format, else the class's format, else the app
    /// default. EffectiveFormat asks for the round in the Round box; the
    /// gap's count to the green asks for the race being counted down (14 Nov
    /// 2026), which is not the Round box's round until it starts.
    /// </summary>
    internal static RaceFormat FormatFor(RoundDefinition? round, RaceClass? raceClass, Func<RaceFormat> appDefault, out string source)
    {
        if (round?.FormatOverride is { } overridden)
        {
            source = $"{round.RoundLabel} override";
            return overridden;
        }

        // A qualifier runs under the class's qualifier format when it has
        // one; everything else - and a qualifier without one - runs under
        // the class format. Both fall through to the app default.
        if (round?.Kind == RoundKind.Qualifier && raceClass?.QualifierFormat is { } qualifierFormat)
        {
            source = $"{round.ClassName} qualifiers";
            return qualifierFormat;
        }

        if (raceClass?.Format is { } classFormat)
        {
            source = $"{round!.ClassName} class";
            return classFormat;
        }

        source = "app default";
        return appDefault();
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
    public void RefreshFormat()
    {
        ApplyEffectiveFormat();

        // WHY (23 Oct 2026, timing layer, P2): the host calls this when the
        // Round box changes, and the crash journal records the round. Each
        // write reads it afresh anyway; this keeps the copy that stands once
        // this page is gone up to date.
        _journalRecorder.CaptureDetails();
    }

    // ==================== The Next Up bar ====================

    /// <summary>The program's not-yet-run rounds in running order, scoped to the current event when there is a real one.</summary>
    private List<RoundDefinition> OrderedProgram()
    {
        if (_rounds is null)
        {
            return new List<RoundDefinition>();
        }

        return ProgramOfDay(_events?.Current, _rounds.All, _activeRound?.Invoke()?.Id);
    }

    /// <summary>
    /// The rounds of <paramref name="current"/> in running order (all of
    /// them on the legacy holder or with no day), as OrderedProgram gives
    /// them. On a CLOSED real day, only its recorded rounds and the round
    /// in <paramref name="keepRoundId"/> (a race already under way on it) -
    /// none of its leftover heats is offered as still to run. Pure.
    /// </summary>
    internal static List<RoundDefinition> ProgramOfDay(RaceEvent? current, IReadOnlyList<RoundDefinition> all, string? keepRoundId)
    {
        // A real current event bounds the day; the legacy holder does not -
        // on it, the whole program is the day, which is how the app worked
        // before events existed.
        if (current is { IsLegacy: false })
        {
            var members = new HashSet<string>(current.RoundIds, StringComparer.Ordinal);

            // WHY (23 Nov 2026, bug hunt 24): a closed day picked on The Day
            // to look at last week's results is the current day, and this
            // offered its leftover unrun heats - the C-Main dropped when the
            // light went - as next: the Next Up bar named it, Start Next
            // started it and Auto start counted down to it, and tonight's race
            // was filed into last week's meeting. RoundPicker.RaceDayOpen
            // already refuses that for the automatic pick; nothing new joins
            // a closed day until it is reopened.
            return all.Where(r => members.Contains(r.Id)
                                  && (current.IsOpen
                                      || r.IsRecorded
                                      || string.Equals(r.Id, keepRoundId, StringComparison.Ordinal)))
                .ToList();
        }

        return all.ToList();
    }

    /// <summary>How many real cars on <paramref name="roster"/> are not checked in - a ghost never is, and is never missing. Pure.</summary>
    internal static int NotCheckedInCount(IEnumerable<string> roster, Func<string, bool> isCheckedIn) =>
        roster.Count(t => !GhostService.IsGhost(t) && !isCheckedIn(t));

    /// <summary>
    /// Who Redo Remaining Heats counts as here: the checked in, and a ghost
    /// car already on a heat - it can never be checked in, and is the
    /// director's to take off, not the re-deal's (bug hunt 24). Pure.
    /// </summary>
    internal static Func<string, bool> PresentForRedo(Func<string, bool> isCheckedIn) =>
        t => GhostService.IsGhost(t) || isCheckedIn(t);

    /// <summary>The Next Up bar's words when nothing is next, or null when the bar says what it always did. Pure.</summary>
    internal static string? ClosedDayNextText(RaceEvent? current) =>
        current is { IsLegacy: false, IsOpen: false }
            ? $"{current.DisplayName} is closed - reopen it to run its heats, or start tonight's race day"
            : null;

    /// <summary>
    /// What runs after the round in hand: the first not-yet-recorded round
    /// that isn't the one already selected. Null when the program is done
    /// or there is no program.
    /// </summary>
    /// <param name="alsoSkip">A round to pass over as well - one just run whose result is not linked to it yet.</param>
    private RoundDefinition? NextRound(string? alsoSkip = null)
    {
        var active = _activeRound?.Invoke();

        return OrderedProgram()
            .FirstOrDefault(r => !r.IsRecorded
                                 && !string.Equals(r.Id, active?.Id, StringComparison.Ordinal)
                                 && (alsoSkip is null || !string.Equals(r.Id, alsoSkip, StringComparison.Ordinal)));
    }

    /// <summary>
    /// The next <paramref name="count"/> races still to run, in running
    /// order, for the phones following a driver (PhoneFollow): the round in
    /// hand first while it is staged or on track, then the program's
    /// unrecorded rounds. Each with its cars in grid order. UI thread.
    /// Read by the Wi-Fi follow view and, per badge, by the relay's private
    /// driverlive (MainForm.DriverLive, 22 Oct 2026, driver experience).
    /// </summary>
    internal IReadOnlyList<UpcomingRace> UpcomingRaces(int count)
    {
        var active = _activeRound?.Invoke();

        // Not a demo: a rehearsal on the selected round is not that round on
        // track, and real drivers' phones said "On track now".
        var onTrack = (_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
                      && !_race.IsPracticeMode && !_race.IsDemoMode;

        var unrun = OrderedProgram()
            .Where(r => !r.IsRecorded && r.AssignedTransponders.Count > 0)
            .ToList();

        // The round on track first, wherever it sits in the program: run out
        // of order, it could fall past the end of the list and its drivers
        // were never told it was theirs.
        var onTrackRound = onTrack && active is not null
            ? unrun.FirstOrDefault(r => string.Equals(r.Id, active.Id, StringComparison.Ordinal))
            : null;

        var ordered = onTrackRound is null
            ? unrun
            : unrun.Where(r => !ReferenceEquals(r, onTrackRound)).Prepend(onTrackRound).ToList();

        // 22 Oct 2026 (driver experience, item 3): each race also carries
        // its round id (the phone's marshal duty is matched on it), whether
        // the one on track is only staged - its drivers belong at the
        // driver stand NOW, which "On track now" never told them - and when
        // it is expected to start (RaceEta). "away" counts the way
        // PhoneFollow.CallUps counts it: the race on track is not one to
        // wait for.
        var schedule = _daySchedule?.All ?? Array.Empty<ScheduleEntry>();
        var late = _daySchedule?.MinutesLate();
        var starts = schedule
            .Where(e => e.ActualStartUtc is not null)
            .Select(e => e.ActualStartUtc!.Value)
            .ToList();
        var nowUtc = DateTime.UtcNow;
        var staged = _race.State == RaceState.Staging;

        var upcoming = new List<UpcomingRace>();
        var before = 0;

        foreach (var round in ordered.Take(Math.Max(0, count)))
        {
            var isOnTrack = ReferenceEquals(round, onTrackRound);

            // Nothing to estimate for the race already on track.
            // Auto start's own start beats any estimate (11 Nov 2026).
            var eta = isOnTrack ? null : AutoStartsAtUtc(round.Id, nowUtc) ?? RaceEta.For(round.Id, before, onTrack, schedule, late, starts, nowUtc);

            upcoming.Add(new UpcomingRace(
                RoundTitle(round), round.AssignedTransponders.ToList(), isOnTrack,
                round.Id, isOnTrack && staged, eta));

            if (!isOnTrack)
            {
                before++;
            }
        }

        return upcoming;
    }

    private static string RoundTitle(RoundDefinition round) =>
        string.IsNullOrWhiteSpace(round.ClassName)
            ? round.RoundLabel
            : $"{round.RoundLabel} ({MixedHeats.ClassLabel(round)})"; // both classes of a shared heat (Phase 4.1)

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

        // The same name the race will be filed under - see TypedLabelFor.
        var typed = TypedLabelFor(round);

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
            // A closed day says why nothing is next (bug hunt 24, see ProgramOfDay).
            nextText = ClosedDayNextText(_events?.Current)
                       ?? (program.Count == 0
                           ? "no program - Quick Race is one press away"
                           : "program complete");
        }
        else
        {
            var roster = next.AssignedTransponders;

            // WHY (23 Nov 2026, bug hunt 24): a ghost car can never be checked
            // in, so a heat with a ghost on it read "1 not checked in" in the
            // warning colour while the run sheet said everyone was here.
            var missing = _checkIns is null
                ? 0
                : NotCheckedInCount(roster, _checkIns.IsCheckedIn);

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

            // Auto start's count, first after the name so it is never the
            // part cut off (11 Nov 2026).
            if (AutoStartWords(DateTime.UtcNow) is { Length: > 0 } auto)
            {
                parts = $"{RoundTitle(next)} · {auto}" + parts[RoundTitle(next).Length..];
                warning |= _autoStartState is AutoStartState.WaitingForCars or AutoStartState.WaitingForMarshals or AutoStartState.Held;
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

        // Track evolution for the next race's class (TonightAtTheTrack): how
        // much quicker or slower the class is running than in its first race
        // tonight - what the drivers ask each other in the pits.
        if (EvolutionFor(next?.ClassName) is { } evolution)
        {
            var words = evolution.Words;
            day += $"{(day.Length > 0 ? " · " : string.Empty)}{evolution.ClassName}: {char.ToLowerInvariant(words[0])}{words[1..]}";
        }

        // Offline-first, said out loud (11 Nov 2026, Phase 6.3): "Website
        // offline - racing carries on. Online pages will catch up", and the
        // standby PC's copy line on a standby - see OfflineFirst. First, so
        // a long day line never cuts it off; it is only there while true.
        if (ResilienceLine?.Invoke() is { Length: > 0 } resilience)
        {
            day = day.Length > 0 ? $"{resilience} · {day}" : resilience;
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

        // 17 Oct 2026, Bug Hunt 8, A03-1: greyed while On Air is opening a
        // race too - a second Start would start the round after it.
        _btnStartNext.Enabled = !racing && !OnAirRunning && (_barStep is not null || next is not null);

        // Read by the flag row: Call Up is greyed when there is nothing next,
        // and Stage stands in when the talk buttons are off (see
        // RaceBar.ButtonsFor).
        _nextWaiting = _barStep is not null || next is not null;

        // Score by Hand, while the decoder is down mid-race (11 Nov 2026).
        UpdateHandScoreOffer();
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

        // Not while a session is under way. The heat on track is not
        // recorded yet, so it counts as "not yet run" below - and rebuilding
        // its roster mid-race would leave the Next Up bar, the tech
        // warnings and the seeding reading a grid that is not the one
        // racing. Start Next and Quick Race refuse the same way.
        if (_race.State is not (RaceState.Idle or RaceState.Finished))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [day] Redo Remaining Heats: a session is already under way - finish or reset it first.");
            return;
        }

        var program = OrderedProgram();

        var pending = program
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

        // Posted heats (12 Nov 2026, setup plan D2): drivers have looked theirs up.
        if (ConfirmPostedChange?.Invoke(FindForm(), $"Redo Remaining Heats deals the drivers into {Plural.Of(pending.Count, "heat")} again, so most of them move.") == false)
        {
            return;
        }

        // The old rosters, captured before anything moves, so one Undo puts
        // the whole re-seed back rather than one heat of it.
        var before = pending.ToDictionary(r => r.Id, r => r.AssignedTransponders.ToList(), StringComparer.Ordinal);
        var mapsBefore = pending.ToDictionary(
            r => r.Id,
            r => r.SharedClassOf is null ? null : new Dictionary<string, string>(r.SharedClassOf, StringComparer.OrdinalIgnoreCase),
            StringComparer.Ordinal);

        // The rosters the re-seed hands out, filled in below as each heat
        // is rebuilt, so the undo can tell whether a heat still holds what
        // this re-seed gave it.
        var after = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // The undo works on the rounds as they are on file WHEN it runs,
        // looked up by id - not on the instances captured here - and puts
        // a roster back only onto a heat that has not been run since and
        // still holds exactly the re-seed's roster. The undo is not cleared
        // when a heat is filed, so "redo, run Q2 Heat 1, Ctrl+Z" used to
        // hand Heat 1 its old roster after it had raced, and its roster no
        // longer matched the drivers in its result (the marshal list and
        // the published documents read those rosters). A heat edited by
        // hand since is left alone too, as Edit Format's undo does, so
        // undoing the re-seed does not quietly undo that edit as well.
        RoundDefinitionStore rounds = _rounds;

        Action? undoReseed = null;
        undoReseed = () =>
        {
            // Not while cars are on track, as Redo itself is refused then: the
            // heat being raced still holds the re-seed's roster, so it would
            // be handed its old one mid-race, and the Next Up bar, the tech
            // warnings and the seeding would read a grid that is not the one
            // racing. Put back on the stack first, so it can still be undone
            // once the race is over - as the venue's undo does.
            if (_race.State is not (RaceState.Idle or RaceState.Finished))
            {
                _undo.Capture("Redo the remaining heats", undoReseed!);
                throw new InvalidOperationException("a session is under way - finish or reset it first, then undo again.");
            }

            var restored = 0;
            var kept = new List<string>();

            foreach (var (id, given) in after)
            {
                if (!before.TryGetValue(id, out var old) || rounds.Get(id) is not { } current)
                {
                    continue;
                }

                if (current.IsRecorded || !current.AssignedTransponders.SequenceEqual(given, StringComparer.Ordinal))
                {
                    kept.Add(current.RoundLabel);
                    continue;
                }

                current.AssignedTransponders = old.ToList();
                current.SharedClassOf = mapsBefore[id];
                rounds.Update(current);
                restored++;
            }

            if (restored == 0 && kept.Count > 0)
            {
                throw new InvalidOperationException(
                    "every re-seeded heat has been run or edited since, so there is nothing left to put back. Nothing was changed.");
            }

            if (kept.Count > 0)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [day] Undo Redo Remaining Heats: put back {Plural.Of(restored, "heat")}; "
                                      + $"left {string.Join(", ", kept)} as they are - run or edited since.");
            }

            // The undo can run from anywhere in the app, for as long as the
            // undo stack keeps it - only touch the bar while it exists.
            if (!IsDisposed)
            {
                UpdateNextUpBar();
            }
        };

        _undo.Capture("Redo the remaining heats", undoReseed);

        var touched = 0;
        var skipped = new List<string>();
        var leftAlone = new List<string>();

        // Worked out for every class before a heat is changed - a shared heat
        // is dealt by each of its classes. See RedealAcrossClasses.
        CheckInStore checkIns = _checkIns;

        var redo = RedealAcrossClasses(
            program,
            className => _profiles.All
                .Where(p => p.RunsClass(className) && checkIns.IsCheckedIn(p.Transponder))
                .Select(p => p.Transponder)
                .ToList(),
            // WHY (23 Nov 2026, bug hunt 24): a ghost on a heat is never
            // checked in, so it was silently dropped from every heat redone.
            PresentForRedo(checkIns.IsCheckedIn),
            (round, transponder) => MixedHeats.ClassOfDriver(round, transponder, _profiles));

        skipped.AddRange(redo.NobodyIn);
        leftAlone.AddRange(redo.LeftAlone.Select(heat => $"{heat.ClassName} {heat.RoundLabel}"));

        foreach (var heat in pending)
        {
            if (!redo.Rosters.TryGetValue(heat.Id, out var given))
            {
                continue;
            }

            heat.AssignedTransponders = given;

            // A shared heat's drivers are written down in the class they were
            // dealt as, as Late Entry does - see RedealAcrossClasses.
            if (redo.ClassOf.TryGetValue(heat.Id, out var classOf))
            {
                heat.SharedClassOf = classOf;
            }

            _rounds.Update(heat);
            after[heat.Id] = given.ToList();
            touched++;
        }

        var note = $"{AppTime.LogStampNow()}  [day] Rebuilt {Plural.Of(touched, "heat")} from the live check-in sheet.";

        if (skipped.Count > 0)
        {
            note += $" Skipped {string.Join(", ", skipped)} - nobody checked in.";
        }

        if (leftAlone.Count > 0)
        {
            note += $" Left {string.Join(", ", leftAlone)} as before - everyone checked in has already raced that round.";
        }

        StatusMessage?.Invoke(note);
        UpdateNextUpBar();
    }

    /// <summary>What Redo Remaining Heats hands out: each rebuilt heat's roster by round id, the classes with nobody checked in, the heats left as they are because everyone has raced their round, and for each rebuilt shared heat the class each of its drivers runs in there.</summary>
    internal sealed record RedoRosters(
        Dictionary<string, List<string>> Rosters, List<string> NobodyIn, List<RoundDefinition> LeftAlone,
        Dictionary<string, Dictionary<string, string>> ClassOf);

    /// <summary>
    /// The rosters Redo Remaining Heats deals every not-yet-run qualifier on
    /// <paramref name="program"/>, class by class. Pure: who is checked in
    /// for a class, whether a driver is checked in, and which class a driver
    /// of a shared heat runs in (MixedHeats.ClassOfDriver) are asked for.
    ///
    /// WHY (bug hunt 17): this grouped the heats by their main class alone.
    /// A heat shared with another class (MixedHeats) was dealt as if every
    /// car in it were the main class's - so with Stock in two heats and Mod
    /// run together with one of them, the Mod drivers were spread over both
    /// and those landing in the Stock-only heat were scored as Stock; and a
    /// class sharing another class's heat while keeping one of its own had
    /// its drivers dealt into its own heat and left in the shared one too.
    /// Now each class's drivers are dealt among the heats that class runs
    /// in, shared ones included, and a shared heat's roster is its classes'
    /// shares one after another. A one-class heat comes out as it always did.
    /// </summary>
    internal static RedoRosters RedealAcrossClasses(
        IReadOnlyList<RoundDefinition> program,
        Func<string, IReadOnlyList<string>> checkedInFor,
        Func<string, bool> isCheckedIn,
        Func<RoundDefinition, string, string> classOfDriver)
    {
        // A one-class heat's class is read exactly as it always was.
        static IReadOnlyList<string> ClassesIn(RoundDefinition round) =>
            MixedHeats.IsShared(round) ? MixedHeats.ClassesOf(round) : new[] { round.ClassName };

        static bool Runs(RoundDefinition round, string className) =>
            MixedHeats.IsShared(round)
                ? MixedHeats.RunsClass(round, className)
                : string.Equals(round.ClassName, className, StringComparison.OrdinalIgnoreCase);

        bool InClass(RoundDefinition round, string transponder, string className) =>
            !MixedHeats.IsShared(round)
            || string.Equals(classOfDriver(round, transponder)?.Trim(), className.Trim(), StringComparison.OrdinalIgnoreCase);

        var pending = program
            .Where(r => r.Kind == RoundKind.Qualifier && !r.IsRecorded)
            .ToList();

        var dealtFor = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        var nobodyIn = new List<string>();

        foreach (var className in pending.SelectMany(ClassesIn).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            // Everyone checked in who races the class - as their main class
            // or one of their others - and then anyone checked in who is in
            // one of these heats already without racing the class: a driver
            // added to a heat by hand (RoundRosterForm's Add Driver, for a
            // class they aren't registered in). This used to rebuild from
            // the main class alone, and silently dropped every one of those
            // from every heat it redid. In a shared heat, only the drivers
            // running there as this class.
            var roster = checkedInFor(className).ToList();

            var addedByHand = pending
                .Where(r => Runs(r, className))
                .SelectMany(r => r.AssignedTransponders.Where(t => !string.IsNullOrWhiteSpace(t) && InClass(r, t, className)))
                .Where(t => !roster.Contains(t, StringComparer.OrdinalIgnoreCase) && isCheckedIn(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            roster.AddRange(addedByHand);

            // A ghost kept on its heat is not somebody checked in (bug hunt
            // 24): a class with only ghosts left is still nobody in, and its
            // heats are left as they are.
            if (roster.All(GhostService.IsGhost))
            {
                nobodyIn.Add(className);
                continue;
            }

            var classDayRounds = program.Where(r => Runs(r, className)).ToList();

            dealtFor[className] = RedealRemainingHeats(classDayRounds, roster);
        }

        var rosters = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var leftAlone = new List<RoundDefinition>();
        var classOfIn = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var heat in pending)
        {
            var given = new List<string>();

            // WHY (bug hunt 19): a two-class driver can be dealt into a shared
            // heat by the one of their classes that isn't first on their
            // profile. Nothing wrote down which class that was, so
            // ClassOfDriver fell back to the profile and scored a Mod run as
            // Stock. Each driver is now recorded as the class that dealt them.
            var classOf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dealtAny = false;
            var triedAny = false;

            foreach (var className in ClassesIn(heat))
            {
                if (dealtFor.TryGetValue(className, out var dealt))
                {
                    triedAny = true;

                    if (dealt.TryGetValue(heat.Id, out var theirs))
                    {
                        dealtAny = true;
                        given.AddRange(theirs);
                        Record(theirs, className);
                        continue;
                    }
                }

                // Nobody checked in for this class, or all of them have
                // raced the round: its drivers in the heat stay as they are.
                var staying = heat.AssignedTransponders.Where(t => InClass(heat, t, className)).ToList();
                given.AddRange(staying);
                Record(staying, className);
            }

            if (dealtAny)
            {
                rosters[heat.Id] = MixedHeats.IsShared(heat)
                    ? given.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                    : given;

                if (MixedHeats.IsShared(heat))
                {
                    classOfIn[heat.Id] = classOf;
                }
            }
            else if (triedAny)
            {
                leftAlone.Add(heat);
            }

            // A car runs once in a heat: one dealt in by both its classes
            // keeps the first, as the roster's Distinct does.
            void Record(IEnumerable<string> drivers, string className)
            {
                foreach (var t in drivers.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    classOf.TryAdd(t.Trim(), className);
                }
            }
        }

        return new RedoRosters(rosters, nobodyIn, leftAlone, classOfIn);
    }

    /// <summary>
    /// The rosters Redo Remaining Heats deals one class's not-yet-run
    /// qualifiers, by round id. <paramref name="classDayRounds"/> is the
    /// class's rounds on today's program, recorded ones included, and
    /// <paramref name="roster"/> who is checked in to race it. A heat left
    /// out of the answer keeps the roster it has. Pure, so the tests can
    /// hold it to what it promises without a race day.
    /// </summary>
    internal static Dictionary<string, List<string>> RedealRemainingHeats(
        IReadOnlyList<RoundDefinition> classDayRounds, IReadOnlyList<string> roster)
    {
        var dealt = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // Heats regrouped by the qualifying round they belong to, read the
        // way Move Drivers Between Heats reads it ("Q1 Heat 2" and "Race 1
        // Heat 2" are round 1, a one-round night's "Heat 2" the one round),
        // so the per-round rotation survives the re-seed. A heat that
        // belongs to no round - one added by hand beside numbered rounds -
        // groups by the text before "Heat", as every heat used to.
        var groups = classDayRounds
            .Where(r => r.Kind == RoundKind.Qualifier && !r.IsRecorded)
            .GroupBy(r =>
            {
                if (RedoRoundKey(r, classDayRounds) is { } round)
                {
                    return (Round: (string?)round, Label: string.Empty);
                }

                var cut = r.RoundLabel.IndexOf("Heat", StringComparison.OrdinalIgnoreCase);
                return (Round: (string?)null, Label: cut < 0 ? r.RoundLabel : r.RoundLabel[..cut].Trim());
            })
            .ToList();

        var groupIndex = 0;

        foreach (var group in groups)
        {
            var heats = group.ToList();
            var pool = roster;
            var round = groupIndex++;

            if (group.Key.Round is { } key)
            {
                // Not anyone who has already raced this round. This used to
                // deal the whole class into the round's unrun heats, so a
                // Redo after Q2 Heat 1 had run put its eight drivers into
                // Q2 Heat 2 as well - a second Q2 heat that counted.
                var raced = classDayRounds
                    .Where(r => r.Kind == RoundKind.Qualifier && r.IsRecorded
                                && RedoRoundKey(r, classDayRounds) == key)
                    .SelectMany(r => r.AssignedTransponders)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                pool = roster.Where(t => !raced.Contains(t)).ToList();

                // Dealt by its own round number, as the build deals it. It
                // used to be a plain deal by its place among the rounds left,
                // which gave an evenly split class Q1's groups again in Q2
                // and Q3.
                round = Math.Max(0, (RaceProgramControl.QualifyingRoundNumber(heats[0], classDayRounds) ?? 1) - 1);
            }

            // Everyone checked in has raced this round already: its heats
            // keep the rosters they have.
            if (pool.Count == 0)
            {
                continue;
            }

            var buckets = RaceProgramControl.DealQualifyingHeatsInto(pool, heats.Count, round);

            for (var i = 0; i < heats.Count; i++)
            {
                dealt[heats[i].Id] = buckets[i];
            }
        }

        return dealt;
    }

    /// <summary>
    /// The qualifying round Redo Remaining Heats counts a heat towards:
    /// HeatBalanceForm.RoundKeyFor, except that an unnumbered qualifier
    /// ("Heat 2") is round 1 while the class has no numbered first round -
    /// the rule the build (RaceProgramControl.BuildRoundOf) and the points
    /// (QualifyingCalculator) have used since bug hunt 17.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): a one-round race day's "Heat 1" and
    /// "Heat 2", with Q2 built beside them after the round count was
    /// raised, read here as heats added by hand that belong to no round.
    /// So the "already raced" check was skipped and Redo dealt the whole
    /// class, Heat 1's drivers included, into Heat 2 - a second round-1
    /// heat that counted. A heat added by hand beside a numbered Q1 still
    /// belongs to no round.
    /// </summary>
    private static string? RedoRoundKey(RoundDefinition round, IReadOnlyList<RoundDefinition> classDayRounds)
    {
        if (HeatBalanceForm.RoundKeyFor(round, classDayRounds) is { } key)
        {
            return key;
        }

        if (HeatBalanceForm.QualifyingRoundOf(round) is not { Length: 0 })
        {
            return null;
        }

        var hasNumberedFirstRound = classDayRounds.Any(r => HeatBalanceForm.QualifyingRoundOf(r) == "Q1");

        return hasNumberedFirstRound ? null : "Q1";
    }

    /// <summary>
    /// Opens the editor on the format that is actually in force, at its
    /// source: the round's override if that is what is running, else (for a
    /// qualifier) the class's qualifier format, else the class's format,
    /// else the app default - the same order EffectiveFormat resolves them
    /// in. Editing what you can see
    /// beats a menu asking which of three formats you meant.
    /// </summary>
    private void EditFormat()
    {
        // WHY (20 Oct 2026): this read the Round box only, while
        // EffectiveFormat reads ShowRound (the Round box, else DemoRound). A
        // demo of a round already run, Round box on freeform, showed that
        // round's override or class format on the chip, but Edit Format
        // opened the App Default - and saving changed the club-wide default.
        // It now picks its round the same way the chip does.
        var round = ShowRound();

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

        // A qualifier under a class with its own qualifier format runs under
        // that, exactly as EffectiveFormat resolves it - so that is the one
        // to open. Falling through to the class branch below would open the
        // MAIN format instead: the heat on screen would not change, and every
        // main later that night would quietly run under the edited numbers.
        // (And a class with a qualifier format but no main format would land
        // on the app default, an edit that does nothing to this heat either.)
        // "Use default" here means what it means in the class editor: drop
        // back to the class's race format, else the app default.
        if (round?.Kind == RoundKind.Qualifier && ClassFor(round) is { QualifierFormat: not null } qualClass)
        {
            using var qualForm = new RaceFormatForm(
                qualClass.QualifierFormat, $"Qualifier Format - {qualClass.Name}", allowUseDefault: true,
                fallback: qualClass.Format ?? RaceFormat.FromSettings(_settings));

            if (qualForm.ShowDialog(FindForm()) == DialogResult.OK)
            {
                // As for the class format below: only the qualifier format
                // goes back, onto the class as it is on file then.
                var classes = _raceClasses;
                var classId = qualClass.Id;
                var oldQualFormat = qualClass.QualifierFormat?.Clone();
                var newQualFormat = qualForm.Result;

                _undo.Capture($"Edit qualifier format for {qualClass.Name}", () =>
                {
                    if (classes.Get(classId) is not { } current)
                    {
                        throw new InvalidOperationException("that class has been deleted since. Nothing was changed.");
                    }

                    if (!ReferenceEquals(current.QualifierFormat, newQualFormat))
                    {
                        throw new InvalidOperationException(
                            $"{current.Name}'s qualifier format has been changed again since, and undoing this now would undo that too. Nothing was changed.");
                    }

                    current.QualifierFormat = oldQualFormat;
                    classes.Upsert(current);
                    ReapplyFormatAfterUndo();
                });

                qualClass.QualifierFormat = newQualFormat;
                _raceClasses.Upsert(qualClass);
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
    /// The one answer the filing reads too (RaceFilingService.AutoAdvanceWantedFor,
    /// 23 Oct 2026, timing layer, P2).
    /// </summary>
    private bool AutoAdvanceWanted => RaceFilingService.AutoAdvanceWantedFor(_events, _settings);

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
                ordered.SelectMany(s => _profiles.Get(s.TransponderId)?.AllClasses ?? (IReadOnlyList<string>)Array.Empty<string>()),
                message => StatusMessage?.Invoke(message),
                ordered.Select(s => _profiles.Get(s.TransponderId)?.Transponder ?? s.TransponderId));


    /// <summary>
    /// Names the saved session. Uses the round's label when the race is tied
    /// to one, so the session pickers elsewhere show "Heat 1" instead of a
    /// generic "Race" - without that there's no way to match a qualifying
    /// figure back to the heat that produced it.
    /// </summary>
    private string BuildSessionLabel(RoundDefinition? round)
    {
        var typed = TypedLabelFor(round);

        var baseLabel = !string.IsNullOrWhiteSpace(typed)
            ? typed
            : !string.IsNullOrWhiteSpace(round?.RoundLabel)
                ? round!.RoundLabel
                : "Race";

        return _currentRaceIsDemo ? $"{SessionRecord.DemoLabelPrefix} {baseLabel}" : baseLabel;
    }

    /// <summary>
    /// The race name typed in (or set) for this race, with leftovers from an
    /// earlier race dropped when the race is tied to a round. Empty when
    /// there is nothing worth keeping, so the caller falls back to the
    /// round's own name.
    ///
    /// The hidden name box here is only a copy of MainForm's Name box, and
    /// otherwise only MainForm's Name box changes it - apart from Quick Race
    /// writing its stamp and a crash-journal restore. Reset, and filing a
    /// Quick Race, clear only the Quick Race stamp (23 Oct 2026, bug hunt
    /// 10); a name the director typed stays. Two things leave a stale name
    /// in it (2 Oct 2026):
    ///
    /// - Once a heat is recorded it drops out of the Round box, but the
    ///   "Class - Heat 1" name MainForm derived for it stays in the box. The
    ///   next race, auto-advanced onto Heat 2, was then filed, read out and
    ///   published as Heat 1 a second time.
    /// - Quick Race writes its "Quick Race 19:05" stamp only into this copy.
    ///   Starting Heat 2 afterwards writes the same "Club - Heat 2" text
    ///   MainForm's box already held, so no TextChanged reaches here, and
    ///   Heat 2 was filed as the Quick Race - with nothing on screen to say
    ///   so, since the Name box is hidden while a round is chosen.
    ///
    /// So when the race is tied to a round, a name that is MainForm's
    /// derived name for a different round, or a Quick Race stamp, is not
    /// this race's name and is ignored. The fallback is that round's own
    /// derived name, the same "Class - Round" a race picked from the Round
    /// box is filed under. A Quick Race itself is never tied to a round
    /// unless the director picks one for it, in which case the round's name
    /// is the right one anyway. Anything else the director typed is kept,
    /// as before, and a race with no round is left exactly as it was.
    /// UI thread.
    /// </summary>
    private string TypedLabelFor(RoundDefinition? round) =>
        TypedLabelFor(_txtSessionLabel.Text, round, _rounds?.All);

    /// <summary>
    /// TypedLabelFor's rule on its own: <paramref name="typedText"/> is the
    /// name box's text, <paramref name="allRounds"/> every round there is
    /// (null with no round store). Pure, so the tests can hold it to the
    /// stale-name rule without a form (bug hunt 24).
    /// </summary>
    internal static string TypedLabelFor(string? typedText, RoundDefinition? round, IEnumerable<RoundDefinition>? allRounds)
    {
        var typed = (typedText ?? string.Empty).Trim();

        if (round is null || typed.Length == 0)
        {
            return typed;
        }

        var own = DerivedRoundName(round);

        if (string.Equals(typed, own, StringComparison.OrdinalIgnoreCase))
        {
            return typed;
        }

        var staleRoundName = allRounds is not null
            && allRounds.Any(r =>
                !string.Equals(r.Id, round.Id, StringComparison.Ordinal)
                && string.Equals(typed, DerivedRoundName(r), StringComparison.OrdinalIgnoreCase));

        return staleRoundName || IsQuickRaceStamp(typed) ? own : typed;
    }

    /// <summary>
    /// The name MainForm puts in the Name box for a round picked in the Round
    /// box (ApplyRoundToSessionSetup). Kept in step with that by hand.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): this still built the one-class name
    /// ("Stock - Q1 Heat 1") after MainForm started writing both classes of
    /// a shared heat ("Stock + Mod - Q1 Heat 1", Phase 4.1). The two never
    /// matched for a shared heat, so its leftover name was never seen as
    /// stale and the next heat was filed, read out and published under it
    /// a second time. It now uses MixedHeats.ClassLabel, as MainForm does.
    /// </summary>
    internal static string DerivedRoundName(RoundDefinition round) =>
        $"{MixedHeats.ClassLabel(round)} - {round.RoundLabel}".Trim();

    /// <summary>The ways AppTime.TimeOfDay writes the Quick Race stamp's time.</summary>
    private static readonly string[] QuickRaceStampTimes = { "HH:mm", "h:mm tt", "h:mm" }; // read with TryParseExact, never written

    /// <summary>
    /// Whether a name is the "Quick Race 19:05" or "Quick Race 7:05 PM" stamp
    /// Quick Race writes. The time is read in the current culture, as it was
    /// written, since the ":" in a format string is the culture's time
    /// separator.
    ///
    /// Either clock, whichever is ticked now (bug hunt 17): the stamp is
    /// written with AppTime.TimeOfDay, and this read "HH:mm" only - so on the
    /// default 12-hour clock no stamp was ever recognised, and it stuck to the
    /// races after it again. "h:mm" is for a culture with no AM/PM words.
    /// </summary>
    private static bool IsQuickRaceStamp(string label)
    {
        const string prefix = "Quick Race ";

        return label.StartsWith(prefix, StringComparison.Ordinal)
            && DateTime.TryParseExact(label[prefix.Length..].Trim(), QuickRaceStampTimes,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Globalization.DateTimeStyles.None, out _);
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

        var (roster, source) = PreRaceRoster();

        // Mission Control is offered from here: this is where somebody is
        // already looking before the green. Closing the check first, as the
        // window it opens is not modal and would otherwise sit behind it.
        var wantsMissionControl = false;

        using (var form = new ReadinessCheckForm(roster, _transponderWatch, _profiles, source, splitLoop: _settings.SplitLoopEnabled,
                   openMissionControl: MissionControlRequested is null ? null : (Action)(() => wantsMissionControl = true)))
        {
            form.ShowDialog(this);
        }

        if (wantsMissionControl)
        {
            MissionControlRequested?.Invoke();
        }
    }

    /// <summary>Raised when somebody asks for Mission Control from Race Control - MainForm opens it.</summary>
    public event Action? MissionControlRequested;

    /// <summary>
    /// Who should be in the next race, and where that list came from: the
    /// selected round's roster if there is one, because that is the list
    /// somebody has already agreed to; failing that, whoever is already on
    /// the board. Shared by the pre-race check, Mission Control and the Big
    /// Screen's roll call, so all three check the same cars.
    /// </summary>
    internal (List<string> Roster, string Source) PreRaceRoster()
    {
        var round = _activeRound?.Invoke();

        if (round is { AssignedTransponders.Count: > 0 })
        {
            return (round.AssignedTransponders.ToList(),
                string.IsNullOrWhiteSpace(round.RoundLabel) ? "round roster" : $"roster for {round.RoundLabel}");
        }

        // Not the ghosts: they are never heard (their passes are the app's
        // own), so they were always "not heard" on Mission Control and the
        // roll call.
        return (_race.GetOrderedStandings()
            .Select(s => s.TransponderId)
            .Where(id => !GhostService.IsGhost(id))
            .ToList(), "drivers already on the board");
    }
}
