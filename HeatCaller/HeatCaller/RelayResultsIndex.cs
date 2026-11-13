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
    ///
    /// WHY (23 Oct 2026, bug hunt 10): by the race NIGHT, not the calendar
    /// date. A main that STARTED at 00:10 was filed under the next day - and
    /// on New Year's Eve the next year's document - so one night showed as
    /// two race days on the archive, and disagreed with the driver's own
    /// night page. Now the same 6am turn as TonightAtTheTrack.NightOf.
    /// </summary>
    internal static string YearOf(SessionRecord session) =>
        NightOf(session).Year.ToString("D4", CultureInfo.InvariantCulture);

    /// <summary>The night a session is filed under, as yyyy-MM-dd in local time. See YearOf for why local and why the night, and Invariant for why invariant.</summary>
    internal static string DayOf(SessionRecord session) =>
        NightOf(session).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The race night a session belongs to - see YearOf.</summary>
    private static DateTime NightOf(SessionRecord session)
    {
        var local = LocalStart(session);

        // A session saved with no time at all is DateTime.MinValue, and six
        // hours before that is no date - filed under its own day, as before.
        return local < DateTime.MinValue.AddDays(1) ? local.Date : TonightAtTheTrack.NightOf(local);
    }

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

    /// <summary>
    /// Whether the relay may already hold a copy of this session's result -
    /// what RelayPublisher.PublishResult's correction means (see
    /// Ledger.NoteWithheld). Only a session's first publish, uncorrected,
    /// can be one the relay has never had.
    ///
    /// It used to be "has been corrected" alone. A re-send of an
    /// uncorrected result - the startup re-send, Publish again from a phone,
    /// a correction undone back to none - that could not be queued (a
    /// restored relay not yet confirmed, a blank token) then marked a result
    /// the relay still held as withheld, and the year's index left that
    /// whole night out for good.
    /// </summary>
    internal static bool MayAlreadyBeOnRelay(SessionRecord session, bool firstPublish) =>
        !firstPublish || session.Edits is { Count: > 0 };

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
            if (!session.IsNotScored())
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
    /// <param name="onRelay">
    /// Whether the relay holds a session's result, when the caller knows
    /// better than one instant can - see Ledger. Applied as well as
    /// <paramref name="from"/>, so the app passes this and no window.
    /// </param>
    internal static string Build(
        string year,
        IEnumerable<SessionRecord> all,
        Func<string?, string?> seasonNameFor,
        bool abbreviate,
        DateTime? from,
        Func<SessionRecord, bool>? onRelay = null,
        Func<string, IReadOnlyList<SessionRecord>, string?>? paceOfDay = null)
    {
        var mine = all
            .Where(s => !s.IsNotScored() && YearOf(s) == year)
            .Where(s => from is null || EffectiveStart(s) >= from.Value)
            .Where(s => onRelay is null || onRelay(s))

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

        // WHY (23 Oct 2026, bug hunt 10): with each name's transponder, as
        // the session results pass theirs. A saved row keeps the name the
        // driver had on the night, so by name alone a driver renamed since
        // and then shown by initials went out under the old full name - in
        // the driver list, the winners and the podium the link-preview card
        // draws - right after the privacy batch had resent this index.
        var privacy = PublicPrivacy.Current;
        var shown = RelayPublisher.DisplayNames(
            everyName,
            abbreviate,
            privacy.TransponderForEach(
                everyName,
                mine.SelectMany(s => s.Results.Select(r => (
                    (string?)RelayDocuments.PublicNameOrUnnamed(r.DriverName, r.Transponder),
                    (string?)r.Transponder)))),
            privacy);

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
        //
        // With the night's pace beside a day when there is one (12 Nov 2026,
        // setup plan step 8, build plan 0.1): "Green to green 6:10 · 31% on
        // track", worked out by the laptop from the day's sessions, since the
        // relay doesn't read results. Left out of a day with none, so an
        // older page - and a day with one race - sees the shape it always did.
        var days = sessionDays
            .Select((day, index) => (Day: day, Index: index))
            .GroupBy(x => x.Day, StringComparer.Ordinal)
            .OrderByDescending(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var indexes = g.Select(x => x.Index).ToList();
                var pace = paceOfDay?.Invoke(g.Key, indexes.Select(i => mine[i]).ToList());

                return string.IsNullOrWhiteSpace(pace)
                    ? (object)new { day = g.Key, sessions = indexes }
                    : new { day = g.Key, sessions = indexes, pace };
            })
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

    /// <summary>
    /// A race day's night pace for its heading on the club's results page
    /// (12 Nov 2026, setup plan step 8): NightPace.PublicLine - the numbers,
    /// never the causes - or null with fewer than two races. The program it
    /// is read against is the race day whose rounds the sessions ran, when
    /// one can be found, for "18 of 18 races run" and its planned breaks.
    /// </summary>
    internal static string? PaceOf(IReadOnlyList<SessionRecord> daySessions, IEnumerable<RoundDefinition> rounds, IEnumerable<RaceEvent> raceDays)
    {
        var roundIds = daySessions.Select(s => s.RoundId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var raceDay = roundIds.Count == 0
            ? null
            : raceDays.FirstOrDefault(d => d is { IsLegacy: false } && d.RoundIds.Any(roundIds.Contains));

        var pace = NightPace.For(daySessions, rounds, raceDay?.RoundIds, breaks: raceDay?.Breaks);
        var line = pace is null ? string.Empty : NightPace.PublicLine(pace);

        return line.Length == 0 ? null : line;
    }

    /// <summary>
    /// Which sessions the relay holds, where RelayResultsFromUtc alone
    /// cannot say.
    ///
    /// The window was one instant - "everything raced since this is on the
    /// relay" - and nothing recorded a gap in it. A session raced while
    /// publishing was unticked was never sent, yet sat inside the window,
    /// so the next index listed it and the archive offered a race day that
    /// came back "that result is not on the relay any more". The other way
    /// round, re-sending one old session from the phone dragged the window
    /// back past every session between it and the old start, none of which
    /// the relay had ever been given. This keeps the exceptions instead:
    /// sessions inside the window the relay was not given, and sessions
    /// before it that it was. Ids only, so a season of them is a few
    /// kilobytes.
    ///
    /// Its own file rather than settings, and kept by RelayPublisher, which
    /// is where it is known whether a result is going anywhere at all. A
    /// file that is missing or unreadable is the window alone - exactly
    /// what was there before this existed.
    /// </summary>
    internal sealed class Ledger
    {
        /// <summary>Inside the window, but never given to the relay. Left out of the index.</summary>
        public HashSet<string> Withheld { get; set; } = new(StringComparer.Ordinal);

        /// <summary>Before the window, but given to the relay since. Listed in the index.</summary>
        public HashSet<string> Extra { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Results the relay has not confirmed it holds as they now stand:
        /// corrections made while they could not be sent (the relay still
        /// has the uncorrected copy), and results queued but not yet taken.
        /// These are sent again the next time results are, however old -
        /// see MainForm.RepublishRecentResults.
        ///
        /// Queued ones are here because the queue lives only in memory. A
        /// night with the relay unreachable, the laptop then shut down, lost
        /// every result in it - and a club racing weekly reopened the app a
        /// week later, well outside the 36 hours republished, so that night
        /// never reached the relay at all. Taken off only when the relay
        /// takes the result (RelayPublisher.Drain), not when it is queued.
        /// </summary>
        public HashSet<string> Owed { get; set; } = new(StringComparer.Ordinal);

        /// <summary>
        /// The relay address and club the window and these exceptions
        /// describe, or null when that was never recorded (a ledger from
        /// before this existed, or none yet). See BelongsTo.
        ///
        /// Recorded rather than inferred from the settings saved before. A
        /// restored backup holds the relay back until it is confirmed, and a
        /// club id saved with a typo and put right a moment later is two
        /// "changes" of relay - each of those cleared the window and the
        /// ledger, and the next index dropped every earlier race day of the
        /// season though the relay still held them all.
        /// </summary>
        public string? Relay { get; set; }

        /// <summary>The club on <see cref="Relay"/>. See there.</summary>
        public string? Club { get; set; }

        /// <summary>
        /// Whether this ledger describes that relay and club. One that never
        /// recorded which is taken to be the one in use, as it always was.
        /// </summary>
        public bool BelongsTo(string relay, string club) =>
            Relay is null
            || (string.Equals(Relay, relay, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Club, club, StringComparison.Ordinal));

        /// <summary>Records which relay and club this ledger describes, if it had not said. Returns whether that changed anything.</summary>
        public bool Adopt(string relay, string club)
        {
            if (Relay is not null)
            {
                return false;
            }

            Relay = relay;
            Club = club;
            return true;
        }

        /// <summary>
        /// Starts again for a different relay or club, which has been given
        /// nothing this ledger knows of. The caller clears the window with it.
        ///
        /// Owed is kept: those are results this laptop still means to send,
        /// and sending them to the relay now in use is what they are for.
        /// </summary>
        public void MoveTo(string relay, string club)
        {
            Withheld.Clear();
            Extra.Clear();
            Relay = relay;
            Club = club;
        }

        internal static string FilePath => Path.Combine(AppPaths.Root, "relay-results.json");

        /// <summary>Whether the relay holds this session's result, by the window and the exceptions to it.</summary>
        public bool Listed(string sessionId, DateTime startedUtc, DateTime? from)
        {
            if (Extra.Contains(sessionId))
            {
                return true;
            }

            // No window is nothing published yet - see AppSettings. It used
            // to mean "no filter", so an index built before the first publish
            // to a new relay, or after Delete Everything Published, listed
            // the club's whole history.
            return from is { } start && startedUtc >= start && !Withheld.Contains(sessionId);
        }

        /// <summary>
        /// The relay has taken a result. Returns the window as it now stands
        /// - opened now if there was none.
        ///
        /// A session from before the window is noted as an exception rather
        /// than moving the window back to it: moving it claimed every session
        /// in between as well.
        /// </summary>
        public DateTime? NoteSent(string sessionId, DateTime startedUtc, DateTime? from, out bool changed)
        {
            changed = Withheld.Remove(sessionId);
            changed |= Owed.Remove(sessionId);

            if (startedUtc <= DateTime.MinValue)
            {
                return from;
            }

            if (from is null)
            {
                // Opened NOW, with this session as an exception before it -
                // not at this session's start. The first result a relay is
                // given is often not the newest: the republish after startup
                // goes oldest first and takes in any session corrected
                // lately, however old, so a heat from March corrected
                // yesterday opened the window in March, and the index then
                // claimed every race day from March on though the relay had
                // been given that heat and the last 36 hours. Opened now,
                // everything sent so far is listed by name and everything
                // raced from here on by the window, which is exactly what
                // the relay holds.
                var opened = DateTime.UtcNow;

                if (startedUtc < opened)
                {
                    changed |= Extra.Add(sessionId);
                }

                return opened;
            }

            if (startedUtc < from.Value)
            {
                changed |= Extra.Add(sessionId);
            }

            return from;
        }

        /// <summary>
        /// A result could not be queued - publishing off, the relay not set
        /// up, or the document too big - or the relay refused it outright.
        /// Returns whether anything changed.
        /// </summary>
        /// <param name="correction">
        /// The session had been saved before. A first save that was withheld
        /// is already here, and one that went is on the relay - so a
        /// correction is owed rather than withheld, or the index would drop
        /// a result the relay still holds.
        /// </param>
        public bool NoteWithheld(string sessionId, DateTime startedUtc, DateTime? from, bool correction)
        {
            if (!Listed(sessionId, startedUtc, from))
            {
                return false;
            }

            return correction ? Owed.Add(sessionId) : Withheld.Add(sessionId);
        }

        /// <summary>
        /// True when relay-results.json was there at startup but could not be
        /// read - see SafeFile.HoldWritesAfterUnreadable - and <see cref="Save"/>
        /// then writes nothing this session.
        ///
        /// The empty ledger it fell back to used to be saved over the file
        /// within seconds of startup, so a sync or backup tool holding it for
        /// a moment lost every result re-sent from before the window (a
        /// club's whole first night among them), every result still owed and
        /// every one withheld. Publishing carries on from memory, and the
        /// file is read again at the next start.
        ///
        /// A new relay or club taking a result (MoveTo) is not written while
        /// held either, so the next start reads the old relay's ledger back
        /// and starts again for the new one at its next delivery - a few of
        /// this session's results drop out of its index until they are sent
        /// again, which beats losing the old ledger's owed results.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HoldWrites { get; private set; }

        /// <summary>Forgets every exception, for when the window itself is cleared.</summary>
        public bool Clear()
        {
            var had = Withheld.Count + Extra.Count + Owed.Count > 0;

            // Written even while held. Clearing goes with Delete Everything
            // Published, after which nothing the file holds is true, and left
            // there it put the deleted results back in the index - and sent
            // the owed ones again - at the next start.
            had |= HoldWrites;
            HoldWrites = false;

            Withheld.Clear();
            Extra.Clear();
            Owed.Clear();
            return had;
        }

        /// <summary>The ledger on disk, or an empty one. Never throws.</summary>
        public static Ledger Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonSerializer.Deserialize<Ledger>(SafeFile.ReadAllTextWithRetry(FilePath));

                    if (loaded is not null)
                    {
                        // Deserialised sets compare by default, which for
                        // strings is ordinal - but say so rather than rely on it.
                        return new Ledger
                        {
                            Withheld = new HashSet<string>(loaded.Withheld ?? new(), StringComparer.Ordinal),
                            Extra = new HashSet<string>(loaded.Extra ?? new(), StringComparer.Ordinal),
                            Owed = new HashSet<string>(loaded.Owed ?? new(), StringComparer.Ordinal),
                            Relay = loaded.Relay,
                            Club = loaded.Club,
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                // The window alone, as before this file existed - but, when
                // it could not be read at all or no copy of it could be kept,
                // never saved over this session (see HoldWrites).
                return new Ledger { HoldWrites = SafeFile.HoldWritesAfterUnreadable(FilePath, ex) };
            }

            return new Ledger();
        }

        /// <summary>Writes the ledger, unless <see cref="HoldWrites"/>. Never throws; SafeFile reports a failed write.</summary>
        public void Save()
        {
            if (HoldWrites)
            {
                return;
            }

            SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
    }
}
