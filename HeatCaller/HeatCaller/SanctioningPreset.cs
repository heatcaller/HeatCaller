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
    int? BumpUpCount = null,
    int? AMainLegs = null,
    int? AMainLegsCounted = null)
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
            changed.Add(method switch
            {
                QualifyingMethod.BestConsecutiveLaps => "Qualifying scored on best consecutive laps",
                QualifyingMethod.RoundPoints => "Qualifying scored on points per round, fewest wins",
                _ => "Qualifying scored on most laps, then lowest time",
            });
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

        // The class's A-main setting - how many legs, and how many of them
        // count. Set only by a preset whose rulebook says how its A-main is
        // run AND scores it the way AMainLegs does (1st = 1 point, fewest
        // wins); a rulebook that scores the legs some other way leaves it.
        if (AMainLegs is { } legs)
        {
            raceClass.AMainLegs = Math.Clamp(legs, 1, HeatCaller.AMainLegs.MaxLegs);
            changed.Add(raceClass.AMainLegs == 1 ? "A-main: a single race" : $"A-main: {raceClass.AMainLegs} legs");
        }

        if (AMainLegsCounted is { } counted)
        {
            raceClass.AMainLegsCounted = counted;
            changed.Add($"A-main legs that count: best {HeatCaller.AMainLegs.LegsCounting(raceClass.AMainLegs, counted)}");
        }

        return changed;
    }
}

/// <summary>
/// The presets that could be built from documents I could actually read,
/// and only those.
///
/// ROAR, EFRA, NZRCA AND THE REEDY RACE (12 Nov 2026, setup plan step 8).
/// ROAR used to be deliberately absent, because its scoring and qualifying
/// section could not be extracted and a preset built from the parts round
/// it would have been a guess with a sanctioning body's name on it. It is
/// here now because the whole current rulebook was read - ROAR Rules
/// Ver. 26-10a, effective 16 Oct 2026 - and every figure below is cited to
/// its rule number and page. EFRA's and NZRCA's are from their 2026
/// documents on their own sites, and the Reedy Race's from reedyrace.com.
///
/// THE POINTS TRAP, which is why three of these leave qualifying alone.
/// ROAR's Qual-Points (14.3.6) and EFRA's Round by Round system (App. 3
/// 9.4.1) both give the top qualifier ZERO points, second 2, third 3. The
/// app's RoundPoints gives first 1 point. That one-point difference is not
/// cosmetic: 1st + 4th is 4 points under ROAR and EFRA and 5 under the app,
/// against 2nd + 2nd's 4 in both, so the app would rank those two drivers
/// the other way round. A preset that switched RoundPoints on and called it
/// ROAR's system would score a sanctioned meeting wrong. The A-main legs,
/// on the other hand, ARE scored 1st = 1 point by EFRA (10.5) and NZRCA
/// (B3.12 / A3.10), which is what AMainLegs does, so those are set.
///
/// An empty list entry would at least be honest; a wrong one is worse
/// than nothing, because it is wrong with authority.
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
            + "one result. The A-main is three eight-minute legs and the lower mains two five-minute legs: what "
            + "this sets is the length of ONE leg. The A-main's legs are the class's A-main setting, which this "
            + "preset leaves as it is, and the app has no multi-leg lower mains.",

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

        // ROAR Rules Ver. 26-10a, effective 16 Oct 2026 (roarracing.com,
        // downloads/ROAR_Rule_Book.pdf, 92 pages). Club-level (Level 1-4)
        // electric racing, which is what a HeatCaller club runs:
        //   8.1.4 (p. 55)  race lengths table - 1/10 electric off-road and
        //                  on-road: qualifiers 5 min, A-mains 5, other
        //                  mains 5; 1/12 electric on-road 8 / 8 / 8.
        //   8.1.3 (p. 55)  those lengths are mandatory at Level 3-5; at
        //                  lower levels optional but announced in advance.
        //   8.8.3 (p. 59)  a round is placed on most laps, shortest time.
        //   8.8.4 (p. 59)  Level 1-4: mains are set from each driver's
        //                  SINGLE fastest qualifying heat.
        //   8.9.1 (p. 59)  electric: all mains use a full-field start;
        //                  qualifiers may use either method.
        // Not set: the qualifier start (8.9.1 allows either), main size and
        // bump-ups (no general rule for electric Level 1-4 - 3.11.6's "two
        // bump ups" is in Section 3, the 1/5-scale fuel rules), the triple
        // A-main (8.8.6 / 14.3.7: Level 5 nationals, "may" lower down), and
        // Level 5's Qual-Points (14.3.6 - see the points trap above).
        Roar("roar-electric-10", "1/10 electric, Level 1-4 (5-minute)", "1/10 electric off-road and on-road", 5),
        Roar("roar-electric-12", "1/12 electric on-road, Level 1-4 (8-minute)", "1/12 electric on-road", 8),

        // EFRA Handbook 2026, Appendix 3 "Electric Cars", v4 (efra.ws,
        // wp-content/uploads/2026/06/EFRA Handbook 2026 App3 v4.pdf), the
        // European Championship and Grand Prix format:
        //   9.4.1  (p. 101-102) 1/10 off-road and 1/10 on-road qualifying
        //                  heats and finals 5 min; 1/12 track 8 min.
        //   1.1.11 (p. 96) and 9.2.3 (p. 101) all finals start from an F1
        //                  type grid.
        //   10.1   (p. 103) 10 drivers in all finals where possible; 9.4.9
        //                  top 10 qualifiers to the A-final.
        //   10.3   (p. 103) off-road: all finals in three legs; on-road:
        //                  all finals in 3 legs. 10.4: best 2 of 3 count.
        //   10.5   (p. 103) a final's winner 1 point, second 2 ... - the
        //                  AMainLegs scale.
        // Not set: the qualifier start (9.2.2 / 9.4.4 call a staggered start
        // car by car but give no gap and do not say how the clock runs),
        // qualifying scoring (Round by Round, 9.4.1 - TQ scores zero, see
        // the points trap; and the rounds-to-count table changes with the
        // rounds completed), bump-ups (none in the appendix), and the
        // three-leg LOWER finals, which the app does not run. The second
        // tie-breaker differs too: 10.6 compares laps and times from each
        // driver's best finish, AMainLegs the last leg's finishing order.
        // The pages of an older handbook left inside the PDF (headed 132,
        // 138, 140, 144) were ignored; the numbers are from pages 96-104.
        Efra("efra-electric-10", "1/10 electric off-road and on-road (5-minute)", "1/10 off-road and 1/10 on-road", 5),
        Efra("efra-electric-12", "1/12 electric track (8-minute)", "1/12 track", 8),

        // NZRCA EP Off-Road Rules 2026, Version 29.0 (res.cloudinary.com/
        // nzrca/.../EP Offroad Rules 2026.pdf), section B3 Race Procedures:
        //   B6.1a  1/10 classes heats and finals five minutes.
        //   B3.5   qualifying staggered start, each driver on an individual
        //          clock for the race period; the gap is the race
        //          director's or the scoring program's.
        //   B3.6   finals at national events a grid start.
        //   B3.9   heats and finals a maximum of 12 cars.
        //   B3.11  A finals at least 3 races; B3.12 1 point for the winner
        //          down to 12 for twelfth, best 2 of 3 count.
        // Not set: qualifying scoring ("IFMAR qualifying points system",
        // B3.5, scale not given in the document), the round count (B3.10:
        // a minimum of 3 heats, a schedule rather than a setting),
        // bump-ups (none stated; B3.7 fixes the grid from qualifying), and
        // lower finals' legs (B3.11.1, the club's discretion).
        new(
            "nzrca-ep-offroad-10",
            "NZRCA",
            "EP off-road 1/10 (5-minute)",
            "NZRCA EP Off-Road Rules 2026, Version 29.0 (nzrca rules page): B3 Race Procedures (B3.5, B3.6, "
            + "B3.9, B3.11, B3.12) and B6 Race Duration (B6.1a). " + CheckRulebook,

            "Qualifiers: 5 minutes (B6.1a), staggered start with each driver on their own clock (B3.5). Finals: "
            + "5 minutes (B6.1a), grid start (B3.6, for national events). Up to 12 cars per final (B3.9). A-main "
            + "in 3 legs, best 2 count, 1 point for the winner (B3.11, B3.12).",

            "How qualifying is scored: the rules name the \"IFMAR qualifying points system\" (B3.5) but give no "
            + "scale, so the class's own method stays. The stagger GAP (the race director's or the scoring "
            + "program's, B3.5), the number of rounds (at least 3, B3.10), bump-ups (none stated) and legs for "
            + "the lower finals (the club's choice, B3.11.1) are not set. Ties: the rules' second tie-breaker "
            + "is laps and times from the best finish (B3.12); HeatCaller's is the last leg.",

            Qualifier: StaggeredOwnClock(5),
            Main: GridStart(5),
            MainSize: 12,
            AMainLegs: 3,
            AMainLegsCounted: 2),

        // NZRCA EP On-Road Rules 2026, Version 29.0 (res.cloudinary.com/
        // nzrca/.../EP Onroad Rules 2026.pdf):
        //   A6.1a-g  21.5t Touring, FWD Touring, Super Stock, Touring
        //            Modified, M-Chassis, Formula One and Pro 10 five
        //            minutes (Pro 12 is eight, so it is not in this one).
        //   A3.4     staggered start, individual clock.
        //   A3.5     finals at national events a grid start.
        //   A3.8     heats and finals a maximum of 12 cars.
        //   A3.10    1 point for the winner of each final, best 2 of 3.
        // Not set: qualifying scoring (A3.4 only says how heats are
        // CALLED - by fastest time so far - not how they are ranked), the
        // round count (A3.9, at least 3), bump-ups (none stated) and
        // lower-final legs. A6.1 also allows a lap count instead of a time.
        new(
            "nzrca-ep-onroad-5min",
            "NZRCA",
            "EP on-road 5-minute classes",
            "NZRCA EP On-Road Rules 2026, Version 29.0 (nzrca rules page): A3 Race Procedures (A3.4, A3.5, "
            + "A3.8, A3.10) and A6 Race Duration (A6.1 a-g: touring, FWD, Super Stock, M-Chassis, Formula One, "
            + "Pro 10 - not Pro 12, which is eight minutes). " + CheckRulebook,

            "Qualifiers: 5 minutes, staggered start with each driver on their own clock (A3.4, A6.1). Finals: "
            + "5 minutes, grid start (A3.5, for national events). Up to 12 cars per final (A3.8). A-main in 3 "
            + "legs, best 2 count, 1 point for the winner (A3.10).",

            "How qualifying is scored is not stated (A3.4 says how heats are called, not ranked), so the class's "
            + "own method stays. The stagger gap, the number of rounds (at least 3, A3.9), bump-ups (none stated) "
            + "and lower-final legs are not set. A6.1 also allows a set number of laps instead of a time. Ties: "
            + "the rules' second tie-breaker is laps and times from the best finish; HeatCaller's is the last leg.",

            Qualifier: StaggeredOwnClock(5),
            Main: GridStart(5),
            MainSize: 12,
            AMainLegs: 3,
            AMainLegsCounted: 2),

        // The Reedy International Touring Car Race of Champions (Team
        // Associated / Reedy), OPEN classes - the most recent published
        // format; reedyrace.com lists no event after 2020.
        //   reedyrace.com/2019/onroad/rules.html: "All qualifying heats, main
        //     events, and Invitational races are 5 minutes in length";
        //     "Main event results will be determined by adding a driver's
        //     best two results".
        //   reedyrace.com/2019/onroad/index.html (event 23-26 May 2019): "all
        //     main events run over three legs".
        // Not set: Open qualifying is "qual points with the best two rounds"
        // but no scale is given, so neither the method nor the count is
        // set; nor start types, main size or bump-ups (not stated); nor the
        // Invitational class's head-to-head format (best 10 of 12 rounds).
        // Main-event ties are broken on the best two results' combined laps
        // and time, where AMainLegs looks at the best finish first.
        new(
            "reedy-tc-2019-open",
            "Reedy Race",
            "Touring Car Race of Champions, Open classes (2019)",
            "Reedy International TC Race of Champions, 23-26 May 2019 - the most recent format published at "
            + "reedyrace.com, which lists no event after 2020: its Rules page (race lengths, main scoring) and Home "
            + "page (\"all main events run over three legs\"). Check against this year's published format before "
            + "the event.",

            "Qualifiers and mains: 5 minutes each. A-main in 3 legs, best 2 results count.",

            "Open qualifying is \"qual points with the best two rounds\", but the scale is not published, so the "
            + "class's own qualifying method and count stay. Start types, main size and bump-ups are not stated. "
            + "The Invitational class (best 10 of 12 head-to-head rounds) is not this. HeatCaller runs only the "
            + "A-main in legs, and breaks ties on the best finish where Reedy adds the best two results' laps and "
            + "time.",

            Qualifier: Length(5),
            Main: Length(5),
            AMainLegs: 3,
            AMainLegsCounted: 2),
    };

    /// <summary>The line every rulebook preset's source ends with.</summary>
    private const string CheckRulebook = "Check against your current rulebook before a sanctioned meeting.";

    /// <summary>A length in minutes and no lap limit - nothing about the start.</summary>
    private static Action<RaceFormat> Length(int minutes) => f =>
    {
        f.TimeLimitMinutes = minutes;
        f.LapLimit = 0;
    };

    /// <summary>A length and a grid start - everyone away together on one clock.</summary>
    private static Action<RaceFormat> GridStart(int minutes) => f =>
    {
        f.StartType = RaceStartType.Standard;
        f.TimeLimitMinutes = minutes;
        f.LapLimit = 0;
    };

    /// <summary>
    /// A length and a staggered start with each driver on their own clock
    /// from their own release. The GAP is left as the class has it - the
    /// rulebooks that ask for this leave it to the race director.
    /// </summary>
    private static Action<RaceFormat> StaggeredOwnClock(int minutes) => f =>
    {
        f.StartType = RaceStartType.StaggeredGrid;
        f.StaggerClockMode = StaggerClock.OwnRelease;
        f.TimeLimitMinutes = minutes;
        f.LapLimit = 0;
    };

    private static SanctioningPreset Roar(string key, string name, string row, int minutes) => new(
        key,
        "ROAR",
        name,
        $"ROAR Rules Ver. 26-10a, effective 16 Oct 2026 (roarracing.com, Rules): 8.1.4 race lengths ({row} row, "
        + "p. 55), 8.8.3 and 8.8.4 qualifying (p. 59), 8.9.1 electric starts (p. 59). 8.1.3: these lengths are "
        + "mandatory at Level 3-5 and optional below, if announced. " + CheckRulebook,

        $"Qualifiers and all mains: {minutes} minutes (8.1.4). Mains: full-field start (8.9.1). Qualifying: each "
        + "round placed on most laps then time (8.8.3), and the mains set from each driver's single fastest heat "
        + "(8.8.4, Level 1-4).",

        "The qualifier start - ROAR allows either a full-field or an IFMAR start (8.9.1). Main size and bump-ups "
        + "(no general rule for electric Level 1-4). The triple A-main (Level 5 nationals, 8.8.6 and 14.3.7). "
        + "Level 5's Qual-Points (14.3.6): it gives the top qualifier 0 points where HeatCaller's points per round "
        + "gives 1, which can swap two drivers' order, so it is not switched on.",

        Qualifier: Length(minutes),
        Main: GridStart(minutes),
        Method: QualifyingMethod.TotalLaps,
        CountBestRounds: 1);

    private static SanctioningPreset Efra(string key, string name, string classes, int minutes) => new(
        key,
        "EFRA",
        name,
        "EFRA Handbook 2026, Appendix 3 Electric Cars, v4 (efra.ws): 9.4.1 heat and final lengths "
        + $"({classes}, p. 101-102), 1.1.11 and 9.2.3 grid start for finals, 9.4.9 and 10.1 finals of 10, "
        + "10.3-10.5 three-leg finals, best 2 of 3, winner 1 point (p. 103). European Championship and Grand Prix "
        + "format. " + CheckRulebook,

        $"Qualifiers: {minutes} minutes. Finals: {minutes} minutes from a grid (F1-type) start. 10 drivers per "
        + "final. A-main in 3 legs, best 2 count, 1 point for the winner.",

        "Qualifying scoring - EFRA's Round by Round points (9.4.1) give the top qualifier 0 points where "
        + "HeatCaller's gives 1, and how many rounds count depends on how many were run, so the class's own "
        + "method stays. The qualifier start (staggered, called car by car, 9.2.2 - no gap given). Bump-ups (none "
        + "in the appendix). EFRA runs the lower finals in three legs too; HeatCaller runs only the A-main in "
        + "legs. Ties: EFRA's second tie-breaker is laps and times from the best finish (10.6); HeatCaller's is "
        + "the last leg.",

        Qualifier: Length(minutes),
        Main: GridStart(minutes),
        MainSize: 10,
        AMainLegs: 3,
        AMainLegsCounted: 2);

    public static SanctioningPreset? Find(string key) =>
        All.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}
