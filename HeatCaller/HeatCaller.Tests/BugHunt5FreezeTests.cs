namespace HeatCaller.Tests;

/// <summary>
/// The window freeze (bug hunt 5, 9 Oct 2026) - the leading explanation for
/// the ten seconds rehearsal 2 lost, and for why HangWatchdog exists at all.
///
/// Every few seconds, on the UI thread, the auto-connect tick opened a
/// decoder: DirectDecoderClient.Connect enumerated HID devices, opened the
/// CP2110 and sent it two feature reports, and ILapClient.Connect disposed
/// one SerialPort and opened another. None of it had a timeout. A CP2110
/// wedged on the bus with its device node still present, a Bluetooth SPP
/// port that takes half a minute to pair, or a USB serial adapter pulled
/// out of the socket, each block in the driver with no exception and almost
/// no CPU - and the window stops. The reads had been moved off the UI thread
/// long before; the connects had not.
///
/// The I-Lap's raw capture was the other half: a StreamWriter with AutoFlush
/// on, written to under _lineLock - a synchronous flush to disk per decoder
/// line, with the UI thread waiting on that same lock inside Disconnect. The
/// lap event was raised under it too, so the whole race pipeline ran holding
/// it.
///
/// What is tested here is what can be tested without a decoder on the bench:
/// that a serial connect hands back before the port is opened, that a second
/// one cannot start on top of the first, that the open happens on another
/// thread, and that nothing waits on _lineLock while a lap event is being
/// handled. The EasyLap connect is the same change in the same shape, but
/// exercising it would enumerate the real USB bus and could open the club's
/// own decoder, so it has no test here. Nor do the three AppUpdates
/// callbacks, which need the main window.
/// </summary>
public sealed class BugHunt5FreezeTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>
    /// A COM port that is not there. Named with a number no USB adapter is
    /// given, so opening it fails on any machine rather than finding
    /// somebody's real decoder.
    /// </summary>
    private const string MissingPort = "COM247";

    /// <summary>The real I-Lap passing record from the bench - see ILapParseTests.</summary>
    private const string RealPassing = "\u0001@\t210\t4385919\t344.699";

    private string CaptureFolder() => Path.Combine(_data.Folder, "ilap-capture");

    // --- The serial connect, off the UI thread -------------------------------

    [Fact]
    public void A_second_serial_connect_while_one_is_in_flight_is_refused()
    {
        var client = new ILapClient(CaptureFolder());

        // Not disposed through `using`: the events below outlive it in
        // reverse declaration order, and the disconnect Dispose does would
        // run the handler again.
        var inFlight = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var connectings = 0;

        client.StatusChanged += (state, _) =>
        {
            if (state == DecoderConnectionState.Connecting)
            {
                Interlocked.Increment(ref connectings);
                return;
            }

            // The result of the attempt, on whichever thread opened the
            // port. Held there, so the attempt is still in flight while the
            // test asks for a second one.
            inFlight.Set();
            release.Wait(TimeSpan.FromSeconds(2));
        };

        client.Connect(MissingPort, 9600);

        Assert.True(inFlight.Wait(TimeSpan.FromSeconds(10)), "The first connect never reported a result.");

        // On the old synchronous Connect this line is not reached until the
        // handler above has given up waiting - the open was on this thread -
        // and the second go then publishes Connecting all over again.
        client.Connect(MissingPort, 9600);

        release.Set();

        Assert.Equal(1, Volatile.Read(ref connectings));

        client.Dispose();
    }

    [Fact]
    public void Opening_the_serial_port_happens_off_the_thread_that_asked_for_it()
    {
        var client = new ILapClient(CaptureFolder());
        var asking = Environment.CurrentManagedThreadId;
        var openedOn = 0;
        var settled = new ManualResetEventSlim(false);

        client.StatusChanged += (state, _) =>
        {
            if (state is DecoderConnectionState.Error or DecoderConnectionState.Connected)
            {
                openedOn = Environment.CurrentManagedThreadId;
                settled.Set();
            }
        };

        client.Connect(MissingPort, 9600);

        Assert.True(settled.Wait(TimeSpan.FromSeconds(10)), "The connect attempt never settled.");
        Assert.NotEqual(asking, openedOn);

        client.Dispose();
    }

    // --- The line lock, free while a pass is handled -------------------------

    [Fact]
    public void A_disconnect_does_not_wait_on_a_lap_event_handler()
    {
        // The freeze shape itself: Disconnect is the UI thread, and it takes
        // _lineLock. The lap event used to be raised while that lock was
        // held, so everything the race does with a pass - and the capture's
        // flush to disk before it - was between the window and its own
        // disconnect.
        var client = new ILapClient(CaptureFolder());
        var handlerRan = new ManualResetEventSlim(false);
        var disconnected = new ManualResetEventSlim(false);

        client.LapEventReceived += _ =>
        {
            var other = new Thread(() =>
            {
                client.Disconnect();
                disconnected.Set();
            })
            {
                IsBackground = true,
                Name = "BugHunt5 disconnect",
            };

            other.Start();
            handlerRan.Set();
            disconnected.Wait(TimeSpan.FromSeconds(2));
        };

        client.FeedLine(RealPassing);

        Assert.True(handlerRan.IsSet, "The lap handler never ran.");
        Assert.True(disconnected.IsSet, "Disconnect waited on the lap handler, so the lap event is still raised under the line lock.");

        client.Dispose();
    }

    [Fact]
    public void Another_line_can_be_read_while_a_lap_event_is_being_handled()
    {
        var client = new ILapClient(CaptureFolder());
        var fed = new ManualResetEventSlim(false);

        client.LapEventReceived += _ =>
        {
            // A clock tick, not a pass: it goes through the same lock and the
            // same capture, and raises nothing, so this cannot recurse.
            var other = new Thread(() =>
            {
                client.FeedLine("@00000124");
                fed.Set();
            })
            {
                IsBackground = true,
                Name = "BugHunt5 reader",
            };

            other.Start();
            fed.Wait(TimeSpan.FromSeconds(2));
        };

        client.FeedLine(RealPassing);

        Assert.True(fed.IsSet, "A second line could not be read while a lap event was being handled.");

        client.Dispose();
    }
}
