namespace HeatCaller;

/// <summary>
/// Drivers new to racing (12 Nov 2026, setup plan D12).
///
/// WHY: keeping new drivers is how clubs survive, and a first night is
/// where they're won or lost. Lapped six times with nobody saying a word,
/// they don't come back. It costs almost nothing to say who they are: the
/// desk sees "first night" or "novice, mentor Al" beside their name, the
/// announcer welcomes them once before their race so the field gives them
/// room, and Q1 seeding by pace already puts a driver with no laps in with
/// the slowest. Pure.
/// </summary>
public static class NewDrivers
{
    /// <summary>
    /// A driver with no result at the club before tonight is on their first
    /// night - see RacedBefore. The night runs 6am to 6am
    /// (TonightAtTheTrack.NightOf), so a newcomer who raced at 19:30 is still
    /// on their first night at 00:05.
    ///
    /// WHY NOT DriverProfile.FirstSeenUtc (15 Nov 2026, bug hunt 21): that is
    /// when somebody typed the profile in. Profiles made tonight by an
    /// entry-list import, at the desk, or on a club's first night with
    /// HeatCaller are mostly regulars, and the desk badged every one of them
    /// "first night" and the announcer welcomed them as "racing with us for
    /// the first time tonight". CommentaryReaders.NightDone counts its
    /// debutants from the filed results for the same reason.
    ///
    /// False with no history to go on, or too little - see RacedBefore.Knows.
    /// </summary>
    public static bool FirstNight(DriverProfile profile, RacedBefore? before) =>
        before is { Knows: true } && !before.Has(profile);

    /// <summary>New to racing: ticked as a novice, or on their first night.</summary>
    public static bool IsNew(DriverProfile profile, RacedBefore? before) =>
        profile.IsNovice || FirstNight(profile, before);

    /// <summary>What Who's Racing puts after their name: " · first night", " · novice, mentor Al Gorithim", or nothing.</summary>
    public static string Badge(DriverProfile profile, RacedBefore? before)
    {
        if (!IsNew(profile, before))
        {
            return string.Empty;
        }

        var what = FirstNight(profile, before) ? "first night" : "novice";
        var mentor = string.IsNullOrWhiteSpace(profile.Mentor) ? string.Empty : $", mentor {profile.Mentor.Trim()}";

        return $" · {what}{mentor}";
    }

    /// <summary>
    /// Who has raced at the club before tonight, from its filed sessions
    /// (15 Nov 2026, bug hunt 21) - any scored session with results, practice
    /// included, as DriverFirsts.IsFirstNight has it for the phone's
    /// first-night guide: a driver who practised here last week is not new.
    /// </summary>
    public sealed class RacedBefore
    {
        private readonly HashSet<string> _transponders;

        private RacedBefore(HashSet<string> transponders, int nights)
        {
            _transponders = transponders;
            Nights = nights;
        }

        /// <summary>How many nights before tonight the club has results filed for.</summary>
        public int Nights { get; }

        /// <summary>
        /// Whether there is enough on file to say who is new: the bar
        /// CommentaryReaders.NightDone sets for its debutants
        /// (CommentaryShowFacts.LeastDaysOnFile). A club in its first nights
        /// with HeatCaller has everybody's first filed race this week, and
        /// would otherwise badge and welcome its whole membership.
        /// </summary>
        public bool Knows => Nights >= CommentaryShowFacts.LeastDaysOnFile;

        /// <summary>Whether any transponder of this driver's, retired ones too, has a result before tonight.</summary>
        public bool Has(DriverProfile profile) =>
            profile.AllTransponderIds.Any(id => _transponders.Contains(id))
            || (profile.Transponder?.Trim() is { Length: > 0 } main && _transponders.Contains(main));

        /// <summary>Reads the club's filed sessions for the nights before tonight's (the 6am night of <paramref name="nowLocal"/>).</summary>
        public static RacedBefore From(IEnumerable<SessionRecord>? history, DateTime nowLocal)
        {
            var tonight = TonightAtTheTrack.NightOf(nowLocal);
            var nights = new HashSet<DateTime>();
            var transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var session in history ?? Enumerable.Empty<SessionRecord>())
            {
                if (session is null || session.IsNotScored() || session.Results is not { Count: > 0 } results)
                {
                    continue;
                }

                var night = TonightAtTheTrack.NightOf(session.StartedAtUtc.ToLocalTime());

                if (night >= tonight)
                {
                    continue;
                }

                nights.Add(night);

                foreach (var result in results)
                {
                    if (result?.Transponder?.Trim() is { Length: > 0 } who)
                    {
                        transponders.Add(who);
                    }
                }
            }

            return new RacedBefore(transponders, nights.Count);
        }
    }

    /// <summary>
    /// RacedBefore, read again only when the night changes or ten minutes
    /// have passed (for a history imported or deleted meanwhile): the desk
    /// redraws its roster at every check-in, and walking the whole history
    /// each time is the UI thread's time for nothing. The same keeping as
    /// MainForm.DriverIsOnFirstNight's for the phone.
    /// </summary>
    public sealed class RacedBeforeKept
    {
        private static readonly TimeSpan KeptFor = TimeSpan.FromMinutes(10);

        private readonly Func<IEnumerable<SessionRecord>?> _history;
        private RacedBefore? _read;
        private DateTime _night;
        private DateTime _readUtc;

        public RacedBeforeKept(Func<IEnumerable<SessionRecord>?> history) => _history = history;

        /// <summary>Who had raced before the night of <paramref name="nowLocal"/>.</summary>
        public RacedBefore Now(DateTime nowLocal)
        {
            var nowUtc = DateTime.UtcNow;
            var night = TonightAtTheTrack.NightOf(nowLocal);

            if (_read is null || _night != night || nowUtc - _readUtc >= KeptFor || nowUtc < _readUtc)
            {
                _read = RacedBefore.From(_history(), nowLocal);
                _night = night;
                _readUtc = nowUtc;
            }

            return _read;
        }
    }

    /// <summary>
    /// The announcer's welcome before a race with new drivers in it, or null
    /// with none: "A big welcome to Max Power, racing with us for the first
    /// time tonight - give them room out there." Names already welcomed
    /// tonight are left out.
    /// </summary>
    public static string? Welcome(IReadOnlyList<(string Name, bool FirstNight)> drivers)
    {
        if (drivers.Count == 0)
        {
            return null;
        }

        var names = Join(drivers.Select(d => d.Name).ToList());
        var allFirst = drivers.All(d => d.FirstNight);

        return allFirst
            ? $"A big welcome to {names}, racing with us for the first time tonight - give {(drivers.Count == 1 ? "them" : "them all")} room out there."
            : $"Look out for {names}, new to racing - give {(drivers.Count == 1 ? "them" : "them all")} room out there and a cheer at the end.";
    }

    private static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
    };
}
