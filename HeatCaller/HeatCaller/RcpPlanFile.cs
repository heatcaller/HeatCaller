using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// A track plan as a file of its own, .hctrack, so clubs can swap layouts
/// (Track Designer overhaul, Phase 7, 25 Oct 2026).
///
/// JSON: what the plan is (Format, Version), the track's name, and the
/// plan itself as the app keeps it (RcpTrackDesign). Left out on the way
/// out: the traced photo (a path on this PC means nothing on another, and
/// the photo isn't in the file) and when the line last changed (that is
/// about this club's races). Kept: the floor, the rails, the lanes, the
/// markings, the line, the tile size, the kit, the room.
///
/// On the way in, a file from anywhere is treated as a stranger's: refused
/// past a sensible size, read, and then mended exactly as a plan loaded
/// from the club's own track-layouts.json is (Repair) - nulls, nonsense
/// numbers - and refused when it holds more than any real plan could.
/// </summary>
public static class RcpPlanFile
{
    public const string Extension = ".hctrack";

    /// <summary>The filter for the file dialogs.</summary>
    public const string Filter = "HeatCaller track plan (*.hctrack)|*.hctrack";

    /// <summary>What the file says it is.</summary>
    public const string FormatName = "HeatCaller track plan";

    public const int Version = 1;

    /// <summary>The largest file opened - far beyond any real plan, which is a few hundred KB at most.</summary>
    public const long MostBytes = 8 * 1024 * 1024;

    /// <summary>The most pieces of each kind a plan opened from a file may hold - a hall a hundred tiles a side, and then some.</summary>
    public const int MostPieces = 20000;

    /// <summary>How far from the middle of the grid anything may sit, in tiles.</summary>
    public const int MostReach = 2000;

    /// <summary>
    /// The highest lane number a file may give a tile. Far past any this
    /// editor hands out, and low enough that one more for a new lane (see
    /// RcpAutoRails.NewLane) can never run off the end of an int.
    /// </summary>
    public const int MostLane = 1_000_000;

    /// <summary>The widest or deepest room a file may say, in its own unit (feet or metres): what the Room tab's boxes go to.</summary>
    public const double MostRoom = 500;

    /// <summary>The longest track or kit name taken from a file.</summary>
    public const int MostName = 100;

    /// <summary>The longest tile label taken from a file.</summary>
    public const int MostLabel = 40;

    private sealed class Envelope
    {
        // Empty until read or written, so a file that doesn't say what it
        // is isn't taken for a plan.
        public string Format { get; set; } = string.Empty;
        public int Version { get; set; }
        public string Name { get; set; } = string.Empty;
        public RcpTrackDesign? Design { get; set; }
    }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The plan as the text of a .hctrack file.</summary>
    public static string Write(RcpTrackDesign design, string name)
    {
        var copy = design.Clone();

        copy.BackdropPath = string.Empty;
        copy.BackdropHidden = false;
        copy.RouteChangedUtc = null;

        return JsonSerializer.Serialize(new Envelope { Format = FormatName, Version = Version, Name = name?.Trim() ?? string.Empty, Design = copy }, Options);
    }

    /// <summary>
    /// A plan read from a .hctrack file's text, mended - or why not: the
    /// file is not a track plan, is from a newer HeatCaller, or holds more
    /// than any real plan.
    /// </summary>
    public static (RcpTrackDesign? Design, string Name, string? Problem) Read(string json)
    {
        Envelope? envelope;

        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(json ?? string.Empty, Options);
        }
        catch (JsonException)
        {
            return (null, string.Empty, "That file isn't a HeatCaller track plan - it couldn't be read.");
        }

        if (envelope is null || !string.Equals(envelope.Format, FormatName, StringComparison.Ordinal) || envelope.Version < 1 || envelope.Design is null)
        {
            return (null, string.Empty, "That file isn't a HeatCaller track plan.");
        }

        if (envelope.Version > Version)
        {
            return (null, string.Empty, "That plan was saved by a newer HeatCaller. Update HeatCaller to open it.");
        }

        var design = envelope.Design;
        Repair(design);
        Sensible(design);

        // A piece of a kind this HeatCaller doesn't know - from a file put
        // together by hand, since a newer HeatCaller's file is refused by its
        // version above - has nothing to draw: a tile becomes plain floor, a
        // turn round no corner or a marking that is no marking goes. Here
        // and not in Repair, so the club's own file never loses anything to
        // an older HeatCaller opening it.
        foreach (var tile in design.Tiles.Where(t => !Enum.IsDefined(typeof(RcpTileKind), t.Kind)))
        {
            tile.Kind = RcpTileKind.Straight;
        }

        // The same for a tile's piece and the edge or corner its rail is on
        // (railed tiles, 7 Nov 2026): one this HeatCaller doesn't know is a
        // plain tile.
        foreach (var tile in design.Tiles.Where(t => !Enum.IsDefined(typeof(RcpPiece), t.Piece)
                     || !Enum.IsDefined(typeof(RcpEdge), t.Facing) || !Enum.IsDefined(typeof(RcpQuadrant), t.Corner)))
        {
            tile.Piece = RcpPiece.Plain;
            tile.Facing = RcpEdge.North;
            tile.Corner = RcpQuadrant.NorthEast;
        }

        design.Curves = design.Curves.Where(c => Enum.IsDefined(typeof(RcpQuadrant), c.Quadrant)).ToList();
        design.Decals = design.Decals.Where(d => Enum.IsDefined(typeof(RcpDecalKind), d.Kind)).ToList();

        // Two pieces in one place can't come from the editor, which
        // replaces what is there - only from a file put together some
        // other way. The last one wins, as the editor would have it. Two
        // turn rails can share a grid point round different quarters - a
        // divider's end has one either side - so a turn is one per quarter.
        design.Tiles = design.Tiles.GroupBy(t => (t.Col, t.Row)).Select(g => g.Last()).ToList();
        design.Walls = design.Walls.GroupBy(w => (w.Col, w.Row, w.Vertical)).Select(g => g.Last()).ToList();
        design.Curves = design.Curves.GroupBy(c => (c.Col, c.Row, c.Quadrant)).Select(g => g.Last()).ToList();

        if (design.Tiles.Count > MostPieces || design.Walls.Count > MostPieces || design.Curves.Count > MostPieces
            || design.Decals.Count > MostPieces || design.Route.Count > MostPieces)
        {
            return (null, string.Empty, "That plan holds far more than any real track - it wasn't opened.");
        }

        bool Near(float x, float y) => Math.Abs(x) <= MostReach && Math.Abs(y) <= MostReach;

        if (!design.Tiles.All(t => Near(t.Col, t.Row)) || !design.Walls.All(w => Near(w.Col, w.Row))
            || !design.Curves.All(c => Near(c.Col, c.Row)) || !design.Decals.All(d => Near(d.Col, d.Row))
            || !design.Route.All(p => Near(p.X, p.Y)))
        {
            return (null, string.Empty, "That plan has pieces thousands of tiles away - it wasn't opened.");
        }

        // A photo path can't be trusted from a file - see the class notes.
        design.BackdropPath = string.Empty;
        design.RouteChangedUtc = null;

        return (design, Capped(envelope.Name, MostName), null);
    }

    /// <summary>
    /// Fills in what a plan left as null - however it was loaded: from the
    /// club's own track-layouts.json (TrackLayoutStore) or a file from
    /// another club. No null lists or pieces, no null text. Nothing else:
    /// a number this HeatCaller wouldn't set - a newer one's lane turns, a
    /// tile size - is left exactly as it is, because the club's file is
    /// saved back whole and an older HeatCaller must never quietly change
    /// what a newer one wrote (review, 25 Oct 2026). The numbers of a file
    /// from outside are made sensible in Read instead.
    /// </summary>
    public static void Repair(RcpTrackDesign design)
    {
        if (design is null)
        {
            return;
        }

        design.Tiles = (design.Tiles ?? new List<RcpTile>()).Where(x => x is not null).ToList();
        design.Walls = (design.Walls ?? new List<RcpWall>()).Where(x => x is not null).ToList();
        design.Curves = (design.Curves ?? new List<RcpCurve>()).Where(x => x is not null).ToList();
        design.Decals = (design.Decals ?? new List<RcpDecal>()).Where(x => x is not null).ToList();
        design.Route = (design.Route ?? new List<RcpRoutePoint>()).Where(x => x is not null).ToList();
        design.KitName ??= string.Empty;
        design.BackdropPath ??= string.Empty;

        foreach (var tile in design.Tiles)
        {
            tile.Label ??= string.Empty;
        }
    }

    /// <summary>
    /// The numbers of a plan from a file made sensible: what this
    /// HeatCaller's editor would never set is put back to what it would.
    /// Only for a file from outside - see Repair.
    /// </summary>
    private static void Sensible(RcpTrackDesign design)
    {
        static int Quarter(int turns) => ((turns % 4) + 4) % 4;

        foreach (var tile in design.Tiles)
        {
            tile.Lane = Math.Clamp(tile.Lane, 0, MostLane);
            tile.Turns = Quarter(tile.Turns);
            tile.Label = Capped(tile.Label, MostLabel);
        }

        foreach (var decal in design.Decals)
        {
            decal.Turns = Quarter(decal.Turns);
        }

        foreach (var curve in design.Curves)
        {
            if (float.IsNaN(curve.Radius) || curve.Radius <= 0 || curve.Radius > 10)
            {
                curve.Radius = 1f;
            }
        }

        design.Route = design.Route.Where(p => !float.IsNaN(p.X) && !float.IsInfinity(p.X) && !float.IsNaN(p.Y) && !float.IsInfinity(p.Y)).ToList();
        design.KitName = Capped(design.KitName, MostName);

        if (double.IsNaN(design.TileSizeMm) || design.TileSizeMm < 100 || design.TileSizeMm > 2000)
        {
            design.TileSizeMm = 500;
        }

        design.LaneTurnRadius = Math.Clamp(design.LaneTurnRadius, 0, RcpAutoRails.MostRadius);
        design.BackdropOpacity = Math.Clamp(design.BackdropOpacity, 0, 100);

        // A room past what the Room tab's boxes go to - 500 of whichever
        // unit it is given in - is no room anybody measured: it goes,
        // rather than coming in as a giant outline the boxes can't show.
        var most = MostRoom * (design.RoomInFeet ? 304.8 : 1000.0);

        if (!(design.RoomWidthMm >= 0 && design.RoomWidthMm <= most && design.RoomDepthMm >= 0 && design.RoomDepthMm <= most))
        {
            design.RoomWidthMm = 0;
            design.RoomDepthMm = 0;
        }
    }

    /// <summary>Text from a file, one line, no longer than <paramref name="most"/>.</summary>
    private static string Capped(string? text, int most)
    {
        var line = string.Join(" ", (text ?? string.Empty).Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

        return line.Length <= most ? line : line[..most].TrimEnd();
    }
}
