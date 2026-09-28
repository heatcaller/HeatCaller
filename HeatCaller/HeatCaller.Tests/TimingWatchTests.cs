namespace HeatCaller.Tests;

/// <summary>
/// TimingWatch: during a race, saying when timing has quietly gone wrong -
/// the decoder not connected, the decoder gone quiet, nothing crossing, one
/// car not seen - and not otherwise.
/// </summary>
public sealed class TimingWatchTests
{
    private static readonly DateTime T0 = new(2026, 9, 27, 20, 0, 0, DateTimeKind.Utc);

    private static DateTime At(double seconds) => T0.AddSeconds(seconds);

    private static TimingWatch.Car Car(string id, long typicalLapMs = 6_000) => new(id, "Driver " + id, typicalLapMs);

    private static TimingWatch.Situation Racing(params TimingWatch.Car[] cars) => new(true, false, true, cars);

    [Fact]
    public void Nothing_is_said_when_no_race_is_running()
    {
        var watch = new TimingWatch();

        Assert.Null(watch.Check(new TimingWatch.Situation(false, false, false, Array.Empty<TimingWatch.Car>()), At(0)));
        Assert.Null(watch.Check(new TimingWatch.Situation(false, false, false, Array.Empty<TimingWatch.Car>()), At(600)));
    }

    [Fact]
    public void A_decoder_that_is_not_connected_is_said_at_once_and_first()
    {
        var watch = new TimingWatch();
        watch.Check(Racing(Car("A")), At(0));

        var problem = watch.Check(new TimingWatch.Situation(true, false, false, new[] { Car("A") }), At(1));

        Assert.Equal("decoder-off", problem?.Key);
    }

    [Fact]
    public void A_decoder_whose_heartbeat_stops_is_said_after_five_seconds()
    {
        var watch = new TimingWatch();
        watch.DecoderConnected(At(-10));
        watch.Heartbeat(At(1));

        Assert.Null(watch.Check(Racing(), At(0)));
        Assert.Null(watch.Check(Racing(), At(5.9)));

        var problem = watch.Check(Racing(), At(7));

        Assert.Equal("decoder-quiet", problem?.Key);
        Assert.Contains("6s", problem!.Text);
    }

    [Fact]
    public void A_decoder_that_never_sends_a_heartbeat_is_never_called_quiet()
    {
        // The I-Lap, or a heartbeat from before this connection.
        var watch = new TimingWatch();
        watch.Heartbeat(At(-20));
        watch.DecoderConnected(At(-10));

        watch.Check(Racing(), At(0));

        Assert.Null(watch.Check(Racing(), At(20)));
    }

    [Fact]
    public void Nothing_crossing_at_all_is_said_after_thirty_seconds()
    {
        var watch = new TimingWatch();
        watch.Check(Racing(), At(0));

        Assert.Null(watch.Check(Racing(), At(29)));

        var problem = watch.Check(Racing(), At(31));
        Assert.Equal("no-crossings", problem?.Key);
        Assert.Contains("31s", problem!.Text);
    }

    [Fact]
    public void A_slow_field_gets_three_of_its_own_laps_before_nothing_crossing_is_said()
    {
        var watch = new TimingWatch();
        var cars = new[] { Car("A", 15_000), Car("B", 15_000) };

        watch.Check(Racing(cars), At(0));
        watch.Pass("A", At(10));
        watch.Pass("B", At(10));

        // 44s of quiet is under three 15s laps.
        Assert.Null(watch.Check(Racing(cars), At(54)));
        Assert.Equal("no-crossings", watch.Check(Racing(cars), At(56))?.Key);
    }

    [Fact]
    public void One_car_not_seen_while_the_others_cross_is_named()
    {
        var watch = new TimingWatch();
        var cars = new[] { Car("A"), Car("B") };

        watch.Check(Racing(cars), At(0));
        watch.Pass("B", At(6));

        for (var t = 6; t <= 24; t += 6)
        {
            watch.Pass("A", At(t));
        }

        Assert.Null(watch.Check(Racing(cars), At(25)));

        var problem = watch.Check(Racing(cars), At(27));

        Assert.Equal("missing:B", problem?.Key);
        Assert.Equal("Not seen: Driver B (21s) - stopped, or a flat transponder?", problem!.Text);
    }

    [Fact]
    public void A_car_with_no_timed_lap_is_measured_against_the_fields_laps()
    {
        var watch = new TimingWatch();
        var cars = new[] { Car("A"), Car("C", typicalLapMs: 0) };

        watch.Check(Racing(cars), At(0));

        for (var t = 2; t <= 20; t += 6)
        {
            watch.Pass("A", At(t));
        }

        Assert.Equal("missing:C", watch.Check(Racing(cars), At(21))?.Key);
    }

    [Fact]
    public void A_red_flag_starts_every_silence_again()
    {
        var watch = new TimingWatch();
        var cars = new[] { Car("A"), Car("B") };

        watch.Check(Racing(cars), At(0));
        watch.Pass("A", At(6));
        watch.Pass("B", At(6));

        // Red at 10s - nothing crosses while the field is stopped.
        watch.Check(new TimingWatch.Situation(false, false, true, cars), At(10));

        // Green again at 60s: the silence during the red is not held against anyone.
        Assert.Null(watch.Check(Racing(cars), At(60)));

        watch.Pass("A", At(66));
        Assert.Null(watch.Check(Racing(cars), At(70)));
    }

    [Fact]
    public void Under_the_checkered_the_last_car_out_is_named_and_nobody_crossing_is_not_a_fault()
    {
        var watch = new TimingWatch();
        var lastCar = new[] { Car("B") };

        watch.Check(Racing(lastCar), At(0));
        watch.Pass("B", At(6));

        // Everyone else has taken the flag and stopped; B hasn't come round.
        var problem = watch.Check(new TimingWatch.Situation(true, true, true, lastCar), At(40));

        Assert.Equal("missing:B", problem?.Key);
    }

    [Fact]
    public void Several_missing_cars_name_the_two_longest_gone()
    {
        var watch = new TimingWatch();
        var cars = new[] { Car("A"), Car("B"), Car("C"), Car("D") };

        watch.Check(Racing(cars), At(0));
        watch.Pass("B", At(2));
        watch.Pass("C", At(4));
        watch.Pass("D", At(3));
        watch.Pass("A", At(30));

        var problem = watch.Check(Racing(cars), At(31));

        Assert.Equal("missing:B,C,D", problem?.Key);
        Assert.Equal("Not seen: Driver B (29s), Driver D (28s) and 1 more - stopped, or flat transponders?", problem!.Text);
    }

    [Fact]
    public void A_car_released_late_is_only_expected_from_its_own_release()
    {
        var watch = new TimingWatch();
        var first = new[] { Car("A") };

        watch.Check(Racing(first), At(0));

        for (var t = 2; t <= 26; t += 6)
        {
            watch.Pass("A", At(t));
        }

        // B let go at 25s, in a hand-released start: not missing at 26s,
        // though the race has been going for 26.
        var both = new[] { Car("A"), Car("B", typicalLapMs: 0) };

        Assert.Null(watch.Check(Racing(both), At(25)));
        Assert.Null(watch.Check(Racing(both), At(26)));
        Assert.Null(watch.Check(Racing(both), At(44)));
    }

    [Fact]
    public void An_I_Lap_is_never_called_quiet_by_an_EasyLap_heartbeat_from_before_the_switch()
    {
        var watch = new TimingWatch();
        watch.DecoderConnected(At(-60));
        watch.Heartbeat(At(-30));

        var iLap = new TimingWatch.Situation(true, false, true, Array.Empty<TimingWatch.Car>(), HeartbeatsExpected: false);

        watch.Check(iLap, At(0));

        Assert.Null(watch.Check(iLap, At(10)));
    }

    [Fact]
    public void Once_the_decoder_is_back_every_count_starts_again()
    {
        var watch = new TimingWatch();
        var cars = new[] { Car("A"), Car("B") };

        watch.Check(Racing(cars), At(0));
        watch.Pass("A", At(5));
        watch.Pass("B", At(5));

        // Unplugged from 10s to 100s.
        Assert.Equal("decoder-off", watch.Check(new TimingWatch.Situation(true, false, false, cars), At(10))?.Key);
        Assert.Equal("decoder-off", watch.Check(new TimingWatch.Situation(true, false, false, cars), At(100))?.Key);

        // Back: nothing is blamed on the loop or the cars for the time it was out.
        Assert.Null(watch.Check(Racing(cars), At(101)));
        Assert.Null(watch.Check(Racing(cars), At(115)));
    }

    [Theory]
    [InlineData(5, "5s")]
    [InlineData(59.9, "59s")]
    [InlineData(65, "1:05")]
    [InlineData(600, "10:00")]
    public void Durations_read_as_seconds_then_minutes(double seconds, string expected)
    {
        Assert.Equal(expected, TimingWatch.Duration(TimeSpan.FromSeconds(seconds)));
    }
}
