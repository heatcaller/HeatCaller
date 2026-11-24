namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): a points cell from a French or Nordic
/// spreadsheet groups thousands with a non-breaking space, and the import
/// used to call it not a number.
/// </summary>
public sealed class BugHunt24_DriverImportTests
{
    [Theory]
    [InlineData("1 234", 1234)]
    [InlineData("1 234", 1234)]
    [InlineData("1 234", 1234)]
    [InlineData("-1 234", -1234)]
    [InlineData("12 345 pts", 12345)]
    [InlineData("1 234", 1234)]
    public void BugHunt24_PointsGroupedWithANonBreakingSpaceAreRead(string text, int expected)
    {
        Assert.True(DriverImport.TryParsePoints(text, out var points));
        Assert.Equal(expected, points);
    }

    [Fact]
    public void BugHunt24_NonBreakingSpaceNotInGroupsOfThreeIsStillRefused()
    {
        Assert.False(DriverImport.TryParsePoints("1 45", out _));
    }
}
