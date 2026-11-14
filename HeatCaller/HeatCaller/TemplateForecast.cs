namespace HeatCaller;

/// <summary>
/// What a night would come to, before it has been started or its heats built
/// (14 Nov 2026, Setup's Templates tab): about how many drivers, how many
/// heats and finals, and when it would finish against the out-by time.
/// </summary>
/// <param name="Drivers">Drivers expected, counted once per class they race in.</param>
/// <param name="Heats">Heats (or, on a club night, races) the night would run.</param>
/// <param name="Finals">Finals, on a race day. 0 on a club night.</param>
/// <param name="Racing">How long the racing takes, first green to last flag, breaks included.</param>
/// <param name="Finish">When it would finish (local time of day), or null with no first race time set.</param>
/// <param name="Spare">Out-by less finish: negative when it runs past. Null without both times.</param>
/// <param name="ClubNight">Whether it's a club night, for the wording.</param>
public sealed record NightForecast(int Drivers, int Heats, int Finals, TimeSpan Racing, TimeSpan? Finish, TimeSpan? Spare, bool ClubNight);

/// <summary>Where a forecast's turnout came from, and the drivers it expects in each class.</summary>
/// <param name="PerClass">Drivers expected in each class.</param>
/// <param name="Source">Words for where it came from: "said they're coming", "the last 3 Points rounds", "last time".</param>
public sealed record ExpectedTurnout(IReadOnlyDictionary<string, int> PerClass, string Source);

/// <summary>
/// The forecast on Setup's Templates tab (14 Nov 2026, direction A with B's
/// forecast). Pure.
///
/// NightPlan projects a night whose heats are built; a night not started yet
/// has none, so this sizes them the way the build would - a class's drivers
/// in heats of at most the heat size, that many heats each round, and on a
/// race day a final for every heat-sized group (MainsBuilder.PlanSeeding) -
/// and times them as NightPlan does: each race's length on track, the
/// default gap between races, and the night's breaks.
/// </summary>
public static class TemplateForecast
{
    /// <summary>How many past nights the average is taken over.</summary>
    public const int NightsAveraged = 6;

    /// <summary>
    /// The drivers expected in each of the template's classes, best source
    /// first: the phones' "I'm coming" for tonight; then the average over the
    /// last few nights started from this template; then the last night that
    /// ran any of these classes. Null when there is nothing to go by - the
    /// forecast is left out rather than guessed.
    /// </summary>
    /// <param name="templateName">The template's name, matched against RaceEvent.TemplateName ignoring case.</param>
    /// <param name="classNames">The classes the night would run.</param>
    /// <param name="pastDays">Real race days, any order.</param>
    /// <param name="roundsOf">A day's rounds.</param>
    /// <param name="coming">Tonight's pre-entries from the phones, or null.</param>
    public static ExpectedTurnout? Turnout(
        string? templateName,
        IReadOnlyList<string> classNames,
        IEnumerable<RaceEvent> pastDays,
        Func<RaceEvent, IEnumerable<RoundDefinition>> roundsOf,
        IReadOnlyList<PreEntry>? coming = null)
    {
        var classes = classNames.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (classes.Count == 0)
        {
            return null;
        }

        if (coming is { Count: > 0 })
        {
            var said = classes.ToDictionary(
                c => c,
                c => coming.Count(p => (p.Classes ?? new List<string>()).Contains(c, StringComparer.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);

            if (said.Values.Sum() > 0)
            {
                return new ExpectedTurnout(said, "said they're coming");
            }
        }

        // Newest first, and only nights that ran something.
        var days = pastDays
            .Where(d => d is not null && !d.IsLegacy)
            .OrderByDescending(d => d.StartedUtc)
            .Select(d => (Day: d, Counts: CountsOf(roundsOf(d))))
            .Where(d => d.Counts.Count > 0)
            .ToList();

        if (!string.IsNullOrWhiteSpace(templateName))
        {
            var same = days
                .Where(d => string.Equals(d.Day.TemplateName?.Trim(), templateName.Trim(), StringComparison.OrdinalIgnoreCase))
                .Take(NightsAveraged)
                .ToList();

            if (same.Count > 0)
            {
                var average = classes.ToDictionary(
                    c => c,
                    c => (int)Math.Round(same.Average(d => d.Counts.TryGetValue(c, out var n) ? n : 0), MidpointRounding.AwayFromZero),
                    StringComparer.OrdinalIgnoreCase);

                if (average.Values.Sum() > 0)
                {
                    var source = same.Count == 1 ? $"the last {templateName.Trim()}" : $"the last {same.Count} nights like it";
                    return new ExpectedTurnout(average, source);
                }
            }
        }

        foreach (var (_, counts) in days)
        {
            var last = classes.ToDictionary(c => c, c => counts.TryGetValue(c, out var n) ? n : 0, StringComparer.OrdinalIgnoreCase);

            if (last.Values.Sum() > 0)
            {
                return new ExpectedTurnout(last, "last time");
            }
        }

        return null;
    }

    /// <summary>The drivers who raced in each class on a day: everybody put in any of its races.</summary>
    internal static Dictionary<string, int> CountsOf(IEnumerable<RoundDefinition> rounds) =>
        rounds
            .Where(r => r is not null && !string.IsNullOrWhiteSpace(r.ClassName) && r.Kind != RoundKind.Practice)
            .GroupBy(r => r.ClassName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.Key, Count: g.SelectMany(r => r.AssignedTransponders ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()))
            .Where(g => g.Count > 0)
            .ToDictionary(g => g.Key, g => g.Count, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What the night would come to. Null when no class has anyone expected.
    /// </summary>
    /// <param name="clubNight">A club night: races only, no finals.</param>
    /// <param name="perClass">Drivers expected in each class (see Turnout).</param>
    /// <param name="rounds">Heats (or races) each driver gets - RaceEvent.QualifyingRounds. Below 1 counts as 1.</param>
    /// <param name="perHeat">Drivers per heat. Below 2 counts as 2, as MainsBuilder takes it.</param>
    /// <param name="heatLength">A heat's length on track in a class.</param>
    /// <param name="finalLength">A final's length on track in a class.</param>
    /// <param name="breaks">The night's planned breaks, or null.</param>
    /// <param name="firstRace">The first race's local time of day, or null.</param>
    /// <param name="outBy">The out-by local time of day, or null.</param>
    /// <param name="gap">Finish to next green; NightPlan.DefaultGap when null.</param>
    public static NightForecast? For(
        bool clubNight,
        IReadOnlyDictionary<string, int> perClass,
        int rounds,
        int perHeat,
        Func<string, TimeSpan> heatLength,
        Func<string, TimeSpan> finalLength,
        IReadOnlyList<PlannedBreak>? breaks,
        TimeSpan? firstRace,
        TimeSpan? outBy,
        TimeSpan? gap = null)
    {
        var size = Math.Max(2, perHeat);
        var each = Math.Max(1, rounds);
        var between = gap is { } g && g > TimeSpan.Zero ? g : NightPlan.DefaultGap;

        var drivers = 0;
        var heats = 0;
        var finals = 0;
        var onTrack = TimeSpan.Zero;

        foreach (var (className, expected) in perClass)
        {
            if (expected <= 0)
            {
                continue;
            }

            var groups = (expected + size - 1) / size;

            drivers += expected;
            heats += groups * each;
            onTrack += heatLength(className) * (groups * each);

            if (!clubNight)
            {
                var mains = MainsBuilder.PlanSeeding(Enumerable.Range(0, expected).Select(i => i.ToString()).ToList(), size).TierCount;
                finals += mains;
                onTrack += finalLength(className) * mains;
            }
        }

        var races = heats + finals;

        if (races == 0)
        {
            return null;
        }

        var breakTime = TimeSpan.FromMinutes((breaks ?? Array.Empty<PlannedBreak>()).Sum(b => Math.Clamp(b?.Minutes ?? 0, 0, NightBreaks.MaxMinutes)));
        var racing = onTrack + between * (races - 1) + breakTime;

        TimeSpan? finish = firstRace is { } start ? start + racing : null;
        TimeSpan? spare = finish is { } f && outBy is { } by ? by - f : null;

        return new NightForecast(drivers, heats, finals, racing, finish, spare, clubNight);
    }

    /// <summary>
    /// The forecast in one line: "~42 drivers · 18 heats + 10 finals ·
    /// finishes about 16:20, 40 min to spare". Turnout's source goes on the
    /// end in brackets when given.
    /// </summary>
    public static string Line(NightForecast forecast, string? source = null)
    {
        var races = forecast.ClubNight
            ? Plural.Of(forecast.Heats, "race")
            : $"{Plural.Of(forecast.Heats, "heat")} + {Plural.Of(forecast.Finals, "final")}";

        string when;

        if (forecast.Finish is { } finish)
        {
            var at = AppTime.TimeOfDay(DateTime.Today.Add(finish));

            when = forecast.Spare switch
            {
                { } spare when spare >= TimeSpan.Zero => $"finishes about {at}, {NightPlan.Minutes(spare)} to spare",
                { } spare => $"finishes about {at}, {NightPlan.Minutes(-spare)} past the out-by time",
                null => $"finishes about {at}",
            };
        }
        else
        {
            when = $"about {NightPlan.Minutes(forecast.Racing)} of racing";
        }

        var from = string.IsNullOrWhiteSpace(source) ? string.Empty : $" (from {source})";

        return $"~{Plural.Of(forecast.Drivers, "driver")}{from} · {races} · {when}";
    }

    /// <summary>Whether the forecast runs past the out-by time - the line is then shown as a warning.</summary>
    public static bool RunsLate(NightForecast forecast) => forecast.Spare is { } spare && spare < TimeSpan.Zero;
}
