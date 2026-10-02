using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// The rehearsal copy is built aside and only becomes the rehearsal's Data
/// folder once it is whole with publishing switched off - so a copy that
/// failed part-way is never reused, with club-website publishing still on,
/// by the next --rehearsal launch.
/// </summary>
public sealed class BugHuntRehearsalTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private string Real => Path.Combine(_data.Folder, "real");

    private string Copy => Path.Combine(_data.Folder, "rehearsal", "Data");

    private string Building => Copy + ".partial";

    private void WriteReal(string relativePath, string text)
    {
        var path = Path.Combine(Real, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void A_copy_that_fails_before_publishing_is_off_leaves_no_Data_folder_to_reuse()
    {
        WriteReal("profiles.json", "[1]");
        WriteReal("settings.json", "this is not json");

        Assert.ThrowsAny<Exception>(() => Rehearsal.Prepare(Real, Copy));

        Assert.False(Directory.Exists(Copy) && Directory.EnumerateFileSystemEntries(Copy).Any());

        // Once the club's settings read again, the next launch copies afresh.
        WriteReal("settings.json", """{ "RelayEnabled": true }""");

        Assert.True(Rehearsal.Prepare(Real, Copy));

        var copied = JsonNode.Parse(File.ReadAllText(Path.Combine(Copy, "settings.json")))!.AsObject();
        Assert.False(copied["RelayEnabled"]!.GetValue<bool>());
        Assert.False(Directory.Exists(Building));
    }

    [Fact]
    public void A_half_made_copy_from_an_earlier_failure_is_thrown_away()
    {
        WriteReal("profiles.json", "[1]");

        Directory.CreateDirectory(Building);
        File.WriteAllText(Path.Combine(Building, "profiles.json"), "[stale]");
        File.WriteAllText(Path.Combine(Building, "leftover.json"), "{}");

        Assert.True(Rehearsal.Prepare(Real, Copy));

        Assert.Equal("[1]", File.ReadAllText(Path.Combine(Copy, "profiles.json")));
        Assert.False(File.Exists(Path.Combine(Copy, "leftover.json")));
        Assert.False(Directory.Exists(Building));
    }

    [Fact]
    public void An_empty_Data_folder_is_filled_with_a_fresh_copy()
    {
        WriteReal("profiles.json", "[1]");
        Directory.CreateDirectory(Copy);

        Assert.True(Rehearsal.Prepare(Real, Copy));

        Assert.Equal("[1]", File.ReadAllText(Path.Combine(Copy, "profiles.json")));
    }
}
