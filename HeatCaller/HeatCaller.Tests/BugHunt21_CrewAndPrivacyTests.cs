namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 21 (15 Nov 2026), drivers' crews and the privacy register: a
/// junior counts as flagged, a race intro is offered one goal and keeps it
/// only when it says it, the gap before the finals is not the end of the
/// night, and a crew voice is not declined or dropped while the engine's
/// voices are not known.
/// </summary>
public sealed class BugHunt21_CrewAndPrivacyTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------ #3 juniors in the privacy register

    [Fact]
    public void Ticking_junior_republishes_their_saved_results()
    {
        var before = PublicPrivacy.From(new[] { new DriverProfile { Transponder = "T1", DriverName = "Mia Kent" } });
        var after = PublicPrivacy.From(new[] { new DriverProfile { Transponder = "T1", DriverName = "Mia Kent", IsJunior = true } });

        Assert.Equal(new[] { "T1" }, PublicPrivacy.Changed(before, after));
        Assert.Contains("T1", after.FlaggedTransponders);
        Assert.NotEqual(before.FlaggedFingerprint, after.FlaggedFingerprint);

        // And un-ticking it, back again.
        Assert.Equal(new[] { "T1" }, PublicPrivacy.Changed(after, before));
    }

    [Fact]
    public void A_club_with_no_juniors_keeps_the_flagged_fingerprint_it_saved_before()
    {
        // The fingerprint's form for a non-junior is unchanged, so no start-up republish.
        var register = PublicPrivacy.From(new[] { new DriverProfile { Transponder = "T1", DriverName = "Sam Parker", NoPhotoOnline = true } });
        var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("T1|100||\n")));

        Assert.Equal(expected, register.FlaggedFingerprint);
    }

    [Fact]
    public void Deleting_a_juniors_profile_keeps_them_a_junior_online()
    {
        var before = PublicPrivacy.From(new[] { new DriverProfile { Transponder = "T1", DriverName = "Mia Kent", IsJunior = true } });

        var retained = PublicPrivacy.Retain(before, Array.Empty<DriverProfile>(), null, out var changed);

        Assert.True(changed);
        var kept = Assert.Single(retained);
        Assert.True(kept.Junior);

        var after = PublicPrivacy.From(Array.Empty<DriverProfile>(), retained);
        Assert.True(after.JuniorForTransponder("T1"));
        Assert.Empty(PublicPrivacy.Changed(before, after));
        var shown = Assert.Single(RelayPublisher.DisplayNames(new[] { "Mia Kent" }, abbreviate: false, new[] { "T1" }, after));
        Assert.StartsWith("Mia K", shown);
        Assert.DoesNotContain("Kent", shown);
    }

    [Fact]
    public void A_kept_driver_saved_before_juniors_counted_still_loads()
    {
        var old = System.Text.Json.JsonSerializer.Deserialize<PrivacyRetainedDriver>(
            "{\"Transponders\":[\"T1\"],\"Name\":\"Sam Parker\",\"AnnouncerName\":\"\",\"NoPhoto\":true,\"NoVideo\":false,\"Initials\":false}")!;

        Assert.False(old.Junior);
        Assert.True(PublicPrivacy.From(Array.Empty<DriverProfile>(), new[] { old }).NoPhoto("T1"));
    }

    // ------------------------------------------------------------ #4 one goal per intro

    private static (CommentaryService Commentary, DateTime Night) TwoGoals()
    {
        var night = new DateTime(2026, 11, 15, 20, 0, 0);
        var profiles = ProfileStore.Load();
        var tonight = TonightAtTheTrack.NightOf(night);

        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Sam Parker", CrewGoal = "Finish on the podium", CrewGoalNight = tonight }, out _));
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "200", DriverName = "Bill Hart", CrewGoal = "Stay consistent all evening", CrewGoalNight = tonight }, out _));

        return (new CommentaryService(profiles, new AppSettings { DriversPickCrew = true }, null), night);
    }

    [Fact]
    public void An_intro_is_offered_one_goal_and_the_rest_stay_free()
    {
        var (commentary, night) = TwoGoals();

        var (driver, lines) = commentary.IntroGoal(new[] { "100", "200" }, "intro:heat1", night);

        Assert.Equal("100", driver);
        Assert.Contains("podium", lines);
        Assert.DoesNotContain("consistent", lines);

        // Bill's is still there for his principal in another race.
        Assert.NotEmpty(commentary.CrewNoteLines("200", "Bill Hart", CommentaryService.CrewNote.Goal, "principal:heat2", night));

        // And Sam's is not given to anybody else as well.
        Assert.Empty(commentary.CrewNoteLines("100", "Sam Parker", CommentaryService.CrewNote.Goal, "principal:heat1", night));
    }

    [Fact]
    public void A_goal_the_intro_did_not_say_is_handed_back()
    {
        var (commentary, night) = TwoGoals();
        commentary.IntroGoal(new[] { "100" }, "intro:heat1", night);

        commentary.ReleaseNote("100", CommentaryService.CrewNote.Goal, "intro:heat1", night);

        Assert.NotEmpty(commentary.CrewNoteLines("100", "Sam Parker", CommentaryService.CrewNote.Goal, "principal:heat2", night));

        // Only the holder can hand it back.
        commentary.ReleaseNote("100", CommentaryService.CrewNote.Goal, "intro:heat1", night);
        Assert.Empty(commentary.CrewNoteLines("100", "Sam Parker", CommentaryService.CrewNote.Goal, "intro:heat3", night));
    }

    [Fact]
    public void Whether_a_line_says_a_goal()
    {
        Assert.True(CommentaryService.GoalSaid("Sam tells us he wants to finish on the podium tonight.", "Sam Parker", "Finish on the podium"));
        Assert.True(CommentaryService.GoalSaid("Bill says he's after staying consistent all evening.", "Bill Hart", "Stay consistent all evening"));

        // Named, but not the goal.
        Assert.False(CommentaryService.GoalSaid("Sam Parker starts from pole in Heat 1.", "Sam Parker", "Finish on the podium"));

        // The goal's words, but about somebody else.
        Assert.False(CommentaryService.GoalSaid("Bill wants a podium finish.", "Sam Parker", "Finish on the podium"));

        // No line at all.
        Assert.False(CommentaryService.GoalSaid(null, "Sam Parker", "Finish on the podium"));

        // Nothing to tell by: kept, as before.
        Assert.True(CommentaryService.GoalSaid("Sam is here to race.", "Sam Parker", "Top 3"));
    }

    // ------------------------------------------------------------ #5 the gap before the finals

    [Fact]
    public void Mains_still_to_build_are_more_racing_tonight()
    {
        static bool More(ProgramStage stage) => MainForm.ProgrammeStillToRun(new ProgramStep(stage, "h", "d", "a"));

        Assert.True(More(ProgramStage.SeedMains));
        Assert.True(More(ProgramStage.BumpUps));
        Assert.True(More(ProgramStage.RunNext));
        Assert.False(More(ProgramStage.Complete));
        Assert.False(More(ProgramStage.BuildQualifying));
        Assert.False(More(ProgramStage.DayClosed));
        Assert.False(MainForm.ProgrammeStillToRun(null));
    }

    // ------------------------------------------------------------ #16 a pick while the voices are not known

    [Fact]
    public void A_voice_pick_waits_while_the_engines_voices_are_not_listed()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "100", DriverName = "Sam Parker" });
        var store = DriverRequestStore.Load();
        var listed = false;
        var rachel = new CrewVoicePick { Engine = DriverCrew.EngineWord(VoiceEngine.ElevenLabs), Id = "rachel-id", Name = "Rachel" };

        var desk = new DriverRequestDesk(store, new AppSettings { DriversPickCrew = true }, profiles, BatteryPackStore.Load(),
            SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()), new UndoService(), _ => { }, badge => badge == "0123456789abcdef0123" ? "100" : null)
        {
            CrewVoiceOf = key => listed && key == DriverCrew.KeyOf(rachel) ? rachel.Copy() : null,
            CrewVoicesListed = () => listed,
        };

        var ask = new DriverRequest
        {
            Id = "000001" + new string('0', 18),
            Badge = "0123456789abcdef0123",
            Kind = DriverAsk.Crew,
            AskedUtc = DateTime.UtcNow,
            Proof = DriverAsk.ByPin,
        };
        ask.Texts["pVoice"] = DriverCrew.KeyOf(rachel);
        store.FileAll(new[] { ask }, DateTime.UtcNow, out _);

        Assert.Equal(0, desk.FileWaitingCrewVoices());
        Assert.True(store.Get(ask.Id)!.IsWaiting);
        Assert.True(store.Get(ask.Id)!.Texts.ContainsKey("pVoice"));

        listed = true;
        Assert.Equal(1, desk.FileWaitingCrewVoices());
        Assert.Equal("rachel-id", profiles.Get("100")!.CrewPrincipalVoice!.Id);
        Assert.Equal(DriverAsk.Approved, store.Get(ask.Id)!.State);
    }

    // ------------------------------------------------------------ #17 a failed listing after an engine switch

    [Fact]
    public void A_failed_listing_after_a_switch_does_not_check_picks_against_the_old_engines_keys()
    {
        var kokoro = new CrewVoicePick { Engine = DriverCrew.EngineWord(VoiceEngine.Kokoro), Id = "bm_george", Name = "George" };
        var elevenKeys = new HashSet<string>(StringComparer.Ordinal) { "elevenlabs:rachel-id" };

        // Switched to Kokoro; its first listing failed.
        var keys = MainForm.KeysAfterFailedListing(elevenKeys, VoiceEngine.ElevenLabs, VoiceEngine.Kokoro);

        Assert.Null(keys);
        Assert.True(MainForm.CrewVoiceStillOffered(keys, VoiceEngine.Kokoro, kokoro));

        // The same engine failing again keeps what it had.
        Assert.Same(elevenKeys, MainForm.KeysAfterFailedListing(elevenKeys, VoiceEngine.ElevenLabs, VoiceEngine.ElevenLabs));
    }
}
