namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 18: a phone's "racing tonight" pick is applied on the roster
/// tick with no desk step, so it can land while a dialog is open on that
/// driver. The shared Edit Driver dialog (Who's Racing double-click, the
/// leaderboard's Edit Driver) and Import Entries' preview both used to save
/// the profile as it was when they opened, putting the old car back.
/// </summary>
public sealed class BugHunt18_DialogsKeepPhoneCarPickTests
{
    private static DriverProfile Sam() => new()
    {
        Transponder = "4000137",
        DriverName = "Sam Parker",
        RaceNumber = "7",
        ClassName = "Stock Buggy",
        CarModel = "Car A",
        Cars = new List<string> { "Car A", "Car B" },
    };

    [Fact]
    public void The_edit_driver_dialog_keeps_a_car_picked_on_the_phone_while_it_was_open()
    {
        var openedOn = ProfileStore.Snapshot(Sam());

        // The desk fixes the race number; the car box still shows Car A.
        var edited = ProfileStore.Snapshot(Sam());
        edited.RaceNumber = "17";

        // Meanwhile the phone: racing Car B tonight.
        var now = ProfileStore.Snapshot(Sam());
        now.SetActiveCar("Car B");

        var saved = DriverEditDialog.ToSave(openedOn, edited, now);

        Assert.Equal("Car B", saved.CarModel);
        Assert.Equal("17", saved.RaceNumber);
    }

    [Fact]
    public void The_edit_driver_dialog_saves_a_new_driver_as_typed()
    {
        var edited = Sam();

        Assert.Same(edited, DriverEditDialog.ToSave(null, edited, null));
    }

    [Fact]
    public void Import_entries_keeps_a_car_picked_on_the_phone_while_the_preview_was_open()
    {
        var sam = Sam();
        var row = new EntryImport.EntryRow(2, "Sam Parker", "4000137", new[] { "Mod Buggy" },
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

        var plan = EntryImport.Plan(new[] { row }, new[] { sam }, new[] { "Stock Buggy", "Mod Buggy" }, replaceDiffering: false);
        Assert.Single(plan.Saves);

        // While the desk reads the preview, Sam picks Car B on his phone.
        var now = ProfileStore.Snapshot(sam);
        now.SetActiveCar("Car B");

        var saves = plan.SavesKeepingChangesMadeSince(t => t == now.Transponder ? now : null);

        var saved = Assert.Single(saves);
        Assert.Equal("Car B", saved.CarModel);
        Assert.Equal(plan.Saves[0].Classes, saved.Classes);
        Assert.Contains("Mod Buggy", saved.Classes);
    }
}
