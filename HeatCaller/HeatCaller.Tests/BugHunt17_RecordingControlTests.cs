using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, #141: "Write And Play It" refilled its picker on the press,
/// and when the driver (or championship) picked had gone from the list the
/// refill put the box on the first entry - which was then written, played
/// and saved without anybody having chosen it.
/// </summary>
public sealed class BugHunt17_RecordingControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static T FieldOf<T>(RecordingControl page, string name) =>
        (T)typeof(RecordingControl).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

    private static void Press(RecordingControl page, string handler) =>
        ((Task)typeof(RecordingControl).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(page, null)!).GetAwaiter().GetResult();

    [Fact]
    public void A_picked_driver_who_has_left_tonights_list_is_not_swapped_for_the_first_name()
    {
        var seen = StaThread.Run(() =>
        {
            using var page = new RecordingControl(new AppSettings());

            var tonight = new List<(string Transponder, string Name)>
            {
                ("1111", "Amy Lee"),
                ("1234", "Bill Hargrove"),
            };

            page.DriversTonight = () => tonight.ToList();

            var played = new List<string>();

            page.PlayADriversNight = (transponder, _) =>
            {
                played.Add(transponder);

                return Task.FromResult<string?>(null);
            };

            typeof(RecordingControl).GetMethod("FillDriversTonight", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
            FieldOf<ComboBox>(page, "_cmbDriverCut").SelectedIndex = 1;

            // Bill's only race tonight is deleted; the picker is not opened again.
            tonight.RemoveAt(1);

            Press(page, "PlayADriversNightAsync");

            var afterGone = (count: played.Count, label: FieldOf<Label>(page, "_lblDriverCut").Text);

            // The box now shows Amy, and a second press is a choice of her.
            Press(page, "PlayADriversNightAsync");

            return (afterGone, played);
        }, TimeSpan.FromMinutes(2), () => "playing a driver's night for a driver who has gone");

        Assert.Equal(0, seen.afterGone.count);
        Assert.Contains("not in tonight's list", seen.afterGone.label, StringComparison.Ordinal);
        Assert.Equal(new[] { "1111" }, seen.played);
    }

    [Fact]
    public void A_picked_championship_that_has_been_deleted_is_not_swapped_for_the_first_one()
    {
        var seen = StaThread.Run(() =>
        {
            using var page = new RecordingControl(new AppSettings());

            var seasons = new List<(string Id, string Name)>
            {
                ("s1", "Winter Series"),
                ("s2", "Summer Series"),
            };

            page.Seasons = () => seasons.ToList();

            var played = new List<string>();

            page.PlayTheSeason = id =>
            {
                played.Add(id);

                return Task.FromResult<string?>(null);
            };

            typeof(RecordingControl).GetMethod("FillSeasons", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, null);
            FieldOf<ComboBox>(page, "_cmbSeason").SelectedIndex = 1;

            seasons.RemoveAt(1);

            Press(page, "PlayTheSeasonAsync");

            var afterGone = (count: played.Count, label: FieldOf<Label>(page, "_lblSeason").Text);

            Press(page, "PlayTheSeasonAsync");

            return (afterGone, played);
        }, TimeSpan.FromMinutes(2), () => "playing a season that has been deleted");

        Assert.Equal(0, seen.afterGone.count);
        Assert.Contains("not in the list", seen.afterGone.label, StringComparison.Ordinal);
        Assert.Equal(new[] { "s1" }, seen.played);
    }
}
