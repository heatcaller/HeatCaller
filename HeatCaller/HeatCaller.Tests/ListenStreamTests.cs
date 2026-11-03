using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// THE WHOLE ROOM TO ANYONE LISTENING ALONG (2 Nov 2026) - see
/// <see cref="ListenStream"/>.
///
/// The stream is one mp3 encode cut into segments on frame boundaries, so
/// the page can join them back together without a click at every join. The
/// tests worth reading first are
/// <see cref="A_run_cuts_numbered_segments_on_frame_boundaries_that_decode_back_to_back"/>,
/// which runs the real Media Foundation encoder, and
/// <see cref="A_line_kept_from_listeners_is_not_cleared_for_the_stream"/>,
/// which is the privacy rule the stream depends on now that it carries the
/// whole mix.
/// </summary>
public sealed class ListenStreamTests
{
    private const int Rate = ListenStream.SampleRate;

    /// <summary>Feeds a run a tenth of a second at a time, as the bus would, and gathers what it sends.</summary>
    private static List<ListenSegment> Stream(Action<ListenStream.Run, int> feed, int tenths, Func<List<ListenSegment>, bool> enough, ListenStream.Run? run = null, List<ListenSegment>? sent = null)
    {
        sent ??= new List<ListenSegment>();
        var gathered = sent;
        run ??= new ListenStream.Run(segment =>
        {
            lock (gathered)
            {
                gathered.Add(segment);
            }
        });

        run.Start();

        try
        {
            for (var i = 0; i < tenths; i++)
            {
                feed(run, i);
            }

            var until = DateTime.UtcNow.AddSeconds(30);

            while (DateTime.UtcNow < until)
            {
                lock (gathered)
                {
                    if (enough(gathered))
                    {
                        break;
                    }
                }

                Thread.Sleep(50);
            }
        }
        finally
        {
            run.Stop();
        }

        lock (gathered)
        {
            return gathered.ToList();
        }
    }

    /// <summary>A tenth of a second of the bus: interleaved stereo at the mix rate.</summary>
    private static float[] BusTenth(int tenth, double hz, float level)
    {
        var frames = Rate / 10;
        var samples = new float[frames * 2];

        for (var f = 0; f < frames; f++)
        {
            var t = ((tenth * frames) + f) / (double)Rate;
            var value = level * (float)Math.Sin(2 * Math.PI * hz * t);
            samples[2 * f] = value;
            samples[(2 * f) + 1] = value;
        }

        return samples;
    }

    private static List<byte[]> FramesOf(byte[] mp3)
    {
        var frames = new List<byte[]>();
        var at = 0;

        while (at < mp3.Length && Mp3Frames.TryHeader(mp3, at, out var length, out _, out _))
        {
            frames.Add(mp3[at..(at + length)]);
            at += length;
        }

        Assert.Equal(mp3.Length, at);
        return frames;
    }

    // ------------------------------------------------------------------
    //  Finding the frames
    // ------------------------------------------------------------------

    [Fact]
    public void A_frame_header_says_how_long_its_frame_is()
    {
        // MPEG-1 Layer III, 128 kbps, 44.1 kHz, no padding.
        Assert.True(Mp3Frames.TryHeader(new byte[] { 0xFF, 0xFB, 0x90, 0x00 }, 0, out var length, out var samples, out var rate));
        Assert.Equal(417, length);
        Assert.Equal(1152, samples);
        Assert.Equal(44100, rate);

        // The same with the padding bit.
        Assert.True(Mp3Frames.TryHeader(new byte[] { 0xFF, 0xFB, 0x92, 0x00 }, 0, out length, out _, out _));
        Assert.Equal(418, length);
    }

    [Fact]
    public void What_is_not_a_frame_header_is_not_taken_for_one()
    {
        Assert.False(Mp3Frames.TryHeader(new byte[] { 0x49, 0x44, 0x33, 0x03 }, 0, out _, out _, out _));
        Assert.False(Mp3Frames.TryHeader(new byte[] { 0xFF, 0xFB, 0xF0, 0x00 }, 0, out _, out _, out _), "a bad bit rate");
        Assert.False(Mp3Frames.TryHeader(new byte[] { 0xFF, 0xFD, 0x90, 0x00 }, 0, out _, out _, out _), "layer II");
        Assert.False(Mp3Frames.TryHeader(new byte[] { 0xFF, 0xFB }, 0, out _, out _, out _), "too short to tell");
    }

    [Fact]
    public void The_tag_in_front_of_the_frames_is_measured_to_be_skipped()
    {
        var tag = new byte[] { (byte)'I', (byte)'D', (byte)'3', 3, 0, 0, 0, 0, 2, 0x25 };
        Assert.Equal(10 + 293, Mp3Frames.TagLength(tag, 0));
        Assert.Equal(0, Mp3Frames.TagLength(new byte[] { 0xFF, 0xFB, 0x90, 0, 0, 0, 0, 0, 0, 0 }, 0));
    }

    // ------------------------------------------------------------------
    //  The stream, through the real encoder
    // ------------------------------------------------------------------

    [Fact]
    public void A_run_cuts_numbered_segments_on_frame_boundaries_that_decode_back_to_back()
    {
        var sent = Stream((run, i) => run.Mixed(BusTenth(i, 440, 0.4f), 0, Rate / 10 * 2), 120, got => got.Count >= 3);

        Assert.True(sent.Count >= 3, $"only {sent.Count} segments came out of twelve seconds");

        var wanted = (int)Math.Ceiling(ListenStream.SegmentSeconds * Rate / 1152);

        for (var i = 0; i < 3; i++)
        {
            var one = sent[i];
            Assert.Equal(i, one.Number);
            Assert.Equal(sent[0].Run, one.Run);
            Assert.Equal(1152, one.SamplesPerFrame);
            Assert.Equal(Rate, one.SampleRate);
            Assert.Equal(wanted, one.Frames);
            Assert.Equal(i == 0 ? 0 : ListenStream.LeadFrames, one.Lead);
            Assert.Equal(one.Lead + one.Frames, FramesOf(one.Mp3).Count);
        }

        // Each segment's lead is the last frames of the one before it.
        var first = FramesOf(sent[0].Mp3);
        var second = FramesOf(sent[1].Mp3);

        for (var k = 0; k < ListenStream.LeadFrames; k++)
        {
            Assert.Equal(first[first.Count - ListenStream.LeadFrames + k], second[k]);
        }

        // The segments' own frames, one after another, are one stream that
        // plays: the page does exactly this, a segment at a time.
        using var joined = new MemoryStream();

        foreach (var one in sent.Take(3))
        {
            foreach (var frame in FramesOf(one.Mp3).Skip(one.Lead))
            {
                joined.Write(frame);
            }
        }

        joined.Position = 0;
        using var reader = new Mp3FileReader(joined);
        var samples = reader.ToSampleProvider();
        var buffer = new float[Rate * 9 * samples.WaveFormat.Channels];
        var read = samples.Read(buffer, 0, buffer.Length);

        Assert.True(read > Rate * 8 * samples.WaveFormat.Channels, $"only {read} samples decoded");

        var middle = buffer.Skip(read / 3).Take(read / 3).ToArray();
        var rms = Math.Sqrt(middle.Average(s => s * (double)s));
        Assert.InRange(rms, 0.2, 0.4);
    }

    [Fact]
    public void A_silent_room_sends_nothing_but_the_stream_is_up()
    {
        var (run, sent) = Gathering();

        // Up is set as the first segment is cut, and Stop waits for the
        // encoder - so anything it was going to send has been by then.
        var got = Stream((r, _) => r.Mixed(new float[Rate / 10 * 2], 0, Rate / 10 * 2), 80, _ => run.Up, run, sent);

        Assert.True(run.Up, "the encoder never started");
        Assert.Empty(got);
    }

    /// <summary>A run, and the list it sends into.</summary>
    private static (ListenStream.Run Run, List<ListenSegment> Sent) Gathering()
    {
        var sent = new List<ListenSegment>();
        var run = new ListenStream.Run(segment =>
        {
            lock (sent)
            {
                sent.Add(segment);
            }
        });

        return (run, sent);
    }

    [Fact]
    public void The_music_goes_out_under_a_silent_bus()
    {
        var sent = Stream(
            (run, i) =>
            {
                // The music's output: stereo at 48 kHz, a tenth at a time.
                var frames = 4800;
                var music = new float[frames * 2];

                for (var f = 0; f < frames; f++)
                {
                    music[2 * f] = music[(2 * f) + 1] = 0.3f * (float)Math.Sin(2 * Math.PI * 220 * ((i * frames) + f) / 48000.0);
                }

                run.Take(ListenStream.Run.MusicSource, music, 0, music.Length, 48000, 2);
                run.Mixed(new float[Rate / 10 * 2], 0, Rate / 10 * 2);
            },
            100,
            got => got.Count >= 2);

        Assert.True(sent.Count >= 2, "the music never went out");
    }

    [Fact]
    public void A_caption_rides_the_segment_its_line_starts_in()
    {
        var (run, sent) = Gathering();

        run.Caption("Green flag!");
        var got = Stream((r, i) => r.Mixed(BusTenth(i, 440, 0.4f), 0, Rate / 10 * 2), 80, all => all.Count >= 2, run, sent);

        Assert.Equal("Green flag!", Assert.Single(got[0].Captions).Text);
        Assert.Empty(got[1].Captions);
    }

    // ------------------------------------------------------------------
    //  On the publisher's queue
    // ------------------------------------------------------------------

    private static ListenSegment Segment(long number, DateTime at, params ListenCaption[] captions) =>
        new(new byte[2048], number == 0 ? 0 : 4, 115, 1152, Rate, 638_000_000_000_000_000, number, captions, at);

    [Fact]
    public void A_segment_is_sent_as_a_stretch_of_the_stream_the_page_can_join_on()
    {
        var at = new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc);
        var queue = new ListenAlong { Wanted = true };

        Assert.Equal(ListenAlong.Verdict.Held, queue.OfferSegment(Segment(7, at, new ListenCaption("Green flag!", 1.25))));

        var held = queue.Take(at)!;
        var body = System.Text.Json.JsonDocument.Parse(ListenAlong.Body(held)).RootElement;

        Assert.Equal("mix", body.GetProperty("kind").GetString());
        Assert.Equal(4, body.GetProperty("lead").GetInt32());
        Assert.Equal(115, body.GetProperty("frames").GetInt32());
        Assert.Equal(1152, body.GetProperty("spf").GetInt32());
        Assert.Equal(Rate, body.GetProperty("rate").GetInt32());
        Assert.Equal(7, body.GetProperty("n").GetInt64());
        Assert.Equal("638000000000000000", body.GetProperty("run").GetString());
        Assert.Equal("Green flag!", body.GetProperty("said").GetString());

        var caption = Assert.Single(body.GetProperty("captions").EnumerateArray().ToList());
        Assert.Equal("Green flag!", caption.GetProperty("text").GetString());
        Assert.Equal(1.25, caption.GetProperty("at").GetDouble());

        // A line still has no kind, so the page plays it whole.
        queue.Offer("Heat 3 to the drivers stand.", new byte[4096], at);
        queue.Done(held);
        Assert.False(System.Text.Json.JsonDocument.Parse(ListenAlong.Body(queue.Take(at)!)).RootElement.TryGetProperty("kind", out _));
    }

    [Fact]
    public void A_segment_goes_stale_long_before_a_line_does()
    {
        var at = new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc);
        var queue = new ListenAlong { Wanted = true };

        queue.OfferSegment(Segment(0, at));
        Assert.Null(queue.Take(at + ListenAlong.SegmentGoesStale + TimeSpan.FromSeconds(1)));

        queue.Offer("Five minutes to heat four.", new byte[4096], at);
        Assert.NotNull(queue.Take(at + ListenAlong.SegmentGoesStale + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void The_recap_says_how_long_the_room_was_streamed_rather_than_counting_lines()
    {
        var at = new DateTime(2026, 11, 2, 20, 0, 0, DateTimeKind.Utc);
        var queue = new ListenAlong { Wanted = true };

        for (var i = 0; i < 40; i++)
        {
            queue.OfferSegment(Segment(i, at));
            queue.Done(queue.Take(at)!);
        }

        // Forty segments of 115 frames is just over two minutes.
        Assert.Equal("Streamed 2 minutes of the room to anyone listening along.", queue.Report());
    }

    // ------------------------------------------------------------------
    //  What stays in the room
    // ------------------------------------------------------------------

    [Fact]
    public void A_line_kept_from_listeners_is_not_cleared_for_the_stream()
    {
        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

        Assert.Null(announcer.CaptionIfCleared("Warning, Sam. Blocking.", true, PublicPrivacy.Empty));
        Assert.Null(announcer.CaptionIfCleared("Black flag, car 7.", true, PublicPrivacy.Empty));
        Assert.Equal("Green flag, here we go!", announcer.CaptionIfCleared("Green flag, here we go!", null, PublicPrivacy.Empty));
    }

    [Fact]
    public void An_announcer_line_is_off_air_unless_it_was_cleared()
    {
        var playback = File.ReadAllText(SourcePath("AnnouncementService.Playback.cs"));
        Assert.Contains("bool onAir = false)", playback);
        Assert.Contains("voice: true, offAir: !onAir);", playback);

        // A driver's own clip is the race's recorded call.
        Assert.Contains("offAir: true", File.ReadAllText(SourcePath("AnnouncerControl.Director.cs")));

        // And the bus takes the off-air sounds back out of what it hands over.
        var bus = File.ReadAllText(SourcePath("AudioBus.cs"));
        Assert.Contains("_onAirMix[i] = buffer[offset + i] - _offAirMix[i];", bus);
    }

    private static string SourcePath(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HeatCaller", file)))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "HeatCaller", file);
    }
}
