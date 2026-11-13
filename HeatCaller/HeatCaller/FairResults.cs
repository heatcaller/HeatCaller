using System.Globalization;

namespace HeatCaller;

/// <summary>How the first qualifying round is seeded. See RaceEvent.FirstRoundSeeding.</summary>
public enum FirstRoundSeeding
{
    /// <summary>Recent pace: similar pace together, fastest in the last heat.</summary>
    Pace,

    /// <summary>Alphabetically, dealt into the heats in turn - every build before 11 Nov 2026.</summary>
    Alphabetical,

    /// <summary>At random, the same random for the same race day so a rebuild gives the same heats.</summary>
    Random,

    /// <summary>
    /// Heads-up / HRQF (11 Nov 2026, Phase 5.2): seeded from tonight's
    /// practice, every other seed per heat, round 2 reversed, round 3
    /// reseeded on points. Not only the first round - see HeadsUpQualifying.
    /// At the end so a saved race day's choice keeps its number.
    /// </summary>
    HeadsUp,

    /// <summary>
    /// By race number (12 Nov 2026, setup plan step 8) - the BRCA norm:
    /// dealt into the heats in race-number order, as alphabetical is dealt in
    /// name order. At the end, like HeadsUp, so saved choices keep their number.
    /// </summary>
    RaceNumber,
}

/// <summary>
/// Seeding the first round (11 Nov 2026, race-night build plan, Phase 3).
///
/// WHY: the first round was a lottery - dealt alphabetically - so a rookie
/// could share a heat with the club's three fastest drivers and be lapped
/// four times, and the fast drivers had nobody to race. Grouping by recent
/// pace gives close racing from the first heat. Later rounds are still dealt
/// in turn, from the order the first round was seeded in, as they always
/// were.
/// </summary>
public static class FirstRound
{
    /// <summary>
    /// The class's drivers in the order the build deals them, and - for the
    /// first round seeded by pace - the heats themselves. Null heats means
    /// "deal <see cref="Order"/> in turn as always".
    /// </summary>
    public sealed record Seeding(List<string> Order, List<List<string>>? FirstRoundHeats);

    /// <param name="roster">The class's drivers, alphabetically (RosterFor).</param>
    /// <param name="mode">The race day's choice.</param>
    /// <param name="pace">Each driver's recent best lap (HeatSeeding.BestLaps).</param>
    /// <param name="heatSizes">How many each first-round heat holds, in running order.</param>
    /// <param name="randomSeed">Fixes the random order for a race day - its id's hash.</param>
    /// <param name="raceNumberOf">Each driver's race number (DriverProfile.RaceNumber), for <see cref="FirstRoundSeeding.RaceNumber"/>.</param>
    public static Seeding For(IReadOnlyList<string> roster, FirstRoundSeeding mode, IReadOnlyDictionary<string, long> pace,
        IReadOnlyList<int> heatSizes, int randomSeed, Func<string, string?>? raceNumberOf = null)
    {
        switch (mode)
        {
            case FirstRoundSeeding.RaceNumber:
                return new Seeding(ByRaceNumber(roster, raceNumberOf ?? (_ => null)), null);

            case FirstRoundSeeding.Random:
                var random = new Random(randomSeed);
                return new Seeding(roster.OrderBy(_ => random.Next()).ToList(), null);

            case FirstRoundSeeding.Pace when roster.Any(d => pace.TryGetValue(d, out var ms) && ms > 0):
                // Slowest first in the program, the fastest last - the way
                // the mains run - and drivers with no lap yet (a rookie, a
                // new class) with the slowest.
                var heats = HeatSeeding.Group(heatSizes, roster, pace);
                return new Seeding(HeatSeeding.ByPace(roster, pace), heats);

            default:
                return new Seeding(roster.ToList(), null);
        }
    }

    /// <summary>
    /// The roster in race-number order (12 Nov 2026, setup plan step 8):
    /// numbers that are numbers first, numerically (2 before 10); then any
    /// other number as text ("7a", "X1"); then drivers with no number, in the
    /// roster's own order - alphabetical, as RosterFor gives it. A tie keeps
    /// the roster's order too, so two drivers sharing a number stay put.
    /// </summary>
    public static List<string> ByRaceNumber(IReadOnlyList<string> roster, Func<string, string?> raceNumberOf) =>
        roster
            .Select((driver, index) => (driver, index, Number: (raceNumberOf(driver) ?? string.Empty).Trim()))
            .OrderBy(x => x.Number.Length == 0 ? 2 : int.TryParse(x.Number, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ? 0 : 1)
            .ThenBy(x => int.TryParse(x.Number, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0)
            .ThenBy(x => x.Number, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.index)
            .Select(x => x.driver)
            .ToList();

    /// <summary>A seed that is the same every run for the same text (FNV-1a), unlike string.GetHashCode.</summary>
    public static int StableSeed(string text)
    {
        unchecked
        {
            var hash = (int)2166136261;

            foreach (var c in text)
            {
                hash = (hash ^ c) * 16777619;
            }

            return hash;
        }
    }

    /// <summary>How each heat is filled for <paramref name="dealt"/>: the sizes the round-robin deal gives.</summary>
    public static List<int> SizesOf(IEnumerable<IReadOnlyCollection<string>> dealt) => dealt.Select(h => h.Count).ToList();

    /// <summary>What the Setup picker shows for each choice.</summary>
    public static string Words(FirstRoundSeeding mode) => mode switch
    {
        FirstRoundSeeding.Alphabetical => "Alphabetical",
        FirstRoundSeeding.Random => "Random",
        FirstRoundSeeding.HeadsUp => "Heads-up (HRQF, from practice)",
        FirstRoundSeeding.RaceNumber => "By race number",
        _ => "By recent pace",
    };
}

/// <summary>
/// The protest window (11 Nov 2026, race-night build plan, Phase 3).
///
/// WHY: a lap-count dispute raised after the finals are built changes who is
/// in which main, and unpicking that at 10pm is the worst argument of the
/// night. A few minutes after a class's last qualifier its results are
/// provisional: the desk sees when they become final, a driver can still
/// query a result from their phone, and building the finals before then asks
/// first.
///
/// 12 Nov 2026, setup plan step 8: every qualifying round and every main
/// opens a window of its own too (<see cref="ForRound"/>), for the phones,
/// the announcer, the Big Screen and Driver Requests. The finals still wait
/// only for the last round's (<see cref="FinalAtUtc"/>, <see cref="ClosesAtUtc"/>).
/// </summary>
public static class ProtestWindow
{
    /// <summary>The window when the race day hasn't chosen one.</summary>
    public const int DefaultMinutes = 5;

    /// <summary>
    /// When the class's qualifying results become final: its last recorded
    /// qualifier's finish plus <paramref name="minutes"/>. Null with no
    /// window, or no qualifier recorded.
    /// </summary>
    public static DateTime? FinalAtUtc(IEnumerable<RoundDefinition> classDayRounds, Func<string, SessionRecord?> sessionOf, int minutes)
    {
        if (minutes <= 0)
        {
            return null;
        }

        var lastEnd = classDayRounds
            .Where(r => r.Kind == RoundKind.Qualifier && r.IsRecorded)
            .Select(r => sessionOf(r.LinkedSessionId!))
            .Where(s => s is not null && !s.IsNotScored())
            .Select(s => (DateTime?)s!.EndedAtUtc)
            .Max();

        return lastEnd?.AddMinutes(minutes);
    }

    /// <summary>Whether the results are still provisional at <paramref name="nowUtc"/>.</summary>
    public static bool IsProvisional(DateTime? finalAtUtc, DateTime nowUtc) => finalAtUtc is { } at && nowUtc < at;

    /// <summary>"Results are provisional until 20:42 (the protest window)." - local time, as AppTime shows it.</summary>
    public static string Words(DateTime finalAtUtc) =>
        $"Results are provisional until {AppTime.TimeOfDay(finalAtUtc.ToLocalTime())} (the protest window).";

    /// <summary>The longest window the Club Website page offers.</summary>
    public const int MostMinutes = 60;

    /// <summary>
    /// The window in force (11 Nov 2026: one setting, on the Club Website
    /// page beside "Let drivers query a result"). <paramref name="clubMinutes"/>
    /// is AppSettings.ProtestMinutes - null until somebody sets it there, and
    /// then the race day's own old value (RaceEvent.ProtestMinutes, the
    /// setting's home before) still counts, so a club that had changed it on
    /// Setup keeps what it chose. Neither: the default.
    /// </summary>
    public static int Minutes(int? clubMinutes, int? oldDayMinutes) =>
        Math.Clamp(clubMinutes ?? oldDayMinutes ?? DefaultMinutes, 0, MostMinutes);

    /// <summary>
    /// The value to carry over once, at startup, from the race days that
    /// already have one (11 Nov 2026): null when the club setting is set
    /// already or no race day ever changed the window from the default -
    /// else the newest race day's choice (the open one first). After that
    /// AppSettings.ProtestMinutes is the only place it is read from.
    /// </summary>
    public static int? CarryOver(int? clubMinutes, RaceEvent? openDay, IEnumerable<RaceEvent> days)
    {
        if (clubMinutes is not null)
        {
            return null;
        }

        var from = openDay is { IsLegacy: false }
            ? openDay
            : days.Where(d => !d.IsLegacy).OrderByDescending(d => d.StartedUtc).FirstOrDefault();

        return from is { } day && day.ProtestMinutes != DefaultMinutes
            ? Math.Clamp(day.ProtestMinutes, 0, MostMinutes)
            : null;
    }

    /// <summary>
    /// When the class's window closes, counted only once EVERY qualifier of
    /// the class today has been recorded (11 Nov 2026) - before that there
    /// is no window yet, and nothing on the phones changes. Null with no
    /// window, no qualifiers, or one still to run. <paramref name="justFiled"/>
    /// is a result filed a moment ago whose round is not linked to it yet -
    /// it counts as that round's result.
    /// </summary>
    public static DateTime? ClosesAtUtc(IEnumerable<RoundDefinition> classDayRounds, Func<string, SessionRecord?> sessionOf, int minutes,
        SessionRecord? justFiled = null)
    {
        if (minutes <= 0)
        {
            return null;
        }

        var qualifiers = classDayRounds.Where(r => r.Kind == RoundKind.Qualifier).ToList();

        if (qualifiers.Count == 0)
        {
            return null;
        }

        DateTime? lastEnd = null;

        foreach (var round in qualifiers)
        {
            var result = justFiled is not null && string.Equals(justFiled.RoundId, round.Id, StringComparison.Ordinal)
                ? justFiled
                : round.IsRecorded ? sessionOf(round.LinkedSessionId!) : null;

            if (result is null)
            {
                return null;
            }

            if (!result.IsNotScored() && (lastEnd is null || result.EndedAtUtc > lastEnd))
            {
                lastEnd = result.EndedAtUtc;
            }
        }

        return lastEnd?.AddMinutes(minutes);
    }

    /// <summary>
    /// A window opened by one round of a class (12 Nov 2026, setup plan
    /// step 8): what it is called ("Q2", "A-Main"), when it closes, and
    /// whether it was the class's last qualifying round.
    /// </summary>
    public sealed record RoundWindow(string What, DateTime ClosesAtUtc, RoundKind Kind, bool LastQualifying, string Key);

    /// <summary>
    /// The window <paramref name="round"/> belongs to (12 Nov 2026, setup
    /// plan step 8).
    ///
    /// WHY: the window used to open only after a class's LAST qualifier, so
    /// a lap-count dispute over Q1 had nowhere to go but the desk, and a
    /// main had no window at all - though a main's result is the one that
    /// goes on the trophy. Now each qualifying ROUND opens one once all its
    /// heats are in (a round is "Q2": every Q2 heat of the class, read from
    /// the labels the way Move Drivers Between Heats reads them), and each
    /// main opens its own as soon as it is in. A heat added by hand beside
    /// numbered rounds is a window of its own. Seeding the finals still
    /// waits only for the LAST round's window - see <see cref="FinalAtUtc"/>.
    ///
    /// Null with no window, a practice, a round not run yet, or a round
    /// whose other heats are still to run. <paramref name="justFiled"/> is a
    /// result filed a moment ago whose round is not linked yet.
    /// </summary>
    public static RoundWindow? ForRound(RoundDefinition round, IReadOnlyList<RoundDefinition> classDayRounds, Func<string, SessionRecord?> sessionOf,
        int minutes, SessionRecord? justFiled = null)
    {
        if (minutes <= 0 || round.Kind == RoundKind.Practice)
        {
            return null;
        }

        SessionRecord? ResultOf(RoundDefinition r) =>
            justFiled is not null && string.Equals(justFiled.RoundId, r.Id, StringComparison.Ordinal)
                ? justFiled
                : r.IsRecorded ? sessionOf(r.LinkedSessionId!) : null;

        if (round.Kind == RoundKind.Main)
        {
            return ResultOf(round) is { } run && !run.IsNotScored()
                ? new RoundWindow(MainName(round), run.EndedAtUtc.AddMinutes(minutes), RoundKind.Main, false, "#" + round.Id)
                : null;
        }

        var key = HeatBalanceForm.RoundKeyFor(round, classDayRounds);
        var group = key is null
            ? new List<RoundDefinition> { round }
            : classDayRounds.Where(r => r.Kind == RoundKind.Qualifier && HeatBalanceForm.RoundKeyFor(r, classDayRounds) == key).ToList();

        DateTime? lastEnd = null;

        foreach (var heat in group)
        {
            if (ResultOf(heat) is not { } result)
            {
                return null;
            }

            if (!result.IsNotScored() && (lastEnd is null || result.EndedAtUtc > lastEnd))
            {
                lastEnd = result.EndedAtUtc;
            }
        }

        if (lastEnd is null)
        {
            return null;
        }

        // The last round: every qualifier of the class is in once this one is.
        var last = classDayRounds.Where(r => r.Kind == RoundKind.Qualifier).All(r => ResultOf(r) is not null);
        var what = key is null
            ? (string.IsNullOrWhiteSpace(round.RoundLabel) ? "the extra heat" : round.RoundLabel.Trim())
            : key.Length == 0
                ? "qualifying"
                : (round.RoundLabel ?? string.Empty).TrimStart().StartsWith("Race", StringComparison.OrdinalIgnoreCase) ? $"Race {key[1..]}" : key;

        return new RoundWindow(what, lastEnd.Value.AddMinutes(minutes), RoundKind.Qualifier, last, key is null ? "#" + round.Id : "Q" + key);
    }

    /// <summary>
    /// The class's LATEST window - the round whose results came in last -
    /// for the results that show "Provisional until 20:42" and the Big
    /// Screen (12 Nov 2026, setup plan step 8). <paramref name="kind"/>
    /// keeps it to qualifiers (the qualifying card) or mains; null for both.
    /// </summary>
    public static RoundWindow? Latest(IReadOnlyList<RoundDefinition> classDayRounds, Func<string, SessionRecord?> sessionOf, int minutes,
        RoundKind? kind = null)
    {
        if (minutes <= 0)
        {
            return null;
        }

        return classDayRounds
            .Where(r => r.IsRecorded && r.Kind != RoundKind.Practice && (kind is null || r.Kind == kind))
            .Select(r => ForRound(r, classDayRounds, sessionOf, minutes))
            .OfType<RoundWindow>()
            .MaxBy(w => w.ClosesAtUtc);
    }

    /// <summary>
    /// Whether a query asked at <paramref name="askedUtc"/> came in while
    /// the race's window was open (12 Nov 2026, setup plan step 8) - those
    /// go to the top of Driver Requests, since the answer can still change
    /// a result before it is final.
    /// </summary>
    public static bool AskedInside(DateTime askedUtc, DateTime? raceEndedUtc, DateTime? closesAtUtc) =>
        closesAtUtc is { } closes && askedUtc < closes && (raceEndedUtc is not { } ended || askedUtc >= ended);

    /// <summary>
    /// Waiting requests in the order the desk sees them: queries asked
    /// inside a protest window first, then everything else, each as it came.
    /// </summary>
    public static List<T> DeskOrder<T>(IEnumerable<T> waiting, Func<T, bool> inWindow) =>
        waiting.Select((r, i) => (r, i)).OrderBy(x => inWindow(x.r) ? 0 : 1).ThenBy(x => x.i).Select(x => x.r).ToList();

    /// <summary>"A-Main", "B-Main Leg 2"... - the label, or the tier's name for an unlabelled main.</summary>
    private static string MainName(RoundDefinition main) =>
        !string.IsNullOrWhiteSpace(main.RoundLabel) ? main.RoundLabel.Trim()
        : main.TierLetter.Length > 0 ? $"{main.TierLetter}-Main"
        : "the main";

    /// <summary>
    /// The announcer's line once a round's window opens (12 Nov 2026, setup
    /// plan step 8), saying which round: "That's Q1 done for Stock. Results
    /// are provisional for five minutes - any queries to the race desk,
    /// please." The last qualifying round says qualifying is done, as
    /// before; a main says "That's the A-Main done for Stock".
    /// </summary>
    public static string Announcement(string className, int minutes, RoundWindow window)
    {
        if (window.Kind == RoundKind.Qualifier && window.LastQualifying && window.What == "qualifying")
        {
            return Announcement(className, minutes);
        }

        var name = string.IsNullOrWhiteSpace(className) ? string.Empty : $" for {className.Trim()}";
        var span = minutes == 1 ? "one minute" : $"{SpokenNumber(minutes)} minutes";
        var what = window.Kind == RoundKind.Main ? $"the {window.What}" : window.What;
        var andQualifying = window.Kind == RoundKind.Qualifier && window.LastQualifying ? ", and that's qualifying" : string.Empty;

        return $"That's {what} done{name}{andQualifying}. Results are provisional for {span} - any queries to the race desk, please.";
    }

    /// <summary>"PROVISIONAL UNTIL 20:42" for the Big Screen's results - local time, as AppTime shows it.</summary>
    public static string BoardWords(DateTime closesAtUtc) =>
        $"PROVISIONAL UNTIL {AppTime.TimeOfDay(closesAtUtc.ToLocalTime())}";

    /// <summary>
    /// Whether a driver may still query a race on their phone: always, unless
    /// the race is a qualifier of a class whose window has closed.
    /// <paramref name="closesAtUtc"/> null means no window - as before.
    /// </summary>
    public static bool QueryOpen(DateTime? closesAtUtc, DateTime nowUtc) => closesAtUtc is not { } at || nowUtc < at;

    /// <summary>"Provisional until 20:42" for the phones - the time in the laptop's local time.</summary>
    public static string PhoneWords(DateTime closesAtUtc) =>
        $"Provisional until {AppTime.TimeOfDay(closesAtUtc.ToLocalTime())}";

    /// <summary>
    /// The announcer's line once a class's last qualifier is in: "That's
    /// qualifying done for Stock. Results are provisional for five minutes -
    /// any queries to the race desk, please." Queries off on the phones, the
    /// desk is still where they go.
    /// </summary>
    public static string Announcement(string className, int minutes)
    {
        var name = string.IsNullOrWhiteSpace(className) ? string.Empty : $" for {className.Trim()}";
        var span = minutes == 1 ? "one minute" : $"{SpokenNumber(minutes)} minutes";

        return $"That's qualifying done{name}. Results are provisional for {span} - any queries to the race desk, please.";
    }

    private static string SpokenNumber(int n) => n switch
    {
        2 => "two",
        3 => "three",
        4 => "four",
        5 => "five",
        6 => "six",
        7 => "seven",
        8 => "eight",
        9 => "nine",
        10 => "ten",
        15 => "fifteen",
        20 => "twenty",
        30 => "thirty",
        _ => n.ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>
/// The mains, shown before they are built (11 Nov 2026, race-night build
/// plan, Phase 3): who goes in which main and why - the qualifying result
/// that put them there - and which places bump up. LiveTime's Seeding
/// Preview, without its fifteen settings.
/// </summary>
public static class MainsPreview
{
    /// <summary>
    /// One line per driver under each main: "  1. Sam Price - 14 laps, 5:02.3 (Q2 Heat 1)".
    /// </summary>
    /// <param name="plan">The tiers, fastest first.</param>
    /// <param name="entries">The qualifying entries, by transponder.</param>
    /// <param name="method">How the class qualifies.</param>
    /// <param name="consecutiveLaps">For best-consecutive qualifying, how many laps.</param>
    /// <param name="bumpUp">How many from each main move up.</param>
    public static string Text(MainsBuilder.SeedPlan plan, IReadOnlyDictionary<string, QualifyingCalculator.Entry> entries,
        QualifyingMethod method, int consecutiveLaps, int bumpUp)
    {
        var lines = new List<string>();
        var place = 1;

        for (var tier = 0; tier < plan.TierCount; tier++)
        {
            var letter = MainsBuilder.SeedPlan.LetterFor(tier);
            var bump = bumpUp > 0 && tier < plan.TierCount - 1
                ? $" (plus the top {bumpUp} of the {MainsBuilder.SeedPlan.LetterFor(tier + 1)}-Main)"
                : string.Empty;
            lines.Add($"{letter}-Main{bump}:");

            foreach (var transponder in plan.Tiers[tier])
            {
                var why = entries.TryGetValue(transponder, out var entry) ? $" - {ResultWords(entry, method, consecutiveLaps)}" : string.Empty;
                var name = entry is null || string.IsNullOrWhiteSpace(entry.DriverName) ? transponder : entry.DriverName;
                var bumping = bumpUp > 0 && tier > 0 && plan.Tiers[tier].IndexOf(transponder) < bumpUp ? "  [bump-up place]" : string.Empty;
                lines.Add($"   {place++}. {name}{why}{bumping}");
            }
        }

        return string.Join("\r\n", lines);
    }

    /// <summary>The counted result in words: "14 laps, 5:02.3 (Q2 Heat 1)", or "best 3 in a row 36.912 (Q1 Heat 2)".</summary>
    public static string ResultWords(QualifyingCalculator.Entry entry, QualifyingMethod method, int consecutiveLaps)
    {
        // Points qualifying (11 Nov 2026, Phase 5.1): "6 pts (best 2 of 3)" -
        // the figure the seeding was ordered on, not a heat's laps.
        if (method == QualifyingMethod.RoundPoints)
        {
            return entry.PointsWords();
        }

        var source = string.IsNullOrWhiteSpace(entry.SourceRoundLabel)
            ? string.Empty
            : entry.HeatsCounted > 1 ? $" (best {entry.HeatsCounted} heats)" : $" ({entry.SourceRoundLabel})";

        if (method == QualifyingMethod.BestConsecutiveLaps && entry.BestConsecutiveMs > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"best {consecutiveLaps} in a row {entry.BestConsecutiveMs / 1000.0:0.000}{source}");
        }

        var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, entry.BestElapsedMs));
        var time = string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}.{elapsed.Milliseconds / 100}");
        return $"{Plural.Of(entry.BestLaps, "lap")}, {time}{source}";
    }
}
