using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// What one driver's saved result should say after a correction - as
/// TOTALS, not as a change to apply on top of the last one. Setting every
/// number back to zero and the outcome back to empty gives the result
/// exactly as the race recorded it.
/// </summary>
/// <param name="Transponder">The transponder the result is saved under.</param>
/// <param name="AddedLaps">Laps added (positive) or taken away (negative) - a missed or a phantom crossing.</param>
/// <param name="TimePenaltyMs">Added to the driver's total time. Never negative.</param>
/// <param name="LapPenalty">Laps taken off as a penalty. Never negative.</param>
/// <param name="Outcome">"" for a finisher, ResultEditor.Dnf or ResultEditor.Dq.</param>
public sealed record ResultAdjustment(string Transponder, int AddedLaps, long TimePenaltyMs, int LapPenalty, string Outcome);

/// <summary>
/// Corrects a race result after it has been saved, and puts the field back
/// in order.
///
/// A pure piece of arithmetic over one SessionRecord, deliberately with no
/// store, dialog or thread in it: the laptop's Edit Result dialog and the
/// phone's result.adjust both come through here, so the two can never
/// disagree about what a five-second penalty does to the order.
///
/// THE ORDER is the race engine's own rule - most laps first, then the
/// shorter total time (RaceControlSession.GetOrderedStandings) - with two
/// groups added underneath it: everyone marked DNF goes after every
/// finisher, and everyone disqualified after them. Ties keep the order
/// the race itself recorded.
///
/// A DQ also counts NO laps. Everything that scores a saved result
/// already treats a no-lap row as a non-finisher - the championship gives
/// it no points (SeasonStore), and it sorts to the bottom of qualifying -
/// so zero laps is what makes a disqualification mean something everywhere
/// at once, without every reader having to learn a new word. The laps the
/// driver really did are kept in AsRaced.
///
/// A PENALTY never touches the lap-by-lap times or the fastest lap: they
/// are what the timing system saw, and a ruling does not change how fast
/// somebody went. Taking a phantom crossing off is the other thing - a
/// correction to what the timing saw - so it takes that crossing's split
/// with it, exactly as RaceControlSession.AdjustLaps does during the race.
/// See RebuildLapTimes. Badges already awarded are not re-evaluated.
/// </summary>
public static class ResultEditor
{
    public const string Dnf = "DNF";
    public const string Dq = "DQ";

    /// <summary>The largest correction accepted in either direction - far past anything real, short of a typo.</summary>
    public const int MaxLapChange = 500;

    /// <summary>An hour. A bigger time penalty than that is a typo, not a ruling.</summary>
    public const long MaxTimePenaltyMs = 60L * 60 * 1000;

    /// <summary>
    /// "DNF", "DQ" or "" for anything that means an ordinary finish; null
    /// for something that is none of those. Case and a few common spellings
    /// ("dsq", "finished") are accepted, because a phone sends what it sends.
    /// </summary>
    public static string? NormaliseOutcome(string? outcome)
    {
        var text = (outcome ?? string.Empty).Trim();

        if (text.Length == 0
            || text.Equals("finished", StringComparison.OrdinalIgnoreCase)
            || text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (text.Equals(Dnf, StringComparison.OrdinalIgnoreCase))
        {
            return Dnf;
        }

        if (text.Equals(Dq, StringComparison.OrdinalIgnoreCase) || text.Equals("DSQ", StringComparison.OrdinalIgnoreCase))
        {
            return Dq;
        }

        return null;
    }

    /// <summary>What a result says now, as an adjustment - the starting point for editing it.</summary>
    public static ResultAdjustment CurrentOf(SessionResultEntry entry) =>
        new(entry.Transponder, entry.AddedLaps, entry.TimePenaltyMs, entry.LapPenalty, NormaliseOutcome(entry.Outcome) ?? string.Empty);

    /// <summary>
    /// Why these adjustments cannot be applied to this session, or null
    /// when they can. Checked by both callers before anything changes.
    /// </summary>
    public static string? Validate(SessionRecord record, IReadOnlyList<ResultAdjustment> adjustments)
    {
        if (record.SessionType != RaceSessionType.Race || record.IsDemoSession())
        {
            return "Only a real race's result can be corrected - not practice, and not a demo race.";
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var adjustment in adjustments)
        {
            var entry = FindEntry(record, adjustment.Transponder);

            if (entry is null)
            {
                return "One of the drivers in that correction is not in this result.";
            }

            if (!seen.Add(entry.Transponder))
            {
                return $"{NameOf(entry)} is in that correction twice.";
            }

            if (Math.Abs(adjustment.AddedLaps) > MaxLapChange)
            {
                return $"{NameOf(entry)}: laps can be changed by at most {MaxLapChange}.";
            }

            if (adjustment.TimePenaltyMs < 0 || adjustment.TimePenaltyMs > MaxTimePenaltyMs)
            {
                return $"{NameOf(entry)}: a time penalty has to be between 0 and 3600 seconds.";
            }

            if (adjustment.LapPenalty < 0 || adjustment.LapPenalty > MaxLapChange)
            {
                return $"{NameOf(entry)}: a lap penalty has to be between 0 and {MaxLapChange}.";
            }

            if (NormaliseOutcome(adjustment.Outcome) is null)
            {
                return $"{NameOf(entry)}: the outcome has to be finished, DNF or DQ.";
            }
        }

        return null;
    }

    /// <summary>
    /// One line per driver whose result these adjustments would change, in
    /// plain words - "time penalty 0s -> 5s". Empty when nothing would
    /// change, which both callers treat as "nothing to save".
    /// </summary>
    public static List<(SessionResultEntry Entry, string Change)> DescribeChanges(
        SessionRecord record, IReadOnlyList<ResultAdjustment> adjustments)
    {
        var changes = new List<(SessionResultEntry Entry, string Change)>();

        foreach (var adjustment in adjustments)
        {
            if (FindEntry(record, adjustment.Transponder) is not { } entry)
            {
                continue;
            }

            var now = CurrentOf(entry);
            var wanted = adjustment with { Outcome = NormaliseOutcome(adjustment.Outcome) ?? string.Empty };
            var parts = new List<string>();

            if (now.AddedLaps != wanted.AddedLaps)
            {
                parts.Add($"laps added {Signed(now.AddedLaps)} -> {Signed(wanted.AddedLaps)}");
            }

            if (now.TimePenaltyMs != wanted.TimePenaltyMs)
            {
                parts.Add($"time penalty {Seconds(now.TimePenaltyMs)} -> {Seconds(wanted.TimePenaltyMs)}");
            }

            if (now.LapPenalty != wanted.LapPenalty)
            {
                parts.Add($"lap penalty {now.LapPenalty} -> {wanted.LapPenalty}");
            }

            if (!string.Equals(now.Outcome, wanted.Outcome, StringComparison.Ordinal))
            {
                parts.Add($"{OutcomeText(now.Outcome)} -> {OutcomeText(wanted.Outcome)}");
            }

            if (parts.Count > 0)
            {
                changes.Add((entry, string.Join(", ", parts)));
            }
        }

        return changes;
    }

    /// <summary>
    /// The result as it would be with these adjustments, in its new order
    /// and with new positions - as COPIES. The record itself is not touched;
    /// this is what the live preview on the laptop and the phone's
    /// confirmation show.
    /// </summary>
    public static List<SessionResultEntry> Preview(SessionRecord record, IReadOnlyList<ResultAdjustment> adjustments)
    {
        var copies = record.Results.Select(Clone).ToList();

        foreach (var adjustment in adjustments)
        {
            if (copies.FirstOrDefault(e => string.Equals(e.Transponder, adjustment.Transponder, StringComparison.OrdinalIgnoreCase)) is { } copy)
            {
                ApplyTo(copy, adjustment);
            }
        }

        return Reordered(copies);
    }

    /// <summary>
    /// Applies the adjustments to the record itself, re-orders it, renumbers
    /// the positions and logs every change in its Edits list. Returns how
    /// many drivers' results changed; zero means the record was left exactly
    /// as it was. The caller takes the Snapshot BEFORE calling this, saves
    /// the record (SessionHistoryStore.SaveEdited), and captures the undo
    /// only once that save has worked - an undo captured for a save that
    /// failed would undo a correction that never happened.
    /// </summary>
    public static int Apply(SessionRecord record, IReadOnlyList<ResultAdjustment> adjustments, string reason, string source)
    {
        var changes = DescribeChanges(record, adjustments);

        if (changes.Count == 0)
        {
            return 0;
        }

        foreach (var adjustment in adjustments)
        {
            if (FindEntry(record, adjustment.Transponder) is { } entry)
            {
                ApplyTo(entry, adjustment);
            }
        }

        record.Results = Reordered(record.Results);

        var now = DateTime.UtcNow;

        foreach (var (entry, change) in changes)
        {
            record.Edits.Add(new ResultEdit
            {
                AtUtc = now,
                Transponder = entry.Transponder,
                DriverName = NameOf(entry),
                Change = change,
                Reason = reason.Trim(),
                Source = source,
            });
        }

        return changes.Count;
    }

    /// <summary>The result and its correction log as they are now, for an undo to put back. See Restore.</summary>
    public static string Snapshot(SessionRecord record) =>
        JsonSerializer.Serialize(new SnapshotData { Results = record.Results, Edits = record.Edits });

    /// <summary>Puts back what Snapshot recorded. The caller saves.</summary>
    public static void Restore(SessionRecord record, string snapshot)
    {
        var data = JsonSerializer.Deserialize<SnapshotData>(snapshot);

        if (data is null)
        {
            return;
        }

        record.Results = data.Results ?? new List<SessionResultEntry>();
        record.Edits = data.Edits ?? new List<ResultEdit>();
    }

    /// <summary>"DNF", "DQ", or "finished".</summary>
    public static string OutcomeText(string? outcome) =>
        string.IsNullOrEmpty(outcome) ? "finished" : outcome;

    private sealed class SnapshotData
    {
        public List<SessionResultEntry>? Results { get; set; }
        public List<ResultEdit>? Edits { get; set; }
    }

    private static SessionResultEntry? FindEntry(SessionRecord record, string transponder) =>
        record.Results.FirstOrDefault(e => string.Equals(e.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Sets one result to what the adjustment says, always working from the
    /// figures the race recorded - never from an earlier correction.
    /// </summary>
    private static void ApplyTo(SessionResultEntry entry, ResultAdjustment adjustment)
    {
        // Taken the first time the result is corrected, and kept from then
        // on: it is the only copy of what the race really said.
        var raced = entry.AsRaced ?? new ResultAsRaced
        {
            Position = entry.Position,
            Laps = entry.Laps,
            ElapsedMs = entry.ElapsedMs,
            Status = entry.Status ?? string.Empty,
            LapTimesMs = entry.LapTimesMs.ToList(),
            FastLapMs = entry.FastLapMs,
        };

        // A result corrected by a version that kept no lap list has an
        // AsRaced without one. That version never changed the entry's lap
        // times either, so what is on the entry now is what the race
        // recorded - take it across here, before anything below edits it.
        if (raced.LapTimesMs.Count == 0 && entry.LapTimesMs.Count > 0)
        {
            raced.LapTimesMs = entry.LapTimesMs.ToList();
        }

        if (raced.FastLapMs == 0)
        {
            raced.FastLapMs = entry.FastLapMs;
        }

        entry.AddedLaps = adjustment.AddedLaps;
        entry.TimePenaltyMs = Math.Max(0, adjustment.TimePenaltyMs);
        entry.LapPenalty = Math.Max(0, adjustment.LapPenalty);
        entry.Outcome = NormaliseOutcome(adjustment.Outcome) ?? string.Empty;

        if (!entry.IsCorrected())
        {
            // Every correction taken back off: the result is the race's
            // again, and carries no trace of having been edited except the
            // log on the session.
            entry.Laps = raced.Laps;
            entry.ElapsedMs = raced.ElapsedMs;
            entry.Status = raced.Status;
            entry.LapTimesMs = raced.LapTimesMs.ToList();
            entry.FastLapMs = raced.FastLapMs;

            // The place the race gave it, too. Reordered breaks a tie on the
            // raced position, which for an uncorrected result is Position -
            // and this one's Position is still wherever the correction being
            // taken back had put it, so a driver restored level with another
            // could come back on the wrong side of them.
            entry.Position = raced.Position;
            entry.AsRaced = null;
            return;
        }

        entry.AsRaced = raced;

        // A crossing taken off by hand was never a lap, so its time goes
        // with it. A lap PENALTY is the other thing entirely - a ruling,
        // not a correction to what the timing saw - so it takes the count
        // down and leaves every lap time alone.
        var (lapTimes, fastLapMs) = RebuildLapTimes(raced, Math.Max(0, -entry.AddedLaps));
        entry.LapTimesMs = lapTimes;
        entry.FastLapMs = fastLapMs;

        entry.Laps = entry.Outcome == Dq
            ? 0
            : Math.Max(0, raced.Laps + entry.AddedLaps - entry.LapPenalty);

        entry.ElapsedMs = Math.Max(0, raced.ElapsedMs + entry.TimePenaltyMs);

        // Status is what the results page, the CSV export and the relay
        // already show - "active" is hidden, anything else is printed - so
        // putting the outcome here makes a DNF or DQ visible everywhere
        // without any of them changing.
        entry.Status = entry.Outcome.Length > 0 ? entry.Outcome : raced.Status;
    }

    /// <summary>
    /// The lap list and the best lap after <paramref name="removals"/>
    /// phantom crossings have been taken off the raced one, always rebuilt
    /// from the race's own figures rather than from the last correction.
    ///
    /// The same arithmetic RaceControlSession.AdjustLaps does during the
    /// race, and for the same reason: a crossing removed by hand had a
    /// split behind it, and left in place a 3.5s "lap" still sets the
    /// driver's best, still holds the race's fastest lap, and still sits in
    /// the best-of-N window qualifying seeds the mains from. The correction
    /// made in Race Control did all of this and the one made afterwards in
    /// Edit Result did not, so the same mistake got two different answers
    /// depending on when it was noticed.
    ///
    /// The last lap is FOLDED into the one before it rather than thrown
    /// away: two crossings a moment apart were one real lap, and summing
    /// them gives that lap back. Laps added by hand have no time of their
    /// own, so they come off first and cost the list nothing.
    /// </summary>
    private static (List<long> LapTimesMs, long FastLapMs) RebuildLapTimes(ResultAsRaced raced, int removals)
    {
        var times = raced.LapTimesMs.ToList();

        if (removals <= 0 || times.Count == 0)
        {
            return (times, raced.FastLapMs);
        }

        var laps = raced.Laps;
        var mergedAny = false;

        for (var i = 0; i < removals && laps > 0; i++)
        {
            if (laps > times.Count)
            {
                // Counted but never timed - a lap added by hand, here or
                // during the race. Nothing to fold.
                laps--;
                continue;
            }

            var last = times.Count - 1;

            if (times.Count >= 2)
            {
                times[last - 1] += times[last];
            }

            times.RemoveAt(last);
            laps--;
            mergedAny = true;
        }

        if (!mergedAny)
        {
            return (times, raced.FastLapMs);
        }

        // Index 0 is not a lap - it is the run from the green flag to the
        // first crossing, and never anybody's best.
        var rebuilt = times.Where((t, i) => i > 0 && t > 0).DefaultIfEmpty(0).Min();

        // Folding a lap into the one before it only ever makes laps longer,
        // so the best can only stay the same or get slower. A saved result
        // does not carry which laps ran through a crash recovery, and the
        // race left those out of the best; taking the slower of the two is
        // what stops one becoming a driver's best the moment a phantom
        // crossing comes off. A race that recorded no best keeps none.
        return (times, raced.FastLapMs > 0 ? Math.Max(raced.FastLapMs, rebuilt) : 0);
    }

    /// <summary>The engine's order - most laps, then least time - with DNFs (and no-lap rows) and then DQs underneath. Positions renumbered 1..N.</summary>
    private static List<SessionResultEntry> Reordered(IEnumerable<SessionResultEntry> results)
    {
        var ordered = results
            .OrderBy(Group)
            .ThenByDescending(e => e.Laps)
            .ThenBy(e => e.ElapsedMs)

            // A tie keeps the order the race itself recorded.
            .ThenBy(e => e.AsRaced?.Position ?? e.Position)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Position = i + 1;
        }

        return ordered;
    }

    /// <summary>
    /// Finishers 0, DNFs 1, DQs 2 - and a row with no laps at all and no
    /// outcome goes in with the DNFs. A car on the entry list that never
    /// crossed the line (every grid car is on the board from a hand-released
    /// green) is a non-finisher too, and sorted with the finishers it came
    /// out ABOVE a DNF who had done laps. The championship counts a no-lap
    /// row as a place (SeasonStore.ComputeStandings, on the promise that
    /// such rows always sit at the bottom), so marking someone DNF cost
    /// them a place's points to a car that never turned a lap. In the DNF
    /// group the laps decide, so the DNF with laps stays ahead of it. With
    /// no DNF in the race this changes nothing: a no-lap row already sorted
    /// after every finisher and before every DQ.
    /// </summary>
    private static int Group(SessionResultEntry entry) =>
        entry.Outcome == Dq ? 2
        : entry.Outcome == Dnf ? 1
        : entry.Laps <= 0 ? 1
        : 0;

    /// <summary>A deep copy, through the same serializer the history file uses - so no field is ever forgotten.</summary>
    private static SessionResultEntry Clone(SessionResultEntry entry) =>
        JsonSerializer.Deserialize<SessionResultEntry>(JsonSerializer.Serialize(entry)) ?? new SessionResultEntry();

    private static string NameOf(SessionResultEntry entry) =>
        string.IsNullOrWhiteSpace(entry.DriverName) ? entry.Transponder : entry.DriverName;

    private static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();

    private static string Seconds(long ms) => $"{ms / 1000.0:0.###}s";
}
