using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the Ambience page's Preview.
///
/// A preview whose crowd was stopped some other way - the Enable ambience
/// switch, a restart with nothing left to play, Race Control's Reset - left
/// the page still owning it. The button kept saying "Stop Preview", and
/// pressed mid-race once a race had opened a crowd of its own, it stopped the
/// race's crowd.
/// </summary>
public sealed class BugHunt24_AmbienceControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static object? Call(object owner, string name, params object?[] args)
    {
        var method = owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(owner.GetType().Name, name);

        try
        {
            return method.Invoke(owner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static FieldInfo FieldOf(object owner, string name) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(owner.GetType().Name, name);

    /// <summary>Marks the page as previewing a crowd it started itself, as Preview pressed with nothing playing leaves it.</summary>
    private static void PreviewStartedByThePage(AmbienceControl page)
    {
        FieldOf(page, "_pagePreviewing").SetValue(page, true);
        FieldOf(page, "_pagePreviewStartedAmbience").SetValue(page, true);
    }

    /// <summary>One tick of the page's monitor timer, as the window's message loop would give it.</summary>
    private static void Tick(AmbienceControl page)
    {
        var timer = (System.Windows.Forms.Timer)FieldOf(page, "_monitorTimer").GetValue(page)!;
        var onTick = typeof(System.Windows.Forms.Timer).GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException(nameof(System.Windows.Forms.Timer), "OnTick");
        onTick.Invoke(timer, new object[] { EventArgs.Empty });
    }

    [Fact]
    public void Stop_preview_after_a_reset_does_not_stop_the_next_races_crowd()
    {
        var store = AmbienceStore.Load();

        var runningAfterPress = StaThread.Run(() =>
        {
            using var crowd = new AmbienceService();
            using var page = new AmbienceControl(store, crowd, new UndoService());
            using var button = new Button();

            // Running with no bed, as BugHunt17_AmbienceControlTests does.
            FieldOf(crowd, "_running").SetValue(crowd, true);
            Call(page, "TogglePreview", button);
            PreviewStartedByThePage(page);

            // Race Control's Reset stops the crowd; the page's timer ticks.
            FieldOf(crowd, "_running").SetValue(crowd, false);
            Tick(page);

            // A race staged without On Air opens a crowd of its own, and the
            // director presses the page's button mid-race.
            FieldOf(crowd, "_running").SetValue(crowd, true);
            Call(page, "TogglePreview", button);

            var running = crowd.IsRunning;
            FieldOf(crowd, "_running").SetValue(crowd, false);
            return running;
        }, TimeSpan.FromSeconds(30), () => "pressing Preview after a reset");

        // It used to stop the race's crowd for the rest of the race.
        Assert.True(runningAfterPress);
    }

    [Fact]
    public void Turning_ambience_off_ends_the_preview()
    {
        var store = AmbienceStore.Load();
        store.Current.Enabled = true;

        var (previewing, text) = StaThread.Run(() =>
        {
            using var crowd = new AmbienceService();
            using var page = new AmbienceControl(store, crowd, new UndoService());

            FieldOf(crowd, "_running").SetValue(crowd, true);
            PreviewStartedByThePage(page);
            var button = (Button)FieldOf(page, "_btnPreview").GetValue(page)!;
            button.Text = "Stop Preview";

            var enabled = (CheckBox)FieldOf(page, "_chkEnabled").GetValue(page)!;
            enabled.Checked = false;

            var stillPreviewing = (bool)FieldOf(page, "_pagePreviewing").GetValue(page)!;
            FieldOf(crowd, "_running").SetValue(crowd, false);
            return (stillPreviewing, button.Text);
        }, TimeSpan.FromSeconds(30), () => "turning ambience off during a preview");

        // It used to keep saying "Stop Preview" with nothing playing.
        Assert.False(previewing);
        Assert.Equal("Preview", text);
    }

    [Fact]
    public void A_restart_with_nothing_left_to_play_ends_the_preview()
    {
        var store = AmbienceStore.Load();

        // Nothing saved to play, so the restart stops the bed and opens none
        // - as deleting the only playable layer does.
        store.Current.Layers.Clear();

        var previewing = StaThread.Run(() =>
        {
            using var crowd = new AmbienceService();
            using var page = new AmbienceControl(store, crowd, new UndoService());

            FieldOf(crowd, "_running").SetValue(crowd, true);
            PreviewStartedByThePage(page);

            Call(page, "ApplyRunState", false);

            var stillPreviewing = (bool)FieldOf(page, "_pagePreviewing").GetValue(page)!;
            FieldOf(crowd, "_running").SetValue(crowd, false);
            return stillPreviewing;
        }, TimeSpan.FromSeconds(30), () => "deleting the last playable layer during a preview");

        Assert.False(previewing);
    }
}
