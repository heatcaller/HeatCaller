namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): the public race control log left a correction
/// out when its words merely contained a transponder number - so at a club
/// whose cars are numbered 1, 5, 12 the laps and seconds of nearly every
/// correction hid it. Only a transponder standing as a word of its own,
/// outside the numbers the editor writes itself, counts now.
/// </summary>
public sealed class BugHunt17_MainForm_RelayTests
{
    private static readonly string[] ShortIds = { "0", "1", "2", "5", "12" };

    [Theory]
    [InlineData("time penalty 0s -> 5s")]
    [InlineData("time penalty 0s -> 12s")]
    [InlineData("time penalty 2.5s -> 0s")]
    [InlineData("time penalty 2,5s -> 0s")]
    [InlineData("lap penalty 0 -> 1")]
    [InlineData("laps added 0 -> +1")]
    [InlineData("laps added +2 -> -1")]
    [InlineData("finished -> DQ")]
    [InlineData("laps added 0 -> +1, time penalty 0s -> 5s, lap penalty 0 -> 2, finished -> DNF")]
    public void TheEditorsOwnNumbersAreNotTransponders(string change)
    {
        Assert.False(MainForm.CorrectionCarriesTransponder(change, ShortIds));
    }

    [Theory]
    [InlineData("Rental 4012345 moved to last", "4012345")]
    [InlineData("time penalty 0s -> 5s (4012345)", "4012345")]
    [InlineData("swapped with car 12", "12")]
    [InlineData("lap penalty 0 -> 1, AB12 excluded", "ab12")]
    public void ATransponderAsAWordOfItsOwnStillHidesTheLine(string change, string id)
    {
        Assert.True(MainForm.CorrectionCarriesTransponder(change, new[] { id }));
    }

    [Fact]
    public void ALongTransponderIsNotFoundInsideALongerNumber()
    {
        Assert.False(MainForm.CorrectionCarriesTransponder("time penalty 0s -> 5s", new[] { "5", "40123455" }));
        Assert.False(MainForm.CorrectionCarriesTransponder("note 140123459", new[] { "4012345" }));
    }
}
