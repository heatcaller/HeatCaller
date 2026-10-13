namespace HeatCaller.Tests;

/// <summary>
/// The I-Lap connect after a Disconnect (bug hunt 7, 13 Oct 2026).
///
/// A connect overtaken by a Disconnect can still be inside the serial stack
/// - a Bluetooth port pairing, say - and a Connect asked for meanwhile used
/// to be dropped without a word: the status said Disconnected, the Connect
/// button stayed greyed out, and nothing was connecting. It is now kept and
/// started the moment the old attempt comes out.
///
/// The old attempt is held here inside its own status report, which is on
/// the connect thread and before the attempt has finished - the same place
/// BugHunt5FreezeTests holds it. The port is one that is not there, so no
/// real decoder is ever opened.
/// </summary>
public sealed class BugHunt7IlapTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>A COM port that is not there - see BugHunt5FreezeTests.</summary>
    private const string MissingPort = "COM247";

    private string CaptureFolder() => Path.Combine(_data.Folder, "ilap-capture");

    [Fact]
    public void A_connect_asked_for_while_an_overtaken_one_is_still_in_flight_is_started_when_it_finishes()
    {
        var client = new ILapClient(CaptureFolder());
        var firstError = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var secondError = new ManualResetEventSlim(false);
        var errors = 0;

        client.StatusChanged += (state, _) =>
        {
            if (state != DecoderConnectionState.Error)
            {
                return;
            }

            if (Interlocked.Increment(ref errors) == 1)
            {
                // The first attempt, held in flight.
                firstError.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }
            else
            {
                secondError.Set();
            }
        };

        client.Connect(MissingPort, 9600);
        Assert.True(firstError.Wait(TimeSpan.FromSeconds(10)), "The first connect never reported a result.");

        client.Disconnect();
        Assert.Equal(DecoderConnectionState.Disconnected, client.State);

        // Used to return having done nothing, with the state left at
        // Disconnected under a greyed-out Connect button.
        client.Connect(MissingPort, 9600);
        Assert.Equal(DecoderConnectionState.Connecting, client.State);

        release.Set();

        Assert.True(secondError.Wait(TimeSpan.FromSeconds(10)), "The connect asked for after the Disconnect never ran.");

        client.Dispose();
    }

    [Fact]
    public void A_disconnect_cancels_a_connect_still_waiting_for_the_overtaken_one()
    {
        var client = new ILapClient(CaptureFolder());
        var firstError = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var connectings = 0;
        var errors = 0;

        client.StatusChanged += (state, _) =>
        {
            if (state == DecoderConnectionState.Connecting)
            {
                Interlocked.Increment(ref connectings);
                return;
            }

            if (state == DecoderConnectionState.Error && Interlocked.Increment(ref errors) == 1)
            {
                firstError.Set();
                release.Wait(TimeSpan.FromSeconds(5));
            }
        };

        client.Connect(MissingPort, 9600);
        Assert.True(firstError.Wait(TimeSpan.FromSeconds(10)), "The first connect never reported a result.");

        client.Disconnect();
        client.Connect(MissingPort, 9600);

        // Given up on before the old attempt came out.
        client.Disconnect();
        release.Set();

        // Long enough for a connect to a missing port to have failed, had
        // one been started.
        Thread.Sleep(TimeSpan.FromSeconds(2));

        Assert.Equal(DecoderConnectionState.Disconnected, client.State);
        Assert.Equal(1, Volatile.Read(ref errors));

        // The first attempt's, and the waiting one's - and no third.
        Assert.Equal(2, Volatile.Read(ref connectings));

        client.Dispose();
    }
}
