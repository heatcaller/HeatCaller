namespace HeatCaller;

public enum RaceSessionType
{
    Practice,
    Race,
}

/// <summary>A single racer's final result within a saved session.</summary>
public sealed class SessionResultEntry
{
    public int Position { get; set; }
    public string Transponder { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;
    public string CarModel { get; set; } = string.Empty;
    public int Laps { get; set; }
    public long FastLapMs { get; set; }
    public long ElapsedMs { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Every counted lap's duration, in order. Empty for sessions saved
    /// before lap-by-lap recording existed - the Lap Times chart treats
    /// those as simply having no chart data rather than erroring.
    /// </summary>
    public List<long> LapTimesMs { get; set; } = new();

    /// <summary>
    /// Each lap's sector times, in step with LapTimesMs - null for a lap
    /// with none (see RacerStanding.SectorsAtLap). Null for a session run
    /// without a split loop, or saved before splits existed. Read through
    /// <see cref="SectorsOf"/>, which only gives sectors that still add up
    /// to the lap - a result corrected since shows none for laps it changed.
    /// </summary>
    public List<long[]?>? SectorTimesMs { get; set; }

    /// <summary>This lap's sectors, when it has some that still add up to it.</summary>
    public long[]? SectorsOf(int lapIndex) =>
        SectorTimesMs is { } all && lapIndex >= 0 && lapIndex < all.Count && lapIndex < LapTimesMs.Count
            ? Sectors.Checked(all[lapIndex], LapTimesMs[lapIndex])
            : null;

    /// <summary>What to save for a standing's sectors: its list when any lap has some, otherwise null - so a session without a split loop saves nothing extra.</summary>
    public static List<long[]?>? SectorsToSave(RacerStanding standing) =>
        standing.SectorsAtLap.Any(sectors => sectors is not null)
            ? standing.SectorsAtLap.Take(standing.LapTimesMs.Count).Select(sectors => sectors?.ToArray()).ToList()
            : null;

    /// <summary>
    /// Passes rejected as too soon to be real laps.
    ///
    /// Zero on sessions saved before this was recorded, which is
    /// indistinguishable from a genuinely clean race - so anything judging
    /// cleanliness has to require some other evidence the race was
    /// recorded by a version that counted them. See RecordsIncidents.
    /// </summary>
    public int RejectedLaps { get; set; }

    /// <summary>
    /// How many of this driver's laps they completed while leading.
    ///
    /// Counted at each crossing rather than derived afterwards: the running
    /// order between crossings isn't stored, so "were they ahead at the
    /// moment they finished that lap" is the only question a saved result
    /// can answer honestly.
    /// </summary>
    public int LapsLed { get; set; }

    /// <summary>
    /// Where they started, 1-based. Zero when the grid wasn't known - no
    /// round selected and no previous race to take an order from.
    /// </summary>
    public int StartPosition { get; set; }

    /// <summary>Warnings race control gave this driver during the race, oldest first. Empty on results saved before warnings existed.</summary>
    public List<RaceWarning> Warnings { get; set; } = new();

    /// <summary>
    /// Penalty race control gave during the race (jump start, penalty
    /// buttons, warning time), already included in ElapsedMs. Zero on
    /// results saved before this existed. The result editor never changes it.
    /// </summary>
    /// <remarks>
    /// 20 Oct 2026: kept on its own so best-consecutive-laps qualifying,
    /// which works from lap times rather than ElapsedMs, can still charge
    /// the penalty - before this a penalized driver could take pole.
    /// </remarks>
    public long RacePenaltyMs { get; set; }

    /// <summary>
    /// Laps in this count that race control added by hand during the race
    /// (+1 Lap) - a transponder that stopped being read, kept going by a
    /// person. Already included in Laps. Zero on results saved before this
    /// was recorded (22 Oct 2026). Laps added afterwards in Edit Result are
    /// AddedLaps; HandCountedLaps.In adds the two together.
    /// </summary>
    public int LapsByHand { get; set; }

    /// <summary>
    /// Race control marked this driver as jumping the start (22 Oct 2026,
    /// driver experience). Set when the result is filed, from the race's
    /// own jump-start record, so a driver can later be told that their
    /// added seconds were for a jump start rather than just "a penalty"
    /// (PenaltyWords). False on every result saved before this date.
    /// </summary>
    public bool JumpStarted { get; set; }

    /// <summary>
    /// This driver was past the line at the green and was credited with the
    /// start lap (bug hunt 17) - see RacerStanding.StartLapCredited. Their
    /// LapTimesMs then begins with a zero, and the entry after it is the
    /// rest of the start: timed from their crossing before the green, not a
    /// flying lap. The zero alone does not say so - a qualifier's start slot
    /// is zero too, and the lap after it is a real one - so it is saved.
    /// False on every result saved before this.
    /// </summary>
    public bool StartLapCredited { get; set; }

    /// <summary>
    /// How many entries at the front of <paramref name="lapTimesMs"/> (this
    /// result's laps, as saved or as raced) are the start and not flying
    /// laps: one, or two for a driver credited with the start lap.
    /// </summary>
    public int StartSlots(IReadOnlyList<long> lapTimesMs) =>
        StartLapCredited && lapTimesMs.Count > 1 && lapTimesMs[0] == 0 ? 2 : 1;

    /// <summary>
    /// Where this car's own clock started, in ms after the green (bug hunt
    /// 17): its first crossing in a qualifying start, its release in a
    /// staggered start on "own release", and zero when every car is timed
    /// from the green. LapTimesMs run from here, so two cars' laps are only
    /// on one clock once each has this added - RaceLines' "held off" needs
    /// that. Zero on every result saved before this was recorded.
    /// </summary>
    public long ClockZeroMs { get; set; }

    // --- Corrections made after the race was saved ---
    //
    // All four are what the result editor has decided for this driver, as
    // totals rather than as a history of presses - so editing a result a
    // second time starts from what it says now, and setting everything back
    // to zero really does give the original back. Zero / empty on every
    // result saved before corrections existed, which is exactly "never
    // corrected". See ResultEditor for how they are applied.

    /// <summary>Laps added (positive) or taken away (negative) by hand after the race - a missed or a phantom crossing.</summary>
    public int AddedLaps { get; set; }

    /// <summary>A time penalty given after the race, added to the total time. Milliseconds.</summary>
    public long TimePenaltyMs { get; set; }

    /// <summary>Laps taken off as a penalty after the race. Counted separately from AddedLaps so the result says WHY it is shorter.</summary>
    public int LapPenalty { get; set; }

    /// <summary>
    /// "DNF", "DQ", or empty for an ordinary finisher. A DNF is placed after
    /// every finisher; a DQ after that, with no laps counted. See
    /// ResultEditor.Dnf and ResultEditor.Dq.
    /// </summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>
    /// The class this driver ran in, written ONLY for a race from a shared
    /// heat (11 Nov 2026, race-night build plan, Phase 4.1: mixed-class
    /// heats) - see MixedHeats.StampClasses. Null on every other result,
    /// which is every result saved before this: the race's class is its
    /// round's, as it always was. Stamped at the flag so the championship,
    /// which only has the saved results, can score each class of a shared
    /// race apart, and so a later change to the heat cannot rewrite who a
    /// past result was scored for.
    /// </summary>
    public string? ClassName { get; set; }

    /// <summary>
    /// The result exactly as the race recorded it, kept the first time it
    /// is corrected so every later correction is worked out from the real
    /// numbers rather than from an earlier correction. Null on a result
    /// that has never been corrected.
    /// </summary>
    public ResultAsRaced? AsRaced { get; set; }

    /// <summary>True when anything about this result was changed after the race.</summary>
    public bool IsCorrected() =>
        AddedLaps != 0 || TimePenaltyMs != 0 || LapPenalty != 0 || !string.IsNullOrEmpty(Outcome);
}

/// <summary>A result's figures as the race itself recorded them, before any correction. See SessionResultEntry.AsRaced.</summary>
public sealed class ResultAsRaced
{
    public int Position { get; set; }
    public int Laps { get; set; }
    public long ElapsedMs { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// The lap-by-lap times as the race recorded them.
    ///
    /// Kept because taking a phantom crossing off a saved result has to
    /// take its TIME with it - the same rule the race itself follows in
    /// RaceControlSession.AdjustLaps - and because a second correction has
    /// to work from the real list rather than from the last one.
    ///
    /// Empty on a result corrected by a version that did not keep it. That
    /// version never changed the entry's lap times either, so what is on
    /// the entry then IS the raced list, and ResultEditor.ApplyTo takes it
    /// across once before touching anything.
    /// </summary>
    public List<long> LapTimesMs { get; set; } = new();

    /// <summary>
    /// The best lap as the race recorded it - which already left out the
    /// start lap and any lap run through a crash recovery, neither of which
    /// a saved result carries. Zero on an older correction, and backfilled
    /// the same way as LapTimesMs.
    /// </summary>
    public long FastLapMs { get; set; }
}

/// <summary>
/// One line of a saved result's correction log: who changed what, when,
/// why, and from where. Kept on the session so a corrected result can
/// always be explained - to a driver asking why they dropped a place, or
/// to the director a week later.
/// </summary>
public sealed class ResultEdit
{
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public string Transponder { get; set; } = string.Empty;
    public string DriverName { get; set; } = string.Empty;

    /// <summary>What changed, in plain words - "time penalty 0s -> 5s".</summary>
    public string Change { get; set; } = string.Empty;

    /// <summary>Why, as the person making the correction gave it. Always filled - the editor refuses a correction without one.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>"Laptop" or "Phone".</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// A recorded practice or race session: when it happened, at which track
/// layout, an optional free-text label (since we have no programmatic way
/// to read Next Level Timing's own "Race Format" selection via the Neon
/// Timing API), and the final standings snapshot when the session ended.
/// </summary>
public sealed class SessionRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public RaceSessionType SessionType { get; set; } = RaceSessionType.Practice;
    public string TrackLayoutName { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? RoundId { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime EndedAtUtc { get; set; }

    /// <summary>
    /// When the race was first called to the grid - staged, or counted down
    /// from rest - in UTC (11 Nov 2026, night pace). Null when it went
    /// straight to green, and on every race saved before this existed. The
    /// night-pace report reads the wait at the grid from it (NightPace).
    /// </summary>
    public DateTime? StagedAtUtc { get; set; }
    public List<SessionResultEntry> Results { get; set; } = new();

    /// <summary>
    /// Drivers scored DNS - did not start - in this race (12 Nov 2026, setup
    /// plan step 8): skipped from its grid by the desk, or by their own
    /// approved "Skip me", by transponder. They have no row in Results (they
    /// never ran, so nothing reads a lap of theirs); the result, the event
    /// report and points qualifying say "DNS" for them. Null for none.
    /// </summary>
    public List<string>? DidNotStart { get; set; }

    /// <summary>
    /// Whether this session was recorded by a version that counts rejected
    /// laps, laps led and grid positions.
    ///
    /// Without this flag an old session is indistinguishable from a race
    /// where nobody put a wheel wrong and nobody led - so every driver in
    /// every race ever run would retroactively earn a badge for being
    /// spotless. False on everything saved before it existed, which is
    /// exactly what it's for.
    /// </summary>
    public bool RecordsIncidents { get; set; }

    /// <summary>
    /// How the laps were cut into sectors when the session was run - the
    /// layout's <see cref="TrackLayout.CutFor"/> - so best sectors from
    /// nights with the split loops in different places are never mixed.
    /// Null for a session without sectors, or saved before this existed.
    /// </summary>
    public string? SectorCut { get; set; }

    /// <summary>The cut a lap of these sectors had: <see cref="SectorCut"/> when it is for that many sectors, otherwise just the count.</summary>
    public string SectorCutOf(long[] sectors) =>
        TrackLayout.CountOfCut(SectorCut) == sectors.Length
            ? SectorCut!
            : sectors.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The cut a lap of these sectors had, as <paramref name="cuts"/> reads
    /// it: <see cref="SectorCutOf(long[])"/>, except that a bare count saved
    /// before the layout's first placed cut is that placed cut (see
    /// <see cref="SectorCutHistory"/>).
    /// </summary>
    public string SectorCutOf(long[] sectors, SectorCutHistory cuts) => cuts.Resolve(this, SectorCutOf(sectors));

    /// <summary>
    /// How long the race was set to run, in seconds - null for a race with
    /// no time limit, or saved before 28 Oct 2026. The relay's results say
    /// "Length: 3:00 Timed" from it, and a driver's lap list works out its
    /// pace (laps and time projected over the race) from it.
    /// </summary>
    public int? TimeLimitSeconds { get; set; }

    /// <summary>The race's lap limit - null for a race with none, or saved before 28 Oct 2026.</summary>
    public int? LapLimit { get; set; }

    /// <summary>
    /// A simulated race from Demo Mode, kept only because "Save demo races
    /// to history" was ticked.
    ///
    /// The lap times in it are invented, so it must never score: no
    /// championship points, no round marked as run, no badges, no personal
    /// bests, no records. False on everything saved before this existed -
    /// see <see cref="IsDemoSession"/> for how those older demo saves are
    /// still recognised.
    /// </summary>
    public bool IsDemo { get; set; }

    /// <summary>The label prefix every saved demo race has always carried.</summary>
    public const string DemoLabelPrefix = "[DEMO]";

    /// <summary>
    /// Whether this session is a saved demo race, including ones saved
    /// before <see cref="IsDemo"/> was recorded.
    ///
    /// Those older saves carry nothing but the "[DEMO]" label prefix Race
    /// Control has always given them, so the label is honoured too. A
    /// method rather than a property so the serializer does not write it
    /// out as a second, derived field.
    /// </summary>
    public bool IsDemoSession() =>
        IsDemo || (Label?.StartsWith(DemoLabelPrefix, StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Where a result pasted in from another timing program's results page
    /// came from - "LiveRC", or "another results page" - and empty for
    /// every race HeatCaller timed itself (1 Nov 2026, asked for by Asher).
    ///
    /// An imported race was run on another track layout, on another
    /// timing system, often years ago, so like a demo race it never
    /// scores: no championship points, no personal bests, no records, no
    /// badges, no commentary facts, and nothing published to the relay. It
    /// is kept to be looked at in Race History. See <see cref="IsNotScored"/>.
    /// </summary>
    public string ImportedFrom { get; set; } = string.Empty;

    /// <summary>Whether this is a result pasted in from elsewhere (<see cref="ImportedFrom"/>).</summary>
    public bool IsImported() => !string.IsNullOrWhiteSpace(ImportedFrom);

    /// <summary>
    /// Whether this session must never count for anything: a saved demo race
    /// (<see cref="IsDemoSession"/>) or a result imported from another
    /// program (<see cref="IsImported"/>). Every place that leaves demo races
    /// out of points, bests, records, badges, commentary and the relay asks
    /// this. A method, like IsDemoSession, so it is never written to the file.
    /// </summary>
    public bool IsNotScored() => IsDemoSession() || IsImported();

    /// <summary>Freeform notes about this session - weather, track surface, incidents, whatever's worth remembering later. Edited from the Race History tab.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Every correction made to this result after it was saved, oldest
    /// first. Empty on a result nobody has corrected, and on every session
    /// saved before the result editor existed - a missing list in an old
    /// file loads as this empty one.
    /// </summary>
    public List<ResultEdit> Edits { get; set; } = new();

    /// <summary>
    /// The handicap this race was scored on, as it stood when it was filed
    /// against its round - see <see cref="SessionHandicap"/>. Null on a race
    /// saved with no round, and on every race saved before 9 Nov 2026; those
    /// still read today's handicaps.
    /// </summary>
    public SessionHandicap? Handicap { get; set; }
}

/// <summary>
/// The class's handicap mode and each driver's credit at the time a race was
/// filed (9 Nov 2026, Bug Hunt 15, B52).
///
/// Handicaps were only ever read from the live store, so once Suggest
/// Handicaps changed the credits after a meeting - the normal weekly routine -
/// every past handicapped race was re-ranked on this week's credits, and an
/// event report regenerated for an old meeting named a different class
/// winner. Kept with the race, the result it was scored on stays the result.
/// The class name says which class it was scored for, so a race linked to a
/// round of another class later goes back to that class's live handicaps.
/// </summary>
public sealed class SessionHandicap
{
    public string ClassName { get; set; } = string.Empty;
    public HandicapMode Mode { get; set; }

    /// <summary>Only the drivers with a credit; anybody not listed had none.</summary>
    public List<SessionHandicapCredit> Credits { get; set; } = new();

    /// <summary>
    /// The other classes of a shared (mixed-class) heat, each with its mode
    /// as it was filed. WHY (15 Nov 2026, bug hunt 20): only the host class
    /// was kept, so the guest class's table was always scored on today's
    /// credits. Empty for a heat of one class, and on races saved before.
    /// </summary>
    public List<SessionHandicapClass> SharedClasses { get; set; } = new();

    /// <summary>The credit kept for this transponder, or null when it had none.</summary>
    public SessionHandicapCredit? For(string transponder) =>
        Credits.FirstOrDefault(c => string.Equals(c.Transponder, transponder, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A class sharing a heat, with its handicap mode when the race was filed. See <see cref="SessionHandicap.SharedClasses"/>.</summary>
public sealed class SessionHandicapClass
{
    public string ClassName { get; set; } = string.Empty;
    public HandicapMode Mode { get; set; }
}

/// <summary>One driver's credit as it stood when the race was filed. See <see cref="SessionHandicap"/>.</summary>
public sealed class SessionHandicapCredit
{
    public string Transponder { get; set; } = string.Empty;
    public double TimeCreditSeconds { get; set; }
    public int LapCredit { get; set; }
}

/// <summary>
/// Which placed cut a bare sector count ("2") saved on a layout stands for.
///
/// 6 Nov 2026, Bug hunt 11b review: bug hunt 11 keyed TrackLayout.CutFor on
/// the tiles for a multi-tile finish, so every session saved before it has
/// SectorCut "2" and the first one filed after has "2:c,r;c,r". The best
/// sectors (DriverStats, CoachReport) compared cuts Ordinal and so ignored
/// all the earlier nights although the loops never moved - while the
/// layout's own sector records carried over on purpose (OfferSectors,
/// IsCarriedOverBareCut). Read the same way: the bare count stands for the
/// layout's FIRST placed cut of that many sectors - the earliest session
/// saved with one, which is the placement OfferSectors carried the records
/// to - for sessions started no later than it. A bare count saved after
/// that (the loops taken off the plan) stays its own cut, and a later
/// placement (a loop moved) never takes the bare-count nights.
/// </summary>
public sealed class SectorCutHistory
{
    // Layout (trimmed, upper invariant) + "\n" + count -> the first placed cut and when it was saved.
    private readonly Dictionary<string, (string Cut, DateTime StartedUtc)> _firstPlaced = new(StringComparer.Ordinal);

    public SectorCutHistory(IEnumerable<SessionRecord?> sessions)
    {
        foreach (var session in sessions)
        {
            if (session?.SectorCut is not { } cut
                || !cut.Contains(':')
                || string.IsNullOrWhiteSpace(session.TrackLayoutName))
            {
                continue;
            }

            var count = TrackLayout.CountOfCut(cut);

            if (count < 2)
            {
                continue;
            }

            var key = KeyOf(session.TrackLayoutName, count);

            if (!_firstPlaced.TryGetValue(key, out var first) || session.StartedAtUtc < first.StartedUtc)
            {
                _firstPlaced[key] = (cut, session.StartedAtUtc);
            }
        }
    }

    /// <summary><paramref name="cut"/> (one of <paramref name="session"/>'s), or the placed cut it stands for.</summary>
    public string Resolve(SessionRecord session, string cut)
    {
        var count = TrackLayout.CountOfCut(cut);

        if (cut.Contains(':')
            || count < 2
            || string.IsNullOrWhiteSpace(session.TrackLayoutName)
            || !_firstPlaced.TryGetValue(KeyOf(session.TrackLayoutName, count), out var first)
            || session.StartedAtUtc > first.StartedUtc)
        {
            return cut;
        }

        return first.Cut;
    }

    private static string KeyOf(string layout, int count) =>
        layout.Trim().ToUpperInvariant() + "\n" + count.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
