namespace HeatCaller;

/// <summary>
/// Race Control's half of "fastest ever here" (10 Nov 2026) - it has the
/// saved races and the layout picked for tonight. See BestEverHere.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>Each driver's best before tonight, by layout and transponder - read once a race, not on every lap.</summary>
    private readonly Dictionary<string, long?> _bestEverHereBefore = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Who has been told this race - once each, since every later best that night beats the old mark too.</summary>
    private readonly HashSet<string> _toldBestEverHere = new(StringComparer.OrdinalIgnoreCase);

    private int _bestEverHereGeneration = -1;

    /// <summary>
    /// For the announcer, as a personal best comes in: the driver's quickest
    /// lap ever here before tonight, when this lap beats it and they have not
    /// been told yet this race - else null. Not for a demo's made-up laps, a
    /// ghost, a rental (one transponder, many drivers) or an excluded guest,
    /// the drivers the track record leaves out too.
    /// </summary>
    internal long? BestEverHereBefore(string transponder, long lapMs)
    {
        if (_bestEverHereGeneration != _race.SessionGeneration)
        {
            _bestEverHereBefore.Clear();
            _toldBestEverHere.Clear();
            _bestEverHereGeneration = _race.SessionGeneration;
        }

        if (lapMs <= 0 || _race.IsDemoMode || _toldBestEverHere.Contains(transponder)
            || GhostService.IsGhost(transponder)
            || _profiles.Get(transponder) is { ExcludeFromRecords: true } or { IsRental: true })
        {
            return null;
        }

        var track = SelectedTrackName();

        if (string.IsNullOrWhiteSpace(track))
        {
            return null;
        }

        var key = $"{track}\n{transponder}";

        if (!_bestEverHereBefore.TryGetValue(key, out var before))
        {
            before = BestEverHere.PreviousBest(_sessionHistory.All, track, transponder);
            _bestEverHereBefore[key] = before;
        }

        if (before is not { } best || lapMs >= best)
        {
            return null;
        }

        _toldBestEverHere.Add(transponder);

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [pace] {transponder}'s {lapMs / 1000.0:0.000} is their quickest ever on {track} (was {best / 1000.0:0.000}).");

        return best;
    }
}
