using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026), finding 89: the Track Designer always opened
/// with "Show this plan as the track's main picture" ticked, so saving a
/// plan quietly made the drawn plan the main picture of a track whose club
/// had chosen its photo. The box now opens the way the caller says.
/// </summary>
public sealed class BugHunt10_RcpTrackEditorFormTests
{
    private static bool HasUnsavedChanges(RcpTrackEditorForm form) =>
        (bool)typeof(RcpTrackEditorForm)
            .GetMethod("HasUnsavedChanges", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, null)!;

    [Fact]
    public void Main_picture_box_opens_as_the_caller_says_and_that_is_not_a_change()
    {
        var seen = StaThread.Run(() =>
        {
            using var unticked = new RcpTrackEditorForm(null, "Club Hall", null, useAsPhoto: false);
            var untickedBox = unticked.UseAsPhoto;
            var untickedChanged = HasUnsavedChanges(unticked);

            using var byDefault = new RcpTrackEditorForm(null, "Club Hall", null);
            var defaultBox = byDefault.UseAsPhoto;
            var defaultChanged = HasUnsavedChanges(byDefault);

            return (untickedBox, untickedChanged, defaultBox, defaultChanged);
        }, TimeSpan.FromSeconds(30), () => "opening the Track Designer");

        Assert.False(seen.untickedBox);
        Assert.False(seen.untickedChanged);

        // Saying nothing keeps the box ticked, as before.
        Assert.True(seen.defaultBox);
        Assert.False(seen.defaultChanged);
    }
}
