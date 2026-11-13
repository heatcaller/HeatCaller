namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, #142: a guide or club-update paragraph was cut into a heading
/// at the first colon near its start, whatever the colon was - so "Racing
/// starts at 18:30 this Friday" went to phones as the heading "Racing starts
/// at 18" over "30 this Friday", and a link lost its "https:".
/// </summary>
public sealed class BugHunt17_RelayDocumentsTests
{
    [Theory]
    [InlineData("Racing starts at 18:30 this Friday")]
    [InlineData("Sign up at https://forms.example/abc")]
    [InlineData("Gearing is 3:1 on the stock cars")]
    public void A_time_a_link_or_a_ratio_is_words_not_a_heading(string typed)
    {
        var part = Assert.Single(RelayDocuments.UpdateParts(typed));

        Assert.Equal(string.Empty, part.Heading);
        Assert.Equal(typed, part.Words);

        Assert.Equal((string.Empty, typed), Assert.Single(RelayDocuments.GuideParts(typed)));
    }

    [Fact]
    public void A_real_heading_after_a_time_is_still_found()
    {
        Assert.Equal(
            ("Doors 18:30", "bring your own pit table"),
            Assert.Single(RelayDocuments.UpdateParts("Doors 18:30: bring your own pit table")));
    }

    [Fact]
    public void A_heading_keeps_the_time_or_link_in_its_words()
    {
        Assert.Equal(
            ("Friday", "racing starts at 18:30"),
            Assert.Single(RelayDocuments.UpdateParts("Friday: racing starts at 18:30")));

        Assert.Equal(
            ("Sign up", "https://forms.example/abc"),
            Assert.Single(RelayDocuments.UpdateParts("Sign up: https://forms.example/abc")));

        // No space after the colon is still a heading, as it always was.
        Assert.Equal(("Parking", "round the back"), Assert.Single(RelayDocuments.GuideParts("Parking:round the back")));
    }
}
