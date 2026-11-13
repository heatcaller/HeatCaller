using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: a pass stamped well behind the decoder's latest clock (a car
/// that sat on the loop) is not a decoder that restarted, and a repeat of a
/// crossing is dropped whichever side of the first read its time lands.
/// </summary>
public sealed class BugHunt17_StreamDecoderReaderTests
{
    private static StreamDecoderReader OpenStintReader(List<string> log) =>
        new(DecoderConnection.OpenStint, new OpenStintProtocol(), log.Add);

    private static List<DirectLapEvent> Feed(StreamDecoderReader reader, string message, long arrivedMs) =>
        reader.Feed(Encoding.ASCII.GetBytes(message + "\n"), arrivedMs);

    [Fact]
    public void A_pass_from_a_car_that_sat_on_the_loop_is_not_a_restart_and_keeps_the_clock_map()
    {
        var log = new List<string>();
        var reader = OpenStintReader(log);

        Feed(reader, "S 50000 -40 5 0 0 1", 10_000);
        Feed(reader, "S 51000 -40 5 0 0 1", 11_000);
        Feed(reader, "S 52000 -40 5 0 0 1", 12_000);
        Feed(reader, "S 53000 -40 5 0 0 1", 13_000);

        // On the loop for 2.5 s: reported as it leaves, stamped from when it arrived.
        var late = Assert.Single(Feed(reader, "P 50500 OPN 7 3.5 20 0", 13_050));

        Assert.DoesNotContain(log, line => line.Contains("restarted"));
        Assert.Equal(13_050u, late.DeviceTimeMs);

        // The map was kept, so the next car is on the decoder's clock at once.
        var next = Assert.Single(Feed(reader, "P 53500 OPN 8 3.5 20 0", 13_520));

        Assert.Equal(13_500u, next.DeviceTimeMs);
    }

    [Fact]
    public void A_pass_stamped_further_back_than_any_dwell_is_still_a_restart()
    {
        var log = new List<string>();
        var reader = OpenStintReader(log);

        Feed(reader, "S 100000 -40 5 0 0 1", 10_000);
        Feed(reader, "S 101000 -40 5 0 0 1", 11_000);
        Feed(reader, "S 102000 -40 5 0 0 1", 12_000);

        var pass = Assert.Single(Feed(reader, "P 500 OPN 7 3.5 20 0", 13_000));

        Assert.Contains(log, line => line.Contains("restarted"));
        Assert.Equal(13_000u, pass.DeviceTimeMs);
    }

    [Fact]
    public void A_repeat_whose_time_lands_just_before_the_first_read_is_still_a_repeat()
    {
        var log = new List<string>();
        var reader = OpenStintReader(log);

        // Just connected: the first read is timed as it arrived (10100).
        Feed(reader, "S 50000 -40 5 0 0 1", 10_000);
        var first = Assert.Single(Feed(reader, "P 49950 OPN 7 3.5 20 0", 10_100));
        Assert.Equal(10_100u, first.DeviceTimeMs);

        // The clock is learned, so the same crossing sent again maps to 9950.
        Feed(reader, "S 51000 -40 5 0 0 1", 11_000);

        Assert.Empty(Feed(reader, "P 49950 OPN 7 3.5 20 0", 11_020));
        Assert.Contains(log, line => line.Contains("Suppressed repeat of 7") && line.Contains("150ms before"));
    }
}
