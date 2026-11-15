using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// The Desk Inbox's Undo for drivers' requests (15 Nov 2026, Asher: "fix
/// them all"): the toast offers Undo for any answer the desk put on the
/// app's Undo - a pack, Noted, a decline, several at once - so a pack no
/// longer asks "Approve it?" first. And Rules counts what phones changed by
/// themselves tonight.
/// </summary>
public sealed partial class DriverExp_Q_DriverRequestsTests
{
    private static DeskInboxForm InboxWithUndo(World w) =>
        new(w.Desk, w.Profiles, _ => { }, null, null, null, null, null, new AppSettings(), null, null, w.Undo);

    private static void Press(DeskInboxForm form, string method, params object?[] args) =>
        typeof(DeskInboxForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, args);

    [Fact]
    public void Undo_on_the_toast_takes_back_a_decline_and_the_request_waits_again()
    {
        StaThread.Run(() =>
        {
            var w = new World();
            var ask = w.File(Ask(1, SamBadge, DriverAsk.Details, Tonight, r => r.Texts["announcer"] = "Zhock"));

            using var inbox = InboxWithUndo(w);
            inbox.Pick(inbox.Items.Single());
            inbox.Decline("Not tonight");

            Assert.Equal(DriverAsk.Declined, w.Store.Get(ask.Id)!.State);
            Assert.True(Field<LinkLabel>(inbox, "_lnkUndo").Visible || Field<object?>(inbox, "_toastUndo") is not null);

            Press(inbox, "UndoLast");

            Assert.True(w.Store.Get(ask.Id)!.IsWaiting);
            Assert.Single(inbox.Items);

            return true;
        }, TimeSpan.FromSeconds(30), () => "undoing a decline from the inbox");
    }

    [Fact]
    public void Noting_two_queries_at_once_is_undone_together()
    {
        StaThread.Run(() =>
        {
            var w = new World();
            var q1 = w.File(Ask(1, SamBadge, DriverAsk.Query, Tonight, r => { r.Texts["race"] = w.Heat.Id; r.Texts["m"] = "Lap 3"; }));
            var q2 = w.File(Ask(2, DevBadge, DriverAsk.Query, Tonight.AddMinutes(1), r => { r.Texts["race"] = w.Heat.Id; r.Texts["m"] = "Lap 5"; }));

            using var inbox = InboxWithUndo(w);
            inbox.Pick(inbox.Items.ToArray());
            Press(inbox, "Noted");

            Assert.False(w.Store.Get(q1.Id)!.IsWaiting);
            Assert.False(w.Store.Get(q2.Id)!.IsWaiting);

            Press(inbox, "UndoLast");

            Assert.True(w.Store.Get(q1.Id)!.IsWaiting);
            Assert.True(w.Store.Get(q2.Id)!.IsWaiting);

            return true;
        }, TimeSpan.FromSeconds(30), () => "undoing two Noted at once");
    }

    [Fact]
    public void A_pack_no_longer_asks_first_the_toast_has_undo_instead()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "DeskInboxForm.Requests.cs")).Replace("\r\n", "\n");
        var pack = source[source.IndexOf("case DriverAsk.Pack:", StringComparison.Ordinal)..source.IndexOf("// Race video (14 Nov 2026, V5, V6).", StringComparison.Ordinal)];

        Assert.DoesNotContain("AppDialog.Show(this, $\"{desk.Summary(one)} - for {desk.NameFor(one)}. Approve it?\"", pack, StringComparison.Ordinal);
        Assert.Contains("desk.ApprovePack(one, out refusal)", pack, StringComparison.Ordinal);
        Assert.Contains("UndoForSteps(StepsSince(before))", source, StringComparison.Ordinal);

        // Late, skip, a swap and hiding a video have no undo, and still ask.
        Assert.Contains("\"Marshal swap\", MessageBoxButtons.YesNo", source, StringComparison.Ordinal);
        Assert.Contains("DriverRequestDesk.KindWords(one.Kind), MessageBoxButtons.YesNo", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Rules_count_what_phones_changed_by_themselves_today()
    {
        var now = DateTime.Now;
        var all = new[]
        {
            new DriverRequest { Id = "1", Kind = DriverAsk.Car, State = DriverAsk.Approved, AnsweredUtc = now.ToUniversalTime() },
            new DriverRequest { Id = "2", Kind = DriverAsk.Setup, State = DriverAsk.Approved, AnsweredUtc = now.ToUniversalTime() },
            new DriverRequest { Id = "3", Kind = DriverAsk.Photo, State = DriverAsk.Approved, AnsweredUtc = now.AddDays(-2).ToUniversalTime() },
            new DriverRequest { Id = "4", Kind = DriverAsk.Details, State = DriverAsk.Approved, AnsweredUtc = now.ToUniversalTime() },
            new DriverRequest { Id = "5", Kind = DriverAsk.Cover, State = DriverAsk.Approved, AnsweredUtc = now.ToUniversalTime() },
            new DriverRequest { Id = "6", Kind = DriverAsk.Car, State = DriverAsk.Waiting },
        };

        Assert.Equal(2, DeskInboxForm.HandledToday(all, now));
        Assert.EndsWith("2 changes handled for you tonight.", DeskInboxForm.HandledWords(2), StringComparison.Ordinal);
        Assert.EndsWith("they never wait here.", DeskInboxForm.HandledWords(0), StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }
}
