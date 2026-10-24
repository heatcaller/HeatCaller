using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10, the race journal: a journal that could not be read at
/// startup was protected only from Record, so Clear (reached by practice,
/// demo and Reset without a write first) deleted it with no copy kept.
/// </summary>
public sealed class BugHunt10_RaceJournalTests : IDisposable
{
    private readonly TempAppData _data = new();

    private static readonly FieldInfo KeepUnread =
        typeof(RaceJournal).GetField("_keepUnreadJournal", BindingFlags.Static | BindingFlags.NonPublic)!;

    public void Dispose()
    {
        // The flag is static - never let one test's setting reach the next.
        KeepUnread.SetValue(null, false);
        _data.Dispose();
    }

    [Fact]
    public void Clear_keeps_a_copy_of_a_journal_that_could_not_be_read_at_startup()
    {
        var journal = _data.PathOf("race-in-progress.json");
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        File.WriteAllText(journal, "the crashed heat");

        // As Unfinished leaves it when the read failed at launch.
        KeepUnread.SetValue(null, true);

        new RaceJournal().Clear();

        var copy = Assert.Single(Directory.GetFiles(AppPaths.Root, "race-in-progress-unread-*.json"));
        Assert.Equal("the crashed heat", File.ReadAllText(copy));
        Assert.False(File.Exists(journal));
        Assert.False((bool)KeepUnread.GetValue(null)!);
    }

    [Fact]
    public void Clear_leaves_an_unread_journal_in_place_when_no_copy_can_be_kept()
    {
        var journal = _data.PathOf("race-in-progress.json");
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        File.WriteAllText(journal, "the crashed heat");
        KeepUnread.SetValue(null, true);

        var sut = new RaceJournal();
        var messages = new List<string>();
        sut.StatusChanged += messages.Add;

        // Something (a sync or backup tool) still holds it, so the copy fails.
        using (new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            sut.Clear();
        }

        Assert.Equal("the crashed heat", File.ReadAllText(journal));
        Assert.Empty(Directory.GetFiles(AppPaths.Root, "race-in-progress-unread-*.json"));
        Assert.True((bool)KeepUnread.GetValue(null)!);
        Assert.Contains(messages, m => m.Contains("left in place", StringComparison.Ordinal));

        // Once the lock lets go, the next Clear keeps the copy and then clears.
        sut.Clear();

        var copy = Assert.Single(Directory.GetFiles(AppPaths.Root, "race-in-progress-unread-*.json"));
        Assert.Equal("the crashed heat", File.ReadAllText(copy));
        Assert.False(File.Exists(journal));
    }

    [Fact]
    public void Clear_without_an_unread_journal_keeps_no_copy()
    {
        var journal = _data.PathOf("race-in-progress.json");
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        File.WriteAllText(journal, "a race already filed");

        new RaceJournal().Clear();

        Assert.False(File.Exists(journal));
        Assert.Empty(Directory.GetFiles(AppPaths.Root, "race-in-progress-unread-*.json"));
    }
}
