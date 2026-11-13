using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, three on the Ambience page.
///
/// Saving the layer editor for an "Always on" layer wrote the constant level
/// over all five tuned levels: the sliders opened at it, and Save writes the
/// sliders back.
///
/// Stop Preview put back the mood from when Preview was pressed although the
/// race had since gone green, dropping the crowd to staging level - or, for a
/// Preview pressed in the finish celebration, left it at Peak for good.
///
/// A session started while the layer editor was open, before any slider was
/// dragged, had its crowd soloed by the next drag and left that way on close.
/// </summary>
public sealed class BugHunt17_AmbienceControlTests : IDisposable
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

    // ------------------------------------------------ the layer editor's Save

    [Fact]
    public void Saving_an_always_on_layer_keeps_its_tuned_levels()
    {
        var saved = StaThread.Run(() =>
        {
            var layer = new AmbienceLayer
            {
                Name = "Crowd",
                AlwaysOn = true,
                ConstantVolume = 0.35f,
                IdleVolume = 0.10f,
                AnticipationVolume = 0.20f,
                RacingVolume = 0.50f,
                ExcitedVolume = 0.80f,
                PeakVolume = 1.0f,
            };

            using var form = new AmbienceLayerForm(layer);

            // Opened and saved with nothing changed.
            Call(form, "BtnSave_Click", null, EventArgs.Empty);

            return form.Result;
        }, TimeSpan.FromSeconds(30), () => "saving an always-on layer");

        Assert.True(saved.AlwaysOn);
        Assert.Equal(0.35f, saved.ConstantVolume, 2);
        Assert.Equal(0.10f, saved.IdleVolume, 2);
        Assert.Equal(0.20f, saved.AnticipationVolume, 2);
        Assert.Equal(0.50f, saved.RacingVolume, 2);
        Assert.Equal(0.80f, saved.ExcitedVolume, 2);
        Assert.Equal(1.0f, saved.PeakVolume, 2);
    }

    // ------------------------------------------------ the page's Preview

    /// <summary>
    /// Preview pressed with the crowd already playing at <paramref name="before"/>,
    /// then <paramref name="meanwhile"/>, then Stop Preview: the mood left.
    /// </summary>
    private AmbienceService.Intensity MoodAfterPreview(AmbienceService.Intensity before, Action<AmbienceService> meanwhile)
    {
        var store = AmbienceStore.Load();

        return StaThread.Run(() =>
        {
            using var crowd = new AmbienceService();
            using var page = new AmbienceControl(store, crowd, new UndoService());
            using var button = new Button();

            // Running with no bed, as BugHunt15_AmbienceTests does: the mood
            // bookkeeping is what is under test.
            FieldOf(crowd, "_running").SetValue(crowd, true);
            crowd.SetIntensity(before);

            Call(page, "TogglePreview", button);
            var previewed = crowd.CurrentIntensity;

            meanwhile(crowd);

            Call(page, "TogglePreview", button);
            var after = crowd.CurrentIntensity;

            FieldOf(crowd, "_running").SetValue(crowd, false);

            Assert.Equal(AmbienceService.Intensity.Racing, previewed);

            return after;
        }, TimeSpan.FromSeconds(30), () => "previewing the crowd");
    }

    [Fact]
    public void Stop_preview_puts_the_mood_back_when_nothing_else_has_asked_for_one()
    {
        Assert.Equal(AmbienceService.Intensity.Anticipation,
            MoodAfterPreview(AmbienceService.Intensity.Anticipation, _ => { }));
    }

    [Fact]
    public void Stop_preview_after_the_green_leaves_the_race_its_mood()
    {
        // Pressed while staging; the green then asks for Racing, which is
        // what the preview had already set.
        Assert.Equal(AmbienceService.Intensity.Racing,
            MoodAfterPreview(AmbienceService.Intensity.Anticipation,
                crowd => crowd.SetIntensity(AmbienceService.Intensity.Racing)));
    }

    [Fact]
    public void Stop_preview_does_not_put_a_finish_celebration_back_as_peak()
    {
        var store = AmbienceStore.Load();

        var after = StaThread.Run(() =>
        {
            using var crowd = new AmbienceService();
            using var page = new AmbienceControl(store, crowd, new UndoService());
            using var button = new Button();

            FieldOf(crowd, "_running").SetValue(crowd, true);
            crowd.CelebrateFinishThenSettle(30);

            Call(page, "TogglePreview", button);
            Call(page, "TogglePreview", button);
            var mood = crowd.CurrentIntensity;

            FieldOf(crowd, "_running").SetValue(crowd, false);

            return mood;
        }, TimeSpan.FromSeconds(30), () => "previewing in the finish celebration");

        Assert.Equal(AmbienceService.Intensity.Idle, after);
    }

    [Fact]
    public void A_mood_asked_for_again_is_still_counted()
    {
        using var crowd = new AmbienceService();

        crowd.SetIntensity(AmbienceService.Intensity.Racing);
        var asked = crowd.MoodsAsked;

        crowd.SetIntensity(AmbienceService.Intensity.Racing);

        Assert.Equal(asked + 1, crowd.MoodsAsked);
    }

    // ------------------------------------------------ the layer editor's close

    [Fact]
    public void Closing_the_editor_puts_the_mix_back_for_a_crowd_started_while_it_was_open()
    {
        var store = AmbienceStore.Load();

        // Nothing saved to play, so the restart opens no device: it stops
        // the bed and finds nothing to open again.
        store.Current.Layers.Clear();

        var runningAfter = StaThread.Run(() =>
        {
            using var crowd = new AmbienceService();

            // Opened between races with the crowd stopped.
            using var form = new AmbienceLayerForm(null, crowd, store);

            // A session starts the crowd from a phone, and a slider dragged
            // after that solos this layer on it.
            FieldOf(crowd, "_running").SetValue(crowd, true);
            FieldOf(form, "_previewTouched").SetValue(form, true);

            Call(form, "EndPreview");

            var running = crowd.IsRunning;
            FieldOf(crowd, "_running").SetValue(crowd, false);

            return running;
        }, TimeSpan.FromSeconds(30), () => "closing the layer editor");

        // Restarted from the saved settings - it used to be left as it was,
        // one layer playing and the rest muted.
        Assert.False(runningAfter);
    }
}
