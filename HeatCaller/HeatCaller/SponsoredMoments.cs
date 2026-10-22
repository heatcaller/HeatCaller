namespace HeatCaller;

/// <summary>
/// A moment of the race a sponsor can put their name to - "the fastest lap,
/// presented by Joe's Hobby Shop".
///
/// Saved BY NAME on a sponsor's booking (SponsorBooking.Presents), so a
/// member added here later cannot quietly re-label a club's deal. Add new
/// ones at the end anyway, for anybody reading a saved file by eye.
/// </summary>
public enum SponsoredMoment
{
    /// <summary>A new fastest lap of the race - the call, and the fastest-lap graphic.</summary>
    FastestLap,

    /// <summary>The lead changing hands - the call, and the lead change banner.</summary>
    LeadChange,

    /// <summary>A close battle - the call, and the battle box on the stream.</summary>
    Battle,

    /// <summary>The driver introductions before a race.</summary>
    DriverIntros,

    /// <summary>The qualifying recap before a main.</summary>
    QualifyingRecap,

    /// <summary>The running order read out during a race - the "run-through".</summary>
    RunThrough,

    /// <summary>Achievements unlocked after a race - first wins, hundredth races, hat tricks.</summary>
    Milestones,

    /// <summary>The race result - the winner's call at the flag, and the podium graphic.</summary>
    RaceResult,
}

/// <summary>
/// Sponsored segments: a sponsor's name on one moment of the broadcast,
/// the way NASCAR sells "the Busch Light pole award" - said with the call,
/// shown on the graphic, and counted for the sponsor report.
///
/// WHY IT IS A SEPARATE THING FROM THE PAID READS
///
/// A paid read (SponsorReads) is the sponsor's own copy, read in a gap the
/// broadcast makes for it. A sponsored moment is the opposite: the race
/// makes the moment and the sponsor's name rides on it, one sentence long.
/// That is what makes it sellable at a club - it costs the night no airtime
/// - and also why it has to be rationed: a fastest lap can fall six times in
/// the first three minutes of a race, and "presented by" six times is the
/// sponsor's name turning into noise. See <see cref="SponsoredMomentGate"/>.
///
/// ONE SPONSOR PER MOMENT, the first in the club's list who has it ticked
/// and is in tonight's rotation. A club that ticks one moment for two
/// sponsors has made a mistake the Sponsors page points out; it is not a
/// split the voice should try to say.
/// </summary>
internal static class SponsoredMoments
{
    /// <summary>What the Sponsors page calls each moment.</summary>
    public static string Label(SponsoredMoment moment) => moment switch
    {
        SponsoredMoment.FastestLap => "Fastest lap",
        SponsoredMoment.LeadChange => "Lead changes",
        SponsoredMoment.Battle => "Battles",
        SponsoredMoment.DriverIntros => "Driver introductions",
        SponsoredMoment.QualifyingRecap => "Qualifying recap",
        SponsoredMoment.RunThrough => "Running order (run-through)",
        SponsoredMoment.Milestones => "Milestones and achievements",
        SponsoredMoment.RaceResult => "Race result",
        _ => moment.ToString(),
    };

    /// <summary>The moment an announcement belongs to, or null for one nobody can sponsor.</summary>
    public static SponsoredMoment? ForEvent(AnnouncementEventType type) => type switch
    {
        AnnouncementEventType.FastLap => SponsoredMoment.FastestLap,
        AnnouncementEventType.LeaderChange => SponsoredMoment.LeadChange,
        AnnouncementEventType.CloseBattle => SponsoredMoment.Battle,
        AnnouncementEventType.PositionReport => SponsoredMoment.RunThrough,
        AnnouncementEventType.AchievementUnlocked => SponsoredMoment.Milestones,
        AnnouncementEventType.RaceCompleted => SponsoredMoment.RaceResult,
        _ => null,
    };

    /// <summary>
    /// The moment an AI commentary segment belongs to (18 Oct 2026), or null
    /// for one nobody can sponsor - a battle watch carries the battles'
    /// sponsor, a pace note the fastest lap's, and so on.
    /// </summary>
    public static SponsoredMoment? ForSegment(SegmentKind kind) => kind switch
    {
        SegmentKind.BattleWatch => SponsoredMoment.Battle,
        SegmentKind.PaceNote => SponsoredMoment.FastestLap,
        SegmentKind.QualifyingReport => SponsoredMoment.QualifyingRecap,
        SegmentKind.Honours or SegmentKind.BadgesEarned => SponsoredMoment.Milestones,
        _ => null,
    };

    /// <summary>
    /// Who presents <paramref name="moment"/>: the first sponsor in the
    /// club's list with it ticked, in tonight's rotation and with a name to
    /// say. Null when nobody does.
    /// </summary>
    public static SponsorBooking? SponsorFor(IEnumerable<SponsorBooking>? sponsors, SponsoredMoment moment)
    {
        if (sponsors is null)
        {
            return null;
        }

        var key = moment.ToString();

        return sponsors.FirstOrDefault(s =>
            s is { Enabled: true }
            && !string.IsNullOrWhiteSpace(s.Name)
            && s.Presents is { } presents
            && presents.Any(p => string.Equals(p, key, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Every moment ticked for more than one sponsor, with the sponsor who
    /// actually gets it - so the Sponsors page can say so rather than let a
    /// club think both are being named.
    /// </summary>
    public static IReadOnlyList<(SponsoredMoment Moment, string Winner, IReadOnlyList<string> Others)> Clashes(IEnumerable<SponsorBooking>? sponsors)
    {
        var list = (sponsors ?? Enumerable.Empty<SponsorBooking>())
            .Where(s => s is { Enabled: true } && !string.IsNullOrWhiteSpace(s.Name))
            .ToList();

        var clashes = new List<(SponsoredMoment, string, IReadOnlyList<string>)>();

        foreach (var moment in Enum.GetValues<SponsoredMoment>())
        {
            var key = moment.ToString();
            var holders = list
                .Where(s => s.Presents is { } presents && presents.Any(p => string.Equals(p, key, StringComparison.OrdinalIgnoreCase)))
                .Select(s => s.Name.Trim())
                .ToList();

            if (holders.Count > 1)
            {
                clashes.Add((moment, holders[0], holders.Skip(1).ToList()));
            }
        }

        return clashes;
    }

    /// <summary>
    /// The delivery the sponsor's credit is performed with, by a voice that
    /// can perform one (ElevenLabs v3, the HeatCaller voice). Every other
    /// voice has it taken out before it speaks (VoiceProfile.SanitiseForModel,
    /// Kokoro), and the screens never show it (RaiseAnnouncementText).
    /// </summary>
    internal const string CreditDelivery = "[cheerful]";

    // A few ways to say each credit, dealt in turn by TemplateBag so the
    // same one is not heard twice running. Every one is a whole sentence
    // about the moment it follows, with {sponsor} in it once.
    private static readonly string[] FastestLapCredits =
    {
        "That fastest lap is brought to you by {sponsor}!",
        "And that fastest lap comes to you courtesy of {sponsor}!",
        "The fastest lap of the race is presented by {sponsor}!",
    };

    private static readonly string[] LeadChangeCredits =
    {
        "That lead change is brought to you by {sponsor}!",
        "And that change at the front comes to you courtesy of {sponsor}!",
        "Lead changes tonight are presented by {sponsor}!",
    };

    private static readonly string[] BattleCredits =
    {
        "This battle is brought to you by {sponsor}!",
        "And this fight comes to you courtesy of {sponsor}!",
        "Tonight's battles are presented by {sponsor}!",
    };

    private static readonly string[] DriverIntroCredits =
    {
        "Your driver introductions tonight are brought to you by {sponsor}!",
        "The driver introductions are presented by {sponsor}!",
    };

    private static readonly string[] QualifyingRecapCredits =
    {
        "This qualifying recap is brought to you by {sponsor}!",
        "Your qualifying recap comes to you courtesy of {sponsor}!",
    };

    private static readonly string[] RunThroughCredits =
    {
        "That running order is brought to you by {sponsor}.",
        "Your running order comes to you courtesy of {sponsor}.",
        "The running order is presented by {sponsor}.",
    };

    private static readonly string[] MilestoneCredits =
    {
        "That milestone is brought to you by {sponsor}!",
        "And that achievement comes to you courtesy of {sponsor}!",
        "Milestones tonight are presented by {sponsor}!",
    };

    private static readonly string[] RaceResultCredits =
    {
        "That result is brought to you by {sponsor}!",
        "And the race result comes to you courtesy of {sponsor}!",
        "Tonight's results are presented by {sponsor}!",
    };

    private static readonly string[] OtherCredits =
    {
        "That's brought to you by {sponsor}!",
    };

    /// <summary>The ways the credit for <paramref name="moment"/> can be said, each with {sponsor} in it once.</summary>
    internal static IReadOnlyList<string> CreditLines(SponsoredMoment moment) => moment switch
    {
        SponsoredMoment.FastestLap => FastestLapCredits,
        SponsoredMoment.LeadChange => LeadChangeCredits,
        SponsoredMoment.Battle => BattleCredits,
        SponsoredMoment.DriverIntros => DriverIntroCredits,
        SponsoredMoment.QualifyingRecap => QualifyingRecapCredits,
        SponsoredMoment.RunThrough => RunThroughCredits,
        SponsoredMoment.Milestones => MilestoneCredits,
        SponsoredMoment.RaceResult => RaceResultCredits,
        _ => OtherCredits,
    };

    /// <summary>
    /// The name as it is said: as the club wrote it on the Sponsors page -
    /// "Name, as it's said" - trimmed of a full stop or exclamation mark of
    /// its own, so "Smith and Co." does not end the line twice.
    /// </summary>
    private static string SaidName(string? sponsorName) =>
        (sponsorName ?? string.Empty).Trim().TrimEnd('.', '!', ' ');

    /// <summary>
    /// The sponsor's credit for a call they present, ready to go on the end
    /// of it: " [cheerful] That fastest lap is brought to you by Joe's Hobby
    /// Shop!" Empty for a blank name.
    ///
    /// WHY A WHOLE SENTENCE (21 Oct 2026). It used to be " Presented by
    /// Joe's Hobby Shop." on the end of every call - three words with no
    /// subject and a full stop, straight after a line the voice had just
    /// shouted. A voice reads a fragment like that the way it reads a
    /// caption: flat, clipped and robotic, and that is exactly how the
    /// sponsored segments sounded on the night. A broadcaster's credit is a
    /// sentence about the moment it follows, said brightly - so each moment
    /// has a few of them, dealt in turn, each with its own delivery.
    ///
    /// AND THEIR TAGLINE AFTER IT (21 Oct 2026), where they gave one - see
    /// <see cref="SaidTagline"/>.
    /// </summary>
    public static string Tag(SponsoredMoment moment, string sponsorName, string? tagline = null)
    {
        var credit = Credit(moment, sponsorName, tagline);

        return credit.Length == 0 ? string.Empty : $" {CreditDelivery} {credit}";
    }

    /// <summary>
    /// The sponsor's credit for the END of what was just said, with no
    /// delivery tag - "That battle is brought to you by Joe's Hobby Shop!
    /// Everything you need to go faster." - and their tagline after it
    /// where they gave one. Empty for a blank name. A null moment - a
    /// segment no moment covers - gets a credit that fits anything.
    ///
    /// Written by HeatCaller, never by the model (21 Oct 2026): the model
    /// used to be asked to end a segment with the credit, and a model asked
    /// to say a sponsor's tagline says something LIKE it. A tagline is the
    /// sponsor's own words and is only worth anything said exactly.
    /// </summary>
    public static string Credit(SponsoredMoment? moment, string? sponsorName, string? tagline = null)
    {
        var name = SaidName(sponsorName);

        if (name.Length == 0)
        {
            return string.Empty;
        }

        var line = TemplateBag.Pick(moment is { } known ? CreditLines(known) : OtherCredits)
            .Replace("{sponsor}", name, StringComparison.Ordinal);

        return WithTagline(line, tagline);
    }

    /// <summary>
    /// A sponsor's tagline as it is said after their credit (21 Oct 2026):
    /// the one line the club typed for them on the Sponsors page, cleaned
    /// the way every typed field is - one line, no brackets, so a tagline
    /// can never pass for a delivery tag - and held to the length the box
    /// is (SponsorRotation.MaxTaglineChars). A tagline with no ending of
    /// its own gets a full stop, so the next sentence does not run on from
    /// it. Empty for none.
    /// </summary>
    public static string SaidTagline(string? tagline)
    {
        var line = CommentaryService.Field(tagline, SponsorRotation.MaxTaglineChars);

        if (line.Length > 0 && char.IsLetterOrDigit(line[^1]))
        {
            line += ".";
        }

        return line;
    }

    /// <summary><paramref name="credit"/> with the tagline after it, or the credit alone when there is none.</summary>
    private static string WithTagline(string credit, string? tagline)
    {
        var said = SaidTagline(tagline);

        return said.Length == 0 ? credit : credit + " " + said;
    }

    /// <summary>
    /// The tagline of the sponsor credited by <paramref name="sponsorName"/>:
    /// the first sponsor in tonight's rotation with that name, as
    /// <see cref="SponsorFor"/> finds who presents a moment, compared the
    /// way the name is said. Null for none, or for a sponsor who gave no
    /// tagline.
    ///
    /// By NAME because a line the AI commentary writes is handed the name
    /// and nothing else (CommentaryService.SponsorTagline).
    /// </summary>
    public static string? TaglineOf(IEnumerable<SponsorBooking>? sponsors, string? sponsorName)
    {
        var name = SaidName(sponsorName);

        if (sponsors is null || name.Length == 0)
        {
            return null;
        }

        var sponsor = sponsors.FirstOrDefault(s =>
            s is { Enabled: true }
            && string.Equals(SaidName(s.Name), name, StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(sponsor?.Tagline) ? null : sponsor!.Tagline;
    }

    /// <summary>
    /// <paramref name="call"/> with the sponsor's <paramref name="credit"/>
    /// on the end. A club's own line with no ending of its own - "Sam sets
    /// the fastest lap" - is closed with a full stop first, so the credit is
    /// a sentence of its own rather than running on from the call's last
    /// word. Just the call, trimmed, when there is no credit.
    /// </summary>
    public static string WithCredit(string call, string credit)
    {
        var line = (call ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(credit))
        {
            return line;
        }

        if (line.Length == 0)
        {
            return credit.Trim();
        }

        if (char.IsLetterOrDigit(line[^1]))
        {
            line += ".";
        }

        return (line + " " + credit.TrimStart()).Trim();
    }

    // THE CREDIT AT THE TOP (21 Oct 2026). On the recap show a sponsor is
    // credited before the segment they present, the way a TV broadcast does
    // it - "This race recap is brought to you by..." and then the recap -
    // rather than tacked on at the end, which is where the model had been
    // putting it. These look forward to what is coming, where the credits above
    // look back at what was just said ("That result..."). Dealt in turn by
    // TemplateBag like the others.
    private static readonly string[] RaceResultLeadIns =
    {
        "This race recap is brought to you by {sponsor}!",
        "Your race recap comes to you courtesy of {sponsor}!",
        "The race recap is presented by {sponsor}!",
    };

    private static readonly string[] MilestoneLeadIns =
    {
        "This look at tonight's milestones is brought to you by {sponsor}!",
        "Tonight's milestones come to you courtesy of {sponsor}!",
        "Milestones tonight are presented by {sponsor}!",
    };

    private static readonly string[] QualifyingRecapLeadIns =
    {
        "This qualifying recap is brought to you by {sponsor}!",
        "Your qualifying recap comes to you courtesy of {sponsor}!",
    };

    private static readonly string[] FastestLapLeadIns =
    {
        "This look at the pace is brought to you by {sponsor}!",
        "Tonight's fastest laps are presented by {sponsor}!",
    };

    private static readonly string[] BattleLeadIns =
    {
        "This look at the battles is brought to you by {sponsor}!",
        "Tonight's battles are presented by {sponsor}!",
    };

    private static readonly string[] RunThroughLeadIns =
    {
        "This running order is brought to you by {sponsor}.",
        "Your running order comes to you courtesy of {sponsor}.",
        "The running order is presented by {sponsor}.",
    };

    private static readonly string[] OtherLeadIns =
    {
        "This part of the show is brought to you by {sponsor}!",
        "This segment is presented by {sponsor}!",
    };

    /// <summary>
    /// The ways a credit at the TOP of a segment can be said, looking
    /// forward to it, each with {sponsor} in it once. A null moment - a
    /// segment no moment covers - gets ones that fit anything.
    /// </summary>
    internal static IReadOnlyList<string> LeadInLines(SponsoredMoment? moment) => moment switch
    {
        SponsoredMoment.RaceResult => RaceResultLeadIns,
        SponsoredMoment.Milestones => MilestoneLeadIns,
        SponsoredMoment.QualifyingRecap => QualifyingRecapLeadIns,
        SponsoredMoment.DriverIntros => DriverIntroCredits,
        SponsoredMoment.FastestLap => FastestLapLeadIns,
        SponsoredMoment.Battle => BattleLeadIns,
        SponsoredMoment.RunThrough => RunThroughLeadIns,
        _ => OtherLeadIns,
    };

    /// <summary>
    /// The sponsor's credit for the top of a segment on the recap show -
    /// "This race recap is brought to you by Joe's Hobby Shop!" - with no
    /// delivery tag: written segments are tagged sentence by sentence at
    /// speaking time (CommentaryService.Tagged), and this is one of their
    /// sentences. Their tagline after it, where they gave one (21 Oct
    /// 2026). Empty for a blank name.
    /// </summary>
    public static string LeadIn(SponsoredMoment? moment, string sponsorName, string? tagline = null)
    {
        var name = SaidName(sponsorName);

        return name.Length == 0
            ? string.Empty
            : WithTagline(TemplateBag.Pick(LeadInLines(moment)).Replace("{sponsor}", name, StringComparison.Ordinal), tagline);
    }

    /// <summary>
    /// <paramref name="written"/> with <paramref name="leadIn"/> in front of
    /// it. Nothing written stays nothing - a credit with no segment after it
    /// is not a segment - and a blank credit leaves the words as they were.
    /// </summary>
    public static string? WithLeadIn(string leadIn, string? written)
    {
        if (string.IsNullOrWhiteSpace(written))
        {
            return written;
        }

        return string.IsNullOrWhiteSpace(leadIn)
            ? written
            : leadIn.Trim() + " " + written.TrimStart();
    }

    /// <summary>
    /// A two-hander with the credit at its top, in the lead voice: on the
    /// front of the host's opening turn, or as a turn of its own in front of
    /// the co-host when the model gave her the first word - the co-host
    /// never reads the credit. Unchanged for a blank credit or no turns.
    /// </summary>
    public static IReadOnlyList<ExchangeTurn> WithLeadIn(string leadIn, IReadOnlyList<ExchangeTurn> turns)
    {
        if (string.IsNullOrWhiteSpace(leadIn) || turns.Count == 0)
        {
            return turns;
        }

        var credited = turns.ToList();

        if (credited[0].FromCoHost)
        {
            credited.Insert(0, new ExchangeTurn(false, leadIn.Trim()));
        }
        else
        {
            credited[0] = credited[0] with { Line = WithLeadIn(leadIn, credited[0].Line) ?? credited[0].Line };
        }

        return credited;
    }
}

/// <summary>
/// Rations the "presented by" on a moment that can happen many times a race,
/// and counts every one that was said, for the sponsor report.
///
/// THE RATION. A once-a-race moment - the intros, the qualifying recap,
/// the result - is always named: the sponsor bought that segment and there
/// is only one of it. A moment that repeats - fastest laps, lead changes,
/// battles, the run-through, achievements - is named at most once every
/// <see cref="RepeatGap"/>, so the first fastest lap of a race carries the
/// sponsor and the next four, a lap apart, do not. The graphic carries the
/// name every time: a caption costs the room nothing.
///
/// THE COUNT is per race night (6am to 6am, TonightAtTheTrack.NightOf), the
/// same night SponsorReport is written for, and only of mentions actually
/// HANDED TO THE VOICE - a "presented by" the ration held back is not one a
/// sponsor can be told they got.
///
/// Locked: the voice adds mentions from the lap pipeline and the Sponsors
/// page reads them from the UI thread.
/// </summary>
internal sealed class SponsoredMomentGate
{
    /// <summary>The least time between two "presented by"s on the same repeating moment.</summary>
    public static readonly TimeSpan RepeatGap = TimeSpan.FromMinutes(3);

    private readonly object _gate = new();
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<SponsoredMoment, DateTime> _lastSaid = new();
    private readonly Dictionary<string, Dictionary<SponsoredMoment, int>> _mentions = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _night = DateTime.MinValue;

    public SponsoredMomentGate(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <summary>Whether this moment happens once a race, and so is always named.</summary>
    public static bool OncePerRace(SponsoredMoment moment) =>
        moment is SponsoredMoment.DriverIntros or SponsoredMoment.QualifyingRecap or SponsoredMoment.RaceResult;

    /// <summary>
    /// True when <paramref name="moment"/> may carry its sponsor's name now.
    /// Asking changes nothing: the mention is only counted, and the ration
    /// only started, by <see cref="Mentioned"/> - once the line is actually
    /// on its way to the speaker, so a line the queue dropped neither uses
    /// up the ration nor goes in the sponsor's report.
    /// </summary>
    public bool CanMention(SponsoredMoment moment)
    {
        lock (_gate)
        {
            var now = _clock();
            RollNight(now);

            return OncePerRace(moment)
                || !_lastSaid.TryGetValue(moment, out var last)
                || now - last >= RepeatGap;
        }
    }

    /// <summary>
    /// Takes the ration for <paramref name="moment"/> as a tagged line is
    /// queued: true when it may carry the name, and the next line inside
    /// <see cref="RepeatGap"/> then may not - even if this one has not been
    /// spoken yet, so two lines queued a second apart cannot both carry it.
    /// Nothing is counted here; <see cref="Mentioned"/> counts, as the line
    /// is spoken.
    /// </summary>
    public bool TryReserve(SponsoredMoment moment) => TryReserve(moment, out _, out _);

    /// <summary>
    /// <see cref="TryReserve(SponsoredMoment)"/>, also handing back what
    /// <see cref="Release"/> needs to give the reservation back: the time it
    /// was taken (stamp) and what the ration said before (previous).
    ///
    /// WHY (20 Oct 2026, Bug Hunt 8): a routine call reserved the ration as
    /// its line was written, and when the queue then dropped the line
    /// nothing gave it back - the sponsor lost "Presented by" for three
    /// minutes over a line nobody heard.
    /// </summary>
    public bool TryReserve(SponsoredMoment moment, out DateTime stamp, out DateTime? previous)
    {
        lock (_gate)
        {
            var now = _clock();
            RollNight(now);

            stamp = now;
            previous = _lastSaid.TryGetValue(moment, out var last) ? last : null;

            if (!OncePerRace(moment)
                && previous is { } p
                && now - p < RepeatGap)
            {
                return false;
            }

            _lastSaid[moment] = now;
            return true;
        }
    }

    /// <summary>
    /// Gives back a reservation from TryReserve for a line that never went
    /// out. Undoes nothing if a later reservation, a Mentioned or a new race
    /// night has replaced it (20 Oct 2026) - so it can never hand back a
    /// ration a line that WAS said is using.
    /// </summary>
    public void Release(SponsoredMoment moment, DateTime stamp, DateTime? previous)
    {
        lock (_gate)
        {
            if (_lastSaid.TryGetValue(moment, out var current) && current == stamp)
            {
                if (previous is { } p)
                {
                    _lastSaid[moment] = p;
                }
                else
                {
                    _lastSaid.Remove(moment);
                }
            }
        }
    }

    /// <summary>A "presented by" for <paramref name="sponsorId"/> on <paramref name="moment"/> has gone to the speaker: counted, and the ration starts again from now.</summary>
    public void Mentioned(SponsoredMoment moment, string sponsorId)
    {
        lock (_gate)
        {
            var now = _clock();
            RollNight(now);

            _lastSaid[moment] = now;

            var key = sponsorId ?? string.Empty;

            if (!_mentions.TryGetValue(key, out var bySponsor))
            {
                bySponsor = new Dictionary<SponsoredMoment, int>();
                _mentions[key] = bySponsor;
            }

            bySponsor[moment] = bySponsor.GetValueOrDefault(moment) + 1;
        }
    }

    /// <summary>Tonight's "presented by" mentions, per sponsor id and moment. A copy, safe to keep.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<SponsoredMoment, int>> MentionsTonight()
    {
        lock (_gate)
        {
            RollNight(_clock());

            return _mentions.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<SponsoredMoment, int>)new Dictionary<SponsoredMoment, int>(pair.Value),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>A new race night starts the count again - and the ration, which is about one race, not one night.</summary>
    private void RollNight(DateTime now)
    {
        var night = TonightAtTheTrack.NightOf(now);

        if (night != _night)
        {
            _night = night;
            _mentions.Clear();
            _lastSaid.Clear();
        }
    }
}
