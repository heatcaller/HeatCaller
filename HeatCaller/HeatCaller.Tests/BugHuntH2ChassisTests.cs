namespace HeatCaller.Tests;

/// <summary>
/// Chassis sheet diagram images (ChassisTemplateStore.SetImage). The diagram
/// editor works on a copy that keeps the sheet's id and picks its picture
/// before Save, so a new picture with the same file name as the sheet's
/// current one used to be copied straight over the sheet's diagram - Cancel
/// then left the sheet showing the picture it had just turned down.
/// </summary>
public sealed class BugHuntH2ChassisTests
{
    [Fact]
    public void SetImage_SameFileNameFromAnotherFolder_LeavesTheOldPictureAlone()
    {
        using var data = new TempAppData();
        var store = ChassisTemplateStore.Load();
        var sheet = store.All[0];

        var first = Path.Combine(data.Folder, "first", "mr03.png");
        var second = Path.Combine(data.Folder, "second", "mr03.png");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.WriteAllBytes(first, new byte[] { 1, 2, 3 });
        File.WriteAllBytes(second, new byte[] { 4, 5, 6 });

        Assert.True(store.SetImage(sheet, first));
        var oldPath = store.ResolveImagePath(sheet);
        Assert.NotNull(oldPath);

        // What the editor does: a copy with the same id, given the new picture.
        var working = sheet.Clone();
        working.Id = sheet.Id;
        working.ImageFileName = sheet.ImageFileName;

        Assert.True(store.SetImage(working, second));

        Assert.NotEqual(sheet.ImageFileName, working.ImageFileName);
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(oldPath!));
        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(store.ResolveImagePath(working)!));
    }

    [Fact]
    public void SetImage_SameFileTwice_StillSucceeds()
    {
        using var data = new TempAppData();
        var store = ChassisTemplateStore.Load();
        var sheet = store.All[0];

        var source = Path.Combine(data.Folder, "mr03.png");
        Directory.CreateDirectory(data.Folder);
        File.WriteAllBytes(source, new byte[] { 7, 8, 9 });

        Assert.True(store.SetImage(sheet, source));
        Assert.True(store.SetImage(sheet, source));
        Assert.Equal(new byte[] { 7, 8, 9 }, File.ReadAllBytes(store.ResolveImagePath(sheet)!));
    }
}
