using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Keeps a copy of the race that is running, so a crash costs you a restart
/// rather than the race.
///
/// Everything else in this app can be redone: a layout can be drawn again, a
/// profile retyped. A race cannot. Fifteen drivers have already driven it,
/// the laps happened once, and if the app goes down in the last main of the
/// night there is nothing to fall back on - which until now meant running it
/// again or writing the results out by hand.
///
/// So after every crossing, every flag and every correction, the whole
/// session is written to one small file. It is deleted the moment the race
/// is saved to history, so the file existing at startup MEANS the last race
/// did not finish - there is no flag to get out of step, and no way for it
/// to offer to restore a race that is already in the history.
///
/// Deliberately a snapshot rather than a log of events to replay: a snapshot
/// restores in one step and cannot drift from the lap logic, whereas a
/// replay would have to reproduce every rule the session applies - minimum
/// lap times, jump starts, stagger offsets - and would quietly diverge the
/// first time one of those changed.
/// </summary>
public sealed class RaceJournal
{
    private static string FilePath => Path.Combine(
        AppPaths.Root,
        "race-in-progress.json");

    /// <summary>Where a journal the user chose not to restore is kept, rather than deleted outright.</summary>
    private static string DiscardedPath => FilePath.Replace(".json", "-discarded.json");

    /// <summary>Where a journal goes when it cannot be deleted after the race is saved - anywhere but the name that means "unfinished".</summary>
    private static string ClearedPath => FilePath.Replace(".json", "-cleared.json");

    /// <summary>
    /// When the last write landed, as <see cref="Environment.TickCount64"/>,
    /// or null when nothing has been written yet this race.
    ///
    /// This and <see cref="_lastFailedAttemptTicks"/> used to be
    /// DateTime.UtcNow, which is the wall clock - and the wall clock can be
    /// set backwards. Windows time sync correcting a laptop that runs fast,
    /// or somebody fixing the clock mid-evening, made "now minus the last
    /// write" negative, which is always under the throttle, so every
    /// crossing was held back until the wall clock caught up with where it
    /// had been. For a 90-second correction that is 90 seconds of laps a
    /// crash would lose, with nothing saying so. The tick count only ever
    /// goes forward, so it is what spacing is measured with here - the same
    /// reason the timing watch in MainForm uses it. The wall clock is still
    /// the right thing for SavedUtc and for the durations told to the user.
    /// </summary>
    private long? _lastWriteTicks;

    /// <summary>
    /// When writing started failing, or null while it is working.
    ///
    /// This used to be a flag that, once set, stopped every write for the
    /// rest of the race. One failure - an antivirus scan holding the file
    /// for a moment, a backup tool, a network profile blinking - and the
    /// safety net was off for the night without anybody knowing it had
    /// come back. Now it tries again every <see cref="RetryInterval"/>.
    /// </summary>
    private DateTime? _failingSinceUtc;

    /// <summary>
    /// When the last failed attempt was, as <see cref="Environment.TickCount64"/>,
    /// to space the retries out - see <see cref="_lastWriteTicks"/> for why
    /// it is not the wall clock. Null when no attempt has failed.
    /// </summary>
    private long? _lastFailedAttemptTicks;

    /// <summary>How long to wait between attempts while writes are failing.</summary>
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Raised when the journal stops being able to write, and again when it
    /// can once more - the two things the race log needs to say. Not raised
    /// for each retry in between.
    /// </summary>
    public event Action<string>? StatusChanged;

    /// <summary>
    /// The most this writes, however fast the laps come.
    ///
    /// A busy heat can put a dozen crossings a second through here. The file
    /// is only a few kilobytes so writing every one would work, but there is
    /// no point: losing a fifth of a second of a race to a crash is not a
    /// loss worth spending disk writes on all evening.
    /// </summary>
    private static readonly TimeSpan Throttle = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Whether a write was asked for and has not landed yet - see
    /// <see cref="IsBehind"/>.
    /// </summary>
    private bool _writePending;

    /// <summary>
    /// True when the file is behind the race: a write was asked for and was
    /// held back - by <see cref="Throttle"/>, or while writes are failing -
    /// or was tried and failed.
    ///
    /// A held-back write used to be simply dropped. Nothing wrote the file
    /// again until the next crossing or flag, so the second of two cars
    /// crossing 300ms apart stayed out of it for a whole lap, not the fifth
    /// of a second the throttle allows for - and a crash in that lap brought
    /// the race back without it. Whatever drives the race clock should call
    /// Record again (not forced) while this is true; Record writes as soon as
    /// the throttle allows, and does nothing before then.
    /// </summary>
    public bool IsBehind => _writePending;

    /// <summary>How old a journal can be and still be offered back - see Unfinished.</summary>
    private static readonly TimeSpan MaxRestorableAge = TimeSpan.FromHours(24);

    /// <summary>One driver, as the journal stores them.</summary>
    public sealed class StandingRecord
    {
        public string TransponderId { get; set; } = string.Empty;
        public int Laps { get; set; }
        public long FastestLapMs { get; set; }
        public long LastLapDeviceTimeMs { get; set; }
        public long LastSeenDeviceTimeMs { get; set; }
        public long ElapsedMs { get; set; }
        public int ConsecutiveValidLaps { get; set; }
        public long PenaltyMs { get; set; }
        public long StreakLapTimeSumMs { get; set; }
        public int StreakLapCount { get; set; }
        public long StreakBaselineLapMs { get; set; }
        public long ReleaseOffsetMs { get; set; }
        public int RejectedLaps { get; set; }
        public int LapsLed { get; set; }
        public int StartPosition { get; set; }

        /// <summary>
        /// Where a qualifier's clock zeroed. Defaults to "not crossed" so a
        /// journal written before this was stored still reads - that driver
        /// just re-zeroes, as every driver used to.
        /// </summary>
        public long QualifyingZeroMs { get; set; } = -1;

        public List<long> LapTimesMs { get; set; } = new();
        public List<int> StreakAtLap { get; set; } = new();

        /// <summary>
        /// Which laps ran through an earlier crash recovery - see
        /// RacerStanding.RecoveryLapIndexes. Empty in a journal written
        /// before this was stored, which is what it was treated as then.
        /// </summary>
        public List<int> RecoveryLapIndexes { get; set; } = new();

        /// <summary>Warnings given this race. Null in a journal written before warnings existed.</summary>
        public List<RaceWarning>? Warnings { get; set; }
    }

    /// <summary>
    /// The whole race, written flat.
    ///
    /// Its own type rather than the session's snapshot record: the snapshot
    /// holds live RacerStanding objects whose lap lists have no setters,
    /// which the JSON reader cannot fill back in. Mapping by hand is a dozen
    /// lines and it means the file format cannot break by accident when
    /// somebody adds a field to the live class.
    /// </summary>
    public sealed class Entry
    {
        public DateTime SavedUtc { get; set; } = DateTime.UtcNow;

        public RaceState State { get; set; }
        public FlagState Flag { get; set; }

        public DateTime? RaceStartUtc { get; set; }
        public long? RaceStartDeviceTimeMs { get; set; }
        public TimeSpan AccumulatedRedTime { get; set; }
        public DateTime? RedFlagStartedUtc { get; set; }

        public long RaceFastestLapMs { get; set; }
        public string RaceFastestLapTransponder { get; set; } = string.Empty;

        // The last lap, if it had started. Without these a race that went
        // down with the checkered out came back with no flag out at all, and
        // every driver who had already taken it went round again. Absent
        // from older journals, which read as "not finishing" - what they
        // restored as before.
        public bool IsFinishing { get; set; }
        public bool FinishingOnTime { get; set; }
        public List<string> TookTheFlag { get; set; } = new();

        public List<StandingRecord> Standings { get; set; } = new();

        // Enough of the setup to put the race back the way it was run,
        // rather than the way the app happens to be configured now.
        public DateTime? SessionStartedAtUtc { get; set; }
        public string TrackLayoutName { get; set; } = string.Empty;
        public int? LapLimit { get; set; }
        public double? TimeLimitMinutes { get; set; }
        public int MinLapTimeMs { get; set; }
        public int StreakThreshold { get; set; }

        // The rest of the race's rules, and what it was being run as.
        // Without them a restored race finished, judged streaks and costed
        // warnings under whatever the app happened to be set to at the next
        // launch, and was saved under a guessed round and a blank label.
        // All null in a journal written before they were stored, which
        // leaves the session's current settings alone - what happened then.
        public bool? FinishOnNextCrossing { get; set; }
        public double? StreakWindowPercent { get; set; }
        public bool? StreakFasterLapBreaks { get; set; }

        /// <summary>
        /// Whether WarningLadder below was written at all. A null ladder is
        /// a real setting - the default ladder - so it cannot also stand for
        /// "an older journal that never stored one".
        /// </summary>
        public bool HasWarningLadder { get; set; }
        public List<WarningStep>? WarningLadder { get; set; }

        /// <summary>The round the race was being run for, if one was picked.</summary>
        public string? RoundId { get; set; }

        /// <summary>The race name typed on Race Control, as it was.</summary>
        public string? RaceLabel { get; set; }

        /// <summary>A rehearsal is never worth restoring, and must never reach the history.</summary>
        public bool IsDemo { get; set; }

        /// <summary>
        /// Practice is no longer written at all - see Record. Kept so a
        /// journal a practice left behind before that can be recognised and
        /// passed over rather than offered back as a race.
        /// </summary>
        public bool IsPractice { get; set; }

        // How the field was let go, and the grid it was let go from. Without
        // these a restored staggered, hand-released, handicap or qualifying
        // race was re-timed under whatever start the app happened to be set
        // to now: a qualifier lost every driver's own clock, a stagger lost
        // its offsets, and a hand-released race treated everybody as
        // released. All null in a journal written before they were stored,
        // which leaves the session's own settings alone - what happened
        // before.
        public RaceStartType? StartType { get; set; }
        public StaggerClock? StaggerClockMode { get; set; }
        public List<string>? Grid { get; set; }
        public Dictionary<string, long>? ReleaseOffsetsMs { get; set; }
        public List<string>? ReleaseCalled { get; set; }
        public Dictionary<string, int>? StartPositions { get; set; }
        public List<string>? JumpStarters { get; set; }
        public Dictionary<string, long>? PendingJumpStartPenalties { get; set; }

        // Which alternate transponders were being filed under their owner's
        // main one, so a driver on their second car stays in one row after a
        // restore. Null in older journals - see RaceControlSession.
        public Dictionary<string, bool>? AlternateMerged { get; set; }
        public List<string>? MergedInto { get; set; }
        public List<string>? HeardThisRace { get; set; }

        // Whether "last lap" and "one minute to go" had already been called.
        // Null in an older journal - the session works them out from the
        // race instead. See RaceControlSession.RestoreSnapshot.
        public bool? LastLapAnnounced { get; set; }
        public bool? FinalMinuteAnnounced { get; set; }

        public int LapCount => Standings.Sum(s => s.Laps);

        public string Describe()
        {
            var drivers = Standings.Count;
            var laps = LapCount;

            var when = RaceStartUtc?.ToLocalTime().ToString("HH:mm") ?? SavedUtc.ToLocalTime().ToString("HH:mm");

            var what = State == RaceState.Racing ? "was still running" : $"was at {State}";

            return $"A race {what}: {drivers} driver{(drivers == 1 ? string.Empty : "s")}, "
                   + $"{laps} lap{(laps == 1 ? string.Empty : "s")} counted, started at {when}"
                   + (string.IsNullOrWhiteSpace(TrackLayoutName) ? "." : $" on {TrackLayoutName}.");
        }
    }

    /// <summary>
    /// Writes the race out, unless it was written a moment ago.
    /// </summary>
    /// <param name="force">Used for flags and corrections, which are rare and worth writing immediately.</param>
    /// <param name="roundId">The round the race is being run for, or null when none is picked.</param>
    /// <param name="raceLabel">The race name typed on Race Control, or null to leave it unrecorded.</param>
    public void Record(
        RaceControlSession race,
        DateTime? sessionStartedAtUtc,
        string trackLayoutName,
        bool force = false,
        string? roundId = null,
        string? raceLabel = null)
    {
        // Nothing to protect: an idle session, or a rehearsal.
        //
        // Nor a finished one. Its result has been saved - or deliberately
        // not saved - and the journal cleared; a correction made after the
        // flag used to write it straight back, so the next launch offered
        // to "recover" a race that was already in the history.
        //
        // Nor practice. It runs on the same session, so its laps came
        // through here too, and a practice that was simply stopped left its
        // journal behind: the next launch offered it back as "a race was
        // still running", and restoring it switched practice mode on with
        // the Practice page not running - which locked Race Control out
        // until the app was restarted. Practice has its own save.
        if (race.IsDemoMode || race.IsPracticeMode || race.State is RaceState.Idle or RaceState.Finished)
        {
            // Nothing owed either - a write held back before the race
            // finished has nothing left to protect.
            _writePending = false;
            return;
        }

        // Somewhere this cannot write - a locked-down club PC, a roaming
        // profile that is not there, a disk with nothing left on it. SafeFile
        // reports every failure to the user, and at four attempts a second
        // that would bury the race log in storage errors for the rest of the
        // night. So while it is failing this tries again only every few
        // seconds, and quietly: one complaint when it starts, one line when
        // it recovers.
        if (_failingSinceUtc.HasValue && IsWithin(_lastFailedAttemptTicks, RetryInterval))
        {
            _writePending = true;
            return;
        }

        if (!force && IsWithin(_lastWriteTicks, Throttle))
        {
            // Held back, not dropped - see IsBehind.
            _writePending = true;
            return;
        }

        try
        {
            var snapshot = race.CreateSnapshot();

            var entry = new Entry
            {
                State = snapshot.State,
                Flag = snapshot.Flag,
                RaceStartUtc = snapshot.RaceStartUtc,
                RaceStartDeviceTimeMs = snapshot.RaceStartDeviceTimeMs,
                AccumulatedRedTime = snapshot.AccumulatedRedTime,
                RedFlagStartedUtc = snapshot.RedFlagStartedUtc,
                RaceFastestLapMs = snapshot.RaceFastestLapMs,
                RaceFastestLapTransponder = snapshot.RaceFastestLapTransponder,
                IsFinishing = snapshot.IsFinishing,
                FinishingOnTime = snapshot.FinishingOnTime,
                TookTheFlag = snapshot.TookTheFlag?.ToList() ?? new List<string>(),
                SessionStartedAtUtc = sessionStartedAtUtc,
                TrackLayoutName = trackLayoutName,
                LapLimit = race.LapLimit,
                TimeLimitMinutes = race.TimeLimit?.TotalMinutes,
                MinLapTimeMs = race.MinLapTimeMs,
                StreakThreshold = race.StreakThreshold,
                FinishOnNextCrossing = race.FinishOnNextCrossing,
                StreakWindowPercent = race.StreakWindowPercent,
                StreakFasterLapBreaks = race.StreakFasterLapBreaks,
                HasWarningLadder = true,
                WarningLadder = CopyLadder(race.WarningLadder),
                RoundId = roundId,
                RaceLabel = raceLabel,
                IsDemo = race.IsDemoMode,
                IsPractice = race.IsPracticeMode,
                StartType = race.StartType,
                StaggerClockMode = race.StaggerClockMode,
                Grid = snapshot.Grid?.ToList(),
                ReleaseOffsetsMs = snapshot.ReleaseOffsetsMs?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                ReleaseCalled = snapshot.ReleaseCalled?.ToList(),
                StartPositions = snapshot.StartPositions?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                JumpStarters = snapshot.JumpStarters?.ToList(),
                PendingJumpStartPenalties = snapshot.PendingJumpStartPenalties?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                AlternateMerged = snapshot.AlternateMerged?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                MergedInto = snapshot.MergedInto?.ToList(),
                HeardThisRace = snapshot.HeardThisRace?.ToList(),
                LastLapAnnounced = snapshot.LastLapAnnounced,
                FinalMinuteAnnounced = snapshot.FinalMinuteAnnounced,
                Standings = snapshot.Standings.Select(s => new StandingRecord
                {
                    TransponderId = s.TransponderId,
                    Laps = s.Laps,
                    FastestLapMs = s.FastestLapMs,
                    LastLapDeviceTimeMs = s.LastLapDeviceTimeMs,
                    LastSeenDeviceTimeMs = s.LastSeenDeviceTimeMs,
                    ElapsedMs = s.ElapsedMs,
                    ConsecutiveValidLaps = s.ConsecutiveValidLaps,
                    PenaltyMs = s.PenaltyMs,
                    StreakLapTimeSumMs = s.StreakLapTimeSumMs,
                    StreakLapCount = s.StreakLapCount,
                    StreakBaselineLapMs = s.StreakBaselineLapMs,
                    ReleaseOffsetMs = s.ReleaseOffsetMs,
                    RejectedLaps = s.RejectedLaps,
                    LapsLed = s.LapsLed,
                    StartPosition = s.StartPosition,
                    QualifyingZeroMs = s.QualifyingZeroMs,
                    LapTimesMs = s.LapTimesMs.ToList(),
                    StreakAtLap = s.StreakAtLap.ToList(),
                    RecoveryLapIndexes = s.RecoveryLapIndexes.ToList(),
                    Warnings = s.Warnings.ToList(),
                }).ToList(),
            };

            var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false });

            // The first attempt goes through SafeFile, which says why it
            // failed. Retries after that are made quietly - SafeFile would
            // report each one - but still refuse once SafeFile is frozen,
            // so a restart after a backup restore never writes behind it.
            var written = _failingSinceUtc.HasValue
                ? TryWriteQuietly(FilePath, json)
                : SafeFile.WriteAllText(FilePath, json);

            if (written)
            {
                NoteWriteSucceeded();
            }
            else
            {
                NoteWriteFailed();
            }
        }
        catch
        {
            // Never let the safety net be the thing that breaks the race.
            NoteWriteFailed();
        }
    }

    /// <summary>
    /// Whether less than <paramref name="span"/> has passed since
    /// <paramref name="sinceTicks"/> (an <see cref="Environment.TickCount64"/>
    /// reading). Null - nothing has happened yet - is never "within", so the
    /// first write of a race and the first retry go straight through, as
    /// DateTime.MinValue used to make them.
    /// </summary>
    private static bool IsWithin(long? sinceTicks, TimeSpan span) =>
        sinceTicks is { } since
        && Environment.TickCount64 - since < (long)span.TotalMilliseconds;

    private void NoteWriteSucceeded()
    {
        _lastWriteTicks = Environment.TickCount64;
        _writePending = false;

        if (_failingSinceUtc is { } since)
        {
            _failingSinceUtc = null;
            RaiseStatus($"Race journal is being written again (it could not be for {(DateTime.UtcNow - since).TotalSeconds:0}s) - the race is protected against a crash once more.");
        }
    }

    private void NoteWriteFailed()
    {
        _lastFailedAttemptTicks = Environment.TickCount64;

        // Still owed: the file holds less than the race, and the next try
        // should not have to wait for the next crossing.
        _writePending = true;

        if (!_failingSinceUtc.HasValue)
        {
            _failingSinceUtc = DateTime.UtcNow;
            RaiseStatus($"Race journal could not be written - if the app crashes now, this race cannot be recovered. Trying again every {RetryInterval.TotalSeconds:0}s.");
        }
    }

    private void RaiseStatus(string message)
    {
        try
        {
            StatusChanged?.Invoke(message);
        }
        catch
        {
            // A listener's fault must not become the journal's.
        }
    }

    /// <summary>
    /// The same write-then-swap SafeFile does, without its report - used
    /// only for retries, after SafeFile has already reported the failure.
    ///
    /// The swap is made inside SafeFile.WhileNoWritesUnderWay, the same lock
    /// every SafeFile write holds and SafeFile.Freeze waits on. Outside it,
    /// a retry landing while a backup restore was putting files back could
    /// swap this journal in after Freeze had said nothing more would be
    /// written - and the frozen check is made again once the lock is held,
    /// because Freeze may have been waiting for exactly that lock.
    ///
    /// Only the swap is inside, not the writing of the temp file: that lock
    /// holds every other save in the app back while it is held, and the
    /// swap is the quick part. The temp file has a name of its own, so
    /// writing it touches nothing a restore or a backup looks at. This runs
    /// at most every RetryInterval while the journal is failing, and the
    /// wait for the lock is SafeFile's own few-second limit at worst.
    /// SafeFile has no quiet write of its own that takes the shared side of
    /// the lock, which would be the better fit.
    /// </summary>
    private static bool TryWriteQuietly(string path, string contents)
    {
        if (SafeFile.IsFrozen)
        {
            return false;
        }

        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var swapped = false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Pushed through to the DISK before the swap, not just handed to
            // Windows' cache - the same thing SafeFile.WriteAtomically does,
            // and for the same reason: a power cut shortly after the swap
            // otherwise leaves race-in-progress.json pointing at a file whose
            // contents never left memory. An empty file, which is exactly
            // what the swap exists to prevent.
            //
            // It matters more here than anywhere else in the app. This is
            // the path taken only while the journal is ALREADY failing to
            // write - a disk under pressure, an antivirus scan - and the
            // journal is the one copy of a race that has not been saved yet.
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            SafeFile.WhileNoWritesUnderWay(() =>
            {
                if (SafeFile.IsFrozen)
                {
                    return;
                }

                if (File.Exists(path))
                {
                    File.Replace(temp, path, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(temp, path);
                }

                swapped = true;
            });
        }
        catch
        {
            // Falls through to the tidy-up below and reports a failure.
        }

        if (!swapped)
        {
            try
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
            catch
            {
                // Left behind; nothing reads it.
            }
        }

        return swapped;
    }

    /// <summary>The race is safely in the history - there is nothing left to recover.</summary>
    public void Clear()
    {
        _lastWriteTicks = null;
        _writePending = false;

        // A new race deserves a fresh try - whatever was in the way may
        // not be any more.
        _failingSinceUtc = null;
        _lastFailedAttemptTicks = null;

        try
        {
            if (File.Exists(FilePath))
            {
                File.Delete(FilePath);
            }
        }
        catch
        {
            // Could not be deleted - most often something holding it open
            // for a moment. Left under its own name it would offer this
            // race back at the next launch, although the race is already in
            // the history, so it is moved out of the way instead. A rename
            // can succeed where a delete did not, and if neither works there
            // is nothing more to do from here.
            try
            {
                File.Move(FilePath, ClearedPath, overwrite: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    /// <summary>What was running when the app last stopped, or null if it finished properly.</summary>
    public static Entry? Unfinished()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return null;
            }

            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(FilePath));

            // Practice is never offered back: it is not a race, and
            // restoring it only locked Race Control - see Record. A journal
            // one left behind before Record stopped writing them is passed
            // over, and the next race's first write replaces it.
            if (entry is null || entry.IsDemo || entry.IsPractice || entry.Standings.Count == 0)
            {
                return null;
            }

            // A race that had finished is not unfinished. Record no longer
            // writes one, but a journal left from before that change - or a
            // Clear that could not delete or move its file - must not offer
            // to "recover" a race that is already in the history.
            if (entry.State == RaceState.Finished)
            {
                return null;
            }

            // Nor is a race from another day. A crash is recovered from the
            // same evening; a journal a day old is not a race anybody is
            // waiting to finish, and it can come from somewhere else
            // entirely - restoring a backup puts back whatever journal was
            // in it, with that dataset's drivers and laps. Offered back, it
            // would drop a race from a different night into this one. Left
            // where it is, the next race's first write replaces it.
            if (DateTime.UtcNow - entry.SavedUtc > MaxRestorableAge)
            {
                return null;
            }

            // Nor one that was only being started. Staging and the countdown
            // come before a race has any laps of its own: whatever rows a
            // journal at either holds belong to the race before - one that
            // has finished and been saved, or one that Stage or Countdown
            // abandoned. Offered back, the countdown one came back as "a race
            // was at Countdown" with the old race's laps, restored under a
            // red flag and a message about a race that never ran. (Staging
            // clears the board, so its journal already had nothing to offer.)
            if (entry.State is RaceState.Staging or RaceState.Countdown)
            {
                return null;
            }

            // A race nobody had started yet is not worth offering back, and
            // neither is one where nothing had happened. Recovering a race
            // with no laps in it saves nobody anything - the grid is still
            // standing there, and re-staging is quicker than reading a
            // dialog about it.
            return entry.State == RaceState.Idle || entry.LapCount == 0 ? null : entry;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sets a session back to what the journal holds - stopped, under a red
    /// flag, ready for the director to drop green when the cars are back on
    /// the track.
    ///
    /// It deliberately does NOT come back racing. The cars are in somebody's
    /// hand, the grid is scattered and nobody is on the throttle, so a race
    /// that resumed live would count whatever crossed the line while the
    /// director was still reading the dialog. Red is what a real race
    /// control would be showing during a stoppage, and the green button it
    /// already has is what ends it.
    ///
    /// Two clocks have to be dealt with, and they are separate:
    ///
    /// The RACE clock runs on wall time from RaceStartUtc, and wall time
    /// kept running through the crash. Left alone, a race that was four
    /// minutes in would come back at eleven and instantly hit a six-minute
    /// limit. So the start is moved FORWARD by however long the app was
    /// down, which puts the clock back to the moment it stopped. The red
    /// period that starts here covers the rest, right up to the green.
    ///
    /// The DECODER clock is the decoder's own, and the app has no idea
    /// where it is until the next car goes past. Every stored per-driver
    /// reading belongs to a decoder session that may not exist any more: if
    /// the decoder was power-cycled too, its clock restarted lower than the
    /// stored numbers, every gap computes negative, and every lap for the
    /// rest of the race is rejected as too quick. So the per-driver
    /// readings are cleared and re-established from each car's next
    /// crossing. That first crossing after the green is timed from the race
    /// time of the car's last counted lap, so it is one long lap spanning
    /// the stoppage - the same as a lap run through a red flag - and it is
    /// kept out of best laps and streaks. See ProcessLapEvent.
    /// </summary>
    public static void Restore(RaceControlSession race, Entry entry)
    {
        var now = DateTime.UtcNow;

        // How long the app was gone. Measured from the last journal write
        // rather than the crash itself, which nothing recorded - at 400ms
        // between writes the difference is not worth a thought.
        var outage = now - entry.SavedUtc;

        if (outage < TimeSpan.Zero)
        {
            // The clock moved backwards between the write and now. Rare,
            // but a machine that picked up a time sync, or crossed a DST
            // boundary while it was down, would do exactly this - and
            // shifting the start by a negative outage would push the race
            // clock backwards. Treat it as no gap at all.
            outage = TimeSpan.Zero;
        }

        // Only a real figure. A journal without one reads as zero, and a
        // zero minimum counts every retrigger of a car parked on the loop
        // as a lap for the rest of the restored race.
        if (entry.MinLapTimeMs > 0)
        {
            race.MinLapTimeMs = entry.MinLapTimeMs;
        }

        // StreakThreshold is deliberately not restored. It is resolved live
        // from Sound Effects now, and what the board highlights should
        // follow the current setting rather than whatever happened to be in
        // force when the app went down. The journal still records it, as a
        // note of what the race was run under.
        race.LapLimit = entry.LapLimit;
        race.TimeLimit = entry.TimeLimitMinutes is > 0 ? TimeSpan.FromMinutes(entry.TimeLimitMinutes.Value) : null;

        // The rest of the rules, where the journal has them - see Entry.
        if (entry.FinishOnNextCrossing is { } finishOnNextCrossing)
        {
            race.FinishOnNextCrossing = finishOnNextCrossing;
        }

        if (entry.StreakWindowPercent is { } streakWindow)
        {
            race.StreakWindowPercent = streakWindow;
        }

        if (entry.StreakFasterLapBreaks is { } fasterLapBreaks)
        {
            race.StreakFasterLapBreaks = fasterLapBreaks;
        }

        if (entry.HasWarningLadder)
        {
            race.WarningLadder = CopyLadder(entry.WarningLadder);
        }

        // Never switched on by a restore. Practice is not journalled any
        // more, and turning it on here - with the Practice page not running
        // to turn it off again - left Race Control refusing every start.
        race.IsPracticeMode = false;

        // The start the race was run under, when the journal has it. Set
        // before the snapshot so everything restored below is read under
        // it. Race Control only writes these between races, so they stay
        // put while the restored race runs.
        if (entry.StartType is { } startType)
        {
            race.StartType = startType;
        }

        if (entry.StaggerClockMode is { } staggerClock)
        {
            race.StaggerClockMode = staggerClock;
        }

        var standings = entry.Standings.Select(s =>
        {
            var standing = new RacerStanding
            {
                TransponderId = s.TransponderId,
                Laps = s.Laps,
                FastestLapMs = s.FastestLapMs,

                // Cleared on purpose - see the note above on the decoder
                // clock. Everything these two were holding is a reading
                // from a decoder session that may be gone.
                LastLapDeviceTimeMs = 0,
                LastSeenDeviceTimeMs = 0,

                ElapsedMs = s.ElapsedMs,
                ConsecutiveValidLaps = s.ConsecutiveValidLaps,
                PenaltyMs = s.PenaltyMs,
                StreakLapTimeSumMs = s.StreakLapTimeSumMs,
                StreakLapCount = s.StreakLapCount,
                StreakBaselineLapMs = s.StreakBaselineLapMs,
                ReleaseOffsetMs = s.ReleaseOffsetMs,
                RejectedLaps = s.RejectedLaps,
                LapsLed = s.LapsLed,
                StartPosition = s.StartPosition,
                QualifyingZeroMs = s.QualifyingZeroMs,
            };

            standing.LapTimesMs.AddRange(s.LapTimesMs);
            standing.StreakAtLap.AddRange(s.StreakAtLap);

            // Null only if the file says so explicitly; missing reads as empty.
            if (s.RecoveryLapIndexes is not null)
            {
                standing.RecoveryLapIndexes.AddRange(s.RecoveryLapIndexes);
            }

            if (s.Warnings is not null)
            {
                standing.Warnings.AddRange(s.Warnings);
            }

            return standing;
        }).ToList();

        // A race that was still running comes back stopped. One that had
        // already taken the checkered is left exactly as it was - there is
        // nothing to resume, only a result to save.
        var resumable = entry.State == RaceState.Racing;

        // A red that was already out when the journal was written.
        //
        // The session only banks red time into AccumulatedRedTime when the
        // red ENDS; while it is out, the time since RedFlagStartedUtc is
        // taken off the clock on the fly. The restore below replaces that
        // red start with "now", so without banking it first the red time up
        // to the last write simply vanished - and turned into race time.
        //
        // The maths, with T0 = RaceStartUtc, A = AccumulatedRedTime,
        // R = RedFlagStartedUtc, S = SavedUtc, N = now:
        //
        //   At the write, the race clock read  S - T0 - A - (S - R)  =  R - T0 - A
        //   (frozen at the moment the red came out, as a red should be).
        //
        //   Restored, the start moves to T0 + (N - S) and a new red starts
        //   at N, so at N the clock reads
        //     N - (T0 + N - S) - A' - (N - N)  =  S - T0 - A'.
        //
        //   With A' = A (what this did) that is R - T0 - A + (S - R): the
        //   race gained every second of the red up to the write. A 5:00 race
        //   red-flagged at 2:00 for three minutes of corrections came back
        //   at 5:00 and ended on the green.
        //
        //   With A' = A + (S - R) it is R - T0 - A: exactly where it stood.
        //
        // Floored at zero, for a clock that moved backwards between the red
        // and the write, as the outage is above.
        var redBeforeWrite = TimeSpan.Zero;

        if (entry.RedFlagStartedUtc is { } redStartedUtc)
        {
            redBeforeWrite = entry.SavedUtc - redStartedUtc;

            if (redBeforeWrite < TimeSpan.Zero)
            {
                redBeforeWrite = TimeSpan.Zero;
            }
        }

        race.RestoreSnapshot(new RaceControlSession.SessionSnapshot(
            standings,
            entry.State,
            resumable ? FlagState.Red : entry.Flag,

            // Moved forward by the outage, so the race clock picks up where
            // it stopped instead of counting the time the app was shut.
            entry.RaceStartUtc is { } started ? started + outage : null,

            entry.RaceStartDeviceTimeMs,

            // Including any red that was already out - see above.
            entry.AccumulatedRedTime + redBeforeWrite,

            // Red from this moment until the director goes green, so the
            // time spent getting the cars back on the track is not charged
            // to the race either. A red that was out in a race not being
            // resumed restarts here too: its time up to the write is banked
            // above, and the start has moved on by the outage, so keeping
            // its old start would take that time off the clock twice.
            resumable || entry.RedFlagStartedUtc.HasValue ? now : null,

            entry.RaceFastestLapMs,
            entry.RaceFastestLapTransponder,

            // The last lap, if it was under way. When it came out is not
            // stored: the straggler grace starts again from the restore, so
            // the race is not called the moment it resumes.
            IsFinishing: entry.IsFinishing,
            FinishingOnTime: entry.FinishingOnTime,
            TookTheFlag: entry.TookTheFlag ?? new List<string>(),

            // Null in an older journal, which leaves each of these as the
            // session has it - see RestoreInto.
            Grid: entry.Grid,
            ReleaseOffsetsMs: entry.ReleaseOffsetsMs,
            ReleaseCalled: entry.ReleaseCalled,
            StartPositions: entry.StartPositions,
            JumpStarters: entry.JumpStarters,
            PendingJumpStartPenalties: entry.PendingJumpStartPenalties,
            AlternateMerged: entry.AlternateMerged,
            MergedInto: entry.MergedInto,
            HeardThisRace: entry.HeardThisRace,
            LastLapAnnounced: entry.LastLapAnnounced,
            FinalMinuteAnnounced: entry.FinalMinuteAnnounced));
    }

    /// <summary>
    /// A copy of a warning ladder, so the journal never shares steps with
    /// the racing class they came from. Null stays null - the default ladder.
    /// </summary>
    private static List<WarningStep>? CopyLadder(List<WarningStep>? ladder) =>
        ladder?.Select(step => new WarningStep(step.Action, step.PenaltySeconds)).ToList();

    /// <summary>
    /// Puts a journal aside instead of deleting it.
    ///
    /// Somebody who says no to restoring a race at nine in the morning may
    /// well want it back at nine fifteen, and the file costs nothing to
    /// keep.
    /// </summary>
    public static void Discard()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return;
            }

            if (File.Exists(DiscardedPath))
            {
                File.Delete(DiscardedPath);
            }

            File.Move(FilePath, DiscardedPath);
        }
        catch
        {
            // ignore
        }
    }
}
