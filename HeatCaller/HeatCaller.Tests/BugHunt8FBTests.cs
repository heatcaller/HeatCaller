namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8, group FB - the announcer (17 Oct 2026).
///
/// The voice queue's kept call and its once-per-race claim, what Stop and
/// mute bin, lines that went out on the listen-along page, a director that
/// kept writing while muted, a horizon rate taken across a pass, recorded
/// takes in a voice whose consent was taken back, rivalry wording under the
/// HeatCaller voice, and the Mixer's float-to-decimal cast.
///
/// Where the fix is in code that needs a window, a sound card or a live
/// announcer, the test reads the source - the same pair of helpers every
/// source-reading test class here keeps.
/// </summary>
public sealed class BugHunt8FBTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    //  A06-1: a kept call goes after the instructions already waiting
    // ------------------------------------------------------------------

    [Fact]
    public void A_kept_call_goes_after_the_instructions_and_race_moments_already_waiting()
    {
        Assert.Equal(0, AnnouncementService.HeldOverSlot(Array.Empty<AnnouncementEventType>()));
        Assert.Equal(0, AnnouncementService.HeldOverSlot(new[] { AnnouncementEventType.LapTime }));
        Assert.Equal(1, AnnouncementService.HeldOverSlot(new[] { AnnouncementEventType.DriverRelease, AnnouncementEventType.LapTime }));
        Assert.Equal(3, AnnouncementService.HeldOverSlot(new[]
        {
            AnnouncementEventType.GridCall,
            AnnouncementEventType.LastLap,
            AnnouncementEventType.PositionReport,
        }));
    }

    // ------------------------------------------------------------------
    //  A06-2 / A06-5: a kept call is claimed; Stop and mute bin the rest
    // ------------------------------------------------------------------

    [Fact]
    public void A_kept_call_counts_as_accepted_and_a_muted_announcer_takes_nothing()
    {
        var enqueue = Between(Source("AnnouncementService.VoiceQueue.cs"), "private bool EnqueueVoiceWork(", "_voiceQueue.Insert(at,");

        Assert.Contains("return kept;", enqueue);
        Assert.Contains("if (Muted)", enqueue);
        Assert.Contains("HeldOverSlot(", enqueue);

        var gate = Between(Source("AnnouncementService.Density.cs"), "private bool RoutineCallGetsTheAir(", "private int WeightOf(");

        Assert.Contains("kept = true;", gate);
    }

    [Fact]
    public void Stop_and_a_reset_bin_the_field_window_and_the_kept_call()
    {
        var source = Source("AnnouncementService.cs");

        var stop = Between(source, "public void StopAllPendingAudio(bool keepPriority = true)", "public void ResetSession()");
        Assert.Contains("BinTheFieldCallsUnderLock();", stop);
        Assert.Contains("ForgetTheHeldOverCall(", stop);

        var reset = source[source.IndexOf("public void ResetSession()", StringComparison.Ordinal)..];
        Assert.Contains("BinTheFieldCallsUnderLock();", reset);
        Assert.Contains("ForgetTheHeldOverCall(", reset);
    }

    // ------------------------------------------------------------------
    //  A06-8 / A06-9: on-screen times are invariant; a sector is said plainly
    // ------------------------------------------------------------------

    [Fact]
    public void A_lap_time_on_screen_does_not_follow_the_pcs_culture()
    {
        Assert.Contains(
            "(ms / 1000.0).ToString(\"0.000\", System.Globalization.CultureInfo.InvariantCulture) + \"s\"",
            Source("AnnouncementService.VoiceQueue.cs"));
    }

    [Fact]
    public void A_sector_time_is_said_plainly_and_shown_as_numerals()
    {
        var extras = AnnouncementService.SectorExtras(2, 2060);

        Assert.Equal(RaceNumbers.Plainly(2060), extras["sectortime"].Item1);
        Assert.Equal("two point zero six seconds", extras["sectortime"].Item1);
        Assert.Equal("2.06s", extras["sectortime"].Item2);
        Assert.Equal("2", extras["sector"].Item2);
    }

    // ------------------------------------------------------------------
    //  A06-10 / A06-11: listen-along
    // ------------------------------------------------------------------

    [Fact]
    public void Level_checks_and_previews_are_not_sent_to_listeners()
    {
        Assert.Contains("bool forListeners = true)", Between(Source("AnnouncementService.cs"), "public void SpeakReminder(", "public sealed record PreparedSpeech("));

        var mixer = Source("MixerControl.cs");
        Assert.Contains("SpeakReminder(\"Announcement level check.\", forListeners: false)", mixer);
        Assert.Contains("over the music and the crowd.\", forListeners: false)", mixer);

        Assert.Contains("(theme.Voice.Stability, theme.Voice.Style), forListeners: false)", Source("SoundscapePreview.cs"));
        Assert.Contains("SpeakReminder(ShowTestLine, showOnScreens: false, forListeners: false)", Source("AnnouncerControl.Director.cs"));
    }

    [Fact]
    public void A_line_sent_to_listeners_rearms_the_failure_notice()
    {
        var offer = Between(Source("AnnouncementService.ListenAlong.cs"), "private bool OfferLineToListeners(", "private byte[]? AsMp3(");

        Assert.Contains("Volatile.Write(ref _listenEncodeComplained, 0);", offer);
    }

    // ------------------------------------------------------------------
    //  A07-1 / A07-3: the director while muted; ghosts in the talking points
    // ------------------------------------------------------------------

    [Fact]
    public void The_director_neither_writes_nor_hands_over_while_muted()
    {
        var source = Source("AnnouncerControl.Director.cs");

        var feed = Between(source, "private void FeedTheDirector()", "private void FeedTheSponsorsOnly(CommentaryDirector director)");
        var muted = feed.LastIndexOf("if (_announcer.Muted)", StringComparison.Ordinal);
        var speak = feed.IndexOf("_ = SpeakNextSegmentAsync(director);", StringComparison.Ordinal);

        Assert.True(muted >= 0 && speak > muted, "A muted announcer is no longer checked before the next segment is written.");

        Assert.Contains("_announcer.Muted", Between(source, "private string? WhyNotNow(SpokenSegment? said)", "private async Task HandOverIfItIsTimeAsync("));

        var handOver = Between(source, "private async Task HandOverIfItIsTimeAsync(", "_handedOver = key;");
        Assert.Contains("_announcer.Muted", handOver);
    }

    [Fact]
    public void The_talking_points_leave_the_ghosts_out()
    {
        var observations = Between(Source("AnnouncerControl.cs"), "private List<string> GenerateObservations()", "private void RefreshLive()");

        Assert.Contains("RacingField(_race.GetOrderedStandings())", observations);
    }

    // ------------------------------------------------------------------
    //  A10-1: a take in a voice whose consent was taken back
    // ------------------------------------------------------------------

    private const string Clone = "clone1";

    private static HeatCallerVoiceClient ConsentFor(DateTime? revoked)
    {
        var settings = new AppSettings
        {
            HeatCallerConsentVoiceId = Clone,
            HeatCallerConsentWho = "A",
            HeatCallerConsentAtUtc = new DateTime(2026, 6, 9, 10, 0, 0, DateTimeKind.Utc),
            HeatCallerConsentRevokedUtc = revoked,
        };

        settings.HeatCallerClonedVoices.Add(Clone);

        return new HeatCallerVoiceClient(settings);
    }

    [Fact]
    public void A_take_recorded_in_a_cloned_voice_stops_when_the_consent_is_taken_back()
    {
        const string line = "GREEN FLAG - WE ARE RACING";

        var folder = Path.Combine(AppPaths.Root, "VoiceLines");

        StingerLibrary.ForTests(folder, TimeSpan.Zero).Save(line, new byte[] { 1, 2, 3 }, "the HeatCaller voice (Dave)", "Club", Clone);

        // Read back from index.json, so the id is proven to survive the file.
        var recorded = StingerLibrary.ForTests(folder, TimeSpan.Zero).TryLoadWithEntry(line);

        Assert.True(recorded.HasValue);

        var entry = recorded.GetValueOrDefault().Entry;

        Assert.Equal(Clone, entry.HeatCallerVoiceId);
        Assert.Equal(new byte[] { 1, 2, 3 }, recorded.GetValueOrDefault().Audio);

        Assert.False(StingerLibrary.TakeAllowed(entry, ConsentFor(revoked: DateTime.UtcNow).ConsentAllows));
        Assert.True(StingerLibrary.TakeAllowed(entry, ConsentFor(revoked: null).ConsentAllows));
    }

    [Fact]
    public void A_take_from_before_the_voice_id_was_stored_is_matched_by_its_name()
    {
        var legacy = new StingerLibrary.Entry("k", "line", "f.audio", "the HeatCaller voice (Dave)", "Club", DateTime.UtcNow);
        var withdrawn = ConsentFor(revoked: DateTime.UtcNow);

        Assert.False(StingerLibrary.TakeAllowed(legacy, withdrawn.ConsentAllows, Clone, "Dave"));

        // Another voice's take, an unnamed one and an ElevenLabs one are not this voice's.
        Assert.True(StingerLibrary.TakeAllowed(legacy, withdrawn.ConsentAllows, Clone, "Ivy"));
        Assert.True(StingerLibrary.TakeAllowed(legacy with { VoiceName = "the HeatCaller voice" }, withdrawn.ConsentAllows, Clone, string.Empty));
        Assert.True(StingerLibrary.TakeAllowed(legacy with { VoiceName = "Pit Lane" }, withdrawn.ConsentAllows, Clone, "Dave"));

        Assert.True(StingerLibrary.TakeAllowed(legacy, ConsentFor(revoked: null).ConsentAllows, Clone, "Dave"));
    }

    // ------------------------------------------------------------------
    //  A11-2: rivalry wording under the HeatCaller voice
    // ------------------------------------------------------------------

    [Fact]
    public void Rivalry_lines_under_the_heatcaller_voice_are_the_standard_ones()
    {
        var check = Between(Source("AnnouncementService.LapCalls.cs"), "private void CheckRivalries(", "private void FireRivalryLine(");

        Assert.Contains("VoiceEngine.HeatCaller", check);
        Assert.Contains("new RivalryLine().Templates", check);
        Assert.DoesNotContain("currentlyAhead, rivalry.Templates)", check);
    }

    // ------------------------------------------------------------------
    //  A13-2: the Mixer's boxes and an absurd stored value
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(1e30f)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(float.NaN)]
    [InlineData(-1e30f)]
    public void A_stored_level_out_of_range_lands_inside_the_box(float stored)
    {
        var value = MixerControl.ToBox(stored, 5f, 100f);

        Assert.InRange(value, 5m, 100m);
    }

    [Fact]
    public void A_stored_level_in_range_is_kept()
    {
        Assert.Equal(40m, MixerControl.ToBox(40f, 5f, 100f));
        Assert.Equal(1.5m, MixerControl.ToBox(1.5f, 1f, 2f));
    }

    // ------------------------------------------------------------------
    //  A15-2: the Connection card is not "above" the Recording tab
    // ------------------------------------------------------------------

    [Fact]
    public void No_message_points_to_a_connection_card_above()
    {
        var folder = Path.Combine(RepoRoot, "HeatCaller", "HeatCaller");

        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.TopDirectoryOnly))
        {
            Assert.DoesNotContain("Connection card above", File.ReadAllText(file));
        }
    }

    // ------------------------------------------------------------------
    //  Reading the source

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

    private static string Between(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);

        Assert.True(end > start, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return source[start..end];
    }
}
