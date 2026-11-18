using System.Reflection;
using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// The Stream overlays editor as the Studio (17 Nov 2026,
/// plans/overlay-editor-mockups.html, option A): a preset that copies itself
/// on its first change, the overlay page's drag messages saved as places,
/// the address the live picture opens, and the page's edit and hold hooks.
/// </summary>
public sealed class OverlayStudioTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static string NewPath() => Path.Combine(Path.GetTempPath(), "hc-studio-overlays-" + Guid.NewGuid().ToString("N")[..8] + ".json");

    private static void Edit(OverlayEditorForm form, Action<OverlayLayout> change) =>
        typeof(OverlayEditorForm).GetMethod("Edit", Private)!.Invoke(form, new object[] { change });

    private static void Undo(OverlayEditorForm form) => typeof(OverlayEditorForm).GetMethod("UndoLast", Private)!.Invoke(form, null);

    private static void Message(OverlayEditorForm form, string json)
    {
        using var doc = JsonDocument.Parse(json);
        typeof(OverlayEditorForm).GetMethod("OnPictureMessage", Private)!.Invoke(form, new object[] { doc.RootElement.Clone() });
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // A test machine's temp folder.
        }
    }

    [Fact]
    public void A_presets_first_change_goes_into_a_copy_and_undo_takes_the_copy_away()
    {
        var path = NewPath();

        try
        {
            var store = OverlayStore.Load(path);
            using var form = new OverlayEditorForm(store, id => "http://localhost:8080/overlay/" + id, OverlayLayouts.AutoId);

            Edit(form, l => l.Glass = 0.6);

            var copy = Assert.Single(store.Layouts);
            Assert.Equal("Race night (auto) (our copy)", copy.Name);
            Assert.Equal(0.6, copy.Glass, 3);

            // The preset itself never changes.
            Assert.Equal(0.82, OverlayLayouts.BuiltIn(OverlayLayouts.AutoId)!.Glass, 3);

            // The change first, then the copy.
            Undo(form);
            Assert.Equal(0.82, Assert.Single(store.Layouts).Glass, 3);
            Undo(form);
            Assert.Empty(store.Layouts);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void A_drag_on_the_picture_is_saved_as_the_place_it_was_dropped()
    {
        var path = NewPath();

        try
        {
            var store = OverlayStore.Load(path);
            var mine = store.AddCopy(OverlayLayouts.BuiltIn(OverlayLayouts.BroadcastId)!, "Friday stream");
            using var form = new OverlayEditorForm(store, id => "http://localhost:8080/overlay/" + id, mine.Id);

            Message(form, """{"t":"place","k":"battle","anchor":"TopRight","x":4.5,"y":12,"scale":1.25}""");

            var battle = store.Get(mine.Id)!.Element(OverlayElementKind.Battle)!;
            Assert.Equal(OverlayAnchor.TopRight, battle.Anchor);
            Assert.Equal(4.5, battle.X);
            Assert.Equal(12, battle.Y);
            Assert.Equal(1.25, battle.Scale);

            // Nonsense from the page changes nothing.
            Message(form, """{"t":"place","k":"battle","anchor":"Default","x":1,"y":1,"scale":1}""");
            Message(form, """{"t":"place","k":"nothing","anchor":"Top","x":1,"y":1,"scale":1}""");
            Message(form, """{"t":"place","k":"battle","anchor":"Top","x":"far","y":1,"scale":1}""");
            Assert.Equal(OverlayAnchor.TopRight, store.Get(mine.Id)!.Element(OverlayElementKind.Battle)!.Anchor);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void The_when_it_shows_pills_switch_auto_and_parts_and_never_leave_none()
    {
        var path = NewPath();

        try
        {
            var store = OverlayStore.Load(path);
            var mine = store.AddCopy(OverlayLayouts.BuiltIn(OverlayLayouts.BroadcastId)!, "Friday stream");
            using var form = new OverlayEditorForm(store, id => "http://localhost:8080/overlay/" + id, mine.Id);
            OverlayDuring Battle() => store.Get(mine.Id)!.Element(OverlayElementKind.Battle)!.During;

            typeof(OverlayEditorForm).GetField("_kind", Private)!.SetValue(form, OverlayElementKind.Battle);
            Assert.Equal(OverlayDuring.Always, Battle());

            // A part clicked from Auto: out of Auto, the parts Auto had, that one changed.
            form.ShowsPillClicked("finished");
            Assert.Equal(OverlayDuring.Racing | OverlayDuring.Finished, Battle());

            form.ShowsPillClicked("finished");
            Assert.Equal(OverlayDuring.Racing, Battle());

            // The last part stays: none would mean "always".
            form.ShowsPillClicked("racing");
            Assert.Equal(OverlayDuring.Racing, Battle());

            form.ShowsPillClicked("auto");
            Assert.Equal(OverlayDuring.Always, Battle());

            // Auto off again: the parts it was showing in.
            form.ShowsPillClicked("auto");
            Assert.Equal(OverlayEditorForm.NaturalParts(OverlayElementKind.Battle), Battle());
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void The_picture_opens_the_layout_in_edit_mode_held_at_the_part_picked()
    {
        var path = NewPath();

        try
        {
            var store = OverlayStore.Load(path);
            using var form = new OverlayEditorForm(store, id => "http://localhost:8080/overlay/" + id, OverlayLayouts.AutoId);

            Assert.Equal("http://localhost:8080/overlay/auto?demo=1&hold=racing&at=95&edit=1&sel=tower", form.PictureAddress());

            // The server stopped: no address, so the picture says so.
            using var stopped = new OverlayEditorForm(store, _ => string.Empty, OverlayLayouts.AutoId);
            Assert.Equal(string.Empty, stopped.PictureAddress());

            // A club with none of its own starts on Pick a look.
            using var first = new OverlayEditorForm(store, id => "http://localhost:8080/overlay/" + id);
            Assert.Equal("http://localhost:8080" + WebServerService.OverlayGalleryPath, first.PictureAddress());
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void A_graphic_at_its_usual_place_moves_from_the_place_it_is_in()
    {
        foreach (var kind in Enum.GetValues<OverlayElementKind>())
        {
            var (anchor, x, y) = OverlayEditorForm.UsualPlace(kind);
            Assert.NotEqual(OverlayAnchor.Default, anchor);
            Assert.InRange(x, 0, 10);
            Assert.InRange(y, 0, 10);
            Assert.NotEqual((OverlayDuring)0, OverlayEditorForm.NaturalParts(kind));
        }
    }

    [Fact]
    public void Overlaps_the_page_measured_are_said_in_the_clubs_words()
    {
        using var doc = JsonDocument.Parse("""{"t":"clash","pairs":[["battle","fastest"],["tower","nobody"]]}""");
        Assert.Equal("Covering each other now: Battle box and Fastest lap.", OverlayEditorForm.ClashWords(doc.RootElement));

        using var none = JsonDocument.Parse("""{"t":"clash","pairs":[]}""");
        Assert.Equal(string.Empty, OverlayEditorForm.ClashWords(none.RootElement));
    }

    [Fact]
    public void The_overlay_page_carries_the_edit_layer_and_the_held_demo()
    {
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings());
        var page = server.BuildBroadcastPage(OverlayLayouts.BuiltIn(OverlayLayouts.AutoId));

        Assert.Contains("const DEMO_HOLD", page);
        Assert.Contains("params.get('edit') !== '1'", page);
        Assert.Contains("id = 'hc-edit'", page);
        // The layout's own size for each graphic, for the resize handle.
        Assert.Contains("\"scale\":", page);
    }

    [Fact]
    public void Pick_a_look_shows_every_layout_with_its_name_made_safe()
    {
        var server = new WebServerService(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings());
        var page = server.BuildOverlayGalleryPage(new[]
        {
            ("auto", "Race night (auto)", "One source", true),
            ("mine", "<b>Friday</b>", "Ours", false),
        });

        Assert.Contains("/overlay/auto?demo=1&hold=racing", page);
        Assert.Contains("&lt;b&gt;Friday&lt;/b&gt;", page);
        Assert.DoesNotContain("<b>Friday</b>", page);
        Assert.Contains("Your own", page);
    }
}
