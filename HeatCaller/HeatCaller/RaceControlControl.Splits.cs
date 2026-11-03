namespace HeatCaller;

/// <summary>
/// Race Control's split-loop calls: a sector quicker than anyone has gone
/// this session (Fastest sector), and a car crossing the split loop up on
/// its own best lap (Up at the split) or on the track record lap (Record
/// pace at the split). Each is switched on, and worded, on Voice -&gt;
/// Announcements; all start off.
///
/// And since 22 Oct 2026 the spoken split times: the split time read out,
/// the coached driver's up-or-down (Coach at the split, on the board's
/// right-click), and two cars' gap at the split. Those three read in
/// practice and not in races until a club ticks them. The fourth, the
/// sectors at the flag, is RaceControlControl.AfterRace's.
///
/// Here rather than in the announcer because the session and the track
/// record are Race Control's - the same as CheckRecordPace.
/// </summary>
public partial class RaceControlControl
{
    /// <summary>A driver gets at most one split call in this many of their laps.</summary>
    private const int SplitCallEveryLaps = 5;

    /// <summary>How far up at the split counts as worth saying: a tenth.</summary>
    private const long SplitCallLeastUpMs = 100;

    private int _splitCallsGeneration = -1;

    /// <summary>A car crossed a split loop: the board's At Split column, then the calls.</summary>
    private void OnSplitCrossed(RacerStanding standing, TimingPoint point)
    {
        RenderLeaderboard();

        try
        {
            CallSplitPace(standing, point);

            // The spoken split times (22 Oct 2026): the coached driver's
            // up-or-down first - somebody asked for it - then a fight timed
            // at the split, then the plain time. The coached driver hears
            // their delta instead of their time, not both.
            var coached = CallSplitCoach(standing, point);
            CallSplitBattle(standing, point);

            if (!coached)
            {
                CallSplitTime(standing, point);
            }
        }
        catch (Exception ex)
        {
            // A call is never worth losing the board over.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [split] Couldn't work out a split call: {ex.Message}");
        }
    }

    /// <summary>A pair's split battle line is said at most once in this many of the chasing car's laps.</summary>
    private const int SplitBattleEveryLaps = 5;

    /// <summary>Under a second apart at the split is a fight - the close battle's own line (AnnouncementService.CheckCloseBattles).</summary>
    private const long SplitBattleCloseMs = 1000;

    /// <summary>How far a pair's gap has to move before it is worth saying again: a tenth.</summary>
    private const long SplitBattleLeastChangeMs = 100;

    /// <summary>Each pair's last split battle line: the chasing car's lap and the gap that was said. Keyed "ahead|behind".</summary>
    private readonly Dictionary<string, (int Lap, long GapMs)> _splitBattleSaid = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>This crossing's time from the line to the split loop, or null when the read can't be trusted - the same tests CallSplitPace makes.</summary>
    private static long? ToTheSplit(RacerStanding standing, int slot)
    {
        var reads = standing.SplitReadsMs;

        if (slot < 0 || slot >= reads.Length || reads[slot].Count != 1 || standing.LastLapDeviceTimeMs <= 0)
        {
            return null;
        }

        var toHere = reads[slot][0] - standing.LastLapDeviceTimeMs;

        // Further round than the car's best whole lap and a half is a read
        // from another lap, or a car that stopped - not a split to read out.
        var longest = standing.FastestLapMs > 0 ? standing.FastestLapMs * 3 / 2 : 120_000;

        return toHere > 0 && toHere <= longest ? toHere : null;
    }

    /// <summary>
    /// Split time (22 Oct 2026): the car's time to the split, every Nth
    /// crossing - the cadence is the announcer's, per driver.
    /// </summary>
    private void CallSplitTime(RacerStanding standing, TimingPoint point)
    {
        if (GhostService.IsGhost(standing.TransponderId) || !_announcer.WillCall(AnnouncementEventType.SplitTime))
        {
            return;
        }

        if (ToTheSplit(standing, (int)point - 1) is { } toHere)
        {
            _announcer.NotifySplitTime(ForAnnouncer(standing), SplitLoopNames.Of(point), toHere);
        }
    }

    /// <summary>
    /// Split coach (22 Oct 2026): the coached driver against their own best
    /// lap at this split - Sectors.LiveDelta, the board's own At Split
    /// figure, so the room hears what the screen shows. Their best of this
    /// session only: a best from another night was set on another evening's
    /// grip, and the coaching is about the laps they are doing now. Silent
    /// until they have a best lap with a split time. True only when the
    /// up-or-down was actually handed to the announcer - otherwise (switched
    /// off for this session, no best yet) the driver still gets their plain
    /// split time.
    /// </summary>
    private bool CallSplitCoach(RacerStanding standing, TimingPoint point)
    {
        ForgetSplitCallsForANewRace();

        if (_coachAtSplit is not { } coached
            || !string.Equals(coached, standing.TransponderId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Sectors.LiveDelta(standing) is not { } live || live.Split != (int)point)
        {
            return false;
        }

        // Tones instead of words (22 Oct 2026): the up-or-down as a pitch,
        // played now rather than queued. The desk chose it for this driver
        // on the board, so it is not one of the club's announcement
        // switches - only the mute stops it.
        if (_coach is { Tones: true })
        {
            _announcer.PlayCoachTone(CoachingWords.ToneAtSplit(live.DeltaMs));
            return true;
        }

        if (_announcer.WillCall(AnnouncementEventType.SplitDelta))
        {
            _announcer.NotifySplitDelta(ForAnnouncer(standing), SplitLoopNames.Of(point), live.DeltaMs);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Ends the split coaching, with a console line (review, 22 Oct 2026):
    /// Race Control's Reset, and the end of a race that finished after the
    /// coach was picked - so a driver picked between races is coached
    /// through the race they were picked for, and one picked mid-race to
    /// the end of that race. Practice, which has no finish, keeps it until
    /// Reset or Stop coaching.
    ///
    /// Since the rest of the coaching came (22 Oct 2026) this ends all of
    /// it - the target pace, the consistency and the tones go with the
    /// split - but not the driver's target lap, which is theirs for the
    /// night (DriverTargets).
    /// </summary>
    private void EndSplitCoaching(string why)
    {
        if (_coach is not { } coach)
        {
            return;
        }

        _coach = null;

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [coach] Stopped coaching {CoachNameOf(coach.Transponder)} - {why}.");
    }

    /// <summary>
    /// Battle gap at the split (22 Oct 2026): this car and the one directly
    /// in front of it in the order, on the same lap, timed apart at this
    /// split loop - said when it is under a second, at most once in five of
    /// the chasing car's laps, and only when the gap has moved a tenth since
    /// it was last said for that pair. Not on lap one, which is a start.
    /// </summary>
    private void CallSplitBattle(RacerStanding standing, TimingPoint point)
    {
        ForgetSplitCallsForANewRace();

        if (!_announcer.WillCall(AnnouncementEventType.SplitBattle) || GhostService.IsGhost(standing.TransponderId))
        {
            return;
        }

        var ordered = _race.GetOrderedStandings();
        var index = -1;

        for (var i = 0; i < ordered.Count; i++)
        {
            if (string.Equals(ordered[i].TransponderId, standing.TransponderId, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }
        }

        if (index <= 0)
        {
            return;
        }

        var ahead = ordered[index - 1];
        var slot = (int)point - 1;

        if (GhostService.IsGhost(ahead.TransponderId)
            || ahead.Laps != standing.Laps
            || standing.Laps < 2
            || ToTheSplit(standing, slot) is null
            || ToTheSplit(ahead, slot) is null)
        {
            return;
        }

        var gap = SplitBattleGap(standing.SplitReadsMs[slot][0], ahead.SplitReadsMs[slot][0]);

        if (gap is not { } gapMs)
        {
            return;
        }

        var key = $"{ahead.TransponderId}|{standing.TransponderId}";

        if (_splitBattleSaid.TryGetValue(key, out var said)
            && !SplitBattleWorthSaying(said.Lap, said.GapMs, standing.Laps, gapMs))
        {
            return;
        }

        _splitBattleSaid[key] = (standing.Laps, gapMs);

        int? place = _race.IsPracticeMode ? null : index;
        _announcer.NotifySplitBattle(ForAnnouncer(ahead), standing.TransponderId, SplitLoopNames.Of(point), gapMs, place);
    }

    /// <summary>
    /// The gap at the split between the chasing car's read and the one in
    /// front's, or null when it is not a fight - the chaser read first (the
    /// order is out of date), or a second or more behind. Pure, for the tests.
    /// </summary>
    internal static long? SplitBattleGap(long chaserReadMs, long aheadReadMs)
    {
        var gap = chaserReadMs - aheadReadMs;

        return gap > 0 && gap < SplitBattleCloseMs ? gap : null;
    }

    /// <summary>
    /// Whether a pair already called at the split is worth calling again:
    /// five of the chaser's laps on, and the gap moved by a tenth. Pure, for
    /// the tests.
    /// </summary>
    internal static bool SplitBattleWorthSaying(int saidLap, long saidGapMs, int lapNow, long gapNowMs) =>
        lapNow - saidLap >= SplitBattleEveryLaps
        && Math.Abs(gapNowMs - saidGapMs) >= SplitBattleLeastChangeMs;

    /// <summary>Whether this driver is the one being coached at the split.</summary>
    private bool IsCoachedAtSplit(string transponder) =>
        _coachAtSplit is { } coached && string.Equals(coached, transponder, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Coaches this driver at the split, or stops - one driver at a time, so
    /// choosing another moves the coaching to them. Said in the console, the
    /// way a club can see why the lines started or stopped.
    ///
    /// Since 22 Oct 2026 it is one of the coaching options on Coach (name):
    /// unticking it leaves the driver's other options as they are, and the
    /// coaching ends only when nothing is left ticked.
    /// </summary>
    private void ToggleSplitCoach(string transponder, string name)
    {
        ForgetSplitCallsForANewRace();

        if (IsCoachedAtSplit(transponder))
        {
            _coach!.AtSplit = false;
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [split] Stopped coaching {name} at the split.");
            EndCoachingIfNothingTicked();
            return;
        }

        CoachFor(transponder).AtSplit = true;

        var why = _announcer.WillCall(AnnouncementEventType.SplitDelta)
            ? string.Empty
            : $" \"Split coach\" is switched off for {(_race.IsPracticeMode ? "practice" : "races")} on Voice > Race Calls, so nothing will be said until it is ticked.";

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [split] Coaching {name} at the split: up or down on their best each time they cross it.{why}");
    }

    /// <summary>A lap's sectors came in: the board, then the fastest-sector call - and the track record's sectors, when this was the record lap and its sectors came in after it.</summary>
    private void OnSectorsRecorded(RacerStanding standing, int lapIndex, long[] sectors)
    {
        RenderLeaderboard();

        try
        {
            CallFastestSector(standing, sectors);
            KeepLateRecordSectors(standing, lapIndex, sectors);
            KeepSectorRecords(standing, lapIndex, sectors);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [split] Couldn't work out a sector call: {ex.Message}");
        }
    }

    /// <summary>The quickest time through each sector this session, as the announcer has heard them.</summary>
    private long[]? _sessionSectorBest;

    /// <summary>The lap count each driver was on when they were last called at the split.</summary>
    private readonly Dictionary<string, int> _lastSplitCallLap = new(StringComparer.OrdinalIgnoreCase);

    private void ForgetSplitCallsForANewRace()
    {
        if (_splitCallsGeneration == _race.SessionGeneration)
        {
            return;
        }

        _splitCallsGeneration = _race.SessionGeneration;
        _sessionSectorBest = null;
        _lastSplitCallLap.Clear();

        // The pairs' memory goes with the race. The coached driver does NOT
        // (review, 22 Oct 2026): Stage and the green each start a new
        // generation, so clearing here dropped a coach picked between races
        // at the first split crossing of the race it was picked for. See
        // EndSplitCoaching for what does end it.
        _splitBattleSaid.Clear();
    }

    private RaceStanding ForAnnouncer(RacerStanding standing) => new()
    {
        Transponder = standing.TransponderId,
        Name = _profiles.Get(standing.TransponderId)?.DriverName,
        Laps = standing.Laps,
        FastLapMs = standing.FastestLapMs,
        ElapsedMs = standing.ElapsedMs,
        Status = "active",
        Id = standing.TransponderId,
    };

    /// <summary>
    /// A lap's sectors came in: a sector quicker than the session's best so
    /// far is called. The first sector times of a session only set the mark
    /// - they are fastest by default.
    /// </summary>
    private void CallFastestSector(RacerStanding standing, long[] sectors)
    {
        ForgetSplitCallsForANewRace();

        if (GhostService.IsGhost(standing.TransponderId))
        {
            return;
        }

        // A sector the mark may take: not a sliver of its own lap, and not a
        // quarter quicker than the mark already set - the same doubts the
        // sector records have (TrackLayout.OfferSectors). One split heard
        // early used to set a mark no real sector could beat, and the
        // fastest-sector call went quiet for the rest of the race.
        var lap = sectors.Sum();

        bool Believable(int i, long? mark) =>
            sectors[i] >= lap / (sectors.Length * 4.0)
            && (mark is not { } set || sectors[i] >= set * TrackLayout.LeastBelievableSectorShare);

        if (_sessionSectorBest is null || _sessionSectorBest.Length != sectors.Length)
        {
            // No mark yet is long.MaxValue: the first believable sector then
            // sets it without being called, as the first of a session should.
            _sessionSectorBest = Enumerable.Range(0, sectors.Length)
                .Select(i => Believable(i, null) ? sectors[i] : long.MaxValue)
                .ToArray();
            return;
        }

        var improved = -1;
        long by = 0;

        for (var i = 0; i < sectors.Length; i++)
        {
            var hasMark = _sessionSectorBest[i] != long.MaxValue;

            if (sectors[i] < _sessionSectorBest[i] && Believable(i, hasMark ? _sessionSectorBest[i] : null))
            {
                if (hasMark && _sessionSectorBest[i] - sectors[i] > by)
                {
                    by = _sessionSectorBest[i] - sectors[i];
                    improved = i;
                }

                _sessionSectorBest[i] = sectors[i];
            }
        }

        // The mark is kept either way; the call only when it will be said.
        if (improved >= 0 && _announcer.WillCall(AnnouncementEventType.FastestSector))
        {
            _announcer.NotifyFastestSector(ForAnnouncer(standing), improved + 1, sectors[improved]);
        }
    }

    /// <summary>
    /// A car crossed a split loop: up on the track record lap at this point
    /// (races only, for drivers who could take the record), or up on its own
    /// best lap by a tenth or more - whichever is switched on, the record
    /// first. At most once in five of the driver's laps.
    /// </summary>
    private void CallSplitPace(RacerStanding standing, TimingPoint point)
    {
        ForgetSplitCallsForANewRace();

        var id = standing.TransponderId;

        if (GhostService.IsGhost(id)
            || (_lastSplitCallLap.TryGetValue(id, out var lastLap) && standing.Laps - lastLap < SplitCallEveryLaps))
        {
            return;
        }

        var slot = (int)point - 1;
        var reads = standing.SplitReadsMs;

        if (slot < 0 || slot >= reads.Length || reads[slot].Count != 1 || standing.LastLapDeviceTimeMs <= 0)
        {
            return;
        }

        var toHere = reads[slot][0] - standing.LastLapDeviceTimeMs;
        var split = SplitLoopNames.Of(point);

        if (_announcer.WillCall(AnnouncementEventType.SplitRecordPace) && RecordUpAtSplit(standing, slot, toHere) is { } recordUp)
        {
            _lastSplitCallLap[id] = standing.Laps;
            _announcer.NotifySplitRecordPace(ForAnnouncer(standing), split, recordUp);
            return;
        }

        // Up on their own best - once they have one with sectors to be up on.
        if (_announcer.WillCall(AnnouncementEventType.SplitPace)
            && Sectors.LapsOf(standing).Skip(1).Any()
            && Sectors.LiveDelta(standing) is { } live
            && live.Split == slot + 1
            && live.DeltaMs <= -SplitCallLeastUpMs)
        {
            _lastSplitCallLap[id] = standing.Laps;
            _announcer.NotifySplitPace(ForAnnouncer(standing), split, -live.DeltaMs);
        }
    }

    /// <summary>
    /// How far up a car is on the track record lap at this split loop, or
    /// null: not a race, nobody who can hold the record, no record with
    /// sectors cut the same way, or not up on it.
    /// </summary>
    private long? RecordUpAtSplit(RacerStanding standing, int slot, long toHere)
    {
        if (_race.IsPracticeMode || _race.IsDemoMode
            || _profiles.Get(standing.TransponderId) is { ExcludeFromRecords: true } or { IsRental: true })
        {
            return null;
        }

        var trackName = SelectedTrackName();

        // The record's sectors as cut by the loops where they are now - a
        // record taken with a loop somewhere else says nothing about here.
        if (string.IsNullOrWhiteSpace(trackName)
            || _trackLayouts.All.FirstOrDefault(t => t.Name == trackName)?.RecordSectorsAsCutNow(standing.SplitReadsMs.Length + 1) is not { } record)
        {
            return null;
        }

        var recordToHere = record.Take(slot + 1).Sum();

        // As Sectors.LiveDelta judges a gap: a read further round than a
        // whole record lap belongs to another lap, and one a quarter
        // quicker to here than the record is a misread (or a record kept
        // from before loops' places were) - not pace to call.
        if (toHere <= 0 || toHere > record.Sum() || toHere < recordToHere * TrackLayout.LeastBelievableSectorShare)
        {
            return null;
        }

        return recordToHere - toHere > 0 ? recordToHere - toHere : null;
    }

    /// <summary>
    /// A lap's sectors that arrived after the lap was counted (a split read
    /// heard just after the line): when that lap is the layout's track
    /// record, set by this driver and still without sectors, they are kept
    /// with it now - CheckAndAnnounceTrackRecord had none to keep.
    /// </summary>
    private void KeepLateRecordSectors(RacerStanding standing, int lapIndex, long[] sectors)
    {
        if (_race.IsPracticeMode || _race.IsDemoMode || lapIndex < 0 || lapIndex >= standing.LapTimesMs.Count)
        {
            return;
        }

        var trackName = SelectedTrackName();

        if (string.IsNullOrWhiteSpace(trackName)
            || _trackLayouts.All.FirstOrDefault(t => t.Name == trackName) is not { } track
            || track.RecordLapMs <= 0
            || track.RecordLapMs != standing.LapTimesMs[lapIndex]
            || track.RecordSectors() is not null)
        {
            return;
        }

        var name = _profiles.Get(standing.TransponderId)?.DriverName;
        var holder = string.IsNullOrWhiteSpace(name) ? standing.TransponderId : name;

        if (!string.Equals(track.RecordHolderName, holder, StringComparison.Ordinal))
        {
            return;
        }

        track.KeepRecordSectors(sectors);
        _trackLayouts.Upsert(track);
    }

    /// <summary>
    /// The layout's sector records (TrackLayout.SectorRecordsByCut): any sector
    /// of a race lap quicker than anyone has ever gone through it. The same
    /// drivers and the same doubts as the lap record - no practice, demo,
    /// ghost, rental or excluded driver, and nothing from a lap that would
    /// not be believed as a lap (IsPlausibleRecord) - nor a sector a quarter
    /// quicker than it usually takes tonight (Sectors.Typical).
    /// </summary>
    private void KeepSectorRecords(RacerStanding standing, int lapIndex, long[] sectors)
    {
        if (_race.IsPracticeMode || _race.IsDemoMode
            || lapIndex < 0 || lapIndex >= standing.LapTimesMs.Count
            || GhostService.IsGhost(standing.TransponderId)
            || _profiles.Get(standing.TransponderId) is { ExcludeFromRecords: true } or { IsRental: true })
        {
            return;
        }

        var trackName = SelectedTrackName();

        if (string.IsNullOrWhiteSpace(trackName)
            || _trackLayouts.All.FirstOrDefault(t => t.Name == trackName) is not { } track
            || !IsPlausibleRecord(standing.LapTimesMs[lapIndex], standing.TransponderId, track, out _))
        {
            return;
        }

        var name = _profiles.Get(standing.TransponderId)?.DriverName;
        var holder = string.IsNullOrWhiteSpace(name) ? standing.TransponderId : name.Trim();
        var before = System.Text.Json.JsonSerializer.Serialize(track.SectorRecordsByCut);
        var cutBefore = track.LatestSectorCut;

        var beaten = track.OfferSectors(sectors, holder, DateTime.UtcNow, Sectors.Typical(_race.GetOrderedStandings(), sectors.Length));

        if (before == System.Text.Json.JsonSerializer.Serialize(track.SectorRecordsByCut) && cutBefore == track.LatestSectorCut)
        {
            return;
        }

        _trackLayouts.Upsert(track);

        foreach (var sector in beaten)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [split] Sector {sector} record on {track.Name}: {holder}, {Sectors.Format(sectors[sector - 1])}.");
        }
    }

    /// <summary>The sectors of the lap that just took the track record, to keep with it - or null without them.</summary>
    private List<long>? RecordLapSectors(string transponder, long lapMs)
    {
        var standing = _race.GetOrderedStandings().FirstOrDefault(s => string.Equals(s.TransponderId, transponder, StringComparison.OrdinalIgnoreCase));

        if (standing is null)
        {
            return null;
        }

        foreach (var (lap, sectors) in Sectors.LapsOf(standing))
        {
            if (standing.LapTimesMs[lap] == lapMs)
            {
                return sectors.ToList();
            }
        }

        return null;
    }
}
