using System.Text.Json.Serialization;

namespace HeatCaller;

/// <summary>How qualifying results are ranked for a class.</summary>
public enum QualifyingMethod
{
    /// <summary>Most laps, then lowest elapsed time. The simplest rule, and what this app did before anything else existed.</summary>
    TotalLaps,

    /// <summary>
    /// Fastest run of N consecutive laps, ignoring everything else.
    ///
    /// Common in RC racing because it rewards a clean sustained run rather
    /// than a single lucky lap, and isn't skewed by one mistake in an
    /// otherwise good heat. N is usually 3 or 5.
    /// </summary>
    BestConsecutiveLaps,

    /// <summary>
    /// Position points per qualifying ROUND, lowest total wins (11 Nov 2026,
    /// race-night build plan, Phase 5.1 - the ROAR "Qual-Points" style).
    ///
    /// WHY: under total laps or best consecutive laps one heat on a green
    /// track, or one heat run while it rained, is compared directly with a
    /// heat on a rubbered-in track an hour later - the later heats win on
    /// conditions, not on driving. Points only ever compare drivers who ran
    /// the SAME round, so a whole round run in the wet costs everybody the
    /// same. Each round ranks its drivers across all of that round's heats
    /// (most laps, then least time - the total-laps rule, inside the round),
    /// 1st scores 1, 2nd scores 2 and so on, and a driver who did not run
    /// the round (or was disqualified from it) scores one more than the
    /// field. CountBestRounds picks the best N rounds. Ties go to the best
    /// single-round finish, then the best single heat by laps and time.
    /// (That is the club scale; 12 Nov 2026, RoundPointsScale adds the
    /// IFMAR and EFRA ones, where the top qualifier scores 0.)
    ///
    /// Added at the END of the enum on purpose: classes are saved with the
    /// method as a number in older files, and inserting it earlier would
    /// quietly change every saved class's rule.
    /// </summary>
    RoundPoints,
}

/// <summary>
/// What a round's places are worth under <see cref="QualifyingMethod.RoundPoints"/>,
/// and the rulebook's tie-breaks and missed-round score that go with them
/// (12 Nov 2026).
///
/// WHY: the club scale gives the top qualifier 1 point. IFMAR (off-road
/// 1.7, track 1.4.5), EFRA (Appendix 3, 9.4.1) and ROAR (14.3.6) give the
/// top qualifier ZERO and second 2, third 3 - a winner's bonus of one point
/// per round won. That changes the order, not just the figures: 1st + 4th
/// is 5 against 2nd + 2nd's 4 on the club scale (2nd + 2nd ahead), and 4
/// against 4 on the rulebook scale, where the round winner then takes it
/// on the best single finish. A sanctioned meeting scored on the club scale
/// would hand that place to the wrong driver.
///
/// The bodies agree on the scale and part company on the rest, so each is
/// its own choice rather than one "0 for the TQ" switch:
///  - a missed or disqualified round: IFMAR track 500 points (off-road 1.7:
///    "no points", which is NOT zero - read as the same 500), EFRA "points
///    for last place in that Round" (the field: every driver in the
///    class's qualifying);
///  - ties, counting rounds only (both bodies discard the dropped rounds'
///    scores, laps and times): IFMAR compares the counted scores lowest
///    first, all of them, then the laps and time of the lowest-scoring
///    round; EFRA the single best finish, then the laps and time of the
///    best points round, then of the second best.
///
/// Club is first, and the default, so every class saved before this scores
/// exactly as it did. Added in this order for the same reason as
/// RoundPoints: a number in a saved file must keep meaning what it meant.
/// </summary>
public enum RoundPointsScale
{
    /// <summary>1st = 1, 2nd = 2; a missed round = field + 1; ties to the best finish, then the best heat anywhere.</summary>
    Club,

    /// <summary>IFMAR: TQ 0, 2nd 2, 3rd 3; a missed round 500; ties on the counted scores, then the lowest score's laps and time.</summary>
    Ifmar,

    /// <summary>EFRA: TQ 0, 2nd 2, 3rd 3; a missed round scores last place (the field); ties on the best finish, then the best round's laps and time.</summary>
    Efra,
}

/// <summary>A racing class (e.g. "Stock Buggy", "Mini-Z MR03") used to group drivers for heats/mains.</summary>
public sealed class RaceClass
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    /// <summary>How this class's qualifying is scored. Per-class because clubs often run different rules for different classes.</summary>
    public QualifyingMethod QualifyingMethod { get; set; } = QualifyingMethod.TotalLaps;

    /// <summary>How many consecutive laps count when using BestConsecutiveLaps. Typically 3 or 5.</summary>
    public int ConsecutiveLapCount { get; set; } = 3;

    /// <summary>
    /// How many of a driver's heats count toward qualifying. 1 means their
    /// single best heat decides it. Higher values total that many of their
    /// best heats and discard the rest - "best 2 of 3", the usual way to
    /// stop one bad round wrecking a day.
    /// </summary>
    public int CountBestRounds { get; set; } = 1;

    /// <summary>
    /// What a round's places score under points qualifying - the club's
    /// 1st = 1 point, or a rulebook's TQ = 0 (12 Nov 2026, see
    /// RoundPointsScale). Read only when QualifyingMethod is RoundPoints.
    /// </summary>
    public RoundPointsScale RoundPointsScale { get; set; } = RoundPointsScale.Club;

    /// <summary>
    /// What scrutineering checks this class requires. Per class because the
    /// rules differ - a stock class cares about motor and tyres, a modified
    /// one might only check the body and ride height.
    ///
    /// Empty means tech isn't run for this class.
    /// </summary>
    public List<string> TechCheckItems { get; set; } = new();

    /// <summary>
    /// Warn when a driver who hasn't passed tech is put in a heat. A warning
    /// rather than a block: the race director decides who races, and a
    /// scrutineer running behind shouldn't stop an event.
    /// </summary>
    public bool WarnOnUninspected { get; set; } = true;

    /// <summary>
    /// Watch for cars crossing the line before the green drops. The passes
    /// are already seen and discarded during staging and countdown, so this
    /// only decides whether to act on them.
    /// </summary>
    public bool DetectJumpStarts { get; set; } = true;

    /// <summary>
    /// Seconds added to a jump-starting driver's time, applied
    /// automatically. Zero means flag only - the jump start is logged,
    /// announced and shown on the leaderboard, but the call on what to do
    /// stays with the race director. That's the default deliberately: a
    /// stray decoder read shouldn't quietly cost someone a race.
    /// </summary>
    public int JumpStartPenaltySeconds { get; set; }

    /// <summary>
    /// Whether a jump start also costs the driver the lap they jumped on.
    ///
    /// OFF by default, which is the change from how this used to behave.
    ///
    /// A car that crosses before the green has its pass discarded - there
    /// is no race yet to count it in. The side effect was that everyone
    /// else banked a start lap a second after the green while the jumper
    /// did not score until a full lap later, so a two-tenths jump quietly
    /// cost about ten seconds of elapsed time for the rest of the race, on
    /// top of any penalty. Nothing on screen explained the gap.
    ///
    /// With this off, a jumper is credited with the start lap at the green
    /// - they were past the line already - and pays the time penalty and
    /// nothing else. Turn it on for a club that wants a jump start to cost
    /// the lap, which is a real rule, just not one the app should apply
    /// without being asked.
    ///
    /// Only applies to crossings before the GREEN. Jumping your own
    /// release in a staggered start still loses the pass, because there is
    /// no shared moment to credit it at.
    /// </summary>
    public bool JumpStartCostsTheLap { get; set; }

    /// <summary>
    /// What each warning given in one of this class's races costs - see
    /// WarningRules. Null follows the default (warning, +5s, +10s, black
    /// flag), for the same reason Format does: a class that never chose
    /// should pick up a changed default.
    /// </summary>
    public List<WarningStep>? WarningLadder { get; set; }

    /// <summary>
    /// How this class's races are run - limits, start procedure, streak
    /// rules - or null to follow the app default.
    ///
    /// Null rather than a filled-in copy of the default, deliberately: a
    /// class that has never chosen a format should FOLLOW the default when
    /// the default changes, and a snapshot taken at creation time would
    /// quietly freeze it instead.
    /// </summary>
    public RaceFormat? Format { get; set; }

    /// <summary>
    /// A separate format for this class's QUALIFIERS, or null to run them
    /// under the class's ordinary format. The common club shape - five
    /// minute quals, eight minute mains - stops needing an override typed
    /// onto every main.
    /// </summary>
    public RaceFormat? QualifierFormat { get; set; }

    /// <summary>
    /// What this class costs a driver as their entry tonight, or null to
    /// charge the race day's Entry fee (12 Nov 2026, setup plan step 8).
    ///
    /// WHY: a race day had one entry fee, and clubs don't - "Juniors $5, Mod
    /// $20" is on half the price boards in the country. One fee for the night
    /// meant the desk worked the difference out in its head and the report's
    /// totals were wrong by it. Null rather than zero, because zero is a real
    /// answer (a free beginners' class) and "not set" has to mean "the day's".
    /// See DeskMoney.FeeFor for how several classes add up.
    /// </summary>
    public decimal? EntryFee { get; set; }

    /// <summary>
    /// What this class costs as a driver's second or later class tonight, or
    /// null for the race day's Each extra class fee - capped at this class's
    /// own EntryFee when it has one, so a $5 class is never $10 as an extra
    /// (12 Nov 2026, setup plan step 8). See DeskMoney.FeeFor.
    /// </summary>
    public decimal? ExtraClassFee { get; set; }

    /// <summary>Maximum drivers per main. Qualifying is split into as many tiers as needed to seat everyone.</summary>
    public int MainSize { get; set; } = 8;

    /// <summary>
    /// How many finishers bump up from each main into the tier above.
    /// Zero disables bump-ups entirely, which is what clubs running
    /// straight seeded mains want.
    /// </summary>
    public int BumpUpCount { get; set; } = 2;

    /// <summary>
    /// How many legs the A-main runs as (11 Nov 2026, Phase 5.3): 1 is a
    /// single A-main, as always; 2 or 3 is a double or triple A-main, scored
    /// on points over the best legs - see AMainLegs.
    /// </summary>
    public int AMainLegs { get; set; } = 1;

    /// <summary>
    /// How many A-main legs count, or 0 for the usual rule (all but one:
    /// best 2 of 3, best 1 of 2) - see AMainLegs.LegsCounting.
    /// </summary>
    public int AMainLegsCounted { get; set; }

    /// <summary>
    /// The class's colour, as ARGB. Zero means nobody has chosen one, and
    /// one is derived from the name instead - see Colour.
    /// </summary>
    public int ColorArgb { get; set; }

    /// <summary>
    /// What this class looks like on a heat sheet and in a programme list.
    ///
    /// Derived from the NAME when unset, rather than left blank or handed
    /// out in creation order. A pit table with four printouts on it wants
    /// them to be four different colours without anybody having configured
    /// anything - and deriving from the name means the same class is the
    /// same colour on every machine at the club, and stays that colour
    /// when another class is added or deleted.
    /// </summary>
    /// <remarks>
    /// Not stored. System.Text.Json serialises get-only properties, and
    /// without this the class file would carry a whole Color object -
    /// R, G, B, A, IsKnownColor and the rest - beside the one int that
    /// actually means something, re-derived and re-written on every save.
    /// </remarks>
    [JsonIgnore]
    public Color Colour => ColorArgb != 0 ? Color.FromArgb(ColorArgb) : DefaultColourFor(Name);

    /// <summary>
    /// A class's colour as it should appear ON SCREEN, brightened for a
    /// dark look.
    ///
    /// The stored colour is chosen to print on white paper, where a light
    /// tint would vanish; on the app's near-black it needs lifting or every
    /// class reads as the same dark smudge. On a light look the printed
    /// colour is what reads best, which is the rule the Classes page uses.
    ///
    /// Here rather than on a page, because the programme's class column,
    /// the heat sheet and the Garage's class bands all have to agree - a
    /// club recognises its classes by these colours, and two pages picking
    /// them separately is how they stop matching.
    /// </summary>
    public static Color OnScreenColourFor(RaceClass? raceClass, string className)
    {
        var printed = raceClass?.Colour ?? DefaultColourFor(className);

        if (AppTheme.Active.Light)
        {
            return printed;
        }

        return Color.FromArgb(
            Math.Min(255, printed.R + 90),
            Math.Min(255, printed.G + 90),
            Math.Min(255, printed.B + 90));
    }

    /// <summary>
    /// A stable, readable colour for a class name.
    ///
    /// Hue from the name's hash, but saturation and lightness fixed: these
    /// are printed on white paper and read under shop lighting, so a
    /// pastel or a near-black would both be useless. The hash is written
    /// out by hand rather than using string.GetHashCode, which is
    /// randomised per process in .NET Core - the same class would come out
    /// a different colour on every launch.
    /// </summary>
    public static Color DefaultColourFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Color.FromArgb(90, 90, 90);
        }

        var hash = 2166136261u;

        foreach (var c in name.Trim().ToLowerInvariant())
        {
            hash = (hash ^ c) * 16777619u;
        }

        // Nudged off the exact primaries: a pure red or green next to a
        // pure blue reads as a traffic light rather than as a set.
        var hue = hash % 360u * 1.0 + 12.0;

        return FromHsl(hue % 360.0, 0.62, 0.42);
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs(hue / 60.0 % 2 - 1));
        var m = lightness - c / 2;

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };

        return Color.FromArgb(
            (int)Math.Round((r + m) * 255),
            (int)Math.Round((g + m) * 255),
            (int)Math.Round((b + m) * 255));
    }
}
