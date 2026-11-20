namespace HeatCaller.Tests;

/// <summary>
/// Usage totals counted on this PC (19 Nov 2026, round 1): the two clocks,
/// opens, the day's rows, the file, and that no driver - above all no
/// junior - is ever named in them.
/// </summary>
public sealed class AppUsageTests : IDisposable
{
    private static readonly AppUsage.Context Evening = new(RaceDay: false, DeskView: false);
    private static readonly DateTime Six = new(2026, 11, 19, 18, 0, 0);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hc-usage-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_folder, AppUsage.FileName);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private AppUsage New(params DriverProfile[] drivers) => new(FilePath, () => drivers, "0.63.0");

    private static AppUsage.Row Row(AppUsage usage, string page, string tab = "") =>
        usage.Days.Single().Rows.Single(r => r.Page == page && r.Tab == tab);

    // --- The two clocks ---------------------------------------------------------

    [Fact]
    public void Time_in_use_counts_as_both_in_use_and_on_screen()
    {
        var usage = New();
        usage.Enter("Race Control", "", Six, Evening);
        usage.Mark(Six.AddSeconds(15), inFront: true, active: true, Evening);
        usage.Mark(Six.AddSeconds(30), inFront: true, active: true, Evening);

        var row = Row(usage, "race_control");
        Assert.Equal(30, row.ShownSeconds);
        Assert.Equal(30, row.ActiveSeconds);
        Assert.Equal(1, row.Opens);
    }

    [Fact]
    public void Nobody_at_the_keyboard_is_on_screen_but_not_in_use()
    {
        var usage = New();
        usage.Enter("Live Timing", "", Six, Evening);
        usage.Mark(Six.AddSeconds(15), inFront: true, active: false, Evening);
        usage.Mark(Six.AddSeconds(30), inFront: true, active: false, Evening);
        usage.Mark(Six.AddSeconds(45), inFront: true, active: false, Evening);

        var row = Row(usage, "live_timing");
        Assert.Equal(45, row.ShownSeconds);
        Assert.Equal(15, row.ActiveSeconds); // the first stretch, from the click that opened it
    }

    [Fact]
    public void Another_window_in_front_counts_nothing()
    {
        var usage = New();
        usage.Enter("Setup", "", Six, Evening);
        usage.Mark(Six.AddSeconds(15), inFront: false, active: false, Evening);
        usage.Mark(Six.AddSeconds(30), inFront: false, active: false, Evening);

        var row = Row(usage, "setup");
        Assert.Equal(15, row.ShownSeconds); // only up to the tick that found it behind
    }

    [Fact]
    public void A_long_gap_is_the_pc_asleep_and_counts_nothing()
    {
        var usage = New();
        usage.Enter("Setup", "", Six, Evening);
        usage.Mark(Six.AddHours(2), inFront: true, active: true, Evening);

        Assert.Equal(0, Row(usage, "setup").ShownSeconds);
    }

    [Fact]
    public void On_screen_time_stops_at_six_hours_a_row_a_day()
    {
        var usage = New();
        usage.Enter("Live Timing", "", Six.AddHours(-12), Evening);

        for (var at = Six.AddHours(-12); at < Six.AddHours(5); at = at.AddSeconds(15))
        {
            usage.Mark(at.AddSeconds(15), inFront: true, active: false, Evening);
        }

        Assert.Equal(AppUsage.MostShownSecondsPerRow, Row(usage, "live_timing").ShownSeconds);
    }

    // --- Opens and rows ---------------------------------------------------------

    [Fact]
    public void Opening_the_same_page_again_without_leaving_it_is_not_a_second_open()
    {
        var usage = New();
        usage.Enter("Setup", "Tonight", Six, Evening);
        usage.Enter("Setup", "Tonight", Six.AddSeconds(5), Evening);
        usage.Enter("Setup", "Club rules", Six.AddSeconds(10), Evening);
        usage.Enter("Setup", "Tonight", Six.AddSeconds(20), Evening);

        Assert.Equal(2, Row(usage, "setup", "tonight").Opens);
        Assert.Equal(1, Row(usage, "setup", "club_rules").Opens);
    }

    [Fact]
    public void A_race_day_and_desk_view_are_rows_of_their_own()
    {
        var usage = New();
        usage.Enter("Who's Racing", "", Six, Evening);
        usage.Enter("Race Control", "", Six.AddSeconds(5), Evening);
        usage.Enter("Who's Racing", "", Six.AddSeconds(10), new AppUsage.Context(RaceDay: true, DeskView: true));

        var rows = usage.Days.Single().Rows.Where(r => r.Page == "whos_racing").ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.RaceDay && r.DeskView);
    }

    [Fact]
    public void Only_events_on_the_list_are_counted()
    {
        var usage = New();
        usage.Count("nav_search_used", Six);
        usage.Count("nav_search_used", Six);
        usage.Count("anything_else", Six);

        var events = usage.Days.Single().Events;
        Assert.Equal(2, events["nav_search_used"]);
        Assert.False(events.ContainsKey("anything_else"));
    }

    [Fact]
    public void Captions_lose_their_counts_and_punctuation()
    {
        var usage = New();

        Assert.Equal("inbox", usage.Scrub("Inbox (3)"));
        Assert.Equal("next_steps", usage.Scrub("  Next Steps "));
        Assert.Equal("", usage.Scrub("(12)"));
    }

    // --- Never a driver ---------------------------------------------------------

    [Fact]
    public void A_caption_with_any_drivers_name_in_it_is_unknown_driver()
    {
        var usage = New(
            new DriverProfile { Transponder = "1001", DriverName = "Mia Kent", AnnouncerName = "Mighty Mia", IsJunior = true },
            new DriverProfile { Transponder = "1002", DriverName = "Billy Stone" });

        Assert.Equal(AppUsage.UnknownDriver, usage.Scrub("Mia Kent"));
        Assert.Equal(AppUsage.UnknownDriver, usage.Scrub("Laps for Kent"));
        Assert.Equal(AppUsage.UnknownDriver, usage.Scrub("Mighty"));
        Assert.Equal(AppUsage.UnknownDriver, usage.Scrub("Billy's garage"));
        Assert.Equal(AppUsage.UnknownDriver, usage.Scrub("Billy’s garage"));
        Assert.Equal("race_control", usage.Scrub("Race Control"));
    }

    [Fact]
    public void A_days_report_with_juniors_on_the_roster_names_none_of_them()
    {
        var juniors = new[]
        {
            new DriverProfile { Transponder = "2001", DriverName = "Mia Kentworth", AnnouncerName = "Turbo Mia", IsJunior = true, JuniorAge = JuniorAge.Under13 },
            new DriverProfile { Transponder = "2002", DriverName = "Oscar Lindqvist", AnnouncerName = "Oz", IsJunior = true },
        };
        var usage = New(juniors);

        // Tabs that carry their names, as a page about one driver might.
        usage.Enter("Driver Hub", "Mia Kentworth", Six, Evening);
        usage.Enter("Driver Hub", "Oscar Lindqvist (Juniors)", Six.AddSeconds(10), Evening);
        usage.Enter("Mia K", "", Six.AddSeconds(20), Evening);
        usage.Enter("Turbo Mia", "Laps", Six.AddSeconds(30), Evening);
        usage.Mark(Six.AddSeconds(40), inFront: true, active: true, Evening);
        usage.Save(Six.AddSeconds(40));

        var everything = usage.ReportJson() + usage.ReportText() + File.ReadAllText(FilePath);

        foreach (var junior in juniors)
        {
            foreach (var word in (junior.DriverName + " " + junior.AnnouncerName).Split(' '))
            {
                if (word.Length >= 3)
                {
                    Assert.DoesNotContain(word, everything, StringComparison.OrdinalIgnoreCase);
                }
            }

            Assert.DoesNotContain(junior.Transponder, everything, StringComparison.Ordinal);
        }

        Assert.Contains(AppUsage.UnknownDriver, everything, StringComparison.Ordinal);
    }

    // --- The file ---------------------------------------------------------------

    [Fact]
    public void The_totals_survive_a_restart()
    {
        var usage = New();
        usage.Enter("Setup", "Tonight", Six, Evening);
        usage.Mark(Six.AddSeconds(15), inFront: true, active: true, Evening);
        usage.Save(Six.AddSeconds(15));

        var again = New();
        var row = Row(again, "setup", "tonight");
        Assert.Equal(15, row.ActiveSeconds);
        Assert.Equal("0.63.0", again.Days.Single().Version);
    }

    [Fact]
    public void Days_older_than_a_month_are_dropped_on_save()
    {
        var usage = New();
        usage.Enter("Setup", "", Six.AddDays(-40), Evening);
        usage.Enter("Race Control", "", Six, Evening);
        usage.Save(Six);

        Assert.Single(New().Days);
    }

    [Fact]
    public void A_damaged_file_starts_again_rather_than_stopping_the_app()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "{ not json");

        Assert.Empty(New().Days);
    }

    [Fact]
    public void Clearing_forgets_everything()
    {
        var usage = New();
        usage.Enter("Setup", "", Six, Evening);
        usage.Clear();
        usage.Save(Six);

        Assert.Empty(New().Days);
    }
}
