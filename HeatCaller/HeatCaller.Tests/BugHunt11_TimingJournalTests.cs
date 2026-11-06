using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11, the race journal: Discard kept a declined race in one fixed
/// "-discarded" slot, deleting the race declined before it, and swallowed a
/// failed move so the caller logged "set aside" while the journal stayed
/// under the "unfinished" name for the next race to write over.
/// </summary>
public sealed class BugHunt11_TimingJournalTests : IDisposable
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

    private string WriteJournal(string text)
    {
        var journal = _data.PathOf("race-in-progress.json");
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        File.WriteAllText(journal, text);
        return journal;
    }

    private static string[] SetAside() =>
        Directory.Exists(AppPaths.Root)
            ? Directory.GetFiles(AppPaths.Root, "race-in-progress-discarded*.json")
            : Array.Empty<string>();

    [Fact]
    public void Discard_never_deletes_a_race_set_aside_earlier()
    {
        var journal = WriteJournal("race A");
        Assert.True(RaceJournal.Discard());
        Assert.False(File.Exists(journal));

        WriteJournal("race B");
        Assert.True(RaceJournal.Discard());
        Assert.False(File.Exists(journal));

        var kept = SetAside().Select(File.ReadAllText).OrderBy(t => t, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "race A", "race B" }, kept);
    }

    [Fact]
    public void Discard_keeps_a_copy_when_the_journal_cannot_be_moved()
    {
        var journal = WriteJournal("the crashed heat");

        // A sync or antivirus tool reading it: no move, but a copy can be made.
        using (new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.True(RaceJournal.Discard());
        }

        var copy = Assert.Single(SetAside());
        Assert.Equal("the crashed heat", File.ReadAllText(copy));
    }

    [Fact]
    public void Discard_says_so_when_no_copy_can_be_kept_and_the_next_Clear_keeps_one()
    {
        var journal = WriteJournal("the crashed heat");

        using (new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(RaceJournal.Discard());
        }

        Assert.Empty(SetAside());
        Assert.Equal("the crashed heat", File.ReadAllText(journal));

        // The next race's Clear (or first Record) keeps a copy before the
        // journal goes, rather than losing the race the operator declined.
        new RaceJournal().Clear();

        var copy = Assert.Single(Directory.GetFiles(AppPaths.Root, "race-in-progress-unread-*.json"));
        Assert.Equal("the crashed heat", File.ReadAllText(copy));
    }

    [Fact]
    public void Discard_with_no_journal_is_fine()
    {
        Assert.True(RaceJournal.Discard());
        Assert.Empty(SetAside());
    }
}
