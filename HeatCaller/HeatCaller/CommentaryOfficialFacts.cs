using System.Globalization;

namespace HeatCaller;

/// <summary>
/// A class scored on handicap, as one race came out on it - handed to the
/// recap by Race Control, which knows the class and the start.
/// </summary>
/// <param name="Mode">Time credits or lap credits. Never None - no handicap is null, not this.</param>
/// <param name="Order">The race re-ordered with the credits applied, as HandicapCalculator.Apply returns it.</param>
public sealed record RaceHandicap(HandicapMode Mode, IReadOnlyList<HandicapCalculator.Adjusted> Order);

/// <summary>
/// WHAT RACE CONTROL DID TO A RESULT, for the commentary (7 Nov 2026).
///
/// WHY THIS EXISTS. Every number a decision changes was already kept on the
/// saved result - the warnings and their reasons, the in-race penalty, the
/// jump start, the corrections log with its reasons, the DNF or DQ, the
/// race director's note - and the AI commentary was handed none of it. The
/// PA called a warning when it was given and then the recap described the
/// race as if the road order had stood: a win handed over by a five-second
/// penalty was read out as an ordinary win, and the driver who crossed the
/// line first was not mentioned at all. The commentary research of 7 Nov
/// 2026 found this, and these are the lines that close it.
///
/// THE REASONS. PenaltyWords keeps a warning's reason off the PUBLIC
/// results document because it is free text a race director typed about a
/// named person. A warning's reason is already said over the PA as it is
/// given ("Warning, Sam. Blocking."), so the room has heard it; these lines
/// go to the same room. Each is cut to <see cref="ReasonChars"/> and passed
/// through the prompt's own guard, and the instruction that carries them
/// tells the model to say them plainly and never to judge anybody by them.
///
/// Pure: nothing here reads a store, so every sentence it can make is a
/// table test. Names come in through a function, as every builder in this
/// layer takes them, and a driver it gives no name for is left out rather
/// than called by a transponder.
/// </summary>
internal static class CommentaryOfficialFacts
{
    /// <summary>The longest reason carried. A reason is a note, and the PA reads it as one.</summary>
    internal const int ReasonChars = 60;

    /// <summary>The longest race director's note carried - a couple of sentences about the track or an incident.</summary>
    internal const int NoteChars = 200;

    /// <summary>How many drivers' race-control lines one recap carries. A race with more than this many decisions is a race the room already knows was messy.</summary>
    internal const int MostDrivers = 6;

    /// <summary>How many "lost places to penalties" lines a recap carries, after the winner's.</summary>
    internal const int MostPlacesLost = 3;

    /// <summary>How many lines the night's own race-control round-up carries.</summary>
    internal const int MostTonight = 5;

    /// <summary>How many handicap placings are listed.</summary>
    internal const int MostHandicapPlaces = 3;

    /// <summary>How many beaten lap targets a recap carries.</summary>
    internal const int MostTargets = 3;

    /// <summary>
    /// The reason RaceControlWarnings files a black flag's DQ under. Its
    /// correction is the black flag itself, which the warning line already
    /// says - so it is not said a second time as a correction.
    /// </summary>
    internal const string BlackFlagReason = "Black flag from race control";

    // ------------------------------------------------------------------
    // One race
    // ------------------------------------------------------------------

    /// <summary>
    /// What race control did to each driver in this race, a line per driver
    /// who had anything done: "Sam: warning 1 for blocking; warning 2 for
    /// blocking, a 5 second penalty". Finishing order, at most
    /// <see cref="MostDrivers"/>. Empty for a clean race, which is most.
    /// </summary>
    internal static IReadOnlyList<string> ForRace(SessionRecord? session, Func<string, string> nameFor)
    {
        var lines = new List<string>();

        if (session?.Results is null)
        {
            return lines;
        }

        foreach (var entry in session.Results.Where(r => r is not null).OrderBy(r => r.Position))
        {
            if (GhostService.IsGhost(entry.Transponder))
            {
                continue;
            }

            var parts = WhatTheyGot(entry, session);

            if (parts.Count == 0)
            {
                continue;
            }

            var name = CommentaryService.Field(nameFor(entry.Transponder));

            if (name.Length == 0)
            {
                continue;
            }

            lines.Add($"{name}: {string.Join("; ", parts)}");

            if (lines.Count >= MostDrivers)
            {
                break;
            }
        }

        return lines;
    }

    /// <summary>
    /// Everything race control did to one result, oldest first: the
    /// warnings in race order, whatever in-race penalty no warning accounts
    /// for (a jump start, the +5s buttons), then what was changed after the
    /// race and why. The same order and the same arithmetic as PenaltyWords,
    /// which says these to the driver themselves - written for a sentence
    /// rather than a list on a phone.
    /// </summary>
    internal static List<string> WhatTheyGot(SessionResultEntry entry, SessionRecord? session)
    {
        var parts = new List<string>();

        var warnings = (entry.Warnings ?? new List<RaceWarning>())
            .Where(w => w is not null)
            .OrderBy(w => w.Number)
            .ThenBy(w => w.AtUtc)
            .ToList();

        var blackFlagged = false;

        foreach (var warning in warnings)
        {
            var why = Reason(warning.Reason);
            var forWhat = why.Length > 0 ? $" for {why}" : string.Empty;

            switch (warning.Action)
            {
                case WarningAction.TimePenalty:
                    parts.Add($"warning {warning.Number}{forWhat}, a {Seconds(warning.PenaltyMs)} second penalty");
                    break;

                case WarningAction.BlackFlag:
                    blackFlagged = true;
                    parts.Add($"black-flagged on warning {warning.Number}{forWhat}, and disqualified");
                    break;

                default:
                    parts.Add($"warning {warning.Number}{forWhat}");
                    break;
            }
        }

        // What race control added that the warnings do not explain.
        var fromWarnings = warnings.Sum(w => Math.Max(0, w.PenaltyMs));
        var rest = Math.Max(0, entry.RacePenaltyMs) - fromWarnings;
        var jumpWarned = warnings.Any(w => (w.Reason ?? string.Empty).Contains("jump start", StringComparison.OrdinalIgnoreCase));

        if (rest > 0)
        {
            parts.Add(entry.JumpStarted
                ? $"jumped the start, {Seconds(rest)} seconds added"
                : $"{Seconds(rest)} seconds added by race control during the race");
        }
        else if (entry.JumpStarted && !jumpWarned)
        {
            parts.Add("jumped the start, no time added");
        }

        parts.AddRange(AfterTheRace(entry, session, saidDisqualified: blackFlagged));

        return parts;
    }

    /// <summary>
    /// What was changed on this result after the race was saved, as the
    /// result says now, and the reasons given for it: "after the race, 5
    /// seconds added because of a missed chicane". Empty for a result
    /// nobody corrected.
    ///
    /// FROM THE TOTALS, NOT THE LOG'S "CHANGE" TEXT. The log says "time
    /// penalty 0s -> 5s", and the prompt's guard strips the arrow, which
    /// leaves "0s - 5s" for a model to misread. The totals are what the
    /// result IS; the log is only read for why.
    /// </summary>
    /// <param name="withReasons">
    /// Whether the reasons typed in Edit Result go with it. True only for
    /// the race's own recap, played in the room. Tonight's recap is the
    /// piece a club passes on to people who were not there, so it says
    /// what changed and leaves the director's own words about a named
    /// driver out (7 Nov 2026, Asher's call) - the rule PenaltyWords keeps
    /// for the public results.
    /// </param>
    internal static List<string> AfterTheRace(SessionResultEntry entry, SessionRecord? session, bool saidDisqualified = false, bool withReasons = true)
    {
        var parts = new List<string>();
        var what = new List<string>();

        if (entry.AddedLaps > 0)
        {
            what.Add($"{Plural.Of(entry.AddedLaps, "lap")} added");
        }
        else if (entry.AddedLaps < 0)
        {
            what.Add($"{Plural.Of(-entry.AddedLaps, "lap")} taken off");
        }

        if (entry.TimePenaltyMs > 0)
        {
            what.Add($"{Seconds(entry.TimePenaltyMs)} seconds added as a penalty");
        }

        if (entry.LapPenalty > 0)
        {
            what.Add($"a {Plural.Of(entry.LapPenalty, "lap")} penalty");
        }

        var outcome = ResultEditor.NormaliseOutcome(entry.Outcome);

        if (outcome == ResultEditor.Dq && !saidDisqualified)
        {
            what.Add("disqualified");
        }
        else if (outcome == ResultEditor.Dnf)
        {
            what.Add("classified as not finishing");
        }

        if (what.Count == 0)
        {
            return parts;
        }

        var reasons = (withReasons ? session?.Edits ?? new List<ResultEdit>() : new List<ResultEdit>())
            .Where(e => e is not null
                        && string.Equals(e.Transponder?.Trim(), entry.Transponder?.Trim(), StringComparison.OrdinalIgnoreCase)
                        && !(e.Reason ?? string.Empty).StartsWith(BlackFlagReason, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.AtUtc)
            .Select(e => Reason(e.Reason))
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(2)
            .ToList();

        var because = reasons.Count > 0 ? $", because: {string.Join("; ", reasons)}" : string.Empty;

        parts.Add($"after the race, {string.Join(" and ", what)}{because}");

        return parts;
    }

    /// <summary>
    /// WHEN A PENALTY DECIDED THE RESULT: who would have been where on the
    /// road, against where they are classified.
    ///
    /// The road result is worked out from what the result already keeps:
    /// the laps as raced (with any missed crossing put back, which is a
    /// correction of the count rather than a penalty) and the time as raced
    /// less the in-race penalty that is inside it. Only drivers who were
    /// penalised are ever said to have lost anything, and only among the
    /// cars that were classified or penalised - an unpenalised retirement
    /// is left out of both orders so it cannot make one driver's penalty
    /// look like two places.
    ///
    /// Empty when no penalty moved anybody, which is nearly every race.
    /// </summary>
    internal static IReadOnlyList<string> PenaltiesDecided(SessionRecord? session, Func<string, string> nameFor)
    {
        var lines = new List<string>();

        if (session?.Results is null)
        {
            return lines;
        }

        var counted = session.Results
            .Where(r => r is not null && !GhostService.IsGhost(r.Transponder))
            .Where(r => Penalised(r) || string.IsNullOrEmpty(ResultEditor.NormaliseOutcome(r.Outcome)))
            .ToList();

        if (!counted.Any(Penalised))
        {
            return lines;
        }

        var road = counted
            .Where(r => RoadLaps(r) > 0)
            .OrderByDescending(RoadLaps)
            .ThenBy(RoadMs)
            .ToList();

        var classified = counted.OrderBy(r => r.Position).ToList();

        if (road.Count == 0 || classified.Count == 0)
        {
            return lines;
        }

        var roadWinner = road[0];
        var winner = classified[0];

        if (!ReferenceEquals(roadWinner, winner) && Penalised(roadWinner))
        {
            var had = CommentaryService.Field(nameFor(roadWinner.Transponder));
            var won = CommentaryService.Field(nameFor(winner.Transponder));

            if (had.Length > 0 && won.Length > 0)
            {
                lines.Add(ResultEditor.NormaliseOutcome(roadWinner.Outcome) == ResultEditor.Dq
                    ? $"{had} had the winning result on the road but was disqualified, so the win went to {won}."
                    : $"{had} had the winning result on the road, but penalties dropped them down the order, so the win went to {won}.");
            }
        }

        var placesLines = 0;

        foreach (var entry in road)
        {
            if (placesLines >= MostPlacesLost || ReferenceEquals(entry, roadWinner) || !Penalised(entry))
            {
                continue;
            }

            if (ResultEditor.NormaliseOutcome(entry.Outcome) == ResultEditor.Dq)
            {
                continue;
            }

            var lost = classified.IndexOf(entry) - road.IndexOf(entry);
            var name = CommentaryService.Field(nameFor(entry.Transponder));

            if (lost > 0 && name.Length > 0)
            {
                lines.Add($"{name} lost {Plural.Of(lost, "place")} to penalties.");
                placesLines++;
            }
        }

        return lines;
    }

    /// <summary>
    /// The race director's own note on this session - the weather, the
    /// track, an incident - typed on the Race History tab. One line, cut to
    /// <see cref="NoteChars"/>, or empty.
    /// </summary>
    internal static string Note(SessionRecord? session) =>
        CommentaryService.Field(session?.Notes, NoteChars).Replace('"', '\'');

    /// <summary>
    /// The race as it came out on handicap, for a class scored that way and
    /// a race that was not a pursuit start (whose credits were spent on
    /// track): what the credits are, the top of the handicap order with
    /// where each was on the road, and who wins on handicap when that is
    /// not who crossed the line first. Empty when there is no handicap or
    /// nobody had a credit.
    /// </summary>
    internal static IReadOnlyList<string> Handicap(RaceHandicap? handicap, Func<string, string> nameFor)
    {
        var lines = new List<string>();

        if (handicap is null
            || handicap.Mode == HandicapMode.None
            || handicap.Order is not { Count: > 0 } order
            || !order.Any(a => a.CreditSeconds != 0 || a.CreditLaps != 0))
        {
            return lines;
        }

        lines.Add(handicap.Mode == HandicapMode.TimeCredit
            ? "This class is scored on handicap: each driver's time credit comes off their race time."
            : "This class is scored on handicap: each driver's lap credit is added to their laps.");

        var ranked = order
            .Where(a => a.Result is not null && !GhostService.IsGhost(a.Result.Transponder))
            .OrderBy(a => a.AdjustedPosition)
            .ToList();

        foreach (var place in ranked.Take(MostHandicapPlaces))
        {
            var name = CommentaryService.Field(nameFor(place.Result.Transponder));

            if (name.Length == 0)
            {
                continue;
            }

            var credit = handicap.Mode == HandicapMode.TimeCredit
                ? place.CreditSeconds != 0 ? $"credit {Seconds((long)Math.Round(place.CreditSeconds * 1000))} seconds" : "no credit"
                : place.CreditLaps != 0 ? $"credit {Plural.Of(place.CreditLaps, "lap")}" : "no credit";

            lines.Add($"  On handicap P{place.AdjustedPosition} {name} - {credit}, P{place.RawPosition} on the road");
        }

        var onHandicap = ranked.FirstOrDefault();
        var onRoad = ranked.OrderBy(a => a.RawPosition).FirstOrDefault();

        if (onHandicap is not null && onRoad is not null && !ReferenceEquals(onHandicap, onRoad))
        {
            var hName = CommentaryService.Field(nameFor(onHandicap.Result.Transponder));
            var rName = CommentaryService.Field(nameFor(onRoad.Result.Transponder));

            if (hName.Length > 0 && rName.Length > 0)
            {
                lines.Add($"On handicap the race goes to {hName}; {rName} was first on the road.");
            }
        }

        return lines.Count > 1 ? lines : new List<string>();
    }

    /// <summary>
    /// The drivers in this race who beat the target lap race control set
    /// them tonight (Coach > Target lap), with the target. Only the drivers
    /// who BEAT it: a driver who missed a coaching goal is not a story to
    /// tell a room, and the target itself is the only time each line holds,
    /// so the recap's rule against one driver's time landing on another
    /// still stands.
    /// </summary>
    internal static IReadOnlyList<string> TargetsBeaten(
        IEnumerable<SessionResultEntry> named,
        Func<string, long?> targetFor,
        Func<string, string> nameFor)
    {
        var lines = new List<string>();

        foreach (var entry in named)
        {
            if (entry is null
                || entry.FastLapMs <= 0
                || ResultEditor.NormaliseOutcome(entry.Outcome) == ResultEditor.Dq
                || targetFor(entry.Transponder) is not { } target
                || target <= 0
                || entry.FastLapMs > target)
            {
                continue;
            }

            var name = CommentaryService.Field(nameFor(entry.Transponder));

            if (name.Length == 0)
            {
                continue;
            }

            lines.Add($"{name} beat the target lap race control set for them tonight, {Lap(target)}s");

            if (lines.Count >= MostTargets)
            {
                break;
            }
        }

        return lines;
    }

    // ------------------------------------------------------------------
    // The night
    // ------------------------------------------------------------------

    /// <summary>
    /// What race control decided tonight, for the night's recap: a result
    /// a penalty changed, a disqualification, a correction made after the
    /// race (what changed, never the reason typed for it), and the race
    /// director's notes. Race order.
    ///
    /// THE NIGHT IS WHERE A CORRECTION CAN BE HEARD. A race's own recap is
    /// written at the flag, and a result corrected in Race History ten
    /// minutes later was never going to be in it. By the end of the night
    /// it is the result, and a recap that read out the old one would be
    /// reading out something the club changed on purpose.
    ///
    /// Warnings with no time on them are left out: a warning that cost
    /// nothing is a race's detail, not the night's.
    /// </summary>
    internal static IReadOnlyList<string> ForTheNight(IEnumerable<SessionRecord>? races, Func<string, string> nameFor, int most = MostTonight)
    {
        var lines = new List<string>();

        foreach (var race in (races ?? Enumerable.Empty<SessionRecord>())
                     .Where(r => r?.Results is not null)
                     .OrderBy(r => r.StartedAtUtc))
        {
            var label = CommentaryFacts.CleanLong(race.Label);
            var where = label.Length > 0 ? $"In {label}" : "In one race";

            var decided = PenaltiesDecided(race, nameFor).FirstOrDefault();

            if (decided is not null)
            {
                lines.Add($"{where}: {decided}");
            }

            foreach (var entry in race.Results.Where(r => r is not null).OrderBy(r => r.Position))
            {
                if (GhostService.IsGhost(entry.Transponder))
                {
                    continue;
                }

                var name = CommentaryService.Field(nameFor(entry.Transponder));

                if (name.Length == 0)
                {
                    continue;
                }

                var flagged = (entry.Warnings ?? new List<RaceWarning>()).FirstOrDefault(w => w?.Action == WarningAction.BlackFlag);

                // Without the reason, for AfterTheRace's: a black flag's
                // reason was typed about a named driver too.
                if (flagged is not null)
                {
                    lines.Add($"{where}, {name} was black-flagged and disqualified.");
                    continue;
                }

                // Without the reasons typed for it - see AfterTheRace.
                if (AfterTheRace(entry, race, withReasons: false).FirstOrDefault() is { } after)
                {
                    lines.Add($"{where}, race control changed {name}'s result {after}.");
                }
            }

            if (Note(race) is { Length: > 0 } note)
            {
                lines.Add($"Race director's note on {(label.Length > 0 ? label : "one race")}: \"{note}\"");
            }
        }

        return lines.Count <= most ? lines : lines.Take(most).ToList();
    }

    // ------------------------------------------------------------------
    // Pieces
    // ------------------------------------------------------------------

    private static bool Penalised(SessionResultEntry r) =>
        r.RacePenaltyMs > 0
        || r.TimePenaltyMs > 0
        || r.LapPenalty > 0
        || ResultEditor.NormaliseOutcome(r.Outcome) == ResultEditor.Dq;

    /// <summary>Laps as raced, with any count correction (a missed or phantom crossing) kept - that is not a penalty.</summary>
    private static int RoadLaps(SessionResultEntry r) => (r.AsRaced?.Laps ?? r.Laps) + (r.AsRaced is null ? 0 : r.AddedLaps);

    /// <summary>Time as raced, less the in-race penalty inside it.</summary>
    private static long RoadMs(SessionResultEntry r) =>
        Math.Max(0, (r.AsRaced?.ElapsedMs ?? r.ElapsedMs) - Math.Max(0, r.RacePenaltyMs));

    /// <summary>A reason, lower-cased at the front, one line, cut, guarded - or empty.</summary>
    internal static string Reason(string? reason)
    {
        var text = CommentaryService.Field(reason, ReasonChars).Replace('"', '\'').TrimEnd('.', ' ');

        if (text.Length == 0)
        {
            return string.Empty;
        }

        // "Blocking" reads as "for blocking" - but "Sam's car" keeps its
        // capital, so only a first word that is not all capitals and not
        // a name the club typed with one is lowered. A preset reason is
        // the common case; anything else is left exactly as typed.
        return WarningRules.PresetReasons.Any(p => string.Equals(p, text, StringComparison.OrdinalIgnoreCase))
            ? text.ToLowerInvariant()
            : text;
    }

    /// <summary>"5", "2.5" - seconds with a decimal POINT whatever the PC's region.</summary>
    internal static string Seconds(long ms) =>
        (Math.Max(0, ms) / 1000.0).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>"14.200" - a lap with a decimal POINT whatever the PC's region.</summary>
    private static string Lap(long ms) =>
        (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
}
