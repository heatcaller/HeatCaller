
namespace HeatCaller.Tests;

/// <summary>
/// The replay's ring buffer (22 Oct 2026, roadmap "Item 6"). It is written
/// on the sound device's thread, so these hold it to the two promises that
/// matter there: a snapshot is the newest run of samples in order, and a
/// snapshot taken while the device writes is never a splice of two moments.
/// </summary>
public sealed class RoadmapCReplayTests
{
    private const int Rate = RaceReplayBuffer.SampleRate;

    private static void Write(RaceReplayBuffer buffer, int from, int count, int perCallback = 4410)
    {
        var i = 0;

        while (i < count)
        {
            var n = Math.Min(perCallback, count - i);

            for (var k = 0; k < n; k++)
            {
                buffer.Put(Value(from + i + k));
            }

            buffer.Commit();
            i += n;
        }
    }

    private static short Value(long n) => (short)(n & 0x7FFF);

    [Fact]
    public void Nothing_written_means_nothing_to_replay()
    {
        var buffer = new RaceReplayBuffer(Rate * 5);

        Assert.Empty(buffer.Last(20));
        Assert.Equal(0, buffer.HeldSeconds);
        Assert.Equal(TimeSpan.MaxValue, buffer.SinceLastAudio);
    }

    [Fact]
    public void The_last_seconds_are_the_newest_samples_in_order()
    {
        var buffer = new RaceReplayBuffer(Rate * 5);

        Write(buffer, 0, Rate * 3);

        var last = buffer.Last(1);

        Assert.Equal(Rate, last.Length);
        Assert.Equal(Value(Rate * 2), last[0]);
        Assert.Equal(Value((Rate * 3) - 1), last[^1]);
    }

    [Fact]
    public void Asking_for_more_than_is_held_gives_what_is_held()
    {
        var buffer = new RaceReplayBuffer(Rate * 5);

        Write(buffer, 0, Rate / 2);

        Assert.Equal(Rate / 2, buffer.Last(20).Length);
    }

    [Fact]
    public void Wrapping_round_keeps_the_newest_and_never_more_than_is_safe()
    {
        var buffer = new RaceReplayBuffer(Rate * 5);

        // Four times round the ring.
        Write(buffer, 0, buffer.Capacity * 4);

        var all = buffer.Last(1000);

        // Never reaches back into the stretch the writer could be in.
        Assert.Equal(buffer.Capacity - RaceReplayBuffer.MostInOneCallback, all.Length);
        Assert.Equal(Value((buffer.Capacity * 4L) - 1), all[^1]);

        for (var i = 1; i < all.Length; i++)
        {
            Assert.Equal((short)((all[i - 1] + 1) & 0x7FFF), all[i]);
        }
    }

    [Fact]
    public void A_new_race_does_not_replay_the_last_one()
    {
        var buffer = new RaceReplayBuffer(Rate * 5);

        Write(buffer, 0, Rate * 2);
        buffer.Restart();

        Assert.Empty(buffer.Last(20));

        Write(buffer, Rate * 2, Rate);

        var last = buffer.Last(20);

        Assert.Equal(Rate, last.Length);
        Assert.Equal(Value(Rate * 2), last[0]);
    }

    [Fact]
    public void Samples_put_but_not_committed_are_not_seen()
    {
        var buffer = new RaceReplayBuffer(Rate * 5);

        Write(buffer, 0, Rate);
        buffer.Put(123);
        buffer.Put(456);

        var last = buffer.Last(20);

        Assert.Equal(Rate, last.Length);
        Assert.Equal(Value(Rate - 1), last[^1]);
    }

    [Fact]
    public async Task A_snapshot_taken_while_the_device_writes_is_one_unbroken_run()
    {
        // A small ring and a fast writer, so the writer laps it many times
        // while the snapshots are being copied - the case the trim at the
        // end of Last is there for.
        var buffer = new RaceReplayBuffer(RaceReplayBuffer.MostInOneCallback * 2);
        using var stop = new CancellationTokenSource();

        var writer = Task.Run(() =>
        {
            long n = 0;

            while (!stop.IsCancellationRequested)
            {
                for (var k = 0; k < 441; k++)
                {
                    buffer.Put(Value(n++));
                }

                buffer.Commit();
            }
        });

        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            var checkedAny = false;

            while (DateTime.UtcNow < deadline)
            {
                var snap = buffer.Last(1);

                for (var i = 1; i < snap.Length; i++)
                {
                    Assert.Equal((short)((snap[i - 1] + 1) & 0x7FFF), snap[i]);
                }

                checkedAny |= snap.Length > 1;
            }

            Assert.True(checkedAny, "The writer never got far enough to snapshot.");
        }
        finally
        {
            stop.Cancel();
            await writer;
        }
    }

    [Fact]
    public void The_sound_is_mono_at_the_mix_rate_and_faded_at_both_ends()
    {
        var samples = Enumerable.Repeat((short)16384, Rate).ToArray();
        var sound = RaceReplayBuffer.ToSound(samples);

        Assert.Equal(1, sound.WaveFormat.Channels);
        Assert.Equal(Rate, sound.WaveFormat.SampleRate);

        var read = new float[Rate];
        Assert.Equal(Rate, sound.Read(read, 0, read.Length));

        Assert.Equal(0f, read[0]);
        Assert.Equal(0.5, read[Rate / 2], 3);
        Assert.True(read[^1] < 0.01f);

        Assert.Equal(0, sound.Read(read, 0, read.Length));
    }
}
