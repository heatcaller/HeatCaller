using System.Text.RegularExpressions;

namespace HeatCaller.Tests;

/// <summary>
/// Usage totals, what the plan's lookback added (19 Nov 2026): the starter
/// feature events, dialogs as rows of their own (named only from the fixed
/// list of windows), the text size on each day, every name fitting what the
/// relay takes, and a day ending at midnight.
/// </summary>
public sealed class AppUsageLookbackTests : IDisposable
{
    /// <summary>What the relay keeps as a page, tab or event name (relay/src/appusage.ts NAME).</summary>
    private static readonly Regex RelayName = new("^[a-z0-9_.]{1,64}$", RegexOptions.CultureInvariant);

    private static readonly AppUsage.Context Evening = new(RaceDay: false, DeskView: false);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hc-usage-look-" + Guid.NewGuid().ToString("N"));

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

    private AppUsage New(params DriverProfile[] drivers) => new(Path.Combine(_folder, AppUsage.FileName), () => drivers, "0.63.0");

    [Fact]
    public void The_starter_feature_events_are_counted_and_fit_the_relay()
    {
        string[] starters = ["nav_search_used", "manage_navigation_opened", "warmup_flag", "photo_finish_opened", "overlay_editor_opened", "chart_exported", "tour_finished", "tour_skipped"];
        var usage = New();

        foreach (var name in starters)
        {
            Assert.Contains(name, AppUsage.Events);
            Assert.Matches(RelayName, name);
            usage.Count(name, new DateTime(2026, 11, 19, 20, 0, 0));
        }

        usage.Count("something_typed", new DateTime(2026, 11, 19, 20, 0, 0));
        Assert.Equal(starters.Order(), usage.Days.Single().Events.Keys.Order());
    }

    [Fact]
    public void Note_without_a_window_counts_nothing_and_never_throws()
    {
        AppUsageTracker.Note("warmup_flag");
    }

    [Fact]
    public void A_dialog_is_named_from_the_list_never_from_its_own_title()
    {
        using var listed = new Form { Text = "Corner Marshals" };
        using var filled = new Form { Text = "Qualifying Standings (best 2 of 3)" };
        using var named = new Form { Text = "Edit Billy Smith" };

        Assert.Equal("Corner Marshals", AppUsageTracker.DialogName(listed));
        Assert.Equal("Qualifying Standings", AppUsageTracker.DialogName(filled));
        Assert.Null(AppUsageTracker.DialogName(named));
        Assert.Null(AppUsageTracker.DialogName(null));
    }

    [Fact]
    public void Every_listed_window_makes_a_name_the_relay_takes()
    {
        var usage = New();

        foreach (var window in LayoutAudit.ConvertedWindows.Where(w => w.Length > 0))
        {
            var slug = usage.Scrub(AppUsageTracker.DialogPrefix + window);
            Assert.Matches(RelayName, slug);
            Assert.StartsWith("dialog_", slug, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Any_caption_scrubs_to_a_name_the_relay_takes_or_to_nothing()
    {
        var usage = New();

        foreach (var caption in new[] { "Live Timing", "Who's Racing", "Setup › Templates", "A/B test: 100%!", "ÉTÉ Réglages", new string('x', 200), "   " })
        {
            var slug = usage.Scrub(caption);
            Assert.True(slug.Length == 0 || RelayName.IsMatch(slug), $"{caption} -> {slug}");
        }
    }

    [Theory]
    [InlineData(1f, 100)]
    [InlineData(1.25f, 125)]
    [InlineData(1.12f, 110)]
    [InlineData(0.2f, 50)]
    [InlineData(9f, 300)]
    [InlineData(float.NaN, 100)]
    public void The_text_size_is_kept_to_the_nearest_five_percent(float scale, int percent)
    {
        Assert.Equal(percent, AppUsage.Context.Percent(scale));
    }

    [Fact]
    public void A_day_keeps_the_text_size_it_was_last_used_at()
    {
        var usage = New();
        var at = new DateTime(2026, 11, 19, 20, 0, 0);

        usage.Enter("Live Timing", "", at, Evening with { TextSize = 100 });
        usage.Enter("Setup", "", at.AddMinutes(1), Evening with { TextSize = 125 });

        Assert.Equal(125, usage.Days.Single().TextSize);
        Assert.Contains("text size 125%", usage.ReportText(), StringComparison.Ordinal);
        Assert.Contains("\"textSize\": 125", usage.ReportJson(), StringComparison.Ordinal);
    }

    [Fact]
    public void Time_after_midnight_goes_to_the_new_day()
    {
        var usage = New();
        var late = new DateTime(2026, 11, 19, 23, 59, 50);

        usage.Enter("Live Timing", "", late, Evening);
        usage.Mark(late.AddSeconds(5), inFront: true, active: true, Evening);
        usage.Mark(late.AddSeconds(20), inFront: true, active: true, Evening);

        var before = usage.Days.Single(d => d.Date == new DateOnly(2026, 11, 19)).Rows.Single();
        var after = usage.Days.Single(d => d.Date == new DateOnly(2026, 11, 20)).Rows.Single();
        Assert.Equal(5, before.ShownSeconds);
        Assert.Equal(15, after.ShownSeconds);
        Assert.Equal(0, after.Opens);
    }
}
