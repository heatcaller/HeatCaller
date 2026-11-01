namespace HeatCaller;

/// <summary>
/// What the badges can know beyond the session and the history: where the
/// app is used, and the stores the home and garage badges read. Built once
/// by MainForm (the stores) and completed per session (where, and the
/// ghosts each driver beat). Every part is optional - a badge whose store
/// is missing is simply not judged.
/// </summary>
public sealed record AchievementContext
{
    /// <summary>Club or home - see AchievementEvaluator.EvaluatePractice and AchievementCatalogue.IsOffered.</summary>
    public RacingSetting RacingAt { get; init; } = RacingSetting.Club;

    public bool AtHome => RacingAt == RacingSetting.Home;

    public ProfileStore? Profiles { get; init; }

    /// <summary>
    /// For Record Holder: a track layout as it stands now, by name, so the
    /// badge follows the record Track Layouts shows. Null judges it on the
    /// saved races alone, as before.
    /// </summary>
    public Func<string, TrackLayout?>? TrackLayoutNamed { get; init; }

    /// <summary>For Dialled In: the setups logged in the Garage.</summary>
    public SetupLogStore? Setups { get; init; }

    /// <summary>For Dialled In: which car a setup was for.</summary>
    public CarStore? Cars { get; init; }

    /// <summary>For Fresh Legs and Pack Rotation: which pack each driver ran.</summary>
    public BatteryPackStore? Packs { get; init; }

    /// <summary>The ghosts as they are set up now - read when a session ends, to see whose pace each one ran.</summary>
    public Func<IReadOnlyList<GhostCar>>? GhostCars { get; init; }

    /// <summary>The ghosts switched on part-way through the race now on the board, by transponder - see AchievementEvaluator.GhostsFromTheGreen.</summary>
    public Func<IReadOnlyCollection<string>>? GhostsJoinedLate { get; init; }

    /// <summary>
    /// The ghosts each driver beat in the session just finished, by the
    /// transponder their result is filed under. Worked out at the flag
    /// (GhostsBeatenBy), because a saved result leaves ghosts out.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<GhostCar>> GhostsBeaten { get; init; } =
        new Dictionary<string, IReadOnlyList<GhostCar>>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The badges for a home track, and the ones that read the garage, the
/// packs and the ghosts (Oct 2026). One catalogue for a club and a home
/// track: a home track is offered every badge but the club-sized ones, and
/// its practice sessions are judged for the badges a driver can earn alone
/// (EvaluatePractice). A club's practice still earns nothing.
/// </summary>
public static partial class AchievementEvaluator
{
    /// <summary>A driver has to do this many laps before beating a ghost counts - a lap or two ahead of one is not beating it.</summary>
    private const int GhostLapsToCount = 5;

    /// <summary>Practice Makes Perfect: laps in one practice session.</summary>
    private const int PracticeLapsForBadge = 100;

    /// <summary>Regular at the Track: different days practised.</summary>
    private const int PracticeDaysForBadge = 10;

    /// <summary>Dialled In: the personal best has to come within this many sessions of the setup, counting its own.</summary>
    private const int SessionsAfterSetup = 3;

    /// <summary>Fresh Legs: the pack run most has to have been run at least this often to be "the one you run most".</summary>
    private const int UsualPackSessions = 3;

    /// <summary>
    /// Every session before this one that a personal best is measured
    /// against at a home track - races and practice, never a demo - newest
    /// first, as the race history is.
    /// </summary>
    private static List<SessionRecord> EarlierSessions(IReadOnlyList<SessionRecord> history, SessionRecord record) =>
        history
            .Where(r => r.SessionType is RaceSessionType.Race or RaceSessionType.Practice
                        && !r.IsNotScored()
                        && !string.Equals(r.Id, record.Id, StringComparison.Ordinal))
            .OrderByDescending(r => r.StartedAtUtc)
            .ToList();

    /// <summary>
    /// The ghosts a race's ghost badges are judged against: every one set
    /// up, less those switched on part-way through it
    /// (AchievementContext.GhostsJoinedLate). One of those joins on no laps
    /// and finishes far down, so every driver was "ahead" of it at the flag.
    /// </summary>
    internal static IReadOnlyList<GhostCar> GhostsFromTheGreen(IReadOnlyList<GhostCar> ghosts, IReadOnlyCollection<string> joinedLate) =>
        joinedLate.Count == 0
            ? ghosts
            : ghosts.Where(g => !joinedLate.Contains(g.Transponder.Trim(), StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>
    /// Which ghosts each real driver beat, from the board as it stood at the
    /// end - ghosts included, which the saved result leaves out. In a race,
    /// finishing ahead of it; in practice, where nobody finishes ahead of
    /// anybody, a quicker best lap. Either way only after enough laps for it
    /// to mean something, and never against a ghost that set no time.
    /// </summary>
    /// <param name="standings">The board in finishing order: transponder, laps, best lap.</param>
    /// <param name="byBestLap">True for practice: judged by best lap rather than by place.</param>
    public static Dictionary<string, IReadOnlyList<GhostCar>> GhostsBeatenBy(
        IReadOnlyList<(string Transponder, int Laps, long FastLapMs)> standings,
        IReadOnlyList<GhostCar> ghosts,
        bool byBestLap)
    {
        var beaten = new Dictionary<string, IReadOnlyList<GhostCar>>(StringComparer.OrdinalIgnoreCase);

        if (ghosts.Count == 0)
        {
            return beaten;
        }

        var byTransponder = ghosts
            .Where(g => !string.IsNullOrWhiteSpace(g.Transponder))
            .GroupBy(g => g.Transponder.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < standings.Count; i++)
        {
            var (transponder, laps, fastLap) = standings[i];

            if (string.IsNullOrWhiteSpace(transponder) || GhostService.IsGhost(transponder) || laps < GhostLapsToCount)
            {
                continue;
            }

            var mine = new List<GhostCar>();

            for (var j = 0; j < standings.Count; j++)
            {
                var other = standings[j];

                if (!GhostService.IsGhost(other.Transponder)
                    || !byTransponder.TryGetValue(other.Transponder.Trim(), out var ghost)
                    || other.FastLapMs <= 0)
                {
                    continue;
                }

                var ahead = byBestLap
                    ? fastLap > 0 && fastLap < other.FastLapMs
                    : i < j;

                if (ahead)
                {
                    mine.Add(ghost);
                }
            }

            if (mine.Count > 0)
            {
                beaten[transponder.Trim()] = mine;
            }
        }

        return beaten;
    }

    /// <summary>
    /// A practice session at a home track: each driver in it judged for the
    /// badges that can be earned alone and make sense lapping on your own -
    /// clean runs, consistency, personal bests, lap totals - and the home
    /// and garage badges. Never a race count, a win or a track record:
    /// those stay races only. At a club, nothing (as ever): practice there
    /// is not judged.
    /// </summary>
    public static List<AchievementUnlock> EvaluatePractice(
        SessionRecord record,
        IReadOnlyList<SessionRecord> history,
        AchievementStore store,
        AchievementContext context)
        => EvaluatePractice(record, history, store, context, out _);

    /// <summary>
    /// As above, and says what it kept: <paramref name="lapsSaved"/> is the
    /// laps it added to each driver's lap total (by transponder) when the
    /// store was saved with them, and null when nothing was saved - so a
    /// caller that judges the same practice again (Practice mode, part-way
    /// through) takes back only laps that really are in the file. Without
    /// this it took back laps whose save had failed, and the career lap
    /// total shrank for good.
    /// </summary>
    public static List<AchievementUnlock> EvaluatePractice(
        SessionRecord record,
        IReadOnlyList<SessionRecord> history,
        AchievementStore store,
        AchievementContext context,
        out Dictionary<string, int>? lapsSaved)
    {
        lapsSaved = null;
        var unlocks = new List<AchievementUnlock>();
        var lapsAdded = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (!context.AtHome || record.SessionType != RaceSessionType.Practice || record.IsNotScored())
        {
            return unlocks;
        }

        var results = record.Results
            .Where(entry => !GhostService.IsGhost(entry.Transponder) && !string.IsNullOrWhiteSpace(entry.Transponder))
            .ToList();

        if (results.Count == 0)
        {
            return unlocks;
        }

        var pool = EarlierSessions(history, record);

        foreach (var entry in results)
        {
            if (entry.Laps < MinimumLapsToCount)
            {
                continue;
            }

            var driver = store.For(entry.Transponder);
            var name = string.IsNullOrWhiteSpace(entry.DriverName) ? entry.Transponder : entry.DriverName;

            // Laps are laps: at home, practice counts towards the totals.
            driver.TotalLaps += entry.Laps;
            lapsAdded[entry.Transponder] = lapsAdded.GetValueOrDefault(entry.Transponder) + entry.Laps;

            void Award(string id, string detail = "")
            {
                if (store.Award(entry.Transponder, id, record.Id, detail)
                    && AchievementCatalogue.Get(id) is { } badge)
                {
                    unlocks.Add(new AchievementUnlock(entry.Transponder, name, badge, detail));
                }
            }

            // --- Clean runs and consistency, as in a race ---
            var streak = LongestCleanRun(entry.LapTimesMs);

            if (streak >= 50)
            {
                Award(AchievementCatalogue.CleanFifty, $"{streak} laps");
            }
            else if (streak >= 25)
            {
                Award(AchievementCatalogue.CleanTwentyFive, $"{streak} laps");
            }
            else if (streak >= 10)
            {
                Award(AchievementCatalogue.CleanTen, $"{streak} laps");
            }

            if (IsWithinTolerance(entry.LapTimesMs, ConsistencyTolerance))
            {
                Award(AchievementCatalogue.Consistent);

                if (IsWithinTolerance(entry.LapTimesMs, TightConsistencyTolerance))
                {
                    Award(AchievementCatalogue.SwissWatch);
                }
            }

            if (entry.LapTimesMs.Count >= 10 && LongestCleanRun(entry.LapTimesMs) >= entry.LapTimesMs.Count - 1)
            {
                Award(AchievementCatalogue.Unbroken, $"{entry.LapTimesMs.Count - 1} laps");
            }

            // --- Personal bests, against races and practice alike ---
            var previousBest = BestLapOnLayout(pool, entry.Transponder, record.TrackLayoutName);

            if (entry.FastLapMs > 0 && previousBest > 0 && entry.FastLapMs < previousBest)
            {
                Award(AchievementCatalogue.PersonalBest, Seconds(entry.FastLapMs));

                if (previousBest - entry.FastLapMs >= 250)
                {
                    Award(AchievementCatalogue.BigStep, $"{(previousBest - entry.FastLapMs) / 1000.0:0.000}s quicker");
                }

                if (HasImprovedThreeRunning(pool, record, entry.Transponder))
                {
                    Award(AchievementCatalogue.ThreeMeetingsImproving);
                }

                var layoutsImproved = LayoutsImprovedOn(pool, record, entry.Transponder);

                if (layoutsImproved >= 3)
                {
                    Award(AchievementCatalogue.Sharpshooter, $"{layoutsImproved} layouts");
                }
            }

            if (previousBest > 0)
            {
                var running = previousBest;
                var bestsBroken = 0;

                foreach (var lap in entry.LapTimesMs.Skip(1))
                {
                    if (lap > 0 && lap < running)
                    {
                        running = lap;
                        bestsBroken++;
                    }
                }

                if (bestsBroken >= 3)
                {
                    Award(AchievementCatalogue.HotStreak, $"{bestsBroken} bests in one session");
                }
            }

            // --- Lap totals ---
            if (driver.TotalLaps >= 1000) { Award(AchievementCatalogue.ThousandLaps, $"{driver.TotalLaps} laps"); }
            if (driver.TotalLaps >= 5000) { Award(AchievementCatalogue.FiveThousandLaps, $"{driver.TotalLaps} laps"); }
            if (driver.TotalLaps >= 10000) { Award(AchievementCatalogue.TenThousandLaps, $"{driver.TotalLaps} laps"); }

            // --- Practice itself ---
            if (entry.Laps >= PracticeLapsForBadge)
            {
                Award(AchievementCatalogue.PracticeMakesPerfect, $"{entry.Laps} laps");
            }

            if (record.StartedAtUtc != default)
            {
                var days = pool
                    .Where(r => r.SessionType == RaceSessionType.Practice
                                && r.StartedAtUtc != default
                                && EntryOf(r, entry.Transponder) is { Laps: >= MinimumLapsToCount })
                    .Select(LocalDay)
                    .Append(LocalDay(record))
                    .Distinct()
                    .Count();

                if (days >= PracticeDaysForBadge)
                {
                    Award(AchievementCatalogue.RegularAtTheTrack, $"{days} days");
                }
            }

            EvaluateHomeAndGarage(entry, record, pool, context, Award, practice: true);
        }

        // Not announced unless kept, as for a race (EvaluateFinishedRace): a
        // badge whose save failed was called over the PA and then called
        // again after every practice, because the file never held it.
        if (!store.Save())
        {
            return new List<AchievementUnlock>();
        }

        lapsSaved = lapsAdded;

        return unlocks
            .OrderByDescending(unlock => (int)unlock.Badge.Tier)
            .ToList();
    }

    /// <summary>
    /// The badges that read the ghosts, the garage and the packs, for one
    /// driver in a race or (at home) a practice. <paramref name="pool"/> is
    /// what their personal bests are measured against - the same list the
    /// New Heights badge used, so the two always agree on what a personal
    /// best was.
    /// </summary>
    private static void EvaluateHomeAndGarage(
        SessionResultEntry entry,
        SessionRecord record,
        IReadOnlyList<SessionRecord> pool,
        AchievementContext? context,
        Action<string, string> award,
        bool practice)
    {
        if (context is null)
        {
            return;
        }

        var profiles = context.Profiles;
        var key = BatteryPackStore.DriverKey(profiles, entry.Transponder);

        // --- Ghosts ---
        if (context.GhostsBeaten.TryGetValue(entry.Transponder.Trim(), out var ghosts))
        {
            // Their own best lap, at full pace or quicker - not a ghost made
            // slower on purpose to be beaten.
            var ownBest = ghosts.FirstOrDefault(g =>
                g.PaceSource is GhostPaceSource.YourPersonalBest or GhostPaceSource.DriverPersonalBest or GhostPaceSource.PersonalBestInRange
                    or GhostPaceSource.OwnBestTonight
                && g.PacePercent <= 100
                && !string.IsNullOrWhiteSpace(g.SourceTransponder)
                && string.Equals(BatteryPackStore.DriverKey(profiles, g.SourceTransponder), key, StringComparison.OrdinalIgnoreCase));

            if (ownBest is not null)
            {
                award(AchievementCatalogue.GhostBuster, practice ? "in practice" : "in a race");
            }

            if (!practice && ghosts.Any(g => g.IsLivePace))
            {
                award(AchievementCatalogue.ShadowBoxer, string.Empty);
            }
        }

        // --- Packs on the day, whether or not it was a best ---
        if (context.Packs is { } packs && profiles?.Get(key) is { } profile && record.StartedAtUtc != default)
        {
            var own = packs.OwnedBy(profile).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            var today = LocalDay(record);

            var ranToday = pool
                .Where(r => r.StartedAtUtc != default && LocalDay(r) == today && EntryOf(r, entry.Transponder) is not null)
                .Append(record)
                .Select(r => packs.PackIdFor(r.Id, key))
                .Where(id => id is not null && own.Contains(id))
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (ranToday >= 3)
            {
                award(AchievementCatalogue.PackRotation, $"{ranToday} packs");
            }
        }

        // --- Personal bests ---
        var previousBest = BestLapOnLayout(pool, entry.Transponder, record.TrackLayoutName);

        if (entry.FastLapMs <= 0 || previousBest <= 0 || entry.FastLapMs >= previousBest)
        {
            return;
        }

        var bests = PersonalBestsOf(pool.Append(record), entry.Transponder);

        var cars = bests
            .Select(b => b.Car)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (cars >= 3)
        {
            award(AchievementCatalogue.GarageTour, $"{cars} cars");
        }

        if (record.StartedAtUtc != default)
        {
            var yesterday = LocalDay(record).AddDays(-1);

            if (bests.Any(b => b.Day == yesterday))
            {
                award(AchievementCatalogue.BackToBack, $"{yesterday:d MMM} and {LocalDay(record):d MMM}");
            }
        }

        if (DialledInAfter(entry, record, pool, context) is { } sessions)
        {
            award(AchievementCatalogue.DialledIn, sessions == 1 ? "the first session after a setup" : $"{sessions} sessions after a setup");
        }

        if (context.Packs is { } store
            && store.PackFor(record.Id, key) is { } packTonight)
        {
            var runs = pool
                .Where(r => EntryOf(r, entry.Transponder) is not null)
                .Select(r => store.PackIdFor(r.Id, key))
                .Where(id => id is not null)
                .GroupBy(id => id!, StringComparer.Ordinal)
                .Select(g => (Pack: g.Key, Sessions: g.Count()))
                .OrderByDescending(g => g.Sessions)
                .ToList();

            // The one run most - clearly most: two level at the top is no
            // usual pack, and tonight's could be either of them.
            var usual = runs.Count > 0 && (runs.Count == 1 || runs[0].Sessions > runs[1].Sessions) ? runs[0] : default;

            if (usual.Pack is not null && usual.Sessions >= UsualPackSessions && !string.Equals(usual.Pack, packTonight.Id, StringComparison.Ordinal))
            {
                award(AchievementCatalogue.FreshLegs, $"on {packTonight.DisplayName}");
            }
        }
    }

    /// <summary>
    /// Every personal best this driver has set, oldest first: a session's
    /// best lap on a layout quicker than every one of theirs there before
    /// it. The first lap ever timed on a layout sets the mark; it does not
    /// beat one - as New Heights has it.
    /// </summary>
    private static List<(SessionRecord Session, string Car, DateTime Day)> PersonalBestsOf(IEnumerable<SessionRecord> sessions, string transponder)
    {
        var bests = new List<(SessionRecord, string, DateTime)>();
        var bestByLayout = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions.Where(s => !string.IsNullOrWhiteSpace(s.TrackLayoutName)).OrderBy(s => s.StartedAtUtc))
        {
            if (EntryOf(session, transponder) is not { FastLapMs: > 0 } mine)
            {
                continue;
            }

            var layout = session.TrackLayoutName.Trim();

            if (bestByLayout.TryGetValue(layout, out var best))
            {
                if (mine.FastLapMs < best)
                {
                    bests.Add((session, mine.CarModel?.Trim() ?? string.Empty, LocalDay(session)));
                    bestByLayout[layout] = mine.FastLapMs;
                }
            }
            else
            {
                bestByLayout[layout] = mine.FastLapMs;
            }
        }

        return bests;
    }

    /// <summary>
    /// For Dialled In: how many sessions this one is after the newest setup
    /// logged for the car it was run in (this one counted) - or null when
    /// there is no such setup, or it is more than three sessions back.
    ///
    /// A setup is for this car when it is linked to a garage car of that
    /// name (or, logged before setups belonged to cars, typed with that
    /// name), and is this driver's: logged under their name, or for a car
    /// of theirs.
    /// </summary>
    private static int? DialledInAfter(SessionResultEntry entry, SessionRecord record, IReadOnlyList<SessionRecord> pool, AchievementContext context)
    {
        if (context.Setups is not { } setups || record.StartedAtUtc == default)
        {
            return null;
        }

        var car = entry.CarModel?.Trim() ?? string.Empty;

        if (car.Length == 0)
        {
            return null;
        }

        var key = BatteryPackStore.DriverKey(context.Profiles, entry.Transponder);
        var driverName = entry.DriverName?.Trim() ?? string.Empty;

        bool IsThisCar(SetupEntry setup)
        {
            var linked = string.IsNullOrEmpty(setup.CarId) ? null : context.Cars?.Get(setup.CarId);
            var setupCar = linked?.DisplayName ?? setup.CarModel?.Trim() ?? string.Empty;

            if (!string.Equals(setupCar, car, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var theirCar = linked is not null
                           && string.Equals(BatteryPackStore.DriverKey(context.Profiles, linked.OwnerTransponder), key, StringComparison.OrdinalIgnoreCase);

            return theirCar || (driverName.Length > 0 && string.Equals(setup.DriverName?.Trim(), driverName, StringComparison.OrdinalIgnoreCase));
        }

        var setup = setups.All
            .Where(s => s.RecordedAtUtc <= record.StartedAtUtc && IsThisCar(s))
            .OrderByDescending(s => s.RecordedAtUtc)
            .FirstOrDefault();

        if (setup is null)
        {
            return null;
        }

        var since = pool.Count(r => r.StartedAtUtc > setup.RecordedAtUtc
                                    && r.StartedAtUtc < record.StartedAtUtc
                                    && EntryOf(r, entry.Transponder) is { Laps: >= MinimumLapsToCount });

        return since + 1 <= SessionsAfterSetup ? since + 1 : null;
    }
}
