using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24: the pack chart's Trend dates (Batteries tab) were a fixed
/// month/day, so a club whose PC writes the day first read 3 November
/// ("11/3") as 11 March. They now follow the PC's own order, as the Trend
/// chart's have since bug hunt 17.
/// </summary>
public class BugHunt24_PackChartControlTests
{
    [Fact]
    public void Pack_chart_trend_dates_put_day_and_month_in_the_order_the_pc_writes_them()
    {
        var thirdOfNovember = new DateTime(2026, 11, 3, 19, 30, 0, DateTimeKind.Local);

        Assert.Equal("3/11", PackChartControl.DateLabel(thirdOfNovember, CultureInfo.GetCultureInfo("en-GB")));
        Assert.Equal("3/11", PackChartControl.DateLabel(thirdOfNovember, CultureInfo.GetCultureInfo("en-AU")));
        Assert.Equal("3.11", PackChartControl.DateLabel(thirdOfNovember, CultureInfo.GetCultureInfo("de-DE")));

        // Month first where that is how dates are written.
        Assert.Equal("11/3", PackChartControl.DateLabel(thirdOfNovember, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void Pack_chart_draws_its_trend_dates_through_the_date_label_not_a_fixed_month_day()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "PackChartControl.cs"));

        Assert.DoesNotContain("ToString(\"M/d\")", source);
        Assert.Contains("DateLabel(at.ToLocalTime(), System.Globalization.CultureInfo.CurrentCulture)", source);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No check.cmd above the test folder.");
    }
}
