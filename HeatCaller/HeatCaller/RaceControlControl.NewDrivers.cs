namespace HeatCaller;

/// <summary>
/// The announcer's welcome for drivers new to racing (12 Nov 2026, setup
/// plan D12): once a night, in the gap before their first race tonight, so
/// the field gives them room. See NewDrivers.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>Who has been welcomed, as "night|transponder", so each is welcomed once a night.</summary>
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

        var newcomers = next.AssignedTransponders
            .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
            .Select(t => (Transponder: t, Profile: _profiles.Get(t)))
            .Where(d => d.Profile is { } p && NewDrivers.IsNew(p, before) && !_welcomed.Contains($"{night}|{d.Transponder}"))
            .ToList();

        if (newcomers.Count == 0)
        {
            return;
        }

        foreach (var (transponder, _) in newcomers)
        {
            _welcomed.Add($"{night}|{transponder}");
        }

        var line = NewDrivers.Welcome(newcomers.Select(d => (SpokenNameOf(d.Transponder), NewDrivers.FirstNight(d.Profile!, before))).ToList());

        if (line is not null)
        {
            SayGapLine(line, "Welcome");
        }
    }
}
