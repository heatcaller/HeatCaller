using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the Stream overlays editor's number boxes on a
/// preset. The first number changed on a preset makes the club's copy, and
/// that copy must hold the number typed - not the preset's old one.
/// </summary>
public sealed class BugHunt24_OverlayEditorFormTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private static string NewPath() => Path.Combine(Path.GetTempPath(), "hc-hunt24-overlays-" + Guid.NewGuid().ToString("N")[..8] + ".json");

    private static NumericUpDown Box(OverlayEditorForm form, string name) =>
        (NumericUpDown)typeof(OverlayEditorForm).GetField(name, Private)!.GetValue(form)!;

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
    public void A_presets_first_boxes_number_lands_in_the_copy()
    {
        var path = NewPath();

        try
        {
            var store = OverlayStore.Load(path);
            using var form = new OverlayEditorForm(store, id => "http://localhost:8080/overlay/" + id, OverlayLayouts.AutoId);

            var glass = Box(form, "_numGlass");
            Assert.Equal(82m, glass.Value);

            glass.Value = 60;

            var copy = Assert.Single(store.Layouts);
            Assert.Equal("Race night (auto) (our copy)", copy.Name);
            Assert.Equal(0.6, copy.Glass, 3);
            Assert.Equal(60m, glass.Value);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void A_presets_first_size_change_lands_on_the_graphic_in_the_copy()
    {
        var path = NewPath();

        try
        {
            var store = OverlayStore.Load(path);
            using var form = new OverlayEditorForm(store, id => "http://localhost:8080/overlay/" + id, OverlayLayouts.AutoId);
            typeof(OverlayEditorForm).GetField("_kind", Private)!.SetValue(form, OverlayElementKind.Tower);

            var scale = Box(form, "_numScale");
            Assert.NotEqual(120m, scale.Value);

            scale.Value = 120;

            var copy = Assert.Single(store.Layouts);
            Assert.Equal(1.2, copy.Element(OverlayElementKind.Tower)!.Scale, 3);
        }
        finally
        {
            Delete(path);
        }
    }
}
