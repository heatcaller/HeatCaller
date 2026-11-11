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
    public static Seeding For(IReadOnlyList<string> roster, FirstRoundSeeding mode, IReadOnlyDictionary<string, long> pace,
        IReadOnlyList<int> heatSizes, int randomSeed)
    {
        switch (mode)
        {
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
