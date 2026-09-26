namespace HeatCaller;

/// <summary>
/// A sanctioning body's rules, as far as they could actually be read.
///
/// THE DESIGN RULE FOR THIS FILE, AND IT IS THE WHOLE POINT OF IT: a
/// preset may only carry a figure that appears in a document somebody can
/// go and check. Everything else is left alone - not filled in with
/// something plausible, not filled in with the app's default, LEFT ALONE.
///
/// That is why the fields are nullable and the formats are mutators
/// rather than whole RaceFormat objects. A preset that handed over a
/// complete format would be asserting a value for every setting in it,
/// including the dozen it says nothing about; a mutator touches the two or
/// three lines the rulebook actually specifies and leaves the rest of the
/// class's own format exactly as the club set it.
///
/// Each preset therefore carries three pieces of prose as well as its
/// numbers: where it came from (with the revision, because at least one of
/// these documents is edited in place at the same URL), what it sets, and
/// what it deliberately does NOT set. The last one is the most useful
/// thing here. A race director who knows the points scale is not encoded
/// will go and look it up; one who assumes it is will find out at the
/// prize-giving.
/// </summary>
public sealed record SanctioningPreset(
    string Key,
    string Body,
    string Name,
    string Source,
    string Encodes,
    string Caveats,
    Action<RaceFormat>? Qualifier = null,
    Action<RaceFormat>? Main = null,
    QualifyingMethod? Method = null,
    int? ConsecutiveLapCount = null,
    int? CountBestRounds = null,
    int? MainSize = null,
    int? BumpUpCount = null)
{
    public override string ToString() => $"{Body}  -  {Name}";

    /// <summary>
    /// Writes this preset onto a class, touching only what it states.
    ///
    /// The formats are edited on a CLONE of whatever that class already
    /// runs, falling back through its own format to the app default - so a
    /// club that has dialled in a minimum lap time or a streak window keeps
    /// both, and only the lines the rulebook names move.
    /// </summary>
    public IReadOnlyList<string> ApplyTo(RaceClass raceClass, RaceFormat appDefault)
    {
        var changed = new List<string>();

        if (Qualifier is not null)
        {
            var format = (raceClass.QualifierFormat ?? raceClass.Format ?? appDefault).Clone();
            Qualifier(format);
            raceClass.QualifierFormat = format;
            changed.Add($"Qualifier format: {format.Describe()}");
        }

        if (Main is not null)
        {
            var format = (raceClass.Format ?? appDefault).Clone();
            Main(format);
            raceClass.Format = format;
            changed.Add($"Race format: {format.Describe()}");
        }

        if (Method is { } method)
        {
            raceClass.QualifyingMethod = method;
            changed.Add(method == QualifyingMethod.BestConsecutiveLaps
                ? "Qualifying scored on best consecutive laps"
                : "Qualifying scored on most laps, then lowest time");
        }

        if (ConsecutiveLapCount is { } laps)
        {
            raceClass.ConsecutiveLapCount = laps;
            changed.Add($"Consecutive laps counted: {laps}");
        }

        if (CountBestRounds is { } best)
        {
            raceClass.CountBestRounds = best;
            changed.Add(best == 1 ? "Single best heat counts" : $"Best {best} heats count");
        }

        if (MainSize is { } size)
        {
            raceClass.MainSize = size;
            changed.Add($"Drivers per main: {size}");
        }

        if (BumpUpCount is { } bump)
        {
            raceClass.BumpUpCount = bump;
            changed.Add(bump == 0 ? "Bump-ups off" : $"Bump-ups: top {bump} move up");
        }

        return changed;
    }
}

/// <summary>
/// The presets that could be built from documents I could actually read,
/// and only those.
///
/// ROAR IS DELIBERATELY ABSENT. Its Section 8 - Scoring, Qualifying and
/// Starting, which is exactly the section a preset would be made of -
/// could not be extracted, and a preset assembled from the parts around it
/// would be a guess wearing a sanctioning body's name. An empty list entry
/// would at least be honest; a wrong one is worse than nothing, because it
/// is wrong with authority.
/// </summary>
public static class SanctioningPresets
{
    public static readonly IReadOnlyList<SanctioningPreset> All = new List<SanctioningPreset>
    {
        new(
            "pnwc-2026",
            "PN Racing",
            "PNWC 2026 qualifying and mains",
            "2026 PNWC rules, revision v26.07. Note that this document is edited IN PLACE at the same URL - "
            + "the revision above is what it read as when this preset was written, and it may have moved since. "
            + "The 2025 document is gone rather than archived.",

            "Five-minute qualifiers on a simultaneous grid start, scored on laps then time, best two rounds "
            + "counting. Eight-minute A-main, ten drivers, no bump-ups.",

            "The points SCALE is not encoded - the rules say highest points wins, which is the opposite "
            + "direction to the national bodies, but do not give the values. Nor is how the main grids are "
            + "seeded from Q1-Q3, the last-lap allowance, or how the lower mains' two legs are combined into "
            + "one result. The app has no model for a multi-leg main at all: the A-main is three eight-minute "
            + "legs and the lower mains two five-minute legs, and what this sets is the length of ONE leg.",

            Qualifier: f =>
            {
                // Simultaneous grid start. NOT RaceStartType.Qualifier -
                // that is the IFMAR rule, where each driver's clock zeroes
                // at their own first crossing. PN sends the field off
                // together on one clock, which is a standard start, and
                // getting these two the wrong way round changes every
                // driver's qualifying time by their run down to the loop.
                f.StartType = RaceStartType.Standard;
                f.TimeLimitMinutes = 5;
                f.LapLimit = 0;
            },

            Main: f =>
            {
                f.StartType = RaceStartType.Standard;
                f.TimeLimitMinutes = 8;
                f.LapLimit = 0;
            },

            Method: QualifyingMethod.TotalLaps,
            CountBestRounds: 2,
            MainSize: 10,
            BumpUpCount: 0),

        new(
            "ifmar-qualifying",
            "IFMAR",
            "IFMAR-style qualifying clocks",
            "IFMAR general rules as published in 2017 and 2019. Those are the most recent versions I could "
            + "reach, and they may well have been superseded - treat this as the CONVENTION rather than as a "
            + "current rulebook.",

            "Nothing but the start type and the scoring rule: each driver's clock starts at their own first "
            + "crossing, so the run from the grid to the timing loop costs nobody anything, and the heat is "
            + "scored on laps then time.",

            "No durations, no round count, no main sizes and no points. The documents I could reach are old "
            + "enough that putting a figure from them into your app would be worse than leaving the club's own "
            + "settings in place - which is what this does.",

            Qualifier: f => f.StartType = RaceStartType.Qualifier,
            Method: QualifyingMethod.TotalLaps),

        new(
            "brca-onroad",
            "BRCA",
            "BRCA on-road qualifying (duration NOT set)",
            "BRCA on-road section rules and the members' handbook, read in 2026. The two disagree: the section "
            + "rules give a five-minute qualifier and the handbook gives four, and the section's rules changed "
            + "materially between 2022 and 2026.",

            "The start type and scoring only - own clock from first crossing, laps then time.",

            "The qualifier DURATION is deliberately left alone, because the two governing documents give "
            + "different numbers and this app has no business picking one. Look up which applies to your "
            + "meeting and set it yourself. Round counts, main sizes and points are not encoded either.",

            Qualifier: f => f.StartType = RaceStartType.Qualifier,
            Method: QualifyingMethod.TotalLaps),
    };

    public static SanctioningPreset? Find(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}
