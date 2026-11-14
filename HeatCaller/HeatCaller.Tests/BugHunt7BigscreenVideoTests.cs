namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7: sponsor videos on the Big Screen and on Live Results, and
/// the names on the on-air band.
///
/// A video that ends in a break before a slide that can't be shown (14), a
/// video still playing as the next race is called (79), a PC where video
/// can't play leading every break with nothing (80), a race call's names
/// carried onto the next show's band (118), and a Live Results video over
/// the opening laps (131).
///
/// The Big Screen needs a window, LibVLC and a sponsors folder to drive,
/// and the phone page runs in a browser, so these are read from the source
/// and the page as served - the way CommercialBreakTests and
/// BugHunt4WebTests already check the same code.
/// </summary>
public sealed class BugHunt7BigscreenVideoTests : IDisposable
{
    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- 14: a video that ends in a break, before a slide that can't be shown -------------

    [Fact]
    public void A_video_that_ends_in_a_break_comes_off_before_the_next_slot_is_tried()
    {
        var slot = MethodBody(
            Source("BigScreenForm.cs"), "private void SponsorSlotEnded()", "private void UpdateCommercialBreak()");

        // Hidden before the next slide is asked for, so a slide that shows
        // nothing does not leave the dead video "visible" holding the break.
        var counted = slot.IndexOf("_breakSlidesShown++;", StringComparison.Ordinal);
        var hidden = slot.IndexOf("_video.View.Visible = false;", counted, StringComparison.Ordinal);
        var next = slot.IndexOf("ShowNextSponsorSlide();", counted, StringComparison.Ordinal);

        Assert.True(counted >= 0 && hidden > counted && next > hidden);

        // And when nothing took the screen, the standings - which is also
        // what stops the old video and lets the music back up.
        var nothing = slot.IndexOf(
            "if (!_picSponsor.Visible && !(_video is not null && _video.View.Visible))", next, StringComparison.Ordinal);
        var board = slot.IndexOf("ShowLeaderboardAgain();", nothing, StringComparison.Ordinal);

        Assert.True(nothing > next && board > nothing);
    }

    // --- 79: a video still playing as the next race is called ------------------------------

    [Fact]
    public void A_sponsor_video_is_stopped_once_the_next_race_is_called()
    {
        var body = MethodBody(
            Source("BigScreenForm.cs"), "private void UpdateCommercialBreak()", "private string WhyNoVideo()");

        var offBreak = body.IndexOf("if (!onBreak || !canCarryOne)", StringComparison.Ordinal);
        var called = body.IndexOf(
            "_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing", offBreak, StringComparison.Ordinal);
        var stopped = body.IndexOf("ShowLeaderboardAgain();", called, StringComparison.Ordinal);

        // Asked BEFORE the "has the break already handed back" return, so a
        // video left running under the recap is caught when the race is,
        // not only on the tick the break ends.
        var handedBack = body.IndexOf("if (!_breakHasTheScreen)", offBreak, StringComparison.Ordinal);

        Assert.True(offBreak >= 0 && called > offBreak && stopped > called);
        Assert.True(handedBack > stopped);
    }

    // --- 80: a PC where video can't play ---------------------------------------------------

    [Fact]
    public void A_break_does_not_lead_with_a_video_this_pc_cannot_play()
    {
        var source = Source("BigScreenForm.cs");

        var next = MethodBody(source, "private int? NextVideoSlide()", "private bool VideoCannotPlayHere()");
        Assert.Contains("if (VideoCannotPlayHere())", next, StringComparison.Ordinal);

        // Settled first, so the engine's lock is never waited on from the
        // UI thread while LibVLC is still loading.
        var check = MethodBody(source, "private bool VideoCannotPlayHere()", "private int? NextStillSlide()");
        var settled = check.IndexOf("SponsorVideoEngine.Settled", StringComparison.Ordinal);
        var problem = check.IndexOf("SponsorVideoEngine.Problem", StringComparison.Ordinal);
        Assert.True(settled >= 0 && problem > settled);

        // And the reason is given in the log, rather than "none could be
        // found just now".
        Assert.Contains(
            "if (VideoCannotPlayHere())",
            MethodBody(source, "private string WhyNoVideo()", "private void OnRaceEvent("),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_video_that_cannot_play_gives_its_break_slot_to_a_still()
    {
        var show = MethodBody(
            Source("BigScreenForm.cs"), "private void ShowNextSponsorSlide(bool leadWithVideo = false)", "private int? NextVideoSlide()");

        var played = show.IndexOf("ShowSponsorVideo(path);", StringComparison.Ordinal);
        var still = show.IndexOf("NextStillSlide() is not { } instead", played, StringComparison.Ordinal);
        var picture = show.IndexOf("if (!PutSponsorPictureUp(path))", still, StringComparison.Ordinal);

        Assert.True(played >= 0 && still > played && picture > still);

        // Not when the break went while the video was starting: that path
        // has already handed the screen back to the standings.
        Assert.Contains("|| !CommercialBreak.Running", show[played..still], StringComparison.Ordinal);
    }

    [Fact]
    public void The_break_s_log_line_says_what_was_actually_shown()
    {
        var body = MethodBody(
            Source("BigScreenForm.cs"), "private void UpdateCommercialBreak()", "private string WhyNoVideo()");

        var asked = body.IndexOf("ShowNextSponsorSlide(leadWithVideo: true);", StringComparison.Ordinal);
        var shown = body.IndexOf("var ledWithVideo = !videoPlaying && _video is not null && _video.View.Visible;", asked, StringComparison.Ordinal);
        var said = body.IndexOf("? \"leading with a video advert.\"", shown, StringComparison.Ordinal);

        Assert.True(asked >= 0 && shown > asked && said > shown);
    }

    // --- 118: names from a line said outside a show ---------------------------------------

    [Fact]
    public void Only_a_line_said_on_air_puts_names_on_the_band()
    {
        var text = MethodBody(
            Source("BigScreenForm.cs"), "private void OnAnnouncementTextReady(string text)", "private IReadOnlyList<LowerThird.Candidate> WhoCouldBeNamed()");

        Assert.Contains("_sayingAbout = ShowOnAir.Playing || _onAir.Visible", text, StringComparison.Ordinal);
        Assert.Contains(": string.Empty;", text, StringComparison.Ordinal);
    }

    // --- Helpers ----------------------------------------------------------------------------

    /// <summary>The folder the repo is checked out in, found by the file that is only ever at its root.</summary>
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

    /// <summary>The source from <paramref name="from"/> up to <paramref name="until"/>.</summary>
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
