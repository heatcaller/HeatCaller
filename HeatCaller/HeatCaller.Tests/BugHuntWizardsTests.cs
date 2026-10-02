using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bugs found in the setup wizards and the soundscapes they offer. Here:
/// the "applied" soundscape marker surviving an upgrade that replaces the
/// built-ins it points at. On stores in a data folder of the test's own.
/// </summary>
public sealed class BugHuntWizardsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void The_applied_marker_follows_an_older_built_in_that_an_upgrade_replaces()
    {
        // An older build's Arcade: a random id, and a version below this one's.
        var oldId = Guid.NewGuid().ToString("N");
        WriteOldBuiltIn(oldId, "Arcade");

        var themes = ThemeStore.Load();

        Assert.Equal(Theme.BuiltInId("Arcade"), themes.ActiveThemeId);
        Assert.NotNull(themes.Get(themes.ActiveThemeId!));
    }

    [Fact]
    public void The_applied_marker_follows_a_renamed_older_built_in_to_its_new_name()
    {
        var oldId = Guid.NewGuid().ToString("N");
        WriteOldBuiltIn(oldId, "Pro Broadcast");

        var themes = ThemeStore.Load();

        Assert.Equal(Theme.BuiltInId("Broadcast"), themes.ActiveThemeId);
        Assert.NotNull(themes.Get(themes.ActiveThemeId!));
    }

    private void WriteOldBuiltIn(string id, string name)
    {
        Directory.CreateDirectory(AppPaths.Root);

        var old = new Theme
        {
            Id = id,
            Name = name,
            IsBuiltIn = true,
            BuiltInVersion = Theme.CurrentBuiltInVersion - 1,
        };

        File.WriteAllText(_data.PathOf("themes.json"), JsonSerializer.Serialize(new List<Theme> { old }));
        File.WriteAllText(_data.PathOf("soundscape-active.txt"), id);
    }
}
