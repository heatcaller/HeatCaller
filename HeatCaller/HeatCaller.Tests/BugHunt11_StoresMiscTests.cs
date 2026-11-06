namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026), stores and odds and ends: a hand-edited or
/// damaged file must not stop the app starting.
/// </summary>
public sealed class BugHunt11_StoresMiscTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private void WriteNotesFile(string json)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_appData.PathOf("club-notes.json"), json);
    }

    [Fact]
    public void A_null_DayId_in_club_notes_does_not_crash_startup_with_no_day_open()
    {
        WriteNotesFile("{\"DayId\":null,\"Notes\":[]}");

        var notes = ClubNotes.Load();

        notes.StartUp(null, _ => null);

        Assert.Equal(string.Empty, notes.DayId);
    }

    [Fact]
    public void A_null_note_in_club_notes_is_dropped_on_load()
    {
        var added = DateTime.UtcNow.ToString("o");
        WriteNotesFile(
            "{\"DayId\":\"\",\"Notes\":[null,{\"Id\":null,\"Text\":null,\"From\":null,\"AddedAtUtc\":\"" + added + "\",\"Approved\":true},"
            + "{\"Id\":\"n1\",\"Text\":\"Raffle closes at eight\",\"From\":\"\",\"AddedAtUtc\":\"" + added + "\",\"Approved\":true}]}");

        var notes = ClubNotes.Load();

        // Startup with tonight open takes the notes held under no day.
        notes.StartUp(new RaceEvent { Id = "tonight" }, _ => null);

        Assert.Equal("tonight", notes.DayId);
        Assert.Equal(2, notes.All.Count);
        Assert.All(notes.All, one =>
        {
            Assert.NotNull(one);
            Assert.False(string.IsNullOrEmpty(one.Id));
            Assert.NotNull(one.Text);
            Assert.NotNull(one.From);
        });
        Assert.NotNull(notes.ToSay());
        Assert.Null(notes.Add("Happy birthday Dot"));
    }
}
