using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 17 (13 Nov 2026): the Photo Finish window read every shot of the
/// night again each time a new one arrived. It now opens only the folders it
/// has not listed (PhotoFinishForm.WithNewShots), and says nothing has
/// changed when nothing has.
/// </summary>
public sealed class BugHunt17_PhotoFinishFormTests : IDisposable
{
    private const string Night = "2026-11-13";

    private static readonly DateTime Nine = new(2026, 11, 13, 21, 0, 0, DateTimeKind.Utc);

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static string Keep(string folderName, string driver, int secondsAfterNine)
    {
        var folder = Path.Combine(PhotoFinish.Root, Night, folderName);
        Directory.CreateDirectory(folder);

        var shot = new PhotoFinish.SavedShot(string.Empty, driver, "7", 1, 9, true, Nine.AddSeconds(secondsAfterNine));
        File.WriteAllText(Path.Combine(folder, "shot.json"), JsonSerializer.Serialize(shot));

        return folder;
    }

    [Fact]
    public void Only_the_new_shot_is_read_and_the_listed_ones_are_kept_as_they_are()
    {
        Keep("a", "Sam", 0);
        Keep("b", "Frank", 10);

        var have = PhotoFinish.ShotsOn(Night);
        Assert.Equal(2, have.Count);

        // Nothing new: nothing to show again.
        Assert.Null(PhotoFinishForm.WithNewShots(Night, have));

        // A listed shot's details are not opened again: spoiling them on
        // disk changes nothing about what is listed.
        File.WriteAllText(Path.Combine(have[0].Folder, "shot.json"), "not json");
        var newFolder = Keep("c", "Maya", 20);

        var now = PhotoFinishForm.WithNewShots(Night, have);

        Assert.NotNull(now);
        Assert.Equal(3, now!.Count);
        Assert.Same(have[0], now[0]);
        Assert.Same(have[1], now[1]);
        Assert.Equal("Maya", now[2].Name);
        Assert.Equal(newFolder, now[2].Folder);

        Assert.Null(PhotoFinishForm.WithNewShots(Night, now));
    }

    [Fact]
    public void A_folder_with_no_details_yet_is_listed_once_they_arrive()
    {
        Keep("a", "Sam", 0);
        var have = PhotoFinish.ShotsOn(Night);

        var pending = Path.Combine(PhotoFinish.Root, Night, "b");
        Directory.CreateDirectory(pending);
        Assert.Null(PhotoFinishForm.WithNewShots(Night, have));

        Keep("b", "Frank", 10);
        Assert.Equal(new[] { "Sam", "Frank" }, PhotoFinishForm.WithNewShots(Night, have)!.Select(s => s.Name));
    }

    [Fact]
    public void A_shot_deleted_from_the_folder_leaves_the_list()
    {
        var gone = Keep("a", "Sam", 0);
        Keep("b", "Frank", 10);
        var have = PhotoFinish.ShotsOn(Night);

        Directory.Delete(gone, recursive: true);

        Assert.Equal("Frank", Assert.Single(PhotoFinishForm.WithNewShots(Night, have)!).Name);
    }

    [Fact]
    public void A_night_with_no_folder_and_nothing_listed_is_unchanged()
    {
        Assert.Null(PhotoFinishForm.WithNewShots("2020-01-01", Array.Empty<PhotoFinish.SavedShot>()));
    }
}
