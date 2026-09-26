using System.Globalization;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// What is in the club's published results, so a phone can navigate years
/// of them without downloading years of them.
///
/// WHY THIS EXISTS AT ALL. The relay does not know what a result is, on
/// purpose: it stores what it was given and hands it back, which is what
/// keeps a column added on the Announcements page from being a relay
/// deploy. So nothing on the relay can group results by day, list the
/// seasons, or say which sessions a driver raced in. The laptop knows all
/// of that, so the laptop publishes it - small, separate, and beside the
/// results rather than inside them.
///
/// ONE DOCUMENT PER CALENDAR YEAR. Not per championship season: a session
/// raced outside a championship belongs to no season, and it still has to
/// be in the index or it cannot be found. A championship's name travels on
/// the session as a label instead, so the page can still offer real season
/// names to pick from. A year is also the right unit for publishing - a
/// race night rewrites this year's index and nothing else, rather than
/// re-sending a decade every time a heat finishes.
///
/// WHAT IT DELIBERATELY DOES NOT CARRY. Lap times, positions, anything
/// that belongs in the result itself. The index says a session happened,
/// when, what it was called and who was in it; the page fetches the
/// result by id when somebody opens it. Keeping it to that is what keeps
/// a year of a busy club to tens of kilobytes.
/// </summary>
internal static class RelayResultsIndex
{
    /// <summary>The channel the index is published to. A collection, keyed by the year as four digits.</summary>
    internal const string Channel = "resultsindex";

    /// <summary>
    /// The version of the index's shape, sent so a page can tell what it
    /// is reading. A page built for a newer shape can say so plainly
    /// rather than drawing an empty season; an older page ignores it.
    /// </summary>
    private const int Shape = 1;

    /// <summary>
    /// Which year a session belongs to, as the index keys it.
    ///
    /// Local time, not UTC. A club night that starts at eight in the
    /// evening on the thirty-first of December is that year's racing to
    /// everyone who was there, whatever a UTC clock says about it - and
    /// the day headings on the page have to agree with the year buckets
    /// or a session goes missing between them.
    /// </summary>
    internal static string YearOf(SessionRecord session) =>
        LocalStart(session).Year.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>The day a session is filed under, as yyyy-MM-dd in local time. See YearOf for why local, and Invariant for why invariant.</summary>
    internal static string DayOf(SessionRecord session) =>
        LocalStart(session).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The time of day a session started, as HH:mm.</summary>
    private static string TimeOf(SessionRecord session) =>
        LocalStart(session).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// When a session actually began, whatever it was saved with.
    ///
    /// The same fallback LocalStart makes, and the reason the index's
    /// window filter has to use this rather than StartedAtUtc raw: a
    /// session saved without a start is filed under the day it ENDED, and
    /// filtering on a start of DateTime.MinValue dropped it from the index
    /// while its result went to the relay anyway - on the relay, and
    /// unreachable from the page.
    ///
    /// Internal so MainForm hands RelayPublisher.PublishResult this same
    /// time: the window the index covers (RelayResultsFromUtc) and the
    /// sessions the index files must agree on when a session started.
    /// </summary>
    internal static DateTime EffectiveStart(SessionRecord session) =>
        session.StartedAtUtc > DateTime.MinValue ? session.StartedAtUtc : session.EndedAtUtc;

    private static DateTime LocalStart(SessionRecord session)
    {
        // Started, not ended: a race that runs past midnight belongs to
        // the night it started, which is the night the club remembers.
        var utc = EffectiveStart(session);

        return utc.Kind == DateTimeKind.Utc ? utc.ToLocalTime() : utc;
    }

    // WHY InvariantCulture on every one of these. A custom format string
    // is rendered with the CURRENT culture's calendar and separators, and
    // "yyyy-MM-dd" is not a shape, it is a request. On a Thai machine the
    // Buddhist calendar makes 2026 into "2569" while the Year property
    // above stays Gregorian, so the document would be keyed 2026 with days
    // in another era; on a machine whose time separator is a dot, "HH:mm"
    // comes out "19.30" and the relay's preview reader rejects it. These
    // strings are read by a web page and a link-preview card, not by the
    // person at the laptop, so they are invariant - the same decision
    // ResultsExporter already made for the same reason.

    /// <summary>
    /// The years a set of sessions covers - the ones whose index documents
    /// would have to change if these sessions did.
    /// </summary>
    internal static IReadOnlyCollection<string> YearsOf(IEnumerable<SessionRecord> sessions)
    {
        var years = new HashSet<string>(StringComparer.Ordinal);

        foreach (var session in sessions)
        {
            if (!session.IsDemoSession())
            {
                years.Add(YearOf(session));
            }
        }

        return years;
    }

    /// <summary>
    /// One year's index, as the JSON published to resultsindex/&lt;year&gt;.
    ///
    /// <paramref name="all"/> is the whole history; the sessions outside
    /// this year are ignored. Passing everything rather than a filtered
    /// list is deliberate - the caller cannot then filter it one way here
    /// and another way when deciding which years to republish.
    /// </summary>
    /// <param name="seasonNameFor">A round id to the championship name it belongs to, or null. May return null.</param>
    /// <param name="from">
    /// The earliest session the relay was actually given. Sessions before
    /// it are left out: the laptop's history reaches back further than the
    /// relay does, and an index naming results the relay has never held
    /// would offer race days that come back empty.
    /// </param>
    internal static string Build(
        string year,
        IEnumerable<SessionRecord> all,
        Func<string?, string?> seasonNameFor,
        bool abbreviate,
        DateTime? from)
    {
        var mine = all
            .Where(s => !s.IsDemoSession() && YearOf(s) == year)
            .Where(s => from is null || EffectiveStart(s) >= from.Value)

            // Oldest first here; the page sorts for display. A stable
            // order means the same history builds the same document, so
            // an unchanged year is republished as bytes the relay already
            // has rather than as a document that merely looks new.
            .OrderBy(EffectiveStart)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

        // ONE name map for the whole year, not one per session.
        //
        // DisplayNames abbreviates only far enough that the names it was
        // GIVEN stop clashing, so "Alex Kent" is "Alex K" in one heat and
        // "Alex Ke" in a heat that also had an Alex Kemp. Built per
        // session, the index would file one person under two names and
        // their history would come apart. Built once over the year, every
        // session agrees.
        var everyName = mine
            .SelectMany(s => s.Results.Select(r => RelayDocuments.PublicNameOrUnnamed(r.DriverName, r.Transponder)))
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var shown = RelayPublisher.DisplayNames(everyName, abbreviate);

        // Each driver gets a number that means nothing outside this
        // document. The page needs a stable handle to gather one person's
        // sessions by, and a real one - a transponder id, a profile id -
        // would be a new identifier published about them for no reason.
        var numberOf = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        var drivers = new List<object>();

        for (var i = 0; i < everyName.Count; i++)
        {
            numberOf[everyName[i]] = i;
            drivers.Add(new { n = shown[i] });
        }

        var sessions = new List<object>();
        var sessionDays = new List<string>();
        var raced = new List<List<int>>();

        for (var i = 0; i < drivers.Count; i++)
        {
            raced.Add(new List<int>());
        }

        for (var index = 0; index < mine.Count; index++)
        {
            var session = mine[index];

            var names = session.Results
                .Select(r => RelayDocuments.PublicNameOrUnnamed(r.DriverName, r.Transponder))
                .Where(n => n.Length > 0);

            foreach (var name in names)
            {
                if (numberOf.TryGetValue(name, out var number))
                {
                    // Guarded: a driver listed twice in one session - which
                    // a hand-corrected result can be - must not put the
                    // session in their history twice.
                    if (raced[number].Count == 0 || raced[number][^1] != index)
                    {
                        raced[number].Add(index);
                    }
                }
            }

            var day = DayOf(session);

            sessionDays.Add(day);

            // The winner, by the same year-wide name map as everything
            // else here, so a link preview and the driver's own history
            // call them the same thing. Position 1 rather than "first in
            // the list": a corrected result is saved in the order it was
            // edited, not the order they finished.
            var winner = session.Results
                .Where(r => r.Position == 1)
                .Select(r => RelayDocuments.PublicNameOrUnnamed(r.DriverName, r.Transponder))
                .FirstOrDefault(n => n.Length > 0);

            var won = winner is not null && numberOf.TryGetValue(winner, out var winnerNumber)
                ? shown[winnerNumber]
                : string.Empty;

            // The podium, for the link-preview card the relay draws. Three
            // rows is what fits on it and what a person wants from a result
            // sent to them in a chat: who won, who was close. Names through
            // the same year-wide map as everything else, so the card and
            // the page never disagree about what somebody is called.
            //
            // In the INDEX rather than read out of the result, because the
            // relay draws that card and the relay does not parse results.
            // What a club publishes about itself is the club's to decide;
            // this is the app deciding to publish three names.
            var top = session.Results
                .Where(r => r.Position >= 1 && r.Position <= 3)
                .OrderBy(r => r.Position)
                .Select(r => new
                {
                    p = r.Position,
                    n = numberOf.TryGetValue(
                            RelayDocuments.PublicNameOrUnnamed(r.DriverName, r.Transponder),
                            out var number)
                        ? shown[number]
                        : string.Empty,
                    laps = r.Laps,
                })
                .Where(r => r.n.Length > 0)
                .ToList();

            sessions.Add(new
            {
                id = session.Id,
                day,
                at = TimeOf(session),
                type = session.SessionType.ToString(),
                label = session.Label ?? string.Empty,
                track = session.TrackLayoutName ?? string.Empty,
                season = seasonNameFor(session.RoundId) ?? string.Empty,
                drivers = session.Results.Count,
                won,
                top,
            });
        }

        // Days, newest first, each holding the indexes of its sessions.
        // Built here rather than in the browser so that paging by day
        // costs the page nothing but an array index - and so that "what
        // days did this club race" is one small array rather than
        // something a phone works out by walking every session.
        var days = sessionDays
            .Select((day, index) => (Day: day, Index: index))
            .GroupBy(x => x.Day, StringComparer.Ordinal)
            .OrderByDescending(g => g.Key, StringComparer.Ordinal)
            .Select(g => new { day = g.Key, sessions = g.Select(x => x.Index).ToList() })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            shape = Shape,
            year,

            // Whether names in this document are shortened. The page says
            // so rather than leaving somebody wondering why the archive
            // calls them "Alex K" when the club calls them Alex Kent.
            abbreviated = abbreviate,
            drivers,
            raced,
            sessions,
            days,
        });
    }
}
