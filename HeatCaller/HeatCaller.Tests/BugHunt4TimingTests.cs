namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 4, timing slice.
///
/// The transponder health record is never written over after it could not
/// be read at startup, the rule most other stores already follow.
///
/// An I-Lap split loop left waiting for its port connects once that port -
/// and only that one - is listed again (MainForm.WaitingSplitLoopCanConnect,
/// the decision the auto-connect tick makes).
///
/// The first-run wizard's COM port picker starts on nothing, rather than on
/// a port nobody chose, when the remembered one isn't there - what it shows
/// is what it saves. Rescan keeps the port already picked, and picks the
/// one that just appeared when the decoder is plugged in.
///
/// Line Up holding off practice's gate auto-start, and Connect reading the
/// split loop's picked COM port, live in MainForm's wiring and have no test
/// here: they need the main window.
/// </summary>
public sealed class BugHunt4TimingTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- An I-Lap split loop waiting for its port ----------------------------------------

    [Fact]
    public void A_waiting_I_Lap_split_loop_connects_once_its_port_is_listed_again()
    {
        Assert.True(MainForm.WaitingSplitLoopCanConnect(
            splitLoopOn: true, DecoderConnection.ILap, lineConnected: true, "COM3", new[] { "COM1", "com3" }));
    }

    [Fact]
    public void A_waiting_split_loop_never_connects_another_port_or_without_the_line()
    {
        string[] withoutIt = { "COM1", "COM5" };
        string[] withIt = { "COM1", "COM3" };

        // Still not there: COM5 is somebody else's port, not the I-Lap's.
        Assert.False(MainForm.WaitingSplitLoopCanConnect(true, DecoderConnection.ILap, true, "COM3", withoutIt));

        // Nothing remembered, the line not connected, the split loop turned
        // off, or the I-Lap now the line.
        Assert.False(MainForm.WaitingSplitLoopCanConnect(true, DecoderConnection.ILap, true, " ", withIt));
        Assert.False(MainForm.WaitingSplitLoopCanConnect(true, DecoderConnection.ILap, false, "COM3", withIt));
        Assert.False(MainForm.WaitingSplitLoopCanConnect(false, DecoderConnection.ILap, true, "COM3", withIt));
        Assert.False(MainForm.WaitingSplitLoopCanConnect(true, DecoderConnection.EasyLap, true, "COM3", withIt));
    }

    // --- The first-run wizard's COM port --------------------------------------------------

    [Fact]
    public void The_first_run_port_picker_starts_on_nothing_when_the_remembered_port_is_missing()
    {
        // A laptop with a Bluetooth port, COM4, and the I-Lap not plugged in yet.
        Assert.Equal(-1, FirstRunWizard.PortToSelect(new[] { "COM1", "COM4" }, "COM5"));
        Assert.Equal(-1, FirstRunWizard.PortToSelect(new[] { "COM1", "COM4" }, string.Empty));
        Assert.Equal(-1, FirstRunWizard.PortToSelect(new[] { "COM1", "COM4" }, null));
        Assert.Equal(-1, FirstRunWizard.PortToSelect(Array.Empty<string>(), "COM4"));
    }

    [Fact]
    public void The_first_run_port_picker_starts_on_the_remembered_port_when_it_is_there()
    {
        Assert.Equal(1, FirstRunWizard.PortToSelect(new[] { "COM1", "COM4", "COM7" }, "com4"));
    }

    [Fact]
    public void Rescan_keeps_the_port_already_picked_while_it_is_still_listed()
    {
        // COM7 picked by hand, nothing remembered yet - and a Rescan used to
        // drop it, back to the (blank) remembered port.
        string[] before = { "COM1", "COM7" };
        string[] after = { "COM1", "COM7", "COM9" };

        Assert.Equal(1, FirstRunWizard.PortToSelect(after, remembered: null, current: "COM7", listedBefore: before));
        Assert.Equal(1, FirstRunWizard.PortToSelect(after, remembered: "COM9", current: "com7", listedBefore: before));
    }

    [Fact]
    public void Rescan_picks_the_port_that_just_appeared_when_the_decoder_is_plugged_in()
    {
        // The hint: "No port listed? Plug the decoder in and press Rescan".
        Assert.Equal(0, FirstRunWizard.PortToSelect(new[] { "COM3" }, remembered: null, current: null, listedBefore: Array.Empty<string>()));

        // Beside a Bluetooth port that was there already, and over a
        // remembered port that is still listed.
        Assert.Equal(1, FirstRunWizard.PortToSelect(new[] { "COM1", "COM3", "COM4" }, remembered: "COM4", current: null, listedBefore: new[] { "COM1", "COM4" }));

        // A picked port that has gone (unplugged and back on another number).
        Assert.Equal(1, FirstRunWizard.PortToSelect(new[] { "COM1", "COM6" }, remembered: null, current: "COM5", listedBefore: new[] { "COM1", "COM5" }));
    }

    [Fact]
    public void Rescan_picks_nothing_when_nothing_new_appeared_and_nothing_is_remembered()
    {
        string[] ports = { "COM1", "COM4" };

        Assert.Equal(-1, FirstRunWizard.PortToSelect(ports, remembered: null, current: null, listedBefore: ports));
        Assert.Equal(-1, FirstRunWizard.PortToSelect(ports, remembered: "COM5", current: null, listedBefore: new[] { "COM1", "COM4", "COM5" }));

        // Two at once (a Bluetooth pairing adds a pair) - no telling which is
        // the decoder.
        Assert.Equal(-1, FirstRunWizard.PortToSelect(new[] { "COM1", "COM5", "COM6" }, remembered: null, current: null, listedBefore: new[] { "COM1" }));

        // Still the remembered port when it is listed.
        Assert.Equal(1, FirstRunWizard.PortToSelect(ports, remembered: "COM4", current: null, listedBefore: ports));

        // The first listing has nothing to compare with, so every port being
        // "new" there picks none of them - a Bluetooth port is not the I-Lap.
        Assert.Equal(-1, FirstRunWizard.PortToSelect(ports, remembered: null, current: null, listedBefore: null));
    }

    // --- Transponder health locked at startup --------------------------------------------

    /// <summary>A pass that carried a reading, as the EasyLap's do.</summary>
    private static DirectLapEvent Read(string transponder) =>
        new() { TransponderId = transponder, SignalHits = 5, SignalStrength = 120 };

    [Fact]
    public void Transponder_health_locked_at_startup_is_not_written_over_by_tonights_readings()
    {
        // Four nights on record for one transponder, five readings each.
        var earlier = TransponderHealthStore.Load();

        for (var night = 0; night < 4; night++)
        {
            for (var pass = 0; pass < TransponderHealthStore.LeastReadings; pass++)
            {
                earlier.Record(Read("84C1C4"), new DateTime(2026, 9, 1 + night, 20, 0, 0));
            }
        }

        earlier.Save();
        var path = _data.PathOf("transponder-health.json");
        var before = File.ReadAllText(path);

        // A backup tool has the file open as HeatCaller starts.
        TransponderHealthStore tonight;

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            tonight = TransponderHealthStore.Load();
        }

        // Tonight's first readings, then the minute's save - and the close's.
        tonight.Record(Read("84C1C4"));
        tonight.SaveIfDue();
        tonight.Save();

        Assert.Equal(before, File.ReadAllText(path));
        Assert.Equal(4, TransponderHealthStore.Load().Assess("84C1C4").NightsOnRecord);
    }

    [Fact]
    public void A_damaged_transponder_health_file_is_kept_aside_and_the_record_starts_again()
    {
        File.WriteAllText(_data.PathOf("transponder-health.json"), "{ this is not json");

        var store = TransponderHealthStore.Load();

        for (var pass = 0; pass < TransponderHealthStore.LeastReadings; pass++)
        {
            store.Record(Read("84C1C4"));
        }

        store.Save();

        Assert.Equal(1, TransponderHealthStore.Load().Assess("84C1C4").NightsOnRecord);
    }
}
