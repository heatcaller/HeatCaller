namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15, B21: a phone's "racing tonight" pick is applied with no desk
/// step, so it can land while the Edit Profile dialog is open on that driver.
/// Saving the dialog used to store the profile as the dialog opened on it,
/// putting the old car back while the driver's phone said the new one.
/// </summary>
public sealed class BugHunt15_EditProfileKeepsPhoneChangesTests : IDisposable
{
    /// <summary>Declared first: the stores the dialog reads load from wherever AppPaths points.</summary>
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static DriverProfile Sam() => new()
    {
        Transponder = "4000137",
        DriverName = "Sam Parker",
        RaceNumber = "7",
        CarModel = "Car A",
        Cars = new List<string> { "Car A", "Car B" },
    };

    [Fact]
    public void A_car_picked_on_the_phone_while_the_dialog_was_open_survives_the_desks_save()
    {
        var before = ProfileStore.Snapshot(Sam());

        // The desk fixes the race number in the dialog, as it shows it.
        var edited = StaThread.Run(() =>
        {
            using var form = new ProfileForm(Sam(), RaceClassStore.Load());
            var shown = form.ProfileAsShown();
            shown.RaceNumber = "17";
            return shown;
        }, TimeSpan.FromSeconds(30), () => "the desk editing Sam's race number");

        // Meanwhile the phone: racing Car B tonight.
        var now = ProfileStore.Snapshot(Sam());
        now.SetActiveCar("Car B");

        var saved = ProfileStore.KeepChangesMadeSince(before, edited, now);

        Assert.Equal("Car B", saved.CarModel);
        Assert.Equal("17", saved.RaceNumber);
    }

    [Fact]
    public void A_car_the_phone_added_meanwhile_is_kept_even_when_the_desk_changed_the_list()
    {
        var before = ProfileStore.Snapshot(Sam());

        var edited = ProfileStore.Snapshot(Sam());
        edited.Cars = new List<string> { "Car A", "Car B", "Car C" };

        var now = ProfileStore.Snapshot(Sam());
        now.SetActiveCar("Car D");

        var saved = ProfileStore.KeepChangesMadeSince(before, edited, now);

        Assert.Equal("Car D", saved.CarModel);
        Assert.Contains("Car C", saved.Cars);
        Assert.Contains("Car D", saved.Cars);
    }

    [Fact]
    public void What_the_desk_changed_wins_over_the_file()
    {
        var before = ProfileStore.Snapshot(Sam());

        var edited = ProfileStore.Snapshot(Sam());
        edited.CarModel = "Car B";

        var now = ProfileStore.Snapshot(Sam());
        now.CarModel = "Car C";
        now.Cars.Add("Car C");

        var saved = ProfileStore.KeepChangesMadeSince(before, edited, now);

        Assert.Equal("Car B", saved.CarModel);
        Assert.Contains("Car C", saved.Cars);
    }

    [Fact]
    public void A_profile_removed_meanwhile_keeps_the_desks_edit()
    {
        var before = ProfileStore.Snapshot(Sam());
        var edited = ProfileStore.Snapshot(Sam());
        edited.RaceNumber = "17";

        Assert.Same(edited, ProfileStore.KeepChangesMadeSince(before, edited, null));
    }
}
