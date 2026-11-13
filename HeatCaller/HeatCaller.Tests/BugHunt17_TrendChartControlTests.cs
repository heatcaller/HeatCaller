using System.Globalization;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: the Trend chart's date labels were month/day whatever the
/// PC's own order, so a club that writes the day first read 3 November
/// ("11/3") as 11 March. The label now follows the PC's short date.
/// </summary>
public class BugHunt17_TrendChartControlTests
{
    [Fact]
    public void A_date_label_puts_day_and_month_in_the_order_the_pc_writes_them()
    {
        var thirdOfNovember = new DateTime(2026, 11, 3, 19, 30, 0);

        Assert.Equal("3/11", TrendChartControl.DayLabel(thirdOfNovember, CultureInfo.GetCultureInfo("en-GB")));
        Assert.Equal("3/11", TrendChartControl.DayLabel(thirdOfNovember, CultureInfo.GetCultureInfo("en-AU")));
        Assert.Equal("3.11", TrendChartControl.DayLabel(thirdOfNovember, CultureInfo.GetCultureInfo("de-DE")));

        // Month first where that is how dates are written.
        Assert.Equal("11/3", TrendChartControl.DayLabel(thirdOfNovember, CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("11/3", TrendChartControl.DayLabel(thirdOfNovember, CultureInfo.InvariantCulture));
    }
}
