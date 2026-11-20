using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Whether usage totals may be sent (19 Nov 2026): ticked to begin with in
/// the US and its territories only, unticked elsewhere until the club ticks
/// it, and the club's own choice always wins.
/// </summary>
public sealed class UsageSharingTests
{
    [Theory]
    [InlineData("US", true)]
    [InlineData("PR", true)]
    [InlineData("GU", true)]
    [InlineData("us", true)]
    [InlineData("GB", false)]
    [InlineData("DE", false)]
    [InlineData("CA", false)]
    [InlineData("AU", false)]
    [InlineData("", false)]
    public void A_club_that_has_not_chosen_gets_the_default_for_its_region(string region, bool expected)
    {
        Assert.Equal(expected, UsageSharing.IsOn(new AppSettings(), region));
    }

    [Fact]
    public void The_clubs_own_choice_wins_in_every_region()
    {
        Assert.False(UsageSharing.IsOn(new AppSettings { ShareUsageStats = false }, "US"));
        Assert.True(UsageSharing.IsOn(new AppSettings { ShareUsageStats = true }, "GB"));
    }

    [Fact]
    public void A_settings_file_from_before_the_box_has_no_choice_in_it()
    {
        var loaded = JsonSerializer.Deserialize<AppSettings>("{}")!;

        Assert.Null(loaded.ShareUsageStats);
        Assert.False(UsageSharing.IsOn(loaded, "FR"));
    }

    [Fact]
    public void Restore_default_settings_never_touches_the_choice()
    {
        Assert.Equal(SettingGroup.KeptOnRestore, SettingsTable.App[nameof(AppSettings.ShareUsageStats)]);
    }
}
