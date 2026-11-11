namespace HeatCaller;

/// <summary>
/// What a driver's saved races say about tonight's lap or tonight's race
/// (11 Nov 2026, voice speed plan, phase 4 #4) - for the calls that know a
/// driver's history: quicker than last time out, back on form, and their
/// milestones at the club. See BestEverHere for the fourth, their quickest
/// ever on the layout.
/// </summary>
/// <remarks>
/// Only races that count: never a demo or an imported result
/// (SessionRecord.IsNotScored), the same races the club's records are kept
/// from. By transponder, as BestEverHere is - the saved rows carry it.
///
/// A NIGHT, NOT A RACE. "Last time out" is the driver's last night on the
/// layout, not their last heat - tonight's earlier heats are tonight. Nights
/// are told apart by the local date the race started on.
/// </remarks>
internal static class DriverHistory
{
    /// <summary>How many of a driver's nights on the layout make "a while" for Back on form.</summary>
    internal const int SlumpNights = 3;

    /// <summary>The races that count, for a driver: not demos, not imported, with them in it.</summary>
    private static IEnumerable<(SessionRecord Record, SessionResultEntry Entry)> TheirRaces(IEnumerable<SessionRecord> history, string transponder)
    {
        foreach (var record in history)
        {
            if (record.IsNotScored())
            {
                continue;
            }

            foreach (var entry in record.Results)
            {
                if (string.Equals(entry.Transponder, transponder, StringComparison.OrdinalIgnoreCase))
                {
                    yield return (record, entry);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// The driver's best lap on each night they ran the layout before
    /// <paramref name="tonight"/>, newest night first.
    /// </summary>
    internal static IReadOnlyList<(DateOnly Night, long BestMs)> NightsOn(
        IEnumerable<SessionRecord> history, string layout, string transponder, DateOnly tonight)
    {
        if (string.IsNullOrWhiteSpace(layout) || string.IsNullOrWhiteSpace(transponder))
        {
            return Array.Empty<(DateOnly, long)>();
        }

        var nights = new Dictionary<DateOnly, long>();

        foreach (var (record, entry) in TheirRaces(history, transponder))
        {
            if (entry.FastLapMs <= 0 || !string.Equals(record.TrackLayoutName, layout, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var night = DateOnly.FromDateTime(record.StartedAtUtc.ToLocalTime());

            if (night >= tonight)
            {
                continue;
            }

            nights[night] = nights.TryGetValue(night, out var was) ? Math.Min(was, entry.FastLapMs) : entry.FastLapMs;
        }

        return nights.OrderByDescending(n => n.Key).Select(n => (n.Key, n.Value)).ToList();
    }

    /// <summary>Their best lap on their last night on the layout before tonight, or null for none.</summary>
    internal static long? LastTimeOut(IReadOnlyList<(DateOnly Night, long BestMs)> nights) =>
        nights.Count > 0 ? nights[0].BestMs : null;

    /// <summary>
    /// The mark a lap must beat to be "back on form": the best of their last
    /// <see cref="SlumpNights"/> nights on the layout, when every one of
    /// those nights was slower than their best before them. Null when they
    /// have not had that run of slower nights - or not enough nights to say.
    /// </summary>
    internal static long? SlumpBest(IReadOnlyList<(DateOnly Night, long BestMs)> nights)
    {
        if (nights.Count <= SlumpNights)
        {
            return null;
        }

        var recent = nights.Take(SlumpNights).Min(n => n.BestMs);
        var before = nights.Skip(SlumpNights).Min(n => n.BestMs);

        return recent > before ? recent : null;
    }

    /// <summary>How many races that count the driver has run, before tonight's.</summary>
    internal static int RacesBefore(IEnumerable<SessionRecord> history, string transponder) =>
        string.IsNullOrWhiteSpace(transponder)
            ? 0
            : TheirRaces(history, transponder).Count(r => r.Record.SessionType == RaceSessionType.Race);

    /// <summary>How many laps the driver has done at the club, practice included, before this race.</summary>
    internal static int LapsBefore(IEnumerable<SessionRecord> history, string transponder) =>
        string.IsNullOrWhiteSpace(transponder) ? 0 : TheirRaces(history, transponder).Sum(r => Math.Max(0, r.Entry.Laps));

    /// <summary>A race number worth saying: the 50th, then every 100th.</summary>
    internal static bool IsRaceMilestone(int raceNumber) =>
        raceNumber == 50 || (raceNumber >= 100 && raceNumber % 100 == 0);

    /// <summary>Laps at the club are called every thousand.</summary>
    internal const int LapMilestoneEvery = 1000;

    /// <summary>The thousand reached on this lap, or null - <paramref name="lapsBefore"/> at the club, plus <paramref name="lapsTonight"/> in this race.</summary>
    internal static int? LapMilestoneReached(int lapsBefore, int lapsTonight)
    {
        var total = lapsBefore + lapsTonight;

        return lapsTonight > 0 && total > 0 && total % LapMilestoneEvery == 0 ? total : null;
    }
}
