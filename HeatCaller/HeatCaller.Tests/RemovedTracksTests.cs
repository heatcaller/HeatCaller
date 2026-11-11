namespace HeatCaller.Tests;

/// <summary>
/// Neon Grid, Insert Coin, Race Day Live, Victory Lane and Midnight Pit Wall
/// were taken out of the app (9 Nov 2026). An install from before then still
/// lists them, with their files in the music folder; the next load drops
/// the entries - not the files, which may be the club's own - and leaves the
/// club's own tracks alone.
/// </summary>
public sealed class RemovedTracksTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_removed_shipped_track_is_dropped_from_the_library_and_its_file_is_kept()
    {
        Directory.CreateDirectory(MusicStore.MusicDirectory);
        var removed = Path.Combine(MusicStore.MusicDirectory, "Neon Grid.mp3");
        var renamed = Path.Combine(MusicStore.MusicDirectory, "Classic Song 2.mp3");
        var kept = Path.Combine(MusicStore.MusicDirectory, "Classic Song 1.mp3");
        var own = Path.Combine(MusicStore.MusicDirectory, "Victory Lane (club mix).mp3");
        foreach (var file in new[] { removed, renamed, kept, own })
        {
            File.WriteAllText(file, "not really an mp3");
        }

        var store = MusicStore.Load();
        store.Current.Tracks.Add(new MusicTrack { Title = "Neon Grid", FilePath = removed, IsBuiltIn = true });
        store.Current.Tracks.Add(new MusicTrack { Title = "Midnight Pit Wall", FilePath = renamed, IsBuiltIn = true });
        store.Current.Tracks.Add(new MusicTrack { Title = "Victory Lane (club mix)", FilePath = own });
        store.Save();

        var reloaded = MusicStore.Load();
        var titles = reloaded.Current.Tracks.Select(t => t.DisplayName).ToList();

        Assert.DoesNotContain("Neon Grid", titles);
        Assert.DoesNotContain("Midnight Pit Wall", titles);
        Assert.Contains("Chrome Apex", titles);
        Assert.Contains("Victory Lane (club mix)", titles);
        // The files are left alone - one may be the club's own (10 Nov 2026).
        Assert.True(File.Exists(removed));
        Assert.True(File.Exists(renamed));
        Assert.True(File.Exists(kept));
        Assert.True(File.Exists(own));
    }
}
