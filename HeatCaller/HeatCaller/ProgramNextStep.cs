namespace HeatCaller;

/// <summary>Where the race day has got to. Drives Today's next-step card and Race Control's Next Up bar.</summary>
internal enum ProgramStage
{
    /// <summary>No race day is open, so there is nothing to file a programme against.</summary>
    NoEvent,

    /// <summary>
    /// The day being looked at has been closed.
    ///
    /// Its own state rather than folded into NoEvent, because there IS a
    /// day and the page is full of it - its classes, its drivers, its
    /// rounds. Saying "no race day is open" over the top of all that reads
    /// as a bug. What is true is narrower: nothing new is added to this
    /// one, so tonight needs a day of its own.
    /// </summary>
    DayClosed,

    /// <summary>There are no classes anywhere in the app yet, so there is nothing to pick from.</summary>
    NoClassesOnFile,

    /// <summary>Classes exist, but none of them is running today.</summary>
    NoClasses,

    /// <summary>Classes are named but not one of them has a driver in it.</summary>
    NoDrivers,

    /// <summary>
    /// The drivers are there and in a class, but the day is set to
    /// checked-in drivers only and nobody has been checked in.
    ///
    /// Its own stage because it is the one empty state whose fix is on
    /// neither of the pages the other empty states send you to.
    /// </summary>
    NobodyCheckedIn,

    /// <summary>Everything is ready and nothing is scheduled yet.</summary>
    BuildQualifying,

    /// <summary>A lower main has finished and its top finishers are owed a place in the tier above. Never reached on a club night.</summary>
    BumpUps,

    /// <summary>There is a round waiting to be run.</summary>
    RunNext,

    /// <summary>Every scheduled round has been run, and a class still has no mains. Never reached on a club night.</summary>
    SeedMains,

    /// <summary>Nothing left to do.</summary>
    Complete,
}

/// <summary>
/// One sentence about what to do next, and the button that does it.
/// </summary>
/// <param name="Stage">Which state the day is in - what the caller switches on to know which command to run.</param>
/// <param name="Headline">The short line, read first. A statement, not an instruction.</param>
/// <param name="Detail">The quieter line under it: the numbers behind the headline.</param>
/// <param name="ActionCaption">What to put on the button. Never empty.</param>
/// <param name="Round">The round the headline is about, when there is one.</param>
/// <param name="ClassName">The class the action applies to, when it is class-scoped.</param>
internal sealed record ProgramStep(
    ProgramStage Stage,
    string Headline,
    string Detail,
    string ActionCaption,
    RoundDefinition? Round = null,
    string ClassName = "");

/// <summary>
/// The wording on the next-step buttons, in one place.
///
/// These captions used to name the operation - "Build the Qualifying
/// Programme", "Seed the Mains", "Apply Bump-Ups" - which is exactly right if
/// you have run a race day before and no use at all if you have not. They now
/// say what pressing them does. The nouns of the sport are untouched: the
/// grid still says A-Main and Q2 Heat 3, the announcer still says bump-up,
/// and RacingWordsForm explains them. Only the verbs moved.
///
/// They are constants here rather than literals at the point of use because
/// the Next Up bar in Race Control has to be SIZED for every caption its
/// button can wear. With the strings written out twice, renaming one of them
/// left the other behind and the button silently clipped its own text.
/// </summary>
internal static class ProgramCaptions
{
    /// <summary>Was "Build the Qualifying Programme".</summary>
    public const string BuildQualifying = "Build tonight's heats";

    /// <summary>
    /// The same command on a club night. A club night has no finals for a
    /// heat to qualify anybody for, so nothing on it is a heat: they are
    /// just the night's races, and the button says so.
    /// </summary>
    public const string BuildRaces = "Build tonight's races";

    /// <summary>Was "Apply Bump-Ups...". Keeps the ellipsis: it asks first.</summary>
    public const string BumpUps = "Move the top finishers up...";

    /// <summary>Was "Seed the Mains".</summary>
    public const string SeedMains = "Build the finals from qualifying";

    /// <summary>Not renamed - "Start A-Main" is the clearest thing it could say.</summary>
    public const string StartNext = "Start Next";

    /// <summary>What the Next Up bar's button says while a session is under way.</summary>
    public const string RaceRunning = "Race Running";

    /// <summary>The finished program's button when the club closes the night with the checklist (11 Nov 2026). Never on the Next Up bar.</summary>
    public const string CloseNight = "Close the night...";

    /// <summary>
    /// Every caption the Next Up bar's button can wear, for measuring it.
    ///
    /// Only the two owed-step stages reach that bar (see
    /// RaceControlControl.UpdateNextUpBar), plus its own two states. A start
    /// caption carries a round label, which is measured separately.
    /// </summary>
    public static readonly string[] NextUpBarWidest =
    {
        RaceRunning,
        StartNext,
        BumpUps,
        SeedMains,
    };
}

/// <summary>
/// Works out the single next thing a race director should do.
///
/// Split out from the control on purpose. This is the one piece of the
/// Program tab that has to be RIGHT rather than merely tidy - it is what
/// the whole screen now leads with - and having it as a pure function of
/// (event, rounds, rosters, results) means it can be reasoned about
/// without a window, a grid or a click anywhere near it.
///
/// The order of the checks below is the design. Each one is a claim about
/// what matters more than what, and they are commented as such, because
/// the difference between a useful banner and an annoying one is entirely
/// in that ordering.
/// </summary>
internal static class ProgramNextStep
{
    /// <summary>A class that is running today, and how many drivers it has after the check-in filter.</summary>
    internal readonly record struct RacingClass(string Name, int RosterCount);

    /// <summary>
    /// What exists in the app at all, as opposed to what is racing today.
    ///
    /// Needed because an empty list of racing classes has four different
    /// causes and they are fixed on four different pages. Without these the
    /// banner could only ever name the nearest one, and it named the wrong
    /// one in the commonest first-run state of all: classes created,
    /// nobody put in them.
    /// </summary>
    /// <param name="ClassesOnFile">Classes that exist, whether or not they are racing.</param>
    /// <param name="DriversOnFile">Drivers that exist and could race (ghosts excluded).</param>
    /// <param name="DriversInAClass">How many of those have been given a class that exists.</param>
    /// <param name="CheckedInOnly">Whether the day only counts drivers who have checked in.</param>
    /// <param name="CheckedInInAClass">How many drivers in a class have checked in.</param>
    internal readonly record struct SetupFacts(
        int ClassesOnFile,
        int DriversOnFile,
        int DriversInAClass,
        bool CheckedInOnly,
        int CheckedInInAClass);

    public static ProgramStep Evaluate(
        RaceEvent? raceEvent,
        IReadOnlyList<RoundDefinition> dayRounds,
        IReadOnlyList<RacingClass> classes,
        int plannedQualifyingRounds,
        SessionHistoryStore history,

        // Not optional. A caller that left this out would get an all-zero
        // reading and be told there are no classes on file whatever the
        // truth - the exact wrong diagnosis this was added to stop.
        SetupFacts facts)
    {
        // 1. Nothing can be filed without a race day. The legacy holder is
        //    not one - it is the cabinet everything pre-event lives in.
        if (raceEvent is null || raceEvent.IsLegacy)
        {
            return new ProgramStep(
                ProgramStage.NoEvent,
                "No race day is open.",
                "Check-ins, the heats and the timetable are all filed against a race day. Start one and this page builds itself from it.",
                "Start a Race Day");
        }

        // A club night has no finals, so three of the eight states below
        // cannot happen on one: there is nothing to seed, nothing to bump up
        // into, and "qualifying is complete" is not a thing that occurs. It
        // also decides what a round is CALLED - a heat is a heat because it
        // qualifies you for something - so it is read here, above the first
        // state that has to say the word. See RaceDayShape.
        var clubNight = raceEvent.IsClubNight;

        // 1b. There is a day, and it is finished with. Everything below
        //     assumes a day that can still be added to - the build, the
        //     seeding, the bump-ups all create rounds, and
        //     RaceProgramControl refuses every one of them on a closed day -
        //     so offering any of them here would be an instruction that
        //     cannot be followed.
        if (!raceEvent.IsOpen)
        {
            return new ProgramStep(
                ProgramStage.DayClosed,
                $"\"{raceEvent.DisplayName}\" is closed.",
                "Nothing new is added to a closed day - its results and standings keep reading exactly as they do now. "
                + "Start a day for tonight, or reopen this one on the Setup tab.",
                "Start a Race Day");
        }

        // 2. Nothing is racing today. Which of the four reasons that can be
        //    decides where the button sends them, so it is worked out
        //    rather than guessed at - see NothingToRace.
        if (classes.Count == 0)
        {
            return NothingToRace(facts, clubNight);
        }

        // 3. Only when EVERY class is empty. One empty class among four is
        //    a note for the detail line, not a roadblock - a class can
        //    legitimately have nobody in it until sign-on closes.
        if (classes.All(c => c.RosterCount == 0))
        {
            // The check-in filter first: when it is on and nobody is
            // through the door, "no drivers" is not what is wrong, and
            // sending the director to the driver list would have them
            // looking at a full one.
            if (facts.CheckedInOnly && facts.CheckedInInAClass == 0 && facts.DriversInAClass > 0)
            {
                return NobodyThroughTheDoor(facts, clubNight);
            }

            return new ProgramStep(
                ProgramStage.NoDrivers,
                classes.Count == 1
                    ? $"No drivers in {classes[0].Name}."
                    : "None of today's classes has any drivers.",
                "A driver joins a class on their own profile. Give each driver the class they race in and they will appear here.",
                "Add Drivers...");
        }

        var empties = classes.Where(c => c.RosterCount == 0).Select(c => c.Name).ToList();

        var emptyNote = empties.Count == 0
            ? string.Empty
            : $" {Join(empties)} {(empties.Count == 1 ? "has" : "have")} no drivers and will be skipped.";

        // 4. Nothing scheduled at all. Deliberately checked against the
        //    whole day rather than per class: a programme half-built for
        //    one class is a programme, and belongs in the running states
        //    below where the director can see what is missing.
        if (dayRounds.Count == 0)
        {
            var racing = classes.Where(c => c.RosterCount > 0).ToList();
            var drivers = racing.Sum(c => c.RosterCount);

            return new ProgramStep(
                ProgramStage.BuildQualifying,
                "Nothing is scheduled yet.",
                (clubNight
                    ? $"{Plural.Of(plannedQualifyingRounds, "race")} each for {Plural.Of(racing.Count, "class", "classes")}, "
                    : $"{Plural.Of(plannedQualifyingRounds, "qualifying round")} for {Plural.Of(racing.Count, "class", "classes")}, ")
                + $"{Plural.Of(drivers, "driver")} in all.{emptyNote}",
                clubNight ? ProgramCaptions.BuildRaces : ProgramCaptions.BuildQualifying);
        }

        // 5. Bump-ups OUTRANK running the next round, and that is the whole
        //    reason this check sits here rather than further down.
        //
        //    A bump-up changes who is in the main above, so doing it after
        //    that main has gone out is not a late correction - it is too
        //    late, full stop. The one thing this banner must never do is
        //    invite the director to start a race that is about to have its
        //    grid changed.
        //
        //    Skipped outright on a club night. Not because the list would be
        //    empty - a day switched from a race day to a club night can still
        //    have the mains it built - but because holding the banner on
        //    "move the top finishers up" is exactly the instruction somebody
        //    who has just said this is a club night does not want.
        var mainsByClass = clubNight
            ? Enumerable.Empty<IGrouping<string, RoundDefinition>>()
            : dayRounds
                .Where(r => r.Kind == RoundKind.Main && r.MainTier >= 0)
                .GroupBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase);

        foreach (var group in mainsByClass)
        {
            var mains = group.OrderBy(r => r.MainTier).ToList();

            var ready = mains.FirstOrDefault(m =>
                MainsBuilder.DescribeBumpUpBlocker(m, mains, history) is null);

            if (ready is null)
            {
                continue;
            }

            // 17 Oct 2026, Bug Hunt 8, A24-1: the same feeder the bump-up uses -
            // with no race day open, "first at the tier below" could be last
            // week's B-Main.
            var feeder = MainsBuilder.FeederFor(ready, mains);

            return new ProgramStep(
                ProgramStage.BumpUps,
                feeder is null
                    ? $"{ready.RoundLabel} is ready for bump-ups."
                    : $"{feeder.RoundLabel} has finished.",
                // The button also offers to skip them. It has to: this step
                // outranks running the next round, so a club not doing
                // bump-ups today - or a main with nobody left to promote -
                // would otherwise hold the banner here for the rest of the
                // day.
                $"The top {ready.BumpUpCount} move up into {ready.RoundLabel}, which grows to take them - "
                + $"so do this before {ready.RoundLabel} goes out. Not doing bump-ups today? The button lets you skip them.",
                // The detail above still calls them bump-ups, because that is
                // what everybody at the track calls them; the button says
                // what pressing it does.
                ProgramCaptions.BumpUps,
                ready,
                group.Key);
        }

        // 6. The ordinary state, and the one the director is in for most of
        //    the day: there is a round waiting, so name it and offer to
        //    start it.
        var next = dayRounds.FirstOrDefault(r => !r.IsRecorded);

        if (next is not null)
        {
            var run = dayRounds.Count(r => r.IsRecorded);
            var left = dayRounds.Count - run;

            var track = string.IsNullOrWhiteSpace(next.TrackLayoutName)
                ? string.Empty
                : $" · {next.TrackLayoutName}";

            return new ProgramStep(
                ProgramStage.RunNext,
                $"Next up: {next.RoundLabel}",
                // "Stock + Mod" for a shared heat (11 Nov 2026, Phase 4.1).
                $"{MixedHeats.ClassLabel(next)} · {Plural.Of(next.AssignedTransponders.Count, "driver")}{track}"
                + $"    —    {run} of {dayRounds.Count} run, {left} to go.",
                $"Start {next.RoundLabel}",
                next,
                next.ClassName);
        }

        // 7. Everything scheduled has been run. If a class qualified but
        //    never got its mains, that is what is missing - and it can only
        //    be reached here, once there is nothing left to run, or it
        //    would jump the queue ahead of another class's heats.
        //    Not on a club night: there are no finals to seed, so a night
        //    whose races have all been run is finished, not half-done.
        foreach (var raceClass in clubNight ? Enumerable.Empty<RacingClass>() : classes)
        {
            var rounds = dayRounds
                // A shared heat is one of this class's qualifiers too
                // (11 Nov 2026, Phase 4.1), so Mod still gets its mains.
                .Where(r => MixedHeats.RunsClass(r, raceClass.Name))
                .ToList();

            var qualifiers = rounds.Count(r => r.Kind == RoundKind.Qualifier && r.IsRecorded);
            var mains = rounds.Count(r => r.Kind == RoundKind.Main);

            if (qualifiers == 0 || mains > 0)
            {
                continue;
            }

            return new ProgramStep(
                ProgramStage.SeedMains,
                $"Qualifying is complete for {raceClass.Name}.",
                $"{Plural.Of(qualifiers, "qualifier")} recorded. The finals are filled straight from the qualifying order - "
                + "fastest into the A-Main - and the lower ones run first so the top finishers can move up.",
                ProgramCaptions.SeedMains,
                ClassName: raceClass.Name);
        }

        // 8. Nothing left. Said plainly rather than left blank, because a
        //    banner that goes quiet reads as broken. With it, how the night
        //    ran (11 Nov 2026, night pace) - the moment a director has time
        //    to read it, and the number that says whether tonight was quick.
        return new ProgramStep(
            ProgramStage.Complete,
            "The program is complete.",
            (dayRounds.Count == 1
                ? "The one round on today's program has been run and recorded."
                : $"All {dayRounds.Count} rounds on today's program have been run and recorded.")
            + PaceNote(dayRounds, history),

            // There is no TQ on a club night, so the same list is just
            // tonight's order. Same form either way, different word for it.
            clubNight ? "Tonight's Standings..." : "Qualifying Standings...",
            ClassName: classes.Count > 0 ? classes[0].Name : string.Empty);
    }

    /// <summary>
    /// " Night pace: green to green 6:10 · ..." for the finished day's
    /// races, or empty with fewer than two of them. See NightPace.
    /// </summary>
    private static string PaceNote(IReadOnlyList<RoundDefinition> dayRounds, SessionHistoryStore history)
    {
        var roundIds = dayRounds.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var linked = dayRounds
            .Where(r => !string.IsNullOrEmpty(r.LinkedSessionId))
            .Select(r => r.LinkedSessionId!)
            .ToHashSet(StringComparer.Ordinal);

        var sessions = history.All
            .Where(s => (s.RoundId is not null && roundIds.Contains(s.RoundId)) || linked.Contains(s.Id));

        return NightPace.For(sessions, dayRounds, roundIds) is { } pace
            ? $" Night pace: {NightPace.Line(pace)}."
            : string.Empty;
    }

    /// <summary>
    /// Nothing is racing today, and why.
    ///
    /// The list of racing classes comes up empty only when no class was
    /// picked for today AND no class has anybody in it - the fallbacks in
    /// RacingClassNames see to that. So reaching here says nothing about
    /// WHICH of those is the problem, and the four cases are fixed in four
    /// different places. They are tested in the order a club would hit
    /// them setting up for the first time.
    /// </summary>
    private static ProgramStep NothingToRace(SetupFacts facts, bool clubNight)
    {
        if (facts.ClassesOnFile == 0)
        {
            return new ProgramStep(
                ProgramStage.NoClassesOnFile,
                "There are no classes yet.",
                $"A class is the rulebook a group of drivers races under, and every {(clubNight ? "race" : "heat")} belongs to one. "
                + "If everyone races together, a single class called \"Open\" is all you need.",
                "Set Up Classes...");
        }

        if (facts.DriversOnFile == 0)
        {
            return new ProgramStep(
                ProgramStage.NoDrivers,
                "There are no drivers yet.",
                "Add the people who are racing and give each one a class. A transponder the app doesn't know is "
                + "offered for naming the first time it crosses the line, so nobody has to be typed in twice.",
                "Add Drivers...");
        }

        if (facts.DriversInAClass == 0)
        {
            return new ProgramStep(
                ProgramStage.NoDrivers,
                "No driver is in a class yet.",
                $"{Plural.Of(facts.DriversOnFile, "driver")} on file, and not one of them has a class - so there is "
                + $"nobody a {(clubNight ? "race" : "heat")} could be built from. A driver's class is set on their own profile.",
                "Add Drivers...");
        }

        if (facts.CheckedInOnly && facts.CheckedInInAClass == 0)
        {
            return NobodyThroughTheDoor(facts, clubNight);
        }

        // Classes and drivers both exist, so the only way to be here is a
        // day whose picked classes name something that no longer does.
        return new ProgramStep(
            ProgramStage.NoClasses,
            "No classes are running today.",
            $"Tick the classes that are racing and all of tonight's {(clubNight ? "races" : "heats")} can be built in one press. "
            + "If you picked some already, they may have been renamed since.",
            "Choose Today's Classes");
    }

    /// <summary>
    /// Everything is set up, but the day counts only checked-in drivers and
    /// nobody has been checked in.
    ///
    /// Worth its own sentence because the setting that causes it is on a
    /// different tab from the list it empties, and the symptom - every
    /// class showing nought drivers - looks exactly like having no drivers
    /// at all.
    /// </summary>
    private static ProgramStep NobodyThroughTheDoor(SetupFacts facts, bool clubNight) =>
        new(ProgramStage.NobodyCheckedIn,
            "Nobody has checked in yet.",
            $"This {(clubNight ? "club night" : "race day")} is set to build {(clubNight ? "races" : "heats")} from checked-in drivers only, and none of the "
            + $"{Plural.Of(facts.DriversInAClass, "driver")} in a class has been checked in. Check them in as they "
            + "arrive - or turn that setting off for today.",
            "Check Drivers In...");

    /// <summary>"A", "A and B", "A, B and C" - the way a person would say it.</summary>
    private static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        2 => $"{names[0]} and {names[1]}",
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };

}
