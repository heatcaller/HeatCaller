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
    internal sealed record Level(string Key, string Name, string Blurb, double Scale, bool TwoVoicesBelowTheMains);

    /// <summary>
    /// The level a club is on until it says otherwise - the pacing exactly
    /// as it shipped, so an existing club hears no change at all.
    /// </summary>
    internal static readonly Level Standard = new(
        "standard",
        "Standard",
        "The pacing as it comes: quiet through the heats, everything saved for the mains.",
        1.0,
        false);

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
            false),

        new Level(
            "measured",
            "Measured",
            "A little under the standard pacing. Colour in the mains, almost none in the heats.",
            0.75,
            false),

        Standard,

        new Level(
            "lively",
            "Lively",
            "Half again, and the two voices come out in the heats as well as the mains.",
            1.5,
            true),

        new Level(
            "nonstop",
            "Non-stop",
            "As much as the month's allowance will carry. For a big meeting with a crowd in.",
            2.0,
            true),
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

        return $"{level.Blurb} Up to {heat} spoken {(heat == 1 ? "segment" : "segments")} in ten minutes in a heat "
             + $"and {main} in the top main"
             + (level.TwoVoicesBelowTheMains
                 ? ", with the co-host out in the heats too."
                 : ", with the co-host held for the mains.")
             + " Intros and recaps are never held back, whatever this says, and the month's allowance still decides "
             + "what is actually spent.";
    }
}
