namespace HeatCaller;

/// <summary>
/// "Leader behind" in a backmarker's ear (11 Nov 2026, race-night build
/// plan item 7.3): whether the race leader is about to lap a car, worked out
/// from the lap history the laptop already has, so the relay's lap voice can
/// warn that driver once before it happens.
///
/// WHY THIS IS WORKED OUT HERE AND NOT ON THE PHONE. The phone's lap voice
/// only ever had the gap to P1 in the board's own words ("+7.412", "+1L"),
/// and once a car is a lap down that is just "+1L" - it says nothing about
/// the leader coming round again for the second lap. And it only knows the
/// car behind by position, which for a backmarker is the next backmarker,
/// never the leader closing on them. The laptop has every crossing of every
/// car, so it can say where the leader really is on track at the moment
/// this car crossed the line.
///
/// HOW. At the moment this car last crossed the line, the leader was
/// somewhere on the lap behind it. The leader reaches that same line at its
/// next crossing - known, if it has happened, else its last crossing plus
/// its usual lap - and the time between is how far behind on track the
/// leader is. It is "about to lap you" when that is small AND the leader is
/// quicker (closing): within <see cref="CloseMs"/>, or within one lap's
/// worth of how much quicker the leader is, so a far quicker leader is
/// warned about a little earlier than one only just quicker.
///
/// WHAT IS SENT is not that time but which approach it is - one more than
/// the laps the car is already down - so the phone can say it once per
/// approach (it keys on the number) and never twice for the same lapping,
/// however many board reads see it. Nothing is sent otherwise.
///
/// NEVER STALE. It is worked out for the car's latest crossing and rides on
/// the same row as the lap it belongs to, so the phone says it in that lap's
/// call or not at all - and that call is dropped if it is late (LAPVOICE_JS
/// STALE_MS), as every lap call is. A late "leader behind" after the leader
/// has gone by would be worse than none.
/// </summary>
internal static class LeaderBehind
{
    /// <summary>A leader this close behind on track and closing is about to lap you, however slowly it is closing.</summary>
    internal const long CloseMs = 2500;

    /// <summary>The least the leader must be quicker per lap to count as closing: a tenth, so two cars on the same pace are never warned about.</summary>
    internal const long MinClosingMs = 100;

    /// <summary>How many recent flying laps make "usual pace" - the middle of them, so one crash lap does not.</summary>
    internal const int PaceLaps = 3;

    /// <summary>
    /// The approach for one car against whoever leads this running order, or
    /// null for none.
    ///
    /// The leader is the first REAL car (bug hunt 17). A ghost laps quicker
    /// than its driver by design and usually tops the order, and taking it
    /// as the leader told real drivers to move over for a car that is not
    /// on the track - and, while it led, never warned them about the real
    /// leader at all. A ghost is never warned either.
    /// </summary>
    internal static int? Approach(
        RacerStanding car,
        IReadOnlyList<RacerStanding> ordered,
        RaceStartType startType = RaceStartType.Standard,
        StaggerClock staggerClock = StaggerClock.SharedGreen,
        Func<string, bool>? tookTheFlag = null) =>
        car is null || ordered is null || Ghosts.IsGhost(car.TransponderId)
            ? null
            : Approach(car, ordered.FirstOrDefault(s => !Ghosts.IsGhost(s.TransponderId))!, startType, staggerClock, tookTheFlag);

    /// <summary>
    /// The approach for one car against the race leader, or null for none - see the class notes.
    ///
    /// Each car's time penalty is taken off first (bug hunt 17). ElapsedMs
    /// carries it, which is right for the order and the board's gaps, but
    /// this is about where the cars are on the track: with it left in, a
    /// 5 s penalty on a backmarker made a leader 7 s behind look 2 s behind
    /// ("Leader behind" most of a lap early, and nothing when it arrived),
    /// and one really 2 s behind look as if it had already gone by.
    ///
    /// And each car's own clock zero is put back (bug hunt 18), so both are
    /// on the green's clock. ElapsedMs runs from a qualifier's own first
    /// crossing, and under OwnRelease from the car's release: a backmarker
    /// that first crossed 3 s after the leader had a leader 5 s behind look
    /// 2 s behind, and the opposite offset lost the call altogether. The
    /// same rule as the ghost gap (RaceControlControl.ClockZeroSinceGreenMs).
    ///
    /// <paramref name="tookTheFlag"/> says whether a car has taken the
    /// checkered flag (RaceTiming.HasTakenFlag); null means nobody has.
    /// </summary>
    internal static int? Approach(
        RacerStanding car,
        RacerStanding leader,
        RaceStartType startType = RaceStartType.Standard,
        StaggerClock staggerClock = StaggerClock.SharedGreen,
        Func<string, bool>? tookTheFlag = null) =>
        car is null || leader is null || ReferenceEquals(car, leader)
            ? null
            : Approach(
                car.LapTimesMs, car.Laps,
                car.ElapsedMs - car.PenaltyMs + RaceControlControl.ClockZeroSinceGreenMs(car, startType, staggerClock),
                leader.LapTimesMs, leader.Laps,
                leader.ElapsedMs - leader.PenaltyMs + RaceControlControl.ClockZeroSinceGreenMs(leader, startType, staggerClock),
                tookTheFlag?.Invoke(leader.TransponderId) == true);

    /// <summary>
    /// The approach worked out from lap histories alone, for tests and any
    /// surface holding saved results: lap times with the start lap first,
    /// the lap counts and the race time of each car's latest crossing, as
    /// <see cref="RaceGaps.Compute"/> takes them.
    /// </summary>
    internal static int? Approach(
        IReadOnlyList<long> carLapTimes,
        int carLaps,
        long carElapsedMs,
        IReadOnlyList<long> leaderLapTimes,
        int leaderLaps,
        long leaderElapsedMs,
        bool leaderFinished = false)
    {
        // A flying lap each to have a pace at all.
        if (carLapTimes is not { Count: >= 2 } || leaderLapTimes is not { Count: >= 2 } || carLaps < 1 || leaderLaps < 1)
        {
            return null;
        }

        if (Pace(carLapTimes) is not { } carPace || Pace(leaderLapTimes) is not { } leaderPace)
        {
            return null;
        }

        var closing = carPace - leaderPace;

        if (closing < MinClosingMs)
        {
            return null;
        }

        // The leader's latest crossing at or before ours.
        long? before = null;
        var beforeLap = 0;

        for (var lap = leaderLaps; lap >= 1; lap--)
        {
            if (RaceGaps.CrossingMs(leaderLapTimes, leaderLaps, leaderElapsedMs, lap) is { } t && t <= carElapsedMs)
            {
                before = t;
                beforeLap = lap;
                break;
            }
        }

        if (before is not { } leaderBefore)
        {
            return null;
        }

        // WHY (23 Nov 2026, bug hunt 24): a leader that has taken the checkered
        // flag will not cross again. The old code still projected its next
        // crossing as its last one plus its usual lap, so a backmarker
        // finishing a couple of seconds ahead of where a finished leader
        // "would" be heard "Leader behind" in the call for their own
        // finishing lap, about a leader whose race was already over. A
        // crossing that really happened is still used as before.
        if (leaderFinished && beforeLap >= leaderLaps)
        {
            return null;
        }

        // Its next crossing: as it happened, if it has, else its usual lap on.
        var next = beforeLap < leaderLaps
            ? RaceGaps.CrossingMs(leaderLapTimes, leaderLaps, leaderElapsedMs, beforeLap + 1) ?? leaderBefore + leaderPace
            : leaderBefore + leaderPace;

        var behind = next - carElapsedMs;

        if (behind <= 0 || behind > Math.Max(CloseMs, closing))
        {
            return null;
        }

        var lapsDown = RaceGaps.Compute(
            carLaps, carElapsedMs, leaderLaps, leaderElapsedMs,
            lap => RaceGaps.CrossingMs(leaderLapTimes, leaderLaps, leaderElapsedMs, lap)).LapsDown;

        return lapsDown + 1;
    }

    /// <summary>The middle of the last few flying laps (the start lap left out), or null for none.</summary>
    private static long? Pace(IReadOnlyList<long> lapTimes)
    {
        var recent = lapTimes.Skip(1).Where(ms => ms > 0).TakeLast(PaceLaps).OrderBy(ms => ms).ToList();
        return recent.Count == 0 ? null : recent[recent.Count / 2];
    }
}
