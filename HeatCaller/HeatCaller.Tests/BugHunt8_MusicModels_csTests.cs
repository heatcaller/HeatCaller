namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (20 Oct 2026): a file picked from the music folder that is
/// already listed must be recognised, so Add Track doesn't list it twice.
/// </summary>
public sealed class BugHunt8_MusicModels_csTests
{
    [Fact]
    public void IsListed_FindsTrackByPath_IgnoringCase()
    {
        var store = new MusicStore();
        var path = Path.Combine("C:", "HeatCaller", "Music", "Neon Drive (1).wav");
        store.Current.Tracks.Add(new MusicTrack { Title = "Neon Drive", FilePath = path });

        Assert.True(store.IsListed(path));
        Assert.True(store.IsListed(path.ToUpperInvariant()));
    }

    [Fact]
    public void IsListed_FalseForOtherFilesAndEmpty()
    {
        var store = new MusicStore();
        store.Current.Tracks.Add(new MusicTrack { Title = "A", FilePath = Path.Combine("C:", "Music", "a.wav") });

        Assert.False(store.IsListed(Path.Combine("C:", "Music", "b.wav")));
        Assert.False(store.IsListed(string.Empty));
    }
}
