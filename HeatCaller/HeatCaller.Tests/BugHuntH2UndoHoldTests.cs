namespace HeatCaller.Tests;

/// <summary>
/// The undo holding folder and read-only files.
///
/// A track imported off a CD or a read-only share keeps its ReadOnly flag,
/// and a move into the holding folder keeps it too. Windows refuses to
/// delete such a file, so Prune used to fail on that held action every time
/// and the folder stayed on disk for good.
/// </summary>
public sealed class BugHuntH2UndoHoldTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Prune_clears_an_old_held_action_holding_a_read_only_file()
    {
        var old = Path.Combine(
            UndoHoldingFolder.RoamingRoot,
            DateTime.UtcNow.AddDays(-(UndoHoldingFolder.KeepDays + 1)).ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N"));
        var inner = Path.Combine(old, "Album");
        Directory.CreateDirectory(inner);

        var top = Path.Combine(old, "track.mp3");
        var nested = Path.Combine(inner, "hidden.mp3");
        File.WriteAllText(top, "x");
        File.WriteAllText(nested, "x");
        File.SetAttributes(top, FileAttributes.ReadOnly);
        File.SetAttributes(nested, FileAttributes.ReadOnly | FileAttributes.Hidden);

        UndoHoldingFolder.Prune();

        Assert.False(Directory.Exists(old));
    }

    [Fact]
    public void ClearReadOnly_takes_the_flag_off_nested_and_hidden_files()
    {
        var folder = Path.Combine(_data.Folder, "held");
        var inner = Path.Combine(folder, "Album");
        Directory.CreateDirectory(inner);

        var top = Path.Combine(folder, "track.mp3");
        var nested = Path.Combine(inner, "hidden.mp3");
        File.WriteAllText(top, "x");
        File.WriteAllText(nested, "x");
        File.SetAttributes(top, FileAttributes.ReadOnly);
        File.SetAttributes(nested, FileAttributes.ReadOnly | FileAttributes.Hidden);

        UndoHoldingFolder.ClearReadOnly(folder);

        Assert.Equal(0, (int)(File.GetAttributes(top) & FileAttributes.ReadOnly));
        Assert.Equal(0, (int)(File.GetAttributes(nested) & FileAttributes.ReadOnly));
    }
}
