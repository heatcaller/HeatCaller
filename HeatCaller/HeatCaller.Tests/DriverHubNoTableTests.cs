using System.Reflection;
using System.Windows.Forms;

namespace HeatCaller.Tests;

/// <summary>
/// Driver Hub without its Table view (23 Nov 2026, Asher: "remove the table
/// tab from the driver hub"). The switch in the upper left is Card and
/// Garage, and a "Table" saved from before opens on Card rather than on a
/// view that is no longer there.
/// </summary>
public sealed class DriverHubNoTableTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));

    [Theory]
    [InlineData("Table")]
    [InlineData("Hub")]
    public void The_switch_is_Card_and_Garage_and_a_saved_Table_opens_on_Card(string saved)
    {
        var seen = StaThread.Run(() =>
        {
            var settings = new AppSettings { DriversPageView = saved };
            var profiles = ProfileStore.Load();
            var classes = RaceClassStore.Load();
            var cars = CarStore.Load();
            var setups = SetupLogStore.Load();
            var templates = ChassisTemplateStore.Load();
            var history = SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>());
            var undo = new UndoService();

            var garage = new DriverGarageSources
            {
                Cars = cars,
                Setups = setups,
                Templates = templates,
                MakeGarage = () => new GarageControl(
                    cars, ChassisTypeStore.Load(), profiles, classes, setups, history,
                    TrackLayoutStore.Load(), templates, undo, null, forOneDriver: true),
                OpenCar = (_, _) => { },
            };

            using var page = new ProfileManagerControl(profiles, classes, undo, settings: settings, garage: garage);
            _ = page.Handle;
            StaThread.Settle();

            var buttons = All(page).OfType<Button>().Select(b => b.Text).ToList();
            var view = typeof(ProfileManagerControl).GetField("_view", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!.ToString();

            return (Card: buttons.Contains("Card"), Garage: buttons.Contains("Garage"), Table: buttons.Contains("Table"), View: view);
        }, TimeSpan.FromSeconds(60), () => "building Driver Hub with " + saved + " saved");

        Assert.True(seen.Card, "Card on the switch");
        Assert.True(seen.Garage, "Garage on the switch");
        Assert.False(seen.Table, "no Table on the switch");
        Assert.Equal("Card", seen.View);
        Assert.False(Enum.GetNames(typeof(ProfileManagerControl).GetNestedType("DriverView", BindingFlags.NonPublic)!).Contains("Table"));
    }
}
