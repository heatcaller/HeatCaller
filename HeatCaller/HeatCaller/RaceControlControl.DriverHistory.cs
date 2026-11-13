namespace HeatCaller;

/// <summary>
/// Race Control's half of the calls that know a driver's history (11 Nov
/// 2026, voice speed plan, phase 4 #4) - it has the saved races and the
/// layout picked for tonight. See DriverHistory.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>Each driver's nights on tonight's layout, by layout and transponder - read once a race, not on every lap.</summary>
    private readonly Dictionary<string, IReadOnlyList<(DateOnly Night, long BestMs)>> _nightsOn = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Each driver's races and laps at the club before this race, by transponder - read once a race.</summary>
    private readonly Dictionary<string, (int Races, int Laps)> _careerBefore = new(StringComparer.OrdinalIgnoreCase);

    private int _driverHistoryGeneration = -1;

    /// <summary>
    /// Who has been told tonight that they beat last time out, or are back on
    /// form - once a night each, not once a race: every heat after the first
    /// is measured against the same last night, and the room has heard it.
    /// "night|kind|transponder".
    /// </summary>
    private readonly HashSet<string> _toldTonight = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A demo's made-up laps, a ghost, a rental (one transponder, many drivers) or an excluded guest - left out, as BestEverHereBefore leaves them out.</summary>
    private bool LeftOutOfHistoryCalls(string transponder) =>
        _race.IsDemoMode
        || GhostService.IsGhost(transponder)
        || _profiles.Get(transponder) is { ExcludeFromRecords: true } or { IsRental: true };

    private void ForgetDriverHistoryOnANewRace()
    {
        if (_driverHistoryGeneration != _race.SessionGeneration)
        {
            _nightsOn.Clear();
            _careerBefore.Clear();
            _driverHistoryGeneration = _race.SessionGeneration;
        }
    }

    /// <summary>
    /// For the announcer, as a personal best comes in that is not their best
    /// ever here: whether it puts them back on form (quicker than any of
    /// their last few nights here, after a run of slower ones) or quicker
    /// than last time out - with the mark it beat. Null for neither, or once
    /// they have been told tonight. <paramref name="on"/> is the announcer's
    /// answer to whether a call is switched on: one that is off is neither
    /// returned nor marked as told.
    /// </summary>
    internal (AnnouncementEventType Type, long Previous)? LapAgainstHistory(
        string transponder, long lapMs, Func<AnnouncementEventType, bool> on)
    {
        ForgetDriverHistoryOnANewRace();

        if (lapMs <= 0 || LeftOutOfHistoryCalls(transponder))
        {
            return null;
        }

        var track = SelectedTrackName();

        if (string.IsNullOrWhiteSpace(track))
        {
            return null;
        }

        var tonight = DriverHistory.NightOf(DateTime.Now);
        var key = $"{track}\n{transponder}";

        if (!_nightsOn.TryGetValue(key, out var nights))
        {
            nights = DriverHistory.NightsOn(_sessionHistory.All, track, transponder, tonight);
            _nightsOn[key] = nights;
        }

        // Only a call that is on (bug hunt 17): with Back on form unticked,
        // a lap that would have been one used to be marked as told for last
        // time out as well, then thrown away by the announcer - so the driver
        // never heard "quicker than last time out" all night.
        if (on(AnnouncementEventType.BackOnForm)
            && DriverHistory.SlumpBest(nights) is { } slump && lapMs < slump
            && _toldTonight.Add($"{tonight}|form|{transponder}"))
        {
            // Back on form says the better thing; last time out is not said as well.
            _toldTonight.Add($"{tonight}|last|{transponder}");

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [pace] {transponder}'s {lapMs / 1000.0:0.000} is their best here in {DriverHistory.SlumpNights} nights (best of those {slump / 1000.0:0.000}).");

            return (AnnouncementEventType.BackOnForm, slump);
        }

        if (on(AnnouncementEventType.BeatLastTimeOut)
            && DriverHistory.LastTimeOut(nights) is { } last && lapMs < last
            && _toldTonight.Add($"{tonight}|last|{transponder}"))
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [pace] {transponder}'s {lapMs / 1000.0:0.000} beats their best last time out on {track} ({last / 1000.0:0.000}, {nights[0].Night:d MMM}).");

            return (AnnouncementEventType.BeatLastTimeOut, last);
        }

        return null;
    }

    /// <summary>
    /// For the announcer: how many races the driver has run at the club and
    /// how many laps they have done, before this race. Null for the drivers
    /// the history calls leave out.
    /// </summary>
    internal (int Races, int Laps)? CareerBefore(string transponder)
    {
        ForgetDriverHistoryOnANewRace();

        if (LeftOutOfHistoryCalls(transponder))
        {
            return null;
        }

        if (!_careerBefore.TryGetValue(transponder, out var career))
        {
            var all = _sessionHistory.All;
            career = (DriverHistory.RacesBefore(all, transponder), DriverHistory.LapsBefore(all, transponder));
            _careerBefore[transponder] = career;
        }

        return career;
    }
}
