namespace HeatCaller.Tests;

/// <summary>
/// Driver experience package M (22 Oct 2026): a driver's own mp3 clip on
/// their night (behind their badge) and the jump start carried onto each
/// filed row. Its "no photo" and "no videos" online rules for the podium
/// videos went on 28 Oct 2026, asked for by Asher ("Remove the podium videos
/// for downloading from the relay and just have locally"): the videos stay
/// on this PC, and the checks below now hold that they do.
/// </summary>
public sealed class DriverExp_M_MediaTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

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

    // ------------------------------------------- the videos stay on this PC

    [Fact]
    public void A_settings_file_that_still_says_share_the_podium_videos_loads_as_ever()
    {
        // WHY (28 Oct 2026): SharePodiumVideos left RaceAudioStore's file. A
        // club's race-audio.json from before still carries it, and must load
        // with every other setting kept rather than fall back to defaults.
        RaceAudioStore.OffForTests = false;

        try
        {
            File.WriteAllText(
                _data.PathOf("race-audio.json"),
                "{\"Enabled\":true,\"DriverHighlights\":true,\"HighlightVideos\":2,\"VideoHandle\":\"@acme\","
                + "\"SharePodiumVideos\":true,\"MusicUnderVideos\":false,\"ShareDriverClips\":true}");

            var store = RaceAudioStore.Load();

            Assert.False(store.LoadedUnreadable);
            Assert.Equal(HighlightVideo.Who.Podium, store.HighlightVideos);
            Assert.Equal("@acme", store.VideoHandle);
            Assert.False(store.MusicUnderVideos);
            Assert.True(store.ShareDriverClips);

            // And it is not written back.
            store.VideoHandle = "@acme-rc";
            Assert.DoesNotContain("SharePodiumVideos", File.ReadAllText(_data.PathOf("race-audio.json")), StringComparison.Ordinal);
        }
        finally
        {
            RaceAudioStore.OffForTests = true;
        }
    }

    [Fact]
    public void Nothing_uploads_a_podium_video_or_shows_its_code()
    {
        foreach (var gone in new[] { "RelayPublisher.Videos.cs", "PodiumVideoQr.cs", "RelayPublisher.Privacy.cs" })
        {
            Assert.False(HasSource(gone), $"{gone} should be gone.");
        }

        foreach (var file in new[] { "RaceAudioRecorder.cs", "RaceControlControl.AfterRace.cs", "BigScreenForm.cs", "RecordingControl.cs", "MainForm.NightEmail.cs", "MainForm.Privacy.cs", "RelayPublisher.Uploads.cs", "RelayPublisher.Clips.cs" })
        {
            var source = SourceOf(file);

            foreach (var leftover in new[] { "UploadPodiumVideo", "PodiumVideosPageUrl", "PodiumVideoQr ", "ShowPodiumVideos(", "SharePodiumVideos =", "/video/", "forget-media\"" })
            {
                Assert.DoesNotContain(leftover, source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void A_video_is_made_and_kept_in_the_highlights_folder_and_goes_nowhere_else()
    {
        // An expression body, so taken up to the next method rather than by braces.
        var recorder = SourceOf("RaceAudioRecorder.cs");
        var from = recorder.IndexOf("private static bool MakeVideo(", StringComparison.Ordinal);
        var to = recorder.IndexOf("private static string? MakeVideoFile(", StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "MakeVideo and MakeVideoFile are no longer where they were.");
        var make = recorder[from..to];

        Assert.Contains("MakeVideoFile(", make, StringComparison.Ordinal);
        Assert.DoesNotContain("Upload", make, StringComparison.Ordinal);
        Assert.DoesNotContain("Publisher", make, StringComparison.Ordinal);
    }

    [Fact]
    public void The_video_options_draw_each_drivers_own_photo()
    {
        var options = MethodBodyOf(SourceOf("RaceControlControl.AfterRace.cs"), "private HighlightVideoOptions? VideoOptionsFor(");

        Assert.Contains("new HighlightDriver(profile?.PhotoFileName ?? string.Empty", options, StringComparison.Ordinal);
        Assert.DoesNotContain("PhotoFor(", options, StringComparison.Ordinal);
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
        Assert.Contains("_uploadStop ??= new CancellationTokenSource()", source, StringComparison.Ordinal);
        Assert.Contains("new MediaTypeHeaderValue(\"audio/mpeg\")", source, StringComparison.Ordinal);
        Assert.Contains("/api/v1/c/{club}/clip/{NewUploadKey()}?{query}", source, StringComparison.Ordinal);
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

    private static bool HasSource(string fileName)
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (Directory.Exists(Path.Combine(folder.FullName, "HeatCaller", "HeatCaller")))
            {
                return File.Exists(Path.Combine(folder.FullName, "HeatCaller", "HeatCaller", fileName));
            }
        }

        throw new DirectoryNotFoundException($"No HeatCaller\\HeatCaller above {AppContext.BaseDirectory}.");
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
