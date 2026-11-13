using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): notes left under no day were never cleared
/// by starting up with no day open again - "no day to no day" is not a
/// change - so last week's waited to be read out and filled the caps.
/// Start-up now drops the ones over a day old, the rule adopting uses.
/// </summary>
public sealed class BugHunt17_ClubNotesTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private void FileUnderNoDay(params (string Text, int DaysOld, bool Said)[] notes)
    {
        var now = DateTime.UtcNow;

        File.WriteAllText(_appData.PathOf("club-notes.json"), JsonSerializer.Serialize(new
        {
            DayId = string.Empty,
            Notes = notes.Select((one, at) => new
            {
                Id = $"n{at}",
                one.Text,
                From = string.Empty,
                AddedAtUtc = now.AddDays(-one.DaysOld),
                Approved = true,
                SaidAtUtc = one.Said ? now.AddDays(-one.DaysOld) : (DateTime?)null,
            }),
        }));
    }

    [Fact]
    public void Starting_up_with_no_day_again_drops_last_weeks_notes_held_under_no_day()
    {
        FileUnderNoDay(("Happy birthday Jack", 7, false), ("Raffle closed at eight", 7, true));

        var notes = ClubNotes.Load();
        Assert.Equal(2, notes.All.Count);

        notes.StartUp(null, _ => null);

        Assert.Empty(notes.All);
        Assert.Null(notes.ToSay());

        // And off the disk, not only out of memory.
        Assert.Empty(ClubNotes.Load().All);
    }

    [Fact]
    public void A_note_from_earlier_tonight_survives_a_restart_before_the_day_is_opened()
    {
        FileUnderNoDay(("Happy birthday Jack", 7, false), ("Jack's first race back", 0, false));

        var notes = ClubNotes.Load();

        notes.StartUp(null, _ => null);

        Assert.Equal("Jack's first race back", Assert.Single(notes.All).Text);
        Assert.Equal("Jack's first race back", notes.ToSay()?.Text);
    }

    [Fact]
    public void Starting_up_on_a_closed_day_drops_them_too()
    {
        FileUnderNoDay(("Happy birthday Jack", 7, false));

        var lastWeek = new RaceEvent { Id = "last-week", FinishedUtc = DateTime.UtcNow.AddDays(-7) };
        var notes = ClubNotes.Load();

        notes.StartUp(lastWeek, id => id == lastWeek.Id ? lastWeek : null);

        Assert.Empty(notes.All);
    }
}
