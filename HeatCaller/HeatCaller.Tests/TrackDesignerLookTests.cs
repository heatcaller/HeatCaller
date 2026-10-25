using System.Globalization;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The Track Designer's new look (Track Designer overhaul, Phase 2, 25 Oct
/// 2026): the board's chips, the panel's tabs (a sixth, Stock, in Phase 4) and the Panel switch.
/// </summary>
public sealed class TrackDesignerLookTests
{
    private static RcpTrackDesign Floor(int cols, int rows, bool feet)
    {
        var design = new RcpTrackDesign { TileSizeMm = 500, RoomInFeet = feet };

        for (var col = 0; col < cols; col++)
        {
            for (var row = 0; row < rows; row++)
            {
                design.Tiles.Add(new RcpTile { Col = col, Row = row });
            }
        }

        return design;
    }

    /// <summary>Runs with the invariant culture, so "0.0" reads the same on every PC.</summary>
    private static T Invariant<T>(Func<T> read)
    {
        var was = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        try
        {
            return read();
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

    [Fact]
    public void An_empty_plan_has_no_chips()
    {
        Assert.Empty(RcpTrackEditorForm.Chips(new RcpTrackDesign(), 0f, lapSeconds: null));
    }

    [Fact]
    public void The_size_chip_follows_the_unit_the_room_is_given_in()
    {
        var feet = Invariant(() => RcpTrackEditorForm.Chips(Floor(12, 8, feet: true), 0f, null));
        var metres = Invariant(() => RcpTrackEditorForm.Chips(Floor(12, 8, feet: false), 0f, null));

        // 12 x 8 tiles of 50cm: 6 x 4 metres, 19.7 x 13.1 feet.
        Assert.Equal("19.7 x 13.1 ft · 12 x 8 tiles", feet[0]);
        Assert.Equal("6.0 x 4.0 m · 12 x 8 tiles", metres[0]);

        // No line drawn yet, and it says so rather than "Lap 0".
        Assert.Equal("No racing line yet", feet[1]);
    }

    [Fact]
    public void The_lap_chip_gives_a_time_only_when_the_track_has_one()
    {
        var design = Floor(12, 8, feet: false);

        // Round the middle of the outside lane: 11 + 7 + 11 + 7 = 36 tiles, 18m.
        foreach (var (x, y) in new[] { (0.5f, 0.5f), (11.5f, 0.5f), (11.5f, 7.5f), (0.5f, 7.5f) })
        {
            design.Route.Add(new RcpRoutePoint { X = x, Y = y });
        }

        var length = RcpPlan.RouteLengthInTiles(design);

        var timed = Invariant(() => RcpTrackEditorForm.Chips(design, length, lapSeconds: 9.5));
        var untimed = Invariant(() => RcpTrackEditorForm.Chips(design, length, lapSeconds: null));

        Assert.Equal("Lap 18.0 m · 9.5s", timed[1]);

        // Never the 12s stand-in Play uses when the track has no lap time.
        Assert.Equal("Lap 18.0 m", untimed[1]);
    }

    [Fact]
    public void The_panel_has_its_six_tabs_and_the_Panel_button_hides_it()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = new RcpTrackEditorForm(null, "Club Hall", null);

            var tabs = ((Array)typeof(RcpTrackEditorForm)
                    .GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(form)!)
                .Cast<object>()
                .Select(pair => (Button)pair.GetType().GetField("Item1")!.GetValue(pair)!)
                .ToList();

            var names = tabs.Select(tab => tab.Text).ToList();

            var showTab = typeof(RcpTrackEditorForm).GetMethod("ShowTab", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var toggle = typeof(RcpTrackEditorForm).GetMethod("TogglePanel", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var shown = typeof(RcpTrackEditorForm).GetField("_panelShown", BindingFlags.Instance | BindingFlags.NonPublic)!;

            // Not remembered, so a designer another test opens at the same
            // time still opens where it would have.
            showTab.Invoke(form, new object[] { "Lap", false });
            var lit = tabs.Where(tab => tab.BackColor == AppTheme.Primary).Select(tab => tab.Text).ToList();

            var before = (bool)shown.GetValue(form)!;
            toggle.Invoke(form, null);
            var hidden = (bool)shown.GetValue(form)!;
            toggle.Invoke(form, null);
            var back = (bool)shown.GetValue(form)!;

            return (names, lit, before, hidden, back);
        }, TimeSpan.FromSeconds(30), () => "opening the Track Designer");

        Assert.Equal(new[] { "Pieces", "Plan", "Room", "Lap", "Stock", "Keys" }, seen.names);
        Assert.Equal(new[] { "Lap" }, seen.lit);
        Assert.True(seen.before);
        Assert.False(seen.hidden);
        Assert.True(seen.back);
    }
}
