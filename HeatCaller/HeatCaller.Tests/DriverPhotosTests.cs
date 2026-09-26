namespace HeatCaller.Tests;

/// <summary>
/// From the 25 Sep audit: a name whose hash lands on int.MinValue made
/// Math.Abs throw inside MainForm's constructor, so the app would not open.
/// These four names hash to exactly that value.
/// </summary>
public sealed class DriverPhotosTests
{
    [Theory]
    [InlineData("Sam bdkubhd")]
    [InlineData("Dave OEFJAHD")]
    [InlineData("Mia AFGIQFH")]
    [InlineData("JDTDEQYR")]
    public void A_name_hashing_to_int_MinValue_still_gets_a_colour(string name)
    {
        var failure = Record.Exception(() => DriverPhotos.ColourFor(name));

        Assert.Null(failure);
    }

    [Fact]
    public void The_same_name_always_gets_the_same_colour_whatever_the_case()
    {
        Assert.Equal(DriverPhotos.ColourFor("Asher Kent"), DriverPhotos.ColourFor("ASHER KENT"));
    }
}
