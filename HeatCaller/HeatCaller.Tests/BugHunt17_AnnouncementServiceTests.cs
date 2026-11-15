namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the names the screens show.
///
/// A name in the possessive kept the voice's respelling: "Ela vayter's clean
/// run is over" went to the Big Screen as written for the voice, because the
/// apostrophe after the name refused the match.
///
/// And the profiles were read from the live list on whichever thread queued
/// the line - a pool thread for a sponsor's read in the break - while the
/// desk could be saving a driver on the UI thread.
/// </summary>
public sealed class BugHunt17_AnnouncementServiceTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DriverProfile[] Profiles =
    {
        new() { Transponder = "1", DriverName = "Ella Vator", AnnouncerName = "Ela vayter" },
        new() { Transponder = "2", DriverName = "Samantha Lee", AnnouncerName = "Sam" },
        new() { Transponder = "3", DriverName = "Sam Jones" },
        new() { Transponder = "4", DriverName = "Olive O'Brien", AnnouncerName = "O" },
    };

    [Fact]
    public void A_name_in_the_possessive_gets_the_real_name_back()
    {
        Assert.Equal("Ella Vator's clean run is over.",
            AnnouncementService.WithScreenNames("Ela vayter's clean run is over.", Profiles));
        Assert.Equal("That is Samantha Lee's quickest ever lap here!",
            AnnouncementService.WithScreenNames("That is Sam's quickest ever lap here!", Profiles));
    }

    [Fact]
    public void A_real_name_in_the_possessive_is_left_whole()
    {
        // "Sam Jones's" is Sam Jones, not Sam the nickname with "Jones's" after it.
        Assert.Equal("Sam Jones's lead over Samantha Lee is two seconds.",
            AnnouncementService.WithScreenNames("Sam Jones's lead over Sam is two seconds.", Profiles));
    }

    [Fact]
    public void Any_other_apostrophe_still_ends_no_name()
    {
        // The O of O'Brien is not Olive's nickname.
        Assert.Equal("Sam Jones leads O'Brien and Olive O'Brien.",
            AnnouncementService.WithScreenNames("Sam Jones leads O'Brien and O.", Profiles));
    }

    [Fact]
    public void The_any_thread_list_is_the_profiles_as_last_changed()
    {
        var store = ProfileStore.Load();
        store.Upsert(new DriverProfile { Transponder = "20", DriverName = "Terry Byte" });
        store.Upsert(new DriverProfile { Transponder = "10", DriverName = "Ella Vator", AnnouncerName = "Ela vayter" });

        IReadOnlyList<DriverProfile>? seen = null;
        var reader = new Thread(() => seen = store.AllIncludingGhostsForAnyThread);
        reader.Start();
        reader.Join();

        Assert.Equal(store.AllIncludingGhosts.Select(p => p.Transponder), seen!.Select(p => p.Transponder));
        Assert.Contains(seen!, p => p.AnnouncerName == "Ela vayter");
    }

    [Fact]
    public void The_screens_names_come_from_the_any_thread_list()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        var source = File.ReadAllText(Path.Combine(dir!.FullName, "HeatCaller", "HeatCaller", "AnnouncementService.cs"));

        // Starred (sailor-mouth, 15 Nov 2026) wraps it now; the names still come from the any-thread list.
        Assert.Contains("Starred(WithScreenNames(VoiceProfile.StripAudioTags(text), _profiles.AllIncludingGhostsForAnyThread));", source);
        Assert.DoesNotContain("_profiles.AllIncludingGhosts)", source);
    }
}
