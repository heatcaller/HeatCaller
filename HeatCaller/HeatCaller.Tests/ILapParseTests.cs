namespace HeatCaller.Tests;

/// <summary>
/// The I-Lap passing record, as the real decoder sent it on the bench on
/// 2 Oct 2026 (ilap-raw-2026-10-02.log):
///   SOH '@' TAB 210 TAB 4385919 TAB 344.699
/// 210 is the decoder's own number and 4385919 the car's transponder. The
/// first parser took 210 - the first run of digits - for every car.
/// </summary>
public sealed class ILapParseTests
{
    private const string RealPassing = "\u0001@\t210\t4385919\t344.699";

    private static List<DirectLapEvent> Feed(params string[] lines)
    {
        var laps = new List<DirectLapEvent>();
        using var client = new ILapClient();
        client.LapEventReceived += laps.Add;

        foreach (var line in lines)
        {
            client.FeedLine(line);
        }

        return laps;
    }

    [Fact]
    public void A_real_passing_record_reads_the_transponder_not_the_decoder_number()
    {
        Assert.Equal("4385919", ILapClient.TryParseILapPassing(RealPassing));

        var laps = Feed(RealPassing);
        Assert.Single(laps);
        Assert.Equal("4385919", laps[0].TransponderId);
    }

    [Fact]
    public void Leading_zeros_on_the_transponder_are_dropped()
    {
        Assert.Equal("4567", ILapClient.TryParseILapPassing("\u0001@\t210\t0004567\t12.000"));
    }

    [Fact]
    public void A_framed_record_that_is_not_a_passing_is_not_a_lap()
    {
        Assert.Null(ILapClient.TryParseILapPassing("\u0001#\t210\t4385919\t344.699"));
        Assert.Empty(Feed("\u0001#\t210\t000123", "\u0001%\t210"));
    }

    [Fact]
    public void The_decoders_once_a_second_clock_is_never_a_lap()
    {
        // As read off COM4 on 3 Oct 2026, with and without the SOH frame.
        Assert.True(ILapClient.IsILapClockTick("@00000124"));
        Assert.True(ILapClient.IsILapClockTick("\u0001@00000059"));
        Assert.False(ILapClient.IsILapClockTick(RealPassing));

        Assert.Empty(Feed("@00000001", "@00000002", "\u0001@00000100", "@00000124"));
    }

    [Fact]
    public void The_short_passing_form_reads_its_two_digit_transponder()
    {
        // Passes read off COM4 on 3 Oct 2026, between clock ticks.
        Assert.False(ILapClient.IsILapClockTick("@040000070442"));
        Assert.Equal("4", ILapClient.TryParseILapPassing("@040000070442"));

        var laps = Feed("@00000007", "@040000070442", "@00000008");
        Assert.Single(laps);
        Assert.Equal("4", laps[0].TransponderId);
    }

    [Fact]
    public void An_unframed_line_still_gets_the_tolerant_reading()
    {
        Assert.Null(ILapClient.TryParseILapPassing("PASS 4385919 12.345"));

        var laps = Feed("PASS 4385919 12.345");
        Assert.Single(laps);
        Assert.Equal("4385919", laps[0].TransponderId);
    }
}
