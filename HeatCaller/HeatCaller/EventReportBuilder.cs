using System.Globalization;
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
        HandicapStore? handicaps = null,
        RaceEventStore? events = null,
        EntryPaymentStore? payments = null)
    {
        var sessions = SessionsFor(options, history, rounds, events);

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
        WriteNightPace(sb, sessions, rounds, ReportEvent(options));
        WriteMoney(sb, sessions, ReportEvent(options), payments, profiles, DeskMoney.ClassLookup(classes.All));
        WriteMarshalNoShows(sb, rounds, ReportEvent(options));

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

        WriteAMainLegs(sb, sessions, rounds, classes, profiles);
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

        // Signed with HeatCaller's mark and a link to its site (31 Oct 2026,
        // the mark on everything the app makes).
        sb.AppendLine($"<p class=\"footer\">Generated {Esc(AppTime.DateAndTime(DateTime.Now))} by "
                      + $"<a class=\"hcmark\" href=\"https://heatcaller.com\">{HeatCallerMark.SunSvg}{HeatCallerMark.WordHtml}</a></p>");
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
    private static List<SessionRecord> SessionsFor(
        Options options, SessionHistoryStore history, RoundDefinitionStore rounds, RaceEventStore? events)
    {
        var real = history.All.Where(s => !s.IsNotScored()).ToList();

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

        // WHY (23 Oct 2026, bug hunt 10): only a session with no round at
        // all counted as unlinked, so a race from a round that belongs to no
        // real race day - added by hand after Close Day, or since filed
        // under the legacy holder - was dropped, and as the dialog has no
        // way to ask for the by-date report it was in no report at all.
        // Such a round now counts like no round, the same rule TechFor
        // applies to an empty or legacy event id. Another real day's rounds
        // stay out. With no event store to ask, the old rule stands, since
        // every round would otherwise look like it belongs nowhere.
        var realDayRoundIds = events?.Real
            .SelectMany(e => e.RoundIds)
            .ToHashSet(StringComparer.Ordinal);

        // The report's own rounds are already in eventSessions; counting
        // them as belonging somewhere keeps a race from being listed twice
        // should the day handed in not be the store's own copy.
        realDayRoundIds?.UnionWith(roundIds);

        var unlinked = real
            .Where(s => (s.RoundId is null
                         || (realDayRoundIds is not null && !realDayRoundIds.Contains(s.RoundId)))
                        && !linkedSessionIds.Contains(s.Id))
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
.hcmark { display: inline-flex; align-items: center; gap: 5px; color: #1a1a1a; text-decoration: none; vertical-align: -3px; }
.hcsun { width: 16px; height: 16px; fill: currentColor; }
.hcword { font-weight: 700; letter-spacing: .05em; text-transform: uppercase; }
.hcword .hcc { color: #FF2E88; }
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
            sb.AppendLine($"<div class=\"card\"><div class=\"k\">{Secs(fastest.r.FastLapMs)}s</div>"
                          + $"<div class=\"l\">Fastest lap - {Esc(name)}</div></div>");
        }

        sb.AppendLine("</div>");
    }

    /// <summary>
    /// Money at the desk (11 Nov 2026, race-night build plan, Phase 4): what
    /// was taken by cash and card, the comps, and who is still owing - for
    /// the drivers who raced. Only for a race day with an entry fee. See
    /// DeskMoney.
    /// </summary>
    /// <remarks>
    /// With classes' own fees (12 Nov 2026, setup plan step 8): a night that
    /// charges only through its classes gets the section too, each driver's
    /// fee is worked out with them (DeskMoney.FeeFor), and a line under the
    /// totals says what each class charged, so the treasurer can check a
    /// figure against the price board.
    /// </remarks>
    private static void WriteMoney(StringBuilder sb, List<SessionRecord> sessions, RaceEvent? raceEvent, EntryPaymentStore? payments, ProfileStore profiles,
        Func<string, RaceClass?> classNamed)
    {
        if (payments is null || raceEvent is not { } day || !DeskMoney.Charges(day, classNamed))
        {
            return;
        }

        var raced = sessions
            .Where(s => s.SessionType == RaceSessionType.Race)
            .SelectMany(s => s.Results.Select(r => r.Transponder))
            .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
            .Select(t => profiles.PrimaryForAlternate(t) ?? t)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => profiles.Get(t))
            .OfType<DriverProfile>()
            .ToList();

        var totals = DeskMoney.Count(raced.Select(p =>
            (string.IsNullOrWhiteSpace(p.DriverName) ? p.Transponder : p.DriverName, CheckInControl.FeeFor(p, day, classNamed), payments.Get(day.Id, p.Transponder))));

        sb.AppendLine("<h2>Money at the desk</h2>");

        if (DeskMoney.FeesLine(day, classNamed) is { } fees)
        {
            sb.AppendLine($"<p class=\"sub\">{Esc(fees)}</p>");
        }
        sb.AppendLine("<div class=\"cards\">");
        sb.AppendLine($"<div class=\"card\"><div class=\"k\">{Esc(DeskMoney.Money(totals.Taken))}</div><div class=\"l\">Taken</div></div>");
        sb.AppendLine($"<div class=\"card\"><div class=\"k\">{Esc(DeskMoney.Money(totals.Cash))}</div><div class=\"l\">Cash</div></div>");
        sb.AppendLine($"<div class=\"card\"><div class=\"k\">{Esc(DeskMoney.Money(totals.Card))}</div><div class=\"l\">Card</div></div>");
        sb.AppendLine($"<div class=\"card\"><div class=\"k\">{totals.Comps}</div><div class=\"l\">Comps</div></div>");
        sb.AppendLine("</div>");

        if (totals.Owing.Count > 0)
        {
            sb.AppendLine($"<div class=\"alert\"><h2>Still owing - {Esc(DeskMoney.Money(totals.OwingAmount))}</h2><ul>");

            foreach (var (name, owes) in totals.Owing)
            {
                sb.AppendLine($"<li>{Esc(name)} - {Esc(DeskMoney.Money(owes))}</li>");
            }

            sb.AppendLine("</ul></div>");
        }
    }

    /// <summary>
    /// How the night ran (11 Nov 2026, race-night build plan, Phase 0):
    /// green to green, the longest gaps and what happened in them, and the
    /// share of the night spent racing. Left out with fewer than two races,
    /// where there are no gaps to speak of. See NightPace.
    /// </summary>
    /// <summary>
    /// The marshals who didn't turn up to their post tonight (12 Nov 2026,
    /// setup plan D4), round by round, for the club's marshal rule. Nothing
    /// when there were none.
    /// </summary>
    private static void WriteMarshalNoShows(StringBuilder sb, RoundDefinitionStore rounds, RaceEvent? raceEvent)
    {
        if (raceEvent is null)
        {
            return;
        }

        var ids = raceEvent.RoundIds.ToHashSet(StringComparer.Ordinal);
        var missed = rounds.All
            .Where(r => ids.Contains(r.Id) && r.MarshalNoShows is { Count: > 0 })
            .ToList();

        if (missed.Count == 0)
        {
            return;
        }

        sb.AppendLine("<h2>Marshal no-shows</h2>");
        sb.AppendLine("<ul>");

        foreach (var round in missed)
        {
            // What the club's rule did to each (12 Nov 2026, setup plan step
            // 8): "Hugh Turn (Turn 3) - loses their best Stock qualifying
            // result tonight". A no-show logged before the rule existed has
            // its name only.
            var each = round.MarshalNoShows!.Select(who =>
                round.NoShowMarshals?.FirstOrDefault(n => string.Equals(n.Who, who, StringComparison.OrdinalIgnoreCase)) is { } noShow
                    ? $"{who} - {MarshalRule.Consequence(noShow)}"
                    : who);

            sb.AppendLine($"<li>{Esc(round.RoundLabel)}: {Esc(string.Join(", ", each))}</li>");
        }

        sb.AppendLine("</ul>");
    }

    private static void WriteNightPace(StringBuilder sb, List<SessionRecord> sessions, RoundDefinitionStore rounds, RaceEvent? raceEvent)
    {
        // The race director's two measures (12 Nov 2026, setup plan step 8)
        // go with the pace even on a night of one race - the first green is
        // what they measure to.
        var measures = NightMeasures.For(raceEvent, sessions, rounds.All);
        var pace = NightPace.For(sessions, rounds.All, raceEvent?.RoundIds, breaks: raceEvent?.Breaks);

        if (pace is null && measures.Empty)
        {
            return;
        }

        sb.AppendLine("<h2>Night pace</h2>");
        sb.AppendLine("<div class=\"cards\">");

        if (measures.EntriesToHeats is { } entries)
        {
            var (value, label) = entries < TimeSpan.Zero
                ? (NightPace.Clock(-entries), "Heats posted before entries closed")
                : (NightPace.Clock(entries), measures.HeatsNotPosted ? "Entries close to first green (heats not posted)" : "Entries close to heats posted");
            sb.AppendLine($"<div class=\"card\"><div class=\"k\">{value}</div><div class=\"l\">{Esc(label)}</div></div>");
        }

        if (measures.DoorsToGreen is { } doors)
        {
            sb.AppendLine($"<div class=\"card\"><div class=\"k\">{NightPace.Clock(doors)}</div><div class=\"l\">Doors open to first green</div></div>");
        }

        if (pace is null)
        {
            sb.AppendLine("</div>");
            return;
        }

        if (pace.MedianGreenToGreen is { } g2g)
        {
            sb.AppendLine($"<div class=\"card\"><div class=\"k\">{NightPace.Clock(g2g)}</div><div class=\"l\">Green to green (typical)</div></div>");
        }

        if (pace.WorstGreenToGreen is { } worst)
        {
            sb.AppendLine($"<div class=\"card\"><div class=\"k\">{NightPace.Clock(worst.GreenToGreen)}</div><div class=\"l\">Worst green to green: "
                          + $"{Esc(worst.Before.Label)} to {Esc(worst.After.Label)} ({Esc(worst.CauseText)})</div></div>");
        }

        if (pace.MedianGap is { } gap)
        {
            sb.AppendLine($"<div class=\"card\"><div class=\"k\">{NightPace.Clock(gap)}</div><div class=\"l\">Finish to next green (typical)</div></div>");
        }

        if (pace.MedianFinishToSaved is { } saving)
        {
            sb.AppendLine($"<div class=\"card\"><div class=\"k\">{NightPace.Clock(saving)}</div><div class=\"l\">Finish to result saved (typical)</div></div>");
        }

        if (pace.MedianCalledToGreen is { } called)
        {
            sb.AppendLine($"<div class=\"card\"><div class=\"k\">{NightPace.Clock(called)}</div><div class=\"l\">Call-up to green (typical)</div></div>");
        }

        if (pace.OnTrackShare is { } share)
        {
            sb.AppendLine($"<div class=\"card\"><div class=\"k\">{NightPace.Percent(share)}</div><div class=\"l\">Of the night on track</div></div>");
        }

        if (pace.RoundsRun is { } run && pace.RoundsPlanned is { } planned)
        {
            sb.AppendLine($"<div class=\"card\"><div class=\"k\">{run} of {planned}</div><div class=\"l\">Races run</div></div>");
        }

        sb.AppendLine("</div>");

        if (pace.LongestGaps.Count == 0)
        {
            return;
        }

        sb.AppendLine("<h3>Longest gaps</h3>");
        sb.AppendLine("<table><tr><th>After</th><th>Before</th><th class=\"num\">Gap</th><th>What happened</th></tr>");

        foreach (var longest in pace.LongestGaps)
        {
            sb.AppendLine($"<tr><td>{Esc(longest.Before.Label)}</td><td>{Esc(longest.After.Label)}</td>"
                          + $"<td class=\"num\">{NightPace.Clock(longest.Length)}</td><td>{Esc(longest.CauseText)}</td></tr>");
        }

        sb.AppendLine("</table>");
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

            // WHY (11 Nov 2026, Phase 4.1, mixed-class heats): a race two
            // classes shared is listed under EACH class, with only that
            // class's drivers and their places within it - the Stock table
            // reads 1-2-3 for the three Stock cars, not 1-4-6. A copy, so the
            // saved race is untouched.
            if (MixedHeats.IsShared(round))
            {
                foreach (var shared in MixedHeats.ClassesOf(round!))
                {
                    var copy = System.Text.Json.JsonSerializer.Deserialize<SessionRecord>(System.Text.Json.JsonSerializer.Serialize(session))!;
                    copy.Results = MixedHeats.ClassResults(session.Results, round, shared, profiles).ToList();
                    Add(shared, copy);
                }

                continue;
            }

            Add(round?.ClassName ?? "Other sessions", session);
        }

        void Add(string className, SessionRecord session)
        {
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

                // Said, so a shared race's short table is not mistaken for a small heat.
                if (MixedHeats.IsShared(round))
                {
                    title += $" ({MixedHeats.ClassLabel(round!)})";
                }

                sb.AppendLine($"<p><strong>{Esc(title)}</strong> &middot; {Esc(AppTime.TimeOfDay(session.StartedAtUtc.ToLocalTime()))}"
                              + (string.IsNullOrWhiteSpace(session.TrackLayoutName) ? "" : $" &middot; {Esc(session.TrackLayoutName)}")
                              + "</p>");

                // On the credits kept with the race when it was filed, so an
                // old meeting's report still names the winner it had (B52).
                var adjusted = HandicapCalculator.ForSession(session, round is null ? null : className, handicaps).Adjusted?
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
                          + (adj.CreditLaps != 0 ? $" (+{adj.CreditLaps}L)" : adj.CreditSeconds != 0 ? $" (-{adj.CreditSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s)" : string.Empty)
                          + "</td>";

                    sb.AppendLine($"<tr{winner}>"
                                  + $"<td class=\"num\">{result.Position}</td>"
                                  + $"<td>{Esc(name)}</td>"
                                  + $"<td>{Esc(string.IsNullOrWhiteSpace(result.CarModel) ? "-" : result.CarModel)}</td>"
                                  + $"<td class=\"num\">{HandCountedLaps.LapsText(result.Laps, HandCountedLaps.In(result))}</td>"
                                  + $"<td class=\"num\">{Secs(result.ElapsedMs)}s</td>"
                                  + $"<td class=\"num\">{(result.FastLapMs > 0 ? $"{Secs(result.FastLapMs)}s" : "-")}</td>"
                                  + (hasOutcome ? $"<td>{Esc(OutcomeOf(result))}</td>" : string.Empty)
                                  + adjCell
                                  + "</tr>");
                }

                sb.AppendLine("</table>");

                // Who was skipped from the grid and scored DNS (12 Nov 2026,
                // setup plan step 8) - see SkipDns.
                if (SkipDns.Words(session, t => NameFor(t, t, profiles)) is { Length: > 0 } dns)
                {
                    sb.AppendLine($"<p class=\"sub\">{Esc(dns)}</p>");
                }

                // 22 Oct 2026: what the * after a lap count means - see HandCountedLaps.
                if (session.Results.Any(r => HandCountedLaps.In(r) > 0))
                {
                    sb.AppendLine($"<p class=\"sub\">{Esc(HandCountedLaps.Legend)}</p>");
                }

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
    /// <summary>
    /// The overall result of each double or triple A-main (11 Nov 2026,
    /// race-night build plan, Phase 5.3): points per leg, best N legs,
    /// fewest wins, with the tie-breaker printed under it. Each leg's own
    /// result is in Results as an ordinary race; this is the one that names
    /// the winner. Only legs in the report's sessions are counted.
    /// </summary>
    private static void WriteAMainLegs(StringBuilder sb, List<SessionRecord> sessions,
        RoundDefinitionStore rounds, RaceClassStore classes, ProfileStore profiles)
    {
        var legs = sessions
            .Where(s => s.RoundId is not null)
            .Select(s => (Session: s, Round: rounds.Get(s.RoundId!)))
            .Where(x => x.Round is { Kind: RoundKind.Main, MainLeg: > 0 } && x.Round.LinkedSessionId == x.Session.Id)
            .GroupBy(x => x.Round!.ClassName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in legs)
        {
            var raceClass = classes.All.FirstOrDefault(c => string.Equals(c.Name, group.Key, StringComparison.OrdinalIgnoreCase));
            var ordered = group.OrderBy(x => x.Round!.MainLeg).Select(x => x.Session).ToList();
            var legCount = Math.Max(group.Max(x => x.Round!.MainLegCount), ordered.Count);
            var counting = AMainLegs.LegsCounting(legCount, raceClass?.AMainLegsCounted ?? 0);
            var overall = AMainLegs.Overall(ordered, counting, profiles.PrimaryForAlternate,
                group.OrderBy(x => x.Round!.MainLeg).Select(x => x.Round!.MainLeg).ToList());

            sb.AppendLine($"<h2>{Esc(group.Key)} A-Main overall</h2>");
            sb.AppendLine($"<p>{Plural.Of(ordered.Count, "leg")} of {legCount} run; the best {counting} count, fewest points wins. {Esc(AMainLegs.TieBreakWords)}</p>");
            sb.AppendLine("<table><tr><th class=\"num\">Pos</th><th>Driver</th><th class=\"num\">Points</th><th>Each leg</th></tr>");

            for (var i = 0; i < overall.Count; i++)
            {
                var s = overall[i];
                var name = profiles.Get(s.Transponder)?.DriverName is { Length: > 0 } profileName ? profileName : s.DriverName;
                sb.AppendLine($"<tr><td class=\"num\">{i + 1}</td><td>{Esc(name)}</td><td class=\"num\">{Esc(s.Words())}</td><td>{Esc(s.Detail)}</td></tr>");
            }

            sb.AppendLine("</table>");
        }
    }

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
            // Every class of a shared heat (Phase 4.1) has qualifying of its own.
            .SelectMany(r => MixedHeats.ClassesOf(r!))
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

            // Points qualifying (11 Nov 2026, Phase 5.1): the points decide
            // the order, so they lead the row, and "Best laps" is the best
            // single heat - the tie-breaker - not a total.
            var points = raceClass?.QualifyingMethod == QualifyingMethod.RoundPoints;

            sb.AppendLine($"<h3>{Esc(className)}</h3>");
            sb.AppendLine("<table><tr><th class=\"num\">Pos</th><th>Driver</th>"
                          + (points ? "<th class=\"num\">Points</th>" : string.Empty)
                          + "<th class=\"num\">Best laps</th>"
                          + (consecutive ? $"<th class=\"num\">Best {lapCount} consec.</th>" : string.Empty)
                          + "<th class=\"num\">Fastest lap</th><th>Counted</th></tr>");

            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                sb.AppendLine("<tr>"
                              + $"<td class=\"num\">{i + 1}</td>"
                              + $"<td>{Esc(e.DriverName)}</td>"
                              + (points ? $"<td class=\"num\">{Esc(e.PointsWords())}</td>" : string.Empty)
                              + $"<td class=\"num\">{e.BestLaps}</td>"
                              + (consecutive ? $"<td class=\"num\">{(e.BestConsecutiveMs > 0 ? $"{Secs(e.BestConsecutiveMs)}s" : "-")}</td>" : string.Empty)
                              + $"<td class=\"num\">{(e.BestFastLapMs > 0 ? $"{Secs(e.BestFastLapMs)}s" : "-")}</td>"
                              + $"<td>{Esc(e.SourceRoundLabel)}{(string.IsNullOrWhiteSpace(e.Forfeit) ? string.Empty : $" <em>({Esc(e.Forfeit)})</em>")}</td>"
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
        // By the name saved with each result (the transponder only when it
        // has none) AND the transponder: grouped by transponder alone, two
        // drivers who used the same unit that day came out as one row under
        // whoever owns it now; by name alone, two drivers called Chris did.
        var bests = sessions
            .SelectMany(s => s.Results.Where(r => r.FastLapMs > 0 && !GhostService.IsGhost(r.Transponder)))
            .GroupBy(r => (
                Name: (string.IsNullOrWhiteSpace(r.DriverName) ? r.Transponder ?? string.Empty : r.DriverName).Trim().ToUpperInvariant(),
                Transponder: (r.Transponder ?? string.Empty).Trim().ToUpperInvariant()))
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
                          + $"<td class=\"num\">{Secs(bests[i].FastLapMs)}s</td></tr>");
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
            // ForDay leaves out inspections filed against a race day that
            // has been deleted - kept only for its undo - which used to show
            // here as if that day's scrutineering still happened.
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
                var laps = string.Join(", ", result.LapTimesMs.Select(l => Secs(l)));
                sb.AppendLine($"<p><strong>{Esc(name)}</strong><br><span style=\"font-size:13px;color:#555\">{Esc(laps)}</span></p>");
            }
        }
    }

    /// <summary>
    /// The name the race saved with the result, as Race History and the
    /// exports show it; the transponder's live profile only for a result
    /// saved with no name. The profile used to come first, so once a
    /// transponder changed hands an old day's report named its new owner as
    /// the heat winner and fastest lap (see ResultsExporter.NameFor).
    /// </summary>
    private static string NameFor(string transponder, string fallback, ProfileStore profiles)
    {
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        var profile = profiles.Get(transponder);
        if (!string.IsNullOrWhiteSpace(profile?.DriverName))
        {
            return profile!.DriverName;
        }

        return string.IsNullOrWhiteSpace(fallback) ? transponder : fallback;
    }

    /// <summary>HTML-escapes a value. Driver names and notes are free text and can contain anything.</summary>
    private static string Esc(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>
    /// Milliseconds as "12.345", whatever the PC's culture (9 Nov 2026, Bug
    /// Hunt 15, B35). On a decimal-comma PC every time came out "12,345", and
    /// the lap-by-lap list - joined with ", " - read "12,345, 13,456", where
    /// a lap's comma and the comma between laps were the same character.
    /// Invariant, as TonightAtTheTrack.Lap and the CSV export already are.
    /// </summary>
    private static string Secs(long ms) => (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
}
