using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): Restore Default Settings (and Put Back
/// Earlier Settings) switched off two children's-privacy choices whose
/// shipped default is the less private one, and emptied words the club
/// wrote itself (first-night guide, club updates, New here? card, short
/// address and the rest). They are the club's own now, kept.
/// </summary>
public sealed class BugHunt24_SettingsTableTests
{
    private static readonly SettingsRestore.SettingsFile App = SettingsRestore.Files.Single(f => f.Label == "App");

    private static readonly string[] PrivacyChoices =
    {
        nameof(AppSettings.RelayUnpublishAfterYears),
        nameof(AppSettings.OfflineVoicesForJuniors),
    };

    private static readonly string[] ClubWords =
    {
        nameof(AppSettings.RelayFirstNightGuide),
        nameof(AppSettings.RelayClubUpdates),
        nameof(AppSettings.RelayNewcomerWhere),
        nameof(AppSettings.RelayNewcomerWhen),
        nameof(AppSettings.RelayNewcomerJoin),
        nameof(AppSettings.RelayTagline),
        nameof(AppSettings.RelaySocialLinks),
        nameof(AppSettings.RelayClubAlias),
        nameof(AppSettings.RelayShortName),
        nameof(AppSettings.InboxQuickReplies),
        nameof(AppSettings.StreamBasics),
        nameof(AppSettings.BetweenRacesCustomLines),
    };

    private static AppSettings Clubs()
    {
        var s = new AppSettings
        {
            RelayUnpublishAfterYears = 2,
            OfflineVoicesForJuniors = true,
            RelayFirstNightGuide = "Bring a charged pack",
            RelayClubUpdates = "New track layout this month",
            RelayNewcomerWhere = "Behind the hobby shop",
            RelayNewcomerWhen = "Tuesdays at 6",
            RelayNewcomerJoin = "Just turn up",
            RelayTagline = "Upstate's friendliest track",
            RelayClubAlias = "upstateminiz",
            RelayShortName = "Upstate",
            InboxQuickReplies = { "See you Tuesday" },
            StreamBasics = "New to RC racing? Come along",
            BetweenRacesCustomLines = "Grab a hot dog at the snack bar",

            // A real tuning setting, so the test also sees a restore still works.
            RelayAbbreviateNames = false,
        };
        s.RelaySocialLinks["facebook"] = "https://facebook.com/upstateminiz";
        return s;
    }

    [Fact]
    public void BugHunt24_The_childrens_privacy_choices_are_kept_on_restore()
    {
        foreach (var name in PrivacyChoices)
        {
            Assert.Equal(SettingGroup.KeptOnRestore, SettingsTable.App[name]);
        }
    }

    [Fact]
    public void BugHunt24_Words_the_club_wrote_are_kept_on_restore()
    {
        foreach (var name in ClubWords)
        {
            Assert.Equal(SettingGroup.KeptOnRestore, SettingsTable.App[name]);
        }
    }

    [Fact]
    public void BugHunt24_A_restore_leaves_them_as_they_are_and_still_resets_tuning()
    {
        var (json, changed) = SettingsRestore.ResetJson(App, JsonSerializer.Serialize(Clubs()), alsoShown: true);
        var back = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal(2, back.RelayUnpublishAfterYears);
        Assert.True(back.OfflineVoicesForJuniors);
        Assert.Equal("Bring a charged pack", back.RelayFirstNightGuide);
        Assert.Equal("New track layout this month", back.RelayClubUpdates);
        Assert.Equal("Behind the hobby shop", back.RelayNewcomerWhere);
        Assert.Equal("Tuesdays at 6", back.RelayNewcomerWhen);
        Assert.Equal("Just turn up", back.RelayNewcomerJoin);
        Assert.Equal("Upstate's friendliest track", back.RelayTagline);
        Assert.Equal("upstateminiz", back.RelayClubAlias);
        Assert.Equal("Upstate", back.RelayShortName);
        Assert.Equal(new[] { "See you Tuesday" }, back.InboxQuickReplies);
        Assert.Equal("New to RC racing? Come along", back.StreamBasics);
        Assert.Equal("Grab a hot dog at the snack bar", back.BetweenRacesCustomLines);
        Assert.Equal("https://facebook.com/upstateminiz", back.RelaySocialLinks["facebook"]);

        foreach (var name in PrivacyChoices.Concat(ClubWords))
        {
            Assert.DoesNotContain(name, changed);
        }

        Assert.True(back.RelayAbbreviateNames);
        Assert.Contains(nameof(AppSettings.RelayAbbreviateNames), changed);
    }

    [Fact]
    public void BugHunt24_Putting_an_older_copy_back_does_not_bring_its_own()
    {
        var older = JsonSerializer.Serialize(new AppSettings
        {
            RelayUnpublishAfterYears = 0,
            OfflineVoicesForJuniors = false,
            RelayFirstNightGuide = "An old guide",
            RelayClubAlias = "oldname",
        });

        var back = JsonSerializer.Deserialize<AppSettings>(SettingsRestore.MergeJson(App, JsonSerializer.Serialize(Clubs()), older))!;

        Assert.Equal(2, back.RelayUnpublishAfterYears);
        Assert.True(back.OfflineVoicesForJuniors);
        Assert.Equal("Bring a charged pack", back.RelayFirstNightGuide);
        Assert.Equal("upstateminiz", back.RelayClubAlias);
        Assert.Equal("See you Tuesday", Assert.Single(back.InboxQuickReplies));
    }
}
