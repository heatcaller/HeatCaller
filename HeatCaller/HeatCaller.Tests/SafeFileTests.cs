namespace HeatCaller.Tests;

/// <summary>
/// SafeFile is under every store. What matters is what it does when a write
/// can't happen: say so, and leave the file that was there alone.
/// </summary>
public sealed class SafeFileTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Writes_the_file_and_creates_its_folder()
    {
        var path = Path.Combine(AppPaths.Root, "new-folder", "file.json");

        Assert.True(SafeFile.WriteAllText(path, "{}"));
        Assert.Equal("{}", File.ReadAllText(path));
    }

    [Fact]
    public void A_locked_file_is_reported_and_left_as_it_was()
    {
        var path = _data.PathOf("locked.json");
        Assert.True(SafeFile.WriteAllText(path, "old"));

        // What antivirus or a backup tool does to a file for a moment.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(SafeFile.WriteAllText(path, "new"));
        }

        Assert.Equal("old", File.ReadAllText(path));

        // The failed write's temp file is cleared up, not left beside it.
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public void A_failed_write_raises_WriteFailed_naming_the_file()
    {
        var path = _data.PathOf("watched.json");
        Assert.True(SafeFile.WriteAllText(path, "old"));

        var messages = new List<string>();
        void Collect(string message) => messages.Add(message);

        SafeFile.WriteFailed += Collect;

        try
        {
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                SafeFile.WriteAllText(path, "new");
            }
        }
        finally
        {
            SafeFile.WriteFailed -= Collect;
        }

        Assert.Contains(messages, m => m.Contains("watched.json", StringComparison.OrdinalIgnoreCase));
    }
}
