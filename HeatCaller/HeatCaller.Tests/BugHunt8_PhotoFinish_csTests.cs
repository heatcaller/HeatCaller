namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 8, #16 (20 Oct 2026): photo finish frames were kept on disk
/// forever. PhotoFinish.PruneOldNights now keeps the newest nights only.
/// </summary>
public sealed class BugHunt8_PhotoFinish_csTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hc-pf-prune-" + Guid.NewGuid().ToString("N"));

    /// <summary>Midday on 20 Oct 2026 - well clear of the 6am night turn.</summary>
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Local);

    public BugHunt8_PhotoFinish_csTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // Temp folder: left for the OS.
        }
    }

    private string Night(string name)
    {
        var shot = Path.Combine(_root, name, "21-14-05.123 P1 Sam #1");
        Directory.CreateDirectory(shot);
        File.WriteAllBytes(Path.Combine(shot, "+0000.jpg"), new byte[] { 0xFF, 0xD8, 0xFF, 1 });
        return Path.Combine(_root, name);
    }

    private IEnumerable<string> Left() =>
        Directory.GetDirectories(_root).Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.Ordinal);

    [Fact]
    public void Only_the_newest_nights_are_kept()
    {
        foreach (var day in new[] { "2026-09-01", "2026-09-08", "2026-09-15", "2026-10-13", "2026-10-20" })
        {
            Night(day);
        }

        var said = new List<string>();
        var gone = PhotoFinish.PruneOldNights(_root, Now, 2, said.Add);

        Assert.Equal(3, gone);
        Assert.Equal(new[] { "2026-10-13", "2026-10-20" }, Left());
        Assert.Single(said);
    }

    [Fact]
    public void A_folder_that_is_not_a_night_is_never_touched()
    {
        Night("2026-01-01");
        Night("2026-01-02");
        Directory.CreateDirectory(Path.Combine(_root, "my pictures"));
        Directory.CreateDirectory(Path.Combine(_root, "2026-13-45"));

        PhotoFinish.PruneOldNights(_root, Now, 1);

        Assert.Equal(new[] { "2026-01-02", "2026-13-45", "my pictures" }, Left());
    }

    [Fact]
    public void Tonight_is_kept_even_when_newer_folders_fill_the_count()
    {
        // A clock that was once set wrong left folders dated in the future.
        Night("2027-05-01");
        Night("2027-05-02");
        Night("2026-10-20");
        Night("2026-10-13");

        PhotoFinish.PruneOldNights(_root, Now, 2);

        Assert.Equal(new[] { "2026-10-20", "2027-05-01", "2027-05-02" }, Left());
    }

    [Fact]
    public void Nothing_to_prune_and_no_folder_are_both_fine()
    {
        Night("2026-10-20");
        Assert.Equal(0, PhotoFinish.PruneOldNights(_root, Now, PhotoFinish.KeepNights));
        Assert.Equal(0, PhotoFinish.PruneOldNights(Path.Combine(_root, "missing"), Now, 1));
    }
}
