using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// A clock the replay controls: TimingClock-style milliseconds, and a wall
/// clock that moves with it from a fixed epoch.
/// </summary>
/// <remarks>
/// WHY (23 Oct 2026, timing layer, P0): bound to a RaceControlSession
/// through its clock seam (NowMsSource / UtcNowSource), so a five-minute
/// heat plays in no time and every figure is exact to the millisecond.
/// </remarks>
internal sealed class ReplayClock
{
    public static readonly DateTime DefaultEpoch = new(2026, 10, 23, 23, 0, 0, DateTimeKind.Utc);

    public ReplayClock(DateTime? epochUtc = null) => EpochUtc = epochUtc ?? DefaultEpoch;

    /// <summary>The wall clock when NowMs is 0.</summary>
    public DateTime EpochUtc { get; }

    public long NowMs { get; private set; }

    public DateTime UtcNow => EpochUtc.AddMilliseconds(NowMs);

    /// <summary>Moves on to <paramref name="ms"/> - never back.</summary>
    public void AdvanceTo(long ms)
    {
        if (ms > NowMs)
        {
            NowMs = ms;
        }
    }

    public void Bind(RaceControlSession race)
    {
        race.NowMsSource = () => NowMs;
        race.UtcNowSource = () => UtcNow;
    }
}

internal enum ReplayKind
{
    Pass,
    Command,
    Tick,
}

/// <summary>
/// One thing the race is given: a pass, a command, or the 500ms tick - with
/// when it ARRIVED (a pass reached the app, a button was pressed, the tick
/// was due) and when the race PROCESSES it. On time, the two are the same.
/// </summary>
internal sealed class ReplayInput
{
    public ReplayKind Kind { get; init; }

    public long ArrivedMs { get; init; }

    public long ProcessedMs { get; set; }

    /// <summary>The pass, for Kind Pass. Its ReceivedMs is ArrivedMs.</summary>
    public DirectLapEvent? Pass { get; init; }

    /// <summary>For a recorded pass: whose alternate transponder it was then.</summary>
    public string? AlternateOf { get; init; }

    public string Name { get; init; } = string.Empty;

    /// <summary>The command, for Kind Command.</summary>
    public Action<RaceControlSession, ReplayContext>? Apply { get; init; }

    /// <summary>Tie-break: the order things arrived in (a recording's Seq).</summary>
    public long Order { get; init; }

    public ReplayInput Copy(long? order = null) => new()
    {
        Kind = Kind,
        ArrivedMs = ArrivedMs,
        ProcessedMs = ProcessedMs,
        Pass = Pass,
        AlternateOf = AlternateOf,
        Name = Name,
        Apply = Apply,
        Order = order ?? Order,
    };
}

/// <summary>What a replayed command can reach: the clock, the snapshots taken so far, and a list of what could not be replayed.</summary>
internal sealed class ReplayContext
{
    public ReplayContext(ReplayClock clock) => Clock = clock;

    public ReplayClock Clock { get; }

    /// <summary>Snapshots taken during the replay, by the recording's Seq (or a script's own number).</summary>
    public Dictionary<long, RaceControlSession.SessionSnapshot> Snapshots { get; } = new();

    /// <summary>Who each transponder was an alternate of, as recorded - see ReplayHarness.FromRecording.</summary>
    public Dictionary<string, string> Alternates { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Commands the replay could not carry out, with why.</summary>
    public List<string> Skipped { get; } = new();
}

/// <summary>One driver's row at the end of a replay.</summary>
internal sealed record ReplayRow(
    string Transponder,
    int Position,
    int Laps,
    long BestLapMs,
    long ElapsedMs,
    long PenaltyMs,
    int Warnings,
    bool TookTheFlag,
    string LapTimes);

/// <summary>Where a replayed race ended up.</summary>
internal sealed class ReplayResult
{
    public required RaceControlSession Race { get; init; }

    public required RaceState State { get; init; }

    public required FlagState Flag { get; init; }

    /// <summary>The race clock at the end - frozen at the finish for a finished race.</summary>
    public required long ElapsedMs { get; init; }

    public required List<ReplayRow> Rows { get; init; }

    /// <summary>The order the cars took the checkered.</summary>
    public required List<string> FlagOrder { get; init; }

    public required List<string> Log { get; init; }

    public required List<string> Skipped { get; init; }

    public ReplayRow Row(string transponder) =>
        Rows.Single(r => string.Equals(r.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

    /// <summary>Everything that makes up the result, as text - two results are the same race when these match.</summary>
    public string Describe(bool withRaceClock = true)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"state={State} flag={Flag}");

        if (withRaceClock)
        {
            text.Append(CultureInfo.InvariantCulture, $" clock={ElapsedMs}");
        }

        text.AppendLine();

        foreach (var row in Rows)
        {
            text.AppendLine(row.ToString());
        }

        text.Append("flag order: ").AppendLine(string.Join(", ", FlagOrder));
        return text.ToString();
    }
}

/// <summary>
/// A race written out by hand: passes at the moment each car crossed, and
/// commands at the moment they were pressed, on the replay clock.
/// </summary>
internal sealed class ReplayScript
{
    /// <summary>The decoder's own clock runs this far ahead of the app's - as a real one does, from its own power-on.</summary>
    public const long DecoderOffsetMs = 5_000_000;

    /// <summary>How long a pass takes to reach the app after the car crosses (the USB link).</summary>
    public const long LatencyMs = 3;

    private readonly List<ReplayInput> _inputs = new();
    private long _order;

    /// <summary>The decoder's reading for a crossing at <paramref name="appMs"/>.</summary>
    public static uint DecoderTime(long appMs) => (uint)(appMs + DecoderOffsetMs);

    /// <summary>A car crossing the line at <paramref name="crossedMs"/>; it reaches the app LatencyMs later.</summary>
    public ReplayScript Pass(long crossedMs, string transponder)
    {
        var arrived = crossedMs + LatencyMs;

        _inputs.Add(new ReplayInput
        {
            Kind = ReplayKind.Pass,
            ArrivedMs = arrived,
            ProcessedMs = arrived,
            Name = transponder,
            Pass = new DirectLapEvent
            {
                TransponderId = transponder,
                DeviceTimeMs = DecoderTime(crossedMs),
                SignalHits = 40,
                SignalStrength = 120,
                ReceivedMs = arrived,
                Decoder = DecoderConnection.EasyLap,
                Point = TimingPoint.Line,
            },
            Order = ++_order,
        });

        return this;
    }

    /// <summary>A car lapping: its first crossing at <paramref name="firstMs"/>, then every <paramref name="lapMs"/>, <paramref name="crossings"/> times in all.</summary>
    public ReplayScript Laps(string transponder, long firstMs, long lapMs, int crossings)
    {
        for (var i = 0; i < crossings; i++)
        {
            Pass(firstMs + i * lapMs, transponder);
        }

        return this;
    }

    public ReplayScript Command(long atMs, string name, Action<RaceControlSession, ReplayContext> apply)
    {
        _inputs.Add(new ReplayInput
        {
            Kind = ReplayKind.Command,
            ArrivedMs = atMs,
            ProcessedMs = atMs,
            Name = name,
            Apply = apply,
            Order = ++_order,
        });

        return this;
    }

    public ReplayScript Command(long atMs, string name, Action<RaceControlSession> apply) =>
        Command(atMs, name, (race, _) => apply(race));

    public ReplayScript Stage(long atMs) => Command(atMs, "Stage", race => race.Stage());

    public ReplayScript Countdown(long atMs) => Command(atMs, "Countdown", race => race.StartCountdown());

    /// <summary>The green, stamped with the decoder's reading at that moment - as GreenFlagAfterAudioAsync's estimate would be.</summary>
    public ReplayScript Green(long atMs) => Command(atMs, "Green", race => race.GreenFlag(DecoderTime(atMs)));

    public ReplayScript Red(long atMs) => Command(atMs, "Red", race => race.RedFlag());

    public ReplayScript Resume(long atMs) => Command(atMs, "Resume", race => race.GreenFlag(DecoderTime(atMs)));

    /// <summary>Stage, a countdown and the green - the start every race night uses.</summary>
    public ReplayScript Start(long greenMs, long countdownMs = 5_000) =>
        Stage(greenMs - countdownMs - 5_000).Countdown(greenMs - countdownMs).Green(greenMs);

    /// <summary>Everything, in the order it arrived - and numbered in that order, which is the order a queue hands things over in.</summary>
    public List<ReplayInput> Inputs =>
        _inputs.OrderBy(i => i.ArrivedMs).ThenBy(i => i.Order).Select((i, index) => i.Copy(order: index + 1)).ToList();
}

/// <summary>How the race is handed what it is given - on time, or held up.</summary>
internal static class ReplayDelivery
{
    /// <summary>
    /// Everything on time, with the 500ms tick (MainForm's timer) due on
    /// every multiple of <paramref name="tickEveryMs"/> up to
    /// <paramref name="untilMs"/>.
    /// </summary>
    public static List<ReplayInput> OnTime(IEnumerable<ReplayInput> inputs, long untilMs, long tickEveryMs = 500)
    {
        var all = inputs.Select(i => i.Copy()).ToList();

        foreach (var input in all)
        {
            input.ProcessedMs = input.ArrivedMs;
        }

        var first = all.Count == 0 ? 0 : all.Min(i => i.ArrivedMs);
        var tick = (first / tickEveryMs + 1) * tickEveryMs;

        for (; tick <= untilMs; tick += tickEveryMs)
        {
            all.Add(new ReplayInput { Kind = ReplayKind.Tick, ArrivedMs = tick, ProcessedMs = tick, Name = "Tick", Order = long.MaxValue });
        }

        return all;
    }

    /// <summary>
    /// The window frozen from <paramref name="fromMs"/> to <paramref name="toMs"/>:
    /// every pass that arrived meanwhile waits, and so does the tick. When it
    /// wakes, the passes go first, in the order they arrived (they were posted
    /// with BeginInvoke), and then ONE tick - Windows keeps only one timer
    /// message waiting, and gives it the lowest priority.
    /// </summary>
    public static List<ReplayInput> WindowFrozen(IEnumerable<ReplayInput> onTime, long fromMs, long toMs)
    {
        var all = onTime.Select(i => i.Copy()).ToList();
        var heldTicks = all.Where(i => i.Kind == ReplayKind.Tick && i.ArrivedMs >= fromMs && i.ArrivedMs < toMs).ToList();

        foreach (var input in all.Where(i => i.Kind == ReplayKind.Pass && i.ArrivedMs >= fromMs && i.ArrivedMs < toMs))
        {
            input.ProcessedMs = toMs;
        }

        if (heldTicks.Count > 0)
        {
            foreach (var tick in heldTicks)
            {
                all.Remove(tick);
            }

            all.Add(new ReplayInput { Kind = ReplayKind.Tick, ArrivedMs = heldTicks[0].ArrivedMs, ProcessedMs = toMs, Name = "Tick", Order = long.MaxValue });
        }

        return all;
    }

    /// <summary>
    /// Only the passes held up: each one that arrived between
    /// <paramref name="fromMs"/> and <paramref name="toMs"/> is processed
    /// <paramref name="lateByMs"/> later - and never before one that arrived
    /// ahead of it, as a queue would hand them over - while the tick runs on
    /// time and can put the checkered out before they are got to.
    /// </summary>
    public static List<ReplayInput> PassesLate(IEnumerable<ReplayInput> onTime, long fromMs, long toMs, long lateByMs)
    {
        var all = onTime.Select(i => i.Copy()).ToList();
        long lastPass = long.MinValue;

        foreach (var input in all.Where(i => i.Kind == ReplayKind.Pass).OrderBy(i => i.ArrivedMs).ThenBy(i => i.Order))
        {
            if (input.ArrivedMs >= fromMs && input.ArrivedMs < toMs)
            {
                input.ProcessedMs = input.ArrivedMs + lateByMs;
            }

            input.ProcessedMs = Math.Max(input.ProcessedMs, lastPass);
            lastPass = input.ProcessedMs;
        }

        return all;
    }
}

/// <summary>
/// Plays what a race was given through a fresh RaceControlSession on a
/// replay clock, and says how it ended.
/// </summary>
/// <remarks>
/// WHY (23 Oct 2026, timing layer, P0): the safety net. A night recorded
/// with TimingRecorder, or written out by hand with ReplayScript, can be
/// played on time and played held up, and the two results compared - which
/// is how a change to how passes reach the race is proved not to change a
/// result. Pure: no window, no files, no real clock.
/// </remarks>
internal static class ReplayHarness
{
    /// <summary>
    /// Runs <paramref name="delivered"/> in the order the race would get to
    /// them: by when they are processed; a tick after anything else due at the
    /// same moment (the timer message is always the last one Windows hands
    /// over); then in the order they arrived.
    /// </summary>
    /// <param name="delivered">What the race is given, with when it processes each (see ReplayDelivery).</param>
    /// <param name="setup">The race's rules and lookups, set before anything is played.</param>
    /// <param name="clock">The clock to play on; a fresh one when left out.</param>
    /// <param name="recorder">Records the run as the app would - for the recorder's own tests.</param>
    /// <param name="setupWithContext">Setup that needs the run's context - FromRecording's alternates lookup.</param>
    public static ReplayResult Run(
        IEnumerable<ReplayInput> delivered,
        Action<RaceControlSession>? setup = null,
        ReplayClock? clock = null,
        TimingRecorder? recorder = null,
        Action<RaceControlSession, ReplayContext>? setupWithContext = null)
    {
        clock ??= new ReplayClock();
        var context = new ReplayContext(clock);
        var race = new RaceControlSession();
        clock.Bind(race);
        setup?.Invoke(race);
        setupWithContext?.Invoke(race, context);
        race.Recorder = recorder;

        var log = new List<string>();
        race.LogMessage += log.Add;

        var flagOrder = new List<string>();

        var ordered = delivered
            .OrderBy(i => i.ProcessedMs)
            .ThenBy(i => i.Kind == ReplayKind.Tick ? 1 : 0)
            .ThenBy(i => i.Order)
            .ToList();

        foreach (var input in ordered)
        {
            clock.AdvanceTo(input.ProcessedMs);

            switch (input.Kind)
            {
                case ReplayKind.Pass:
                    if (input.AlternateOf is not null)
                    {
                        if (input.AlternateOf.Length > 0)
                        {
                            context.Alternates[input.Pass!.TransponderId] = input.AlternateOf;
                        }
                        else
                        {
                            context.Alternates.Remove(input.Pass!.TransponderId);
                        }
                    }

                    race.ProcessLapEvent(input.Pass!);
                    break;

                case ReplayKind.Tick:
                    race.Tick();
                    race.PollReleases();
                    break;

                case ReplayKind.Command:
                    input.Apply!(race, context);
                    break;
            }

            // Who has taken the flag since the last step, in board order.
            foreach (var standing in race.GetOrderedStandings())
            {
                if (race.HasTakenFlag(standing.TransponderId) && !flagOrder.Contains(standing.TransponderId, StringComparer.OrdinalIgnoreCase))
                {
                    flagOrder.Add(standing.TransponderId);
                }
            }
        }

        race.Recorder = null;

        var rows = race.GetOrderedStandings()
            .Select((s, index) => new ReplayRow(
                s.TransponderId,
                index + 1,
                s.Laps,
                s.FastestLapMs,
                s.ElapsedMs,
                s.PenaltyMs,
                s.Warnings.Count,
                race.HasTakenFlag(s.TransponderId),
                string.Join(" ", s.LapTimesMs)))
            .ToList();

        return new ReplayResult
        {
            Race = race,
            State = race.State,
            Flag = race.Flag,
            ElapsedMs = (long)race.ElapsedRaceTime.TotalMilliseconds,
            Rows = rows,
            FlagOrder = flagOrder,
            Log = log,
            Skipped = context.Skipped,
        };
    }

    /// <summary>
    /// The inputs a recording holds, and the clock to play them on (its wall
    /// clock lined up with the recording's, so a RestoreAfterGap's "taken
    /// at" means the same moment).
    /// </summary>
    /// <param name="records">TimingRecorder.ReadAll of the file.</param>
    /// <param name="asRecorded">
    /// True plays each pass when the race processed it (ClockMs) - the night
    /// exactly as it ran, late passes and all. False plays each one when it
    /// arrived (ReceivedMs) - the night as it would have run with nothing late.
    /// Commands are played when they were recorded either way.
    /// </param>
    public static (List<ReplayInput> Inputs, ReplayClock Clock, Action<RaceControlSession, ReplayContext> Setup) FromRecording(
        IReadOnlyList<TimingRecord> records,
        bool asRecorded = true)
    {
        var header = records.FirstOrDefault(r => r.Kind == TimingRecord.HeaderKind) ?? records.FirstOrDefault();
        var clock = new ReplayClock(header is null ? null : header.Utc.AddMilliseconds(-header.ClockMs));
        var inputs = new List<ReplayInput>();

        foreach (var record in records)
        {
            if (record.Kind == TimingRecord.PassKind && record.Transponder is { } transponder)
            {
                var arrived = record.ReceivedMs is > 0 ? record.ReceivedMs.Value : record.ClockMs;

                inputs.Add(new ReplayInput
                {
                    Kind = ReplayKind.Pass,
                    ArrivedMs = arrived,
                    ProcessedMs = asRecorded ? record.ClockMs : arrived,
                    Name = transponder,
                    AlternateOf = record.Synthetic == true ? null : record.AlternateOf ?? string.Empty,
                    Pass = new DirectLapEvent
                    {
                        TransponderId = transponder,
                        DeviceTimeMs = (uint)(record.DeviceTimeMs ?? 0),
                        SignalHits = record.SignalHits ?? 0,
                        SignalStrength = record.SignalStrength ?? 0,
                        IsSynthetic = record.Synthetic ?? false,
                        ReceivedMs = record.ReceivedMs ?? 0,
                        Decoder = record.Decoder ?? 0,
                        Point = (TimingPoint)(record.Point ?? 0),
                    },
                    Order = record.Seq,
                });
            }
            else if (record.Kind == TimingRecord.CommandKind && record.Command is { } command)
            {
                var copy = record;

                inputs.Add(new ReplayInput
                {
                    Kind = ReplayKind.Command,
                    ArrivedMs = record.ClockMs,
                    ProcessedMs = record.ClockMs,
                    Name = command,
                    Apply = (race, context) => ApplyRecordedCommand(race, context, copy),
                    Order = record.Seq,
                });
            }
        }

        // The owner lookup answers what the recording said, as of the pass
        // being played - see ReplayInput.AlternateOf.
        Action<RaceControlSession, ReplayContext> setup = (race, context) =>
            race.AlternateTransponderOwner = id => context.Alternates.TryGetValue(id, out var owner) ? owner : null;

        return (inputs, clock, setup);
    }

    private static void ApplyRecordedCommand(RaceControlSession race, ReplayContext context, TimingRecord record)
    {
        var id = record.Transponder ?? string.Empty;

        switch (record.Command)
        {
            case "Config":
                ApplyConfig(race, record.Config);
                break;
            case "Stage":
                race.Stage();
                break;
            case "Countdown":
                race.StartCountdown();
                break;
            case "Green":
            case "Resume":
                race.GreenFlag(record.DeviceTimeMs ?? race.EstimatedDeviceTimeNowMs);
                break;
            case "Red":
                race.RedFlag();
                break;
            case "Yellow":
                race.YellowFlag();
                break;
            case "Checkered":
                race.CheckeredFlag();
                break;
            case "Reset":
                race.Reset();
                break;
            case "AdjustLaps":
                race.AdjustLaps(id, (int)(record.Value ?? 0));
                break;
            case "Penalty":
                race.AdjustPenaltyTime(id, record.Value ?? 0);
                break;
            case "ClearPenalties":
                race.ClearPenalties(id);
                break;
            case "Warning":
                race.IssueWarning(id, record.Text ?? string.Empty);
                break;
            case "RemoveWarning":
                race.RemoveLastWarning(id);
                break;
            case "Release":
                race.ReleaseDriver(id);
                break;
            case "ReleaseAll":
                race.ReleaseAll();
                break;
            case "SetGrid":
                race.SetGrid(record.Grid ?? new List<string>(), record.Value == 1);
                break;
            case "SetReleaseSchedule":
                race.SetReleaseSchedule(record.Offsets ?? new Dictionary<string, long>());
                break;
            case "RemoveGhost":
                race.RemoveGhostStanding(id);
                break;
            case "Snapshot":
                context.Snapshots[record.Seq] = race.CreateSnapshot();
                break;
            case "Restore":
                if (record.SnapshotId is { } restoreId && context.Snapshots.TryGetValue(restoreId, out var snapshot))
                {
                    race.RestoreSnapshot(snapshot);
                }
                else
                {
                    context.Skipped.Add($"{record.Seq}: Restore of a snapshot nobody recorded (a crash journal) - not replayed.");
                }

                break;
            case "RestoreAfterGap":
                if (record.SnapshotId is { } gapId && context.Snapshots.TryGetValue(gapId, out var held) && record.TakenUtc is { } takenUtc)
                {
                    race.RestoreSnapshotAfterGap(held, takenUtc);
                }
                else
                {
                    context.Skipped.Add($"{record.Seq}: RestoreAfterGap of a snapshot nobody recorded - not replayed.");
                }

                break;
            default:
                context.Skipped.Add($"{record.Seq}: {record.Command} cannot be replayed.");
                break;
        }
    }

    private static void ApplyConfig(RaceControlSession race, Dictionary<string, string>? config)
    {
        if (config is null)
        {
            return;
        }

        string? Get(string key) => config.TryGetValue(key, out var value) ? value : null;
        long? Long(string key) => long.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        bool? Bool(string key) => bool.TryParse(Get(key), out var b) ? b : null;

        race.TimeLimit = Long("TimeLimitMs") is { } limitMs ? TimeSpan.FromMilliseconds(limitMs) : null;
        race.LapLimit = Long("LapLimit") is { } laps ? (int)laps : null;
        race.FinishOnNextCrossing = Bool("FinishOnNextCrossing") ?? race.FinishOnNextCrossing;
        race.MinLapTimeMs = Long("MinLapTimeMs") is { } min ? (int)min : race.MinLapTimeMs;

        if (Enum.TryParse<RaceStartType>(Get("StartType"), out var startType))
        {
            race.StartType = startType;
        }

        if (Enum.TryParse<StaggerClock>(Get("StaggerClockMode"), out var stagger))
        {
            race.StaggerClockMode = stagger;
        }

        race.DetectJumpStarts = Bool("DetectJumpStarts") ?? race.DetectJumpStarts;
        race.JumpStartPenaltyMs = Long("JumpStartPenaltyMs") ?? race.JumpStartPenaltyMs;
        race.JumpStartCostsTheLap = Bool("JumpStartCostsTheLap") ?? race.JumpStartCostsTheLap;

        if (double.TryParse(Get("StreakWindowPercent"), NumberStyles.Float, CultureInfo.InvariantCulture, out var window))
        {
            race.StreakWindowPercent = window;
        }

        race.StreakFasterLapBreaks = Bool("StreakFasterLapBreaks") ?? race.StreakFasterLapBreaks;
        race.IsPracticeMode = Bool("IsPracticeMode") ?? race.IsPracticeMode;
        race.IsDemoMode = Bool("IsDemoMode") ?? race.IsDemoMode;
        race.SplitCount = Long("SplitCount") is { } splits ? (int)splits : race.SplitCount;
        race.WarningLadder = Get("WarningLadder") is { Length: > 0 } ladder
            ? JsonSerializer.Deserialize<List<WarningStep>>(ladder)
            : null;
    }
}
