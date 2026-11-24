using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, Who's Racing and Desk mode: an empty roster under a lit
/// Everyone chip after picking a class with no tech; a second car's
/// transponder answered "No driver"; the desk card saying "Not checked in"
/// for a driver who was; the "first night" badge missing on a tick; and
/// papers going out of date at midnight in the middle of a race night.
/// </summary>
public sealed class BugHunt24_CheckInControlTests : IDisposable
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

    private static Setup Page(IEnumerable<DriverProfile> drivers, TechInspectionStore? tech = null, RaceClassStore? classes = null,
        params string[] checkedIn)
    {
        var profiles = ProfileStore.Load();
        profiles.UpsertMany(drivers.ToList());

        var checkIns = CheckInStore.Load();

        foreach (var transponder in checkedIn)
        {
            checkIns.SetCheckedIn(transponder, true);
        }

        var page = new CheckInControl(profiles, checkIns, classes ?? RaceClassStore.Load(), new AppSettings(), new UndoService(),
            tech: tech);

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
        }, TimeSpan.FromMinutes(1), () => "bug hunt 24, Who's Racing");

    private static void Call(CheckInControl page, string method, params object[] args) =>
        typeof(CheckInControl).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, args);

    private static string DeskHeadlineField(CheckInControl page) =>
        (string)typeof(CheckInControl).GetField("_deskHeadline", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private static List<string?> ShownTransponders(RosterGrid grid) =>
        grid.Rows.Cast<DataGridViewRow>().Select(row => (row.Tag as DriverProfile)?.Transponder).ToList();

    private static string? DriverCell(RosterGrid grid, string transponder) =>
        grid.Rows.Cast<DataGridViewRow>()
            .Single(row => (row.Tag as DriverProfile)?.Transponder == transponder)
            .Cells["driver"].Value as string;

    // ------------------------------------------------------------ #10, F072

    [Fact]
    public void Picking_a_class_without_tech_while_Needs_tech_is_picked_shows_the_whole_class() =>
        OnUiThread(() =>
        {
            var classes = RaceClassStore.Load();
            classes.Upsert(new RaceClass { Name = "Mod", TechCheckItems = new List<string> { "Width", "Weight" } });
            classes.Upsert(new RaceClass { Name = "Stock" });

            var s = Page(new[]
            {
                new DriverProfile { Transponder = "1001", DriverName = "Ana", ClassName = "Stock" },
                new DriverProfile { Transponder = "1002", DriverName = "Ben", ClassName = "Stock" },
                new DriverProfile { Transponder = "1003", DriverName = "Cal", ClassName = "Stock" },
                new DriverProfile { Transponder = "1004", DriverName = "Mo", ClassName = "Mod" },
            }, tech: TechInspectionStore.Load(), classes: classes, checkedIn: new[] { "1001", "1002", "1003", "1004" });

            using (s.Page)
            using (s.Host)
            {
                s.Page.ShowClass("Mod");
                s.Page.ShowOnly(CheckInControl.RosterShow.NeedsTech);
                Assert.Equal(new[] { "1004" }, ShownTransponders(s.Grid));

                s.Page.ShowClass("Stock");

                Assert.Equal(CheckInControl.RosterShow.Everyone, s.Page.Filters.Show);
                Assert.Equal(new[] { "1001", "1002", "1003" }, ShownTransponders(s.Grid).OrderBy(t => t));
            }
        });

    // ------------------------------------------------------------ #126, F073

    [Fact]
    public void A_drivers_second_transponder_typed_at_the_desk_finds_and_checks_them_in() =>
        OnUiThread(() =>
        {
            var ana = new DriverProfile { Transponder = "1111111", DriverName = "Ana", ClassName = "Stock" };
            ana.AlternateTransponder = "7654321";

            var s = Page(new[] { ana, new DriverProfile { Transponder = "2222222", DriverName = "Ben", ClassName = "Stock" } });

            using (s.Page)
            using (s.Host)
            {
                Assert.Equal("1111111", Assert.Single(s.Page.DriversNamed("7654321")).Transponder);
                Assert.Equal("1111111", Assert.Single(s.Page.DriversNamed("76543")).Transponder);

                using var box = new TextBox { Text = "7654321" };
                s.Page.HandleEntry(box, fromDesk: true);

                Assert.True(s.CheckIns.IsCheckedIn("1111111"));
                Assert.False(s.CheckIns.IsCheckedIn("7654321"));
            }
        });

    // ------------------------------------------------------------ #129, F076

    [Fact]
    public void Desk_headline_says_checked_in_for_a_driver_here_after_undo()
    {
        Assert.Equal("Not checked in", CheckInControl.DeskHeadline(false, "Checked in · 7:05 PM"));
        Assert.Equal("Checked in", CheckInControl.DeskHeadline(true, "Not checked in"));
        Assert.Equal("Checked in · 7:05 PM", CheckInControl.DeskHeadline(true, "Checked in · 7:05 PM"));
        Assert.Equal("Already checked in", CheckInControl.DeskHeadline(true, "Already checked in"));
    }

    [Fact]
    public void The_desk_card_after_undo_and_a_phone_check_in_says_checked_in() =>
        OnUiThread(() =>
        {
            var s = Page(new[] { new DriverProfile { Transponder = "1001", DriverName = "Ana", ClassName = "Stock" } });

            using (s.Page)
            using (s.Host)
            {
                var ana = s.Profiles.Get("1001")!;
                Call(s.Page, "CheckInAtDesk", ana, "at the desk");
                Assert.StartsWith("Checked in", DeskHeadlineField(s.Page), StringComparison.Ordinal);

                // The card's Undo, line for line.
                s.Page.CheckOutDrivers(new[] { ana });
                typeof(CheckInControl).GetField("_deskHeadline", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s.Page, "Not checked in");
                Call(s.Page, "RefreshDeskCard");

                // Then their phone checks them in.
                s.CheckIns.SetCheckedIn("1001", true);
                s.Page.RefreshCheckInMarks();

                Assert.StartsWith("Checked in", DeskHeadlineField(s.Page), StringComparison.Ordinal);
            }
        });

    // ------------------------------------------------------------ #128, F075

    [Fact]
    public void A_newcomer_checked_in_without_a_redraw_gets_their_badge_and_loses_it_checked_out() =>
        OnUiThread(() =>
        {
            var s = Page(new[]
            {
                new DriverProfile { Transponder = "1001", DriverName = "Nia", ClassName = "Stock", IsNovice = true },
                new DriverProfile { Transponder = "1002", DriverName = "Ben", ClassName = "Stock" },
            });

            using (s.Page)
            using (s.Host)
            {
                Assert.Equal("Nia", DriverCell(s.Grid, "1001"));

                // The right-click menu / the picked bar.
                s.Page.CheckInDrivers(new[] { s.Profiles.Get("1001")! });
                Assert.Equal("Nia · novice", DriverCell(s.Grid, "1001"));

                s.Page.CheckOutDrivers(new[] { s.Profiles.Get("1001")! });
                Assert.Equal("Nia", DriverCell(s.Grid, "1001"));

                // Their phone.
                s.CheckIns.SetCheckedIn("1001", true);
                s.Page.RefreshCheckInMarks();
                Assert.Equal("Nia · novice", DriverCell(s.Grid, "1001"));

                // A regular stays a plain name.
                Assert.Equal("Ben", DriverCell(s.Grid, "1002"));
            }
        });

    [Fact]
    public void The_roster_name_has_the_badge_only_while_here()
    {
        var nia = new DriverProfile { Transponder = "1001", DriverName = "Nia", IsNovice = true, Mentor = "Al" };

        Assert.Equal("Nia · novice, mentor Al", CheckInControl.RosterName(nia, here: true, before: null));
        Assert.Equal("Nia", CheckInControl.RosterName(nia, here: false, before: null));
        Assert.Equal("1002", CheckInControl.RosterName(new DriverProfile { Transponder = "1002" }, here: true, before: null));
    }

    // ------------------------------------------------------------ #130, F077

    [Fact]
    public void Papers_good_for_the_race_date_stay_good_past_midnight()
    {
        var settings = new AppSettings { CheckMemberships = true, AskForWaivers = true, WaiverMonths = 6 };
        var driver = new DriverProfile
        {
            Transponder = "1001",
            DriverName = "Ana",
            MembershipNumber = "M-1",
            MembershipExpires = new DateTime(2026, 11, 21),
            WaiverSignedOn = new DateTime(2026, 5, 22), // runs out 22 Nov
        };

        // 00:30 on the 22nd is still the night of the 21st.
        Assert.Empty(CheckInControl.PapersProblemsAt(driver, settings, new DateTime(2026, 11, 22, 0, 30, 0)));

        // The next night they are out of date.
        var next = CheckInControl.PapersProblemsAt(driver, settings, new DateTime(2026, 11, 22, 6, 0, 0));
        Assert.Contains(next, p => p.StartsWith("Membership expired", StringComparison.Ordinal));
        Assert.Contains(next, p => p.StartsWith("Waiver ran out", StringComparison.Ordinal));
    }

    [Fact]
    public void Who_is_racing_judges_papers_by_the_night_not_the_calendar_day()
    {
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "HeatCaller", "HeatCaller", "CheckInControl.Papers.cs"));

        Assert.Contains("PapersProblems(DriverProfile profile) => PapersProblemsAt(profile, _settings, DateTime.Now)", source);
        Assert.Contains("TonightAtTheTrack.NightOf(nowLocal)", source);
    }

    // ------------------------------------------------------------ #127, F074

    [Fact]
    public void The_desk_card_time_is_not_windows_short_time()
    {
        var source = File.ReadAllText(Path.Combine(SourceRoot(), "HeatCaller", "HeatCaller", "CheckInControl.cs"));

        Assert.DoesNotContain("{DateTime.Now:t}", source);
        Assert.Contains("ShowOnDesk(profile, CheckedInHeadline(DateTime.Now), AppTheme.Success)", source);
    }

    internal static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Couldn't find the repo root.");
    }
}

/// <summary>Bug hunt 24, #127: Desk mode's "Checked in · time" follows the 24-hour tick on Themes.</summary>
[Collection(AppTimeCollection.Name)]
public sealed class BugHunt24_CheckInControlTimeTests
{
    [Fact]
    public void The_desk_card_headline_follows_the_24_hour_tick()
    {
        var was = AppTime.Use24Hour;

        try
        {
            var at = new DateTime(2026, 11, 22, 19, 5, 0);

            AppTime.Use24Hour = true;
            Assert.Equal("Checked in · 19:05", CheckInControl.CheckedInHeadline(at));

            AppTime.Use24Hour = false;
            Assert.StartsWith("Checked in · 7:05", CheckInControl.CheckedInHeadline(at), StringComparison.Ordinal);
        }
        finally
        {
            AppTime.Use24Hour = was;
        }
    }
}
