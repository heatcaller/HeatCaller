namespace HeatCaller;

/// <summary>
/// A night template (12 Nov 2026, setup plan B4): a race day's settings
/// saved under a name - "Tuesday club night", "Points round" - to start a
/// night from in one press.
///
/// WHY: a new day already carries last week's settings forward (A1), which
/// is right for a club that runs one kind of night. A club that runs two -
/// a weekly club night and a monthly points round, with different classes,
/// heat counts, fees and times - switched between them by retyping half the
/// Setup tab. A template is each of them, kept.
/// </summary>
public sealed class NightTemplate
{
    public string Name { get; set; } = string.Empty;

    public RaceDayShape Shape { get; set; } = RaceDayShape.RaceDay;

    public string VenueName { get; set; } = string.Empty;

    public string TrackLayoutName { get; set; } = string.Empty;

    public string ChampionshipId { get; set; } = string.Empty;

    public List<string> ClassNames { get; set; } = new();

    public List<string> ClassRunningOrder { get; set; } = new();

    public bool RotateClassOrder { get; set; }

    public int QualifyingRounds { get; set; }

    public int DriversPerHeat { get; set; }

    public FirstRoundSeeding FirstRoundSeeding { get; set; } = FirstRoundSeeding.Pace;

    public int ShareHeatsUnder { get; set; }

    /// <summary>Races off between a driver's races (setup plan step 8) - see RaceEvent.RacesOffBetween.</summary>
    public int RacesOffBetween { get; set; } = RestOrder.DefaultGap;

    public decimal EntryFee { get; set; }

    public decimal ExtraClassFee { get; set; }

    public string BetweenRacesPreset { get; set; } = string.Empty;

    public bool CheckedInDriversOnly { get; set; }

    public TimeSpan? FirstRaceLocalTime { get; set; }

    public TimeSpan? EntriesCloseLocalTime { get; set; }

    public TimeSpan? OutByLocalTime { get; set; }

    public List<PlannedBreak> Breaks { get; set; } = new();

    /// <summary>
    /// The days of the week this template is the night on (12 Nov 2026, setup
    /// plan step 7): "every Tuesday". Next Steps then offers to start that
    /// night from it. Empty for none.
    /// </summary>
    public List<DayOfWeek> Weekdays { get; set; } = new();

    /// <summary>Particular dates it's the night on (local dates) - a points round once a month. These win over a weekday.</summary>
    public List<DateTime> Dates { get; set; } = new();
}

/// <summary>Making a template from a day, and a day from a template. Pure.</summary>
public static class NightTemplates
{
    /// <summary>A template of everything about this day that a new day can start from - never its rounds, check-ins or results.</summary>
    public static NightTemplate From(RaceEvent day, string name) => new()
    {
        Name = name.Trim(),
        Shape = day.Shape,
        VenueName = day.VenueName ?? string.Empty,
        TrackLayoutName = day.TrackLayoutName ?? string.Empty,
        ChampionshipId = day.ChampionshipId ?? string.Empty,
        ClassNames = (day.ClassNames ?? new List<string>()).ToList(),
        ClassRunningOrder = (day.ClassRunningOrder ?? new List<string>()).ToList(),
        RotateClassOrder = day.RotateClassOrder,
        QualifyingRounds = day.QualifyingRounds,
        DriversPerHeat = day.DriversPerHeat,
        FirstRoundSeeding = day.FirstRoundSeeding,
        ShareHeatsUnder = day.ShareHeatsUnder,
        RacesOffBetween = day.RacesOffBetween,
        EntryFee = day.EntryFee,
        ExtraClassFee = day.ExtraClassFee,
        BetweenRacesPreset = day.BetweenRacesPreset ?? string.Empty,
        CheckedInDriversOnly = day.CheckedInDriversOnly,
        FirstRaceLocalTime = day.FirstRaceLocalTime,
        EntriesCloseLocalTime = day.EntriesCloseLocalTime,
        OutByLocalTime = day.OutByLocalTime,
        Breaks = NightBreaks.Copy(day.Breaks),
    };

    /// <summary>
    /// Puts a template's settings onto a day just started from it, over what
    /// the new day carried forward from last week. The day's name, rounds,
    /// check-ins and results are left alone.
    /// </summary>
    public static void ApplyTo(NightTemplate template, RaceEvent day)
    {
        day.Shape = template.Shape;
        day.VenueName = template.VenueName;
        day.TrackLayoutName = template.TrackLayoutName;
        day.ChampionshipId = template.ChampionshipId;
        day.ClassNames = template.ClassNames.ToList();
        day.ClassRunningOrder = template.ClassRunningOrder.ToList();
        day.RotateClassOrder = template.RotateClassOrder;

        if (template.QualifyingRounds > 0)
        {
            day.QualifyingRounds = template.QualifyingRounds;
        }

        if (template.DriversPerHeat > 0)
        {
            day.DriversPerHeat = template.DriversPerHeat;
        }

        day.FirstRoundSeeding = template.FirstRoundSeeding;
        day.ShareHeatsUnder = template.ShareHeatsUnder;
        day.RacesOffBetween = template.RacesOffBetween;
        day.EntryFee = template.EntryFee;
        day.ExtraClassFee = template.ExtraClassFee;
        day.BetweenRacesPreset = template.BetweenRacesPreset;
        day.CheckedInDriversOnly = template.CheckedInDriversOnly;
        day.FirstRaceLocalTime = template.FirstRaceLocalTime;
        day.EntriesCloseLocalTime = template.EntriesCloseLocalTime;
        day.OutByLocalTime = template.OutByLocalTime;
        day.Breaks = NightBreaks.Copy(template.Breaks);
    }

    /// <summary>
    /// Follows a class being renamed, in every template: its classes and
    /// their running order. True when any template changed, so the caller
    /// saves only then.
    ///
    /// Bug hunt 17: templates were left naming the old class, so a night
    /// started from one listed a class that no longer existed and the
    /// renamed class was not racing that night, with nothing said.
    /// Matched ignoring case, and a name listed twice afterwards is listed
    /// once, as the race days do it (RaceEventStore.RenameClass).
    /// </summary>
    public static bool RenameClass(IEnumerable<NightTemplate>? templates, string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
        {
            return false;
        }

        var changed = false;

        foreach (var template in templates ?? Enumerable.Empty<NightTemplate>())
        {
            if (template is null)
            {
                continue;
            }

            if (Renamed(template.ClassNames, oldName, newName) is { } names)
            {
                template.ClassNames = names;
                changed = true;
            }

            if (Renamed(template.ClassRunningOrder, oldName, newName) is { } order)
            {
                template.ClassRunningOrder = order;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>A list of class names with one renamed, or null when the list doesn't hold the old name.</summary>
    internal static List<string>? Renamed(List<string>? names, string oldName, string newName) =>
        names is null || !names.Any(n => string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase))
            ? null
            : names
                .Select(n => string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase) ? newName : n)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>The templates with this one saved: replacing one of the same name (ignoring case), the rest in name order.</summary>
    public static List<NightTemplate> Saved(IEnumerable<NightTemplate>? templates, NightTemplate template) =>
        (templates ?? Enumerable.Empty<NightTemplate>())
        .Where(t => t is not null && !string.Equals(t.Name.Trim(), template.Name.Trim(), StringComparison.OrdinalIgnoreCase))
        .Append(template)
        .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <summary>A template's line for the picker: "Tuesday club night - Club night, 3 classes, first race 19:00".</summary>
    public static string Describe(NightTemplate t)
    {
        var kind = t.Shape == RaceDayShape.ClubNight ? "Club night" : "Race day";
        var first = t.FirstRaceLocalTime is { } time ? $", first race {AppTime.TimeOfDay(DateTime.Today.Add(time))}" : string.Empty;

        var when = WhenWords(t) is { Length: > 0 } on ? $" - {on}" : string.Empty;

        return $"{t.Name} - {kind}, {Plural.Of(t.ClassNames.Count, "class", "classes")}{first}{when}";
    }

    /// <summary>
    /// The template that's tonight's night (setup plan step 7), or null: one
    /// set for this date first, then one set for this day of the week. The
    /// first of each, in the templates' order.
    /// </summary>
    public static NightTemplate? ForDay(IEnumerable<NightTemplate>? templates, DateTime localDay)
    {
        var all = (templates ?? Enumerable.Empty<NightTemplate>()).Where(t => t is not null).ToList();
        var day = localDay.Date;

        return all.FirstOrDefault(t => (t.Dates ?? new List<DateTime>()).Any(d => d.Date == day))
               ?? all.FirstOrDefault(t => (t.Weekdays ?? new List<DayOfWeek>()).Contains(day.DayOfWeek));
    }

    /// <summary>When a template is the night, in a few words: "Tuesdays", "Tue and Thu, and 6 Dec", or "" for never.</summary>
    public static string WhenWords(NightTemplate t)
    {
        var format = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;
        var days = (t.Weekdays ?? new List<DayOfWeek>()).Distinct().OrderBy(d => ((int)d + 6) % 7).ToList();
        var dates = (t.Dates ?? new List<DateTime>()).Select(d => d.Date).Where(d => d >= DateTime.Today).Distinct().OrderBy(d => d).ToList();

        var parts = new List<string>();

        if (days.Count == 1)
        {
            parts.Add(format.GetDayName(days[0]) + "s");
        }
        else if (days.Count > 1)
        {
            parts.Add(string.Join(" and ", days.Select(format.GetAbbreviatedDayName)));
        }

        if (dates.Count > 0)
        {
            parts.Add(string.Join(", ", dates.Take(3).Select(d => d.ToString("d MMM"))) + (dates.Count > 3 ? ", ..." : string.Empty));
        }

        return string.Join(", and ", parts);
    }
}
