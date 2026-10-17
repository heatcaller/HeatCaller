namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 8 (17 Oct 2026), group FK - the final wave:
///
/// A26-3 the race history counts its changes, so the live qualifying
/// projection sees an edited or deleted result at once. A14-5 a driver's
/// own clip is looked for under every name their transponder raced under
/// tonight, and the transponder. A32-1 closing while minimised leaves
/// "start maximised" as it was.
/// </summary>
public sealed class BugHunt8FKTests
{
    private static SessionRecord Race(string id, params (string Transponder, string Name)[] rows) => new()
    {
        Id = id,
        SessionType = RaceSessionType.Race,
        Results = rows
            .Select((r, i) => new SessionResultEntry { Position = i + 1, Transponder = r.Transponder, DriverName = r.Name })
            .ToList(),
    };

    // --- A26-3: SessionHistoryStore.Revision ------------------------------------

    [Fact]
    public void Revision_goes_up_on_every_change_to_the_history()
    {
        var first = Race("one", ("1001", "Dave Smith"));
        var second = Race("two", ("1002", "Jane Brown"));

        // A view never reaches disk, but counts its changes all the same.
        var history = SessionHistoryStore.FromSessions(new[] { first, second });
        var start = history.Revision;

        first.Results[0].DriverName = "Dave Smyth";
        Assert.True(history.SaveEdited(first));
        var afterEdit = history.Revision;
        Assert.True(afterEdit > start);

        Assert.True(history.SaveChanges());
        var afterChanges = history.Revision;
        Assert.True(afterChanges > afterEdit);

        Assert.True(history.Delete("two"));
        Assert.True(history.Revision > afterChanges);
    }

    [Fact]
    public void Revision_does_not_move_for_an_edit_that_was_not_made()
    {
        var history = SessionHistoryStore.FromSessions(new[] { Race("one", ("1001", "Dave")) });
        var start = history.Revision;

        // Not in the store - SaveEdited ignores it without saving.
        Assert.False(history.SaveEdited(Race("elsewhere", ("1001", "Dave"))));

        Assert.Equal(start, history.Revision);
    }

    // --- A14-5: the names a driver's clip may be filed under ---------------------

    [Fact]
    public void Clip_names_take_every_name_the_transponder_raced_under_and_the_transponder()
    {
        var tonight = new List<SessionRecord>
        {
            Race("heat1", ("1001", "Dave Smith"), ("1002", "Jane Brown")),
            Race("heat2", ("1001", "Dave Smyth"), ("1003", "Someone Else")),
            Race("heat3", ("1001", ""), ("1001", "dave smith")),
        };

        var names = AnnouncerControl.ClipNamesFor("Dave Smith", "1001", tonight);

        Assert.Equal(new[] { "Dave Smith", "Dave Smyth", "1001" }, names);
    }

    [Fact]
    public void Clip_names_with_no_races_are_the_name_and_the_transponder()
    {
        Assert.Equal(new[] { "Dave Smith", "1001" }, AnnouncerControl.ClipNamesFor("Dave Smith", "1001", null));
        Assert.Equal(new[] { "Dave Smith", "1001" }, AnnouncerControl.ClipNamesFor(" Dave Smith ", "1001", new List<SessionRecord>()));
    }

    // --- A32-1: closing while minimised --------------------------------------------

    [Fact]
    public void Closing_minimised_says_nothing_about_start_maximised()
    {
        Assert.Null(MainForm.StartMaximizedFor(FormWindowState.Minimized));
        Assert.True(MainForm.StartMaximizedFor(FormWindowState.Maximized));
        Assert.False(MainForm.StartMaximizedFor(FormWindowState.Normal));
    }
}
