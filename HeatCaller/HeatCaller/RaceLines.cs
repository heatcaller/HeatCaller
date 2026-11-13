using System.Globalization;

namespace HeatCaller;

/// <summary>
/// A driver's own lines for one race (11 Nov 2026, race-night build plan
/// item 7.4, "mid-pack race lines"): "Best lap 0.31s quicker than your Heat
/// 1", "Held off Sam for 4 laps", "Best Stock finish this year".
///
/// WHY. The night page, the report card and the night email were written
/// for the front of the field: a win, a podium, a best lap of the night.
/// A driver who finished 5th of 8 for the third week running got their
/// place, their laps and nothing that said the night went anywhere - and
/// mid-pack is most of the field. Every one of them has a small true thing
/// in their race worth hearing, and the laptop already holds what proves it.
///
/// EVERY LINE IS CHECKABLE AND ONLY GOOD NEWS. The driver was there, so each
/// line is plain arithmetic over the filed results, with a floor under it
/// so a rounding wobble is never news: a lap at least
/// <see cref="QuickerByMs"/> quicker, a car kept behind within
/// <see cref="HeldOffWithinMs"/> at the line for at least
/// <see cref="HeldOffLaps"/> laps running to the flag, a finish better than
/// every one of at least <see cref="YearRaces"/> earlier races in the class.
/// Nothing is said for a slower lap or a lost place: the report card already
/// carries the honest numbers, and a line that rubs it in is not what this
/// is for. No line at all is the usual answer, and that is fine.
///
/// "THIS YEAR", NOT "THIS SEASON". A club's championship need not run with
/// the calendar, and the results do not say which season a race belonged
/// to, so the claim made is the one a driver can check: the calendar year.
///
/// OTHER DRIVERS BY FIRST NAME, the way a driver says it at the pits - "held
/// off Sam" - and by the full name only when two in the race share it.
/// Never a ghost (a recording, not a rival) or a car nobody named.
///
/// PURE: no clock, no disk, no settings. DriverNightExtras.Races puts the
/// lines on each race of the night document (`lines`, the Me page's race
/// card), and NightEmail prints them under RACE BY RACE.
/// </summary>
internal static class RaceLines
{
    /// <summary>A best lap must be at least this much quicker than the earlier race's to be said.</summary>
    internal const long QuickerByMs = 50;

    /// <summary>The car behind must be within this at the line, lap after lap, to have been "held off".</summary>
    internal const long HeldOffWithinMs = 1000;

    /// <summary>The fewest laps running to the flag that count as holding somebody off.</summary>
    internal const int HeldOffLaps = 3;

    /// <summary>The fewest earlier races in the class this year before "best finish this year" means anything.</summary>
    internal const int YearRaces = 3;

    /// <summary>The most lines for one race.</summary>
    internal const int MostLines = 3;

    /// <summary>
    /// The lines for one driver's race - the most interesting first, at
    /// most <see cref="MostLines"/>, and usually none.
    /// </summary>
    /// <param name="transponder">The car they raced it in. Only used to find their rows - never written.</param>
    /// <param name="race">The race.</param>
    /// <param name="tonight">Tonight's races, for "quicker than your Heat 1". The race itself may be among them.</param>
    /// <param name="history">Every filed race, tonight's included or not, for "best finish this year". Null for none.</param>
    /// <param name="classOf">A race's class name, or empty when it has none.</param>
    /// <param name="labelOf">A race's name as the night page shows it ("Heat 1").</param>
    internal static IReadOnlyList<string> For(
        string? transponder,
        SessionRecord? race,
        IEnumerable<SessionRecord>? tonight,
        IEnumerable<SessionRecord>? history,
        Func<SessionRecord, string> classOf,
        Func<SessionRecord, string> labelOf)
    {
        var who = transponder?.Trim();

        if (string.IsNullOrEmpty(who) || race?.Results is null || !Counts(race))
        {
            return Array.Empty<string>();
        }

        var row = RowOf(race, who);

        if (row is null)
        {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        var className = (classOf(race) ?? string.Empty).Trim();

        if (BestFinishLine(who, race, row, className, tonight, history, classOf) is { } finish)
        {
            lines.Add(finish);
        }

        if (HeldOffLine(race, row) is { } held)
        {
            lines.Add(held);
        }

        if (QuickerLine(who, race, row, className, tonight, classOf, labelOf) is { } quicker)
        {
            lines.Add(quicker);
        }

        return lines.Take(MostLines).ToList();
    }

    /// <summary>
    /// "Held off Sam for 4 laps": the car that finished right behind them
    /// was within <see cref="HeldOffWithinMs"/> at the line on each of the
    /// last laps, running to the flag. Null for fewer than
    /// <see cref="HeldOffLaps"/>, a ghost, or nobody named.
    /// </summary>
    internal static string? HeldOffLine(SessionRecord race, SessionResultEntry row)
    {
        if (row.Position < 1 || IsOut(row))
        {
            return null;
        }

        var behind = race.Results.FirstOrDefault(r =>
            r is not null && r.Position == row.Position + 1 && !Ghosts.IsGhost(r.Transponder));

        if (behind is null || NameOf(race, behind) is not { } name)
        {
            return null;
        }

        // Both from the green. In a qualifying start, or a staggered start
        // on "own release", each car's lap times run from its own start, so
        // without its clock zero two cars half a lap apart all race read as
        // nose to tail.
        var mine = Crossings(row.LapTimesMs, row.ClockZeroMs);
        var theirs = Crossings(behind.LapTimesMs, behind.ClockZeroMs);
        var laps = 0;

        // Lap by lap back from their last crossing, while the car behind
        // crossed the same lap within the margin after them.
        for (var lap = mine.Count - 1; lap >= 1; lap--)
        {
            if (lap >= theirs.Count)
            {
                break;
            }

            var gap = theirs[lap] - mine[lap];

            if (gap <= 0 || gap > HeldOffWithinMs)
            {
                break;
            }

            laps++;
        }

        return laps >= HeldOffLaps
            ? string.Create(CultureInfo.InvariantCulture, $"Held off {name} for {laps} laps")
            : null;
    }

    /// <summary>
    /// "Best lap 0.31s quicker than your Heat 1": against their first race
    /// tonight in the same class on the same layout. Null when this is that
    /// race, it was not quicker by <see cref="QuickerByMs"/>, or either has
    /// no best lap.
    /// </summary>
    internal static string? QuickerLine(
        string who,
        SessionRecord race,
        SessionResultEntry row,
        string className,
        IEnumerable<SessionRecord>? tonight,
        Func<SessionRecord, string> classOf,
        Func<SessionRecord, string> labelOf)
    {
        if (className.Length == 0 || row.FastLapMs <= 0 || tonight is null)
        {
            return null;
        }

        var start = RelayResultsIndex.EffectiveStart(race);
        var first = tonight
            .Where(s => s is not null && Counts(s) && s.Id != race.Id && RelayResultsIndex.EffectiveStart(s) < start)
            .Where(s => SameText(classOf(s), className) && SameText(s.TrackLayoutName, race.TrackLayoutName))
            .OrderBy(RelayResultsIndex.EffectiveStart)
            .Select(s => (Session: s, Row: RowOf(s, who)))
            .FirstOrDefault(x => x.Row is { FastLapMs: > 0 });

        if (first.Row is null)
        {
            return null;
        }

        var quicker = first.Row.FastLapMs - row.FastLapMs;

        if (quicker < QuickerByMs)
        {
            return null;
        }

        var label = (labelOf(first.Session) ?? string.Empty).Trim();
        var seconds = (quicker / 1000.0).ToString("0.00", CultureInfo.InvariantCulture);

        return label.Length > 0
            ? $"Best lap {seconds}s quicker than your {label}"
            : $"Best lap {seconds}s quicker than your first race tonight";
    }

    /// <summary>
    /// "Best Stock finish this year", or "First Stock win this year" for a
    /// win: better than every earlier race of theirs in the class this
    /// calendar year, with at least <see cref="YearRaces"/> of them to beat.
    /// </summary>
    internal static string? BestFinishLine(
        string who,
        SessionRecord race,
        SessionResultEntry row,
        string className,
        IEnumerable<SessionRecord>? tonight,
        IEnumerable<SessionRecord>? history,
        Func<SessionRecord, string> classOf)
    {
        if (className.Length == 0 || row.Position < 1 || IsOut(row))
        {
            return null;
        }

        var start = RelayResultsIndex.EffectiveStart(race);
        var year = start.Year;
        var seen = new HashSet<string>(StringComparer.Ordinal) { race.Id ?? string.Empty };
        var earlier = new List<int>();

        foreach (var s in (history ?? Enumerable.Empty<SessionRecord>()).Concat(tonight ?? Enumerable.Empty<SessionRecord>()))
        {
            if (s is null || !Counts(s) || !seen.Add(s.Id ?? string.Empty))
            {
                continue;
            }

            var at = RelayResultsIndex.EffectiveStart(s);

            if (at >= start || at.Year != year || !SameText(classOf(s), className))
            {
                continue;
            }

            if (RowOf(s, who) is { Position: > 0 } theirs)
            {
                earlier.Add(theirs.Position);
            }
        }

        if (earlier.Count < YearRaces || row.Position >= earlier.Min())
        {
            return null;
        }

        return row.Position == 1
            ? $"First {className} win this year"
            : $"Best {className} finish this year";
    }

    /// <summary>
    /// Each race's lines as the night email prints them: "Heat 2: Held off
    /// Sam for 4 laps". Races with none are left out.
    /// </summary>
    internal static IReadOnlyList<string> ForEmail(IEnumerable<NightRace>? races) =>
        (races ?? Enumerable.Empty<NightRace>())
            .Where(r => r is not null && r.Lines is { Count: > 0 })
            .SelectMany(r => r.Lines!.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => $"{r.Label}: {l.Trim()}"))
            .ToList();

    /// <summary>A race that counts: not a demo, not practice.</summary>
    private static bool Counts(SessionRecord s) =>
        !s.IsDemoSession() && s.SessionType != RaceSessionType.Practice && s.Results is not null;

    private static SessionResultEntry? RowOf(SessionRecord s, string who) =>
        s.Results?.FirstOrDefault(r => r is not null && string.Equals(r.Transponder?.Trim(), who, StringComparison.OrdinalIgnoreCase));

    private static bool IsOut(SessionResultEntry row)
    {
        var outcome = (row.Outcome ?? string.Empty).Trim();
        return string.Equals(outcome, ResultEditor.Dq, StringComparison.OrdinalIgnoreCase)
            || string.Equals(outcome, ResultEditor.Dnf, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameText(string? a, string? b) =>
        string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Time since the green at each crossing, the start lap first - the car's own clock zero (SessionResultEntry.ClockZeroMs) and then its laps.</summary>
    private static List<long> Crossings(IReadOnlyList<long>? lapTimes, long clockZeroMs)
    {
        var at = new List<long>();
        var sum = Math.Max(0, clockZeroMs);

        foreach (var ms in lapTimes ?? Array.Empty<long>())
        {
            sum += ms;
            at.Add(sum);
        }

        return at;
    }

    /// <summary>Their first name, or the whole name when another driver in the race shares it; null for nobody named.</summary>
    private static string? NameOf(SessionRecord race, SessionResultEntry row)
    {
        var full = (row.DriverName ?? string.Empty).Trim();

        if (full.Length == 0 || string.Equals(full, "Unnamed driver", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var first = FirstName(full);
        var shared = race.Results.Any(r =>
            r is not null && !ReferenceEquals(r, row) && !Ghosts.IsGhost(r.Transponder)
            && string.Equals(FirstName((r.DriverName ?? string.Empty).Trim()), first, StringComparison.OrdinalIgnoreCase));

        return shared ? full : first;
    }

    private static string FirstName(string full)
    {
        var space = full.IndexOf(' ');
        return space > 0 ? full[..space] : full;
    }
}
