using System.Globalization;

namespace HeatCaller;

/// <summary>How much quicker (or slower) a class is running now than in its first race tonight.</summary>
/// <param name="FirstRace">The class's first race tonight, which the change is counted from.</param>
/// <param name="DeltaMs">How much each driver's middle lap has changed between their first race tonight and their latest -
/// the middle of those changes. Negative when the track has got quicker.</param>
/// <param name="Drivers">How many drivers that was worked out from.</param>
public sealed record TrackEvolution(string ClassName, string FirstRace, string LatestRace, long DeltaMs, int Drivers, int Races)
{
    /// <summary>"Track 0.31s quicker since Q1 Heat 1", "Track 0.12s slower since Q1 Heat 1", or "Track the same as in Q1 Heat 1".</summary>
    public string Words
    {
        get
        {
            var by = Math.Abs(DeltaMs) / 1000.0;

            if (by < 0.005)
            {
                return $"Track the same as in {FirstRace}";
            }

            var amount = by.ToString("0.00", CultureInfo.InvariantCulture);
            return DeltaMs < 0
                ? $"Track {amount}s quicker since {FirstRace}"
                : $"Track {amount}s slower since {FirstRace}";
        }
    }
}

/// <summary>The fastest lap in one class tonight, and who set it.</summary>
public sealed record FastestTonight(string ClassName, string DriverName, long LapMs, string Race);

/// <summary>
/// Tonight at the track, from the races already saved: how the track has
/// changed for each class since its first race of the night (track
/// evolution - the grip coming up as the rubber goes down, or going off as
/// it gets dusty or wet), and the fastest lap in each class.
///
/// "Tonight" is the race night the same way the transponder health counts
/// it: from 6am to 6am, so a club night that runs past midnight is one
/// night. A race counts for its class through the round it was run for;
/// a freeform race with no round has no class and is left out.
///
/// The track is measured through the drivers: each driver's middle flying
/// lap in their first race tonight against the same in their latest, and
/// the middle of those changes across the class. The same drivers on both
/// sides, because comparing one heat with another compares the drivers in
/// them - after heats are grouped by pace, the last heat is quicker however
/// the track is. Middle laps rather than best laps, because one flyer says
/// more about a driver than the track; and rather than the mean, because
/// one crash lap would move it a second.
/// </summary>
public static class TonightAtTheTrack
{
    private static readonly TimeSpan NightTurnsAt = TimeSpan.FromHours(6);

    /// <summary>Fewer flying laps than this from a driver in a race and it says nothing about the track.</summary>
    internal const int LeastLaps = 3;

    /// <summary>Fewer drivers than this to compare and the change is one driver's night, not the track's.</summary>
    internal const int LeastDrivers = 2;

    /// <summary>The night <paramref name="local"/> belongs to, as its date - 6am to 6am.</summary>
    internal static DateTime NightOf(DateTime local) => (local - NightTurnsAt).Date;

    /// <summary>Tonight's saved races (not demos, not practice), oldest first, each with its round's class.</summary>
    internal static List<(SessionRecord Session, string ClassName)> Races(
        IEnumerable<SessionRecord> history, Func<string, RoundDefinition?> roundOf, DateTime nowLocal)
    {
        var tonight = NightOf(nowLocal);

        return history
            .Where(s => !s.IsDemoSession() && s.SessionType != RaceSessionType.Practice)
            .Where(s => NightOf(s.StartedAtUtc.ToLocalTime()) == tonight)
            .Select(s => (Session: s, Round: string.IsNullOrEmpty(s.RoundId) ? null : roundOf(s.RoundId)))
            .Where(x => x.Round is not null && !string.IsNullOrWhiteSpace(x.Round.ClassName))
            .OrderBy(x => x.Session.StartedAtUtc)
            .Select(x => (x.Session, x.Round!.ClassName.Trim()))
            .ToList();
    }

    /// <summary>Each class's change since its first race tonight - for classes with drivers who have raced twice or more.</summary>
    public static IReadOnlyList<TrackEvolution> Evolution(
        IEnumerable<SessionRecord> history, Func<string, RoundDefinition?> roundOf, DateTime nowLocal)
    {
        var list = new List<TrackEvolution>();

        foreach (var group in Races(history, roundOf, nowLocal).GroupBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase))
        {
            var races = group.Select(r => r.Session).ToList();

            if (races.Count < 2)
            {
                continue;
            }

            // Each driver's middle lap in each race they had laps in, in
            // running order: their first against their latest.
            var changes = new List<long>();
            var drivers = races
                .SelectMany(race => race.Results
                    .Where(r => !string.IsNullOrWhiteSpace(r.Transponder) && !GhostService.IsGhost(r.Transponder))
                    .Select(r => (r.Transponder, Middle: MiddleLap(r))))
                .Where(x => x.Middle > 0)
                .GroupBy(x => x.Transponder, StringComparer.OrdinalIgnoreCase);

            foreach (var driver in drivers)
            {
                var laps = driver.Select(x => x.Middle).ToList();

                if (laps.Count >= 2)
                {
                    changes.Add(laps[^1] - laps[0]);
                }
            }

            if (changes.Count < LeastDrivers)
            {
                continue;
            }

            changes.Sort();

            list.Add(new TrackEvolution(group.Key, NameOf(races[0], roundOf), NameOf(races[^1], roundOf),
                changes[changes.Count / 2], changes.Count, races.Count));
        }

        return list.OrderBy(e => e.ClassName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// The fastest lap in each class tonight, quickest class first. Not a
    /// ghost's, not a disqualified result's, and not a driver kept out of
    /// records (<paramref name="excluded"/>, by transponder). A rental's
    /// counts: it was raced tonight, even if it can't hold a track record.
    /// </summary>
    public static IReadOnlyList<FastestTonight> FastestLaps(
        IEnumerable<SessionRecord> history, Func<string, RoundDefinition?> roundOf, DateTime nowLocal,
        Func<string, bool>? excluded = null)
    {
        var list = new List<FastestTonight>();

        foreach (var group in Races(history, roundOf, nowLocal).GroupBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase))
        {
            var best = group
                .SelectMany(r => r.Session.Results
                    .Where(x => x.FastLapMs > 0 && !GhostService.IsGhost(x.Transponder))
                    .Where(x => ResultEditor.NormaliseOutcome(x.Outcome) != ResultEditor.Dq)
                    .Where(x => excluded?.Invoke(x.Transponder) != true)
                    .Select(x => (Result: x, r.Session)))
                .OrderBy(x => x.Result.FastLapMs)
                .FirstOrDefault();

            if (best.Result is null)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(best.Result.DriverName) ? best.Result.Transponder : best.Result.DriverName;
            list.Add(new FastestTonight(group.Key, name, best.Result.FastLapMs, NameOf(best.Session, roundOf)));
        }

        return list.OrderBy(f => f.LapMs).ToList();
    }

    /// <summary>A driver's middle flying lap in one race, or 0 when they had too few to say.</summary>
    internal static long MiddleLap(SessionResultEntry result)
    {
        var laps = result.LapTimesMs
            .Skip(1)
            .Where(ms => ms > 0)
            .OrderBy(ms => ms)
            .ToList();

        return laps.Count < LeastLaps ? 0 : laps[laps.Count / 2];
    }

    /// <summary>The race's own label, or its round's when it has none.</summary>
    private static string NameOf(SessionRecord session, Func<string, RoundDefinition?> roundOf)
    {
        if (!string.IsNullOrWhiteSpace(session.Label))
        {
            return session.Label.Trim();
        }

        return !string.IsNullOrEmpty(session.RoundId) && roundOf(session.RoundId) is { } round && !string.IsNullOrWhiteSpace(round.RoundLabel)
            ? round.RoundLabel.Trim()
            : "the first race";
    }

    /// <summary>A lap time as the Big Screen shows one: "12.345".</summary>
    public static string Lap(long ms) => (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
}
