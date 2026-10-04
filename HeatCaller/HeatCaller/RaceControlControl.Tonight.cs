namespace HeatCaller;

/// <summary>
/// Race Control's side of the night's story, for the Big Screen and the
/// phones: the championship as it would stand if the race on track ended now
/// (ChampionshipLive), how the track has changed tonight and the fastest laps
/// (TonightAtTheTrack), and the attract cards shown between races
/// (AttractMode).
///
/// Everything here reads the history, programme and season stores, so it is
/// worked out on the UI thread and kept for a little while rather than
/// worked out again on every refresh: the championship every two seconds,
/// the rest every fifteen.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>How often the "if it ended now" table is worked out again during a race.</summary>
    private const long IfItEndedNowEveryMs = 2_000;

    /// <summary>How often tonight's figures and the championship's top are worked out again.</summary>
    private const long TonightEveryMs = 15_000;

    private IfItEndedNow? _ifItEndedNow;
    private long _ifItEndedNowAt;

    /// <summary>
    /// The championships this race counts towards and the races they already
    /// count (ChampionshipLive.Candidates), for the race and round they were
    /// worked out for - the saved history does not change mid-race, so it is
    /// gone through once a race rather than every two seconds.
    /// </summary>
    private (int Generation, string? RoundId, IReadOnlyList<(Season Season, IReadOnlyList<SessionRecord> Counted)> List)? _championshipCandidates;

    private TonightFigures? _tonightFigures;
    private (long At, ChampionshipTop? Top)? _championshipTop;

    /// <summary>Tonight's figures as last worked out, and when (Environment.TickCount64).</summary>
    private sealed record TonightFigures(long At, IReadOnlyList<TrackEvolution> Evolution, IReadOnlyList<FastestTonight> Fastest);

    /// <summary>
    /// The championship this race counts towards, as it would stand if the
    /// race ended now - or null when no race is on, it is practice, a demo
    /// or a warm-up, or the race counts towards no championship. UI thread.
    /// </summary>
    internal IfItEndedNow? ChampionshipIfItEndedNow()
    {
        if (_race.State != RaceState.Racing || _race.IsPracticeMode || _race.IsDemoMode || _race.IsWarmingUp)
        {
            _ifItEndedNow = null;
            return null;
        }

        var now = Environment.TickCount64;

        var round = RoundForThisRace();
        var fresh = _championshipCandidates is { } held
            && held.Generation == _race.SessionGeneration
            && string.Equals(held.RoundId, round?.Id, StringComparison.Ordinal);

        if (fresh && _ifItEndedNowAt != 0 && now - _ifItEndedNowAt < IfItEndedNowEveryMs)
        {
            return _ifItEndedNow;
        }

        _ifItEndedNowAt = now;

        try
        {
            if (!fresh)
            {
                _championshipCandidates = (_race.SessionGeneration, round?.Id,
                    ChampionshipLive.Candidates(round, _seasons, _rounds, _events, _sessionHistory));
            }

            // The rows the race would be filed with: ghosts out, names from
            // the profiles - see RaceFiling.ResultsFrom.
            var racers = _race.GetOrderedStandings()
                .Where(s => !GhostService.IsGhost(s.TransponderId))
                .ToList();

            _ifItEndedNow = ChampionshipLive.FromCandidates(
                _championshipCandidates!.Value.List, RaceFiling.ResultsFrom(racers, _profiles), round, _profiles);
        }
        catch
        {
            // A table that can't be worked out is not shown - the race goes on.
            _ifItEndedNow = null;
        }

        return _ifItEndedNow;
    }

    /// <summary>Tonight's figures, worked out at most every fifteen seconds. UI thread.</summary>
    private TonightFigures Tonight()
    {
        var now = Environment.TickCount64;

        if (_tonightFigures is { } kept && now - kept.At < TonightEveryMs)
        {
            return kept;
        }

        IReadOnlyList<TrackEvolution> evolution = Array.Empty<TrackEvolution>();
        IReadOnlyList<FastestTonight> fastest = Array.Empty<FastestTonight>();

        try
        {
            var history = _sessionHistory.All;
            Func<string, RoundDefinition?> roundOf = id => _rounds?.Get(id);

            evolution = TonightAtTheTrack.Evolution(history, roundOf, DateTime.Now);
            fastest = TonightAtTheTrack.FastestLaps(history, roundOf, DateTime.Now);
        }
        catch
        {
            // Whatever could not be worked out is left off the cards.
        }

        _tonightFigures = new TonightFigures(now, evolution, fastest);
        return _tonightFigures;
    }

    /// <summary>The championship's top five for the attract card, worked out at most every fifteen seconds - and only while the cards are up.</summary>
    private ChampionshipTop? ChampionshipTopKept()
    {
        var now = Environment.TickCount64;

        if (_championshipTop is { } kept && now - kept.At < TonightEveryMs)
        {
            return kept.Top;
        }

        ChampionshipTop? top;

        try
        {
            top = ChampionshipTopNow();
        }
        catch
        {
            top = null;
        }

        _championshipTop = (now, top);
        return top;
    }

    /// <summary>How the track has changed tonight for <paramref name="className"/>, or null when there is nothing to say yet.</summary>
    private TrackEvolution? EvolutionFor(string? className) =>
        string.IsNullOrWhiteSpace(className)
            ? null
            : Tonight().Evolution.FirstOrDefault(e => string.Equals(e.ClassName, className.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The top five of the championship that matters now: the one for the
    /// next race's class, or else the one raced in most recently. Null when
    /// there is no championship with a race in it.
    /// </summary>
    private ChampionshipTop? ChampionshipTopNow()
    {
        if (_seasons is null || _rounds is null)
        {
            return null;
        }

        var nextClass = (NextRound() ?? _activeRound?.Invoke())?.ClassName;

        var candidates = _seasons.All
            .Select(s => (Season: s, Sessions: EventSpine.SessionsFor(s, _events, _rounds, _sessionHistory)
                .Where(x => !x.IsDemoSession())
                .ToList()))
            .Where(c => c.Sessions.Count > 0)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var pick = candidates.FirstOrDefault(c =>
            !string.IsNullOrWhiteSpace(nextClass)
            && string.Equals(c.Season.ClassName, nextClass.Trim(), StringComparison.OrdinalIgnoreCase));

        if (pick.Season is null)
        {
            pick = candidates.OrderByDescending(c => c.Sessions.Max(s => s.StartedAtUtc)).First();
        }

        var table = SeasonStore.ComputeStandings(pick.Season, pick.Sessions, _profiles);

        return new ChampionshipTop(pick.Season.Name,
            table.Take(5).Select(e => (e.DriverName, e.TotalPoints)).ToList(),
            pick.Sessions.Count);
    }

    /// <summary>The Big Screen's between-races cards, as they stand now. UI thread.</summary>
    internal IReadOnlyList<AttractCard> AttractCards()
    {
        var tonight = Tonight();

        return AttractMode.Cards(
            UpcomingRaces(3),
            t => _profiles.Get(t)?.DriverName is { Length: > 0 } name ? name : t,
            tonight.Fastest,
            tonight.Evolution,
            ChampionshipTopKept());
    }
}
