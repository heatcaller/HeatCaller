using System.Reflection;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 21, the check-in desk (15 Nov 2026):
/// the Driver Paid slip asked for the whole fee again from a driver who only
/// owed the extra-class top-up, and the desk log said the same (#8); a
/// top-up paid the other way rewrote the whole payment under that way, so
/// the cash and card totals were out (#12); Desk mode's strip counted only
/// the roster's hidden class chip (#13); and "first night" was when the
/// profile was made, so regulars entered tonight were badged and welcomed
/// as first-timers (#9).
/// </summary>
public sealed class BugHunt21_DeskTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static IEnumerable<Control> Everything(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;

            foreach (var inner in Everything(child))
            {
                yield return inner;
            }
        }
    }

    private sealed record Setup(CheckInControl Page, ProfileStore Profiles, Form Host, RosterGrid Grid, UndoService Undo);

    private static Setup Page(RaceEventStore? events = null, EntryPaymentStore? payments = null, params string[] checkedIn)
    {
        var profiles = ProfileStore.Load();
        profiles.UpsertMany(new List<DriverProfile>
        {
            new() { Transponder = "1001", DriverName = "Ana", ClassName = "Stock" },
            new() { Transponder = "1002", DriverName = "Ben", ClassName = "Stock" },
            new() { Transponder = "1003", DriverName = "Cal", ClassName = "Mod" },
        });

        var checkIns = CheckInStore.Load();

        foreach (var transponder in checkedIn)
        {
            checkIns.SetCheckedIn(transponder, true);
        }

        var undo = new UndoService();
        var page = new CheckInControl(profiles, checkIns, RaceClassStore.Load(), new AppSettings(), undo, events: events, payments: payments);

        var host = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-20000, -20000),
            ClientSize = new Size(1200, 700),
        };

        page.Dock = DockStyle.Fill;
        host.Controls.Add(page);
        host.Show();
        StaThread.Settle();

        return new Setup(page, profiles, host, Assert.Single(Everything(page).OfType<RosterGrid>()), undo);
    }

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 21, the check-in desk");

    private static void Call(CheckInControl page, string method, params object[] args) =>
        typeof(CheckInControl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args);

    private static DriverProfile RowProfile(RosterGrid grid, string transponder) =>
        grid.Rows.Cast<DataGridViewRow>().Select(row => row.Tag as DriverProfile).Single(p => p?.Transponder == transponder)!;

    /// <summary>Tonight: $15 to enter, $10 an extra class, Stock and Mod racing.</summary>
    private static RaceEventStore Tonight(out RaceEvent tonight)
    {
        var events = RaceEventStore.Load();
        tonight = new RaceEvent
        {
            Name = "Tonight",
            EntryFee = 15m,
            ExtraClassFee = 10m,
            ClassNames = new List<string> { "Stock", "Mod" },
        };
        events.Upsert(tonight);
        events.SetActive(tonight.Id);
        return events;
    }

    // ---------------------------------------------------------------- #12

    [Fact]
    public void A_top_up_paid_by_card_after_cash_keeps_the_cash_and_card_apart()
    {
        var cash = DeskMoney.Settle(null, PaidHow.Cash, 15m, string.Empty, DateTime.UtcNow);
        Assert.Equal(15m, cash.Taken);
        Assert.False(cash.TopUp);

        // A second class ticked: $10 more, taken by card.
        var topUp = DeskMoney.Settle(cash.Payment, PaidHow.Card, 25m, string.Empty, DateTime.UtcNow);
        Assert.True(topUp.TopUp);
        Assert.Equal(10m, topUp.Taken);
        Assert.Equal(25m, topUp.Payment!.Amount);
        Assert.Equal(15m, DeskMoney.CashOf(topUp.Payment));
        Assert.Equal(10m, DeskMoney.CardOf(topUp.Payment));
        Assert.Equal(0m, DeskMoney.StillOwed(topUp.Payment, 25m));
        Assert.Equal($"Cash {DeskMoney.Money(15m)} + Card {DeskMoney.Money(10m)}", DeskMoney.Words(topUp.Payment, 25m));

        var totals = DeskMoney.Count(new[] { ("Sam", 25m, (EntryPayment?)topUp.Payment) });
        Assert.Equal(15m, totals.Cash);
        Assert.Equal(10m, totals.Card);
        Assert.Empty(totals.Owing);

        // A third class, cash again: the cash side grows, the card stays.
        var again = DeskMoney.Settle(topUp.Payment, PaidHow.Cash, 35m, string.Empty, DateTime.UtcNow);
        Assert.Equal(25m, DeskMoney.CashOf(again.Payment));
        Assert.Equal(10m, DeskMoney.CardOf(again.Payment));
    }

    [Fact]
    public void Putting_right_a_driver_paid_in_full_still_moves_the_whole_fee_and_comp_and_owing_are_as_they_were()
    {
        var cash = new EntryPayment { How = PaidHow.Cash, Amount = 15m };

        var card = DeskMoney.Settle(cash, PaidHow.Card, 15m, string.Empty, DateTime.UtcNow).Payment!;
        Assert.Equal(PaidHow.Card, card.How);
        Assert.Equal(15m, DeskMoney.CardOf(card));
        Assert.Equal(0m, DeskMoney.CashOf(card));

        Assert.Equal(PaidHow.Comp, DeskMoney.Settle(cash, PaidHow.Comp, 15m, "Marshal", DateTime.UtcNow).Payment!.How);
        Assert.Null(DeskMoney.Settle(cash, PaidHow.Owing, 15m, string.Empty, DateTime.UtcNow).Payment);
    }

    [Fact]
    public void A_payment_saved_before_the_split_reads_as_all_one_way()
    {
        var old = JsonSerializer.Deserialize<EntryPayment>("""{ "How": 2, "Amount": 25, "Note": "", "AtUtc": "2026-11-14T19:00:00Z" }""")!;

        Assert.Equal(PaidHow.Card, old.How);
        Assert.Equal(25m, DeskMoney.CardOf(old));
        Assert.Equal(0m, DeskMoney.CashOf(old));
        Assert.Equal($"Card {DeskMoney.Money(25m)}", DeskMoney.Words(old, 25m));
    }

    [Fact]
    public void At_the_desk_a_card_top_up_is_logged_as_what_was_taken_and_undo_puts_the_cash_back() =>
        OnUiThread(() =>
        {
            var events = Tonight(out var tonight);
            var payments = EntryPaymentStore.InMemory();
            var s = Page(events: events, payments: payments, checkedIn: "1001");

            using (s.Page)
            using (s.Host)
            {
                Call(s.Page, "Settle", tonight, new List<DriverProfile> { s.Profiles.Get("1001")! }, PaidHow.Cash, string.Empty);
                Call(s.Page, "ChangeClass", RowProfile(s.Grid, "1001"), "Ana", "Mod", true);
                Call(s.Page, "Settle", tonight, new List<DriverProfile> { s.Profiles.Get("1001")! }, PaidHow.Card, string.Empty);

                var payment = payments.Get(tonight.Id, "1001");
                Assert.Equal(15m, DeskMoney.CashOf(payment));
                Assert.Equal(10m, DeskMoney.CardOf(payment));

                Assert.Equal($"paid {DeskMoney.Money(10m)} more card", s.Page.DeskLog[0].What);

                var cashBox = Everything(s.Page).OfType<Label>().Single(l => l.Text.StartsWith("Taken tonight:", StringComparison.Ordinal));
                Assert.StartsWith($"Taken tonight: {DeskMoney.Money(15m)} cash · {DeskMoney.Money(10m)} card", cashBox.Text, StringComparison.Ordinal);

                s.Undo.Undo();

                var back = payments.Get(tonight.Id, "1001")!;
                Assert.Equal(PaidHow.Cash, back.How);
                Assert.Equal(15m, back.Amount);
                Assert.Equal(0m, back.OtherWay);
            }
        });

    // ---------------------------------------------------------------- #8

    [Fact]
    public void The_driver_paid_slip_asks_only_for_the_top_up_from_a_driver_who_paid_for_one_class() =>
        OnUiThread(() =>
        {
            var events = Tonight(out var tonight);
            var payments = EntryPaymentStore.InMemory();
            var s = Page(events: events, payments: payments, checkedIn: "1001");

            using (s.Page)
            using (s.Host)
            {
                Call(s.Page, "Settle", tonight, new List<DriverProfile> { s.Profiles.Get("1001")! }, PaidHow.Cash, string.Empty);
                Call(s.Page, "ChangeClass", RowProfile(s.Grid, "1001"), "Ana", "Mod", true);

                s.Page.OpenDriverPaid();
                var slip = s.Page.PaidForm!;

                try
                {
                    s.Page.AddToPaidSlip(new[] { s.Profiles.Get("1001")!, s.Profiles.Get("1002")! });

                    var total = (Label)typeof(DriverPaidForm).GetField("_lblTotal", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(slip)!;

                    // Ana owes $10 more, Ben his $15: $25, not $40.
                    Assert.Equal($"2 drivers - {DeskMoney.Money(25m)} to take", total.Text);
                }
                finally
                {
                    slip.Close();
                    slip.Dispose();
                }
            }
        });

    // ---------------------------------------------------------------- #13

    [Fact]
    public void In_desk_mode_the_strip_counts_everyone_whatever_class_chip_is_left_picked() =>
        OnUiThread(() =>
        {
            var s = Page(checkedIn: new[] { "1001", "1003" });

            using (s.Page)
            using (s.Host)
            {
                var strip = Assert.Single(Everything(s.Page).OfType<ReadinessStrip>());
                string CheckedIn() => strip.Tiles.Single(t => t.Caption == "Checked in") is var t ? $"{t.Value} {t.Detail}" : string.Empty;

                s.Page.ShowClass("Stock");
                Assert.Equal("1 of 2", CheckedIn());

                s.Page.ShowDeskMode(true);
                Assert.Equal("2 of 3", CheckedIn());

                s.Page.ShowDeskMode(false);
                Assert.Equal("1 of 2", CheckedIn());
            }
        });

    // ---------------------------------------------------------------- #9

    private static SessionRecord Raced(DateTime utc, params string[] transponders) => new()
    {
        SessionType = RaceSessionType.Race,
        StartedAtUtc = utc,
        Results = transponders.Select((t, i) => new SessionResultEntry { Position = i + 1, Transponder = t, Laps = 10 }).ToList(),
    };

    private static List<SessionRecord> FourEarlierNights(params string[] transponders) =>
        Enumerable.Range(1, 4).Select(week => Raced(DateTime.UtcNow.AddDays(-7 * week), transponders)).ToList();

    [Fact]
    public void A_regular_whose_profile_was_made_tonight_is_not_on_their_first_night()
    {
        var before = NewDrivers.RacedBefore.From(FourEarlierNights("2001"), DateTime.Now);

        // Entered tonight from the entry list - but racing here for weeks.
        var regular = new DriverProfile { Transponder = "2001", DriverName = "Reg", FirstSeenUtc = DateTime.UtcNow };

        Assert.False(NewDrivers.FirstNight(regular, before));
        Assert.Equal(string.Empty, NewDrivers.Badge(regular, before));
    }

    [Fact]
    public void A_newcomer_who_signed_up_last_week_is_on_their_first_night()
    {
        var before = NewDrivers.RacedBefore.From(FourEarlierNights("2001"), DateTime.Now);
        var newcomer = new DriverProfile { Transponder = "2002", DriverName = "Nia", FirstSeenUtc = DateTime.UtcNow.AddDays(-6) };

        Assert.True(NewDrivers.FirstNight(newcomer, before));
        Assert.Equal(" · first night", NewDrivers.Badge(newcomer, before));
    }

    [Fact]
    public void A_driver_who_raced_on_another_transponder_of_theirs_is_not_new()
    {
        var before = NewDrivers.RacedBefore.From(FourEarlierNights("OLD-1"), DateTime.Now);
        var driver = new DriverProfile
        {
            Transponder = "NEW-1",
            Transponders = new List<OwnedTransponder> { new() { Id = "OLD-1" } },
        };

        Assert.False(NewDrivers.FirstNight(driver, before));
    }

    [Fact]
    public void A_club_with_too_few_nights_on_file_welcomes_nobody_as_a_first_timer()
    {
        // The club's first night with HeatCaller: nothing earlier at all.
        var nothing = NewDrivers.RacedBefore.From(new List<SessionRecord> { Raced(DateTime.UtcNow, "2001") }, DateTime.Now);
        Assert.False(NewDrivers.FirstNight(new DriverProfile { Transponder = "2001", FirstSeenUtc = DateTime.UtcNow }, nothing));

        // Three nights is still too few (CommentaryShowFacts.LeastDaysOnFile).
        var three = NewDrivers.RacedBefore.From(FourEarlierNights("2001").Take(3), DateTime.Now);
        Assert.False(NewDrivers.FirstNight(new DriverProfile { Transponder = "2002" }, three));

        // A ticked novice is still new.
        Assert.True(NewDrivers.IsNew(new DriverProfile { Transponder = "2002", IsNovice = true }, three));
        Assert.False(NewDrivers.FirstNight(new DriverProfile { Transponder = "2002" }, null));
    }

    [Fact]
    public void Tonights_own_races_and_demo_races_do_not_make_a_driver_a_regular()
    {
        var history = FourEarlierNights("2001");
        history.Add(Raced(DateTime.UtcNow, "2002"));
        history.Add(new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            StartedAtUtc = DateTime.UtcNow.AddDays(-3),
            IsDemo = true,
            Results = new List<SessionResultEntry> { new() { Position = 1, Transponder = "2002" } },
        });

        Assert.True(NewDrivers.FirstNight(new DriverProfile { Transponder = "2002" }, NewDrivers.RacedBefore.From(history, DateTime.Now)));
    }
}
