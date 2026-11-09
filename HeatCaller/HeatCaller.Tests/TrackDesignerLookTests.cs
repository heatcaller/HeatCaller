using System.Globalization;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The Track Designer's new look (Track Designer overhaul, Phase 2, 25 Oct
/// 2026): the board's chips, the inspector's sections (8 Nov 2026, in place of the panel's tabs), the bar over the board and the P switch.
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
    public void The_inspector_opens_one_section_at_a_time_and_the_tool_picks_the_bar_over_the_board()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = new RcpTrackEditorForm(null, "Club Hall", null)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                ShowInTaskbar = false,
            };

            // On screen, so what is hidden reads as hidden - before it is shown,
            // everything in it says it is.
            form.Show();

            var flags = BindingFlags.Instance | BindingFlags.NonPublic;

            var sections = ((Array)typeof(RcpTrackEditorForm).GetField("_tabs", flags)!.GetValue(form)!)
                .Cast<object>()
                .Select(entry => (
                    Name: (string)entry.GetType().GetField("Item1")!.GetValue(entry)!,
                    Header: (Control)entry.GetType().GetField("Item2")!.GetValue(entry)!,
                    Cards: (Section[])entry.GetType().GetField("Item3")!.GetValue(entry)!))
                .ToList();

            var names = sections.Select(section => section.Name).ToList();

            var showTab = typeof(RcpTrackEditorForm).GetMethod("ShowTab", flags)!;
            var toggle = typeof(RcpTrackEditorForm).GetMethod("TogglePanel", flags)!;
            var shown = typeof(RcpTrackEditorForm).GetField("_panelShown", flags)!;
            var setTool = typeof(RcpTrackEditorForm).GetMethod("SetTool", flags)!;
            var strip = (Control)typeof(RcpTrackEditorForm).GetField("_contextBar", flags)!.GetValue(form)!;

            // Not remembered, so a designer another test opens at the same
            // time still opens where it would have.
            showTab.Invoke(form, new object[] { "Lap", false });

            var open = sections.Where(section => section.Cards.All(card => card.Visible)).Select(section => section.Name).ToList();
            var lit = sections.Where(section => section.Header.BackColor == AppTheme.Primary).Select(section => section.Name).ToList();

            // A closed section's header says its headline after its name.
            var plan = sections.Single(section => section.Name == "Plan").Header;
            var planHeader = plan.Text + "   ·   " + (string)plan.GetType().GetProperty("Headline")!.GetValue(plan)!;

            // Keys has no header until it is opened.
            var keysHidden = !sections.Single(section => section.Name == "Keys").Header.Visible;

            // The old Stock tab's name still opens its section.
            showTab.Invoke(form, new object[] { "Stock", false });
            var stockOpens = sections.Single(section => section.Name == "Parts").Cards.All(card => card.Visible);

            // Each tool shows its own row over the board, and only that one.
            setTool.Invoke(form, new object[] { RcpTool.Route });
            var routeRows = strip.Controls.Cast<Control>().Count(bar => bar.Visible);
            var routeHasTrace = strip.Controls.Cast<Control>().Single(bar => bar.Visible)
                .Controls.Cast<Control>().Any(part => part.Text == "Trace it for me");

            var before = (bool)shown.GetValue(form)!;
            toggle.Invoke(form, null);
            var hidden = (bool)shown.GetValue(form)!;
            toggle.Invoke(form, null);
            var back = (bool)shown.GetValue(form)!;

            return (names, open, lit, planHeader, keysHidden, stockOpens, routeRows, routeHasTrace, before, hidden, back);
        }, TimeSpan.FromSeconds(30), () => "opening the Track Designer");

        Assert.Equal(new[] { "Plan", "Room", "Lap", "Parts", "Auto", "Keys" }, seen.names);
        Assert.Equal(new[] { "Lap" }, seen.open);
        Assert.Equal(new[] { "Lap" }, seen.lit);
        Assert.StartsWith("Plan   ·   Club Hall", seen.planHeader);
        Assert.True(seen.keysHidden);
        Assert.True(seen.stockOpens);
        Assert.Equal(1, seen.routeRows);
        Assert.True(seen.routeHasTrace);
        Assert.True(seen.before);
        Assert.False(seen.hidden);
        Assert.True(seen.back);
    }
}