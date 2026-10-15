namespace HeatCaller;

/// <summary>
/// How much the club wants the announcer to talk.
///
/// WHY THIS SITS ON TOP OF <see cref="RaceStature"/> RATHER THAN REPLACING
/// IT (12 Oct 2026)
///
/// The pacing built the night before this gives an evening a SHAPE: quiet
/// through practice and the heats, everything it held back spent on the top
/// main. That shape is right for every club - it is what stops a room
/// learning to tune the announcer out - but the LEVEL it sits at is a
/// matter of taste, and taste is not something this app gets to decide.
///
/// A club with forty drivers, a good PA and a crowd that came to watch
/// wants more than four lines in a heat. A club of eight mates on a
/// Tuesday wants the announcer to say the result and get out of the way.
/// Both of those are the same SHAPE at a different volume.
///
/// So the stature decides the shape and this scales it. One dial, five
/// stops, and the quiet-early-loud-late curve is preserved at every one of
/// them: Non-stop in a heat is still quieter than Non-stop in the A-main.
///
/// WHAT IT CANNOT DO
///
/// It cannot silence a race. Intros and recaps are Essential and are never
/// refused for airtime at all - see CommentaryBudget.Judge, which lets an
/// essential past before this is even looked at. A club on Sparing still
/// hears every race introduced and every result called; what it loses is
/// the colour around them.
///
/// And it cannot make a club talk more than it can afford. Every money
/// gate sits in FRONT of the airtime ceiling, so Non-stop at a club with a
/// small monthly allowance buys very little: the ceiling goes up and the
/// headroom is what actually decides. That is worth saying plainly on the
/// page, because a club that turns this up and hears no difference will
/// otherwise conclude the setting does nothing.
///
/// AND THE LAP CALLS, WHICH IT DID NOT REACH UNTIL NOW (14 Oct 2026)
///
/// Reported from the floor: the dial was turned to its quietest stop
/// mid-race and the announcer "still won't shut up". It was working - the
/// key is pushed to the director on the tick and the ceiling is recomputed
/// at every segment - and it was working on the wrong thing. It governed
/// the AI commentary segments and nothing else, while the noise was the
/// per-event calls off the timing loop: every lap, every personal best,
/// every lead change, each one a tick box on the Voice page and none of
/// them aware this dial existed.
///
/// A control a club reads as "how chatty is the announcer" that leaves the
/// chattiest thing in the app untouched is a control that lies, so it now
/// reaches both: <see cref="ScaledPerLap"/> scales the club's own per-lap
/// cap, and <see cref="CallIsWorthIt"/> holds the lightest calls at the
/// quiet stops.
///
/// IT STILL CANNOT TOUCH AN INSTRUCTION. A flag, a countdown, a green, a
/// grid call, a driver's release, the finish - those are not chatter, they
/// are the race being run, and a dial about taste must not be able to
/// swallow one. See CallIsWorthIt, which exempts them by asking the
/// queue's own priority list rather than keeping a second one.
/// </summary>
internal static class ShowChatter
{
    /// <summary>
    /// One stop on the dial.
    /// </summary>
    /// <param name="Key">What is stored in settings. Never changes once shipped, so the wording below can.</param>
    /// <param name="Name">What the club picks it by on the Commentary page.</param>
    /// <param name="Blurb">One line, in a club's words rather than a writer's.</param>
    /// <param name="Scale">
    /// What the race's own ceiling is multiplied by. Rounded rather than
    /// truncated where it lands between two whole lines, so three quarters
    /// of nine is seven and not six.
    /// </param>
    /// <param name="TwoVoicesBelowTheMains">
    /// Whether the second voice is allowed outside the mains. The default
    /// pacing holds it for them, because a signal spent on everything is
    /// not a signal - but a club that wants a chatty evening means this as
    /// much as it means the count.
    /// </param>
    /// <param name="PerLapScale">
    /// What the club's "Max spoken announcements per lap" is multiplied by
    /// - see <see cref="ScaledPerLap"/>.
    ///
    /// The same number as <paramref name="Scale"/> at every stop, and
    /// written out separately rather than reused, because the two are only
    /// coincidentally equal: one scales a ten-minute ceiling on written
    /// segments and the other a per-crossing cap on timing calls. A future
    /// stop that wanted to be quiet about laps and generous about segments
    /// should not have to be modelled as two dials.
    /// </param>
    /// <param name="QuietestCall">
    /// The lightest per-event call this stop will say, as a Weight off the
    /// Voice page - see <see cref="CallIsWorthIt"/>. Nought holds nothing
    /// back, which is every stop from Standard up.
    /// </param>
    internal sealed record Level(
        string Key,
        string Name,
        string Blurb,
        double Scale,
        bool TwoVoicesBelowTheMains,
        double PerLapScale = 1.0,
        int QuietestCall = 0);

    /// <summary>
    /// The level a club is on until it says otherwise - the pacing exactly
    /// as it shipped, so an existing club hears no change at all.
    /// </summary>
    internal static readonly Level Standard = new(
        "standard",
        "Standard",
        "The pacing as it comes: quiet through the heats, everything saved for the mains.",
        1.0,
        false,
        1.0,
        0);

    /// <summary>
    /// The five stops, quietest first - which is also the order they are
    /// offered in, so the dial reads left to right like a volume control.
    /// </summary>
    internal static readonly IReadOnlyList<Level> All = new[]
    {
        new Level(
            "sparing",
            "Sparing",
            "Barely there. The result, and little else - for a club that wants the racing to do the talking.",
            0.5,
            false,
            0.5,
            // Keeps the calls a room turns its head for - the race's
            // fastest lap, a record, a change of lead, the last lap, the
            // final minute, provisional pole - and holds the rest: every
            // lap completed, every lap time, every personal best, every
            // streak, close battles, lappings, the running order. Those
            // are the ones that fire on a crossing and so fire constantly.
            85),

        new Level(
            "measured",
            "Measured",
            "A little under the standard pacing. Colour in the mains, almost none in the heats.",
            0.75,
            false,
            0.75,
            // Only the ones that fire for EVERY car on EVERY lap - the lap
            // count, the lap time, personal bests, a qualifying
            // improvement - plus the running order, which the Voice page's
            // own note calls the most intrusive line in the set.
            60),

        Standard,

        new Level(
            "lively",
            "Lively",
            "Half again, and the two voices come out in the heats as well as the mains.",
            1.5,
            true,
            1.5,
            0),

        new Level(
            "nonstop",
            "Non-stop",
            "As much as the month's allowance will carry. For a big meeting with a crowd in.",
            2.0,
            true,
            2.0,
            0),
    };

    /// <summary>
    /// The hard ceiling on a scaled cap, whatever the dial says.
    ///
    /// Twenty spoken segments in ten minutes is one every thirty seconds
    /// with no gap for a race in between, and past that the number has
    /// stopped meaning anything - the minimum gap between segments and the
    /// month's allowance are both biting long before. It is here so that
    /// "Non-stop" cannot be read by a future stature as a number nobody
    /// sanity-checked.
    /// </summary>
    internal const int Most = 20;

    /// <summary>
    /// And the floor. One, because a ceiling of zero is not a quiet
    /// announcer, it is a broken one - a club that wants silence has a
    /// switch for that, and every tick box on the Commentary page.
    /// </summary>
    internal const int Least = 1;

    /// <summary>
    /// The level a stored key means, or <see cref="Standard"/> when it is
    /// blank, unknown, or from a newer version of the app.
    ///
    /// Standard for an unknown key rather than a guess at the nearest, for
    /// the reason <see cref="CoHostStance.For"/> gives: a club whose
    /// settings came back from a backup written by a later build gets the
    /// pacing it has always had, not a volume nobody in the room chose.
    /// </summary>
    internal static Level For(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Standard;
        }

        var wanted = key.Trim();

        foreach (var level in All)
        {
            if (string.Equals(level.Key, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return level;
            }
        }

        return Standard;
    }

    /// <summary>
    /// A race's own ceiling, scaled by the dial and held inside
    /// <see cref="Least"/> and <see cref="Most"/>.
    ///
    /// Rounded away from zero at the half, so a dial that halves a
    /// three-line practice session leaves two rather than one: the
    /// quietest stop is meant to be sparing, not mute.
    /// </summary>
    internal static int Scaled(int cap, string? key)
    {
        var scaled = (int)Math.Round(cap * For(key).Scale, MidpointRounding.AwayFromZero);

        return Math.Clamp(scaled, Least, Most);
    }

    /// <summary>
    /// Whether this level lets the second voice out below the mains.
    /// </summary>
    internal static bool TwoVoicesEverywhere(string? key) => For(key).TwoVoicesBelowTheMains;

    /// <summary>The fewest per-lap calls a crossing may make, and the most - the Voice page's own range.</summary>
    internal const int LeastPerLap = 1;

    internal const int MostPerLap = 5;

    /// <summary>
    /// The club's "Max spoken announcements per lap" with the dial applied
    /// - see VoiceProfile.MaxAnnouncementsPerLap, which is the shape this
    /// scales.
    ///
    /// The club's own number stays the shape and this is the volume, which
    /// is the same arrangement <see cref="Scaled"/> has with the race's
    /// airtime cap, and it matters for the same reason: a club that set
    /// this to 1 on purpose is not made louder by moving to Non-stop
    /// past the point it asked for, and a club on 5 keeps the room it
    /// asked for at Standard.
    ///
    /// Held inside the Voice page's own 1-5, so the dial can never put the
    /// cap somewhere the page could not show.
    /// </summary>
    internal static int ScaledPerLap(int cap, string? key)
    {
        var scaled = (int)Math.Round(
            Math.Clamp(cap, LeastPerLap, MostPerLap) * For(key).PerLapScale,
            MidpointRounding.AwayFromZero);

        return Math.Clamp(scaled, LeastPerLap, MostPerLap);
    }

    /// <summary>
    /// Whether a per-event call is worth saying at this stop, from its
    /// Weight on the Voice page.
    /// </summary>
    /// <param name="alwaysSaid">
    /// True for a call that is an INSTRUCTION rather than colour - a flag,
    /// a countdown, a green, the finish, a driver's release, a grid call.
    /// Those are never held, whatever the dial says, and the caller decides
    /// which they are by asking the voice queue's own priority list: a
    /// second list kept here would be the one that went stale.
    /// </param>
    internal static bool CallIsWorthIt(int weight, string? key, bool alwaysSaid)
    {
        if (alwaysSaid)
        {
            return true;
        }

        var floor = For(key).QuietestCall;

        return floor <= 0 || weight >= floor;
    }

    /// <summary>
    /// What this stop holds back out of the per-event calls, in the words
    /// the Voice page uses for them - or empty at a stop that holds none.
    ///
    /// Worked out from the real table rather than written down, like the
    /// figures in <see cref="Describe"/> and for the same reason: a club
    /// reading "holds the lap-by-lap calls" has to be able to trust that
    /// the list is the one the code uses.
    /// </summary>
    internal static IReadOnlyList<string> CallsHeldBack(string? key)
    {
        var floor = For(key).QuietestCall;

        if (floor <= 0)
        {
            return Array.Empty<string>();
        }

        var held = new List<string>();

        // The shipped weights, not the club's own file: this line is on the
        // Commentary page describing what the stop MEANS, and a club that
        // had re-weighted one call would otherwise read a different list
        // from its neighbour. What actually gates a call at run time is
        // CallIsWorthIt against that club's real config.
        var profile = VoiceProfile.CreateDefault();

        foreach (var call in Enum.GetValues<AnnouncementEventType>())
        {
            if (call == AnnouncementEventType.Reminder)
            {
                continue;
            }

            var config = profile.Events.GetValueOrDefault(call.ToString());

            if (config is null || CallIsWorthIt(config.Weight, key, alwaysSaid: false))
            {
                continue;
            }

            held.Add(VoiceProfile.Describe(call).Label);
        }

        return held;
    }

    /// <summary>
    /// How the Commentary page says what is set - the stop's own words, and
    /// then what it actually buys in whole spoken segments, which is the
    /// part a club can hear.
    ///
    /// The figures are worked out from the real thing rather than written
    /// down, so a change to either table cannot leave this line lying.
    /// </summary>
    internal static string Describe(string? key)
    {
        var level = For(key);

        var heat = Scaled(new RaceStature(RaceBilling.Heat, false).AirtimeCap, key);
        var main = Scaled(new RaceStature(RaceBilling.TopMain, false).AirtimeCap, key);

        // AND WHAT IT DOES TO THE LAP CALLS (14 Oct 2026), which is the
        // half a club was never told about - see the note at the top of
        // this class for the night that cost. Named rather than counted:
        // "holds back Every lap completed, Lap time, ..." is the sentence
        // that would have answered the question from the floor.
        var perLap = ScaledPerLap(new VoiceProfile().MaxAnnouncementsPerLap, key);

        var held = CallsHeldBack(key);

        return $"{level.Blurb} Up to {heat} spoken {(heat == 1 ? "segment" : "segments")} in ten minutes in a heat "
             + $"and {main} in the top main"
             + (level.TwoVoicesBelowTheMains
                 ? ", with the co-host out in the heats too."
                 : ", with the co-host held for the mains.")
             + $" On the timing calls it allows {perLap} per lap"
             + (held.Count > 0
                 ? $" and holds back {Plural.Of(held.Count, "call")} outright: {string.Join(", ", held)}."
                 : " and holds none of them back.")
             + " Intros, recaps, flags, countdowns, grid calls and driver releases are never held back, whatever "
             + "this says, and the month's allowance still decides what is actually spent.";
    }
}
