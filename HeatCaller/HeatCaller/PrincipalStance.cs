namespace HeatCaller;

/// <summary>
/// What the Team Principal is LIKE tonight - and unlike
/// <see cref="CoHostStance"/>, it is not a club's choice.
///
/// WHY THIS EXISTS (18 Oct 2026)
///
/// The principal shipped with a brief telling him to be "measured and
/// reflective", and measured and reflective is what came back: two
/// paragraphs of corporate nothing, the same two whether the team had won
/// the main or put both cars in the fence. A guest with no position is a
/// guest reciting, and recitation is the thing that makes a room talk over
/// a segment.
///
/// Every real principal interview that is worth watching is worth watching
/// because the man has a position before the first question lands. He is
/// vindicated, or he is defending something, or he is quietly furious, or
/// he is being blunt because the result leaves nothing to dress up. The
/// words change, but what makes it listenable is that he came in with
/// something to protect.
///
/// AND IT IS PICKED FROM THE RESULT, NOT FROM A SETTING
///
/// CoHostStance is a club's choice because a co-host's character is
/// constant - a club picks a sceptic and gets a sceptic every week. A
/// principal's is the opposite: the whole point is that it moves with the
/// night. A club that picked "defensive" would get a man defending a win,
/// which is worse than the corporate nothing it replaced.
///
/// So the stance is derived, every time, from facts the app already has,
/// and <see cref="For"/> is a pure function of them. That also means it
/// can be tested without a model, which is most of why the thresholds
/// below are honest.
///
/// WHAT A STANCE IS NOT
///
/// It is not a licence to invent, and the brief's last words are still
/// that the facts are the facts. A vindicated principal does not get to
/// claim he predicted it; he gets to sound like a man whose week has been
/// justified by numbers that are in front of him.
///
/// And it is not a licence to throw his own driver under the bus. See
/// <see cref="NeverHisOwnDriver"/>, which goes out with every stance here
/// and is not optional. It is the single rule that makes a frustrated
/// principal safe to put on a PA in a room of forty people who all know
/// each other.
/// </summary>
internal static class PrincipalStance
{
    /// <summary>
    /// One position the principal can come in with.
    /// </summary>
    /// <param name="Key">What this stance is called in the log, so a club reading the console can see which one fired. Never spoken.</param>
    /// <param name="Name">How the log names it, in a club's words.</param>
    /// <param name="Brief">What the model is told, in the third person. One or two sentences; any longer and it crowds out the facts.</param>
    internal sealed record Stance(string Key, string Name, string Brief);

    /// <summary>
    /// The sentence that goes out with EVERY stance, including the two
    /// unhappy ones.
    ///
    /// THE RULE THAT MAKES ALL OF THIS SAFE, and it is a borrowed one:
    /// CoHostStance.NeverCruel exists because a club of forty people all
    /// know each other and a driver hearing the PA take a dig at him in
    /// front of his kids is the fastest way for a club to switch the
    /// announcer off. A principal is a worse risk than a co-host, because
    /// a principal has somebody specific to blame and a real one would do
    /// it.
    ///
    /// So he does not. He is loyal on the record, always - which happens
    /// to be exactly what makes the segment good. A principal who will not
    /// say his driver threw it away has to say something ELSE, and the
    /// something else is the team's: we did not give him a car he could
    /// trust in traffic. Everybody in the room knows that is a dodge, and
    /// a dodge everybody can see is far more interesting than either a
    /// compliment or a complaint.
    /// </summary>
    internal const string NeverHisOwnDriver =
        "Whatever he is feeling, the team principal never criticizes his own driver on the record: no blaming "
        + "them for a mistake, no doubting their ability, nothing a driver would mind hearing over a PA with "
        + "their family in the room. Where the night went wrong he takes it on the team - what they gave the "
        + "driver to work with, what they got wrong, what they will put right - and never on the person "
        + "driving. He is never unkind about another club's driver either.";

    /// <summary>
    /// A principal with nothing in particular to protect - what a club got
    /// before this file, and what an ordinary night still gets.
    ///
    /// Its Brief is EMPTY rather than a sentence asking for even-handedness,
    /// for the reason CoHostStance.EvenHanded gives: an instruction to have
    /// no position is still an instruction, and it spends words in a brief
    /// telling the model to do what it already does.
    /// </summary>
    internal static readonly Stance Plain = new(string.Empty, "Plain", string.Empty);

    /// <summary>
    /// A night that went better than the team had any right to expect.
    ///
    /// The one stance where the principal is allowed to be pleased with
    /// himself, and it has the highest bar of the five for that reason: a
    /// principal who sounds vindicated every time his driver scrapes a
    /// third is a principal nobody believes when it is real.
    /// </summary>
    internal static readonly Stance Vindicated = new(
        "vindicated",
        "Vindicated",
        "The team principal comes in vindicated - a night that has justified what the team has been doing, and "
        + "he lets that show rather than playing it down. He is generous about the people who did the work and "
        + "careful not to gloat about anyone they beat. He never claims he predicted it.");

    /// <summary>
    /// A night the team has to answer for: a retirement, or a result well
    /// below where the table says they belong.
    ///
    /// THE MOST INTERESTING OF THE FIVE and the most dangerous, which is
    /// why the loyalty rule is loudest here. A principal defending a bad
    /// night in public is the archetype of the whole segment - and the one
    /// most likely, without
    /// <see cref="NeverHisOwnDriver"/>, to defend the team by naming
    /// whoever he thinks is at fault.
    /// </summary>
    internal static readonly Stance Defensive = new(
        "defensive",
        "Defensive",
        "The team principal comes in with something to defend - a night that has not gone the way it should "
        + "have. He does not pretend otherwise and he does not catastrophize either: he accounts for it as the "
        + "team's to put right, and he deflects the sharpest question rather than answering it squarely. A "
        + "listener should be able to hear him declining to blame anybody.");

    /// <summary>
    /// A night where the pace was there and the result was not - the
    /// specific frustration a principal has and nobody else in the
    /// broadcast does.
    ///
    /// Needs <see cref="PrincipalFacts.WhereThePaceWent"/> to have found
    /// something, because that is the only evidence this stance is about.
    /// Frustration with no figure behind it is a mood the app invented
    /// about a driver's evening.
    /// </summary>
    internal static readonly Stance Frustrated = new(
        "frustrated",
        "Frustrated but loyal",
        "The team principal is quietly frustrated: the pace was there tonight and the result does not show it, "
        + "and that gap is what he keeps coming back to. The frustration is with the situation and with his own "
        + "side of it - never with the driver, whom he backs without being asked to.");

    /// <summary>
    /// A solid night with nothing to argue about - a podium, or a
    /// workmanlike evening in the points.
    /// </summary>
    internal static readonly Stance Pleased = new(
        "pleased",
        "Cautiously pleased",
        "The team principal is pleased without getting carried away - a night that did the job. He is more "
        + "interested in what it sets up than in celebrating it, and he resists being talked into a bigger "
        + "claim than the facts support.");

    /// <summary>
    /// A night with nothing to dress up. The short answers, which are
    /// their own kind of good radio.
    /// </summary>
    internal static readonly Stance Blunt = new(
        "blunt",
        "Blunt",
        "The team principal is blunt tonight - short answers, nothing dressed up, no search for a silver "
        + "lining that is not there. He is not rude and he is not sulking; there is simply not much to say "
        + "about a night like this one and he does not pad it.");

    /// <summary>
    /// Every stance, for the console and for the tests. In the order
    /// <see cref="For"/> considers them.
    /// </summary>
    internal static readonly IReadOnlyList<Stance> All = new[]
    {
        Vindicated, Defensive, Frustrated, Pleased, Blunt, Plain,
    };

    /// <summary>
    /// How far below their championship position a night has to finish
    /// before the principal has something to defend.
    ///
    /// Three places. Two is a bad heat and a room knows it; three is the
    /// point where a driver who is third in the table finishing sixth has
    /// had the kind of night somebody asks about.
    /// </summary>
    internal const int PlacesBelowToDefend = 3;

    /// <summary>
    /// How far ABOVE their championship position a night has to finish
    /// before he is vindicated by it.
    ///
    /// Also three, and deliberately symmetrical: the two stances are the
    /// same measurement in opposite directions, and a different bar for
    /// each would mean a club heard "vindicated" more often than
    /// "defensive" for reasons nobody could point at.
    /// </summary>
    internal const int PlacesAboveToClaim = 3;

    /// <summary>
    /// The position the principal comes in with, worked out from the night.
    ///
    /// THE ORDER IS THE WHOLE FUNCTION. Several of these are true at once
    /// on a real night - a driver can win the main having retired from a
    /// heat, with the pace figures saying he left time out there - and the
    /// question is not which is true but which a principal would actually
    /// LEAD with. So:
    ///
    ///   A RETIREMENT FIRST, because it is the thing the room watched
    ///   happen and the thing the host is going to ask about whatever else
    ///   is on the sheet. A principal who opened on the championship with
    ///   a car still in the fence would be a principal nobody listens to.
    ///
    ///   THEN A NIGHT WELL ABOVE OR BELOW THE TABLE, because that is the
    ///   night being out of the ordinary, which is what a stance is for.
    ///
    ///   THEN THE PACE GAP, which is the subtlest of the five and would
    ///   never win against a result that spoke for itself.
    ///
    ///   THEN A PODIUM, then nothing. The last two are the ordinary
    ///   evenings, and the ordinary evening getting the plain treatment is
    ///   correct rather than a gap: a stance on every night is a stance on
    ///   no night.
    ///
    /// NOTHING IN HERE IS ABOUT THE DRIVER. Every branch reads a count or
    /// a position off a record the club saved. There is no "he looked
    /// rattled", because nothing on this laptop knows that and a mood
    /// read out over a PA about somebody standing in the room is the one
    /// kind of colour that cannot be invented.
    /// </summary>
    /// <param name="night">The driver's evening. See <see cref="TeamNight"/>.</param>
    /// <param name="bestFinishTonight">
    /// Their best finish of the night, 1-based, or 0 when they finished
    /// nothing. Passed in rather than read off
    /// <see cref="TeamNight.Finishes"/> so a caller holding a corrected
    /// result does not have to rebuild the list to get this right.
    /// </param>
    /// <param name="championshipPosition">
    /// Where they stand in the season, or null when there is no
    /// championship or they are not in it. Null skips the two stances that
    /// compare the night with the table - a club with no season still gets
    /// the other three.
    /// </param>
    internal static Stance For(TeamNight night, int bestFinishTonight, int? championshipPosition)
    {
        // A CAR THAT DID NOT FINISH. Checked first and on its own, before
        // anything is compared with the table: see the ordering note above.
        if (night.Retirements > 0)
        {
            return Defensive;
        }

        if (championshipPosition is > 0 && bestFinishTonight > 0)
        {
            var standing = championshipPosition.Value;

            if (bestFinishTonight <= standing - PlacesAboveToClaim)
            {
                return Vindicated;
            }

            if (bestFinishTonight >= standing + PlacesBelowToDefend)
            {
                return Defensive;
            }
        }

        // THE PACE GAP, and only where the figures support one - the same
        // evidence PrincipalFacts.WhereThePaceWent needs, asked the same
        // way, so the stance and the fact cannot contradict each other on
        // air. A principal sounding frustrated about a gap the facts do
        // not mention is a principal frustrated about nothing.
        if (night.PaceIsReadable
            && night.PotentialGainMs >= PrincipalFacts.LeastCostWorthSaying
            && bestFinishTonight is 0 or > 3)
        {
            return Frustrated;
        }

        if (night.Wins > 0 || night.Podiums > 0)
        {
            return Pleased;
        }

        // A NIGHT WITH NOTHING IN IT EITHER WAY gets blunt rather than
        // plain, as long as there was a night at all. Blunt IS the honest
        // position on a mid-pack evening, and it is the one stance that
        // makes a segment shorter rather than longer.
        return night.RacesRun >= PrincipalFacts.LeastRacesForANight ? Blunt : Plain;
    }

    /// <summary>
    /// What goes into the interview brief for a stance - its own sentences
    /// and the loyalty rule, or NOTHING at all for the plain principal.
    ///
    /// Nothing, for the plain one, for the reason CoHostStance.BriefFor
    /// gives: a brief is a budget, and every sentence in it is a sentence
    /// the model reads instead of a fact. The loyalty rule rides with the
    /// stance because it is the stance that introduces any risk of an edge
    /// in the first place - a principal with no position has nobody to
    /// blame.
    /// </summary>
    internal static string BriefFor(Stance stance) =>
        stance.Brief.Length == 0 ? string.Empty : stance.Brief + " " + NeverHisOwnDriver;
}
