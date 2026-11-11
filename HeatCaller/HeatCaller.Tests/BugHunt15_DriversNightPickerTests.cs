using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15, B29: the Recording page's driver's-night picker kept a
/// renamed driver's old name. It only rebuilt when the transponders changed,
/// so a name corrected on the Drivers page mid-evening was still the one the
/// piece was written about and its file saved under.
/// </summary>
public sealed class BugHunt15_DriversNightPickerTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_driver_renamed_with_the_same_transponder_shows_and_plays_under_the_new_name()
    {
        var seen = StaThread.Run(() =>
        {
            using var page = new RecordingControl(new AppSettings());

            var tonight = new List<(string Transponder, string Name)>
            {
                ("1111", "Amy Lee"),
                ("1234", "Jon Smith"),
            };

            page.DriversTonight = () => tonight.ToList();

            var fill = typeof(RecordingControl).GetMethod("FillDriversTonight", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var combo = (ComboBox)typeof(RecordingControl).GetField("_cmbDriverCut", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            var cached = (List<(string Transponder, string Name)>)typeof(RecordingControl)
                .GetField("_driversTonight", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

            fill.Invoke(page, null);
            combo.SelectedIndex = 1;

            // Corrected on the Drivers page: same transponder, same order.
            tonight[1] = ("1234", "John Smith");

            fill.Invoke(page, null);

            return (item: (string)combo.Items[1]!, name: cached[combo.SelectedIndex].Name, picked: combo.SelectedIndex);
        }, TimeSpan.FromSeconds(30), () => "filling the driver's-night picker");

        Assert.Equal("John Smith", seen.item);
        Assert.Equal("John Smith", seen.name);

        // Still on the driver who was picked, found again by transponder.
        Assert.Equal(1, seen.picked);
    }

    [Fact]
    public void Pressing_play_without_opening_the_picker_still_sends_the_new_name()
    {
        // (10 Nov 2026, bug hunt 16) The button read the cached list as it
        // was when the picker last opened, so a rename made since was lost.
        var sent = StaThread.Run(() =>
        {
            using var page = new RecordingControl(new AppSettings());

            var tonight = new List<(string Transponder, string Name)>
            {
                ("1111", "Amy Lee"),
                ("1234", "Jon Smiht"),
            };

            page.DriversTonight = () => tonight.ToList();

            (string Transponder, string Name)? got = null;

            page.PlayADriversNight = (transponder, name) =>
            {
                got = (transponder, name);

                return Task.FromResult<string?>(null);
            };

            var fill = typeof(RecordingControl).GetMethod("FillDriversTonight", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var play = typeof(RecordingControl).GetMethod("PlayADriversNightAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var combo = (ComboBox)typeof(RecordingControl).GetField("_cmbDriverCut", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;

            fill.Invoke(page, null);
            combo.SelectedIndex = 1;

            // Corrected on the Drivers page; the picker is not opened again.
            tonight[1] = ("1234", "Jon Smith");

            ((Task)play.Invoke(page, null)!).GetAwaiter().GetResult();

            return got;
        }, TimeSpan.FromMinutes(2), () => "playing a driver's night");

        Assert.Equal(("1234", "Jon Smith"), sent);
    }
}
