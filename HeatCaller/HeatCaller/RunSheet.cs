namespace HeatCaller;

/// <summary>
/// The words and judgements on Race Day's run sheet (17 Nov 2026, the Race
/// Day redesign, mockup A+B in plans/race-day-mockups.html): the night as a
/// timetable, the next race opened up on it, a strip across the top when
/// the night is running late, and the fixes that would bring it back.
///
/// WHY THIS EXISTS. The old Next Steps page had all of this, as one long
/// grey sentence: "Finishes about 9:01 PM - 4 h 02 min past the 5:00 PM
/// out-by time. To finish in time: drop Q3 (4 races) to save 31 min; ...".
/// A race director reads the screen between races, standing up, with
/// somebody asking them a question. The worst news on the page was the
/// quietest text on it. This turns each part of that sentence into
/// something the page can show at a size and in a colour that fits how
/// much it matters.
///
/// Pure: no controls, no stores. RaceProgramControl draws the sheet and
/// TodayControl the strip and the fixes from what this says.
/// </summary>
internal static class RunSheet
{
    /// <summary>How the night's pace looks: no strip, an amber one, or a red one.</summary>
    public enum Pace
    {
        OnTime,
        Watch,
        Problem,
    }

    /// <summary>Running this far past the out-by time turns the strip amber. Less than that is not news.</summary>
    public static readonly TimeSpan WatchAt = TimeSpan.FromMinutes(10);

    /// <summary>And this far turns it red.</summary>
    public static readonly TimeSpan ProblemAt = TimeSpan.FromMinutes(30);

    /// <summary>The pace, from how far past the out-by time the night finishes (NightPlan.Projection.Over).</summary>
    public static Pace PaceOf(TimeSpan? over) => over switch
    {
        { } late when late >= ProblemAt => Pace.Problem,
        { } late when late >= WatchAt => Pace.Watch,
        _ => Pace.OnTime,
    };

    /// <summary>
    /// A clock time without its AM or PM - "4:58" from "4:58 PM" - for the
    /// places on the sheet too narrow for it, where the rows beside it say
    /// which half of the day it is.
    /// </summary>
    public static string ShortClock(string timeOfDay)
    {
        var text = (timeOfDay ?? string.Empty).Trim();

        foreach (var marker in new[] { " AM", " PM", " am", " pm", "AM", "PM", "am", "pm" })
        {
            if (text.EndsWith(marker, StringComparison.Ordinal) && text.Length > marker.Length)
            {
                return text[..^marker.Length].TrimEnd();
            }
        }

        return text;
    }

    /// <summary>"4 h 02 min late", the strip's headline.</summary>
    public static string LateWords(TimeSpan over) => $"{NightPlan.Minutes(over)} late";

    /// <summary>
    /// A race's name with its class first: "Stock · Q1 Heat 1".
    ///
    /// Every class has a "Q1 Heat 1", so the label alone named four races
    /// on the old page - the headline, a tile and three grid rows all said
    /// "Q1 Heat 1", and only the small print said which.
    /// </summary>
    public static string RaceName(string? classLabel, string? roundLabel)
    {
        var label = (roundLabel ?? string.Empty).Trim();
        var className = (classLabel ?? string.Empty).Trim();

        if (className.Length == 0)
        {
            return label;
        }

        return label.Length == 0 ? className : $"{className} · {label}";
    }

    /// <summary>What a row's status pill says, and the shape and colour it is drawn in.</summary>
    public enum Pill
    {
        None,

        /// <summary>Run and saved. Quiet.</summary>
        Recorded,

        /// <summary>Everybody in it is here.</summary>
        Ready,

        /// <summary>Somebody in it isn't checked in.</summary>
        Missing,

        /// <summary>Nobody has been put in it.</summary>
        Empty,

        /// <summary>A main that will be built from qualifying.</summary>
        NotBuilt,

        /// <summary>A race a ticked fix would take off the night.</summary>
        Dropped,

        /// <summary>The night's end, inside the out-by time.</summary>
        OnTime,

        /// <summary>The night's end, past the out-by time.</summary>
        Late,
    }

    /// <summary>The shape every pill and check is drawn with, so no state is told by colour alone.</summary>
    public enum Mark
    {
        None,
        Tick,
        Triangle,
        Square,
    }

    /// <summary>A pill: its kind and its words.</summary>
    public readonly record struct Status(Pill Pill, string Words)
    {
        public Mark Mark => MarkOf(Pill);
    }

    /// <summary>The shape a pill is drawn with: a tick when fine, a triangle to watch, a square to stop.</summary>
    public static Mark MarkOf(Pill pill) => pill switch
    {
        Pill.Recorded or Pill.Ready or Pill.OnTime => Mark.Tick,
        Pill.Missing or Pill.Empty => Mark.Triangle,
        Pill.Late => Mark.Square,
        _ => Mark.None,
    };

    /// <summary>
    /// A round's pill.
    /// </summary>
    /// <param name="recorded">Run and saved.</param>
    /// <param name="drivers">How many drivers are in it.</param>
    /// <param name="here">How many of them are checked in.</param>
    /// <param name="countHere">
    /// Whether the club is checking drivers in tonight at all. A club that
    /// isn't would otherwise see every race as "5 missing".
    /// </param>
    public static Status StatusOf(bool recorded, int drivers, int here, bool countHere)
    {
        if (recorded)
        {
            return new(Pill.Recorded, "Recorded");
        }

        if (drivers <= 0)
        {
            return new(Pill.Empty, "No drivers");
        }

        if (!countHere || here >= drivers)
        {
            return new(Pill.Ready, "Ready");
        }

        return new(Pill.Missing, $"{drivers - Math.Max(0, here)} missing");
    }

    /// <summary>"4 of 5 here" for a race still to run, "5 drivers" for one that has run or a night not checking in.</summary>
    public static string DriversWords(bool recorded, int drivers, int here, bool countHere) =>
        recorded || !countHere || drivers <= 0
            ? Plural.Of(Math.Max(0, drivers), "driver")
            : $"{Math.Clamp(here, 0, drivers)} of {drivers} here";

    /// <summary>"5 min": a race's length on track, in whole minutes, never under one.</summary>
    public static string LengthWords(TimeSpan length) =>
        $"{Math.Max(1, (int)Math.Round(length.TotalMinutes))} min";

    /// <summary>
    /// The "Racing done" row's pill: "+3 h 30" in red past the out-by time,
    /// "On time" inside it, nothing with no out-by time set.
    /// </summary>
    public static Status EndOf(TimeSpan? over, bool hasOutBy)
    {
        if (!hasOutBy)
        {
            return new(Pill.None, string.Empty);
        }

        if (over is not { } late || late <= TimeSpan.Zero)
        {
            return new(Pill.OnTime, "On time");
        }

        var minutes = (int)Math.Round(late.TotalMinutes);

        return new(Pill.Late, minutes < 60 ? $"+{minutes} min" : $"+{minutes / 60} h {minutes % 60:00}");
    }

    /// <summary>One way to bring the night back, as the "Get back on time" card lists it.</summary>
    /// <param name="Label">"Drop Q3 (4 races)".</param>
    /// <param name="Saves">What it takes off the night.</param>
    /// <param name="Round">For dropping a round, its qualifying round number.</param>
    public sealed record Choice(NightPlan.FixKind Kind, string Label, TimeSpan Saves, int? Round)
    {
        /// <summary>Whether the app can make the change itself - a tick box - or only suggest it.</summary>
        public bool CanTick => Kind != NightPlan.FixKind.Advice;

        /// <summary>"−31 min".</summary>
        public string SavesWords => "−" + NightPlan.Minutes(Saves);
    }

    /// <summary>
    /// The night's fixes as the card lists them, biggest saving first - the
    /// same fixes, and the same minutes, the old sentence gave. Empty with
    /// no plan, or a plan that finishes in time.
    /// </summary>
    public static IReadOnlyList<Choice> ChoicesFor(NightPlan.Projection? plan)
    {
        if (plan?.Over is null)
        {
            return Array.Empty<Choice>();
        }

        return plan.Fixes
            .Select(fix => new Choice(fix.Kind, LabelOf(fix), fix.Saves, fix.DropsRound))
            .ToList();
    }

    /// <summary>
    /// What all the ticked choices take off the night. Each choice's saving
    /// is worked out on its own, so two that overlap - a dropped round's
    /// qualifiers would have been shortened too - can come to a little more
    /// than they really save. The card says "about".
    /// </summary>
    public static TimeSpan Saving(IEnumerable<Choice> ticked) =>
        TimeSpan.FromTicks(ticked.Where(c => c.CanTick).Sum(c => c.Saves.Ticks));

    /// <summary>
    /// The short label: the fix's own words without "to save ...", which
    /// the card shows beside it in green, and started with a capital.
    /// </summary>
    private static string LabelOf(NightPlan.Fix fix)
    {
        if (fix.Kind == NightPlan.FixKind.ShorterQualifiers)
        {
            return "Qualifiers 1 min shorter";
        }

        var text = fix.Text;
        var cut = text.IndexOf(" to save ", StringComparison.Ordinal);

        if (cut > 0)
        {
            text = text[..cut];
        }

        // "run one main fewer in Stock" reads as "One main fewer in Stock".
        if (fix.Kind == NightPlan.FixKind.OneMainFewer && text.StartsWith("run ", StringComparison.Ordinal))
        {
            text = text[4..];
        }

        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}
