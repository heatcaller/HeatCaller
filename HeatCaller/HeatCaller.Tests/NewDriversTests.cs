namespace HeatCaller.Tests;

/// <summary>
/// Drivers new to racing (12 Nov 2026, setup plan D12): who counts as new,
/// what the desk shows, and the announcer's welcome. See NewDrivers.
/// </summary>
public sealed class NewDriversTests : IDisposable
{
    // The time, not midnight's date: a first night is the 6am night (bug hunt 17).
    private static readonly DateTime Today = DateTime.Now;

    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_driver_with_no_earlier_results_is_on_their_first_night_and_a_ticked_novice_is_new()
    {
        // Since bug hunt 21, the filed results say who is new, not when the profile was made.
        var history = Enumerable.Range(1, 4)
            .Select(week => new SessionRecord
            {
                SessionType = RaceSessionType.Race,
                StartedAtUtc = DateTime.UtcNow.AddDays(-7 * week),
                Results = new List<SessionResultEntry> { new() { Position = 1, Transponder = "2" }, new() { Position = 2, Transponder = "3" } },
            })
            .ToList();
        var before = NewDrivers.RacedBefore.From(history, Today);

        var firstNight = new DriverProfile { Transponder = "1" };
        var regular = new DriverProfile { Transponder = "2" };
        var novice = new DriverProfile { Transponder = "3", IsNovice = true, Mentor = "Al Gorithim" };

        Assert.True(NewDrivers.FirstNight(firstNight, before));
        Assert.False(NewDrivers.IsNew(regular, before));
        Assert.True(NewDrivers.IsNew(novice, before));

        Assert.Equal(" · first night", NewDrivers.Badge(firstNight, before));
        Assert.Equal(" · novice, mentor Al Gorithim", NewDrivers.Badge(novice, before));
        Assert.Equal(string.Empty, NewDrivers.Badge(regular, before));
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
