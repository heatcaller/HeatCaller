using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace HeatCaller;

/// <summary>What the pointer does on the plan.</summary>
public enum RcpTool
{
    /// <summary>Lay floor tiles.</summary>
    Floor,

    /// <summary>Stand rails along the tile seams.</summary>
    Wall,

    /// <summary>Swing a run of rail through a corner.</summary>
    Curve,

    /// <summary>Flat markings: the line, grid boxes, kerbs, posts.</summary>
    Marker,

    /// <summary>Draw the racing line.</summary>
    Route,

    /// <summary>Take things back off.</summary>
    Erase,

    /// <summary>Move and size the photo being traced over.</summary>
    Backdrop,
}

/// <summary>One thing that can be picked out of the palette.</summary>
public enum RcpPaletteItem
{
    Floor,
    Blocked,
    Rail,
    Divider,
    Turn,
    FinishLine,
    GridBox,
    Post,
    Kerb,
    Furniture,
    Split1,
    Split2,
    Split3,
}

/// <summary>
/// The drawing board.
///
/// The floor is painted in tiles; the circuit is built on top of it out of
/// rails that run along the tile seams, exactly as it is on a real track -
/// so a rail down the middle of the floor with racing on both sides is a
/// normal thing to draw here, which is the whole point.
/// </summary>
public sealed class RcpTrackCanvas : Control
{
    private readonly System.Windows.Forms.Timer _paceTimer = new() { Interval = 33 };
    private readonly List<RcpTrackDesign> _undo = new();
    private readonly List<RcpTrackDesign> _redo = new();

    private RcpTrackDesign _design = new();
    private Image? _backdrop;

    private float _scale = 64f;
    private PointF _origin = new(40, 40);

    private Point? _hoverCell;
    private (int Col, int Row, bool Vertical)? _hoverEdge;
    private (Point At, RcpQuadrant Quadrant)? _hoverCorner;

    private int _draggingRoutePoint = -1;
    private bool _painting;
    private bool _panning;
    private Point _panFrom;
    private PointF _panOrigin;
    private PointF _backdropFrom;

    // --- One drag, one stroke ------------------------------------------------
    // A drag across forty tiles is one thing the user did, not forty, so it
    // takes one undo step and one clone of the plan rather than forty of
    // each.

    private bool _strokeOpen;
    private bool _strokeRemembered;

    /// <summary>Where the floor brush was last put down, so a fast drag can be filled in behind it.</summary>
    private Point? _lastPaintedCell;

    /// <summary>Where the eraser last was, in pixels, for the same reason.</summary>
    private Point? _lastErasedAt;

    // --- The rail run being dragged ------------------------------------------
    // Rail is laid as a run of grid points rather than as separate seams,
    // which is what makes a corner possible to spot: three points that do
    // not lie in a line are a turn, and a turn on a real track is a curved
    // rail rather than two straight ones meeting at a right angle.

    /// <summary>The point the run has reached.</summary>
    private Point? _railAt;

    /// <summary>The point before that, which is the arm a corner would swing from.</summary>
    private Point? _railPrev;

    /// <summary>The direction of the last seam laid, as a unit step.</summary>
    private Point? _railDir;

    /// <summary>Where the run began, and which way it set off, so a lap brought back round can be closed.</summary>
    private Point? _railStart;
    private Point? _railFirstDir;

    /// <summary>
    /// How many seams the run has come in the direction it is going.
    ///
    /// A turn rail occupies a tile of the run on each side of it, so a
    /// corner needs that tile to be its own - which means two of them
    /// cannot sit a single tile apart. This is the count that says whether
    /// there is room.
    /// </summary>
    private int _railStraightRun;

    /// <summary>Said once a session, not once a corner.</summary>
    private bool _toldAboutTightCorners;

    /// <summary>Whether this drag has laid any rail, which decides if the mouse-up is a click.</summary>
    private bool _railLaid;

    /// <summary>
    /// Holding Ctrl turns whatever tool is in hand into the eraser for as
    /// long as the button is down, so a mistake can be wiped off without
    /// going to the toolbar and back.
    /// </summary>
    private bool _ctrlErasing;

    private float _paceDistance;
    private DateTime _paceLastTick = DateTime.UtcNow;

    private const int UndoDepth = 150;

    /// <summary>How close to a seam or a corner the pointer has to be, in tiles.</summary>
    private const float EdgeGrab = 0.32f;
    private const float CornerGrab = 0.36f;

    public RcpTrackCanvas()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw
                 | ControlStyles.Selectable, true);

        DoubleBuffered = true;
        TabStop = true;

        _paceTimer.Tick += (_, _) => AdvancePaceCar();
    }

    public RcpTrackDesign Design
    {
        get => _design;
        set
        {
            _design = value ?? new RcpTrackDesign();
            _undo.Clear();
            _redo.Clear();
            ReloadBackdrop();
            RaiseChanged();
        }
    }

    public RcpTool Tool { get; set; } = RcpTool.Floor;
    public RcpPaletteItem Item { get; set; } = RcpPaletteItem.Floor;

    /// <summary>Radius of the next turn rail, in tiles.</summary>
    public float CurveRadius { get; set; } = 1f;

    public int MarkerTurns { get; set; }

    public bool ShowGrid { get; set; } = true;
    public bool ShowRoute { get; set; } = true;

    public double LapSeconds { get; set; } = 12;

    public bool IsPlaying => _paceTimer.Enabled;

    public event Action? DesignChanged;
    public event Action<string>? StatusChanged;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    // --- Undo ----------------------------------------------------------------

    private void Remember()
    {
        // Inside a drag, only the first change is worth recording. The user
        // made one stroke, so one press of Undo should take the whole
        // stroke back - and cloning the plan on every tile of a long drag
        // is work nobody asked for on top of that.
        if (_strokeOpen)
        {
            if (_strokeRemembered)
            {
                return;
            }

            _strokeRemembered = true;
        }

        _undo.Add(_design.Clone());

        if (_undo.Count > UndoDepth)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
    }

    /// <summary>
    /// The undo step for sizing the photo with the wheel.
    ///
    /// The wheel has no press and release to open and close a stroke with,
    /// and every notch is its own event - so a turn of the wheel is taken to
    /// be one gesture for as long as the notches keep coming and nothing
    /// else has been remembered, undone or redone in between. That is told
    /// by the snapshot this took still being the newest one on the undo
    /// list (Undo removes it, any other change puts a newer one on top, and
    /// Redo puts a fresh copy on top). Sizing used to take no undo step at
    /// all, so Ctrl+Z after sizing a photo took back whatever came before as
    /// well - usually the loading of the photo itself.
    /// </summary>
    private void RememberWheelResize()
    {
        var now = Environment.TickCount64;
        var sameGesture = _wheelResizeSnapshot is not null
                          && _undo.Count > 0
                          && ReferenceEquals(_undo[^1], _wheelResizeSnapshot)
                          && now - _wheelResizeAt < WheelResizeGapMs;

        _wheelResizeAt = now;

        if (sameGesture)
        {
            return;
        }

        // Mid-drag (the photo being dragged and the wheel turned at once)
        // the stroke already owns the undo step, so this is left to it.
        if (_strokeOpen)
        {
            Remember();
            _wheelResizeSnapshot = null;
            return;
        }

        Remember();
        _wheelResizeSnapshot = _undo.Count > 0 ? _undo[^1] : null;
    }

    /// <summary>How long a pause between wheel notches ends one sizing gesture, in milliseconds.</summary>
    private const long WheelResizeGapMs = 1000;

    /// <summary>The snapshot the current wheel-sizing gesture took, while it is still the newest.</summary>
    private RcpTrackDesign? _wheelResizeSnapshot;

    /// <summary>When the wheel last sized the photo (Environment.TickCount64).</summary>
    private long _wheelResizeAt;

    /// <summary>Opens a drag, so everything it changes becomes one undo step.</summary>
    private void BeginStroke()
    {
        _strokeOpen = true;
        _strokeRemembered = false;
    }

    private void EndStroke()
    {
        var wasOpen = _strokeOpen;

        _strokeOpen = false;
        _strokeRemembered = false;
        _lastPaintedCell = null;
        _lastErasedAt = null;
        _railAt = null;
        _railPrev = null;
        _railDir = null;
        _railStart = null;
        _railFirstDir = null;
        _railStraightRun = 0;
        _railLaid = false;
        _ctrlErasing = false;

        if (wasOpen)
        {
            // Everything the stroke changed, announced once.
            RaiseChanged();
        }
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Add(_design.Clone());
        ShowInstead(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);

        RaiseChanged();
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(_design.Clone());
        ShowInstead(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);

        RaiseChanged();
    }

    /// <summary>
    /// Shows a plan from Undo or Redo - with its photo. The photo used to be
    /// left exactly as it was, so undoing a photo brought back the name of
    /// the one before it and went on showing the new one (and undoing Clear
    /// photo brought the name back with nothing on screen).
    /// </summary>
    private void ShowInstead(RcpTrackDesign design)
    {
        var photoChanged = !string.Equals(design.BackdropPath, _design.BackdropPath, StringComparison.Ordinal);

        _design = design;

        if (photoChanged)
        {
            ReloadBackdrop();
        }
    }

    public void ClearAll()
    {
        Remember();

        _design.Tiles.Clear();
        _design.Walls.Clear();
        _design.Curves.Clear();
        _design.Decals.Clear();
        _design.Route.Clear();

        RaiseChanged();
    }

    private void RaiseChanged()
    {
        // The canvas always redraws - that is the feedback the drag is for.
        // The readouts beside it do not: they walk the whole plan to count
        // rails and measure the lap, and doing that again for every tile of
        // a long drag across a big layout is what makes an editor feel
        // heavy. They are told once, when the stroke ends.
        if (_strokeOpen)
        {
            Invalidate();
            return;
        }

        DesignChanged?.Invoke();
        Invalidate();
    }

    // --- The photo being traced ---------------------------------------------

    /// <summary>
    /// Puts a photo under the plan to trace over. Null once it is showing, or
    /// why it isn't - and then the plan is exactly as it was, the photo it
    /// already had included.
    ///
    /// Opened BEFORE the plan is touched. A damaged file used to go in first:
    /// the photo that was there was gone, the damaged one was saved in its
    /// place by OK, and Undo brought the old name back with the wrong picture
    /// still on screen.
    /// </summary>
    public string? LoadBackdrop(string path)
    {
        var (photo, problem) = DecodeBackdrop(path);

        if (photo is null)
        {
            return problem ?? "That photo couldn't be opened.";
        }

        Remember();

        _design.BackdropPath = path;
        _design.BackdropHidden = false;

        // Dropped roughly over whatever has been drawn already, so it lands
        // somewhere useful rather than at the origin.
        var bounds = _design.BoundsInTiles();

        _design.BackdropX = _design.IsEmpty ? 0 : bounds.Left;
        _design.BackdropY = _design.IsEmpty ? 0 : bounds.Top;
        _design.BackdropWidth = _design.IsEmpty ? 12f : Math.Max(4f, bounds.Width);

        _backdrop?.Dispose();
        _backdrop = photo;
        BackdropProblem = null;

        RaiseChanged();

        StatusChanged?.Invoke("Photo loaded - use the Photo tool to drag it into place and the wheel to size it, then trace over it.");
        return null;
    }

    public void ClearBackdrop()
    {
        Remember();

        _design.BackdropPath = string.Empty;
        _backdrop?.Dispose();
        _backdrop = null;

        RaiseChanged();
    }

    public void ToggleBackdrop()
    {
        // Hiding or showing the photo is its own undo step, like loading or
        // clearing it. Whether the photo is hidden is part of the plan, and
        // every undo snapshot carries it, so without a step of its own the
        // newest snapshot was the one taken before the last stroke. Ctrl+Z
        // after Hide then showed the photo again and also took back the
        // rail just laid.
        Remember();

        _design.BackdropHidden = !_design.BackdropHidden;
        RaiseChanged();
    }

    /// <summary>
    /// Why the plan's traced photo isn't shown, when it isn't - or null. Kept
    /// as well as said: the designer is built, and its toolbar sets the
    /// status line, after the plan is loaded, so a reason said only as it
    /// happened was never seen. The form shows it once it is built.
    /// </summary>
    public string? BackdropProblem { get; private set; }

    /// <summary>Says why the traced photo isn't shown, and keeps it - see BackdropProblem.</summary>
    private void BackdropNotShown(string reason)
    {
        BackdropProblem = reason;
        StatusChanged?.Invoke(reason);
    }

    private void ReloadBackdrop()
    {
        _backdrop?.Dispose();
        _backdrop = null;
        BackdropProblem = null;

        if (string.IsNullOrWhiteSpace(_design.BackdropPath))
        {
            return;
        }

        var (photo, problem) = DecodeBackdrop(_design.BackdropPath);

        if (photo is null)
        {
            BackdropNotShown(problem ?? "That image couldn't be opened.");
            return;
        }

        _backdrop = photo;
    }

    /// <summary>
    /// Opens a photo to trace over - or says why it can't be opened, leaving
    /// everything as it is. Nothing here touches the plan or what is on
    /// screen: LoadBackdrop needs to know whether a photo opens BEFORE it
    /// puts it in the plan.
    /// </summary>
    private static (Bitmap? Photo, string? Problem) DecodeBackdrop(string path)
    {
        // Never a path to another computer - not even to see whether it is
        // there. The path comes from track-layouts.json, which a restored
        // backup writes, and touching a network path makes Windows sign in
        // to that computer with this account's credentials (and can hang the
        // designer while it times out). Choose photo... only takes a photo on
        // this PC, so a plan made here never has one.
        if (PlainFileName.IsNetworkPath(path))
        {
            return (null, "The traced photo is on another computer, so it isn't shown. Copy it to this PC, then pick it again with the Choose photo button.");
        }

        if (!File.Exists(path))
        {
            return (null, "The traced photo isn't where it was any more, so it isn't shown. Pick it again with the Choose photo button.");
        }

        try
        {
            // Sized from its header before anything is decoded (see
            // ImageProbe), and decoded from the very bytes that were
            // checked. The path is kept with the plan, in track-layouts.json,
            // which a restored backup writes, and this runs as the designer
            // opens. It used to decode whatever the file held, so a huge
            // photo, or a small file whose header claims tens of thousands
            // of pixels each way, froze the designer (or crashed it out of
            // memory) every time that track was opened. Anything bigger
            // than Choose photo... takes in is left off, as an unreadable
            // one always was.
            if (ImageProbe.ReadStored(path) is not { } bytes)
            {
                return (null, "That image couldn't be opened.");
            }

            // Read into memory rather than held open, so the file isn't
            // locked for as long as the plan is open. The copy has to be a
            // Bitmap made while the stream is still open: an Image straight
            // from FromStream keeps reading the stream for as long as it
            // lives, and this one closes at the end of the block.
            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream);
            return (new Bitmap(decoded), null);
        }
        catch
        {
            return (null, "That image couldn't be opened.");
        }
    }

    // --- View ----------------------------------------------------------------

    public void Fit()
    {
        if (_design.IsEmpty && _backdrop is null)
        {
            _scale = 64f;
            _origin = new PointF(40, 40);
            Invalidate();
            return;
        }

        var bounds = _design.IsEmpty
            ? new RectangleF(_design.BackdropX, _design.BackdropY, _design.BackdropWidth, _design.BackdropWidth)
            : _design.BoundsInTiles();

        var margin = 1.2f;

        _scale = Math.Clamp(
            Math.Min(
                (Width - 24) / Math.Max(1f, bounds.Width + margin),
                (Height - 24) / Math.Max(1f, bounds.Height + margin)),
            6f, 260f);

        _origin = new PointF(
            (Width - bounds.Width * _scale) / 2f - bounds.Left * _scale,
            (Height - bounds.Height * _scale) / 2f - bounds.Top * _scale);

        Invalidate();
    }

    public void Zoom(float factor, Point? around = null)
    {
        var pivot = around ?? new Point(Width / 2, Height / 2);
        var before = ToTile(pivot);

        _scale = Math.Clamp(_scale * factor, 6f, 260f);

        _origin = new PointF(pivot.X - before.X * _scale, pivot.Y - before.Y * _scale);

        Invalidate();
    }

    private PointF ToTile(Point pixel) => new(
        (pixel.X - _origin.X) / _scale,
        (pixel.Y - _origin.Y) / _scale);

    private Point ToCell(Point pixel)
    {
        var tile = ToTile(pixel);
        return new Point((int)MathF.Floor(tile.X), (int)MathF.Floor(tile.Y));
    }

    /// <summary>The tile seam nearest the pointer, as a grid line.</summary>
    private (int Col, int Row, bool Vertical)? EdgeAt(Point pixel)
    {
        var tile = ToTile(pixel);

        var col = (int)MathF.Floor(tile.X);
        var row = (int)MathF.Floor(tile.Y);

        var dx = tile.X - col;
        var dy = tile.Y - row;

        var options = new (float Distance, int Col, int Row, bool Vertical)[]
        {
            (dy, col, row, false),
            (1 - dy, col, row + 1, false),
            (dx, col, row, true),
            (1 - dx, col + 1, row, true),
        };

        var best = options.OrderBy(o => o.Distance).First();

        return best.Distance > EdgeGrab ? null : (best.Col, best.Row, best.Vertical);
    }

    /// <summary>The grid point nearest the pointer, and which way a corner there would turn.</summary>
    private (Point At, RcpQuadrant Quadrant)? CornerAt(Point pixel)
    {
        var tile = ToTile(pixel);

        var col = (int)MathF.Round(tile.X);
        var row = (int)MathF.Round(tile.Y);

        var dx = tile.X - col;
        var dy = tile.Y - row;

        if (MathF.Sqrt(dx * dx + dy * dy) > CornerGrab)
        {
            return null;
        }

        var quadrant = dx >= 0
            ? dy >= 0 ? RcpQuadrant.SouthEast : RcpQuadrant.NorthEast
            : dy >= 0 ? RcpQuadrant.SouthWest : RcpQuadrant.NorthWest;

        return (new Point(col, row), quadrant);
    }

    /// <summary>The grid intersection nearest the pointer. Unlike CornerAt, this never gives up.</summary>
    private Point ToGridPoint(Point pixel)
    {
        var tile = ToTile(pixel);
        return new Point((int)MathF.Round(tile.X), (int)MathF.Round(tile.Y));
    }

    /// <summary>
    /// Every cell between two cells, so a quick drag lays a solid run
    /// rather than a dotted one.
    ///
    /// A mouse move arrives whenever Windows gets round to it, which on a
    /// fast sweep can be half a dozen tiles apart - painting only where the
    /// pointer happened to be sampled left gaps, and a diagonal sweep left
    /// a line of tiles touching only at their corners, which is not floor
    /// you could drive on. So a diagonal hop is broken into two square ones
    /// and the run comes out connected.
    /// </summary>
    private static IEnumerable<Point> CellsBetween(Point from, Point to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var steps = Math.Max(Math.Abs(dx), Math.Abs(dy));

        if (steps == 0)
        {
            yield return to;
            yield break;
        }

        var last = from;

        for (var i = 1; i <= steps; i++)
        {
            var fraction = i / (float)steps;

            var cell = new Point(
                from.X + (int)MathF.Round(dx * fraction),
                from.Y + (int)MathF.Round(dy * fraction));

            if (cell.X != last.X && cell.Y != last.Y)
            {
                yield return new Point(cell.X, last.Y);
            }

            yield return cell;
            last = cell;
        }
    }

    /// <summary>
    /// Points along a drag, close enough together that nothing is stepped
    /// over.
    ///
    /// The eraser works on whatever is under the pointer rather than on a
    /// cell, so it is sampled in pixels. A third of a tile is fine enough
    /// that a rail cannot hide between two samples, and coarse enough that
    /// a long sweep is a few dozen checks rather than a few thousand.
    /// </summary>
    private IEnumerable<Point> PixelsBetween(Point from, Point to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;

        var distance = MathF.Sqrt(dx * dx + dy * dy);
        var stride = Math.Max(4f, _scale / 3f);
        var steps = Math.Min(400, (int)MathF.Ceiling(distance / stride));

        if (steps <= 1)
        {
            yield return to;
            yield break;
        }

        for (var i = 1; i <= steps; i++)
        {
            var fraction = i / (float)steps;

            yield return new Point(
                from.X + (int)MathF.Round(dx * fraction),
                from.Y + (int)MathF.Round(dy * fraction));
        }
    }

    // --- Editing -------------------------------------------------------------

    private void PaintTile(Point cell)
    {
        var kind = Item == RcpPaletteItem.Blocked ? RcpTileKind.Infield : RcpTileKind.Straight;
        var existing = _design.At(cell.X, cell.Y);

        if (existing is not null && existing.Kind == kind)
        {
            return;
        }

        Remember();

        if (existing is null)
        {
            _design.Tiles.Add(new RcpTile { Col = cell.X, Row = cell.Y, Kind = kind });
        }
        else
        {
            existing.Kind = kind;
        }

        RaiseChanged();
    }

    /// <summary>
    /// Puts a rail on a seam, or changes the one that is there.
    ///
    /// Clicking the same seam again turns a single rail into a pair stood
    /// back to back, and again takes it away - so one control does place,
    /// upgrade and remove without going back to the palette.
    /// </summary>
    private void PlaceWall((int Col, int Row, bool Vertical) edge, bool cycle)
    {
        var existing = _design.WallAt(edge.Col, edge.Row, edge.Vertical);
        var wantDouble = Item == RcpPaletteItem.Divider;

        if (existing is null)
        {
            Remember();
            _design.Walls.Add(new RcpWall
            {
                Col = edge.Col,
                Row = edge.Row,
                Vertical = edge.Vertical,
                Double = wantDouble,
            });

            RaiseChanged();
            return;
        }

        if (!cycle)
        {
            return;
        }

        Remember();

        if (!existing.Double)
        {
            existing.Double = true;
            StatusChanged?.Invoke("Two rails back to back - racing on both sides.");
        }
        else
        {
            _design.Walls.Remove(existing);
        }

        RaiseChanged();
    }

    // --- Dragging a run of rail ---------------------------------------------

    /// <summary>
    /// Carries the run of rail as far as the pointer, putting a turn rail in
    /// wherever it changes direction.
    ///
    /// This is the part that makes drawing a circuit feel like drawing
    /// rather than clicking: hold the button down, go round the shape of
    /// the track, and the corners appear where you turned. On a real track
    /// that is exactly what you do - you run straight rail until the
    /// direction changes and then reach for a curved piece - so the editor
    /// reaching for it on your behalf is the same job done for you rather
    /// than a different one.
    /// </summary>
    private void ExtendRailRun(Point to)
    {
        if (_railAt is not { } anchor)
        {
            _railAt = to;
            return;
        }

        // The pointer can cross several intersections between two mouse
        // messages, and it can cross them diagonally - a quick sweep across
        // a zoomed-out plan easily jumps five or six. So one axis is run
        // out completely and then the other, which puts ONE corner where
        // the two meet. Taking a step of each in turn instead would climb
        // the diagonal as a staircase and leave a dozen little corners
        // behind for a drag the user meant as two straights.
        var horizontalFirst = RunHorizontalFirst(anchor, to);

        Walk(horizontalFirst);
        Walk(!horizontalFirst);

        void Walk(bool horizontal)
        {
            // The guard is for arithmetic, not for users. A drag across the
            // biggest plan anyone will draw is a few hundred seams; beyond
            // that something has gone wrong and the loop should stop rather
            // than lock up the editor.
            var guard = 0;

            while (_railAt is { } at && guard++ < 600)
            {
                var step = horizontal
                    ? new Point(Math.Sign(to.X - at.X), 0)
                    : new Point(0, Math.Sign(to.Y - at.Y));

                if (step is { X: 0, Y: 0 })
                {
                    return;
                }

                StepRail(at, step);
            }
        }
    }

    /// <summary>
    /// Which axis a jump should be run out along first.
    ///
    /// A run already travelling one way carries on that way while there is
    /// still ground to cover on it. Following a wall and then turning down
    /// is then the one corner the user drew, rather than a corner at
    /// whichever end the arithmetic happened to prefer.
    /// </summary>
    private bool RunHorizontalFirst(Point from, Point to)
    {
        if (_railDir is { } heading)
        {
            if (heading.X != 0 && to.X != from.X)
            {
                return true;
            }

            if (heading.Y != 0 && to.Y != from.Y)
            {
                return false;
            }
        }

        return Math.Abs(to.X - from.X) >= Math.Abs(to.Y - from.Y);
    }

    /// <summary>One seam of the run, which is either more straight or a corner.</summary>
    private void StepRail(Point at, Point step)
    {
        var next = new Point(at.X + step.X, at.Y + step.Y);

        if (_railDir is { } previous && previous != step && _railPrev is { } from)
        {
            // Doubling back on itself is not a corner, it is a mistake -
            // and there is no rail shaped like it. Treat it as the start of
            // a fresh run so the pointer can be dragged back over a line
            // without the editor inventing something.
            if (previous.X == -step.X && previous.Y == -step.Y)
            {
                LayStraight(at, next);
            }
            else if (_railStraightRun >= 2)
            {
                TurnRailRun(from, at, next);
            }
            else
            {
                DeclineTurn(at, next);
            }

            _railStraightRun = 1;
        }
        else
        {
            LayStraight(at, next);
            _railStraightRun = _railDir == step ? _railStraightRun + 1 : 1;
        }

        _railStart ??= at;
        _railFirstDir ??= step;

        _railPrev = at;
        _railDir = step;
        _railAt = next;
        _railLaid = true;
    }

    /// <summary>
    /// Leaves a corner square, because a turn rail would not fit there.
    ///
    /// A one-tile turn rail takes up a tile of the run on the way in and a
    /// tile on the way out, so two of them cannot share the same tile. Put
    /// one in anyway and the two arcs meet nose to nose at right angles -
    /// a shape that draws, but that could not be built out of anything RCP
    /// sells, which is worse than an honest square corner.
    ///
    /// So the straight goes in, the corner stays square, and the user is
    /// told why once. Widening the drag by a tile gets the turn rail; a
    /// tighter corner than that is a deliberate choice, and the curve tool
    /// with a half-tile radius is where it is made.
    /// </summary>
    private void DeclineTurn(Point at, Point next)
    {
        LayStraight(at, next);

        if (_toldAboutTightCorners)
        {
            return;
        }

        _toldAboutTightCorners = true;

        StatusChanged?.Invoke(
            "Left that corner square - a turn rail needs a tile of straight either side of it. "
            + "Give the corner one more tile, or set a tighter radius and place it with the curve tool.");
    }

    /// <summary>
    /// Rounds off the last corner of a run brought back to where it started.
    ///
    /// Every other corner is made by the step that leaves it, so the one the
    /// run finishes on has nothing to make it - drag a rectangle and three
    /// corners come out curved and the fourth a right angle, which is not a
    /// track. If the pointer has come back to the point it set off from,
    /// the direction it first left in IS the onward arm, so the corner can
    /// be finished the same way as the rest.
    /// </summary>
    private void CloseRailRun()
    {
        if (_railAt is not { } at
            || _railStart is not { } start
            || _railFirstDir is not { } first
            || _railPrev is not { } from
            || _railDir is not { } last
            || at != start)
        {
            return;
        }

        // Straight on through the start point, or doubling back over the
        // first rail - neither is a corner.
        if (last == first || (last.X == -first.X && last.Y == -first.Y))
        {
            return;
        }

        // Same rule as every other corner: it needs the tile to sit on.
        if (_railStraightRun < 2)
        {
            return;
        }

        TurnRailRun(from, at, new Point(at.X + first.X, at.Y + first.Y));
    }

    /// <summary>
    /// Swaps the corner of the run for a turn rail.
    ///
    /// A turn rail of one tile's radius reaches exactly one tile along each
    /// arm, so it stands where the last straight was and where the next one
    /// would have gone. Both of those are taken out of the way - leaving
    /// them in would put a straight rail inside the arc, which is a piece
    /// nobody has and a track nobody could drive.
    ///
    /// Always a one-tile radius, whatever the palette is set to. This is
    /// the corner piece that comes in the box; a sweeper or a hairpin is a
    /// deliberate choice, so it stays a deliberate click with the curve
    /// tool.
    /// </summary>
    private void TurnRailRun(Point from, Point at, Point next)
    {
        Remember();

        RemoveStraight(from, at);
        RemoveStraight(at, next);

        var arm = new Point(from.X - at.X, from.Y - at.Y);
        var onward = new Point(next.X - at.X, next.Y - at.Y);

        var quadrant = QuadrantOf(arm, onward);

        if (_design.CurveAt(at.X, at.Y) is { } existing)
        {
            existing.Quadrant = quadrant;
            existing.Radius = 1f;
            existing.Double = Item == RcpPaletteItem.Divider;
        }
        else
        {
            _design.Curves.Add(new RcpCurve
            {
                Col = at.X,
                Row = at.Y,
                Quadrant = quadrant,
                Radius = 1f,
                Double = Item == RcpPaletteItem.Divider,
            });
        }

        StatusChanged?.Invoke("Turn rail dropped in where the run changed direction.");
        RaiseChanged();
    }

    /// <summary>
    /// Which way a corner bulges, given the two directions its arms leave in.
    ///
    /// One arm is always horizontal and the other always vertical - a
    /// corner joining two parallel arms is not a corner - so the quadrant
    /// is just which side each of them went.
    /// </summary>
    private static RcpQuadrant QuadrantOf(Point armA, Point armB)
    {
        var east = armA.X > 0 || armB.X > 0;
        var south = armA.Y > 0 || armB.Y > 0;

        return east
            ? south ? RcpQuadrant.SouthEast : RcpQuadrant.NorthEast
            : south ? RcpQuadrant.SouthWest : RcpQuadrant.NorthWest;
    }

    /// <summary>Stands a rail on the seam between two neighbouring grid points.</summary>
    private void LayStraight(Point a, Point b)
    {
        // A rail is held by the lower-numbered end of the seam it sits on,
        // so which way the pointer happened to be travelling makes no
        // difference to the piece that goes in.
        var vertical = a.X == b.X;

        PlaceWall((Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), vertical), cycle: false);
    }

    /// <summary>Takes a rail back off a seam, if one is on it.</summary>
    private void RemoveStraight(Point a, Point b)
    {
        var vertical = a.X == b.X;

        if (_design.WallAt(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), vertical) is { } wall)
        {
            _design.Walls.Remove(wall);
        }
    }

    private void PlaceCurve((Point At, RcpQuadrant Quadrant) corner, bool cycle)
    {
        var existing = _design.CurveAt(corner.At.X, corner.At.Y);

        if (existing is null)
        {
            Remember();

            _design.Curves.Add(new RcpCurve
            {
                Col = corner.At.X,
                Row = corner.At.Y,
                Quadrant = corner.Quadrant,
                Radius = CurveRadius,
                Double = Item == RcpPaletteItem.Divider,
            });

            RaiseChanged();
            return;
        }

        if (!cycle)
        {
            return;
        }

        Remember();

        // Clicking it again turns it to face where the pointer is; clicking
        // it in the same quadrant takes it away.
        if (existing.Quadrant != corner.Quadrant)
        {
            existing.Quadrant = corner.Quadrant;
        }
        else if (Math.Abs(existing.Radius - CurveRadius) > 0.01f)
        {
            existing.Radius = CurveRadius;
        }
        else
        {
            _design.Curves.Remove(existing);
        }

        RaiseChanged();
    }

    private void PlaceMarker(Point cell)
    {
        var kind = Item switch
        {
            RcpPaletteItem.FinishLine => RcpDecalKind.FinishLine,
            RcpPaletteItem.GridBox => RcpDecalKind.GridBox,
            RcpPaletteItem.Post => RcpDecalKind.Post,
            RcpPaletteItem.Kerb => RcpDecalKind.Kerb,
            RcpPaletteItem.Split1 => RcpDecalKind.SplitLoop1,
            RcpPaletteItem.Split2 => RcpDecalKind.SplitLoop2,
            RcpPaletteItem.Split3 => RcpDecalKind.SplitLoop3,
            _ => RcpDecalKind.Furniture,
        };

        var existing = _design.Decals.FirstOrDefault(d => d.Col == cell.X && d.Row == cell.Y);

        Remember();

        // One of each split loop on a plan: placed somewhere else, it moves
        // there - a track has one Split 1.
        if (RcpPlan.SplitNumberOf(kind) > 0)
        {
            _design.Decals.RemoveAll(d => d is not null && d.Kind == kind && !(d.Col == cell.X && d.Row == cell.Y));
        }

        if (existing is null)
        {
            _design.Decals.Add(new RcpDecal { Col = cell.X, Row = cell.Y, Kind = kind, Turns = MarkerTurns });
        }
        else if (existing.Kind != kind)
        {
            existing.Kind = kind;
            existing.Turns = MarkerTurns;
        }
        else
        {
            existing.Turns = (existing.Turns + 1) % 4;
            MarkerTurns = existing.Turns;
        }

        RaiseChanged();
    }

    /// <summary>Takes off whatever is nearest: a curve, then a rail, then a marking, then the floor.</summary>
    private void EraseAt(Point pixel)
    {
        if (CornerAt(pixel) is { } corner && _design.CurveAt(corner.At.X, corner.At.Y) is { } curve)
        {
            Remember();
            _design.Curves.Remove(curve);
            RaiseChanged();
            return;
        }

        if (EdgeAt(pixel) is { } edge && _design.WallAt(edge.Col, edge.Row, edge.Vertical) is { } wall)
        {
            Remember();
            _design.Walls.Remove(wall);
            RaiseChanged();
            return;
        }

        var cell = ToCell(pixel);

        if (_design.Decals.FirstOrDefault(d => d.Col == cell.X && d.Row == cell.Y) is { } decal)
        {
            Remember();
            _design.Decals.Remove(decal);
            RaiseChanged();
            return;
        }

        if (_design.At(cell.X, cell.Y) is { } tile)
        {
            Remember();
            _design.Tiles.Remove(tile);
            RaiseChanged();
        }
    }

    /// <summary>Rails all the way round the outside of the floor, in one go.</summary>
    public void WallThePerimeter()
    {
        Remember();

        var added = RcpPlan.WallThePerimeter(_design);

        StatusChanged?.Invoke(added == 0
            ? "The floor is already walled in."
            : $"{Plural.Of(added, "rail")} added round the edge of the floor.");

        RaiseChanged();
    }

    private void AddRoutePoint(Point pixel)
    {
        var tile = ToTile(pixel);

        var snapped = new PointF(
            MathF.Round(tile.X * 2f) / 2f,
            MathF.Round(tile.Y * 2f) / 2f);

        Remember();
        _design.Route.Add(new RcpRoutePoint { X = snapped.X, Y = snapped.Y });
        RaiseChanged();
    }

    private int RoutePointAt(Point pixel)
    {
        for (var i = 0; i < _design.Route.Count; i++)
        {
            var point = _design.Route[i].Point;
            var screen = new PointF(_origin.X + point.X * _scale, _origin.Y + point.Y * _scale);

            if (Math.Abs(screen.X - pixel.X) <= 8 && Math.Abs(screen.Y - pixel.Y) <= 8)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Turns the racing line round, keeping where it starts: the cars run
    /// the other way. Sectors are measured in the line's direction
    /// (RcpPlan.MeasureSectors), so a line traced or drawn against the cars
    /// would otherwise swap them.
    /// </summary>
    public void ReverseRoute()
    {
        if (_design.Route.Count < 2)
        {
            StatusChanged?.Invoke("Draw the racing line first.");
            return;
        }

        Remember();

        var first = _design.Route[0];
        var rest = _design.Route.Skip(1).Reverse().ToList();

        // A closed lap keeps its first point and runs round the other way;
        // an open line simply runs from its other end.
        _design.Route = _design.RouteClosed
            ? new[] { first }.Concat(rest).ToList()
            : _design.Route.AsEnumerable().Reverse().ToList();

        StatusChanged?.Invoke("The racing line now runs the other way - its arrows show which.");
        RaiseChanged();
    }

    public void AutoRoute()
    {
        var route = RcpPlan.AutoRoute(_design, out var problem);

        if (route is null || route.Count < 3)
        {
            StatusChanged?.Invoke(string.IsNullOrEmpty(problem)
                ? "Couldn't work out a route - draw it with the Route tool."
                : problem);
            return;
        }

        Remember();

        _design.Route = route.Select(p => new RcpRoutePoint { X = p.X, Y = p.Y }).ToList();

        // A lap only when the walk came back round to where it started -
        // which is the one case RcpPlan.AutoRoute reports no problem for.
        // When the corridor dead-ends or doubles back it hands back the part
        // it managed, and calling that closed drew a leg straight across the
        // rails from its end to its start and counted that leg in the lap
        // length, the speed readout and the pace car. The side panel's "The
        // line closes into a lap" box follows this (SyncSidePanel).
        var closed = string.IsNullOrEmpty(problem);

        _design.RouteClosed = closed;

        StatusChanged?.Invoke(closed
            ? $"Route traced through {route.Count} tiles."
            : $"{problem} The line is left open.");

        RaiseChanged();
    }

    // --- Pace car -------------------------------------------------------------

    public void TogglePlay()
    {
        if (_paceTimer.Enabled)
        {
            _paceTimer.Stop();
        }
        else
        {
            _paceLastTick = DateTime.UtcNow;
            _paceTimer.Start();
        }

        Invalidate();
    }

    public void StopPlaying() => _paceTimer.Stop();

    private void AdvancePaceCar()
    {
        var length = RcpPlan.RouteLengthInTiles(_design);

        if (length <= 0)
        {
            _paceTimer.Stop();
            return;
        }

        var now = DateTime.UtcNow;
        var elapsed = (float)(now - _paceLastTick).TotalSeconds;
        _paceLastTick = now;

        _paceDistance = (_paceDistance + length * elapsed / (float)Math.Max(1.0, LapSeconds)) % length;

        Invalidate();
    }

    private (PointF Point, float Heading)? PaceCarPosition()
    {
        if (_design.Route.Count < 2)
        {
            return null;
        }

        var points = _design.Route.Select(p => p.Point).ToList();

        if (_design.RouteClosed)
        {
            points.Add(points[0]);
        }

        var travelled = 0f;

        for (var i = 1; i < points.Count; i++)
        {
            var segment = RcpPlan.Distance(points[i - 1], points[i]);

            if (segment <= 0)
            {
                continue;
            }

            if (travelled + segment >= _paceDistance)
            {
                var fraction = (_paceDistance - travelled) / segment;

                var at = new PointF(
                    points[i - 1].X + (points[i].X - points[i - 1].X) * fraction,
                    points[i - 1].Y + (points[i].Y - points[i - 1].Y) * fraction);

                var heading = MathF.Atan2(points[i].Y - points[i - 1].Y, points[i].X - points[i - 1].X) * 180f / MathF.PI;

                return (at, heading);
            }

            travelled += segment;
        }

        return (points[0], 0f);
    }

    // --- Input ----------------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button == MouseButtons.Middle
            || (e.Button == MouseButtons.Right && ModifierKeys.HasFlag(Keys.Shift)))
        {
            _panning = true;
            _panFrom = e.Location;
            _panOrigin = _origin;
            Cursor = Cursors.SizeAll;
            return;
        }

        if (e.Button == MouseButtons.Right)
        {
            if (Tool == RcpTool.Route)
            {
                var index = RoutePointAt(e.Location);

                if (index >= 0)
                {
                    Remember();
                    _design.Route.RemoveAt(index);
                    RaiseChanged();
                    return;
                }
            }

            EraseAt(e.Location);
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (ModifierKeys.HasFlag(Keys.Control))
        {
            _painting = true;
            _ctrlErasing = true;
            BeginStroke();
            _lastErasedAt = e.Location;
            EraseAt(e.Location);
            return;
        }

        switch (Tool)
        {
            case RcpTool.Floor:
                _painting = true;
                BeginStroke();
                _lastPaintedCell = ToCell(e.Location);
                PaintTile(_lastPaintedCell.Value);
                break;

            case RcpTool.Wall:
                _painting = true;
                BeginStroke();

                // Nothing is laid yet. The run is anchored at the nearest
                // intersection and waits to see whether this turns into a
                // drag - if it doesn't, the mouse-up treats it as a click
                // on the seam, which is what it has always done.
                _railAt = ToGridPoint(e.Location);
                _railPrev = null;
                _railDir = null;
                _railStart = null;
                _railFirstDir = null;
                _railStraightRun = 0;
                _railLaid = false;
                break;

            case RcpTool.Curve:
                if (CornerAt(e.Location) is { } corner)
                {
                    PlaceCurve(corner, cycle: true);
                }

                break;

            case RcpTool.Marker:
                PlaceMarker(ToCell(e.Location));
                break;

            case RcpTool.Erase:
                _painting = true;
                BeginStroke();
                _lastErasedAt = e.Location;
                EraseAt(e.Location);
                break;

            case RcpTool.Backdrop:
                _panning = false;
                _painting = true;

                // One drag of the photo is one undo step, however far it
                // goes: the move's first change remembers the plan and the
                // rest of the drag adds nothing (see OnMouseMove).
                BeginStroke();
                _panFrom = e.Location;
                _backdropFrom = new PointF(_design.BackdropX, _design.BackdropY);
                break;

            case RcpTool.Route:
                _draggingRoutePoint = RoutePointAt(e.Location);

                if (_draggingRoutePoint < 0)
                {
                    AddRoutePoint(e.Location);
                }
                else
                {
                    // Dragging a point that is already there is an edit of
                    // its own, so it gets an undo step of its own: one for
                    // the whole drag, taken when the point first moves. It
                    // used to take none, so Ctrl+Z after adding a point and
                    // then moving it took the point away altogether rather
                    // than putting it back where it was.
                    BeginStroke();
                }

                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_panning)
        {
            _origin = new PointF(_panOrigin.X + (e.X - _panFrom.X), _panOrigin.Y + (e.Y - _panFrom.Y));
            Invalidate();
            return;
        }

        // Not while Ctrl is erasing. Ctrl+drag erases with any tool, the
        // Photo tool included, and the press that started it never got as
        // far as the Photo tool's own setup - so the anchor this would move
        // the photo from was whatever an earlier drag or pan left behind.
        // The photo used to leap there and follow the mouse, and nothing
        // past the first point was erased.
        if (_painting && !_ctrlErasing && Tool == RcpTool.Backdrop && e.Button == MouseButtons.Left)
        {
            var x = _backdropFrom.X + (e.X - _panFrom.X) / _scale;
            var y = _backdropFrom.Y + (e.Y - _panFrom.Y) / _scale;

            // The move is its own undo step, taken the first time the photo
            // actually goes anywhere (once per drag - the stroke opened on
            // the press sees to that), so a click that leaves it where it
            // was adds nothing to undo. Without it, Ctrl+Z after placing a
            // photo took back whatever came before as well - usually the
            // loading of the photo, so it vanished instead of moving back.
            if (x != _design.BackdropX || y != _design.BackdropY)
            {
                Remember();
            }

            _design.BackdropX = x;
            _design.BackdropY = y;
            Invalidate();
            return;
        }

        var cell = ToCell(e.Location);
        var changed = _hoverCell != cell;

        _hoverCell = cell;

        var edge = Tool is RcpTool.Wall or RcpTool.Erase ? EdgeAt(e.Location) : null;
        var corner = Tool is RcpTool.Curve or RcpTool.Erase ? CornerAt(e.Location) : null;

        if (!Equals(edge, _hoverEdge))
        {
            _hoverEdge = edge;
            changed = true;
        }

        if (!Equals(corner, _hoverCorner))
        {
            _hoverCorner = corner;
            changed = true;
        }

        if (_draggingRoutePoint >= 0 && _draggingRoutePoint < _design.Route.Count)
        {
            var tile = ToTile(e.Location);
            var point = _design.Route[_draggingRoutePoint];
            var x = MathF.Round(tile.X * 2f) / 2f;
            var y = MathF.Round(tile.Y * 2f) / 2f;

            // Only a real move is remembered (once per drag - the stroke
            // opened on the press sees to that), so a click on a point that
            // leaves it in place adds nothing to undo.
            if (x != point.X || y != point.Y)
            {
                Remember();
            }

            point.X = x;
            point.Y = y;

            RaiseChanged();
            return;
        }

        if (_painting && _ctrlErasing && e.Button == MouseButtons.Left)
        {
            foreach (var step in PixelsBetween(_lastErasedAt ?? e.Location, e.Location))
            {
                EraseAt(step);
            }

            _lastErasedAt = e.Location;
            return;
        }

        if (_painting && e.Button == MouseButtons.Left)
        {
            switch (Tool)
            {
                case RcpTool.Floor:
                    // Filled in from wherever the brush last was, so how
                    // fast the drag went makes no difference to what comes
                    // out of it.
                    foreach (var step in CellsBetween(_lastPaintedCell ?? cell, cell))
                    {
                        PaintTile(step);
                    }

                    _lastPaintedCell = cell;
                    break;

                case RcpTool.Wall:
                    // No cycling on a drag: running along a line should lay
                    // rail, not toggle what it passes over.
                    ExtendRailRun(ToGridPoint(e.Location));
                    break;

                case RcpTool.Erase:
                    foreach (var step in PixelsBetween(_lastErasedAt ?? e.Location, e.Location))
                    {
                        EraseAt(step);
                    }

                    _lastErasedAt = e.Location;
                    break;
            }

            return;
        }

        if (changed)
        {
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_painting && Tool == RcpTool.Backdrop)
        {
            RaiseChanged();
        }

        // A press and release with the rail tool that never went anywhere
        // is a click, and a click on a seam still does what it always did:
        // puts a rail on, doubles it up, takes it away.
        if (_painting && !_ctrlErasing && Tool == RcpTool.Wall && e.Button == MouseButtons.Left)
        {
            if (_railLaid)
            {
                CloseRailRun();
            }
            else if (EdgeAt(e.Location) is { } edge)
            {
                PlaceWall(edge, cycle: true);
            }
        }

        EndStroke();

        _painting = false;
        _draggingRoutePoint = -1;

        if (_panning)
        {
            _panning = false;
            Cursor = Cursors.Default;
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        _hoverCell = null;
        _hoverEdge = null;
        _hoverCorner = null;
        Invalidate();
    }

    /// <summary>
    /// Closes off a drag that ended without a mouse-up.
    ///
    /// Alt-Tab away mid-drag, or let a dialog steal the mouse, and the
    /// button release goes somewhere else. Without this the editor would
    /// still think a stroke was open, which means the readouts beside the
    /// plan stop updating and the next change lands in the same undo step
    /// as the abandoned one.
    /// </summary>
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);

        // A route point being dragged counts too: that drag opens a stroke
        // of its own for its undo step, without setting _painting.
        if (Capture || (!_painting && _draggingRoutePoint < 0))
        {
            return;
        }

        EndStroke();

        _painting = false;
        _draggingRoutePoint = -1;

        if (_panning)
        {
            _panning = false;
            Cursor = Cursors.Default;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (Tool == RcpTool.Backdrop && _backdrop is not null)
        {
            // The wheel sizes the photo rather than the view, so it can be
            // matched to the grid without the grid moving underneath it.
            var width = Math.Clamp(_design.BackdropWidth * (e.Delta > 0 ? 1.04f : 1 / 1.04f), 1f, 200f);

            if (width != _design.BackdropWidth)
            {
                RememberWheelResize();
            }

            _design.BackdropWidth = width;
            RaiseChanged();
            return;
        }

        Zoom(e.Delta > 0 ? 1.12f : 1 / 1.12f, e.Location);
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.KeyCode)
        {
            case Keys.R:
                MarkerTurns = (MarkerTurns + 1) % 4;
                StatusChanged?.Invoke($"Markings turned to {MarkerTurns * 90} degrees.");
                Invalidate();
                e.Handled = true;
                break;

            case Keys.Z when e.Control:
                Undo();
                e.Handled = true;
                break;

            case Keys.Y when e.Control:
                Redo();
                e.Handled = true;
                break;

            case Keys.F:
                Fit();
                e.Handled = true;
                break;

            case Keys.G:
                ShowGrid = !ShowGrid;
                Invalidate();
                e.Handled = true;
                break;
        }
    }

    // --- Paint ----------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (var background = new SolidBrush(AppTheme.Background))
        {
            g.FillRectangle(background, ClientRectangle);
        }

        if (ShowGrid)
        {
            DrawGrid(g);
        }

        RcpTrackPainter.Draw(g, _design, _scale, _origin, ShowRoute, showLabels: true, backdrop: _backdrop);

        DrawHover(g);

        if (_paceTimer.Enabled && PaceCarPosition() is { } pace)
        {
            RcpTrackPainter.DrawPaceCar(
                g,
                new PointF(_origin.X + pace.Point.X * _scale, _origin.Y + pace.Point.Y * _scale),
                pace.Heading,
                _scale);
        }

        DrawRuler(g);
    }

    private void DrawGrid(Graphics g)
    {
        if (_scale < 12f)
        {
            return;
        }

        var dot = Math.Max(1f, _scale * 0.03f);

        using var brush = new SolidBrush(Color.FromArgb(70, AppTheme.TextMuted));

        var firstCol = (int)MathF.Floor(-_origin.X / _scale);
        var firstRow = (int)MathF.Floor(-_origin.Y / _scale);
        var cols = (int)(Width / _scale) + 2;
        var rows = (int)(Height / _scale) + 2;

        for (var col = firstCol; col <= firstCol + cols; col++)
        {
            for (var row = firstRow; row <= firstRow + rows; row++)
            {
                var x = _origin.X + col * _scale;
                var y = _origin.Y + row * _scale;

                g.FillEllipse(brush, x - dot / 2f, y - dot / 2f, dot, dot);
            }
        }
    }

    private void DrawHover(Graphics g)
    {
        switch (Tool)
        {
            case RcpTool.Wall when _hoverEdge is { } edge:
            {
                var from = new PointF(_origin.X + edge.Col * _scale, _origin.Y + edge.Row * _scale);

                var to = edge.Vertical
                    ? new PointF(from.X, from.Y + _scale)
                    : new PointF(from.X + _scale, from.Y);

                using var pen = new Pen(Color.FromArgb(200, AppTheme.Accent), Math.Max(3f, _scale * RcpTrackPainter.RailThickness))
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };

                g.DrawLine(pen, from, to);
                break;
            }

            case RcpTool.Curve when _hoverCorner is { } corner:
            {
                var point = new PointF(_origin.X + corner.At.X * _scale, _origin.Y + corner.At.Y * _scale);
                var radius = CurveRadius * _scale;

                var (dx, dy) = corner.Quadrant switch
                {
                    RcpQuadrant.NorthEast => (1f, -1f),
                    RcpQuadrant.SouthEast => (1f, 1f),
                    RcpQuadrant.SouthWest => (-1f, 1f),
                    _ => (-1f, -1f),
                };

                var start = corner.Quadrant switch
                {
                    RcpQuadrant.NorthEast => 90f,
                    RcpQuadrant.SouthEast => 180f,
                    RcpQuadrant.SouthWest => 270f,
                    _ => 0f,
                };

                var centre = new PointF(point.X + dx * radius, point.Y + dy * radius);

                using var pen = new Pen(Color.FromArgb(190, AppTheme.Accent), Math.Max(3f, _scale * RcpTrackPainter.RailThickness));
                g.DrawArc(pen, centre.X - radius, centre.Y - radius, radius * 2f, radius * 2f, start, 90f);

                using var dot = new SolidBrush(AppTheme.Accent);
                g.FillEllipse(dot, point.X - 3, point.Y - 3, 6, 6);
                break;
            }

            case RcpTool.Floor or RcpTool.Marker or RcpTool.Erase when _hoverCell is { } cell:
            {
                var rect = new RectangleF(_origin.X + cell.X * _scale, _origin.Y + cell.Y * _scale, _scale, _scale);

                var colour = Tool == RcpTool.Erase ? AppTheme.Danger : AppTheme.Accent;

                using var wash = new SolidBrush(Color.FromArgb(60, colour));
                using var outline = new Pen(colour, 2f) { DashStyle = DashStyle.Dash };

                g.FillRectangle(wash, rect);
                g.DrawRectangle(outline, rect.Left, rect.Top, rect.Width, rect.Height);
                break;
            }
        }
    }

    private void DrawRuler(Graphics g)
    {
        var metresPerTile = _design.TileSizeMm / 1000.0;

        if (metresPerTile <= 0)
        {
            return;
        }

        var barPixels = (float)(1.0 / metresPerTile) * _scale;

        if (barPixels < 24 || barPixels > Width - 60)
        {
            return;
        }

        var y = Height - 22;
        var left = 16f;

        using var pen = new Pen(AppTheme.TextMuted, 2f);
        using var brush = new SolidBrush(AppTheme.TextMuted);

        g.DrawLine(pen, left, y, left + barPixels, y);
        g.DrawLine(pen, left, y - 5, left, y + 5);
        g.DrawLine(pen, left + barPixels, y - 5, left + barPixels, y + 5);

        g.DrawString("1 m", AppTheme.SmallFont, brush, left + barPixels + 6, y - 9);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _paceTimer.Stop();
            _paceTimer.Dispose();
            _backdrop?.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>Names, blurbs and sample drawings for the palette.</summary>
public static class RcpTiles
{
    public static string NameOf(RcpPaletteItem item) => item switch
    {
        RcpPaletteItem.Floor => "Floor tile",
        RcpPaletteItem.Blocked => "Blocked out",
        RcpPaletteItem.Rail => "Rail",
        RcpPaletteItem.Divider => "Divider (two rails)",
        RcpPaletteItem.Turn => "Turn rail",
        RcpPaletteItem.FinishLine => "Finish line",
        RcpPaletteItem.GridBox => "Grid box",
        RcpPaletteItem.Post => "Rail post",
        RcpPaletteItem.Kerb => "Apex kerb",
        RcpPaletteItem.Split1 => "Split 1 loop",
        RcpPaletteItem.Split2 => "Split 2 loop",
        RcpPaletteItem.Split3 => "Split 3 loop",
        _ => "Rostrum / table",
    };

    public static string HintOf(RcpPaletteItem item) => item switch
    {
        RcpPaletteItem.Floor => "The foam. Drag to lay the whole floor first.",
        RcpPaletteItem.Blocked => "Floor you can't use - a pillar, a table leg.",
        RcpPaletteItem.Rail => "Drag to run rail. Corners turn themselves.",
        RcpPaletteItem.Divider => "Two back to back, for racing on both sides.",
        RcpPaletteItem.Turn => "A corner by hand, at whatever radius you set.",
        RcpPaletteItem.FinishLine => "The chequer across the track.",
        RcpPaletteItem.GridBox => "One staggered starting box.",
        RcpPaletteItem.Post => "The pink disc.",
        RcpPaletteItem.Kerb => "Red and white, for marking an apex.",
        RcpPaletteItem.Split1 or RcpPaletteItem.Split2 or RcpPaletteItem.Split3 =>
            "Where a split loop lies across the track. With the racing line through it, it measures each sector for sector speeds.",
        _ => "Anything else standing on the floor.",
    };

    public static RcpTool ToolFor(RcpPaletteItem item) => item switch
    {
        RcpPaletteItem.Floor or RcpPaletteItem.Blocked => RcpTool.Floor,
        RcpPaletteItem.Rail or RcpPaletteItem.Divider => RcpTool.Wall,
        RcpPaletteItem.Turn => RcpTool.Curve,
        _ => RcpTool.Marker,
    };

    public static IReadOnlyList<RcpPaletteItem> Floor { get; } = new[]
    {
        RcpPaletteItem.Floor,
        RcpPaletteItem.Blocked,
    };

    public static IReadOnlyList<RcpPaletteItem> Barriers { get; } = new[]
    {
        RcpPaletteItem.Rail,
        RcpPaletteItem.Divider,
        RcpPaletteItem.Turn,
    };

    public static IReadOnlyList<RcpPaletteItem> Markings { get; } = new[]
    {
        RcpPaletteItem.FinishLine,
        RcpPaletteItem.GridBox,
        RcpPaletteItem.Post,
        RcpPaletteItem.Kerb,
        RcpPaletteItem.Furniture,
        RcpPaletteItem.Split1,
        RcpPaletteItem.Split2,
        RcpPaletteItem.Split3,
    };

    /// <summary>
    /// A one-tile plan showing what this item looks like.
    ///
    /// Built out of the real thing and drawn by the real painter, so the
    /// palette can never show something the canvas wouldn't draw.
    /// </summary>
    public static RcpTrackDesign Sample(RcpPaletteItem item)
    {
        var design = new RcpTrackDesign { TileSizeMm = 500 };

        design.Tiles.Add(new RcpTile
        {
            Col = 0,
            Row = 0,
            Kind = item == RcpPaletteItem.Blocked ? RcpTileKind.Infield : RcpTileKind.Straight,
        });

        switch (item)
        {
            case RcpPaletteItem.Rail:
                design.Walls.Add(new RcpWall { Col = 0, Row = 1, Vertical = false });
                break;

            case RcpPaletteItem.Divider:
                design.Walls.Add(new RcpWall { Col = 0, Row = 1, Vertical = false, Double = true });
                break;

            case RcpPaletteItem.Turn:
                design.Curves.Add(new RcpCurve { Col = 1, Row = 1, Quadrant = RcpQuadrant.NorthWest, Radius = 0.7f });
                break;

            case RcpPaletteItem.FinishLine:
                design.Decals.Add(new RcpDecal { Col = 0, Row = 0, Kind = RcpDecalKind.FinishLine });
                break;

            case RcpPaletteItem.GridBox:
                design.Decals.Add(new RcpDecal { Col = 0, Row = 0, Kind = RcpDecalKind.GridBox });
                break;

            case RcpPaletteItem.Post:
                design.Decals.Add(new RcpDecal { Col = 0, Row = 0, Kind = RcpDecalKind.Post });
                break;

            case RcpPaletteItem.Kerb:
                design.Decals.Add(new RcpDecal { Col = 0, Row = 0, Kind = RcpDecalKind.Kerb });
                break;

            case RcpPaletteItem.Furniture:
                design.Decals.Add(new RcpDecal { Col = 0, Row = 0, Kind = RcpDecalKind.Furniture });
                break;

            case RcpPaletteItem.Split1:
                design.Decals.Add(new RcpDecal { Col = 0, Row = 0, Kind = RcpDecalKind.SplitLoop1 });
                break;

            case RcpPaletteItem.Split2:
                design.Decals.Add(new RcpDecal { Col = 0, Row = 0, Kind = RcpDecalKind.SplitLoop2 });
                break;

            case RcpPaletteItem.Split3:
                design.Decals.Add(new RcpDecal { Col = 0, Row = 0, Kind = RcpDecalKind.SplitLoop3 });
                break;
        }

        return design;
    }
}
