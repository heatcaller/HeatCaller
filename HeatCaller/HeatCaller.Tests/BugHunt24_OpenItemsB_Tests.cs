using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 open items (24 Nov 2026), #218: in the Stream overlays editor,
/// nudging the championship box by a number must keep it under the pylon,
/// where the page really draws it - not jump it to the pylon's own corner.
/// </summary>
public sealed class BugHunt24_OpenItemsB_Tests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static string NewPath() => Path.Combine(Path.GetTempPath(), "hc-hunt24b-overlays-" + Guid.NewGuid().ToString("N")[..8] + ".json");

    private static NumericUpDown Box(OverlayEditorForm form, string name) =>
        (NumericUpDown)typeof(OverlayEditorForm).GetField(name, Private)!.GetValue(form)!;

    private static void Pick(OverlayEditorForm form, OverlayElementKind kind)
    {
        typeof(OverlayEditorForm).GetField("_kind", Private)!.SetValue(form, kind);
        typeof(OverlayEditorForm).GetMethod("ShowElement", Private)!.Invoke(form, null);
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
    public void The_editor_and_the_overlap_check_agree_where_every_graphic_usually_sits()
    {
        foreach (var kind in Enum.GetValues<OverlayElementKind>())
        {
            Assert.Equal(OverlayOverlap.UsualPlace(kind), OverlayEditorForm.UsualPlace(kind));
        }

        Assert.Equal((OverlayAnchor.TopLeft, 2.0, 28.0), OverlayEditorForm.UsualPlace(OverlayElementKind.Champ));
        Assert.NotEqual(OverlayEditorForm.UsualPlace(OverlayElementKind.Tower), OverlayEditorForm.UsualPlace(OverlayElementKind.Champ));
    }

    [Fact]
    public void The_championship_box_resized_by_a_number_stays_under_the_pylon()
    {
        var path = NewPath();

        try
        {
            var store = OverlayStore.Load(path);
            using var form = new OverlayEditorForm(store, id => "http://localhost:8080/overlay/" + id, OverlayLayouts.BroadcastId);
            Pick(form, OverlayElementKind.Champ);

            Assert.Equal(28m, Box(form, "_numY").Value);

            Box(form, "_numScale").Value = 120;

            var copy = Assert.Single(store.Layouts);
            var champ = copy.Element(OverlayElementKind.Champ)!;
            Assert.Equal(OverlayAnchor.TopLeft, champ.Anchor);
            Assert.Equal(2, champ.X, 3);
            Assert.Equal(28, champ.Y, 3);
            Assert.Equal(1.2, champ.Scale, 3);

            var tower = OverlayOverlap.BoxOf(copy.Element(OverlayElementKind.Tower)!);
            Assert.True(OverlayOverlap.BoxOf(champ).T >= tower.B, "The championship box should start below the pylon, not on it.");
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void The_championship_box_nudged_across_keeps_its_place_under_the_pylon()
    {
        var path = NewPath();

        try
        {
            var store = OverlayStore.Load(path);
            using var form = new OverlayEditorForm(store, id => "http://localhost:8080/overlay/" + id, OverlayLayouts.BroadcastId);
            Pick(form, OverlayElementKind.Champ);

            Box(form, "_numX").Value = 3;

            var champ = Assert.Single(store.Layouts).Element(OverlayElementKind.Champ)!;
            Assert.Equal(OverlayAnchor.TopLeft, champ.Anchor);
            Assert.Equal(3, champ.X, 3);
            Assert.Equal(28, champ.Y, 3);
        }
        finally
        {
            Delete(path);
        }
    }
}
