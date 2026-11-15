using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 19, Who's Racing:
/// #2 and #3 - ticking a class in the Class cell, or recording papers, saved
/// the row's copy of the driver from when the list was drawn, putting back a
/// car picked on the phone since;
/// #15 - a race day started from another tab left the list's ticks on the
/// old day, so the next phone check-in logged a "from a phone" line for
/// almost every driver;
/// #17 - Desk mode's "Owes $15" was not rebuilt when the driver's fee
/// changed, while its Paid Cash button took the new fee.
/// </summary>
public sealed class BugHunt19CheckInTests : IDisposable
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

    private sealed record Setup(CheckInControl Page, ProfileStore Profiles, CheckInStore CheckIns, Form Host, RosterGrid Grid);

    private static Setup Page(AppSettings? settings = null, RaceEventStore? events = null, EntryPaymentStore? payments = null,
        string? dayId = null, params string[] checkedIn)
    {
        var profiles = ProfileStore.Load();
        profiles.UpsertMany(new List<DriverProfile>
        {
            new()
            {
                Transponder = "1001",
                DriverName = "Ana",
                ClassName = "Stock",
                CarModel = "Car A",
                Cars = new List<string> { "Car A", "Car B" },
            },
            new() { Transponder = "1002", DriverName = "Ben", ClassName = "Stock" },
        });

        var checkIns = CheckInStore.Load();

        if (dayId is not null)
        {
            checkIns.CurrentEventId = dayId;
        }

        foreach (var transponder in checkedIn)
        {
            checkIns.SetCheckedIn(transponder, true);
        }

        var page = new CheckInControl(profiles, checkIns, RaceClassStore.Load(), settings ?? new AppSettings(), new UndoService(),
            events: events, payments: payments);

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

        return new Setup(page, profiles, checkIns, host, Assert.Single(Everything(page).OfType<RosterGrid>()));
    }

    private static void OnUiThread(Action body) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => "bug hunt 19, Who's Racing");

    private static void Call(CheckInControl page, string method, params object[] args) =>
        typeof(CheckInControl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args);

    private static DriverProfile RowProfile(RosterGrid grid, string transponder) =>
        grid.Rows.Cast<DataGridViewRow>().Select(row => row.Tag as DriverProfile).Single(p => p?.Transponder == transponder)!;

    /// <summary>The phone's "racing tonight: Car B" - a new copy stored, the list not redrawn.</summary>
    private static void PhonePicksCarB(ProfileStore profiles)
    {
        var after = ProfileStore.Snapshot(profiles.Get("1001")!);
        after.SetActiveCar("Car B");
        profiles.Upsert(after);
    }

    [Fact]
    public void Ticking_a_class_keeps_a_car_picked_on_the_phone_since_the_list_was_drawn() =>
        OnUiThread(() =>
        {
            var s = Page();

            using (s.Page)
            using (s.Host)
            {
                var row = RowProfile(s.Grid, "1001");
                PhonePicksCarB(s.Profiles);

                Call(s.Page, "ChangeClass", row, "Ana", "Mod", true);

                var saved = s.Profiles.Get("1001")!;
                Assert.Equal("Car B", saved.CarModel);
                Assert.True(saved.RunsClass("Mod"));
                Assert.True(saved.RunsClass("Stock"));
            }
        });

    [Fact]
    public void Recording_papers_keeps_a_car_picked_on_the_phone_since_the_list_was_drawn() =>
        OnUiThread(() =>
        {
            var s = Page(new AppSettings { AskForWaivers = true });

            using (s.Page)
            using (s.Host)
            {
                var row = RowProfile(s.Grid, "1001");
                PhonePicksCarB(s.Profiles);

                // The papers window as it closes with Save, waiver ticked. Not
                // shown: a test can't answer a window (StaThread closes it).
                using var form = new PapersForm("Ana", row, waivers: true, memberships: false);
                foreach (var box in Everything(form).OfType<CheckBox>().Where(b => b.Text.Contains("aiver", StringComparison.Ordinal)))
                {
                    box.Checked = true;
                }

                Call(s.Page, "SavePapers", row, "Ana", form);

                var saved = s.Profiles.Get("1001")!;
                Assert.Equal("Car B", saved.CarModel);
            }
        });

    [Fact]
    public void Recording_papers_for_several_drivers_keeps_a_car_picked_on_the_phone() =>
        OnUiThread(() =>
        {
            var s = Page(new AppSettings { AskForWaivers = true });

            using (s.Page)
            using (s.Host)
            {
                var rows = new List<DriverProfile> { RowProfile(s.Grid, "1001"), RowProfile(s.Grid, "1002") };
                PhonePicksCarB(s.Profiles);

                using var form = new PapersForm(rows.Count, waivers: true, memberships: false);
                foreach (var box in Everything(form).OfType<CheckBox>())
                {
                    box.Checked = true;
                }

                Call(s.Page, "SavePapersForMany", (IReadOnlyList<DriverProfile>)rows, form);

                var saved = s.Profiles.Get("1001")!;
                Assert.Equal("Car B", saved.CarModel);
                Assert.NotNull(saved.WaiverSignedOn);
            }
        });

    [Fact]
    public void A_new_race_day_is_not_logged_as_every_driver_checking_out_from_a_phone() =>
        OnUiThread(() =>
        {
            var s = Page(dayId: "last-night", checkedIn: "1001");

            using (s.Page)
            using (s.Host)
            {
                // Last night: Ben ticks in from his phone.
                s.CheckIns.SetCheckedIn("1002", true);
                s.Page.RefreshCheckInMarks();
                Assert.Single(s.Page.DeskLog);

                // Tonight is started from another tab; the list is not redrawn.
                s.CheckIns.CurrentEventId = "tonight";
                s.Page.RefreshCheckInMarks();

                Assert.Empty(s.Page.DeskLog);

                // A real phone check-in tonight is still logged.
                s.CheckIns.SetCheckedIn("1001", true);
                s.Page.RefreshCheckInMarks();
                var entry = Assert.Single(s.Page.DeskLog);
                Assert.Equal("Ana", entry.Who);
                Assert.Equal("checked in from a phone", entry.What);
            }
        });

    [Fact]
    public void Desk_mode_s_owes_line_follows_a_class_ticked_since() =>
        OnUiThread(() =>
        {
            var events = RaceEventStore.Load();
            var tonight = new RaceEvent
            {
                Name = "Tonight",
                EntryFee = 15m,
                ExtraClassFee = 10m,
                ClassNames = new List<string> { "Stock", "Mod" },
            };
            events.Upsert(tonight);
            events.SetActive(tonight.Id);

            var s = Page(events: events, payments: EntryPaymentStore.InMemory());

            using (s.Page)
            using (s.Host)
            {
                s.Page.ShowDeskMode(true);
                var ana = s.Profiles.Get("1001")!;
                Call(s.Page, "ShowOnDesk", ana, "Checked in", Color.Green);

                string Owes() => Everything(s.Page).OfType<Label>()
                    .Select(l => l.Text).Single(t => t.Contains("Owes ", StringComparison.Ordinal));

                Assert.Contains(DeskMoney.Money(15m), Owes(), StringComparison.Ordinal);

                // Ana runs Mod as well: ticked in Roster, then back to the desk.
                Call(s.Page, "ChangeClass", RowProfile(s.Grid, "1001"), "Ana", "Mod", true);
                Call(s.Page, "RefreshDeskCard");

                Assert.Contains(DeskMoney.Money(25m), Owes(), StringComparison.Ordinal);
            }
        });
}
