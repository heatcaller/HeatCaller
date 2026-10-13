namespace HeatCaller.Tests;

/// <summary>
/// The ceiling on the race recordings, added 11 Oct 2026.
///
/// The recording of every race and the per-driver clips cut out of it had
/// been written after every race since they were built, and nothing ever
/// deleted any of it. A club running a season would have put twenty
/// gigabytes of broadcast into Documents without once being told it was
/// happening - which is exactly the fault the fifth bug hunt found in the
/// photo-finish frames, where fourteen kept backups of them filled a
/// laptop.
///
/// It is a CEILING and not an archive. A club that wants a night kept
/// copies the folder, which is what the store's own note tells them to do -
/// so the two things these tests defend are that the oldest nights really
/// do go, and that anything which is not one of our night folders is left
/// exactly where a club put it.
/// </summary>
public sealed class RaceAudioKeepTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly string _folder;

    public RaceAudioKeepTests()
    {
        // A folder of this test's own. The store's real default is under
        // Documents, which AppPaths.RedirectForTests deliberately does NOT
        // cover - so a test that pruned the default would be deleting
        // whoever ran the suite's own race recordings.
        _folder = Path.Combine(_data.Folder, "audio");
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => _data.Dispose();

    private RaceAudioStore Store(int keep)
    {
        var store = RaceAudioStore.Load();

        store.FolderSetting = _folder;
        store.KeepNights = keep;

        Assert.Equal(_folder, store.Folder);

        return store;
    }

    private void Night(string name, int files = 2)
    {
        var folder = Path.Combine(_folder, name);

        Directory.CreateDirectory(folder);

        // As StartFor leaves it. _folder is not the default folder, and
        // outside that only a marked night is ours to delete.
        RaceAudioStore.MarkAsNight(folder);

        for (var at = 0; at < files; at++)
        {
            File.WriteAllText(Path.Combine(folder, $"race-{at}.mp3"), new string('x', 1024));
        }
    }

    private string[] NightsLeft() =>
        Directory.EnumerateDirectories(_folder)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    // ------------------------------------------------------------------

    [Fact]
    public void The_oldest_nights_go_and_the_newest_stay()
    {
        Night("2026-09-06");
        Night("2026-09-13");
        Night("2026-09-20");
        Night("2026-09-27");
        Night("2026-10-04");
        Night("2026-10-11");

        Store(3).PruneOldNights();

        Assert.Equal(new[] { "2026-09-27", "2026-10-04", "2026-10-11" }, NightsLeft());
    }

    [Fact]
    public void A_club_inside_the_count_loses_nothing()
    {
        Night("2026-10-04");
        Night("2026-10-11");

        Store(6).PruneOldNights();

        Assert.Equal(new[] { "2026-10-04", "2026-10-11" }, NightsLeft());
    }

    [Fact]
    public void It_sorts_by_the_folders_name_and_not_by_its_timestamp()
    {
        // A folder's own modified time is not the night it holds: copying
        // it, a sync tool touching it, or restoring it from a backup all
        // move it - and a club would lose the wrong night. Every night
        // folder is named yyyy-MM-dd, and a name sorts the way a date does.
        Night("2026-09-06");
        Night("2026-10-11");

        // The OLD night touched most recently, which is what a sync tool
        // or a copy would do.
        Directory.SetLastWriteTimeUtc(Path.Combine(_folder, "2026-09-06"), DateTime.UtcNow);
        Directory.SetLastWriteTimeUtc(Path.Combine(_folder, "2026-10-11"), DateTime.UtcNow.AddDays(-60));

        Store(1).PruneOldNights();

        Assert.Equal(new[] { "2026-10-11" }, NightsLeft());
    }

    [Fact]
    public void Anything_that_is_not_one_of_our_night_folders_is_left_alone()
    {
        // Exactly what somebody who has just read the note about copying a
        // night would do.
        Night("2026-09-06");
        Night("2026-09-13");
        Night("2026-10-11");
        Night("Keep these");
        Night("2026-13-45");
        Night("2026-10-11 backup");

        Store(1).PruneOldNights();

        Assert.Equal(
            new[] { "2026-10-11", "2026-10-11 backup", "2026-13-45", "Keep these" },
            NightsLeft());
    }

    [Fact]
    public void A_folder_with_nothing_in_it_yet_is_not_a_fault()
    {
        Store(6).PruneOldNights();

        Assert.Empty(NightsLeft());

        // And neither is one that was never made - a club that has not
        // finished a race since installing.
        var store = RaceAudioStore.Load();
        store.FolderSetting = Path.Combine(_data.Folder, "never-made");

        store.PruneOldNights();
    }

    [Fact]
    public void It_says_what_it_removed_so_a_club_is_never_quietly_deleted_from()
    {
        Night("2026-09-06");
        Night("2026-09-13");
        Night("2026-10-11");

        var said = new List<string>();

        Store(1).PruneOldNights(said.Add);

        var all = string.Join("\n", said);

        Assert.Contains("2 nights", all);
        Assert.Contains("keeping the last 1 night", all);

        // And how to keep one, in the same breath - a club reading this
        // line at midnight is the only club that will ever read it.
        Assert.Contains("Copy a night's folder", all);
    }

    [Fact]
    public void Nothing_is_said_when_nothing_was_removed()
    {
        Night("2026-10-11");

        var said = new List<string>();

        Store(6).PruneOldNights(said.Add);

        Assert.Empty(said);
    }

    [Fact]
    public void The_count_cannot_be_turned_off_or_set_to_nothing_by_a_hand_edited_file()
    {
        var store = RaceAudioStore.Load();

        store.KeepNights = 0;
        Assert.Equal(1, store.KeepNights);

        store.KeepNights = -5;
        Assert.Equal(1, store.KeepNights);

        store.KeepNights = 10_000;
        Assert.Equal(104, store.KeepNights);
    }

    // ------------------------------------------------------------------
    //  What a club is shown before they choose a count
    // ------------------------------------------------------------------

    [Fact]
    public void What_is_on_disk_counts_the_nights_and_adds_up_the_files()
    {
        Night("2026-10-04", files: 3);
        Night("2026-10-11", files: 3);
        Night("Keep these", files: 1);

        var (nights, megabytes) = Store(6).OnDisk();

        // The night folders only - a club deciding how many nights to keep
        // is asking what a night costs.
        Assert.Equal(2, nights);

        // The size is every file under the folder, so what it reports is
        // what the folder actually takes. Seven kilobytes rounds to zero
        // megabytes, which is the honest answer.
        Assert.Equal(0, megabytes);
    }

    [Fact]
    public void A_club_that_has_recorded_nothing_is_told_so_rather_than_shown_a_zero()
    {
        var (nights, megabytes) = Store(6).OnDisk();

        Assert.Equal(0, nights);
        Assert.Equal(0, megabytes);
    }
}
