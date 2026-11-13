using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Mixed-class heats (11 Nov 2026, race-night build plan, Phase 4.1): small
/// classes sharing one race - 3 Stock and 4 Mod on track together - and
/// still scored apart.
///
/// WHY THE MODEL IS "A MAIN CLASS PLUS OTHERS", NOT A LIST OF CLASSES.
/// Every round has one <see cref="RoundDefinition.ClassName"/>, and almost
/// everything in the app keys on it: the program, the qualifying, the mains,
/// the championships, the event report, the relay, the heat sheet, the
/// marshals, the Next Steps card, Auto start. Replacing it with a list would
/// have touched every one of those at once and migrated every saved file.
/// Instead a round keeps ClassName as its MAIN class and gains an optional
/// list of the other classes sharing the heat
/// (<see cref="RoundDefinition.SharedClasses"/>). Null or empty - every file
/// saved before this, and every round built without the option - is a
/// one-class round and behaves exactly as it always did; nothing here
/// changes a thing for it. That is the safety rule every method below
/// starts from: <see cref="IsShared"/> false means today's code path.
///
/// WHO IS IN WHICH CLASS. Each driver in a shared heat belongs to ONE of
/// its classes - one car, one race. The heat remembers it explicitly
/// (<see cref="RoundDefinition.SharedClassOf"/>, transponder to class),
/// written when the heat is built or put together, because only then is it
/// certain: the driver came from the Stock heat. Without an entry (a late
/// entry, a driver moved in by hand) the driver's profile decides - their
/// main class when it is one of the heat's, otherwise their first other
/// class that is - and failing that the heat's main class. When the race is
/// saved, each result is stamped with the class it ran in
/// (<see cref="SessionResultEntry.ClassName"/>), so the championship, which
/// only ever sees saved results, can split a shared race without the
/// program to hand, and a later edit to the heat cannot rewrite who a past
/// result was scored for.
///
/// HOW A SHARED HEAT IS SCORED. It counts for EACH of its classes, with
/// only that class's drivers' results - so to Stock it is a three-car heat
/// and to Mod a four-car one. Qualifying, mains seeding, standings and the
/// championships all read it that way; positions within a class close up
/// behind the other class's cars exactly as they do behind a ghost.
/// </summary>
public static class MixedHeats
{
    /// <summary>"Stock + Mod" - how a shared round's classes are written everywhere it is described.</summary>
    public const string Joiner = " + ";

    /// <summary>True when more than one class runs in this round. False for every round saved before 11 Nov 2026.</summary>
    public static bool IsShared(RoundDefinition? round) =>
        round?.SharedClasses is { Count: > 0 } others
        && others.Any(c => !string.IsNullOrWhiteSpace(c) && !Same(c, round.ClassName));

    /// <summary>The round's classes, main class first, each once. Just the main class for a one-class round.</summary>
    public static IReadOnlyList<string> ClassesOf(RoundDefinition round)
    {
        var list = new List<string>();

        void Add(string? name)
        {
            var trimmed = name?.Trim() ?? string.Empty;
            if (trimmed.Length > 0 && !list.Any(c => Same(c, trimmed)))
            {
                list.Add(trimmed);
            }
        }

        Add(round.ClassName);

        foreach (var other in round.SharedClasses ?? new List<string>())
        {
            Add(other);
        }

        return list;
    }

    /// <summary>
    /// Whether this class runs in the round - its main class, or one sharing
    /// it. The test every "rounds of this class" list uses
    /// (RoundDefinitionStore.ForClass and the program's day lists), so a
    /// shared heat is one of Stock's heats AND one of Mod's.
    /// </summary>
    public static bool RunsClass(RoundDefinition round, string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
        {
            return false;
        }

        if (Same(round.ClassName, className))
        {
            return true;
        }

        return round.SharedClasses is { Count: > 0 } others && others.Any(c => Same(c, className));
    }

    /// <summary>"Stock + Mod" for a shared round, the class name otherwise - for labels, the heat sheet, the Big Screen, the phone and the grid call.</summary>
    public static string ClassLabel(RoundDefinition round) =>
        IsShared(round) ? string.Join(Joiner, ClassesOf(round)) : round.ClassName;

    /// <summary>"Stock and Mod" - the classes as the announcer says them; a "+" read aloud is "plus". The class name for a one-class round.</summary>
    public static string SpokenClasses(RoundDefinition round)
    {
        if (!IsShared(round))
        {
            return round.ClassName;
        }

        var classes = ClassesOf(round);
        return classes.Count == 2
            ? $"{classes[0]} and {classes[1]}"
            : string.Join(", ", classes.Take(classes.Count - 1)) + " and " + classes[^1];
    }

    /// <summary>
    /// The class a driver runs in in this round. The round's own record
    /// first, then their profile, then the round's main class - see the
    /// class notes. Always the main class for a one-class round.
    /// </summary>
    public static string ClassOfDriver(RoundDefinition round, string transponder, ProfileStore? profiles = null) =>
        ClassOfDriverBy(round, transponder, profiles is null ? null : key => profiles.Get(profiles.PrimaryForAlternate(key) ?? key));

    /// <summary><see cref="ClassOfDriver"/>, with the profile found by whatever lookup the caller can use on its thread.</summary>
    private static string ClassOfDriverBy(RoundDefinition round, string transponder, Func<string, DriverProfile?>? profileOf)
    {
        if (!IsShared(round))
        {
            return round.ClassName;
        }

        var classes = ClassesOf(round);
        var key = transponder?.Trim() ?? string.Empty;

        if (round.SharedClassOf is { Count: > 0 } map
            && map.FirstOrDefault(kv => string.Equals(kv.Key?.Trim(), key, StringComparison.OrdinalIgnoreCase)).Value is { } recorded
            && classes.FirstOrDefault(c => Same(c, recorded)) is { } known)
        {
            return known;
        }

        if (profileOf is not null && key.Length > 0)
        {
            if (profileOf(key) is { } profile)
            {
                var fromProfile = ProfileClasses(profile).FirstOrDefault(p => classes.Any(c => Same(c, p)));
                if (fromProfile is not null)
                {
                    return classes.First(c => Same(c, fromProfile));
                }
            }
        }

        return classes[0];
    }

    /// <summary>
    /// Whether a result in this round counts for <paramref name="className"/>.
    /// Always true in a one-class round - every result counts, as it always
    /// did. In a shared round, only that class's drivers': the class the
    /// result was stamped with when saved, or failing that
    /// <see cref="ClassOfDriver"/>.
    /// </summary>
    public static bool Counts(RoundDefinition round, string className, string transponder, string? stamped = null, ProfileStore? profiles = null)
    {
        if (!IsShared(round))
        {
            return true;
        }

        var runsIn = !string.IsNullOrWhiteSpace(stamped) && RunsClass(round, stamped)
            ? stamped!
            : ClassOfDriver(round, transponder, profiles);

        return Same(runsIn, className);
    }

    /// <summary>The round's drivers who run in <paramref name="className"/>, in roster order. Everyone for a one-class round.</summary>
    public static IReadOnlyList<string> DriversOfClass(RoundDefinition round, string className, ProfileStore? profiles = null) =>
        round.AssignedTransponders
            .Where(t => Counts(round, className, t, null, profiles))
            .ToList();

    /// <summary>
    /// Writes each result's class onto it when the round is shared - see the
    /// class notes. Leaves a one-class round's results untouched (null), so
    /// a race saved from an ordinary heat is byte-for-byte what it always was.
    /// </summary>
    public static void StampClasses(IEnumerable<SessionResultEntry> results, RoundDefinition? round, ProfileStore? profiles = null)
    {
        if (round is null || !IsShared(round))
        {
            return;
        }

        foreach (var result in results)
        {
            if (string.IsNullOrWhiteSpace(result.ClassName) || !RunsClass(round, result.ClassName))
            {
                result.ClassName = ClassOfDriver(round, result.Transponder, profiles);
            }
        }
    }

    /// <summary>
    /// A class was renamed: moves the stamps on a shared heat's saved result
    /// to the new name (bug hunt 17). The round has its new name by now, so
    /// a stamp still saying the old one - no longer one of the round's
    /// classes - is the renamed class's. Left alone, the renamed class's
    /// championship found no row stamped with its name and scored the heat
    /// as nothing (<see cref="ScoringFields"/>). The same call with the names
    /// swapped is the undo. True when a stamp changed.
    /// </summary>
    public static bool RenameStamps(IEnumerable<SessionResultEntry> results, RoundDefinition? round, string oldName, string newName)
    {
        if (round is null || string.IsNullOrWhiteSpace(oldName) || RunsClass(round, oldName)
            || ClassesOf(round).FirstOrDefault(c => Same(c, newName)) is not { } renamed)
        {
            return false;
        }

        var changed = false;

        foreach (var result in results)
        {
            if (Same(result.ClassName, oldName))
            {
                result.ClassName = renamed;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// A race's results split into the fields that are scored against each
    /// other, for the championship (SeasonStore.ComputeStandings), which
    /// only has the saved results to go on.
    ///
    /// No result stamped with a class - every race but a shared heat's - is
    /// one field, the whole result, exactly as before. A shared heat's
    /// results are split by their stamp. For a championship of one class
    /// (<paramref name="seasonClass"/>) only that class's field is kept, so
    /// a Stock series scores the Stock cars of a shared heat and nothing
    /// else; for one that counts every class, each class's field is scored
    /// on its own, so the Mod winner is first in Mod rather than fourth in a
    /// mixed table. Results keep their order (by position) inside a field.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<SessionResultEntry>> ScoringFields(IReadOnlyList<SessionResultEntry> results, string? seasonClass)
    {
        if (!results.Any(r => !string.IsNullOrWhiteSpace(r.ClassName)))
        {
            return new[] { results };
        }

        // An unstamped row beside stamped ones (a driver added by hand in
        // Edit Result) goes with the first stamped class - the heat's main
        // class, which StampClasses writes first in a heat's own order.
        var fallback = results.First(r => !string.IsNullOrWhiteSpace(r.ClassName)).ClassName!.Trim();

        string ClassOf(SessionResultEntry r) => string.IsNullOrWhiteSpace(r.ClassName) ? fallback : r.ClassName.Trim();

        if (!string.IsNullOrWhiteSpace(seasonClass))
        {
            return new[] { (IReadOnlyList<SessionResultEntry>)results.Where(r => Same(ClassOf(r), seasonClass)).ToList() };
        }

        return results
            .GroupBy(ClassOf, StringComparer.OrdinalIgnoreCase)
            .Select(g => (IReadOnlyList<SessionResultEntry>)g.ToList())
            .ToList();
    }

    /// <summary>
    /// Each row of a saved result with the class it raced in and its place
    /// within that class (13 Nov 2026, mixed-class results) - for Race
    /// History, the printed sheet, the export and the phones, which list a
    /// shared heat as it finished with each driver's class place beside the
    /// overall one. Places run 1..N inside each class in finishing order, the
    /// same split the championship scores (ScoringFields). For a race with
    /// no stamped rows - every race not from a shared heat - the class is
    /// the round's (or null without one) and the place is null: the overall
    /// position is the only place there is.
    /// </summary>
    public static IReadOnlyDictionary<SessionResultEntry, (string? Class, int? Place)> ClassPlacesOf(
        IReadOnlyList<SessionResultEntry> results, RoundDefinition? round)
    {
        var map = new Dictionary<SessionResultEntry, (string?, int?)>(ReferenceEqualityComparer.Instance);
        var stamped = results.Any(r => !string.IsNullOrWhiteSpace(r.ClassName));

        if (!stamped)
        {
            foreach (var r in results)
            {
                map[r] = (string.IsNullOrWhiteSpace(round?.ClassName) ? null : round!.ClassName, null);
            }

            return map;
        }

        foreach (var field in ScoringFields(results, null))
        {
            var place = 0;

            foreach (var r in field.OrderBy(r => r.Position <= 0 ? int.MaxValue : r.Position))
            {
                place++;
                map[r] = (field.First(f => !string.IsNullOrWhiteSpace(f.ClassName)).ClassName!.Trim(), place);
            }
        }

        return map;
    }

    /// <summary>"3rd" - a place as the results print it.</summary>
    public static string Ordinal(int n) => (n % 100) is 11 or 12 or 13
        ? $"{n}th"
        : (n % 10) switch
        {
            1 => $"{n}st",
            2 => $"{n}nd",
            3 => $"{n}rd",
            _ => $"{n}th",
        };

    /// <summary>
    /// The result as one class saw it: only that class's rows of a shared
    /// heat, in finishing order, with their positions renumbered 1..N.
    /// Copies - the saved result is never touched. A one-class result comes
    /// back unchanged. For the recap, the event report and the class
    /// results on the relay, which list a class's finishing order.
    /// </summary>
    public static IReadOnlyList<SessionResultEntry> ClassResults(IReadOnlyList<SessionResultEntry> results, RoundDefinition? round, string className, ProfileStore? profiles = null)
    {
        if (round is null || !IsShared(round))
        {
            return results;
        }

        var rows = results
            .Where(r => Counts(round, className, r.Transponder, r.ClassName, profiles))
            .OrderBy(r => r.Position <= 0 ? int.MaxValue : r.Position)
            .Select(Clone)
            .ToList();

        for (var i = 0; i < rows.Count; i++)
        {
            rows[i].Position = i + 1;
        }

        return rows;
    }

    /// <summary>
    /// Each driver's place within their own class in a shared heat, by
    /// transponder - for the announcer, which calls positions within each
    /// class where it calls positions. Empty for a one-class round, which
    /// is the signal to call overall positions as always.
    /// </summary>
    public static IReadOnlyDictionary<string, int> ClassPlaces(IReadOnlyList<(string Transponder, int Position)> order, RoundDefinition? round, ProfileStore? profiles = null)
    {
        var places = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (round is null || !IsShared(round))
        {
            return places;
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var (transponder, _) in order.OrderBy(o => o.Position <= 0 ? int.MaxValue : o.Position))
        {
            var cls = ClassOfDriver(round, transponder, profiles);
            counts[cls] = counts.GetValueOrDefault(cls) + 1;
            places[transponder] = counts[cls];
        }

        return places;
    }

    /// <summary>
    /// Which class each car of the race on track runs in, for the announcer
    /// - null for a one-class race, which it calls exactly as always.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? ClassMapFor(RoundDefinition? round, ProfileStore? profiles = null)
    {
        if (round is null || !IsShared(round))
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transponder in round.AssignedTransponders)
        {
            map[transponder.Trim()] = ClassOfDriver(round, transponder, profiles);
        }

        return map;
    }

    /// <summary>
    /// <see cref="ClassMapFor(RoundDefinition?, ProfileStore?)"/> for a reader
    /// off the UI thread - the announcer (bug hunt 17). A driver put in the
    /// heat by hand has no entry in the heat's own record, so their profile
    /// decides, as it does when the result is saved; without it the announcer
    /// called them in the heat's main class and the results did not.
    /// <paramref name="drivers"/> is ProfileStore.AnyThread, whose profiles
    /// are the live objects: their strings are safe to read here, and their
    /// list of other classes is read as a copy that cannot throw (ProfileClasses).
    /// </summary>
    public static IReadOnlyDictionary<string, string>? ClassMapFor(RoundDefinition? round, IDriverLookup drivers)
    {
        if (round is null || !IsShared(round))
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transponder in round.AssignedTransponders)
        {
            map[transponder.Trim()] = ClassOfDriverBy(round, transponder, drivers.Get);
        }

        return map;
    }

    /// <summary>
    /// The announcer's line at the flag of a shared race (11 Nov 2026,
    /// Phase 4.1): each class's winner, in the order they finished - "Sam
    /// wins Stock, Alex wins Mod." The overall winner's own call has already
    /// been said by then; this is what stops a Stock driver who finished
    /// fourth overall from going unnamed when they won their class. Null when
    /// there is nothing to say (one class on track, or nobody with a lap).
    /// </summary>
    /// <param name="finishOrder">The cars in finishing order, transponders first to last.</param>
    /// <param name="classOf">Which class each car runs in (<see cref="ClassMapFor"/>).</param>
    /// <param name="nameOf">The spoken name for a transponder.</param>
    public static string? ClassWinnersLine(IReadOnlyList<string> finishOrder, IReadOnlyDictionary<string, string>? classOf, Func<string, string> nameOf)
    {
        if (classOf is null || classOf.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
        {
            return null;
        }

        var winners = new List<(string Class, string Name)>();

        foreach (var transponder in finishOrder)
        {
            if (!classOf.TryGetValue(transponder.Trim(), out var cls) || winners.Any(w => Same(w.Class, cls)))
            {
                continue;
            }

            winners.Add((cls, nameOf(transponder)));
        }

        if (winners.Count < 2)
        {
            return null;
        }

        return string.Join(", ", winners.Select(w => $"{w.Name} wins {w.Class}")) + ".";
    }

    /// <summary>
    /// The recap's fact for a shared race (11 Nov 2026, Phase 4.1): "Two
    /// classes shared this race and each is scored on its own. Stock: 1st
    /// Sam, 2nd Jo. Mod: 1st Alex." - from the classes stamped on the saved
    /// result, top three of each. Null for a race with no stamps, which is
    /// every one-class race, so the recap's facts are unchanged for them.
    /// </summary>
    public static string? ClassWinnersFact(IReadOnlyList<SessionResultEntry> results, Func<string, string> nameOf)
    {
        var stamped = results.Where(r => !string.IsNullOrWhiteSpace(r.ClassName) && !GhostService.IsGhost(r.Transponder)).ToList();

        if (stamped.Select(r => r.ClassName!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
        {
            return null;
        }

        var parts = stamped
            .OrderBy(r => r.Position <= 0 ? int.MaxValue : r.Position)
            .GroupBy(r => r.ClassName!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key}: " + string.Join(", ", g.Take(3).Select((r, i) => $"{Ordinal(i + 1)} {nameOf(r.Transponder)}")) + ".");

        return $"{Plural.Of(stamped.Select(r => r.ClassName!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count(), "class", "classes")} shared this race and each is scored on its own, "
               + "so call each class's places within it. " + string.Join(" ", parts);
    }

    // ------------------------------------------------------------------
    // Run Together - two heats put into one race by hand.
    // ------------------------------------------------------------------

    /// <summary>
    /// Why two heats cannot run together, in words for the director, or
    /// null when they can. Both must be qualifiers still to run and off the
    /// track - mains are seeded per class and a recorded heat has a result
    /// that belongs to it alone. They must not share a class (two Stock heats
    /// are one class's business: Move Drivers Between Heats), nor a driver
    /// (one person, one car, one race), and must be in the same qualifying
    /// round, or one class's Q2 would be filed as its Q1.
    /// </summary>
    public static string? WhyNotTogether(RoundDefinition a, RoundDefinition b, string? onTrackRoundId = null)
    {
        if (string.Equals(a.Id, b.Id, StringComparison.Ordinal))
        {
            return "Pick two different heats to run together.";
        }

        if (a.Kind != RoundKind.Qualifier || b.Kind != RoundKind.Qualifier)
        {
            return "Only qualifying heats can run together. Mains are seeded and scored one class at a time.";
        }

        if (a.IsRecorded || b.IsRecorded)
        {
            return "One of these heats has already been run, so its result belongs to it alone.";
        }

        if (onTrackRoundId is { Length: > 0 }
            && (string.Equals(a.Id, onTrackRoundId, StringComparison.Ordinal) || string.Equals(b.Id, onTrackRoundId, StringComparison.Ordinal)))
        {
            return "One of these heats is on track now. Put them together once it has been recorded.";
        }

        var aClasses = ClassesOf(a);
        var bClasses = ClassesOf(b);

        if (aClasses.Any(c => bClasses.Any(d => Same(c, d))))
        {
            return "These heats share a class. To move drivers between heats of one class, use Move Drivers Between Heats.";
        }

        var both = a.AssignedTransponders.Select(t => t.Trim())
            .Intersect(b.AssignedTransponders.Select(t => t.Trim()), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (both.Count > 0)
        {
            return $"{Plural.Of(both.Count, "driver")} {(both.Count == 1 ? "is" : "are")} in both heats, and nobody can drive two cars in one race.";
        }

        if (!string.Equals(HeatBalanceForm.QualifyingRoundOf(a), HeatBalanceForm.QualifyingRoundOf(b), StringComparison.OrdinalIgnoreCase))
        {
            return $"\"{a.RoundLabel}\" and \"{b.RoundLabel}\" are in different qualifying rounds, so each class would lose track of which round the race counted for.";
        }

        return null;
    }

    /// <summary>
    /// Puts <paramref name="fold"/>'s drivers and classes into
    /// <paramref name="keep"/>, which becomes the shared heat; the caller
    /// then deletes <paramref name="fold"/>. Changes <paramref name="keep"/>
    /// in place and nothing else. Every driver's class is written down (see
    /// the class notes) so the split never depends on a profile.
    ///
    /// The caller checks <see cref="WhyNotTogether"/> first; the program
    /// takes the earlier heat as <paramref name="keep"/>, so the race keeps
    /// its slot and its name.
    /// </summary>
    public static void RunTogether(RoundDefinition keep, RoundDefinition fold, ProfileStore? profiles = null)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in keep.AssignedTransponders)
        {
            map[t] = ClassOfDriver(keep, t, profiles);
        }

        foreach (var t in fold.AssignedTransponders)
        {
            map[t] = ClassOfDriver(fold, t, profiles);
        }

        var others = ClassesOf(keep).Skip(1).Concat(ClassesOf(fold))
            .Where(c => !Same(c, keep.ClassName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        keep.SharedClasses = others;
        keep.SharedClassOf = map;
        keep.AssignedTransponders = keep.AssignedTransponders
            .Concat(fold.AssignedTransponders)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>What Undo needs to take a Run Together apart: the kept heat as it was, and the heat folded into it.</summary>
    public sealed record RunTogetherUndo(RoundDefinition KeptBefore, RoundDefinition Folded);

    /// <summary>
    /// Run Together on the program (the right-click menu): the earlier of the
    /// two heats becomes the shared one, keeping its slot and name, and the
    /// later is deleted. Null, with <paramref name="refusal"/> saying why,
    /// when they cannot run together (<see cref="WhyNotTogether"/>) - and
    /// then nothing has changed. The race day's own list of rounds is the
    /// caller's to update, since this only knows the round store.
    /// </summary>
    public static RunTogetherUndo? RunTogetherIn(RoundDefinitionStore rounds, RoundDefinition a, RoundDefinition b,
        ProfileStore? profiles, string? onTrackRoundId, out string? refusal)
    {
        refusal = WhyNotTogether(a, b, onTrackRoundId);

        if (refusal is not null)
        {
            return null;
        }

        var keep = a.Order <= b.Order ? a : b;
        var fold = ReferenceEquals(keep, a) ? b : a;

        var undo = new RunTogetherUndo(Snapshot(keep), Snapshot(fold));

        RunTogether(keep, fold, profiles);
        rounds.Update(keep);
        rounds.Delete(fold.Id);

        return undo;
    }

    /// <summary>
    /// The undo of <see cref="RunTogetherIn"/>: the kept heat goes back to
    /// exactly what it was, and the folded heat comes back in its own slot
    /// (RoundDefinitionStore.Restore keeps its Order). Refused - false, with
    /// the reason - once the shared heat has been run, because its result
    /// is one race and cannot be split back into two; a heat deleted since
    /// is not brought back either.
    /// </summary>
    public static bool UndoRunTogether(RoundDefinitionStore rounds, RunTogetherUndo undo, out string? refusal)
    {
        var current = rounds.Get(undo.KeptBefore.Id);

        if (current is null)
        {
            refusal = $"\"{undo.KeptBefore.RoundLabel}\" has been deleted since, so there is nothing to take apart.";
            return false;
        }

        if (current.IsRecorded)
        {
            refusal = $"\"{current.RoundLabel}\" has been run as one race since, and its result cannot be split back into two heats.";
            return false;
        }

        // Its place in the running order is kept as it is NOW: a reorder
        // since then is the director's, and is not undone with this.
        var order = current.Order;
        CopyInto(undo.KeptBefore, current);
        current.Order = order;
        rounds.Update(current);
        rounds.Restore(new[] { Snapshot(undo.Folded) });

        refusal = null;
        return true;
    }

    /// <summary>A deep copy of a round, for an undo to put back exactly - Run Together changes the kept heat in place.</summary>
    public static RoundDefinition Snapshot(RoundDefinition round) =>
        JsonSerializer.Deserialize<RoundDefinition>(JsonSerializer.Serialize(round))!;

    /// <summary>Copies <paramref name="from"/>'s every saved field onto <paramref name="to"/>, keeping the object (others may hold it).</summary>
    public static void CopyInto(RoundDefinition from, RoundDefinition to)
    {
        var copy = Snapshot(from);
        foreach (var property in typeof(RoundDefinition).GetProperties().Where(p => p.CanRead && p.CanWrite))
        {
            property.SetValue(to, property.GetValue(copy));
        }
    }

    // ------------------------------------------------------------------
    // The build - classes too small for a heat of their own.
    // ------------------------------------------------------------------

    /// <summary>
    /// One qualifying round's heats, with the small classes put together:
    /// any class whose WHOLE round is a single heat of fewer than
    /// <paramref name="under"/> drivers shares a heat with another such class,
    /// as long as the two together fit in <paramref name="heatSize"/> and no
    /// driver is in both. The smallest are paired first, and a pair can take
    /// a third small class while there is room - three tiny classes make one
    /// race. Heats that are not combined come back as they were, in order;
    /// a combined heat sits where its first class's heat was.
    ///
    /// <paramref name="under"/> zero or less is the option turned off, and
    /// the heats come back untouched - the default for every race day.
    /// </summary>
    public static List<RoundDefinition> CombineSmallClasses(IReadOnlyList<RoundDefinition> roundHeats, int under, int heatSize)
    {
        var result = roundHeats.ToList();

        if (under <= 0 || roundHeats.Count < 2)
        {
            return result;
        }

        // Only a class with exactly one heat in this round: splitting a
        // class that already needs two heats across a shared one would make
        // its own heats lopsided for no gain.
        var small = roundHeats
            .Where(h => !IsShared(h)
                        && h.AssignedTransponders.Count > 0
                        && h.AssignedTransponders.Count < under
                        && roundHeats.Count(o => Same(o.ClassName, h.ClassName)) == 1)
            .OrderBy(h => h.AssignedTransponders.Count)
            .ThenBy(h => result.IndexOf(h))
            .ToList();

        var used = new HashSet<RoundDefinition>(ReferenceEqualityComparer.Instance);

        foreach (var first in small)
        {
            if (used.Contains(first))
            {
                continue;
            }

            var group = new List<RoundDefinition> { first };
            var size = first.AssignedTransponders.Count;

            foreach (var next in small)
            {
                if (used.Contains(next) || group.Contains(next))
                {
                    continue;
                }

                if (size + next.AssignedTransponders.Count > heatSize)
                {
                    continue;
                }

                if (group.Any(g => g.AssignedTransponders.Intersect(next.AssignedTransponders, StringComparer.OrdinalIgnoreCase).Any()))
                {
                    continue;
                }

                group.Add(next);
                size += next.AssignedTransponders.Count;
            }

            if (group.Count < 2)
            {
                continue;
            }

            // The heat that comes first in the round keeps its place, its
            // name and its main class.
            var ordered = group.OrderBy(g => result.IndexOf(g)).ToList();
            var keep = ordered[0];

            foreach (var fold in ordered.Skip(1))
            {
                RunTogether(keep, fold);
                result.Remove(fold);
            }

            foreach (var g in group)
            {
                used.Add(g);
            }
        }

        return result;
    }

    /// <summary>
    /// For the curfew planner (NightPlan.FixesFor): the pairs of classes
    /// that could share their heats, smallest first - classes running ONE
    /// heat a round whose rosters fit in a heat together. Each pair saves
    /// one heat per qualifying round still to run.
    /// </summary>
    public static IReadOnlyList<(string A, string B, int HeatsSaved)> ShareableClasses(IReadOnlyList<RoundDefinition> unrunQualifiers, int heatSize)
    {
        var perClass = unrunQualifiers
            .Where(r => r.Kind == RoundKind.Qualifier && !r.IsRecorded && !IsShared(r) && !string.IsNullOrWhiteSpace(r.ClassName))
            .GroupBy(r => r.ClassName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Class: g.Key, Heats: g.ToList()))
            .ToList();

        // A class whose rounds are each one heat: as many heats as rounds.
        var single = perClass
            .Where(c => c.Heats.GroupBy(h => HeatBalanceForm.QualifyingRoundOf(h) ?? h.Id).All(r => r.Count() == 1))
            .Select(c => (c.Class, c.Heats, Size: c.Heats.Max(h => h.AssignedTransponders.Count)))
            .Where(c => c.Size > 0)
            .OrderBy(c => c.Size)
            .ToList();

        var pairs = new List<(string, string, int)>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var a in single)
        {
            if (taken.Contains(a.Class))
            {
                continue;
            }

            foreach (var b in single)
            {
                if (Same(a.Class, b.Class) || taken.Contains(b.Class) || a.Size + b.Size > heatSize)
                {
                    continue;
                }

                var rounds = a.Heats.Select(h => HeatBalanceForm.QualifyingRoundOf(h))
                    .Intersect(b.Heats.Select(h => HeatBalanceForm.QualifyingRoundOf(h)))
                    .Count();

                if (rounds == 0)
                {
                    continue;
                }

                pairs.Add((a.Class, b.Class, rounds));
                taken.Add(a.Class);
                taken.Add(b.Class);
                break;
            }
        }

        return pairs;
    }

    /// <summary>
    /// The driver's classes, main class first, as a list of its own.
    ///
    /// WHY IT IS A COPY TAKEN BY INDEX (bug hunt 17): the announcer reads
    /// this off the UI thread (<see cref="ClassMapFor(RoundDefinition?, IDriverLookup)"/>),
    /// and DriverProfile.Classes is a list the UI thread edits in place -
    /// ticking a class at check-in (CheckInControl.SetRunsClass). Walking it
    /// with foreach while it changed threw "Collection was modified" into
    /// the announcer's position report. Read by index, a change underneath
    /// can at worst run past the end, which is caught: the classes read so
    /// far stand - the main class always, since it is a string - and the
    /// next call, a moment later, reads the list as it settled.
    /// </summary>
    private static IReadOnlyList<string> ProfileClasses(DriverProfile profile)
    {
        var found = new List<string>();

        if (profile.ClassName is { } main && !string.IsNullOrWhiteSpace(main))
        {
            found.Add(main.Trim());
        }

        try
        {
            // One read of the property: the undo swaps in a whole new list.
            var others = profile.Classes;

            for (var i = 0; others is not null && i < others.Count; i++)
            {
                var other = others[i];

                if (!string.IsNullOrWhiteSpace(other))
                {
                    found.Add(other.Trim());
                }
            }
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidOperationException)
        {
            // The list changed under the read - see above.
        }

        return found;
    }

    private static SessionResultEntry Clone(SessionResultEntry entry) =>
        JsonSerializer.Deserialize<SessionResultEntry>(JsonSerializer.Serialize(entry)) ?? new SessionResultEntry();

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
