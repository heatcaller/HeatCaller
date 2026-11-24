using System.Net;

namespace HeatCaller.Tests;

/// <summary>
/// ElevenLabs retires its 19 Default voices on 31 Dec 2026 (24 Nov 2026,
/// Asher, plans/elevenlabs-voice-replacement-plan.md): the retirement switch
/// (step B), a dead voice said plainly once and rested (C), HeatCaller's six
/// in every picker (D), and each line deleted from the account's history (H).
/// </summary>
[Collection(ElevenLabsHttpCollection.Name)]
public sealed class ElevenLabsRetiredVoicesTests : IDisposable
{
    private const string Charlie = "IKne3meq5aSn9XLyUdCD";
    private const string Jessica = "cgSgspJ2msm6clMCkdW9";
    private const string Callum = "N2lVS1w4EtoT3dr4eOWO";
    private const string Daniel = "onwK4e9ZLuTAKqWW03F9";

    private const string VoiceNotFound =
        "{\"detail\":{\"type\":\"not_found\",\"code\":\"voice_not_found\",\"message\":\"A voice with voice_id 'x' was not found.\",\"status\":\"voice_not_found\"}}";

    private const string MissingPermission =
        "{\"detail\":{\"type\":\"authentication_error\",\"code\":\"unauthorized\",\"message\":\"The API key you used is missing the permission speech_history_write to execute this operation.\",\"status\":\"missing_permissions\"}}";

    private static readonly DateOnly Before = new(2026, 12, 31);
    private static readonly DateOnly After = new(2027, 1, 1);

    private readonly TempAppData _data = new();
    private readonly HttpClient _realHttp = ElevenLabsClient.Http;
    private readonly Func<bool> _realAsk = ElevenLabsClient.AskNotToKeepLines;
    private readonly Action<string>? _realNotice = ElevenLabsClient.Notice;
    private readonly List<string> _said = new();

    public ElevenLabsRetiredVoicesTests()
    {
        ElevenLabsClient.AskNotToKeepLines = () => true;
        ElevenLabsClient.Notice = line => { lock (_said) { _said.Add(line); } };
        Forget();
    }

    public void Dispose()
    {
        ElevenLabsClient.HistoryDeletesSettledAsync().GetAwaiter().GetResult();
        ElevenLabsClient.Http = _realHttp;
        ElevenLabsClient.AskNotToKeepLines = _realAsk;
        ElevenLabsClient.Notice = _realNotice;
        Forget();
        _data.Dispose();
    }

    private static void Forget()
    {
        ElevenLabsClient.ForgetNoLoggingRefusals();
        ElevenLabsClient.ForgetGoneVoices();
        ElevenLabsClient.ForgetHistoryRefusals();
    }

    // ------------------------------------------------------------------
    // B. The rule
    // ------------------------------------------------------------------

    [Fact]
    public void All_nineteen_default_voices_are_known_once_each_with_daniel_and_callum_as_published()
    {
        Assert.Equal(19, RetiredVoices.All.Count);
        Assert.Equal(19, RetiredVoices.All.Select(r => r.Id).Distinct().Count());
        Assert.Equal("Daniel", RetiredVoices.Find(Daniel)!.Name);
        Assert.Equal("Callum", RetiredVoices.Find(Callum)!.Name);
        Assert.Null(RetiredVoices.Find("fnYMz3F5gMEDGMWcH1ex"));
        Assert.Null(RetiredVoices.Find(""));
    }

    [Fact]
    public void A_seat_moves_to_heatcallers_voice_for_it_and_anything_else_to_elevenlabs_replacement()
    {
        Assert.Equal("Darian - Warm Grounded Storyteller", RetiredVoices.ForSeat(Charlie, RetiredVoices.Seat.TeamPrincipal)!.Name);
        Assert.Equal("Finley - Articulate Anchor", RetiredVoices.ForSeat(Charlie, RetiredVoices.Seat.Announcer)!.Name);
        Assert.Equal("Baxter - Dry Calm Aussie", RetiredVoices.ForOther(Charlie)!.Name);
        Assert.Equal("g7LVvkPWALzPxOQbF6OE", RetiredVoices.ForOther(Jessica)!.Id);

        // Callum's named replacement answers voice_not_found: a seat still
        // gets its own voice, a crew pick none.
        Assert.Equal("Wyatt - Seasoned Mentor", RetiredVoices.ForSeat(Callum, RetiredVoices.Seat.TechnicalLead)!.Name);
        Assert.Null(RetiredVoices.ForOther(Callum));

        Assert.Null(RetiredVoices.ForSeat("someone-else", RetiredVoices.Seat.CoHost));
    }

    [Fact]
    public void The_six_seats_have_the_voices_asher_picked()
    {
        Assert.Equal(
            new[] { "fnYMz3F5gMEDGMWcH1ex", "BFd5oBc2DDna33pSi4Gf", "FrS6cKLB1wg4WYgPa9GW", "gOupLcAkjEnguROwi4oS", "dvbL7qkNGZY1IqPGZAjM", "jSuBIjxMKhqIfb0wCK1F" },
            RetiredVoices.Suggested.Select(s => s.Voice.Id));
        Assert.Equal(6, RetiredVoices.Suggested.Select(s => s.Seat).Distinct().Count());
    }

    [Fact]
    public void The_cast_tab_line_names_the_voice_and_its_replacement()
    {
        Assert.Equal("ElevenLabs retires Charlie on 31 December. Switch to Darian?",
            RetiredVoices.SeatNotice(Charlie, RetiredVoices.Seat.TeamPrincipal, Before));
        Assert.Null(RetiredVoices.SeatNotice("fnYMz3F5gMEDGMWcH1ex", RetiredVoices.Seat.Announcer, Before));
    }

    [Fact]
    public void The_voices_go_on_the_first_of_january_local_date()
    {
        Assert.False(RetiredVoices.AreGone(Before));
        Assert.True(RetiredVoices.AreGone(After));
    }

    [Fact]
    public void Seats_switch_by_themselves_only_from_the_first_of_january()
    {
        var settings = new AppSettings
        {
            CoHostElevenLabsVoiceId = "Xb7hH8MSUJpSbSDYk0k2",
            CoHostElevenLabsVoiceName = "Alice",
            PrincipalElevenLabsVoiceId = "my-own-clone",
            PrincipalElevenLabsVoiceName = "Charlie",
            RecapHostElevenLabsVoiceId = "bIHbv24MWmeRgasZH58o",
        };
        var announcer = new VoiceProfile { VoiceId = Charlie, VoiceName = "Charlie" };

        Assert.Empty(RetiredVoices.SwitchSeats(settings, announcer, Before));
        Assert.Equal(Charlie, announcer.VoiceId);

        var moves = RetiredVoices.SwitchSeats(settings, announcer, After);

        Assert.Equal(3, moves.Count);
        Assert.Equal(("fnYMz3F5gMEDGMWcH1ex", "Finley - Articulate Anchor"), (announcer.VoiceId, announcer.VoiceName));
        Assert.Equal("BFd5oBc2DDna33pSi4Gf", settings.CoHostElevenLabsVoiceId);
        Assert.Equal("jSuBIjxMKhqIfb0wCK1F", settings.RecapHostElevenLabsVoiceId);

        // Matched by id: a club's own voice called Charlie stays.
        Assert.Equal("my-own-clone", settings.PrincipalElevenLabsVoiceId);
        Assert.Contains("ElevenLabs retired Charlie", RetiredVoices.Words(moves[0]));

        // Said once: the next start finds nothing to move.
        Assert.Empty(RetiredVoices.SwitchSeats(settings, announcer, After));
    }

    [Fact]
    public void A_switched_seat_renders_afresh_because_saved_lines_are_kept_by_voice_id()
    {
        var old = new VoiceProfile { VoiceId = Charlie, Engine = VoiceEngine.ElevenLabs };
        var moved = new VoiceProfile { VoiceId = RetiredVoices.ForSeat(Charlie, RetiredVoices.Seat.Announcer)!.Id, Engine = VoiceEngine.ElevenLabs };

        Assert.NotEqual(VoiceLibrary.KeyFor(old, "Green flag!"), VoiceLibrary.KeyFor(moved, "Green flag!"));
    }

    [Fact]
    public void A_crew_pick_moves_to_elevenlabs_replacement_and_callum_is_cleared()
    {
        var jessica = new CrewVoicePick { Engine = "elevenlabs", Id = Jessica, Name = "Jessica" };
        var callum = new CrewVoicePick { Engine = "elevenlabs", Id = Callum, Name = "Callum" };
        var kokoro = new CrewVoicePick { Engine = "kokoro", Id = Jessica, Name = "odd" };

        Assert.False(RetiredVoices.SwitchCrewPick(jessica, Before).Changed);

        var (moved, changed) = RetiredVoices.SwitchCrewPick(jessica, After);
        Assert.True(changed);
        Assert.Equal(("g7LVvkPWALzPxOQbF6OE", "Jade - Upbeat and Natural"), (moved!.Id, moved.Name));
        Assert.Equal(Jessica, jessica.Id);

        Assert.Equal((null, true), RetiredVoices.SwitchCrewPick(callum, After));
        Assert.False(RetiredVoices.SwitchCrewPick(kokoro, After).Changed);
        Assert.False(RetiredVoices.SwitchCrewPick(null, After).Changed);
    }

    [Fact]
    public void Only_drivers_with_a_retired_crew_voice_are_saved_and_as_copies()
    {
        var on = new DriverProfile { Transponder = "1", DriverName = "A", CrewTechVoice = new CrewVoicePick { Engine = "elevenlabs", Id = Jessica } };
        var off = new DriverProfile { Transponder = "2", DriverName = "B", CrewTechVoice = new CrewVoicePick { Engine = "elevenlabs", Id = "own" } };

        Assert.Empty(RetiredVoices.SwitchCrew(new[] { on, off }, Before));

        var changed = Assert.Single(RetiredVoices.SwitchCrew(new[] { on, off }, After));
        Assert.Equal("g7LVvkPWALzPxOQbF6OE", changed.CrewTechVoice!.Id);
        Assert.Equal(Jessica, on.CrewTechVoice!.Id);
    }

    [Fact]
    public void A_crew_pick_moved_to_a_library_voice_still_counts_as_there_and_reserved_still_works()
    {
        var keys = MainForm.CrewKeysStillThere(Array.Empty<CrewVoiceOption>(), VoiceEngine.ElevenLabs);
        var jade = new CrewVoicePick { Engine = "elevenlabs", Id = "g7LVvkPWALzPxOQbF6OE" };

        Assert.True(MainForm.CrewVoiceStillOffered(keys, VoiceEngine.ElevenLabs, jade));
        Assert.Empty(MainForm.CrewKeysStillThere(Array.Empty<CrewVoiceOption>(), VoiceEngine.Kokoro));

        // HeatCaller's cast as the club's seats: the co-host's Alicia is never offered to drivers.
        var settings = new AppSettings();
        CastControl.ApplyHeatCallersCast(settings);
        var reserved = DriverCrew.Reserved(settings, string.Empty, VoiceEngine.ElevenLabs);
        var offered = DriverCrew.Pickable(MainForm.CrewVoicesWithSuggested(Array.Empty<CrewVoiceOption>()), Array.Empty<string>(), reserved);

        Assert.DoesNotContain(offered, o => o.Pick.Id == "BFd5oBc2DDna33pSi4Gf");
        Assert.Contains(offered, o => o.Pick.Id == "FrS6cKLB1wg4WYgPa9GW");
    }

    [Fact]
    public void The_crew_list_gets_the_six_once_each()
    {
        var account = new[] { new CrewVoiceOption(new CrewVoicePick { Engine = "elevenlabs", Id = "fnYMz3F5gMEDGMWcH1ex", Name = "Finley" }, "http://p") };
        var list = MainForm.CrewVoicesWithSuggested(account);

        Assert.Equal(6, list.Count);
        Assert.Single(list, v => v.Pick.Id == "fnYMz3F5gMEDGMWcH1ex");
        Assert.Equal("http://p", list[0].Sample);
    }

    // ------------------------------------------------------------------
    // D. The pickers
    // ------------------------------------------------------------------

    [Fact]
    public void The_cast_lists_and_the_announcer_list_offer_the_six_marked_and_never_twice()
    {
        var listed = new[] { new CastControl.VoiceChoice("BFd5oBc2DDna33pSi4Gf", "Alicia", string.Empty, "Alicia") };
        var suggested = CastControl.SuggestedChoices(listed).ToList();

        Assert.Equal(5, suggested.Count);
        Assert.All(suggested, c => Assert.StartsWith("Suggested by HeatCaller: ", c.Display));
        Assert.DoesNotContain(suggested, c => c.Id == "BFd5oBc2DDna33pSi4Gf");
        Assert.Equal("Finley - Articulate Anchor", suggested[0].Name);

        var announcer = VoiceSettingsControl.SuggestedVoiceInfos(new[] { new ElevenLabsClient.VoiceInfo("fnYMz3F5gMEDGMWcH1ex", "Finley") }).ToList();
        Assert.Equal(5, announcer.Count);
        Assert.All(announcer, v => Assert.StartsWith("Suggested by HeatCaller: ", v.Shown));
        Assert.DoesNotContain(":", announcer[0].Name);
    }

    [Fact]
    public void Use_heatcallers_cast_fills_every_seat_and_speaks_in_it()
    {
        var settings = new AppSettings { CoHostKokoroAlways = true, CoHostKokoroVoiceName = "af_bella" };

        var announcer = CastControl.ApplyHeatCallersCast(settings);

        Assert.Equal("fnYMz3F5gMEDGMWcH1ex", announcer.Id);
        Assert.Equal("BFd5oBc2DDna33pSi4Gf", settings.CoHostElevenLabsVoiceId);
        Assert.Equal("FrS6cKLB1wg4WYgPa9GW", settings.MechanicElevenLabsVoiceId);
        Assert.Equal("gOupLcAkjEnguROwi4oS", settings.PrincipalElevenLabsVoiceId);
        Assert.Equal("dvbL7qkNGZY1IqPGZAjM", settings.HonoursElevenLabsVoiceId);
        Assert.Equal("jSuBIjxMKhqIfb0wCK1F", settings.RecapHostElevenLabsVoiceId);
        Assert.False(settings.CoHostKokoroAlways);

        var question = CastControl.HeatCallersCastQuestion();
        Assert.Contains("Announcer: Finley - Articulate Anchor", question);
        Assert.Contains("paid ElevenLabs plan", question);
    }

    [Fact]
    public void A_seat_shows_the_retirement_line_with_switch_or_the_gone_line_with_pick_another()
    {
        var onCharlie = new SecondVoice(Charlie, "Charlie", "", "", "");
        var (words, offer) = CastControl.SeatNoticeFor(VoiceEngine.ElevenLabs, onCharlie, RetiredVoices.Seat.TeamPrincipal, Before, null);
        Assert.Equal("ElevenLabs retires Charlie on 31 December. Switch to Darian?", words);
        Assert.True(offer);

        // Another engine's list: nothing to say.
        Assert.Null(CastControl.SeatNoticeFor(VoiceEngine.Kokoro, onCharlie, RetiredVoices.Seat.TeamPrincipal, Before, null).Words);

        var deleted = new SecondVoice("deleted-id", "Old Pal", "", "", "");
        var gone = CastControl.SeatNoticeFor(VoiceEngine.ElevenLabs, deleted, RetiredVoices.Seat.CoHost, Before,
            id => id == "deleted-id" ? ElevenLabsClient.VoiceRefusal.NotFound : null);
        Assert.Equal("ElevenLabs no longer has the voice Old Pal. Pick another.", gone.Words);
        Assert.False(gone.Offer);

        var paid = CastControl.SeatNoticeFor(VoiceEngine.ElevenLabs, deleted, RetiredVoices.Seat.CoHost, Before,
            _ => ElevenLabsClient.VoiceRefusal.NeedsPaidPlan);
        Assert.StartsWith("Library voices need a paid ElevenLabs plan, so Old Pal can't speak on this key. Pick another,", paid.Words);

        Assert.Null(CastControl.SeatNoticeFor(VoiceEngine.ElevenLabs, deleted, RetiredVoices.Seat.CoHost, Before, _ => null).Words);
    }

    // ------------------------------------------------------------------
    // C. A dead voice, said plainly once
    // ------------------------------------------------------------------

    private static VoiceProfile Profile(string key = "key-1", string voice = Charlie, string name = "Charlie") => new()
    {
        ApiKey = key,
        VoiceId = voice,
        VoiceName = name,
        Engine = VoiceEngine.ElevenLabs,
    };

    [Theory]
    [InlineData(404, VoiceNotFound, 0)]
    [InlineData(400, VoiceNotFound, 0)]
    [InlineData(402, "{\"detail\":{\"status\":\"free_users_not_allowed\",\"message\":\"Free users cannot use library voices via the API.\"}}", 1)]
    [InlineData(401, "{\"detail\":{\"status\":\"free_users_not_allowed\"}}", 1)]
    public void A_refusal_about_the_voice_is_read_for_what_it_is(int status, string body, int why)
    {
        Assert.Equal((ElevenLabsClient.VoiceRefusal)why, ElevenLabsClient.VoiceRefusalOf(status, body));
    }

    [Theory]
    [InlineData(400, "{\"detail\":\"no\"}")]
    [InlineData(401, "{\"detail\":{\"status\":\"quota_exceeded\"}}")]
    [InlineData(500, VoiceNotFound)]
    public void Any_other_refusal_says_nothing_about_the_voice(int status, string body)
    {
        Assert.Null(ElevenLabsClient.VoiceRefusalOf(status, body));
    }

    [Fact]
    public async Task A_voice_elevenlabs_no_longer_has_is_not_retried_without_the_flag_and_not_asked_again()
    {
        var server = Serve(new Answer(HttpStatusCode.BadRequest, VoiceNotFound));

        var first = await Assert.ThrowsAsync<ElevenLabsClient.VoiceGoneException>(() => new ElevenLabsClient().SynthesizeAsync(Profile(), "Green flag!"));
        Assert.Equal("ElevenLabs no longer has the voice Charlie. Pick another on Voice > Cast.", first.Plain);
        Assert.Contains("(400)", first.Message);
        Assert.Single(server.Requests);

        // Rested: the next line in it - whole, streamed or timed - makes no request.
        await Assert.ThrowsAsync<ElevenLabsClient.VoiceGoneException>(() => new ElevenLabsClient().SynthesizeAsync(Profile(), "Yellow flag!"));
        await Assert.ThrowsAsync<ElevenLabsClient.VoiceGoneException>(() => new ElevenLabsClient().OpenSpeechStreamAsync(Profile(), "Yellow flag!"));
        Assert.Null(await new ElevenLabsClient().SynthesizeWithTimestampsAsync(Profile(), "Yellow flag!"));
        Assert.Single(server.Requests);
        Assert.True(ElevenLabsClient.IsVoiceGone(Profile()));

        // Said once, plainly.
        Assert.Single(_said);
        Assert.Contains("ElevenLabs no longer has the voice Charlie. Pick another on Voice > Cast.", _said[0]);
        Assert.Contains(ElevenLabsClient.GoneVoiceWords(), w => w.Contains("Charlie"));

        // Only that voice: another on the same key is still asked, and the no-logging flag is not taken as refused.
        await new ElevenLabsClient().SynthesizeAsync(Profile(voice: "other", name: "Other"), "Green flag!");
        Assert.Equal(2, server.Requests.Count);
        Assert.Contains("enable_logging=false", server.Requests[1].Url);
        Assert.True(ElevenLabsClient.AsksNotToLog("key-1"));

        // And the same voice on another key is that key's own business.
        Assert.False(ElevenLabsClient.IsVoiceGone(Profile(key: "key-2")));
    }

    [Fact]
    public async Task A_dead_voice_found_by_the_timestamps_call_is_rested_too()
    {
        var server = Serve(new Answer(HttpStatusCode.NotFound, VoiceNotFound));

        Assert.Null(await new ElevenLabsClient().SynthesizeWithTimestampsAsync(Profile(), "Car seven."));
        Assert.True(ElevenLabsClient.IsVoiceGone(Profile()));
        Assert.Single(server.Requests);
    }

    [Fact]
    public async Task A_library_voice_on_a_free_plan_is_said_as_needing_a_paid_plan()
    {
        Serve(new Answer(HttpStatusCode.PaymentRequired, "{\"detail\":{\"status\":\"free_users_not_allowed\",\"message\":\"Free users cannot use library voices via the API.\"}}"));

        var refused = await Assert.ThrowsAsync<ElevenLabsClient.VoiceGoneException>(
            () => new ElevenLabsClient().SynthesizeAsync(Profile(voice: "fnYMz3F5gMEDGMWcH1ex", name: "Finley - Articulate Anchor"), "Hello."));

        Assert.StartsWith("Library voices need a paid ElevenLabs plan", refused.Plain);
        Assert.Equal(ElevenLabsClient.VoiceRefusal.NeedsPaidPlan, refused.Why);
    }

    [Fact]
    public void Every_path_that_speaks_a_line_consults_the_rest()
    {
        var synthesis = Source("AnnouncementService.Synthesis.cs");
        var gone = synthesis.IndexOf("catch (ElevenLabsClient.VoiceGoneException)", StringComparison.Ordinal);
        Assert.True(gone > 0 && gone < synthesis.IndexOf("catch (Exception ex) when (IsQuotaExceeded(ex))", gone, StringComparison.Ordinal),
            "a dead voice is caught before the quota and key refusals");
        Assert.Contains("if (ElevenLabsClient.IsVoiceGone(profile))", synthesis);
        Assert.Contains("ElevenLabsClient.IsVoiceGone(profile)", Source("AnnouncementService.Streaming.cs"));
    }

    // ------------------------------------------------------------------
    // H. Each line deleted from the history
    // ------------------------------------------------------------------

    [Fact]
    public async Task A_whole_line_is_deleted_from_the_history_by_the_id_in_its_own_answer()
    {
        var server = Serve(new Answer(HttpStatusCode.OK, HistoryId: "hist-1"), new Answer(HttpStatusCode.NotFound));

        var audio = await new ElevenLabsClient().SynthesizeAsync(Profile(), "Green flag!");
        await ElevenLabsClient.HistoryDeletesSettledAsync();

        Assert.Equal(new byte[] { 1, 2, 3 }, audio);
        Assert.Equal(2, server.Requests.Count);
        Assert.Equal(HttpMethod.Delete, server.Requests[1].Method);
        Assert.Equal("/v1/history/hist-1", server.Requests[1].Url);
        Assert.Equal("key-1", server.Requests[1].Key);
        Assert.Empty(_said);
    }

    [Fact]
    public async Task Nothing_is_deleted_without_a_history_id_or_with_the_box_unticked()
    {
        var server = Serve(new Answer(HttpStatusCode.OK), new Answer(HttpStatusCode.OK, HistoryId: "hist-2"));

        await new ElevenLabsClient().SynthesizeAsync(Profile(), "One.");
        ElevenLabsClient.AskNotToKeepLines = () => false;
        await new ElevenLabsClient().SynthesizeAsync(Profile(), "Two.");
        await ElevenLabsClient.HistoryDeletesSettledAsync();

        Assert.Equal(2, server.Requests.Count);
        Assert.All(server.Requests, r => Assert.Equal(HttpMethod.Post, r.Method));
    }

    [Fact]
    public async Task A_key_without_the_speech_history_permission_is_said_once_and_not_asked_again()
    {
        var server = Serve(
            new Answer(HttpStatusCode.OK, HistoryId: "h1"),
            new Answer(HttpStatusCode.Unauthorized, MissingPermission),
            new Answer(HttpStatusCode.OK, HistoryId: "h2"));

        await new ElevenLabsClient().SynthesizeAsync(Profile(), "One.");
        await ElevenLabsClient.HistoryDeletesSettledAsync();
        await new ElevenLabsClient().SynthesizeAsync(Profile(), "Two.");
        await ElevenLabsClient.HistoryDeletesSettledAsync();

        Assert.Equal(3, server.Requests.Count);
        Assert.Equal(HttpMethod.Post, server.Requests[2].Method);
        var said = Assert.Single(_said);
        Assert.Contains("This ElevenLabs key can't remove lines from its history. Give it the Speech history permission on elevenlabs.io.", said);
    }

    [Fact]
    public async Task A_streamed_line_is_deleted_once_the_stream_is_given_back()
    {
        var server = Serve(new Answer(HttpStatusCode.OK, HistoryId: "stream-1"), new Answer(HttpStatusCode.OK));

        var response = await new ElevenLabsClient().OpenSpeechStreamAsync(Profile(), "Green flag!");
        await ElevenLabsClient.HistoryDeletesSettledAsync();
        Assert.Single(server.Requests);

        Assert.Equal(new byte[] { 1, 2, 3 }, await response.Content.ReadAsByteArrayAsync());
        response.Dispose();
        response.Dispose();
        await ElevenLabsClient.HistoryDeletesSettledAsync();

        Assert.Equal(2, server.Requests.Count);
        Assert.Equal("/v1/history/stream-1", server.Requests[1].Url);
    }

    [Fact]
    public async Task A_timed_line_is_deleted_too()
    {
        const string timed = "{\"audio_base64\":\"AQID\",\"alignment\":{\"characters\":[\"a\"],\"character_start_times_seconds\":[0],\"character_end_times_seconds\":[0.1]}}";
        var server = Serve(new Answer(HttpStatusCode.OK, timed, HistoryId: "timed-1"), new Answer(HttpStatusCode.OK));

        Assert.NotNull(await new ElevenLabsClient().SynthesizeWithTimestampsAsync(Profile(), "a"));
        await ElevenLabsClient.HistoryDeletesSettledAsync();

        Assert.Equal("/v1/history/timed-1", server.Requests[1].Url);
    }

    [Fact]
    public void The_page_says_lines_leave_the_history()
    {
        var page = Source("VoiceSettingsControl.cs");
        Assert.Contains("\"Ask ElevenLabs not to keep our lines, and remove each from its history\"", page);
        Assert.Contains("ElevenLabs may keep backups for up to 30 days", page);
    }

    // ------------------------------------------------------------------
    // A fake ElevenLabs
    // ------------------------------------------------------------------

    private sealed record Answer(HttpStatusCode Status, string? Body = null, string? HistoryId = null);

    private sealed record Seen(HttpMethod Method, string Url, string? Key);

    private static FakeElevenLabs Serve(params Answer[] answers)
    {
        var server = new FakeElevenLabs(answers);
        ElevenLabsClient.Http = new HttpClient(server) { BaseAddress = new Uri("https://api.elevenlabs.io/") };
        return server;
    }

    private sealed class FakeElevenLabs(Answer[] answers) : HttpMessageHandler
    {
        private int _next;

        public List<Seen> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Answer answer;

            lock (Requests)
            {
                Requests.Add(new Seen(request.Method, request.RequestUri!.PathAndQuery,
                    request.Headers.TryGetValues("xi-api-key", out var keys) ? keys.FirstOrDefault() : null));
                answer = _next < answers.Length ? answers[_next++] : new Answer(HttpStatusCode.OK);
            }

            var response = new HttpResponseMessage(answer.Status)
            {
                Content = answer.Body is { } body ? new StringContent(body) : new ByteArrayContent(new byte[] { 1, 2, 3 }),
            };

            if (answer.HistoryId is { } id)
            {
                response.Headers.TryAddWithoutValidation(ElevenLabsClient.HistoryItemHeader, id);
            }

            return Task.FromResult(response);
        }
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
