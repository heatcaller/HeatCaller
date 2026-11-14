using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// The beta tier (22 Oct 2026): the pages not yet tested at a real event,
/// kept out of a beta club's sidebar when it asks (AppSettings.HideUntestedPages,
/// NavTiers.UntestedPages).
///
/// Only the overloads that take the flag as an argument are called here.
/// NavTiers.HidingUntested is a static the sidebar sets before each rebuild,
/// and setting it from a test would leak into every other test reading
/// SectionOf at the same time.
/// </summary>
public sealed class RoadmapEBetaTierTests
{
    [Theory]
    [InlineData("Championship")]
    [InlineData("Rentals")]
    [InlineData("Handicaps")]
    [InlineData("Venues")]
    [InlineData("Garage")]
    public void The_pages_no_rehearsal_has_run_are_untested(string page)
    {
        Assert.True(NavTiers.IsUntested(page));
        Assert.Equal(NavTier.Untested, NavTiers.TierOf(page, raceNight: false, hidingUntested: true));
        Assert.Equal(NavTiers.UntestedHeading, NavTiers.SectionOf(page, raceNight: false, hidingUntested: true));
    }

    [Theory]
    [InlineData("Race Control")]
    [InlineData("Race Day")]
    [InlineData("Connection")]
    [InlineData("Driver Hub")]
    [InlineData("Voice")]
    [InlineData("Sounds")]
    [InlineData("Local Screens")]
    [InlineData("Club Website")]
    public void The_pages_a_night_is_run_from_are_never_on_the_list(string page)
    {
        Assert.False(NavTiers.IsUntested(page));
        Assert.NotEqual(NavTier.Untested, NavTiers.TierOf(page, raceNight: true, hidingUntested: true));
    }

    [Fact]
    public void With_the_beta_tier_off_nothing_moves()
    {
        // The two-argument answers a club without the tick has always had.
        Assert.Equal(NavTier.Club, NavTiers.TierOf("Handicaps", raceNight: false, hidingUntested: false));
        Assert.Equal(NavTier.Essentials, NavTiers.TierOf("Garage", raceNight: false, hidingUntested: false));
        Assert.Equal(NavTier.Tonight, NavTiers.TierOf("Championship", raceNight: true, hidingUntested: false));
        Assert.Equal("CLUB TOOLS", NavTiers.SectionOf("Venues", raceNight: false, hidingUntested: false));
    }

    [Fact]
    public void An_untested_page_turned_up_by_a_search_is_under_its_own_heading_even_on_a_race_night()
    {
        // Championship is a Tonight page - but while it is hidden as untested
        // it is only in the sidebar because a search found it, and it says so.
        Assert.Equal(NavTier.Untested, NavTiers.TierOf("Championship", raceNight: true, hidingUntested: true));
        Assert.Equal(NavTiers.UntestedHeading, NavTiers.SectionOf("Championship", raceNight: true, hidingUntested: true));
    }

    [Fact]
    public void The_untested_group_comes_last_so_a_search_shows_it_at_the_foot()
    {
        Assert.Equal(NavTier.Untested, Enum.GetValues<NavTier>().Max());
    }

    [Fact]
    public void A_settings_file_from_before_the_beta_tier_loads_with_every_page_shown()
    {
        // An existing club must not lose its Championship page on an update.
        var loaded = JsonSerializer.Deserialize<AppSettings>("{\"RaceNightFocus\": true}")!;

        Assert.False(loaded.HideUntestedPages);
        Assert.False(new AppSettings().HideUntestedPages);
    }

    [Fact]
    public void The_setting_survives_a_save_and_a_load()
    {
        var json = JsonSerializer.Serialize(new AppSettings { HideUntestedPages = true });

        Assert.True(JsonSerializer.Deserialize<AppSettings>(json)!.HideUntestedPages);
    }
}
