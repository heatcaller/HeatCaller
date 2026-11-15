using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 20, Who's Racing's gates:
/// tech at a club that never runs it kept everybody from "Ready to race";
/// a class ticked after paying left its extra-class fee uncollected, the
/// driver still showing as paid; the Owes chip counted the whole roster,
/// absent drivers too; and Desk mode judged tech by a class chip left picked
/// on the roster, out of sight.
/// </summary>
public sealed class BugHunt20ReadinessTests : IDisposable
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

    private sealed record Setup(CheckInControl Page, ProfileStore Profiles, Form Host, RosterGrid Grid);

    private static Setup Page(TechInspectionStore? tech = null, RaceClassStore? classes = null, RaceEventStore? events = null,
        EntryPaymentStore? payments = null, params string[] checkedIn)
    {
        var profiles = ProfileStore.Load();
        profiles.UpsertMany(new List<DriverProfile>
        {
            new() { Transponder = "1001", DriverName = "Ana", ClassName = "Stock" },
            new() { Transponder = "1002", DriverName = "Ben", ClassName = "Stock" },
            new() { Transponder = "1003", DriverName = "Cal", ClassName = "Stock" },
        });

        var checkIns = CheckInStore.Load();

        foreach (var transponder in checkedIn)
        {
            checkIns.SetCheckedIn(transponder, true);
        }

        var page = new CheckInControl(profiles, checkIns, classes ?? RaceClassStore.Load(), new AppSettings(), new UndoService(),
            tech: tech, events: events, payments: payments);

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

        return new Setup(page, profiles, host, Assert.Single(Everything(page).OfType<RosterGrid>()));
    }

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 20, Who's Racing");

    private static void Call(CheckInControl page, string method, params object[] args) =>
        typeof(CheckInControl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args);

    private static DriverProfile RowProfile(RosterGrid grid, string transponder) =>
        grid.Rows.Cast<DataGridViewRow>().Select(row => row.Tag as DriverProfile).Single(p => p?.Transponder == transponder)!;

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

    [Fact]
    public void At_a_club_that_runs_no_tech_a_checked_in_driver_is_ready_to_race() =>
        OnUiThread(() =>
        {
            // A tech store, as MainForm always passes - but no class has a
            // checklist and nobody has been inspected.
            var tech = TechInspectionStore.Load();
            var s = Page(tech: tech, checkedIn: "1001");

            using (s.Page)
            using (s.Host)
            {
                var r = s.Page.ReadinessOf(s.Profiles.Get("1001")!);

                Assert.Equal(CheckInControl.Gate.NotRun, r.Tech);
                Assert.True(r.Ready);
            }
        });

    [Fact]
    public void A_class_ticked_after_paying_leaves_the_extra_class_fee_owed() =>
        OnUiThread(() =>
        {
            var events = Tonight(out var tonight);
            var payments = EntryPaymentStore.InMemory();
            var s = Page(events: events, payments: payments, checkedIn: "1001");

            using (s.Page)
            using (s.Host)
            {
                var sam = s.Profiles.Get("1001")!;
                Call(s.Page, "Settle", tonight, new List<DriverProfile> { sam }, PaidHow.Cash, string.Empty);

                Assert.Equal(CheckInControl.Gate.Done, s.Page.ReadinessOf(s.Profiles.Get("1001")!).Paid);

                Call(s.Page, "ChangeClass", RowProfile(s.Grid, "1001"), "Ana", "Mod", true);
                sam = s.Profiles.Get("1001")!;

                var r = s.Page.ReadinessOf(sam);
                Assert.Equal(CheckInControl.Gate.Open, r.Paid);
                Assert.False(r.Ready);

                var payment = payments.Get(tonight.Id, "1001");
                Assert.Equal(10m, DeskMoney.StillOwed(payment, CheckInControl.FeeFor(sam, tonight)));
                Assert.StartsWith($"Owes {DeskMoney.Money(10m)} more", DeskMoney.Words(payment, 25m), StringComparison.Ordinal);
            }
        });

    [Fact]
    public void Still_owed_is_what_the_fee_has_gone_up_by_since_paying()
    {
        var cash = new EntryPayment { How = PaidHow.Cash, Amount = 15m };

        Assert.Equal(0m, DeskMoney.StillOwed(cash, 15m));
        Assert.Equal(10m, DeskMoney.StillOwed(cash, 25m));
        Assert.Equal(0m, DeskMoney.StillOwed(cash, 10m));
        Assert.Equal(25m, DeskMoney.StillOwed(null, 25m));
        Assert.Equal(0m, DeskMoney.StillOwed(new EntryPayment { How = PaidHow.Comp }, 25m));

        Assert.False(DeskMoney.ForPhone(25m, cash)!.Paid);
        Assert.True(DeskMoney.ForPhone(15m, cash)!.Paid);

        var totals = DeskMoney.Count(new[] { ("Sam", 25m, (EntryPayment?)cash) });
        Assert.Equal(15m, totals.Cash);
        Assert.Equal(10m, totals.OwingAmount);
    }

    [Fact]
    public void The_owes_chip_counts_and_shows_only_drivers_who_are_here() =>
        OnUiThread(() =>
        {
            var events = Tonight(out _);
            var s = Page(events: events, payments: EntryPaymentStore.InMemory(), checkedIn: "1001");

            using (s.Page)
            using (s.Host)
            {
                s.Page.ShowOnly(CheckInControl.RosterShow.Owes);

                var shown = s.Grid.Rows.Cast<DataGridViewRow>().Select(row => (row.Tag as DriverProfile)?.Transponder).ToList();
                Assert.Equal(new[] { "1001" }, shown);
            }
        });

    [Fact]
    public void Desk_mode_judges_tech_in_every_class_whatever_chip_is_left_picked() =>
        OnUiThread(() =>
        {
            var classes = RaceClassStore.Load();
            foreach (var name in new[] { "Stock", "Mod" })
            {
                classes.Upsert(new RaceClass { Name = name, TechCheckItems = new List<string> { "Width", "Weight" } });
            }

            var tech = TechInspectionStore.Load();
            tech.CurrentEventId = "day-1";
            tech.Upsert(new TechInspection { Transponder = "1001", DriverName = "Ana", ClassName = "Stock", Result = TechResult.Passed });

            var s = Page(tech: tech, classes: classes, checkedIn: "1001");

            using (s.Page)
            using (s.Host)
            {
                Call(s.Page, "ChangeClass", RowProfile(s.Grid, "1001"), "Ana", "Mod", true);
                s.Page.ShowClass("Stock");

                // The roster, with Stock picked, judges Stock alone - as it always did.
                Assert.Equal(CheckInControl.Gate.Done, s.Page.ReadinessOf(s.Profiles.Get("1001")!).Tech);

                s.Page.ShowDeskMode(true);

                var r = s.Page.ReadinessOf(s.Profiles.Get("1001")!);
                Assert.Equal(CheckInControl.Gate.Open, r.Tech);
                Assert.False(r.Ready);

                s.Page.ShowDeskMode(false);
                Assert.Equal(CheckInControl.Gate.Done, s.Page.ReadinessOf(s.Profiles.Get("1001")!).Tech);
            }
        });
}
