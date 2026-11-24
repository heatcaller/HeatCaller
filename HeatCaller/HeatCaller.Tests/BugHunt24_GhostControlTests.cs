using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), two on the Ghost Cars page.
///
/// "Edit..." with no ghost picked handed null on to EditGhost, which is the
/// Add path: the Add Ghost Car window opened and Save put a new ghost in the
/// garage.
///
/// The line under the list counted only each ghost's own ON switch, so with
/// "Enable ghost cars" unticked it still said "2 of 2 ghosts will run." - and
/// ticking or unticking the box did not redraw it at all.
/// </summary>
public sealed class BugHunt24_GhostControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static T OnSta<T>(Func<T> work) =>
        StaThread.Run(work, TimeSpan.FromMinutes(1), () => "building the Ghost Cars page");

    private static object? Call(object owner, string name)
    {
        var method = owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(owner.GetType().Name, name);

        try
        {
            return method.Invoke(owner, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static T FieldOf<T>(object owner, string name) =>
        (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(owner.GetType().Name, name)).GetValue(owner)!;

    private static GhostStore StoreWith(bool racingOn, int ghosts)
    {
        var store = GhostStore.Load();
        store.Current.Enabled = racingOn;

        for (var i = 0; i < ghosts; i++)
        {
            store.Current.Ghosts.Add(new GhostCar { Name = $"Ghost {i + 1}", Enabled = true });
        }

        return store;
    }

    private static GhostControl Page(GhostStore store) => new(
        store,
        ProfileStore.Load(),
        SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()),
        new UndoService());

    // ------------------------------------------------------------------
    //  Edit... with nothing picked
    // ------------------------------------------------------------------

    [Fact]
    public void BugHunt24_Edit_with_no_ghost_picked_does_nothing_rather_than_adding_one()
    {
        var store = StoreWith(racingOn: true, ghosts: 1);

        var (opened, count) = OnSta(() =>
        {
            using var page = Page(store);

            // Nothing picked: a freshly opened page selects nothing.
            var didOpen = (bool)Call(page, "EditPicked")!;
            return (didOpen, store.Current.Ghosts.Count);
        });

        Assert.False(opened);
        Assert.Equal(1, count);
    }

    [Fact]
    public void BugHunt24_Edit_with_an_empty_garage_does_nothing()
    {
        var store = StoreWith(racingOn: true, ghosts: 0);

        var (opened, count) = OnSta(() =>
        {
            using var page = Page(store);
            var didOpen = (bool)Call(page, "EditPicked")!;
            return (didOpen, store.Current.Ghosts.Count);
        });

        Assert.False(opened);
        Assert.Equal(0, count);
    }

    // ------------------------------------------------------------------
    //  The line under the list
    // ------------------------------------------------------------------

    [Fact]
    public void BugHunt24_Status_line_with_ghost_racing_off_says_none_will_run()
    {
        var line = GhostControl.StatusLine(total: 2, on: 2, racingOn: false);

        Assert.DoesNotContain("2 of 2", line);
        Assert.Contains("off", line);
        Assert.Contains("no ghosts will run", line);
    }

    [Fact]
    public void BugHunt24_Status_line_with_ghost_racing_on_counts_the_ghosts_marked_on()
    {
        Assert.Equal("1 of 2 ghosts will run.", GhostControl.StatusLine(total: 2, on: 1, racingOn: true));
        Assert.Equal(
            "No ghosts yet - press Quick Rival for the fastest way to get one.",
            GhostControl.StatusLine(total: 0, on: 0, racingOn: false));
    }

    [Fact]
    public void BugHunt24_Page_opened_with_ghost_racing_off_does_not_say_ghosts_will_run()
    {
        var store = StoreWith(racingOn: false, ghosts: 2);

        var text = OnSta(() =>
        {
            using var page = Page(store);
            return FieldOf<Label>(page, "_lblStatus").Text;
        });

        Assert.DoesNotContain("2 of 2", text);
        Assert.Contains("no ghosts will run", text);
    }

    [Fact]
    public void BugHunt24_Unticking_enable_ghost_cars_redraws_the_line_at_once()
    {
        var store = StoreWith(racingOn: true, ghosts: 2);

        var (before, after, backOn) = OnSta(() =>
        {
            using var page = Page(store);
            var label = FieldOf<Label>(page, "_lblStatus");
            var box = FieldOf<CheckBox>(page, "_chkEnabled");

            var first = label.Text;
            box.Checked = false;
            var second = label.Text;
            box.Checked = true;
            return (first, second, label.Text);
        });

        Assert.Equal("2 of 2 ghosts will run.", before);
        Assert.Contains("no ghosts will run", after);
        Assert.Equal("2 of 2 ghosts will run.", backOn);
    }
}
