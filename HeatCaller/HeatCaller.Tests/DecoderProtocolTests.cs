using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// The decoders added on 1 Nov 2026 (plans/decoder-support-plan.md), read
/// with no decoder attached: each protocol against records built from its
/// published description, the reader that puts their passes on the app's
/// clock, and the simulated decoder.
/// </summary>
/// <remarks>
/// The figures are the published ones: the RC4 passing's transponder
/// 7404583, 16 hits, decoder 0x405FC and clock 0x0004758ABDE619E8 µs (the
/// amb-p3-parser project's test record); the RCHourglass wiki's
/// "23E35500075A06-01152A3787"; OpenStint's protocol page's passing and
/// status lines. The records themselves are built here (DecoderWire) - no
/// other project's files or code are copied.
/// </remarks>
public sealed class DecoderProtocolTests
{
    private static List<DecoderReading> Read(IDecoderProtocol protocol, params byte[][] chunks)
    {
        var readings = new List<DecoderReading>();

        foreach (var chunk in chunks)
        {
            protocol.Feed(chunk, readings, _ => { });
        }

        return readings;
    }

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    /// <summary>Every byte on its own - the worst a serial port or socket can split a record.</summary>
    private static byte[][] OneByOne(byte[] bytes) => bytes.Select(b => new[] { b }).ToArray();

    // --- The decoder list ----------------------------------------------------------------

    [Fact]
    public void The_saved_numbers_of_the_first_two_decoders_never_change()
    {
        Assert.Equal(1, DecoderConnection.EasyLap);
        Assert.Equal(2, DecoderConnection.ILap);
        Assert.Equal(0, DecoderConnection.IndexOf(DecoderConnection.EasyLap));
        Assert.Equal(1, DecoderConnection.IndexOf(DecoderConnection.ILap));
    }

    [Theory]
    [InlineData(DecoderConnection.MyLapsP3)]
    [InlineData(DecoderConnection.ZRound)]
    [InlineData(DecoderConnection.Cano)]
    [InlineData(DecoderConnection.OpenStint)]
    [InlineData(DecoderConnection.EasyLapSerial)]
    public void Every_new_decoder_is_offered_and_marked_beta_until_proven_on_real_hardware(int decoder)
    {
        Assert.True(DecoderConnection.IsOffered(decoder));
        Assert.Equal(decoder, DecoderConnection.ValueAt(DecoderConnection.IndexOf(decoder)));
        Assert.True(DecoderConnection.IsStream(decoder));
        Assert.True(DecoderConnection.IsBeta(decoder));
        Assert.EndsWith("(Beta)", DecoderConnection.Items()[DecoderConnection.IndexOf(decoder)]);
        Assert.NotNull(DecoderProtocols.For(decoder));
    }

    [Fact]
    public void The_EasyLap_and_I_Lap_are_neither_beta_nor_run_by_the_stream_client()
    {
        Assert.False(DecoderConnection.IsBeta(DecoderConnection.EasyLap));
        Assert.False(DecoderConnection.IsBeta(DecoderConnection.ILap));
        Assert.False(DecoderConnection.IsStream(DecoderConnection.EasyLap));
        Assert.False(DecoderConnection.IsStream(DecoderConnection.ILap));
    }

    [Fact]
    public void Each_decoder_keeps_its_own_connection_settings_with_the_published_defaults()
    {
        var settings = new AppSettings();

        var p3 = settings.StreamDecoder(DecoderConnection.MyLapsP3);
        Assert.Equal(DecoderTransport.Network, p3.Transport);
        Assert.Equal(5403, p3.Port);

        Assert.Equal(19200, settings.StreamDecoder(DecoderConnection.ZRound).Baud);
        Assert.Equal(57600, settings.StreamDecoder(DecoderConnection.Cano).Baud);
        Assert.Equal(5556, settings.StreamDecoder(DecoderConnection.OpenStint).Port);
        Assert.Equal(38400, settings.StreamDecoder(DecoderConnection.EasyLapSerial).Baud);

        p3.Host = "192.168.1.50";
        Assert.Same(p3, settings.StreamDecoder(DecoderConnection.MyLapsP3));
    }

    [Fact]
    public void The_connection_settings_survive_being_saved_and_read_back()
    {
        var settings = new AppSettings();
        var saved = settings.StreamDecoder(DecoderConnection.ZRound);
        saved.Transport = DecoderTransport.Serial;
        saved.ComPort = "COM7";

        var json = JsonSerializer.Serialize(settings.StreamDecoders);
        var back = JsonSerializer.Deserialize<Dictionary<int, StreamDecoderSettings>>(json)!;

        Assert.Equal(DecoderTransport.Serial, back[DecoderConnection.ZRound].Transport);
        Assert.Equal("COM7", back[DecoderConnection.ZRound].ComPort);
        Assert.Equal(19200, back[DecoderConnection.ZRound].Baud);
    }

    // --- MyLaps / AMB P3 -----------------------------------------------------------------

    private const ulong RC4Clock = 0x0004758ABDE619E8;

    [Fact]
    public void A_P3_passing_reads_the_published_RC4_values()
    {
        var record = DecoderWire.P3Passing(0x19BB2, 7404583, RC4Clock, 51, 16, 0x405FC);

        var reading = Assert.Single(Read(new P3Protocol(), record));

        Assert.Equal("7404583", reading.TransponderId);
        Assert.Equal((long)(RC4Clock / 1000), reading.DeviceMs);
        Assert.Equal(16, reading.Hits);
        Assert.Equal(51, reading.Strength);
    }

    [Fact]
    public void A_P3_record_split_into_single_bytes_reads_the_same()
    {
        var record = DecoderWire.P3Passing(1, 7404583, RC4Clock, 51, 16, 0x405FC);

        var reading = Assert.Single(Read(new P3Protocol(), OneByOne(record)));

        Assert.Equal("7404583", reading.TransponderId);
    }

    [Fact]
    public void P3_escapes_the_framing_bytes_inside_a_record_and_reads_them_back()
    {
        // 0x8D, 0x8E and 0x8F in the transponder, the clock and the hits.
        const uint transponder = 0x008F8E8D;
        const ulong clock = 0x00008E8F8D000000;
        var record = DecoderWire.P3Passing(1, transponder, clock, 0x8F, 0x8E, 1);

        // Nothing but the two ends may be a framing byte on the wire.
        Assert.DoesNotContain(record[1..^1], b => b is 0x8E or 0x8F);

        var reading = Assert.Single(Read(new P3Protocol(), record));

        Assert.Equal(transponder.ToString(CultureInfo.InvariantCulture), reading.TransponderId);
        Assert.Equal((long)(clock / 1000), reading.DeviceMs);
        Assert.Equal(0x8E, reading.Hits);
        Assert.Equal(0x8F, reading.Strength);
    }

    [Fact]
    public void A_P3_status_record_is_not_a_pass()
    {
        Assert.Empty(Read(new P3Protocol(), DecoderWire.P3Status(26, 40, 120, 0x405FC)));
    }

    [Fact]
    public void P3_reads_every_record_of_a_run_and_skips_noise_before_and_between()
    {
        var stream = new List<byte> { 0x00, 0x12, 0x8F };
        stream.AddRange(DecoderWire.P3Passing(1, 1001, 1_000_000, 50, 10, 1));
        stream.AddRange(DecoderWire.P3Status(26, 40, 120, 1));
        stream.Add(0x55);
        stream.AddRange(DecoderWire.P3Passing(2, 1002, 2_000_000, 50, 10, 1));

        var readings = Read(new P3Protocol(), stream.ToArray());

        Assert.Equal(new[] { "1001", "1002" }, readings.Select(r => r.TransponderId));
    }

    [Fact]
    public void A_P3_record_cut_short_by_the_next_start_is_dropped_and_the_next_one_read()
    {
        var first = DecoderWire.P3Passing(1, 1001, 1_000_000, 50, 10, 1);
        var second = DecoderWire.P3Passing(2, 1002, 2_000_000, 50, 10, 1);
        var logged = new List<string>();
        var readings = new List<DecoderReading>();

        new P3Protocol().Feed(first[..20].Concat(second).ToArray(), readings, logged.Add);

        Assert.Equal("1002", Assert.Single(readings).TransponderId);
        Assert.Contains(logged, line => line.Contains("cut short"));
    }

    // --- ZRound open protocol ----------------------------------------------------------

    [Fact]
    public void ZRound_opens_with_the_handshake_and_race_start()
    {
        Assert.Equal("%C&%I&", Encoding.ASCII.GetString(new ZRoundProtocol().Greeting));
    }

    [Fact]
    public void A_ZRound_lap_with_a_comma_carries_any_length_of_transponder()
    {
        var reading = Assert.Single(Read(new ZRoundProtocol(), Ascii("%L18A6E7,1D4C0&")));

        Assert.Equal("1615591", reading.TransponderId); // 0x18A6E7
        Assert.Equal(120_000, reading.DeviceMs);        // 0x1D4C0
    }

    [Fact]
    public void A_ZRound_lap_without_a_comma_is_two_hex_digits_of_transponder()
    {
        var reading = Assert.Single(Read(new ZRoundProtocol(), Ascii("%L0A1D4C0&")));

        Assert.Equal("10", reading.TransponderId);
        Assert.Equal(120_000, reading.DeviceMs);
    }

    [Fact]
    public void ZRound_clock_messages_are_clock_readings_and_the_ack_is_nothing()
    {
        var readings = Read(new ZRoundProtocol(), OneByOne(Ascii("%A&%T3E8&junk%T7D0&")));

        Assert.Equal(2, readings.Count);
        Assert.All(readings, r => Assert.Null(r.TransponderId));
        Assert.Equal(new long?[] { 1000, 2000 }, readings.Select(r => r.DeviceMs));
    }

    [Fact]
    public void A_ZRound_message_that_is_not_hex_is_not_a_lap()
    {
        Assert.Empty(Read(new ZRoundProtocol(), Ascii("%LXY,12&%L12,ZZ&%L1&%Q&")));
    }

    // --- Cano / RCHourglass --------------------------------------------------------------

    [Fact]
    public void A_Cano_line_reads_as_the_RCHourglass_wiki_works_it_out()
    {
        var reading = Assert.Single(Read(new CanoProtocol(), Ascii("23E35500075A06\r\n")));

        Assert.Equal("2351957", reading.TransponderId);
        Assert.Equal(120_449, reading.DeviceMs); // 0x75A06 quarter milliseconds
        Assert.Equal(0, reading.Hits);
    }

    [Fact]
    public void An_RCHourglass_line_adds_hits_and_read_quality()
    {
        var reading = Assert.Single(Read(new CanoProtocol(), OneByOne(Ascii("23E35500075A06-01152A3787\r\n"))));

        Assert.Equal("2351957", reading.TransponderId);
        Assert.Equal(21, reading.Hits); // 0x15

        // 0x2A. The wiki's worked example calls it 52%, but every other field
        // of that line is plain hex (0x15 hits = 21, 0x37 = 5.5 V, 0x87 - 100 =
        // 35 C), and 0x2A is 42 - the wiki's sum slipped, not the format.
        Assert.Equal(42, reading.Strength);
    }

    [Fact]
    public void The_Cano_power_up_line_and_other_text_are_not_passes()
    {
        Assert.Empty(Read(new CanoProtocol(), Ascii("COPYRIGHT 2013 HOWARD W. CANO\r\n23E355\r\n23E35500075A06XYZ\r\n")));
    }

    [Fact]
    public void Cano_records_written_by_the_simulator_read_back()
    {
        var reading = Assert.Single(Read(new CanoProtocol(), Ascii(DecoderWire.CanoPassing(2351957, 120_449, 21, 52))));

        Assert.Equal("2351957", reading.TransponderId);
        Assert.Equal(120_449, reading.DeviceMs);
        Assert.Equal(21, reading.Hits);
        Assert.Equal(52, reading.Strength);
    }

    // --- OpenStint -----------------------------------------------------------------------

    [Fact]
    public void An_OpenStint_passing_reads_by_position_as_its_protocol_page_shows()
    {
        var readings = Read(new OpenStintProtocol(),
            Ascii("P 1618706341 OPN 1615544 3.50 64 89113 24.3\nP 1658197240 AMB 3616557 3.88 21 92423 18.9\n"));

        Assert.Equal(2, readings.Count);
        Assert.Equal("1615544", readings[0].TransponderId);
        Assert.Equal(1618706341, readings[0].DeviceMs);
        Assert.Equal(64, readings[0].Hits);
        Assert.Equal("3616557", readings[1].TransponderId);
        Assert.Equal(21, readings[1].Hits);
    }

    [Fact]
    public void OpenStint_status_and_time_sync_are_clock_readings_and_the_trainer_is_nothing()
    {
        var readings = Read(new OpenStintProtocol(),
            Ascii("S 1792039754 -41.018744 5.08 0 0 3.01\nT 1618721816 OPN 1615544 905370\nL 1792039754 START -8.3\n"));

        Assert.Equal(new long?[] { 1792039754, 1618721816 }, readings.Select(r => r.DeviceMs));
        Assert.All(readings, r => Assert.Null(r.TransponderId));
    }

    [Fact]
    public void OpenStint_fields_added_on_the_end_in_future_are_ignored_and_bad_ones_are_not_passes()
    {
        var readings = Read(new OpenStintProtocol(),
            Ascii("P 100 OPN 7 3.50 64 89113 24.3 future fields\nP 100 OPN\nP abc OPN 7\nP 100 OPN -7 1 1\n"));

        Assert.Equal("7", Assert.Single(readings).TransponderId);
    }

    // --- EasyLap on a serial port ----------------------------------------------------------

    /// <summary>
    /// Rehearsal 3's real EasyLap night, with the HID wrapping taken off each
    /// report - what a serial EasyLap would send, if the serial models carry
    /// the same UART stream - read through the serial path. Every pass must
    /// come out as it did through the USB path.
    /// </summary>
    [Fact]
    public void Rehearsal_3_s_EasyLap_stream_reads_the_same_on_the_serial_path_as_over_USB()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Captures", "easylap-rehearsal3.log");
        var reports = File.ReadLines(path)
            .Select(line => line.IndexOf("[raw HID] ", StringComparison.Ordinal) is var at and >= 0 ? line[(at + 10)..] : null)
            .OfType<string>()
            .Select(hex => hex.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(h => byte.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray())
            .ToList();

        var overUsb = new List<DirectLapEvent>();
        var usb = new EasyLapFramer(_ => { }, overUsb.Add);

        foreach (var report in reports)
        {
            usb.AddReport(report);
        }

        var serial = new EasyLapStreamProtocol();
        var readings = new List<DecoderReading>();

        foreach (var report in reports)
        {
            var length = report[0];

            if (length > 0 && length < report.Length)
            {
                serial.Feed(report.AsSpan(1, length), readings, _ => { });
            }
        }

        var overSerial = readings.Where(r => r.TransponderId is not null).ToList();

        Assert.Equal(155, overUsb.Count);
        Assert.Equal(
            overUsb.Select(p => (p.TransponderId, (long)p.DeviceTimeMs, p.SignalHits, p.SignalStrength)),
            overSerial.Select(r => (r.TransponderId!, r.DeviceMs!.Value, r.Hits, r.Strength)));

        // The heartbeats come through as clock readings - one a second.
        Assert.InRange(readings.Count(r => r.TransponderId is null), 700, 800);
    }

    // --- The reader: onto the app's clock ----------------------------------------------------

    private static StreamDecoderReader OpenStintReader(List<string>? log = null) =>
        new(DecoderConnection.OpenStint, new OpenStintProtocol(), line => log?.Add(line));

    private static List<DirectLapEvent> Feed(StreamDecoderReader reader, string message, long arrivedMs) =>
        reader.Feed(Ascii(message + "\n"), arrivedMs);

    [Fact]
    public void A_pass_before_the_decoder_s_clock_is_learned_is_timed_as_it_arrived()
    {
        var reader = OpenStintReader();

        var pass = Assert.Single(Feed(reader, "P 5000 OPN 7 3.5 20 0", 12_345));

        Assert.Equal(12_345u, pass.DeviceTimeMs);
        Assert.Equal(12_345, pass.ReceivedMs);
        Assert.Equal(DecoderConnection.OpenStint, pass.Decoder);
        Assert.Equal(TimingPoint.Line, pass.Point);
        Assert.Equal(20, pass.SignalHits);
    }

    [Fact]
    public void Once_the_clock_is_learned_a_pass_is_timed_by_the_decoder_not_by_when_it_arrived()
    {
        var reader = OpenStintReader();

        // The decoder's clock, read three times; the quickest arrival (5000
        // after the first reading) is the closest to the true offset.
        Feed(reader, "S 1000 -40 5 0 0 1", 5_000);
        Feed(reader, "S 2000 -40 5 0 0 1", 6_003);
        Feed(reader, "S 3000 -40 5 0 0 1", 7_001);

        // Crossed at 3500 on the decoder's clock, arrived 20ms late.
        var pass = Assert.Single(Feed(reader, "P 3500 OPN 7 3.5 20 0", 7_520));

        Assert.Equal(7_500u, pass.DeviceTimeMs);
        Assert.Equal(7_520, pass.ReceivedMs);
    }

    [Fact]
    public void A_decoder_that_restarts_is_timed_as_its_passes_arrive_until_its_clock_is_learned_again()
    {
        var log = new List<string>();
        var reader = OpenStintReader(log);

        Feed(reader, "S 1000 -40 5 0 0 1", 5_000);
        Feed(reader, "S 2000 -40 5 0 0 1", 6_000);
        Feed(reader, "S 3000 -40 5 0 0 1", 7_000);

        // Restarted: its clock is back near zero.
        Feed(reader, "S 10 -40 5 0 0 1", 9_000);
        var pass = Assert.Single(Feed(reader, "P 500 OPN 7 3.5 20 0", 9_600));

        Assert.Equal(9_600u, pass.DeviceTimeMs);
        Assert.Contains(log, line => line.Contains("restarted"));
    }

    [Fact]
    public void A_mapped_time_far_from_the_arrival_is_not_believed()
    {
        var reader = OpenStintReader();

        Feed(reader, "S 1000 -40 5 0 0 1", 5_000);
        Feed(reader, "S 2000 -40 5 0 0 1", 6_000);
        Feed(reader, "S 3000 -40 5 0 0 1", 7_000);

        // A pass stamped ten seconds ahead of the clock it has been keeping.
        var pass = Assert.Single(Feed(reader, "P 14000 OPN 7 3.5 20 0", 8_000));

        Assert.Equal(8_000u, pass.DeviceTimeMs);
    }

    [Fact]
    public void The_same_car_read_twice_in_one_crossing_is_one_pass()
    {
        var reader = OpenStintReader();

        Assert.Single(Feed(reader, "P 1000 OPN 7 3.5 20 0", 10_000));
        Assert.Empty(Feed(reader, "P 1300 OPN 7 3.5 20 0", 10_300));
        Assert.Single(Feed(reader, "P 1300 OPN 8 3.5 20 0", 10_300));
        Assert.Single(Feed(reader, "P 13000 OPN 7 3.5 20 0", 22_000));
    }

    [Fact]
    public void A_P3_decoder_s_microsecond_calendar_clock_maps_onto_the_app_s()
    {
        var reader = new StreamDecoderReader(DecoderConnection.MyLapsP3, new P3Protocol(), _ => { });
        var passes = new List<DirectLapEvent>();

        // Five laps, each record arriving 3-9ms after the decoder made it.
        for (var lap = 0; lap < 5; lap++)
        {
            var decoderMs = 1000L * lap * 12;
            var record = DecoderWire.P3Passing((uint)lap, 7404583 + (uint)(lap % 2), RC4Clock + (ulong)decoderMs * 1000, 50, 10, 1);
            passes.AddRange(reader.Feed(record, 50_000 + decoderMs + 3 + (lap * 3 % 7)));
        }

        Assert.Equal(5, passes.Count);

        // From the third on, the decoder's own spacing: exactly 12 s apart.
        Assert.Equal(12_000, (long)passes[4].DeviceTimeMs - passes[3].DeviceTimeMs);
        Assert.Equal(12_000, (long)passes[3].DeviceTimeMs - passes[2].DeviceTimeMs);
    }

    // --- The simulated decoder ------------------------------------------------------------------

    [Theory]
    [InlineData(DecoderConnection.MyLapsP3)]
    [InlineData(DecoderConnection.ZRound)]
    [InlineData(DecoderConnection.Cano)]
    [InlineData(DecoderConnection.OpenStint)]
    [InlineData(DecoderConnection.EasyLapSerial)]
    public void The_simulated_decoder_s_bytes_read_back_as_the_club_s_own_cars(int decoder)
    {
        var club = new[] { "1001", "2351957", "84C1C4", "Bob's car" };
        var reader = new StreamDecoderReader(decoder, DecoderProtocols.For(decoder), _ => { });
        var passes = new List<DirectLapEvent>();
        long now = 0;

        // Each chunk read as it is sent, arriving 4ms later on the app's clock.
        using var simulator = new SimulatedDecoder(decoder, club, bytes => passes.AddRange(reader.Feed(bytes, 100_000 + now + 4)), seed: 7);

        long beat = 0;

        for (now = 0; now <= 60_000; now += 50)
        {
            simulator.Tick(now, ref beat);
        }

        var expected = decoder == DecoderConnection.EasyLapSerial ? new[] { "84C1C4" } : new[] { "1001", "2351957" };

        Assert.Equal(expected, simulator.Transponders.OrderBy(t => t, StringComparer.Ordinal));
        Assert.Equal(expected, passes.Select(p => p.TransponderId).Distinct().OrderBy(t => t, StringComparer.Ordinal));

        // A minute of 11-17 s laps: three to six crossings each.
        Assert.All(expected, car => Assert.InRange(passes.Count(p => p.TransponderId == car), 3, 6));
    }

    [Fact]
    public void With_no_club_transponder_the_protocol_can_carry_the_simulator_makes_up_its_own()
    {
        using var simulator = new SimulatedDecoder(DecoderConnection.Cano, new[] { "99999999", "abc" }, _ => { }, seed: 1);

        Assert.Equal(new[] { "1001", "1002", "1003", "1004" }, simulator.Transponders);
    }
}
