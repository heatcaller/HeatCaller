namespace HeatCaller;

/// <summary>One line of the championship table as it would read if the race on track ended now.</summary>
/// <param name="Change">Places gained (positive) or lost (negative) against the table before this race; 0 for no change or a first appearance.</param>
/// <param name="New">Not in the table before this race - their first scoring round.</param>
/// <param name="InThisRace">On track in the race now.</param>
public sealed record LiveChampionRow(int Position, string DriverName, int Points, int Change, bool New, bool InThisRace);

/// <summary>The championship this race counts towards, as it would stand if the race ended now.</summary>
/// <param name="Headline">One line for a ticker: who would lead, and by how much - or who would take the lead from whom.</param>
public sealed record IfItEndedNow(string SeasonName, IReadOnlyList<LiveChampionRow> Rows, string Headline);

/// <summary>
/// "If it ended now": the championship table recomputed every couple of
/// seconds during a race that counts towards one, with the race's running
/// order scored as if the flag had just fallen.
///
/// The same two questions Championship.SwingFor asks after the flag - the
/// table with this race and without it - asked of a race that has not been
/// saved yet. The race is scored as the result Race Control would file
/// (RaceFiling.ResultsFrom), so a guest or a ghost drops out and the field
/// closes up exactly as it will when the result is saved, and the drop
/// rule ("best 5 of 7") is applied by the one place that applies it.
///
/// Read-only: nothing here is saved or said. It is shown on the Big Screen
/// and the phones while a race is on.
/// </summary>
public static class ChampionshipLive
{
    /// <summary>How many lines of the table are worth showing.</summary>
    public const int Rows = 8;

    /// <summary>The id the race on track is scored under, which no saved race can have.</summary>
    internal const string LiveSessionId = "if-it-ended-now";

    /// <summary>
    /// The first championship the race would count towards, as it would
    /// stand with <paramref name="results"/> as its result - or null when
    /// the race counts towards none, or nobody has a lap yet.
    ///
    /// UI thread: it reads the history, programme and event stores.
    /// </summary>
    /// <param name="round">The round the race is being run for, if any. A season's rule takes a race by its round's class and kind.</param>
    /// <param name="filedSessionId">The id of the saved race <paramref name="results"/> came from, when it has been filed already - see Candidates.</param>
    public static IfItEndedNow? Compute(
        IReadOnlyList<SessionResultEntry> results,
        RoundDefinition? round,
        SeasonStore? seasons,
        RoundDefinitionStore? rounds,
        RaceEventStore? events,
        SessionHistoryStore history,
        ProfileStore? profiles = null,
        string? filedSessionId = null) =>
        FromCandidates(Candidates(round, seasons, rounds, events, history, filedSessionId), results, round, profiles);

    /// <summary>
    /// The championships a race run for <paramref name="round"/> would count
    /// towards, each with the saved races it already counts. Nothing here
    /// changes while the race is on, so Race Control works it out once a race
    /// and asks only <see cref="FromCandidates"/> every couple of seconds -
    /// going through the whole history for every season each time is not
    /// something to do on the thread that is counting laps.
    /// </summary>
    /// <param name="filedSessionId">The saved race being scored again as the live one, which is left out of the counted races.</param>
    public static IReadOnlyList<(Season Season, IReadOnlyList<SessionRecord> Counted)> Candidates(
        RoundDefinition? round,
        SeasonStore? seasons,
        RoundDefinitionStore? rounds,
        RaceEventStore? events,
        SessionHistoryStore history,
        string? filedSessionId = null)
    {
        var list = new List<(Season, IReadOnlyList<SessionRecord>)>();

        if (seasons is null || rounds is null)
        {
            return list;
        }

        // Only the round matters to the rule - the results are not looked at.
        var probe = Live(Array.Empty<SessionResultEntry>(), round);

        foreach (var season in seasons.All)
        {
            // Only a season whose own rule would take this race: a race that
            // is not saved yet cannot have been ticked in by hand.
            if (!season.AutoInclude || !EventSpine.RuleAccepts(season, probe, rounds, events))
            {
                continue;
            }

            // A round being run again replaces the race it was filed under
            // before, so that one is left out of both tables.
            var counted = EventSpine.SessionsFor(season, events, rounds, history)
                .Where(s => !s.IsNotScored())
                .Where(s => round is null || !string.Equals(s.RoundId, round.Id, StringComparison.Ordinal))
                // A race already filed is left out by its own id too (6 Nov
                // 2026): a freeform race, or one whose round has since been
                // deleted, has no round to leave it out by, so it stayed in
                // the counted table AND came back as the live one, and the
                // championship check put its points to air doubled.
                .Where(s => filedSessionId is null || !string.Equals(s.Id, filedSessionId, StringComparison.Ordinal))
                .ToList();

            list.Add((season, counted));
        }

        return list;
    }

    /// <summary>The first of <paramref name="candidates"/> with a table to show, with <paramref name="results"/> as the race's result.</summary>
    public static IfItEndedNow? FromCandidates(
        IReadOnlyList<(Season Season, IReadOnlyList<SessionRecord> Counted)> candidates,
        IReadOnlyList<SessionResultEntry> results,
        RoundDefinition? round,
        ProfileStore? profiles = null)
    {
        if (!results.Any(r => r.Laps > 0))
        {
            return null;
        }

        var live = Live(results, round);

        foreach (var (season, counted) in candidates)
        {
            if (Table(season, counted, live, profiles) is { } table)
            {
                return table;
            }
        }

        return null;
    }

    /// <summary>The race on track as the result it would be filed as.</summary>
    internal static SessionRecord Live(IReadOnlyList<SessionResultEntry> results, RoundDefinition? round) => new()
    {
        Id = LiveSessionId,
        SessionType = RaceSessionType.Race,
        RoundId = round?.Id,
        StartedAtUtc = DateTime.UtcNow,
        Results = results.ToList(),
    };

    /// <summary>The table with and without <paramref name="live"/>, read off as the rows to show - apart for testing.</summary>
    internal static IfItEndedNow? Table(Season season, IReadOnlyList<SessionRecord> counted, SessionRecord live, ProfileStore? profiles)
    {
        var before = SeasonStore.ComputeStandings(season, counted, profiles);
        var after = SeasonStore.ComputeStandings(season, counted.Append(live).ToList(), profiles);

        if (after.Count == 0)
        {
            return null;
        }

        var onTrack = live.Results
            .Select(r => string.IsNullOrWhiteSpace(r.DriverName) ? r.Transponder : r.DriverName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int Was(string name) => before.FindIndex(e => string.Equals(e.DriverName, name, StringComparison.OrdinalIgnoreCase));

        var rows = after.Take(Rows).Select((entry, i) =>
        {
            var was = Was(entry.DriverName);

            return new LiveChampionRow(
                i + 1,
                entry.DriverName,
                entry.TotalPoints,
                was < 0 ? 0 : was - i,
                was < 0,
                onTrack.Contains(entry.DriverName));
        }).ToList();

        return new IfItEndedNow(season.Name, rows, Headline(season.Name, before, after));
    }

    /// <summary>"Sam would take the Winter Series lead from Alex", or "Sam would lead the Winter Series by 12".</summary>
    internal static string Headline(string seasonName, IReadOnlyList<SeasonStandingEntry> before, IReadOnlyList<SeasonStandingEntry> after)
    {
        var leader = after[0];
        var series = string.IsNullOrWhiteSpace(seasonName) ? "championship" : seasonName.Trim();

        // Level on points is level, whichever way the tie-break falls.
        if (after.Count > 1 && after[1].TotalPoints == leader.TotalPoints)
        {
            return $"{leader.DriverName} and {after[1].DriverName} would be level at the top of the {series}";
        }

        if (before.Count > 0 && !string.Equals(before[0].DriverName, leader.DriverName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{leader.DriverName} would take the {series} lead from {before[0].DriverName}";
        }

        if (after.Count == 1)
        {
            return $"{leader.DriverName} would lead the {series}";
        }

        return $"{leader.DriverName} would lead the {series} by {Plural.Of(leader.TotalPoints - after[1].TotalPoints, "point")}";
    }

    /// <summary>The Big Screen's ticker line: the headline, then the top of the table with each move.</summary>
    public static string Ticker(IfItEndedNow table, int shown = 5) =>
        $"IF IT ENDED NOW:  {table.Headline}   •   "
        + string.Join("   ", table.Rows.Take(shown).Select(r => $"{r.Position}. {r.DriverName} {r.Points}{Arrow(r)}"));

    /// <summary>" ▲2", " ▼1", " NEW" or nothing.</summary>
    public static string Arrow(LiveChampionRow row) =>
        row.New ? " NEW"
        : row.Change > 0 ? $" ▲{row.Change}"
        : row.Change < 0 ? $" ▼{-row.Change}"
        : string.Empty;
}
