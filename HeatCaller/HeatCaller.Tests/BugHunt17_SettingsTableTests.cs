using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): the club's night templates, its own checklist
/// lines and the next night it has announced were filed as tuning,
/// so Restore Default Settings emptied them and Put Back Earlier
/// Settings swapped them for an older copy's. They are the club's own, kept.
/// </summary>
public sealed class BugHunt17_SettingsTableTests
{
    private static readonly SettingsRestore.SettingsFile App = SettingsRestore.Files.Single(f => f.Label == "App");

    private static AppSettings Clubs() => new()
    {
        NightTemplates = { new NightTemplate { Name = "Tuesday club night" } },
        CloseNightOwnItems = { "Lock the clubhouse" },
        ReadyOwnItems = { "Water the track" },
        NextNight = new AnnouncedNight { Day = new DateTime(2026, 11, 18), Title = "Club Night" },
    };

    [Fact]
    public void They_are_kept_on_restore()
    {
        foreach (var name in new[]
                 {
                     nameof(AppSettings.NightTemplates), nameof(AppSettings.CloseNightOwnItems),
                     nameof(AppSettings.ReadyOwnItems), nameof(AppSettings.NextNight),
                 })
        {
            Assert.Equal(SettingGroup.KeptOnRestore, SettingsTable.App[name]);
        }
    }

    [Fact]
    public void A_restore_leaves_them_as_they_are()
    {
        var (json, changed) = SettingsRestore.ResetJson(App, JsonSerializer.Serialize(Clubs()), alsoShown: true);
        var back = JsonSerializer.Deserialize<AppSettings>(json)!;

        Assert.Equal("Tuesday club night", Assert.Single(back.NightTemplates).Name);
        Assert.Equal(new[] { "Lock the clubhouse" }, back.CloseNightOwnItems);
        Assert.Equal(new[] { "Water the track" }, back.ReadyOwnItems);
        Assert.Equal("Club Night", back.NextNight?.Title);
        Assert.DoesNotContain(nameof(AppSettings.NightTemplates), changed);
        Assert.DoesNotContain(nameof(AppSettings.NextNight), changed);
    }

    [Fact]
    public void Putting_an_older_copy_back_does_not_bring_its_own()
    {
        var older = JsonSerializer.Serialize(new AppSettings { CloseNightOwnItems = { "An old line" } });

        var back = JsonSerializer.Deserialize<AppSettings>(SettingsRestore.MergeJson(App, JsonSerializer.Serialize(Clubs()), older))!;

        Assert.Equal("Tuesday club night", Assert.Single(back.NightTemplates).Name);
        Assert.Equal(new[] { "Lock the clubhouse" }, back.CloseNightOwnItems);
        Assert.Equal(new[] { "Water the track" }, back.ReadyOwnItems);
        Assert.Equal("Club Night", back.NextNight?.Title);
    }
}
