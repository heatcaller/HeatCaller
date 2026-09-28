namespace HeatCaller;

/// <summary>One position -> points mapping row in a scoring scheme (e.g. 1st place = 25 points).</summary>
public sealed class PointsSchemeEntry
{
    public int Position { get; set; }
    public int Points { get; set; }
}

/// <summary>
/// A named championship: an explicit set of Race sessions from history,
/// scored with a configurable points-per-finishing-position table and
/// summed per driver into a standings table. Sessions are chosen
/// explicitly (rather than "everything in a date range") so a rained-out
/// or test session can be left out without deleting it from history.
/// </summary>
public sealed class Season
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The races that actually count. This stays the single source of
    /// truth for scoring - the rule below fills it, and a person can
    /// override it - so nothing that reads a championship has to know the
    /// rule exists.
    /// </summary>
    public List<string> IncludedSessionIds { get; set; } = new();

    // ---------------------------------------------------------------
    //  The rule
    //
    //  Picking every race by hand was deliberate once: it let a rained-off
    //  round be left out without deleting it from history. It also meant a
    //  championship silently stopped counting the moment somebody forgot
    //  to come back and tick last night's mains, which is the failure that
    //  actually happens.
    //
    //  So there is a rule now, and the tick list survives as the override.
    //  Everything below is inert unless AutoInclude is on, which is why a
    //  championship made before any of this existed behaves exactly as it
    //  did - it deserializes with AutoInclude false.
    // ---------------------------------------------------------------

    /// <summary>
    /// Whether races join this championship on their own as they are
    /// recorded. False on anything made before the rule existed, and on
    /// purpose: turning it on for an old season would rewrite a standings
    /// table somebody may have already published.
    /// </summary>
    public bool AutoInclude { get; set; }

    /// <summary>The class this championship is for. Empty counts every class, which is what the old behaviour did.</summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>
    /// Follows a class rename. Returns true when this championship was for
    /// that class and now names the new one. An "every class" championship
    /// (empty ClassName) is never touched - it names no class to follow.
    /// </summary>
    public bool RenameClass(string oldName, string newName)
    {
        if (string.IsNullOrEmpty(ClassName) || !string.Equals(ClassName, oldName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ClassName = newName;
        return true;
    }

    /// <summary>
    /// Count mains only, leaving qualifying heats out - the usual club
    /// rule. Only consulted when AutoInclude is on.
    /// </summary>
    public bool MainsOnly { get; set; } = true;

    /// <summary>
    /// Which race days count. Empty means all of them.
    ///
    /// Named to match EventSpine.SessionsFor, which was written against
    /// this property before it existed - the build error that found it was
    /// the first time anything had asked the compiler about that method.
    /// </summary>
    public List<string> IncludedEventIds { get; set; } = new();

    /// <summary>
    /// Races the rule would have included and a person took back out - the
    /// rained-off round the old design was built around. Kept separately
    /// from the included list so that re-running the rule does not quietly
    /// put them back.
    /// </summary>
    public List<string> ExcludedSessionIds { get; set; } = new();

    /// <summary>
    /// How many of each driver's rounds count - "best 5 of 7". Zero counts
    /// every round, which is a straight sum and what this always did.
    ///
    /// Per DRIVER, not per championship: somebody who missed two rounds
    /// drops nothing, which is the whole point of the rule.
    /// </summary>
    public int CountBestRounds { get; set; }

    public List<PointsSchemeEntry> PointsScheme { get; set; } = DefaultPointsScheme();

    /// <summary>
    /// Points drivers bring in from before this championship was run in
    /// HeatCaller - the standings from the club's old software, imported
    /// with the drivers or typed in on the Championship page (27 Sep 2026).
    /// Empty on anything made before, which then scores exactly as it did.
    /// </summary>
    public List<CarriedPointsEntry> CarriedPoints { get; set; } = new();

    /// <summary>A simple linear scheme (10-9-8-...-1) - the most common club-racing default. Users can edit it into an F1-style table or anything else in the Season tab.</summary>
    public static List<PointsSchemeEntry> DefaultPointsScheme()
    {
        var entries = new List<PointsSchemeEntry>();
        for (var position = 1; position <= 10; position++)
        {
            entries.Add(new PointsSchemeEntry { Position = position, Points = Math.Max(0, 11 - position) });
        }
        return entries;
    }

    public int PointsForPosition(int position) =>
        PointsScheme.FirstOrDefault(e => e.Position == position)?.Points ?? 0;
}

/// <summary>
/// One driver's points carried into a championship - see Season.CarriedPoints.
/// Added to what they score in HeatCaller, and never dropped by the
/// best-rounds rule: that drops rounds, and this is not one.
/// </summary>
public sealed class CarriedPointsEntry
{
    /// <summary>The driver by the name the standings go by - the name saved with their results.</summary>
    public string DriverName { get; set; } = string.Empty;

    public int Points { get; set; }
}

/// <summary>One driver's aggregated standing within a season.</summary>
public sealed class SeasonStandingEntry
{
    public required string DriverName { get; init; }
    public int TotalPoints { get; set; }
    public int SessionsCounted { get; set; }
    public int Wins { get; set; }
    public int Podiums { get; set; }
    public int BestFinish { get; set; } = int.MaxValue;

    /// <summary>What they scored before any rounds were dropped. Equal to TotalPoints when the season counts everything.</summary>
    public int RawPoints { get; set; }

    /// <summary>How many of their rounds were dropped by the "best N" rule. Zero when it is off, or when they raced no more than N.</summary>
    public int DroppedRounds { get; set; }

    /// <summary>Rounds they were entered in but completed no laps - scored nothing and not counted.</summary>
    public int Dnfs { get; set; }

    /// <summary>Points carried in from before - included in TotalPoints and RawPoints. Zero for most.</summary>
    public int CarriedIn { get; set; }
}
