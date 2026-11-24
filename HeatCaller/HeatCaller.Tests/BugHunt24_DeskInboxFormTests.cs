using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the Desk Inbox: the toast's Undo after one
/// approve, the "handled for you tonight" count past midnight, the "Holding
/// up" caption following Race Control, and several picked rows surviving a
/// redraw. In the driver-requests partial class so the World fixture is shared.
/// </summary>
public sealed partial class DriverExp_Q_DriverRequestsTests
{
    // #160 (F114): "before" is taken after the question's window closes, just
    // before the desk call, so a phone's own change made while the window was
    // open is not swept into this answer's Undo.
    [Fact]
    public void BugHunt24_Approving_one_takes_the_undo_mark_after_its_window_not_before()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "DeskInboxForm.Requests.cs")).Replace("\r\n", "\n");
        var start = source.IndexOf("var one = current[0];", StringComparison.Ordinal);
        var end = source.IndexOf("Toast($\"Approved: {desk.NameFor(one)}", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);

        var body = source[start..end];
        var head = body[..body.IndexOf("switch (one.Kind)", StringComparison.Ordinal)];

        Assert.DoesNotContain("NewestUndo()", head, StringComparison.Ordinal);

        var calls = new[]
        {
            "desk.ApplyDetails(one, choice",
            "desk.ApplyCrew(one, chosen",
            "desk.ApprovePack(one, out",
            "desk.ApproveVideoHide(one, out",
            "desk.ApproveRaceAsk(one, out",
            "desk.ApproveCover(one, out",
        };

        foreach (var call in calls)
        {
            Assert.Matches(new Regex(@"before = NewestUndo\(\);\s*if \(!" + Regex.Escape(call)), body);
        }

        Assert.Equal(calls.Length, Regex.Matches(body, @"before = NewestUndo\(\);").Count);
    }

    // #159 (F113): the count is the race night, 6am to 6am, not the calendar day.
    [Fact]
    public void BugHunt24_Handled_for_you_tonight_keeps_counting_past_midnight()
    {
        static DriverRequest Handled(string id, DateTime local) => new()
        {
            Id = id,
            Kind = DriverAsk.Car,
            State = DriverAsk.Approved,
            AnsweredUtc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime(),
        };

        var all = new[]
        {
            Handled("1", new DateTime(2026, 10, 14, 21, 0, 0)),  // this evening
            Handled("2", new DateTime(2026, 10, 15, 0, 10, 0)),  // after midnight, same night
            Handled("3", new DateTime(2026, 10, 14, 5, 0, 0)),   // the night before
        };

        Assert.Equal(2, DeskInboxForm.HandledToday(all, new DateTime(2026, 10, 15, 0, 30, 0, DateTimeKind.Local)));
        Assert.Equal(0, DeskInboxForm.HandledToday(all, new DateTime(2026, 10, 15, 6, 30, 0, DateTimeKind.Local)));
    }

    // #157 (F111): the "Holding up <race>" caption follows Race Control to the
    // next race without the rows being redrawn.
    [Fact]
    public void BugHunt24_The_holding_up_caption_follows_race_control_to_the_next_race()
    {
        StaThread.Run(() =>
        {
            var w = new World();
            w.File(Ask(1, DevBadge, DriverAsk.Late, Tonight));
            var race = "A-Main Buggy";

            using var inbox = new DeskInboxForm(w.Desk, w.Profiles, _ => { }, null, null, null,
                () => new VoteTarget(null, race, null, Array.Empty<VoteOption>(), null),
                null, new AppSettings(), null, null, null);

            inbox.Pick(inbox.Items.Single());
            Assert.Equal("Holding up A-Main Buggy · 1", inbox.GroupCaptions[0].Text);

            race = "B-Main Buggy";
            inbox.RefreshAll(force: false);

            Assert.Equal("Holding up B-Main Buggy · 1", inbox.GroupCaptions[0].Text);
            Assert.True(inbox.GroupCaptions[0].Blocking);
            Assert.Single(inbox.SelectedItems());

            return true;
        }, TimeSpan.FromSeconds(30), () => "the holding-up caption after the race moved on");
    }

    // #158 (F112): three rows picked with the lowest one current all stay
    // picked when the list redraws.
    [Fact]
    public void BugHunt24_Several_picked_rows_stay_picked_when_the_list_redraws_with_the_lowest_current()
    {
        StaThread.Run(() =>
        {
            var w = new World();
            w.File(Ask(1, SamBadge, DriverAsk.Query, Tonight, r => { r.Texts["race"] = w.Heat.Id; r.Texts["m"] = "Lap 1"; }));
            w.File(Ask(2, DevBadge, DriverAsk.Query, Tonight.AddMinutes(1), r => { r.Texts["race"] = w.Heat.Id; r.Texts["m"] = "Lap 2"; }));
            w.File(Ask(3, SamBadge, DriverAsk.Query, Tonight.AddMinutes(2), r => { r.Texts["race"] = w.Heat.Id; r.Texts["m"] = "Lap 3"; }));

            using var inbox = new DeskInboxForm(w.Desk, w.Profiles, _ => { }, null, null, null, null, null, new AppSettings(), null, null, null);
            var grid = Field<DataGridView>(inbox, "_grid");
            _ = grid.Handle;
            Assert.Equal(3, grid.Rows.Count);

            // As a downward Ctrl+click leaves it: the last row current, all three picked.
            var column = grid.Columns.Cast<DataGridViewColumn>().First(c => c.Visible);
            grid.CurrentCell = grid.Rows[2].Cells[column.Index];
            grid.Rows[0].Selected = true;
            grid.Rows[1].Selected = true;
            grid.Rows[2].Selected = true;

            var picked = inbox.SelectedItems().Select(i => i.Key).ToList();
            var currentKey = ((InboxItem)grid.Rows[2].Tag!).Key;
            Assert.Equal(3, picked.Count);

            inbox.RefreshAll(force: true);

            Assert.Equal(picked, inbox.SelectedItems().Select(i => i.Key).ToList());
            Assert.Equal(currentKey, ((InboxItem)grid.CurrentRow!.Tag!).Key);

            // A new request arriving redraws the list by itself - the three stay picked.
            w.File(Ask(4, DevBadge, DriverAsk.Query, Tonight.AddMinutes(3), r => { r.Texts["race"] = w.Heat.Id; r.Texts["m"] = "Lap 4"; }));
            inbox.RefreshAll(force: false);

            Assert.Equal(4, grid.Rows.Count);
            Assert.Equal(picked.OrderBy(k => k, StringComparer.Ordinal), inbox.SelectedItems().Select(i => i.Key).OrderBy(k => k, StringComparer.Ordinal));

            return true;
        }, TimeSpan.FromSeconds(30), () => "several picked rows through a redraw");
    }
}
