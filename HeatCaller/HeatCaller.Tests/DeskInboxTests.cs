using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// THE DESK INBOX (15 Nov 2026): the list's order and chips, how long an
/// item has waited, Later, the quick replies - and parity with the club
/// notes window, the room vote window and Online Sign-ups, one test per row
/// of the plan's parity list (plans/relay-themes-and-desk-inbox-plan.md).
/// Driver Requests' rows are in DriverExp_Q_DeskInboxParityTests.
/// </summary>
public sealed class DeskInboxTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime At = new(2026, 11, 15, 19, 0, 0, DateTimeKind.Utc);

    private static InboxItem Item(string id, string kind, int minutes, bool blocking = false, InboxSource source = InboxSource.Request) =>
        new(source, id, kind, kind, "Sam", "what", "", At.AddMinutes(minutes), blocking, null);

    private static T Field<T>(DeskInboxForm form, string name) =>
        (T)typeof(DeskInboxForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    /// <summary>Opens the Done fold - a click on a button in a window never shown does nothing.</summary>
    internal static void ShowDone(DeskInboxForm form)
    {
        typeof(DeskInboxForm).GetField("_showDone", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, true);
        Call(form, "ShowDone");
    }

    private static void Call(DeskInboxForm form, string method, params object?[] args) =>
        typeof(DeskInboxForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);

    // ------------------------------------------------------------ the list

    [Fact]
    public void Race_blocking_comes_first_then_oldest_and_later_goes_to_the_foot()
    {
        var items = new[]
        {
            Item("a", DriverAsk.Details, 0),
            Item("b", DriverAsk.Late, 9, blocking: true),
            Item("c", "note", 3, source: InboxSource.Note),
            Item("d", DriverAsk.Skip, 4, blocking: true),
        };

        Assert.Equal(new[] { "d", "b", "a", "c" }, DeskInbox.Order(items).Select(i => i.Id));

        var later = new HashSet<string> { items[3].Key };
        Assert.Equal(new[] { "b", "a", "c", "d" }, DeskInbox.Order(items, later).Select(i => i.Id));
        Assert.True(DeskInbox.AnyRaceBlocking(items, later));
        Assert.False(DeskInbox.AnyRaceBlocking(items, new HashSet<string> { items[1].Key, items[3].Key }));
    }

    [Theory]
    [InlineData(DriverAsk.Late, true)]
    [InlineData(DriverAsk.Skip, true)]
    [InlineData(DriverAsk.Cover, true)]
    [InlineData(DriverAsk.Swap, true)]
    [InlineData(DriverAsk.Details, false)]
    [InlineData(DriverAsk.Query, false)]
    public void Late_skip_and_marshal_swaps_hold_up_the_race(string kind, bool blocking)
    {
        Assert.Equal(blocking, DeskInbox.RaceBlockingKind(kind));
    }

    [Fact]
    public void Each_chip_shows_its_own_and_says_how_many()
    {
        var items = new[]
        {
            Item("1", DriverAsk.Late, 0, blocking: true),
            Item("2", DriverAsk.Query, 0),
            Item("3", DriverAsk.Crew, 0),
            Item("4", "note", 0, source: InboxSource.Note),
            Item("5", "signup", 0, source: InboxSource.Signup),
        };

        Assert.Equal(5, DeskInbox.Filter(items, InboxFilter.All).Count);
        Assert.Equal(new[] { "1" }, DeskInbox.Filter(items, InboxFilter.RaceBlocking).Select(i => i.Id));
        Assert.Equal(3, DeskInbox.Filter(items, InboxFilter.Requests).Count);
        Assert.Equal(new[] { "2" }, DeskInbox.Filter(items, InboxFilter.Queries).Select(i => i.Id));
        Assert.Equal(new[] { "3" }, DeskInbox.Filter(items, InboxFilter.Crew).Select(i => i.Id));
        Assert.Equal(new[] { "4" }, DeskInbox.Filter(items, InboxFilter.Notes).Select(i => i.Id));
        Assert.Equal(new[] { "5" }, DeskInbox.Filter(items, InboxFilter.Signups).Select(i => i.Id));

        Assert.Equal("Requests 3", DeskInbox.ChipText(InboxFilter.Requests, 3));
        Assert.Equal("Sign-ups", DeskInbox.ChipText(InboxFilter.Signups, 0));
        Assert.Equal("Requests 99", DeskInbox.ChipText(InboxFilter.Requests, 140));
    }

    [Theory]
    [InlineData(20, "now")]
    [InlineData(60 * 4 + 5, "4m")]
    [InlineData(3600, "1h")]
    [InlineData(3600 + 5 * 60, "1h 5m")]
    [InlineData(3 * 86400, "3d")]
    public void How_long_it_has_waited_in_the_fewest_characters(int seconds, string said)
    {
        Assert.Equal(said, DeskInbox.Waited(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void A_race_blocking_one_goes_amber_then_red_sooner()
    {
        var late = Item("1", DriverAsk.Late, 0, blocking: true);
        var details = Item("2", DriverAsk.Details, 0);

        Assert.Equal(InboxAge.Fine, DeskInbox.Age(late, At.AddMinutes(1)));
        Assert.Equal(InboxAge.Amber, DeskInbox.Age(late, At.AddMinutes(3)));
        Assert.Equal(InboxAge.Red, DeskInbox.Age(late, At.AddMinutes(6)));

        Assert.Equal(InboxAge.Fine, DeskInbox.Age(details, At.AddMinutes(3)));
        Assert.Equal(InboxAge.Amber, DeskInbox.Age(details, At.AddMinutes(6)));
        Assert.Equal(InboxAge.Red, DeskInbox.Age(details, At.AddMinutes(16)));
    }

    [Fact]
    public void Later_lasts_until_the_race_is_over_or_ten_minutes()
    {
        var snooze = new InboxSnooze();

        snooze.Snooze("a", At);
        snooze.Snooze("b", At);
        Assert.Equal(new[] { "a", "b" }, snooze.Snoozed(At.AddMinutes(9)).OrderBy(k => k));
        Assert.Empty(snooze.Snoozed(At.AddMinutes(10)));

        snooze.Snooze("a", At);
        snooze.WakeAll();
        Assert.False(snooze.IsSnoozed("a", At));
    }

    [Fact]
    public void Quick_replies_are_the_clubs_own_or_heatcallers()
    {
        Assert.Equal(DeskInbox.DefaultQuickReplies, DeskInbox.QuickReplies(null));
        Assert.Equal(DeskInbox.DefaultQuickReplies, DeskInbox.QuickReplies(new[] { " ", "" }));
        Assert.Equal(new[] { "See Pat", "Tomorrow" }, DeskInbox.QuickReplies(new[] { " See Pat ", "Tomorrow", "See Pat" }));
        Assert.Equal(8, DeskInbox.QuickReplies(Enumerable.Range(1, 12).Select(i => $"Reply {i}")).Count);
    }

    // ------------------------------------------------------------ club notes

    private static DeskInboxForm InboxOverNotes(ClubNotes notes) =>
        new(null, ProfileStore.Load(), null, null, notes, null, null, null, new AppSettings(), null, null, null);

    [Fact]
    public void Parity_notes_waiting_ones_are_listed_with_who_sent_them_and_approving_them_lets_the_announcer_say_it()
    {
        var notes = ClubNotes.Load();
        notes.NewMeeting("tonight");
        Assert.Null(notes.AddFromTheRoom("Shout out to the canteen", "Dot"));
        Assert.Null(notes.AddFromTheRoom("Happy birthday Grandad", null));

        StaThread.Run(() =>
        {
            using var inbox = InboxOverNotes(notes);

            var items = inbox.Items;
            Assert.Equal(new[] { "Dot", "The room" }, items.Select(i => i.From));
            Assert.All(items, i => Assert.Equal(InboxSource.Note, i.Source));

            inbox.Pick(items[0]);
            var approve = Field<Button>(inbox, "_btnApprove");

            // Says what it does out loud, not OK.
            Assert.Equal("Let the announcer say it", approve.Text);
            Assert.True(approve.Enabled);
            Assert.True(Field<Button>(inbox, "_btnRemove").Enabled);

            Call(inbox, "Approve");

            Assert.Equal(1, notes.Counts().ToSay);
            Assert.Equal(1, notes.Counts().ToApprove);
            Assert.Single(inbox.Items);

            // Undo, while it has not been said: back to waiting.
            Call(inbox, "UndoLast");
            Assert.Equal(2, notes.Counts().ToApprove);

            return true;
        }, TimeSpan.FromSeconds(30), () => "approving a club note from the inbox");
    }

    [Fact]
    public void Parity_notes_typing_one_adds_it_as_yours_and_enter_never_approves_a_strangers()
    {
        var notes = ClubNotes.Load();
        notes.NewMeeting("tonight");
        Assert.Null(notes.AddFromTheRoom("Shout out to the canteen", "Dot"));

        StaThread.Run(() =>
        {
            using var inbox = InboxOverNotes(notes);

            var box = Field<TextBox>(inbox, "_txtNote");
            Assert.Equal(ClubNotes.MostChars, box.MaxLength);

            box.Text = "Raffle closes at eight";
            Call(inbox, "AddTypedNote");

            Assert.Equal(2, notes.All.Count);
            Assert.True(Field<bool>(inbox, "_justAdded"));

            // The one typed is ready to say; the room's still waits (bug hunt 17).
            Assert.Equal(1, notes.Counts().ToApprove);
            Assert.Equal(1, notes.Counts().ToSay);

            // No accept button: Enter in the box only ever adds.
            Assert.Null(inbox.AcceptButton);

            return true;
        }, TimeSpan.FromSeconds(30), () => "typing a club note into the inbox");
    }

    [Fact]
    public void Parity_notes_ready_and_said_ones_are_in_done_and_can_still_be_removed()
    {
        var notes = ClubNotes.Load();
        notes.NewMeeting("tonight");
        Assert.Null(notes.Add("Raffle closes at eight"));
        var said = notes.All.Single();
        notes.Said(said.Id);

        StaThread.Run(() =>
        {
            using var inbox = InboxOverNotes(notes);

            ShowDone(inbox);
            var done = Field<DataGridView>(inbox, "_gridDone");
            Assert.Equal(1, done.Rows.Count);
            Assert.Equal("Said", done.Rows[0].Cells["answer"].Value);
            Assert.Equal("You", done.Rows[0].Cells["from"].Value);

            _ = done.Handle;
            done.Rows[0].Selected = true;

            // Said already: removing it is tidying, so no question.
            Call(inbox, "RemoveNote", (object?)said);
            Assert.Empty(notes.All);

            return true;
        }, TimeSpan.FromSeconds(30), () => "removing a said note from the inbox's done list");
    }

    // ------------------------------------------------------------ the vote

    [Fact]
    public void Parity_vote_the_card_opens_on_the_race_about_to_run_tallies_and_stops()
    {
        var vote = RoomVote.Load();
        vote.NewMeeting("tonight");
        var target = new VoteTarget("round-7", "Mod Buggy A-Main", "Mod Buggy",
            new[] { new VoteOption("100", "Sam Parker"), new VoteOption("200", "Dev") }, "/vote on this PC's address");

        StaThread.Run(() =>
        {
            using var inbox = new DeskInboxForm(null, ProfileStore.Load(), null, null, null, vote, () => target, null, new AppSettings(), null, null, null);

            var open = Field<Button>(inbox, "_btnOpenVote");
            var stop = Field<Button>(inbox, "_btnStopVote");

            // SAYS WHAT RACE.
            Assert.Equal("Open voting on Mod Buggy A-Main", open.Text);
            Assert.True(open.Enabled);
            Assert.False(stop.Enabled);
            Assert.StartsWith("No vote has been opened tonight.", Field<Label>(inbox, "_lblVote").Text);
            Assert.Contains("/vote on this PC's address", Field<Label>(inbox, "_lblVoteWhere").Text);

            Call(inbox, "OpenVoting");
            Assert.True(vote.Taking);
            Assert.Equal("round-7", vote.OnRaceKey);
            Assert.StartsWith("Voting is OPEN on", Field<Label>(inbox, "_lblVote").Text);
            Assert.True(stop.Enabled);

            // The tally: every driver, picked or not.
            var tally = Field<DataGridView>(inbox, "_tally");
            Assert.Equal(new[] { "Driver", "Votes", "Share" }, tally.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText));
            Assert.Equal(2, tally.Rows.Count);

            Call(inbox, "StopVoting");
            Assert.False(vote.Taking);
            Assert.StartsWith("Voting on", Field<Label>(inbox, "_lblVote").Text);

            return true;
        }, TimeSpan.FromSeconds(30), () => "the inbox's vote card");
    }

    [Fact]
    public void Parity_vote_no_race_picked_or_nobody_staged_is_nothing_to_open()
    {
        var vote = RoomVote.Load();
        var target = new VoteTarget(null, null, null, Array.Empty<VoteOption>(), null);

        StaThread.Run(() =>
        {
            using var inbox = new DeskInboxForm(null, ProfileStore.Load(), null, null, null, vote, () => target, null, new AppSettings(), null, null, null);

            Assert.False(Field<Button>(inbox, "_btnOpenVote").Enabled);
            Assert.Equal("Open voting", Field<Button>(inbox, "_btnOpenVote").Text);
            Assert.Contains("Voting from the room is switched off", Field<Label>(inbox, "_lblVoteWhere").Text);

            return true;
        }, TimeSpan.FromSeconds(30), () => "the vote card with nothing to vote on");
    }

    // ------------------------------------------------------------ sign-ups

    [Fact]
    public void Parity_signups_waiting_ones_are_listed_and_the_invites_keep_every_button()
    {
        StaThread.Run(() =>
        {
            using var inbox = WindowCatalog.OnlineSignupsWindow(filled: true);

            var signups = inbox.Items.Where(i => i.Source == InboxSource.Signup).ToList();
            Assert.Equal(6, signups.Count);
            Assert.Contains(signups, s => s.Note.Contains("From an invite this laptop doesn't know", StringComparison.Ordinal));

            inbox.Pick(signups[0]);
            Assert.Equal("Approve", Field<Button>(inbox, "_btnApprove").Text);
            Assert.Equal("Turn Down", Field<Button>(inbox, "_btnDecline").Text);
            Assert.True(Field<Button>(inbox, "_btnApprove").Enabled);
            Assert.True(Field<Button>(inbox, "_btnDecline").Enabled);

            var invites = Field<DataGridView>(inbox, "_gridInvites");
            Assert.Equal(12, invites.Rows.Count);
            Assert.Equal(new[] { "For", "Status", "Made", "Works until", "Code" }, invites.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText));

            foreach (var (field, text) in new[]
                     {
                         ("_btnInvite", "Invite Drivers"), ("_btnHere", "Code for Someone Here"), ("_btnShowCode", "Show Code"),
                         ("_btnResend", "Email Again"), ("_btnCancelInvite", "Cancel Invite"),
                     })
            {
                Assert.Equal(text, Field<Button>(inbox, field).Text);
            }

            // Codes are masked until Show Code.
            Assert.Contains(invites.Rows.Cast<DataGridViewRow>(), r => Equals(r.Cells["code"].Value, "••••••••"));

            // "Let new drivers sign up online with an invite" - the desk's own switch.
            var on = Field<CheckBox>(inbox, "_chkSignupsOn");
            Assert.Equal("Let new drivers sign up online with an invite", on.Text);
            Assert.True(on.Checked);

            return true;
        }, TimeSpan.FromSeconds(60), () => "the inbox's sign-ups");
    }

    // ------------------------------------------------------------ keys

    [Fact]
    public void S_puts_the_picked_one_off_until_later_and_S_again_brings_it_back()
    {
        var notes = ClubNotes.Load();
        notes.NewMeeting("tonight");
        Assert.Null(notes.AddFromTheRoom("Shout out to the canteen", "Dot"));
        Assert.Null(notes.AddFromTheRoom("Happy birthday Grandad", "Mia"));

        StaThread.Run(() =>
        {
            using var inbox = InboxOverNotes(notes);
            var press = typeof(DeskInboxForm).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!;

            inbox.Pick(inbox.Items[0]);
            Assert.True((bool)press.Invoke(inbox, new object[] { new Message(), Keys.S })!);

            // Dot's is at the foot now, marked Later.
            Assert.Equal(new[] { "Mia", "Dot" }, inbox.Items.Select(i => i.From));
            var grid = Field<DataGridView>(inbox, "_grid");
            Assert.Equal("Later", grid.Rows[1].Cells["waited"].Value);

            inbox.Pick(inbox.Items[1]);
            press.Invoke(inbox, new object[] { new Message(), Keys.S });
            Assert.Equal(new[] { "Dot", "Mia" }, inbox.Items.Select(i => i.From));

            // The race being over brings back anything still put off.
            inbox.Pick(inbox.Items[0]);
            press.Invoke(inbox, new object[] { new Message(), Keys.S });
            inbox.RaceOver();
            Assert.Equal(new[] { "Dot", "Mia" }, inbox.Items.Select(i => i.From));

            return true;
        }, TimeSpan.FromSeconds(30), () => "Later from the keyboard");
    }

    [Fact]
    public void Every_button_that_opened_an_old_window_opens_the_inbox_on_its_chip()
    {
        static string Source(string file)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);

            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
            {
                dir = dir.Parent;
            }

            return File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", file)).Replace("\r\n", "\n");
        }

        Assert.Contains("private void ShowDriverRequests() => ShowInbox(InboxFilter.Requests);", Source("MainForm.DriverRequests.cs"));
        Assert.Contains("private void ShowSignups() => ShowInbox(InboxFilter.Signups);", Source("MainForm.Relay.cs"));
        Assert.Contains("InboxRequested?.Invoke(InboxFilter.Notes);", Source("RaceControlControl.Broadcast.cs"));
        Assert.Contains("InboxRequested?.Invoke(InboxFilter.Vote);", Source("RaceControlControl.Broadcast.cs"));

        // The old windows are gone.
        var app = typeof(DeskInboxForm).Assembly;
        foreach (var gone in new[] { "HeatCaller.DriverRequestsForm", "HeatCaller.ClubNotesForm", "HeatCaller.RoomVoteForm", "HeatCaller.SignupsForm" })
        {
            Assert.Null(app.GetType(gone));
        }
    }

    [Fact]
    public void Quick_replies_open_with_nothing_selected()
    {
        // 15 Nov 2026, Asher: "quick replies load with the text selected".
        // Shown, as the desk sees it: Windows selects all of the box it focuses first.
        var (start, length, total) = StaThread.Run(() =>
        {
            using var form = new QuickRepliesForm(new[] { "Not tonight, sorry", "Ask at the desk" });
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            var box = form.RepliesBox;
            var got = (box.SelectionStart, box.SelectionLength, box.TextLength);
            form.Close();
            return got;
        }, TimeSpan.FromSeconds(30), () => "showing Quick replies");

        Assert.Equal(0, length);
        Assert.Equal(total, start);
    }
}
