using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Exports results in formats other tools can read.
///
/// A note on format choice: there is no single standard file layout that RC
/// timing software agrees on, so rather than guess at one vendor's
/// proprietary shape and produce something subtly wrong, this exports three
/// formats whose structure is fully specified here:
///
///  - Results CSV   - one row per driver per session. Opens in any
///                    spreadsheet and is the easiest thing to hand to
///                    someone who just wants the finishing order.
///  - Lap CSV       - one row per lap. The right shape for analysis, and
///                    what most pace-analysis tools expect.
///  - JSON          - everything, structured, for anyone writing their own
///                    tooling against it.
///
/// If a club needs a specific vendor layout, the shape is easy to add once
/// a real sample file is available to match against.
/// </summary>
public static class ResultsExporter
{
    public enum Format
    {
        ResultsCsv,
        LapCsv,
        Json,
    }

    public static string Describe(Format format) => format switch
    {
        Format.ResultsCsv => "Results CSV - one row per driver, per session",
        Format.LapCsv => "Lap CSV - one row per lap",
        _ => "JSON - complete structured data",
    };

    public static string Extension(Format format) => format == Format.Json ? "json" : "csv";

    /// <summary>
    /// The export's text. A CSV starts with a byte order mark character:
    /// written out as UTF-8 that becomes the marker Excel needs to read
    /// accented names correctly (see CsvHelper.FileEncoding), whichever way
    /// the caller writes the string.
    /// </summary>
    public static string Build(Format format, IReadOnlyList<SessionRecord> sessions,
        RoundDefinitionStore? rounds, ProfileStore profiles) => format switch
    {
        Format.ResultsCsv => ByteOrderMark + BuildResultsCsv(sessions, rounds, profiles),
        Format.LapCsv => ByteOrderMark + BuildLapCsv(sessions, rounds, profiles),
        _ => BuildJson(sessions, rounds, profiles),
    };

    private const string ByteOrderMark = "\uFEFF";

    private static string BuildResultsCsv(IReadOnlyList<SessionRecord> sessions,
        RoundDefinitionStore? rounds, ProfileStore profiles)
    {
        var sb = new StringBuilder();
        // Status last, so a spreadsheet or importer already reading the
        // columns before it by position keeps working. Without it a
        // disqualified driver came out as an unexplained no-lap row at the
        // bottom and a DNF as an ordinary finisher - the outcome Edit Result
        // set (ResultEditor puts it in Status) was lost on the way out.
        // ClassPosition last of all (13 Nov 2026, mixed-class results): a
        // driver's place within their own class in a shared heat, empty for
        // every other race. Class is the driver's own class there, not the
        // heat's main one.
        sb.AppendLine("SessionDate,SessionTime,SessionLabel,Round,Class,Track,Position,Driver,Transponder,Car,Laps,TotalTimeSeconds,BestLapSeconds,Status,LapsCountedByHand,ClassPosition");

        foreach (var session in sessions.OrderBy(s => s.StartedAtUtc))
        {
            var round = session.RoundId is null ? null : rounds?.Get(session.RoundId);
            var local = session.StartedAtUtc.ToLocalTime();

            var places = MixedHeats.ClassPlacesOf(session.Results, round);

            foreach (var result in session.Results.OrderBy(r => r.Position))
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    Csv(local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                    Csv(session.Label),
                    Csv(round?.RoundLabel ?? string.Empty),
                    Csv(places[result].Class ?? string.Empty),
                    Csv(session.TrackLayoutName),
                    result.Position.ToString(CultureInfo.InvariantCulture),
                    Csv(NameFor(result, profiles)),
                    Csv(result.Transponder),
                    Csv(result.CarModel),
                    result.Laps.ToString(CultureInfo.InvariantCulture),
                    Seconds(result.ElapsedMs),
                    result.FastLapMs > 0 ? Seconds(result.FastLapMs) : string.Empty,
                    Csv(result.Status),
                    // After Status for the same reason Status is last of
                    // the rest (22 Oct 2026) - see HandCountedLaps.
                    HandCountedLaps.In(result).ToString(CultureInfo.InvariantCulture),
                    places[result].Place?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                }));
            }

            // 12 Nov 2026: then who was skipped and scored DNS (SkipDns) -
            // rows in the same columns, so a sheet reading them by position
            // still parses: no position, laps or times (they never ran),
            // Status DNS. Without them a DNS driver read as "not in this
            // heat", which the points sheet can't tell from didn't start.
            foreach (var transponder in session.DidNotStart ?? new List<string>())
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    Csv(local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                    Csv(session.Label),
                    Csv(round?.RoundLabel ?? string.Empty),
                    Csv(round is null ? string.Empty : MixedHeats.ClassOfDriver(round, transponder, profiles)),
                    Csv(session.TrackLayoutName),
                    string.Empty,
                    Csv(DnsNameFor(transponder, profiles)),
                    Csv(transponder),
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    Csv(Dns),
                    string.Empty,
                    string.Empty,
                }));
            }
        }

        return sb.ToString();
    }

    private static string BuildLapCsv(IReadOnlyList<SessionRecord> sessions,
        RoundDefinitionStore? rounds, ProfileStore profiles)
    {
        var sb = new StringBuilder();

        // A column for each sector, after the lap's own - as many as the
        // most finely cut lap in the export, so no reader has to cope with
        // rows of different widths. Empty for a lap without sectors. None
        // at all when nothing was run with a split loop, so the file is
        // exactly what it always was.
        var sectorColumns = sessions
            .SelectMany(s => s.Results)
            .SelectMany(r => Enumerable.Range(0, r.LapTimesMs.Count).Select(i => r.SectorsOf(i)?.Length ?? 0))
            .DefaultIfEmpty(0)
            .Max();

        sb.AppendLine("SessionDate,SessionTime,SessionLabel,Round,Class,Driver,Transponder,LapNumber,LapSeconds,CumulativeSeconds"
                      + string.Concat(Enumerable.Range(1, sectorColumns).Select(n => $",S{n}Seconds")));

        foreach (var session in sessions.OrderBy(s => s.StartedAtUtc))
        {
            var round = session.RoundId is null ? null : rounds?.Get(session.RoundId);
            var local = session.StartedAtUtc.ToLocalTime();

            // No rows for a DNS driver (12 Nov 2026, SkipDns): a lap file is
            // one row per lap, and they never turned one. The Results CSV and
            // the JSON carry them.
            var places = MixedHeats.ClassPlacesOf(session.Results, round);

            foreach (var result in session.Results.OrderBy(r => r.Position))
            {
                long cumulative = 0;

                for (var lap = 0; lap < result.LapTimesMs.Count; lap++)
                {
                    cumulative += result.LapTimesMs[lap];

                    var sectors = result.SectorsOf(lap);

                    sb.AppendLine(string.Join(",", new[]
                    {
                        Csv(local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        Csv(local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                        Csv(session.Label),
                        Csv(round?.RoundLabel ?? string.Empty),
                        Csv(places[result].Class ?? string.Empty),
                        Csv(NameFor(result, profiles)),
                        Csv(result.Transponder),
                        (lap + 1).ToString(CultureInfo.InvariantCulture),
                        Seconds(result.LapTimesMs[lap]),
                        Seconds(cumulative),
                    }.Concat(Enumerable.Range(0, sectorColumns).Select(i => sectors is not null && i < sectors.Length ? Seconds(sectors[i]) : string.Empty))));
                }
            }
        }

        return sb.ToString();
    }

    private static string BuildJson(IReadOnlyList<SessionRecord> sessions,
        RoundDefinitionStore? rounds, ProfileStore profiles)
    {
        var payload = sessions.OrderBy(s => s.StartedAtUtc).Select(session =>
        {
            var round = session.RoundId is null ? null : rounds?.Get(session.RoundId);
            var places = MixedHeats.ClassPlacesOf(session.Results, round);

            return new
            {
                sessionId = session.Id,
                startedUtc = session.StartedAtUtc,
                startedLocal = session.StartedAtUtc.ToLocalTime(),
                label = session.Label,

                // Said outright, so a tool reading the file can leave out
                // simulated races without having to parse the label.
                isDemo = session.IsDemoSession(),
                importedFrom = session.IsImported() ? session.ImportedFrom : null,
                round = round?.RoundLabel,
                className = round?.ClassName,
                track = session.TrackLayoutName,
                results = session.Results.OrderBy(r => r.Position).Select(r => new
                {
                    position = r.Position,

                    // The class this driver raced in, and their place in it, for a
                    // shared heat (13 Nov 2026); for any other race the class is
                    // the round's and classPosition is null.
                    className = places[r].Class,
                    classPosition = places[r].Place,
                    driver = NameFor(r, profiles),
                    transponder = r.Transponder,
                    car = r.CarModel,
                    laps = r.Laps,
                    lapsCountedByHand = HandCountedLaps.In(r),
                    totalTimeSeconds = r.ElapsedMs / 1000.0,
                    bestLapSeconds = r.FastLapMs > 0 ? r.FastLapMs / 1000.0 : (double?)null,
                    lapTimesSeconds = r.LapTimesMs.Select(l => l / 1000.0).ToList(),

                    // Each lap's sectors, in step with lapTimesSeconds - null
                    // for a lap without them - or null when no lap had any.
                    sectorTimesSeconds = Enumerable.Range(0, r.LapTimesMs.Count).Any(i => r.SectorsOf(i) is not null)
                        ? Enumerable.Range(0, r.LapTimesMs.Count).Select(i => r.SectorsOf(i)?.Select(ms => ms / 1000.0).ToList()).ToList()
                        : null,

                    // What the race recorded ("active" for an ordinary run),
                    // and the outcome a correction set - "DNF", "DQ", or
                    // null for a finisher - so a tool can tell a
                    // disqualification from a car that never started.
                    status = r.Status,
                    outcome = string.IsNullOrEmpty(r.Outcome) ? null : r.Outcome,
                }),

                // 12 Nov 2026: who was skipped and scored DNS (SkipDns), after
                // everything a reader already knows - an empty list for a race
                // with nobody DNS. Not in results: they have no position, laps
                // or times, and a tool walking results expects those.
                didNotStart = (session.DidNotStart ?? new List<string>()).Select(t => new
                {
                    driver = DnsNameFor(t, profiles),
                    transponder = t,
                    status = Dns,
                }),
            };
        });

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// The name the race saved with the result, as Race History and the
    /// standings show it. The transponder's live profile is only a fallback
    /// for a result saved with no name: it used to come first, so a loaner
    /// transponder handed to somebody else renamed every old result on it to
    /// its new owner in the export.
    /// </summary>
    private static string NameFor(SessionResultEntry result, ProfileStore profiles)
    {
        if (!string.IsNullOrWhiteSpace(result.DriverName))
        {
            return result.DriverName;
        }

        var profile = profiles.Get(result.Transponder);
        if (!string.IsNullOrWhiteSpace(profile?.DriverName))
        {
            return profile!.DriverName;
        }

        return string.IsNullOrWhiteSpace(result.DriverName) ? result.Transponder : result.DriverName;
    }

    /// <summary>What a DNS driver's Status says (12 Nov 2026), as Race History's grid and CSV say it.</summary>
    internal const string Dns = "DNS";

    /// <summary>A DNS driver's name (12 Nov 2026): their profile's, or the transponder - they have no result row to carry one.</summary>
    private static string DnsNameFor(string transponder, ProfileStore profiles) =>
        profiles.Get(transponder)?.DriverName is { Length: > 0 } name ? name : transponder;

    /// <summary>Invariant culture throughout - a decimal comma would break the CSV for anyone on a European locale.</summary>
    private static string Seconds(long ms) =>
        (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

    /// <summary>
    /// Quotes a CSV field when it needs it. Driver names and session labels
    /// are free text and routinely contain commas.
    /// </summary>
    private static string Csv(string? value)
    {
        // Only ever given text - the numeric columns are written directly -
        // so a name or label that looks like a formula is made inert. See
        // CsvHelper.NeutraliseFormula.
        var text = CsvHelper.NeutraliseFormula(value ?? string.Empty);

        if (text.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"")}\"";
    }
}
