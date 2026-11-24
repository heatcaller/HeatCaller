using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), Driver Hub. A check-in made on a phone while
/// the page was open left the "N in tonight" count and the card's Tonight
/// tile on the old value until the page was left and opened again.
/// </summary>
public sealed class BugHunt24_ProfileManagerControlTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Driver_Hub_follows_a_check_in_and_a_check_out_made_while_it_is_open()
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Bill" });

        var checkIns = CheckInStore.Load();

        var countField = typeof(ProfileManagerControl).GetField("_lblRosterCount", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var tilesField = typeof(ProfileManagerControl).GetField("_headerTiles", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var selectDriver = typeof(ProfileManagerControl).GetMethod("SelectDriver", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var (before, afterIn, afterOut) = StaThread.Run(() =>
        {
            // With the garage, as MainForm builds it: the Tonight tile is
            // only on that layout (BuildRoomyLayout). Without it the page is
            // the layout catalogue's painted card, which has no tiles.
            var cars = CarStore.Load();
            var setups = SetupLogStore.Load();
            var templates = ChassisTemplateStore.Load();
            var undo = new UndoService();
            var garage = new DriverGarageSources
            {
                Cars = cars,
                Setups = setups,
                Templates = templates,
                MakeGarage = () => new GarageControl(
                    cars, ChassisTypeStore.Load(), profiles, RaceClassStore.Load(), setups,
                    SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>()),
                    TrackLayoutStore.Load(), templates, undo, forOneDriver: true),
                OpenCar = (_, _) => { },
            };

            using var page = new ProfileManagerControl(
                profiles,
                RaceClassStore.Load(),
                undo,
                new DriverStatsSources { CheckIns = checkIns },
                garage: garage);

            _ = page.Handle;
            StaThread.Settle();
            selectDriver.Invoke(page, new object[] { "101" });
            StaThread.Settle();

            (string Count, string Tile) Read() =>
                (((Label)countField.GetValue(page)!).Text, ((StatTile[])tilesField.GetValue(page)!)[0].Value.Text);

            var before = Read();

            // A phone tap: the store changes and MainForm tells the page.
            checkIns.SetCheckedIn("101", true);
            page.RefreshCheckIns();
            var afterIn = Read();

            checkIns.SetCheckedIn("101", false);
            page.RefreshCheckIns();
            var afterOut = Read();

            return (before, afterIn, afterOut);
        }, TimeSpan.FromSeconds(30), () => "checking a driver in under the Driver Hub");

        Assert.EndsWith("0 in tonight", before.Count);
        Assert.Equal("Not in yet", before.Tile);

        Assert.EndsWith("1 in tonight", afterIn.Count);
        Assert.Equal("Checked in", afterIn.Tile);

        Assert.EndsWith("0 in tonight", afterOut.Count);
        Assert.Equal("Not in yet", afterOut.Tile);
    }
}
