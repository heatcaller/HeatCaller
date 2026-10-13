namespace HeatCaller.Tests;

/// <summary>
/// THE ANNOUNCER'S LINES ON THEIR WAY OUT (13 Oct 2026) - see
/// <see cref="ListenAlong"/>.
///
/// WHAT THIS FILE IS ABOUT
///
/// Live audio is the one thing in this app where late is worse than
/// missing. Everything else the relay carries - results, standings,
/// badges - is still true an hour afterwards and is queued, retried and
/// never thrown away. A lap call read ninety seconds after the lap is
/// actively wrong: it describes a pass that has already been overtaken by
/// two more.
///
/// So this queue is the one that throws work away on purpose, and most of
/// this file is about proving it throws away the RIGHT work. The two worth
/// reading first:
///
/// <see cref="A_connection_that_cannot_keep_up_loses_the_oldest_lines"/> -
/// overflow drops the front, not the back, which is the opposite of what a
/// queue normally does and the whole point of this one.
///
/// <see cref="A_line_answered_after_the_switch_went_off_does_not_eat_a_new_one"/>
/// - the failure nobody would think of. Unticking the box empties the
/// queue; a request already in flight then answers and says "done", and a
/// blind dequeue at that moment would throw away a line that had never
/// been sent at all.
/// </summary>
public sealed class ListenAlongTests
{
    private static readonly DateTime Eight = new(2026, 10, 13, 20, 0, 0, DateTimeKind.Utc);

    /// <summary>Audio of a believable size - above the 256-byte floor, well under the cap.</summary>
    private static byte[] Audio(int bytes = 4096) => new byte[bytes];

    private static ListenAlong Open()
    {
        return new ListenAlong { Wanted = true };
    }

    // ------------------------------------------------------------------
    //  Nothing goes anywhere until the club says so
    // ------------------------------------------------------------------

    [Fact]
    public void A_club_that_has_not_switched_listening_on_collects_nothing()
    {
        var listen = new ListenAlong();

        Assert.Equal(ListenAlong.Verdict.NobodyIsListening, listen.Offer("Lap five", Audio(), Eight));
        Assert.Equal(0, listen.Waiting);
    }

    [Fact]
    public void A_club_that_has_not_switched_it_on_is_not_told_it_threw_lines_away()
    {
        var listen = new ListenAlong();

        for (var i = 0; i < 20; i++)
        {
            listen.Offer("Lap five", Audio(), Eight);
        }

        // Nothing was offered as far as the night's account is concerned,
        // so the recap says nothing about listening at all. A club that
        // never asked for this should not read a line about it.
        Assert.Equal(0, listen.OfferedTonight);
        Assert.Null(listen.Report());
    }

    [Fact]
    public void Switching_it_off_lets_go_of_what_was_waiting()
    {
        var listen = Open();
        listen.Offer("Lap five", Audio(), Eight);
        listen.Offer("Lap six", Audio(), Eight);

        listen.Wanted = false;

        // Not held for later. A club that has just switched this off does
        // not want the last fifteen seconds going out behind its back.
        Assert.Equal(0, listen.Waiting);
        Assert.Null(listen.Take(Eight));
    }

    [Fact]
    public void Switching_it_off_and_on_again_does_not_lose_the_numbering()
    {
        var listen = Open();
        listen.Offer("one", Audio(), Eight);

        listen.Wanted = false;
        listen.Wanted = true;

        listen.Offer("two", Audio(), Eight);

        // The second line is still the SECOND id of the evening. Reusing
        // L000001 would have the relay treat it as a correction of a line
        // that had already played.
        Assert.Equal(ListenAlong.IdFor(listen.Run, 2), listen.Take(Eight)!.Id);
    }

    // ------------------------------------------------------------------
    //  What is worth sending
    // ------------------------------------------------------------------

    [Fact]
    public void A_line_with_audio_is_held_to_be_sent()
    {
        var listen = Open();

        Assert.Equal(ListenAlong.Verdict.Held, listen.Offer("Lap five, Dave leads", Audio(), Eight));

        var next = listen.Take(Eight);

        Assert.NotNull(next);
        Assert.Equal("Lap five, Dave leads", next!.Text);
    }

    [Fact]
    public void Nothing_is_held_for_a_line_with_no_audio()
    {
        var listen = Open();

        Assert.Equal(ListenAlong.Verdict.NothingToSend, listen.Offer("Lap five", null, Eight));
        Assert.Equal(0, listen.Waiting);
    }

    [Fact]
    public void A_handful_of_bytes_is_not_audio()
    {
        var listen = Open();

        // Far below the shortest real line. Sending it would put a
        // document on the relay that the page would try to play and fail
        // on, which looks to a listener exactly like the club being off
        // the air.
        Assert.Equal(ListenAlong.Verdict.NothingToSend, listen.Offer("Lap five", new byte[64], Eight));
        Assert.Equal(0, listen.Waiting);
    }

    [Fact]
    public void A_line_too_long_for_one_relay_document_is_refused_rather_than_cut()
    {
        var listen = Open();

        Assert.Equal(
            ListenAlong.Verdict.TooLong,
            listen.Offer("The race recap, all of it", Audio(ListenAlong.MostBytes + 1), Eight));

        Assert.Equal(0, listen.Waiting);
        Assert.Equal(1, listen.RefusedTonight);
    }

    [Fact]
    public void A_line_of_exactly_the_largest_size_still_goes()
    {
        var listen = Open();

        // The boundary in the direction that matters: off by one the other
        // way would silently refuse the longest lines a club could
        // legitimately send.
        Assert.Equal(
            ListenAlong.Verdict.Held,
            listen.Offer("A long one", Audio(ListenAlong.MostBytes), Eight));
    }

    [Fact]
    public void The_largest_line_allowed_still_fits_one_relay_document()
    {
        // THE ARITHMETIC THAT MostBytes IS FOR. The audio travels base64'd
        // inside JSON, which costs a third on top, and the relay takes one
        // listen document of 256 KB. If this ever fails, MostBytes has
        // been raised without the relay's limit being raised with it, and
        // every long line would come back 413.
        var body = ListenAlong.Body(
            new ListenAlong.Spoken(
                ListenAlong.IdFor(long.MaxValue, 999_999),
                new string('x', 500),
                new byte[ListenAlong.MostBytes],
                Eight));

        Assert.True(
            System.Text.Encoding.UTF8.GetByteCount(body) <= ListenAlong.MostDocumentBytes,
            $"The biggest line allowed makes a {System.Text.Encoding.UTF8.GetByteCount(body)}-byte document, "
            + $"and the relay takes {ListenAlong.MostDocumentBytes}.");
    }

    // ------------------------------------------------------------------
    //  Falling behind
    // ------------------------------------------------------------------

    [Fact]
    public void A_connection_that_cannot_keep_up_loses_the_oldest_lines()
    {
        var listen = Open();

        for (var i = 1; i <= ListenAlong.MostHeld + 4; i++)
        {
            listen.Offer("line " + i, Audio(), Eight);
        }

        Assert.Equal(ListenAlong.MostHeld, listen.Waiting);
        Assert.Equal(4, listen.DroppedTonight);

        // The head is line 5, not line 1: the four at the front went. A
        // listener hears a gap and then the present, which is what
        // listening along means. Keeping the oldest would have them
        // hearing four laps ago and falling further behind all night.
        Assert.Equal("line 5", listen.Take(Eight)!.Text);
    }

    [Fact]
    public void A_line_that_waited_too_long_is_thrown_away_rather_than_sent()
    {
        var listen = Open();
        listen.Offer("Lap five", Audio(), Eight);

        Assert.Null(listen.Take(Eight + ListenAlong.GoesStale + TimeSpan.FromSeconds(1)));
        Assert.Equal(1, listen.StaleTonight);
        Assert.Equal(0, listen.Waiting);
    }

    [Fact]
    public void A_line_right_on_the_edge_of_stale_still_goes()
    {
        var listen = Open();
        listen.Offer("Lap five", Audio(), Eight);

        Assert.NotNull(listen.Take(Eight + ListenAlong.GoesStale));
    }

    [Fact]
    public void A_connection_back_after_a_minute_off_starts_from_what_is_being_said_now()
    {
        var listen = Open();

        listen.Offer("a minute ago", Audio(), Eight);
        listen.Offer("also a minute ago", Audio(), Eight);
        listen.Offer("just now", Audio(), Eight + TimeSpan.FromSeconds(60));

        var next = listen.Take(Eight + TimeSpan.FromSeconds(61));

        Assert.Equal("just now", next!.Text);
        Assert.Equal(2, listen.StaleTonight);
    }

    // ------------------------------------------------------------------
    //  Sending, and failing to
    // ------------------------------------------------------------------

    [Fact]
    public void A_sent_line_leaves_the_queue()
    {
        var listen = Open();
        listen.Offer("Lap five", Audio(), Eight);

        var sent = listen.Take(Eight)!;
        listen.Done(sent);

        Assert.Equal(0, listen.Waiting);
        Assert.Equal(1, listen.SentTonight);
        Assert.Null(listen.Take(Eight));
    }

    [Fact]
    public void A_line_that_could_not_be_sent_is_offered_again_unchanged()
    {
        var listen = Open();
        listen.Offer("Lap five", Audio(), Eight);

        var first = listen.Take(Eight);
        var second = listen.Take(Eight + TimeSpan.FromSeconds(2));

        // The same object, not a copy and not the next one: there is no
        // failure call, so not saying Done IS the retry.
        Assert.Same(first, second);
        Assert.Equal(0, listen.SentTonight);
    }

    [Fact]
    public void Lines_go_out_in_the_order_they_were_said()
    {
        var listen = Open();
        listen.Offer("first", Audio(), Eight);
        listen.Offer("second", Audio(), Eight);

        var one = listen.Take(Eight)!;
        Assert.Equal("first", one.Text);

        listen.Done(one);
        Assert.Equal("second", listen.Take(Eight)!.Text);
    }

    [Fact]
    public void A_line_answered_after_the_switch_went_off_does_not_eat_a_new_one()
    {
        var listen = Open();
        listen.Offer("in flight", Audio(), Eight);

        var flying = listen.Take(Eight)!;

        // The box is unticked while that request is on the network, which
        // empties the queue, and then the club changes its mind.
        listen.Wanted = false;
        listen.Wanted = true;
        listen.Offer("said after", Audio(), Eight);

        // The old request finally answers. A blind dequeue here would
        // throw away a line that had never been sent.
        listen.Done(flying);

        Assert.Equal(0, listen.SentTonight);
        Assert.Equal("said after", listen.Take(Eight)!.Text);
    }

    [Fact]
    public void A_line_let_go_is_not_counted_as_one_that_was_sent()
    {
        // THE LIE THIS PREVENTS. A document the relay refuses as too large
        // is dropped rather than retried forever, and for a while that
        // dropping went through Done - so the night's recap told the club
        // those lines had gone out to listeners when they never left the
        // building.
        var listen = Open();
        listen.Offer("unsendable", Audio(), Eight);

        listen.LetGo(listen.Take(Eight)!);

        Assert.Equal(0, listen.SentTonight);
        Assert.Equal(1, listen.RefusedTonight);
        Assert.Equal(0, listen.Waiting);
        Assert.Contains("could not be sent at all", listen.Report()!);
    }

    [Fact]
    public void Letting_one_line_go_does_not_hold_up_the_next()
    {
        var listen = Open();
        listen.Offer("unsendable", Audio(), Eight);
        listen.Offer("fine", Audio(), Eight);

        listen.LetGo(listen.Take(Eight)!);

        Assert.Equal("fine", listen.Take(Eight)!.Text);
    }

    [Fact]
    public void Letting_go_of_a_line_that_is_no_longer_the_head_changes_nothing()
    {
        // The same in-flight trap Done has: the box can be unticked while
        // a request is out, which empties the queue, and the answer then
        // arrives for a line that is no longer there.
        var listen = Open();
        listen.Offer("in flight", Audio(), Eight);

        var flying = listen.Take(Eight)!;

        listen.Wanted = false;
        listen.Wanted = true;
        listen.Offer("said after", Audio(), Eight);

        listen.LetGo(flying);

        Assert.Equal("said after", listen.Take(Eight)!.Text);
    }

    // ------------------------------------------------------------------
    //  The ids
    // ------------------------------------------------------------------

    [Fact]
    public void Ids_sort_into_the_order_the_lines_were_said()
    {
        // THE BUG THIS PREVENTS. The page plays what the relay hands back
        // in sorted id order, so unpadded numbers would have played the
        // evening scrambled from the tenth line on - "10" sorts between
        // "1" and "2".
        var ids = Enumerable.Range(1, 30).Select(n => ListenAlong.IdFor(Eight.Ticks, n)).ToList();

        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal).ToList(), ids);
    }

    [Fact]
    public void An_id_is_something_the_relay_will_accept()
    {
        // The relay's item-id pattern: starts with a letter or digit, then
        // letters, digits, dots, dashes and underscores, up to 64.
        var id = ListenAlong.IdFor(long.MaxValue, 999_999);

        Assert.Equal(id, RelayPublisher.SafeItemId(id));
        Assert.True(id.Length <= 64);
    }

    // ------------------------------------------------------------------
    //  The document, and the agreement it keeps with the page
    // ------------------------------------------------------------------

    [Fact]
    public void The_document_carries_the_audio_the_words_and_the_time()
    {
        var body = ListenAlong.Body(
            new ListenAlong.Spoken(ListenAlong.IdFor(Eight.Ticks, 1), "Lap five", new byte[] { 1, 2, 3 }, Eight));

        using var doc = System.Text.Json.JsonDocument.Parse(body);

        // These three names are an agreement with LISTEN_JS in
        // relay/src/pages.ts. Renaming one here without renaming it there
        // is a page that plays nothing and says nothing about why.
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), doc.RootElement.GetProperty("audio").GetString());
        Assert.Equal("Lap five", doc.RootElement.GetProperty("said").GetString());
        Assert.NotNull(doc.RootElement.GetProperty("at").GetString());
    }

    [Fact]
    public void A_line_with_no_words_still_sends_its_audio()
    {
        var listen = Open();
        listen.Offer(null, Audio(), Eight);

        var next = listen.Take(Eight)!;

        // The words are decoration on this channel; the audio is the
        // point. A null caption must not cost a listener the line.
        Assert.Empty(next.Text);
        Assert.NotEmpty(next.Mp3);
    }

    // ------------------------------------------------------------------
    //  What the night reads afterwards
    // ------------------------------------------------------------------

    [Fact]
    public void A_quiet_success_is_reported_as_one_line()
    {
        var listen = Open();
        listen.Offer("one", Audio(), Eight);
        listen.Done(listen.Take(Eight)!);

        Assert.Equal("Sent 1 line out to anyone listening along.", listen.Report());
    }

    [Fact]
    public void A_night_that_fell_behind_says_so()
    {
        var listen = Open();

        for (var i = 1; i <= ListenAlong.MostHeld + 2; i++)
        {
            listen.Offer("line " + i, Audio(), Eight);
        }

        var report = listen.Report();

        Assert.NotNull(report);
        Assert.Contains("did not make it in time", report);
    }

    [Fact]
    public void A_night_with_a_line_too_long_says_which_way_it_failed()
    {
        var listen = Open();
        listen.Offer("the recap", Audio(ListenAlong.MostBytes + 1), Eight);

        var report = listen.Report();

        Assert.NotNull(report);
        Assert.Contains("could not be sent at all", report);
    }

    [Fact]
    public void A_new_meeting_starts_the_counting_and_the_numbering_over()
    {
        var listen = Open();
        listen.NewMeeting("2026-10-06");
        listen.Offer("last night", Audio(), Eight);
        listen.Done(listen.Take(Eight)!);

        listen.NewMeeting("2026-10-13");

        Assert.Null(listen.Report());
        Assert.Equal(0, listen.Waiting);

        listen.Offer("tonight", Audio(), Eight);
        Assert.Equal(ListenAlong.IdFor(listen.Run, 1), listen.Take(Eight)!.Id);
    }

    [Fact]
    public void The_same_meeting_asked_for_again_changes_nothing()
    {
        // THE BUG THIS PREVENTS, and it would have been silent: the day
        // goes in on every line, because this app has no
        // race-day-changed event. If that were not idempotent, every line
        // would reset the numbering, every id would be L000001, and the
        // relay would treat the whole evening as corrections of its first
        // line - so a listener would hear one line, over and over.
        var listen = Open();

        listen.NewMeeting("2026-10-13");
        listen.Offer("one", Audio(), Eight);

        listen.NewMeeting("2026-10-13");
        listen.Offer("two", Audio(), Eight);

        Assert.Equal(2, listen.Waiting);
        Assert.Equal(ListenAlong.IdFor(listen.Run, 1), listen.Take(Eight)!.Id);
    }

    [Fact]
    public void No_race_day_open_is_one_meeting_and_not_a_new_one_every_line()
    {
        // The same trap by the other door: null read as "a day I have not
        // seen" would reset on every line of a practice evening with no
        // race day open at all.
        var listen = Open();

        listen.NewMeeting(null);
        listen.Offer("one", Audio(), Eight);

        listen.NewMeeting(null);
        listen.Offer("two", Audio(), Eight);

        Assert.Equal(2, listen.Waiting);
    }

    // ------------------------------------------------------------------
    //  The words a club reads
    // ------------------------------------------------------------------

    [Fact]
    public void Every_refusal_has_a_reason_written_for_a_club_to_read()
    {
        foreach (var verdict in Enum.GetValues<ListenAlong.Verdict>())
        {
            var because = ListenAlong.Because(verdict);

            Assert.False(string.IsNullOrWhiteSpace(because));
            Assert.DoesNotContain("something this version does not have words for", because);
        }
    }

    [Fact]
    public void The_channel_is_the_one_the_relay_registered()
    {
        // Spelt out rather than compared to a constant, because the other
        // end of this agreement is in TypeScript and cannot be referenced
        // from here. relay/src/types.ts, the "listen" entry in CHANNELS.
        string registered = ListenAlong.Channel;

        Assert.Equal("listen", registered);
    }
}
