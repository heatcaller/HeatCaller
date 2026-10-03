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
    public List<SessionResultEntry> Results { get; set; } = new();

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

    /// <summary>Freeform notes about this session - weather, track surface, incidents, whatever's worth remembering later. Edited from the Race History tab.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Every correction made to this result after it was saved, oldest
    /// first. Empty on a result nobody has corrected, and on every session
    /// saved before the result editor existed - a missing list in an old
    /// file loads as this empty one.
    /// </summary>
    public List<ResultEdit> Edits { get; set; } = new();
}
