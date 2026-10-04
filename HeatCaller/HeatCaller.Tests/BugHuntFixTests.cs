namespace HeatCaller.Tests;

/// <summary>
/// Pins for the timing bug hunt's fixes (4 Oct 2026) that can be checked
/// without a window: each one the shape of the scenario the hunt found.
/// </summary>
public sealed class BugHuntFixTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static byte[] Hex(string hex) =>
        hex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(b => Convert.ToByte(b, 16)).ToArray();

    /// <summary>A HID report as the EasyLap sends one: its length, then the bytes.</summary>
    private static byte[] Report(byte[] payload)
    {
        var report = new byte[64];
        report[0] = (byte)payload.Length;
        payload.CopyTo(report, 1);
        return report;
    }

    // ------------------------------------------------------------------
    //  The EasyLap framer, in step
    // ------------------------------------------------------------------

    [Fact]
    public void A_pass_short_of_a_byte_in_step_is_not_read_as_a_phantom_car_and_the_next_pass_survives()
    {
        var laps = new List<string>();
        var framer = new EasyLapFramer(_ => { }, pass => laps.Add(pass.TransponderId));

        // Rehearsal 3's own bytes: a heartbeat, two passes.
        var heartbeat = Hex("0b 2f 83 37 85 21 01 14 d0 01 03");
        var shortOfAByte = Hex("0d d6 84 fb d4 00 00 cb 80 21 01 55 f6").ToList();
        shortOfAByte.RemoveAt(4);
        var intact = Hex("0d 7d 84 c1 c4 00 00 4d 82 21 01 0c 56");

        // In step on heartbeats first.
        for (var i = 0; i < 4; i++)
        {
            framer.AddReport(Report(heartbeat));
        }

        framer.AddReport(Report(shortOfAByte.Concat(intact).Concat(heartbeat).ToArray()));
        framer.AddReport(Report(heartbeat));

        // The overrun used to make one pass of 13 bytes straddling the two -
        // transponder 84FB00, nobody's - and lose both. Now the damaged one
        // is lost and the intact one is read.
        Assert.Equal(new[] { "84C1C4" }, laps);
    }

    // ------------------------------------------------------------------
    //  The decoder clock map's first readings
    // ------------------------------------------------------------------

    [Fact]
    public void A_misframed_first_reading_is_not_trusted_and_the_good_ones_after_it_start_the_map()
    {
        var map = new DecoderClockMap();

        // A heartbeat whose clock bytes were noise, then good ones.
        map.Add(600_000, 0x800D0000);
        map.Add(601_000, 18_975_000);
        Assert.Null(map.Estimate(601_100));

        map.Add(602_000, 18_976_000);
        map.Add(603_000, 18_977_000);

        // The three good ones agree: the map starts again from them, and
        // the noise is gone. It used to be taken unchecked as one of the
        // first three, and the map gave an offset 24 days out.
        var estimate = map.Estimate(603_100);
        Assert.NotNull(estimate);
        Assert.Equal(603_000 - 18_977_000L, estimate.Value.Offset);
    }

    // ------------------------------------------------------------------
    //  Line Up
    // ------------------------------------------------------------------

    [Fact]
    public void Line_up_ignores_a_car_sat_over_both_loops()
    {
        var pairing = new LineUpPairing();

        // Held over both loops, each reading it every two seconds, 430 ms
        // apart - every repeat used to pair, and Save offered 430 ms.
        for (var t = 10_000L; t < 30_000; t += 2_000)
        {
            pairing.AddLine("100", t);
            pairing.AddSplit("100", t + 430);
        }

        Assert.Single(pairing.Gaps);
        Assert.Null(pairing.Answer);
    }

    [Fact]
    public void Line_up_gives_no_answer_while_the_gaps_disagree()
    {
        var pairing = new LineUpPairing();

        pairing.AddLine("100", 10_000);
        pairing.AddSplit("100", 10_020);
        pairing.AddLine("100", 20_000);
        pairing.AddSplit("100", 20_900);
        pairing.AddLine("100", 30_000);
        pairing.AddSplit("100", 31_400);

        Assert.Equal(3, pairing.Gaps.Count);
        Assert.Null(pairing.Answer);

        // Two more that agree with the middle one settle it.
        pairing.AddLine("100", 40_000);
        pairing.AddSplit("100", 40_880);
        pairing.AddLine("100", 50_000);
        pairing.AddSplit("100", 50_920);

        Assert.Equal(900, pairing.Answer);
    }

    // ------------------------------------------------------------------
    //  Sector records
    // ------------------------------------------------------------------

    [Fact]
    public void A_first_sector_record_is_not_taken_from_a_sliver_of_its_lap()
    {
        var track = new TrackLayout { Name = "Oval" };

        // The split heard two seconds early: 1.0 s of a 10 s lap.
        track.OfferSectors(new long[] { 1_000, 9_000 }, "Maya", DateTime.UtcNow);

        var records = track.CurrentSectorRecords(2);
        Assert.Null(records[0]);
        Assert.NotNull(records[1]);
    }

    // ------------------------------------------------------------------
    //  Duplicating a setup
    // ------------------------------------------------------------------

    [Fact]
    public void Duplicating_a_setup_from_before_chassis_sheets_keeps_what_it_says()
    {
        var old = new SetupEntry
        {
            DriverName = "Maya",
            Tires = "30 shore",
            Gearing = "8T / 64T",
            Springs = "Medium",
            RideHeight = "3.2 mm",
            Motor = "Stock 70T",
            Notes = "Pushes on entry",
            LinkedSessionId = "abc",
        };

        var copy = old.CloneForNewEntry();

        Assert.NotEqual(old.Id, copy.Id);
        Assert.Null(copy.LinkedSessionId);
        Assert.Equal(
            (old.Tires, old.Gearing, old.Springs, old.RideHeight, old.Motor, old.Notes),
            (copy.Tires, copy.Gearing, copy.Springs, copy.RideHeight, copy.Motor, copy.Notes));
    }
}
