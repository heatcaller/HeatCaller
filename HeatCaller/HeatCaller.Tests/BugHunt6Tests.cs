namespace HeatCaller.Tests;

/// <summary>
/// THE SWARM BUG HUNT OF 12 OCT 2026 - the findings that can be pinned by
/// a test, each one written against the behaviour the bug had.
///
/// Five days of new code were read by eight agents at once, none of which
/// could compile any of it. What they found divides into three kinds, and
/// only two of them are testable:
///
///   - FALSE CLAIMS about a championship, which are pinned where they
///     belong, in CommentaryStakesTests;
///   - THREADING, which is what most of this file is: three stores reached
///     from an HttpListener thread, the relay publisher's thread and the UI
///     thread with no lock between them;
///   - ORDERING, where a fire-and-forget voice call was treated as though
///     it had finished speaking - which needs a voice engine and a sound
///     device to show, and is pinned by its own arithmetic here instead.
///
/// WHY THE THREADING TESTS LOOK LIKE A STRESS TEST. Because that is the
/// only shape that can fail: a dictionary written while it is enumerated
/// throws InvalidOperationException, and whether it does depends on timing.
/// Each of these hammers the pair of operations that collided, hard enough
/// that the unlocked version fails nearly every run, and they are fast.
/// </summary>
public sealed class BugHunt6Tests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    // ------------------------------------------------------------------
    //  Club notes, written from the room and read by a window
    // ------------------------------------------------------------------

    [Fact]
    public void The_notes_list_is_handed_out_as_a_copy()
    {
        // All used to return the store's own list. ClubNotesForm.Fill walks
        // it, on the UI thread, with no try/catch - and a note arriving
        // from the room appends to it on an HttpListener thread. The caller
        // was enumerating live state.
        var notes = ClubNotes.Load();

        notes.NewMeeting("night-1");
        notes.Add("Happy birthday Dot");

        var held = notes.All;

        notes.Add("And many more");

        // The copy is the copy: what the caller is walking cannot grow
        // underneath them.
        Assert.Single(held);
        Assert.Equal(2, notes.All.Count);
    }

    [Fact]
    public async Task Notes_arriving_from_the_room_cannot_break_a_window_reading_them()
    {
        var notes = ClubNotes.Load();

        notes.NewMeeting("night-1");

        var reader = Task.Run(() =>
        {
            for (var pass = 0; pass < 400; pass++)
            {
                // Exactly what ClubNotesForm.Fill does: walk them all, and
                // ask for the counts.
                foreach (var note in notes.All)
                {
                    _ = note.Text.Length;
                }

                _ = notes.Counts();
                _ = notes.Waiting.Count;
                _ = notes.ToSay();
            }
        });

        var writer = Task.Run(() =>
        {
            for (var at = 0; at < 400; at++)
            {
                // Refused past the ceiling, which is fine - the point is
                // the list being written while the other thread walks it.
                notes.AddFromTheRoom($"Shout out number {at}");
                notes.Add($"Typed note {at}");
            }
        });

        // An exception on either thread IS the failure this test is looking
        // for, so awaiting is enough: Task.WhenAll rethrows the first one
        // and the first one is a failed test. Awaited rather than waited on
        // because a blocking wait in a test can deadlock (xUnit1031).
        await Task.WhenAll(reader, writer);
    }

    [Fact]
    public void Approving_a_note_by_its_id_leaves_every_other_note_alone()
    {
        // The STORE was always right about this; ClubNotesForm was not.
        // Restoring the selection after a redraw marked the row selected
        // and left the grid's CurrentRow on the first one - and the window
        // reads CurrentRow - so "Let the announcer say it" handed this
        // method the id of the OLDEST waiting note while the row somebody
        // was looking at stayed highlighted. This pins the half that can be
        // tested without a message loop: the right id approves the right
        // note and nothing else.
        var notes = ClubNotes.Load();

        notes.NewMeeting("night-1");
        notes.AddFromTheRoom("Happy birthday Dot");
        notes.AddFromTheRoom("Well driven Sam");

        var second = notes.Waiting[1];

        notes.Approve(second.Id);

        Assert.True(notes.All.Single(one => one.Id == second.Id).Approved);
        Assert.Equal(second.Id, notes.ToSay()?.Id);

        // And the FIRST one, which is the one the window's bug approved
        // instead, is untouched.
        Assert.False(notes.All.First().Approved);
    }

    // ------------------------------------------------------------------
    //  The room's vote, cast from phones and tallied by a window
    // ------------------------------------------------------------------

    [Fact]
    public void The_grid_a_vote_offers_is_handed_out_as_a_copy()
    {
        var vote = RoomVote.Load();

        vote.NewMeeting("night-1");

        // THREE, THEN TWO, so the two counts differ and the assertion at
        // the end can tell a copy from the list itself. Two is the fewest a
        // vote can be at all (RoomVote.OpenFor: one name is not a
        // question), so a one-driver second vote would simply have been
        // refused - which is what this fixture did when it was written.
        Assert.Null(vote.OpenFor(
            "round-a",
            "A-Main",
            "Box Stock",
            new[] { new VoteOption("t1", "Bill"), new VoteOption("t2", "Sam"), new VoteOption("t3", "Dot") }));

        var grid = vote.Options;

        Assert.Null(vote.OpenFor(
            "round-b",
            "B-Main",
            "Box Stock",
            new[] { new VoteOption("t4", "Eve"), new VoteOption("t5", "Zed") }));

        // The list the caller is holding is the grid they asked for, not
        // the one the next vote replaced it with.
        Assert.Equal(3, grid.Count);
        Assert.Equal(2, vote.Options.Count);
        Assert.Equal(new[] { "Bill", "Sam", "Dot" }, grid.Select(one => one.Name).ToArray());
    }

    [Fact]
    public async Task Votes_arriving_from_phones_cannot_break_a_window_tallying_them()
    {
        var vote = RoomVote.Load();

        vote.NewMeeting("night-1");

        Assert.Null(vote.OpenFor(
            "round-a",
            "A-Main",
            "Box Stock",
            new[] { new VoteOption("t1", "Bill"), new VoteOption("t2", "Sam") }));

        var reader = Task.Run(() =>
        {
            for (var pass = 0; pass < 400; pass++)
            {
                // Exactly what RoomVoteForm's one-second timer does.
                foreach (var standing in vote.Standing())
                {
                    _ = standing.Share;
                }

                _ = vote.Votes;
                _ = vote.Taking;
                _ = vote.Options.Count;
            }
        });

        // Two writers, because the two that collide in the app are a phone
        // on an HttpListener thread and the relay's collector on the
        // publisher's thread - and they write the same dictionary.
        var phones = Task.Run(() =>
        {
            for (var at = 0; at < 300; at++)
            {
                vote.Cast($"phone-{at}", at % 2 == 0 ? "t1" : "t2");
            }
        });

        var relay = Task.Run(() =>
        {
            for (var at = 0; at < 300; at++)
            {
                vote.Cast($"relay:phone-{at}", "t2");
                _ = vote.PickOf($"relay:phone-{at}");
            }
        });

        await Task.WhenAll(reader, phones, relay);

        // And every vote that was accepted is still there to be counted.
        Assert.True(vote.Votes > 0);
        Assert.Equal(vote.Votes, vote.Standing().Sum(one => one.Votes));
    }

    // ------------------------------------------------------------------
    //  The night's recap, and how long it is allowed to take
    // ------------------------------------------------------------------

    [Fact]
    public void The_recap_is_allowed_longer_than_the_longest_recap_can_take_to_read()
    {
        // A show winds itself up after four minutes, which is right for a
        // segment. A recap is up to fifteen pieces of twenty seconds, which
        // is five - so the music came up under the last minute of it and
        // the bookend recording closed its file there. Both callers now
        // pass NightRecap.LongestRead.
        var longestPossible = NightRecap.LongestChunk * NightRecap.MostChunks;

        Assert.True(
            NightRecap.LongestRead > longestPossible,
            $"{NightRecap.LongestRead} is not longer than the {longestPossible} fifteen pieces of twenty seconds take.");

        // And the margin is a margin rather than a rounding - there is a
        // wait while each piece is fetched and started.
        Assert.True(NightRecap.LongestRead - longestPossible >= TimeSpan.FromSeconds(30));
    }
}
