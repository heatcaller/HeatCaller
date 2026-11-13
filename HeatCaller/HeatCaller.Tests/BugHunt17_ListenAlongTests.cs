namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), the listen-along queue's counts.
///
/// Take only looks at the head, and the publisher then spends a while
/// sending it. With the queue full, a newer line pushed that head off and it
/// was counted as dropped there and then - and when the relay answered for
/// it, Done found a different head and did nothing. The recap reported a
/// line "that did not make it" which listeners had heard, and the stream's
/// health left out its slowest delivery and added a "never sent" that was.
/// </summary>
public sealed class BugHunt17_ListenAlongTests
{
    private static readonly DateTime Eight = new(2026, 11, 13, 20, 0, 0, DateTimeKind.Utc);

    private static byte[] Audio() => new byte[4096];

    private static ListenSegment Segment(long number, DateTime at) =>
        new(new byte[2048], 4, 115, 1152, 44100, 638_000_000_000_000_000, number, Array.Empty<ListenCaption>(), at);

    /// <summary>A queue with one line being sent and enough said since to push it off the front.</summary>
    private static (ListenAlong Listen, ListenAlong.Spoken Flying) PushedOffMidSend()
    {
        var listen = new ListenAlong { Wanted = true };
        listen.Offer("in flight", Audio(), Eight);

        var flying = listen.Take(Eight)!;

        for (var n = 1; n <= ListenAlong.MostHeld; n++)
        {
            listen.Offer("said since " + n, Audio(), Eight);
        }

        Assert.Equal(ListenAlong.MostHeld, listen.Waiting);
        return (listen, flying);
    }

    [Fact]
    public void A_line_pushed_off_while_it_was_being_sent_counts_as_sent_once_the_relay_has_it()
    {
        var (listen, flying) = PushedOffMidSend();

        // Not known yet either way.
        Assert.Equal(0, listen.DroppedTonight);

        listen.Done(flying);

        Assert.Equal(1, listen.SentTonight);
        Assert.Equal(0, listen.DroppedTonight);

        // And nothing else left the queue for it.
        Assert.Equal(ListenAlong.MostHeld, listen.Waiting);
        Assert.Equal("said since 1", listen.Take(Eight)!.Text);
    }

    [Fact]
    public void A_line_pushed_off_whose_send_failed_is_one_that_never_went_out()
    {
        var (listen, _) = PushedOffMidSend();

        // No answer for it: the publisher just asks for the next.
        Assert.Equal("said since 1", listen.Take(Eight)!.Text);

        Assert.Equal(0, listen.SentTonight);
        Assert.Equal(1, listen.DroppedTonight);

        // Once, however many more passes there are.
        listen.Take(Eight);
        Assert.Equal(1, listen.DroppedTonight);
    }

    [Fact]
    public void A_line_pushed_off_and_then_let_go_is_refused_not_dropped()
    {
        var (listen, flying) = PushedOffMidSend();

        listen.LetGo(flying);

        Assert.Equal(1, listen.RefusedTonight);
        Assert.Equal(0, listen.DroppedTonight);
        Assert.Equal(0, listen.SentTonight);
        Assert.Equal(ListenAlong.MostHeld, listen.Waiting);
    }

    [Fact]
    public void A_line_pushed_off_with_nobody_sending_it_is_still_dropped_at_once()
    {
        var listen = new ListenAlong { Wanted = true };

        for (var n = 0; n <= ListenAlong.MostHeld; n++)
        {
            listen.Offer("line " + n, Audio(), Eight);
        }

        Assert.Equal(1, listen.DroppedTonight);
    }

    [Fact]
    public void A_stretch_pushed_off_while_it_was_being_sent_is_in_the_streams_health_as_delivered()
    {
        var listen = new ListenAlong { Wanted = true };
        listen.OfferSegment(Segment(1, Eight));

        var flying = listen.Take(Eight)!;

        for (var n = 1; n <= ListenAlong.MostHeld; n++)
        {
            listen.OfferSegment(Segment(1 + n, Eight.AddSeconds(n)));
        }

        // The slow one - the delivery the health figures are there to show.
        listen.Done(flying, Eight.AddSeconds(7));

        var delivery = listen.TakeDelivery()!.Value;

        Assert.Equal(1, delivery.Sent);
        Assert.Equal(7, delivery.WorstSeconds, 3);
        Assert.Equal(1, delivery.Late);
        Assert.Equal(0, delivery.Missed);
        Assert.DoesNotContain("gap", listen.Report() ?? string.Empty);
    }

    [Fact]
    public void An_answer_after_the_switch_went_off_still_counts_for_nothing()
    {
        var (listen, flying) = PushedOffMidSend();

        listen.Wanted = false;
        listen.Wanted = true;

        listen.Done(flying);

        Assert.Equal(0, listen.SentTonight);
    }
}
