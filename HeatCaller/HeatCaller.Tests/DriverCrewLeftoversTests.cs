using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Driver crew leftovers (19 Nov 2026, plans/driver-crew-plan.md): a "said
/// as" spelling for crew names, per-field approve with an edit box each,
/// crew voices picked at the desk, and HeatCaller / Kokoro voice samples
/// sent to the relay for the phone's Play button.
/// </summary>
public sealed class DriverCrewLeftoversTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string SamBadge = "0123456789abcdef0123";

    private static CrewVoicePick Pick(VoiceEngine engine, string id, string name = "") =>
        new() { Engine = DriverCrew.EngineWord(engine), Id = id, Name = name.Length > 0 ? name : id };

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();

        public DriverRequestStore Store { get; } = DriverRequestStore.Load();

        public DriverRequestDesk Desk { get; }

        public World(bool junior = false, JuniorAge age = JuniorAge.NotSet)
        {
            Profiles.Upsert(new DriverProfile { Transponder = "100", DriverName = "Sam Parker", IsJunior = junior, JuniorAge = age, CrewTechName = "Old Tess" });

            Desk = new DriverRequestDesk(Store, new AppSettings { DriversPickCrew = true }, Profiles, BatteryPackStore.Load(),
                SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()), new UndoService(), _ => { },
                badge => badge == SamBadge ? "100" : null);
        }

        public DriverRequest File(int n, Action<DriverRequest> fill)
        {
            var ask = new DriverRequest
            {
                Id = n.ToString("x6") + new string('0', 18),
                Badge = SamBadge,
                Kind = DriverAsk.Crew,
                AskedUtc = DateTime.UtcNow,
                Proof = DriverAsk.ByPin,
            };
            fill(ask);
            Store.FileAll(new[] { ask }, DateTime.UtcNow, out _);
            return Store.Get(ask.Id)!;
        }

        public DriverProfile Sam => Profiles.Get("100")!;
    }

    // ------------------------------------------------------------ said as

    [Theory]
    [InlineData("Zhock", true)]
    [InlineData("Grah vel", true)]
    [InlineData("Gr4vel", false)]
    [InlineData("", false)]
    public void A_said_as_is_letters_spaces_hyphens_and_apostrophes(string text, bool ok) =>
        Assert.Equal(ok, DriverCrew.IsASaidAs(text));

    [Fact]
    public void The_said_as_is_what_is_spoken_but_only_beside_a_name_of_their_own()
    {
        Assert.Equal("Grah vel", DriverCrew.SpokenNameFor("Gravel", " Grah  vel ", "Toto"));
        Assert.Equal("Gravel", DriverCrew.SpokenNameFor("Gravel", "", "Toto"));
        Assert.Equal("Gravel", DriverCrew.SpokenNameFor("Gravel", "Gr4vel", "Toto"));
        Assert.Equal("Toto", DriverCrew.SpokenNameFor("", "Grah vel", "Toto"));
    }

    [Fact]
    public void A_said_as_from_the_phone_waits_for_the_desk_and_goes_on_the_profile()
    {
        var w = new World();
        var ask = w.File(1, r =>
        {
            r.Texts["pName"] = "Gravel";
            r.Texts["pSay"] = "Grah vel";
        });

        Assert.Contains(ask.Texts.Keys, k => k == "pSay");
        Assert.Contains("Principal said as: 'Grah vel' (was empty)", w.Desk.Describe(ask));

        Assert.True(w.Desk.ApplyCrew(ask, out var refusal), refusal);
        Assert.Equal("Gravel", w.Sam.CrewPrincipalName);
        Assert.Equal("Grah vel", w.Sam.CrewPrincipalSaidAs);
    }

    [Fact]
    public void The_inbox_reads_a_said_as_to_forty_characters()
    {
        var json = JsonSerializer.Serialize(new
        {
            ok = true,
            items = new object[]
            {
                new { id = new string('1', 24), b = SamBadge, k = "crew", v = "pin", at = "2026-11-19T19:40:10.000Z", f = new Dictionary<string, object> { ["tSay"] = new string('a', 50) } },
            },
        });

        var ask = Assert.Single(DriverRequestStore.ParseInbox(json, DateTime.UtcNow, out _));
        Assert.Equal(DriverCrew.SaidAsMaxChars, ask.Text("tSay").Length);
    }

    [Fact]
    public void An_under_13s_said_as_is_the_desks_too()
    {
        var w = new World(junior: true, age: JuniorAge.Under13);
        var ask = w.File(1, r => r.Texts["tSay"] = "Tess");

        Assert.False(w.Desk.ApplyCrew(ask, out var refusal));
        Assert.Equal(DriverRequestDesk.Under13NamesRefusal, refusal);
    }

    [Fact]
    public void The_phone_is_told_the_said_as_with_the_name()
    {
        var crew = new DriverLive.CrewForPhone(VoiceEngine.Kokoro, Array.Empty<CrewVoiceOption>());
        var one = DriverLive.CrewOf(new DriverProfile { Transponder = "1", CrewPrincipalName = "Gravel", CrewPrincipalSaidAs = "Grah vel" }, crew);

        var principal = Assert.IsType<Dictionary<string, object?>>(one["principal"]);
        Assert.Equal("Grah vel", principal["say"]);
        Assert.Equal(string.Empty, Assert.IsType<Dictionary<string, object?>>(one["tech"])["say"]);
    }

    // ------------------------------------------------------------ per-field approve

    [Fact]
    public void The_approve_dialog_gets_each_word_field_with_what_the_profile_has_now()
    {
        var w = new World();
        var ask = w.File(1, r =>
        {
            r.Texts["tName"] = "Turbo Tess";
            r.Texts["goal"] = "A top three";
        });

        var fields = w.Desk.CrewFieldsOf(ask, w.Sam);

        Assert.Equal(new[] { "tName", "goal" }, fields.Select(f => f.Key));
        Assert.Equal("Old Tess", fields[0].Current);
        Assert.Equal("Turbo Tess", fields[0].Proposed);
        Assert.Equal(DriverCrew.NameMaxChars, fields[0].MaxChars);
    }

    [Fact]
    public void Only_the_ticked_fields_go_on_and_the_phone_is_told_some_of_it_is_in()
    {
        var w = new World();
        var ask = w.File(1, r =>
        {
            r.Texts["pName"] = "Gravel";
            r.Texts["thanks"] = "Bill cheats";
        });

        Assert.True(w.Desk.ApplyCrew(ask, new Dictionary<string, string> { ["pName"] = "Gravel" }, out var refusal), refusal);

        Assert.Equal("Gravel", w.Sam.CrewPrincipalName);
        Assert.Equal(string.Empty, w.Sam.CrewThanks);
        var answered = w.Store.Get(ask.Id)!;
        Assert.Equal(DriverAsk.Approved, answered.State);
        Assert.Equal(DriverRequestDesk.CrewPartlySaid, answered.Said);
    }

    [Fact]
    public void A_field_reworded_at_the_desk_goes_on_as_reworded()
    {
        var w = new World();
        var ask = w.File(1, r => r.Texts["thanks"] = "thanks to my dad Mark Kent");

        Assert.True(w.Desk.ApplyCrew(ask, new Dictionary<string, string> { ["thanks"] = "  thanks to my dad " }, out var refusal), refusal);

        Assert.Equal("thanks to my dad", w.Sam.CrewThanks);
        Assert.Equal(DriverRequestDesk.CrewEditedSaid, w.Store.Get(ask.Id)!.Said);
    }

    [Fact]
    public void A_reworded_name_that_is_not_a_name_and_nothing_ticked_are_refused()
    {
        var w = new World();
        var ask = w.File(1, r => r.Texts["pName"] = "Gravel");

        Assert.False(w.Desk.ApplyCrew(ask, new Dictionary<string, string> { ["pName"] = "R2D2" }, out var refusal));
        Assert.Contains("isn't a name", refusal);

        Assert.False(w.Desk.ApplyCrew(ask, new Dictionary<string, string>(), out refusal));
        Assert.Contains("Nothing is ticked", refusal);

        Assert.True(w.Store.Get(ask.Id)!.IsWaiting);
        Assert.Equal(string.Empty, w.Sam.CrewPrincipalName);
    }

    [Fact]
    public void Unticking_a_juniors_note_lets_their_crew_name_through()
    {
        // The junior rules are not changed: a junior's note from a phone is
        // still refused - the desk may now leave it out and take the rest.
        var w = new World(junior: true, age: JuniorAge.ThirteenToSeventeen);
        var ask = w.File(1, r =>
        {
            r.Texts["pName"] = "Gravel";
            r.Texts["thanks"] = "My nan";
        });

        Assert.False(w.Desk.ApplyCrew(ask, out var refusal));
        Assert.Contains("at the desk", refusal);

        Assert.True(w.Desk.ApplyCrew(ask, new Dictionary<string, string> { ["pName"] = "Gravel" }, out refusal), refusal);
        Assert.Equal("Gravel", w.Sam.CrewPrincipalName);
        Assert.Equal(string.Empty, w.Sam.CrewThanks);
    }

    [Fact]
    public void The_dialogs_choice_is_the_ticked_boxes_trimmed()
    {
        var choice = ApproveCrewRequestForm.ChoiceOf(new[]
        {
            ("pName", true, " Gravel "),
            ("goal", false, "A top three"),
            ("thanks", true, ""),
        });

        Assert.Equal(2, choice.Count);
        Assert.Equal("Gravel", choice["pName"]);
        Assert.Equal(string.Empty, choice["thanks"]);
    }

    [Fact]
    public void The_approve_dialog_builds_a_tick_and_a_box_for_each_word_field()
    {
        var w = new World();
        var ask = w.File(1, r =>
        {
            r.Texts["pName"] = "Gravel";
            r.Texts["pSay"] = "Grah vel";
            r.Texts["story"] = "Built over winter";
        });

        var rows = StaThread.Run(() =>
        {
            using var form = new ApproveCrewRequestForm(w.Desk, ask, w.Sam);
            return form.Rows.Select(r => (r.Key, r.Tick.Checked, r.Box.Text)).ToList();
        }, TimeSpan.FromSeconds(60), () => "building the crew approve dialog");

        Assert.Equal(new[] { "pName", "pSay", "story" }, rows.Select(r => r.Key));
        Assert.All(rows, r => Assert.True(r.Checked));
        Assert.Equal("Built over winter", rows[2].Text);
    }

    // ------------------------------------------------------------ desk voice picking

    [Fact]
    public void The_desks_voice_list_is_the_club_own_the_phones_list_and_a_pick_no_longer_on_it()
    {
        var emma = Pick(VoiceEngine.Kokoro, "bf_emma", "Emma");
        var adam = Pick(VoiceEngine.Kokoro, "am_adam", "Adam");
        var offered = new[] { new CrewVoiceOption(emma, string.Empty), new CrewVoiceOption(adam, string.Empty) };

        var (choices, selected) = ProfileForm.CrewVoiceChoices(offered, adam);
        Assert.Equal(new[] { "The club's own voice", "Emma", "Adam" }, choices.Select(c => c.Words));
        Assert.Equal(2, selected);

        var gone = Pick(VoiceEngine.ElevenLabs, "rachel-id", "Rachel");
        var (kept, keptAt) = ProfileForm.CrewVoiceChoices(offered, gone);
        Assert.Equal(3, keptAt);
        Assert.True(kept[3].Kept);
        Assert.Contains("Rachel", kept[3].Words);

        Assert.Equal(0, ProfileForm.CrewVoiceChoices(offered, null).Selected);
    }

    [Fact]
    public void The_voice_saved_is_the_one_picked_and_an_unchanged_or_unlisted_pick_is_kept()
    {
        var emma = Pick(VoiceEngine.Kokoro, "bf_emma", "Emma");
        var rachel = Pick(VoiceEngine.ElevenLabs, "rachel-id", "Rachel");
        var (choices, _) = ProfileForm.CrewVoiceChoices(new[] { new CrewVoiceOption(emma, string.Empty) }, rachel);

        StaThread.Run(() =>
        {
            using var picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            picker.Items.AddRange(choices.Cast<object>().ToArray());

            picker.SelectedIndex = 1;
            Assert.Equal("bf_emma", ProfileForm.CrewVoiceKept(picker, choices, rachel)!.Id);

            picker.SelectedIndex = 0;
            Assert.Null(ProfileForm.CrewVoiceKept(picker, choices, rachel));

            picker.SelectedIndex = 2;
            Assert.Equal("rachel-id", ProfileForm.CrewVoiceKept(picker, choices, rachel)!.Id);

            Assert.Equal("rachel-id", ProfileForm.CrewVoiceKept(null, choices, rachel)!.Id);
            return 0;
        }, TimeSpan.FromSeconds(30), () => "picking a voice");
    }

    [Fact]
    public void The_profile_dialog_shows_the_voice_lists_only_when_there_are_voices_and_keeps_the_said_as()
    {
        var emma = Pick(VoiceEngine.Kokoro, "bf_emma", "Emma");
        var sam = new DriverProfile
        {
            Transponder = "100",
            DriverName = "Sam",
            CrewPrincipalName = "Gravel",
            CrewPrincipalSaidAs = "Grah vel",
            CrewTechVoice = emma,
        };

        var (shown, pickers) = StaThread.Run(() =>
        {
            using var form = new ProfileForm(sam, RaceClassStore.Load(), crewVoices: new[] { new CrewVoiceOption(emma, string.Empty) });
            var count = AllControls(form).OfType<ComboBox>().Count(c => c.Items.Count > 0 && c.Items[0] is ProfileForm.CrewVoiceChoice);
            return (form.ProfileAsShown(), count);
        }, TimeSpan.FromSeconds(60), () => "the profile dialog with crew voices");

        Assert.Equal(2, pickers);
        Assert.Equal("Grah vel", shown.CrewPrincipalSaidAs);
        Assert.Equal("bf_emma", shown.CrewTechVoice!.Id);
        Assert.Null(shown.CrewPrincipalVoice);

        var none = StaThread.Run(() =>
        {
            using var form = new ProfileForm(sam, RaceClassStore.Load());
            return AllControls(form).OfType<ComboBox>().Count(c => c.Items.Count > 0 && c.Items[0] is ProfileForm.CrewVoiceChoice);
        }, TimeSpan.FromSeconds(60), () => "the profile dialog without crew voices");

        Assert.Equal(0, none);
    }

    [Fact]
    public void Edit_driver_from_other_pages_offers_the_voices_too_and_keeps_a_pick_not_on_the_list()
    {
        var emma = Pick(VoiceEngine.Kokoro, "bf_emma", "Emma");
        var rachel = Pick(VoiceEngine.ElevenLabs, "rachel-id", "Rachel");
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "100", DriverName = "Sam", CrewPrincipalVoice = rachel });

        var (shown, words) = StaThread.Run(() =>
        {
            using var form = DriverEditDialog.FormFor(profiles.Get("100"), "100", RaceClassStore.Load(), new UndoService(), profiles,
                new[] { new CrewVoiceOption(emma, string.Empty) });
            var pickers = AllControls(form).OfType<ComboBox>()
                .Where(c => c.Items.Count > 0 && c.Items[0] is ProfileForm.CrewVoiceChoice)
                .ToList();
            return (form.ProfileAsShown(), pickers.Select(p => p.Text).ToList());
        }, TimeSpan.FromSeconds(60), () => "Edit Driver with crew voices");

        Assert.Equal(2, words.Count);
        Assert.Contains("(not on the list now)", words[0]);
        Assert.Equal("The club's own voice", words[1]);
        Assert.Equal("rachel-id", shown.CrewPrincipalVoice!.Id);
        Assert.Null(shown.CrewTechVoice);

        var without = StaThread.Run(() =>
        {
            using var form = DriverEditDialog.FormFor(profiles.Get("100"), "100", RaceClassStore.Load(), new UndoService(), profiles, null);
            return AllControls(form).OfType<ComboBox>().Count(c => c.Items.Count > 0 && c.Items[0] is ProfileForm.CrewVoiceChoice);
        }, TimeSpan.FromSeconds(60), () => "Edit Driver without crew voices");

        Assert.Equal(0, without);
    }

    [Fact]
    public void A_voice_the_desk_picked_on_edit_driver_is_saved_and_a_phone_pick_meanwhile_kept_when_the_desk_left_it()
    {
        var emma = Pick(VoiceEngine.Kokoro, "bf_emma", "Emma");
        var adam = Pick(VoiceEngine.Kokoro, "am_adam", "Adam");
        var openedOn = new DriverProfile { Transponder = "100", DriverName = "Sam" };

        // The desk picked Emma for the principal; the phone meanwhile picked Adam for the tech lead.
        var edited = ProfileStore.Snapshot(openedOn);
        edited.CrewPrincipalVoice = emma;
        var onFile = ProfileStore.Snapshot(openedOn);
        onFile.CrewTechVoice = adam;

        var saved = DriverEditDialog.ToSave(openedOn, edited, onFile);

        Assert.Equal("bf_emma", saved.CrewPrincipalVoice!.Id);
        Assert.Equal("am_adam", saved.CrewTechVoice!.Id);
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (var inner in AllControls(child))
            {
                yield return inner;
            }
        }
    }

    // ------------------------------------------------------------ samples

    [Fact]
    public void Only_heatcaller_and_kokoro_voices_not_on_the_relay_are_sent()
    {
        var emma = Pick(VoiceEngine.Kokoro, "bf_emma", "Emma");
        var adam = Pick(VoiceEngine.Kokoro, "am_adam", "Adam");
        var hc = Pick(VoiceEngine.HeatCaller, "hc-1", "Max");
        var rachel = Pick(VoiceEngine.ElevenLabs, "rachel-id", "Rachel");
        var voices = new[]
        {
            new CrewVoiceOption(emma, string.Empty),
            new CrewVoiceOption(adam, string.Empty),
            new CrewVoiceOption(hc, string.Empty),
            new CrewVoiceOption(rachel, "https://storage.googleapis.com/r.mp3"),
        };
        var onRelay = new HashSet<string> { DriverCrew.KeyOf(adam) };

        Assert.Equal(new[] { "bf_emma", "hc-1" }, CrewSamples.StillToSend(voices, onRelay).Select(v => v.Pick.Id));

        var marked = CrewSamples.Marked(voices, onRelay);
        Assert.Equal(new[] { false, true, false, false }, marked.Select(v => v.OnRelay));
    }

    [Fact]
    public void The_phone_is_told_a_sample_is_on_the_relay_but_never_both_kinds()
    {
        var emma = Pick(VoiceEngine.Kokoro, "bf_emma", "Emma");
        var crew = new DriverLive.CrewForPhone(VoiceEngine.Kokoro, new[]
        {
            new CrewVoiceOption(emma, string.Empty, OnRelay: true),
            new CrewVoiceOption(Pick(VoiceEngine.Kokoro, "am_adam"), string.Empty),
        });

        using var doc = JsonDocument.Parse(DriverLive.MeDocument(Array.Empty<DriverProfile>(), _ => SamBadge, crew: crew));
        var voices = doc.RootElement.GetProperty("crewVoices");

        Assert.True(voices[0].GetProperty("h").GetBoolean());
        Assert.False(voices[0].TryGetProperty("s", out _));
        Assert.False(voices[1].TryGetProperty("h", out _));
    }

    [Fact]
    public void A_sample_is_made_once_and_kept_on_this_pc()
    {
        var hc = Pick(VoiceEngine.HeatCaller, "hc-1", "Max");
        var asked = 0;
        var mp3 = new byte[] { (byte)'I', (byte)'D', (byte)'3', 1, 2, 3 };

        Task<byte[]> Fetch(string id, CancellationToken _)
        {
            Assert.Equal("hc-1", id);
            asked++;
            return Task.FromResult(mp3);
        }

        Task<byte[]> NoKokoro(string id, string line, CancellationToken _) => throw new InvalidOperationException("not Kokoro");

        Assert.Equal(mp3, CrewSamples.SampleOfAsync(hc, Fetch, NoKokoro).GetAwaiter().GetResult());
        Assert.Equal(mp3, CrewSamples.SampleOfAsync(hc, Fetch, NoKokoro).GetAwaiter().GetResult());
        Assert.Equal(1, asked);
        Assert.Equal(mp3, CrewSamples.Kept(DriverCrew.KeyOf(hc)));
    }

    [Fact]
    public void The_relays_list_of_samples_reads_keys_only()
    {
        var keys = RelayPublisher.KeysFrom("{\"ok\":true,\"keys\":[\"0123456789ab\",\"BAD\",5,\"ba9876543210\"]}");

        Assert.NotNull(keys);
        Assert.Equal(new[] { "0123456789ab", "ba9876543210" }, keys!.OrderBy(k => k));
        Assert.Null(RelayPublisher.KeysFrom("{\"ok\":false}"));
        Assert.Null(RelayPublisher.KeysFrom("not json"));
    }
}
