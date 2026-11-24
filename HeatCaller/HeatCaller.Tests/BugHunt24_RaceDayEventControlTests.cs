using System.Runtime.InteropServices;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, Setup's race day page (RaceDayEventControl):
/// #253 - the wheel over the marshal rule or "Next race is:" box changed the
/// setting and saved it while the page was only being scrolled;
/// #254 - the status line said "0 classes racing" for a day that races
/// every class with a driver;
/// #255 - the Start Tonight window said nothing about tonight's night being
/// open when a closed past night was the current day;
/// #256 - picking a closed past night in the Race day box on Tonight threw
/// the page over to Templates.
/// </summary>
public sealed class BugHunt24_RaceDayEventControlTests : IDisposable
{
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int Notch = 120;

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static void OnUiThread(Action body, string where) =>
        StaThread.Run(() =>
        {
            body();
            return true;
        }, TimeSpan.FromMinutes(1), () => where);

    private static RaceDayEventControl Build(RaceEventStore events, AppSettings settings)
    {
        var classes = RaceClassStore.Load();

        if (!classes.All.Any(c => c.Name == "Stock"))
        {
            classes.Upsert(new RaceClass { Name = "Stock" });
        }

        return new RaceDayEventControl(
            events, VenueStore.Load(), TrackLayoutStore.Load(), SeasonStore.Load(), CheckInStore.Load(),
            RoundDefinitionStore.Load(), DayScheduleStore.Load(), classes, undo: null, history: null, settings);
    }

    private static RaceEvent ClosedPastNight() => new()
    {
        Name = "Last Tuesday",
        ClassNames = new List<string> { "Stock" },
        StartedUtc = DateTime.UtcNow.AddDays(-7),
        FinishedUtc = DateTime.UtcNow.AddDays(-7).AddHours(4),
    };

    // --- #253 ----------------------------------------------------------------------

    [Fact]
    public void BugHunt24_The_wheel_over_the_marshal_rule_box_leaves_the_rule_alone()
    {
        OnUiThread(() =>
        {
            var settings = new AppSettings { MarshalNoShowRule = MarshalNoShowRule.LosesBestQualifier };
            using var window = new QuietWindow { ClientSize = new Size(1000, 700) };
            var page = Build(RaceEventStore.Load(), settings);
            page.Dock = DockStyle.Fill;
            window.Controls.Add(page);
            window.Show();
            page.ShowTab(RaceDayEventControl.ClubRulesTab);
            StaThread.Settle();

            var box = page.MarshalRuleBox;
            var before = box.SelectedIndex;

            Assert.True(box.Enabled);

            foreach (var delta in new[] { -Notch, -Notch, Notch, Notch, Notch })
            {
                Wheel(box, delta);

                Assert.Equal(before, box.SelectedIndex);
                Assert.Equal(MarshalNoShowRule.LosesBestQualifier, settings.MarshalNoShowRule);
            }
        }, "Marshal rule box, wheel");
    }

    private static void Wheel(Control box, int delta)
    {
        var screen = box.PointToScreen(new Point(box.Width / 2, box.Height / 2));
        var wParam = (IntPtr)(delta << 16);
        var lParam = (IntPtr)((screen.Y << 16) | (screen.X & 0xFFFF));

        SendMessage(box.Handle, WM_MOUSEWHEEL, wParam, lParam);
        StaThread.Settle();
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private sealed class QuietWindow : Form
    {
        public QuietWindow()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Opacity = 0d;
            Font = AppTheme.BodyFont;
            BackColor = AppTheme.Background;
        }

        protected override bool ShowWithoutActivation => true;
    }

    // --- #254 ----------------------------------------------------------------------

    [Fact]
    public void BugHunt24_A_day_with_no_classes_ticked_says_every_class_with_a_driver_not_0_classes()
    {
        var words = RaceDayEventControl.ClassesRacingWords(0);

        Assert.Contains("every class with a driver", words);
        Assert.DoesNotContain("0 classes", words);
        Assert.Equal("1 class racing", RaceDayEventControl.ClassesRacingWords(1));
        Assert.Equal("3 classes racing", RaceDayEventControl.ClassesRacingWords(3));
    }

    // --- #255 ----------------------------------------------------------------------

    [Fact]
    public void BugHunt24_The_open_night_is_tonights_when_a_closed_past_night_is_current()
    {
        var past = ClosedPastNight();
        var tonight = new RaceEvent { Name = "Tonight", ClassNames = new List<string> { "Stock" } };

        Assert.Same(tonight, RaceDayEventControl.OpenNightOf(past, new[] { past, tonight }));
        Assert.Same(tonight, RaceDayEventControl.OpenNightOf(tonight, new[] { past, tonight }));
        Assert.Null(RaceDayEventControl.OpenNightOf(past, new[] { past }));
    }

    [Fact]
    public void BugHunt24_The_Start_Tonight_window_names_the_open_night_by_the_Templates_rule()
    {
        var body = Between(Source("RaceDayEventControl.cs"), "private bool StartTonightWindow(", "using var ask = new NewRaceDayForm(");

        Assert.Contains("OpenNight()?.DisplayName", body);
        Assert.DoesNotContain("_events.Active is { IsLegacy: false, IsOpen: true } open", body);
    }

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("Couldn't find the repo root (no folder with check.cmd in it) above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    private static string Between(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);
        Assert.True(end >= 0, $"\"{until}\" is not after \"{from}\" any more - this test needs updating.");

        return source[start..end];
    }

    // --- #256 ----------------------------------------------------------------------

    [Fact]
    public void BugHunt24_Picking_a_closed_past_night_while_tonights_is_open_stays_on_Tonight()
    {
        OnUiThread(() =>
        {
            var events = RaceEventStore.Load();
            using var page = Build(events, new AppSettings());

            Assert.True(page.StartNewDayFromWizard(RaceDayShape.RaceDay, new[] { "Stock" }));
            page.RefreshAll();
            Assert.Equal(RaceDayEventControl.TonightTab, page.SelectedTab);

            var past = ClosedPastNight();
            events.Upsert(past);
            events.SetActive(past.Id);
            page.RefreshAll();

            Assert.Equal(past.Id, events.Active?.Id);
            Assert.Equal(RaceDayEventControl.TonightTab, page.SelectedTab);
        }, "Tonight tab, past night picked");
    }

    [Fact]
    public void BugHunt24_The_tab_is_chosen_for_the_open_night_not_only_the_current_day()
    {
        var past = ClosedPastNight();
        var tonight = new RaceEvent { Name = "Tonight", ClassNames = new List<string> { "Stock" } };
        var empty = new RaceEvent { Name = "Empty" };

        Assert.Equal(tonight.Id, RaceDayEventControl.TabNightId(past, new[] { past, tonight }));
        Assert.Equal(tonight.Id, RaceDayEventControl.TabNightId(tonight, new[] { past, tonight }));

        // An open day nobody has put anything in still counts while it is current, as before.
        Assert.Equal(empty.Id, RaceDayEventControl.TabNightId(empty, new[] { past, empty }));

        // No night open (or only an empty one left behind): Templates.
        Assert.Equal(string.Empty, RaceDayEventControl.TabNightId(past, new[] { past }));
        Assert.Equal(string.Empty, RaceDayEventControl.TabNightId(past, new[] { past, empty }));
        Assert.Equal(string.Empty, RaceDayEventControl.TabNightId(null, Array.Empty<RaceEvent>()));
    }
}
