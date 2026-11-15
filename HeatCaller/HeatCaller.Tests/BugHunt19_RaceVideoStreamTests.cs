using System.Net.WebSockets;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19, race video on the laptop: #6 a stop or restart while the OBS
/// link was down left the piece open for good; #23 a wrong OBS password was
/// called "OBS isn't answering"; #10 and #32 last night's pieces were never
/// let go while the app ran, and a junior race aged out of the document
/// while its piece was still in it; #49 leaving a link box unchanged still
/// redrew every row.
/// </summary>
public sealed class BugHunt19_RaceVideoStreamTests
{
    private static readonly DateTime At = new(2026, 11, 14, 19, 0, 0, DateTimeKind.Utc);

    private static JsonElement Status(bool active, double? durationMs = null) =>
        JsonDocument.Parse("{\"op\":7,\"d\":{\"requestType\":\"GetStreamStatus\",\"requestId\":\"hc-status\",\"responseData\":{\"outputActive\":"
            + (active ? "true" : "false") + (durationMs is { } ms ? ",\"outputDuration\":" + ms : string.Empty) + "}}}").RootElement;

    [Fact]
    public void OBS_saying_it_is_not_streaming_on_reconnect_ends_the_open_piece()
    {
        var stream = new StreamTonight();
        stream.Started(At);

        var status = ObsLink.StreamStatus(Status(active: false), At.AddHours(1))!.Value;
        stream.ObsSays(status.Streaming, status.LiveSinceUtc, At.AddHours(1));

        Assert.False(stream.Live);
        Assert.Equal(At.AddHours(1), stream.Pieces[0].EndedUtc);

        // And the next stream gets a piece of its own.
        stream.Started(At.AddHours(2));
        Assert.Equal(2, stream.Pieces.Count);
    }

    [Fact]
    public void A_stream_restarted_while_the_link_was_down_is_a_new_piece_from_its_real_start()
    {
        var stream = new StreamTonight();
        stream.Started(At);
        var now = At.AddHours(1);

        // OBS has been streaming ten minutes: the restart was at 19:50.
        var status = ObsLink.StreamStatus(Status(active: true, durationMs: 10 * 60 * 1000), now)!.Value;
        stream.ObsSays(status.Streaming, status.LiveSinceUtc, now);

        Assert.Equal(2, stream.Pieces.Count);
        Assert.Equal(At.AddMinutes(50), stream.Pieces[0].EndedUtc);
        Assert.Equal(At.AddMinutes(50), stream.Pieces[1].StartedUtc);
        Assert.True(stream.Live);
    }

    [Fact]
    public void The_same_stream_still_live_on_reconnect_stays_one_piece()
    {
        var stream = new StreamTonight();
        stream.Started(At);
        var now = At.AddHours(1);

        var status = ObsLink.StreamStatus(Status(active: true, durationMs: 60 * 60 * 1000 + 2000), now)!.Value;
        stream.ObsSays(status.Streaming, status.LiveSinceUtc, now);

        Assert.Single(stream.Pieces);
        Assert.True(stream.Live);
    }

    [Fact]
    public void A_refused_password_is_said_as_one()
    {
        var refused = ObsLink.Closed((WebSocketCloseStatus)4009);
        Assert.IsType<InvalidOperationException>(refused);
        Assert.Contains("password", refused.Message);

        Assert.IsType<WebSocketException>(ObsLink.Closed(WebSocketCloseStatus.NormalClosure));
        Assert.IsType<WebSocketException>(ObsLink.Closed(null));
    }

    [Fact]
    public void Last_nights_pieces_are_let_go_while_the_app_runs_so_tonights_stream_is_recorded()
    {
        var stream = new StreamTonight();

        for (var i = 0; i < 10; i++)
        {
            stream.Started(At.AddMinutes(i * 10));
            stream.Stopped(At.AddMinutes(i * 10 + 5));
        }

        var tomorrow = At.AddDays(1);
        stream.Started(tomorrow);

        Assert.Single(stream.Pieces);
        Assert.Equal(tomorrow, stream.Pieces[0].StartedUtc);
    }

    [Fact]
    public void Last_nights_pieces_are_let_go_before_the_closing_checklist_counts_them()
    {
        var stream = new StreamTonight();
        stream.Started(At);
        stream.Stopped(At.AddHours(2));
        var changed = 0;
        stream.Changed += () => changed++;

        stream.ForgetEarlierNight(At.AddHours(10));
        Assert.Single(stream.Pieces);
        Assert.Equal(0, changed);

        stream.ForgetEarlierNight(At.AddHours(21));
        Assert.Empty(stream.Pieces);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Junior_races_are_looked_for_as_far_back_as_the_pieces_held()
    {
        var stream = new StreamTonight();
        stream.Started(At);
        stream.Stopped(At.AddHours(1));
        stream.Started(At.AddHours(5));

        // 21 hours on, the first piece is still held (the second is only 16
        // hours old), so its junior races must still be named.
        var now = At.AddHours(21);
        Assert.Equal(At, stream.JuniorsSinceUtc(now));

        Assert.Equal(now.AddHours(-20), new StreamTonight().JuniorsSinceUtc(now));
    }

    [Fact]
    public void Leaving_a_link_box_unchanged_redraws_nothing()
    {
        var stream = new StreamTonight();
        stream.Started(At);
        stream.SetLink(0, "https://youtu.be/AAAAAAAAAAA");
        var changed = 0;
        stream.Changed += () => changed++;

        Assert.True(stream.SetLink(0, "AAAAAAAAAAA"));
        Assert.Equal(0, changed);

        Assert.True(stream.SetLink(0, "BBBBBBBBBBB"));
        Assert.Equal(1, changed);
    }
}
