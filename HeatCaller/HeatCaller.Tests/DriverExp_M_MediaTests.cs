namespace HeatCaller.Tests;

/// <summary>
/// Driver experience package M (22 Oct 2026): a driver's own mp3 clip on
/// their night (behind their badge), "no photo" and "no videos" online for
/// the podium videos, and the jump start carried onto each filed row.
/// </summary>
public sealed class DriverExp_M_MediaTests
{
    private const string Secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static DriverProfile Profile(string transponder, string name, bool rental = false, bool noPhoto = false, bool noVideo = false) => new()
    {
        Transponder = transponder,
        DriverName = name,
        IsRental = rental,
        NoPhotoOnline = noPhoto,
        NoVideoOnline = noVideo,
    };

    private static SessionResultEntry Row(string transponder, int position) =>
        new() { Transponder = transponder, Position = position, DriverName = "Row " + transponder };

    // ------------------------------------------------------------ badge ids

    [Fact]
    public void Badge_ids_are_the_check_in_ids_for_named_real_drivers_only()
    {
        var profiles = new Dictionary<string, DriverProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["T1"] = Profile("T1", "Sam Parker"),
            ["T2"] = Profile("T2", "Rental Three", rental: true),
            ["T3"] = Profile("T3", "   "),
        };

        var results = new[]
        {
            Row("T1", 1),
            Row("T2", 2),
            Row("T3", 3),
            Row("T4", 4),
            Row(Ghosts.Prefix + "1", 5),
            Row("", 6),
            Row("t1", 7),
        };

        var ids = ClipShare.DriverIdsFor(results, t => profiles.TryGetValue(t, out var p) ? p : null, Secret);

        Assert.Equal(RelayCheckIn.IdFor("T1", Secret), Assert.Single(ids).Value);
        Assert.True(ids.ContainsKey("T1"));
        Assert.True(DriverNight.IsADriverId(ids["T1"]));
    }

    [Fact]
    public void Without_a_check_in_key_nobody_gets_a_badge_id()
    {
        var sam = Profile("T1", "Sam Parker");

        Assert.Empty(ClipShare.DriverIdsFor(new[] { Row("T1", 1) }, _ => sam, ""));
        Assert.Empty(ClipShare.DriverIdsFor(new[] { Row("T1", 1) }, _ => sam, null));
    }

    // ------------------------------------------------------------ the query

    [Fact]
    public void A_clip_goes_up_with_the_badge_label_and_race_and_never_a_transponder()
    {
        var badge = RelayCheckIn.IdFor("T1", Secret);
        var race = Guid.NewGuid().ToString("N");

        var query = RelayPublisher.ClipQuery(badge, "Q1 Heat 2 (Stock & Mod)", race);

        Assert.NotNull(query);
        Assert.Contains("driver=" + badge, query!, StringComparison.Ordinal);
        Assert.Contains("label=Q1%20Heat%202%20%28Stock%20%26%20Mod%29", query!, StringComparison.Ordinal);
        Assert.Contains("race=" + race, query!, StringComparison.Ordinal);
        Assert.DoesNotContain("T1", query!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("T1")]
    [InlineData("ABCDEF0123456789ABCD")]
    [InlineData("0123456789abcdef012")]
    public void A_clip_without_a_badge_id_is_not_sent(string? driverId)
    {
        Assert.Null(RelayPublisher.ClipQuery(driverId, "A-Main", null));
    }

    [Fact]
    public void A_race_id_the_relay_would_refuse_is_left_out_and_a_long_label_is_cut()
    {
        var badge = RelayCheckIn.IdFor("T1", Secret);

        var query = RelayPublisher.ClipQuery(badge, new string('a', 200), "not-a-session-id");

        Assert.NotNull(query);
        Assert.DoesNotContain("race=", query!, StringComparison.Ordinal);
        Assert.Contains("label=" + new string('a', 80), query!, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', 81), query!, StringComparison.Ordinal);
    }

    // ------------------------------------------------------- which clips go

    [Fact]
    public void Only_mp3s_of_drivers_with_a_badge_id_are_shared()
    {
        static RaceHighlights.DriverClip Clip(string transponder, int position) =>
            new(transponder, "Driver " + transponder, position, new List<RaceHighlights.Window>(), new List<RaceHighlights.Moment>());

        var encoded = new List<(RaceHighlights.DriverClip Clip, string Mp3)>
        {
            (Clip("T1", 1), @"C:\night\Highlights\1st Sam.mp3"),
            (Clip("T2", 2), @"C:\night\Highlights\2nd Alex.mp3"),
            (Clip("T1", 1), @"C:\night\Highlights\1st Sam.wav"),
            (Clip("", 3), @"C:\night\Highlights\3rd.mp3"),
        };

        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["T1"] = RelayCheckIn.IdFor("T1", Secret) };

        var shared = RaceAudioRecorder.ClipsToShare(encoded, ids);

        var only = Assert.Single(shared);
        Assert.Equal("T1", only.Clip.Transponder);
        Assert.EndsWith(".mp3", only.Mp3);
    }

    // -------------------------------------------------- photo and video

    [Fact]
    public void No_videos_online_is_read_from_the_privacy_register()
    {
        var register = PublicPrivacy.From(new[] { Profile("T1", "Sam Parker", noVideo: true), Profile("T2", "Alex Jones") });

        Assert.True(HighlightShare.NotOnline("T1", register));
        Assert.True(HighlightShare.NotOnline("t1", register));
        Assert.False(HighlightShare.NotOnline("T2", register));
        Assert.False(HighlightShare.NotOnline("nobody", register));
        Assert.False(HighlightShare.NotOnline(null, register));
    }

    [Fact]
    public void No_photo_online_blanks_the_photo_only_on_a_video_that_may_go_up()
    {
        var register = PublicPrivacy.From(new[] { Profile("T1", "Sam Parker", noPhoto: true), Profile("T2", "Alex Jones") });

        // On the podium, shared: drawn without it.
        Assert.Equal(string.Empty, HighlightShare.PhotoFor("sam.jpg", "T1", 1, sharing: true, register));

        // Kept on this PC - not shared, or off the podium so never sent.
        Assert.Equal("sam.jpg", HighlightShare.PhotoFor("sam.jpg", "T1", 1, sharing: false, register));
        Assert.Equal("sam.jpg", HighlightShare.PhotoFor("sam.jpg", "T1", 4, sharing: true, register));

        // Somebody who did not ask keeps theirs.
        Assert.Equal("alex.jpg", HighlightShare.PhotoFor("alex.jpg", "T2", 2, sharing: true, register));
        Assert.Equal(string.Empty, HighlightShare.PhotoFor(null, "T2", 2, sharing: true, register));
    }

    // ------------------------------------------------- the wiring, by source
    //
    // Race Control and the recorder need a sound card, a relay and a race
    // to run, so the wiring is pinned by reading the source, as the audio
    // tests do.

    [Fact]
    public void Filing_passes_who_jumped_the_start()
    {
        // The filing moved out of Race Control into RaceFilingService
        // (23 Oct 2026, timing layer, P2), word for word.
        var source = SourceOf("RaceFilingService.cs");

        // And threaded, from the race as it finished (P4b): the frozen view's
        // jump starters, falling back to the live race's inline.
        Assert.Contains("RaceFiling.ResultsFrom(racers, _profiles, finished is null ? _race.HasJumpStarted : finished.HasJumpStarted)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RaceFiling.ResultsFrom(racers, _profiles);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_no_video_driver_is_checked_before_the_podium_upload()
    {
        var share = MethodBodyOf(SourceOf("RaceAudioRecorder.cs"), "private static void ShareOnline(");

        var check = share.IndexOf("HighlightShare.NotOnline(clip.Transponder)", StringComparison.Ordinal);
        var upload = share.IndexOf("UploadPodiumVideoAsync(", StringComparison.Ordinal);

        Assert.True(check >= 0 && upload > check, "The no-video check has to come before the upload.");
    }

    [Fact]
    public void The_video_options_draw_photos_through_the_privacy_rule()
    {
        var options = MethodBodyOf(SourceOf("RaceControlControl.AfterRace.cs"), "private HighlightVideoOptions? VideoOptionsFor(");

        Assert.Contains("HighlightShare.PhotoFor(profile?.PhotoFileName, row.Transponder, row.Position, share is not null)", options, StringComparison.Ordinal);
        Assert.DoesNotContain("new HighlightDriver(profile?.PhotoFileName", options, StringComparison.Ordinal);
    }

    [Fact]
    public void Clips_go_up_after_every_clip_and_video_and_only_when_shared()
    {
        var write = MethodBodyOf(SourceOf("RaceAudioRecorder.cs"), "private static void WriteClips(");

        var videos = write.IndexOf("MakeVideo(", StringComparison.Ordinal);
        var clips = write.IndexOf("ShareClips(encoded, clipShare, log)", StringComparison.Ordinal);

        Assert.True(videos >= 0 && clips > videos, "The drivers' clips go up after the videos.");
        Assert.Contains("if (clipShare is not null && encoded.Count > 0)", write, StringComparison.Ordinal);

        var after = MethodBodyOf(SourceOf("RaceControlControl.AfterRace.cs"), "private ClipShare? ClipShareFor(");

        Assert.Contains("!store.ShareDriverClips", after, StringComparison.Ordinal);
        Assert.Contains("!publisher.CanShareDriverClips", after, StringComparison.Ordinal);
    }

    [Fact]
    public void Clips_need_drivers_nights_and_go_up_as_audio_on_the_quiesced_upload_path()
    {
        var source = SourceOf("RelayPublisher.Clips.cs");

        Assert.Contains("&& _settings.PublishDriverNightsToTheRelay", source, StringComparison.Ordinal);
        Assert.Contains("RelayCheckInSecret", source, StringComparison.Ordinal);
        Assert.Contains("_videoUploadStop ??= new CancellationTokenSource()", source, StringComparison.Ordinal);
        Assert.Contains("new MediaTypeHeaderValue(\"audio/mpeg\")", source, StringComparison.Ordinal);
        Assert.Contains("/api/v1/c/{club}/clip/{NewVideoKey()}?{query}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("transponder", MethodBodyOf(source, "internal static string? ClipQuery("), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_recording_page_has_the_share_clips_tick_with_its_badge_hint()
    {
        var source = SourceOf("RecordingControl.cs");

        Assert.Contains("_chkShareClips.Checked = _audio.ShareDriverClips;", source, StringComparison.Ordinal);
        Assert.Contains("_audio.ShareDriverClips = _chkShareClips.Checked", source, StringComparison.Ordinal);
        Assert.Contains("Only for that driver", source, StringComparison.Ordinal);
        Assert.Contains("only their badge code opens", source, StringComparison.Ordinal);
        Assert.Contains("_chkShareClips.Enabled = clips;", source, StringComparison.Ordinal);
    }

    private static string SourceOf(string fileName)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);

        while (folder is not null)
        {
            var candidate = Path.Combine(folder.FullName, "HeatCaller", "HeatCaller", fileName);

            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            folder = folder.Parent;
        }

        throw new FileNotFoundException($"No HeatCaller\\HeatCaller\\{fileName} above {AppContext.BaseDirectory}.");
    }

    private static string MethodBodyOf(string source, string signature)
    {
        var at = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, $"\"{signature}\" is no longer in the source.");

        var open = source.IndexOf('{', at);
        Assert.True(open >= 0, $"\"{signature}\" has no body.");

        var depth = 0;

        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;

                if (depth == 0)
                {
                    return source[open..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"\"{signature}\" has no matching closing brace.");
    }
}
