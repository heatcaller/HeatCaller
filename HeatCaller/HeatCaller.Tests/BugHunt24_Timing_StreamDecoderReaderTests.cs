using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24: a pass the link held up for more than two seconds (a Wi-Fi
/// drop, a Bluetooth hiccup) and then delivered in a burst keeps the
/// decoder's own time. It used to be timed as it arrived, so a car that
/// crossed first was placed behind one that crossed after it, and its next
/// lap came out seconds short.
/// </summary>
public sealed class BugHunt24_Timing_StreamDecoderReaderTests
{
    private static StreamDecoderReader OpenStintReader(List<string> log) =>
        new(DecoderConnection.OpenStint, new OpenStintProtocol(), log.Add);

    private static List<DirectLapEvent> Feed(StreamDecoderReader reader, string message, long arrivedMs) =>
        reader.Feed(Encoding.ASCII.GetBytes(message + "\n"), arrivedMs);

    /// <summary>The decoder's clock read three times, a second apart: decoder time + 4000 is app time.</summary>
    private static void LearnTheClock(StreamDecoderReader reader)
    {
        Feed(reader, "S 1000 -40 5 0 0 1", 5_000);
        Feed(reader, "S 2000 -40 5 0 0 1", 6_000);
        Feed(reader, "S 3000 -40 5 0 0 1", 7_000);
    }

    [Fact]
    public void Two_passes_held_up_on_the_link_keep_the_decoders_times_and_their_order()
    {
        var log = new List<string>();
        var reader = OpenStintReader(log);

        LearnTheClock(reader);

        // One on-time pass, so the reader has used the map before.
        var onTime = Assert.Single(Feed(reader, "P 3200 OPN 9 3.5 20 0", 7_210));
        Assert.Equal(7_200u, onTime.DeviceTimeMs);

        Feed(reader, "S 4000 -40 5 0 0 1", 8_000);

        // The link stalls, then hands over both passes at once at 13000:
        // car 7 crossed at 10000 (3 s before), car 8 at 11500 (1.5 s before).
        var burst = Feed(reader, "P 6000 OPN 7 3.5 20 0\nP 7500 OPN 8 3.5 20 0", 13_000);

        Assert.Equal(2, burst.Count);
        Assert.Equal("7", burst[0].TransponderId);
        Assert.Equal("8", burst[1].TransponderId);
        Assert.Equal(10_000u, burst[0].DeviceTimeMs);
        Assert.Equal(11_500u, burst[1].DeviceTimeMs);
        Assert.True(burst[0].DeviceTimeMs < burst[1].DeviceTimeMs, "The car that crossed first is timed first.");
        Assert.DoesNotContain(log, line => line.Contains("restarted"));

        // The map came through it: the next on-time pass is on the decoder's clock.
        Feed(reader, "S 9100 -40 5 0 0 1", 13_100);
        var next = Assert.Single(Feed(reader, "P 9500 OPN 7 3.5 20 0", 13_510));

        Assert.Equal(13_500u, next.DeviceTimeMs);
    }

    [Fact]
    public void The_first_pass_of_the_night_held_up_on_the_link_is_still_timed_by_the_decoder()
    {
        var log = new List<string>();
        var reader = OpenStintReader(log);

        LearnTheClock(reader);

        // No pass yet. Both arrive at 11000: car 7 crossed at 8000, car 8 at 9500.
        var burst = Feed(reader, "P 4000 OPN 7 3.5 20 0\nP 5500 OPN 8 3.5 20 0", 11_000);

        Assert.Equal(2, burst.Count);
        Assert.Equal(8_000u, burst[0].DeviceTimeMs);
        Assert.Equal(9_500u, burst[1].DeviceTimeMs);
    }

    [Fact]
    public void A_pass_stamped_further_back_than_any_link_holds_one_is_timed_as_it_arrived()
    {
        var log = new List<string>();
        var reader = OpenStintReader(log);

        LearnTheClock(reader);

        var onTime = Assert.Single(Feed(reader, "P 3200 OPN 9 3.5 20 0", 7_210));
        Assert.Equal(7_200u, onTime.DeviceTimeMs);

        // Mapped, this would be 7500 - more than a minute before it arrived.
        var pass = Assert.Single(Feed(reader, "P 3500 OPN 7 3.5 20 0", 70_000));

        Assert.Equal(70_000u, pass.DeviceTimeMs);
    }

    [Fact]
    public void A_pass_stamped_ahead_of_its_arrival_is_still_timed_as_it_arrived()
    {
        var log = new List<string>();
        var reader = OpenStintReader(log);

        LearnTheClock(reader);

        // Stamped ten seconds ahead of the clock the decoder has been keeping.
        var pass = Assert.Single(Feed(reader, "P 14000 OPN 7 3.5 20 0", 8_000));

        Assert.Equal(8_000u, pass.DeviceTimeMs);
    }
}
