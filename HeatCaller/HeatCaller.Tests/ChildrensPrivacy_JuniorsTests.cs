using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Children's privacy (15 Nov 2026): a junior's name is shortened on every
/// published surface whatever the club's "Shorten published names" says
/// (plan 1.3); Sailor-mouth plays clean around juniors (plan 4.1); and a
/// junior's "On stream: Not at all" keeps their name off the stream, the
/// captions, listen-along and the preview (plan 4.2).
/// </summary>
[Collection(PublicPrivacyCurrentCollection.Name)]
public sealed class ChildrensPrivacy_JuniorsTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly PublicPrivacy _was = PublicPrivacy.Current;

    // A car an earlier test said by its number ("Sam", named "Sam") would
    // withhold this class's Sam Parker lines from listen-along: each test
    // starts with none, and the run's set goes back after.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte>? _spokenByNumber =
        AnnouncementService.SwapCarsSpokenByNumberForTests(null);

    public void Dispose()
    {
        AnnouncementService.SwapCarsSpokenByNumberForTests(_spokenByNumber);
        PublicPrivacy.Current = _was;
        _data.Dispose();
    }

    private static DriverProfile Mia(bool offStream = false) =>
        new() { Transponder = "7001", DriverName = "Mia Kent", IsJunior = true, RaceNumber = "4", JuniorOffStream = offStream };

    private static DriverProfile Sam() => new() { Transponder = "7002", DriverName = "Sam Parker" };

    private static ProfileStore Profiles(params DriverProfile[] drivers)
    {
        var store = ProfileStore.Load();

        foreach (var driver in drivers)
        {
            store.Upsert(driver);
        }

        return store;
    }

    // ------------------------------------------------------------ 1.3 junior names on every published surface

    [Fact]
    public void With_shortening_off_a_junior_is_Mia_K_in_the_schedules_line_ups()
    {
        var events = RaceEventStore.Load();
        var rounds = RoundDefinitionStore.Load();
        var schedule = DayScheduleStore.Load();
        var profiles = Profiles(Mia(), Sam());
        EventSpine.Bind(events, CheckInStore.Load(), TechInspectionStore.Load(), schedule, rounds);
        PublicPrivacy.Current = PublicPrivacy.From(profiles.All);

        var day = new RaceEvent { Name = "Tonight" };
        events.Upsert(day);
        events.SetActive(day.Id);

        var heat = new RoundDefinition
        {
            ClassName = "Junior",
            RoundLabel = "Q1 Heat 1",
            Kind = RoundKind.Qualifier,
            AssignedTransponders = new List<string> { "7001", "7002" },
        };
        rounds.Add(heat);
        events.AddRoundsToActive(new[] { heat.Id });
        schedule.SyncToProgram(EventSpine.Programme(events, rounds)
            .Select(r => new DayScheduleStore.ProgramLine(r.Id, r.ClassName, r.RoundLabel))
            .ToList());

        var json = RelayDocuments.Programme(schedule, rounds, events, profiles, lineups: true, abbreviate: false);

        Assert.DoesNotContain("Mia Kent", json);
        Assert.Contains("\"Mia K\"", json);
        Assert.Contains("\"Sam Parker\"", json);
    }

    [Fact]
    public void With_shortening_off_a_junior_is_Mia_K_on_the_badges_page()
    {
        var profiles = Profiles(Mia(), Sam());
        PublicPrivacy.Current = PublicPrivacy.From(profiles.All);

        var earned = new List<DriverAchievements>
        {
            new() { Transponder = "7001", Unlocked = new List<UnlockedAchievement> { new() { Id = AchievementCatalogue.FirstRace } } },
            new() { Transponder = "7002", Unlocked = new List<UnlockedAchievement> { new() { Id = AchievementCatalogue.FirstRace } } },
        };

        File.WriteAllText(_data.PathOf("achievements.json"), JsonSerializer.Serialize(earned));

        var json = RelayDocuments.Achievements(AchievementStore.Load(), profiles, abbreviate: false);

        Assert.DoesNotContain("Mia Kent", json);
        Assert.Contains("\"Mia K\"", json);
        Assert.Contains("\"Sam Parker\"", json);
    }

    [Fact]
    public void A_name_with_no_transponder_is_still_matched_to_a_junior()
    {
        var register = PublicPrivacy.From(new[] { Mia(), Sam() });

        Assert.Equal(new[] { "Mia K", "Sam Parker" }, RelayPublisher.DisplayNames(new[] { "Mia Kent", "Sam Parker" }, abbreviate: false, null, register));

        // A transponder the register knows decides on its own: an adult who
        // shares the junior's name keeps theirs where the caller knows them.
        var twins = PublicPrivacy.From(new[] { Mia(), new DriverProfile { Transponder = "7003", DriverName = "Mia Kent" } });
        Assert.Equal(new[] { "Mia Kent" }, RelayPublisher.DisplayNames(new[] { "Mia Kent" }, abbreviate: false, new string?[] { "7003" }, twins));
    }

    [Fact]
    public void A_club_with_no_juniors_gets_the_same_names_as_before()
    {
        var register = PublicPrivacy.From(new[] { Sam() });

        Assert.False(register.AnyJuniors);
        Assert.Equal(new[] { "Sam Parker" }, RelayPublisher.DisplayNames(new[] { "Sam Parker" }, abbreviate: false, null, register));
        Assert.Equal("Sam Parker leads.", register.Redact("Sam Parker leads."));
    }

    [Fact]
    public void With_shortening_off_a_junior_is_Mia_K_in_the_public_captions()
    {
        var register = PublicPrivacy.From(new[] { Mia(), Sam() });
        var at = new DateTime(2026, 11, 15, 19, 0, 0, DateTimeKind.Utc);
        var lines = new[] { new CaptionLine(1, "Mia Kent takes the lead from Sam Parker!", at) };

        var json = MainForm.CaptionsDocument(lines, register, at, namesACarByNumber: null, shorten: null);

        Assert.DoesNotContain("Kent", json);
        Assert.Contains("Mia K takes the lead from Sam Parker!", json);
    }

    [Fact]
    public void A_caption_cut_inside_a_juniors_name_does_not_let_the_surname_out()
    {
        var register = PublicPrivacy.From(new[] { Mia() });
        var at = new DateTime(2026, 11, 15, 19, 0, 0, DateTimeKind.Utc);
        // One sentence over CaptionLog.PieceChars, cut at its words - so
        // somewhere "Mia" and "Kent" can fall either side of a cut.
        var whole = "Into the very last lap now and the leader of this junior race tonight after a long and very hard fought battle all the way from the start line is Mia Kent who has led since the hairpin";
        var pieces = CaptionLog.Pieces(whole);

        Assert.True(pieces.Count > 1);

        var lines = pieces.Select((p, i) => new CaptionLine(i + 1, p, at) { WholeText = whole, PieceIndex = i }).ToList();
        var json = MainForm.CaptionsDocument(lines, register, at, namesACarByNumber: null, shorten: null);

        Assert.DoesNotContain("Kent", json);
    }

    [Fact]
    public void With_shortening_off_a_junior_is_Mia_K_in_the_night_before_preview()
    {
        var register = PublicPrivacy.From(new[] { Mia(), Sam() });

        var shown = register.ForPublicProse("Tomorrow MIA KENT meets Sam Parker. Mia Kent has the pace.");

        Assert.Equal("Tomorrow Mia K meets Sam Parker. Mia K has the pace.", shown);
    }

    [Fact]
    public void An_initials_only_junior_stays_initials()
    {
        var register = PublicPrivacy.From(new[] { new DriverProfile { Transponder = "7001", DriverName = "Mia Kent", IsJunior = true, InitialsOnline = true } });

        Assert.Equal("M.K. wins.", register.Redact("Mia Kent wins."));
        Assert.Equal(new[] { "M.K." }, RelayPublisher.DisplayNames(new[] { "Mia Kent" }, abbreviate: false, null, register));
    }

    [Fact]
    public void Listen_along_withholds_a_line_saying_a_juniors_full_name_when_the_club_does_not_shorten()
    {
        var register = PublicPrivacy.From(new[] { Mia(), Sam() });
        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load())
        {
            ShortenNamesForCloudVoices = () => false,
        };

        Assert.Null(announcer.CaptionIfCleared("Mia Kent takes the lead!", audioShortened: false, register));
        Assert.Equal("Sam Parker takes the lead!", announcer.CaptionIfCleared("Sam Parker takes the lead!", audioShortened: false, register));
    }

    // ------------------------------------------------------------ 4.2 a junior off the stream

    [Fact]
    public void Off_the_stream_lines_naming_them_are_withheld_from_listeners_and_the_captions()
    {
        var register = PublicPrivacy.From(new[] { Mia(offStream: true), Sam() });
        var at = new DateTime(2026, 11, 15, 19, 0, 0, DateTimeKind.Utc);

        Assert.True(AnnouncementService.WithheldFromListeners("Mia is flying!", register));
        Assert.True(register.MentionsOffStream("Kent is flying!"));
        Assert.False(AnnouncementService.WithheldFromListeners("Sam Parker is flying!", register));

        var json = MainForm.CaptionsDocument(
            new[] { new CaptionLine(1, "Mia Kent leads.", at), new CaptionLine(2, "Sam Parker is second.", at) },
            register, at);

        Assert.DoesNotContain("Mia", json);
        Assert.Contains("Sam Parker is second.", json);
    }

    [Fact]
    public void Shortened_by_default_a_junior_is_not_withheld()
    {
        var register = PublicPrivacy.From(new[] { Mia(), Sam() });

        Assert.False(register.MentionsOffStream("Mia Kent leads."));
        Assert.False(AnnouncementService.WithheldFromListeners("Mia Kent leads.", register));
    }

    [Fact]
    public void Off_the_stream_the_preview_leaves_out_every_sentence_naming_them()
    {
        var register = PublicPrivacy.From(new[] { Mia(offStream: true), Sam() });

        var shown = register.ForPublicProse("Big night tomorrow! Mia Kent is chasing Sam Parker. Sam Parker has the pace.\n\nKent won last week.");

        Assert.DoesNotContain("Mia", shown);
        Assert.DoesNotContain("Kent", shown);
        Assert.Contains("Big night tomorrow! Sam Parker has the pace.", shown);
    }

    [Fact]
    public void Off_the_stream_only_counts_for_a_junior()
    {
        var adult = PublicPrivacy.From(new[] { new DriverProfile { Transponder = "7002", DriverName = "Sam Parker", JuniorOffStream = true } });

        Assert.False(adult.MentionsOffStream("Sam Parker leads."));
    }

    [Fact]
    public void Off_the_stream_the_overlay_shows_their_car_not_their_name()
    {
        var offStream = new Dictionary<string, string> { ["Mia Kent"] = JuniorNames.OffStreamName("4") };

        var map = JuniorNames.ForStream(new[] { "Mia Kent", "Ava Long" }, JuniorNames.Full, offStream);

        Assert.Equal("Car 4", map["Mia Kent"]);
        Assert.False(map.ContainsKey("Ava Long"));
        Assert.Equal("A driver", JuniorNames.OffStreamName(" "));
    }

    [Fact]
    public void Off_the_stream_the_stream_state_names_them_by_car()
    {
        var profiles = Profiles(Mia(offStream: true), new DriverProfile { Transponder = "7004", DriverName = "Ava Long", IsJunior = true });
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);
        race.ProcessLapEvent(new DirectLapEvent { TransponderId = "7001", DeviceTimeMs = 10_000, SignalHits = 1, SignalStrength = 100, IsSynthetic = true });
        race.ProcessLapEvent(new DirectLapEvent { TransponderId = "7004", DeviceTimeMs = 10_500, SignalHits = 1, SignalStrength = 100, IsSynthetic = true });

        var server = new WebServerService(new RaceTiming(race), profiles, settings: new AppSettings { DisplayLook = Looks.MidnightKey });
        var names = JsonDocument.Parse(server.BuildStateJson()).RootElement.GetProperty("streamNames");

        Assert.Equal("Car 4", names.GetProperty("Mia Kent").GetString());
        Assert.Equal("Ava L", names.GetProperty("Ava Long").GetString());
    }

    [Fact]
    public void The_profile_dialog_keeps_the_on_stream_choice_and_the_consent()
    {
        var driver = Mia(offStream: true);
        driver.StreamConsent = true;

        var shown = StaThread.Run(() =>
        {
            using var form = new ProfileForm(driver, RaceClassStore.Load());
            return form.ProfileAsShown();
        }, TimeSpan.FromSeconds(90), () => "editing Mia Kent in Driver Hub");

        Assert.True(shown.JuniorOffStream);
        Assert.True(shown.StreamConsent);

        var plain = StaThread.Run(() =>
        {
            using var form = new ProfileForm(Mia(), RaceClassStore.Load());
            return form.ProfileAsShown();
        }, TimeSpan.FromSeconds(90), () => "editing Mia Kent in Driver Hub");

        Assert.False(plain.JuniorOffStream);
        Assert.False(plain.StreamConsent);
    }

    [Fact]
    public void Choosing_not_at_all_rebuilds_the_register_and_redoes_the_preview()
    {
        Assert.NotEqual(MainForm.PrivacyKeyOf(new[] { Mia() }), MainForm.PrivacyKeyOf(new[] { Mia(offStream: true) }));

        var before = PublicPrivacy.From(new[] { Mia() });
        var after = PublicPrivacy.From(new[] { Mia(offStream: true) });

        Assert.NotEqual(before.OffStreamFingerprint, after.OffStreamFingerprint);

        // No saved result changes, so nothing is republished for it.
        Assert.Empty(PublicPrivacy.Changed(before, after));
        Assert.Equal(before.FlaggedFingerprint, after.FlaggedFingerprint);
    }
}

/// <summary>
/// Every test class that sets PublicPrivacy.Current, run one at a time
/// (15 Nov 2026): it is one static register, and two classes setting it in
/// parallel made a listen-along test see the other's drivers - it passed
/// alone and failed once in a full run.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PublicPrivacyCurrentCollection
{
    public const string Name = "PublicPrivacy.Current";
}
