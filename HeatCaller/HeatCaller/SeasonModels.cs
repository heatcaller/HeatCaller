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

    /// <summary>
    /// How many rounds this championship is SCHEDULED to run - the length
    /// of the season, as the club planned it. Zero means they have not
    /// said, which is what every season made before 12 Oct 2026 holds and
    /// is the honest default.
    ///
    /// WHY THIS IS NOT DERIVABLE, AND WHY IT HAD TO BE ASKED FOR
    ///
    /// Nothing else in this app knows it. A Season is a set of races that
    /// have HAPPENED plus a rule that takes more as they are recorded, so
    /// the only round count anywhere is "how many have been run so far".
    /// <see cref="CountBestRounds"/> looks like a length and is not - it is
    /// the drop cap, and "best 5 of 7" is a rule about a driver's own
    /// rounds rather than about the calendar.
    ///
    /// WHAT IT UNLOCKS, which is the whole reason it exists: everything
    /// the announcer could say about what is still POSSIBLE. "Nobody can
    /// catch him now", "three rounds left and these two are level", "a win
    /// today and it is his" are all claims about the rounds remaining, and
    /// with no planned length there are no rounds remaining to reason
    /// about - only rounds already run. See CommentaryReaders.TitleStakes.
    ///
    /// Zero keeps the announcer quiet about all of it rather than guessing,
    /// which is right: a club that has not told the app its season length
    /// has not given it the figure, and inventing one would put a false
    /// "he cannot be caught" over a PA.
    /// </summary>
    public int PlannedRounds { get; set; }

    /// <summary>
    /// One sentence about the season in the club's own words - "two rounds
    /// left and four points covering the top three" - read out as typed at
    /// the night's opening and sign-off (7 Nov 2026, known issues). Empty,
    /// as every season saved before then is, says nothing.
    ///
    /// TYPED, NEVER WORKED OUT. The sum behind that sentence is the one the
    /// commentary readers refuse to do by arithmetic (see
    /// CommentaryReaders.TitleOnTheLine), so the only way it reaches the PA
    /// is somebody at the club saying it first. Kept apart from
    /// RaceEvent.Notes, which is the day's scratch pad and must never be
    /// read to a room.
    /// </summary>
    public string AnnouncerNote { get; set; } = string.Empty;

    /// <summary>The longest note the box takes - what CommentaryFacts.CleanLong keeps, so nothing typed is cut off.</summary>
    public const int AnnouncerNoteMaxLength = 60;

    /// <summary>
    /// How many rounds are still to come after the ones counted so far,
    /// or null when the club has not said how long the season is.
    ///
    /// Never negative: a club that planned six rounds and ran seven has a
    /// season that over-ran, and the honest answer is then that none
    /// remain rather than minus one.
    /// </summary>
    public int? RoundsRemainingAfter(int roundsCounted) =>
        PlannedRounds <= 0 ? null : Math.Max(0, PlannedRounds - Math.Max(0, roundsCounted));

    /// <summary>
    /// The biggest figure in this season's scheme.
    ///
    /// Read off the table rather than assumed to be the first row's: a club
    /// that typed its own scheme can have the best score anywhere in it,
    /// and a bonus row below the podium is a real thing people enter.
    ///
    /// NOT "THE MOST A ROUND CAN BE WORTH", which is what this was called
    /// and is not what it answers. It reads the ROWS; a race is scored
    /// through <see cref="PointsForPosition"/>, which takes the FIRST row
    /// for a position - so a scheme holding two rows for first place has a
    /// biggest figure that nobody can ever score. Anything proving what is
    /// still possible wants a PLACE and then that lookup's price for it:
    /// see <see cref="BestPayingPosition"/>, which is what
    /// CommentaryReaders.TitleOnTheLine asks, and
    /// CommentaryStakesTests.The_best_paying_place_is_read_through_the_lookup_that_scores_a_race,
    /// which is the two of them disagreeing on purpose.
    /// </summary>
    public int MostPointsInARound() =>
        PointsScheme.Count == 0 ? 0 : Math.Max(0, PointsScheme.Max(row => row.Points));

    /// <summary>
    /// The finishing position that PAYS the most on this season's own
    /// scheme - the place a driver's best possible round is - or zero when
    /// nothing scores.
    ///
    /// WHY THIS IS NOT ALWAYS ONE, and why anything that assumed it was
    /// was wrong. The scheme is a list a club TYPES. Third paying more than
    /// second is a real thing people enter, by design (an endurance round
    /// weighted differently) and by accident (a row edited and the one
    /// above it forgotten). So "the best available result" and "the most
    /// points available" are two different positions, and a simulation
    /// built on winning the race proves nothing about the points.
    ///
    /// READ THROUGH <see cref="PointsForPosition"/> rather than off the
    /// rows, because that lookup is what actually scores a race and it
    /// takes the FIRST row for a position: a scheme holding two rows for
    /// first place pays whichever one was typed first, and the higher of
    /// the two is a figure nobody can score. The lowest position among
    /// those paying the most wins the tie, so a proof built on it has the
    /// fewest hypothetical cars in it.
    ///
    /// Used by CommentaryReaders.TitleOnTheLine to build a rival's best
    /// possible remaining round. See its remarks for why that has to be
    /// played out rather than added up.
    /// </summary>
    public int BestPayingPosition()
    {
        var bestPosition = 0;
        var bestPoints = 0;

        foreach (var row in PointsScheme)
        {
            if (row.Position < 1)
            {
                continue;
            }

            var pays = PointsForPosition(row.Position);

            if (bestPosition == 0 || pays > bestPoints || (pays == bestPoints && row.Position < bestPosition))
            {
                bestPosition = row.Position;
                bestPoints = pays;
            }
        }

        return bestPoints > 0 ? bestPosition : 0;
    }

    /// <summary>
    /// Whether any row of this scheme TAKES points away - a negative
    /// figure somewhere in the table.
    ///
    /// The Season Rules page accepts one, and a club has reasons to: a
    /// penalty row for a position that is really a disqualification. It
    /// makes one thing unprovable, which is why this is asked.
    ///
    /// WHAT IT BREAKS. Every "nobody can catch him" claim in this app is
    /// proved by playing the remaining rounds out with the rival at their
    /// best and the leader at their worst, and the leader's worst is taken
    /// to be scoring NOTHING - not entering. On a scheme where nothing is
    /// negative that is exactly right: a round worth zero either leaves a
    /// total alone or is dropped by the best-of rule, so it is the same as
    /// not being there. With a negative row it is no longer the worst case
    /// at all - a leader can finish in a place that costs them points and
    /// come out BELOW where their absence would have left them - so a proof
    /// built on absence would claim a title that could still be lost.
    ///
    /// So CommentaryReaders.TitleOnTheLine refuses the whole season rather
    /// than price a worst case it has not got.
    /// </summary>
    public bool PenalisesAnyone() => PointsScheme.Any(row => row.Points < 0);

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

    /// <summary>
    /// What a place scores. NEVER BELOW ZERO (22 Oct 2026, known issues -
    /// the club chose "clamp at zero"): a negative row could only have come
    /// from a hand-edited file or an old build, which let one be typed, and
    /// it took points off a driver for turning up. The Championship page
    /// refuses one now; a row already on file scores nothing. Rows are left
    /// as they are on disk, so <see cref="PenalisesAnyone"/> still sees one
    /// and the commentary stays as careful as it was.
    /// </summary>
    public int PointsForPosition(int position) =>
        Math.Max(0, PointsScheme.FirstOrDefault(e => e.Position == position)?.Points ?? 0);
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

    /// <summary>
    /// Every transponder a scored row under this name raced on, in the order
    /// met; empty for carried points alone. For the public standings to
    /// match an initials-only driver by transponder as well as by name (23
    /// Oct 2026, bug hunt 10) - a row from before a rename carries the old
    /// name, which the privacy register no longer knows. Never published.
    /// </summary>
    public List<string> Transponders { get; } = new();
}
