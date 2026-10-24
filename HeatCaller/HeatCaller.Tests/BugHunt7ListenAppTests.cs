using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// Listening along from home (bug hunt 7, 13 Oct 2026).
///
/// Ids that repeated after a restart or a new meeting, so the page took
/// tonight's lines for ones it had already played; a document that the
/// default JSON encoder made too big by writing every '+' as six bytes;
/// Kokoro's 24 kHz wavs, which the Windows mp3 encoder refuses outright.
/// And three that need a sound device or a relay to see happen - test
/// speech going out to listeners, audio tags in the caption, and the close
/// that was sent once and forgotten - pinned by reading the source, as
/// BugHunt5AudioLockTests does for the same reason.
/// </summary>
public sealed class BugHunt7ListenAppTests
{
    private static readonly DateTime Eight = new(2026, 10, 13, 20, 0, 0, DateTimeKind.Utc);

    private static byte[] Audio(int bytes = 4096) => new byte[bytes];

    // ------------------------------------------------------------------
    //  The ids
    // ------------------------------------------------------------------

    [Fact]
    public void A_new_meeting_never_hands_out_an_id_already_used()
    {
        var listen = new ListenAlong { Wanted = true };

        listen.NewMeeting("2026-10-06");
        listen.Offer("last week", Audio(), Eight);
        var lastWeek = listen.Take(Eight)!;
        listen.Done(lastWeek);

        listen.NewMeeting("2026-10-13");
        listen.Offer("tonight", Audio(), Eight);
        var tonight = listen.Take(Eight)!;

        // Both are the first line of their meeting. They must still differ,
        // or the page skips tonight's as heard - and tonight's must sort
        // after, so the page plays them in the order they were said.
        Assert.NotEqual(lastWeek.Id, tonight.Id);
        Assert.True(string.CompareOrdinal(tonight.Id, lastWeek.Id) > 0);
    }

    [Fact]
    public void A_later_launch_numbers_after_an_earlier_one()
    {
        var before = DateTime.UtcNow.Ticks;
        var listen = new ListenAlong();
        var after = DateTime.UtcNow.Ticks;

        // Taken from the clock, so a restart mid-evening starts above
        // everything the last run sent.
        Assert.InRange(listen.Run, before, after);

        // Line 400 of an earlier run still sorts before line 1 of a later one.
        Assert.True(string.CompareOrdinal(ListenAlong.IdFor(before, 1), ListenAlong.IdFor(before - 1, 400)) > 0);
    }

    // ------------------------------------------------------------------
    //  The document size
    // ------------------------------------------------------------------

    [Fact]
    public void The_largest_line_fits_one_relay_document_even_when_its_base64_is_all_plus_signs()
    {
        // 0xFB 0xEF 0xBE is "++++" in base64 - the worst case for an
        // encoder that writes '+' as "+". Real mp3 data has about one
        // '+' in sixty-four characters, which was already enough to go over.
        var audio = new byte[ListenAlong.MostBytes];

        for (var i = 0; i + 2 < audio.Length; i += 3)
        {
            audio[i] = 0xFB;
            audio[i + 1] = 0xEF;
            audio[i + 2] = 0xBE;
        }

        var body = ListenAlong.Body(
            new ListenAlong.Spoken(ListenAlong.IdFor(long.MaxValue, 999_999), new string('x', 500), audio, Eight));

        Assert.True(
            System.Text.Encoding.UTF8.GetByteCount(body) <= ListenAlong.MostDocumentBytes,
            $"The biggest line allowed makes a {System.Text.Encoding.UTF8.GetByteCount(body)}-byte document, "
            + $"and the relay takes {ListenAlong.MostDocumentBytes}.");

        // And it still reads back as the same audio.
        using var doc = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal(Convert.ToBase64String(audio), doc.RootElement.GetProperty("audio").GetString());
    }

    [Fact]
    public void Random_audio_at_the_largest_size_fits_one_relay_document()
    {
        var audio = new byte[ListenAlong.MostBytes];
        new Random(1).NextBytes(audio);

        var body = ListenAlong.Body(
            new ListenAlong.Spoken(ListenAlong.IdFor(long.MaxValue, 999_999), new string('x', 500), audio, Eight));

        Assert.True(System.Text.Encoding.UTF8.GetByteCount(body) <= ListenAlong.MostDocumentBytes);
    }

    [Fact]
    public void Words_with_quotes_and_accents_still_read_back_as_written()
    {
        // The relaxed encoder still escapes what JSON itself needs.
        const string said = "\"Zoë\" takes it <by a nose> & a half";

        var body = ListenAlong.Body(
            new ListenAlong.Spoken(ListenAlong.IdFor(Eight.Ticks, 1), said, new byte[] { 1, 2, 3 }, Eight));

        using var doc = System.Text.Json.JsonDocument.Parse(body);
        Assert.Equal(said, doc.RootElement.GetProperty("said").GetString());
    }

    // ------------------------------------------------------------------
    //  Kokoro's sample rate
    // ------------------------------------------------------------------

    [Fact]
    public void Kokoro_audio_at_24_kHz_is_resampled_to_a_rate_the_mp3_encoder_takes()
    {
        var kokoro = new RawSourceWaveStream(new MemoryStream(new byte[4800]), new WaveFormat(24_000, 16, 1));

        var ready = AnnouncementService.AtAnMp3Rate(kokoro.ToSampleProvider());

        Assert.True(AnnouncementService.IsAnMp3Rate(ready.WaveFormat.SampleRate));
        Assert.Equal(1, ready.WaveFormat.Channels);

        var buffer = new float[9600];
        Assert.True(ready.Read(buffer, 0, buffer.Length) > 0);
    }

    [Fact]
    public void Audio_already_at_an_mp3_rate_is_passed_through_untouched()
    {
        var eleven = new RawSourceWaveStream(new MemoryStream(new byte[4800]), new WaveFormat(44_100, 16, 1));
        var samples = eleven.ToSampleProvider();

        Assert.Same(samples, AnnouncementService.AtAnMp3Rate(samples));
    }

    [Theory]
    [InlineData(32_000, true)]
    [InlineData(44_100, true)]
    [InlineData(48_000, true)]
    [InlineData(24_000, false)]
    [InlineData(22_050, false)]
    [InlineData(16_000, false)]
    public void Only_the_mpeg1_rates_count_as_mp3_rates(int rate, bool expected)
    {
        Assert.Equal(expected, AnnouncementService.IsAnMp3Rate(rate));
    }

    // ------------------------------------------------------------------
    //  Pinned by the source
    // ------------------------------------------------------------------

    [Fact]
    public void A_test_asked_for_by_hand_is_not_sent_to_listeners()
    {
        var test = Between(SourceOf("AnnouncementService.cs"), "public void SpeakTest(", "public VoiceEngine VoiceEngineInUse");
        Assert.Contains("forListeners: false", test);

        var queue = SourceOf("AnnouncementService.VoiceQueue.cs");
        Assert.Contains("bool forListeners = true", queue);
        Assert.Matches(@"if \(forListeners\)\s*\{\s*OfferLineToListeners\(text, audio[,)]", queue);
    }

    [Fact]
    public void The_caption_listeners_read_has_no_audio_tags()
    {
        var offer = Between(SourceOf("AnnouncementService.ListenAlong.cs"), "private void OfferLineToListeners(", "private byte[]? AsMp3(");
        Assert.Contains("var words = VoiceProfile.StripAudioTags(text);", offer);
    }

    [Fact]
    public void Audio_tags_come_out_of_a_caption()
    {
        Assert.Equal("And Tom takes the lead!", VoiceProfile.StripAudioTags("[excited] And Tom takes the lead!"));
    }

    [Fact]
    public void Unticking_listening_is_retried_by_the_worker_until_the_relay_takes_it()
    {
        var listen = SourceOf("RelayPublisher.Listen.cs");
        var close = Between(listen, "public void CloseListeningOnTheRelay()", "private void SendListenClose(");

        // No longer gated on this run having opened it, and no longer a
        // single fire-and-forget send.
        Assert.DoesNotContain("_listenOpenTold", close);
        Assert.DoesNotContain("Task.Run", close);
        Assert.Contains("_listenClosePending = true;", close);

        var send = Between(listen, "private void SendListenClose(", "\n}");
        Assert.Matches(@"if \(TellTheRelayListeningIsOpen\(target, club, wanted: false, token\)\)\s*\{\s*_listenClosePending = false;", send);

        Assert.Contains("if (_listenClosePending)", SourceOf("RelayPublisher.cs"));
    }

    private static string Between(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"\"{start}\" is not in the source.");

        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"\"{end}\" does not follow \"{start}\".");

        return source[from..to];
    }

    /// <summary>The same walk up from the test folder BugHunt5AudioLockTests uses.</summary>
    private static string SourceOf(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null)
        {
            var candidate = Path.Combine(folder.FullName, "HeatCaller", "HeatCaller", fileName);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            folder = folder.Parent;
        }

        throw new FileNotFoundException($"No HeatCaller\\HeatCaller\\{fileName} above {AppContext.BaseDirectory}.");
    }
}
