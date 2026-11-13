namespace HeatCaller;

/// <summary>
/// A position report being read out, as the board can cut it off - see
/// <see cref="AnnouncementService"/>'s CorrectPositionReportOnAir.
/// </summary>
/// <param name="Order">The cars it names, by transponder, leader first.</param>
/// <param name="Depth">How far down the order it reads.</param>
/// <param name="TakenUnderInterrupt">The flag generation it was taken off the queue under, or null for one no flag cuts.</param>
/// <param name="TakenUnderCountdown">The countdown generation it was taken off the queue under.</param>
internal sealed record PositionReportOnAir(IReadOnlyList<string> Order, int Depth, int? TakenUnderInterrupt, int? TakenUnderCountdown)
{
    private int _cut;

    /// <summary>Marks it cut. True the first time only, so one report is corrected once.</summary>
    public bool MarkCut() => Interlocked.Exchange(ref _cut, 1) == 0;

    public bool Cut => Volatile.Read(ref _cut) == 1;

    private IReadOnlyList<string>? _stillOnTheBoard;

    /// <summary>
    /// The cars it names that are still on the board, leader first - Order
    /// less any taken off since it was read (a ghost switched off).
    /// </summary>
    public IReadOnlyList<string> StillOnTheBoard => Volatile.Read(ref _stillOnTheBoard) ?? Order;

    /// <summary>
    /// A car it names has been taken off the board (10 Nov 2026, bug hunt
    /// 15). Kept on this same report rather than a copy, because the
    /// report's work takes it off air by reference.
    /// </summary>
    public void LeftTheBoard(string transponder)
    {
        var before = StillOnTheBoard;

        if (before.Contains(transponder, StringComparer.OrdinalIgnoreCase))
        {
            Volatile.Write(ref _stillOnTheBoard,
                before.Where(car => !string.Equals(car, transponder, StringComparison.OrdinalIgnoreCase)).ToList());
        }
    }
}

/// <summary>
/// A position report the order changes under while it is being read out
/// (9 Nov 2026, Asher: "cut it off and wire it like the other examples to
/// correct it. the next report may take some time to come in").
/// </summary>
/// <remarks>
/// It used to be let finish - "a rundown stopped halfway is worse than one
/// a car moved in, and the next report puts it right" - but the next
/// report is five leader laps away by default, so the room was left with
/// the wrong order on the voice and the right one on the screen. Now it
/// is treated like a battle line: when a car it names changes place (a
/// pass on track, or a ruling), it is cut off after the usual beat
/// (StopCurrentPlaybackAfterABeat) and a short correction goes next in the
/// queue: "Hold on - Sam takes the lead! So it's Sam leads, Bill second,
/// Frank third."
///
/// ONLY A CHANGE OF PLACE. A car crossing the line again does not cut it:
/// on a five-second track the leader crosses during nearly every report,
/// and the order it is reading is still the order. And ONCE: the
/// correction is not cut in its turn - it is dropped unsaid instead if the
/// order moves again before it is heard.
///
/// The correction gives places, not gaps: a car that has just made a pass
/// is mid-lap for everyone behind it, so the gaps would be a lap old.
///
/// FOR THE LEAD the correction IS the lead change call, so the ordinary
/// one for the same crossing is not queued as well - the same rule as the
/// battle pass (CallTheBattlePass).
/// </remarks>
public sealed partial class AnnouncementService
{
    internal const string ReportCorrectionForTheLead = "[shouting] Hold on - {driver} takes the lead! So it's {positions}.";

    internal const string ReportCorrectionForAPlace = "[excited] Hold on - {driver} is through into {place}! So it's {positions}.";

    internal const string ReportRulingForTheLead = "[shouting] And with that ruling, {driver} takes the lead! So it's {positions}.";

    internal const string ReportRulingForAPlace = "[excited] And with that ruling, {driver} is up into {place}! So it's {positions}.";

    /// <summary>The position report being read out now, or null. Under _voiceQueueLock.</summary>
    private PositionReportOnAir? _positionReportOnAir;

    /// <summary>
    /// The first place, from the top, where <paramref name="now"/> names a
    /// different car from <paramref name="read"/>, or -1 when they name the
    /// same cars in the same order. A list that got shorter or longer
    /// differs at the end of the shorter one.
    /// </summary>
    internal static int FirstChangedPlace(IReadOnlyList<string> read, IReadOnlyList<string> now)
    {
        var shared = Math.Min(read.Count, now.Count);

        for (var i = 0; i < shared; i++)
        {
            if (!string.Equals(read[i], now[i], StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return read.Count == now.Count ? -1 : shared;
    }

    /// <summary>
    /// Each car's place as a report of a heat classes share says it
    /// (WordSharedRunningOrder): its class and its place in it, or - for a
    /// car the map does not know - no class and its place overall.
    /// </summary>
    private static List<(string Class, int Place)> ClassPlaces(IReadOnlyList<string> order, IReadOnlyDictionary<string, string> classOf)
    {
        var places = new List<(string Class, int Place)>(order.Count);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < order.Count; i++)
        {
            if (!classOf.TryGetValue(order[i]?.Trim() ?? string.Empty, out var cls) || string.IsNullOrWhiteSpace(cls))
            {
                places.Add((string.Empty, i + 1));
                continue;
            }

            places.Add((cls, counts[cls] = counts.GetValueOrDefault(cls) + 1));
        }

        return places;
    }

    /// <summary>
    /// FirstChangedPlace for a heat classes share (13 Nov 2026, bug hunt
    /// 17): the first car, from the top of <paramref name="now"/>, whose
    /// place in its own class is not the one <paramref name="read"/> gave
    /// it, or -1 when every car is where the report said. The report names
    /// class places only, so two cars of different classes swapping on the
    /// road change no word of it - and it was cut off and "corrected" with
    /// an overall place and the same class places it had just read.
    /// </summary>
    internal static int FirstChangedClassPlace(IReadOnlyList<string> read, IReadOnlyList<string> now, IReadOnlyDictionary<string, string> classOf)
    {
        var readPlaces = ClassPlaces(read, classOf);
        var was = new Dictionary<string, (string Class, int Place)>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < read.Count; i++)
        {
            was[read[i]] = readPlaces[i];
        }

        var nowPlaces = ClassPlaces(now, classOf);

        for (var i = 0; i < now.Count; i++)
        {
            if (!was.TryGetValue(now[i], out var before)
                || before.Place != nowPlaces[i].Place
                || !string.Equals(before.Class, nowPlaces[i].Class, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        // The same cars in the same class places, less some: it differs
        // where the shorter one ends, as FirstChangedPlace has it.
        return read.Count == now.Count ? -1 : Math.Min(read.Count, now.Count);
    }

    /// <summary>
    /// Where the order now differs from the one a report read: by overall
    /// place, or by class place in a heat classes share.
    /// </summary>
    private int FirstChangedPlaceAsReported(IReadOnlyList<string> read, IReadOnlyList<string> now) =>
        SharedHeatClasses?.Invoke() is { Count: > 0 } classOf
            ? FirstChangedClassPlace(read, now, classOf)
            : FirstChangedPlace(read, now);

    /// <summary>The correction's line, before {positions} is filled - see the class remarks.</summary>
    /// <param name="className">
    /// In a heat classes share, the mover's class, with <paramref name="place"/>
    /// its place in that class: "takes the lead in Stock", "through into
    /// second in Stock" - the places the report itself gives.
    /// </param>
    internal static string ReportCorrectionTemplate(string driver, int place, bool byRuling, string? className = null)
    {
        if (!string.IsNullOrWhiteSpace(className))
        {
            return ReportCorrectionTemplate(driver, place, byRuling)
                .Replace("takes the lead!", $"takes the lead in {className}!", StringComparison.Ordinal)
                .Replace($"into {LeadTracker.SpokenOrdinal(place)}!", $"into {LeadTracker.SpokenOrdinal(place)} in {className}!", StringComparison.Ordinal);
        }

        var template = (byRuling, place <= 1) switch
        {
            (false, true) => ReportCorrectionForTheLead,
            (false, false) => ReportCorrectionForAPlace,
            (true, true) => ReportRulingForTheLead,
            (true, false) => ReportRulingForAPlace,
        };

        return template
            .Replace("{driver}", driver, StringComparison.Ordinal)
            .Replace("{place}", LeadTracker.SpokenOrdinal(place), StringComparison.Ordinal);
    }

    private static List<string> OrderOf(IReadOnlyList<PositionReportOrder.Row> rows) =>
        rows.Select(row => row.Standing.Transponder).ToList();

    /// <summary>
    /// Called by a position report as it reaches the speaker - from here the
    /// board can cut it off. On the voice thread, inside the line's work, so
    /// the generations it was taken off the queue under are to hand.
    /// </summary>
    private PositionReportOnAir PutPositionReportOnAir(IReadOnlyList<string> order, int depth)
    {
        var report = new PositionReportOnAir(order, depth, _dequeuedUnderGeneration.Value, _dequeuedUnderCountdown.Value);

        lock (_voiceQueueLock)
        {
            _positionReportOnAir = report;
        }

        return report;
    }

    /// <summary>What a position report does as it reaches the speaker, for tests - which cannot hold one in the speaker while the board moves.</summary>
    internal void PutPositionReportOnAirForTest(IReadOnlyList<string> order, int depth)
    {
        var report = new PositionReportOnAir(order, depth, Volatile.Read(ref _interruptGeneration), Volatile.Read(ref _countdownCutGeneration));

        lock (_voiceQueueLock)
        {
            _positionReportOnAir = report;
        }
    }

    /// <summary>
    /// A car taken off the board mid-race (a ghost switched off) is taken
    /// out of the report being read too - see CorrectPositionReportOnAir.
    /// Under _voiceQueueLock, from OnStandingRemoved.
    /// </summary>
    private void ForgetInPositionReportOnAir(string transponder) =>
        _positionReportOnAir?.LeftTheBoard(transponder);

    /// <summary>The report has finished, been cut or been dropped.</summary>
    private void TakePositionReportOffAir(PositionReportOnAir? report)
    {
        if (report is null)
        {
            return;
        }

        lock (_voiceQueueLock)
        {
            if (ReferenceEquals(_positionReportOnAir, report))
            {
                _positionReportOnAir = null;
            }
        }
    }

    /// <summary>True when a flag, the countdown or Reset has cut everything since the voice loop took this report.</summary>
    private bool CutSinceTaken(PositionReportOnAir report) =>
        (report.TakenUnderInterrupt is int interrupt && interrupt != Volatile.Read(ref _interruptGeneration))
        || (report.TakenUnderCountdown is int countdown && countdown != Volatile.Read(ref _countdownCutGeneration));

    /// <summary>
    /// Asked on every board update, on the timing thread: cuts off a position
    /// report being read out when a car it names has changed place, and
    /// queues the correction next. Returns true when the correction names a
    /// new leader, so the lead change call for the same crossing is not
    /// queued behind it.
    /// </summary>
    /// <param name="byRuling">A correction by hand moved the order, not the track.</param>
    private bool CorrectPositionReportOnAir(bool byRuling)
    {
        PositionReportOnAir? report;

        lock (_voiceQueueLock)
        {
            report = _positionReportOnAir;
        }

        // A report a flag, the countdown or Reset has already cut is not in
        // the speaker any more - and something else may be, which must not
        // be stopped.
        if (report is null || report.Cut || CutSinceTaken(report))
        {
            return false;
        }

        if (RunningOrderNow(report.Depth) is not { Count: >= 2 } now)
        {
            return false;
        }

        // A car taken off the board while it was read (a ghost switched
        // off) has not been passed: the car behind it moving up into its
        // slot was called as a pass, or as taking a lead it was simply left
        // (10 Nov 2026, bug hunt 16). So it is compared without that car,
        // and against no more cars than are left of what it read - the car
        // pulled up from beyond its depth has not got through either.
        var read = report.StillOnTheBoard;
        var nowOrder = OrderOf(now);

        if (read.Count < report.Order.Count && nowOrder.Count > read.Count)
        {
            nowOrder = nowOrder.Take(read.Count).ToList();
        }

        var place = FirstChangedPlaceAsReported(read, nowOrder);

        if (place < 0 || !report.MarkCut())
        {
            return false;
        }

        var wasSpeaking = StopCurrentPlaybackAfterABeat();

        var mover = place < now.Count ? now[place].Standing : null;
        var why = mover is null
            ? "the order it was reading changed"
            : byRuling
                ? $"a correction by hand put {DisplayNameFor(mover.Transponder)} into {Ordinal(place + 1)}"
                : $"{DisplayNameFor(mover.Transponder)} got through into {Ordinal(place + 1)}";

        StatusMessage?.Invoke($"[voice] Position report {(wasSpeaking ? "cut off mid-sentence" : "dropped before it spoke")} - {why}.");

        if (Muted || _postRaceQuiet || mover is null)
        {
            return false;
        }

        QueuePositionReportCorrection(now, place, report.Depth, byRuling);

        return place == 0;
    }

    /// <summary>
    /// Where the correction for a cut-off report goes: NEXT, ahead of
    /// everything waiting but an instruction to a driver sitting still for
    /// it (a release, the grid call, staging) - which is a few words, and a
    /// car left on the line otherwise.
    /// </summary>
    /// <remarks>
    /// 11 Nov 2026, Asher: "another thing fired, then correction, and by that
    /// time the correction was wrong ... ensure corrections fire during what
    /// they are correcting." It went in the slot a kept call gets
    /// (HeldOverSlot), behind every race moment and reminder already
    /// waiting. In the 14:44 race that was a twelve-second AI line about a
    /// personal best - the correction ran out its few seconds behind it and
    /// was dropped unheard - and then the race's fastest lap, which put the
    /// correction five seconds after the cut, by when the pass it named was
    /// being undone. A correction is the end of the line it cut off, so it
    /// is said in that line's place; what was waiting waits one line more.
    /// </remarks>
    internal static int CorrectionSlot(IReadOnlyList<AnnouncementEventType> queued)
    {
        var i = 0;

        while (i < queued.Count && IsInstructionToADriver(queued[i]))
        {
            i++;
        }

        return i;
    }

    private static bool IsInstructionToADriver(AnnouncementEventType type) => type is
        AnnouncementEventType.DriverRelease or
        AnnouncementEventType.GridCall or
        AnnouncementEventType.RaceStaging;

    /// <summary>
    /// The first place a line queued after a correction may go: behind any
    /// correction still waiting, so a kept call or a lap call that would
    /// otherwise go "next" does not push it back. 0 when none waits.
    /// </summary>
    internal static int PastWaitingCorrections(IReadOnlyList<bool> isCorrection)
    {
        for (var i = isCorrection.Count - 1; i >= 0; i--)
        {
            if (isCorrection[i])
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>PastWaitingCorrections for the queue as it stands. Under _voiceQueueLock.</summary>
    private int PastWaitingCorrectionsLocked() =>
        PastWaitingCorrections(_voiceQueue.Select(waiting => waiting.IsCorrection).ToList());

    /// <summary>
    /// Puts the correction for a cut-off position report next in the queue
    /// (CorrectionSlot), worded from <paramref name="now"/>, with the few
    /// seconds a battle's pass call gets (QueueBattlePass).
    /// </summary>
    private void QueuePositionReportCorrection(List<PositionReportOrder.Row> now, int place, int depth, bool byRuling)
    {
        var mover = now[place].Standing;
        var worded = OrderOf(now);

        // In a heat classes share the mover is named by the class place it
        // has taken, as the order after it is (13 Nov 2026, bug hunt 17).
        var inClass = SharedHeatClasses?.Invoke() is { Count: > 0 } classOf
            ? ClassPlaces(worded, classOf)[place]
            : (Class: string.Empty, Place: place + 1);
        var template = ReportCorrectionTemplate(SpokenNameFor(mover), inClass.Place, byRuling, inClass.Class);
        var words = WordRunningOrder(now, template, sayGaps: false, placesOnly: true);

        if (words.Length == 0)
        {
            return;
        }

        var config =_voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.PositionReport));
        var speed = config?.SpeedFor(_voiceStore.Current.Speed);

        RaiseAnnouncementText(words, mover.Transponder);

        lock (_voiceQueueLock)
        {
            var at = CorrectionSlot(_voiceQueue.Select(waiting => waiting.Type).ToList());

            _voiceQueue.Insert(at, new QueuedVoiceItem(AnnouncementEventType.PositionReport, mover.Transponder, OnAir: "Position report",
                SayByMs: (byRuling ? TimingClock.NowMs : LapSeenAt(mover.Transponder)) + LapCallSayWithinMs, ShownText: words, Work: async () =>
            {
                try
                {
                    // Not cut in its turn (see the class remarks) - dropped
                    // unsaid if the order moves again before it is heard.
                    await SynthesizeAndPlayAsync(words, "[voice] Position report correction", speedOverride: speed,
                        whyNotNow: () => RunningOrderNow(depth) is { Count: >= 2 } after && FirstChangedPlaceAsReported(worded, OrderOf(after)) >= 0
                            ? "the order moved again before it was said"
                            : null);
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[voice] Position report correction failed: {ex.Message}");
                }
            })
            {
                IsCorrection = true,
            });
        }

        _voiceQueueSignal.Release();
    }
}
