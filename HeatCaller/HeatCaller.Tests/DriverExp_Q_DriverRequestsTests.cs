using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Drivers' own requests on the laptop (22 Oct 2026, driver experience,
/// package Q): the relay's inbox read, each request filed once and kept
/// across a restart, the newer details request replacing the older, the
/// per-night query cap, what the phone is shown - and the desk's answers:
/// only the ticked changes applied (with an undo), the old email address
/// told, privacy only ever turned on, a pack refused when it isn't theirs,
/// and a query noted exactly once without touching the result.
/// </summary>
public sealed class DriverExp_Q_DriverRequestsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string SamBadge = "0123456789abcdef0123";
    private const string DevBadge = "fedcba9876543210fedc";
    private const string NobodyBadge = "aaaaaaaaaaaaaaaaaaaa";

    private static readonly DateTime Tonight = new(2026, 10, 22, 19, 0, 0, DateTimeKind.Utc);

    // Distinct in the first six characters, so each has a reference of its own.
    private static string IdOf(int n) => n.ToString("x6") + new string('0', 18);

    private static DriverRequest Ask(int n, string badge, string kind, DateTime atUtc, Action<DriverRequest>? fill = null)
    {
        var request = new DriverRequest { Id = IdOf(n), Badge = badge, Kind = kind, AskedUtc = atUtc, Proof = DriverAsk.ByPin };
        fill?.Invoke(request);
        return request;
    }

    // ------------------------------------------------------------ the inbox

    [Fact]
    public void The_inbox_is_read_with_its_texts_and_flags_and_bad_items_are_set_aside()
    {
        var json = JsonSerializer.Serialize(new
        {
            ok = true,
            items = new object[]
            {
                new
                {
                    id = IdOf(1), b = SamBadge, k = "details", v = "pin", at = "2026-10-22T19:40:10.000Z",
                    f = new Dictionary<string, object> { ["announcer"] = "Zhock", ["about"] = "", ["initials"] = true, ["guardian"] = false, ["mystery"] = "x" },
                },
                new { id = "not-an-id", b = SamBadge, k = "pack", v = "badge", at = "2026-10-22T19:41:00Z", f = new { pack = "none" } },
                new { id = IdOf(3), b = "TRANSPONDER", k = "pack", v = "badge", at = "2026-10-22T19:41:00Z", f = new { pack = "none" } },
                new { id = IdOf(4), b = SamBadge, k = "refund", v = "badge", at = "2026-10-22T19:41:00Z", f = new { pack = "none" } },
            },
        });

        var asks = DriverRequestStore.ParseInbox(json, Tonight, out var unusable);

        var ask = Assert.Single(asks);
        Assert.Equal(IdOf(1), ask.Id);
        Assert.Equal(SamBadge, ask.Badge);
        Assert.Equal(DriverAsk.Details, ask.Kind);
        Assert.Equal(DriverAsk.ByPin, ask.Proof);
        Assert.Equal(new DateTime(2026, 10, 22, 19, 40, 10, DateTimeKind.Utc), ask.AskedUtc);
        Assert.Equal("Zhock", ask.Text("announcer"));
        Assert.True(ask.Texts.ContainsKey("about"));
        Assert.Equal(string.Empty, ask.Text("about"));
        Assert.True(ask.Flags["initials"]);
        Assert.False(ask.Guardian);
        Assert.False(ask.Texts.ContainsKey("mystery"));
        Assert.Equal(IdOf(1)[..6].ToUpperInvariant(), ask.Ref);

        // A bad id can't be confirmed, so it isn't listed at all; a good id
        // with nothing usable in it is confirmed and dropped.
        Assert.Equal(new[] { IdOf(3), IdOf(4) }, unusable);
    }

    // ------------------------------------------------------------ filing

    [Fact]
    public void A_request_is_filed_once_and_is_still_there_after_a_restart()
    {
        var store = DriverRequestStore.Load();
        var ask = Ask(1, SamBadge, DriverAsk.Pack, Tonight, r => r.Texts["pack"] = "none");

        Assert.Equal(new[] { ask.Id }, store.FileAll(new[] { ask }, Tonight, out var filed));
        Assert.Equal(1, filed);

        // Collected again - the confirmation was lost: recognised, and
        // confirmed again, but not filed twice.
        Assert.Equal(new[] { ask.Id }, store.FileAll(new[] { ask }, Tonight, out filed));
        Assert.Equal(0, filed);
        Assert.Single(store.All);

        var again = DriverRequestStore.Load();
        var kept = Assert.Single(again.All);
        Assert.Equal(ask.Id, kept.Id);
        Assert.True(kept.IsWaiting);
        Assert.Equal("none", kept.Text("pack"));
    }

    [Fact]
    public void A_newer_details_request_replaces_the_same_drivers_older_one_and_nothing_else()
    {
        var store = DriverRequestStore.Load();

        store.FileAll(new[]
        {
            Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["announcer"] = "Zhock"),
            Ask(2, SamBadge, DriverAsk.Pack, Tonight.AddMinutes(1), r => r.Texts["pack"] = "none"),
            Ask(3, DevBadge, DriverAsk.Details, Tonight.AddMinutes(2), r => r.Texts["pronouns"] = "he/him"),
        }, Tonight, out _);

        store.FileAll(new[] { Ask(4, SamBadge, DriverAsk.Details, Tonight.AddMinutes(5), r => r.Texts["announcer"] = "Jacques") }, Tonight, out _);

        var older = store.Get(IdOf(1))!;
        Assert.True(older.Replaced);
        Assert.Equal(DriverAsk.Declined, older.State);
        Assert.Equal(DriverRequestStore.ReplacedSaid, older.Said);

        Assert.True(store.IsWaiting(IdOf(2)));
        Assert.True(store.IsWaiting(IdOf(3)));
        Assert.True(store.IsWaiting(IdOf(4)));

        // The phone sees the old one declined, with why.
        var answers = store.AnswersFor(SamBadge, Tonight.AddMinutes(10));
        Assert.Contains(answers, a => a.Ref == DriverAsk.RefOf(IdOf(1)) && a.State == DriverAsk.Declined && a.Said == DriverRequestStore.ReplacedSaid);
        Assert.Contains(answers, a => a.Ref == DriverAsk.RefOf(IdOf(4)) && a.State == DriverAsk.Waiting);
        Assert.DoesNotContain(answers, a => a.Ref == DriverAsk.RefOf(IdOf(3)));
    }

    [Fact]
    public void An_older_details_request_arriving_late_is_the_one_replaced()
    {
        var store = DriverRequestStore.Load();

        store.FileAll(new[] { Ask(2, SamBadge, DriverAsk.Details, Tonight.AddMinutes(5), r => r.Texts["announcer"] = "New") }, Tonight, out _);
        store.FileAll(new[] { Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["announcer"] = "Old") }, Tonight, out _);

        Assert.True(store.IsWaiting(IdOf(2)));
        Assert.True(store.Get(IdOf(1))!.Replaced);
    }

    [Fact]
    public void The_eleventh_query_of_a_night_is_declined_as_too_many()
    {
        var store = DriverRequestStore.Load();
        var queries = Enumerable.Range(1, DriverRequestStore.MostQueriesPerNight + 1)
            .Select(n => Ask(n, SamBadge, DriverAsk.Query, Tonight.AddMinutes(n), r =>
            {
                r.Texts["race"] = new string('a', 32);
                r.Texts["m"] = $"Query {n}";
            }))
            .ToList();

        store.FileAll(queries, Tonight, out var filed);

        Assert.Equal(DriverRequestStore.MostQueriesPerNight + 1, filed);
        Assert.Equal(DriverRequestStore.MostQueriesPerNight, store.Waiting.Count);

        var last = store.Get(IdOf(DriverRequestStore.MostQueriesPerNight + 1))!;
        Assert.Equal(DriverAsk.Declined, last.State);
        Assert.Equal(DriverRequestStore.TooManySaid, last.Said);

        // Another driver's are their own.
        store.FileAll(new[] { Ask(99, DevBadge, DriverAsk.Query, Tonight, r => r.Texts["m"] = "Mine") }, Tonight, out _);
        Assert.True(store.IsWaiting(IdOf(99)));
    }

    [Fact]
    public void The_phone_is_shown_only_the_last_day_and_an_answer_is_given_once()
    {
        var store = DriverRequestStore.Load();
        var yesterday = Tonight.AddDays(-2);

        store.FileAll(new[] { Ask(1, SamBadge, DriverAsk.Pack, yesterday, r => r.Texts["pack"] = "none") }, yesterday, out _);
        store.FileAll(new[] { Ask(2, SamBadge, DriverAsk.Pack, Tonight, r => r.Texts["pack"] = "none") }, Tonight, out _);

        Assert.True(store.Answer(IdOf(2), DriverAsk.Declined, new string('x', 200), Tonight));
        Assert.False(store.Answer(IdOf(2), DriverAsk.Approved, string.Empty, Tonight));
        Assert.False(store.Answer(IdOf(1), DriverAsk.Waiting, string.Empty, Tonight));

        var answer = Assert.Single(store.AnswersFor(SamBadge, Tonight.AddHours(1)));
        Assert.Equal(DriverAsk.RefOf(IdOf(2)), answer.Ref);
        Assert.Equal(DriverAsk.Pack, answer.Kind);
        Assert.Equal(DriverAsk.Declined, answer.State);
        Assert.Equal(DriverAsk.SaidMaxChars, answer.Said.Length);

        Assert.Empty(store.AnswersFor(DevBadge, Tonight));
        Assert.Empty(store.AnswersFor(string.Empty, Tonight));
    }

    [Fact]
    public void Answered_requests_older_than_thirty_days_are_dropped_and_waiting_ones_kept()
    {
        var store = DriverRequestStore.Load();
        var longAgo = DateTime.UtcNow.AddDays(-40);

        store.FileAll(new[]
        {
            Ask(1, SamBadge, DriverAsk.Pack, longAgo, r => r.Texts["pack"] = "none"),
            Ask(2, DevBadge, DriverAsk.Pack, longAgo, r => r.Texts["pack"] = "none"),
        }, longAgo, out _);

        Assert.True(store.Answer(IdOf(1), DriverAsk.Approved, string.Empty, longAgo));

        var again = DriverRequestStore.Load();
        var kept = Assert.Single(again.All);
        Assert.Equal(IdOf(2), kept.Id);
    }

    // ------------------------------------------------------------ the desk

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();

        public BatteryPackStore Packs { get; } = BatteryPackStore.Load();

        public UndoService Undo { get; } = new();

        public DriverRequestStore Store { get; } = DriverRequestStore.Load();

        public List<string> Log { get; } = new();

        public List<(string To, string Subject, string Body)> Notices { get; } = new();

        public SessionRecord Heat { get; }

        public DriverRequestDesk Desk { get; }

        public World()
        {
            Assert.True(Profiles.TryAdd(new DriverProfile
            {
                Transponder = "100",
                DriverName = "Sam Parker",
                AnnouncerName = "Jacques",
                Pronouns = "she/her",
                Email = "sam@example.com",
                NoPhotoOnline = true,
            }, out _));
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "500", DriverName = "Dev" }, out _));

            Heat = new SessionRecord
            {
                Id = new string('a', 32),
                Label = "Q1 Heat 2",
                StartedAtUtc = Tonight,
                Notes = "Track was damp.",
                Results = new List<SessionResultEntry>
                {
                    new() { Position = 1, Transponder = "100", DriverName = "Sam Parker", Laps = 12, FastLapMs = 11_200 },
                    new() { Position = 2, Transponder = "999", DriverName = "Someone", Laps = 11, FastLapMs = 11_900 },
                },
            };

            var badges = new Dictionary<string, string> { [SamBadge] = "100", [DevBadge] = "500" };

            Desk = new DriverRequestDesk(Store, new AppSettings(), Profiles, Packs, SessionHistoryStore.FromSessions(new[] { Heat }), Undo, Log.Add,
                badge => badges.TryGetValue(badge, out var t) ? t : null)
            {
                SendNotice = (to, subject, body) =>
                {
                    Notices.Add((to, subject, body));
                    return Task.FromResult(new MailResult { Ok = true, Message = "Sent." });
                },
            };
        }

        public DriverRequest File(DriverRequest ask)
        {
            Store.FileAll(new[] { ask }, DateTime.UtcNow, out _);
            return Store.Get(ask.Id)!;
        }
    }

    private static DriverRequestDesk.DetailsChoice Choice(Dictionary<string, string>? texts = null, Dictionary<string, bool>? flags = null) =>
        new(texts ?? new Dictionary<string, string>(), flags ?? new Dictionary<string, bool>());

    [Fact]
    public void Only_the_ticked_changes_are_applied_and_undo_puts_the_driver_back()
    {
        var w = new World();
        var ask = w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r =>
        {
            r.Texts["announcer"] = "Zhock";
            r.Texts["pronouns"] = "they/them";
            r.Texts["about"] = "Racing since I was eight";
            r.Flags["initials"] = true;
        }));

        var fields = w.Desk.FieldsOf(ask, w.Profiles.Get("100"));
        Assert.Equal(new[] { "announcer", "pronouns", "about", "initials" }, fields.Select(f => f.Key));
        Assert.Contains("Announcer name: 'Zhock' (was 'Jacques')", w.Desk.Describe(ask));

        // The desk unticked pronouns and edited the announcer name.
        Assert.True(w.Desk.ApplyDetails(ask, Choice(
            new Dictionary<string, string> { ["announcer"] = "Zhok", ["about"] = "Racing since I was eight" },
            new Dictionary<string, bool> { ["initials"] = true }), out var refusal), refusal);

        var sam = w.Profiles.Get("100")!;
        Assert.Equal("Zhok", sam.AnnouncerName);
        Assert.Equal("she/her", sam.Pronouns);
        Assert.Equal("Racing since I was eight", sam.AboutLine);
        Assert.True(sam.InitialsOnline);
        Assert.Equal(DriverAsk.Approved, w.Store.Get(ask.Id)!.State);
        Assert.Empty(w.Notices);
        Assert.DoesNotContain(w.Log, line => line.Contains("Sam", StringComparison.Ordinal));

        // Answered once: a second approve is refused.
        Assert.False(w.Desk.ApplyDetails(ask, Choice(new Dictionary<string, string> { ["pronouns"] = "x" }), out _));

        Assert.NotNull(w.Undo.Undo());

        sam = w.Profiles.Get("100")!;
        Assert.Equal("Jacques", sam.AnnouncerName);
        Assert.Equal(string.Empty, sam.AboutLine);
        Assert.False(sam.InitialsOnline);
        Assert.True(w.Store.IsWaiting(ask.Id));
    }

    [Fact]
    public void A_changed_email_tells_the_old_address_without_giving_it_the_new_one()
    {
        var w = new World();
        var ask = w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["email"] = "sam.parker@newmail.com"));

        Assert.True(w.Desk.ApplyDetails(ask, Choice(new Dictionary<string, string> { ["email"] = "sam.parker@newmail.com" }), out var refusal), refusal);

        Assert.Equal("sam.parker@newmail.com", w.Profiles.Get("100")!.Email);

        var notice = Assert.Single(w.Notices);
        Assert.Equal("sam@example.com", notice.To);
        Assert.Contains("s•••@newmail.com", notice.Body);
        Assert.DoesNotContain("sam.parker@newmail.com", notice.Body);
    }

    [Fact]
    public void A_bad_email_is_refused_and_nothing_changes()
    {
        var w = new World();
        var ask = w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["email"] = "x"));

        Assert.False(w.Desk.ApplyDetails(ask, Choice(new Dictionary<string, string> { ["email"] = "not an address" }), out _));
        Assert.Equal("sam@example.com", w.Profiles.Get("100")!.Email);
        Assert.True(w.Store.IsWaiting(ask.Id));
        Assert.Empty(w.Notices);
    }

    [Fact]
    public void A_phone_can_never_turn_a_privacy_choice_off()
    {
        var w = new World();
        var ask = w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Flags["noPhoto"] = false));

        // Not even offered to the desk.
        Assert.Empty(w.Desk.FieldsOf(ask, w.Profiles.Get("100")));

        // And refused if it got there anyway.
        Assert.True(w.Desk.ApplyDetails(ask, Choice(flags: new Dictionary<string, bool> { ["noPhoto"] = false }), out _));
        Assert.True(w.Profiles.Get("100")!.NoPhotoOnline);
    }

    [Fact]
    public void A_request_from_a_badge_nobody_has_can_only_be_discarded()
    {
        var w = new World();
        var ask = w.File(Ask(1, NobodyBadge, DriverAsk.Details, Tonight, r => r.Texts["announcer"] = "Who"));

        Assert.Equal("Unknown driver", w.Desk.NameFor(ask));
        Assert.StartsWith("Unknown driver", w.Desk.NoteFor(ask));
        Assert.False(w.Desk.ApplyDetails(ask, Choice(new Dictionary<string, string> { ["announcer"] = "Who" }), out _));

        Assert.True(w.Desk.Discard(ask));
        Assert.Empty(w.Store.All);

        // A known driver's can't be discarded - it is answered.
        var sams = w.File(Ask(2, SamBadge, DriverAsk.Pack, Tonight, r => r.Texts["pack"] = "none"));
        Assert.False(w.Desk.Discard(sams));
    }

    [Fact]
    public void Approving_a_pack_puts_it_in_their_car_and_one_that_is_not_theirs_is_refused()
    {
        var w = new World();
        var mine = new BatteryPack { Id = new string('1', 32), Label = "Red 2S #3", OwnerTransponder = "100" };
        var devs = new BatteryPack { Id = new string('2', 32), Label = "Dev's", OwnerTransponder = "500" };
        w.Packs.Upsert(mine);
        w.Packs.Upsert(devs);

        var ask = w.File(Ask(1, SamBadge, DriverAsk.Pack, Tonight, r => r.Texts["pack"] = mine.Id));
        Assert.True(w.Desk.ApprovePack(ask, out var refusal), refusal);
        Assert.Equal(mine.Id, w.Packs.CurrentPackId("100"));
        Assert.Equal(DriverAsk.Approved, w.Store.Get(ask.Id)!.State);

        var wrong = w.File(Ask(2, SamBadge, DriverAsk.Pack, Tonight.AddMinutes(1), r => r.Texts["pack"] = devs.Id));
        Assert.False(w.Desk.ApprovePack(wrong, out refusal));
        Assert.Contains("isn't one", refusal);
        Assert.Equal(mine.Id, w.Packs.CurrentPackId("100"));
        Assert.True(w.Store.IsWaiting(wrong.Id));

        var none = w.File(Ask(3, SamBadge, DriverAsk.Pack, Tonight.AddMinutes(2), r => r.Texts["pack"] = "none"));
        Assert.True(w.Desk.ApprovePack(none, out refusal), refusal);
        Assert.Null(w.Packs.CurrentPackId("100"));
    }

    [Fact]
    public void Noted_adds_the_query_to_the_races_notes_once_and_never_touches_the_result()
    {
        var w = new World();
        var resultsBefore = JsonSerializer.Serialize(w.Heat.Results);

        var ask = w.File(Ask(1, SamBadge, DriverAsk.Query, Tonight, r =>
        {
            r.Texts["race"] = w.Heat.Id;
            r.Texts["m"] = "My third lap wasn't counted";
        }));

        Assert.True(w.Desk.InThatRace(ask));
        Assert.DoesNotContain("Not in that race", w.Desk.NoteFor(ask));

        Assert.True(w.Desk.Note(ask, out var refusal), refusal);

        // WHY (23 Oct 2026, bug hunt 10): formatted as the app formats it - an
        // interpolated HH:mm takes the culture's time separator ("." in
        // Finnish), so the test failed on such a PC though the app was right.
        var line = $"[Driver query {AppTime.TimeOfDay(Tonight.ToLocalTime())}] Sam P.: My third lap wasn't counted";
        Assert.Equal("Track was damp." + Environment.NewLine + line, w.Heat.Notes);
        Assert.Equal(DriverAsk.Noted, w.Store.Get(ask.Id)!.State);
        Assert.Equal(resultsBefore, JsonSerializer.Serialize(w.Heat.Results));

        // Answered: a second Noted is refused and adds nothing.
        Assert.False(w.Desk.Note(ask, out _));
        Assert.Equal(2, w.Heat.Notes.Split(line).Length);

        // Undo takes the line back out and the query waits again; noting it
        // again adds it once.
        Assert.NotNull(w.Undo.Undo());
        Assert.Equal("Track was damp.", w.Heat.Notes);
        Assert.True(w.Store.IsWaiting(ask.Id));
        Assert.True(w.Desk.Note(ask, out _));
        Assert.Equal(2, w.Heat.Notes.Split(line).Length);
    }

    [Fact]
    public void A_query_about_a_race_they_were_not_in_says_so()
    {
        var w = new World();
        var ask = w.File(Ask(1, DevBadge, DriverAsk.Query, Tonight, r =>
        {
            r.Texts["race"] = w.Heat.Id;
            r.Texts["m"] = "Where am I?";
        }));

        Assert.False(w.Desk.InThatRace(ask));
        Assert.Contains("Not in that race", w.Desk.NoteFor(ask));

        var lost = w.File(Ask(2, DevBadge, DriverAsk.Query, Tonight, r =>
        {
            r.Texts["race"] = new string('b', 32);
            r.Texts["m"] = "And this one?";
        }));

        Assert.Contains("doesn't have", w.Desk.NoteFor(lost));
        Assert.False(w.Desk.Note(lost, out _));
        Assert.True(w.Store.IsWaiting(lost.Id));
    }

    [Fact]
    public void Decline_sends_the_reason_back_and_undo_reopens_it()
    {
        var w = new World();
        var ask = w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["announcer"] = "Rude word"));

        Assert.True(w.Desk.Decline(ask, "  Please pick another name.  "));

        var answer = Assert.Single(w.Store.AnswersFor(SamBadge, DateTime.UtcNow));
        Assert.Equal(DriverAsk.Declined, answer.State);
        Assert.Equal("Please pick another name.", answer.Said);

        Assert.False(w.Desk.Decline(ask, "Again"));
        Assert.NotNull(w.Undo.Undo());
        Assert.True(w.Store.IsWaiting(ask.Id));
    }

    // ------------------------------------------------------------ small rules

    [Theory]
    [InlineData("Sam Parker", "Sam P.")]
    [InlineData("  Sam   van der parker ", "Sam P.")]
    [InlineData("Cher", "Cher")]
    [InlineData("", "A driver")]
    public void A_query_is_signed_with_first_name_and_initial(string name, string expected) =>
        Assert.Equal(expected, DriverRequestDesk.ShortName(name));

    [Fact]
    public void The_old_address_notice_masks_the_new_one()
    {
        Assert.Equal("s•••@gmail.com", DriverRequestDesk.MaskEmail("sam@gmail.com"));
        Assert.Equal("•••", DriverRequestDesk.MaskEmail("nobody"));

        var (_, body) = DriverRequestDesk.OldEmailNotice("Sam Parker", string.Empty, new DateTime(2026, 10, 22));
        Assert.Contains("removed", body);
    }

    [Theory]
    [InlineData(true, true, "Zhock", "Jacques", null, true)]
    [InlineData(true, true, "Zhock", "Jacques", "Zhock", false)]
    [InlineData(true, true, "Zhock ", "Jacques", "Zhock", false)]
    [InlineData(true, true, "Zhockk", "Jacques", "Zhock", true)]
    [InlineData(true, true, "Jacques", "Jacques", null, false)]
    [InlineData(true, true, "", "Jacques", null, false)]
    [InlineData(true, false, "Zhock", "Jacques", null, false)]
    [InlineData(false, true, "Zhock", "Jacques", null, false)]
    public void A_changed_announcer_name_must_be_heard_before_it_is_approved(bool canSpeak, bool ticked, string box, string current, string? heard, bool stillToHear) =>
        Assert.Equal(stillToHear, ApproveDriverRequestForm.NameStillToHear(canSpeak, ticked, box, current, heard));

    [Fact]
    public void The_desk_windows_build_with_requests_waiting()
    {
        StaThread.Run(() =>
        {
            var w = new World();
            var details = w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r =>
            {
                r.Texts["announcer"] = "Zhock";
                r.Texts["email"] = "sam.parker@newmail.com";
                r.Flags["initials"] = true;
                r.Flags["guardian"] = true;
            }));
            w.File(Ask(2, NobodyBadge, DriverAsk.Pack, Tonight, r => r.Texts["pack"] = "none"));

            using (var list = new DriverRequestsForm(w.Desk, w.Profiles, _ => { }))
            {
                Assert.Equal("Driver Requests", list.Text);
            }

            using (var approve = new ApproveDriverRequestForm(w.Desk, details, w.Profiles.Get("100")!, w.Profiles, _ => { }, null))
            {
                // A changed announcer name not yet heard: Approve is off.
                Assert.False(Inside(approve).OfType<Button>().Single(b => b.Text == "Approve").Enabled);
                Assert.Contains(Inside(approve).OfType<Button>(), b => b.Text == "Hear It");
            }

            using (var noVoice = new ApproveDriverRequestForm(w.Desk, details, w.Profiles.Get("100")!, w.Profiles, null, null))
            {
                // No voice to hear it in: nothing to wait for.
                Assert.True(Inside(noVoice).OfType<Button>().Single(b => b.Text == "Approve").Enabled);
                Assert.DoesNotContain(Inside(noVoice).OfType<Button>(), b => b.Text == "Hear It");
            }

            using (var decline = new DriverRequestReasonForm("Sam Parker"))
            {
                Assert.Equal(DriverAsk.SaidMaxChars, Inside(decline).OfType<TextBox>().Single().MaxLength);
            }

            return true;
        }, TimeSpan.FromSeconds(30), () => "building the driver requests windows");
    }

    private static IEnumerable<Control> Inside(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;

            foreach (var deeper in Inside(child))
            {
                yield return deeper;
            }
        }
    }
}
