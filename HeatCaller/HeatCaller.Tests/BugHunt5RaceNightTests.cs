namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 5, race night: the five things a volunteer could do at 7pm with
/// forty racers waiting and no way back.
///
/// A sponsor video that the grid roll call took the screen from kept
/// playing, with its audio over the start of the race and the music muted
/// for the whole heat, because the roll call stopped the slide "showing"
/// without stopping the video - and StopSponsorVideo is the only route to
/// AdBreak.End. Delete on the programme would take a round that had already
/// been run while reassuring the operator that nothing was lost. Close Day
/// fired from its Click handler with no question and no look at the track,
/// from the button beside Delete Day - which refuses outright while a round
/// is on track. Open Big Screen opened a second identical window over the
/// first. And deleting a run-sheet line captured a whole-sheet snapshot, so
/// undoing it took back everything done to the sheet since.
///
/// Two of these live inside a window, where the only way in is a press on a
/// button: the roll call's end of a slide, and the second Big Screen. They
/// are checked by reading the source, the way InstallerTests checks the
/// order of things in Main - a worse test than a real one, and much better
/// than none, because both failures are silent and neither shows up until a
/// club hits it on a race night.
/// </summary>
public sealed class BugHunt5RaceNightTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- The roll call over a sponsor video ---------------------------------

    [Fact]
    public void The_roll_call_coming_up_over_a_sponsor_video_stops_the_video()
    {
        // It used to clear _showingSponsor and put the PICTURE away, and
        // leave LibVLC playing: the ad's sound carried on over the staging
        // and the start, and with nothing "showing" neither the rotation's
        // tick nor the video's own Finished could reach
        // ShowLeaderboardAgain - the only route to StopSponsorVideo, which
        // is the only caller of AdBreak.End in the app. The music and the
        // crowd bed stayed down for the whole heat.
        var rollCall = MethodBody(
            Source("BigScreenForm.cs"), "private void UpdateRollCall()", "private void HideRollCall()");

        Assert.Contains("StopSponsorVideo();", rollCall, StringComparison.Ordinal);
    }

    // --- Deleting a round that has already been run -------------------------

    [Fact]
    public void A_selection_of_rounds_that_have_not_been_run_has_nothing_to_warn_about()
    {
        var rounds = new[] { Round("Q1 Heat 1"), Round("Q1 Heat 2") };

        Assert.Empty(RaceProgramControl.RecordedAmong(rounds));
    }

    [Fact]
    public void The_rounds_in_a_selection_that_have_been_run_are_the_ones_named()
    {
        var q1 = Round("Q1 Heat 1", run: true);
        var q2 = Round("Q1 Heat 2");
        var q3 = Round("Q1 Heat 3", run: true);

        var recorded = RaceProgramControl.RecordedAmong(new[] { q1, q2, q3 });

        // In the order they were given, which is the order of the grid.
        Assert.Equal(new[] { "Q1 Heat 1", "Q1 Heat 3" }, recorded.Select(r => r.RoundLabel));
    }

    [Fact]
    public void Deleting_one_round_that_has_been_run_says_its_result_stops_counting()
    {
        // The old dialog said "This does not delete any recorded session
        // history" and nothing else - true of the history file, and the
        // opposite of what an operator concludes from it. A SessionRecord
        // holds a RoundId and no event id, so the round going takes both
        // links with it.
        var question = RaceProgramControl.RecordedDeleteQuestion(new[] { Round("A-Main", run: true) });

        Assert.Contains("\"A-Main\" has already been run", question, StringComparison.Ordinal);
        Assert.Contains("race day", question, StringComparison.Ordinal);
        Assert.Contains("championship", question, StringComparison.Ordinal);
        Assert.Contains("standings move", question, StringComparison.Ordinal);

        // It still offers to do it - taking a run round off the programme is
        // what PlanSeedMains sends a club here to do.
        Assert.Contains("Delete it anyway?", question, StringComparison.Ordinal);
    }

    [Fact]
    public void Deleting_several_rounds_that_have_been_run_lists_every_one_of_them()
    {
        var question = RaceProgramControl.RecordedDeleteQuestion(new[]
        {
            Round("Q1 Heat 1", run: true),
            Round("A-Main", run: true, className: "Stock"),
        });

        Assert.Contains("These 2 rounds have already been run", question, StringComparison.Ordinal);
        Assert.Contains("Q1 Heat 1", question, StringComparison.Ordinal);
        Assert.Contains("A-Main", question, StringComparison.Ordinal);
        Assert.Contains("(Stock)", question, StringComparison.Ordinal);
        Assert.Contains("Delete them anyway?", question, StringComparison.Ordinal);
    }

    // --- Close Day ----------------------------------------------------------

    [Fact]
    public void Closing_a_day_says_how_many_rounds_have_not_been_run_yet()
    {
        var question = RaceDayEventControl.CloseDayQuestion("Thursday Club Night", roundsLeftToRun: 7);

        Assert.Contains("Close \"Thursday Club Night\"?", question, StringComparison.Ordinal);
        Assert.Contains("7 rounds on the program have not been run yet", question, StringComparison.Ordinal);
    }

    [Fact]
    public void One_round_left_to_run_is_said_in_the_singular()
    {
        Assert.Contains(
            "1 round on the program has not been run yet",
            RaceDayEventControl.CloseDayQuestion("Club Night", roundsLeftToRun: 1),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Closing_a_day_with_everything_run_does_not_mention_rounds_left()
    {
        // The end of the night is the one time this is the right button, and
        // it should not be made to read like a warning.
        var question = RaceDayEventControl.CloseDayQuestion("Club Night", roundsLeftToRun: 0);

        Assert.DoesNotContain("not been run yet", question, StringComparison.Ordinal);
        Assert.Contains("Close \"Club Night\"?", question, StringComparison.Ordinal);
    }

    [Fact]
    public void Closing_a_day_says_what_stops_working_and_how_to_put_it_back()
    {
        // What one press actually did, none of which the button or the hint
        // under it says: RoundPicker.RaceDayOpen goes false so finished
        // races file against no round, CheckInStore.CurrentEventId follows
        // Active to the legacy sheet, and ActiveRoundScope and
        // QualifyingRoundsCounted stop seeing tonight's heats.
        var question = RaceDayEventControl.CloseDayQuestion("Club Night", roundsLeftToRun: 3);

        Assert.Contains("file against no round", question, StringComparison.Ordinal);
        Assert.Contains("check-in", question, StringComparison.Ordinal);
        Assert.Contains("legacy day", question, StringComparison.Ordinal);
        Assert.Contains("Seed", question, StringComparison.Ordinal);
        Assert.Contains("Reopen Day", question, StringComparison.Ordinal);
    }

    [Fact]
    public void The_day_holding_the_round_on_track_is_the_one_Close_Day_must_refuse()
    {
        // The same pure guard the delete uses, so the two gestures cannot
        // disagree about what "on track" means.
        var day = new RaceEvent { Name = "Club Night", RoundIds = { "r1", "r2" } };
        var other = new RaceEvent { Name = "Last Week", RoundIds = { "r9" } };

        Assert.Same(day, RaceDayDeleter.DayWithRoundOnTrack(new[] { day, other }, "r2"));
        Assert.Null(RaceDayDeleter.DayWithRoundOnTrack(new[] { day, other }, "r3"));
        Assert.Null(RaceDayDeleter.DayWithRoundOnTrack(new[] { day, other }, null));
    }

    [Fact]
    public void Close_Day_looks_at_the_track_and_asks_before_the_day_is_closed()
    {
        // It used to call RaceEventStore.Close straight from the Click
        // handler. Both the guard and the question have to come before it,
        // and there is no way to press that button in a test. They now live
        // in AskToCloseDay, which The Day and the Calendar both go through,
        // so the two Close Day buttons cannot disagree.
        var source = Source("RaceDayEventControl.cs");
        var ask = MethodBody(source, "internal static bool AskToCloseDay(", "private void CloseOrReopen()");
        var close = MethodBody(source, "private void CloseOrReopen()", null);

        var guard = ask.IndexOf("DayWithRoundOnTrack", StringComparison.Ordinal);
        var asked = ask.IndexOf("CloseDayQuestion", StringComparison.Ordinal);
        var noByDefault = ask.IndexOf("MessageBoxDefaultButton.Button2", StringComparison.Ordinal);

        Assert.True(guard >= 0, "Close Day does not check for a round on track at all.");
        Assert.True(asked >= 0, "Close Day does not ask anything at all.");
        Assert.True(noByDefault >= 0, "Close Day's question no longer has No as the default.");
        Assert.True(guard < asked, "The round-on-track guard must run before the question.");

        var gate = close.IndexOf("AskToCloseDay(", StringComparison.Ordinal);
        var closed = close.IndexOf("_events.Close(", StringComparison.Ordinal);

        Assert.True(gate >= 0, "The Day's Close Day no longer goes through AskToCloseDay.");
        Assert.True(closed >= 0, "Close Day no longer closes the day.");
        Assert.True(gate < closed, "The guard and the question must come before the day is closed.");

        // The Calendar's Close Day used to skip both.
        var calendar = Source("EventsControl.cs");
        var calendarGate = calendar.IndexOf("AskToCloseDay(", StringComparison.Ordinal);
        var calendarClosed = calendar.IndexOf(".Close(", StringComparison.Ordinal);

        Assert.True(calendarGate >= 0, "The Calendar's Close Day does not go through AskToCloseDay.");
        Assert.True(calendarGate < calendarClosed, "The Calendar must ask before it closes the day.");
    }

    // --- Two Big Screens ----------------------------------------------------

    [Fact]
    public void Opening_the_Big_Screen_twice_brings_the_one_already_up_to_the_front()
    {
        // Reachable from this page's own button and from Local Screens
        // through TriggerOpenBigScreen, and it used to build a new window
        // each time - each subscribed to LapProcessed, StateChanged,
        // FlagChanged, SectorsRecorded and SplitCrossed, each running its own
        // 500ms timer and its own sponsor rotation. Closing the top one
        // revealed an identical one underneath, so Escape read as broken.
        var open = MethodBody(
            Source("RaceControlControl.Layout.cs"), "_actBigScreen = () =>", "_btnBigScreen.Click");

        Assert.Contains("_bigScreen", open, StringComparison.Ordinal);
        Assert.Contains("Activate()", open, StringComparison.Ordinal);

        // And the field has to be let go when the window closes, or the
        // button would do nothing for the rest of the night.
        Assert.Contains("FormClosed", open, StringComparison.Ordinal);
        Assert.Contains("_bigScreen = null", open, StringComparison.Ordinal);
    }

    // --- Undoing a run-sheet delete ----------------------------------------

    [Fact]
    public void Undoing_a_run_sheet_delete_leaves_a_line_deleted_after_it_deleted()
    {
        var store = DayScheduleStore.Load();
        var lunch = Line("Lunch");
        var presentation = Line("Presentation");

        store.Add(lunch);
        store.Add(presentation);

        // What the old undo captured: the whole sheet as it stood before the
        // first delete.
        var snapshot = store.Snapshot();

        store.Delete(lunch.Id);
        store.Delete(presentation.Id);

        // Undoing the FIRST delete through that snapshot brings the second
        // one's line back with it - the behaviour this fixes.
        store.Restore(snapshot);
        Assert.Equal(2, store.All.Count);

        // Back to where the two deletes left the sheet, and undo only the
        // first the way the page does now.
        store.Delete(lunch.Id);
        store.Delete(presentation.Id);

        foreach (var entry in DayScheduleControl.LinesToPutBack(new[] { lunch }, store.Everything))
        {
            store.Add(entry);
        }

        Assert.Equal(new[] { "Lunch" }, store.All.Select(e => e.Description));
    }

    [Fact]
    public void A_deleted_line_comes_back_with_its_time_its_anchor_and_its_tick()
    {
        var store = DayScheduleStore.Load();
        var lunch = Line("Lunch", time: "12:30", afterRounds: 4);

        store.Add(lunch);
        store.SetComplete(lunch.Id, true);
        store.Delete(lunch.Id);

        foreach (var entry in DayScheduleControl.LinesToPutBack(new[] { lunch }, store.Everything))
        {
            store.Add(entry);
        }

        var back = Assert.Single(store.All);

        Assert.Equal(lunch.Id, back.Id);
        Assert.Equal("12:30", back.ScheduledTime);
        Assert.Equal(4, back.AfterRounds);
        Assert.True(back.IsComplete);
    }

    [Fact]
    public void A_line_that_is_back_on_the_sheet_already_is_not_put_back_twice()
    {
        // Undo pressed twice, or the line typed again by hand since. The
        // same rule DayScheduleStore.RestoreTimes keeps: an undo takes back
        // the one thing it names, onto the sheet as it is now.
        var lunch = Line("Lunch");

        Assert.Empty(DayScheduleControl.LinesToPutBack(new[] { lunch }, new[] { lunch }));

        // And the same line twice in one delete cannot come back twice.
        Assert.Single(DayScheduleControl.LinesToPutBack(new[] { lunch, lunch }, Array.Empty<ScheduleEntry>()));
    }

    [Fact]
    public void A_line_deleted_on_one_race_day_goes_back_to_that_day_after_switching()
    {
        // An undo made after switching race days reaches the day it was made
        // on, because the line carries its own EventId.
        var store = DayScheduleStore.Load();

        store.CurrentEventId = "day-one";
        var lunch = Line("Lunch");
        store.Add(lunch);
        store.Delete(lunch.Id);

        store.CurrentEventId = "day-two";

        foreach (var entry in DayScheduleControl.LinesToPutBack(new[] { lunch }, store.Everything))
        {
            store.Add(entry);
        }

        Assert.Empty(store.All);
        Assert.Equal(new[] { "day-one" }, store.Everything.Select(e => e.EventId));
    }

    // --- Helpers ------------------------------------------------------------

    private static RoundDefinition Round(string label, bool run = false, string className = "Stock") =>
        new()
        {
            RoundLabel = label,
            ClassName = className,
            LinkedSessionId = run ? Guid.NewGuid().ToString("N") : null,
        };

    private static ScheduleEntry Line(string description, string time = "", int afterRounds = 0) =>
        new()
        {
            Description = description,
            ScheduledTime = time,
            AfterRounds = afterRounds,
        };

    /// <summary>
    /// The folder the repo is checked out in, found by the file that is only
    /// ever at its root - the same way InstallerTests finds it.
    /// </summary>
    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    /// <summary>
    /// The source from <paramref name="from"/> up to <paramref name="until"/>
    /// - what comes after the one and before the other, which is enough to
    /// say whether a call is inside it. Null for <paramref name="until"/>
    /// takes the rest of the file.
    /// </summary>
    private static string MethodBody(string source, string from, string? until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var rest = source[start..];

        if (until is null)
        {
            return rest;
        }

        var end = rest.IndexOf(until, StringComparison.Ordinal);

        Assert.True(end >= 0, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return rest[..end];
    }
}
