using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// A driver's own crew and broadcast notes (15 Nov 2026, driver crew plan,
/// plans/driver-crew-plan.md): which voice and name speak for them, what
/// their phone is told, a crew request on Driver Requests (voices straight
/// on, words wait for the desk), the filter, and the junior rules.
/// </summary>
public sealed class DriverCrewTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string SamBadge = "0123456789abcdef0123";

    private static readonly SecondVoice ClubPrincipal = new("club-el", "Club EL", "club-hc", "Club HC", "rev1", "bm_lewis", KokoroAlways: false, KokoroStandInVoiceName: "bm_lewis");

    private static CrewVoicePick Pick(VoiceEngine engine, string id, string name = "") =>
        new() { Engine = DriverCrew.EngineWord(engine), Id = id, Name = name.Length > 0 ? name : id };

    private static readonly IReadOnlyCollection<string> None = Array.Empty<string>();

    // ------------------------------------------------------------ the voice

    [Fact]
    public void A_driver_with_no_pick_hears_the_clubs_voice_and_a_club_with_none_the_announcers()
    {
        Assert.Same(ClubPrincipal, DriverCrew.VoiceFor(ClubPrincipal, null, VoiceEngine.ElevenLabs, true, None, None).Voice);

        var blank = new SecondVoice(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        Assert.Null(DriverCrew.VoiceFor(blank, null, VoiceEngine.ElevenLabs, true, None, None).Voice);
    }

    [Fact]
    public void Their_own_pick_swaps_only_that_engines_voice_and_keeps_the_roles_stand_in()
    {
        var club = ClubPrincipal with { KokoroAlways = true };
        var (voice, why) = DriverCrew.VoiceFor(club, Pick(VoiceEngine.ElevenLabs, "rachel-id", "Rachel"), VoiceEngine.ElevenLabs, true, None, None);

        Assert.NotNull(voice);
        Assert.Equal("rachel-id", voice!.ElevenLabsVoiceId);
        Assert.Equal("Rachel", voice.ElevenLabsVoiceName);
        Assert.False(voice.KokoroAlways);
        Assert.Equal("bm_lewis", voice.StandInKokoroVoice);
        Assert.Equal("club-hc", voice.HeatCallerVoice);
        Assert.Contains("Rachel", why);
    }

    [Fact]
    public void A_pick_for_another_engine_a_hidden_one_a_reserved_one_or_drivers_crews_off_give_the_clubs()
    {
        var pick = Pick(VoiceEngine.ElevenLabs, "rachel-id", "Rachel");
        var key = DriverCrew.KeyOf(pick);

        var other = DriverCrew.VoiceFor(ClubPrincipal, pick, VoiceEngine.Kokoro, true, None, None);
        Assert.Same(ClubPrincipal, other.Voice);
        Assert.Contains("another engine", other.Why);

        Assert.Same(ClubPrincipal, DriverCrew.VoiceFor(ClubPrincipal, pick, VoiceEngine.ElevenLabs, true, new[] { key }, None).Voice);
        Assert.Same(ClubPrincipal, DriverCrew.VoiceFor(ClubPrincipal, pick, VoiceEngine.ElevenLabs, true, None, new[] { key }).Voice);
        Assert.Same(ClubPrincipal, DriverCrew.VoiceFor(ClubPrincipal, pick, VoiceEngine.ElevenLabs, false, None, None).Voice);
    }

    [Fact]
    public void The_announcer_and_the_co_hosts_are_reserved_but_the_clubs_principal_is_not()
    {
        var settings = new AppSettings { CoHostElevenLabsVoiceId = "cohost-id", PrincipalElevenLabsVoiceId = "club-el" };
        var announcer = DriverCrew.KeyOf(Pick(VoiceEngine.ElevenLabs, "announcer-id"));

        var reserved = DriverCrew.Reserved(settings, announcer, VoiceEngine.ElevenLabs);

        Assert.Contains(announcer, reserved);
        Assert.Contains(DriverCrew.KeyOf(Pick(VoiceEngine.ElevenLabs, "cohost-id")), reserved);
        Assert.DoesNotContain(DriverCrew.KeyOf(Pick(VoiceEngine.ElevenLabs, "club-el")), reserved);
    }

    [Fact]
    public void A_key_is_twelve_hex_characters_and_never_the_voice_id()
    {
        var key = DriverCrew.KeyOf(Pick(VoiceEngine.ElevenLabs, "21m00Tcm4TlvDq8ikWAM"));

        Assert.Matches("^[0-9a-f]{12}$", key);
        Assert.DoesNotContain("21m00", key);
        Assert.NotEqual(key, DriverCrew.KeyOf(Pick(VoiceEngine.Kokoro, "21m00Tcm4TlvDq8ikWAM")));
    }

    // ------------------------------------------------------------ the name

    [Theory]
    [InlineData("Gravel Jones", true)]
    [InlineData("Ana-María O'Neil", true)]
    [InlineData("Dr. Zed", true)]
    [InlineData("R2D2", false)]
    [InlineData("<facts>", false)]
    [InlineData("", false)]
    [InlineData("Abcdefghijklmnopqrstuvwxy", false)]
    public void A_crew_name_is_letters_spaces_hyphens_and_apostrophes(string name, bool ok) =>
        Assert.Equal(ok, DriverCrew.IsAName(name));

    [Fact]
    public void Their_own_name_first_then_the_clubs_and_a_name_that_is_not_one_is_passed_over()
    {
        Assert.Equal("Gravel", DriverCrew.NameFor("  Gravel ", "Toto"));
        Assert.Equal("Toto", DriverCrew.NameFor("R2D2", "Toto"));
        Assert.Equal("Toto", DriverCrew.NameFor("", "Toto"));
        Assert.Equal(string.Empty, DriverCrew.NameFor(null, null));
    }

    // ------------------------------------------------------------ notes

    [Fact]
    public void Tonights_goal_is_only_tonights()
    {
        var now = new DateTime(2026, 11, 15, 20, 0, 0);
        var profile = new DriverProfile { Transponder = "1", CrewGoal = "A top three", CrewGoalNight = TonightAtTheTrack.NightOf(now) };

        Assert.Equal("A top three", DriverCrew.GoalTonight(profile, now));
        Assert.Equal("A top three", DriverCrew.GoalTonight(profile, now.AddHours(5)));
        Assert.Equal(string.Empty, DriverCrew.GoalTonight(profile, now.AddDays(7)));
    }

    [Fact]
    public void A_juniors_notes_are_kept_off_a_streamed_night_unless_the_club_says_so()
    {
        var junior = new DriverProfile { Transponder = "1", IsJunior = true, CrewNotesParentConsent = true };
        var adult = new DriverProfile { Transponder = "2" };
        var noConsent = new DriverProfile { Transponder = "3", IsJunior = true };

        // No parent's consent on the profile: never, stream or not.
        Assert.False(DriverCrew.OwnWordsMayBeSaid(noConsent, streamOn: false, juniorNotesOnStream: true));

        Assert.False(DriverCrew.OwnWordsMayBeSaid(junior, streamOn: true, juniorNotesOnStream: false));
        Assert.True(DriverCrew.OwnWordsMayBeSaid(junior, streamOn: true, juniorNotesOnStream: true));
        Assert.True(DriverCrew.OwnWordsMayBeSaid(junior, streamOn: false, juniorNotesOnStream: false));
        Assert.True(DriverCrew.OwnWordsMayBeSaid(adult, streamOn: true, juniorNotesOnStream: false));
    }

    [Fact]
    public void A_note_naming_another_driver_is_found_by_whole_words()
    {
        var others = new[] { "Bill Hart", "Al" };

        Assert.Equal("Bill Hart", DriverCrew.NamesAnotherDriver("beat bill hart tonight", others));
        Assert.Null(DriverCrew.NamesAnotherDriver("billboard heart", others));
        Assert.Null(DriverCrew.NamesAnotherDriver("all good", others));
    }

    [Fact]
    public void Notes_reach_the_prompt_as_the_drivers_own_words_and_not_when_crews_are_off()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile
        {
            Transponder = "100",
            DriverName = "Sam",
            CrewThanks = "Dad's \"workshop\"",
            CrewCarStory = "Built over winter",
        }, out _));

        var settings = new AppSettings { DriversPickCrew = true };
        var commentary = new CommentaryService(profiles, settings, null);

        var lines = commentary.CrewNoteLines("100", "Sam", CommentaryService.CrewNote.Thanks, "principal:a");
        Assert.Contains(CommentaryService.OwnWordsLead, lines);
        Assert.Contains("Dad's 'workshop'", lines);
        Assert.DoesNotContain("winter", lines);
        Assert.NotEmpty(CommentaryService.CrewNotesRule(lines));

        settings.DriversPickCrew = false;
        Assert.Equal(string.Empty, commentary.CrewNoteLines("100", "Sam", CommentaryService.CrewNote.Thanks, "principal:a"));
        Assert.Equal(string.Empty, CommentaryService.CrewNotesRule(string.Empty));
    }

    [Fact]
    public void A_juniors_notes_are_left_out_of_the_prompt_when_the_stream_is_on()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "200", DriverName = "Mia", IsJunior = true, CrewNotesParentConsent = true, CrewThanks = "My nan" }, out _));

        var commentary = new CommentaryService(profiles, new AppSettings { DriversPickCrew = true }, null) { StreamIsOn = () => true };

        Assert.Equal(string.Empty, commentary.CrewNoteLines("200", "Mia", CommentaryService.CrewNote.Thanks, "principal:a"));
        Assert.Contains("junior", commentary.JuniorRule("200"));
    }

    [Fact]
    public void A_juniors_principal_comes_in_warm_or_plain()
    {
        Assert.Equal(PrincipalStance.Plain, PrincipalStance.ForJunior(PrincipalStance.Defensive));
        Assert.Equal(PrincipalStance.Plain, PrincipalStance.ForJunior(PrincipalStance.Frustrated));
        Assert.Equal(PrincipalStance.Plain, PrincipalStance.ForJunior(PrincipalStance.Blunt));
        Assert.Equal(PrincipalStance.Pleased, PrincipalStance.ForJunior(PrincipalStance.Pleased));
        Assert.Equal(PrincipalStance.Vindicated, PrincipalStance.ForJunior(PrincipalStance.Vindicated));
    }

    // ------------------------------------------------------------ the phone

    [Fact]
    public void The_phone_gets_the_voices_once_and_a_pick_on_another_engine_asks_them_to_pick_again()
    {
        var rachel = Pick(VoiceEngine.ElevenLabs, "rachel-id", "Rachel");
        var crew = new DriverLive.CrewForPhone(VoiceEngine.ElevenLabs, new[]
        {
            new CrewVoiceOption(rachel, "https://storage.googleapis.com/x.mp3"),
            new CrewVoiceOption(Pick(VoiceEngine.ElevenLabs, "adam-id", "Adam"), "http://insecure/x.mp3"),
        });

        var sam = new DriverProfile
        {
            Transponder = "100",
            DriverName = "Sam",
            CrewPrincipalName = "Gravel",
            CrewPrincipalVoice = rachel,
            CrewTechVoice = Pick(VoiceEngine.Kokoro, "am_eric"),
        };

        var json = DriverLive.MeDocument(new[] { sam }, _ => SamBadge, crew: crew);
        using var doc = JsonDocument.Parse(json);

        var voices = doc.RootElement.GetProperty("crewVoices");
        Assert.Equal(2, voices.GetArrayLength());
        Assert.Equal(DriverCrew.KeyOf(rachel), voices[0].GetProperty("k").GetString());
        Assert.True(voices[0].TryGetProperty("s", out _));
        Assert.False(voices[1].TryGetProperty("s", out _));

        var mine = doc.RootElement.GetProperty("drivers").GetProperty(SamBadge).GetProperty("crew");
        Assert.Equal("Gravel", mine.GetProperty("principal").GetProperty("name").GetString());
        Assert.Equal(DriverCrew.KeyOf(rachel), mine.GetProperty("principal").GetProperty("voice").GetString());
        Assert.Equal(string.Empty, mine.GetProperty("tech").GetProperty("voice").GetString());
        Assert.True(mine.GetProperty("repick").GetBoolean());
        Assert.False(mine.TryGetProperty("voices", out _));

        // Drivers' crews off: nothing about a crew at all.
        using var off = JsonDocument.Parse(DriverLive.MeDocument(new[] { sam }, _ => SamBadge));
        Assert.False(off.RootElement.TryGetProperty("crewVoices", out _));
        Assert.False(off.RootElement.GetProperty("drivers").GetProperty(SamBadge).TryGetProperty("crew", out _));
    }

    [Fact]
    public void Hiding_voices_keeps_ones_hidden_that_are_not_on_the_list_now()
    {
        var a = new CrewVoiceOption(Pick(VoiceEngine.ElevenLabs, "a"), string.Empty);
        var b = new CrewVoiceOption(Pick(VoiceEngine.ElevenLabs, "b"), string.Empty);
        var elsewhere = DriverCrew.KeyOf(Pick(VoiceEngine.Kokoro, "am_eric"));

        var hidden = CrewVoicesForm.HiddenAfter(new[] { a, b }, new HashSet<string> { DriverCrew.KeyOf(a.Pick) }, new[] { elsewhere, DriverCrew.KeyOf(a.Pick) });

        Assert.Contains(elsewhere, hidden);
        Assert.Contains(DriverCrew.KeyOf(b.Pick), hidden);
        Assert.DoesNotContain(DriverCrew.KeyOf(a.Pick), hidden);
    }

    // ------------------------------------------------------------ Driver Requests

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();

        public DriverRequestStore Store { get; } = DriverRequestStore.Load();

        public AppSettings Settings { get; } = new() { DriversPickCrew = true };

        public Dictionary<string, CrewVoicePick> Offered { get; } = new();

        public DriverRequestDesk Desk { get; }

        public World(bool junior = false, JuniorAge age = JuniorAge.NotSet)
        {
            // Upsert: a second world in one test loads the first one's drivers from disk.
            Profiles.Upsert(new DriverProfile { Transponder = "100", DriverName = "Sam Parker", IsJunior = junior, JuniorAge = age });
            Profiles.Upsert(new DriverProfile { Transponder = "500", DriverName = "Bill Hart" });

            Desk = new DriverRequestDesk(Store, Settings, Profiles, BatteryPackStore.Load(), SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()),
                new UndoService(), _ => { }, badge => badge == SamBadge ? "100" : null)
            {
                CrewVoiceOf = key => Offered.TryGetValue(key, out var pick) ? pick.Copy() : null,
            };
        }

        public DriverRequest File(int n, Action<DriverRequest> fill, DateTime? at = null, bool pin = true)
        {
            var ask = new DriverRequest
            {
                Id = n.ToString("x6") + new string('0', 18),
                Badge = SamBadge,
                Kind = DriverAsk.Crew,
                AskedUtc = at ?? DateTime.UtcNow,
                Proof = pin ? DriverAsk.ByPin : DriverAsk.ByBadge,
            };
            fill(ask);
            Store.FileAll(new[] { ask }, DateTime.UtcNow, out _);
            return Store.Get(ask.Id)!;
        }
    }

    [Fact]
    public void The_inbox_reads_a_crew_request_with_its_whole_car_story()
    {
        var json = JsonSerializer.Serialize(new
        {
            ok = true,
            items = new object[]
            {
                new
                {
                    id = new string('1', 24), b = SamBadge, k = "crew", v = "pin", at = "2026-11-15T19:40:10.000Z",
                    f = new Dictionary<string, object> { ["pName"] = "Gravel", ["pVoice"] = "abcdef012345", ["story"] = new string('s', 120), ["guardian"] = true },
                },
            },
        });

        var ask = Assert.Single(DriverRequestStore.ParseInbox(json, DateTime.UtcNow, out _));

        Assert.Equal(DriverAsk.Crew, ask.Kind);
        Assert.Equal(120, ask.Text("story").Length);
        Assert.Equal("abcdef012345", ask.Text("pVoice"));
        Assert.True(ask.Guardian);
        Assert.Equal("Crew and notes", DriverRequestDesk.KindWords(DriverAsk.Crew));
    }

    [Fact]
    public void A_voice_only_request_applies_by_itself_and_is_answered_applied()
    {
        var w = new World();
        var rachel = Pick(VoiceEngine.ElevenLabs, "rachel-id", "Rachel");
        w.Offered[DriverCrew.KeyOf(rachel)] = rachel;

        var ask = w.File(1, r => r.Texts["pVoice"] = DriverCrew.KeyOf(rachel));

        Assert.Equal(1, w.Desk.FileWaitingCrewVoices());

        Assert.Equal("rachel-id", w.Profiles.Get("100")!.CrewPrincipalVoice!.Id);
        var answered = w.Store.Get(ask.Id)!;
        Assert.Equal(DriverAsk.Approved, answered.State);
        Assert.Equal(DriverRequestDesk.CrewVoiceAppliedSaid, answered.Said);
    }

    [Fact]
    public void A_mixed_request_applies_the_voice_and_leaves_the_words_waiting_for_the_desk()
    {
        var w = new World();
        var rachel = Pick(VoiceEngine.ElevenLabs, "rachel-id", "Rachel");
        w.Offered[DriverCrew.KeyOf(rachel)] = rachel;

        var ask = w.File(1, r =>
        {
            r.Texts["pVoice"] = DriverCrew.KeyOf(rachel);
            r.Texts["pName"] = "Gravel Jones";
        });

        w.Desk.FileWaitingCrewVoices();

        var profile = w.Profiles.Get("100")!;
        Assert.Equal("rachel-id", profile.CrewPrincipalVoice!.Id);
        Assert.Equal(string.Empty, profile.CrewPrincipalName);

        var waiting = w.Store.Get(ask.Id)!;
        Assert.True(waiting.IsWaiting);
        Assert.False(waiting.Texts.ContainsKey("pVoice"));

        Assert.True(w.Desk.ApplyCrew(waiting, out var refusal), refusal);
        Assert.Equal("Gravel Jones", w.Profiles.Get("100")!.CrewPrincipalName);
        Assert.Equal(DriverAsk.Approved, w.Store.Get(ask.Id)!.State);
    }

    [Fact]
    public void A_voice_no_longer_offered_is_declined_with_a_reason()
    {
        var w = new World();
        var ask = w.File(1, r => r.Texts["tVoice"] = "abcdef012345");

        w.Desk.FileWaitingCrewVoices();

        var answered = w.Store.Get(ask.Id)!;
        Assert.Equal(DriverAsk.Declined, answered.State);
        Assert.Equal(DriverRequestDesk.CrewVoiceGoneSaid, answered.Said);
        Assert.Null(w.Profiles.Get("100")!.CrewTechVoice);
    }

    [Fact]
    public void A_newer_crew_request_takes_in_the_older_one()
    {
        var w = new World();
        var now = DateTime.UtcNow;
        var older = w.File(1, r => r.Texts["pName"] = "Gravel", now.AddMinutes(-5));
        var newer = w.File(2, r => r.Texts["goal"] = "A top three", now);

        Assert.True(w.Store.Get(older.Id)!.Replaced);
        var kept = w.Store.Get(newer.Id)!;
        Assert.Equal("Gravel", kept.Text("pName"));
        Assert.Equal("A top three", kept.Text("goal"));
    }

    [Fact]
    public void Approving_sets_tonights_goal_for_the_night_it_was_sent()
    {
        var w = new World();
        var ask = w.File(1, r => r.Texts["goal"] = "A top three");

        Assert.True(w.Desk.ApplyCrew(ask, out var refusal), refusal);

        var profile = w.Profiles.Get("100")!;
        Assert.Equal("A top three", profile.CrewGoal);
        Assert.Equal(DriverRequestStore.NightOf(ask.AskedUtc), profile.CrewGoalNight);
    }

    [Fact]
    public void A_juniors_notes_from_a_phone_a_badge_only_request_or_a_bad_name_is_refused()
    {
        // 13 to 17: an under-13's names are the desk's too (see below).
        var junior = new World(junior: true, age: JuniorAge.ThirteenToSeventeen);
        var note = junior.File(1, r => r.Texts["thanks"] = "My nan");
        Assert.False(junior.Desk.ApplyCrew(note, out var refusal));
        Assert.Contains("at the desk", refusal);
        Assert.Contains("Junior", junior.Desk.NoteFor(note));

        // Even with a parent's tick: the switch that took that was removed (15 Nov 2026).
        var withParent = junior.File(2, r =>
        {
            r.Texts["thanks"] = "My nan";
            r.Flags["guardian"] = true;
        });
        Assert.False(junior.Desk.ApplyCrew(withParent, out refusal));

        // Their crew's names are still theirs to send, and clearing a note is fine.
        // Id 9: the worlds below share the store on disk, and file 3 and 4.
        var names = junior.File(9, r =>
        {
            r.Texts["pName"] = "Gravel";
            r.Texts["thanks"] = "";
        });
        Assert.True(junior.Desk.ApplyCrew(names, out refusal), refusal);

        // Each in a world of its own: a second crew request would replace the first.
        var badgeOnly = new World();
        Assert.False(badgeOnly.Desk.ApplyCrew(badgeOnly.File(3, r => r.Texts["pName"] = "Gravel", pin: false), out refusal));
        Assert.Contains("personal PIN", refusal);

        var badName = new World();
        Assert.False(badName.Desk.ApplyCrew(badName.File(4, r => r.Texts["pName"] = "R2D2"), out refusal));
        Assert.Contains("isn't a name", refusal);
    }

    [Fact]
    public void A_note_that_names_another_driver_is_flagged_for_the_desk()
    {
        var w = new World();
        var ask = w.File(1, r => r.Texts["goal"] = "Beat Bill Hart");

        Assert.Contains("Names another driver (Bill Hart)", w.Desk.NoteFor(ask));
    }

    [Fact]
    public void The_crew_chip_shows_only_crew_requests_and_the_queries_chip_only_queries()
    {
        // The Desk Inbox's chips since 15 Nov 2026 (Driver Requests' two ticks before).
        static InboxItem Item(string id, string kind) =>
            new(InboxSource.Request, id, kind, kind, "Sam", "", "", DateTime.UtcNow, false, null);

        var all = new[] { Item("1", DriverAsk.Crew), Item("2", DriverAsk.Query), Item("3", DriverAsk.Details) };

        Assert.Equal(new[] { "1" }, DeskInbox.Filter(all, InboxFilter.Crew).Select(r => r.Id));
        Assert.Equal(new[] { "2" }, DeskInbox.Filter(all, InboxFilter.Queries).Select(r => r.Id));
        Assert.Equal(3, DeskInbox.Filter(all, InboxFilter.Requests).Count);
    }

    // ------------------------------------------------------------ 15 Nov: the gaps

    [Fact]
    public void Each_note_is_offered_to_one_segment_a_night_and_again_only_to_that_one()
    {
        var profiles = ProfileStore.Load();
        Assert.True(profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Sam", CrewThanks = "Dad" }, out _));
        var commentary = new CommentaryService(profiles, new AppSettings { DriversPickCrew = true }, null);
        var night = new DateTime(2026, 11, 15, 20, 0, 0);

        Assert.NotEmpty(commentary.CrewNoteLines("100", "Sam", CommentaryService.CrewNote.Thanks, "principal:race1", night));
        Assert.NotEmpty(commentary.CrewNoteLines("100", "Sam", CommentaryService.CrewNote.Thanks, "principal:race1", night.AddMinutes(1)));
        Assert.Empty(commentary.CrewNoteLines("100", "Sam", CommentaryService.CrewNote.Thanks, "recap:race2", night.AddMinutes(30)));

        // Next week, it is new again.
        Assert.NotEmpty(commentary.CrewNoteLines("100", "Sam", CommentaryService.CrewNote.Thanks, "recap:race9", night.AddDays(7)));
    }

    [Fact]
    public void Notes_past_the_heavy_mark_are_said_on_the_broadcast_window()
    {
        var commentary = new CommentaryService(ProfileStore.Load(), new AppSettings(), null);

        commentary.WeighNotes("intro for Heat 1", new string('x', CommentaryService.NotesHeavyChars - 1));
        Assert.Null(commentary.CrewNotesAlert);

        commentary.WeighNotes("intro for Heat 1", new string('x', CommentaryService.NotesHeavyChars));
        Assert.Contains("intro for Heat 1", commentary.CrewNotesAlert);
    }

    [Fact]
    public void A_voice_gone_from_the_engines_list_gives_the_clubs()
    {
        var pick = Pick(VoiceEngine.ElevenLabs, "deleted-id", "Gone");
        var (voice, why) = DriverCrew.VoiceFor(ClubPrincipal, pick, VoiceEngine.ElevenLabs, true, None, None, _ => false);

        Assert.Same(ClubPrincipal, voice);
        Assert.Contains("no longer", why);
        Assert.NotSame(ClubPrincipal, DriverCrew.VoiceFor(ClubPrincipal, pick, VoiceEngine.ElevenLabs, true, None, None, _ => true).Voice);
    }

    [Fact]
    public void A_goal_after_the_last_race_is_for_the_next_announced_night_or_held_for_the_next_one()
    {
        var asked = new DateTime(2026, 11, 15, 22, 30, 0);
        var tonight = TonightAtTheTrack.NightOf(asked).Date;

        Assert.Equal((tonight, false), DriverCrew.GoalNight(asked, tonightIsOver: false, nextNight: null));
        Assert.Equal((new DateTime(2026, 11, 22), false), DriverCrew.GoalNight(asked, tonightIsOver: true, nextNight: new DateTime(2026, 11, 22)));
        Assert.Equal((tonight, true), DriverCrew.GoalNight(asked, tonightIsOver: true, nextNight: null));

        var held = new DriverProfile { Transponder = "1", CrewGoal = "Win", CrewGoalNight = tonight, CrewGoalHeld = true };
        Assert.Equal(string.Empty, DriverCrew.GoalTonight(held, asked));
        Assert.Equal("Win", DriverCrew.GoalTonight(held, asked.AddDays(7)));
        Assert.Equal(string.Empty, DriverCrew.GoalTonight(held, asked.AddDays(DriverCrew.HeldGoalDays + 1)));
    }

    [Fact]
    public void A_goal_approved_after_the_nights_last_race_is_kept_for_the_next_and_the_phone_is_told()
    {
        var w = new World();
        w.Desk.TonightIsOver = () => true;
        w.Desk.NextNightDay = () => null;
        var ask = w.File(1, r => r.Texts["goal"] = "Top three");

        Assert.True(w.Desk.ApplyCrew(ask, out var refusal), refusal);

        var profile = w.Profiles.Get("100")!;
        Assert.True(profile.CrewGoalHeld);
        Assert.Equal(DriverRequestDesk.GoalHeldSaid, w.Store.Get(ask.Id)!.Said);
    }

    [Fact]
    public void Words_with_a_voice_that_is_gone_tell_the_phone_when_the_desk_approves()
    {
        var w = new World();
        var ask = w.File(1, r =>
        {
            r.Texts["pVoice"] = "abcdef012345";
            r.Texts["pName"] = "Gravel";
        });

        w.Desk.FileWaitingCrewVoices();

        var waiting = w.Store.Get(ask.Id)!;
        Assert.True(waiting.IsWaiting);
        Assert.Contains("isn't offered any more", w.Desk.NoteFor(waiting));

        Assert.True(w.Desk.ApplyCrew(waiting, out var refusal), refusal);
        Assert.Equal(DriverRequestDesk.WordsInVoiceGoneSaid, w.Store.Get(ask.Id)!.Said);
    }

    [Fact]
    public void A_juniors_notes_from_a_phone_are_refused_even_with_a_parents_tick()
    {
        var w = new World(junior: true);
        var ask = w.File(1, r =>
        {
            r.Texts["thanks"] = "My nan";
            r.Flags["guardian"] = true;
        });

        Assert.False(w.Desk.ApplyCrew(ask, out var refusal));
        Assert.Contains("at the desk", refusal);
        Assert.Contains("No parent's consent", w.Desk.NoteFor(ask));
    }

    [Fact]
    public void The_phone_is_told_a_juniors_notes_are_taken_at_the_desk()
    {
        var crew = new DriverLive.CrewForPhone(VoiceEngine.ElevenLabs, Array.Empty<CrewVoiceOption>());

        Assert.Equal(false, DriverLive.CrewOf(new DriverProfile { Transponder = "1", IsJunior = true }, crew)["notes"]);
        Assert.Equal(false, DriverLive.CrewOf(new DriverProfile { Transponder = "3", IsJunior = true, CrewNotesParentConsent = true }, crew)["notes"]);
        Assert.False(DriverLive.CrewOf(new DriverProfile { Transponder = "2" }, crew).ContainsKey("notes"));
    }

    [Fact]
    public void A_juniors_notes_go_online_only_as_set_or_not_until_a_parent_agreed()
    {
        var crew = new DriverLive.CrewForPhone(VoiceEngine.ElevenLabs, Array.Empty<CrewVoiceOption>());
        var junior = new DriverProfile { Transponder = "1", IsJunior = true, CrewThanks = "My nan", CrewCarStory = "" };

        var without = DriverLive.CrewOf(junior, crew);
        Assert.Equal(true, without["thanks"]);
        Assert.Equal(false, without["car"]);
        Assert.Equal(false, without["goal"]);
        Assert.DoesNotContain("My nan", JsonSerializer.Serialize(without));

        junior.CrewNotesParentConsent = true;
        Assert.Equal("My nan", DriverLive.CrewOf(junior, crew)["thanks"]);

        var adult = new DriverProfile { Transponder = "2", CrewThanks = "My nan" };
        Assert.Equal("My nan", DriverLive.CrewOf(adult, crew)["thanks"]);
    }

    [Fact]
    public void A_juniors_about_line_goes_online_only_as_set_until_a_parent_agreed_and_the_phone_knows_they_are_one()
    {
        string Me(DriverProfile p) => DriverLive.MeDocument(new[] { p }, _ => SamBadge);

        var junior = new DriverProfile { Transponder = "1", DriverName = "Sam Parker", IsJunior = true, AboutLine = "Races a red buggy" };
        var doc = Me(junior);
        Assert.DoesNotContain("red buggy", doc);
        Assert.Contains("\"aboutSet\":true", doc);
        Assert.Contains("\"junior\":true", doc);

        junior.CrewNotesParentConsent = true;
        Assert.Contains("red buggy", Me(junior));

        var adult = new DriverProfile { Transponder = "2", DriverName = "Bill Hart", AboutLine = "Races a red buggy" };
        var adultDoc = Me(adult);
        Assert.Contains("red buggy", adultDoc);
        Assert.DoesNotContain("junior", adultDoc);
    }

    // ------------------------------------------------------------ the about line (plan 1.1)

    [Fact]
    public void A_juniors_about_line_is_left_out_of_the_prompt_as_their_notes_are()
    {
        var junior = new DriverProfile { Transponder = "1", IsJunior = true, Team = "HFCC", AboutLine = "Races a red buggy" };

        var line = CommentaryService.DriverDetailLine("Sam", junior,
            DriverCrew.OwnWordsMayBeSaid(junior, streamOn: false, juniorNotesOnStream: false));
        Assert.Equal("Sam: team HFCC", line);

        junior.CrewNotesParentConsent = true;
        Assert.Contains("red buggy", CommentaryService.DriverDetailLine("Sam", junior,
            DriverCrew.OwnWordsMayBeSaid(junior, streamOn: false, juniorNotesOnStream: false)));
        Assert.Equal("Sam: team HFCC", CommentaryService.DriverDetailLine("Sam", junior,
            DriverCrew.OwnWordsMayBeSaid(junior, streamOn: true, juniorNotesOnStream: false)));
    }

    [Fact]
    public void A_juniors_pronouns_about_line_and_email_from_a_phone_are_refused_at_the_desk()
    {
        var w = new World(junior: true);
        var ask = w.File(1, r =>
        {
            r.Kind = DriverAsk.Details;
            r.Texts["about"] = "Races a red buggy";
        });

        var choice = new DriverRequestDesk.DetailsChoice(
            new Dictionary<string, string> { ["about"] = "Races a red buggy" }, new Dictionary<string, bool>());
        Assert.False(w.Desk.ApplyDetails(ask, choice, out var refusal));
        Assert.Contains("about line", refusal);
        Assert.Equal(string.Empty, w.Profiles.Get("100")!.AboutLine ?? string.Empty);

        var name = new DriverRequestDesk.DetailsChoice(
            new Dictionary<string, string> { ["announcer"] = "Sammy" }, new Dictionary<string, bool>());
        Assert.True(w.Desk.ApplyDetails(ask, name, out refusal), refusal);
        Assert.Equal("Sammy", w.Profiles.Get("100")!.AnnouncerName);
    }

    // ------------------------------------------------------------ marshal swaps (plan 4.4)

    [Fact]
    public void A_junior_cannot_ask_another_driver_to_cover_their_post()
    {
        var w = new World(junior: true);
        w.Desk.DutyOf = (_, _) => new MarshalSwaps.Duty("Q2 Heat 3", "Turn 3");
        w.Desk.CoverCandidates = (_, _) => new[] { ("500", "abcdefabcdefabcdefab") };
        var ask = w.File(1, r =>
        {
            r.Kind = DriverAsk.Swap;
            r.Texts["round"] = new string('a', 32);
            r.Texts["to"] = MarshalSwaps.KeyOf("abcdefabcdefabcdefab");
        });

        Assert.False(w.Desk.ApplySwap(ask, out var said));
        Assert.Equal(DriverRequestDesk.JuniorSwapWords, said);
    }

    [Fact]
    public void A_junior_is_never_asked_by_another_driver_to_cover_a_post()
    {
        var w = new World();
        w.Profiles.Upsert(new DriverProfile { Transponder = "500", DriverName = "Bill Hart", IsJunior = true });
        w.Desk.DutyOf = (_, _) => new MarshalSwaps.Duty("Q2 Heat 3", "Turn 3");
        w.Desk.CoverCandidates = (_, _) => new[] { ("500", "abcdefabcdefabcdefab") };
        var ask = w.File(1, r =>
        {
            r.Kind = DriverAsk.Swap;
            r.Texts["round"] = new string('a', 32);
            r.Texts["to"] = MarshalSwaps.KeyOf("abcdefabcdefabcdefab");
        });

        Assert.False(w.Desk.ApplySwap(ask, out var said));
        Assert.Contains("can't cover it", said);
    }

    // ------------------------------------------------------------ shout-outs (plan 4.5)

    [Fact]
    public void A_shout_out_naming_a_junior_here_tonight_is_flagged()
    {
        var sam = new DriverProfile { Transponder = "1", DriverName = "Sam Parker", AnnouncerName = "Sammy P", IsJunior = true };
        var bill = new DriverProfile { Transponder = "2", DriverName = "Bill Hart" };
        var jo = new DriverProfile { Transponder = "3", DriverName = "Jo Wells", IsJunior = true };
        var all = new[] { sam, bill, jo };
        DriverProfile? Of(string t) => all.FirstOrDefault(p => p.Transponder == t);

        var tonight = ClubNotes.JuniorsTonight(all, Of, new[] { "1", "2" });
        Assert.Equal(new[] { sam }, tonight);

        Assert.Equal("Sam", ClubNotes.NamesAJunior("Good luck sam from Oakfield Primary!", tonight));
        Assert.Equal("Sam Parker", ClubNotes.NamesAJunior("Go Sam Parker!", tonight));
        Assert.Equal("Sammy P", ClubNotes.NamesAJunior("Sammy P's mum says hi", tonight));
        Assert.NotNull(ClubNotes.NamesAJunior("Happy birthday to Sam's dad", tonight));
        Assert.Null(ClubNotes.NamesAJunior("Go Bill!", tonight));
        Assert.Null(ClubNotes.NamesAJunior("Samantha's cakes are in the canteen", tonight));

        // Jo isn't checked in, so not here tonight - but before anyone checks in, every junior counts.
        Assert.Null(ClubNotes.NamesAJunior("Go Jo Wells", tonight));
        Assert.NotNull(ClubNotes.NamesAJunior("Go Jo Wells", ClubNotes.JuniorsTonight(all, Of, Array.Empty<string>())));
    }

    // --- Under-13s' crew names: desk only (15 Nov 2026, children's privacy plan 2.1) ---

    [Fact]
    public void The_phone_is_told_who_counts_as_under_13_with_no_band_counting()
    {
        var crew = new DriverLive.CrewForPhone(VoiceEngine.ElevenLabs, Array.Empty<CrewVoiceOption>());

        Assert.Equal(true, DriverLive.CrewOf(new DriverProfile { Transponder = "1", IsJunior = true }, crew)["under13"]);
        Assert.Equal(true, DriverLive.CrewOf(new DriverProfile { Transponder = "2", IsJunior = true, JuniorAge = JuniorAge.Under13 }, crew)["under13"]);
        Assert.Equal(false, DriverLive.CrewOf(new DriverProfile { Transponder = "3", IsJunior = true, JuniorAge = JuniorAge.ThirteenToSeventeen }, crew)["under13"]);
        Assert.Equal(false, DriverLive.CrewOf(new DriverProfile { Transponder = "4", JuniorAge = JuniorAge.Under13 }, crew)["under13"]);
    }

    [Theory]
    [InlineData(JuniorAge.NotSet)]
    [InlineData(JuniorAge.Under13)]
    public void An_under_13s_crew_names_from_a_phone_are_refused_but_a_voice_or_a_cleared_name_is_not(JuniorAge age)
    {
        var w = new World(junior: true, age: age);
        var named = w.File(1, r => r.Texts["tName"] = "Turbo Tess");

        Assert.False(w.Desk.ApplyCrew(named, out var refusal));
        Assert.Equal(DriverRequestDesk.Under13NamesRefusal, refusal);
        Assert.Equal(string.Empty, w.Profiles.Get("100")!.CrewTechName);

        // The desk declines it, so the next asks don't take it in.
        w.Store.Answer(named.Id, DriverAsk.Declined, string.Empty, DateTime.UtcNow);

        var rachel = Pick(VoiceEngine.ElevenLabs, "rachel-id", "Rachel");
        w.Offered[DriverCrew.KeyOf(rachel)] = rachel;
        var voice = w.File(2, r => r.Texts["pVoice"] = DriverCrew.KeyOf(rachel));
        Assert.Equal(1, w.Desk.FileWaitingCrewVoices());
        Assert.Equal(DriverAsk.Approved, w.Store.Get(voice.Id)!.State);

        var cleared = w.File(3, r => r.Texts["pName"] = string.Empty);
        Assert.True(w.Desk.ApplyCrew(cleared, out refusal), refusal);
    }

    [Fact]
    public void A_13_to_17s_crew_names_still_wait_for_the_desk_as_before()
    {
        var w = new World(junior: true, age: JuniorAge.ThirteenToSeventeen);
        var named = w.File(1, r => r.Texts["tName"] = "Turbo Tess");

        Assert.True(w.Desk.ApplyCrew(named, out var refusal), refusal);
        Assert.Equal("Turbo Tess", w.Profiles.Get("100")!.CrewTechName);
    }
}