namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (20 Oct 2026), BroadcastImaging: an unreadable imaging.json
/// is never saved over, a null soundscape entry no longer throws, and a
/// piece's path written on another PC or account is found in this one's
/// Imaging folder.
/// </summary>
public sealed class BugHunt8_BroadcastImaging_csTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void An_imaging_file_that_could_not_be_read_is_not_saved_over()
    {
        var path = _data.PathOf("imaging.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string Original = "{ \"Enabled\": false, \"Level\": 42 }";
        File.WriteAllText(path, Original);

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // Read while locked: loads as the defaults and holds its writes.
            _ = BroadcastImaging.Level;
        }

        BroadcastImaging.Level = 77;

        Assert.Equal(Original, File.ReadAllText(path));
    }

    [Fact]
    public void A_null_soundscape_entry_reads_as_not_customised()
    {
        var path = _data.PathOf("imaging.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ \"Soundscapes\": { \"classic-id\": null } }");

        var theme = new Theme { Id = "classic-id", Name = "Classic" };

        Assert.False(BroadcastImaging.IsCustom(theme));
        Assert.False(BroadcastImaging.IsCustom(theme, default(ImagingElement)));
        _ = BroadcastImaging.FilesFor(theme, default(ImagingElement));
        BroadcastImaging.RestoreDefaults(theme, default(ImagingElement));
    }

    /// <summary>
    /// Where another PC or Windows account kept its data folder. Built with
    /// Path.Combine so these tests run on any OS, not only Windows.
    /// </summary>
    private static string OtherAccount(string soundscape, string file) =>
        Path.Combine(Path.GetTempPath(), "SomeoneElse-HC-Test", "AppData", "Roaming", "HeatCaller", "Imaging", soundscape, file);

    [Fact]
    public void A_club_piece_from_another_account_is_found_in_this_ones_Imaging_folder()
    {
        var here = Path.Combine(BroadcastImaging.ClubRoot, "Classic", "Classic Jingle 1.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(here)!);
        File.WriteAllBytes(here, new byte[] { 1, 2, 3 });

        var old = OtherAccount("Classic", "Classic Jingle 1.mp3");

        Assert.Equal(Path.GetFullPath(here), BroadcastImaging.Relocated(old), ignoreCase: true);
    }

    [Fact]
    public void A_path_with_nothing_to_find_is_returned_unchanged()
    {
        var old = OtherAccount("Classic", "Not There 9.mp3");

        Assert.Equal(old, BroadcastImaging.Relocated(old));
    }

    [Fact]
    public void A_restored_imaging_file_plays_this_accounts_copy_of_each_club_piece()
    {
        var here = Path.Combine(BroadcastImaging.ClubRoot, "Classic", "Classic Jingle 1.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(here)!);
        File.WriteAllBytes(here, new byte[] { 1, 2, 3 });

        var path = _data.PathOf("imaging.json");
        var old = System.Text.Json.JsonSerializer.Serialize(OtherAccount("Classic", "Classic Jingle 1.mp3"));
        File.WriteAllText(path, "{ \"Soundscapes\": { \"classic-id\": { \"Jingle\": [ " + old + " ] } } }");

        var theme = new Theme { Id = "classic-id", Name = "Classic" };

        var files = BroadcastImaging.FilesFor(theme, ImagingElement.Jingle);

        Assert.Single(files);
        Assert.Equal(Path.GetFullPath(here), files[0], ignoreCase: true);
    }

    /// <summary>
    /// 6 Nov 2026: marks are kept by file name, so a club's piece added to a
    /// built-in soundscape must never take a shipped piece's name. With
    /// shipped Jingle 2 removed, the copy used to be named Jingle 4 beside
    /// the shipped Jingle 4.
    /// </summary>
    [Fact]
    public void A_club_piece_added_to_a_built_in_soundscape_never_takes_a_shipped_name()
    {
        var theme = new Theme { Id = "broadcast-id", Name = "Broadcast", IsBuiltIn = true };
        var shipped = BroadcastImaging.DefaultFiles(theme, ImagingElement.Jingle);
        Assert.True(shipped.Count >= 4);

        BroadcastImaging.Remove(theme, ImagingElement.Jingle, shipped[1]);

        Assert.True(BroadcastImaging.Add(theme, ImagingElement.Jingle, shipped[1], out var problem), problem);

        var added = BroadcastImaging.FilesFor(theme, ImagingElement.Jingle)
            .Except(shipped, StringComparer.OrdinalIgnoreCase)
            .Single();

        Assert.DoesNotContain(Path.GetFileName(added), shipped.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
    }
}
