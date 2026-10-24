using HeatCaller;
using Xunit;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 9 (22 Oct 2026): the podium video upload sent the raw driver
/// name - full name, or the transponder number - to the public video page.
/// </summary>
public class BugHunt9_RelayPublisher_Videos_csTests
{
    [Fact]
    public void Unnamed_car_never_goes_out_as_its_transponder()
    {
        Assert.Equal(RelayDocuments.UnnamedDriver,
            RelayPublisher.PodiumVideoName("4471023", "4471023", null, abbreviate: true));
        Assert.Equal(RelayDocuments.UnnamedDriver,
            RelayPublisher.PodiumVideoName("4471023", "4471023", null, abbreviate: false));
        Assert.Equal(RelayDocuments.UnnamedDriver,
            RelayPublisher.PodiumVideoName("  ", "4471023", null, abbreviate: true));
    }

    [Fact]
    public void Full_name_is_shortened_when_the_club_asked()
    {
        Assert.Equal("Samuel H",
            RelayPublisher.PodiumVideoName("Samuel Hargrove", "123", null, abbreviate: true));
        Assert.Equal("Samuel Hargrove",
            RelayPublisher.PodiumVideoName("Samuel Hargrove", "123", null, abbreviate: false));
    }

    [Fact]
    public void Caller_public_name_is_used_as_given()
    {
        Assert.Equal("Sam Ke",
            RelayPublisher.PodiumVideoName("Sam Kent", "1", "Sam Ke", abbreviate: true));
    }

    [Fact]
    public void Podium_names_are_shortened_over_the_whole_field()
    {
        var field = new List<(string? Name, string? Transponder, int Position)>
        {
            ("Sam Kent", "1", 1),
            ("Sam King", "2", 2),
            ("3", "3", 3),
            ("Emily Chen", "4", 4),
        };

        var names = RelayPublisher.PodiumPublicNames(field, abbreviate: true);

        Assert.Equal(3, names.Count);
        Assert.NotEqual(names["1"], names["2"]);
        Assert.DoesNotContain("Kent", names["1"]);
        Assert.Equal(RelayDocuments.UnnamedDriver, names["3"]);
        Assert.False(names.ContainsKey("4"));
    }

    [Fact]
    public void Unnamed_placeholder_is_not_shortened_to_look_like_a_name()
    {
        var field = new List<(string? Name, string? Transponder, int Position)>
        {
            ("Sam Kent", "1", 1),
            (null, "2", 2),
        };

        var names = RelayPublisher.PodiumPublicNames(field, abbreviate: true);

        Assert.Equal(RelayDocuments.UnnamedDriver, names["2"]);
        Assert.Equal(RelayDocuments.UnnamedDriver,
            RelayPublisher.PodiumVideoName("2", "2", names["2"], abbreviate: true));
    }
}
