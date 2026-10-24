namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): a closed race day made current to look at
/// its results must not take the notes onto it, or wipe tonight's.
/// </summary>
public sealed class BugHunt10_ClubNotesTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private static RaceEvent Open(string id) => new() { Id = id };

    private static RaceEvent Closed(string id) => new() { Id = id, FinishedUtc = DateTime.UtcNow.AddDays(-7) };

    [Fact]
    public void Only_an_open_real_day_is_a_day_for_the_notes()
    {
        Assert.Equal("tonight", ClubNotes.OpenDayId(Open("tonight")));
        Assert.Null(ClubNotes.OpenDayId(Closed("last-week")));
        Assert.Null(ClubNotes.OpenDayId(new RaceEvent { Id = "legacy", IsLegacy = true }));
        Assert.Null(ClubNotes.OpenDayId(Open("")));
        Assert.Null(ClubNotes.OpenDayId(null));
    }

    [Fact]
    public void Notes_added_before_the_night_survive_looking_at_last_week_then_opening_tonight()
    {
        var notes = ClubNotes.Load();

        notes.NewMeeting(null);
        Assert.Null(notes.Add("Happy birthday Dot"));

        notes.FollowDay(Closed("last-week"));
        Assert.Equal(string.Empty, notes.DayId);

        notes.FollowDay(Open("tonight"));

        Assert.Equal("tonight", notes.DayId);
        Assert.Single(notes.All);
    }

    [Fact]
    public void Looking_at_a_closed_day_mid_night_keeps_tonights_notes()
    {
        var notes = ClubNotes.Load();

        notes.FollowDay(Open("tonight"));
        Assert.Null(notes.Add("Raffle closes at eight"));

        notes.FollowDay(Closed("last-week"));
        notes.FollowDay(Open("tonight"));

        Assert.Equal("tonight", notes.DayId);
        Assert.Single(notes.All);
    }

    [Fact]
    public void Starting_up_on_a_closed_day_clears_last_weeks_and_adopts_what_follows()
    {
        var notes = ClubNotes.Load();

        notes.NewMeeting("last-week");
        notes.Add("about last week");

        // Startup with last week's closed day still current.
        var lastWeek = Closed("last-week");
        notes.StartUp(lastWeek, id => id == lastWeek.Id ? lastWeek : null);
        Assert.Empty(notes.All);
        Assert.Equal(string.Empty, notes.DayId);

        Assert.Null(notes.Add("Jack's first race back"));
        notes.FollowDay(Open("tonight"));

        Assert.Equal("tonight", notes.DayId);
        Assert.Single(notes.All);
    }

    [Fact]
    public void Restarting_mid_night_while_looking_at_last_week_keeps_tonights_notes()
    {
        var tonight = Open("tonight");
        var lastWeek = Closed("last-week");
        var notes = ClubNotes.Load();

        notes.FollowDay(tonight);
        Assert.Null(notes.Add("Raffle closes at eight"));

        // The app restarts with last week's closed day current and
        // tonight's still open.
        notes.StartUp(lastWeek, id => id == tonight.Id ? tonight : id == lastWeek.Id ? lastWeek : null);

        Assert.Equal("tonight", notes.DayId);
        Assert.Single(notes.All);
    }

    [Fact]
    public void Starting_up_on_tonights_open_day_puts_the_notes_on_it()
    {
        var tonight = Open("tonight");
        var notes = ClubNotes.Load();

        notes.StartUp(null, _ => null);
        Assert.Null(notes.Add("Happy birthday Dot"));

        notes.StartUp(tonight, _ => null);

        Assert.Equal("tonight", notes.DayId);
        Assert.Single(notes.All);
    }
}
