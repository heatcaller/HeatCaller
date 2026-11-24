namespace HeatCaller.Tests;

/// <summary>
/// 23 Nov 2026, bug hunt 24 (F124): a replaced or taken-down driver photo
/// waits in DriverPhotos\Retired for undo, and should go after a week. The
/// sweep used to run only when another photo was retired, so it could stay
/// for months, and Forget left an earlier photo of the same driver behind.
/// Real files in a throwaway data folder; no window is shown.
/// </summary>
public sealed class BugHunt24_DriverPhotosTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static string Retired => Path.Combine(AppPaths.Root, "DriverPhotos", "Retired");

    private static string Plant(string name, DateTime lastWriteUtc)
    {
        Directory.CreateDirectory(Retired);
        var path = Path.Combine(Retired, name);
        File.WriteAllText(path, "jpeg");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    [Fact]
    public void A_retired_photo_older_than_a_week_is_cleared_without_another_photo_being_retired()
    {
        var old = Plant("old.jpg", DateTime.UtcNow.AddDays(-(UndoHoldingFolder.KeepDays + 1)));
        var fresh = Plant("fresh.jpg", DateTime.UtcNow);

        DriverPhotos.PruneRetiredNow();

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void The_sweep_with_no_retired_folder_does_nothing()
    {
        Assert.False(Directory.Exists(Retired));

        var ex = Record.Exception(() => DriverPhotos.PruneRetiredNow());

        Assert.Null(ex);
        Assert.False(Directory.Exists(Retired));
    }

    [Fact]
    public void Forgetting_a_driver_also_clears_an_earlier_replaced_photo_past_the_undo_window()
    {
        var photos = Path.Combine(AppPaths.Root, "DriverPhotos");
        Directory.CreateDirectory(photos);
        File.WriteAllText(Path.Combine(photos, "now.jpg"), "jpeg");
        var earlier = Plant("earlier.jpg", DateTime.UtcNow.AddDays(-(UndoHoldingFolder.KeepDays + 1)));

        DriverPhotos.Forget("now.jpg");

        Assert.False(File.Exists(Path.Combine(photos, "now.jpg")));
        Assert.False(File.Exists(earlier));
    }
}
