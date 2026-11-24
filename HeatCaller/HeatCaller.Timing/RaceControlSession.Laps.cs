namespace HeatCaller;

/// <summary>
/// RaceControlSession's lap processing: a pass from the decoder, filed under
/// the right driver (an alternate transponder under its owner's main one),
/// then judged - before the green, jump start, too soon, a lap, the flag -
/// and counted. Moved here unchanged from RaceControlSession.cs on 3 Oct
/// 2026, the first step of splitting that file by area (see ROADMAP.md,
/// "Finding your way around"). The fields these use stay in the main file,
/// where their initializers run in a known order.
/// </summary>
public sealed partial class RaceControlSession
{
    /// <summary>
    /// Files a pass from a driver's alternate transponder under their main
    /// one, so the laps land in the driver's own row - one lap count, one
    /// best, one position - and everything downstream that is keyed by
    /// transponder (history, records, handicaps, the grid and release
    /// schedule, which all hold main transponders) sees the driver it
    /// expects.
    ///
    /// Decided once per race, on the alternate's first pass:
    ///   - the main transponder hasn't been heard this race: the driver is
    ///     on their second car, so its laps go under the main transponder
    ///     for the rest of the race;
    ///   - the main transponder HAS been heard: both cars are out (a friend
    ///     borrowing the spare, say). Merging would interleave two cars'
    ///     crossings into nonsense lap times, so the alternate is timed as
    ///     a car of its own - it still shows the owner's name, because name
    ///     lookups resolve alternates.
    /// The decision is kept for the whole race, so a driver's laps never
    /// jump between rows. If the main car turns up AFTER its alternate was
    /// merged, the two can no longer be told apart; the log says so once.
    ///
    /// Passes that are not an alternate - main transponders, rentals,
    /// unknown cars - come back untouched.
    /// </summary>
    private DirectLapEvent ResolveAlternateTransponder(DirectLapEvent lap)
    {
        var id = lap.TransponderId;

        // Already being filed under a driver this race: under that driver for
        // the rest of it, whoever the transponder belongs to now. Only
        // WHETHER it was merged used to be kept; WHO it was merged into was
        // looked up afresh on every pass, so a transponder transferred to
        // another driver mid-heat moved its laps into that driver's row from
        // then on - someone not even on track - and one that stopped being
        // an alternate split the driver's laps into a second row halfway.
        if (_alternateMerged.TryGetValue(id, out var decidedMerge) && decidedMerge
            && _heardAsByRow.FirstOrDefault(row => string.Equals(row.Value, id, StringComparison.OrdinalIgnoreCase)).Key is { Length: > 0 } mergedInto)
        {
            _heardThisRace.Add(id);
            return lap.WithTransponder(mergedInto);
        }

        var primary = AlternateTransponderOwner?.Invoke(id);

        if (string.IsNullOrWhiteSpace(primary))
        {
            RefileCountdownJumpToAlternate(id); // WHY (23 Oct 2026, bug hunt 10): a countdown jump filed here from this driver's alternate moves to the alternate now that both cars are out
            _heardThisRace.Add(id);

            if (_mergedInto.Contains(id) && _bothCarsWarned.Add(id))
            {
                Log($"{id} is on track while its alternate transponder is already being timed as {id} - "
                    + "both cars' crossings are going into one row, so its lap times can't be trusted this race.");
            }

            return lap;
        }

        if (!_alternateMerged.TryGetValue(id, out var merged))
        {
            // Not when the main transponder has been heard, and not when
            // another of the driver's transponders is already being filed
            // under it either: a driver can own several now, and two of
            // them out together are two cars, whose crossings in one row
            // would make nonsense lap times.
            //
            // Nor when this transponder has already crossed the line this
            // race under its own id - given to a driver mid-race from the
            // board's menu, say. Its laps so far are in a row of their own,
            // and they stay together there rather than splitting halfway.
            merged = !_heardThisRace.Contains(id) && !_heardThisRace.Contains(primary) && !_mergedInto.Contains(primary);
            _alternateMerged[id] = merged;

            if (merged)
            {
                _mergedInto.Add(primary);
                _heardAsByRow = new Dictionary<string, string>(_heardAsByRow, StringComparer.OrdinalIgnoreCase) { [primary] = id };

                // Bug hunt 17: a countdown jump booked under this
                // transponder's own id follows it into the owner's row.
                RefileCountdownJumpToOwner(id, primary);
            }
            else
            {
                Log(_heardThisRace.Contains(id)
                    ? $"{id} is one of {primary}'s transponders now, but it was timed on its own earlier this race - "
                      + $"keeping {id} as a separate car until the race ends."
                    : $"{id} is one of {primary}'s transponders, but {primary} already has a car on track - "
                      + $"timing {id} as a separate car this race.");
            }
        }

        _heardThisRace.Add(id);

        if (!merged)
        {
            return lap;
        }

        // A copy rather than a change: the event is init-only, and the same
        // instance has already been handed to TransponderWatch. Every field
        // is carried across (DirectLapEvent.WithTransponder).
        return lap.WithTransponder(primary);
    }

    /// <summary>
    /// A pass from the line: filed, judged and counted - see JudgePass.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P1): judged by when it ARRIVED, not by
    /// when the race got round to it. Its arrival stamp (ReceivedMs, taken on
    /// the decoder's own thread the moment its bytes came in) is "now" for
    /// everything the judging works out from the clock - the time limit, who
    /// the checkered applies to, a car's own window, a staggered release, the
    /// start-reference check. A pass that waited ten seconds behind a frozen
    /// window used to be judged ten seconds late: one that crossed at 4:59.6
    /// was booked after the checkered, and with finish on next crossing that
    /// driver was finished a lap early. A pass that is not late is judged
    /// exactly as before - its arrival and "now" are the same moment. The
    /// 500ms tick still uses the clock as it is.
    ///
    /// Recorded first, as given (P0) - see Recorder.
    /// </remarks>
    public void ProcessLapEvent(DirectLapEvent lap)
    {
        if (Recorder is { } recorder && _recordingSuppressed == 0)
        {
            try
            {
                recorder.Pass(lap, lap.IsSynthetic ? null : AlternateTransponderOwner?.Invoke(lap.TransponderId), NowMsSource(), UtcNowSource());
            }
            catch
            {
                // A recording is never worth a lap.
            }
        }

        // Saved and put back, so a pass handed in from inside a listener
        // (none do today) leaves the outer one's moment as it was.
        var outerMoment = _judgingAtMs;
        var outerThread = _judgingThreadId;
        var moment = JudgingMomentOf(lap);

        _judgingThreadId = Environment.CurrentManagedThreadId;
        _judgingAtMs = moment;

        try
        {
            JudgePass(lap);
        }
        finally
        {
            _judgingAtMs = outerMoment;
            _judgingThreadId = outerThread;
        }
    }

    /// <summary>ProcessLapEvent's judging, at the moment the pass arrived - see there. The rules themselves are unchanged.</summary>
    private void JudgePass(DirectLapEvent lap)
    {
        // Updated unconditionally, even outside the Racing state, so
        // whatever's about to call GreenFlag has a fresh reference point -
        // the decoder is still sending events during Staging/Countdown, and
        // that time needs to be captured before it's needed as lap timing's
        // zero-point.
        //
        // Ghost passes don't touch it: they run on their own clock (time
        // since the green), not the decoder's.
        if (!lap.IsSynthetic)
        {
            LastKnownDeviceTimeMs = lap.DeviceTimeMs;

            // WHY (23 Oct 2026, timing layer, P1): when it arrived, not when it
            // was got to - the decoder's clock read DeviceTimeMs THEN, so the
            // estimate the next green is stamped with (EstimatedDeviceTimeNowMs)
            // is not thrown out by however long the pass waited.
            _lastKnownDeviceClockMs = ArrivalOf(lap);

            lap = ResolveAlternateTransponder(lap);
        }

        // Before the pass is judged, not after: if the clock ran out a
        // fraction of a second ago, the checkered is already out and THIS
        // crossing is the one that takes it - exactly as the lap limit
        // already works. Leaving it to the next poll gave the driver an
        // ordinary lap and a whole extra one to run.
        ApplyTimeLimit();

        // A pass during the countdown is a jump start - the car crossed the
        // line before the green. These passes were already arriving and
        // being discarded here; this just acts on them.
        //
        // Staging is NOT the countdown. It is the field being called: cars
        // are carried out, placed, rolled across the loop to their grid
        // slot, and every one of those crossings used to be booked as a
        // jump start - with the class's penalty - before anyone had been
        // told to go. Only once the countdown is running is a crossing a
        // driver anticipating the start. Staging passes are ignored, as
        // they were before jump starts were detected at all.
        if ((State is RaceState.Staging or RaceState.Countdown) && !lap.IsSynthetic)
        {
            var parkedOnTheLoop = NoteSightingBeforeGreen(lap.TransponderId, lap.DeviceTimeMs);

            if (State == RaceState.Countdown)
            {
                RegisterJumpStart(lap.TransponderId, lap.DeviceTimeMs, parkedOnTheLoop);
            }

            return;
        }

        // Between races the pass means nothing, but it is still counted
        // towards the car's run of sightings - silently, nothing is said or
        // flagged. A countdown can be started straight from here without
        // Stage, and a car left on the loop since the last race used to
        // arrive at that countdown with no history at all: its first pass
        // was a run of one, it was called as a jump start and handed the
        // class penalty - the very Rehearsal 3 call the parked check exists
        // to stop. Counted here, its run is already long when the lights
        // begin. See NoteSightingBeforeGreen for why an old entry cannot
        // mark a car that has only just arrived.
        if ((State is RaceState.Idle or RaceState.Finished) && !lap.IsSynthetic)
        {
            CountSightingInARow(lap.TransponderId, lap.DeviceTimeMs);
            return;
        }

        // Under a red, a car still waiting for its own release keeps its run
        // of sightings going, silently, as it does between races. Its run is
        // the only thing that says it is parked on the loop rather than
        // moving. A red stopped the count, the run aged past the repeat gap
        // while the red lasted, and the car's first sighting after it was
        // booked as a jump start - penalty, call and red row - for a car that
        // had never moved.
        if (State == RaceState.Racing && Flag == FlagState.Red && _raceStartDeviceTimeMs is not null
            && !lap.IsSynthetic && !IsReleased(lap.TransponderId))
        {
            CountSightingInARow(lap.TransponderId, lap.DeviceTimeMs);
            return;
        }

        if (State != RaceState.Racing || Flag == FlagState.Red || _raceStartDeviceTimeMs is null)
        {
            return; // not actively racing - ignore passes
        }

        // A car that crosses before its own release has jumped, exactly as
        // one crossing before the green has. Ghosts run to their own clock
        // and are never held. Standard starts never reach the body of this:
        // with no schedule, IsReleased is true for everyone from the green.
        //
        // That includes the parked-on-the-loop check. A car waiting for its
        // staggered (or hand) release sits on its grid slot for seconds
        // after the green, and if that slot is on the loop the decoder goes
        // on reporting it every two seconds. Its first report after the
        // green used to be booked as a jump - call, red row, class penalty -
        // even though the countdown had just logged it as parked and let it
        // be. Its run of sightings carries on from the countdown's, so a car
        // already parked stays parked, and one that arrives on the loop now
        // is flagged at its first pass exactly as it would have been in the
        // countdown. No crossing time is passed: the green has already
        // credited whatever start laps it was going to, and held starts
        // never get one.
        if (!lap.IsSynthetic && !IsReleased(lap.TransponderId))
        {
            var parkedOnTheLoop = NoteSightingBeforeGreen(lap.TransponderId, lap.DeviceTimeMs);
            RegisterJumpStart(lap.TransponderId, parkedOnTheLoop: parkedOnTheLoop);
            return;
        }

        // Their race is already over: they took the checkered, and the
        // only reason the session is still running is that others haven't.
        if (IsFinishing && _tookTheFlag.Contains(lap.TransponderId))
        {
            return;
        }

        // Their own window has closed, and the session is only still open
        // because a later-released car is still inside its own. Reachable
        // only with FinishOnNextCrossing off; with it on there is a
        // checkered out and their next crossing is their finish, which
        // counts.
        //
        // Gated on there being a window to extend at all, so a standard
        // race - where the session ends on the same tick every driver runs
        // out - reaches none of this. Practice sets a time limit purely to
        // drive its clock ring and must never be cut off by it.
        if (!FinishOnNextCrossing
            && !IsPracticeMode
            && LastWindowExtension > TimeSpan.Zero
            && IsOutOfTime(lap.TransponderId))
        {
            return;
        }

        if (!_standings.TryGetValue(lap.TransponderId, out var standing))
        {
            // The first real car of the race. Until now it was a race of
            // ghosts, where a ghost may hold the fastest lap (see below) -
            // but from here a ghost may not, and one that took it while the
            // real field was still on its way to the line would otherwise
            // keep it, and be the lap every real one was measured against,
            // until somebody beat it.
            if (!Ghosts.IsGhost(lap.TransponderId)
                && Ghosts.IsGhost(_raceFastestLapTransponder)
                && _standings.Keys.All(k => Ghosts.IsGhost(k)))
            {
                _raceFastestLapMs = 0;
                _raceFastestLapTransponder = string.Empty;
            }

            standing = new RacerStanding
            {
                TransponderId = lap.TransponderId,
                ReleaseOffsetMs = ReleaseOffsetMsFor(lap.TransponderId),
                StartPosition = StartPositionOf(lap.TransponderId),
            };
            _standings[lap.TransponderId] = standing;

            // Every car on a hand-released grid has had a row since the
            // green, so a new row here is a car that was not on it. Said in
            // the log, so the director knows why it was never held - see
            // IsReleased.
            if (HandReleaseArmed && !lap.IsSynthetic)
            {
                Log($"{lap.TransponderId} was not on the hand-released grid - timed from the green.");
            }
        }

        // Where this driver's own race began.
        //
        // OwnRelease shifts the zero point to their release, so everyone is
        // measured over the same amount of green-flag time and the result is
        // decided on pace. SharedGreen leaves it at the green, so the wait is
        // time lost and first across the line wins. A standard start has no
        // offset either way, so both behave as they always did.
        long releaseZeroMs;
        long elapsedSinceStart;

        // True when this crossing is the one that zeroed a qualifier's
        // clock - which makes it their start lap. See below.
        var qualifyingClockZeroedHere = false;

        // A qualifier's time since the green BEFORE it is floored at zero -
        // see the start-reference check below, which needs the real figure.
        long rawQualifierSinceGreenMs = 0;

        if (StartType == RaceStartType.Qualifier)
        {
            // Qualifying: this driver's clock zeroes at their own first
            // crossing, whenever that was. Ghosts report time since the
            // green directly, which serves as the same reference.
            //
            // Never below zero. -1 is how a standing says "not crossed yet",
            // and a car over the loop a few milliseconds before the
            // reference produced exactly that: its zero read as unset, the
            // NEXT crossing zeroed the clock again, and that driver was
            // timed from a lap later than everybody else.
            rawQualifierSinceGreenMs = lap.IsSynthetic
                ? lap.DeviceTimeMs
                : lap.DeviceTimeMs - _raceStartDeviceTimeMs.Value;
            var sinceGreen = Math.Max(0, rawQualifierSinceGreenMs);

            if (standing.QualifyingZeroMs < 0)
            {
                standing.QualifyingZeroMs = sinceGreen;
                qualifyingClockZeroedHere = true;
            }

            releaseZeroMs = standing.QualifyingZeroMs;
            elapsedSinceStart = sinceGreen - releaseZeroMs;
        }
        else
        {
            releaseZeroMs = StaggerClockMode == StaggerClock.OwnRelease
                ? standing.ReleaseOffsetMs
                : 0;

            // Ghosts report time since the green directly - see GhostService.
            elapsedSinceStart = lap.IsSynthetic
                ? lap.DeviceTimeMs
                : lap.DeviceTimeMs - _raceStartDeviceTimeMs.Value - releaseZeroMs;
        }

        // Set when the check below moves the start reference on this pass.
        var startReferenceCorrected = false;

        // Sanity check on the first real pass of the race. If the start
        // reference disagrees with the wall clock by more than a few
        // seconds, it came from a different clock - no pass seen since the
        // app opened (reference 0), a stale pass, a decoder power cycle, or
        // the demo's simulated clock - and every lap time and gap this race
        // would be off by that amount (or negative, rejecting every lap).
        // Re-anchor from this pass instead. Skipped for the demo, whose
        // simulated clock intentionally doesn't track wall time.
        if (!lap.IsSynthetic && !_startReferenceChecked && !IsDemoMode && RaceStartUtc.HasValue)
        {
            _startReferenceChecked = true;

            // "Wall" in name only now: the monotonic clock, so a time sync
            // since the green is not read as a broken reference - see
            // _raceStartClockMs.
            var wallSinceStartMs = MsSinceGreen;

            // Compared against time since THIS driver's release, since that
            // is what elapsedSinceStart now measures. Comparing against the
            // green would read a staggered driver's legitimate delay as a
            // broken clock and "correct" a reference that was fine.
            var wallSinceReleaseMs = wallSinceStartMs - releaseZeroMs;

            // A qualifier is judged on time since the GREEN, unfloored.
            //
            // Its elapsed time is measured from the driver's own zero, which
            // was itself worked out from the reference under test - so on
            // the first crossing both sides of the comparison moved together
            // and it could not see anything. Worse, the zero is floored at
            // nothing: a reference AHEAD of the decoder's clock (a decoder
            // that was power-cycled restarts lower) put every driver at
            // zero, elapsed stayed at zero for the whole session, and the
            // check passed on the floored value.
            var decoderSaysMs = StartType == RaceStartType.Qualifier ? rawQualifierSinceGreenMs : elapsedSinceStart;
            var wallSaysMs = StartType == RaceStartType.Qualifier ? wallSinceStartMs : wallSinceReleaseMs;

            if (Math.Abs(decoderSaysMs - wallSaysMs) > 5000)
            {
                Log($"Start reference corrected: decoder said {decoderSaysMs}ms since {(StartType == RaceStartType.Qualifier ? "the green" : "release")}, wall clock says {wallSaysMs}ms.");
                startReferenceCorrected = true;

                // The stored reference stays anchored to the GREEN - it is
                // shared by every driver, and only the per-driver offset
                // above moves it to a release.
                _raceStartDeviceTimeMs = lap.DeviceTimeMs - Math.Max(1, wallSinceStartMs);
                elapsedSinceStart = Math.Max(1, wallSinceReleaseMs);

                // A qualifier's zero was worked out from the reference just
                // thrown away, so it has to be worked out again from the
                // corrected one. Left alone, the zero sat wherever the stale
                // reference put it - minutes out - and the "release" the
                // line above measures from was that nonsense figure, so the
                // driver was timed from somewhere that never happened.
                if (StartType == RaceStartType.Qualifier)
                {
                    var correctedSinceGreen = Math.Max(0, lap.DeviceTimeMs - _raceStartDeviceTimeMs.Value);

                    if (qualifyingClockZeroedHere)
                    {
                        standing.QualifyingZeroMs = correctedSinceGreen;
                    }

                    releaseZeroMs = standing.QualifyingZeroMs;
                    elapsedSinceStart = Math.Max(0, correctedSinceGreen - releaseZeroMs);
                }
            }
        }

        // A car credited with the start lap at the green (see GreenFlag) that
        // crosses again just after the green was never past the line. It was
        // read in the countdown and was behind the line by the green: moved
        // back off the loop, or rolled over the line and reversed. The credit
        // used to stand, and this crossing, judged only on its gap from the
        // countdown read, counted as a second lap - a lap up on the field all
        // race, with the dash to the line kept as a flying lap.
        //
        // So the credit is withdrawn and this crossing is the start lap, as
        // for everyone else; a jump-start flag and penalty stay. Only when
        // the gap would otherwise have counted it: a car parked on the loop,
        // read every couple of seconds, is thrown out as before and keeps
        // its credit. Not when the start reference was corrected on this
        // very pass, which leaves no time since the green to trust.
        //
        // "Just after" is a short fixed window, never more than the minimum
        // lap time. It used to be the minimum lap time itself, but a real
        // jumper that went seconds early on a short lap comes round inside
        // that - a 7 s lap after a 4.2 s jump is 2.8 s after the green - and
        // lost the lap it had earned. A credited car whose first crossing
        // comes later than the window (stalled on the grid) still keeps the
        // lap - nothing tells it apart from a real jumper.
        if (standing.StartLapCredited && standing.Laps == 1 && standing.LapTimesMs.Count == 1
            && !lap.IsSynthetic && !startReferenceCorrected
            && elapsedSinceStart > 0 && elapsedSinceStart < Math.Min(MinLapTimeMs, BehindTheLineAtTheGreenWindowMs)
            && lap.DeviceTimeMs - standing.LastSeenDeviceTimeMs >= MinLapTimeMs)
        {
            standing.Laps = 0;
            standing.LapTimesMs.Clear();
            standing.StreakAtLap.Clear();
            standing.SectorsAtLap.Clear();
            standing.ClearSplitReads();
            standing.ConsecutiveValidLaps = 0;
            // 20 Oct 2026: the credit is withdrawn with nothing broken after
            // it (the counted lap that follows clears this anyway).
            standing.RunBrokenSinceLastLap = false;
            standing.StreakLapTimeSumMs = 0;
            standing.StreakLapCount = 0;
            standing.StreakBaselineLapMs = 0;
            standing.StartLapCredited = false;
            standing.LastLapDeviceTimeMs = 0;
            standing.LastSeenDeviceTimeMs = 0;

            Log($"{lap.TransponderId} was behind the line at the green - start lap credit withdrawn, this crossing is its start lap.");
        }

        long lapTimeMs;
        bool isValid;

        // True for the first crossing of a driver brought back from the race
        // journal after a crash - see below.
        var firstLapAfterRecovery = false;

        // What a too-soon pass was judged on, for the log line that rejects
        // it - see RejectionReason. Null on a driver's first pass, which is
        // not judged against an earlier one.
        long? gapFromLastSeenMs = null;

        // Whether the lap time below runs from this driver's last counted
        // crossing on the decoder's clock - the only kind of lap a removed
        // lap's time can be given to (see GiveRemovedLapToTheShorterLap).
        var timedFromLastCountedCrossing = false;

        if (standing.LastSeenDeviceTimeMs == 0)
        {
            // Very first pass of the race for this racer - always counts.
            lapTimeMs = elapsedSinceStart;
            isValid = elapsedSinceStart > 0;

            // Unless they have timed laps already. That is a driver restored
            // from the race journal: it clears the per-driver decoder
            // readings (a decoder power-cycled while the app was down would
            // make them meaningless), so this crossing lands here - and used
            // to be booked as a lap as long as the WHOLE race so far. That
            // one lap then sat in the pace chart, and pushed every earlier
            // crossing the gap column works out from the lap list out of
            // line with the elapsed time.
            //
            // Timed instead from where their laps already add up to - the
            // race time of their last counted crossing - so the lap list
            // still sums to their elapsed time. It spans the stoppage, so it
            // is long, the same as any lap run through a red flag; it is
            // kept out of best laps and streaks below.
            //
            // Not for a qualifier re-zeroing here, which the case below
            // already deals with.
            if (standing.LapTimesMs.Count > 0 && !qualifyingClockZeroedHere)
            {
                firstLapAfterRecovery = true;
                lapTimeMs = Math.Max(0, elapsedSinceStart - standing.LapTimesMs.Sum());
            }

            // A qualifier's first crossing is their start lap, the same as
            // anybody's first crossing is: it ends the run from the grid
            // and starts the clock. Its "time" is zero because their clock
            // starts right here - the same zero a jump-start credit puts in
            // the start-lap slot.
            //
            // It used to fail the check above on exactly that zero, so every
            // driver in every qualifier began with a rejected pass - counted
            // in RejectedLaps, buzzed as an invalid lap, streak reset - and
            // their first REAL lap then became the start lap, which never
            // competes for a best. The quickest lap of a short qualifier
            // could be the one that was thrown away.
            //
            // Only when their lap list is empty: a standing restored from a
            // journal too old to hold its qualifying zero re-zeroes here
            // mid-race, and that crossing is not a start lap.
            if (qualifyingClockZeroedHere && standing.LapTimesMs.Count == 0)
            {
                lapTimeMs = 0;
                isValid = true;
            }
        }
        else
        {
            // Validity is judged against the immediately preceding pass of
            // ANY kind, valid or rejected - see the class-level comment on
            // MinLapTimeMs for why this matters.
            var gapFromLastSeen = lap.DeviceTimeMs - standing.LastSeenDeviceTimeMs;
            isValid = gapFromLastSeen >= MinLapTimeMs;
            gapFromLastSeenMs = gapFromLastSeen;

            // The lap TIME we report/announce is still measured from the
            // last actually-counted lap, since that's the meaningful
            // duration for a real lap - not from whatever rejected noise
            // happened to precede it.
            lapTimeMs = standing.LastLapDeviceTimeMs > 0
                ? lap.DeviceTimeMs - standing.LastLapDeviceTimeMs
                : elapsedSinceStart;
            timedFromLastCountedCrossing = standing.LastLapDeviceTimeMs > 0;
        }

        // A device clock that has gone BACKWARDS cannot produce a duration
        // worth having, and this is not hypothetical: ILapClient restarts
        // its stopwatch on every Connect, so a USB hiccup, a wrong port or
        // somebody pressing Connect mid-race re-zeroes device time. The
        // arithmetic above would then hand out a lap of about minus six
        // minutes, and a negative ElapsedMs sorts that driver to the top of
        // every same-lap tie for the rest of the race - while the lap chart
        // and the pace analysis carry a -388s lap.
        //
        // Rejected rather than guessed at. Once the timebase has moved
        // under us the real duration is gone, and a rejected lap is a
        // visible, explainable loss where a negative one is a silently
        // wrong result that looks like a win.
        //
        // Not only when the pass was otherwise valid. The first crossing on
        // the new timebase is usually "too soon" as well - its gap from the
        // last pass seen is negative too - and was rejected as such, without
        // anything below being put right.
        var clockWentBackwards = lapTimeMs < 0 && standing.LastSeenDeviceTimeMs != 0;

        if (clockWentBackwards)
        {
            Log($"Lap rejected: the decoder clock went backwards for {lap.TransponderId} "
                + $"({lapTimeMs}ms since that driver's last counted lap). "
                + "The decoder was most likely reconnected during the race.");

            isValid = false;

            // Re-anchored on this pass, so the NEXT lap is measured on the
            // new timebase. Without it every later crossing was measured
            // from a counted lap on the old clock, came out negative in its
            // turn, and was rejected too: one reconnect cost the driver the
            // rest of the race rather than the one lap it really broke.
            standing.LastLapDeviceTimeMs = lap.DeviceTimeMs;

            // Split reads on the old clock can't be placed against the new one.
            standing.ClearSplitReads();

            // Nor can a lap taken off by hand: the next lap is timed from
            // here, not from the crossing that was taken off.
            _removedLapTimes.Remove(standing.TransponderId);

            // The race's own zero point is on the old clock as well, and
            // every driver's elapsed time is measured from it - left there,
            // elapsed came out negative, was floored at zero, and the order
            // within each lap stopped meaning anything. Moved once, from the
            // wall clock, the same way the start-reference check above does
            // it; the next driver through finds it already right.
            //
            // Bug hunt 17: not only when elapsed came out negative. A decoder
            // plugged in shortly before the green has a small reference, and
            // its restarted clock can read above it by the first crossing -
            // elapsed was then positive but minutes short, the reference
            // stayed on the old clock, and every elapsed time for the rest of
            // the race was measured from the wrong zero. So it is moved
            // whenever the decoder and the race clock disagree by more than
            // a few seconds, as the start-reference check judges it.
            var wallSinceGreenMs = MsSinceGreen;
            var decoderSinceMs = StartType == RaceStartType.Qualifier ? rawQualifierSinceGreenMs : elapsedSinceStart;
            var clockSinceMs = StartType == RaceStartType.Qualifier ? wallSinceGreenMs : wallSinceGreenMs - releaseZeroMs;

            if (!lap.IsSynthetic && !IsDemoMode && RaceStartUtc.HasValue
                && (elapsedSinceStart < 0 || Math.Abs(decoderSinceMs - clockSinceMs) > 5000))
            {
                _raceStartDeviceTimeMs = lap.DeviceTimeMs - Math.Max(1, wallSinceGreenMs);
                Log($"Race start reference moved onto the decoder's new clock ({wallSinceGreenMs}ms since the green).");
            }
        }

        standing.LastSeenDeviceTimeMs = lap.DeviceTimeMs; // always updated, valid or not

        var isFastestLap = false;
        var isPersonalBest = false;

        // Where the lap now ending began, on the line's clock - for its
        // sectors, worked out below once the lap is counted.
        var lapStartedAtMs = standing.LastLapDeviceTimeMs;
        long[]? lapSectors = null;

        // Ghosts run in real races as pace cars to chase. They keep their
        // own laps and bests, but nothing that decides or describes the
        // real race - its fastest lap, who led, the last-lap call, the
        // finish - may come from one. A track-record ghost reaching the lap
        // limit used to put the checkered out on the real field.
        //
        // Only when there IS a real field. A race of nothing but ghosts has
        // no real race to protect, and left to this rule it could never
        // reach its lap limit at all.
        var realCarsRacing = _standings.Keys.Any(k => !Ghosts.IsGhost(k));
        var isGhost = realCarsRacing && Ghosts.IsGhost(standing.TransponderId);

        // Bug hunt 17: the lap after one taken off by hand is timed from the
        // crossing that was taken off, so it is not a timed lap either - see
        // _lapTimedFromRemovedCrossing. Kept out of bests and streaks the
        // same way as the first lap after a recovery.
        //
        // Second pass: and its stored time is put right where that can be
        // done - the removed lap's time goes to whichever of this lap and
        // the one before is the shorter without it, so that neither is left
        // falsely short in the lap list that qualifying and the records
        // read. See GiveRemovedLapToTheShorterLap.
        if (isValid && _lapTimedFromRemovedCrossing.Remove(standing.TransponderId))
        {
            firstLapAfterRecovery = true;

            if (_removedLapTimes.Remove(standing.TransponderId, out var removedLap) && timedFromLastCountedCrossing)
            {
                lapTimeMs = GiveRemovedLapToTheShorterLap(standing, removedLap, lapTimeMs);
            }
        }

        if (isValid)
        {
            standing.Laps++;
            standing.LastLapDeviceTimeMs = lap.DeviceTimeMs;

            // A car's first counted lap is what puts it in this race, so a
            // grid borrowed from the last race is renumbered over the cars
            // that are actually here - see RenumberStartPositionsOverStarters.
            if (standing.Laps == 1)
            {
                RenumberStartPositionsOverStarters();
            }

            // A jump-start penalty is owed from before the race began, so
            // it's applied on the driver's first counted lap - the earliest
            // point at which there's an elapsed time to add it to.
            //
            // Into PenaltyMs, not straight into ElapsedMs: elapsed is
            // rebuilt from device time a few lines down, so anything added
            // to it directly would survive until the driver's next
            // crossing and no longer.
            if (_pendingJumpStartPenalties.Remove(standing.TransponderId, out var owed) && owed > 0)
            {
                standing.PenaltyMs += owed;
                Log($"Jump-start penalty applied: {standing.TransponderId} +{owed / 1000.0:0.0}s");
            }

            // Elapsed is device time since the start PLUS every penalty
            // this driver is carrying, and it is rebuilt from scratch on
            // every counted lap.
            //
            // It used to be the device time alone. A penalty went into
            // ElapsedMs through AdjustPenaltyTime, moved the driver down
            // the order exactly as intended - and was then wiped by this
            // line on their very next crossing, while the board went on
            // showing "+10s" in the penalty column. Ten seconds of
            // punishment lasted one lap, and nothing about the display
            // said so. The same line quietly undid every jump-start
            // penalty on the driver's second lap.
            //
            // PenaltyMs is the record; this is the one place it is folded
            // in. AdjustPenaltyTime keeps adjusting ElapsedMs directly so
            // the board moves the instant a penalty is given, and the two
            // agree because both are derived from the same PenaltyMs.
            standing.ElapsedMs = Math.Max(0, elapsedSinceStart + standing.PenaltyMs);
            // The first counted pass is NOT a lap. Its "time" runs from the
            // green to the car's first crossing - a second or two when the
            // grid is just behind the line, or a lap plus reaction time when
            // it isn't. It was allowed to set the best lap, so a 1.4s
            // grid-to-line dash became a driver's best (and the race's
            // fastest lap), no real lap could ever beat it, and the fastest
            // lap announcement stopped firing. The demo's staggered first
            // crossing did the same thing.
            //
            // Still counted as a lap and still in LapTimesMs - lap counts and
            // gaps depend on it - but it never competes for best lap.
            //
            // A start lap credited at the green to a car past the line (a
            // jump start) is a zero; the crossing after it runs from wherever
            // the car crossed before the green - perhaps being set on its
            // grid slot - so that one is the start lap in effect, and does not
            // compete either. See RacerStanding.StartLapCredited.
            var isStartLap = standing.LapTimesMs.Count == 0
                             || (standing.StartLapCredited && standing.LapTimesMs.Count == 1);

            // The first lap after a crash recovery is not a timed lap either:
            // it runs through the stoppage. See where it is worked out above.
            var competesForBest = !isStartLap && !firstLapAfterRecovery;

            // Personal best for this driver - its own event, distinct from
            // the race's fastest lap below.
            if (competesForBest && lapTimeMs > 0 && (standing.FastestLapMs == 0 || lapTimeMs < standing.FastestLapMs))
            {
                var hadOwnBest = standing.FastestLapMs > 0;
                standing.FastestLapMs = lapTimeMs;
                isPersonalBest = hadOwnBest; // first flying lap isn't an improvement
            }

            // "Fastest lap" means fastest of the RACE, not the driver's own
            // best. Comparing against the driver's personal best meant every
            // racer set off the announcement as they each improved, which
            // made it near-constant early on and robbed it of any meaning.
            if (competesForBest && !isGhost && lapTimeMs > 0 && (_raceFastestLapMs == 0 || lapTimeMs < _raceFastestLapMs))
            {
                var hadPrevious = _raceFastestLapMs > 0;
                _raceFastestLapMs = lapTimeMs;
                _raceFastestLapTransponder = standing.TransponderId;

                // The first lap of the race is fastest by default, which
                // isn't news - only announce once there's something to beat.
                isFastestLap = hadPrevious;
            }

            // A lap can be perfectly VALID (cleared the minimum-lap-time
            // filter) while still being a poor, inconsistent lap compared
            // to how this racer has been running. If it's noticeably slower
            // than the current streak's own average, the streak breaks here
            // - this lap becomes lap 1 of a fresh potential streak rather
            // than silently extending the old one. The very first lap of
            // the race is excluded from seeding this average: its duration
            // is measured from the Green-flag reference point rather than a
            // real previous pass, so it's not a meaningful lap time to
            // compare future laps against.
            if (firstLapAfterRecovery)
            {
                // A run cannot carry on through a crash and a restart: this
                // lap starts a fresh one, and - like the start lap - it is
                // not the yardstick for the next, which is judged on its own.
                standing.ConsecutiveValidLaps = 0;
                standing.StreakLapTimeSumMs = 0;
                standing.StreakLapCount = 0;
                standing.StreakBaselineLapMs = 0;
            }
            else if (!isStartLap)
            {
                // Judged against the driver's previous flying lap, within a
                // window of plus-or-minus StreakWindowPercent of it.
                var baselineMs = standing.StreakBaselineLapMs;
                var window = Math.Max(0, StreakWindowPercent) / 100.0;
                var allowance = baselineMs * window;

                var tooSlow = baselineMs > 0 && lapTimeMs > baselineMs + allowance;
                var tooFast = StreakFasterLapBreaks && baselineMs > 0 && lapTimeMs < baselineMs - allowance;

                var breaksStreakOnPace = standing.ConsecutiveValidLaps > 0 && (tooSlow || tooFast);

                if (breaksStreakOnPace)
                {
                    Log($"Streak broken by {(tooFast ? "fast" : "slow")} lap: {lap.TransponderId}  "
                        + $"lap_time={lapTimeMs}ms vs previous lap {baselineMs}ms "
                        + $"(±{StreakWindowPercent:0.#}% = {baselineMs - allowance:0}-{baselineMs + allowance:0}ms)");
                    standing.ConsecutiveValidLaps = 0;
                    standing.StreakLapTimeSumMs = 0;
                    standing.StreakLapCount = 0;
                }
                else
                {
                    // Only laps that BELONG to the streak feed its average.
                    // The breaking lap used to be added here too, seeding
                    // the fresh streak with the very time judged too slow -
                    // so the next lap was compared against a badly inflated
                    // baseline, and the check drifted slower after every
                    // incident. A driver going 7.0, 7.0, 7.0, 10.0 ended up
                    // with a 10.0 baseline, letting an 8.5 pass unnoticed.
                    standing.StreakLapTimeSumMs += lapTimeMs;
                    standing.StreakLapCount++;
                }

                // The yardstick for the NEXT lap, set whether or not this one
                // broke the run - a broken streak still starts again from the
                // lap that broke it, which is the last one actually driven.
                standing.StreakBaselineLapMs = lapTimeMs;
            }

            standing.ConsecutiveValidLaps++;
            // 20 Oct 2026: a counted lap closes any break since the last one
            // - its StreakAtLap entry below now says where the run stands.
            standing.RunBrokenSinceLastLap = false;
            standing.LapTimesMs.Add(lapTimeMs);
            standing.StreakAtLap.Add(standing.ConsecutiveValidLaps);

            // Its sectors: only for a flying lap timed by the decoder from
            // the line's last crossing - the start lap, a lap through a crash
            // recovery and a ghost's have none.
            var sectorsAllowed = !lap.IsSynthetic && !isGhost && competesForBest
                                 && lapStartedAtMs > 0 && lap.DeviceTimeMs - lapStartedAtMs == lapTimeMs;
            lapSectors = CloseSplits(standing, sectorsAllowed, lapStartedAtMs, lap.DeviceTimeMs);

            // In step with the lap list whatever came before - a standing
            // brought back from a journal written before sectors were kept
            // has none for its earlier laps.
            var sectorIndex = standing.LapTimesMs.Count - 1;

            while (standing.SectorsAtLap.Count < sectorIndex)
            {
                standing.SectorsAtLap.Add(null);
            }

            if (standing.SectorsAtLap.Count > sectorIndex)
            {
                standing.SectorsAtLap.RemoveRange(sectorIndex, standing.SectorsAtLap.Count - sectorIndex);
            }

            standing.SectorsAtLap.Add(lapSectors);

            // Remembered, so it stays out of bests and streaks when the
            // laps are worked out again after a lap is removed by hand -
            // see RecomputeAfterLapRemoval.
            if (firstLapAfterRecovery)
            {
                standing.RecoveryLapIndexes.Add(standing.LapTimesMs.Count - 1);
            }

            // Were they ahead at the moment they finished this lap?
            //
            // Asked here rather than reconstructed afterwards, because the
            // running order BETWEEN crossings is never stored - this is the
            // only moment the question has an honest answer. Ordering is the
            // standings' own, so it agrees with the board. A ghost is never
            // credited, and one out in front does not rob the real leader.
            if (!isGhost && ReferenceEquals(RaceLeader(), standing))
            {
                standing.LapsLed++;
            }

            Log($"Lap counted: {lap.TransponderId}  lap={standing.Laps}  lap_time={lapTimeMs}ms" +
                (standing.ConsecutiveValidLaps >= StreakThreshold ? $"  streak={standing.ConsecutiveValidLaps}" : "") +
                (isFastestLap ? "  (new fastest lap)" : ""));
        }
        else
        {
            standing.ConsecutiveValidLaps = 0;
            // 20 Oct 2026: remembered, because a rejected pass leaves no
            // StreakAtLap entry - without this, taking a lap off by hand
            // read the run back from before the rejection and revived it.
            standing.RunBrokenSinceLastLap = true;
            standing.StreakLapTimeSumMs = 0;
            standing.StreakLapCount = 0;
            standing.RejectedLaps++;

            // The backwards clock has already said why, above.
            if (!clockWentBackwards)
            {
                Log($"Lap rejected ({RejectionReason(gapFromLastSeenMs, lapTimeMs, standing.LastLapDeviceTimeMs > 0)}): {lap.TransponderId}");
            }
        }

        // Safely, like everything else on the way to a finish: the flag is
        // taken further down this method, and a throwing lap listener used
        // to stop the race ending on the very crossing that ended it.
        SafeRaise(LapProcessed, h => h(standing, isValid, isFastestLap), nameof(LapProcessed));

        if (lapSectors is not null)
        {
            var sectorLap = standing.LapTimesMs.Count - 1;
            SafeRaise(SectorsRecorded, h => h(standing, sectorLap, lapSectors), nameof(SectorsRecorded));
        }

        if (isPersonalBest)
        {
            SafeRaise(PersonalBestLap, h => h(standing), nameof(PersonalBestLap));
        }

        // The leader crossing to start the final lap. Announced once, for
        // the first car to reach one short of the limit - by definition the
        // leader on laps. Practice has no limit-driven finish, so it never
        // has a last lap to call. Nor is it a black-flagged car's to start:
        // it is out of the race, and cannot put the checkered out (below).
        //
        // WHY (23 Nov 2026, bug hunt 24): nor once the checkered is out. Then
        // nobody is starting a final lap - this crossing is the one that
        // takes the flag, a few lines down - and the old check called "last
        // lap" for a car as it finished (a race whose clock ran out with the
        // leader two short of the lap limit, say).
        if (isValid && !isGhost && !standing.IsBlackFlagged && !IsPracticeMode && !_lastLapAnnounced
            && !IsFinishing
            && LapLimit.HasValue && standing.Laps == LapLimit.Value - 1)
        {
            _lastLapAnnounced = true;
            SafeRaise(LastLapStarted, h => h(standing), nameof(LastLapStarted));
        }

        // Crossing the line while the checkered is out IS taking it.
        // Deliberately after LapProcessed, so the lap that brought them home
        // is reported before the race is declared over.
        //
        // WHY (23 Oct 2026, timing layer, P1): and only if the checkered was
        // already out when this crossing arrived - see _checkeredOutAtClockMs.
        if (isValid && IsFinishing && FlagAppliesTo(lap.TransponderId) && CheckeredWasOutWhenThisPassArrived())
        {
            MarkTookTheFlag(lap.TransponderId);
        }

        // A ghost can take the flag once it is out (above), but can never
        // be the car that puts it out.
        //
        // Only while the race is still running: when this crossing was the
        // last car taking the flag, the race finished just above, and it
        // then logged "lap limit reached" underneath "race complete".
        //
        // Nor a black-flagged car (bug hunt 13). Its crossings are still
        // recorded, but it is out of the race, and the finish does not wait
        // for it (see EveryExpectedCarHasTakenTheFlag). A disqualified
        // leader who ignored the call to come in used to reach the limit
        // first and put the checkered out for the whole field a lap early -
        // or, with FinishOnNextCrossing off, end the race there and then.
        if (isValid && !isGhost && !standing.IsBlackFlagged && State == RaceState.Racing && LapLimit.HasValue && standing.Laps >= LapLimit.Value)
        {
            // Same reasoning as the time-limit guard in Tick() above.
            if (IsPracticeMode)
            {
                return;
            }

            // WHY (23 Nov 2026, bug hunt 24): said only by the car that
            // brings the checkered out. The old code said it again for every
            // lead-lap car crossing under the flag while others were still
            // out. What follows is unchanged: with the flag already out,
            // BeginFinishing does nothing and the flag was taken above.
            if (!IsFinishing)
            {
                Log($"Lap limit of {LapLimit.Value} reached by {lap.TransponderId}.");
            }

            if (FinishOnNextCrossing)
            {
                // The car that reached the limit has taken the flag on this
                // very crossing; everyone else takes it on their next one.
                BeginFinishing(timeLimitReached: false);
                MarkTookTheFlag(lap.TransponderId);
            }
            else
            {
                Finish(timeLimitReached: false);
            }
        }
    }

    /// <summary>
    /// Whether the checkered was already out at the moment the pass being
    /// judged arrived - always, when nobody knows when it came out (a race
    /// put back by an undo or the journal), which is how it always was.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P1): the poll can put the checkered out
    /// before a pass that arrived earlier has been got to. That pass crossed
    /// before the flag, so it is an ordinary lap and the driver takes the
    /// flag on their next crossing - exactly what happens when it is got to
    /// on time.
    /// </remarks>
    private bool CheckeredWasOutWhenThisPassArrived() =>
        _checkeredOutAtClockMs is not { } outAt || NowMs >= outAt;

    /// <summary>
    /// Why a pass was thrown out, in the words the race log uses.
    /// </summary>
    /// <remarks>
    /// The minimum lap time is checked against the PREVIOUS PASS of any kind
    /// - see the class-level comment on MinLapTimeMs - but the log used to
    /// print the time since the last COUNTED lap beside it. Rehearsal 3 (27
    /// Sep 2026) had cars parked on the loop, seen every two seconds: each
    /// pass rightly thrown out as two seconds after the one before, and
    /// logged as "too soon, 4000ms &lt; 3000ms minimum" - then 5999, then 8104
    /// - which reads as the check being broken. Both figures now, each
    /// saying what it is measured from.
    /// </remarks>
    /// <param name="gapFromLastSeenMs">Time since this car's previous pass, counted or not; null on its first pass of the race.</param>
    /// <param name="lapTimeMs">The lap time the pass would have had.</param>
    /// <param name="measuredFromCountedLap">True when <paramref name="lapTimeMs"/> runs from a counted lap, false when from the start.</param>
    internal string RejectionReason(long? gapFromLastSeenMs, long lapTimeMs, bool measuredFromCountedLap)
    {
        if (gapFromLastSeenMs is not { } gap)
        {
            // A first pass is only ever rejected for coming no later than the
            // start itself. No figure: for a driver restored from the race
            // journal the lap time is not measured from the start.
            return "first pass came at or before the start";
        }

        var lapPart = measuredFromCountedLap
            ? $"{lapTimeMs}ms since the last counted lap"
            : $"{lapTimeMs}ms since the start";

        return $"too soon: {gap}ms after the previous pass, {MinLapTimeMs}ms minimum; {lapPart}";
    }
}
