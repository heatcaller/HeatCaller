using System.Text.Json;

namespace HeatCaller;

/// <summary>How a class applies handicaps.</summary>
public enum HandicapMode
{
    /// <summary>No handicapping - everyone scored on raw result.</summary>
    None,

    /// <summary>
    /// Seconds deducted from a driver's total time. Keeps everyone racing
    /// the same number of laps, which is easier to follow trackside than a
    /// staggered start.
    /// </summary>
    TimeCredit,

    /// <summary>
    /// Whole laps credited. Blunter than time, but some clubs prefer it
    /// because the leaderboard stays readable without doing sums.
    /// </summary>
    LapCredit,
}

/// <summary>One driver's handicap.</summary>
public sealed class DriverHandicap
{
    public required string Transponder { get; set; }

    /// <summary>Seconds credited. Used when the class is on TimeCredit.</summary>
    public double TimeCreditSeconds { get; set; }

    /// <summary>Whole laps credited. Used when the class is on LapCredit.</summary>
    public int LapCredit { get; set; }

    /// <summary>Set by hand rather than suggested. Auto-suggestion leaves these alone.</summary>
    public bool Locked { get; set; }

    public string Notes { get; set; } = string.Empty;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public bool HasAny => TimeCreditSeconds != 0 || LapCredit != 0;
}

/// <summary>
/// Per-driver handicaps for mixed-ability grids.
///
/// Deliberately stored and applied SEPARATELY from timing data. A handicap
/// is a scoring decision, not a measurement - baking it into recorded lap
/// times would corrupt the one thing in this app that has to stay true, and
/// would make personal bests and track records meaningless. Raw results are
/// recorded exactly as timed; the handicap is applied when standings are
/// presented.
/// </summary>
public sealed class HandicapStore
{
    private readonly List<DriverHandicap> _handicaps;

    /// <summary>
    /// Handicap mode per class, keyed by class name.
    ///
    /// Held here rather than on RaceClass so all handicap configuration
    /// lives in one place - the mode is meaningless without the per-driver
    /// credits it governs, and splitting them across two stores meant two
    /// files had to agree.
    /// </summary>
    private readonly Dictionary<string, HandicapMode> _classModes = new(StringComparer.OrdinalIgnoreCase);

    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "handicaps.json");

    private HandicapStore(List<DriverHandicap> handicaps, Dictionary<string, HandicapMode>? classModes = null)
    {
        _handicaps = handicaps;

        if (classModes is not null)
        {
            foreach (var (key, value) in classModes)
            {
                _classModes[key] = value;
            }
        }
    }

    /// <summary>Both the per-driver credits and the per-class modes, so one file holds all handicap state.</summary>
    private sealed class HandicapFile
    {
        public List<DriverHandicap> Handicaps { get; set; } = new();
        public Dictionary<string, HandicapMode> ClassModes { get; set; } = new();
    }

    /// <summary>How a class scores handicaps. None unless set.</summary>
    public HandicapMode ModeFor(string className) =>
        _classModes.TryGetValue(className, out var mode) ? mode : HandicapMode.None;

    public void SetMode(string className, HandicapMode mode)
    {
        if (mode == HandicapMode.None)
        {
            _classModes.Remove(className);
        }
        else
        {
            _classModes[className] = mode;
        }

        Save();
    }

    /// <summary>
    /// What recent renames overwrote, newest first, so renaming back - which
    /// is exactly what undoing a class rename does, through the same Renamed
    /// event with the names swapped - can put it back. Only renames that
    /// overwrote something are listed.
    ///
    /// A list rather than just the last one: with only the last remembered,
    /// any later rename forgot it, so undoing two renames in a row (or a
    /// rename with another made after it) lost the first overwritten mode.
    /// Capped at the undo depth, since nothing older can be undone anyway.
    /// </summary>
    private readonly List<(string From, string To, HandicapMode Displaced)> _renameDisplaced = new();

    /// <summary>
    /// Follows a class being renamed, so its handicap mode is not left
    /// behind under a name nothing uses any more (and the renamed class
    /// quietly scored with no handicap).
    ///
    /// The class mode is the only thing here keyed by class - driver credits
    /// are keyed by transponder. Lookup ignores case, as the dictionary
    /// does.
    ///
    /// The renamed class's own mode always goes with it, and replaces any
    /// mode already filed under the new name. The class list does not allow
    /// two classes with the same name, so a mode already under the new name
    /// can only be left over from a class that was deleted - it used to win,
    /// so the renamed class took on a dead class's handicap and lost its own.
    /// What was replaced is returned, and is also remembered so that renaming
    /// straight back (an undo) restores it. A class with no mode clears the
    /// new name for the same reason: the renamed class had no handicap, and
    /// a left-over one must not be handed to it.
    ///
    /// Returns the mode that was replaced under the new name, or null when
    /// there was none. No save when nothing changed.
    /// </summary>
    public HandicapMode? RenameClass(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return null;
        }

        // Only a rename that exactly reverses a remembered one is an undo of
        // it - the newest such, and it is used up here.
        var undoIndex = _renameDisplaced.FindIndex(r =>
            string.Equals(r.From, newName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(r.To, oldName, StringComparison.OrdinalIgnoreCase));

        var isUndo = undoIndex >= 0;
        var pendingDisplaced = HandicapMode.None;
        if (isUndo)
        {
            pendingDisplaced = _renameDisplaced[undoIndex].Displaced;
            _renameDisplaced.RemoveAt(undoIndex);
        }

        // A change of case only ("f1" to "F1") finds the same entry under
        // both names, so it is removed and put back under the new spelling
        // and nothing is displaced.
        var sameKey = string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase);

        var hasMode = _classModes.TryGetValue(oldName, out var mode);

        HandicapMode? displaced = null;
        if (!sameKey && _classModes.TryGetValue(newName, out var existing))
        {
            displaced = existing;
        }

        if (!hasMode && displaced is null)
        {
            // Nothing under either name. An undo can still have something to
            // put back (the class it displaced had a mode, the renamed one
            // did not) - handled below.
            if (!isUndo)
            {
                return null;
            }
        }

        _classModes.Remove(oldName);
        if (hasMode)
        {
            _classModes[newName] = mode;
        }
        else if (!sameKey)
        {
            _classModes.Remove(newName);
        }

        if (isUndo)
        {
            // Put back what the original rename overwrote, under the name
            // this undo has just vacated.
            _classModes[oldName] = pendingDisplaced;
        }
        else if (displaced is { } d)
        {
            _renameDisplaced.Insert(0, (oldName, newName, d));
            while (_renameDisplaced.Count > UndoService.MaxDepth)
            {
                _renameDisplaced.RemoveAt(_renameDisplaced.Count - 1);
            }
        }

        Save();
        return displaced;
    }

    /// <summary>
    /// Forgets a deleted class's handicap mode, so it cannot be picked up
    /// later by another class given the same name. Returns what it was
    /// (None when it had none) for the delete's undo to hand to
    /// RestoreClassMode. No save when there was nothing to forget.
    /// </summary>
    public HandicapMode RemoveClassMode(string className)
    {
        if (string.IsNullOrWhiteSpace(className) || !_classModes.TryGetValue(className, out var mode))
        {
            return HandicapMode.None;
        }

        _classModes.Remove(className);
        Save();
        return mode;
    }

    /// <summary>
    /// Puts a class's mode back exactly as it was - the undo half of
    /// RemoveClassMode and of a rename's returned mode. None or null
    /// removes the entry, as SetMode does.
    /// </summary>
    public void RestoreClassMode(string className, HandicapMode? mode)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return;
        }

        SetMode(className, mode ?? HandicapMode.None);
    }

    public static HandicapStore Load()
    {
        var unreadable = false;

        try
        {
            if (File.Exists(FilePath))
            {
                var text = SafeFile.ReadAllTextWithRetry(FilePath);

                // Earlier builds wrote a bare array - read it rather than
                // silently discarding someone's handicaps. Told apart by the
                // first character rather than by trying one format and then
                // the other: an array handed to the object reader throws,
                // and the throw used to skip straight past this fallback to
                // "unreadable", so the old format was never actually read.
                var first = text.TrimStart().FirstOrDefault();
                if (first == '[')
                {
                    var legacy = JsonSerializer.Deserialize<List<DriverHandicap>>(text);
                    if (legacy is not null)
                    {
                        return new HandicapStore(legacy);
                    }
                }
                else
                {
                    // Current format holds both credits and class modes.
                    var file = JsonSerializer.Deserialize<HandicapFile>(text);
                    if (file is not null)
                    {
                        // A "null" list in the file would otherwise sit in
                        // the store and throw on first use.
                        return new HandicapStore(file.Handicaps ?? new List<DriverHandicap>(), file.ClassModes);
                    }
                }

                // Present, parsed, and neither shape this store writes. Not
                // a new install either, so the same rule applies. A file
                // holding the literal "null" parses to nothing without
                // throwing - in either branch above - and used to fall
                // straight through to the empty store below with the flag
                // left false, so the first handicap set tonight replaced
                // every driver's credits and every class mode.
                unreadable = true;
            }
        }
        catch (Exception ex)
        {
            // Unreadable file: start empty rather than refusing to open -
            // but, when it could not be read at all or no copy of it could
            // be kept, never save over it this session (see LoadedUnreadable).
            unreadable = SafeFile.HoldWritesAfterUnreadable(FilePath, ex);
        }

        return new HandicapStore(new List<DriverHandicap>()) { LoadedUnreadable = unreadable };
    }

    /// <summary>
    /// True when the file was there at startup but could not be read - see
    /// SafeFile.HoldWritesAfterUnreadable. The file holds the club's
    /// per-driver handicaps and class modes: nothing anybody could type back in. It
    /// used to be loaded as empty and then written over whole at the first
    /// change, so a backup or antivirus tool holding it open for a moment
    /// at launch was enough to lose all of it. What this store holds is
    /// then only what was added this session, and none of it is saved.
    /// </summary>
    public bool LoadedUnreadable { get; private init; }

    public void Save()
    {
        // Not written: the real file is almost certainly fine, and saving
        // this session's near-empty list would replace it. The console
        // has already told the operator why and asked for a restart.
        if (LoadedUnreadable)
        {
            return;
        }

        var file = new HandicapFile
        {
            Handicaps = _handicaps,
            ClassModes = new Dictionary<string, HandicapMode>(_classModes, StringComparer.OrdinalIgnoreCase),
        };

        SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
    }

    public IReadOnlyList<DriverHandicap> All => _handicaps.ToList();

    public DriverHandicap? Get(string transponder) =>
        _handicaps.FirstOrDefault(h => string.Equals(h.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

    public double TimeCreditMsFor(string transponder) =>
        (Get(transponder)?.TimeCreditSeconds ?? 0) * 1000.0;

    public int LapCreditFor(string transponder) => Get(transponder)?.LapCredit ?? 0;

    public void Upsert(DriverHandicap handicap)
    {
        handicap.UpdatedUtc = DateTime.UtcNow;

        var index = _handicaps.FindIndex(h =>
            string.Equals(h.Transponder, handicap.Transponder, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            _handicaps[index] = handicap;
        }
        else
        {
            _handicaps.Add(handicap);
        }

        Save();
    }

    public void Delete(string transponder)
    {
        _handicaps.RemoveAll(h => string.Equals(h.Transponder, transponder, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    // --- Undo -------------------------------------------------------------
    //
    // Per driver: an undo puts back only the drivers its action touched.
    // The whole-list Snapshot/Restore below also wiped out every handicap
    // set for anyone else since, when an older step was undone.

    /// <summary>A detached copy of a handicap - edits to the live one (the edit dialog writes onto what it is given) don't reach it.</summary>
    public static DriverHandicap CopyOf(DriverHandicap handicap) =>
        JsonSerializer.Deserialize<DriverHandicap>(JsonSerializer.Serialize(handicap))!;

    /// <summary>What these drivers' handicaps are now (null for none), for PutBack to restore.</summary>
    public IReadOnlyList<(string Transponder, DriverHandicap? Before)> CopiesFor(IEnumerable<string> transponders) =>
        transponders
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => (t, Get(t) is { } handicap ? CopyOf(handicap) : null))
            .ToList();

    /// <summary>
    /// The undo half of any per-driver change: each driver's handicap goes
    /// back to what CopiesFor saw - or is removed, when they had none. The
    /// old UpdatedUtc comes back with it. Everyone else is left alone.
    /// </summary>
    public void PutBack(IReadOnlyList<(string Transponder, DriverHandicap? Before)> before)
    {
        foreach (var (transponder, handicap) in before)
        {
            var index = _handicaps.FindIndex(h => string.Equals(h.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

            if (handicap is null)
            {
                if (index >= 0)
                {
                    _handicaps.RemoveAt(index);
                }
            }
            else if (index >= 0)
            {
                _handicaps[index] = CopyOf(handicap);
            }
            else
            {
                _handicaps.Add(CopyOf(handicap));
            }
        }

        Save();
    }

    public string Snapshot() => JsonSerializer.Serialize(_handicaps);

    public void Restore(string snapshot)
    {
        try
        {
            var restored = JsonSerializer.Deserialize<List<DriverHandicap>>(snapshot);
            if (restored is null)
            {
                return;
            }

            _handicaps.Clear();
            _handicaps.AddRange(restored);
            Save();
        }
        catch
        {
            // A failed restore leaves the current state alone.
        }
    }

    public sealed record Suggestion(
        string Transponder,
        string DriverName,
        double CurrentSeconds,
        double SuggestedSeconds,
        int SessionsUsed,
        double AverageBestLapSeconds);

    /// <summary>
    /// The saved sessions a class's handicaps are worked out from, newest
    /// first as the history keeps them. Demo races never count.
    ///
    /// 17 Oct 2026, Bug Hunt 8, A26-1: with a class and the race programme,
    /// only races whose round belongs to that class. Every class, every
    /// session type was used, so a driver who also ran a faster class had
    /// those laps averaged in and was handed the wrong credit. A session
    /// carries no class of its own, so it is found through its round. With
    /// either left out, every non-demo session is used, as it always was.
    /// </summary>
    internal static IEnumerable<SessionRecord> SessionsFor(SessionHistoryStore history, RoundDefinitionStore? rounds, string? className)
    {
        var sessions = history.All.Where(s => !s.IsNotScored());

        if (rounds is null || string.IsNullOrWhiteSpace(className))
        {
            return sessions;
        }

        // A race with no round - a Quick Race - is kept (22 Oct 2026, bug
        // hunt): it carries no class to rule it out, and dropping every one
        // left a club that runs its handicap class without the programme
        // with no recent pace and no suggestions at all. Only a round that
        // belongs to ANOTHER class is left out.
        return sessions.Where(s =>
            s.SessionType == RaceSessionType.Race
            && (s.RoundId is not { } id
                || string.Equals(rounds.Get(id)?.ClassName, className, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Suggests time credits from recent pace.
    ///
    /// The reference is the FASTEST driver in the group, and everyone else
    /// is credited the per-lap gap to them multiplied by a typical race
    /// distance. That makes the target "everyone finishes together", which
    /// is what a handicap is for.
    ///
    /// Suggestions only - never applied automatically. A handicap decides
    /// who wins, so it should be a person's decision, and locked drivers are
    /// left alone entirely.
    ///
    /// 17 Oct 2026, Bug Hunt 8, A26-1: given the class and the race
    /// programme, only that class's races are used - see SessionsFor.
    /// </summary>
    public List<Suggestion> Suggest(
        IEnumerable<DriverProfile> drivers,
        SessionHistoryStore history,
        int recentSessions,
        int typicalLaps,
        string? className = null,
        RoundDefinitionStore? rounds = null)
    {
        var sessions = SessionsFor(history, rounds, className).ToList();

        var pace = new Dictionary<string, (string Name, double AvgBest, int Count)>(StringComparer.OrdinalIgnoreCase);

        foreach (var driver in drivers)
        {
            // A driver's best lap from each of their most recent sessions,
            // averaged. One freak lap shouldn't set someone's handicap for
            // the season, and one bad race shouldn't either. Saved demo
            // races are skipped - invented lap times must never decide a
            // real driver's handicap.
            var bests = sessions
                .SelectMany(s => s.Results.Where(r =>
                    string.Equals(r.Transponder, driver.Transponder, StringComparison.OrdinalIgnoreCase)
                    && r.FastLapMs > 0))
                .Take(Math.Max(1, recentSessions))
                .Select(r => r.FastLapMs / 1000.0)
                .ToList();

            if (bests.Count > 0)
            {
                pace[driver.Transponder] = (driver.DriverName, bests.Average(), bests.Count);
            }
        }

        if (pace.Count < 2)
        {
            return new List<Suggestion>();
        }

        var quickest = pace.Values.Min(p => p.AvgBest);
        var laps = Math.Max(1, typicalLaps);

        return pace.Select(kv =>
        {
            var gapPerLap = kv.Value.AvgBest - quickest;
            var suggested = Math.Round(gapPerLap * laps, 1);

            return new Suggestion(
                kv.Key,
                kv.Value.Name,
                Get(kv.Key)?.TimeCreditSeconds ?? 0,
                suggested,
                kv.Value.Count,
                Math.Round(kv.Value.AvgBest, 3));
        })
        .OrderByDescending(s => s.SuggestedSeconds)
        .ToList();
    }
}

/// <summary>
/// Applies handicaps to a finishing order without touching the underlying
/// results.
/// </summary>
public static class HandicapCalculator
{
    public sealed record Adjusted(
        SessionResultEntry Result,
        int RawPosition,
        int AdjustedPosition,
        double CreditSeconds,
        int CreditLaps)
    {
        public bool Moved => RawPosition != AdjustedPosition;
    }

    /// <summary>
    /// Re-orders a session's results with handicaps applied. The raw
    /// position is kept alongside so both can be shown - hiding what
    /// actually happened would make the result impossible to check.
    /// </summary>
    public static List<Adjusted> Apply(
        IReadOnlyList<SessionResultEntry> results,
        HandicapStore handicaps,
        HandicapMode mode) =>
        Apply(results, mode, handicaps.TimeCreditMsFor, handicaps.LapCreditFor);

    /// <summary>
    /// The same, on the credits kept with the race when it was filed (see
    /// <see cref="SessionHandicap"/>) rather than on today's.
    /// </summary>
    public static List<Adjusted> Apply(IReadOnlyList<SessionResultEntry> results, SessionHandicap kept) =>
        Apply(results, kept.Mode,
            t => (kept.For(t)?.TimeCreditSeconds ?? 0) * 1000.0,
            t => kept.For(t)?.LapCredit ?? 0);

    /// <summary>
    /// A saved race's handicap mode and order, for a race of
    /// <paramref name="className"/> (null for a race with no round): what was
    /// kept with the race when it was filed, so later changes to the credits
    /// or the class's mode do not re-rank it (9 Nov 2026, Bug Hunt 15, B52).
    /// Today's handicaps only for a race saved before that was kept, or since
    /// linked to a round of another class. Adjusted is null for no handicap.
    /// </summary>
    public static (HandicapMode Mode, List<Adjusted>? Adjusted) ForSession(
        SessionRecord session, string? className, HandicapStore? live)
    {
        if (className is null)
        {
            return (HandicapMode.None, null);
        }

        if (session.Handicap is { } kept && string.Equals(kept.ClassName, className, StringComparison.OrdinalIgnoreCase))
        {
            return kept.Mode == HandicapMode.None ? (HandicapMode.None, null) : (kept.Mode, Apply(session.Results, kept));
        }

        var mode = live?.ModeFor(className) ?? HandicapMode.None;
        return mode == HandicapMode.None ? (HandicapMode.None, null) : (mode, Apply(session.Results, live!, mode));
    }

    /// <summary>
    /// What to keep with a race filed against a round of
    /// <paramref name="className"/>: the class's mode now, and the credit of
    /// each driver in it who has one. See <see cref="SessionHandicap"/>.
    /// </summary>
    public static SessionHandicap Snapshot(IEnumerable<SessionResultEntry> results, HandicapStore handicaps, string className) => new()
    {
        ClassName = className,
        Mode = handicaps.ModeFor(className),
        Credits = results
            .Select(r => handicaps.Get(r.Transponder))
            .Where(h => h is { HasAny: true })
            .GroupBy(h => h!.Transponder, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SessionHandicapCredit
            {
                Transponder = g.First()!.Transponder,
                TimeCreditSeconds = g.First()!.TimeCreditSeconds,
                LapCredit = g.First()!.LapCredit,
            })
            .ToList(),
    };

    private static List<Adjusted> Apply(
        IReadOnlyList<SessionResultEntry> results,
        HandicapMode mode,
        Func<string, double> timeCreditMsFor,
        Func<string, int> lapCreditFor)
    {
        var raw = results.OrderBy(r => r.Position).ToList();

        if (mode == HandicapMode.None)
        {
            return raw.Select((r, i) => new Adjusted(r, i + 1, i + 1, 0, 0)).ToList();
        }

        if (mode == HandicapMode.TimeCredit)
        {
            return ApplyTimeCredit(raw, timeCreditMsFor);
        }

        var scored = raw.Select((r, i) =>
        {
            var creditLaps = mode == HandicapMode.LapCredit
                ? lapCreditFor(r.Transponder)
                : 0;

            return new
            {
                Result = r,
                RawPosition = i + 1,
                CreditLaps = creditLaps,
                EffectiveLaps = r.Laps + creditLaps,
                EffectiveMs = r.ElapsedMs,
            };
        })
        // Finishers first, then DNFs, then DQs - the grouping the result
        // editor gave them - and only within a group the same rule as an
        // unhandicapped result: most laps, then lowest time. A DNF keeps
        // the laps it drove, so ranking on laps alone let a driver who
        // retired a lap down with two laps of credit be scored ahead of
        // everyone who finished, and the event report named them the class
        // winner. A credit makes up for pace; it cannot finish a race for
        // somebody who did not.
        //
        // For the same reason a car that never turned a lap stays behind
        // every car in its group that did, as the time-credit ranking below
        // already keeps it. A grid car that never crossed the line lands in
        // the DNF group, and with its lap credit added it would otherwise
        // count as having covered that many laps in no time at all - level
        // on laps with a DNF who drove them, and ahead of them on the 0 ms
        // time. The result editor ranks the DNF who drove laps first, and
        // the handicapped order must agree with it.
        .OrderBy(x => OutcomeGroup(x.Result))
        .ThenByDescending(x => x.Result.Laps > 0)
        .ThenByDescending(x => x.EffectiveLaps)
        .ThenBy(x => x.EffectiveMs)
        .ToList();

        return scored.Select((x, i) => new Adjusted(
            x.Result, x.RawPosition, i + 1, 0, x.CreditLaps)).ToList();
    }

    /// <summary>
    /// Time credits, ranked on how long each driver would have taken to
    /// cover the WINNER'S distance, less their credit.
    ///
    /// The credit used to come off elapsed time after the field had been
    /// sorted on laps, so it could only ever break a tie between drivers on
    /// the same lap. But Suggest sets a credit as the driver's gap to the
    /// quickest over a whole race - on a timed race that is the laps they
    /// are down, not seconds on the same lap. A driver 30s slower over 30
    /// laps finishes three laps down on the clock, and a credit of 30s
    /// could never make up a single one of them: the handicap did nothing.
    ///
    /// So each driver's time is scaled up to the leader's lap count at
    /// their own average pace - 297s for 27 laps becomes 330s for 30 - and
    /// the credit comes off that. The same 30s credit then brings them back
    /// level, which is what Suggest aims for. When everyone is on the same
    /// lap the scaling does nothing and this is the old rule exactly.
    ///
    /// A time penalty is not scaled with the laps - it is a fixed sum, not
    /// pace - whenever the lap list shows how much of the elapsed time it
    /// was. Drivers with no laps have nothing to scale and stay at the back
    /// in their raw order.
    ///
    /// A pursuit (handicap-stagger) start does not come through here: its
    /// credits were spent as the head start on track.
    /// </summary>
    private static List<Adjusted> ApplyTimeCredit(List<SessionResultEntry> raw, Func<string, double> timeCreditMsFor)
    {
        // 6 Nov 2026, Bug hunt 11: the winner's distance and the end of the
        // racing come from the finishers only - the same group-0 drivers who
        // are the only ones ever projected below. They used to be taken over
        // every entry, so a car disqualified after a lap more than the winner
        // re-scaled every finisher to a distance nobody who counts drove, and
        // could change who won on handicap; and a DQ'd car timed after the
        // flag pushed the end of the racing out, so finishers who were still
        // running no longer counted as running. A DNF stops early and a DQ
        // does not count, so neither may set the yardstick. With no finisher
        // nobody is projected and the figures are not used.
        var finishers = raw.Where(r => OutcomeGroup(r) == 0).ToList();
        var leaderLaps = finishers.Count > 0 ? finishers.Max(r => r.Laps) : 0;

        var measured = raw.Select((r, i) =>
        {
            // Only separable when every counted lap has its time - laps
            // added by hand have none, and then the whole figure is scaled.
            var penaltyMs = r.Laps > 0 && r.LapTimesMs is { } laps && laps.Count == r.Laps
                ? Math.Max(0, r.ElapsedMs - laps.Sum())
                : 0;

            return (Result: r, RawPosition: i + 1, PenaltyMs: penaltyMs, DrivenMs: r.ElapsedMs - penaltyMs);
        }).ToList();

        // How long the racing lasted - the latest any finisher was still timed
        // (see above: a DNF or DQ does not set it).
        var finishersMeasured = measured.Where(m => OutcomeGroup(m.Result) == 0).ToList();
        var raceSpanMs = finishersMeasured.Count > 0 ? finishersMeasured.Max(m => m.DrivenMs) : 0;

        var scored = measured.Select(m =>
        {
            var r = m.Result;
            var creditMs = (long)timeCreditMsFor(r.Transponder);
            var hasLaps = r.Laps > 0;

            // Projecting to the leader's distance at the driver's own pace
            // is only fair for somebody who was still running at the end.
            // A driver who retired after five laps at a good pace would
            // otherwise be credited with the other twenty-five they never
            // drove - and, with a credit on top, handed the win. Still
            // running means timed within a couple of their own laps of the
            // end of the racing; anybody else is ranked behind every
            // finisher, on distance and then time as before.
            //
            // A driver marked DNF or DQ was not, whatever the clock says: a
            // DNF on the last lap is timed close enough to the end to pass
            // the test below, and would be projected to the full distance
            // and credited as if they had finished it.
            var averageLapMs = hasLaps ? m.DrivenMs / (double)r.Laps : 0;
            var runningAtEnd = hasLaps
                && OutcomeGroup(r) == 0
                && m.DrivenMs + (2 * averageLapMs) >= raceSpanMs;

            var effectiveMs = runningAtEnd
                ? (long)Math.Round(m.DrivenMs * (double)leaderLaps / r.Laps) + m.PenaltyMs - creditMs
                : r.ElapsedMs - creditMs;

            return new
            {
                Result = r,
                m.RawPosition,
                Group = OutcomeGroup(r),
                CreditSeconds = creditMs / 1000.0,
                HasLaps = hasLaps,
                RunningAtEnd = runningAtEnd,
                EffectiveMs = effectiveMs,
            };
        })
        // Finishers, then DNFs, then DQs, as the result editor grouped them
        // - a credit re-ranks drivers within a group, never across one.
        .OrderBy(x => x.Group)
        .ThenByDescending(x => x.HasLaps)
        .ThenByDescending(x => x.RunningAtEnd)

        // Retired drivers keep the old order among themselves: distance first.
        .ThenByDescending(x => x.RunningAtEnd ? 0 : x.Result.Laps)
        .ThenBy(x => x.HasLaps ? x.EffectiveMs : 0)

        // A dead heat goes to whoever actually covered more ground, then
        // to the order they finished in.
        .ThenByDescending(x => x.Result.Laps)
        .ThenBy(x => x.RawPosition)
        .ToList();

        return scored.Select((x, i) => new Adjusted(
            x.Result, x.RawPosition, i + 1, x.CreditSeconds, 0)).ToList();
    }

    /// <summary>
    /// 0 for a finisher, 1 for a DNF or a car that never turned a lap, 2 for
    /// a DQ - the same groups, in the same order, that ResultEditor puts a
    /// corrected result in (see ResultEditor.Group). A handicap only moves
    /// drivers within their group.
    /// </summary>
    private static int OutcomeGroup(SessionResultEntry entry) =>
        entry.Outcome == ResultEditor.Dq ? 2
        : entry.Outcome == ResultEditor.Dnf ? 1
        : entry.Laps <= 0 ? 1
        : 0;
}
