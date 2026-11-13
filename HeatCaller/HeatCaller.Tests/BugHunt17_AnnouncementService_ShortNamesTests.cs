namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): "Shorten published names" took a driver's
/// name and their Announcer name for two different people. Saved as "Jon
/// Lee" and said as "Jon Leigh", both shortened to "Jon L", so each was
/// given surname letters until they differed: "Jon Lee" stayed whole and
/// "Jon Leigh" went to the cloud voice and the public captions as "Jon Lei".
/// One driver's two spellings no longer push each other longer.
/// </summary>
public sealed class BugHunt17_AnnouncementService_ShortNamesTests
{
    [Fact]
    public void A_drivers_name_and_its_respelling_both_shorten_to_an_initial()
    {
        var map = AnnouncementService.ShortNameMap(new[] { (Driver: "Jon Lee", Spoken: "Jon Leigh") });

        Assert.Equal("Jon L", map["Jon Lee"]);
        Assert.Equal("Jon L", map["Jon Leigh"]);
    }

    [Fact]
    public void A_line_naming_the_driver_either_way_carries_no_surname()
    {
        var map = AnnouncementService.ShortNameMap(new[]
        {
            (Driver: "Mia Nguyen", Spoken: "Mia Nwin"),
            (Driver: "Billy Smith", Spoken: string.Empty),
        });

        Assert.Equal("Mia N takes the lead from Billy S", AnnouncementService.ShortenNames("Mia Nwin takes the lead from Billy Smith", map));
        Assert.Equal("Mia N wins", AnnouncementService.ShortenNames("Mia Nguyen wins", map));
    }

    [Fact]
    public void Two_different_drivers_who_would_share_a_short_form_are_still_told_apart()
    {
        var map = AnnouncementService.ShortNameMap(new[]
        {
            (Driver: "Sam Jones", Spoken: string.Empty),
            (Driver: "Sam Jordan", Spoken: string.Empty),
        });

        Assert.Equal("Sam Jon", map["Sam Jones"]);
        Assert.Equal("Sam Jor", map["Sam Jordan"]);
    }

    [Fact]
    public void A_one_word_announcer_name_is_left_as_it_is()
    {
        var map = AnnouncementService.ShortNameMap(new[] { (Driver: "Billy Smith", Spoken: "Rocket") });

        Assert.Equal("Billy S", map["Billy Smith"]);
        Assert.False(map.ContainsKey("Rocket"));
    }
}
