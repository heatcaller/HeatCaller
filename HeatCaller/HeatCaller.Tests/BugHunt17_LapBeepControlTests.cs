using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, Sounds > Lap Beeps. A cue is kept by transponder and stays
/// when its driver is deleted or changes transponder. The line under the
/// list counted every cue kept, so it read "3 of 2 drivers have a cue" and
/// warned of a share with somebody who is not in the list. It now counts
/// the cues of the drivers listed.
/// </summary>
public sealed class BugHunt17_LapBeepControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_cue_left_behind_by_a_driver_who_has_gone_is_not_counted()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam Smith" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Bill Jones" });

        var cues = LapBeepStore.Load();
        cues.Upsert(new DriverLapBeep { Transponder = "101", ToneIndex = 0, Enabled = true });
        cues.Upsert(new DriverLapBeep { Transponder = "102", ToneIndex = 1, Enabled = true });

        // Nobody's any more, and on the same cue as Sam.
        cues.Upsert(new DriverLapBeep { Transponder = "999", ToneIndex = 0, Enabled = true });

        var status = typeof(LapBeepControl).GetField("_lblStatus", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var text = StaThread.Run(() =>
        {
            using var page = new LapBeepControl(cues, profiles, SoundEffectsStore.Load(), new UndoService());
            _ = page.Handle;

            return ((Label)status.GetValue(page)!).Text;
        }, TimeSpan.FromSeconds(30), () => "building the Lap Beeps page");

        Assert.Equal("2 of 2 drivers have a cue.", text);
    }
}
