namespace HeatCaller;

/// <summary>
/// The stores the Driver Hub card reads a driver's record from.
///
/// A bag of optional references rather than ten more constructor
/// parameters: the card works with any of them missing - it simply shows
/// less - and a page that only wants the profile half never has to invent
/// stores it does not have.
/// </summary>
public sealed class DriverStatsSources
{
    public SessionHistoryStore? History { get; init; }
    public SeasonStore? Seasons { get; init; }
    public RoundDefinitionStore? Rounds { get; init; }
    public RaceEventStore? Events { get; init; }
    public RivalryStore? Rivalries { get; init; }
    public PersonalBestResetStore? PersonalBestResets { get; init; }
    public HandicapStore? Handicaps { get; init; }
    public TechInspectionStore? TechInspections { get; init; }
    public CheckInStore? CheckIns { get; init; }
    public SetupLogStore? SetupLog { get; init; }

    /// <summary>The setup sheets, for the names of the fields a setup changed - see DriverStats.RecentActivity.</summary>
    public ChassisTemplateStore? ChassisTemplates { get; init; }

    /// <summary>The track layouts, for the lap records a driver holds.</summary>
    public TrackLayoutStore? TrackLayouts { get; init; }
}

/// <summary>
/// Everything the per-driver sums read, copied out of the live stores on
/// the UI thread.
///
/// The stores are not safe to enumerate from another thread - a race can
/// finish and be added to history while the card is still working - so a
/// background calculation is only ever handed this. Every list in it is a
/// fresh copy or an object nothing else holds, and a saved SessionRecord's
/// results are never changed once it is in history, so reading them off
/// the UI thread is safe.
///
/// Built once per change of data rather than once per driver: the
/// championship tables in particular cost a pass over history per season,
/// and clicking down the roster should not pay that for every name.
/// </summary>
public sealed class DriverStatsSnapshot
{
    /// <summary>Every saved session, newest first (the order SessionHistoryStore.All gives).</summary>
    public required IReadOnlyList<SessionRecord> Sessions { get; init; }

    /// <summary>Round id to the class that round was for - the only place a saved race's class is recorded.</summary>
    public required IReadOnlyDictionary<string, string> RoundClasses { get; init; }

    /// <summary>Every enabled rivalry line, as its two driver names.</summary>
    public required IReadOnlyList<(string A, string B)> Rivalries { get; init; }

    /// <summary>Each championship's current table, worked out exactly as the Championship page works it out.</summary>
    public required IReadOnlyList<(string Season, IReadOnlyList<SeasonStandingEntry> Table)> Championships { get; init; }

    /// <summary>A private copy of the badges file, or null when it could not be read.</summary>
    public AchievementStore? Achievements { get; init; }

    /// <summary>Every logged setup, newest first - a fresh list, as SetupLogStore.All gives it.</summary>
    public IReadOnlyList<SetupEntry> Setups { get; init; } = Array.Empty<SetupEntry>();

    /// <summary>The setup sheets by id, copied out of the store for the field names.</summary>
    public IReadOnlyDictionary<string, ChassisTemplate> SetupSheets { get; init; } = new Dictionary<string, ChassisTemplate>();

    /// <summary>Each layout's lap record as it stands - layout, lap, who it was announced for, when - copied from the live store.</summary>
    public IReadOnlyList<(string Layout, long LapMs, string Holder, DateTime? SetUtc)> TrackRecords { get; init; } =
        Array.Empty<(string, long, string, DateTime?)>();

    /// <summary>Each layout's sector records as they stand now - layout, sector (from 1), time, holder - copied from the live store.</summary>
    public IReadOnlyList<(string Layout, int Sector, long Ms, string Holder)> SectorRecords { get; init; } =
        Array.Empty<(string, int, long, string)>();

    /// <summary>Takes the copy. UI thread only - see the class summary.</summary>
    public static DriverStatsSnapshot Take(DriverStatsSources sources, ProfileStore profiles)
    {
        IReadOnlyList<SessionRecord> sessions = sources.History?.All ?? Array.Empty<SessionRecord>();

        var roundClasses = new Dictionary<string, string>(StringComparer.Ordinal);

        if (sources.Rounds is not null)
        {
            foreach (var round in sources.Rounds.All)
            {
                if (!string.IsNullOrWhiteSpace(round.Id) && !string.IsNullOrWhiteSpace(round.ClassName))
                {
                    roundClasses[round.Id] = round.ClassName.Trim();
                }
            }
        }

        var rivalries = (sources.Rivalries?.All ?? Array.Empty<RivalryLine>())
            .Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.DriverA) && !string.IsNullOrWhiteSpace(r.DriverB))
            .Select(r => (A: r.DriverA.Trim(), B: r.DriverB.Trim()))
            .ToList();

        var championships = new List<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>();

        if (sources.Seasons is not null && sources.Rounds is not null && sources.History is not null)
        {
            foreach (var season in sources.Seasons.All)
            {
                try
                {
                    // The same two calls the Championship page and the
                    // relay make (SeasonsControl, MainForm.PublishStandings-
                    // ToRelay): which races count is EventSpine's answer,
                    // the points are SeasonStore's. Nothing re-scored here.
                    var counted = EventSpine.SessionsFor(season, sources.Events, sources.Rounds, sources.History)
                        .Where(s => !s.IsNotScored())
                        .ToList();

                    var table = SeasonStore.ComputeStandings(season, counted, profiles);

                    championships.Add((season.Name, (IReadOnlyList<SeasonStandingEntry>)table));
                }
                catch
                {
                    // One championship that cannot be worked out is left off
                    // the card; it is not a reason to show no stats at all.
                }
            }
        }

        AchievementStore? achievements;

        try
        {
            // A private copy, loaded on the UI thread like every other
            // reader of that file - its loader copies an unreadable file
            // aside, and that must not race a save from the race wrap-up.
            achievements = AchievementStore.Load();
        }
        catch
        {
            achievements = null;
        }

        // The sheets copied into a dictionary of their own: the store hands
        // out its live list, and the sums may run on another thread.
        var sheets = new Dictionary<string, ChassisTemplate>(StringComparer.Ordinal);

        foreach (var sheet in sources.ChassisTemplates?.All ?? Array.Empty<ChassisTemplate>())
        {
            if (!string.IsNullOrEmpty(sheet.Id))
            {
                sheets[sheet.Id] = sheet;
            }
        }

        return new DriverStatsSnapshot
        {
            Sessions = sessions,
            RoundClasses = roundClasses,
            Rivalries = rivalries,
            Championships = championships,
            Achievements = achievements,
            // Copies, not the store's own entries: the Garage's setup form
            // edits an entry's values in place while it is open, and the
            // sums may be reading these on another thread.
            Setups = (sources.SetupLog?.All ?? Array.Empty<SetupEntry>()).Select(SetupLogStore.CopyOf).ToList(),
            SetupSheets = sheets,

            // Copied as values: the sums may run on another thread, and a
            // race finishing meanwhile writes a new record into the layout.
            TrackRecords = (sources.TrackLayouts?.All ?? Array.Empty<TrackLayout>())
                .Where(t => t.RecordLapMs > 0 && !string.IsNullOrWhiteSpace(t.Name))
                .Select(t => (t.Name.Trim(), t.RecordLapMs, t.RecordHolderName?.Trim() ?? string.Empty, t.RecordSetUtc))
                .ToList(),

            // Only those for the way each lap is cut now.
            SectorRecords = (sources.TrackLayouts?.All ?? Array.Empty<TrackLayout>())
                .Where(t => t.HasSectorRecords() && !string.IsNullOrWhiteSpace(t.Name))
                .SelectMany(t => t.CurrentSectorRecords()
                    .Where(r => r is not null)
                    .Select(r => (t.Name.Trim(), r!.Sector, r.Ms, r.Holder?.Trim() ?? string.Empty)))
                .ToList(),
        };
    }
}

/// <summary>Who to work the numbers out for.</summary>
public sealed class DriverStatsQuery
{
    /// <summary>The driver's main transponder - the profile's key.</summary>
    public required string Transponder { get; init; }

    /// <summary>Their main transponder and any alternate that ProfileStore resolves to them. Case-blind.</summary>
    public required IReadOnlySet<string> Transponders { get; init; }

    /// <summary>The profile's driver name, trimmed; empty when they have none.</summary>
    public required string DriverName { get; init; }
}

/// <summary>A badge a driver has earned: which, when, and what it was for ("12 laps", "4.586").</summary>
public sealed record BadgeLine(AchievementDefinition Badge, DateTime EarnedUtc, string Detail);

/// <summary>A layout whose lap record a driver holds.</summary>
public sealed record TrackRecordLine(string Layout, long LapMs, DateTime? SetUtc);

/// <summary>A personal best on one track layout.</summary>
public sealed record PersonalBestLine(string Layout, long LapMs, DateTime SetUtc, string Car);

/// <summary>A driver's best time through each sector of a layout, and those added up - their ideal lap there.</summary>
public sealed record SectorBestLine(string Layout, long[] Sectors, long IdealMs);

/// <summary>A sector record of a layout that is theirs now.</summary>
public sealed record SectorRecordLine(string Layout, int Sector, long Ms);

/// <summary>Where a driver stands in one championship.</summary>
public sealed record ChampionshipLine(string Season, int Position, int FieldSize, int Points, int Wins);

/// <summary>
/// The driver's top rival and the head-to-head between them.
/// <paramref name="Named"/> is true when the pair is set up as a rivalry on
/// the Rivalries page, false when this is simply who they have raced most.
/// </summary>
public sealed record RivalLine(string Name, int Ahead, int Behind, bool Named);

/// <summary>What kind of thing a line of a driver's recent activity is.</summary>
public enum ActivityKind
{
    Race,
    PersonalBest,
    Setup,
}

/// <summary>One line of a driver's recent activity: when, what kind, and what happened, in words.</summary>
public sealed record ActivityLine(DateTime WhenUtc, ActivityKind Kind, string Text);

/// <summary>What the card says about today: check-in, tech, handicap. Read live from the stores.</summary>
public sealed record RaceDayLine(
    bool? CheckedIn,
    string? Tech,
    bool TechFailed,
    string? Handicap,
    int EventsAttended,
    int SetupSheets);

/// <summary>
/// One driver's record, worked out from saved history.
///
/// Every figure here follows a rule some other page already applies, so
/// the card never disagrees with the page a race director would check it
/// against:
///
/// - Demo races never count (SessionRecord.IsDemoSession).
/// - Ghost cars never count. A finishing place is the driver's place among
///   real cars only, closed up behind any ghost - AchievementEvaluator's
///   PlaceInRace rule. Ghosts are never a rival.
/// - A race row with no laps is a start but not a finish (a DNF), as the
///   championship scores it; wins, podiums and average finish are judged
///   on finishes, as the achievements count them (a lap or more).
/// - Personal bests are the Personal Bests page's rule, resets included.
/// - Championship places are the Championship page's own table.
/// - The rival record is the Head to Head page's rule: every shared
///   session, whoever finished higher.
/// </summary>
public sealed class DriverStats
{
    /// <summary>Histories up to this many sessions are summed straight away; longer ones on a background thread.</summary>
    public const int InlineSessionLimit = 300;

    /// <summary>How many finishes the recent-form strip shows.</summary>
    public const int FormLength = 5;

    /// <summary>True when the history could not be read. Every other figure is then empty.</summary>
    public bool Failed { get; private init; }

    public int Starts { get; private set; }
    public int Finishes { get; private set; }
    public int Dnfs => Starts - Finishes;
    public int Wins { get; private set; }
    public int Seconds { get; private set; }
    public int Thirds { get; private set; }
    public int Podiums => Wins + Seconds + Thirds;

    /// <summary>Wins as a share of races started, 0-100. Null before their first race.</summary>
    public double? WinRatePercent => Starts == 0 ? null : Wins * 100.0 / Starts;

    /// <summary>Mean finishing place over races finished. Null when they have finished none.</summary>
    public double? AverageFinish { get; private set; }

    public long RaceLaps { get; private set; }
    public int PracticeSessions { get; private set; }

    /// <summary>Laps turned in practice, counted the same way as race laps.</summary>
    public long PracticeLaps { get; private set; }

    /// <summary>Every lap the timer has for them - races and practice together.</summary>
    public long TotalLaps => RaceLaps + PracticeLaps;

    /// <summary>Races and practice sessions together.</summary>
    public int Sessions => Starts + PracticeSessions;

    /// <summary>Their best finishing place. Null until they finish a race.</summary>
    public int? BestFinish { get; private set; }

    /// <summary>Finishes in the top five, the podium included.</summary>
    public int TopFives { get; private set; }

    /// <summary>
    /// Their mean driver rating over every race that could be rated (0-150,
    /// see DriverRating). Null before their first. Races of one car are not
    /// rated - winning a race nobody else was in says nothing.
    /// </summary>
    public double? AverageRating { get; private set; }

    /// <summary>Their best single-race driver rating. Null before their first rated race.</summary>
    public double? BestRating { get; private set; }

    /// <summary>Laps completed while leading, across every race.</summary>
    public long LapsLed { get; private set; }

    /// <summary>The day of their most recent win, or null.</summary>
    public DateTime? LastWinUtc { get; private set; }

    /// <summary>Separate days they were on track at all - a race or a practice session counts the day.</summary>
    public int DaysOnTrack { get; private set; }

    /// <summary>Races finished as a share of races started, 0-100. Null before their first race.</summary>
    public double? FinishRatePercent => Starts == 0 ? null : Finishes * 100.0 / Starts;

    /// <summary>Podiums as a share of races started, 0-100. Null before their first race.</summary>
    public double? PodiumRatePercent => Starts == 0 ? null : Podiums * 100.0 / Starts;

    /// <summary>Mean laps per race started. Null before their first race.</summary>
    public double? AverageRaceLaps => Starts == 0 ? null : RaceLaps / (double)Starts;

    public DateTime? LastRacedUtc { get; private set; }
    public DateTime? LastOnTrackUtc { get; private set; }
    public DateTime? FirstSeenUtc { get; private set; }

    /// <summary>The last few races, newest first: the place, or 0 for a race they did not finish.</summary>
    public IReadOnlyList<int> RecentForm { get; private set; } = Array.Empty<int>();

    /// <summary>Classes they have raced in, most raced first. Only races run from a programme round carry a class.</summary>
    public IReadOnlyList<string> Classes { get; private set; } = Array.Empty<string>();

    /// <summary>One line per layout, after personal-best resets. Filled by <see cref="ApplyPersonalBests"/>.</summary>
    public IReadOnlyList<PersonalBestLine> PersonalBests { get; private set; } = Array.Empty<PersonalBestLine>();

    /// <summary>The quickest of their personal bests, or null.</summary>
    public PersonalBestLine? BestLap { get; private set; }

    public IReadOnlyList<ChampionshipLine> Championships { get; private set; } = Array.Empty<ChampionshipLine>();

    public RivalLine? Rival { get; private set; }

    public int BadgeCount { get; private set; }

    /// <summary>Their most recently earned badges, newest first.</summary>
    public IReadOnlyList<AchievementDefinition> LatestBadges { get; private set; } = Array.Empty<AchievementDefinition>();

    /// <summary>Every badge they have earned, newest first, with when and what it was for.</summary>
    public IReadOnlyList<BadgeLine> Badges { get; private set; } = Array.Empty<BadgeLine>();

    /// <summary>Their best through each sector, layout by layout - from laps run with a split loop. Newest layout first.</summary>
    public IReadOnlyList<SectorBestLine> BestSectors { get; private set; } = Array.Empty<SectorBestLine>();

    /// <summary>The sector records that are theirs now.</summary>
    public IReadOnlyList<SectorRecordLine> SectorRecordsHeld { get; private set; } = Array.Empty<SectorRecordLine>();

    /// <summary>The layouts whose lap record is theirs now, quickest first.</summary>
    public IReadOnlyList<TrackRecordLine> RecordsHeld { get; private set; } = Array.Empty<TrackRecordLine>();

    /// <summary>Layouts they have held the record on and no longer do (from their badges' running totals).</summary>
    public IReadOnlyList<string> RecordsHeldBefore { get; private set; } = Array.Empty<string>();

    /// <summary>Filled on the UI thread once the sums are back - it reads live stores.</summary>
    public RaceDayLine? RaceDay { get; set; }

    /// <summary>How many lines of recent activity the card shows.</summary>
    public const int ActivityLength = 8;

    /// <summary>How many of their latest races and setups are looked at for it - plenty to fill it.</summary>
    private const int ActivityLookBack = ActivityLength;

    /// <summary>
    /// What they have been up to lately, newest first (27 Sep 2026): their
    /// races, their personal bests, and the setups logged for them, with
    /// what each setup changed. Personal bests join once
    /// <see cref="ApplyPersonalBests"/> has settled them.
    /// </summary>
    public IReadOnlyList<ActivityLine> RecentActivity { get; private set; } = Array.Empty<ActivityLine>();

    /// <summary>The races and setups of RecentActivity, before the personal bests join them.</summary>
    private List<ActivityLine> _activity = new();

    /// <summary>
    /// Every timed lap of theirs that could be a personal best, before any
    /// reset is applied. The resets live in a store that is only safe to
    /// read on the UI thread, so the last step happens there - see
    /// <see cref="ApplyPersonalBests"/>.
    /// </summary>
    private readonly List<(string Layout, string ResultName, long LapMs, DateTime StartedUtc, string Car)> _lapCandidates = new();

    public static DriverStats CouldNotRead() => new() { Failed = true };

    /// <summary>
    /// The sums. Pure: reads only the query and the snapshot, touches no
    /// store and no control, so it is safe on any thread.
    /// </summary>
    public static DriverStats Compute(DriverStatsQuery query, DriverStatsSnapshot snapshot)
    {
        var stats = new DriverStats();
        var mine = query.Transponders;

        // The names this driver appears under in saved results - the key
        // the championship tables use (ComputeStandings keys on the name
        // saved with each result, or the transponder when there was none).
        var namesInResults = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var races = new List<(SessionRecord Session, SessionResultEntry Row, int? Place)>();

        // Separate days they turned up, by race night
        // (TonightAtTheTrack.NightOf, 6am to 6am) - two heats and a practice
        // run on one evening is one day at the track.
        var daysOnTrack = new HashSet<DateTime>();

        // Laps with sector times: layout, when, sectors.
        var sectorLaps = new List<(string Layout, DateTime StartedUtc, int Lap, string Cut, long[] Sectors)>();

        // 6 Nov 2026, Bug hunt 11b review: a session saved with the bare
        // count before the cut was keyed on the tiles is read as the layout's
        // first placed cut (SectorCutHistory), so the upgrade does not throw
        // away every earlier night's sectors. Every driver's sessions, not
        // just theirs: the first placement is the layout's.
        var cuts = new SectorCutHistory(snapshot.Sessions);

        foreach (var session in snapshot.Sessions)
        {
            if (session.IsNotScored())
            {
                continue;
            }

            SessionResultEntry? row = null;

            foreach (var result in session.Results)
            {
                if (string.IsNullOrWhiteSpace(result.Transponder) || !mine.Contains(result.Transponder.Trim()))
                {
                    continue;
                }

                namesInResults.Add(string.IsNullOrWhiteSpace(result.DriverName) ? result.Transponder : result.DriverName);

                // The Personal Bests page's filter: a timed lap, a named
                // driver and a named layout (PersonalBestsControl.ComputeBests).
                if (result.FastLapMs > 0
                    && !string.IsNullOrWhiteSpace(result.DriverName)
                    && !string.IsNullOrWhiteSpace(session.TrackLayoutName))
                {
                    stats._lapCandidates.Add((session.TrackLayoutName, result.DriverName, result.FastLapMs, session.StartedAtUtc, result.CarModel));
                }

                // Their laps with sectors, by layout - the start lap
                // (index 0) is not a lap of the circuit.
                if (!string.IsNullOrWhiteSpace(session.TrackLayoutName) && result.SectorTimesMs is not null)
                {
                    for (var i = 1; i < result.LapTimesMs.Count; i++)
                    {
                        if (result.SectorsOf(i) is { } sectors)
                        {
                            sectorLaps.Add((session.TrackLayoutName.Trim(), session.StartedAtUtc, i, session.SectorCutOf(sectors, cuts), sectors));
                        }
                    }
                }

                // One row per driver per session. An older session can hold
                // two under one driver - their main and their alternate
                // transponder, from before alternates were filed together -
                // and the better placed is the one that stands, as the
                // championship does it.
                if (row is null || result.Position < row.Position)
                {
                    row = result;
                }
            }

            if (row is null)
            {
                continue;
            }

            if (session.StartedAtUtc != default)
            {
                // WHY (23 Oct 2026, bug hunt 10): this used the local calendar
                // date, so a club night whose A-main started after midnight
                // counted as two days at the track. It now groups by race
                // night (6am to 6am), the same as achievements and the coach
                // report, so one visit is one day.
                daysOnTrack.Add(TonightAtTheTrack.NightOf(session.StartedAtUtc.ToLocalTime()));

                if (stats.LastOnTrackUtc is null || session.StartedAtUtc > stats.LastOnTrackUtc)
                {
                    stats.LastOnTrackUtc = session.StartedAtUtc;
                }

                if (stats.FirstSeenUtc is null || session.StartedAtUtc < stats.FirstSeenUtc)
                {
                    stats.FirstSeenUtc = session.StartedAtUtc;
                }
            }

            if (session.SessionType == RaceSessionType.Race)
            {
                races.Add((session, row, PlaceAmongRealCars(session, row)));
            }
            else
            {
                stats.PracticeSessions++;
                stats.PracticeLaps += Math.Max(0, row.Laps);
            }
        }

        // --- Results ---
        //
        // Newest first, as history hands them over.
        races = races.OrderByDescending(r => r.Session.StartedAtUtc).ToList();

        var places = new List<int>();
        var ratings = new List<double>();

        foreach (var (session, row, place) in races)
        {
            stats.Starts++;
            stats.RaceLaps += Math.Max(0, row.Laps);
            stats.LapsLed += Math.Max(0, row.LapsLed);

            // The race's own rating for them - the same sum the stream's
            // podium shows at the flag, worked out from the saved result.
            if (DriverRating.ForSession(session).TryGetValue(row.Transponder, out var rating))
            {
                ratings.Add(rating);
            }

            if (session.StartedAtUtc != default && (stats.LastRacedUtc is null || session.StartedAtUtc > stats.LastRacedUtc))
            {
                stats.LastRacedUtc = session.StartedAtUtc;
            }

            // A lap or more is a finish - AchievementEvaluator's
            // MinimumLapsToCount. No laps is a DNF, which is what
            // ComputeStandings calls it. So is a result the race director
            // has marked DNF or DQ in Edit Result - see IsFinish.
            if (!IsFinish(row) || place is not { } p)
            {
                continue;
            }

            stats.Finishes++;
            places.Add(p);

            if (stats.BestFinish is null || p < stats.BestFinish)
            {
                stats.BestFinish = p;
            }

            if (p <= 5)
            {
                stats.TopFives++;
            }

            if (p == 1)
            {
                stats.Wins++;

                // Races are newest first here, so the first win this loop
                // meets is the latest one.
                if (session.StartedAtUtc != default && stats.LastWinUtc is null)
                {
                    stats.LastWinUtc = session.StartedAtUtc;
                }
            }
            else if (p == 2) { stats.Seconds++; }
            else if (p == 3) { stats.Thirds++; }
        }

        stats.AverageFinish = places.Count == 0 ? null : places.Average();
        stats.AverageRating = ratings.Count == 0 ? null : ratings.Average();
        stats.BestRating = ratings.Count == 0 ? null : ratings.Max();
        stats.DaysOnTrack = daysOnTrack.Count;

        stats.RecentForm = races
            .Take(FormLength)
            .Select(r => IsFinish(r.Row) && r.Place is { } p ? p : 0)
            .ToList();

        // --- Recent activity: races and setups (personal bests join later) ---
        foreach (var (session, row, place) in races.Take(ActivityLookBack))
        {
            if (session.StartedAtUtc == default)
            {
                continue;
            }

            // The cars that raced: AchievementEvaluator's field, a lap or
            // more. A hand-released grid files the whole roster at the green
            // and a DQ'd car is cut to no laps, so counting rows read "P1 of
            // 6" for a heat only two cars ran.
            var field = session.Results.Count(e => !GhostService.IsGhost(e.Transponder) && e.Laps >= 1);

            var outcome = !IsFinish(row) || place is not { } finished
                ? "did not finish"
                : $"P{finished} of {field}" + (row.FastLapMs > 0 ? $"  ·  best {LapText(row.FastLapMs)}s" : string.Empty);

            stats._activity.Add(new ActivityLine(session.StartedAtUtc, ActivityKind.Race, $"{RaceName(session, snapshot)}  ·  {outcome}"));
        }

        stats._activity.AddRange(SetupActivity(query.DriverName, snapshot));
        stats.RecentActivity = stats.MergeActivity(Array.Empty<ActivityLine>());

        // A saved race records its round, and the round its class - the
        // same lookup EventSpine.RuleAccepts uses to put a race in a
        // class's championship.
        stats.Classes = races
            .Select(r => !string.IsNullOrEmpty(r.Session.RoundId) && snapshot.RoundClasses.TryGetValue(r.Session.RoundId, out var cls) ? cls : null)
            .Where(cls => cls is not null)
            .Select(cls => cls!)
            .GroupBy(cls => cls, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.Key)
            .ToList();

        // --- Championships ---
        var standings = new List<ChampionshipLine>();

        foreach (var (season, table) in snapshot.Championships)
        {
            for (var i = 0; i < table.Count; i++)
            {
                if (!namesInResults.Contains(table[i].DriverName ?? string.Empty))
                {
                    continue;
                }

                // The first match is the best placed - the table is in
                // championship order.
                standings.Add(new ChampionshipLine(season, i + 1, table.Count, table[i].TotalPoints, table[i].Wins));
                break;
            }
        }

        stats.Championships = standings.OrderBy(s => s.Position).ThenBy(s => s.Season, StringComparer.CurrentCultureIgnoreCase).ToList();

        // --- Rival ---
        stats.Rival = TopRival(query.DriverName, snapshot);

        // --- Badges ---
        if (snapshot.Achievements is not null)
        {
            // Badges are filed under whichever transponder crossed the line,
            // so an alternate's are theirs too. The same badge under both is
            // one badge.
            var earned = snapshot.Achievements.All
                .Where(d => d.Transponder is { } t && mine.Contains(t.Trim()))
                .SelectMany(d => d.Unlocked)
                .GroupBy(u => u.Id, StringComparer.Ordinal)
                .Select(g => g.OrderBy(u => u.UnlockedUtc).First())
                .ToList();

            stats.BadgeCount = earned.Count;

            stats.Badges = earned
                .OrderByDescending(u => u.UnlockedUtc)
                .Select(u => AchievementCatalogue.Get(u.Id) is { } badge ? new BadgeLine(badge, u.UnlockedUtc, u.Detail ?? string.Empty) : null)
                .Where(b => b is not null)
                .Select(b => b!)
                .ToList();

            stats.LatestBadges = stats.Badges.Take(5).Select(b => b.Badge).ToList();
        }

        // --- Track records ---
        stats.RecordsHeld = RecordsHeldBy(mine, query.DriverName, namesInResults, snapshot);

        // Best sectors per layout, cut the way their latest laps there are:
        // the split loops can move between nights, and sectors from loops
        // in other places measure other stretches of track.
        stats.BestSectors = sectorLaps
            .GroupBy(l => l.Layout, StringComparer.OrdinalIgnoreCase)
            .Select(layout =>
            {
                // The latest lap: the newest session, and its last lap.
                var latest = layout.OrderByDescending(l => l.StartedUtc).ThenByDescending(l => l.Lap).First();
                var cut = latest.Sectors.Length;
                long[]? best = null;

                foreach (var (_, _, _, _, sectors) in layout.Where(l => l.Sectors.Length == cut && string.Equals(l.Cut, latest.Cut, StringComparison.Ordinal)))
                {
                    best ??= sectors.ToArray();

                    for (var i = 0; i < cut; i++)
                    {
                        best[i] = Math.Min(best[i], sectors[i]);
                    }
                }

                return (Layout: layout.Key, Newest: layout.Max(l => l.StartedUtc), Best: best!);
            })
            .OrderByDescending(l => l.Newest)
            .Select(l => new SectorBestLine(l.Layout, l.Best, l.Best.Sum()))
            .ToList();

        // Sector records announced under a name they race as.
        var asNames = new HashSet<string>(namesInResults, StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(query.DriverName))
        {
            asNames.Add(query.DriverName.Trim());
        }

        stats.SectorRecordsHeld = snapshot.SectorRecords
            .Where(r => asNames.Contains(r.Holder))
            .Select(r => new SectorRecordLine(r.Layout, r.Sector, r.Ms))
            .OrderBy(r => r.Layout, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Sector)
            .ToList();

        if (snapshot.Achievements is not null)
        {
            var held = stats.RecordsHeld.Select(r => r.Layout).ToHashSet(StringComparer.OrdinalIgnoreCase);

            stats.RecordsHeldBefore = snapshot.Achievements.All
                .Where(d => d.Transponder is { } t && mine.Contains(t.Trim()))
                .SelectMany(d => d.TrackRecords ?? new List<string>())
                .Where(layout => !string.IsNullOrWhiteSpace(layout) && !held.Contains(layout.Trim()))
                .Select(layout => layout.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(layout => layout, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        return stats;
    }

    /// <summary>
    /// Settles the personal bests, applying any reset made on the Personal
    /// Bests page. UI thread only: it reads the resets store.
    ///
    /// The Personal Bests page's rule (PersonalBestsControl.ComputeBests):
    /// a reset is looked up by the name saved with the result and the
    /// layout, and a lap set at or before it does not count. The best per
    /// layout is then simply the quickest lap left.
    /// </summary>
    public void ApplyPersonalBests(PersonalBestResetStore? resets)
    {
        var bests = new Dictionary<string, PersonalBestLine>(StringComparer.OrdinalIgnoreCase);

        foreach (var (layout, resultName, lapMs, startedUtc, car) in _lapCandidates)
        {
            var cutoff = resets?.EffectiveCutoff(resultName, layout);

            if (cutoff is not null && startedUtc <= cutoff)
            {
                continue;
            }

            if (!bests.TryGetValue(layout, out var existing) || lapMs < existing.LapMs)
            {
                bests[layout] = new PersonalBestLine(layout, lapMs, startedUtc, car);
            }
        }

        PersonalBests = bests.Values
            .OrderBy(b => b.Layout, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        BestLap = PersonalBests.OrderBy(b => b.LapMs).FirstOrDefault();

        RecentActivity = MergeActivity(PersonalBests
            .Where(b => b.SetUtc != default)
            .Select(b => new ActivityLine(b.SetUtc, ActivityKind.PersonalBest, $"Personal best  ·  {b.Layout}  ·  {LapText(b.LapMs)}s"))
            .ToList());
    }

    /// <summary>
    /// The races and setups with the personal bests, newest first, cut to
    /// <see cref="ActivityLength"/>. A personal best goes above the race it
    /// was set in: they share a time, and the sort keeps them in the order
    /// given.
    /// </summary>
    private IReadOnlyList<ActivityLine> MergeActivity(IReadOnlyList<ActivityLine> personalBests) =>
        personalBests
            .Concat(_activity)
            .OrderByDescending(line => line.WhenUtc)
            .Take(ActivityLength)
            .ToList();

    /// <summary>A lap time as the card shows one: seconds to the thousandth.</summary>
    private static string LapText(long ms) => (ms / 1000.0).ToString("0.000");

    /// <summary>
    /// A saved race's name for the card: its label without "[DEMO]" and the
    /// like, with the class after it when the label doesn't already say it -
    /// "Q2 Heat 1 (Stock Buggy)".
    /// </summary>
    private static string RaceName(SessionRecord session, DriverStatsSnapshot snapshot)
    {
        var label = System.Text.RegularExpressions.Regex.Replace(session.Label ?? string.Empty, @"\[[^\]]*\]", string.Empty).Trim();

        if (label.Length == 0)
        {
            label = "Race";
        }

        return !string.IsNullOrEmpty(session.RoundId)
               && snapshot.RoundClasses.TryGetValue(session.RoundId, out var cls)
               && !label.Contains(cls, StringComparison.OrdinalIgnoreCase)
            ? $"{label} ({cls})"
            : label;
    }

    /// <summary>
    /// The setups logged under this driver's name, newest first, each with
    /// what it changed from the one before it on the same car - the Garage's
    /// "Changed Since Last", in a line.
    /// </summary>
    private static IEnumerable<ActivityLine> SetupActivity(string driverName, DriverStatsSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            yield break;
        }

        ChassisTemplate? SheetFor(string? id) =>
            id is not null && snapshot.SetupSheets.TryGetValue(id, out var sheet) ? sheet : null;

        var theirs = snapshot.Setups
            .Where(e => e.RecordedAtUtc != default
                        && string.Equals((e.DriverName ?? string.Empty).Trim(), driverName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.RecordedAtUtc)
            .Take(ActivityLookBack);

        foreach (var entry in theirs)
        {
            var previous = SetupDiff.Previous(entry, snapshot.Setups);

            var what = previous is null ? "the first one logged"
                : previous.IsLegacy != entry.IsLegacy ? "the first on a setup sheet"
                : SetupDiff.Describe(SetupDiff.Between(previous, entry, SheetFor).Where(c => c.OnSheet).ToList(), max: 2);

            var car = (entry.CarModel ?? string.Empty).Trim();

            yield return new ActivityLine(
                entry.RecordedAtUtc,
                ActivityKind.Setup,
                (car.Length > 0 ? $"Setup on {car}" : "Setup") + $"  ·  {what}");
        }
    }

    /// <summary>What today looks like for this driver, from the live race-day stores. UI thread only.</summary>
    public static RaceDayLine? RaceDayFor(DriverProfile profile, DriverStatsSources? sources)
    {
        if (sources is null)
        {
            return null;
        }

        var transponder = profile.Transponder;

        bool? checkedIn = sources.CheckIns?.IsCheckedIn(transponder);

        string? tech = null;
        var techFailed = false;

        if (sources.TechInspections is not null)
        {
            // Latest, not Cleared: a failed inspection is the one thing a
            // race director most needs to see on this card.
            if (sources.TechInspections.Latest(transponder) is { } inspection)
            {
                tech = $"{inspection.ResultText} at {inspection.InspectedAtUtc.ToLocalTime():HH:mm}";
                techFailed = !inspection.IsCleared;
            }
            else
            {
                tech = "Not inspected yet";
            }
        }

        string? handicap = null;

        if (sources.Handicaps?.Get(transponder) is { HasAny: true } credit)
        {
            var parts = new List<string>();

            if (credit.LapCredit != 0)
            {
                parts.Add($"{(credit.LapCredit > 0 ? "+" : string.Empty)}{Plural.Of(credit.LapCredit, "lap")}");
            }

            if (credit.TimeCreditSeconds != 0)
            {
                parts.Add($"{credit.TimeCreditSeconds:0.0}s time credit");
            }

            handicap = string.Join(", ", parts);
        }

        // Only race days that still exist - a deleted day's sheet is kept
        // for its undo, not for counting (CheckInStore.EventsAttended).
        var eventsAttended = sources.CheckIns?.EventsAttended(transponder, sources.Events is null ? null : id => sources.Events.Get(id) is not null) ?? 0;

        // The setup log files entries by driver name.
        var name = profile.DriverName?.Trim() ?? string.Empty;

        var setupSheets = name.Length == 0 || sources.SetupLog is null
            ? 0
            : sources.SetupLog.All.Count(e => string.Equals(e.DriverName?.Trim(), name, StringComparison.OrdinalIgnoreCase));

        return new RaceDayLine(checkedIn, tech, techFailed, handicap, eventsAttended, setupSheets);
    }

    /// <summary>
    /// Whether this result counts as a finish on the driver card: a lap or
    /// more, and not marked DNF or DQ in Edit Result.
    ///
    /// The laps alone are not enough. ResultEditor keeps a DNF's laps (only a
    /// DQ is cut to none), and places it after every finisher, so a driver
    /// retired after 12 laps in a three-car race sits in P3 with laps on the
    /// board. Going by laps alone counted that as a finish, a podium and a
    /// top five, showed "P3" in the form strip and "P3 of 3" in the activity
    /// list, while the results page said DNF. The outcome the race director
    /// chose is the official word, so it wins here. An outcome that is none
    /// of the known ones (NormaliseOutcome gives null) is left as a finish,
    /// as it was before, rather than guessed at.
    /// </summary>
    private static bool IsFinish(SessionResultEntry row) =>
        row.Laps >= 1
        && ResultEditor.NormaliseOutcome(row.Outcome) is not (ResultEditor.Dnf or ResultEditor.Dq);

    /// <summary>
    /// Where this row finished among real cars only - AchievementEvaluator's
    /// PlaceInRace rule: ghosts taken out, the rest in stored order, places
    /// closed up behind whatever was removed.
    /// </summary>
    private static int? PlaceAmongRealCars(SessionRecord race, SessionResultEntry row)
    {
        var ranked = race.Results
            .Where(e => !GhostService.IsGhost(e.Transponder))
            .OrderBy(e => e.Position)
            .ToList();

        var index = ranked.IndexOf(row);

        return index < 0 ? null : index + 1;
    }

    /// <summary>
    /// The one other driver this driver's story is most about.
    ///
    /// A rivalry somebody has set up on the Rivalries page wins outright -
    /// that is the club saying who the rival is. Failing that, whoever they
    /// have shared the most sessions with. The record between them is the
    /// Head to Head page's (HeadToHeadControl.Compare): drivers matched by
    /// name, every shared non-demo session, practice included, and whoever
    /// finished higher takes it. Ghost cars are never a rival.
    /// </summary>
    /// <summary>
    /// The lap records that are this driver's now. A layout's record keeps
    /// only the holder's name as it was announced, so it is matched by the
    /// lap itself: one of their (non-demo) results on that layout has the
    /// record lap as its best, or among its laps. Should somebody else have the very same lap
    /// there too, the name announced decides - so a tie never makes two
    /// holders, and a driver renamed since still keeps a record nobody
    /// else could hold.
    /// </summary>
    private static List<TrackRecordLine> RecordsHeldBy(IReadOnlySet<string> mine, string driverName, IReadOnlySet<string> namesInResults, DriverStatsSnapshot snapshot)
    {
        var held = new List<TrackRecordLine>();

        foreach (var (layout, lapMs, holder, setUtc) in snapshot.TrackRecords)
        {
            var theirs = false;
            var others = false;

            foreach (var session in snapshot.Sessions)
            {
                if (session.IsNotScored() || !string.Equals(session.TrackLayoutName?.Trim(), layout, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var result in session.Results)
                {
                    // Among all their laps, not only their best (the badge's
                    // IsLiveTrackRecord rule): a phantom Race Control refused
                    // as a record, run later by the setter himself, still
                    // becomes his best lap. Matched on the best alone, the
                    // card left out a record he still holds, and listed it
                    // as one he held before.
                    if (result.FastLapMs != lapMs && !(result.LapTimesMs?.Contains(lapMs) ?? false))
                    {
                        continue;
                    }

                    if (mine.Contains(result.Transponder?.Trim() ?? string.Empty))
                    {
                        theirs = true;
                    }
                    else
                    {
                        others = true;
                    }
                }
            }

            if (!theirs)
            {
                continue;
            }

            var named = holder.Length > 0
                        && (string.Equals(holder, driverName, StringComparison.OrdinalIgnoreCase)
                            || namesInResults.Contains(holder)
                            || mine.Contains(holder));

            if (named || !others)
            {
                held.Add(new TrackRecordLine(layout, lapMs, setUtc));
            }
        }

        return held.OrderBy(r => r.LapMs).ThenBy(r => r.Layout, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static RivalLine? TopRival(string driverName, DriverStatsSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(driverName))
        {
            return null;
        }

        var tally = new Dictionary<string, (string Name, int Ahead, int Behind, DateTime Last)>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in snapshot.Sessions)
        {
            if (session.IsNotScored())
            {
                continue;
            }

            // Never a ghost standing in for them: a ghost can be given any
            // name, a real driver's included - the Head to Head page's rule.
            var me = session.Results.FirstOrDefault(r =>
                !GhostService.IsGhost(r.Transponder)
                && string.Equals(r.DriverName, driverName, StringComparison.OrdinalIgnoreCase));

            if (me is null)
            {
                continue;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var other in session.Results)
            {
                if (string.IsNullOrWhiteSpace(other.DriverName)
                    || GhostService.IsGhost(other.Transponder)
                    || string.Equals(other.DriverName, driverName, StringComparison.OrdinalIgnoreCase)
                    || !seen.Add(other.DriverName))
                {
                    continue;
                }

                var ahead = me.Position < other.Position;

                var current = tally.TryGetValue(other.DriverName, out var found)
                    ? found
                    : (Name: other.DriverName.Trim(), Ahead: 0, Behind: 0, Last: DateTime.MinValue);

                tally[other.DriverName] = (
                    current.Name,
                    current.Ahead + (ahead ? 1 : 0),
                    current.Behind + (ahead ? 0 : 1),
                    session.StartedAtUtc > current.Last ? session.StartedAtUtc : current.Last);
            }
        }

        var named = snapshot.Rivalries
            .Select(r => string.Equals(r.A, driverName, StringComparison.OrdinalIgnoreCase) ? r.B
                       : string.Equals(r.B, driverName, StringComparison.OrdinalIgnoreCase) ? r.A
                       : null)
            .Where(n => n is not null && !string.Equals(n, driverName, StringComparison.OrdinalIgnoreCase))
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (named.Count > 0)
        {
            var pick = named
                .Select(n => tally.TryGetValue(n, out var t) ? t : (Name: n, Ahead: 0, Behind: 0, Last: DateTime.MinValue))
                .OrderByDescending(t => t.Ahead + t.Behind)
                .ThenByDescending(t => t.Last)
                .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
                .First();

            return new RivalLine(pick.Name, pick.Ahead, pick.Behind, Named: true);
        }

        if (tally.Count == 0)
        {
            return null;
        }

        var most = tally.Values
            .OrderByDescending(t => t.Ahead + t.Behind)
            .ThenByDescending(t => t.Last)
            .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .First();

        return new RivalLine(most.Name, most.Ahead, most.Behind, Named: false);
    }
}
