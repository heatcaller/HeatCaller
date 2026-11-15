using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Setup plan step 8, the phone side (12 Nov 2026): marshals saying from their
/// phone they're at their post, the marshal's "about 5 minutes" (the eta it
/// rides on), the next race's countdown on phones, marshal swaps asked and
/// answered on phones, and a driver's own entry money on their check-in card.
/// </summary>
public sealed class SetupStep8PhonesTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Now = new(2026, 11, 12, 19, 40, 37, DateTimeKind.Utc);

    private static string Badge(int n) => n.ToString("x20");

    // ------------------------------------------------------------ marshals in place

    [Fact]
    public void Each_marshal_says_they_are_in_and_all_in_opens_the_gate()
    {
        var posts = new MarshalPosts();
        var marshals = new[] { "1", "2", "3" };

        Assert.False(posts.AllIn("r1", marshals));
        Assert.True(posts.In("r1", "1"));
        Assert.False(posts.In("r1", "1"));
        Assert.True(posts.In("r1", " 2 "));

        Assert.Equal(new[] { true, true, false }, posts.Arrived("r1", marshals));
        Assert.False(posts.AllIn("r1", marshals));
        Assert.False(posts.IsIn("r2", "1"));

        posts.In("r1", "3");
        Assert.True(posts.AllIn("r1", marshals));
        Assert.False(posts.AllIn("r1", Array.Empty<string>()));
    }

    [Fact]
    public void Only_the_last_rounds_are_remembered()
    {
        var posts = new MarshalPosts();

        for (var i = 0; i <= MarshalPosts.RoundsKept; i++)
        {
            posts.In("r" + i, "1");
        }

        Assert.False(posts.IsIn("r0", "1"));
        Assert.True(posts.IsIn("r" + MarshalPosts.RoundsKept, "1"));
    }

    [Fact]
    public void The_clock_keeps_phone_confirmations_across_its_gaps()
    {
        var clock = new AutoStartClock();
        clock.Posts.In("r2", "1");

        clock.Open("gen1", "r2", Now, AutoStartClock.DefaultGap);
        clock.Close();

        Assert.True(clock.Posts.IsIn("r2", "1"));
    }

    [Fact]
    public void The_banner_says_who_is_in_and_who_it_is_waiting_for()
    {
        var marshals = new List<(string Name, string Post)> { ("Hugh Turn", "Turn 1"), ("Rhoda Rage", "Turn 2"), ("Axel Grease", "Turn 3") };

        Assert.Equal("Marshals for Q2 Heat 3 - 2 of 3 in: Hugh Turn, Rhoda Rage; waiting for Axel Grease (Turn 3).",
            MarshalsInPlace.Ask("Q2 Heat 3", marshals, waiting: false, new[] { true, true, false }));
        Assert.Equal("Q2 Heat 3 is waiting for its marshals - 1 of 3 in: Rhoda Rage; waiting for Hugh Turn (Turn 1), Axel Grease (Turn 3).",
            MarshalsInPlace.Ask("Q2 Heat 3", marshals, waiting: true, new[] { false, true, false }));
        Assert.Equal("Marshals for Q2 Heat 3 - all 3 in: Hugh Turn, Rhoda Rage, Axel Grease.",
            MarshalsInPlace.Ask("Q2 Heat 3", marshals, waiting: false, new[] { true, true, true }));

        // Nobody in from a phone: the wording the desk already knew.
        Assert.Equal(MarshalsInPlace.Ask("Q2 Heat 3", marshals, waiting: false),
            MarshalsInPlace.Ask("Q2 Heat 3", marshals, waiting: false, new[] { false, false, false }));
    }

    // ------------------------------------------------------------ the countdown on phones

    [Fact]
    public void Phones_are_told_the_start_time_while_counting_and_the_state_otherwise()
    {
        var at = new DateTime(2026, 11, 12, 19, 42, 19, 640, DateTimeKind.Utc);

        var counting = PhoneCountdown.For(AutoStartState.Counting, at)!;
        Assert.Equal("counting", counting.State);
        Assert.Equal(new DateTime(2026, 11, 12, 19, 42, 19, DateTimeKind.Utc), counting.AtUtc);
        Assert.Equal("counting|2026-11-12T19:42:19Z", PhoneCountdown.KeyOf(counting));

        Assert.Equal("held", PhoneCountdown.For(AutoStartState.Held, at)!.State);
        Assert.Null(PhoneCountdown.For(AutoStartState.Held, at)!.AtUtc);

        foreach (var soon in new[] { AutoStartState.WaitingForFloor, AutoStartState.WaitingForCars, AutoStartState.WaitingForMarshals, AutoStartState.Start })
        {
            Assert.Equal("soon", PhoneCountdown.For(soon, at)!.State);
        }

        Assert.Null(PhoneCountdown.For(AutoStartState.Off, at));
        Assert.Equal(string.Empty, PhoneCountdown.KeyOf(null));
    }

    [Fact]
    public void The_relay_state_carries_the_countdown_between_races()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", "WebServerService.cs"));

        Assert.Contains("relayNode[\"nextStart\"]", source, StringComparison.Ordinal);
        Assert.Contains("RaceState.Idle or RaceState.Finished", source, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ money

    [Fact]
    public void A_driver_s_own_entry_reads_paid_or_owing_and_nothing_with_no_fee()
    {
        var owing = DeskMoney.ForPhone(15m, null)!;
        Assert.False(owing.Paid);
        Assert.Equal($"Entry: {DeskMoney.Money(15m)} - pay at the desk", owing.Words);

        var paid = DeskMoney.ForPhone(15m, new EntryPayment { How = PaidHow.Cash, Amount = 15m })!;
        Assert.True(paid.Paid);
        Assert.Equal("Paid ✓ - thank you!", paid.Words);

        Assert.True(DeskMoney.ForPhone(15m, new EntryPayment { How = PaidHow.Comp, Note = "Marshal" })!.Paid);
        Assert.False(DeskMoney.ForPhone(15m, new EntryPayment { How = PaidHow.Owing })!.Paid);
        Assert.Null(DeskMoney.ForPhone(0m, new EntryPayment { How = PaidHow.Cash }));
    }

    [Fact]
    public void The_check_in_list_carries_each_driver_s_own_entry_and_nothing_without_a_fee()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "M1", DriverName = "Sam Parker" });
        profiles.Upsert(new DriverProfile { Transponder = "M2", DriverName = "Alex Jones" });

        var settings = new AppSettings
        {
            RelayClub = "acme-rc",
            RelayCheckInEnabled = true,
            RelayCheckInSecret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
        };

        var roster = new[]
        {
            new WebServerService.CheckInEntry("M1", "Sam Parker", "Stock", false),
            new WebServerService.CheckInEntry("M2", "Alex Jones", "Stock", true),
        };

        DeskMoney.PhoneMoney? MoneyOf(string t) => t == "M1"
            ? DeskMoney.ForPhone(15m, null)
            : DeskMoney.ForPhone(15m, new EntryPayment { How = PaidHow.Card, Amount = 15m });

        using var doc = JsonDocument.Parse(RelayDocuments.CheckIn(roster, profiles, settings, "Tonight", new Dictionary<string, string?>(), MoneyOf));
        var drivers = doc.RootElement.GetProperty("drivers").EnumerateArray()
            .ToDictionary(d => d.GetProperty("id").GetString()!, d => d.Clone());

        var sam = drivers[RelayCheckIn.IdFor("M1", settings.RelayCheckInSecret)].GetProperty("entry");
        var alex = drivers[RelayCheckIn.IdFor("M2", settings.RelayCheckInSecret)].GetProperty("entry");

        Assert.Equal($"Entry: {DeskMoney.Money(15m)} - pay at the desk", sam.GetProperty("words").GetString());
        Assert.False(sam.GetProperty("paid").GetBoolean());
        Assert.Equal("Paid ✓ - thank you!", alex.GetProperty("words").GetString());
        Assert.True(alex.GetProperty("paid").GetBoolean());

        // No fee tonight (or an older caller): no "entry" at all.
        using var none = JsonDocument.Parse(RelayDocuments.CheckIn(roster, profiles, settings, "Tonight", new Dictionary<string, string?>()));
        Assert.All(none.RootElement.GetProperty("drivers").EnumerateArray(), d => Assert.False(d.TryGetProperty("entry", out _)));
    }

    // ------------------------------------------------------------ swaps, pure

    [Fact]
    public void A_cover_key_is_short_steady_and_not_the_badge()
    {
        var key = MarshalSwaps.KeyOf(Badge(7));

        Assert.Equal(MarshalSwaps.KeyLength, key.Length);
        Assert.Matches("^[0-9a-f]{12}$", key);
        Assert.Equal(key, MarshalSwaps.KeyOf(Badge(7).ToUpperInvariant()));
        Assert.NotEqual(key, MarshalSwaps.KeyOf(Badge(8)));
        Assert.DoesNotContain(key, Badge(7), StringComparison.Ordinal);
    }

    [Fact]
    public void Names_are_short()
    {
        Assert.Equal("Max P.", MarshalSwaps.ShortName("Max Power"));
        Assert.Equal("Rhoda R.", MarshalSwaps.ShortName("  Rhoda  van rage "));
        Assert.Equal("Cher", MarshalSwaps.ShortName("Cher"));
        Assert.Equal(string.Empty, MarshalSwaps.ShortName(null));
    }

    [Fact]
    public void Nobody_driving_marshalling_or_asking_can_be_asked()
    {
        var who = MarshalSwaps.Candidates(new[] { "1", "2", "3", "4", "5", "4" }, driving: new[] { "2" }, marshalling: new[] { "3" }, asking: "1");

        Assert.Equal(new[] { "4", "5" }, who);
    }

    [Fact]
    public void A_cover_moves_the_post_and_a_cover_of_a_cover_moves_the_owner_s()
    {
        var round = new RoundDefinition { RoundLabel = "Q2 Heat 3", AssignedTransponders = new List<string> { "9" } };

        Assert.Null(MarshalSwaps.Cover(round, "1", "4"));
        Assert.Equal("4", round.MarshalCovers!["1"]);

        // 4 is covering 1's post: 4 asking 5 moves 1's post to 5.
        Assert.Null(MarshalSwaps.Cover(round, "4", "5"));
        Assert.Equal("5", round.MarshalCovers!["1"]);
        Assert.Single(round.MarshalCovers);

        Assert.NotNull(MarshalSwaps.Cover(round, "1", "9"));

        // Back to the owner undoes it.
        Assert.Null(MarshalSwaps.Cover(round, "5", "1"));
        Assert.Null(round.MarshalCovers);
    }

    // ------------------------------------------------------------ the desk

    private const string Max = "0000000000000000000a";
    private const string Hugh = "0000000000000000000b";
    private const string Rhoda = "0000000000000000000c";
    private const string Round = "0123456789abcdef0123456789abcdef";

    private sealed class Desk
    {
        private int _next;

        public AppSettings Settings { get; } = new();
        public ProfileStore Profiles { get; } = ProfileStore.Load();
        public DriverRequestStore Store { get; } = DriverRequestStore.Load();
        public DriverRequestDesk Requests { get; }
        public List<(string Round, string Marshal, string Cover)> Swaps { get; } = new();
        public List<(string Round, string Who)> InPlace { get; } = new();
        public List<(string Round, string Cover, string Made)> MadeByPhones { get; } = new();
        public List<string> Failed { get; } = new();

        /// <summary>Why the program refuses the next swap, or null to make it.</summary>
        public string? Refusal { get; set; }

        public Desk(bool needDesk)
        {
            Settings.MarshalSwapsNeedDesk = needDesk;
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "100", DriverName = "Max Power" }, out _));
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "200", DriverName = "Hugh Turn" }, out _));
            Assert.True(Profiles.TryAdd(new DriverProfile { Transponder = "300", DriverName = "Rhoda Rage" }, out _));
            var badges = new Dictionary<string, string> { [Max] = "100", [Hugh] = "200", [Rhoda] = "300" };

            Requests = new DriverRequestDesk(Store, Settings, Profiles, null, SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()), new UndoService(), _ => { },
                badge => badges.TryGetValue(badge, out var t) ? t : null)
            {
                MarshalAtPost = (round, t) =>
                {
                    if (t != "100")
                    {
                        return (false, "You're not marshalling Q2 Heat 3 any more - have a look at your duty.");
                    }

                    InPlace.Add((round, t));
                    return (true, "Thanks - Race Control has you on Turn 3 for Q2 Heat 3.");
                },
                DutyOf = (round, main) => round == Round && main == "100" ? new MarshalSwaps.Duty("Q2 Heat 3 (Stock)", "Turn 3") : null,
                CoverCandidates = (round, main) => new[] { ("200", Hugh) },
                ApplyMarshalCover = (round, marshal, cover) =>
                {
                    if (Refusal is not null)
                    {
                        return Refusal;
                    }

                    Swaps.Add((round, marshal, cover));
                    return null;
                },
            };
            Requests.MarshalSwappedByPhones = (round, cover, made) => MadeByPhones.Add((round, cover, made));
            Requests.MarshalSwapFailed = Failed.Add;
        }

        public DriverRequest Ask(string kind, Dictionary<string, string> texts, string badge)
        {
            var ask = new DriverRequest
            {
                Id = (++_next).ToString("x24"),
                Badge = badge,
                Kind = kind,
                AskedUtc = DateTime.UtcNow.AddSeconds(_next),
                Proof = DriverAsk.ByBadge,
            };

            foreach (var (k, v) in texts)
            {
                ask.Texts[k] = v;
            }

            Store.FileAll(new[] { ask }, DateTime.UtcNow, out _);
            Requests.FileWaitingWrites();
            return Store.Get(ask.Id)!;
        }
    }

    [Fact]
    public void The_new_kinds_are_writes_with_their_own_limits()
    {
        foreach (var kind in new[] { DriverAsk.Post, DriverAsk.Swap, DriverAsk.Cover })
        {
            Assert.True(DriverAsk.IsKind(kind));
            Assert.True(DriverAsk.IsWrite(kind));
        }

        Assert.Equal(40, DriverRequestStore.LimitOf("round"));
        Assert.Equal(MarshalSwaps.KeyLength, DriverRequestStore.LimitOf("to"));
        Assert.Equal(3, DriverRequestStore.LimitOf("answer"));
        Assert.True(new AppSettings().MarshalSwapsNeedDesk);
        Assert.True(SettingsTable.App.ContainsKey(nameof(AppSettings.MarshalSwapsNeedDesk)));
    }

    [Fact]
    public void I_m_at_my_post_is_filed_by_itself_and_race_control_has_them()
    {
        var desk = new Desk(needDesk: true);

        var post = desk.Ask(DriverAsk.Post, new() { ["round"] = Round }, Max);
        Assert.Equal(DriverAsk.Approved, post.State);
        Assert.Equal(new[] { (Round, "100") }, desk.InPlace);
        Assert.Contains("Turn 3", post.Said, StringComparison.Ordinal);

        var notTheirs = desk.Ask(DriverAsk.Post, new() { ["round"] = Round }, Hugh);
        Assert.Equal(DriverAsk.Declined, notTheirs.State);
    }

    [Fact]
    public void A_swap_asked_and_accepted_waits_for_the_desk_then_is_made()
    {
        var desk = new Desk(needDesk: true);

        var swap = desk.Ask(DriverAsk.Swap, new() { ["round"] = Round, ["to"] = MarshalSwaps.KeyOf(Hugh) }, Max);
        Assert.Equal(DriverAsk.Approved, swap.State);
        Assert.Equal("Asked Hugh T. to cover Turn 3 - waiting for them to say yes.", swap.Said);
        Assert.Equal(Hugh, swap.Text("toBadge"));

        var offer = Assert.Single(MarshalSwaps.Offers(desk.Store.All, DateTime.UtcNow));
        Assert.Equal((Max, Hugh, Round, "Turn 3", MarshalSwaps.Asked), (offer.FromBadge, offer.ToBadge, offer.RoundId, offer.Post, offer.State));

        var yes = desk.Ask(DriverAsk.Cover, new() { ["offer"] = swap.Id, ["answer"] = "yes" }, Hugh);
        Assert.Equal(DriverAsk.Waiting, yes.State);
        Assert.Empty(desk.Swaps);
        Assert.Equal(MarshalSwaps.AtDesk, MarshalSwaps.Offers(desk.Store.All, DateTime.UtcNow).Single().State);
        Assert.Equal("Covers Max Power's post: Turn 3 in Q2 Heat 3 (Stock)", desk.Requests.Summary(yes));

        Assert.True(desk.Requests.ApproveCover(yes, out var refusal), refusal);
        Assert.Equal(new[] { (Round, "100", "200") }, desk.Swaps);
        Assert.Equal(MarshalSwaps.Accepted, MarshalSwaps.Offers(desk.Store.All, DateTime.UtcNow).Single().State);
        Assert.Equal("You're covering Turn 3 in Q2 Heat 3 (Stock) for Max P.", desk.Store.Get(yes.Id)!.Said);
    }

    [Fact]
    public void Without_the_desk_s_ok_a_yes_is_made_by_itself_and_a_no_never_waits()
    {
        var desk = new Desk(needDesk: false);
        var swap = desk.Ask(DriverAsk.Swap, new() { ["round"] = Round, ["to"] = MarshalSwaps.KeyOf(Hugh) }, Max);

        var no = desk.Ask(DriverAsk.Cover, new() { ["offer"] = swap.Id, ["answer"] = "no" }, Hugh);
        Assert.Equal(DriverAsk.Approved, no.State);
        Assert.Equal(MarshalSwaps.Declined, MarshalSwaps.Offers(desk.Store.All, DateTime.UtcNow).Single().State);
        Assert.Empty(desk.Swaps);

        var yes = desk.Ask(DriverAsk.Cover, new() { ["offer"] = swap.Id, ["answer"] = "yes" }, Hugh);
        Assert.Equal(DriverAsk.Approved, yes.State);
        Assert.Single(desk.Swaps);

        // The host is told what the swap did, to decide whether it was late (13 Nov 2026).
        var made = Assert.Single(desk.MadeByPhones);
        Assert.Equal((Round, "200", "Hugh T. is covering Max P.'s post (Turn 3) in Q2 Heat 3 (Stock)"), made);
        Assert.Empty(desk.Failed);
    }

    [Fact]
    public void A_yes_the_program_refuses_tells_race_control_the_old_marshal_is_still_on()
    {
        var desk = new Desk(needDesk: false) { Refusal = "Q2 Heat 3 has already run." };
        var swap = desk.Ask(DriverAsk.Swap, new() { ["round"] = Round, ["to"] = MarshalSwaps.KeyOf(Hugh) }, Max);

        var yes = desk.Ask(DriverAsk.Cover, new() { ["offer"] = swap.Id, ["answer"] = "yes" }, Hugh);

        Assert.Equal(DriverAsk.Declined, yes.State);
        Assert.Empty(desk.MadeByPhones);
        Assert.Equal("Marshal swap didn't go through: Hugh T. said yes to covering Max P. (Turn 3) in Q2 Heat 3 (Stock), "
                     + "but Q2 Heat 3 has already run. Max P. is still on the post.", Assert.Single(desk.Failed));
    }

    [Fact]
    public void A_swap_the_desk_approves_raises_no_phone_notice()
    {
        var desk = new Desk(needDesk: true);
        var swap = desk.Ask(DriverAsk.Swap, new() { ["round"] = Round, ["to"] = MarshalSwaps.KeyOf(Hugh) }, Max);
        var yes = desk.Ask(DriverAsk.Cover, new() { ["offer"] = swap.Id, ["answer"] = "yes" }, Hugh);

        Assert.True(desk.Requests.ApproveCover(yes, out var refusal), refusal);
        Assert.Single(desk.Swaps);
        Assert.Empty(desk.MadeByPhones);
        Assert.Empty(desk.Failed);
    }

    [Fact]
    public void Only_the_driver_asked_can_answer_and_only_someone_who_can_cover_can_be_asked()
    {
        var desk = new Desk(needDesk: false);

        var nobody = desk.Ask(DriverAsk.Swap, new() { ["round"] = Round, ["to"] = MarshalSwaps.KeyOf(Rhoda) }, Max);
        Assert.Equal(DriverAsk.Declined, nobody.State);

        var notMarshalling = desk.Ask(DriverAsk.Swap, new() { ["round"] = Round, ["to"] = MarshalSwaps.KeyOf(Hugh) }, Rhoda);
        Assert.Equal(DriverAsk.Declined, notMarshalling.State);

        var swap = desk.Ask(DriverAsk.Swap, new() { ["round"] = Round, ["to"] = MarshalSwaps.KeyOf(Hugh) }, Max);
        var stranger = desk.Ask(DriverAsk.Cover, new() { ["offer"] = swap.Id, ["answer"] = "yes" }, Rhoda);

        Assert.Equal(DriverAsk.Declined, stranger.State);
        Assert.Empty(desk.Swaps);
        Assert.Equal(MarshalSwaps.Asked, MarshalSwaps.Offers(desk.Store.All, DateTime.UtcNow).Single().State);
    }

    // ------------------------------------------------------------ the live document

    [Fact]
    public void The_marshal_entry_says_round_next_eta_in_and_who_can_cover()
    {
        var profiles = new Dictionary<string, DriverProfile>
        {
            ["100"] = new() { Transponder = "100", DriverName = "Max Power" },
            ["200"] = new() { Transponder = "200", DriverName = "Hugh Turn" },
            ["300"] = new() { Transponder = "300", DriverName = "Rhoda Rage" },
            ["400"] = new() { Transponder = "400", DriverName = "Axel Grease" },
        };
        var badges = new Dictionary<string, string> { ["100"] = Max, ["200"] = Hugh, ["300"] = Rhoda, ["400"] = Badge(4) };
        var eta = new DateTime(2026, 11, 12, 19, 44, 50, DateTimeKind.Utc);

        var offers = new[]
        {
            new MarshalSwaps.Offer("a".PadLeft(24, '0'), Rhoda, Hugh, "r2", "Q2 Heat 3 (Stock)", "Turn 1", MarshalSwaps.Asked, Now),
        };

        var json = DriverLive.Document(new DriverLive.LiveInputs
        {
            NowUtc = Now,
            Upcoming = new[]
            {
                new UpcomingRace("Q2 Heat 2 (Stock)", new[] { "300" }, OnTrack: true, RoundId: "r1"),
                new UpcomingRace("Q2 Heat 3 (Stock)", new[] { "400" }, OnTrack: false, RoundId: "r2", EtaUtc: eta),
            },
            Duties = new[]
            {
                new MarshalAssigner.MarshalDuty("r2", "Q2 Heat 3 (Stock)", "100", "Turn 3"),
                new MarshalAssigner.MarshalDuty("r2", "Q2 Heat 3 (Stock)", "300", "Turn 1"),
            },
            CheckedIn = new[] { "100", "200", "300", "400" },
            ProfileOf = t => profiles.TryGetValue(t, out var p) ? p : null,
            BadgeOf = t => badges.TryGetValue(t, out var b) ? b : null,
            MarshalIn = (round, main) => round == "r2" && main == "100",
            CoverOffers = offers,
            MainOfBadge = b => badges.FirstOrDefault(kv => kv.Value == b).Key,
            MoneyOf = main => main == "100" ? DeskMoney.ForPhone(15m, null) : null,
        });

        var drivers = JsonDocument.Parse(json).RootElement.GetProperty("drivers");
        var max = drivers.GetProperty(Max);
        var duty = max.GetProperty("marshal");

        Assert.Equal("r2", duty.GetProperty("round").GetString());
        Assert.True(duty.GetProperty("next").GetBoolean());
        Assert.True(duty.GetProperty("in").GetBoolean());
        Assert.Equal("2026-11-12T19:45:00Z", duty.GetProperty("eta").GetString());

        // Hugh only: Axel drives it, Rhoda marshals it, Max is asking.
        var choice = Assert.Single(duty.GetProperty("choices").EnumerateArray());
        Assert.Equal(MarshalSwaps.KeyOf(Hugh), choice.GetProperty("k").GetString());
        Assert.Equal("Hugh T.", choice.GetProperty("name").GetString());

        Assert.False(drivers.GetProperty(Rhoda).GetProperty("marshal").TryGetProperty("in", out _));

        // Hugh was asked by Rhoda; Rhoda sees her ask; only Max has money.
        var ask = drivers.GetProperty(Hugh).GetProperty("coverAsk");
        Assert.Equal("Rhoda R.", ask.GetProperty("from").GetString());
        Assert.Equal("Turn 1", ask.GetProperty("post").GetString());
        Assert.Equal("asked", drivers.GetProperty(Rhoda).GetProperty("askedCover").GetProperty("state").GetString());
        Assert.Equal("Hugh T.", drivers.GetProperty(Rhoda).GetProperty("askedCover").GetProperty("to").GetString());

        Assert.False(max.GetProperty("entry").GetProperty("paid").GetBoolean());
        Assert.False(drivers.GetProperty(Hugh).TryGetProperty("entry", out _));

        // Never a transponder or another driver's badge code.
        foreach (var secret in new[] { Hugh, Rhoda, Badge(4) })
        {
            Assert.DoesNotContain(secret, max.GetRawText(), StringComparison.Ordinal);
        }
    }
}
