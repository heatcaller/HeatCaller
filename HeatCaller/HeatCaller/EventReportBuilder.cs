using System.Net;
using System.Text;

namespace HeatCaller;

/// <summary>
/// Builds a self-contained HTML report for one event day.
///
/// HTML rather than an image: a day's results don't fit on a card, and a
/// report that can be printed, emailed, or opened on a phone is far more
/// use than a screenshot. Everything - styles included - goes in one file so
/// it survives being forwarded around.
///
/// The report deliberately leads with what people ask about after an event:
/// who won, what times were set, and whether anything is still outstanding.
/// </summary>
public static class EventReportBuilder
{
    public sealed record Options(
        DateTime Day,
        string EventName,
        bool IncludeQualifying,
        bool IncludeLapDetail,
        bool IncludeTech,
        bool IncludeRentals,

        // The race day the report is for, when there is one. With it, the
        // report covers that event's own rounds rather than a calendar
        // date - see SessionsFor. Null keeps the by-date report.
        RaceEvent? Event = null,

        // True when more than one race day raced on the chosen date, so
        // the report is the by-date one on purpose and says so at the top
        // rather than looking like one meeting's report.
        bool SeveralRaceDays = false);

    public static string Build(
        Options options,
        SessionHistoryStore history,
        RoundDefinitionStore rounds,
        RaceClassStore classes,
        ProfileStore profiles,
        TrackLayoutStore trackLayouts,
        TechInspectionStore tech,
        RentalTransponderStore rentals,
        HandicapStore? handicaps = null)
    {
        var sessions = SessionsFor(options, history, rounds);

        var sb = new StringBuilder();
        WriteHead(sb, options);

        sb.AppendLine("<body>");
        sb.AppendLine($"<h1>{Esc(options.EventName)}</h1>");
        sb.AppendLine($"<p class=\"sub\">{Esc(options.Day.ToLongDateString())}</p>");

        if (options.SeveralRaceDays && ReportEvent(options) is null)
        {
            sb.AppendLine("<p class=\"sub\">More than one race day raced on this date, so this report covers "
                          + "everything recorded on the date rather than one race day's program.</p>");
        }

        if (sessions.Count == 0)
        {
            sb.AppendLine("<p class=\"empty\">No sessions were recorded on this day.</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        WriteSummary(sb, sessions, profiles);

        // Outstanding items first when there are any - the point of putting
        // them in a report is that someone acts on them, and nobody acts on
        // something buried at the bottom.
        if (options.IncludeRentals)
        {
            WriteOutstandingRentals(sb, rentals);
        }

        if (options.IncludeQualifying)
        {
            WriteQualifying(sb, sessions, rounds, classes, profiles, ReportEvent(options));
        }

        WriteResultsByClass(sb, sessions, rounds, classes, profiles, options, handicaps);
        WriteRecords(sb, sessions, trackLayouts, profiles);

        if (options.IncludeTech)
        {
            WriteTech(sb, TechFor(options, tech, sessions));
        }

        if (options.IncludeLapDetail)
        {
            WriteLapDetail(sb, sessions, profiles);
        }

        sb.AppendLine($"<p class=\"footer\">Generated {Esc(AppTime.DateAndTime(DateTime.Now))} by HeatCaller.</p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    /// <summary>The event the report is scoped to - never the legacy holder, which is every old round at once and not a meeting.</summary>
    private static RaceEvent? ReportEvent(Options options) =>
        options.Event is { IsLegacy: false } raceEvent ? raceEvent : null;

    /// <summary>
    /// The sessions the report covers.
    ///
    /// With no event, everything started on the chosen local date - as it
    /// always was. With one, the event's own rounds wherever they fell on
    /// the clock: a meeting that ran past midnight used to lose its late
    /// mains from the report of the day it belonged to, and the next day's
    /// report picked them up alongside somebody else's racing. Sessions
    /// that belong to no round at all (practice, a quick race) are kept
    /// when they ran on a day the event raced, which is where the by-date
    /// report would have had them.
    ///
    /// Saved demo races are left out either way: this is the day's
    /// official record, and a simulated race in it would read as real
    /// results, real records and real qualifying.
    /// </summary>
    private static List<SessionRecord> SessionsFor(Options options, SessionHistoryStore history, RoundDefinitionStore rounds)
    {
        var real = history.All.Where(s => !s.IsDemoSession()).ToList();

        if (ReportEvent(options) is not { } raceEvent)
        {
            return real
                .Where(s => s.StartedAtUtc.ToLocalTime().Date == options.Day.Date)
                .OrderBy(s => s.StartedAtUtc)
                .ToList();
        }

        var roundIds = raceEvent.RoundIds.ToHashSet(StringComparer.Ordinal);

        // A round points at its session as well as the session pointing at
        // its round; either link is enough.
        var linkedSessionIds = raceEvent.RoundIds
            .Select(rounds.Get)
            .Where(r => !string.IsNullOrEmpty(r?.LinkedSessionId))
            .Select(r => r!.LinkedSessionId!)
            .ToHashSet(StringComparer.Ordinal);

        var eventSessions = real
            .Where(s => (s.RoundId is not null && roundIds.Contains(s.RoundId)) || linkedSessionIds.Contains(s.Id))
            .ToList();

        var eventDays = eventSessions
            .Select(s => s.StartedAtUtc.ToLocalTime().Date)
            .ToHashSet();

        // Nothing recorded against the event yet - the chosen day is the
        // only day there is to go on.
        if (eventDays.Count == 0)
        {
            eventDays.Add(options.Day.Date);
        }

        var unlinked = real
            .Where(s => s.RoundId is null && !linkedSessionIds.Contains(s.Id))
            .Where(s => eventDays.Contains(s.StartedAtUtc.ToLocalTime().Date));

        return eventSessions
            .Concat(unlinked)
            .OrderBy(s => s.StartedAtUtc)
            .ToList();
    }

    private static void WriteHead(StringBuilder sb, Options options)
    {
        // Styles inlined so the file works when emailed or copied to a phone
        // with no network access.
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>{Esc(options.EventName)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(@"
body { font-family: -apple-system, Segoe UI, Roboto, sans-serif; margin: 32px auto; max-width: 900px;
       color: #1a1a1a; line-height: 1.5; }
h1 { margin-bottom: 4px; font-size: 28px; }
h2 { margin-top: 36px; border-bottom: 2px solid #d0d0d0; padding-bottom: 6px; font-size: 20px; }
h3 { margin-top: 24px; font-size: 16px; color: #444; }
.sub { color: #666; margin-top: 0; }
.empty { color: #888; font-style: italic; }
.footer { margin-top: 48px; color: #888; font-size: 12px; border-top: 1px solid #ddd; padding-top: 12px; }
table { border-collapse: collapse; width: 100%; margin: 12px 0 24px; font-size: 14px; }
th { text-align: left; background: #f2f2f2; padding: 7px 10px; border-bottom: 2px solid #ddd; }
td { padding: 6px 10px; border-bottom: 1px solid #eee; }
tr:nth-child(even) td { background: #fafafa; }
td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; }
.win { font-weight: 600; }
.alert { background: #fff4f4; border-left: 4px solid #d64545; padding: 12px 16px; margin: 16px 0; }
.alert h2 { margin-top: 0; border: none; color: #a02020; }
.cards { display: flex; gap: 16px; flex-wrap: wrap; margin: 16px 0; }
.card { flex: 1 1 160px; background: #f7f7f7; border-radius: 6px; padding: 12px 16px; }
.card .k { font-size: 24px; font-weight: 600; }
.card .l { color: #666; font-size: 12px; text-transform: uppercase; letter-spacing: .04em; }
@media print { body { margin: 0; max-width: none; } h2 { page-break-after: avoid; } table { page-break-inside: avoid; } }
");
        sb.AppendLine("</style></head>");
    }

    private static void WriteSummary(StringBuilder sb, List<SessionRecord> sessions, ProfileStore profiles)
    {
        // Ghosts are pace references, not drivers - left out of the count
        // and, above all, of the fastest lap, which one of them could
        // otherwise take from every real driver of the day.
        var drivers = sessions.SelectMany(s => s.Results.Select(r => r.Transponder))
            .Where(t => !GhostService.IsGhost(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var laps = sessions.Sum(s => s.Results.Sum(r => r.Laps));

        var fastest = sessions
            .SelectMany(s => s.Results.Where(r => r.FastLapMs > 0 && !GhostService.IsGhost(r.Transponder)).Select(r => (s, r)))
            .OrderBy(x => x.r.FastLapMs)
            .FirstOrDefault();

        sb.AppendLine("<div class=\"cards\">");
        sb.AppendLine($"<div class=\"card\"><div class=\"k\">{sessions.Count}</div><div class=\"l\">Sessions</div></div>");
        sb.AppendLine($"<div class=\"card\"><div class=\"k\">{drivers}</div><div class=\"l\">Drivers</div></div>");
        sb.AppendLine($"<div class=\"card\"><div class=\"k\">{laps}</div><div class=\"l\">Laps run</div></div>");

        if (fastest.r is not null)
        {
            var name = NameFor(fastest.r.Transponder, fastest.r.DriverName, profiles);
            sb.AppendLine($"<div class=\"card\"><div class=\"k\">{fastest.r.FastLapMs / 1000.0:0.000}s</div>"
                          + $"<div class=\"l\">Fastest lap - {Esc(name)}</div></div>");
        }

        sb.AppendLine("</div>");
    }

    /// <summary>
    /// Anything still on loan. Listed near the top because a report is only
    /// worth writing if someone acts on it, and missing hardware is the item
    /// most likely to need acting on tonight rather than next week.
    /// </summary>
    private static void WriteOutstandingRentals(StringBuilder sb, RentalTransponderStore rentals)
    {
        var out_ = rentals.OutNow;
        if (out_.Count == 0)
        {
            return;
        }

        sb.AppendLine("<div class=\"alert\">");
        sb.AppendLine($"<h2>{Plural.Of(out_.Count, "rental transponder")} not returned</h2>");
        sb.AppendLine("<table><tr><th>Unit</th><th>With</th><th>Deposit held</th><th>Out since</th></tr>");

        foreach (var unit in out_)
        {
            sb.AppendLine("<tr>"
                          + $"<td>{Esc(unit.DisplayName)}</td>"
                          + $"<td>{Esc(unit.CurrentHolderName)}</td>"
                          + $"<td>{Esc(string.IsNullOrWhiteSpace(unit.DepositHeld) ? "-" : unit.DepositHeld)}</td>"
                          + $"<td>{Esc(unit.CheckedOutUtc is { } t ? AppTime.TimeOfDay(t.ToLocalTime()) : "-")}</td>"
                          + "</tr>");
        }

        sb.AppendLine("</table></div>");
    }

    private static void WriteResultsByClass(StringBuilder sb, List<SessionRecord> sessions,
        RoundDefinitionStore rounds, RaceClassStore classes, ProfileStore profiles, Options options,
        HandicapStore? handicaps)
    {
        sb.AppendLine("<h2>Results</h2>");

        // Group by the class each session's round belonged to. Sessions with
        // no round are collected under "Other" rather than dropped - an
        // unlinked race still happened and its results still matter.
        var byClass = new Dictionary<string, List<SessionRecord>>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions)
        {
            var round = session.RoundId is null ? null : rounds.Get(session.RoundId);
            var className = round?.ClassName ?? "Other sessions";

            if (!byClass.TryGetValue(className, out var list))
            {
                list = new List<SessionRecord>();
                byClass[className] = list;
            }

            list.Add(session);
        }

        foreach (var (className, classSessions) in byClass.OrderBy(kv => kv.Key))
        {
            sb.AppendLine($"<h3>{Esc(className)}</h3>");

            foreach (var session in classSessions)
            {
                var round = session.RoundId is null ? null : rounds.Get(session.RoundId);
                var title = round?.RoundLabel ?? session.Label;

                sb.AppendLine($"<p><strong>{Esc(title)}</strong> &middot; {Esc(AppTime.TimeOfDay(session.StartedAtUtc.ToLocalTime()))}"
                              + (string.IsNullOrWhiteSpace(session.TrackLayoutName) ? "" : $" &middot; {Esc(session.TrackLayoutName)}")
                              + "</p>");

                var mode = handicaps is null || round is null ? HandicapMode.None : handicaps.ModeFor(round.ClassName);
                var adjusted = mode == HandicapMode.None
                    ? null
                    : HandicapCalculator.Apply(session.Results, handicaps!, mode)
                        .ToDictionary(a => a.Result.Transponder, a => a, StringComparer.OrdinalIgnoreCase);

                // The DNF or DQ set in Edit Result, and why. Left out, a DNF
                // printed as an ordinary finisher and a DQ as a row of no laps
                // beside a full race time and a best lap - this report is the
                // one sent round as the meeting's record, and it was the one
                // place that contradicted the ruling. The CSV export, the
                // phone and the results site already show it. A column only
                // for a race that has one, so an uncorrected race reads as
                // it always did.
                var hasOutcome = session.Results.Any(r => OutcomeOf(r).Length > 0);

                sb.AppendLine("<table><tr><th class=\"num\">Pos</th><th>Driver</th><th>Car</th>"
                              + "<th class=\"num\">Laps</th><th class=\"num\">Time</th><th class=\"num\">Best lap</th>"
                              + (hasOutcome ? "<th>Status</th>" : string.Empty)
                              + (adjusted is not null ? "<th class=\"num\">Handicap pos</th>" : string.Empty)
                              + "</tr>");

                foreach (var result in session.Results.OrderBy(r => r.Position))
                {
                    var name = NameFor(result.Transponder, result.DriverName, profiles);

                    // With handicaps on, the WINNER is whoever is P1 after
                    // adjustment - that's what the class is scored on.
                    var adj = adjusted is not null && adjusted.TryGetValue(result.Transponder, out var a) ? a : null;
                    var isWinner = adj is not null ? adj.AdjustedPosition == 1 : result.Position == 1;
                    var winner = isWinner ? " class=\"win\"" : string.Empty;

                    var adjCell = adj is null
                        ? string.Empty
                        : $"<td class=\"num\">P{adj.AdjustedPosition}"
                          + (adj.CreditLaps != 0 ? $" (+{adj.CreditLaps}L)" : adj.CreditSeconds != 0 ? $" (-{adj.CreditSeconds:0.0}s)" : string.Empty)
                          + "</td>";

                    sb.AppendLine($"<tr{winner}>"
                                  + $"<td class=\"num\">{result.Position}</td>"
                                  + $"<td>{Esc(name)}</td>"
                                  + $"<td>{Esc(string.IsNullOrWhiteSpace(result.CarModel) ? "-" : result.CarModel)}</td>"
                                  + $"<td class=\"num\">{result.Laps}</td>"
                                  + $"<td class=\"num\">{result.ElapsedMs / 1000.0:0.000}s</td>"
                                  + $"<td class=\"num\">{(result.FastLapMs > 0 ? $"{result.FastLapMs / 1000.0:0.000}s" : "-")}</td>"
                                  + (hasOutcome ? $"<td>{Esc(OutcomeOf(result))}</td>" : string.Empty)
                                  + adjCell
                                  + "</tr>");
                }

                sb.AppendLine("</table>");

                // The correction log, so the order above can be explained.
                if (session.Edits is { Count: > 0 } edits)
                {
                    sb.AppendLine("<p class=\"sub\">Corrected after the race:</p><ul class=\"sub\">");

                    foreach (var edit in edits)
                    {
                        sb.AppendLine($"<li>{Esc(NameFor(edit.Transponder, edit.DriverName, profiles))}: {Esc(edit.Change)}"
                                      + (string.IsNullOrWhiteSpace(edit.Reason) ? string.Empty : $" - {Esc(edit.Reason)}")
                                      + "</li>");
                    }

                    sb.AppendLine("</ul>");
                }
            }
        }
    }


    /// <summary>"DNF", "DQ", or empty for a finisher - see ResultEditor.</summary>
    private static string OutcomeOf(SessionResultEntry result) =>
        ResultEditor.NormaliseOutcome(result.Outcome) ?? string.Empty;

    /// <summary>
    /// Qualifying standings per class, using the same calculator and rules
    /// as the Race Program page - so the report can't disagree with what
    /// the mains were seeded from.
    /// </summary>
    private static void WriteQualifying(StringBuilder sb, List<SessionRecord> sessions,
        RoundDefinitionStore rounds, RaceClassStore classes, ProfileStore profiles, RaceEvent? raceEvent)
    {
        // Only classes that actually ran qualifying in the report's scope.
        //
        // With an event that is the event's rounds - the same filter the
        // Race Day programme seeds the mains with - so the report shows the
        // qualifying the mains were actually built from.
        var dayRoundIds = raceEvent is not null
            ? raceEvent.RoundIds.ToHashSet(StringComparer.Ordinal)
            : sessions
                .Where(s => s.RoundId is not null)
                .Select(s => s.RoundId!)
                .ToHashSet();

        var classNames = dayRoundIds
            .Select(rounds.Get)
            .Where(r => r is { Kind: RoundKind.Qualifier })
            .Select(r => r!.ClassName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        if (classNames.Count == 0)
        {
            return;
        }

        sb.AppendLine("<h2>Qualifying</h2>");

        foreach (var className in classNames)
        {
            var raceClass = classes.All.FirstOrDefault(c => string.Equals(c.Name, className, StringComparison.OrdinalIgnoreCase));

            // Scoped to the report's rounds, matching how the report itself is scoped.
            var entries = QualifyingCalculator.Compute(className, rounds, ProfileFreeHistory(sessions), profiles, raceClass, dayRoundIds);
            if (entries.Count == 0)
            {
                continue;
            }

            var consecutive = raceClass?.QualifyingMethod == QualifyingMethod.BestConsecutiveLaps;
            var lapCount = Math.Max(1, raceClass?.ConsecutiveLapCount ?? 3);

            sb.AppendLine($"<h3>{Esc(className)}</h3>");
            sb.AppendLine("<table><tr><th class=\"num\">Pos</th><th>Driver</th><th class=\"num\">Best laps</th>"
                          + (consecutive ? $"<th class=\"num\">Best {lapCount} consec.</th>" : string.Empty)
                          + "<th class=\"num\">Fastest lap</th><th>Counted</th></tr>");

            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                sb.AppendLine("<tr>"
                              + $"<td class=\"num\">{i + 1}</td>"
                              + $"<td>{Esc(e.DriverName)}</td>"
                              + $"<td class=\"num\">{e.BestLaps}</td>"
                              + (consecutive ? $"<td class=\"num\">{(e.BestConsecutiveMs > 0 ? $"{e.BestConsecutiveMs / 1000.0:0.000}s" : "-")}</td>" : string.Empty)
                              + $"<td class=\"num\">{(e.BestFastLapMs > 0 ? $"{e.BestFastLapMs / 1000.0:0.000}s" : "-")}</td>"
                              + $"<td>{Esc(e.SourceRoundLabel)}</td>"
                              + "</tr>");
            }

            sb.AppendLine("</table>");
        }
    }

    /// <summary>
    /// The calculator wants a SessionHistoryStore, but the report has only
    /// this day's sessions in hand. A throwaway store over that list keeps
    /// the calculator's signature intact and the report scoped to its day.
    /// </summary>
    private static SessionHistoryStore ProfileFreeHistory(List<SessionRecord> sessions) =>
        SessionHistoryStore.FromSessions(sessions);

    private static void WriteRecords(StringBuilder sb, List<SessionRecord> sessions,
        TrackLayoutStore trackLayouts, ProfileStore profiles)
    {
        // A driver's best lap of the day, per driver.
        // Ghosts left out - see WriteSummary.
        var bests = sessions
            .SelectMany(s => s.Results.Where(r => r.FastLapMs > 0 && !GhostService.IsGhost(r.Transponder)))
            .GroupBy(r => r.Transponder, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(r => r.FastLapMs).First())
            .OrderBy(r => r.FastLapMs)
            .ToList();

        if (bests.Count == 0)
        {
            return;
        }

        sb.AppendLine("<h2>Fastest laps of the day</h2>");
        sb.AppendLine("<table><tr><th class=\"num\">#</th><th>Driver</th><th class=\"num\">Best lap</th></tr>");

        for (var i = 0; i < bests.Count; i++)
        {
            var name = NameFor(bests[i].Transponder, bests[i].DriverName, profiles);
            sb.AppendLine($"<tr><td class=\"num\">{i + 1}</td><td>{Esc(name)}</td>"
                          + $"<td class=\"num\">{bests[i].FastLapMs / 1000.0:0.000}s</td></tr>");
        }

        sb.AppendLine("</table>");
    }

    /// <summary>
    /// The inspections the report covers, scoped the way its sessions are.
    ///
    /// With no race day, everything inspected on the chosen date, as it
    /// always was. With one, the inspections filed against that race day
    /// wherever they fell on the clock, plus any filed against no race day
    /// on a day it raced - the same rule SessionsFor applies to sessions.
    /// Reading the calendar date alone put a second race day's inspections
    /// on the same date into this one's report, and left out a late
    /// re-inspection after midnight that belonged to it.
    /// </summary>
    private static IReadOnlyList<TechInspection> TechFor(Options options, TechInspectionStore tech, List<SessionRecord> sessions)
    {
        if (ReportEvent(options) is not { } raceEvent)
        {
            return tech.ForDay(options.Day);
        }

        var days = sessions
            .Select(s => s.StartedAtUtc.ToLocalTime().Date)
            .ToHashSet();

        days.Add(options.Day.Date);

        return tech.All
            .Where(i => string.Equals(i.EventId, raceEvent.Id, StringComparison.Ordinal)
                        || ((string.IsNullOrEmpty(i.EventId)
                             || string.Equals(i.EventId, RaceEventStore.LegacyEventId, StringComparison.Ordinal))
                            && days.Contains(i.InspectedAtUtc.ToLocalTime().Date)))
            .ToList();
    }

    private static void WriteTech(StringBuilder sb, IReadOnlyList<TechInspection> inspections)
    {
        if (inspections.Count == 0)
        {
            return;
        }

        var failed = inspections.Where(i => !i.IsCleared).ToList();

        sb.AppendLine("<h2>Tech inspection</h2>");
        sb.AppendLine($"<p>{Plural.Of(inspections.Count, "car")} inspected"
                      + (failed.Count == 0 ? ", all cleared." : $", <strong>{failed.Count} not cleared</strong>.")
                      + "</p>");

        sb.AppendLine("<table><tr><th>Driver</th><th>Class</th><th>Result</th><th>Inspector</th><th>Notes</th></tr>");

        foreach (var inspection in inspections)
        {
            sb.AppendLine("<tr>"
                          + $"<td>{Esc(inspection.DriverName)}</td>"
                          + $"<td>{Esc(inspection.ClassName)}</td>"
                          + $"<td>{Esc(inspection.ResultText)}</td>"
                          + $"<td>{Esc(inspection.Inspector)}</td>"
                          + $"<td>{Esc(inspection.Notes)}</td>"
                          + "</tr>");
        }

        sb.AppendLine("</table>");
    }

    /// <summary>
    /// Every lap of every session. Optional and last, because it's long -
    /// useful for someone analysing their pace afterwards, noise for
    /// everyone else.
    /// </summary>
    private static void WriteLapDetail(StringBuilder sb, List<SessionRecord> sessions, ProfileStore profiles)
    {
        var any = sessions.Any(s => s.Results.Any(r => r.LapTimesMs.Count > 0));
        if (!any)
        {
            return;
        }

        sb.AppendLine("<h2>Lap by lap</h2>");

        foreach (var session in sessions.Where(s => s.Results.Any(r => r.LapTimesMs.Count > 0)))
        {
            sb.AppendLine($"<h3>{Esc(session.Label)} &middot; {Esc(AppTime.TimeOfDay(session.StartedAtUtc.ToLocalTime()))}</h3>");

            foreach (var result in session.Results.Where(r => r.LapTimesMs.Count > 0).OrderBy(r => r.Position))
            {
                var name = NameFor(result.Transponder, result.DriverName, profiles);
                var laps = string.Join(", ", result.LapTimesMs.Select(l => $"{l / 1000.0:0.000}"));
                sb.AppendLine($"<p><strong>{Esc(name)}</strong><br><span style=\"font-size:13px;color:#555\">{Esc(laps)}</span></p>");
            }
        }
    }

    private static string NameFor(string transponder, string fallback, ProfileStore profiles)
    {
        var profile = profiles.Get(transponder);
        if (!string.IsNullOrWhiteSpace(profile?.DriverName))
        {
            return profile!.DriverName;
        }

        return string.IsNullOrWhiteSpace(fallback) ? transponder : fallback;
    }

    /// <summary>HTML-escapes a value. Driver names and notes are free text and can contain anything.</summary>
    private static string Esc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
