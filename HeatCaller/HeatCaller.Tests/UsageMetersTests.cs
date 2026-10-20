using System.Globalization;
using System.Net;
using System.Text;

namespace HeatCaller.Tests;

/// <summary>
/// The monthly allowance meters (19 Oct 2026): what each meter says, how
/// "tonight" is counted across a race night and a new month, that the 80%
/// and 100% notices go once a month even across a restart, reading the
/// relay's and ElevenLabs' answers, and a top-up request falling back to a
/// prefilled email when the club has no mailbox set up.
/// </summary>
public sealed class UsageMetersTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly HttpClient _realRelayHttp = UsageMeters.Http;
    private readonly HttpClient _realElevenLabsHttp = ElevenLabsClient.SubscriptionHttp;
    private readonly Action<string> _realOpen = TopUpRequest.OpenMailTo;

    /// <summary>The test clock, in UTC.</summary>
    private DateTime _now = Local(2026, 10, 19, 19, 0);

    private static readonly DateTime NovemberResets = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DecemberResets = new(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        UsageMeters.Http = _realRelayHttp;
        ElevenLabsClient.SubscriptionHttp = _realElevenLabsHttp;
        TopUpRequest.OpenMailTo = _realOpen;
        _data.Dispose();
    }

    /// <summary>A local wall-clock time on the track's PC, as UTC - so the 6am night boundary is the same in any time zone the tests run in.</summary>
    private static DateTime Local(int year, int month, int day, int hour, int minute) =>
        new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Local).ToUniversalTime();

    /// <summary>Reads a display string with the invariant culture, so "34,120" and "1 Nov" read the same on any PC running the tests.</summary>
    private static string? Invariant(Func<string?> read)
    {
        var before = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            return read();
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    private UsageMeters NewMeters() => new(AppPaths.Root, () => _now);

    private static Meter Commentary(long used, long allowance, long tonight = 0, bool known = true, DateTime? resets = null) =>
        new(MeterKind.Commentary, known, used, allowance, 0, resets, DateTime.UtcNow, tonight, known ? null : "Couldn't ask.");

    // --- What a meter says --------------------------------------------------

    [Fact]
    public void Levels_follow_eighty_and_one_hundred_percent()
    {
        Assert.Equal(MeterLevel.Ok, Commentary(359, 450).Level);
        Assert.Equal(MeterLevel.Watch, Commentary(360, 450).Level);
        Assert.Equal(MeterLevel.Watch, Commentary(449, 450).Level);
        Assert.Equal(MeterLevel.Spent, Commentary(450, 450).Level);
        Assert.Equal(MeterLevel.Spent, Commentary(470, 450).Level);
        Assert.True(Commentary(450, 450).Spent);

        // Nothing on the plan, or not read: no level to be at.
        Assert.Equal(MeterLevel.Unknown, Commentary(10, 0).Level);
        Assert.False(Commentary(10, 0).Spent);
        Assert.Equal(MeterLevel.Unknown, Commentary(0, 0, known: false).Level);

        Assert.Equal(0, Commentary(10, 0).Fraction);
        Assert.Equal(0, Commentary(500, 450).Remaining);
    }

    [Fact]
    public void Summary_forecast_and_percent_read_as_the_window_shows_them()
    {
        // Midday UTC, so the local date is 1 Nov wherever the tests run.
        var resets = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
        var meter = Commentary(212, 450, tonight: 38, resets: resets);

        Assert.Equal("212 of 450 lines · 38 tonight · starts again 1 Nov", Invariant(meter.Summary));
        Assert.Equal("About 6 more race nights like tonight.", Invariant(meter.Forecast));
        Assert.Equal("47%", meter.ShortPercent());

        // Nothing tonight yet: no "0 tonight", and no forecast from nothing.
        var quiet = Commentary(212, 450);
        Assert.Equal("212 of 450 lines", Invariant(quiet.Summary));
        Assert.Null(quiet.Forecast());

        // One line short of the end is not 100%.
        Assert.Equal("99%", Commentary(449, 450).ShortPercent());

        // Counted in whole numbers: 29 / 100 * 100 as a double is 28.999...
        Assert.Equal("29%", Commentary(29, 100).ShortPercent());
        Assert.Equal("57%", Commentary(57, 100).ShortPercent());
        Assert.Equal("Not quite enough left for another race night like tonight.", Commentary(440, 450, tonight: 30).Forecast());
        Assert.Equal("Used up for this month.", Commentary(450, 450, tonight: 30).Forecast());

        var unread = Commentary(0, 0, known: false);
        Assert.Equal("?", unread.ShortPercent());
        Assert.Equal("Couldn't ask.", unread.Summary());
        Assert.Null(unread.Forecast());
        Assert.Equal("-", Commentary(5, 0).ShortPercent());
    }

    [Fact]
    public void Names_units_and_who_can_ask_for_more()
    {
        var now = DateTime.UtcNow;
        var voice = new Meter(MeterKind.HeatCallerVoice, true, 34120, 100000, 0, null, now, 0, null);
        var key = new Meter(MeterKind.ElevenLabsKey, true, 1, 2, 0, null, now, 0, null);

        Assert.Equal("AI commentary", Commentary(1, 2).Name);
        Assert.Equal("lines", Commentary(1, 2).Unit);
        Assert.Equal("HeatCaller voice", voice.Name);
        Assert.Equal("characters", voice.Unit);
        Assert.Equal("34,120 of 100,000 characters", Invariant(voice.Summary));
        Assert.Equal("ElevenLabs (your own key)", key.Name);

        Assert.True(Commentary(1, 2).CanAskForMore);
        Assert.True(voice.CanAskForMore);
        Assert.False(key.CanAskForMore);
    }

    // --- Tonight, and a new month -------------------------------------------

    [Fact]
    public void Tonight_counts_from_the_first_figure_of_the_race_night()
    {
        var meters = NewMeters();

        meters.Report(MeterKind.Commentary, 100, 450, NovemberResets);
        Assert.Equal(0, meters.Get(MeterKind.Commentary)!.UsedTonight);

        _now = Local(2026, 10, 19, 21, 0);
        meters.Report(MeterKind.Commentary, 138, 450, NovemberResets);
        Assert.Equal(38, meters.Get(MeterKind.Commentary)!.UsedTonight);

        // Five in the morning is still the same race night.
        _now = Local(2026, 10, 20, 5, 0);
        meters.Report(MeterKind.Commentary, 150, 450, NovemberResets);
        Assert.Equal(50, meters.Get(MeterKind.Commentary)!.UsedTonight);

        // Past 6am it is a new one, before anything is reported...
        _now = Local(2026, 10, 20, 7, 0);
        Assert.Equal(0, meters.Get(MeterKind.Commentary)!.UsedTonight);
        Assert.Equal(150, meters.Get(MeterKind.Commentary)!.Used);

        // ...and after.
        _now = Local(2026, 10, 20, 19, 0);
        meters.Report(MeterKind.Commentary, 160, 450, NovemberResets);
        Assert.Equal(0, meters.Get(MeterKind.Commentary)!.UsedTonight);

        _now = Local(2026, 10, 20, 20, 0);
        meters.Report(MeterKind.Commentary, 171, 450, NovemberResets);
        Assert.Equal(11, meters.Get(MeterKind.Commentary)!.UsedTonight);
    }

    [Fact]
    public void A_new_month_mid_meeting_carries_tonight_across_it()
    {
        var meters = NewMeters();

        meters.Report(MeterKind.Commentary, 400, 450, NovemberResets);
        meters.Report(MeterKind.Commentary, 430, 450, NovemberResets);
        Assert.Equal(30, meters.Get(MeterKind.Commentary)!.UsedTonight);

        // The month turns over at midnight UTC, mid-meeting: the 5 lines of
        // the new month are tonight's too, on top of the 30 before it.
        meters.Report(MeterKind.Commentary, 5, 450, DecemberResets);
        Assert.Equal(35, meters.Get(MeterKind.Commentary)!.UsedTonight);
        Assert.Equal(5, meters.Get(MeterKind.Commentary)!.Used);

        meters.Report(MeterKind.Commentary, 25, 450, DecemberResets);
        Assert.Equal(55, meters.Get(MeterKind.Commentary)!.UsedTonight);

        // And it survives a restart, start below zero and all.
        var again = NewMeters();
        Assert.Equal(55, again.Get(MeterKind.Commentary)!.UsedTonight);

        // The next race night starts from nothing, as ever.
        _now = Local(2026, 10, 20, 19, 0);
        again.Report(MeterKind.Commentary, 30, 450, DecemberResets);
        Assert.Equal(0, again.Get(MeterKind.Commentary)!.UsedTonight);
    }

    [Fact]
    public void A_late_figure_one_lower_keeps_tonights_start()
    {
        var meters = NewMeters();

        meters.Report(MeterKind.Commentary, 400, 450, NovemberResets);
        meters.Report(MeterKind.Commentary, 430, 450, NovemberResets);

        // The five-minute usage answer, read on the relay just before the
        // last line's own figure and landing just after it.
        meters.Report(MeterKind.Commentary, 429, 450, NovemberResets);
        Assert.Equal(29, meters.Get(MeterKind.Commentary)!.UsedTonight);

        meters.Report(MeterKind.Commentary, 431, 450, NovemberResets);
        Assert.Equal(31, meters.Get(MeterKind.Commentary)!.UsedTonight);
    }

    [Fact]
    public void A_figure_without_its_reset_date_keeps_the_one_it_had()
    {
        var meters = NewMeters();

        meters.Report(MeterKind.HeatCallerVoice, 1000, 100000, NovemberResets, topUp: 20000);
        meters.Report(MeterKind.HeatCallerVoice, 1200, 100000, null);

        var meter = meters.Get(MeterKind.HeatCallerVoice)!;
        Assert.Equal(NovemberResets, meter.ResetsUtc);
        Assert.Equal(20000, meter.TopUp);
        Assert.Equal(200, meter.UsedTonight);
    }

    [Fact]
    public void Changed_is_raised_only_when_something_changed()
    {
        var meters = NewMeters();
        var changes = 0;
        meters.Changed += () => changes++;

        meters.Report(MeterKind.Commentary, 100, 450, NovemberResets);
        meters.Report(MeterKind.Commentary, 100, 450, NovemberResets);
        Assert.Equal(1, changes);

        meters.Report(MeterKind.Commentary, 101, 450, NovemberResets);
        Assert.Equal(2, changes);

        meters.Forget(MeterKind.Commentary);
        meters.Forget(MeterKind.Commentary);
        Assert.Equal(3, changes);
        Assert.Null(meters.Get(MeterKind.Commentary));
        Assert.Empty(meters.All);
    }

    // --- Remembered ---------------------------------------------------------

    [Fact]
    public void Figures_and_tonight_survive_a_restart()
    {
        var first = NewMeters();
        first.Report(MeterKind.Commentary, 100, 450, NovemberResets, topUp: 50);
        first.Report(MeterKind.Commentary, 140, 450, NovemberResets);
        first.Unavailable(MeterKind.ElevenLabsKey, "Can't read it.");
        first.Save();

        Assert.True(File.Exists(_data.PathOf("usage-meters.json")));

        var again = NewMeters();
        var meter = again.Get(MeterKind.Commentary)!;

        Assert.True(meter.Known);
        Assert.Equal(140, meter.Used);
        Assert.Equal(450, meter.Allowance);
        Assert.Equal(50, meter.TopUp);
        Assert.Equal(NovemberResets, meter.ResetsUtc);
        Assert.Equal(DateTimeKind.Utc, meter.ResetsUtc!.Value.Kind);
        Assert.Equal(40, meter.UsedTonight);

        var key = again.Get(MeterKind.ElevenLabsKey)!;
        Assert.False(key.Known);
        Assert.Equal("Can't read it.", key.Note);

        Assert.Equal(new[] { MeterKind.Commentary, MeterKind.ElevenLabsKey }, again.All.Select(m => m.Kind));
    }

    [Fact]
    public void A_damaged_file_starts_empty_rather_than_failing()
    {
        File.WriteAllText(_data.PathOf("usage-meters.json"), "{ not json");

        var meters = NewMeters();

        Assert.Empty(meters.All);
        meters.Report(MeterKind.Commentary, 1, 450, NovemberResets);
        Assert.Equal(1, meters.Get(MeterKind.Commentary)!.Used);

        // Which emails had gone this month was in it, so none is sent now.
        Assert.True(meters.NoticesUnknown);
    }

    [Fact]
    public void An_unreadable_file_sends_no_email_that_may_have_gone_already()
    {
        File.WriteAllText(_data.PathOf("usage-meters.json"), "{ not json");

        var logged = new List<string>();
        var meters = NewMeters();
        meters.Initialise(new AppSettings(), logged.Add);

        meters.Report(MeterKind.Commentary, 450, 450, NovemberResets);

        Assert.True(meters.Get(MeterKind.Commentary)!.Spent);
        Assert.Contains(logged, line => line.Contains("not emailed: the record of which emails have already gone", StringComparison.Ordinal));
    }

    [Fact]
    public void A_file_locked_at_startup_is_not_written_over()
    {
        var path = _data.PathOf("usage-meters.json");
        var first = NewMeters();
        first.Report(MeterKind.Commentary, 450, 450, NovemberResets);
        first.Save();
        var saved = File.ReadAllText(path);

        UsageMeters again;

        // Held open, as a backup or sync tool does, while it is first read.
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            again = NewMeters();
            Assert.Empty(again.All);
        }

        again.Report(MeterKind.Commentary, 10, 450, DecemberResets);
        again.Save();

        // The record of the emails already sent is still there for the next start.
        Assert.Equal(saved, File.ReadAllText(path));
        Assert.True(again.NoticesUnknown);
    }

    [Fact]
    public void Each_level_is_reached_once_a_month_even_across_a_restart()
    {
        var reached = new List<(MeterKind Kind, MeterLevel Level)>();

        var first = NewMeters();
        first.LevelReached += (meter, level) => reached.Add((meter.Kind, level));

        first.Report(MeterKind.Commentary, 300, 450, NovemberResets);
        Assert.Empty(reached);

        first.Report(MeterKind.Commentary, 360, 450, NovemberResets);
        first.Report(MeterKind.Commentary, 370, 450, NovemberResets);
        Assert.Equal(new[] { (MeterKind.Commentary, MeterLevel.Watch) }, reached);

        // A restart: the 80% one is not sent again.
        var again = NewMeters();
        again.LevelReached += (meter, level) => reached.Add((meter.Kind, level));

        again.Report(MeterKind.Commentary, 380, 450, NovemberResets);
        Assert.Single(reached);

        again.Report(MeterKind.Commentary, 450, 450, NovemberResets);
        again.Report(MeterKind.Commentary, 460, 450, NovemberResets);
        Assert.Equal((MeterKind.Commentary, MeterLevel.Spent), reached[^1]);
        Assert.Equal(2, reached.Count);

        // A top-up brings it back under 100% - and does not send 80% again.
        again.Report(MeterKind.Commentary, 460, 500, NovemberResets, topUp: 50);
        Assert.Equal(2, reached.Count);

        // A new month is a new month.
        again.Report(MeterKind.Commentary, 400, 450, DecemberResets);
        Assert.Equal((MeterKind.Commentary, MeterLevel.Watch), reached[^1]);
        Assert.Equal(3, reached.Count);
    }

    [Fact]
    public void A_meter_that_goes_straight_to_spent_raises_spent_only()
    {
        var reached = new List<MeterLevel>();
        var meters = NewMeters();
        meters.LevelReached += (_, level) => reached.Add(level);

        meters.Report(MeterKind.HeatCallerVoice, 100000, 100000, NovemberResets);
        meters.Report(MeterKind.HeatCallerVoice, 90000, 120000, NovemberResets);

        Assert.Equal(new[] { MeterLevel.Spent }, reached);
    }

    [Fact]
    public void A_failing_subscriber_never_reaches_whoever_reported()
    {
        var meters = NewMeters();
        meters.Changed += () => throw new InvalidOperationException("boom");
        meters.LevelReached += (_, _) => throw new InvalidOperationException("boom");

        meters.Report(MeterKind.Commentary, 450, 450, NovemberResets);

        Assert.True(meters.Get(MeterKind.Commentary)!.Spent);
    }

    // --- Asking the relay ---------------------------------------------------

    private static AppSettings RelaySettings() => new()
    {
        RelayBaseUrl = "https://relay.example.com",
        RelayClub = "acme-rc",
        RelayToken = "club-token",
        CommentaryEnabled = true,
        CommentaryUseRelay = true,
    };

    private static VoiceProfile Voice(VoiceEngine engine, string key = "") => new() { Engine = engine, ApiKey = key };

    private const string UsageAnswer = """
        { "ok": true,
          "commentary": { "enabled": true, "used": 212, "allowance": 450, "topUp": 50, "resetsAt": "2026-11-01T00:00:00.000Z" },
          "voice":      { "enabled": true, "used": 34120, "allowance": 100000, "topUp": 0, "resetsAt": null },
          "requests":   [ { "id": "r_1", "meter": "commentary", "askedAt": "2026-10-19T20:01:00.000Z", "note": "Big night", "status": "open" },
                          { "id": "r_0", "meter": "voice", "askedAt": "2026-10-02T18:00:00.000Z", "note": "", "status": "done" } ] }
        """;

    [Fact]
    public async Task Refresh_reads_the_relays_usage_answer()
    {
        var relay = new FakeServer(HttpStatusCode.OK, UsageAnswer);
        UsageMeters.Http = new HttpClient(relay);

        var meters = NewMeters();
        await meters.RefreshAsync(RelaySettings(), Voice(VoiceEngine.HeatCaller));

        // One relay for both: asked once, as the club, with its token.
        var asked = Assert.Single(relay.Asked);
        Assert.Equal("https://relay.example.com/api/v1/c/acme-rc/usage", asked.Url);
        Assert.Equal("Bearer club-token", asked.Authorization);

        var commentary = meters.Get(MeterKind.Commentary)!;
        Assert.True(commentary.Known);
        Assert.Equal(212, commentary.Used);
        Assert.Equal(450, commentary.Allowance);
        Assert.Equal(50, commentary.TopUp);
        Assert.Equal(NovemberResets, commentary.ResetsUtc);

        var voice = meters.Get(MeterKind.HeatCallerVoice)!;
        Assert.Equal(34120, voice.Used);
        Assert.Equal(100000, voice.Allowance);
        Assert.Null(voice.ResetsUtc);

        Assert.Equal(2, meters.Requests.Count);
        Assert.Equal("r_1", meters.Requests[0].Id);
        Assert.Equal(MeterKind.Commentary, meters.Requests[0].Kind);
        Assert.True(meters.Requests[0].Open);
        Assert.Equal("Big night", meters.Requests[0].Note);
        Assert.Equal(MeterKind.HeatCallerVoice, meters.Requests[1].Kind);
        Assert.False(meters.Requests[1].Open);
        Assert.Null(meters.LastProblem);
    }

    [Fact]
    public async Task Refresh_forgets_a_meter_the_plan_does_not_include_and_one_switched_off()
    {
        UsageMeters.Http = new HttpClient(new FakeServer(HttpStatusCode.OK, """
            { "ok": true,
              "commentary": { "enabled": true, "used": 10, "allowance": 450, "topUp": 0, "resetsAt": "2026-11-01T00:00:00.000Z" },
              "voice": { "enabled": false, "used": 0, "allowance": 0, "topUp": 0, "resetsAt": null },
              "requests": [] }
            """));

        var meters = NewMeters();
        meters.Report(MeterKind.HeatCallerVoice, 5, 100000, NovemberResets);
        meters.Report(MeterKind.ElevenLabsKey, 5, 10000, NovemberResets);

        await meters.RefreshAsync(RelaySettings(), Voice(VoiceEngine.HeatCaller));

        Assert.NotNull(meters.Get(MeterKind.Commentary));
        Assert.Null(meters.Get(MeterKind.HeatCallerVoice));

        // Not on ElevenLabs any more: that meter goes too.
        Assert.Null(meters.Get(MeterKind.ElevenLabsKey));

        // Commentary switched off on this PC: its meter goes, even though
        // the relay would still answer for it.
        var off = RelaySettings();
        off.CommentaryUseRelay = false;
        await meters.RefreshAsync(off, Voice(VoiceEngine.Kokoro));

        Assert.Empty(meters.All);
        Assert.Empty(meters.Requests);
    }

    [Fact]
    public async Task A_relay_that_does_not_answer_leaves_the_last_figures()
    {
        UsageMeters.Http = new HttpClient(new FakeServer(HttpStatusCode.BadGateway, "nope"));

        var meters = NewMeters();
        meters.Report(MeterKind.Commentary, 99, 450, NovemberResets);

        await meters.RefreshAsync(RelaySettings(), Voice(VoiceEngine.Kokoro));

        Assert.Equal(99, meters.Get(MeterKind.Commentary)!.Used);
        Assert.NotNull(meters.LastProblem);
    }

    [Fact]
    public void A_usage_answer_of_the_wrong_shape_is_not_read()
    {
        Assert.Null(UsageMeters.ParseUsage("[1,2]"));
        Assert.Null(UsageMeters.ParseUsage("not json"));

        var empty = UsageMeters.ParseUsage("{}")!;
        Assert.Null(empty.Commentary);
        Assert.Null(empty.Voice);
        Assert.Empty(empty.Requests);
    }

    // --- The club's own ElevenLabs key --------------------------------------

    [Fact]
    public async Task Refresh_reads_the_elevenlabs_plan_for_the_clubs_own_key()
    {
        var elevenLabs = new FakeServer(HttpStatusCode.OK, """
            { "tier": "creator", "character_count": 1200, "character_limit": 30000, "next_character_count_reset_unix": 1793491200 }
            """);
        ElevenLabsClient.SubscriptionHttp = new HttpClient(elevenLabs);
        UsageMeters.Http = new HttpClient(new FakeServer(HttpStatusCode.InternalServerError, string.Empty));

        var meters = NewMeters();
        await meters.RefreshAsync(new AppSettings(), Voice(VoiceEngine.ElevenLabs, "sk-test"));

        var asked = Assert.Single(elevenLabs.Asked);
        Assert.Equal("https://api.elevenlabs.io/v1/user/subscription", asked.Url);
        Assert.Equal("sk-test", asked.ApiKey);

        var meter = meters.Get(MeterKind.ElevenLabsKey)!;
        Assert.True(meter.Known);
        Assert.Equal(1200, meter.Used);
        Assert.Equal(30000, meter.Allowance);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1793491200).UtcDateTime, meter.ResetsUtc);
        Assert.False(meter.CanAskForMore);
    }

    [Fact]
    public async Task A_key_that_cannot_read_its_plan_says_which_permission_to_give()
    {
        ElevenLabsClient.SubscriptionHttp = new HttpClient(new FakeServer(HttpStatusCode.Unauthorized,
            """{ "detail": { "status": "missing_permissions" } }"""));

        var meters = NewMeters();
        await meters.RefreshAsync(new AppSettings(), Voice(VoiceEngine.ElevenLabs, "sk-test"));

        var meter = meters.Get(MeterKind.ElevenLabsKey)!;
        Assert.False(meter.Known);
        Assert.Equal(UsageMeters.ElevenLabsCantReadPlan, meter.Note);
        Assert.Equal(MeterLevel.Unknown, meter.Level);
        Assert.Contains("User: read", meter.Summary());
    }

    // --- Asking for more ----------------------------------------------------

    [Fact]
    public async Task A_top_up_request_without_a_mailbox_opens_a_prefilled_email()
    {
        var relay = new FakeServer(HttpStatusCode.OK, """{ "ok": true, "request": { "id": "r_2" }, "already": false }""");
        UsageMeters.Http = new HttpClient(relay);

        var opened = new List<string>();
        TopUpRequest.OpenMailTo = opened.Add;

        var settings = RelaySettings();

        // The mailbox's From name is often a person's own: it is not sent.
        settings.SmtpFromName = "Jane Volunteer";
        Assert.False(PinMailer.IsConfigured(settings));

        var said = await TopUpRequest.SendAsync(settings, Commentary(450, 450, resets: NovemberResets), "Big night on Friday");

        // Recorded on the relay...
        var asked = Assert.Single(relay.Asked);
        Assert.Equal("https://relay.example.com/api/v1/c/acme-rc/usage/topup-request", asked.Url);
        Assert.Equal("Bearer club-token", asked.Authorization);
        Assert.Contains("\"meter\":\"commentary\"", asked.Body);
        Assert.Contains("Big night on Friday", asked.Body);

        // ...and the operator told, through the club's email program.
        var mailTo = Assert.Single(opened);
        Assert.StartsWith("mailto:support@heatcaller.com?subject=Top-up%20request%3A%20acme-rc%20-%20AI%20commentary&body=", mailTo);
        Assert.Contains(Uri.EscapeDataString("Big night on Friday"), mailTo);
        Assert.Contains(Uri.EscapeDataString("450 of 450 lines"), mailTo);
        Assert.DoesNotContain(" ", mailTo);
        Assert.DoesNotContain("club-token", mailTo);
        Assert.DoesNotContain("Jane", mailTo);
        Assert.Contains(Uri.EscapeDataString("Club: acme-rc"), mailTo);

        Assert.StartsWith("Asked for more AI commentary.", said);
        Assert.Contains("press Send", said);
    }

    [Fact]
    public async Task A_second_request_this_month_says_it_was_already_asked()
    {
        UsageMeters.Http = new HttpClient(new FakeServer(HttpStatusCode.OK, """{ "ok": true, "request": { "id": "r_2" }, "already": true }"""));

        var opened = new List<string>();
        TopUpRequest.OpenMailTo = opened.Add;

        var said = await TopUpRequest.SendAsync(RelaySettings(), Commentary(450, 450), null);

        Assert.StartsWith("Already asked this month", said);
        Assert.Empty(opened);
    }

    [Fact]
    public async Task The_clubs_own_elevenlabs_plan_is_not_topped_up_here()
    {
        var opened = new List<string>();
        TopUpRequest.OpenMailTo = opened.Add;

        var key = new Meter(MeterKind.ElevenLabsKey, true, 10, 10, 0, null, DateTime.UtcNow, 0, null);
        var said = await TopUpRequest.SendAsync(RelaySettings(), key, null);

        Assert.Contains("elevenlabs.io", said);
        Assert.Empty(opened);
    }

    // --- The emails ---------------------------------------------------------

    [Fact]
    public void The_usage_emails_say_what_the_club_hears_instead()
    {
        var voice = new Meter(MeterKind.HeatCallerVoice, true, 100000, 100000, 0, null, DateTime.UtcNow, 0, null);

        Assert.Equal("80% of this month's AI commentary is used", UsageMail.Subject(Commentary(360, 450), MeterLevel.Watch));
        Assert.Equal(
            "This month's AI commentary is used up - the model on this PC is writing instead",
            UsageMail.Subject(Commentary(450, 450), MeterLevel.Spent));
        Assert.Equal(
            "This month's HeatCaller voice is used up - the free Kokoro voice is speaking instead",
            UsageMail.Subject(voice, MeterLevel.Spent));

        Assert.Contains("Ask for More", UsageMail.Body(Commentary(360, 450), MeterLevel.Watch, "Acme RC"));

        // Only what is true: no model here, or the club's own ElevenLabs plan.
        Assert.Equal(
            "This month's AI commentary is used up - the normal announcements carry on",
            UsageMail.Subject(Commentary(450, 450), MeterLevel.Spent, localModel: false));

        var key = new Meter(MeterKind.ElevenLabsKey, true, 10000, 10000, 0, null, DateTime.UtcNow, 0, null);
        Assert.Equal(
            "This month's ElevenLabs (your own key) is used up - more characters are bought on elevenlabs.io",
            UsageMail.Subject(key, MeterLevel.Spent));
    }

    [Fact]
    public void Usage_emails_go_to_the_chosen_address_or_the_clubs_own()
    {
        var settings = new AppSettings { SmtpUser = "club@example.com" };
        Assert.Equal("club@example.com", UsageMail.AddressFor(settings));

        settings.UsageEmailTo = "  chair@example.com ";
        Assert.Equal("chair@example.com", UsageMail.AddressFor(settings));

        settings.UsageEmailTo = "not an address";
        Assert.Equal("club@example.com", UsageMail.AddressFor(settings));

        Assert.True(new AppSettings().UsageEmailsOn);
    }

    // --- A relay, or ElevenLabs, that answers one thing ----------------------

    private sealed record Hit(string Url, string? Authorization, string? ApiKey, string Body);

    private sealed class FakeServer(HttpStatusCode status, string answer) : HttpMessageHandler
    {
        public List<Hit> Asked { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var sent = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            Asked.Add(new Hit(
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("xi-api-key", out var keys) ? keys.FirstOrDefault() : null,
                sent));

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(answer, Encoding.UTF8, "application/json"),
                RequestMessage = request,
            };
        }
    }
}
