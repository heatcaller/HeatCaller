using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Race video, the laptop's half (14 Nov 2026, race video plan Phase 0):
/// tonight's stream pieces, YouTube links read, OBS's WebSocket answered,
/// and the private "stream" document the relay works each race out of.
/// </summary>
public sealed class RaceVideoTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?feature=share&v=dQw4w9WgXcQ&t=30", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ?si=abc", "dQw4w9WgXcQ")]
    [InlineData("youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void Reads_the_video_out_of_any_YouTube_link(string link, string id) =>
        Assert.Equal(id, StreamTonight.VideoIdFrom(link));

    [Theory]
    [InlineData("https://vimeo.com/123456789")]
    [InlineData("https://www.youtube.com/channel/UC1234567890")]
    [InlineData("https://evil.example/watch?v=dQw4w9WgXcQ")]
    [InlineData("not a link")]
    [InlineData("")]
    public void Takes_nothing_that_isnt_a_YouTube_video(string link) =>
        Assert.Null(StreamTonight.VideoIdFrom(link));

    [Fact]
    public void A_dropped_and_restarted_stream_is_two_pieces_each_with_its_own_link()
    {
        var stream = new StreamTonight();
        var at = new DateTime(2026, 11, 14, 23, 0, 0, DateTimeKind.Utc);

        stream.Started(at);
        stream.Started(at.AddMinutes(1));
        stream.Stopped(at.AddMinutes(30));
        stream.Started(at.AddMinutes(32));

        Assert.Equal(2, stream.Pieces.Count);
        Assert.True(stream.Live);
        Assert.True(stream.SetLink(0, "https://youtu.be/AAAAAAAAAAA"));
        Assert.False(stream.SetLink(1, "https://vimeo.com/1"));
        stream.Nudge(0, 5);
        stream.Nudge(0, -1);

        Assert.Equal("AAAAAAAAAAA", stream.Pieces[0].VideoId);
        Assert.Equal(4, stream.Pieces[0].NudgeSeconds);
        Assert.Equal(at.AddMinutes(30), stream.Pieces[0].EndedUtc);
    }

    [Fact]
    public void The_document_carries_the_pieces_the_switches_and_the_hidden_and_junior_races()
    {
        var stream = new StreamTonight();
        var at = new DateTime(2026, 11, 14, 23, 0, 0, DateTimeKind.Utc);
        stream.Started(at);
        stream.SetLink(0, "AAAAAAAAAAA");
        stream.SetRaceHidden("0123456789abcdef0123456789abcdef", hidden: true);
        var settings = new AppSettings { VideoEnabled = true, VideoOnPublicResults = false, VideoLeadInSeconds = 12, VideoObsPassword = "secret" };

        var json = stream.Document(settings, new[] { "fedcba9876543210fedcba9876543210" });
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("on").GetBoolean());
        Assert.False(root.GetProperty("publicResults").GetBoolean());
        Assert.Equal(12, root.GetProperty("leadIn").GetInt32());
        Assert.Equal("AAAAAAAAAAA", root.GetProperty("pieces")[0].GetProperty("videoId").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("pieces")[0].GetProperty("endedUtc").ValueKind);
        Assert.Equal("0123456789abcdef0123456789abcdef", root.GetProperty("hidden")[0].GetString());
        Assert.Equal("fedcba9876543210fedcba9876543210", root.GetProperty("juniors")[0].GetString());
        Assert.DoesNotContain("secret", json);
    }

    [Fact]
    public void Another_nights_pieces_are_let_go_when_tonights_file_loads()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stream-{Guid.NewGuid():N}.json");
        try
        {
            var then = new DateTime(2026, 11, 6, 23, 0, 0, DateTimeKind.Utc);
            var old = new StreamTonight(path);
            old.Started(then);
            old.SetLink(0, "AAAAAAAAAAA");

            Assert.Single(new StreamTonight(path, then.AddHours(3)).Pieces);
            Assert.Empty(new StreamTonight(path, then.AddDays(8)).Pieces);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Answers_OBS_password_challenge_as_its_protocol_says()
    {
        // The worked example in obs-websocket's own protocol document.
        Assert.Equal("1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=",
            ObsLink.AuthAnswer("supersecretpassword", "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=", "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY="));
    }

    [Fact]
    public void Hears_the_stream_starting_and_stopping_and_nothing_else()
    {
        static JsonElement Event(string type, string state) =>
            JsonDocument.Parse("{\"op\":5,\"d\":{\"eventType\":\"" + type + "\",\"eventData\":{\"outputActive\":true,\"outputState\":\"" + state + "\"}}}").RootElement;

        Assert.True(ObsLink.StreamEvent(Event("StreamStateChanged", "OBS_WEBSOCKET_OUTPUT_STARTED")));
        Assert.False(ObsLink.StreamEvent(Event("StreamStateChanged", "OBS_WEBSOCKET_OUTPUT_STOPPED")));
        Assert.Null(ObsLink.StreamEvent(Event("StreamStateChanged", "OBS_WEBSOCKET_OUTPUT_STARTING")));
        Assert.Null(ObsLink.StreamEvent(Event("RecordStateChanged", "OBS_WEBSOCKET_OUTPUT_STARTED")));
    }

    [Theory]
    [InlineData("early", "20", "Video of Q1 Heat 2 starts in the wrong place - it opens too early, by about 20 s.")]
    [InlineData("late", "", "Video of Q1 Heat 2 starts in the wrong place - it opens too late.")]
    [InlineData("", "30", "Video of Q1 Heat 2 starts in the wrong place - off by about 30 s.")]
    [InlineData("", "9999", "Video of Q1 Heat 2 starts in the wrong place.")]
    public void Says_what_a_driver_reported_about_a_race_video(string direction, string seconds, string start) =>
        Assert.StartsWith(start, DriverRequestDesk.VideoOffWords("Q1 Heat 2", direction, seconds));

    [Fact]
    public void A_hidden_piece_goes_to_the_relay_hidden()
    {
        var stream = new StreamTonight();
        stream.Started(new DateTime(2026, 11, 14, 23, 0, 0, DateTimeKind.Utc));
        stream.SetLink(0, "AAAAAAAAAAA");
        stream.SetPieceHidden(0, hidden: true);

        using var doc = JsonDocument.Parse(stream.Document(new AppSettings { VideoEnabled = true }, Array.Empty<string>()));
        Assert.True(doc.RootElement.GetProperty("pieces")[0].GetProperty("hidden").GetBoolean());
    }

    [Fact]
    public void A_drivers_video_report_keeps_which_way_and_how_far()
    {
        Assert.Equal(5, DriverRequestStore.LimitOf("d"));
        Assert.Equal(3, DriverRequestStore.LimitOf("s"));
    }

    [Fact]
    public void Reads_which_videos_YouTube_refused_and_nothing_else()
    {
        var dead = RelayPublisher.ParseDeadVideos("{\"ok\":true,\"dead\":[\"AAAAAAAAAAA\",\"not an id\",5,\"AAAAAAAAAAA\"]}");
        Assert.Equal(new[] { "AAAAAAAAAAA" }, dead);
        Assert.Empty(RelayPublisher.ParseDeadVideos("not json"));
        Assert.Empty(RelayPublisher.ParseDeadVideos("[]"));
    }

    [Fact]
    public void The_OBS_password_is_sealed_with_the_other_secrets_never_in_the_plain_settings()
    {
        var settings = new AppSettings { VideoObsPassword = "obs-secret" };
        Assert.Equal("obs-secret", settings.CaptureSecrets().VideoObsPassword);
        Assert.DoesNotContain("obs-secret", JsonSerializer.Serialize(settings));
    }
}
