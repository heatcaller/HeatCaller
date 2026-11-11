namespace HeatCaller.Tests;

/// <summary>
/// Where the pack chart's Trend date labels go (10 Nov 2026, bug hunt 16):
/// a plot narrower than a label - a narrow Batteries card at a large text
/// size - used to make Math.Clamp throw from OnPaint.
/// </summary>
public sealed class PackChartControlTests
{
    [Fact]
    public void DateLabelIsCentredOnItsDateWhenItFits()
    {
        Assert.Equal(90f, PackChartControl.DateLabelLeft(100f, 20f, 40f, 300f));
    }

    [Fact]
    public void DateLabelIsKeptInsideThePlot()
    {
        Assert.Equal(40f, PackChartControl.DateLabelLeft(45f, 20f, 40f, 300f));
        Assert.Equal(280f, PackChartControl.DateLabelLeft(298f, 20f, 40f, 300f));
    }

    [Fact]
    public void DateLabelWiderThanThePlotStartsAtItsLeftEdgeInsteadOfThrowing()
    {
        // A 10px plot (the floor Draw allows) and a 60px "M/d" label at 200% text.
        var left = PackChartControl.DateLabelLeft(45f, 60f, 40f, 50f);

        Assert.Equal(40f, left);
    }
}
