namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), closing HeatCaller while a race's clips and
/// highlight videos are still being made: a person closing the window is
/// asked and can wait; a video is written under a ".part.mp4" name until it
/// is whole; and the sweep removes the working files a close left behind -
/// and nothing else.
/// </summary>
public sealed class BugHunt17_ClosingWhileClipsAreMadeTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly string _folder;

    public BugHunt17_ClosingWhileClipsAreMadeTests()
    {
        RaceAudioStore.OffForTests = false;

        // Never the store's default, which is under the real Documents.
        _folder = Path.Combine(_data.Folder, "audio");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        RaceAudioStore.OffForTests = true;
        _data.Dispose();
    }

    private RaceAudioStore Store()
    {
        var store = RaceAudioStore.Load();

        store.FolderSetting = _folder;

        Assert.Equal(_folder, store.Folder);

        return store;
    }

    private string Highlights(string night)
    {
        var folder = Path.Combine(_folder, night);
        Directory.CreateDirectory(folder);
        RaceAudioStore.MarkAsNight(folder);

        var highlights = Path.Combine(folder, DriversOwnClip.FolderName);
        Directory.CreateDirectory(highlights);
        return highlights;
    }

    /// <summary>A file last written long enough ago that nothing can still be writing it.</summary>
    private static string Old(string path)
    {
        File.WriteAllText(path, "not really audio");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        return path;
    }

    // ------------------------------------------------------------ the question

    [Theory]
    [InlineData(CloseReason.UserClosing, 1, true)]
    [InlineData(CloseReason.UserClosing, 3, true)]
    [InlineData(CloseReason.UserClosing, 0, false)] // nothing being made
    [InlineData(CloseReason.WindowsShutDown, 2, false)] // nobody there to answer
    [InlineData(CloseReason.TaskManagerClosing, 2, false)]
    [InlineData(CloseReason.ApplicationExitCall, 2, false)] // a restart after a restore
    [InlineData(CloseReason.None, 2, false)]
    public void Only_a_person_closing_the_window_is_asked_and_only_while_clips_are_being_made(CloseReason reason, int races, bool asked)
    {
        Assert.Equal(asked, MainForm.AsksBeforeClosingOnClips(reason, races));
    }

    [Fact]
    public void The_question_says_what_is_still_being_made()
    {
        Assert.Equal("the drivers' clips from the last race", MainForm.ClipsStillBeingMade(races: 1, withVideos: 0));
        Assert.Equal("the drivers' clips and highlight videos from the last race", MainForm.ClipsStillBeingMade(races: 1, withVideos: 1));
        Assert.Equal("the drivers' clips and highlight videos from the last 3 races", MainForm.ClipsStillBeingMade(races: 3, withVideos: 1));

        var question = MainForm.ClipsClosingQuestion(races: 2, withVideos: 2);

        Assert.StartsWith("HeatCaller is still making the drivers' clips and highlight videos from the last 2 races.", question, StringComparison.Ordinal);
        Assert.Contains("closes by itself", question, StringComparison.Ordinal);
        Assert.Contains("are lost", question, StringComparison.Ordinal);

        // Videos are only mentioned when there are some to make.
        Assert.DoesNotContain("video", MainForm.ClipsClosingQuestion(races: 1, withVideos: 0), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Waiting_is_the_last_button_so_escape_and_a_closed_question_wait()
    {
        // AppDialog.Choose answers the LAST label for Escape, Alt+F4 and a
        // window closed without a button - which has to be the answer that
        // loses nothing.
        Assert.Equal(new[] { "Close now", "Wait, then close" }, MainForm.ClipsClosingAnswers);
        Assert.Equal(1, AppDialog.IndexChosen(DialogResult.Cancel, MainForm.ClipsClosingAnswers.Length));

        var closing = Source("MainForm.ClosingWait.cs");
        Assert.Contains("defaultIndex: ClipsClosingAnswers.Length - 1,", closing, StringComparison.Ordinal);
    }

    [Fact]
    public void The_close_asks_before_anything_is_torn_down()
    {
        var main = Source("MainForm.cs");

        var ask = main.IndexOf("if (WaitsForClipsBeforeClosing(e.CloseReason))", StringComparison.Ordinal);
        var tornDown = main.IndexOf("_closingDown = true;", StringComparison.Ordinal);

        Assert.True(ask >= 0 && tornDown > ask, "The question has to come before the window starts closing down - the clips still need the relay.");
    }

    [Fact]
    public void A_race_is_counted_until_its_clips_are_over_and_only_once()
    {
        var before = RaceAudioRecorder.AfterRaceWork;

        var clipsOnly = RaceAudioRecorder.NoteAfterRaceWork(videos: false);
        var withVideos = RaceAudioRecorder.NoteAfterRaceWork(videos: true);

        try
        {
            Assert.Equal((before.Races + 2, before.WithVideos + 1), RaceAudioRecorder.AfterRaceWork);
        }
        finally
        {
            withVideos();
            withVideos();
            clipsOnly();
            clipsOnly();
        }

        Assert.Equal(before, RaceAudioRecorder.AfterRaceWork);
    }

    // ------------------------------------------------------ the ".part" name

    [Fact]
    public void A_video_is_written_under_a_part_name_that_still_ends_in_mp4()
    {
        var mp4 = Path.Combine(_folder, "Highlights", "A-Main - P1 Sam Hill.mp4");

        var part = HighlightVideo.PartPathFor(mp4);

        Assert.Equal(Path.Combine(_folder, "Highlights", "A-Main - P1 Sam Hill.part.mp4"), part);

        // Windows' writer picks the container from the extension.
        Assert.Equal(".mp4", Path.GetExtension(part));
        Assert.EndsWith(HighlightVideo.PartEnding, part, StringComparison.Ordinal);
    }

    [Fact]
    public void A_finished_video_is_renamed_to_its_real_name()
    {
        var highlights = Highlights("2026-11-13");
        var mp4 = Path.Combine(highlights, "A-Main - P1 Sam Hill.mp4");
        var part = HighlightVideo.PartPathFor(mp4);
        File.WriteAllText(part, "the whole video");

        HighlightVideo.MoveIntoPlace(part, mp4);

        Assert.False(File.Exists(part));
        Assert.Equal("the whole video", File.ReadAllText(mp4));
    }

    [Fact]
    public void A_finished_video_never_replaces_one_already_there()
    {
        var highlights = Highlights("2026-11-13");
        var mp4 = Path.Combine(highlights, "A-Main - P1 Sam Hill.mp4");
        var part = HighlightVideo.PartPathFor(mp4);
        File.WriteAllText(mp4, "the first");
        File.WriteAllText(part, "the second");

        Assert.ThrowsAny<IOException>(() => HighlightVideo.MoveIntoPlace(part, mp4));

        Assert.Equal("the first", File.ReadAllText(mp4));
        Assert.True(File.Exists(part));
    }

    [Fact]
    public void The_video_is_only_renamed_after_the_writer_has_finished_and_let_go()
    {
        // Needs Windows' encoder to run, so pinned by reading the source.
        var source = Source("HighlightVideo.cs");

        var open = source.IndexOf("Mp4Writer.Open(partPath,", StringComparison.Ordinal);
        var finish = source.IndexOf("writer.Finish();", StringComparison.Ordinal);
        var letGo = source.IndexOf("writer.Dispose();", finish, StringComparison.Ordinal);
        var rename = source.IndexOf("MoveIntoPlace(partPath, mp4Path);", StringComparison.Ordinal);

        Assert.True(open >= 0 && finish > open && letGo > finish && rename > letGo, "Open the part, Finish, Dispose, then rename.");
        Assert.DoesNotContain("Mp4Writer.Open(mp4Path", source, StringComparison.Ordinal);
    }

    // --------------------------------------------------------------- the sweep

    [Fact]
    public void The_sweep_removes_a_part_video_and_a_videos_wav_left_by_a_close()
    {
        var highlights = Highlights("2026-11-06");

        var clip = Old(Path.Combine(highlights, "A-Main - P1 Sam Hill.mp3"));
        var videoWav = Old(Path.Combine(highlights, "A-Main - P1 Sam Hill" + RaceAudioRecorder.VideoWavMarker + ".wav"));
        var part = Old(Path.Combine(highlights, "A-Main - P1 Sam Hill" + HighlightVideo.PartEnding));
        var finished = Old(Path.Combine(highlights, "A-Main - P2 Jo Reid.mp4"));

        var lines = new List<string>();
        RaceAudioRecorder.SweepOrphans(Store(), null, lines.Add);

        Assert.False(File.Exists(videoWav));
        Assert.False(File.Exists(part));

        // The clip and a finished video are the club's, and stay.
        Assert.True(File.Exists(clip));
        Assert.True(File.Exists(finished));

        Assert.Contains(lines, line => line.Contains("never finished", StringComparison.Ordinal) && line.Contains(part, StringComparison.Ordinal));
    }

    [Fact]
    public void Working_files_written_moments_ago_are_left_for_the_next_sweep()
    {
        var highlights = Highlights("2026-11-13");

        File.WriteAllText(Path.Combine(highlights, "Heat 1 - P1 Sam Hill.mp3"), "the clip");

        var videoWav = Path.Combine(highlights, "Heat 1 - P1 Sam Hill" + RaceAudioRecorder.VideoWavMarker + ".wav");
        var part = Path.Combine(highlights, "Heat 1 - P1 Sam Hill" + HighlightVideo.PartEnding);
        File.WriteAllText(videoWav, "being cut");
        File.WriteAllText(part, "being written");

        RaceAudioRecorder.SweepOrphans(Store(), null, _ => { });

        Assert.True(File.Exists(videoWav));
        Assert.True(File.Exists(part));
    }

    [Fact]
    public void A_kept_clip_of_a_driver_called_video_is_not_taken_for_a_working_file()
    {
        var highlights = Highlights("2026-11-06");

        // A clip the encoder refused is kept as a plain wav under the
        // driver's name - and this driver's name ends the way a video's
        // working file does. No clip of "P4 Vic" beside it, so it is a clip.
        var kept = Old(Path.Combine(highlights, "A-Main - P4 Vic Video.wav"));

        Assert.False(RaceAudioRecorder.IsVideoLeftover(kept));

        RaceAudioRecorder.SweepOrphans(Store(), null, _ => { });

        Assert.True(File.Exists(kept));
    }

    [Fact]
    public void Only_files_in_a_highlights_folder_are_ever_taken()
    {
        var highlights = Highlights("2026-11-06");
        var night = Path.GetDirectoryName(highlights)!;

        // The club's own files, beside the recordings rather than among the clips.
        Old(Path.Combine(night, "Club day.mp3"));
        var ownWav = Old(Path.Combine(night, "Club day video.wav"));
        var ownPart = Old(Path.Combine(night, "Phone clip.part.mp4"));

        Assert.False(RaceAudioRecorder.IsVideoLeftover(ownWav));
        Assert.False(RaceAudioRecorder.IsVideoLeftover(ownPart));

        RaceAudioRecorder.SweepOrphans(Store(), null, _ => { });

        Assert.True(File.Exists(ownWav));
        Assert.True(File.Exists(ownPart));
    }

    [Fact]
    public void A_working_file_is_told_by_its_name_and_place()
    {
        var highlights = Highlights("2026-11-06");

        Old(Path.Combine(highlights, "A-Main - P1 Sam Hill.mp3"));
        Old(Path.Combine(highlights, "A-Main - P2 Jo Reid.wav")); // a clip kept as a wav

        Assert.True(RaceAudioRecorder.IsVideoLeftover(Path.Combine(highlights, "A-Main - P1 Sam Hill video.wav")));
        Assert.True(RaceAudioRecorder.IsVideoLeftover(Path.Combine(highlights, "A-Main - P2 Jo Reid video.wav")));
        Assert.True(RaceAudioRecorder.IsVideoLeftover(Path.Combine(highlights, "A-Main - P1 Sam Hill.part.mp4")));

        // Finished things, and names that only look alike.
        Assert.False(RaceAudioRecorder.IsVideoLeftover(Path.Combine(highlights, "A-Main - P1 Sam Hill.mp4")));
        Assert.False(RaceAudioRecorder.IsVideoLeftover(Path.Combine(highlights, "A-Main - P1 Sam Hill.mp3")));
        Assert.False(RaceAudioRecorder.IsVideoLeftover(Path.Combine(highlights, "A-Main - P1 Sam Hill.wav")));
        Assert.False(RaceAudioRecorder.IsVideoLeftover(Path.Combine(highlights, "A-Main - P9 Nobody video.wav")));
        Assert.False(RaceAudioRecorder.IsVideoLeftover(Path.Combine(highlights, " video.wav")));
    }

    [Fact]
    public void A_videos_working_files_are_held_while_it_is_made_so_no_sweep_takes_them()
    {
        // The video queue can keep a race's cut waiting, untouched, for
        // minutes behind other races' videos. It takes a real encode to hold
        // one, so pinned by reading the source.
        var recorder = Source("RaceAudioRecorder.cs");
        var from = recorder.IndexOf("private static string? MakeVideoFile(", StringComparison.Ordinal);
        var to = recorder.IndexOf("private static void CutWav(", StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "MakeVideoFile is no longer where it was.");
        var make = recorder[from..to];

        Assert.Contains("Publishing[wavHeld] = 0;", make, StringComparison.Ordinal);
        Assert.Contains("Publishing[partHeld] = 0;", make, StringComparison.Ordinal);
        Assert.Contains("Publishing.TryRemove(wavHeld, out _);", make, StringComparison.Ordinal);
        Assert.Contains("Publishing.TryRemove(partHeld, out _);", make, StringComparison.Ordinal);

        var sweep = recorder[recorder.IndexOf("internal static void SweepOrphans(", StringComparison.Ordinal)..];
        Assert.Contains("|| InUse(stray)", sweep, StringComparison.Ordinal);
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
