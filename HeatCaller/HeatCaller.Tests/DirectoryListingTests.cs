using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// The club directory (19 Nov 2026, cross-club index phase 1): "List this
/// club in the HeatCaller directory" on the Club Website page. Off by
/// default, kept across a save, and sent to the relay with the club's look
/// only when ticked. The relay admin's half is on the relay (directory.ts).
/// </summary>
public sealed class DirectoryListingTests
{
    [Fact]
    public void It_is_off_by_default_and_a_new_club_sends_nothing_about_it()
    {
        var settings = new AppSettings();

        Assert.False(settings.RelayListInDirectory);
        Assert.False(JsonDocument.Parse(RelayDocuments.Site(settings)).RootElement.TryGetProperty("directory", out _));
    }

    [Fact]
    public void Ticked_it_goes_with_the_look_and_unticked_it_comes_back_out()
    {
        var settings = new AppSettings { RelayListInDirectory = true };

        Assert.True(JsonDocument.Parse(RelayDocuments.Site(settings)).RootElement.GetProperty("directory").GetBoolean());

        settings.RelayListInDirectory = false;
        Assert.False(JsonDocument.Parse(RelayDocuments.Site(settings)).RootElement.TryGetProperty("directory", out _));
    }

    [Fact]
    public void It_survives_a_save_and_load()
    {
        var back = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { RelayListInDirectory = true }))!;
        Assert.True(back.RelayListInDirectory);

        var old = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.False(old.RelayListInDirectory);
    }

    [Fact]
    public void It_is_filed_with_the_roster_switch()
    {
        Assert.Equal(SettingsTable.App[nameof(AppSettings.RelayPublishRoster)], SettingsTable.App[nameof(AppSettings.RelayListInDirectory)]);
    }
}
