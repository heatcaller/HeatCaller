namespace HeatCaller.Tests;

/// <summary>
/// Recording the night's two bookends, added 11 Oct 2026.
///
/// Every race was already recorded through its own broadcast, so a recap,
/// the honours and the interview all ended up in that race's mp3. The cold
/// open and the sign-off were in no file at all - and they are the two set
/// pieces about the NIGHT rather than about one heat, which makes them the
/// two a club would actually post.
///
/// THE ONE THING THESE TESTS REALLY DEFEND is not the feature, it is the
/// race recordings. AudioBus takes exactly one listener, by design - so a
/// second recorder started while a race is being recorded would silently
/// displace it, and the race's own mp3 would end wherever that happened
/// with nothing anywhere to say why.
/// </summary>
public sealed class BookendRecordingTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Theory]
    [InlineData(SegmentKind.ColdOpen, true)]
    [InlineData(SegmentKind.SignOffNumbers, true)]
    [InlineData(SegmentKind.RaceIntro, false)]
    [InlineData(SegmentKind.RaceRecap, false)]
    [InlineData(SegmentKind.BattleWatch, false)]
    [InlineData(SegmentKind.FormGuide, false)]
    [InlineData(SegmentKind.ComingUpNext, false)]
    public void Only_the_nights_top_and_tail_are_recorded_on_their_own(SegmentKind kind, bool bookend)
    {
        // The races have their own recordings already, and everything in
        // between is inside one of them.
        Assert.Equal(bookend, BookendRecording.IsBookend(kind));
    }

    [Fact]
    public void Nothing_is_started_for_a_segment_that_is_not_a_bookend()
    {
        var said = new List<string>();

        Assert.Null(BookendRecording.StartFor(SegmentKind.RaceRecap, said.Add));

        // And silently: a segment that is not a bookend is the ordinary
        // case, not something worth a line in a race-night log.
        Assert.Empty(said);
    }

    [Fact]
    public void A_bookend_is_not_recorded_while_something_else_already_is()
    {
        // THE ONE THAT MATTERS. AudioBus is a single listener; the thing
        // that would be lost is a club's recording of a race.
        var listener = new Ears();

        try
        {
            AudioBus.Listen(listener);

            Assert.True(AudioBus.Listening);

            var said = new List<string>();

            Assert.Null(BookendRecording.StartFor(SegmentKind.SignOffNumbers, said.Add));

            // And this one IS said out loud, because it is the one a club
            // could not have predicted - and because the reassurance
            // matters: nothing was lost.
            var all = string.Join("\n", said);

            Assert.Contains("already recording", all);
            Assert.Contains("Nothing is lost", all);
        }
        finally
        {
            AudioBus.StopListening(listener);
        }

        Assert.False(AudioBus.Listening);
    }

    [Fact]
    public void Nobody_listening_is_the_resting_state()
    {
        // The property the refusal above hangs on has to be false when no
        // recorder is running, or every bookend would refuse itself.
        Assert.False(AudioBus.Listening);
    }

    [Fact]
    public void A_club_with_recording_switched_off_gets_a_broadcast_and_no_file()
    {
        // RaceAudioStore.OffForTests is on for the whole suite, so this is
        // the switched-off path - and the thing being checked is that it
        // comes back as a quiet null rather than as an exception on the
        // thread that is about to run a cold open.
        var said = new List<string>();

        Assert.Null(BookendRecording.StartFor(SegmentKind.ColdOpen, said.Add));
    }

    // ------------------------------------------------------------------
    //  Read from the source
    // ------------------------------------------------------------------

    [Fact]
    public void The_recording_is_closed_off_in_a_finally()
    {
        // A block that threw, or was abandoned because a race moved on,
        // still has to close its file: half a broadcast on disk is worth
        // having, and a recorder left listening would hold the tap for the
        // rest of the evening and silently displace the next race's own
        // recording.
        // BOUNDED BY THE NEXT METHOD rather than by a statement inside
        // this one. It used to read as far as "if (spoken)", and that
        // early return is gone (12 Oct 2026): the whole of the method now
        // sits inside the try whose finally closes the file, which is the
        // fix the assertion below pins.
        var body = MethodBody(
            Source("AnnouncerControl.Director.cs"),
            "private async Task RunAsShowAsync(ShowTreatment treatment, SpokenSegment said, CommentaryDirector director)",
            "private void SayContinuityLine(");

        // THE CODE, WITH THE PROSE TAKEN OUT, because this test is about
        // the order statements run in and a comment is not a statement.
        // Without this it read the word "finally" out of the remark that
        // explains why the fallback moved INSIDE the finally - so the
        // explanation of the fix failed the test for the fix. Every search
        // below is on the code for that reason.
        var code = WithoutComments(body);

        var started = code.IndexOf("BookendRecording.StartFor(said.Kind", StringComparison.Ordinal);
        var spoken = code.IndexOf("SpeakOnce();", StringComparison.Ordinal);
        var guard = code.IndexOf("finally", StringComparison.Ordinal);
        var finished = code.IndexOf("taping?.Finish(", StringComparison.Ordinal);

        Assert.True(started >= 0, "The bookends are no longer recorded where this test reads it.");
        Assert.True(guard > started, "The recording is no longer closed off from a finally.");
        Assert.True(finished > guard, "The recording is closed off somewhere other than the finally.");

        // AND THE WORDS GO OUT BEFORE THE FILE IS CLOSED, which is the
        // whole of a bug found on 12 Oct 2026 and the reason this test
        // needed a second assertion.
        //
        // SpeakReminder queues and returns. The plain fallback - the path a
        // club with no music files takes, which is most of them - used to
        // sit AFTER the finally, so the cold open and the sign-off were
        // recorded, closed and reported before a word had been queued.
        // Every one of those files was nought bytes, with "no sound came
        // out of HeatCaller at all" beside it in the log, and they are the
        // two set pieces a club would actually post.
        Assert.True(
            spoken >= 0,
            "The plain fallback no longer speaks where this test reads it.");

        Assert.True(
            spoken < guard,
            "The plain fallback speaks AFTER the finally that closes the recording, so the bookend's file is "
            + "closed before a word has been queued - see the remarks above.");
    }

    [Fact]
    public void Anything_that_wants_to_record_asks_whether_the_tap_is_free_first()
    {
        // One place may call AudioBus.Listen without asking - the race
        // recorder, which is the thing being protected. Everything else
        // asks.
        var bookend = Source("BookendRecording.cs");

        var asked = bookend.IndexOf("AudioBus.Listening", StringComparison.Ordinal);
        var started = bookend.IndexOf("RaceAudioRecorder.StartFor(", StringComparison.Ordinal);

        Assert.True(asked >= 0, "Nothing checks whether the tap is already taken.");
        Assert.True(started > asked, "The recorder is started before anything checks whether the tap is free.");
    }

    /// <summary>A listener that does nothing - it only has to occupy the tap.</summary>
    private sealed class Ears : AudioBus.IMixListener
    {
        public void Mixed(float[] buffer, int offset, int count)
        {
            // Nothing. The test is about who holds the tap, not what
            // comes down it.
        }
    }

    // ------------------------------------------------------------------
    //
    //  The same pair every test class that reads source keeps, kept again
    //  here rather than shared: each carries its own, so none of them can
    //  be broken by a change made for another.

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    /// <summary>
    /// Source with its whole-line comments taken out, so a test about the
    /// order of statements cannot be answered by the prose beside them.
    ///
    /// WHOLE-LINE ONLY. A "//" part way along a line could be inside a
    /// string - a URL in a message - and this is a test helper rather than
    /// a parser, so it leaves those alone. Every comment in the methods
    /// read here is on a line of its own.
    /// </summary>
    private static string WithoutComments(string body) =>
        string.Join(
            "\n",
            body.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string MethodBody(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var rest = source[start..];
        var end = rest.IndexOf(until, StringComparison.Ordinal);

        Assert.True(end >= 0, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return rest[..end];
    }
}
