using System.Globalization;

namespace HeatCaller;

/// <summary>How much quicker (or slower) a class is running now than in its first race tonight.</summary>
/// <param name="FirstRace">The class's first race tonight, which the change is counted from.</param>
/// <param name="DeltaMs">How much each driver's middle lap has changed between their first race tonight and their latest -
/// the middle of those changes. Negative when the track has got quicker.</param>
/// <param name="Drivers">How many drivers that was worked out from.</param>
public sealed record TrackEvolution(string ClassName, string FirstRace, string LatestRace, long DeltaMs, int Drivers, int Races)
{
    /// <summary>"Track 0.31s quicker since Q1 Heat 1", "Track 0.12s slower since Q1 Heat 1", or "Track the same as in Q1 Heat 1".</summary>
    public string Words
    {
        get
        {
            var by = Math.Abs(DeltaMs) / 1000.0;

            if (by < 0.005)
            {
                return $"Track the same as in {FirstRace}";
            }

            var amount = by.ToString("0.00", CultureInfo.InvariantCulture);
            return DeltaMs < 0
                ? $"Track {amount}s quicker since {FirstRace}"
                : $"Track {amount}s slower since {FirstRace}";
        }
    }
}

/// <summary>The fastest lap in one class tonight, and who set it.</summary>
public sealed record FastestTonight(string ClassName, string DriverName, long LapMs, string Race);

/// <summary>
/// How one driver's pace has moved tonight: their middle flying lap in
/// their FIRST race of the night against their LATEST.
///
/// WHY THIS IS A RECORD OF ITS OWN AND NOT A SECOND SUM
///
/// The class-wide evolution figure is the MIDDLE of exactly these
/// changes - <see cref="TonightAtTheTrack.Evolution"/> reads them from
/// <see cref="TonightAtTheTrack.Changes"/> and sorts them - so a line
/// about one driver and the figure on the Next Up bar can never disagree
/// about the same laps. Two implementations of "how much quicker is he"
/// would drift apart the first time a result was corrected mid-evening,
/// and nothing would ever say so.
/// </summary>
/// <param name="Transponder">The driver.</param>
/// <param name="FirstMs">Their middle lap in their first race tonight.</param>
/// <param name="LatestMs">And in their latest.</param>
/// <param name="Races">How many of tonight's races they had enough flying laps in to be measured at all.</param>
/// <param name="FirstRace">The race the change is counted FROM, as the club labelled it - the scope any line about this has to say.</param>
/// <param name="LatestRace">And the one it is counted to.</param>
public sealed record DriverPaceTonight(
    string Transponder,
    long FirstMs,
    long LatestMs,
    int Races,
    string FirstRace,
    string LatestRace)
{
    /// <summary>Their middle lap now against then. Negative when they are quicker now - the same sign TrackEvolution.DeltaMs uses.</summary>
    public long DeltaMs => LatestMs - FirstMs;
}

/// <summary>
/// How one driver's RACE pace tonight compares with their PRACTICE pace
/// tonight: their middle flying lap in their latest practice run against
/// the same in their latest race.
///
/// WHY THIS IS NOT A DriverPaceTonight WITH A DIFFERENT FIRST RACE
///
/// Because practice is not a race, and the whole reason this took so long
/// to build is that saying otherwise would have broken things a long way
/// from here. A practice session has no finishing order, no result and no
/// round - so no class - which is exactly why
/// <see cref="TonightAtTheTrack.Races"/> filters it out and why every
/// reader in the commentary layer does the same. A pace figure that
/// quietly started counting practice would also have made a practice
/// session a race in everything else reading the same helper: the field
/// strength, the form guide, the championship, "their first Open race
/// here".
///
/// So practice is read by a helper of its own
/// (<see cref="TonightAtTheTrack.PracticeRuns"/>), compared in a type of
/// its own, and never goes near Races.
///
/// WHAT THE SCOPE HONESTLY IS, which any line about this has to say.
/// Practice has no class, so this is "in practice tonight" and nothing
/// stronger. A driver who practised one car and is racing another is
/// comparing two different cars, and nothing in the app can tell. That is
/// a fair thing for a commentator to say out loud - it is what a
/// commentator at a real meeting is doing too - but it is not a fair thing
/// to dress up as a per-class figure, and the fact built from this says
/// "in practice" rather than naming a class.
/// </summary>
/// <param name="Transponder">The driver.</param>
/// <param name="PracticeMs">Their middle lap in their latest practice run tonight.</param>
/// <param name="RaceMs">And in their latest race tonight.</param>
/// <param name="Runs">How many of tonight's practice runs they had enough flying laps in to be measured at all.</param>
/// <param name="RaceName">The race the comparison is counted TO, as the club labelled it - the scope a line has to say.</param>
public sealed record DriverPracticeToNow(
    string Transponder,
    long PracticeMs,
    long RaceMs,
    int Runs,
    string RaceName)
{
    /// <summary>Their middle lap racing against practising. Negative when they are quicker now - the same sign the other two records use.</summary>
    public long DeltaMs => RaceMs - PracticeMs;
}

/// <summary>
/// Tonight at the track, from the races already saved: how the track has
/// changed for each class since its first race of the night (track
/// evolution - the grip coming up as the rubber goes down, or going off as
/// it gets dusty or wet), and the fastest lap in each class.
///
/// "Tonight" is the race night the same way the transponder health counts
/// it: from 6am to 6am, so a club night that runs past midnight is one
/// night. A race counts for its class through the round it was run for;
/// a freeform race with no round has no class and is left out.
///
/// The track is measured through the drivers: each driver's middle flying
/// lap in their first race tonight against the same in their latest, and
/// the middle of those changes across the class. The same drivers on both
/// sides, because comparing one heat with another compares the drivers in
/// them - after heats are grouped by pace, the last heat is quicker however
/// the track is. Middle laps rather than best laps, because one flyer says
/// more about a driver than the track; and rather than the mean, because
/// one crash lap would move it a second.
/// </summary>
public static class TonightAtTheTrack
{
    private static readonly TimeSpan NightTurnsAt = TimeSpan.FromHours(6);

    /// <summary>Fewer flying laps than this from a driver in a race and it says nothing about the track.</summary>
    internal const int LeastLaps = 3;

    /// <summary>Fewer drivers than this to compare and the change is one driver's night, not the track's.</summary>
    internal const int LeastDrivers = 2;

    /// <summary>The night <paramref name="local"/> belongs to, as its date - 6am to 6am.</summary>
    internal static DateTime NightOf(DateTime local) => (local - NightTurnsAt).Date;

    /// <summary>Tonight's saved races (not demos, not practice), oldest first, each with its round's class.</summary>
    internal static List<(SessionRecord Session, string ClassName)> Races(
        IEnumerable<SessionRecord> history, Func<string, RoundDefinition?> roundOf, DateTime nowLocal)
    {
        var tonight = NightOf(nowLocal);

        return history
            .Where(s => !s.IsNotScored() && s.SessionType != RaceSessionType.Practice)
            .Where(s => NightOf(s.StartedAtUtc.ToLocalTime()) == tonight)
            .Select(s => (Session: s, Round: string.IsNullOrEmpty(s.RoundId) ? null : roundOf(s.RoundId)))
            .Where(x => x.Round is not null && !string.IsNullOrWhiteSpace(x.Round.ClassName))
            .OrderBy(x => x.Session.StartedAtUtc)
            .Select(x => (x.Session, x.Round!.ClassName.Trim()))
            .ToList();
    }

    /// <summary>Each class's change since its first race tonight - for classes with drivers who have raced twice or more.</summary>
    public static IReadOnlyList<TrackEvolution> Evolution(
        IEnumerable<SessionRecord> history, Func<string, RoundDefinition?> roundOf, DateTime nowLocal)
    {
        var list = new List<TrackEvolution>();

        foreach (var group in Races(history, roundOf, nowLocal).GroupBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase))
        {
            var races = group.Select(r => r.Session).ToList();

            if (races.Count < 2)
            {
                continue;
            }

            // Each driver's middle lap in their first race tonight against
            // their latest - through Changes, so the class's figure is the
            // middle of exactly the numbers a driver's own line is read
            // from. See Changes and PaceTonight for why that matters.
            var changes = Changes(races, roundOf).Select(change => change.DeltaMs).ToList();

            if (changes.Count < LeastDrivers)
            {
                continue;
            }

            changes.Sort();

            list.Add(new TrackEvolution(group.Key, NameOf(races[0], roundOf), NameOf(races[^1], roundOf),
                changes[changes.Count / 2], changes.Count, races.Count));
        }

        return list.OrderBy(e => e.ClassName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// The fastest lap in each class tonight, quickest class first. Not a
    /// ghost's, not a disqualified result's, and not a driver kept out of
    /// records (<paramref name="excluded"/>, by transponder). A rental's
    /// counts: it was raced tonight, even if it can't hold a track record.
    /// </summary>
    public static IReadOnlyList<FastestTonight> FastestLaps(
        IEnumerable<SessionRecord> history, Func<string, RoundDefinition?> roundOf, DateTime nowLocal,
        Func<string, bool>? excluded = null)
    {
        var list = new List<FastestTonight>();

        foreach (var group in Races(history, roundOf, nowLocal).GroupBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase))
        {
            var best = group
                .SelectMany(r => r.Session.Results
                    .Where(x => x.FastLapMs > 0 && !GhostService.IsGhost(x.Transponder))
                    .Where(x => ResultEditor.NormaliseOutcome(x.Outcome) != ResultEditor.Dq)
                    .Where(x => excluded?.Invoke(x.Transponder) != true)
                    .Select(x => (Result: x, r.Session)))
                .OrderBy(x => x.Result.FastLapMs)
                .FirstOrDefault();

            if (best.Result is null)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(best.Result.DriverName) ? best.Result.Transponder : best.Result.DriverName;
            list.Add(new FastestTonight(group.Key, name, best.Result.FastLapMs, NameOf(best.Session, roundOf)));
        }

        return list.OrderBy(f => f.LapMs).ToList();
    }

    /// <summary>A driver's middle flying lap in one race, or 0 when they had too few to say.</summary>
    internal static long MiddleLap(SessionResultEntry result)
    {
        var laps = result.LapTimesMs
            .Skip(1)
            .Where(ms => ms > 0)
            .OrderBy(ms => ms)
            .ToList();

        return laps.Count < LeastLaps ? 0 : laps[laps.Count / 2];
    }

    /// <summary>
    /// Each driver's change across a set of tonight's races, in the order
    /// the races ran - the one measurement both the class's evolution
    /// figure and anything said about a driver are made from.
    ///
    /// Drivers with only one race in the set are left out: one race is not
    /// a change, whatever the laps came out at.
    ///
    /// THE LeastDrivers BAR IS NOT APPLIED HERE, and that is deliberate.
    /// It exists because one driver's night is not the TRACK's - see the
    /// class summary - which is a judgement about the class-wide figure and
    /// not about a driver's own. Evolution applies it; a reader asking
    /// about one person must not, or a two-car class could never say that
    /// either of them had found half a second.
    /// </summary>
    /// <param name="races">Tonight's races to measure over, OLDEST FIRST - what Races returns.</param>
    /// <param name="roundOf">A round by id, for the label of a race with none of its own.</param>
    internal static List<DriverPaceTonight> Changes(
        List<SessionRecord> races, Func<string, RoundDefinition?> roundOf)
    {
        var found = new List<DriverPaceTonight>();

        var drivers = races
            .SelectMany(race => race.Results
                .Where(r => !string.IsNullOrWhiteSpace(r.Transponder) && !GhostService.IsGhost(r.Transponder))
                .Select(r => (r.Transponder, Middle: MiddleLap(r), Race: race)))
            .Where(x => x.Middle > 0)
            .GroupBy(x => x.Transponder, StringComparer.OrdinalIgnoreCase);

        foreach (var driver in drivers)
        {
            // In running order, because SelectMany walked the races in it
            // and GroupBy keeps what it was given: first is their first
            // race tonight and last is their latest.
            var runs = driver.ToList();

            if (runs.Count < 2)
            {
                continue;
            }

            found.Add(new DriverPaceTonight(
                runs[0].Transponder,
                runs[0].Middle,
                runs[^1].Middle,
                runs.Count,
                NameOf(runs[0].Race, roundOf),
                NameOf(runs[^1].Race, roundOf)));
        }

        return found;
    }

    /// <summary>
    /// Each driver's own change tonight, the biggest first - for a race
    /// intro that wants to say what the night has done to somebody rather
    /// than what it has done to the track.
    ///
    /// Biggest first so that the driver who has found - or lost - the most
    /// is the one that survives a cap, which is the one a commentator
    /// would reach for. By how much it moved either way, because a driver
    /// going backwards all evening is as much of a story as one coming on.
    ///
    /// Empty before anybody has had two races tonight, which is every
    /// club's first race of the night and the right answer for it.
    ///
    /// Call it on the UI thread: it reads the session history and the
    /// programme.
    /// </summary>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="roundOf">A round by id - how a race gets its class and its label.</param>
    /// <param name="nowLocal">Now, in local time, for which night counts as tonight.</param>
    /// <param name="className">The class to measure, or blank for every class racing tonight.</param>
    public static IReadOnlyList<DriverPaceTonight> PaceTonight(
        IEnumerable<SessionRecord> history,
        Func<string, RoundDefinition?> roundOf,
        DateTime nowLocal,
        string? className = null)
    {
        var wanted = (className ?? string.Empty).Trim();

        var races = Races(history, roundOf, nowLocal)
            .Where(r => wanted.Length == 0 || string.Equals(r.ClassName, wanted, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Session)
            .ToList();

        return races.Count < 2
            ? Array.Empty<DriverPaceTonight>()
            : Changes(races, roundOf)
                .OrderByDescending(change => Math.Abs(change.DeltaMs))
                .ToList();
    }

    /// <summary>
    /// Tonight's saved PRACTICE runs, oldest first - not demos.
    ///
    /// NO ROUND AND NO CLASS, unlike <see cref="Races"/>, because a
    /// practice session has neither: PracticeModeControl files it with a
    /// label of "Practice" and no RoundId at all. So there is nothing to
    /// scope it by and nothing is pretended - see
    /// <see cref="DriverPracticeToNow"/>, which says what that costs.
    ///
    /// A helper of its own rather than a flag on Races, deliberately.
    /// Races is read by the field strength, the form guide, the
    /// championship and half the history readers, and every one of them
    /// means "a race that had a result". A parameter that let practice in
    /// would be one call site away from a practice run counting as a start
    /// in somebody's career.
    /// </summary>
    internal static List<SessionRecord> PracticeRuns(IEnumerable<SessionRecord> history, DateTime nowLocal)
    {
        var tonight = NightOf(nowLocal);

        return history
            .Where(s => !s.IsNotScored() && s.SessionType == RaceSessionType.Practice)
            .Where(s => NightOf(s.StartedAtUtc.ToLocalTime()) == tonight)
            .OrderBy(s => s.StartedAtUtc)
            .ToList();
    }

    /// <summary>
    /// Each driver's race pace tonight against their practice pace
    /// tonight, the biggest change first - "he was a second off this in
    /// practice".
    ///
    /// THEIR LATEST PRACTICE RUN AND THEIR LATEST RACE, not their first of
    /// either. A club practises, then races, then often practises again
    /// between classes; the run that says something about how a driver has
    /// come on is the last one before now, and the race that does is the
    /// one they have just had. First-to-latest is what
    /// <see cref="PaceTonight"/> measures, and it measures it over races,
    /// where there is a running order to count along.
    ///
    /// Biggest first, for the reason PaceTonight gives: the driver who has
    /// found or lost the most is the one that should survive a cap.
    ///
    /// Empty until a driver has both a practice run and a race tonight
    /// with enough flying laps in each - which is most nights at most
    /// clubs, and the right answer for them. A club that never practises
    /// through HeatCaller hears nothing about practice, as before.
    ///
    /// Call it on the UI thread: it reads the session history and the
    /// programme.
    /// </summary>
    /// <param name="history">Every session the club has filed.</param>
    /// <param name="roundOf">A round by id - how a RACE gets its class and its label. Practice has neither.</param>
    /// <param name="nowLocal">Now, in local time, for which night counts as tonight.</param>
    /// <param name="className">The class whose races to measure, or blank for every class racing tonight. Practice is never scoped by it - see DriverPracticeToNow.</param>
    public static IReadOnlyList<DriverPracticeToNow> PracticeAgainstNow(
        IEnumerable<SessionRecord> history,
        Func<string, RoundDefinition?> roundOf,
        DateTime nowLocal,
        string? className = null)
    {
        var filed = history as ICollection<SessionRecord> ?? history.ToList();

        var practice = PracticeRuns(filed, nowLocal);

        if (practice.Count == 0)
        {
            return Array.Empty<DriverPracticeToNow>();
        }

        var wanted = (className ?? string.Empty).Trim();

        var races = Races(filed, roundOf, nowLocal)
            .Where(r => wanted.Length == 0 || string.Equals(r.ClassName, wanted, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Session)
            .ToList();

        if (races.Count == 0)
        {
            return Array.Empty<DriverPracticeToNow>();
        }

        var found = new List<DriverPracticeToNow>();

        // The same exclusions the rest of this file makes: a ghost's laps
        // are a recording and a blank transponder is not a driver.
        static IEnumerable<(string Transponder, long Middle, SessionRecord Session)> Measured(List<SessionRecord> sessions) =>
            sessions
                .SelectMany(session => session.Results
                    .Where(r => !string.IsNullOrWhiteSpace(r.Transponder) && !GhostService.IsGhost(r.Transponder))
                    .Select(r => (r.Transponder, Middle: MiddleLap(r), Session: session)))
                .Where(x => x.Middle > 0);

        var practised = Measured(practice)
            .GroupBy(x => x.Transponder, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var driver in Measured(races).GroupBy(x => x.Transponder, StringComparer.OrdinalIgnoreCase))
        {
            if (!practised.TryGetValue(driver.Key, out var runs))
            {
                continue;
            }

            // Both walked oldest first by the two helpers above, and
            // GroupBy keeps what it was given - so the last of each is
            // their latest.
            var raced = driver.ToList();

            found.Add(new DriverPracticeToNow(
                driver.Key,
                runs[^1].Middle,
                raced[^1].Middle,
                runs.Count,
                NameOf(raced[^1].Session, roundOf)));
        }

        return found
            .OrderByDescending(change => Math.Abs(change.DeltaMs))
            .ToList();
    }

    /// <summary>The race's own label, or its round's when it has none.</summary>
    private static string NameOf(SessionRecord session, Func<string, RoundDefinition?> roundOf)
    {
        if (!string.IsNullOrWhiteSpace(session.Label))
        {
            return session.Label.Trim();
        }

        return !string.IsNullOrEmpty(session.RoundId) && roundOf(session.RoundId) is { } round && !string.IsNullOrWhiteSpace(round.RoundLabel)
            ? round.RoundLabel.Trim()
            : "the first race";
    }

    /// <summary>A lap time as the Big Screen shows one: "12.345".</summary>
    public static string Lap(long ms) => (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
}
