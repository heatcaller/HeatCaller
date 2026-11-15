using System.Collections.Concurrent;
using System.Drawing;

namespace HeatCaller;

// Setting up the night, the race director's way (12 Nov 2026, setup plan
// step 1 - plans/race-night-setup-plan.md, Part 4, items D1, D2, D3 and D5).
//
// WHY: clicks are not what slow a club night down. People, cutoffs and
// handovers are. A club runs on "entries close at 6:30, racing at 7:00". It
// pins the heats on a board, and drivers look up their heat once and trust
// it. Practice is when every car crosses the loop. And a dirt track gets
// watered after Q2. None of that was in the app, so the director carried it
// all in their head. Everything here is pure and is tested without a
// window; the pages it shows on read these and nothing else.

/// <summary>
/// A break planned into the night before it starts (D5), such as "water the
/// track after Q2, 10 minutes" or "dinner before the finals, 20 minutes". The
/// night plan's finish and estimated starts allow for it. So does the gap
/// before the race that follows, which auto-start counts, and the announcer
/// tells the room when it begins.
/// </summary>
public sealed class PlannedBreak
{
    /// <summary><see cref="AfterRound"/> for a break before the finals, after the last qualifier.</summary>
    public const int BeforeFinals = 0;

    /// <summary>
    /// The qualifying round it follows: 1 for after Q1 (Race 1 on a club
    /// night), and so on. <see cref="BeforeFinals"/> for between the last
    /// qualifier and the first main.
    /// </summary>
    public int AfterRound { get; set; } = 1;

    /// <summary>How long it is, in minutes, from 1 to <see cref="NightBreaks.MaxMinutes"/>.</summary>
    public int Minutes { get; set; } = 10;

    /// <summary>What it's for, said to the room ("water the track"). May be blank.</summary>
    public string What { get; set; } = string.Empty;

    public PlannedBreak Copy() => new() { AfterRound = AfterRound, Minutes = Minutes, What = What };
}

/// <summary>Where the night's planned breaks fall, and how they're said (D5).</summary>
public static class NightBreaks
{
    /// <summary>The longest break the Breaks window takes, in minutes.</summary>
    public const int MaxMinutes = 120;

    /// <summary>
    /// Where a round sits in the night for the breaks: a qualifier is its
    /// round number, and a main comes after every qualifier. Null for a
    /// practice, or for a heat whose label was changed by hand so its round
    /// can't be told. No break is put next to a round like that, because a
    /// break in the wrong place is worse than none.
    /// </summary>
    internal static int? PositionOf(RoundDefinition? round) => round switch
    {
        null => null,
        { Kind: RoundKind.Main } => int.MaxValue,
        { Kind: RoundKind.Qualifier } q => NightPlan.RoundNumber(q.RoundLabel),
        _ => null,
    };

    /// <summary>A break's place between two positions: after its round, or just before the mains.</summary>
    private static int PointOf(PlannedBreak b) => b.AfterRound <= PlannedBreak.BeforeFinals ? int.MaxValue - 1 : b.AfterRound;

    /// <summary>
    /// The breaks that fall between two races that run one after the other:
    /// "after Q2" falls between Q2 Heat 4 and Q3 Heat 1, and also between
    /// Q2 and the first main when Q3 has been dropped. None between two
    /// heats of the same round. <paramref name="reached"/> is the furthest
    /// position the night has already got to: a break behind it was crossed
    /// already, so a held heat of an earlier round run late does not bring
    /// it round again.
    /// </summary>
    public static IReadOnlyList<PlannedBreak> Between(RoundDefinition? before, RoundDefinition? next, IEnumerable<PlannedBreak>? breaks, int? reached = null)
    {
        if (breaks is null || PositionOf(before) is not { } a || PositionOf(next) is not { } b || b <= a)
        {
            return Array.Empty<PlannedBreak>();
        }

        if (reached is { } furthest && furthest > a)
        {
            a = furthest;
        }

        return breaks
            .Where(x => x is not null && x.Minutes > 0 && PointOf(x) >= a && PointOf(x) < b)
            .ToList();
    }

    /// <summary>How long some breaks take in all.</summary>
    public static TimeSpan Length(IEnumerable<PlannedBreak> breaks) =>
        TimeSpan.FromMinutes(breaks.Sum(b => Math.Clamp(b.Minutes, 0, MaxMinutes)));

    /// <summary>When a break falls, as the Breaks window and the Setup page say it: "After Q2", "After Race 2", "Before the finals".</summary>
    public static string WhenName(int afterRound, bool clubNight) =>
        afterRound <= PlannedBreak.BeforeFinals
            ? "Before the finals"
            : clubNight ? $"After Race {afterRound}" : $"After Q{afterRound}";

    /// <summary>
    /// The breaks on one line for the Setup page: "After Q2, 10 min (water
    /// the track) · Before the finals, 20 min (dinner)", or "None" when
    /// there are none.
    /// </summary>
    public static string Summary(IReadOnlyList<PlannedBreak>? breaks, bool clubNight)
    {
        if (breaks is null || breaks.Count == 0)
        {
            return "None";
        }

        return string.Join(" · ", Ordered(breaks).Select(b =>
            $"{WhenName(b.AfterRound, clubNight)}, {b.Minutes} min"
            + (string.IsNullOrWhiteSpace(b.What) ? string.Empty : $" ({b.What.Trim()})")));
    }

    /// <summary>The breaks in the order the night meets them.</summary>
    public static IReadOnlyList<PlannedBreak> Ordered(IEnumerable<PlannedBreak> breaks) =>
        breaks.Where(b => b is not null).OrderBy(PointOf).ToList();

    /// <summary>A copy of the breaks, for a new race day that starts from an old one.</summary>
    public static List<PlannedBreak> Copy(IEnumerable<PlannedBreak>? breaks) =>
        (breaks ?? Enumerable.Empty<PlannedBreak>()).Where(b => b is not null).Select(b => b.Copy()).ToList();

    /// <summary>
    /// What the announcer says as a break begins: "Time for a 10-minute
    /// break - water the track. Racing starts again at about 20:40."
    /// </summary>
    public static string Announcement(IReadOnlyList<PlannedBreak> breaks, DateTime resumesUtc)
    {
        var minutes = (int)Math.Round(Length(breaks).TotalMinutes);
        var whats = breaks
            .Select(b => b.What?.Trim())
            .Where(w => !string.IsNullOrEmpty(w))
            .ToList();

        var why = whats.Count == 0 ? string.Empty : $" - {string.Join(" and ", whats)}";

        return $"Time for a {minutes}-minute break{why}. Racing starts again {NightPlan.About(resumesUtc)}.";
    }
}

/// <summary>
/// Entries close (D1): the time on the race day after which a driver who
/// checks in is a late entry. The announcer calls it ten minutes before
/// and again as it passes, and the Next Steps card counts down to it.
/// </summary>
public static class EntriesClose
{
    /// <summary>How long before the close the announcer gives the first call.</summary>
    public static readonly TimeSpan FirstCall = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long after the close it's still worth saying "entries are now
    /// closed". An app opened an hour later shouldn't announce it.
    /// </summary>
    public static readonly TimeSpan ClosedCallFor = TimeSpan.FromMinutes(15);

    /// <summary>A call the announcer makes about the close.</summary>
    public enum Call
    {
        None,

        /// <summary>"Entries close in 10 minutes."</summary>
        Soon,

        /// <summary>"Entries are now closed."</summary>
        Closed,
    }

    /// <summary>When entries close as a moment, the occurrence nearest <paramref name="nowUtc"/> - or null with no time set.</summary>
    public static DateTime? AtUtc(RaceEvent? day, DateTime nowUtc) =>
        day?.EntriesCloseLocalTime is { } time ? NightPlan.NearestOccurrence(time, nowUtc) : null;

    /// <summary>
    /// Which call is due now, if any. Only before the night's racing has
    /// started: once a race has run, "entries close" is old news, and a
    /// club that set the time but is already racing doesn't want to hear it.
    /// </summary>
    /// <param name="saidSoon">Whether the first call has been made for this close.</param>
    /// <param name="saidClosed">Whether the closed call has been made for this close.</param>
    public static Call Due(DateTime closeUtc, DateTime nowUtc, bool racingStarted, bool saidSoon, bool saidClosed)
    {
        if (racingStarted)
        {
            return Call.None;
        }

        if (nowUtc >= closeUtc)
        {
            return !saidClosed && nowUtc < closeUtc + ClosedCallFor ? Call.Closed : Call.None;
        }

        // Inside the last ten minutes, but not the last one: an app opened at
        // 6:29 for a 6:30 close says "closed" a minute later, not both.
        return !saidSoon && closeUtc - nowUtc <= FirstCall && closeUtc - nowUtc > TimeSpan.FromMinutes(1)
            ? Call.Soon
            : Call.None;
    }

    /// <summary>What the announcer says for a call.</summary>
    public static string Line(Call call, DateTime closeUtc, DateTime nowUtc)
    {
        if (call == Call.Closed)
        {
            return "Entries are now closed. Tonight's heats are being put together - check the board or your phone for yours.";
        }

        var minutes = Math.Max(2, (int)Math.Ceiling((closeUtc - nowUtc).TotalMinutes));

        return $"Entries close in {minutes} minutes. If you're racing tonight and haven't checked in, see the desk now.";
    }

    /// <summary>
    /// The Next Steps card while the heats are still to build: "Entries close
    /// at 18:30, in 12 min." before, "Entries closed at 18:30." after.
    /// </summary>
    public static string Note(DateTime closeUtc, DateTime nowUtc)
    {
        var at = AppTime.TimeOfDay(closeUtc.ToLocalTime());

        if (nowUtc >= closeUtc)
        {
            return $"Entries closed at {at} - build the heats and post them.";
        }

        var minutes = Math.Max(1, (int)Math.Ceiling((closeUtc - nowUtc).TotalMinutes));

        return $"Entries close at {at}, in {NightPlan.Minutes(TimeSpan.FromMinutes(minutes))}. Build the heats then - or now, if everyone is here.";
    }

    /// <summary>The line on Who's Racing once entries are closed: anyone checked in now is a late entry.</summary>
    public static string? LateLine(RaceEvent? day, DateTime nowUtc) =>
        AtUtc(day, nowUtc) is { } close && nowUtc >= close && nowUtc < close + TimeSpan.FromHours(8)
            ? $"Entries closed at {AppTime.TimeOfDay(close.ToLocalTime())}. A driver checked in now is a late entry: "
              + "they go into the heats still to run without moving anyone else (once the heats are posted, Next Steps asks first)."
            : null;
}

/// <summary>
/// Posting the heats (D2) and the practice check that goes with it (D3).
/// Posting is the moment the heats go on the board: the heat sheets and
/// marshal list are printed, the room is told, and from then on nothing
/// moves a driver without asking, because drivers have looked up their heat.
/// </summary>
public static class HeatPosting
{
    /// <summary>
    /// Whether tonight's heats are built and still to be posted: the club
    /// posts its heats, the day is open, it has races, and none has run yet.
    /// Once a race has run, the heats are out whether or not anyone pressed
    /// Post, so the card stops asking.
    /// </summary>
    public static bool Owed(RaceEvent? day, IReadOnlyList<RoundDefinition> dayRounds, bool clubPostsHeats) =>
        clubPostsHeats
        && day is { IsLegacy: false, IsOpen: true, HeatsPostedUtc: null }
        && dayRounds.Any(r => r is not null && r.Kind != RoundKind.Practice)
        && !dayRounds.Any(r => r is not null && r.IsRecorded && r.Kind != RoundKind.Practice);

    /// <summary>Whether the heats have been posted and still stand, so a change that moves drivers should ask first.</summary>
    public static bool Posted(RaceEvent? day) => day is { IsLegacy: false, HeatsPostedUtc: not null };

    /// <summary>
    /// The question asked before something that moves drivers in posted heats:
    /// "The heats were posted at 18:42. Drivers may already have looked theirs
    /// up..."
    /// </summary>
    public static string ChangeQuestion(RaceEvent day, string what) =>
        $"The heats were posted at {AppTime.TimeOfDay((day.HeatsPostedUtc ?? DateTime.UtcNow).ToLocalTime())}, so drivers and marshals may already have looked theirs up.\r\n\r\n"
        + $"{what} Go ahead anyway?\r\n\r\n"
        + "To fit in a driver who arrived late without moving anyone else, use Add a Late Entry (right-click the program) instead.";

    /// <summary>
    /// The cars on tonight's heats that the loop hasn't heard tonight (D3):
    /// each transponder in a race still to run, other than ghosts, that
    /// neither crossed the line tonight nor counted a lap in a race saved
    /// tonight. In program order, each once.
    /// </summary>
    public static IReadOnlyList<string> NotHeard(IEnumerable<RoundDefinition> dayRounds, Func<string, bool> heardTonight)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var round in dayRounds.Where(r => r is not null && !r.IsRecorded && r.Kind != RoundKind.Practice))
        {
            foreach (var transponder in round.AssignedTransponders)
            {
                if (string.IsNullOrWhiteSpace(transponder) || GhostService.IsGhost(transponder) || !seen.Add(transponder.Trim()))
                {
                    continue;
                }

                if (!heardTonight(transponder.Trim()))
                {
                    missing.Add(transponder.Trim());
                }
            }
        }

        return missing;
    }

    /// <summary>
    /// The not-heard list as the card and the Post window say it: "4 cars
    /// on tonight's heats haven't crossed the loop tonight: Al Gorithim,
    /// Rhoda Rage, Hugh Turn and 1 more. Get them out in practice, or check
    /// their transponders, before Q1." Empty when every car has been heard.
    /// </summary>
    public static string NotHeardWords(IReadOnlyList<string> names, int listed = 3)
    {
        if (names.Count == 0)
        {
            return string.Empty;
        }

        var shown = names.Take(listed).ToList();
        var more = names.Count - shown.Count;

        var list = more > 0
            ? $"{string.Join(", ", shown)} and {more} more"
            : shown.Count == 1 ? shown[0] : $"{string.Join(", ", shown.Take(shown.Count - 1))} and {shown[^1]}";

        return names.Count == 1
            ? $"1 car on tonight's heats hasn't crossed the loop tonight: {list}. Get it out in practice, or check its transponder, before the first race."
            : $"{names.Count} cars on tonight's heats haven't crossed the loop tonight: {list}. Get them out in practice, or check their transponders, before the first race.";
    }

    /// <summary>What the announcer says as the heats are posted.</summary>
    public static string Announcement(bool clubNight) =>
        clubNight
            ? "Tonight's races are posted. Check the board, or your phone, to see when you're on."
            : "Tonight's heats are posted. Check the board, or your phone, for your heat and your marshal post.";
}

/// <summary>
/// Every transponder the loop has heard tonight (D3), however long ago,
/// which TransponderWatch can't say because
/// it forgets after twelve minutes. Fed with every line pass. A restart
/// loses it, which is why <see cref="HeatPosting.NotHeard"/> also counts the
/// races saved tonight.
/// </summary>
public static class TonightsSightings
{
    private static readonly ConcurrentDictionary<string, DateTime> LastHeard = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Notes that a transponder crossed the line. Any thread.</summary>
    public static void Note(string? transponder, DateTime utc)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return;
        }

        LastHeard.AddOrUpdate(transponder.Trim(), utc, (_, was) => utc > was ? utc : was);
    }

    /// <summary>Whether the transponder has crossed the line since <paramref name="sinceUtc"/>.</summary>
    public static bool HeardSince(string transponder, DateTime sinceUtc) =>
        LastHeard.TryGetValue(transponder.Trim(), out var last) && last >= sinceUtc;

    /// <summary>
    /// When tonight began: 6am local, the same turnover as
    /// TransponderHealthStore, so a club night that runs past midnight is
    /// still one night.
    /// </summary>
    public static DateTime NightStartUtc(DateTime nowLocal)
    {
        var start = nowLocal.Date.AddHours(6);

        if (nowLocal < start)
        {
            start = start.AddDays(-1);
        }

        return DateTime.SpecifyKind(start, DateTimeKind.Local).ToUniversalTime();
    }

    /// <summary>
    /// The transponders that counted a lap on the loop in races saved since
    /// <paramref name="sinceUtc"/>, practice included. A hand-counted lap
    /// proves nothing about the transponder, so only timed laps count.
    /// </summary>
    public static HashSet<string> InSessions(IEnumerable<SessionRecord> sessions, DateTime sinceUtc)
    {
        var heard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in sessions.Where(s => s is not null && s.EndedAtUtc >= sinceUtc))
        {
            foreach (var row in session.Results ?? new List<SessionResultEntry>())
            {
                if (!string.IsNullOrWhiteSpace(row.Transponder) && (row.LapTimesMs?.Count ?? 0) > 0)
                {
                    heard.Add(row.Transponder.Trim());
                }
            }
        }

        return heard;
    }

    /// <summary>
    /// The transponders that count as this car on the loop (12 Nov 2026,
    /// setup plan step 7): the one on the heat, and every one its driver
    /// owns. A driver in two classes often moves one transponder between
    /// cars, or runs a second in their other car - either crossing is them.
    /// </summary>
    public static IReadOnlyList<string> IdsOf(string transponder, DriverProfile? owner)
    {
        var ids = new List<string> { transponder.Trim() };

        foreach (var id in owner?.AllTransponderIds ?? Array.Empty<string>())
        {
            if (!ids.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>Whether any of these transponders has been heard.</summary>
    public static bool HeardAny(IEnumerable<string> ids, Func<string, bool> heard) => ids.Any(heard);

    /// <summary>For tests.</summary>
    internal static void Clear() => LastHeard.Clear();
}

/// <summary>
/// The marshal list as printed pages, to pin on the board beside the heat
/// sheets when the heats are posted (D2): MarshalAssigner.ToText, a class
/// at a time, on pages the size of the heat sheet's.
/// </summary>
internal static class MarshalSheetRenderer
{
    private const int PageWidth = 1275;
    private const int PageHeight = 1650;
    private const int Margin = 70;
    private const int LineHeight = 30;

    /// <summary>The pages for these classes' marshal lists. The caller disposes them.</summary>
    public static IReadOnlyList<Bitmap> RenderPages(IReadOnlyList<(string ClassName, string Text)> lists)
    {
        var lines = new List<string>();

        foreach (var (_, text) in lists)
        {
            lines.AddRange(text.Replace("\r", string.Empty).Split('\n'));
            lines.Add(string.Empty);
        }

        var perPage = (PageHeight - 2 * Margin - 40) / LineHeight;
        var pages = new List<Bitmap>();

        try
        {
            for (var first = 0; first < Math.Max(1, lines.Count); first += perPage)
            {
                var bitmap = new Bitmap(PageWidth, PageHeight);
                pages.Add(bitmap);

                using var g = Graphics.FromImage(bitmap);
                g.Clear(Color.White);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                using var heading = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Point);
                using var body = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Point);
                using var foot = new Font("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);

                var y = (float)Margin;

                foreach (var line in lines.Skip(first).Take(perPage))
                {
                    var isHeading = line.StartsWith("CORNER MARSHALS", StringComparison.Ordinal);
                    g.DrawString(line, isHeading ? heading : body, Brushes.Black, Margin, y);
                    y += LineHeight;
                }

                HeatCallerMark.DrawPrintFooter(g, Margin, PageWidth - Margin, PageHeight - Margin, foot);
                bitmap.SetResolution(150f, 150f);
            }
        }
        catch
        {
            foreach (var page in pages)
            {
                page.Dispose();
            }

            throw;
        }

        return pages;
    }
}
