namespace HeatCaller;

/// <summary>
/// Race Control's split-loop calls: a sector quicker than anyone has gone
/// this session (Fastest sector), and a car crossing the split loop up on
/// its own best lap (Up at the split) or on the track record lap (Record
/// pace at the split). Each is switched on, and worded, on Voice -&gt;
/// Announcements; all start off.
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
        }
        catch (Exception ex)
        {
            // A call is never worth losing the board over.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [split] Couldn't work out a split call: {ex.Message}");
        }
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
