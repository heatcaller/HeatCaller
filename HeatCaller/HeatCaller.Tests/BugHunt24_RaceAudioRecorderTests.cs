using System.Security.AccessControl;
using System.Security.Principal;

namespace HeatCaller.Tests;

/// <summary>
/// The 24th bug hunt's race-audio recorder fix (23 Nov 2026): the after-race
/// tidy-up steps past a folder Windows will not open (System Volume
/// Information at the top of a drive, the hidden junctions in Documents)
/// instead of giving up on the whole sweep.
/// </summary>
public sealed class BugHunt24_RaceAudioRecorderTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly string _folder;
    private readonly List<(DirectoryInfo Folder, FileSystemAccessRule Rule)> _denied = new();

    public BugHunt24_RaceAudioRecorderTests()
    {
        RaceAudioStore.OffForTests = false;

        // Never the store's default, which is under the real Documents.
        _folder = Path.Combine(_data.Folder, "audio");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        foreach (var (folder, rule) in _denied)
        {
            try
            {
                var acl = folder.GetAccessControl();
                acl.RemoveAccessRule(rule);
                folder.SetAccessControl(acl);
            }
            catch
            {
                // Best effort; the temp folder is the test session's own.
            }
        }

        RaceAudioStore.OffForTests = true;
        _data.Dispose();
    }

    private RaceAudioStore Store()
    {
        var store = RaceAudioStore.Load();

        store.FolderSetting = _folder;

        Assert.Equal(_folder, store.Folder);

        return store;
    }

    private string Night(string name)
    {
        var folder = Path.Combine(_folder, name);
        Directory.CreateDirectory(folder);
        RaceAudioStore.MarkAsNight(folder);
        return folder;
    }

    /// <summary>A subfolder this account is not allowed to look inside.</summary>
    private void Unreadable(string name)
    {
        var folder = new DirectoryInfo(Path.Combine(_folder, name));
        folder.Create();

        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny);

        var acl = folder.GetAccessControl();
        acl.AddAccessRule(rule);
        folder.SetAccessControl(acl);
        _denied.Add((folder, rule));
    }

    private static string Old(string path)
    {
        File.WriteAllText(path, "not a wav");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-7));
        return path;
    }

    [Fact]
    public void BugHunt24_The_tidy_up_still_runs_when_the_recordings_folder_holds_one_Windows_will_not_open()
    {
        var night = Night("2026-11-20");
        var leftover = Old(Path.Combine(night, "A-Main" + RaceAudioRecorder.PublishedExtension));
        var bed = Old(Path.Combine(night, "A-Main" + RecapBed.Extension));

        Unreadable("System Volume Information");

        var lines = new List<string>();
        RaceAudioRecorder.SweepOrphans(Store(), null, lines.Add);

        Assert.DoesNotContain(lines, line => line.Contains("Could not tidy up earlier recordings", StringComparison.Ordinal));
        Assert.False(File.Exists(leftover));
        Assert.False(File.Exists(bed));
    }

    [Fact]
    public void BugHunt24_The_files_walk_steps_past_an_unreadable_folder_and_still_finds_hidden_files()
    {
        var night = Night("2026-11-21");
        var hidden = Path.Combine(night, "Heat 1" + RaceAudioRecorder.PublishedExtension);
        File.WriteAllText(hidden, "not a wav");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        Unreadable("$RECYCLE.BIN");

        var found = RaceAudioRecorder.FilesUnder(_folder, "*" + RaceAudioRecorder.PublishedExtension).ToList();

        Assert.Contains(found, path => string.Equals(path, hidden, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BugHunt24_The_sweep_no_longer_walks_with_a_search_that_stops_at_the_first_closed_folder()
    {
        var recorder = Source("RaceAudioRecorder.cs");
        var from = recorder.IndexOf("internal static void SweepOrphans(", StringComparison.Ordinal);
        var to = recorder.IndexOf("internal static bool IsVideoLeftover(", StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "SweepOrphans is no longer where it was.");

        Assert.DoesNotContain("SearchOption.AllDirectories", recorder[from..to], StringComparison.Ordinal);
    }

    private static string Source(string file)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file));
    }
}
