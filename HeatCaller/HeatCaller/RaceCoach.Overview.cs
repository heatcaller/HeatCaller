namespace HeatCaller;

/// <summary>
/// The Overview tab's news: what happened at the club lately, written the
/// way somebody at the pit table would tell it.
///
/// 13 Nov 2026, the Race Engineer rework (see RaceCoach): the tab used to
/// open on four counts and a records table, which answered "how much" and
/// never "what happened". Each item here is a plain rule over the saved
/// sessions - a record still standing, a PB taken last night, a driver who
/// got quicker through a race, a driver whose mistakes cost the most - so
/// the same history always reads the same way, and nothing is made up.
/// </summary>
public static partial class RaceCoach
{
    /// <summary>What a piece of news is about. The order here is the order the feed shows them in.</summary>
    public enum NewsKind
    {
        Record,
        PersonalBest,
        GotQuicker,
        Mistakes,
    }

    /// <summary>One item of news: a title in a sentence, and a line under it with the figures.</summary>
    public sealed record NewsItem(NewsKind Kind, string Title, string Detail)
    {
        /// <summary>The short badge beside it: REC, PB, an up arrow, or "!".</summary>
        public string Badge => BadgeOf(Kind);
    }

    /// <summary>The badge for a kind of news.</summary>
    public static string BadgeOf(NewsKind kind) => kind switch
    {
        NewsKind.Record => "REC",
        NewsKind.PersonalBest => "PB",
        NewsKind.GotQuicker => "↑",
        _ => "!",
    };

    /// <summary>
    /// What the Overview tab says: the headline about the club lately, the
    /// last race night (null when nothing has been raced), and the news from
    /// that night.
    /// </summary>
    public sealed record ClubNews(string Headline, DateTime? NightLocal, IReadOnlyList<NewsItem> Items);

    /// <summary>The most news items the feed shows. More than five and it stops being the headlines.</summary>
    public const int MostNews = 5;

    /// <summary>A typical best lap that moved less than this in a month has not really moved.</summary>
    public const long SteadyMonthMs = 20;

    /// <summary>The fewest drivers in each month before the club's typical lap means anything.</summary>
    public const int FewestForAMonth = 3;

    /// <summary>A mistake story needs at least this much time lost in the mistakes - under a second is a wobble.</summary>
    public const long MistakeNewsMs = 1000;

    /// <summary>The most car lengths worth saying: past this a gap is better said in seconds.</summary>
    public const double MostLengthsToSay = 40;

    /// <summary>
    /// Every lap that could stand as a record, quickest first: a lap from a
    /// car that may hold one (AchievementEvaluator.CanHoldTrackRecord - no
    /// ghosts, rentals or excluded guests), so the Overview's records agree
    /// with the badge and the live record. Demo and imported sessions are
    /// the caller's to leave out. A tie goes to whoever set it first, as the
    /// badge has it - history is listed newest first, so without that the
    /// later lap that only equalled it was named the holder.
    /// </summary>
    public static IEnumerable<(SessionRecord Session, SessionResultEntry Result)> RecordLaps(
        IEnumerable<SessionRecord> sessions, ProfileStore? profiles) =>
        sessions
            .SelectMany(s => s.Results.Select(r => (Session: s, Result: r)))
            .Where(x => x.Result.FastLapMs > 0 && AchievementEvaluator.CanHoldTrackRecord(x.Result.Transponder, profiles))
            .OrderBy(x => x.Result.FastLapMs)
            .ThenBy(x => x.Session.StartedAtUtc);

    /// <summary>The name a result goes by: the driver's, or the transponder when nobody was named.</summary>
    public static string WhoIs(SessionResultEntry result) =>
        string.IsNullOrWhiteSpace(result.DriverName) ? result.Transponder : result.DriverName.Trim();

    /// <summary>
    /// The sessions of the last race night, oldest first: every scored
    /// session on the same 6am-to-6am night as the last one. Empty when
    /// nothing has been raced.
    ///
    /// WHY (23 Nov 2026, bug hunt 24): this was every session within twelve
    /// hours of the last one. An all-day meeting (practice from 9:00, the
    /// last main at 21:30) lost its morning, which then counted as "before":
    /// a first-time visitor was told they took time off their best, and a
    /// layout's first day reported a record that "has stood since" the
    /// morning. A race night is 6am to 6am, as everywhere else in the app.
    /// </summary>
    public static List<SessionRecord> LastNight(IEnumerable<SessionRecord> sessions)
    {
        var scored = Scored(sessions);

        if (scored.Count == 0)
        {
            return new List<SessionRecord>();
        }

        var night = NightOf(scored.Max(s => s.StartedAtUtc));

        return scored
            .Where(s => NightOf(s.StartedAtUtc) == night)
            .OrderBy(s => s.StartedAtUtc)
            .ToList();
    }

    /// <summary>The race night (6am to 6am, local) a session that started at <paramref name="startedUtc"/> belongs to.</summary>
    private static DateTime NightOf(DateTime startedUtc)
    {
        var local = DateTime.SpecifyKind(startedUtc, DateTimeKind.Utc).ToLocalTime();

        // A session saved with no time at all is DateTime.MinValue, and six
        // hours before that is no date - it goes under its own day, as
        // RaceHistoryControl.NightOf has it.
        return local < DateTime.MinValue.AddDays(1) ? local.Date : TonightAtTheTrack.NightOf(local);
    }

    /// <summary>
    /// The Overview tab's headline and news, from every saved session.
    /// Demo and imported sessions are left out, as they are of every
    /// record: their laps were never run here.
    /// </summary>
    public static ClubNews Overview(IEnumerable<SessionRecord> sessions, ProfileStore? profiles, TrackLayoutStore? layouts)
    {
        var scored = Scored(sessions);
        var night = LastNight(scored);

        if (night.Count == 0)
        {
            return new ClubNews("Nothing raced yet. Run a race and the news from the night starts here.", null, Array.Empty<NewsItem>());
        }

        var nightStart = night[0].StartedAtUtc;
        var before = scored.Where(s => s.StartedAtUtc < nightStart).ToList();

        var records = RecordNews(scored, night, before, profiles, layouts);
        var bests = PersonalBestNews(night, before, layouts, out var pbCount);
        var quicker = QuickerNews(night);
        var mistakes = MistakeNews(night, layouts);

        // The first of each kind, then the seconds while there is room - so
        // a night with two records and three PBs still says who got quicker.
        var picked = new List<NewsItem>();

        foreach (var item in new[] { records.FirstOrDefault(), bests.FirstOrDefault(), quicker, mistakes }
                     .Concat(records.Skip(1))
                     .Concat(bests.Skip(1)))
        {
            if (item is not null && picked.Count < MostNews)
            {
                picked.Add(item);
            }
        }

        var items = picked.OrderBy(i => i.Kind).ToList();
        var newRecords = records.Count(r => r.Title.StartsWith("New ", StringComparison.Ordinal));

        var headline = MonthHeadline(scored)
            ?? NightHeadline(night, pbCount, newRecords);

        return new ClubNews(headline, night[0].StartedAtUtc.ToLocalTime(), items);
    }

    private static List<SessionRecord> Scored(IEnumerable<SessionRecord> sessions) =>
        sessions.Where(s => !s.IsNotScored() && s.Results.Count > 0).ToList();

    private static bool SameLayout(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The layouts raced on the night, the busiest first.</summary>
    private static List<string> NightLayouts(IReadOnlyList<SessionRecord> night) =>
        night
            .Where(s => !string.IsNullOrWhiteSpace(s.TrackLayoutName))
            .GroupBy(s => s.TrackLayoutName.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => g.Key)
            .ToList();

    /// <summary>
    /// A record on each layout raced on the night: a new one, or how long
    /// the old one has stood and who came closest to it. A layout's first
    /// night is no story - its first lap is a record by default.
    /// </summary>
    private static List<NewsItem> RecordNews(
        IReadOnlyList<SessionRecord> scored, IReadOnlyList<SessionRecord> night, IReadOnlyList<SessionRecord> before,
        ProfileStore? profiles, TrackLayoutStore? layouts)
    {
        var news = new List<NewsItem>();
        var nightEnd = night[^1].StartedAtUtc;

        foreach (var layout in NightLayouts(night))
        {
            var oldRecord = RecordLaps(before.Where(s => SameLayout(s.TrackLayoutName, layout)), profiles).FirstOrDefault();
            var tonight = RecordLaps(night.Where(s => SameLayout(s.TrackLayoutName, layout)), profiles).FirstOrDefault();

            if (oldRecord.Result is null || tonight.Result is null)
            {
                continue;
            }

            var trackCars = TrackCars(layouts, layout);

            if (tonight.Result.FastLapMs < oldRecord.Result.FastLapMs)
            {
                var gap = oldRecord.Result.FastLapMs - tonight.Result.FastLapMs;

                // Beating your own record is "their own", not their name twice.
                var whose = string.Equals(WhoIs(oldRecord.Result), WhoIs(tonight.Result), StringComparison.OrdinalIgnoreCase)
                    ? "their own"
                    : $"{WhoIs(oldRecord.Result)}'s";

                news.Add(new NewsItem(
                    NewsKind.Record,
                    $"New {layout} record for {WhoIs(tonight.Result)}",
                    $"{PaceAnalysis.Time(tonight.Result.FastLapMs)} in {SessionName(tonight.Session)}, "
                    + $"{Gap(gap)}{InLengths(gap, tonight.Result.FastLapMs, trackCars)} under {whose} "
                    + $"{PaceAnalysis.Time(oldRecord.Result.FastLapMs)}."));

                continue;
            }

            var off = tonight.Result.FastLapMs - oldRecord.Result.FastLapMs;
            var stood = HowLong(oldRecord.Session.StartedAtUtc, nightEnd);

            news.Add(new NewsItem(
                NewsKind.Record,
                $"{WhoIs(oldRecord.Result)}'s {layout} record has stood {stood}",
                $"{PaceAnalysis.Time(oldRecord.Result.FastLapMs)} on {Day(oldRecord.Session.StartedAtUtc)}. "
                + (off == 0
                    ? $"{WhoIs(tonight.Result)} matched it last night."
                    : $"Closest last night: {WhoIs(tonight.Result)}, {PaceAnalysis.Time(tonight.Result.FastLapMs)}, "
                      + $"{Gap(off)}{InLengths(off, oldRecord.Result.FastLapMs, trackCars)} off.")));
        }

        // New records first: they are the bigger news.
        return news
            .OrderByDescending(n => n.Title.StartsWith("New ", StringComparison.Ordinal))
            .Take(2)
            .ToList();
    }

    /// <summary>
    /// PBs taken on the night: a driver's best lap on a layout that night,
    /// quicker than any lap they had run there before. The biggest gain
    /// first. A first visit sets no PB worth telling - there was nothing to
    /// beat. Counted by name, as the Personal Bests tab counts them.
    /// </summary>
    private static List<NewsItem> PersonalBestNews(
        IReadOnlyList<SessionRecord> night, IReadOnlyList<SessionRecord> before, TrackLayoutStore? layouts, out int count)
    {
        var found = new List<(string Who, string Layout, long Gain, long Lap, long Old, SessionRecord Session)>();

        foreach (var layout in NightLayouts(night))
        {
            var tonight = BestsByDriver(night.Where(s => SameLayout(s.TrackLayoutName, layout)));
            var earlier = BestsByDriver(before.Where(s => SameLayout(s.TrackLayoutName, layout)));

            foreach (var (key, now) in tonight)
            {
                if (earlier.TryGetValue(key, out var then) && now.Lap < then.Lap)
                {
                    found.Add((now.Who, layout, then.Lap - now.Lap, now.Lap, then.Lap, now.Session));
                }
            }
        }

        count = found.Count;

        var top = found.OrderByDescending(f => f.Gain).ThenBy(f => f.Who, StringComparer.CurrentCultureIgnoreCase).Take(2).ToList();
        var news = new List<NewsItem>();

        for (var i = 0; i < top.Count; i++)
        {
            var pb = top[i];
            var trackCars = TrackCars(layouts, pb.Layout);
            var more = i == top.Count - 1 && count > top.Count
                ? $" {count - top.Count} more PB{(count - top.Count == 1 ? "" : "s")} on the night."
                : string.Empty;

            news.Add(new NewsItem(
                NewsKind.PersonalBest,
                $"{pb.Who} took {Gap(pb.Gain)} off their {pb.Layout} best",
                $"{PaceAnalysis.Time(pb.Lap)} in {SessionName(pb.Session)}, down from {PaceAnalysis.Time(pb.Old)}"
                + $"{(SayLengths(pb.Gain, pb.Lap, trackCars) is { } lengths ? $" ({lengths})" : string.Empty)}.{more}"));
        }

        return news;
    }

    /// <summary>Each driver's best lap in these sessions, by their name in capitals. Ghosts and nameless cars left out.</summary>
    private static Dictionary<string, (string Who, long Lap, SessionRecord Session)> BestsByDriver(IEnumerable<SessionRecord> sessions)
    {
        var bests = new Dictionary<string, (string Who, long Lap, SessionRecord Session)>();

        foreach (var session in sessions)
        {
            foreach (var result in session.Results)
            {
                if (result.FastLapMs <= 0 || string.IsNullOrWhiteSpace(result.DriverName) || GhostService.IsGhost(result.Transponder))
                {
                    continue;
                }

                var key = result.DriverName.Trim().ToUpperInvariant();

                if (!bests.TryGetValue(key, out var best) || result.FastLapMs < best.Lap)
                {
                    bests[key] = (result.DriverName.Trim(), result.FastLapMs, session);
                }
            }
        }

        return bests;
    }

    /// <summary>
    /// The driver who got quickest through a race on the night: Pace's own
    /// "got quicker as it went on" (the last third quicker than the first
    /// by as much as PaceAnalysis asks), the biggest such gain.
    /// </summary>
    private static NewsItem? QuickerNews(IReadOnlyList<SessionRecord> night)
    {
        var best = night
            .Where(s => s.SessionType == RaceSessionType.Race)
            .SelectMany(s => s.Results
                .Where(r => !GhostService.IsGhost(r.Transponder))
                .Select(r => (Session: s, Result: r, Row: PaceAnalysis.For(r))))
            .Where(x => !x.Row.Thin && x.Row.FadeMs <= -Math.Max(150, x.Row.BestLapMs / 20))
            .OrderBy(x => x.Row.FadeMs)
            .FirstOrDefault();

        if (best.Result is null)
        {
            return null;
        }

        return new NewsItem(
            NewsKind.GotQuicker,
            $"{WhoIs(best.Result)} got quicker through {SessionName(best.Session)}",
            $"The last third was {-best.Row.FadeMs / 1000.0:0.00}s a lap faster than the first. "
            + $"Best lap {PaceAnalysis.Time(best.Row.BestLapMs)}.");
    }

    /// <summary>
    /// The driver whose mistakes cost the most in a race on the night -
    /// when the mistakes are the story of their race (RaceCoach.Headline's
    /// rule) and cost at least <see cref="MistakeNewsMs"/>.
    /// </summary>
    private static NewsItem? MistakeNews(IReadOnlyList<SessionRecord> night, TrackLayoutStore? layouts)
    {
        var worst = night
            .Where(s => s.SessionType == RaceSessionType.Race)
            .SelectMany(s => s.Results
                .Where(r => !GhostService.IsGhost(r.Transponder))
                .Select(r =>
                {
                    var laps = Laps(r);
                    return (Session: s, Result: r, Laps: laps, Row: PaceAnalysis.For(r), Count: MistakeLaps(laps).Count, Cost: MistakeCostMs(laps));
                }))
            .Where(x => !x.Row.Thin && x.Count > 0 && x.Cost >= MistakeNewsMs
                        && x.Row.PotentialGainMs > 0 && x.Cost >= x.Row.PotentialGainMs * MistakesAreTheStory)
            .OrderByDescending(x => x.Cost)
            .FirstOrDefault();

        if (worst.Result is null)
        {
            return null;
        }

        // Car lengths only while they are a distance you can picture: past
        // forty, "169 car lengths" is just a big number, and the seconds say
        // it better.
        var trackCars = TrackCars(layouts, worst.Session.TrackLayoutName);
        var lengths = CarLengths(worst.Cost, worst.Row.BestLapMs, trackCars) is double cars && cars <= MostLengthsToSay
            ? Lengths(cars)
            : null;

        return new NewsItem(
            NewsKind.Mistakes,
            $"{WhoIs(worst.Result)}: quick laps, {Count(worst.Count, "mistake")}",
            $"Best lap {PaceAnalysis.Time(worst.Row.BestLapMs)} in {SessionName(worst.Session)}. "
            + $"The {(worst.Count == 1 ? "mistake" : "mistakes")} cost {worst.Cost / 1000.0:0.0}s"
            + $"{(lengths is null ? string.Empty : $", about {lengths}")}.");
    }

    /// <summary>
    /// How the club's month went, on its busiest layout: the typical best
    /// lap (the middle driver's best) this month against the month before.
    /// Null when either month had too few drivers to say.
    /// </summary>
    private static string? MonthHeadline(IReadOnlyList<SessionRecord> scored)
    {
        var last = scored.Max(s => s.StartedAtUtc);
        var monthAgo = last.AddDays(-30);
        var twoMonthsAgo = last.AddDays(-60);

        var layout = scored
            .Where(s => s.StartedAtUtc > monthAgo && !string.IsNullOrWhiteSpace(s.TrackLayoutName))
            .GroupBy(s => s.TrackLayoutName.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

        if (layout is null)
        {
            return null;
        }

        var here = scored.Where(s => SameLayout(s.TrackLayoutName, layout)).ToList();
        var now = BestsByDriver(here.Where(s => s.StartedAtUtc > monthAgo)).Values.Select(v => v.Lap).ToList();
        var then = BestsByDriver(here.Where(s => s.StartedAtUtc > twoMonthsAgo && s.StartedAtUtc <= monthAgo)).Values.Select(v => v.Lap).ToList();

        if (now.Count < FewestForAMonth || then.Count < FewestForAMonth)
        {
            return null;
        }

        var change = Middle(now) - Middle(then);

        if (change <= -SteadyMonthMs)
        {
            return $"The club got quicker this month: the typical best lap on {layout} is down {Gap(-change)} on the month before.";
        }

        if (change >= SteadyMonthMs)
        {
            return $"A slower month on {layout}: the typical best lap is up {Gap(change)} on the month before.";
        }

        return $"A steady month on {layout}: the typical best lap is within a few hundredths of the month before.";
    }

    /// <summary>The headline when there is not a month to compare: what the last night came to.</summary>
    private static string NightHeadline(IReadOnlyList<SessionRecord> night, int pbs, int newRecords)
    {
        var drivers = night
            .SelectMany(s => s.Results)
            .Where(r => !GhostService.IsGhost(r.Transponder))
            .Select(r => WhoIs(r).ToUpperInvariant())
            .Distinct()
            .Count();

        var what = new List<string>();

        if (newRecords > 0)
        {
            what.Add(newRecords == 1 ? "a new track record" : $"{newRecords} new track records");
        }

        if (pbs > 0)
        {
            what.Add(Count(pbs, "PB"));
        }

        var sessions = Count(night.Count, "session");

        return what.Count == 0
            ? $"{Day(night[0].StartedAtUtc)}: {Count(drivers, "driver")} ran {sessions}."
            : $"{Day(night[0].StartedAtUtc)}: {Count(drivers, "driver")} ran {sessions}, with {string.Join(" and ", what)}.";
    }

    /// <summary>The middle value; the lower middle of an even count, a lap somebody actually ran.</summary>
    private static long Middle(List<long> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[(sorted.Count - 1) / 2];
    }

    /// <summary>A session as it is named in a sentence: its label, or "a race" / "practice".</summary>
    public static string SessionName(SessionRecord session) =>
        !string.IsNullOrWhiteSpace(session.Label) ? session.Label.Trim()
        : session.SessionType == RaceSessionType.Race ? "a race"
        : "practice";

    /// <summary>A gap in seconds: "0.075s".</summary>
    public static string Gap(long ms) => $"{ms / 1000.0:0.000}s";

    /// <summary>", 7 car lengths" after a gap, or nothing when the layout has no length.</summary>
    private static string InLengths(long gapMs, long lapMs, double? trackCars) =>
        SayLengths(gapMs, lapMs, trackCars) is { } lengths ? $" ({lengths})" : string.Empty;

    /// <summary>A day as the club says it: "Fri 7 Nov".</summary>
    public static string Day(DateTime utc) => utc.ToLocalTime().ToString("ddd d MMM");

    /// <summary>How long something has stood: "5 days", "4 weeks", "3 months".</summary>
    public static string HowLong(DateTime fromUtc, DateTime toUtc)
    {
        var days = Math.Max(0, (int)(toUtc - fromUtc).TotalDays);

        return days < 1 ? "since last night"
            : days < 14 ? Count(days, "day")
            : days < 61 ? Count(days / 7, "week")
            : days < 730 ? Count(days / 30, "month")
            : Count(days / 365, "year");
    }

    /// <summary>"one mistake", "3 mistakes": the small numbers in words, as they are said.</summary>
    public static string Count(int n, string thing)
    {
        string[] words = { "no", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten" };
        var number = n >= 0 && n < words.Length ? words[n] : n.ToString("N0");

        return $"{number} {thing}{(n == 1 ? "" : "s")}";
    }
}
