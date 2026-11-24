namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24: SoundEffectsStore.Save was void and dropped the answer from
/// SafeFile.WriteAllText, so a failed write looked like a good one and the
/// Sound Effects page said "Saved" although nothing reached the file.
/// </summary>
public sealed class BugHunt24_SoundEffectsStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Save_returns_true_when_the_sounds_reach_the_file()
    {
        var store = SoundEffectsStore.Load();

        var written = store.Save(SoundEffectsStore.Copy(store.Current));

        Assert.True(written);
        Assert.True(File.Exists(_data.PathOf("sound-effects.json")));
    }

    [Fact]
    public void Save_returns_false_when_the_file_cannot_be_written_but_keeps_the_sounds_for_this_session()
    {
        var store = SoundEffectsStore.Load();

        // A folder where the file should be: the swap onto it fails, the way
        // a locked or read-only file or a full disk would.
        Directory.CreateDirectory(_data.PathOf("sound-effects.json"));

        var profile = SoundEffectsStore.Copy(store.Current);
        var written = store.Save(profile);

        Assert.False(written);
        Assert.Same(profile, store.Current);
    }
}
