namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: the restored-backup guard compared the relay's address but
/// not the one "that last tested clean". A backup edited to change only
/// RelayLastGoodBaseUrl restored with nothing held, and the Club Website page
/// then offered that address under Restore Last Working - one click copied
/// it, with this laptop's own sealed token, into the live settings.
/// </summary>
public sealed class BugHunt17_RestoredSettingsTests
{
    private const string ClubRelay = "https://relay.club.invalid";
    private const string OldClubRelay = "https://old.club.invalid";
    private const string EvilRelay = "https://relay.evil.invalid";

    [Fact]
    public void A_last_good_address_this_laptop_never_had_holds_the_relay()
    {
        var parts = RestoredSettings.WhatNeedsConfirming(
            Settings(ClubRelay, ClubRelay), Settings(ClubRelay, EvilRelay));

        Assert.Equal(RestoredSettings.Part.PublishRelay, parts);
    }

    [Fact]
    public void A_last_good_address_this_laptop_was_using_or_remembering_asks_nothing()
    {
        // The backup's last-good is the address in use here.
        Assert.Equal(RestoredSettings.Part.None,
            RestoredSettings.WhatNeedsConfirming(Settings(ClubRelay, OldClubRelay), Settings(ClubRelay, "relay.club.invalid/")));

        // ...or the one this laptop already remembered as last good.
        Assert.Equal(RestoredSettings.Part.None,
            RestoredSettings.WhatNeedsConfirming(Settings(ClubRelay, OldClubRelay), Settings(ClubRelay, OldClubRelay)));

        // ...or there is none in the backup.
        Assert.Equal(RestoredSettings.Part.None,
            RestoredSettings.WhatNeedsConfirming(Settings(ClubRelay, ClubRelay), Settings(ClubRelay, string.Empty)));
    }

    [Fact]
    public void The_hold_message_can_name_the_address_for_a_page_that_does_not_show_it()
    {
        var message = RestoredSettings.PublishRelayMessageNaming(" " + EvilRelay + " ");

        Assert.Contains(EvilRelay + " -", message, StringComparison.Ordinal);
        Assert.DoesNotContain("address above", message, StringComparison.Ordinal);

        // Nothing to name: the usual words.
        Assert.Equal(RestoredSettings.PublishRelayMessage, RestoredSettings.PublishRelayMessageNaming("  "));
    }

    /// <summary>
    /// The Club Website page's side of it, pinned in the source: its window
    /// is not one these tests can press buttons on.
    /// </summary>
    [Fact]
    public void The_Club_Website_page_names_a_hidden_address_and_does_not_offer_a_held_last_good()
    {
        var page = File.ReadAllText(Path.Combine(SourceDir(), "RelaySettingsControl.cs"));

        // Ticking Publish is not a check of the address.
        Assert.Contains("SaveFields(confirmRestored: false);", page, StringComparison.Ordinal);

        // Simple mode hides the address row, so the Status card names it.
        Assert.Contains("RestoredSettings.PublishRelayMessageNaming(address)", page, StringComparison.Ordinal);
        Assert.Contains("&& (SimpleMode.ShowAll || _restoredAddressNamed);", page, StringComparison.Ordinal);

        // Restore Last Working is not offered while held.
        Assert.Contains("if (!HasLastGood() || RestoredSettings.IsHeld(RestoredSettings.Part.PublishRelay))", page, StringComparison.Ordinal);
        Assert.Contains("_lblLastGood.Text = RestoredSettings.LastGoodHeldMessage;", page, StringComparison.Ordinal);
    }

    private static string Settings(string relay, string lastGood) =>
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [nameof(AppSettings.RelayBaseUrl)] = relay,
            [nameof(AppSettings.RelayLastGoodBaseUrl)] = lastGood,
        });

    private static string SourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HeatCaller.sln")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("HeatCaller.sln"), "HeatCaller");
    }
}
