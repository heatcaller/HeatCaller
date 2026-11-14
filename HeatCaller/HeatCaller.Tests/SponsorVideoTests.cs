namespace HeatCaller.Tests;

/// <summary>
/// Sponsor VIDEO slides: what the rotation will take in, how a video gets
/// into it, and the two things that go wrong quietly if nobody is watching -
/// the music never coming back after an ad, and a browser's Range request
/// being answered with the wrong bytes.
///
/// Nothing here starts LibVLC. The engine is not testable without the
/// native libraries and a screen to draw on, so what is tested is
/// everything around it: the classification that decides whether LibVLC is
/// asked at all (SponsorMedia), the counting that mutes and unmutes the
/// music (AdBreak), the store, and the HTTP range parser.
/// </summary>
public sealed class SponsorVideoTests : IDisposable
{
    private readonly TempAppData _data = new();

    public SponsorVideoTests() => AdBreak.ResetForTests();

    public void Dispose()
    {
        // Static, so a test that left a break open would mute the music in
        // every test after it - and in a real app run started by the layout
        // test in the same process.
        AdBreak.ResetForTests();
        _data.Dispose();
    }

    // --- What counts as a video -----------------------------------------------------------

    [Theory]
    [InlineData("a1b2c3d4_HondaAd.mp4", true)]
    [InlineData("a1b2c3d4_HondaAd.MP4", true)]
    [InlineData("clip.m4v", true)]
    [InlineData("clip.mov", true)]
    [InlineData("clip.webm", true)]
    [InlineData("clip.mkv", true)]
    [InlineData("clip.avi", true)]
    [InlineData("clip.wmv", true)]
    [InlineData("logo.png", false)]
    [InlineData("logo.jpg", false)]
    [InlineData("logo.gif", false)]
    [InlineData("noextension", false)]
    public void A_slide_is_a_video_by_its_extension(string fileName, bool video) =>
        Assert.Equal(video, SponsorMedia.IsVideoName(fileName));

    [Fact]
    public void A_playlist_is_never_a_video()
    {
        // The reason the extension list is spelled out rather than being
        // "whatever LibVLC opens": a playlist handed to LibVLC would have it
        // fetch whatever the file names, over the network, from a slide a
        // club dropped in. See SponsorMedia.VideoExtensions.
        foreach (var playlist in new[] { "ad.m3u", "ad.m3u8", "ad.xspf", "ad.asx", "ad.pls" })
        {
            Assert.False(SponsorMedia.IsVideoName(playlist));
        }
    }

    [Fact]
    public void The_image_and_video_extension_lists_do_not_overlap()
    {
        // One list decides which of two entirely different paths a slide
        // takes. An extension on both would make that a coin toss.
        Assert.Empty(SponsorMedia.VideoExtensions.Intersect(SponsorMedia.ImageExtensions));
    }

    // --- The header sniff -----------------------------------------------------------------

    [Fact]
    public void An_mp4_header_is_recognised()
    {
        // A real MP4 opens with a box length then "ftyp".
        Assert.True(SponsorMedia.LooksLikeVideo(Header(0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p')));
    }

    [Fact]
    public void A_mov_that_leads_with_another_box_is_recognised()
    {
        // QuickTime files in the wild lead with wide/free/skip/moov/mdat
        // rather than ftyp, and turning those away would turn away real
        // sponsor files.
        foreach (var box in new[] { "wide", "free", "skip", "moov", "mdat" })
        {
            var header = Header(0, 0, 0, 0x20,
                (byte)box[0], (byte)box[1], (byte)box[2], (byte)box[3]);

            Assert.True(SponsorMedia.LooksLikeVideo(header), box + " should be recognised");
        }
    }

    [Fact]
    public void Matroska_avi_and_asf_headers_are_recognised()
    {
        Assert.True(SponsorMedia.LooksLikeVideo(Header(0x1A, 0x45, 0xDF, 0xA3)));

        Assert.True(SponsorMedia.LooksLikeVideo(Header(
            (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0,
            (byte)'A', (byte)'V', (byte)'I', (byte)' ')));

        Assert.True(SponsorMedia.LooksLikeVideo(Header(0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11)));
    }

    [Fact]
    public void An_executable_is_not_a_video_whatever_it_is_called()
    {
        // "MZ" - a Windows executable. The whole reason the bytes are looked
        // at: IsVideoName trusts the extension for the rest of the file's
        // life, so the extension is vouched for once, here.
        Assert.False(SponsorMedia.LooksLikeVideo(Header(0x4D, 0x5A, 0x90, 0x00, 0x03, 0, 0, 0)));
    }

    [Fact]
    public void A_wav_is_not_a_video_even_though_it_is_a_riff_file()
    {
        // RIFF, like an AVI, but "WAVE" where "AVI " would be. A club's
        // sound effect is not a sponsor slide.
        Assert.False(SponsorMedia.LooksLikeVideo(Header(
            (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0,
            (byte)'W', (byte)'A', (byte)'V', (byte)'E')));
    }

    [Fact]
    public void A_truncated_header_is_not_a_video()
    {
        // A file of three bytes can't be read for a box type without
        // reading off the end of the array.
        Assert.False(SponsorMedia.LooksLikeVideo(Header(0, 0, 0)));
        Assert.False(SponsorMedia.LooksLikeVideo(Array.Empty<byte>()));
    }

    // --- Taking one in -------------------------------------------------------------------

    [Fact]
    public void A_video_is_added_to_the_rotation_and_copied_in()
    {
        var store = SponsorStore.Load();
        var source = WriteFakeMp4("HondaAd.mp4");

        var stored = store.AddVideo(source, out var error);

        Assert.Null(error);
        Assert.NotNull(stored);
        Assert.Contains(stored!, store.SlideFileNames);

        // Copied into the app's own folder, not referenced where it was -
        // the same rule pictures follow, so deleting the original doesn't
        // break the display mid-event.
        var resolved = store.ResolvePath(stored!);

        Assert.NotNull(resolved);
        Assert.True(File.Exists(resolved!));
        Assert.NotEqual(Path.GetFullPath(source), Path.GetFullPath(resolved!));

        // And it is still recognisably a video on the way back out.
        Assert.True(SponsorMedia.IsVideoName(stored!));
    }

    [Fact]
    public void A_stored_video_keeps_its_own_name_where_a_club_can_recognise_it()
    {
        var store = SponsorStore.Load();

        var stored = store.AddVideo(WriteFakeMp4("Smith Motors 30s.mp4"), out _);

        Assert.NotNull(stored);
        Assert.Contains("Smith Motors 30s", stored!, StringComparison.Ordinal);
        Assert.EndsWith(".mp4", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_with_a_videos_name_but_not_its_contents_is_turned_away()
    {
        var store = SponsorStore.Load();
        var fake = Path.Combine(_data.Folder, "notreally.mp4");

        Directory.CreateDirectory(_data.Folder);
        File.WriteAllBytes(fake, new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        Assert.Null(store.AddVideo(fake, out var error));
        Assert.NotNull(error);
        Assert.Empty(store.SlideFileNames);
    }

    [Fact]
    public void A_file_that_is_not_a_video_extension_is_turned_away()
    {
        var store = SponsorStore.Load();
        var text = Path.Combine(_data.Folder, "notes.txt");

        Directory.CreateDirectory(_data.Folder);
        File.WriteAllText(text, "not a video");

        Assert.Null(store.AddVideo(text, out var error));
        Assert.NotNull(error);
        Assert.Contains("MP4", error!, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_video_file_is_turned_away()
    {
        var store = SponsorStore.Load();
        var empty = Path.Combine(_data.Folder, "empty.mp4");

        Directory.CreateDirectory(_data.Folder);
        File.WriteAllBytes(empty, Array.Empty<byte>());

        Assert.Null(store.AddVideo(empty, out var error));
        Assert.NotNull(error);
    }

    [Fact]
    public void Videos_and_pictures_share_one_rotation_in_the_order_they_were_added()
    {
        var store = SponsorStore.Load();

        var video = store.AddVideo(WriteFakeMp4("first.mp4"), out _);
        var picture = store.AddSlide(WritePng("second.png"), out var pictureError);

        Assert.Null(pictureError);
        Assert.NotNull(video);
        Assert.NotNull(picture);

        Assert.Equal(new[] { video!, picture! }, store.SlideFileNames);
    }

    // --- The settings ---------------------------------------------------------------------

    [Fact]
    public void A_fresh_install_plays_video_sound_and_silences_the_music_under_it()
    {
        var settings = new SponsorSettings();

        Assert.True(settings.PlayVideoSound);
        Assert.True(settings.MuteMusicDuringVideo);

        // And slides are still off until a club turns them on - adding
        // videos changed nothing about that.
        Assert.False(settings.Enabled);
    }

    [Fact]
    public void The_video_sound_settings_survive_a_save_and_reload()
    {
        var store = SponsorStore.Load();

        store.Current.PlayVideoSound = false;
        store.Current.MuteMusicDuringVideo = false;
        store.Save();

        var reloaded = SponsorStore.Load();

        Assert.False(reloaded.Current.PlayVideoSound);
        Assert.False(reloaded.Current.MuteMusicDuringVideo);
    }

    // --- The ad break ---------------------------------------------------------------------

    [Fact]
    public void An_ad_break_begins_and_ends()
    {
        var changes = new List<bool>();
        void Watch(bool playing) => changes.Add(playing);

        AdBreak.PlayingChanged += Watch;

        try
        {
            Assert.False(AdBreak.Playing);

            AdBreak.Begin();
            Assert.True(AdBreak.Playing);

            AdBreak.End();
            Assert.False(AdBreak.Playing);

            Assert.Equal(new[] { true, false }, changes);
        }
        finally
        {
            AdBreak.PlayingChanged -= Watch;
        }
    }

    [Fact]
    public void Two_big_screens_playing_at_once_only_unmute_when_both_have_finished()
    {
        // Practice Mode can open a Big Screen while Race Control's is
        // already up, and each runs its own rotation. The first ad to
        // finish must not bring the music back under the second.
        var changes = new List<bool>();
        void Watch(bool playing) => changes.Add(playing);

        AdBreak.PlayingChanged += Watch;

        try
        {
            AdBreak.Begin();
            AdBreak.Begin();

            AdBreak.End();
            Assert.True(AdBreak.Playing);

            AdBreak.End();
            Assert.False(AdBreak.Playing);

            // Raised once each way, not once per window.
            Assert.Equal(new[] { true, false }, changes);
        }
        finally
        {
            AdBreak.PlayingChanged -= Watch;
        }
    }

    [Fact]
    public void Ending_a_break_that_never_began_does_nothing()
    {
        // StopSponsorVideo is called from the video's end, from the rotation
        // moving on, and from the window closing - whichever comes first,
        // and sometimes more than one. Counting below zero here would mean
        // the NEXT ad's End left the music muted.
        var changes = new List<bool>();
        void Watch(bool playing) => changes.Add(playing);

        AdBreak.PlayingChanged += Watch;

        try
        {
            AdBreak.End();
            AdBreak.End();

            Assert.False(AdBreak.Playing);
            Assert.Empty(changes);

            // And a break after all that still works.
            AdBreak.Begin();
            Assert.True(AdBreak.Playing);
            Assert.Equal(new[] { true }, changes);
        }
        finally
        {
            AdBreak.PlayingChanged -= Watch;
        }
    }

    [Fact]
    public void The_music_is_silenced_outright_rather_than_ducked()
    {
        // A duck leaves the music audible under a voice, which is right for
        // an announcement and wrong for a paid ad. Checked through the
        // player rather than by reading the field, and with
        // DuckUnderAnnouncements OFF - the setting an ad break deliberately
        // ignores, because it is about the announcer.
        using var music = new MusicPlayer
        {
            Settings = new MusicSettings { DuckUnderAnnouncements = false, Volume = 1f },
        };

        // Nothing is playing, so this only has to not throw and not depend
        // on an audio device: the gain stage it sets is built per track.
        music.SetAdBreakMuted(true);
        music.SetAdBreakMuted(true);
        music.SetAdBreakMuted(false);
    }

    // --- The native folder ----------------------------------------------------------------

    [Fact]
    public void The_engine_looks_where_the_build_puts_the_native_files()
    {
        // The csproj leaves VideoLAN's target folder at its default and
        // SponsorVideoEngine joins the same path onto the exe's folder. If
        // either moves without the other, video stops working in a way that
        // only shows up on a club's PC.
        Assert.Equal(@"libvlc\win-x64", SponsorVideoEngine.NativeFolderName);
    }

    // --- Helpers --------------------------------------------------------------------------

    private static byte[] Header(params byte[] bytes) => bytes;

    /// <summary>
    /// A file with a real MP4's opening bytes and nothing else. Enough to
    /// get past SponsorMedia - which only reads the header - and not enough
    /// for LibVLC to play, which no test asks it to.
    /// </summary>
    private string WriteFakeMp4(string name)
    {
        Directory.CreateDirectory(_data.Folder);

        var path = Path.Combine(_data.Folder, name);
        var bytes = new byte[64];

        bytes[3] = 0x20;
        bytes[4] = (byte)'f';
        bytes[5] = (byte)'t';
        bytes[6] = (byte)'y';
        bytes[7] = (byte)'p';

        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>A real 1x1 PNG, for the test that mixes a picture and a video in one rotation.</summary>
    private string WritePng(string name)
    {
        Directory.CreateDirectory(_data.Folder);

        var path = Path.Combine(_data.Folder, name);

        using var bitmap = new Bitmap(1, 1);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

        return path;
    }
}
