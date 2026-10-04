namespace HeatCaller.Tests;

/// <summary>
/// The split loop's groundwork: tying an EasyLap's clock to the app's
/// (DecoderClockMap), knowing whose a split transponder is without it ever
/// being a line transponder (SplitTransponders, SplitOwnerOf), placing a
/// split pass on the line's clock (SplitLoop), and Line Up's pairing.
/// </summary>
public sealed class SplitLoopTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------- the clock map

    [Fact]
    public void The_clock_map_takes_the_quickest_reading_as_the_offset()
    {
        var map = new DecoderClockMap();

        // Decoder clock 1000 behind the app's; readings arrive 2-9 ms late.
        map.Add(11_002, 10_000);
        map.Add(12_009, 11_000);
        map.Add(13_004, 12_000);
        map.Add(14_005, 13_000);

        var estimate = map.Estimate(14_100);

        Assert.NotNull(estimate);
        Assert.Equal(1002, estimate.Value.Offset);
        Assert.Equal(15_002, map.ToApp(14_000, 14_100));
        Assert.Equal(13_998, map.ToDevice(15_000, 14_100));
    }

    [Fact]
    public void The_clock_map_is_not_trusted_too_soon_or_once_the_decoder_goes_quiet()
    {
        var map = new DecoderClockMap();

        map.Add(1_000, 0);
        map.Add(2_000, 1_000);
        Assert.Null(map.Estimate(2_100));

        map.Add(3_000, 2_000);
        Assert.NotNull(map.Estimate(3_100));

        // Nothing heard for longer than Fresh.
        Assert.Null(map.Estimate(3_000 + DecoderClockMap.Fresh + 1));
    }

    [Fact]
    public void A_decoder_that_restarts_is_learned_again()
    {
        var map = new DecoderClockMap();

        map.Add(101_000, 100_000);
        map.Add(102_000, 101_000);
        map.Add(103_000, 102_000);

        // Restarted: its clock is back near zero.
        map.Add(104_000, 50);
        Assert.Null(map.Estimate(104_100));

        map.Add(105_000, 1_050);
        map.Add(106_000, 2_050);
        Assert.Equal(103_950, map.Estimate(106_100)!.Value.Offset);
    }

    [Fact]
    public void A_misframed_heartbeat_is_held_back_and_then_dropped()
    {
        var map = new DecoderClockMap();

        map.Add(11_002, 10_000);
        map.Add(12_002, 11_000);
        map.Add(13_002, 12_000);

        // Noise for a clock: not trusted while it is held back...
        map.Add(13_500, 4_000_000_000);
        Assert.Null(map.Estimate(13_600));

        // ...and dropped when the next good reading contradicts it.
        map.Add(14_002, 13_000);
        Assert.Equal(1002, map.Estimate(14_100)!.Value.Offset);
    }

    // ----------------------------------------------- whose split transponder

    private static ProfileStore Profiles()
    {
        var store = ProfileStore.Load();

        var maya = new DriverProfile { Transponder = "100", DriverName = "Maya" };
        maya.SplitTransponders.Add(new OwnedTransponder { Id = "7001", CarId = "red" });
        Assert.True(store.TryAdd(maya, out _));

        // Dev's LINE transponder has the same number as Maya's split one.
        Assert.True(store.TryAdd(new DriverProfile { Transponder = "7001", DriverName = "Dev" }, out _));

        return store;
    }

    [Fact]
    public void A_split_transponder_is_never_a_line_transponder_and_the_numbers_are_separate()
    {
        var profiles = Profiles();

        Assert.Equal("Maya", profiles.SplitOwnerOf("7001")!.Value.Driver.DriverName);
        Assert.Equal("red", profiles.SplitOwnerOf("7001")!.Value.Entry.CarId);

        // The line still knows 7001 as Dev's, and Maya's split transponder
        // is nobody's on the line.
        Assert.Equal("Dev", profiles.Get("7001")!.DriverName);
        Assert.Null(profiles.SplitOwnerOf("100"));
    }

    [Fact]
    public void Two_drivers_cannot_have_the_same_split_transponder()
    {
        var profiles = Profiles();

        var dev = ProfileStore.Snapshot(profiles.Get("7001")!);
        dev.SplitTransponders.Add(new OwnedTransponder { Id = "7001" });

        Assert.Contains("Maya", profiles.DescribeSplitClash(dev, editing: "7001"));

        // Maya saving her own is fine.
        var maya = ProfileStore.Snapshot(profiles.Get("100")!);
        Assert.Null(profiles.DescribeSplitClash(maya, editing: "100"));
    }

    [Fact]
    public void A_profile_copy_keeps_its_split_transponders()
    {
        var profiles = Profiles();

        var copy = ProfileStore.Snapshot(profiles.Get("100")!);

        Assert.Equal("7001", Assert.Single(copy.SplitTransponders).Id);
    }

    [Fact]
    public void A_split_transponder_goes_with_its_car_and_undo_brings_it_back()
    {
        var profiles = Profiles();
        var cars = CarStore.Load();
        var undo = new UndoService();

        cars.Upsert(new Car { Id = "red", Name = "Red", OwnerTransponder = "100" });

        Assert.Null(CarTransferring.Apply(new CarTransferring.Plan("red", "7001", Array.Empty<string>()), cars, profiles, undo));

        Assert.Empty(profiles.Get("100")!.SplitTransponders);
        Assert.Equal("Dev", profiles.SplitOwnerOf("7001")!.Value.Driver.DriverName);
        Assert.Equal("red", profiles.SplitOwnerOf("7001")!.Value.Entry.CarId);

        Assert.NotNull(undo.Undo());

        Assert.Equal("Maya", profiles.SplitOwnerOf("7001")!.Value.Driver.DriverName);
        Assert.Empty(profiles.Get("7001")!.SplitTransponders);
    }

    // ------------------------------------------------------ the split loop

    [Fact]
    public void A_split_pass_is_placed_on_the_line_clock_with_the_line_up_taken_off()
    {
        var profiles = Profiles();

        // An I-Lap split (passes on the app clock), an EasyLap line whose
        // clock is 5000 behind the app's, lined up at 30 ms late.
        var split = new SplitLoop(profiles.SplitOwnerOf)
        {
            AppTimeOf = pass => pass.ReceivedMs,
            LineTimeOf = app => app - 5_000,
            OffsetMs = () => 30,
        };

        var seen = split.Record(new DirectLapEvent
        {
            TransponderId = "7001",
            ReceivedMs = 20_030,
            Decoder = DecoderConnection.ILap,
            Point = TimingPoint.Split1,
        });

        Assert.Equal("100", seen.Driver);
        Assert.Equal("red", seen.CarId);
        Assert.Equal(TimingPoint.Split1, seen.Point);
        Assert.Equal(20_000, seen.AppMs);
        Assert.Equal(15_000, seen.LineMs);
        Assert.Equal(15_030, seen.RawLineMs);
        Assert.Equal(20_030, split.LastPassAt);
    }

    [Fact]
    public void A_split_pass_waits_for_the_clocks_and_an_unknown_one_is_offered_for_adding()
    {
        var profiles = Profiles();

        var split = new SplitLoop(profiles.SplitOwnerOf)
        {
            AppTimeOf = pass => pass.ReceivedMs,
            LineTimeOf = _ => null,
        };

        var seen = split.Record(new DirectLapEvent { TransponderId = "8888", ReceivedMs = 1_000, Decoder = DecoderConnection.ILap });

        Assert.Null(seen.Driver);
        Assert.Null(seen.LineMs);
        Assert.Equal("8888", Assert.Single(split.UnknownRecently(2_000)));

        // Forgotten after ten minutes.
        Assert.Empty(split.UnknownRecently(1_000 + 10 * 60_000 + 1));
    }

    // ------------------------------------------------------------ Line Up

    [Fact]
    public void Line_up_pairs_each_crossing_by_driver_and_takes_the_median()
    {
        var pairing = new LineUpPairing();

        Assert.Null(pairing.AddLine("100", 10_000));
        Assert.Equal(20, pairing.AddSplit("100", 10_020));

        // The split read first this time; another driver's pass in between
        // is not paired with it.
        Assert.Null(pairing.AddSplit("100", 20_025));
        Assert.Null(pairing.AddLine("500", 20_000));
        Assert.Equal(25, pairing.AddLine("100", 20_000));

        Assert.Null(pairing.Answer);

        Assert.Null(pairing.AddLine("100", 30_000));
        Assert.Equal(90, pairing.AddSplit("100", 30_090));

        // Median of 20, 25, 90.
        Assert.Equal(25, pairing.Answer);
    }

    [Fact]
    public void Line_up_never_pairs_readings_too_far_apart()
    {
        var pairing = new LineUpPairing();

        Assert.Null(pairing.AddLine("100", 10_000));
        Assert.Null(pairing.AddSplit("100", 10_000 + LineUpPairing.Window + 1));
        Assert.Empty(pairing.Gaps);
    }

    // ------------------------------------------------- a pass and its copies

    [Fact]
    public void Copies_of_a_pass_keep_where_and_when_it_was_seen()
    {
        var pass = new DirectLapEvent { TransponderId = "200", DeviceTimeMs = 5, SignalHits = 3, ReceivedMs = 77, Decoder = DecoderConnection.EasyLap };

        var filed = pass.WithTransponder("100");
        Assert.Equal(("100", 5u, 3, 77L, DecoderConnection.EasyLap), (filed.TransponderId, filed.DeviceTimeMs, filed.SignalHits, filed.ReceivedMs, filed.Decoder));

        var split = pass.Stamped(DecoderConnection.ILap, 99, TimingPoint.Split1);
        Assert.Equal(("200", 99L, DecoderConnection.ILap, TimingPoint.Split1), (split.TransponderId, split.ReceivedMs, split.Decoder, split.Point));
    }
}
