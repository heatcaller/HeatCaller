namespace HeatCaller;

/// <summary>
/// How big the race in front of the announcer is - and therefore how much
/// of the night's voice it is allowed to have.
///
/// WHY (11 Oct 2026)
///
/// The airtime cap was a flat six spoken segments per ten minutes, from the
/// first practice session of the afternoon to the last lap of the A-main.
/// That is not how a broadcast is paced. A programme that talks at the same
/// rate all evening teaches a room to stop listening, because nothing it
/// says is ever more important than anything else it said - and then the
/// one moment that mattered, the pass for the win on the last lap of the
/// final, arrives with the same six-an-hour shrug as heat three.
///
/// Real coverage is quiet early and loud late. It holds back through the
/// qualifiers, so that when the mains come round and the voices start
/// coming in over each other, the room looks up. The restraint is not a
/// limitation to be apologised for; it is the thing that makes the loud
/// part land.
///
/// WHAT THIS IS NOT
///
/// It is not the money. The budget already back-loads what a night may
/// SPEND: <see cref="CommentaryBudget.Entitled"/> releases the night's
/// allotment against how much of the programme has run, so the mains
/// already arrive with the most headroom. What was still flat was the
/// airtime cap - the "whatever the budget allows, no more than N in ten
/// minutes" rule - and a cap that never moves is what made every race
/// sound alike.
///
/// It is also not <see cref="ShowDrama"/>, which is about what a race
/// actually DID (a photo finish, a track record) and dresses the music
/// accordingly. This is about what a race IS, known before it starts.
///
/// WHERE IT COMES FROM
///
/// The programme, which the club typed in: a round's
/// <see cref="RoundKind"/> and, for a main, its
/// <see cref="RoundDefinition.MainTier"/>. Nothing is inferred from labels
/// and nothing is guessed - a club running freeform races with no
/// programme at all gets <see cref="Unknown"/>, which is the flat six the
/// app has always had. That is the important case: this feature must not
/// quietly take the voice away from a club that never set up a programme.
/// </summary>
/// <param name="Billing">What kind of race it is.</param>
/// <param name="LastOfTheNight">
/// It is the last race on tonight's programme. Worth its own flag rather
/// than folding into the billing: the last race of an evening is the one
/// the whole room has stayed for, whether it is an A-main or the only heat
/// a small club ran.
/// </param>
public readonly record struct RaceStature(RaceBilling Billing, bool LastOfTheNight)
{
    /// <summary>
    /// Nothing is known about the race - a freeform session, a club with
    /// no programme, or a tick before the round has been picked.
    ///
    /// DELIBERATELY THE OLD BEHAVIOUR, not the quietest band. A club that
    /// has never opened the Program tab has not asked for a quieter
    /// afternoon; it has simply not told the app anything, and taking its
    /// announcer down to three lines in ten minutes because of that would
    /// be this feature breaking the app for the clubs least able to
    /// explain why.
    /// </summary>
    internal static readonly RaceStature Unknown = new(RaceBilling.Unknown, false);

    /// <summary>
    /// How many spoken segments a ten-minute stretch may hold for a race
    /// of this stature - what <see cref="CommentaryBudget.AirtimeCap"/>
    /// used to be for every race alike.
    ///
    /// THE SHAPE OF THESE NUMBERS IS THE FEATURE. Practice is background
    /// and gets least; the qualifiers come DOWN from what they had, which
    /// is the holding-back half and the half that costs something; the
    /// lesser mains sit where everything used to; and the top main gets
    /// half again, which is where all of it is spent.
    ///
    /// The cap is a ceiling, never a target. A quiet club with a small
    /// allowance will not reach four in a heat, let alone nine in a main -
    /// the money gates are all still in front of this one, and nothing here
    /// makes anything be said. It only decides what is allowed to be.
    /// </summary>
    internal int AirtimeCap
    {
        get
        {
            var cap = Billing switch
            {
                RaceBilling.Practice => 3,
                RaceBilling.Heat => 4,
                RaceBilling.LesserMain => 6,
                RaceBilling.TopMain => 9,

                // Unknown, and anything a later version adds that this
                // build has not heard of.
                _ => CommentaryBudget.AirtimeCap,
            };

            // THE LAST RACE OF THE NIGHT. Everyone who is still in the hall
            // is watching this one, and after it the room goes home - so
            // there is no later race for the airtime to be saved up for.
            // Two rather than one because it has to be audible: a club
            // whose last race is a heat should hear the difference.
            return LastOfTheNight ? cap + 2 : cap;
        }
    }

    /// <summary>
    /// Whether a race of this stature is worth two voices.
    ///
    /// The second voice is the most expensive thing the show does - one
    /// request, but a longer one, and the words of two people instead of
    /// one - and it is also the loudest signal the broadcast has. Spending
    /// it on every heat is how it stops meaning anything. A room that hears
    /// the second voice only in the mains learns, without ever being told,
    /// that two voices means this one counts.
    ///
    /// Unknown is true, for the reason <see cref="Unknown"/> gives: a club
    /// with no programme keeps every bit of the show it already had.
    /// </summary>
    internal bool WorthTwoVoices =>
        LastOfTheNight
        || Billing is RaceBilling.TopMain or RaceBilling.LesserMain or RaceBilling.Unknown;

    /// <summary>
    /// What this is, for the console - so a quiet heat is explainable
    /// afterwards rather than looking like a fault. The log line a club
    /// reads at midnight is the whole reason any of this is sayable.
    /// </summary>
    internal string Describe => Billing switch
    {
        RaceBilling.Practice => LastOfTheNight ? "practice, and the last on the program" : "practice",
        RaceBilling.Heat => LastOfTheNight ? "a heat, and the last on the program" : "a heat",
        RaceBilling.LesserMain => LastOfTheNight ? "a main, and the last on the program" : "a main",
        RaceBilling.TopMain => LastOfTheNight ? "the top main, and the last on the program" : "the top main",
        _ => "a race with nothing on the program to size it by",
    };

    /// <summary>
    /// What the round in front of the announcer is worth, read off the
    /// club's own programme.
    ///
    /// <paramref name="round"/> null - a freeform race, or no round picked
    /// yet - is <see cref="Unknown"/>. So is a programme that cannot be
    /// read: this is asked on a half-second tick while a club edits the
    /// Program tab beside it, and a store caught mid-save must cost the
    /// night nothing worse than a flat cap.
    ///
    /// <paramref name="withinRoundIds"/> is the open race day's rounds
    /// (RaceEventStore.ActiveRoundScope), or null for every round. The
    /// round store holds every meeting's rounds, so without it a round set
    /// up for tomorrow, or a stale one from an old meeting, could be "last".
    /// </summary>
    internal static RaceStature Of(
        RoundDefinition? round,
        RoundDefinitionStore? programme,
        IReadOnlySet<string>? withinRoundIds = null)
    {
        if (round is null)
        {
            return Unknown;
        }

        var billing = round.Kind switch
        {
            RoundKind.Practice => RaceBilling.Practice,
            RoundKind.Qualifier => RaceBilling.Heat,

            // A main with no tier is an untiered main - a club that runs
            // one final and never set a tier on it - and that is the top
            // main of its class, not a lesser one. Tier 0 is the A-main.
            RoundKind.Main => round.MainTier <= 0 ? RaceBilling.TopMain : RaceBilling.LesserMain,

            _ => RaceBilling.Unknown,
        };

        return new RaceStature(billing, IsLastOfTheNight(round, programme, withinRoundIds));
    }

    /// <summary>
    /// Whether this round is the last RACE on tonight's programme.
    ///
    /// Practice is left out of the reckoning on both sides: a club that
    /// puts an open practice session at the end of the evening has not made
    /// that session the climax of the night, and a club whose last entry is
    /// practice should still have its final main treated as the finale.
    /// </summary>
    private static bool IsLastOfTheNight(
        RoundDefinition round,
        RoundDefinitionStore? programme,
        IReadOnlySet<string>? withinRoundIds)
    {
        if (programme is null || round.Kind == RoundKind.Practice)
        {
            return false;
        }

        try
        {
            var last = programme.All
                .Where(one => one.Kind != RoundKind.Practice
                              && (withinRoundIds is null || withinRoundIds.Contains(one.Id)))
                .OrderBy(one => one.Order)
                .LastOrDefault();

            return last is not null && string.Equals(last.Id, round.Id, StringComparison.Ordinal);
        }
        catch
        {
            // Read on a tick while the Program tab is being edited beside
            // it. A store caught mid-save costs a flat cap, never the show.
            return false;
        }
    }
}

/// <summary>
/// What kind of race it is, biggest last - see <see cref="RaceStature"/>.
///
/// <see cref="Unknown"/> is FIRST and is the default value of the enum on
/// purpose: a stature nobody filled in has to behave as the app did before
/// any of this existed, and a default of Practice would have silenced
/// every club with no programme.
/// </summary>
public enum RaceBilling
{
    /// <summary>Nothing on the programme to size this race by.</summary>
    Unknown,

    /// <summary>A practice session - background, and the quietest the show ever is.</summary>
    Practice,

    /// <summary>A heat or qualifier. Held back, so the mains have somewhere to go.</summary>
    Heat,

    /// <summary>A B-main, C-main or below.</summary>
    LesserMain,

    /// <summary>The A-main, or a club's single untiered final. Everything the night held back is spent here.</summary>
    TopMain,
}
