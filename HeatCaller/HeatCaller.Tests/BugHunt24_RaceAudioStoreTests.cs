using System.Security.AccessControl;
using System.Security.Principal;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), RaceAudioStore.OnDisk.
///
/// A recordings folder at the top of a drive, or Documents itself, holds a
/// folder Windows will not list ("System Volume Information", the "My Music"
/// junction). The old size walk threw on it and the Recording page said no
/// nights and 0 MB beside gigabytes of recordings.
/// </summary>
public sealed class BugHunt24_RaceAudioStoreTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly string _folder;

    public BugHunt24_RaceAudioStoreTests()
    {
        // A folder of this test's own - never the real Documents default.
        _folder = Path.Combine(_data.Folder, "audio");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => _data.Dispose();

    private RaceAudioStore Store()
    {
        var store = RaceAudioStore.Load();

        store.FolderSetting = _folder;

        Assert.Equal(_folder, store.Folder);

        return store;
    }

    [Fact]
    public void BugHunt24_OnDisk_keeps_the_nights_and_size_when_a_folder_beside_them_cannot_be_listed()
    {
        var night = Path.Combine(_folder, "2026-11-20");
        Directory.CreateDirectory(night);
        RaceAudioStore.MarkAsNight(night);

        // Two megabytes of recording, so the size is not lost to rounding.
        File.WriteAllBytes(Path.Combine(night, "race-1.mp3"), new byte[2 * 1024 * 1024]);

        // A folder the person running the test is refused a listing of, as
        // "System Volume Information" refuses a normal account.
        var locked = new DirectoryInfo(Path.Combine(_folder, "Locked"));
        locked.Create();
        File.WriteAllText(Path.Combine(locked.FullName, "inside.txt"), "x");

        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(
            me,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny);

        var security = locked.GetAccessControl();
        security.AddAccessRule(deny);
        locked.SetAccessControl(security);

        try
        {
            // The deny really bites, or the test proves nothing.
            Assert.Throws<UnauthorizedAccessException>(() => Directory.EnumerateFiles(locked.FullName).ToList());

            var (nights, megabytes) = Store().OnDisk();

            Assert.Equal(1, nights);
            Assert.Equal(2, megabytes);
        }
        finally
        {
            // The owner may always change the folder's permissions, so the
            // temp folder can be cleaned up afterwards.
            var undo = locked.GetAccessControl();
            undo.RemoveAccessRule(deny);
            locked.SetAccessControl(undo);
        }
    }
}
