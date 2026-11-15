using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// THE DESK INBOX'S PARITY WITH DRIVER REQUESTS (15 Nov 2026, Asher: "inbox
/// replaces old as long as there's no missing functionality"). One test per
/// row of the plan's parity list for Driver Requests: the waiting list and
/// what it showed, each answer, both filters, the swaps rule, the answered
/// list kept 30 days. Answers that put up a question (Approve on a pack, a
/// late, a swap) are not pressed here - a test must never answer a real
/// ShowDialog - but which answers are offered for each kind is.
/// </summary>
public sealed partial class DriverExp_Q_DriverRequestsTests
{
    private static T Field<T>(DeskInboxForm form, string name) =>
        (T)typeof(DeskInboxForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;

    private static DeskInboxForm InboxOver(World w, AppSettings? settings = null) =>
        new(w.Desk, w.Profiles, _ => { }, null, null, null, null, null, settings ?? new AppSettings(), null, null, null);

    [Fact]
    public void Parity_requests_the_list_shows_who_what_the_note_and_puts_what_holds_up_the_race_first()
    {
        StaThread.Run(() =>
        {
            var w = new World();
            w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["announcer"] = "Zhock"));
            w.File(Ask(2, DevBadge, DriverAsk.Late, Tonight.AddMinutes(5)));
            w.File(Ask(3, NobodyBadge, DriverAsk.Pack, Tonight.AddMinutes(1), r => r.Texts["pack"] = "none"));

            using var inbox = InboxOver(w);
            var items = inbox.Items;

            Assert.Equal(3, items.Count);

            // Late is race-blocking, so first though it came last.
            Assert.Equal(DriverAsk.Late, items[0].Kind);
            Assert.True(items[0].RaceBlocking);
            Assert.Equal(new[] { DriverAsk.Details, DriverAsk.Pack }, items.Skip(1).Select(i => i.Kind));

            // Who, what and the note, as Driver Requests' columns had them.
            var details = items.Single(i => i.Kind == DriverAsk.Details);
            Assert.Equal("Sam Parker", details.From);
            Assert.Equal(w.Desk.Summary((DriverRequest)details.Payload!), details.What);
            Assert.Equal(w.Desk.NoteFor((DriverRequest)details.Payload!), details.Note);
            Assert.Equal("Unknown driver", items.Single(i => i.Kind == DriverAsk.Pack).From);

            var grid = Field<DataGridView>(inbox, "_grid");
            Assert.Equal(new[] { "Waited", "Kind", "From", "What", "Note" }, grid.Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText));
            Assert.Equal(3, grid.Rows.Count);

            return true;
        }, TimeSpan.FromSeconds(30), () => "the inbox's request list");
    }

    [Fact]
    public void Parity_requests_each_kind_offers_the_answers_driver_requests_did()
    {
        StaThread.Run(() =>
        {
            var w = new World();
            var details = w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["announcer"] = "Zhock"));
            var query = w.File(Ask(2, SamBadge, DriverAsk.Query, Tonight, r => { r.Texts["race"] = w.Heat.Id; r.Texts["m"] = "My third lap wasn't counted"; }));
            var unknown = w.File(Ask(3, NobodyBadge, DriverAsk.Pack, Tonight, r => r.Texts["pack"] = "none"));

            using var inbox = InboxOver(w);
            var approve = Field<Button>(inbox, "_btnApprove");
            var noted = Field<Button>(inbox, "_btnNoted");
            var decline = Field<Button>(inbox, "_btnDecline");
            var discard = Field<Button>(inbox, "_btnDiscard");
            var later = Field<Button>(inbox, "_btnLater");

            InboxItem Of(DriverRequest r) => inbox.Items.Single(i => i.Id == r.Id);

            // Details from a known driver: Approve... (its own form, with Hear It), Decline.
            inbox.Pick(Of(details));
            Assert.Equal("Approve", approve.Text);
            Assert.True(approve.Enabled);
            Assert.False(noted.Enabled);
            Assert.True(decline.Enabled);
            Assert.False(discard.Enabled);
            Assert.True(later.Enabled);

            // A result query: Noted, not Approve.
            inbox.Pick(Of(query));
            Assert.False(approve.Enabled);
            Assert.True(noted.Enabled);

            // A badge nobody here has: only Decline or Discard.
            inbox.Pick(Of(unknown));
            Assert.False(approve.Enabled);
            Assert.True(decline.Enabled);
            Assert.True(discard.Enabled);

            return true;
        }, TimeSpan.FromSeconds(30), () => "the inbox's answers for each kind");
    }

    [Fact]
    public void Parity_requests_noted_and_a_quick_reply_decline_reach_the_phone_and_go_to_done()
    {
        StaThread.Run(() =>
        {
            var w = new World();
            var q1 = w.File(Ask(1, SamBadge, DriverAsk.Query, Tonight, r => { r.Texts["race"] = w.Heat.Id; r.Texts["m"] = "My third lap wasn't counted"; }));
            var q2 = w.File(Ask(2, DevBadge, DriverAsk.Query, Tonight.AddMinutes(1), r => { r.Texts["race"] = w.Heat.Id; r.Texts["m"] = "My third lap wasn't counted"; }));
            var details = w.File(Ask(3, SamBadge, DriverAsk.Details, Tonight.AddMinutes(2), r => r.Texts["announcer"] = "Zhock"));

            using var inbox = InboxOver(w);

            // Two queries at once: one Noted for both (N).
            inbox.Pick(inbox.Items.Where(i => i.Kind == DriverAsk.Query).ToArray());
            typeof(DeskInboxForm).GetMethod("Noted", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(inbox, null);

            Assert.Equal(DriverAsk.Noted, w.Store.Get(q1.Id)!.State);
            Assert.Equal(DriverAsk.Noted, w.Store.Get(q2.Id)!.State);

            // A quick reply: declined with those words, no form.
            inbox.Pick(inbox.Items.Single(i => i.Id == details.Id));
            inbox.Decline("Ask at the desk");

            var answered = w.Store.Get(details.Id)!;
            Assert.Equal(DriverAsk.Declined, answered.State);
            Assert.Equal("Ask at the desk", answered.Said);
            Assert.Empty(inbox.Items);

            // The Done list keeps every answered one (30 days, as Driver Requests did).
            var toggle = Field<Button>(inbox, "_doneToggle");
            Assert.Equal("Show done (3)", toggle.Text);
            DeskInboxTests.ShowDone(inbox);
            Assert.Equal(3, Field<DataGridView>(inbox, "_gridDone").Rows.Count);
            Assert.Equal(new[] { "Sent", "Kind", "From", "What", "Answer", "Ref" },
                Field<DataGridView>(inbox, "_gridDone").Columns.Cast<DataGridViewColumn>().Select(c => c.HeaderText));

            return true;
        }, TimeSpan.FromSeconds(30), () => "noting and declining from the inbox");
    }

    [Fact]
    public void Parity_requests_the_queries_and_crew_chips_and_the_swaps_rule()
    {
        StaThread.Run(() =>
        {
            var w = new World();
            w.File(Ask(1, SamBadge, DriverAsk.Query, Tonight, r => { r.Texts["race"] = w.Heat.Id; r.Texts["m"] = "My third lap wasn't counted"; }));
            w.File(Ask(2, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["announcer"] = "Zhock"));

            using var inbox = InboxOver(w);

            inbox.SetFilter(InboxFilter.Queries);
            Assert.Equal(1, Field<DataGridView>(inbox, "_grid").Rows.Count);

            inbox.SetFilter(InboxFilter.Crew);
            Assert.Equal(0, Field<DataGridView>(inbox, "_grid").Rows.Count);
            Assert.Equal("All clear - nothing is waiting for you.", Field<Label>(inbox, "_lblEmpty").Text);

            inbox.SetFilter(InboxFilter.Requests);
            Assert.Equal(2, Field<DataGridView>(inbox, "_grid").Rows.Count);

            // "Marshal swaps from phones need the desk's OK" - the desk's own setting.
            var swaps = Field<CheckBox>(inbox, "_chkSwapsNeedDesk");
            Assert.Equal("Marshal swaps from phones need the desk's OK", swaps.Text);
            var was = w.Desk.SwapsNeedDesk;
            swaps.Checked = !was;
            Assert.Equal(!was, w.Desk.SwapsNeedDesk);

            return true;
        }, TimeSpan.FromSeconds(30), () => "the inbox's request chips and rules");
    }
}
