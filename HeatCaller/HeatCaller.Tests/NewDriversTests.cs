namespace HeatCaller.Tests;

/// <summary>
/// Drivers new to racing (12 Nov 2026, setup plan D12): who counts as new,
/// what the desk shows, and the announcer's welcome. See NewDrivers.
/// </summary>
public sealed class NewDriversTests : IDisposable
{
    private static readonly DateTime Today = DateTime.Today;

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_driver_first_seen_today_is_on_their_first_night_and_a_ticked_novice_is_new()
    {
        var firstNight = new DriverProfile { Transponder = "1", FirstSeenUtc = DateTime.UtcNow };
        var regular = new DriverProfile { Transponder = "2", FirstSeenUtc = DateTime.UtcNow.AddDays(-60) };
        var novice = new DriverProfile { Transponder = "3", FirstSeenUtc = DateTime.UtcNow.AddDays(-14), IsNovice = true, Mentor = "Al Gorithim" };

        Assert.True(NewDrivers.FirstNight(firstNight, Today));
        Assert.False(NewDrivers.IsNew(regular, Today));
        Assert.True(NewDrivers.IsNew(novice, Today));
        Assert.False(NewDrivers.IsNew(new DriverProfile { Transponder = "4" }, Today));

        Assert.Equal(" · first night", NewDrivers.Badge(firstNight, Today));
        Assert.Equal(" · novice, mentor Al Gorithim", NewDrivers.Badge(novice, Today));
        Assert.Equal(string.Empty, NewDrivers.Badge(regular, Today));
    }

    [Fact]
    public void The_welcome_names_them_and_asks_the_field_for_room()
    {
        Assert.Equal("A big welcome to Max Power, racing with us for the first time tonight - give them room out there.",
            NewDrivers.Welcome(new[] { ("Max Power", true) }));

        Assert.StartsWith("Look out for Max Power and Rhoda Rage, new to racing",
            NewDrivers.Welcome(new[] { ("Max Power", true), ("Rhoda Rage", false) }));

        Assert.Null(NewDrivers.Welcome(Array.Empty<(string, bool)>()));
    }

    [Fact]
    public void Editing_a_driver_in_Driver_Hub_keeps_the_novice_tick_and_mentor()
    {
        var driver = new DriverProfile { Transponder = "1", DriverName = "Max Power", IsNovice = true, Mentor = "Al Gorithim" };

        var shown = StaThread.Run(() =>
        {
            using var form = new ProfileForm(driver, RaceClassStore.Load());
            return form.ProfileAsShown();
        }, TimeSpan.FromSeconds(30), () => "editing Max Power in Driver Hub");

        Assert.True(shown.IsNovice);
        Assert.Equal("Al Gorithim", shown.Mentor);
    }
}
