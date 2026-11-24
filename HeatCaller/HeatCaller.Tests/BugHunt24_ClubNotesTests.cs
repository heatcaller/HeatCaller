namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), club notes: a hand-edited club-notes.json
/// whose note has no Id, an empty Id, or an Id another note already has
/// must not hold up every later note for the night.
/// </summary>
public sealed class BugHunt24_ClubNotesTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private void WriteNotesFile(string json)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_appData.PathOf("club-notes.json"), json);
    }

    private static string Note(string idPart, string text, string added) =>
        "{" + idPart + "\"Text\":\"" + text + "\",\"From\":\"\",\"AddedAtUtc\":\"" + added + "\",\"Approved\":true}";

    [Theory]
    [InlineData("")]
    [InlineData("\"Id\":\"\",")]
    [InlineData("\"Id\":\"  \",")]
    public void BugHunt24_ClubNotes_note_with_missing_or_blank_id_gets_an_id_and_does_not_block_later_notes(string idPart)
    {
        var added = DateTime.UtcNow.ToString("o");
        WriteNotesFile(
            "{\"DayId\":\"\",\"Notes\":["
            + Note(idPart, "Raffle closes at eight", added) + ","
            + Note("\"Id\":\"n2\",", "Happy birthday Dot", added) + "]}");

        var notes = ClubNotes.Load();

        var first = notes.ToSay();
        Assert.NotNull(first);
        Assert.False(string.IsNullOrWhiteSpace(first!.Id));
        Assert.NotEqual("n2", first.Id);

        // The announcer can now mark it said, so the next note comes up.
        notes.Said(first.Id);
        Assert.Equal("n2", notes.ToSay()?.Id);

        // And the Club Notes window can take it off.
        notes.Remove(first.Id);
        Assert.DoesNotContain(notes.All, one => one.Id == first.Id);
    }

    [Fact]
    public void BugHunt24_ClubNotes_two_notes_sharing_an_id_load_with_different_ids()
    {
        var added = DateTime.UtcNow.ToString("o");
        WriteNotesFile(
            "{\"DayId\":\"\",\"Notes\":["
            + Note("\"Id\":\"same\",", "Raffle closes at eight", added) + ","
            + Note("\"Id\":\"same\",", "Happy birthday Dot", added) + "]}");

        var notes = ClubNotes.Load();

        Assert.Equal(2, notes.All.Count);
        Assert.Equal("same", notes.All[0].Id);
        Assert.NotEqual(notes.All[0].Id, notes.All[1].Id);

        // Saying the first no longer leaves the second stuck behind it.
        notes.Said(notes.ToSay()!.Id);
        var next = notes.ToSay();
        Assert.NotNull(next);
        Assert.Equal("Happy birthday Dot", next!.Text);
        notes.Said(next.Id);
        Assert.Null(notes.ToSay());
    }
}
