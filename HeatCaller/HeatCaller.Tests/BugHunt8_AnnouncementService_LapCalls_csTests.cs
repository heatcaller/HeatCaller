namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (20 Oct 2026), AnnouncementService.LapCalls.cs.
///
/// A rivalry with a ghost car in it could be set up on the Rivalries page
/// (its drop-downs list AllRaceable) but never said a word, because the
/// announcer looked both drivers up in ProfileStore.All, which leaves
/// ghosts out. CheckRivalries needs a live announcer and a race, so the
/// lookup is checked in the source, and the list it now uses is checked
/// for what the fix relies on: ghosts in it, and real drivers first.
/// </summary>
public sealed class BugHunt8_AnnouncementService_LapCalls_csTests : IDisposable
{
    private const string Ghost = "GHOST-AAAAAA";

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Rivalry_drivers_are_looked_up_in_the_list_the_rivalries_page_offers()
    {
        var check = Between(Source("AnnouncementService.LapCalls.cs"), "private void CheckRivalries(", "private void FireRivalryLine(");

        Assert.Contains("_profiles.AllRaceable.FirstOrDefault(p => string.Equals(p.DriverName, rivalry.DriverA", check);
        Assert.Contains("_profiles.AllRaceable.FirstOrDefault(p => string.Equals(p.DriverName, rivalry.DriverB", check);
        Assert.DoesNotContain("_profiles.All.FirstOrDefault(", check);
    }

    [Fact]
    public void A_ghost_is_found_by_name_and_a_real_driver_wins_a_shared_name()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = Ghost, DriverName = "Pace Ghost", ExcludeFromRecords = true });

        Assert.Equal(Ghost, FirstByName(profiles, "pace ghost"));
        Assert.Equal("101", FirstByName(profiles, "Sam"));

        // A ghost given a real driver's name: the real driver is the rival.
        profiles.Upsert(new DriverProfile { Transponder = Ghost, DriverName = "Sam", ExcludeFromRecords = true });

        Assert.Equal("101", FirstByName(profiles, "Sam"));
    }

    // The same lookup CheckRivalries makes.
    private static string? FirstByName(ProfileStore profiles, string name) =>
        profiles.AllRaceable.FirstOrDefault(p => string.Equals(p.DriverName, name, StringComparison.OrdinalIgnoreCase))?.Transponder;

    // ------------------------------------------------------------------
    //  Reading the source

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    private static string Between(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);

        Assert.True(end > start, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return source[start..end];
    }
}
