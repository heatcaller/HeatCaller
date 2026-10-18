using System.Globalization;
using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// The EasyLap decoder path, played back from a real night: every HID report
/// rehearsal 3 read (Captures\easylap-rehearsal3.log) goes through
/// EasyLapFramer - the code that read it on the night - and the A-Main's
/// passes then go on through RaceControlSession.
///
/// Until these, nothing between the USB bytes and a counted lap was tested:
/// the race-night test feeds laps as race times, never as decoder readings.
///
/// Every expected figure below is read from the same night's console: the
/// "[decoded msg]" and "[LAP EVENT]" lines in the capture itself, and the
/// "Lap counted" lines the race wrote (quoted beside the table).
/// </summary>
public sealed class EasyLapCaptureTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- The capture -------------------------------------------------------------------

    private sealed record CaptureLine(TimeSpan At, string Kind, string Text);

    /// <summary>"6:20:36.584 PM  [LAP EVENT] transponder=..." - the console's time stamp, two spaces, the line.</summary>
    private static readonly Regex LinePattern = new(
        @"^(?<time>\d{1,2}:\d\d:\d\d\.\d{3} [AP]M)  (?<text>\[(?<kind>[^\]]+)\].*)$",
        RegexOptions.CultureInvariant);

    private static List<CaptureLine> ReadCapture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Captures", "easylap-rehearsal3.log");
        Assert.True(File.Exists(path), $"The capture is missing from the test output: {path}");

        var lines = new List<CaptureLine>();

        foreach (var raw in File.ReadLines(path))
        {
            if (raw.Length == 0 || raw.StartsWith('#'))
            {
                continue;
            }

            var match = LinePattern.Match(raw);
            Assert.True(match.Success, $"A capture line the test can't read: {raw}");

            var at = DateTime.ParseExact(match.Groups["time"].Value, "h:mm:ss.fff tt", CultureInfo.InvariantCulture).TimeOfDay;
            lines.Add(new CaptureLine(at, match.Groups["kind"].Value, match.Groups["text"].Value.TrimEnd()));
        }

        return lines;
    }

    private static byte[] ReportBytes(CaptureLine line) =>
        line.Text["[raw HID] ".Length..]
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(hex => byte.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
            .ToArray();

    // --- The framer ---------------------------------------------------------------------

    [Fact]
    public void Every_report_decodes_exactly_as_it_did_on_the_night()
    {
        var capture = ReadCapture();
        var produced = new List<string>();
        var passes = new List<DirectLapEvent>();
        var framer = new EasyLapFramer(produced.Add, passes.Add);

        foreach (var report in capture.Where(l => l.Kind == "raw HID"))
        {
            framer.AddReport(ReportBytes(report));
        }

        // Line for line: each report echoed, then every message it completed
        // and every pass decoded from one - 3,244 reports, 893 messages, 155
        // passes, no resynchronising needed.
        Assert.Equal(capture.Select(l => l.Text).ToList(), produced);

        Assert.Equal(155, passes.Count);
        Assert.Equal(new[] { "84C1C4", "84FBD4" }, passes.Select(p => p.TransponderId).Distinct().OrderBy(t => t, StringComparer.Ordinal));
        Assert.DoesNotContain(produced, l => l.StartsWith("[framing]", StringComparison.Ordinal));

        // One pass read off by hand: "0d e4 84 c1 c4 00 00 cc 26 29 01 3e f8",
        // the first of the A-Main - transponder 84 c1 c4, clock 0x012926cc
        // little-endian, 0x3e hits, 0xf8 strength.
        Assert.Contains(passes, p => p.TransponderId == "84C1C4" && p.DeviceTimeMs == 19_474_124 && p.SignalHits == 62 && p.SignalStrength == 248);

        // The decoder's clock never runs backwards for a car.
        foreach (var car in passes.GroupBy(p => p.TransponderId))
        {
            var times = car.Select(p => p.DeviceTimeMs).ToList();
            Assert.True(times.SequenceEqual(times.OrderBy(t => t)), $"{car.Key}'s passes are out of order");
        }
    }

    [Fact]
    public void Every_heartbeat_carries_the_decoders_clock_once_a_second()
    {
        var capture = ReadCapture();

        // In stream order, so each pass can be set against the heartbeat
        // that follows it.
        var stream = new List<(bool Heartbeat, uint Clock)>();
        var framer = new EasyLapFramer(_ => { }, pass => stream.Add((false, pass.DeviceTimeMs)), clock => stream.Add((true, clock)));

        foreach (var report in capture.Where(l => l.Kind == "raw HID"))
        {
            framer.AddReport(ReportBytes(report));
        }

        var heartbeats = stream.Where(s => s.Heartbeat).Select(s => s.Clock).ToList();

        // 738 heartbeats over 737 seconds, the first read off by hand:
        // "0b 2f 83 37 85 21 01 ..." - clock bytes 37 85 21 01, 0x01218537.
        Assert.Equal(738, heartbeats.Count);
        Assert.Equal(18_974_007u, heartbeats[0]);

        for (var i = 1; i < heartbeats.Count; i++)
        {
            var gap = heartbeats[i] - heartbeats[i - 1];
            Assert.InRange(gap, 990u, 1010u);
        }

        // The same clock as the passes: the heartbeat after a pass reads
        // just after the pass's own time (a pass is reported a moment
        // after the car crossed).
        for (var i = 0; i < stream.Count; i++)
        {
            if (stream[i].Heartbeat)
            {
                continue;
            }

            var next = stream.Skip(i + 1).FirstOrDefault(s => s.Heartbeat);

            if (next.Heartbeat)
            {
                Assert.InRange(next.Clock - stream[i].Clock, 0u, 1500u);
            }
        }
    }

    [Fact]
    public void A_pass_split_across_reports_after_a_stray_byte_is_still_read()
    {
        var produced = new List<string>();
        var passes = new List<DirectLapEvent>();
        var framer = new EasyLapFramer(produced.Add, passes.Add);

        // The same pass as above, but with a stray 0xff in front and cut in
        // two: three data bytes in the first report, eleven in the second.
        framer.AddReport(new byte[] { 0x03, 0xff, 0x0d, 0xe4 });
        Assert.Empty(passes);

        framer.AddReport(new byte[] { 0x0b, 0x84, 0xc1, 0xc4, 0x00, 0x00, 0xcc, 0x26, 0x29, 0x01, 0x3e, 0xf8 });

        var pass = Assert.Single(passes);
        Assert.Equal("84C1C4", pass.TransponderId);
        Assert.Equal(19_474_124u, pass.DeviceTimeMs);
        Assert.Equal(62, pass.SignalHits);
        Assert.Equal(248, pass.SignalStrength);

        Assert.Contains("[framing] Skipped 1 byte(s) to resynchronize with the decoder stream.", produced);
    }

    // --- The A-Main, through the race ------------------------------------------------------

    // Where Race Control moved the race on, placed between the passes either
    // side of it in the console:
    //
    //  - Stage: "Staging - racers called to the grid." sits between the passes
    //    at 6:20:34.572 and 6:20:36.584.
    //  - Countdown: "Countdown started." between 6:20:44.621 and 6:20:46.630.
    //  - Green: after "No round selected" at 6:21:00.122, before the pass at
    //    6:21:00.697, which was the first one judged as racing.
    private static readonly TimeSpan StageAt = new(0, 18, 20, 34, 700);
    private static readonly TimeSpan CountdownAt = new(0, 18, 20, 45, 600);
    private static readonly TimeSpan GreenAt = new(0, 18, 21, 0, 400);

    /// <summary>After the last pass of the race (6:23:12.730), which took the checkered flag.</summary>
    private static readonly TimeSpan FinishedAt = new(0, 18, 23, 13, 0);

    /// <summary>
    /// The decoder's clock at the green, as Race Control estimates it: the
    /// last pass before it (19,496,124 at 6:20:58.690) plus the 1.7s of wall
    /// clock since. Lap times don't depend on it - both cars were credited
    /// at the green and are timed from their own crossings - only whether a
    /// crossing falls inside the 8s credit window does.
    /// </summary>
    private const long GreenDeviceTimeMs = 19_497_830;

    /// <summary>The console's "Lap counted" lines for the A-Main, in order - lines 2 to 11 for each car.</summary>
    private static readonly (string Car, int Lap, long LapMs)[] CountedOnTheNight =
    {
        ("84FBD4", 2, 15292), ("84C1C4", 2, 15288),
        ("84FBD4", 3, 5902), ("84C1C4", 3, 5901),
        ("84FBD4", 4, 6742), ("84C1C4", 4, 6757),
        ("84FBD4", 5, 5406), ("84C1C4", 5, 5407),
        ("84C1C4", 6, 5684), ("84FBD4", 6, 5711),
        ("84FBD4", 7, 5888), ("84C1C4", 7, 5898),
        ("84FBD4", 8, 5865), ("84C1C4", 8, 5875),
        ("84FBD4", 9, 5496), ("84C1C4", 9, 5494),
        ("84C1C4", 10, 5695), ("84FBD4", 10, 5716),
        ("84FBD4", 11, 72608), ("84C1C4", 11, 72642),
    };

    [Fact]
    public void The_A_Main_replays_to_the_laps_it_counted_on_the_night()
    {
        var capture = ReadCapture();

        var race = new RaceControlSession();
        var log = new List<string>();
        race.LogMessage += log.Add;

        var counted = new List<(string Car, int Lap, long LapMs)>();
        race.LapProcessed += (standing, isValid, _) =>
        {
            if (isValid)
            {
                counted.Add((standing.TransponderId, standing.Laps, standing.LapTimesMs[^1]));
            }
        };

        var framer = new EasyLapFramer(_ => { }, race.ProcessLapEvent);

        var staged = false;
        var countingDown = false;
        var green = false;

        foreach (var report in capture.Where(l => l.Kind == "raw HID"))
        {
            if (report.At > FinishedAt)
            {
                break;
            }

            if (!staged && report.At >= StageAt)
            {
                race.Stage();
                staged = true;
            }

            if (!countingDown && report.At >= CountdownAt)
            {
                race.StartCountdown();
                countingDown = true;
            }

            if (!green && report.At >= GreenAt)
            {
                race.GreenFlag(GreenDeviceTimeMs);
                green = true;
            }

            framer.AddReport(ReportBytes(report));
        }

        Assert.True(green, "The replay never reached the green");

        // The race counted what it counted on the night.
        Assert.Equal(CountedOnTheNight, counted);

        // Both cars sat on the loop through staging and the countdown. On the
        // night each was called as a jump start; now each is said to be
        // sitting there (ParkedOnLoopTests), and neither is flagged.
        Assert.Contains("84C1C4 is sitting on the loop", string.Join("\n", log));
        Assert.Contains("84FBD4 is sitting on the loop", string.Join("\n", log));
        Assert.False(race.HasJumpStarted("84C1C4"));
        Assert.False(race.HasJumpStarted("84FBD4"));

        // Both were on the line when the green dropped, and credited with the start lap, as on the night.
        Assert.Contains("84FBD4 was past the line at the green - credited with the start lap.", log);
        Assert.Contains("84C1C4 was past the line at the green - credited with the start lap.", log);

        // Four passes each thrown out while they were still parked after the
        // green - every one about two seconds after the pass before it. The
        // night's log called the last "8137ms < 3000ms minimum"
        // (LapRejectionTests).
        var standings = race.GetOrderedStandings();
        Assert.Equal(4, standings.Single(s => s.TransponderId == "84C1C4").RejectedLaps);
        Assert.Equal(4, standings.Single(s => s.TransponderId == "84FBD4").RejectedLaps);
        Assert.Contains("Lap rejected (too soon: 2138ms after the previous pass, 3000ms minimum; 8137ms since the last counted lap): 84C1C4", log);
    }
}
