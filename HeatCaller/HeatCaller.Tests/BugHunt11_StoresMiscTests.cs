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

        // 5 Nov 2026, Bug hunt 11 review: the note with "Text": null is
        // dropped too - kept as "", approved and unsaid, it was what ToSay
        // handed out first forever, and the announcer never says (or marks
        // said) an empty note, so the real one behind it never came up.
        var only = Assert.Single(notes.All);
        Assert.Equal("n1", only.Id);
        Assert.NotNull(only.From);
        Assert.Equal("n1", notes.ToSay()?.Id);
        Assert.Null(notes.Add("Happy birthday Dot"));
    }
}
