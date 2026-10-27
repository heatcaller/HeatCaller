namespace HeatCaller.Tests;

/// <summary>
/// Three reports from the floor (26 Oct 2026, asked for by Asher): "can't
/// hear the heat reminders that fire between races by listening through
/// the relay", "when watching the live captions through the relay they
/// stopped updating" and "Big screen doesn't reliably show live captions".
/// </summary>
/// <remarks>
/// A long line - a recap, the driver introductions, a round intro - was
/// captioned as its first 300 characters, gone from the Big Screen twenty
/// seconds into a ninety-second line and frozen on the live page until the
/// announcer stopped. It is now captioned in pieces, each released as the
/// voice reaches it. A short transponder number said as a car's name kept
/// every later line with that number in it - every Timetable reminder -
/// off listen-along and the public captions. And listen lines went last in
/// the relay publisher's pass, behind every document a finished race
/// sends. The publisher's order and the playback hooks need a relay and a
/// sound device to see happen, so they are pinned by reading the source,
/// as BugHunt7ListenAppTests does.
/// </remarks>
public sealed class Captions_ListenAlong_Oct26Tests
{
    private static readonly DateTime At = new(2026, 10, 26, 19, 30, 0, DateTimeKind.Utc);

    /// <summary>A recap-sized line: twenty sentences, about ninety seconds of talking.</summary>
    private static readonly string Recap = string.Join(" ", Enumerable.Range(1, 20)
        .Select(i => $"Sentence {i} of the recap, about the race that has just finished and the drivers who made it."));

    // ------------------------------------------------- a long line, in pieces

    [Fact]
    public void A_long_line_is_captioned_in_pieces_released_as_the_voice_reaches_them()
    {
        var log = new CaptionLog();

        var first = log.Add(Recap, At);

        Assert.NotNull(first);
        Assert.StartsWith("Sentence 1 of the recap", first!.Text);
        Assert.True(first.Text.Length <= CaptionLog.PieceChars);

        // Only the first piece is up as the line starts.
        Assert.Single(log.Last(CaptionLog.Kept, At));
        Assert.Equal(1L, log.LatestAt(At));

        // Half a minute in, the voice is a good way through and so are the
        // captions - the strip is not showing the opening for the whole line.
        var halfway = log.Last(CaptionLog.Kept, At.AddSeconds(30));
        Assert.True(halfway.Count > 2, $"only {halfway.Count} pieces up thirty seconds into the line");
        Assert.DoesNotContain(halfway, l => l.Text.Contains("Sentence 20 "));
        Assert.Equal(halfway.Select(l => l.N).OrderBy(n => n), halfway.Select(l => l.N));

        // Each piece is stamped with when it was reached, so the Big Screen
        // holds it for its own time rather than the line's.
        Assert.True(halfway[^1].AtUtc > At.AddSeconds(20));
        Assert.True(BigScreenForm.CaptionStrength(halfway[^1], At.AddSeconds(30)) > 0.99);
    }

    [Fact]
    public void Every_word_of_a_long_line_is_captioned_and_nothing_is_cut()
    {
        var pieces = CaptionLog.Pieces(Recap);

        Assert.Equal(Recap, string.Join(" ", pieces));
        Assert.All(pieces, p => Assert.True(p.Length <= CaptionLog.PieceChars));

        // Split at sentences: none starts part-way through one.
        Assert.All(pieces, p => Assert.StartsWith("Sentence ", p));
    }

    [Fact]
    public void A_short_line_is_one_piece_as_it_always_was()
    {
        Assert.Equal(new[] { "Green flag!" }, CaptionLog.Pieces("  Green\r\n flag!  "));
        Assert.Empty(CaptionLog.Pieces("   "));

        // A decimal is not the end of a sentence.
        Assert.Single(CaptionLog.Pieces("Sam sets a 6.935s lap."));
    }

    [Fact]
    public void When_the_line_finishes_the_rest_of_its_caption_goes_up_at_once()
    {
        var log = new CaptionLog();
        log.Add(Recap, At);

        // The voice was quicker than the estimate: the line has ended.
        log.SaidInFull(At.AddSeconds(40));

        var all = log.Last(CaptionLog.Kept, At.AddSeconds(40));
        Assert.Contains(all, l => l.Text.Contains("Sentence 20 "));
        Assert.True(all[^1].AtUtc <= At.AddSeconds(40));
    }

    [Fact]
    public void A_line_cut_off_never_shows_the_words_the_room_did_not_hear()
    {
        var log = new CaptionLog();
        log.Add(Recap, At);

        // A yellow flag cut the recap off ten seconds in.
        var upToTheCut = log.Last(CaptionLog.Kept, At.AddSeconds(10)).Count;
        log.Unsaid();

        Assert.Equal(upToTheCut, log.Last(CaptionLog.Kept, At.AddMinutes(10)).Count);
        Assert.DoesNotContain(log.Last(CaptionLog.Kept, At.AddMinutes(10)), l => l.Text.Contains("Sentence 20 "));
    }

    [Fact]
    public void The_next_line_lets_go_of_what_was_waiting_and_numbers_still_count_up()
    {
        var log = new CaptionLog();
        log.Add(Recap, At);

        var next = log.Add("Five minutes to Stock Q1 Heat 2.", At.AddSeconds(5));

        Assert.NotNull(next);

        var kept = log.Last(CaptionLog.Kept, At.AddSeconds(5));
        Assert.Equal(next!.N, kept[^1].N);
        Assert.Equal(kept.Select(l => l.N).OrderBy(n => n), kept.Select(l => l.N));
        Assert.Equal(next.N, log.LatestAt(At.AddSeconds(5)));
    }

    [Fact]
    public void Playback_tells_the_captions_how_each_line_ended()
    {
        var play = Method(Source("AnnouncementService.Playback.cs"), "public static async Task PlayPcmAsync(");

        // Played to the end: the rest goes up. Cut off or dropped: it never does.
        Assert.Contains("heardToTheEnd = !stop.IsCancellationRequested;", play);
        Assert.Contains("CaptionLog.Shared.SaidInFull();", play);
        Assert.Contains("CaptionLog.Shared.Unsaid();", play);

        // In the finally, so the stillWanted drop and a failed device count too.
        Assert.True(play.IndexOf("finally", StringComparison.Ordinal) < play.IndexOf("CaptionLog.Shared.Unsaid();", StringComparison.Ordinal));
    }

    // ------------------------------------------------- a short number as a name

    [Fact]
    public void A_short_transponder_said_as_a_name_does_not_keep_every_reminder_off_the_public_pages()
    {
        var ids = new[] { "2", "3", "5", "1" };

        // The Timetable's reminders and the counts in a race: the number is
        // never where a name goes.
        Assert.False(AnnouncementService.MentionsAnyId("5 minutes to Stock Q1 Heat 2.", ids));
        Assert.False(AnnouncementService.MentionsAnyId("1 minute to Stock Q1 Heat 2.", ids));
        Assert.False(AnnouncementService.MentionsAnyId("Time's up for Stock Q1 Heat 2!", ids));
        Assert.False(AnnouncementService.MentionsAnyId("Heat 3 to the drivers stand", ids));
        Assert.False(AnnouncementService.MentionsAnyId("Stock Buggy, Round 2 - to the grid!", ids));
        Assert.False(AnnouncementService.MentionsAnyId("A 5 point 8 8 seconds lap!", ids));
        Assert.False(AnnouncementService.MentionsAnyId("3 laps to go!", ids));
        Assert.False(AnnouncementService.MentionsAnyId("The next race is at 7:30.", new[] { "7", "30" }));

        // Where it IS the car's name it still stays in the room.
        Assert.True(AnnouncementService.MentionsAnyId("3 sets the fastest lap!", ids));
        Assert.True(AnnouncementService.MentionsAnyId("Fastest lap, 3.", ids));
        Assert.True(AnnouncementService.MentionsAnyId("Sam is about to lap 3!", ids));
        Assert.True(AnnouncementService.MentionsAnyId("3 laps Sam!", ids));
        Assert.True(AnnouncementService.MentionsAnyId("Heat 2 is next, and 5 is on pole.", ids));
    }

    [Fact]
    public void A_transponder_with_letters_in_it_is_matched_exactly_as_before()
    {
        // An EasyLap id is hex. Nothing about counts applies to it.
        Assert.True(AnnouncementService.MentionsAnyId("Heat 84C1C4 is quick", new[] { "84C1C4" }));
        Assert.True(AnnouncementService.MentionsAnyId("84C1C4 sets the fastest lap!", new[] { "84C1C4" }));
    }

    [Fact]
    public void The_public_captions_keep_a_reminder_when_a_short_numbered_car_was_said()
    {
        var lines = new List<CaptionLine>
        {
            new(1, "3 sets the fastest lap!", At),
            new(2, "Heat 3 to the drivers stand.", At.AddSeconds(30)),
        };

        var json = MainForm.CaptionsDocument(lines, PublicPrivacy.Empty, At,
            namesACarByNumber: text => AnnouncementService.MentionsAnyId(text, new[] { "3" }));

        Assert.Contains("Heat 3 to the drivers stand.", json);
        Assert.DoesNotContain("fastest lap", json);
    }

    // ------------------------------------------------- listen lines first

    [Fact]
    public void Listen_lines_go_first_in_the_pass_and_between_the_documents_drained()
    {
        var publisher = Source("RelayPublisher.cs");

        // Before Drain in the worker's pass.
        var first = publisher.IndexOf("SendListenLinesWaiting(pass.Token);", StringComparison.Ordinal);
        var drain = publisher.IndexOf("Drain(pass.Token);", StringComparison.Ordinal);
        Assert.True(first > 0 && first < drain, "the waiting lines go before the documents");

        // And inside Drain, after the result and after each snapshot.
        var body = Method(publisher, "private void Drain(CancellationToken token)");
        Assert.Equal(2, Count(body, "SendListenLinesWaiting(token);"));
        Assert.True(body.IndexOf("SendListenLinesWaiting(token);", StringComparison.Ordinal)
                    < body.IndexOf("var taken = TakeSnapshots();", StringComparison.Ordinal));
        Assert.True(body.LastIndexOf("SendListenLinesWaiting(token);", StringComparison.Ordinal)
                    > body.IndexOf("for (var i = 0; i < taken.Count; i++)", StringComparison.Ordinal));

        // Nothing at all when nobody is listening or nothing is waiting.
        var waiting = Method(Source("RelayPublisher.Listen.cs"), "private void SendListenLinesWaiting(CancellationToken token)");
        Assert.Contains("!_settings.ListenAlongOnTheRelay", waiting);
        Assert.Contains("Listening.Waiting == 0", waiting);
    }

    // ------------------------------------------------- helpers

    private static int Count(string text, string what)
    {
        var count = 0;

        for (var at = text.IndexOf(what, StringComparison.Ordinal); at >= 0; at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>One method's text, from its signature to the next member at the class's indent.</summary>
    private static string Method(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no \"{signature}\"");

        var end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        Assert.True(end > start, $"no end to \"{signature}\"");

        return source[start..end];
    }

    /// <summary>The folder the repo is checked out in, found by the file that is only ever at its root (as ShowOnScreenTests finds it).</summary>
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
}
