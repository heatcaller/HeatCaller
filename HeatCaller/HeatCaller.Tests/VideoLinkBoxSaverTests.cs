namespace HeatCaller.Tests;

/// <summary>
/// The race video link box saves as it's filled in, not only on Leave (open
/// items 19 Nov 2026, item 1): on 16 Nov a link pasted and left in the box
/// never saved.
/// </summary>
public sealed class VideoLinkBoxSaverTests
{
    private sealed class Box : TextBox
    {
        public void LeaveNow() => OnLeave(EventArgs.Empty);
    }

    private static (Box Box, StreamTonight Stream, VideoLinkBoxSaver Saver, List<string> Refused) Card()
    {
        var stream = new StreamTonight();
        stream.Started(new DateTime(2026, 11, 16, 23, 0, 0, DateTimeKind.Utc));
        var box = new Box();
        var refused = new List<string>();
        var saver = new VideoLinkBoxSaver(box, text => stream.SetLink(0, text), () => refused.Add(box.Text));
        return (box, stream, saver, refused);
    }

    [Fact]
    public void A_pasted_link_saves_at_once_without_leaving_the_box()
    {
        var (box, stream, saver, refused) = Card();
        using var _ = box;

        box.Text = "https://www.youtube.com/live/dQw4w9WgXcQ?si=abc";

        Assert.Equal("dQw4w9WgXcQ", stream.Pieces[0].VideoId);
        Assert.False(saver.Pending);
        Assert.Empty(refused);
    }

    [Fact]
    public void A_typed_link_waits_a_second_after_the_last_key_then_saves()
    {
        var (box, stream, saver, refused) = Card();
        using var _ = box;

        foreach (var c in "dQw4w9WgXcQ")
        {
            box.AppendText(c.ToString());
        }

        Assert.Equal(string.Empty, stream.Pieces[0].VideoId);
        Assert.True(saver.Pending);
        Assert.Equal(1000, VideoLinkBoxSaver.DebounceMs);

        saver.SaveNow(); // the debounce timer's tick

        Assert.Equal("dQw4w9WgXcQ", stream.Pieces[0].VideoId);
        Assert.False(saver.Pending);
        Assert.Empty(refused);
    }

    [Fact]
    public void A_half_typed_link_isnt_told_off_until_the_box_is_left()
    {
        var (box, stream, saver, refused) = Card();
        using var _ = box;

        box.AppendText("h");
        box.AppendText("t");
        saver.SaveNow();
        Assert.Empty(refused);

        box.LeaveNow();
        Assert.Equal(new[] { "ht" }, refused);
        Assert.Equal(string.Empty, stream.Pieces[0].VideoId);
    }

    [Fact]
    public void Leaving_the_box_still_saves_and_stops_the_wait()
    {
        var (box, stream, saver, refused) = Card();
        using var _ = box;

        box.AppendText("d");
        box.Text = "dQw4w9WgXc"; // 10 characters: not a video yet
        box.AppendText("Q");
        Assert.True(saver.Pending);

        box.LeaveNow();

        Assert.False(saver.Pending);
        Assert.Equal("dQw4w9WgXcQ", stream.Pieces[0].VideoId);
        Assert.Empty(refused);
    }

    [Fact]
    public void The_relay_is_asked_sooner_while_YouTube_refuses_a_video()
    {
        // Open items item 1: the relay re-asks a refused video after 2, 5,
        // 15 and 60 minutes, set off by the laptop's ask - so the laptop
        // asks every two minutes while one is refused, ten otherwise.
        Assert.Equal(TimeSpan.FromMinutes(2), RelayPublisher.VideoCheckWait(1));
        Assert.Equal(TimeSpan.FromMinutes(10), RelayPublisher.VideoCheckWait(0));
    }

    [Fact]
    public void The_card_wires_its_link_boxes_through_the_saver()
    {
        var source = File.ReadAllText(Path.Combine(SourceDir(), "RelaySettingsControl.Video.cs"));
        Assert.Contains("new VideoLinkBoxSaver(", source);
        Assert.DoesNotContain("link.Leave +=", source);
    }

    private static string SourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller");
    }
}
