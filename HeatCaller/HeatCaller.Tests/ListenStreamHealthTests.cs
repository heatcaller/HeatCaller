namespace HeatCaller.Tests;

/// <summary>
/// HOW THE STREAM IS DOING, COUNTED (11 Nov 2026, asked for by Asher:
/// Listen Now's music "skips or drops", and maybe the voice) - see
/// <see cref="ListenStream.HealthEvery"/>.
///
/// Nothing about the sound changes here. These say that the music bed
/// counts the two things it does that the room never hears - running dry
/// and being trimmed - without mistaking a track fading out or the music
/// stopping for either, and that the relay side counts how long each
/// stretch took to get there.
/// </summary>
public sealed class ListenStreamHealthTests
{
    private const int Rate = ListenStream.SampleRate;

    /// <summary>A tenth of a second, which is how the bus and the music hand over.</summary>
    private const int Tenth = Rate / 10;

    private static float[] Sound(float level = 0.5f) => Enumerable.Repeat(level, Tenth).ToArray();

    private static void AddTenths(ListenStream.Run.Bed bed, int tenths, float level = 0.5f)
    {
        for (var i = 0; i < tenths; i++)
        {
            bed.Add(Sound(level));
        }
    }

    private static void UnderTenths(ListenStream.Run.Bed bed, int tenths)
    {
        for (var i = 0; i < tenths; i++)
        {
            bed.Under(new float[Tenth]);
        }
    }

    [Fact]
    public void Music_that_runs_dry_while_it_is_playing_counts_as_a_drop_as_long_as_the_silence_it_made()
    {
        var bed = new ListenStream.Run.Bed();

        AddTenths(bed, 6);
        UnderTenths(bed, 6);

        // Nothing came from the music for three of the bus's reads.
        UnderTenths(bed, 3);

        // And then it did, and the bed picked up again.
        AddTenths(bed, 5);
        UnderTenths(bed, 1);

        var health = bed.TakeHealth();

        Assert.Equal(1, health.Drops);
        Assert.Equal(0.3, health.DropSeconds, 3);
        Assert.Equal(0.3, health.LongestDropSeconds, 3);
        Assert.Equal(0, health.Skips);
    }

    [Fact]
    public void A_track_fading_out_is_not_a_drop()
    {
        var bed = new ListenStream.Run.Bed();

        AddTenths(bed, 5);
        AddTenths(bed, 1, 0.0001f);
        UnderTenths(bed, 9);
        AddTenths(bed, 5);
        UnderTenths(bed, 1);

        Assert.Equal(0, bed.TakeHealth().Drops);
    }

    [Fact]
    public void Music_stopped_for_a_while_is_not_a_drop()
    {
        var bed = new ListenStream.Run.Bed();

        AddTenths(bed, 6);
        UnderTenths(bed, 6);
        UnderTenths(bed, (int)(ListenStream.Run.Bed.LongestDrop * 10) + 5);
        AddTenths(bed, 5);
        UnderTenths(bed, 1);

        Assert.Equal(0, bed.TakeHealth().Drops);
    }

    [Fact]
    public void Music_that_piles_up_is_trimmed_and_the_jump_is_counted()
    {
        var bed = new ListenStream.Run.Bed();

        AddTenths(bed, 15);
        UnderTenths(bed, 1);

        var health = bed.TakeHealth();

        Assert.Equal(1, health.Skips);
        Assert.Equal(1.5 - ListenStream.BedLeadSeconds, health.SkipSeconds, 3);
        Assert.Equal(ListenStream.BedLeadSeconds, health.LeastHeldSeconds, 3);
        Assert.Equal(0, health.Drops);
    }

    [Fact]
    public void The_bed_says_how_much_it_held_and_the_device_rate_and_starts_again_once_asked()
    {
        var bed = new ListenStream.Run.Bed();

        bed.Add(Enumerable.Repeat(0.5f, 8 * Tenth).ToArray(), 48000);
        UnderTenths(bed, 2);

        var first = bed.TakeHealth();

        Assert.Equal(2, first.Heard);
        Assert.Equal(0.7, first.LeastHeldSeconds, 3);
        Assert.Equal(0.8, first.MostHeldSeconds, 3);
        Assert.Equal(48000, first.SourceRate);

        var again = bed.TakeHealth();

        Assert.Equal(0, again.Heard);
        Assert.Equal(0, again.LeastHeldSeconds);
    }

    // ------------------------------------------------------------------
    //  The console's line
    // ------------------------------------------------------------------

    private static ListenStream.BedHealth Played(int drops = 0, double dropSeconds = 0, double longest = 0, int skips = 0, double skipSeconds = 0, int rate = Rate) =>
        new(30, drops, dropSeconds, longest, skips, skipSeconds, 0.41, 0.62, rate);

    [Fact]
    public void A_clean_stretch_says_nothing_until_it_is_time_to_say_it_is_clean()
    {
        var health = new ListenStream.Health(Played(), default, 0);
        var delivery = new ListenAlong.Delivery(100, 0.31, 0.8, 0, 0);

        Assert.Null(ListenStream.HealthLine(health, delivery, sayClean: false));
        Assert.Equal(
            "clean. music held 0.41 s to 0.62 s. 100 stretches to the relay, 0.31 s on average, worst 0.80 s.",
            ListenStream.HealthLine(health, delivery, sayClean: true));
    }

    [Fact]
    public void A_stretch_with_trouble_says_what_it_was()
    {
        var health = new ListenStream.Health(Played(drops: 2, dropSeconds: 1.1, longest: 0.6, skips: 1, skipSeconds: 0.7, rate: 48000), default, 3);

        Assert.Equal(
            "music ran dry 2 times (1.10 s silent, longest 0.60 s); music trimmed 1 time (0.70 s jumped); "
            + "3 reads of sound dropped before the encoder. music held 0.41 s to 0.62 s, resampled from 48000 Hz.",
            ListenStream.HealthLine(health, null, sayClean: false));
    }

    [Fact]
    public void Stretches_reaching_the_relay_late_are_trouble_on_their_own()
    {
        var health = new ListenStream.Health(default, default, 0);

        Assert.Equal(
            "clean. 98 stretches to the relay, 0.42 s on average, worst 2.80 s, 4 late (over 1.00 s), 2 never sent.",
            ListenStream.HealthLine(health, new ListenAlong.Delivery(98, 0.42, 2.8, 4, 2), sayClean: false));
    }

    // ------------------------------------------------------------------
    //  Getting to the relay
    // ------------------------------------------------------------------

    private static ListenSegment Segment(long number, DateTime at) =>
        new(new byte[2048], 4, 115, 1152, Rate, 638_000_000_000_000_000, number, Array.Empty<ListenCaption>(), at);

    [Fact]
    public void The_queue_counts_how_long_each_stretch_took_to_reach_the_relay_and_starts_again_once_asked()
    {
        var at = new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
        var queue = new ListenAlong { Wanted = true };

        queue.OfferSegment(Segment(1, at));
        queue.Done(queue.Take(at)!, at.AddSeconds(0.4));
        queue.OfferSegment(Segment(2, at));
        queue.Done(queue.Take(at)!, at.AddSeconds(2.4));

        var delivery = queue.TakeDelivery();

        Assert.NotNull(delivery);
        Assert.Equal(2, delivery.Value.Sent);
        Assert.Equal(1.4, delivery.Value.AverageSeconds, 3);
        Assert.Equal(2.4, delivery.Value.WorstSeconds, 3);
        Assert.Equal(1, delivery.Value.Late);
        Assert.Equal(0, delivery.Value.Missed);
        Assert.Null(queue.TakeDelivery());
    }

    [Fact]
    public void A_stretch_that_went_stale_before_it_was_sent_is_counted_as_never_sent()
    {
        var at = new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
        var queue = new ListenAlong { Wanted = true };

        queue.OfferSegment(Segment(1, at));
        Assert.Null(queue.Take(at + ListenAlong.SegmentGoesStale + TimeSpan.FromSeconds(1)));

        Assert.Equal(new ListenAlong.Delivery(0, 0, 0, 0, 1), queue.TakeDelivery());
    }

    [Fact]
    public void A_line_on_its_own_is_not_counted_as_a_stretch()
    {
        var at = new DateTime(2026, 11, 11, 20, 0, 0, DateTimeKind.Utc);
        var queue = new ListenAlong { Wanted = true };

        queue.Offer("Heat 3 to the drivers stand.", new byte[4096], at);
        queue.Done(queue.Take(at)!, at.AddSeconds(5));

        Assert.Null(queue.TakeDelivery());
    }
}
