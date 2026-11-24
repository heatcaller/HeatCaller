using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, Sounds > Lap Beeps. The list and Auto-Assign All read every
/// raceable profile, ghost cars included, so a ghost was listed, counted as a
/// driver and handed a lap cue of its own. The page now lists, counts and
/// assigns real drivers only.
/// </summary>
public sealed class BugHunt24_LapBeepControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static ProfileStore DriversAndGhosts(int drivers, int ghosts)
    {
        var profiles = ProfileStore.Load();

        for (var i = 0; i < drivers; i++)
        {
            profiles.Upsert(new DriverProfile { Transponder = (101 + i).ToString(), DriverName = $"Driver {(char)('A' + i)}" });
        }

        for (var i = 0; i < ghosts; i++)
        {
            // Named to sort among the drivers, as a ghost's name can.
            profiles.Upsert(new DriverProfile { Transponder = $"{Ghosts.Prefix}{i + 1}", DriverName = $"Driver B ghost {i + 1}" });
        }

        return profiles;
    }

    [Fact]
    public void BugHunt24_LapBeeps_cue_drivers_leave_ghost_cars_out()
    {
        var profiles = DriversAndGhosts(3, 2);

        var listed = LapBeepControl.CueDrivers(profiles);

        Assert.Equal(3, listed.Count);
        Assert.DoesNotContain(listed, p => GhostService.IsGhost(p.Transponder));
    }

    [Fact]
    public void BugHunt24_LapBeeps_ghost_cars_are_not_counted_as_drivers()
    {
        var profiles = DriversAndGhosts(7, 2);
        var cues = LapBeepStore.Load();

        var status = typeof(LapBeepControl).GetField("_lblStatus", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var text = StaThread.Run(() =>
        {
            using var page = new LapBeepControl(cues, profiles, SoundEffectsStore.Load(), new UndoService());
            _ = page.Handle;

            return ((Label)status.GetValue(page)!).Text;
        }, TimeSpan.FromSeconds(30), () => "building the Lap Beeps page");

        Assert.Equal("0 of 7 drivers have a cue.", text);
    }

    [Fact]
    public void BugHunt24_LapBeeps_auto_assign_all_gives_no_cue_to_a_ghost()
    {
        var profiles = DriversAndGhosts(3, 1);
        var cues = LapBeepStore.Load();
        var ghost = $"{Ghosts.Prefix}1";

        var autoAssign = typeof(LapBeepControl).GetMethod("AutoAssign", BindingFlags.Instance | BindingFlags.NonPublic)!;

        StaThread.Run(() =>
        {
            using var page = new LapBeepControl(cues, profiles, SoundEffectsStore.Load(), new UndoService());
            _ = page.Handle;
            autoAssign.Invoke(page, null);
            return true;
        }, TimeSpan.FromSeconds(30), () => "auto-assigning lap cues");

        Assert.Null(cues.Get(ghost));
        Assert.NotNull(cues.Get("101"));
        Assert.NotNull(cues.Get("102"));
        Assert.NotNull(cues.Get("103"));
    }
}
