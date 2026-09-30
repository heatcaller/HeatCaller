namespace HeatCaller.Tests;

internal static partial class WindowCatalog
{
    /// <summary>
    /// The track designer (RcpTrackEditorForm, titled "Track Designer -
    /// &lt;track&gt;", or "Track Designer" for a new one), which Track Layouts'
    /// Track Designer... opens: a club's big layout under a long name - a
    /// floor of 24 by 14 tiles with a corner blocked out, rails round its edge
    /// and a divider down the middle, turn rails, every kind of marking and a
    /// racing line round it - checked against a kit far too small for it, so
    /// the kit check lists what it is short of; a new track, drawn from
    /// nothing, with no name; the same big layout whose traced photo is no
    /// longer where it was picked from, so the status line opens on why; and a
    /// small floor with a lap drawn round it and no rails yet - the most lines
    /// the lap's readout has - that fits the kit it is checked against.
    ///
    /// Building it starts nothing: the pace car runs only once Play is
    /// pressed, and nothing is saved or asked. The traced photo it looks for
    /// is a file in the test's own data folder that is not there.
    /// </summary>
    private static IEnumerable<(string Name, Func<Form> Build)> TrackDesignerWindows() => new (string, Func<Form>)[]
    {
        ("Track Designer", () => new RcpTrackEditorForm(TrackDesignerClubLayout(), TrackDesignerLongName(), 14.25)),
        ("Track Designer (a new track)", () => new RcpTrackEditorForm(null, string.Empty, null)),
        ("Track Designer (its traced photo gone)", () => new RcpTrackEditorForm(TrackDesignerPhotoGone(), TrackDesignerLongName(), 14.25)),
        ("Track Designer (a floor and a lap, no rails yet)", () => new RcpTrackEditorForm(TrackDesignerFloorOnly(), "Club Hall", null)),
    };

    private static string TrackDesignerLongName() => "Riverside Community Hall - Winter Series RCP Layout (Tuesday Nights)";

    /// <summary>
    /// A club's big layout on 50cm tiles: a floor of 24 by 14 with a corner
    /// of it blocked out, rails round every edge, a divider down the middle
    /// with a turn rail either side of each end, every kind of marking, and
    /// a racing line round the divider - checked against the 50cm kit, which
    /// is far too small for it.
    /// </summary>
    private static RcpTrackDesign TrackDesignerClubLayout()
    {
        const int cols = 24;
        const int rows = 14;
        const int middle = rows / 2;

        var design = new RcpTrackDesign
        {
            TileSizeMm = 500,
            KitName = "50cm Wide-L / Oblong",
            RouteClosed = true,
        };

        for (var col = 0; col < cols; col++)
        {
            for (var row = 0; row < rows; row++)
            {
                // The corner the timing table stands on.
                var blocked = col < 3 && row >= rows - 3;

                design.Tiles.Add(new RcpTile
                {
                    Col = col,
                    Row = row,
                    Kind = blocked ? RcpTileKind.Infield : RcpTileKind.Straight,
                });
            }
        }

        for (var col = 0; col < cols; col++)
        {
            design.Walls.Add(new RcpWall { Col = col, Row = 0 });
            design.Walls.Add(new RcpWall { Col = col, Row = rows });
        }

        for (var row = 0; row < rows; row++)
        {
            design.Walls.Add(new RcpWall { Col = 0, Row = row, Vertical = true });
            design.Walls.Add(new RcpWall { Col = cols, Row = row, Vertical = true });
        }

        // The divider, racing on both sides of it.
        for (var col = 4; col < cols - 4; col++)
        {
            design.Walls.Add(new RcpWall { Col = col, Row = middle, Double = true });
        }

        design.Curves.Add(new RcpCurve { Col = 4, Row = middle, Quadrant = RcpQuadrant.NorthWest, Radius = 1.5f });
        design.Curves.Add(new RcpCurve { Col = 4, Row = middle, Quadrant = RcpQuadrant.SouthWest, Radius = 1.5f });
        design.Curves.Add(new RcpCurve { Col = cols - 4, Row = middle, Quadrant = RcpQuadrant.NorthEast, Radius = 1.5f });
        design.Curves.Add(new RcpCurve { Col = cols - 4, Row = middle, Quadrant = RcpQuadrant.SouthEast, Radius = 1.5f });

        design.Decals.Add(new RcpDecal { Col = 12, Row = 2, Kind = RcpDecalKind.FinishLine, Turns = 1 });

        for (var box = 0; box < 6; box++)
        {
            design.Decals.Add(new RcpDecal { Col = 13 + box, Row = 1 + box % 2, Kind = RcpDecalKind.GridBox });
        }

        design.Decals.Add(new RcpDecal { Col = 4, Row = middle, Kind = RcpDecalKind.Post });
        design.Decals.Add(new RcpDecal { Col = cols - 5, Row = middle, Kind = RcpDecalKind.Post, Turns = 1 });
        design.Decals.Add(new RcpDecal { Col = 6, Row = middle - 1, Kind = RcpDecalKind.Kerb });
        design.Decals.Add(new RcpDecal { Col = 17, Row = middle, Kind = RcpDecalKind.Kerb, Turns = 2 });
        design.Decals.Add(new RcpDecal { Col = 1, Row = rows - 2, Kind = RcpDecalKind.Furniture });

        foreach (var (x, y) in new[]
                 {
                     (3f, 3.5f), (21f, 3.5f), (22.5f, 5f), (22.5f, 9f),
                     (21f, 10.5f), (3f, 10.5f), (1.5f, 9f), (1.5f, 5f),
                 })
        {
            design.Route.Add(new RcpRoutePoint { X = x, Y = y });
        }

        return design;
    }

    /// <summary>
    /// The club's big layout with a traced photo that has been moved since it
    /// was picked - a file in the test's own data folder that is not there, so
    /// the designer opens without it and its status line says why.
    /// </summary>
    private static RcpTrackDesign TrackDesignerPhotoGone()
    {
        var design = TrackDesignerClubLayout();

        design.BackdropPath = Path.Combine(AppPaths.Root, "track-designer-photos", "club-hall-from-the-balcony.jpg");
        design.BackdropWidth = 24f;

        return design;
    }

    /// <summary>
    /// A small floor on 30cm tiles, 12 by 8 with four blocked out, and a lap
    /// drawn round it but no rails yet - the readout's most lines: the lap,
    /// its speed, and the note to wall the edge - checked against the 30cm
    /// kit, which it fits.
    /// </summary>
    private static RcpTrackDesign TrackDesignerFloorOnly()
    {
        var design = new RcpTrackDesign
        {
            TileSizeMm = 300,
            KitName = "30cm Mini-96",
            RouteClosed = true,
        };

        for (var col = 0; col < 12; col++)
        {
            for (var row = 0; row < 8; row++)
            {
                var blocked = col is 5 or 6 && row is 3 or 4;

                design.Tiles.Add(new RcpTile
                {
                    Col = col,
                    Row = row,
                    Kind = blocked ? RcpTileKind.Infield : RcpTileKind.Straight,
                });
            }
        }

        foreach (var (x, y) in new[] { (1.5f, 1.5f), (10.5f, 1.5f), (10.5f, 6.5f), (1.5f, 6.5f) })
        {
            design.Route.Add(new RcpRoutePoint { X = x, Y = y });
        }

        return design;
    }
}
