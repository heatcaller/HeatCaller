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
/// makes the moment and the sponsor's name rides on it, three words long.
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
    /// The words added to a call: " Presented by Joe's Hobby Shop." The name
    /// is said as the club wrote it on the Sponsors page - "Name, as it's
    /// said" - and trimmed of a full stop of its own, so "Smith and Co."
    /// does not end the line twice.
    /// </summary>
    public static string Tag(string sponsorName)
    {
        var name = (sponsorName ?? string.Empty).Trim().TrimEnd('.', '!', ' ');

        return name.Length == 0 ? string.Empty : $" Presented by {name}.";
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
