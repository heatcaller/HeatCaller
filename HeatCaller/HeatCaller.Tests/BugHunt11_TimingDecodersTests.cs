using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026): a pass reported just after a heartbeat that
/// carries a later clock - a car on the loop as the decoder is connected -
/// is normal reporting order, not a decoder that restarted.
/// </summary>
public sealed class BugHunt11_TimingDecodersTests
{
    private static StreamDecoderReader OpenStintReader(List<string> log) =>
        new(DecoderConnection.OpenStint, new OpenStintProtocol(), log.Add);

    private static List<DirectLapEvent> Feed(StreamDecoderReader reader, string message, long arrivedMs) =>
        reader.Feed(Encoding.ASCII.GetBytes(message + "\n"), arrivedMs);

    [Fact]
    public void A_pass_reported_after_a_later_heartbeat_at_connect_is_not_a_restart()
    {
        var log = new List<string>();
        var reader = OpenStintReader(log);

        // Connected while a car was on the loop: the status comes first, then
        // the pass for the crossing 50ms before it.
        Feed(reader, "S 50000 -40 5 0 0 1", 10_000);
        Assert.Single(Feed(reader, "P 49950 OPN 7 3.5 20 0", 10_100));
        Feed(reader, "S 51000 -40 5 0 0 1", 11_000);

        Assert.DoesNotContain(log, line => line.Contains("restarted"));

        // Three readings kept, so the next pass is on the decoder's clock.
        var pass = Assert.Single(Feed(reader, "P 51500 OPN 8 3.5 20 0", 11_520));

        Assert.Equal(11_500u, pass.DeviceTimeMs);
    }

    [Fact]
    public void A_clock_well_behind_the_latest_reading_is_still_a_restart()
    {
        var log = new List<string>();
        var reader = OpenStintReader(log);

        Feed(reader, "S 50000 -40 5 0 0 1", 10_000);
        Feed(reader, "S 51000 -40 5 0 0 1", 11_000);
        Feed(reader, "S 52000 -40 5 0 0 1", 12_000);

        // Back below the first reading by more than any reporting delay.
        Feed(reader, "S 47000 -40 5 0 0 1", 13_000);

        Assert.Contains(log, line => line.Contains("restarted"));

        var pass = Assert.Single(Feed(reader, "P 47500 OPN 7 3.5 20 0", 13_600));

        Assert.Equal(13_600u, pass.DeviceTimeMs);
    }
}
