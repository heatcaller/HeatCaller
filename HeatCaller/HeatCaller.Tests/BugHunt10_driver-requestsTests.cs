namespace HeatCaller.Tests;

/// <summary>
/// The tenth bug hunt's fixes to drivers' own requests (23 Oct 2026): a
/// newer details request carries the older one's changes forward instead of
/// dropping them (and a PIN-proven email never rides onto a badge-only
/// request); the query cap's night runs 6am to 6am; the phone is given its
/// answers newest last; a Noted line is matched as a whole line and only
/// taken out by the undo of the Noted that wrote it; and undoing an approved
/// pack reopens the request too.
/// </summary>
public sealed class BugHunt10_DriverRequestsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string SamBadge = "0123456789abcdef0123";

    private static readonly DateTime Tonight = new(2026, 10, 14, 19, 0, 0, DateTimeKind.Utc);

    private static string IdOf(int n) => n.ToString("x6") + new string('0', 18);

    private static DriverRequest Ask(int n, string kind, DateTime atUtc, Action<DriverRequest>? fill = null, string proof = DriverAsk.ByPin)
    {
        var request = new DriverRequest { Id = IdOf(n), Badge = SamBadge, Kind = kind, AskedUtc = atUtc, Proof = proof };
        fill?.Invoke(request);
        return request;
    }

    // ------------------------------------------------------------ carried forward

    [Fact]
    public void A_newer_details_request_keeps_what_the_older_one_asked_for()
    {
        var store = DriverRequestStore.Load();

        store.FileAll(new[] { Ask(1, DriverAsk.Details, Tonight, r => r.Flags["noPhoto"] = true) }, Tonight, out _);
        store.FileAll(new[] { Ask(2, DriverAsk.Details, Tonight.AddMinutes(10), r => r.Texts["announcer"] = "Zhock") }, Tonight, out _);

        Assert.True(store.Get(IdOf(1))!.Replaced);

        var kept = store.Get(IdOf(2))!;
        Assert.True(kept.IsWaiting);
        Assert.Equal("Zhock", kept.Text("announcer"));
        Assert.True(kept.Flags["noPhoto"]);

        // Kept across a restart too.
        var again = DriverRequestStore.Load().Get(IdOf(2))!;
        Assert.True(again.Flags["noPhoto"]);
    }

    [Fact]
    public void The_newer_value_wins_and_a_privacy_tick_from_either_stays_on()
    {
        var store = DriverRequestStore.Load();

        store.FileAll(new[]
        {
            Ask(1, DriverAsk.Details, Tonight, r =>
            {
                r.Texts["announcer"] = "Old";
                r.Texts["pronouns"] = "they/them";
                r.Flags["emailMyNight"] = true;
                r.Flags["initials"] = true;
            }),
        }, Tonight, out _);

        store.FileAll(new[]
        {
            Ask(2, DriverAsk.Details, Tonight.AddMinutes(5), r =>
            {
                r.Texts["announcer"] = "New";
                r.Flags["emailMyNight"] = false;
            }),
        }, Tonight, out _);

        var kept = store.Get(IdOf(2))!;
        Assert.Equal("New", kept.Text("announcer"));
        Assert.Equal("they/them", kept.Text("pronouns"));
        Assert.False(kept.Flags["emailMyNight"]);
        Assert.True(kept.Flags["initials"]);
    }

    [Fact]
    public void An_older_request_arriving_late_is_carried_into_the_newer_one()
    {
        var store = DriverRequestStore.Load();

        store.FileAll(new[] { Ask(2, DriverAsk.Details, Tonight.AddMinutes(5), r => r.Texts["announcer"] = "New") }, Tonight, out _);
        store.FileAll(new[] { Ask(1, DriverAsk.Details, Tonight, r => { r.Texts["announcer"] = "Old"; r.Flags["noVideo"] = true; }) }, Tonight, out _);

        Assert.True(store.Get(IdOf(1))!.Replaced);

        var kept = store.Get(IdOf(2))!;
        Assert.True(kept.IsWaiting);
        Assert.Equal("New", kept.Text("announcer"));
        Assert.True(kept.Flags["noVideo"]);
    }

    [Fact]
    public void A_pin_proven_email_never_rides_onto_a_badge_only_request_and_still_waits()
    {
        var store = DriverRequestStore.Load();

        store.FileAll(new[]
        {
            Ask(1, DriverAsk.Details, Tonight, r =>
            {
                r.Texts["email"] = "sam@newmail.com";
                r.Flags["noVideo"] = true;
            }),
        }, Tonight, out _);

        store.FileAll(new[] { Ask(2, DriverAsk.Details, Tonight.AddMinutes(5), r => r.Texts["announcer"] = "Zhock", DriverAsk.ByBadge) }, Tonight, out _);

        var badgeOnly = store.Get(IdOf(2))!;
        Assert.True(badgeOnly.IsWaiting);
        Assert.False(badgeOnly.Texts.ContainsKey("email"));
        Assert.True(badgeOnly.Flags["noVideo"]);

        // The email is not lost: the older request waits on with only that.
        var older = store.Get(IdOf(1))!;
        Assert.True(older.IsWaiting);
        Assert.False(older.Replaced);
        Assert.Equal(new[] { "email" }, older.Texts.Keys.ToArray());
        Assert.Equal("sam@newmail.com", older.Text("email"));
        Assert.Empty(older.Flags);
    }

    [Fact]
    public void Email_my_night_waits_on_with_an_email_that_cannot_ride_along()
    {
        var store = DriverRequestStore.Load();

        store.FileAll(new[]
        {
            Ask(1, DriverAsk.Details, Tonight, r =>
            {
                r.Texts["email"] = "sam@newmail.com";
                r.Flags["emailMyNight"] = true;
            }),
        }, Tonight, out _);

        store.FileAll(new[] { Ask(2, DriverAsk.Details, Tonight.AddMinutes(5), r => r.Texts["announcer"] = "Zhock", DriverAsk.ByBadge) }, Tonight, out _);

        Assert.False(store.Get(IdOf(2))!.Flags.ContainsKey("emailMyNight"));

        var older = store.Get(IdOf(1))!;
        Assert.True(older.IsWaiting);
        Assert.Equal("sam@newmail.com", older.Text("email"));
        Assert.True(older.Flags["emailMyNight"]);
    }

    [Fact]
    public void An_email_rides_between_two_pin_proven_requests()
    {
        var store = DriverRequestStore.Load();

        store.FileAll(new[] { Ask(1, DriverAsk.Details, Tonight, r => r.Texts["email"] = "sam@newmail.com") }, Tonight, out _);
        store.FileAll(new[] { Ask(2, DriverAsk.Details, Tonight.AddMinutes(5), r => r.Texts["announcer"] = "Zhock") }, Tonight, out _);

        Assert.True(store.Get(IdOf(1))!.Replaced);
        Assert.Equal("sam@newmail.com", store.Get(IdOf(2))!.Text("email"));
    }

    // ------------------------------------------------------------ the night

    [Fact]
    public void The_query_cap_night_runs_six_am_to_six_am()
    {
        var evening = new DateTime(2026, 10, 14, 21, 0, 0, DateTimeKind.Local).ToUniversalTime();
        var pastMidnight = new DateTime(2026, 10, 15, 0, 30, 0, DateTimeKind.Local).ToUniversalTime();
        var nextMorning = new DateTime(2026, 10, 15, 6, 30, 0, DateTimeKind.Local).ToUniversalTime();

        Assert.Equal(DriverRequestStore.NightOf(evening), DriverRequestStore.NightOf(pastMidnight));
        Assert.NotEqual(DriverRequestStore.NightOf(evening), DriverRequestStore.NightOf(nextMorning));

        var store = DriverRequestStore.Load();
        var queries = Enumerable.Range(1, DriverRequestStore.MostQueriesPerNight)
            .Select(n => Ask(n, DriverAsk.Query, evening.AddMinutes(n), r => r.Texts["m"] = $"Query {n}"))
            .ToList();

        store.FileAll(queries, evening, out _);
        store.FileAll(new[] { Ask(50, DriverAsk.Query, pastMidnight, r => r.Texts["m"] = "After midnight") }, pastMidnight, out _);
        store.FileAll(new[] { Ask(51, DriverAsk.Query, nextMorning, r => r.Texts["m"] = "Next night") }, nextMorning, out _);

        var late = store.Get(IdOf(50))!;
        Assert.Equal(DriverAsk.Declined, late.State);
        Assert.Equal(DriverRequestStore.TooManySaid, late.Said);
        Assert.True(store.IsWaiting(IdOf(51)));
    }

    // ------------------------------------------------------------ the phone

    [Fact]
    public void The_phone_is_given_its_newest_answers_newest_last()
    {
        var store = DriverRequestStore.Load();
        var asks = Enumerable.Range(1, DriverRequestStore.MostAnswersShown + 2)
            .Select(n => Ask(n, DriverAsk.Pack, Tonight.AddMinutes(n), r => r.Texts["pack"] = "none"))
            .ToList();

        store.FileAll(asks, Tonight, out _);

        var answers = store.AnswersFor(SamBadge, Tonight.AddHours(1));
        Assert.Equal(DriverRequestStore.MostAnswersShown, answers.Count);
        Assert.Equal(DriverAsk.RefOf(IdOf(3)), answers[0].Ref);
        Assert.Equal(DriverAsk.RefOf(IdOf(DriverRequestStore.MostAnswersShown + 2)), answers[^1].Ref);
    }

    // ------------------------------------------------------------ notes lines

    [Fact]
    public void A_note_line_is_found_only_as_a_whole_line()
    {
        var nl = Environment.NewLine;
        var shortLine = "[Driver query 19:00] Sam P.: Lap 3";
        var longLine = "[Driver query 19:00] Sam P.: Lap 3 wasn't counted";

        Assert.True(DriverRequestDesk.IndexOfLine("Damp." + nl + longLine, shortLine) < 0);
        Assert.True(DriverRequestDesk.IndexOfLine("Damp." + nl + longLine + nl + shortLine, shortLine) > 0);
        Assert.Equal(0, DriverRequestDesk.IndexOfLine(shortLine + "\n" + longLine, shortLine));

        Assert.Equal("Damp." + nl + longLine, DriverRequestDesk.WithoutLine("Damp." + nl + longLine + nl + shortLine, shortLine));
        Assert.Equal("Damp." + nl + longLine, DriverRequestDesk.WithoutLine("Damp." + nl + shortLine + nl + longLine, shortLine));
        Assert.Equal(longLine, DriverRequestDesk.WithoutLine(shortLine + "\r\n" + longLine, shortLine));
        Assert.Equal("Damp." + nl + longLine, DriverRequestDesk.WithoutLine("Damp." + nl + longLine, shortLine));
    }

    // ------------------------------------------------------------ the desk

    private sealed class World
    {
        public ProfileStore Profiles { get; } = ProfileStore.Load();

        public BatteryPackStore Packs { get; } = BatteryPackStore.Load();

        public UndoService Undo { get; } = new();

        public DriverRequestStore Store { get; } = DriverRequestStore.Load();

        public SessionRecord Heat { get; }

        public DriverRequestDesk Desk { get; }

        public World()
        {
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Sam Parker" }, out _));

            Heat = new SessionRecord
            {
                Id = new string('a', 32),
                Label = "Q1 Heat 2",
                StartedAtUtc = Tonight,
                Notes = "Track was damp.",
                Results = new List<SessionResultEntry>
                {
                    new() { Position = 1, Transponder = "100", DriverName = "Sam Parker", Laps = 12, FastLapMs = 11_200 },
                },
            };

            var badges = new Dictionary<string, string> { [SamBadge] = "100" };

            Desk = new DriverRequestDesk(Store, new AppSettings(), Profiles, Packs, SessionHistoryStore.FromSessions(new[] { Heat }), Undo, _ => { },
                badge => badges.TryGetValue(badge, out var t) ? t : null);
        }

        public DriverRequest File(DriverRequest ask)
        {
            Store.FileAll(new[] { ask }, DateTime.UtcNow, out _);
            return Store.Get(ask.Id)!;
        }
    }

    [Fact]
    public void Undoing_the_second_of_two_identical_queries_leaves_the_first_ones_line()
    {
        var w = new World();
        var first = w.File(Ask(1, DriverAsk.Query, Tonight, r => { r.Texts["race"] = w.Heat.Id; r.Texts["m"] = "Lap 3"; }));
        var resent = w.File(Ask(2, DriverAsk.Query, Tonight, r => { r.Texts["race"] = w.Heat.Id; r.Texts["m"] = "Lap 3"; }));

        Assert.True(w.Desk.Note(first, out var refusal), refusal);
        var notedOnce = w.Heat.Notes;
        Assert.True(w.Desk.Note(resent, out refusal), refusal);
        Assert.Equal(notedOnce, w.Heat.Notes);

        // Undo the resend: it waits again, and the first one's line stays.
        Assert.NotNull(w.Undo.Undo());
        Assert.True(w.Store.IsWaiting(resent.Id));
        Assert.Equal(DriverAsk.Noted, w.Store.Get(first.Id)!.State);
        Assert.Equal(notedOnce, w.Heat.Notes);

        // Undo the first: now the line goes.
        Assert.NotNull(w.Undo.Undo());
        Assert.Equal("Track was damp.", w.Heat.Notes);
    }

    [Fact]
    public void Undoing_an_approved_pack_puts_the_old_pack_back_and_reopens_the_request()
    {
        var w = new World();
        var red = new BatteryPack { Id = new string('1', 32), Label = "Red", OwnerTransponder = "100" };
        var blue = new BatteryPack { Id = new string('2', 32), Label = "Blue", OwnerTransponder = "100" };
        w.Packs.Upsert(red);
        w.Packs.Upsert(blue);
        Assert.True(w.Packs.SetCurrent("100", red.Id));

        var ask = w.File(Ask(1, DriverAsk.Pack, Tonight, r => r.Texts["pack"] = blue.Id));
        Assert.True(w.Desk.ApprovePack(ask, out var refusal), refusal);
        Assert.Equal(blue.Id, w.Packs.CurrentPackId("100"));
        Assert.Equal(DriverAsk.Approved, w.Store.Get(ask.Id)!.State);

        Assert.NotNull(w.Undo.Undo());
        Assert.Equal(red.Id, w.Packs.CurrentPackId("100"));
        Assert.True(w.Store.IsWaiting(ask.Id));

        // Already in the car: still a yes to the phone, and still undoable.
        var same = w.File(Ask(2, DriverAsk.Pack, Tonight.AddMinutes(1), r => r.Texts["pack"] = red.Id));
        var before = w.Undo.Count;
        Assert.True(w.Desk.ApprovePack(same, out refusal), refusal);
        Assert.Equal(before + 1, w.Undo.Count);
        Assert.NotNull(w.Undo.Undo());
        Assert.Equal(red.Id, w.Packs.CurrentPackId("100"));
        Assert.True(w.Store.IsWaiting(same.Id));
    }
}
