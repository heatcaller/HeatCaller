namespace HeatCaller;

/// <summary>
/// The announcer's welcome for drivers new to racing (12 Nov 2026, setup
/// plan D12): once a night, in the gap before their first race tonight, so
/// the field gives them room. See NewDrivers.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>Who has been welcomed, as "night|main transponder", so each driver is welcomed once a night.</summary>
    private readonly HashSet<string> _welcomed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Who had raced here before tonight - a first night is judged by that (bug hunt 21, see NewDrivers.FirstNight).</summary>
    private NewDrivers.RacedBeforeKept? _racedBefore;

    private void WelcomeNewDrivers(bool floorBusy)
    {
        if (floorBusy || _race.IsDemoMode || NextRound() is not { } next)
        {
            return;
        }

        // The time, not the date: a first night is the 6am night (see NewDrivers.FirstNight).
        var now = DateTime.Now;
        var night = NightEmail.NightKey(TonightAtTheTrack.NightOf(now));
        var before = (_racedBefore ??= new NewDrivers.RacedBeforeKept(() => _sessionHistory.All)).Now(now);

        // WHY (23 Nov 2026, bug hunt 24): the once-a-night guard was keyed on
        // the round's raw transponder, so a new driver racing their second car
        // on its own (alternate) transponder later in the night did not match
        // and was welcomed a second time. Now the key is the driver's main
        // transponder, and one driver seated twice in one race is named once.
        var newcomers = NewcomersToWelcome(next.AssignedTransponders, _profiles.Get, p => NewDrivers.IsNew(p, before), night, _welcomed);

        if (newcomers.Count == 0)
        {
            return;
        }

        var line = NewDrivers.Welcome(newcomers.Select(d => (SpokenNameOf(d.Transponder), NewDrivers.FirstNight(d.Profile, before))).ToList());

        if (line is not null)
        {
            SayGapLine(line, "Welcome");
        }
    }

    /// <summary>
    /// The new drivers in a race still to be welcomed tonight, one row per
    /// driver, and marks them welcomed. Keyed "night|main transponder" so a
    /// driver's alternate transponder counts as them (bug hunt 24).
    /// </summary>
    internal static IReadOnlyList<(string Transponder, DriverProfile Profile)> NewcomersToWelcome(
        IEnumerable<string> assigned,
        Func<string, DriverProfile?> profileOf,
        Func<DriverProfile, bool> isNew,
        string night,
        ISet<string> welcomed)
    {
        var rows = new List<(string Transponder, DriverProfile Profile)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in assigned)
        {
            if (string.IsNullOrWhiteSpace(t) || GhostService.IsGhost(t) || profileOf(t) is not { } profile || !isNew(profile))
            {
                continue;
            }

            var main = profile.Transponder is { Length: > 0 } m && !string.IsNullOrWhiteSpace(m) ? m.Trim() : t.Trim();
            var key = $"{night}|{main}";

            if (welcomed.Contains(key) || !seen.Add(key))
            {
                continue;
            }

            rows.Add((t, profile));
        }

        foreach (var key in seen)
        {
            welcomed.Add(key);
        }

        return rows;
    }
}
