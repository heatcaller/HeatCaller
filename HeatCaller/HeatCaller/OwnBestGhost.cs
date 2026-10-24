namespace HeatCaller;

/// <summary>
/// CHASE YOUR OWN GHOST (22 Oct 2026).
///
/// One ghost per driver, running at that driver's own best lap - their best
/// tonight on this layout, or their best ever on it before they have set
/// one tonight (GhostPaceSource.OwnBestTonight). It sits on the board and
/// the Big Screen like any other ghost, named "Sam's best", so a driver in
/// practice sees on every lap whether they are gaining on the best version
/// of themselves or losing to it - and the gap to it, since it is a car in
/// the order like any other.
///
/// FIXED FOR THE RUN, NOT LIVE. A LiveOwnPace ghost already exists and
/// follows the driver's best as they set it; that makes a good pacer and a
/// target that can never be beaten, because it improves the moment you do.
/// This one stays put, so beating it means something. Picking the same
/// driver again brings it up to the new best (PaceRevision) - from its next
/// lap, without going back to the line - which is the "now beat that" step.
///
/// Everything a ghost is kept out of, this one is kept out of too: it is a
/// GhostCar with a ghost transponder, so every GhostService.IsGhost check -
/// records, results, points, badges, the warning banner - applies unchanged.
/// </summary>
internal static class OwnBestGhost
{
    /// <summary>What the ghost is called on the board: "Sam's best".</summary>
    internal static string NameFor(string? driverName, string transponder) =>
        $"{(string.IsNullOrWhiteSpace(driverName) ? transponder : driverName.Trim())}'s best";

    /// <summary>
    /// Puts a driver's own-best ghost into the settings, ready to run: the
    /// one they already have, brought up to their latest best, or a new one.
    /// Switches ghost racing on, and this ghost with it - asking to chase
    /// your best is asking for a ghost.
    /// </summary>
    /// <returns>The ghost, and whether it was already there (and so refreshed rather than added).</returns>
    internal static (GhostCar Ghost, bool Refreshed) ChaseFor(
        GhostSettings settings, string transponder, string? driverName, string? carModel)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.Enabled = true;

        var existing = settings.Ghosts.FirstOrDefault(g =>
            g.PaceSource == GhostPaceSource.OwnBestTonight
            && string.Equals(g.SourceTransponder, transponder, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.Enabled = true;
            existing.PaceRevision++;
            return (existing, true);
        }

        var ghost = new GhostCar
        {
            Name = NameFor(driverName, transponder),
            CarModel = carModel ?? string.Empty,
            PaceSource = GhostPaceSource.OwnBestTonight,
            SourceTransponder = transponder,

            // Exactly their best - not the Quick Rival's 2% quicker. The
            // question this ghost asks is "can you beat yourself", and a
            // handicap would answer a different one.
            PacePercent = 100,

            // Small variation: a best lap is a driver's own pace on a good
            // run, and a metronome at it is a lap they have only done once.
            Consistency = GhostConsistency.Steady,
        };

        settings.Ghosts.Add(ghost);

        return (ghost, false);
    }

    /// <summary>
    /// The drivers to offer, those on the practice board first - the driver
    /// asking is almost always one of them - then everyone else, ghosts never.
    /// </summary>
    internal static List<DriverProfile> Candidates(IEnumerable<DriverProfile> profiles, IEnumerable<string> onTrack)
    {
        var onTrackSet = new HashSet<string>(onTrack, StringComparer.OrdinalIgnoreCase);

        return profiles
            .Where(p => !GhostService.IsGhost(p.Transponder))
            .OrderByDescending(p => onTrackSet.Contains(p.Transponder))
            .ToList();
    }

    /// <summary>
    /// Every own-best ghost in the settings that is switched on, as ghost
    /// transponder to the driver it chases (22 Oct 2026) - for the board,
    /// which puts each one under its driver in practice, and for the Coach
    /// menu's gap to the ghost.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ChasedBy(GhostSettings? settings)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (settings is null || !settings.Enabled)
        {
            return map;
        }

        foreach (var ghost in settings.Ghosts)
        {
            if (ghost is { Enabled: true, PaceSource: GhostPaceSource.OwnBestTonight }
                && !string.IsNullOrWhiteSpace(ghost.Transponder)
                && !string.IsNullOrWhiteSpace(ghost.SourceTransponder))
            {
                map.TryAdd(ghost.Transponder, ghost.SourceTransponder);
            }
        }

        return map;
    }

    /// <summary>
    /// How far the driver is behind their ghost ON THE TRACK at the moment
    /// they cross the line, in ms - negative when they are ahead of it - or
    /// null when it can't be told (22 Oct 2026).
    ///
    /// Both cars' elapsed times are on the same clock, milliseconds since the
    /// green (GhostService measures its passes against the green), so the
    /// time since the ghost last crossed is the driver's elapsed less the
    /// ghost's, taken round one ghost lap. Under half a lap since it crossed:
    /// the ghost is that far up the road, and the driver is behind it. Over
    /// half a lap: the ghost is coming round to the line behind them, and
    /// the driver is ahead by what is left of its lap.
    /// </summary>
    /// <param name="driverElapsedMs">
    /// The driver's elapsed time at this crossing, measured from the green with
    /// any penalty taken off (22 Oct 2026, bug hunt 9) - not RacerStanding.ElapsedMs
    /// as it stands. See RaceControlControl.SinceGreenAtLastCrossingMs.
    /// </param>
    /// <param name="ghostElapsedMs">The ghost's elapsed time at its last crossing, also measured from the green.</param>
    /// <param name="ghostLapMs">The ghost's lap time - its last lap.</param>
    internal static long? GapToGhostMs(long driverElapsedMs, long ghostElapsedMs, long ghostLapMs)
    {
        if (ghostLapMs <= 0 || ghostElapsedMs <= 0 || driverElapsedMs <= 0)
        {
            return null;
        }

        var sinceGhost = ((driverElapsedMs - ghostElapsedMs) % ghostLapMs + ghostLapMs) % ghostLapMs;

        return sinceGhost <= ghostLapMs / 2 ? sinceGhost : -(ghostLapMs - sinceGhost);
    }

    /// <summary>
    /// The board in practice with each own-best ghost moved to just under
    /// the driver it chases, so the two are read side by side (22 Oct 2026).
    /// Everyone else keeps their order. A ghost whose driver is not on the
    /// board stays where it was.
    /// </summary>
    /// <param name="chasedBy">Ghost transponder to the driver it chases - see <see cref="ChasedBy"/>.</param>
    internal static IReadOnlyList<T> BesideTheirDriver<T>(IReadOnlyList<T> ordered, Func<T, string> idOf, IReadOnlyDictionary<string, string> chasedBy)
    {
        if (ordered.Count < 2 || chasedBy.Count == 0)
        {
            return ordered;
        }

        var onBoard = new HashSet<string>(ordered.Select(idOf), StringComparer.OrdinalIgnoreCase);

        // Only ghosts whose driver is on the board move.
        var moving = ordered
            .Where(row => chasedBy.TryGetValue(idOf(row), out var driver) && onBoard.Contains(driver))
            .ToList();

        if (moving.Count == 0)
        {
            return ordered;
        }

        var movingIds = new HashSet<string>(moving.Select(idOf), StringComparer.OrdinalIgnoreCase);
        var result = new List<T>(ordered.Count);

        foreach (var row in ordered)
        {
            var id = idOf(row);

            if (movingIds.Contains(id))
            {
                continue;
            }

            result.Add(row);

            foreach (var ghost in moving)
            {
                if (string.Equals(chasedBy[idOf(ghost)], id, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(ghost);
                }
            }
        }

        return result;
    }
}
