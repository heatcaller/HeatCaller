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
        sb.AppendLine("SessionDate,SessionTime,SessionLabel,Round,Class,Track,Position,Driver,Transponder,Car,Laps,TotalTimeSeconds,BestLapSeconds,Status");

        foreach (var session in sessions.OrderBy(s => s.StartedAtUtc))
        {
            var round = session.RoundId is null ? null : rounds?.Get(session.RoundId);
            var local = session.StartedAtUtc.ToLocalTime();

            foreach (var result in session.Results.OrderBy(r => r.Position))
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    Csv(local.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                    Csv(session.Label),
                    Csv(round?.RoundLabel ?? string.Empty),
                    Csv(round?.ClassName ?? string.Empty),
                    Csv(session.TrackLayoutName),
                    result.Position.ToString(CultureInfo.InvariantCulture),
                    Csv(NameFor(result, profiles)),
                    Csv(result.Transponder),
                    Csv(result.CarModel),
                    result.Laps.ToString(CultureInfo.InvariantCulture),
                    Seconds(result.ElapsedMs),
                    result.FastLapMs > 0 ? Seconds(result.FastLapMs) : string.Empty,
                    Csv(result.Status),
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
                        Csv(round?.ClassName ?? string.Empty),
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

            return new
            {
                sessionId = session.Id,
                startedUtc = session.StartedAtUtc,
                startedLocal = session.StartedAtUtc.ToLocalTime(),
                label = session.Label,

                // Said outright, so a tool reading the file can leave out
                // simulated races without having to parse the label.
                isDemo = session.IsDemoSession(),
                round = round?.RoundLabel,
                className = round?.ClassName,
                track = session.TrackLayoutName,
                results = session.Results.OrderBy(r => r.Position).Select(r => new
                {
                    position = r.Position,
                    driver = NameFor(r, profiles),
                    transponder = r.Transponder,
                    car = r.CarModel,
                    laps = r.Laps,
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
