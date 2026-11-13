namespace HeatCaller;

/// <summary>The three ways the Start Tonight window can start a race day.</summary>
public enum StartTonightKind
{
    /// <summary>Like the last race day: its kind of night, its classes, and everything a new day carries forward.</summary>
    LikeLast,

    /// <summary>From a night template (setup plan B4).</summary>
    Template,

    /// <summary>The two questions: what kind of night, and which classes.</summary>
    Different,
}

/// <summary>One choice at the top of the Start Tonight window.</summary>
/// <param name="Kind">Which way it starts the day.</param>
/// <param name="Caption">The choice as the window shows it: "Like last time (Tue 4 Nov)".</param>
/// <param name="Detail">What it would start, under the caption.</param>
/// <param name="LastDay">For LikeLast, the day it copies.</param>
/// <param name="Template">For Template, the template.</param>
public sealed record StartTonightOption(StartTonightKind Kind, string Caption, string Detail, RaceEvent? LastDay = null, NightTemplate? Template = null);

/// <summary>
/// ONE WAY TO START A NIGHT (12 Nov 2026, setup plan step 8).
///
/// There were seven doors into "start a race day" and they did different
/// things: Setup's New Race Day asked two questions, Set Up Tonight asked its
/// own, Next Steps' Start a Race Day opened the wizard, "Start tonight like
/// last time" and "Start tonight's Tuesday club night" did it in one press
/// with no window at all, the Calendar's New Race Day asked the two
/// questions, and the Who's Racing banner opened the wizard. A director who
/// learned one learned nothing about the next, and the guide spent a section
/// explaining which was which.
///
/// Now every one of them opens the same Start Tonight window
/// (NewRaceDayForm), which offers at the top: like last time, each night
/// template, or something different - the two questions. The window works
/// out nothing itself; this class says which choices there are and which is
/// picked to begin with, and RaceDayEventControl.StartTonight carries the
/// answer out through StartDayFrom, so there is still one implementation and
/// one undo entry. A door that used to be one press - Next Steps' card -
/// opens the window with its own choice already picked, so it's that press
/// and Start.
/// </summary>
public static class StartTonight
{
    /// <summary>
    /// The choices, in the window's order: like last time (when there is a
    /// last race day with classes to go by), each night template, and
    /// something different - always there, and the only choice on a club's
    /// first night.
    /// </summary>
    public static IReadOnlyList<StartTonightOption> Options(RaceEvent? lastDay, IEnumerable<NightTemplate>? templates)
    {
        var options = new List<StartTonightOption>();

        if (lastDay is { ClassNames.Count: > 0 } last)
        {
            options.Add(new StartTonightOption(StartTonightKind.LikeLast, LikeLastCaption(last), LikeLastDetail(last), LastDay: last));
        }

        foreach (var template in (templates ?? Enumerable.Empty<NightTemplate>()).Where(t => t is not null && !string.IsNullOrWhiteSpace(t.Name)))
        {
            options.Add(new StartTonightOption(StartTonightKind.Template, template.Name.Trim(), NightTemplates.Describe(template), Template: template));
        }

        options.Add(new StartTonightOption(
            StartTonightKind.Different,
            "Something different...",
            "Choose what kind of night it is and which classes are racing. Everything else starts from your last race day."));

        return options;
    }

    /// <summary>"Like last time (Tue 4 Nov)" - the day the last race day was started.</summary>
    public static string LikeLastCaption(RaceEvent last) => $"Like last time ({last.StartedUtc.ToLocalTime():ddd d MMM})";

    /// <summary>What "like last time" starts: the last day's name, kind of night, classes and venue.</summary>
    public static string LikeLastDetail(RaceEvent last)
    {
        var kind = last.IsClubNight ? "Club night" : "Race day";
        var at = string.IsNullOrWhiteSpace(last.VenueName) ? string.Empty : $" at {last.VenueName.Trim()}";

        return $"As \"{last.DisplayName}\": {kind}, {string.Join(", ", last.ClassNames)}{at}, with its heat sizes, fees, breaks and the rest.";
    }

    /// <summary>
    /// Which choice is picked when the window opens: the one asked for (a
    /// Next Steps card's, or the template picked on Setup), else tonight's
    /// template, else like last time, else something different.
    /// </summary>
    /// <param name="options">From <see cref="Options"/>.</param>
    /// <param name="tonightsTemplate">The template set for tonight (NightTemplates.ForDay), or null.</param>
    /// <param name="wanted">The kind a door asked for, or null.</param>
    /// <param name="wantedTemplate">For a Template door, the template's name.</param>
    public static int Preselect(IReadOnlyList<StartTonightOption> options, NightTemplate? tonightsTemplate, StartTonightKind? wanted = null, string? wantedTemplate = null)
    {
        int Find(Func<StartTonightOption, bool> match)
        {
            for (var i = 0; i < options.Count; i++)
            {
                if (match(options[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        bool IsTemplate(StartTonightOption option, string? name) =>
            option.Kind == StartTonightKind.Template && name is not null
            && string.Equals(option.Template?.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase);

        var asked = wanted switch
        {
            StartTonightKind.Template => Find(o => IsTemplate(o, wantedTemplate)),
            { } kind => Find(o => o.Kind == kind),
            null => -1,
        };

        if (asked >= 0)
        {
            return asked;
        }

        var tonight = Find(o => IsTemplate(o, tonightsTemplate?.Name));

        if (tonight >= 0)
        {
            return tonight;
        }

        var likeLast = Find(o => o.Kind == StartTonightKind.LikeLast);

        return likeLast >= 0 ? likeLast : options.Count - 1;
    }
}
