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

    /// <summary>
    /// Pick a piece of the plan up: drag a box round it, then move it,
    /// turn it, mirror it, copy it or take it away. Appended at the END -
    /// the tools are numbered on the toolbar and in the status line by the
    /// order they come in here.
    /// </summary>
    Select,

    /// <summary>
    /// Paint a lane: floor under it and rails down both sides that follow it
    /// (RcpAutoRails), or drag a box for a ring. Appended, like Select.
    /// </summary>
    Lane,

    /// <summary>Cycle a corner: a one-tile turn or square. Appended.</summary>
    Corner,

    /// <summary>
    /// Lay a whole piece - a tile with its rail already on, as it goes down
    /// on a real track (railed tiles, Phase 2, 7 Nov 2026): a rail tile, an
    /// outer curve tile, a finish or arched rail tile, a blank. Appended.
    /// </summary>
    Piece,
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

    /// <summary>A tile's name - "Pit lane", "Marshal" - typed in (Phase 8). Appended.</summary>
    Label,

    // The pieces (railed tiles, Phase 2, 7 Nov 2026): a tile with its rail
    // already on. Appended.

    /// <summary>A tile with a straight rail on one edge.</summary>
    RailTile,

    /// <summary>The quarter-circle outer curve tile with its one-tile turn rail.</summary>
    CurveTile,

    /// <summary>A tile with the finish-line rail.</summary>
    FinishTile,

    /// <summary>A tile whose rail arches into the lane, for a straight.</summary>
    ArchedTile,

    /// <summary>A tile with no bolt holes.</summary>
    BlankTile,
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

    /// <summary>Fires once a run of arrow-key nudges has stopped - see NudgeSelection.</summary>
    private readonly System.Windows.Forms.Timer _nudgeSettle = new() { Interval = 350 };

    /// <summary>When an arrow key last nudged (Environment.TickCount64), so a run can be told from the next one.</summary>
    private long _nudgedAt;

    /// <summary>How long a pause between arrow-key presses ends one nudge run, in milliseconds.</summary>
    private const long NudgeRunGapMs = 700;
    private readonly List<RcpTrackDesign> _undo = new();
    private readonly List<RcpTrackDesign> _redo = new();

    private RcpTrackDesign _design = new();
    private Image? _backdrop;

    private float _scale = 64f;
    private PointF _origin = new(40, 40);

    private Point? _hoverCell;
    private (int Col, int Row, bool Vertical)? _hoverEdge;
    private (Point At, RcpQuadrant Quadrant)? _hoverCorner;

    /// <summary>The edge and corner the next piece would take at the pointer (the Pieces tool's ghost).</summary>
    private (RcpEdge Facing, RcpQuadrant Corner) _hoverPiece;

    private int _draggingRoutePoint = -1;
    private bool _painting;
    private bool _panning;
    private Point _panFrom;
    private PointF _panOrigin;
    private PointF _backdropFrom;

    // WHY (20 Oct 2026, Bug Hunt 8, #82): true only for a drag that STARTED
    // with the Photo tool, the one press that sets _backdropFrom/_panFrom.
    // A 7 key mid-drag used to make the photo leap to a stale anchor.
    private bool _draggingBackdrop;

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

    // --- What is picked -------------------------------------------------------
    // The pieces themselves, by reference, so a move moves the very rails
    // that were picked and everything that reads the plan afterwards sees
    // it (see RcpSelection). The catch is that Undo swaps in a different
    // COPY of the plan, which leaves every reference pointing at pieces
    // that are no longer on it - so the selection is dropped whenever the
    // plan is replaced (ShowInstead, ClearAll, LoadStock, the Design
    // setter) rather than left to go quietly wrong.

    private RcpSelection _selection = new();

    // The angled view (Phase 8): which way it looks, and a drag turning it.
    private RcpAngle _angle = RcpAngle.Start;
    private Point? _angleFrom;

    // A stretch being dragged (Phase 8): which handle - true for the right
    // edge's (columns), false for the bottom's (rows) - the selection's box
    // when it began, and how many tiles it has gone so far.
    private bool? _stretchColumns;
    private Rectangle _stretchBox;
    private int _stretchBy;

    /// <summary>The marquee being dragged, in pixels. Null when there isn't one.</summary>
    private Point? _marqueeFrom;
    private Point? _marqueeAt;

    /// <summary>Whether this marquee adds to what was already picked - Shift as it started.</summary>
    private bool _marqueeAdds;

    /// <summary>Whether this drag is moving what is picked rather than drawing a marquee round it.</summary>
    private bool _movingSelection;

    /// <summary>The cell the pointer was over at the last step of a move, so each step is a delta.</summary>
    private Point? _moveLast;

    /// <summary>Whether a move has actually moved anything, which decides if it settles and takes an undo step.</summary>
    private bool _moved;

    /// <summary>
    /// What was copied - a standalone plan with its top-left at (0,0), not
    /// the pieces themselves, so it goes on being what was copied however
    /// the plan is edited afterwards.
    /// </summary>
    private RcpTrackDesign? _clipboard;

    /// <summary>Where it was copied from, so a paste with the pointer off the board lands beside the original.</summary>
    private Point _clipboardFrom;

    /// <summary>
    /// Whether Space is held: the left button then moves the view, whatever
    /// tool is in hand - the way drawing programs do it (Track Designer
    /// overhaul, Phase 2). The middle button and Shift + right button still do.
    /// </summary>
    private bool _spaceHeld;

    // --- The lane being painted (Track Designer overhaul, Phase 3) ----------

    /// <summary>The lane this stroke paints: the one it started on, or a new one.</summary>
    private int? _laneId;

    /// <summary>The ring being dragged out: the cell its box started at, and the cell it has reached.</summary>
    private Point? _ringFrom;
    private Point? _ringAt;

    /// <summary>Said once a session, not once a rail: why a rail along a lane's edge came back.</summary>
    private bool _toldAboutLaneEdges;

    /// <summary>Said once a stroke: that the lane joined up with one it ran into.</summary>
    private bool _toldAboutJoining;

    /// <summary>Where the zoom readout was last drawn, so a click on it can be told from a click on the plan.</summary>
    private RectangleF _zoomChip;

    /// <summary>The zoom the readout calls 100%: a tile 64 pixels across, as a new plan opens.</summary>
    internal const float HomeScale = 64f;

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
        _nudgeSettle.Tick += (_, _) => SettleNudgeRun();
    }

    public RcpTrackDesign Design
    {
        get => _design;
        set
        {
            _design = value ?? new RcpTrackDesign();
            _undo.Clear();
            _redo.Clear();
            ClearSelection();
            ReloadBackdrop();
            RaiseChanged();
        }
    }

    /// <summary>
    /// What the pointer does.
    ///
    /// Picking a different tool drops whatever was picked. A selection only
    /// means anything to the tool that made it - the arrow keys nudge it,
    /// Delete takes it off, the turn and mirror buttons act on it - so
    /// leaving it highlighted under the floor brush would be a ring round
    /// pieces that none of those things can reach any more.
    /// </summary>
    public RcpTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value)
            {
                return;
            }

            _tool = value;

            if (value != RcpTool.Select)
            {
                // A stretch half dragged goes with the selection it was of.
                if (_stretchColumns is not null)
                {
                    EndStroke();
                    _painting = false;
                }

                ClearSelection();
            }

            Invalidate();
        }
    }

    private RcpTool _tool = RcpTool.Floor;

    public RcpPaletteItem Item { get; set; } = RcpPaletteItem.Floor;

    /// <summary>Radius of the next turn rail, in tiles.</summary>
    public float CurveRadius { get; set; } = 1f;

    /// <summary>
    /// Which edge the next rail tile's rail goes on, when the pointer isn't
    /// near one edge of the tile (R turns it). Near an edge, that edge.
    /// </summary>
    public RcpEdge PieceFacing { get; set; } = RcpEdge.North;

    /// <summary>Which corner the next outer curve tile's arc goes across, when the pointer isn't in one corner of the tile (R turns it).</summary>
    public RcpQuadrant PieceCorner { get; set; } = RcpQuadrant.NorthWest;

    /// <summary>
    /// What the plan's rails come to as pieces, as of the last edit: the
    /// custom tiles - a second rail on a tile, cut for it by hand - are
    /// marked on the board, and the window reports the rest.
    /// </summary>
    public RcpPieces.Report PieceReport { get; private set; } = RcpPieces.Report.None;

    /// <summary>How wide the Lane tool paints, in tiles: 1, 2 or 3.</summary>
    public int LaneWidth
    {
        get => _laneWidth;
        set => _laneWidth = Math.Clamp(value, 1, 3);
    }

    // Two: a real RCP lane is never narrower (Asher, 25 Oct 2026).
    private int _laneWidth = 2;

    /// <summary>The Lane tool drags out a ring round a box, rather than painting where the pointer goes.</summary>
    public bool LaneRing { get; set; }

    public int MarkerTurns { get; set; }

    public bool ShowGrid { get; set; } = true;
    public bool ShowRoute { get; set; } = true;

    /// <summary>
    /// What the board says about the plan, over its top-left corner - the
    /// floor's size and the lap (set by RcpTrackEditorForm). On the board,
    /// not in a panel, because the drawing is where people are looking.
    /// </summary>
    public IReadOnlyList<string> Chips
    {
        get => _chips;
        set
        {
            var next = value ?? Array.Empty<string>();

            if (next.SequenceEqual(_chips))
            {
                return;
            }

            _chips = next;
            Invalidate();
        }
    }

    private IReadOnlyList<string> _chips = Array.Empty<string>();

    /// <summary>
    /// The first chip - the plan's size - in red: the plan uses more of some
    /// part than the club owns (the Stock tab, Track Designer overhaul Phase 4).
    /// </summary>
    public bool ChipWarning
    {
        get => _chipWarning;
        set
        {
            if (_chipWarning == value)
            {
                return;
            }

            _chipWarning = value;
            Invalidate();
        }
    }

    private bool _chipWarning;

    /// <summary>
    /// The board's floor: a dark hall floor whatever the app's look, so the
    /// foam, the silver rails and the pink turns always read the same - the
    /// colours they are drawn in were sampled off a real track, and a pale
    /// page under them changed how all of them looked.
    /// </summary>
    internal static readonly Color BoardFloor = ColorTranslator.FromHtml("#17191E");

    /// <summary>The grid's dots, the ruler and the chips' text: light on the board's floor.</summary>
    internal static readonly Color BoardInk = ColorTranslator.FromHtml("#9AA3B2");

    public double LapSeconds { get; set; } = 12;

    public bool IsPlaying => _paceTimer.Enabled;

    /// <summary>How fast Play goes against the lap time: 0.5, 1 or 2 (Phase 6).</summary>
    public float PaceRate { get; set; } = 1f;

    /// <summary>Raised when Play starts or stops - by its button, or by itself when the line goes.</summary>
    public event Action? PlayingChanged;

    /// <summary>Raised with where to show it when the lap chip is clicked - the window lists the sectors there.</summary>
    public event Action<Point>? LapChipClicked;

    /// <summary>Where the lap chip was last drawn, so a click on it can be told from a click on the plan.</summary>
    private RectangleF? _lapChipBox;

    /// <summary>The lap's length when the pace car last moved - a line changed mid-run is not a lap done.</summary>
    private float _paceLength;

    /// <summary>Laps the pace car has finished since Play was pressed.</summary>
    private int _paceLaps;

    public event Action? DesignChanged;
    public event Action<string>? StatusChanged;

    /// <summary>Raised when the board switches between the flat plan and the angled view.</summary>
    public event Action? AngledChanged;

    /// <summary>
    /// The board shows the plan tipped and turned (RcpAngledView) rather
    /// than flat. A way to look, not a place to draw: dragging turns and
    /// tips it, the wheel zooms, and nothing on the plan can change until
    /// it is flat again.
    /// </summary>
    public bool Angled { get; private set; }

    /// <summary>Between the flat plan and the angled view. Whatever was being dragged is dropped.</summary>
    public void ToggleAngled()
    {
        EndStroke();
        _painting = false;
        _angleFrom = null;
        Angled = !Angled;
        Cursor = Angled ? Cursors.SizeAll : Cursors.Default;

        StatusChanged?.Invoke(Angled
            ? "The angled view: drag to turn the track round and tip it, wheel to zoom. Press View, or pick a tool, to draw again."
            : "Back to the plan.");

        AngledChanged?.Invoke();
        Invalidate();
    }

    /// <summary>
    /// A tile clicked with the Markings tool's Label: the cell and the name
    /// it has now (empty for none). The window asks for the name and hands
    /// it back to SetTileLabel - the board asks nobody anything itself.
    /// </summary>
    public event Action<Point, string>? LabelRequested;

    /// <summary>Raised when what is picked changes, so the buttons that act on it can be greyed.</summary>
    public event Action? SelectionChanged;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public bool HasSelection => !_selection.IsEmpty;

    public bool HasClipboard => _clipboard is not null;

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

        // A move that actually went somewhere lands here, however the drag
        // finished - mouse-up, Alt-Tab, a dialog stealing the mouse. The
        // overlap is resolved once, at the end, so sliding a block across
        // the plan and back does not eat everything on the way (see
        // RcpSelection.Settle).
        if (_movingSelection && _moved)
        {
            _selection.Settle(_design);
        }

        _movingSelection = false;
        _moveLast = null;
        _moved = false;

        // A stretch let go anywhere but the board - Alt-Tab, a dialog - is
        // dropped: nothing has changed until it is let go here.
        if (_stretchColumns is not null)
        {
            _stretchColumns = null;
            _stretchBy = 0;
            StatusChanged?.Invoke("Stretch dropped - nothing changed.");
            Invalidate();
        }

        _marqueeFrom = null;
        _marqueeAt = null;
        _marqueeAdds = false;

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
        _draggingBackdrop = false;
        _laneId = null;
        _ringFrom = null;
        _ringAt = null;

        if (wasOpen)
        {
            // Everything the stroke changed, announced once.
            RaiseChanged();
        }
    }

    public void Undo()
    {
        // 17 Oct 2026, Bug Hunt 8, A31-4: ignored while a drag is held.
        // The rest of the stroke would find _strokeRemembered set, take no
        // snapshot and leave a stale mid-stroke Redo behind.
        if (_strokeOpen)
        {
            return;
        }

        if (_undo.Count == 0)
        {
            return;
        }

        // Before the snapshot, so Redo brings back a plan that has landed
        // rather than one with the nudged pieces stacked on what they hit.
        FlushPendingSettle();

        _redo.Add(_design.Clone());
        ShowInstead(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);

        RaiseChanged();
    }

    public void Redo()
    {
        // 17 Oct 2026, Bug Hunt 8, A31-4: ignored while a drag is held.
        // The rest of the stroke would find _strokeRemembered set, take no
        // snapshot and leave a stale mid-stroke Redo behind.
        if (_strokeOpen)
        {
            return;
        }

        if (_redo.Count == 0)
        {
            return;
        }

        FlushPendingSettle();

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

        // Every piece in the selection belongs to the plan being put away,
        // so the selection goes with it - see the field notes.
        ClearSelection();

        _design = design;

        if (photoChanged)
        {
            ReloadBackdrop();
        }
    }

    /// <summary>
    /// Replaces what is drawn with one of RCP's own layouts, in one step
    /// Undo takes back.
    ///
    /// Deliberately NOT the Design setter, which throws the undo history
    /// away: starting from a kit, seeing it, and deciding it was not what
    /// you wanted should cost one Ctrl+Z - not the work you did before
    /// you tried it.
    ///
    /// The room is left alone. Which kit you start from says nothing
    /// about the size of the hall it has to go in.
    /// </summary>
    public void LoadStock(RcpTrackDesign stock)
    {
        Remember();
        ClearSelection();

        _design.Tiles.Clear();
        _design.Walls.Clear();
        _design.Curves.Clear();
        _design.Decals.Clear();
        _design.Route.Clear();
        _design.RouteAuto = false;

        _design.Tiles.AddRange(stock.Tiles);
        _design.Walls.AddRange(stock.Walls);
        _design.Curves.AddRange(stock.Curves);
        _design.Decals.AddRange(stock.Decals);

        _design.TileSizeMm = stock.TileSizeMm;
        _design.KitName = stock.KitName;

        RaiseChanged();
    }

    /// <summary>
    /// Replaces what is drawn with a layout from Build me a track (Track
    /// Designer overhaul, Phase 5), in one step Undo takes back - as
    /// LoadStock does. Unlike a kit, it brings its racing line (traced, so
    /// it follows the track) and its lanes' turn radius with it. The room
    /// and the tile size are the plan's own and stay as they are.
    /// </summary>
    public void LoadBuilt(RcpTrackDesign layout)
    {
        Remember();
        ClearSelection();

        var copy = layout.Clone();

        _design.Tiles = copy.Tiles;
        _design.Walls = copy.Walls;
        _design.Curves = copy.Curves;
        _design.Decals = copy.Decals;
        _design.Route = copy.Route;
        _design.RouteClosed = copy.RouteClosed;
        _design.RouteAuto = copy.RouteAuto;
        _design.LaneTurnRadius = copy.LaneTurnRadius;

        if (!string.IsNullOrEmpty(copy.KitName))
        {
            _design.KitName = copy.KitName;
        }

        RaiseChanged();
    }

    /// <summary>The longest name a tile is given - it is drawn across the foot of one tile.</summary>
    public const int MostLabel = 40;

    /// <summary>
    /// Names a tile ("Pit lane", "Marshal") - one undo step; empty takes
    /// the name off. One line, held to MostLabel. Nothing happens for a
    /// cell with no tile, or a name that is the same as before.
    /// </summary>
    public void SetTileLabel(Point cell, string label)
    {
        if (_design.At(cell.X, cell.Y) is not { } tile)
        {
            return;
        }

        var line = string.Join(" ", (label ?? string.Empty).Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();

        if (line.Length > MostLabel)
        {
            line = line[..MostLabel].TrimEnd();
        }

        if (string.Equals(tile.Label ?? string.Empty, line, StringComparison.Ordinal))
        {
            return;
        }

        Remember();
        tile.Label = line;
        RaiseChanged();
    }

    /// <summary>
    /// Replaces what is drawn with a plan opened from a .hctrack file (Track
    /// Designer overhaul, Phase 7), in one step Undo takes back: its floor,
    /// rails, markings, line, lanes' turns, tile size and kit. The room is
    /// this club's hall, so it stays - unless none has been given here, when
    /// the file's is taken. The traced photo stays too; a file carries none.
    /// The laps raced here before stop counting towards its lap time.
    /// </summary>
    /// <returns>Whether the room came from the file.</returns>
    public bool LoadPlanFile(RcpTrackDesign plan)
    {
        Remember();
        ClearSelection();

        var copy = plan.Clone();

        _design.Tiles = copy.Tiles;
        _design.Walls = copy.Walls;
        _design.Curves = copy.Curves;
        _design.Decals = copy.Decals;
        _design.Route = copy.Route;
        _design.RouteClosed = copy.RouteClosed;
        _design.RouteAuto = copy.RouteAuto;
        _design.LaneTurnRadius = copy.LaneTurnRadius;
        _design.TileSizeMm = copy.TileSizeMm;
        _design.KitName = copy.KitName;

        // A different track: the laps raced on this layout before are not
        // laps of it (RcpLapModel.Raced) - even where this layout had no
        // racing line, when Track Layouts wouldn't count it a change.
        _design.RouteChangedUtc = DateTime.UtcNow;

        var roomFromFile = !_design.HasRoom && copy.HasRoom;

        if (roomFromFile)
        {
            _design.RoomWidthMm = copy.RoomWidthMm;
            _design.RoomDepthMm = copy.RoomDepthMm;
            _design.RoomInFeet = copy.RoomInFeet;
        }

        RaiseChanged();

        return roomFromFile;
    }

    /// <summary>
    /// The size of the room the plan has to go in - one undo step, taken
    /// here rather than by the side panel.
    ///
    /// The room is part of the plan (RcpTrackDesign) and so rides on every
    /// undo snapshot. The side panel used to write it straight into the
    /// plan with no step of its own, so Ctrl+Z after typing the hall's size
    /// and then drawing a rail took the rail back AND silently blanked the
    /// measurement - the same bug the photo's own undo step was added for.
    /// </summary>
    public void SetRoom(double widthMm, double depthMm, bool inFeet)
    {
        if (_design.RoomWidthMm == widthMm
            && _design.RoomDepthMm == depthMm
            && _design.RoomInFeet == inFeet)
        {
            return;
        }

        Remember();

        _design.RoomWidthMm = widthMm;
        _design.RoomDepthMm = depthMm;
        _design.RoomInFeet = inFeet;

        RaiseChanged();
    }

    // The rest of the side panel's plan settings, each an undo step of its
    // own for the reason SetRoom gives: they ride on every undo snapshot,
    // so written straight into the plan they were silently taken back by
    // the next Ctrl+Z, along with whatever that Ctrl+Z was meant for.

    /// <summary>The size of a tile, in millimetres - one undo step per run of changes to it.</summary>
    public void SetTileSize(double mm)
    {
        if (_design.TileSizeMm == mm)
        {
            return;
        }

        RememberSetting(nameof(SetTileSize));
        _design.TileSizeMm = mm;
        RaiseChanged();
    }

    /// <summary>
    /// The kit to check the plan against, and the tile size it comes in
    /// (null to leave the size alone) - as one undo step, since choosing a
    /// kit is one choice.
    /// </summary>
    public void SetKit(string name, double? tileSizeMm)
    {
        var size = tileSizeMm ?? _design.TileSizeMm;

        if (string.Equals(_design.KitName, name, StringComparison.Ordinal) && _design.TileSizeMm == size)
        {
            return;
        }

        Remember();
        _design.KitName = name;
        _design.TileSizeMm = size;
        RaiseChanged();
    }

    /// <summary>Whether the racing line closes into a lap.</summary>
    public void SetRouteClosed(bool closed)
    {
        if (_design.RouteClosed == closed)
        {
            return;
        }

        Remember();
        _design.RouteClosed = closed;

        // Opened or closed by hand: traced again, it would only be closed.
        _design.RouteAuto = false;
        RaiseChanged();
    }

    /// <summary>
    /// How strongly the traced photo shows - one undo step per drag of the
    /// slider. Only redrawn, not announced: the readouts do not depend on
    /// it, and working them all out again for every step of a drag is what
    /// RaiseChanged keeps a stroke from doing.
    /// </summary>
    public void SetBackdropOpacity(int opacity)
    {
        if (_design.BackdropOpacity == opacity)
        {
            return;
        }

        RememberSetting(nameof(SetBackdropOpacity));
        _design.BackdropOpacity = opacity;
        Invalidate();
    }

    /// <summary>
    /// The undo step for a setting changed a notch at a time - the slider
    /// dragged, the tile size's arrows held. Told apart from the next
    /// gesture the way RememberWheelResize does it: the same setting again,
    /// soon, with the snapshot this took still the newest one.
    /// </summary>
    private void RememberSetting(string setting)
    {
        var now = Environment.TickCount64;
        var sameGesture = _settingSnapshot is not null
                          && _undo.Count > 0
                          && ReferenceEquals(_undo[^1], _settingSnapshot)
                          && string.Equals(_settingChanged, setting, StringComparison.Ordinal)
                          && now - _settingAt < WheelResizeGapMs;

        _settingAt = now;

        if (sameGesture)
        {
            return;
        }

        Remember();
        _settingChanged = setting;
        _settingSnapshot = _undo.Count > 0 ? _undo[^1] : null;
    }

    /// <summary>The snapshot the current setting gesture took, while it is still the newest.</summary>
    private RcpTrackDesign? _settingSnapshot;

    /// <summary>Which setting that gesture is changing.</summary>
    private string? _settingChanged;

    /// <summary>When that setting last changed (Environment.TickCount64).</summary>
    private long _settingAt;

    public void ClearAll()
    {
        Remember();
        ClearSelection();

        _design.Tiles.Clear();
        _design.Walls.Clear();
        _design.Curves.Clear();
        _design.Decals.Clear();
        _design.Route.Clear();
        _design.RouteAuto = false;

        RaiseChanged();
    }

    private void RaiseChanged()
    {
        Rework();

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

    /// <summary>
    /// Works out again everything the plan works out for itself, after an
    /// edit (Track Designer overhaul, Phase 3, 25 Oct 2026): the rails round
    /// its lanes (RcpAutoRails), and the racing line, when it was traced and
    /// not touched by hand since (RcpTrackDesign.RouteAuto).
    ///
    /// Inside the edit's own undo step - RaiseChanged comes after the
    /// Remember - so Undo takes back the rails with the lane that made them.
    /// Not while a selection is being dragged or nudged: what is picked
    /// carries its own rails along, and only lands (RcpSelection.Settle) when
    /// the drag ends - working the rails out over the block's half-landed
    /// tiles would take the picked rails out of the selection mid-drag. The
    /// line waits for the end of a stroke, as the readouts do.
    /// </summary>
    private void Rework()
    {
        if (_movingSelection || _nudgeSettle.Enabled)
        {
            return;
        }

        var outcome = RcpAutoRails.Apply(_design);
        var forgot = false;

        foreach (var wall in outcome.RemovedWalls)
        {
            forgot |= _selection.Forget(wall);
        }

        foreach (var curve in outcome.RemovedCurves)
        {
            forgot |= _selection.Forget(curve);
        }

        if (forgot)
        {
            SelectionChanged?.Invoke();
        }

        // Which tile each rail is bolted to (railed tiles, Phase 1, 7 Nov
        // 2026) - worked out from the rails after every edit, and saved with
        // the plan, ready for the editor to lay tiles and rails as one piece.
        PieceReport = RcpPieces.Adopt(_design);

        if (!_strokeOpen && _design.RouteAuto)
        {
            RetraceLine();
        }
    }

    /// <summary>
    /// Traces the racing line again, quietly, the way it runs now. Only a
    /// whole lap replaces it: while the corridor is broken - a tile taken
    /// out halfway through an edit - the line is left as it was, and traced
    /// again once the corridor closes.
    /// </summary>
    private void RetraceLine()
    {
        var traced = RcpPlan.AutoRoute(_design, out var problem);

        if (traced is null || traced.Count < 3 || !string.IsNullOrEmpty(problem))
        {
            return;
        }

        traced = SameWayRound(traced, _design.Route);

        var same = _design.RouteClosed
                   && traced.Count == _design.Route.Count
                   && traced.Select((point, i) => point == _design.Route[i].Point).All(match => match);

        if (same)
        {
            return;
        }

        var forgot = false;

        foreach (var point in _design.Route)
        {
            forgot |= _selection.Forget(point);
        }

        if (forgot)
        {
            SelectionChanged?.Invoke();
        }

        _design.Route = traced.Select(p => new RcpRoutePoint { X = p.X, Y = p.Y }).ToList();
        _design.RouteClosed = true;
    }

    /// <summary>
    /// A traced lap turned to run the same way round as the line it
    /// replaces - so a line turned round with Reverse stays turned round
    /// when it is traced again. Keeps its first point.
    /// </summary>
    internal static List<PointF> SameWayRound(List<PointF> traced, IReadOnlyList<RcpRoutePoint> was)
    {
        if (was.Count < 3 || traced.Count < 3)
        {
            return traced;
        }

        var before = SignedArea(was.Select(p => p.Point).ToList());
        var now = SignedArea(traced);

        if (before * now >= 0)
        {
            return traced;
        }

        return new[] { traced[0] }.Concat(traced.Skip(1).Reverse()).ToList();
    }

    /// <summary>Twice the area a closed line goes round, signed by which way round it goes.</summary>
    private static float SignedArea(IReadOnlyList<PointF> points)
    {
        var sum = 0f;

        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];

            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum;
    }

    /// <summary>
    /// The radius of the turns the lanes put round their bends
    /// (RcpTrackDesign.LaneTurnRadius): 1 or 2 tiles, or 0 for square - one
    /// undo step. Every lane's turns follow it; a corner set by hand with the
    /// Corners tool keeps what it was given.
    /// </summary>
    public void SetLaneTurnRadius(int radius)
    {
        radius = Math.Clamp(radius, 0, RcpAutoRails.MostRadius);

        if (_design.LaneTurnRadius == radius)
        {
            return;
        }

        Remember();
        _design.LaneTurnRadius = radius;
        RaiseChanged();
    }

    /// <summary>
    /// The racing line changed by hand through what is picked, so it is no
    /// longer traced again by itself. Moving the whole of it with the rest
    /// of the plan is not that: traced again, it comes out the same.
    /// </summary>
    private void LineTouchedByHand(bool deleting)
    {
        if (_selection.Route.Count == 0)
        {
            return;
        }

        // The line on its own, with no track picked with it, is moved by hand.
        if (deleting || _selection.HoldsPartOfTheLine(_design) || _selection.Tiles.Count == 0)
        {
            _design.RouteAuto = false;
        }
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

    // --- Pieces (railed tiles, Phase 2, 7 Nov 2026) --------------------------
    // A tile and its rail go down together, as they do on a real RCP track:
    // the rail is bolted to its tile and stays on (Asher, 7 Nov 2026).

    /// <summary>The piece the palette has picked, for the Pieces tool.</summary>
    private RcpPiece PieceFor(RcpPaletteItem item) => item switch
    {
        RcpPaletteItem.RailTile => RcpPiece.Rail,
        RcpPaletteItem.CurveTile => RcpPiece.OuterCurve,
        RcpPaletteItem.FinishTile => RcpPiece.Finish,
        RcpPaletteItem.ArchedTile => RcpPiece.Arched,
        RcpPaletteItem.BlankTile => RcpPiece.Blank,
        _ => RcpPiece.Plain,
    };

    /// <summary>
    /// Which edge of its tile a rail laid at the pointer goes on: the edge
    /// the pointer is near, or - in the middle of the tile, and all along a
    /// drag - the one R has set.
    /// </summary>
    private RcpEdge FacingAt(Point pixel, bool dragging)
    {
        if (dragging)
        {
            return PieceFacing;
        }

        var tile = ToTile(pixel);
        var dx = tile.X - MathF.Floor(tile.X);
        var dy = tile.Y - MathF.Floor(tile.Y);

        var nearest = new[] { (dy, RcpEdge.North), (1 - dy, RcpEdge.South), (dx, RcpEdge.West), (1 - dx, RcpEdge.East) }
            .OrderBy(o => o.Item1)
            .First();

        return nearest.Item1 <= EdgeGrab ? nearest.Item2 : PieceFacing;
    }

    /// <summary>Which corner of its tile an outer curve laid at the pointer has its arc across: the corner the pointer is in, or the one R has set.</summary>
    private RcpQuadrant CornerAtPointer(Point pixel, bool dragging)
    {
        if (dragging)
        {
            return PieceCorner;
        }

        var tile = ToTile(pixel);
        var dx = tile.X - MathF.Floor(tile.X) - 0.5f;
        var dy = tile.Y - MathF.Floor(tile.Y) - 0.5f;

        // Only well into a corner; in the middle, what R set.
        if (MathF.Abs(dx) < 0.15f && MathF.Abs(dy) < 0.15f)
        {
            return PieceCorner;
        }

        return dx >= 0
            ? dy >= 0 ? RcpQuadrant.SouthEast : RcpQuadrant.NorthEast
            : dy >= 0 ? RcpQuadrant.SouthWest : RcpQuadrant.NorthWest;
    }

    /// <summary>
    /// Lays the picked piece on a cell - the tile with its rail on, in one
    /// go. Clicked on a tile that already is exactly that piece, it goes
    /// back to a plain tile: a click both lays and takes off.
    /// </summary>
    private void LayPiece(Point cell, Point pixel, bool dragging)
    {
        var piece = PieceFor(Item);
        var facing = FacingAt(pixel, dragging);
        var corner = CornerAtPointer(pixel, dragging);
        var existing = _design.At(cell.X, cell.Y);

        var same = existing is not null && existing.Kind != RcpTileKind.Infield && existing.Piece == piece
            && (!RcpPieces.HasStraight(piece) || existing.Facing == facing)
            && (piece != RcpPiece.OuterCurve || existing.Corner == corner);

        if (same && dragging)
        {
            return;
        }

        // A drag carries on the way the press set it: pressed near the
        // north edge, the whole run has its rails along the north.
        if (!dragging)
        {
            PieceFacing = facing;
            PieceCorner = corner;
        }

        Remember();

        var (walls, curves) = RcpPieces.Lay(_design, cell.X, cell.Y, same ? RcpPiece.Plain : piece, facing, corner);
        var forgot = false;

        foreach (var wall in walls)
        {
            forgot |= _selection.Forget(wall);
        }

        foreach (var curve in curves)
        {
            forgot |= _selection.Forget(curve);
        }

        Unpick(forgot);

        if (same)
        {
            StatusChanged?.Invoke("Back to a plain tile.");
        }

        RaiseChanged();
    }

    /// <summary>A corner as the status line says it: "north-west".</summary>
    private static string Words(RcpQuadrant corner) => corner switch
    {
        RcpQuadrant.NorthEast => "north-east",
        RcpQuadrant.SouthEast => "south-east",
        RcpQuadrant.SouthWest => "south-west",
        _ => "north-west",
    };

    /// <summary>R with the Pieces tool: turns the next piece a quarter clockwise.</summary>
    private void TurnPiece()
    {
        PieceFacing = PieceFacing switch
        {
            RcpEdge.North => RcpEdge.East,
            RcpEdge.East => RcpEdge.South,
            RcpEdge.South => RcpEdge.West,
            _ => RcpEdge.North,
        };

        PieceCorner = PieceCorner switch
        {
            RcpQuadrant.NorthWest => RcpQuadrant.NorthEast,
            RcpQuadrant.NorthEast => RcpQuadrant.SouthEast,
            RcpQuadrant.SouthEast => RcpQuadrant.SouthWest,
            _ => RcpQuadrant.NorthWest,
        };

        Invalidate();
    }

    // --- Lanes (Track Designer overhaul, Phase 3, 25 Oct 2026) ---------------

    /// <summary>The lane a cell of floor is part of, or null for plain floor, blocked floor or none.</summary>
    private int? LaneUnder(Point cell) =>
        _design.At(cell.X, cell.Y) is { Lane: > 0 } tile && tile.Kind != RcpTileKind.Infield ? tile.Lane : null;

    /// <summary>
    /// Starts a lane stroke. Started on a lane, it carries that lane on -
    /// which is how a bend is widened or a bay added; started anywhere else,
    /// it is a new lane, and where it runs alongside another the seam
    /// between them is a divider.
    /// </summary>
    private void StartLane(Point cell)
    {
        _laneId = LaneUnder(cell) ?? RcpAutoRails.NewLane(_design);
        _toldAboutJoining = false;
        PaintLane(cell);
    }

    /// <summary>Paints the lane brush at a cell: floor under it, part of this stroke's lane. The rails follow (Rework).</summary>
    private void PaintLane(Point cell)
    {
        if (_laneId is not { } lane)
        {
            return;
        }

        var changed = false;

        foreach (var at in RcpAutoRails.Brush(cell, LaneWidth))
        {
            changed |= LayLaneTile(at, lane, joinOthers: true);
        }

        if (changed)
        {
            RaiseChanged();
        }
    }

    /// <summary>
    /// Makes one cell part of a lane: a tile laid where there was none, plain
    /// or blocked floor taken into it. A cell of ANOTHER lane joins the two
    /// up when <paramref name="joinOthers"/> - the whole of that lane takes
    /// this one's number, so the divider between them goes - and is left
    /// alone otherwise.
    /// </summary>
    private bool LayLaneTile(Point at, int lane, bool joinOthers)
    {
        var tile = _design.At(at.X, at.Y);

        if (tile is null)
        {
            Remember();
            _design.Tiles.Add(new RcpTile { Col = at.X, Row = at.Y, Lane = lane });
            return true;
        }

        var blocked = tile.Kind == RcpTileKind.Infield;

        if (tile.Lane == lane && !blocked)
        {
            return false;
        }

        if (tile.Lane > 0 && tile.Lane != lane && !blocked)
        {
            if (!joinOthers)
            {
                return false;
            }

            Remember();

            var other = tile.Lane;

            foreach (var joined in _design.Tiles.Where(t => t.Lane == other))
            {
                joined.Lane = lane;
            }

            if (!_toldAboutJoining)
            {
                _toldAboutJoining = true;
                StatusChanged?.Invoke("Joined up with the lane it ran into - one lane now, with no divider between them.");
            }

            return true;
        }

        Remember();
        tile.Lane = lane;

        // Blocked floor is unblocked by a lane painted over it; any other
        // kind (an old plan's start/finish or corner tile) keeps its kind.
        if (blocked)
        {
            tile.Kind = RcpTileKind.Straight;
        }

        return true;
    }

    /// <summary>
    /// Lays a ring: a closed lane, the lane's width, just inside the box
    /// dragged - the quickest way from nothing to a track. A lane of its
    /// own; where it crosses another lane, that lane keeps its tiles.
    /// </summary>
    private void LayRing(Point from, Point to)
    {
        if (RcpAutoRails.Ring(from, to, LaneWidth) is not { } cells)
        {
            var least = LaneWidth * 2 + 1;
            StatusChanged?.Invoke($"A ring needs room for an island in the middle - drag a box at least {least} tiles each way.");
            return;
        }

        var lane = RcpAutoRails.NewLane(_design);
        var laid = false;

        foreach (var at in cells)
        {
            laid |= LayLaneTile(at, lane, joinOthers: false);
        }

        if (!laid)
        {
            return;
        }

        StatusChanged?.Invoke("Ring laid, with rails down both sides and turns at its corners. Trace route puts the racing line round it.");
        RaiseChanged();
    }

    /// <summary>
    /// One click of the Corners tool: a one-tile turn, a two-tile turn, then
    /// square, then round again (RcpCorners). Whatever it touches is a hand
    /// piece from then on, so a lane leaves that corner as it was set.
    /// </summary>
    private void CycleCorner(Point at)
    {
        if (RcpCorners.Next(_design, at, out var why) is not { } step)
        {
            StatusChanged?.Invoke(why);
            return;
        }

        Remember();

        var (walls, curves) = RcpCorners.Apply(_design, at, step);
        var forgot = false;

        foreach (var wall in walls)
        {
            forgot |= _selection.Forget(wall);
        }

        foreach (var curve in curves)
        {
            forgot |= _selection.Forget(curve);
        }

        if (forgot)
        {
            SelectionChanged?.Invoke();
        }

        StatusChanged?.Invoke(step.Said);
        RaiseChanged();
    }

    /// <summary>Back to 100% - a tile 64 pixels across - about the middle of the board.</summary>
    public void ZoomHome()
    {
        Zoom(HomeScale / _scale);
        StatusChanged?.Invoke("Back to 100%. F fits the whole plan in view.");
    }

    /// <summary>The zoom as the readout says it: 100% is a tile 64 pixels across.</summary>
    public int ZoomPercent => (int)MathF.Round(_scale / HomeScale * 100f);

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

        // A lane's own rail, clicked, is a hand rail from now on (see
        // RcpWall.Auto): the lane leaves that seam as it is set here.
        existing.Auto = false;

        if (!existing.Double)
        {
            existing.Double = true;
            StatusChanged?.Invoke("Two rails back to back - racing on both sides.");
            RaiseChanged();
            return;
        }

        _design.Walls.Remove(existing);
        Unpick(_selection.Forget(existing));
        RaiseChanged();

        // Taken off the edge of a lane, the lane puts its own rail back.
        if (_design.WallAt(edge.Col, edge.Row, edge.Vertical) is { Auto: true } && !_toldAboutLaneEdges)
        {
            _toldAboutLaneEdges = true;
            StatusChanged?.Invoke(
                "That seam is the edge of a lane, so the lane has put its rail back. "
                + "To open it up, paint the floor beyond it into the lane with the Lane tool.");
        }
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
            // Changed by hand, a lane's turn is a hand turn from now on.
            existing.Auto = false;
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

        // A lane's own turn, clicked, is a hand turn from now on - or the
        // lane would put it straight back as it was.
        existing.Auto = false;

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

    /// <summary>
    /// A rail post's quarter goes in the corner of its tile the pointer is
    /// in (8 Nov 2026): as Turns, the corner it is turned to - 0 the
    /// north-west, then clockwise. Null in the middle of the tile.
    /// </summary>
    private int? PostTurnsAt(Point pixel)
    {
        var tile = ToTile(pixel);
        var dx = tile.X - MathF.Floor(tile.X) - 0.5f;
        var dy = tile.Y - MathF.Floor(tile.Y) - 0.5f;

        if (MathF.Abs(dx) < 0.15f && MathF.Abs(dy) < 0.15f)
        {
            return null;
        }

        return dx < 0
            ? dy < 0 ? 0 : 3
            : dy < 0 ? 1 : 2;
    }

    private void PlaceMarker(Point cell, Point? pixel = null)
    {
        // A rail post's quarter in the corner clicked, rather than turned
        // round to it one click at a time.
        if (Item == RcpPaletteItem.Post && pixel is { } at && PostTurnsAt(at) is { } corner)
        {
            var post = _design.Decals.FirstOrDefault(d => d.Col == cell.X && d.Row == cell.Y);

            if (post is null || post.Kind != RcpDecalKind.Post || post.Turns != corner)
            {
                MarkerTurns = corner;

                if (post is { Kind: RcpDecalKind.Post })
                {
                    Remember();
                    post.Turns = corner;
                    RaiseChanged();
                    return;
                }
            }
        }

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

    /// <summary>
    /// Takes off whatever is nearest: a curve, then a rail, then a marking,
    /// then the floor.
    ///
    /// A lane's own rails are passed over (25 Oct 2026): the lane would only
    /// put them straight back. The eraser takes the lane's floor instead, and
    /// the rails follow it in.
    /// </summary>
    private void EraseAt(Point pixel)
    {
        if (CornerAt(pixel) is { } corner && _design.CurveAt(corner.At.X, corner.At.Y) is { Auto: false } curve)
        {
            Remember();
            _design.Curves.Remove(curve);
            Unpick(_selection.Forget(curve));
            RaiseChanged();
            return;
        }

        // On a lane's own rail: the lane's tile beside it, whichever side
        // of the rail the pointer is on.
        if (EdgeAt(pixel) is { } laneEdge
            && _design.WallAt(laneEdge.Col, laneEdge.Row, laneEdge.Vertical) is { Auto: true }
            && LaneTileBeside(laneEdge, ToCell(pixel)) is { } laneTile)
        {
            Remember();
            _design.Tiles.Remove(laneTile);
            Unpick(_selection.Forget(laneTile));
            RaiseChanged();
            return;
        }

        if (EdgeAt(pixel) is { } edge && _design.WallAt(edge.Col, edge.Row, edge.Vertical) is { Auto: false } wall)
        {
            Remember();
            _design.Walls.Remove(wall);
            Unpick(_selection.Forget(wall));
            RaiseChanged();
            return;
        }

        var cell = ToCell(pixel);

        if (_design.Decals.FirstOrDefault(d => d.Col == cell.X && d.Row == cell.Y) is { } decal)
        {
            Remember();
            _design.Decals.Remove(decal);
            Unpick(_selection.Forget(decal));
            RaiseChanged();
            return;
        }

        // The whole piece: the tile, and the rail bolted to it (railed
        // tiles, Phase 2, 7 Nov 2026). A rail can be taken off its tile on
        // its own by erasing on the rail itself, above.
        if (_design.At(cell.X, cell.Y) is { } tile)
        {
            Remember();

            var (takenWalls, takenCurves) = RcpPieces.TakeUp(_design, tile);
            var forgot = _selection.Forget(tile);

            foreach (var taken in takenWalls)
            {
                forgot |= _selection.Forget(taken);
            }

            foreach (var taken in takenCurves)
            {
                forgot |= _selection.Forget(taken);
            }

            Unpick(forgot);
            RaiseChanged();
        }
    }

    /// <summary>
    /// The lane tile on one side of a seam: the one under the pointer when
    /// that is lane, otherwise the one across the seam - or null.
    /// </summary>
    private RcpTile? LaneTileBeside((int Col, int Row, bool Vertical) seam, Point under)
    {
        var a = new Point(seam.Col, seam.Row);
        var b = seam.Vertical ? new Point(seam.Col - 1, seam.Row) : new Point(seam.Col, seam.Row - 1);

        foreach (var cell in under == b ? new[] { b, a } : new[] { a, b })
        {
            if (LaneUnder(cell) is not null)
            {
                return _design.At(cell.X, cell.Y);
            }
        }

        return null;
    }

    /// <summary>
    /// Tells the buttons when an erase took a piece out of the selection.
    ///
    /// Right-click and Ctrl+click erase in the Select tool too. A piece
    /// erased while still picked used to stay in the selection: a nudge or
    /// a drag then moved it and settled it, clearing a real piece nobody had
    /// picked from where it landed, and a copy brought the erased piece
    /// back. So EraseAt takes it out of the selection as it goes.
    /// </summary>
    private void Unpick(bool wasPicked)
    {
        if (wasPicked)
        {
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>
    /// Rail the edge: the outside tiles of the floor become rail tiles
    /// facing out, and its outside corners outer curve tiles - a square
    /// outside corner would be two rails on one tile, which is custom work
    /// (railed tiles, Phase 2, 7 Nov 2026). Corners already rounded or
    /// railed by hand are left as they are.
    /// </summary>
    public void WallThePerimeter()
    {
        Remember();

        var added = RcpPlan.WallThePerimeter(_design);
        var rounded = RcpPieces.RoundOutsideCorners(_design);

        StatusChanged?.Invoke(added == 0 && rounded == 0
            ? "The floor already has rails all round."
            : rounded == 0
                ? $"{Plural.Of(added, "rail tile")} round the edge of the floor."
                : $"{Plural.Of(Math.Max(0, added - rounded * 2), "rail tile")} round the edge of the floor, and {Plural.Of(rounded, "outer curve tile")} at its corners.");

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

        // Drawn by hand from here on, so not traced again by itself.
        _design.RouteAuto = false;
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

        // Brand-new points, so a selection holding the old ones would be
        // left pointing at pieces that are no longer on the plan - drawn as
        // dots over bare floor, and counted in the box round the selection.
        // Every other path that replaces part of the plan drops it too.
        ClearSelection();

        // The same way round as the line it replaces, if there was one -
        // a line turned round with Reverse stays turned round.
        var closedLap = string.IsNullOrEmpty(problem);

        if (closedLap)
        {
            route = SameWayRound(route, _design.Route);
        }

        _design.Route = route.Select(p => new RcpRoutePoint { X = p.X, Y = p.Y }).ToList();

        // A whole lap is traced again by itself whenever the corridor
        // changes, until the line is touched by hand (RouteAuto).
        _design.RouteAuto = closedLap;

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

    // --- Picking a piece of the plan up ---------------------------------------

    /// <summary>
    /// The block of cells a marquee covers, snapped OUT to whole tiles.
    ///
    /// Snapped out rather than rounded, for two reasons. A drag that
    /// touches any part of a tile takes that tile, which is what a lasso
    /// is for; and because the block is whole tiles, the rails round its
    /// edge lie exactly on its boundary and come along with it (see
    /// RcpSelection.In) rather than being left behind every time somebody
    /// slides a layout across the room. It also means a plain click with
    /// nothing dragged picks the one tile under the pointer.
    /// </summary>
    private Rectangle MarqueeCells(Point from, Point to)
    {
        var a = ToTile(from);
        var b = ToTile(to);

        var left = (int)MathF.Floor(Math.Min(a.X, b.X));
        var top = (int)MathF.Floor(Math.Min(a.Y, b.Y));
        var right = (int)MathF.Ceiling(Math.Max(a.X, b.X));
        var bottom = (int)MathF.Ceiling(Math.Max(a.Y, b.Y));

        // A drag that began and ended on the same grid line would be no
        // cells wide, which picks nothing. One tile is the least a click
        // can mean.
        return Rectangle.FromLTRB(left, top, Math.Max(right, left + 1), Math.Max(bottom, top + 1));
    }

    /// <summary>Whether the pointer is over what is picked - which is what makes a drag a move rather than a new marquee.</summary>
    /// <summary>The most tiles one drag of a stretch handle adds.</summary>
    private const int MostStretch = 60;

    /// <summary>
    /// Where a stretch handle is drawn, on screen: the middle of the
    /// selection's right edge (columns) or bottom edge (rows). Null when
    /// there is nothing picked that has a size that way to stretch.
    /// </summary>
    private RectangleF? StretchHandle(bool columns)
    {
        if (Tool != RcpTool.Select || _selection.Pieces == 0)
        {
            return null;
        }

        var box = _stretchColumns is null ? _selection.Bounds() : _stretchBox;

        if (box.Width < 1 || box.Height < 1)
        {
            return null;
        }

        // Only an edge across the floor, not along its end (RcpStretch).
        var floor = _design.Tiles.Where(t => t.Kind != RcpTileKind.Infield).ToList();

        if (floor.Count == 0
            || (columns && (box.Right <= floor.Min(t => t.Col) || box.Right > floor.Max(t => t.Col)))
            || (!columns && (box.Bottom <= floor.Min(t => t.Row) || box.Bottom > floor.Max(t => t.Row))))
        {
            return null;
        }

        var size = Math.Max(12f, UiLayout.AtTextSize(14));
        var centre = columns
            ? new PointF(_origin.X + box.Right * _scale, _origin.Y + (box.Top + box.Height / 2f) * _scale)
            : new PointF(_origin.X + (box.Left + box.Width / 2f) * _scale, _origin.Y + box.Bottom * _scale);

        return new RectangleF(centre.X - size / 2f, centre.Y - size / 2f, size, size);
    }

    /// <summary>Which stretch handle is under the pointer: true for the right one, false for the bottom one, null for neither.</summary>
    private bool? StretchHandleAt(Point pixel)
    {
        foreach (var columns in new[] { true, false })
        {
            if (StretchHandle(columns) is { } handle)
            {
                handle.Inflate(3, 3);

                if (handle.Contains(pixel))
                {
                    return columns;
                }
            }
        }

        return null;
    }

    private static string StretchSays(bool columns, int by) => by == 0
        ? "Drag the handle out to make the plan longer here, or in to make it shorter."
        : $"{(by > 0 ? "Longer" : "Shorter")} by {Plural.Of(Math.Abs(by), columns ? "column" : "row")} of tiles, right across the plan.";

    /// <summary>The handles on the selection, and while one is dragged, where the new edge will be.</summary>
    private void DrawStretch(Graphics g)
    {
        if (_stretchColumns is { } columns && _stretchBy != 0)
        {
            using var line = new Pen(Color.FromArgb(230, AppTheme.Accent), 2f) { DashStyle = DashStyle.Dash };

            if (columns)
            {
                var x = _origin.X + (_stretchBox.Right + _stretchBy) * _scale;
                g.DrawLine(line, x, 0, x, Height);
            }
            else
            {
                var y = _origin.Y + (_stretchBox.Bottom + _stretchBy) * _scale;
                g.DrawLine(line, 0, y, Width, y);
            }
        }

        // White with the accent round it, so they stand out from the
        // selection's own wash, which is the accent.
        using var fill = new SolidBrush(Color.White);
        using var rim = new Pen(AppTheme.Accent, 2.5f);

        foreach (var which in new[] { true, false })
        {
            if (StretchHandle(which) is { } handle)
            {
                g.FillRectangle(fill, handle);
                g.DrawRectangle(rim, handle.X, handle.Y, handle.Width, handle.Height);
            }
        }
    }

    /// <summary>
    /// A stretch let go: the plan made longer or shorter at the edge it was
    /// dragged from, as one undo step - or, when a hand-placed turn rail is
    /// in the way, nothing, and why (RcpStretch). What is picked afterwards
    /// is the selection's box, stretched.
    /// </summary>
    private void FinishStretch(bool columns, int by)
    {
        var box = _stretchBox;

        // Nothing to stretch from any more: the tool changed, or what was
        // picked went, while the handle was held.
        if (by == 0 || Tool != RcpTool.Select || _selection.Pieces == 0)
        {
            return;
        }

        var at = columns ? box.Right : box.Bottom;

        // Tried on a copy first, so a stretch refused leaves no empty undo step.
        var trial = _design.Clone();
        var why = columns ? RcpStretch.Columns(trial, at, by) : RcpStretch.Rows(trial, at, by);

        if (why is not null)
        {
            StatusChanged?.Invoke(why);
            return;
        }

        Remember();

        if (columns)
        {
            RcpStretch.Columns(_design, at, by);
        }
        else
        {
            RcpStretch.Rows(_design, at, by);
        }

        var stretched = columns
            ? new Rectangle(box.X, box.Y, box.Width + by, box.Height)
            : new Rectangle(box.X, box.Y, box.Width, box.Height + by);

        _selection = RcpSelection.In(_design, stretched);

        RaiseChanged();
        SelectionChanged?.Invoke();
        StatusChanged?.Invoke(StretchSays(columns, by).Replace("Longer", "Made longer").Replace("Shorter", "Made shorter"));
    }

    private bool PointerOnSelection(Point pixel)
    {
        if (_selection.IsEmpty)
        {
            return false;
        }

        var box = _selection.Bounds();
        var tile = ToTile(pixel);

        // Grown by a third of a tile on every side. Bounds() is a box in
        // GRID POINTS, so a selection with no tile or marking in it is
        // degenerate - one horizontal rail is a box no tiles high, one
        // vertical rail none wide, a lone turn rail nothing at all. The
        // pointer could then never be "on" it, so dragging the very rail
        // you had just picked drew a fresh marquee instead of moving it.
        const float grab = 0.34f;

        return tile.X >= box.Left - grab && tile.X <= box.Right + grab
               && tile.Y >= box.Top - grab && tile.Y <= box.Bottom + grab;
    }

    private void AnnounceSelection()
    {
        SelectionChanged?.Invoke();
        StatusChanged?.Invoke(_selection.Describe());
        Invalidate();
    }

    public void ClearSelection()
    {
        if (_selection.IsEmpty)
        {
            return;
        }

        // Anything already moved has to land before the selection goes.
        // Escape mid-drag - and a tool key, which comes through the Tool
        // setter - used to empty the selection with a move still open, so
        // EndStroke's settle found nothing to settle and left two tiles in
        // one cell: invisible, counted twice by the inventory and the kit
        // check, and saved that way. A nudge run waiting to settle is the
        // same case.
        var settled = (_movingSelection && _moved) || _nudgeSettle.Enabled;

        if (settled)
        {
            _selection.Settle(_design);
            _moved = false;
        }

        _nudgeSettle.Stop();

        _selection = new RcpSelection();

        SelectionChanged?.Invoke();

        // 6 Nov 2026: the settle changes the plan, and a nudge run's own
        // settle (SettleNudgeRun) is what works out its rails and line again
        // and tells the readouts. Escape or a tool key inside the run's
        // 350 ms stopped that timer here and told nobody - the moved lane
        // kept its old rails, the line went untraced, and Save wrote it so.
        if (settled)
        {
            RaiseChanged();
            return;
        }

        Invalidate();
    }

    public void SelectEverything()
    {
        // WHY (20 Oct 2026, Bug Hunt 8, #81): ignored while a move-drag is
        // held. EndStroke settles whatever is picked at mouse-up, so swapping
        // the selection mid-drag left the moved block stacked, unsettled, on
        // what it was dropped over - and saved that way.
        if (_movingSelection)
        {
            return;
        }

        FlushPendingSettle();

        _selection = RcpSelection.Everything(_design);

        if (_selection.IsEmpty)
        {
            StatusChanged?.Invoke("There is nothing on the plan yet.");
            SelectionChanged?.Invoke();
            return;
        }

        AnnounceSelection();
    }

    public void CopySelection()
    {
        if (_selection.IsEmpty)
        {
            StatusChanged?.Invoke("Nothing picked. Drag a box round something first.");
            return;
        }

        _clipboardFrom = _selection.Bounds().Location;
        _clipboard = _selection.Copy();

        SelectionChanged?.Invoke();
        StatusChanged?.Invoke($"Copied. Ctrl+V puts it down where the pointer is. {_selection.Describe()}");
    }

    public void CutSelection()
    {
        if (_selection.IsEmpty)
        {
            StatusChanged?.Invoke("Nothing picked. Drag a box round something first.");
            return;
        }

        // Before the copy too, so the clipboard holds the landed block.
        // 17 Oct 2026, Bug Hunt 8, A31-1: a nudge run still waiting for its
        // timer is landed first, or Remember snapshots the plan with the
        // nudged pieces stacked on what they hit, and Undo brings that back.
        FlushPendingSettle();

        _clipboardFrom = _selection.Bounds().Location;
        _clipboard = _selection.Copy();

        // PART OF THE RACING LINE STAYS PUT (22 Oct 2026, known issues):
        // a paste only lays route points on a plan with no line, so cutting
        // a stretch of it lost those points for good. See HoldsPartOfTheLine.
        var keepTheLine = _selection.HoldsPartOfTheLine(_design);

        Remember();
        LineTouchedByHand(deleting: !keepTheLine);
        _selection.DeleteFrom(_design, keepTheLine);
        _selection = new RcpSelection();

        SelectionChanged?.Invoke();
        StatusChanged?.Invoke(keepTheLine
            ? "Cut. The racing line stays where it is - only part of it was picked. Ctrl+V puts the rest down where the pointer is."
            : "Cut. Ctrl+V puts it down where the pointer is.");
        RaiseChanged();
    }

    /// <summary>
    /// Puts a copy down. Where the pointer is, if it is over the board -
    /// otherwise one tile along from where it was copied, which is what
    /// makes a paste visible rather than exactly on top of the original.
    /// </summary>
    public void PasteClipboard()
    {
        // WHY (20 Oct 2026, Bug Hunt 8, #81): ignored while a move-drag is
        // held. EndStroke settles whatever is picked at mouse-up, so swapping
        // the selection mid-drag left the moved block stacked, unsettled, on
        // what it was dropped over - and saved that way.
        if (_movingSelection)
        {
            return;
        }

        if (_clipboard is null)
        {
            StatusChanged?.Invoke("Nothing copied yet. Pick something and press Ctrl+C.");
            return;
        }

        // Where the pointer is - or, with the pointer off the board, clear
        // of where it was copied from. One tile along was not enough: the
        // paste settles what it lands on, so it ate the original (see
        // Beside). The clipboard is normalised to (0,0), so its own bounds
        // are its size.
        var at = _hoverCell
                 ?? Beside(new Rectangle(
                     _clipboardFrom,
                     RcpSelection.Everything(_clipboard).Bounds().Size));

        FlushPendingSettle();
        Remember();
        _selection = RcpSelection.Paste(_design, _clipboard, at);

        // Points put down from the clipboard are a line drawn by hand.
        if (_selection.Route.Count > 0)
        {
            _design.RouteAuto = false;
        }

        AnnounceSelection();
        RaiseChanged();
    }

    /// <summary>A second copy, one tile down and across, picked up ready to be dragged where it belongs.</summary>
    public void DuplicateSelection()
    {
        // WHY (20 Oct 2026, Bug Hunt 8, #81): ignored while a move-drag is
        // held. EndStroke settles whatever is picked at mouse-up, so swapping
        // the selection mid-drag left the moved block stacked, unsettled, on
        // what it was dropped over - and saved that way.
        if (_movingSelection)
        {
            return;
        }

        if (_selection.IsEmpty)
        {
            StatusChanged?.Invoke("Nothing picked. Drag a box round something first.");
            return;
        }

        // The nudged block lands first, so the copy is of where it ended up
        // and the timer does not settle the copy in its place.
        FlushPendingSettle();

        var box = _selection.Bounds();
        var copy = _selection.Copy();

        Remember();
        _selection = RcpSelection.Paste(_design, copy, Beside(box));

        if (_selection.Route.Count > 0)
        {
            _design.RouteAuto = false;
        }

        AnnounceSelection();
        RaiseChanged();
    }

    /// <summary>
    /// Where a copy of <paramref name="box"/> goes so that it lands CLEAR of
    /// the original.
    ///
    /// It used to go one tile down and across, which looked right and was
    /// not: a paste settles what it has landed on (RcpSelection.Settle), and
    /// one tile of overlap meant the duplicate cleared away the original's
    /// own tiles and rails underneath it. Duplicating a 4 by 2 block left
    /// 13 tiles where there should have been 16, with a hole in the block
    /// you had just copied.
    ///
    /// A whole box-width clear of it, plus a tile of gap so the two do not
    /// even share a seam - which is also the more useful place for a track:
    /// duplicate a corner complex and it lands beside the original, ready
    /// to be dragged into line.
    /// </summary>
    internal static Point Beside(Rectangle box) =>
        new(box.Left + Math.Max(1, box.Width) + 1, box.Top);

    public void DeleteSelection()
    {
        if (_selection.IsEmpty)
        {
            StatusChanged?.Invoke("Nothing picked. Drag a box round something first.");
            return;
        }

        // 17 Oct 2026, Bug Hunt 8, A31-1: a nudge run still waiting for its
        // timer is landed first, or Remember snapshots the plan with the
        // nudged pieces stacked on what they hit, and Undo brings that back.
        FlushPendingSettle();

        var pieces = _selection.Pieces;

        Remember();
        LineTouchedByHand(deleting: true);
        _selection.DeleteFrom(_design);
        _selection = new RcpSelection();

        SelectionChanged?.Invoke();
        StatusChanged?.Invoke($"{Plural.Of(pieces, "piece")} taken off. Ctrl+Z puts them back.");
        RaiseChanged();
    }

    /// <summary>
    /// Moves what is picked by whole tiles - the arrow keys.
    ///
    /// A RUN of presses is one gesture, the way a drag is. It used to settle
    /// on every press, which is exactly what RcpSelection.Settle says must
    /// not happen: holding an arrow down walked the selection across the
    /// plan eating everything it passed over, and nudging back did not bring
    /// any of it back. Now the move happens at once and the overlap is
    /// resolved once, a moment after the last press - so travelling over
    /// something and carrying on costs it nothing, and stopping on it
    /// replaces it, which is what a drag does.
    /// </summary>
    public void NudgeSelection(int cols, int rows)
    {
        if (_selection.IsEmpty)
        {
            return;
        }

        // One undo step for the whole run, taken on the first press of it -
        // told apart from the next run by the gap since the last press, the
        // same way the wheel's sizing gesture is (RememberWheelResize).
        var now = Environment.TickCount64;

        if (!_nudgeSettle.Enabled || now - _nudgedAt > NudgeRunGapMs)
        {
            Remember();
        }

        _nudgedAt = now;

        LineTouchedByHand(deleting: false);
        _selection.Move(cols, rows);

        // Restarted on every press, so it only fires once the run stops.
        _nudgeSettle.Stop();
        _nudgeSettle.Start();

        AnnounceSelection();
        RaiseChanged();
    }

    /// <summary>Settles the nudge run that has just stopped - see NudgeSelection.</summary>
    private void SettleNudgeRun()
    {
        _nudgeSettle.Stop();

        if (_selection.IsEmpty)
        {
            return;
        }

        _selection.Settle(_design);
        RaiseChanged();
    }

    /// <summary>
    /// Lands a nudge run still waiting for its timer, for Save Layout -
    /// see FlushPendingSettle. 17 Oct 2026, Bug Hunt 8, A31-1: the form
    /// reads Result before the timer can fire. Not in the Result getter,
    /// which the side panel reads after every nudge press.
    /// </summary>
    public void LandPendingNudge() => FlushPendingSettle();

    /// <summary>
    /// Settles a nudge run still waiting for its timer, now.
    ///
    /// The timer settles whatever is picked when it fires, so anything that
    /// replaces the selection (Ctrl+A, a paste, a duplicate, a new marquee)
    /// or snapshots the plan (Undo, Redo) has to land the run first.
    /// Otherwise the nudged pieces were left stacked on whatever they had
    /// landed on - for good once Ctrl+A had picked both, as Settle never
    /// clears a picked piece - which is the case ClearSelection already
    /// handles for Escape and the tool keys.
    /// </summary>
    private void FlushPendingSettle()
    {
        if (_nudgeSettle.Enabled)
        {
            SettleNudgeRun();
        }
    }

    /// <summary>Quarter turns clockwise, or anticlockwise for a negative count.</summary>
    public void TurnSelection(int quarters)
    {
        if (!PickedSomething())
        {
            return;
        }

        var turns = ((quarters % 4) + 4) % 4;

        if (turns == 0)
        {
            return;
        }

        // 17 Oct 2026, Bug Hunt 8, A31-1: a nudge run still waiting for its
        // timer is landed first, or Remember snapshots the plan with the
        // nudged pieces stacked on what they hit, and Undo brings that back.
        FlushPendingSettle();

        Remember();
        LineTouchedByHand(deleting: false);

        for (var i = 0; i < turns; i++)
        {
            _selection.TurnClockwise();
        }

        _selection.Settle(_design);

        AnnounceSelection();
        RaiseChanged();
        // Said the way it was asked for: [ asks for one quarter the other
        // way, and is done as three clockwise because that is the same
        // thing and there is only one mapping to get right.
        StatusChanged?.Invoke(quarters switch
        {
            1 => "Turned a quarter clockwise, where it stood.",
            -1 => "Turned a quarter counterclockwise, where it stood.",
            _ => $"Turned {turns} quarters clockwise, where it stood.",
        });
    }

    public void MirrorSelection(bool leftToRight)
    {
        if (!PickedSomething())
        {
            return;
        }

        // 17 Oct 2026, Bug Hunt 8, A31-1: a nudge run still waiting for its
        // timer is landed first, or Remember snapshots the plan with the
        // nudged pieces stacked on what they hit, and Undo brings that back.
        FlushPendingSettle();

        Remember();
        LineTouchedByHand(deleting: false);

        if (leftToRight)
        {
            _selection.MirrorLeftRight();
        }
        else
        {
            _selection.MirrorTopBottom();
        }

        _selection.Settle(_design);

        AnnounceSelection();
        RaiseChanged();
        StatusChanged?.Invoke(leftToRight
            ? "Mirrored left to right."
            : "Mirrored top to bottom.");
    }

    private bool PickedSomething()
    {
        if (!_selection.IsEmpty)
        {
            return true;
        }

        StatusChanged?.Invoke("Nothing picked. Take the Select tool and drag a box round something first.");

        return false;
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
            _paceLaps = 0;
            _paceDistance = 0;
            _paceTimer.Start();
        }

        PlayingChanged?.Invoke();
        Invalidate();
    }

    public void StopPlaying()
    {
        if (_paceTimer.Enabled)
        {
            _paceTimer.Stop();
            PlayingChanged?.Invoke();
        }
    }

    private void AdvancePaceCar()
    {
        var length = RcpPlan.RouteLengthInTiles(_design);

        if (length <= 0)
        {
            StopPlaying();
            return;
        }

        var now = DateTime.UtcNow;
        var elapsed = (float)(now - _paceLastTick).TotalSeconds * PaceRate;
        _paceLastTick = now;

        var travelled = _paceDistance + length * elapsed / (float)Math.Max(1.0, LapSeconds);

        // Over the line: another lap done - unless the line itself changed
        // under the car, which only moves it.
        if (Math.Abs(length - _paceLength) < 1e-3f)
        {
            _paceLaps += (int)(travelled / length);
        }

        _paceLength = length;
        _paceDistance = travelled % length;

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

        // A stretch being dragged takes no other press - a right click
        // would erase under it.
        if (_stretchColumns is not null)
        {
            return;
        }

        // The lap chip (the second) is a button too: the sectors - in the
        // angled view as well, where it shows too.
        if (e.Button == MouseButtons.Left && !_spaceHeld && _lapChipBox is { } under && under.Contains(e.Location))
        {
            LapChipClicked?.Invoke(new Point((int)under.Left, (int)under.Bottom + 2));
            return;
        }

        // The angled view only looks: a drag turns it, and nothing else
        // reaches the plan.
        if (Angled)
        {
            _angleFrom = e.Button == MouseButtons.Left ? e.Location : null;
            return;
        }

        // The zoom readout in the corner is a button: back to 100%.
        if (e.Button == MouseButtons.Left && !_spaceHeld && _zoomChip.Contains(e.Location))
        {
            ZoomHome();
            return;
        }

        if (e.Button == MouseButtons.Middle
            || (e.Button == MouseButtons.Right && ModifierKeys.HasFlag(Keys.Shift))
            || (e.Button == MouseButtons.Left && _spaceHeld))
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
                    _design.RouteAuto = false;
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

            case RcpTool.Piece:
                // One stroke: a click lays one piece, a drag a run of them.
                _painting = true;
                BeginStroke();
                _lastPaintedCell = ToCell(e.Location);
                LayPiece(_lastPaintedCell.Value, e.Location, dragging: false);
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

            case RcpTool.Marker when Item == RcpPaletteItem.Label:
                var labelCell = ToCell(e.Location);

                if (_design.At(labelCell.X, labelCell.Y) is { } labelled)
                {
                    LabelRequested?.Invoke(labelCell, labelled.Label ?? string.Empty);
                }
                else
                {
                    StatusChanged?.Invoke("A label goes on a tile - lay the floor there first.");
                }

                break;

            case RcpTool.Marker:
                PlaceMarker(ToCell(e.Location), e.Location);
                break;

            case RcpTool.Lane:
                _painting = true;
                BeginStroke();

                var laneCell = ToCell(e.Location);

                if (LaneRing)
                {
                    // Nothing is laid until the box is let go.
                    _ringFrom = laneCell;
                    _ringAt = laneCell;
                    Invalidate();
                }
                else
                {
                    _lastPaintedCell = laneCell;
                    StartLane(laneCell);
                }

                break;

            case RcpTool.Corner:
                if (CornerAt(e.Location) is { } bend)
                {
                    CycleCorner(bend.At);
                }
                else
                {
                    StatusChanged?.Invoke("Click nearer a corner of the grid - where two rails meet, or on a turn rail.");
                }

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
                _draggingBackdrop = true;
                break;

            case RcpTool.Select when !ModifierKeys.HasFlag(Keys.Shift) && StretchHandleAt(e.Location) is { } columns:
                // A handle on the selection's right or bottom edge: drag
                // it to make the plan longer or shorter there (RcpStretch).
                // Nothing changes until it is let go.
                FlushPendingSettle();
                _painting = true;

                // A stroke, so nothing else - Undo, Redo - lands mid-drag.
                BeginStroke();
                _stretchColumns = columns;
                _stretchBox = _selection.Bounds();
                _stretchBy = 0;
                Invalidate();
                break;

            case RcpTool.Select:
                _painting = true;

                // Over what is already picked - and not adding to it -
                // means this drag moves it. Otherwise it draws a marquee
                // round something new.
                if (PointerOnSelection(e.Location) && !ModifierKeys.HasFlag(Keys.Shift))
                {
                    // One drag is one undo step, taken the first time the
                    // selection actually goes anywhere (see OnMouseMove).
                    BeginStroke();
                    _movingSelection = true;
                    _moveLast = ToCell(e.Location);
                    _moved = false;
                }
                else
                {
                    // Picking changes nothing on the plan, so it takes no
                    // undo step - Ctrl+Z after a marquee should take back
                    // the last edit, not the last look.
                    _marqueeFrom = e.Location;
                    _marqueeAt = e.Location;
                    _marqueeAdds = ModifierKeys.HasFlag(Keys.Shift);
                }

                Invalidate();
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

        if (Angled)
        {
            if (_angleFrom is { } from && e.Button == MouseButtons.Left)
            {
                _angle = _angle.Dragged(e.X - from.X, e.Y - from.Y);
                _angleFrom = e.Location;
                Invalidate();
            }

            return;
        }

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
        // 20 Oct 2026, #82: the same leap came from the 7 key switching to
        // the Photo tool mid-drag, so the drag must also have started with
        // the Photo tool (_draggingBackdrop).
        if (_painting && _draggingBackdrop && !_ctrlErasing && Tool == RcpTool.Backdrop && e.Button == MouseButtons.Left)
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

        // The piece's ghost follows the edge or corner the pointer is near,
        // inside the cell as well as from one cell to the next.
        if (Tool == RcpTool.Piece && !_painting)
        {
            var piece = (FacingAt(e.Location, dragging: false), CornerAtPointer(e.Location, dragging: false));

            if (piece != _hoverPiece)
            {
                _hoverPiece = piece;
                changed = true;
            }
        }

        var edge = Tool is RcpTool.Wall or RcpTool.Erase ? EdgeAt(e.Location) : null;
        var corner = Tool is RcpTool.Curve or RcpTool.Erase or RcpTool.Corner ? CornerAt(e.Location) : null;

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
                _design.RouteAuto = false;
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

        if (_stretchColumns is { } stretchingColumns && e.Button == MouseButtons.Left)
        {
            var at = stretchingColumns
                ? (e.X - _origin.X) / _scale - _stretchBox.Right
                : (e.Y - _origin.Y) / _scale - _stretchBox.Bottom;

            // Shorter by at most all but one of the selection's tiles, and
            // longer by no more than any hall needs.
            var least = -((stretchingColumns ? _stretchBox.Width : _stretchBox.Height) - 1);
            var by = Math.Clamp((int)MathF.Round(at), Math.Min(0, least), MostStretch);

            if (by != _stretchBy)
            {
                _stretchBy = by;
                StatusChanged?.Invoke(StretchSays(stretchingColumns, by) + " Let go to make it so.");
                Invalidate();
            }

            return;
        }

        if (_movingSelection && e.Button == MouseButtons.Left && _moveLast is { } last)
        {
            var now = ToCell(e.Location);

            if (now != last)
            {
                // Only a real move is remembered, and only once per drag
                // (the stroke opened on the press sees to that) - so a
                // click on the selection that leaves it where it was adds
                // nothing to undo.
                Remember();
                LineTouchedByHand(deleting: false);

                _selection.Move(now.X - last.X, now.Y - last.Y);
                _moveLast = now;
                _moved = true;

                RaiseChanged();
            }

            return;
        }

        if (_marqueeFrom is not null && e.Button == MouseButtons.Left)
        {
            _marqueeAt = e.Location;
            Invalidate();
            return;
        }

        // The pointer says what a press would do: a hand over what is
        // picked, because dragging there moves it.
        // Not while Space is held: the next press pans, and the hand says so.
        if (Tool == RcpTool.Select && !_panning && !_spaceHeld)
        {
            var wanted = StretchHandleAt(e.Location) switch
            {
                true => Cursors.SizeWE,
                false => Cursors.SizeNS,
                null => PointerOnSelection(e.Location) ? Cursors.SizeAll : Cursors.Default,
            };

            if (Cursor != wanted)
            {
                Cursor = wanted;
            }
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

                case RcpTool.Piece when _lastPaintedCell != cell:
                    // A run: every cell the drag crosses, each with its rail
                    // on the side R set - the cell pressed on keeps the edge
                    // it was pressed near.
                    foreach (var step in CellsBetween(_lastPaintedCell ?? cell, cell))
                    {
                        LayPiece(step, e.Location, dragging: true);
                    }

                    _lastPaintedCell = cell;
                    break;

                case RcpTool.Wall:
                    // No cycling on a drag: running along a line should lay
                    // rail, not toggle what it passes over.
                    ExtendRailRun(ToGridPoint(e.Location));
                    break;

                case RcpTool.Lane when _ringFrom is not null:
                    if (_ringAt != cell)
                    {
                        _ringAt = cell;
                        Invalidate();
                    }

                    break;

                case RcpTool.Lane:
                    // A drag that started with another tool (L pressed mid-
                    // drag) starts its lane here.
                    if (_laneId is null)
                    {
                        StartLane(cell);
                    }

                    // Filled in behind a fast drag, as the floor brush is.
                    foreach (var step in CellsBetween(_lastPaintedCell ?? cell, cell))
                    {
                        PaintLane(step);
                    }

                    _lastPaintedCell = cell;
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

        if (Angled)
        {
            _angleFrom = null;
            return;
        }

        // 20 Oct 2026, #82: keyed on how the drag started, to match OnMouseMove.
        if (_painting && _draggingBackdrop && Tool == RcpTool.Backdrop)
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

        // The ring is laid when its box is let go.
        if (_painting && !_ctrlErasing && Tool == RcpTool.Lane && e.Button == MouseButtons.Left
            && _ringFrom is { } ringFrom && _ringAt is { } ringTo)
        {
            LayRing(ringFrom, ringTo);
        }

        // What the marquee caught, before EndStroke clears it. A marquee
        // abandoned by Alt-Tab goes through EndStroke without coming here,
        // so it is dropped rather than applied.
        if (e.Button == MouseButtons.Left && _marqueeFrom is { } from && _marqueeAt is { } to)
        {
            PickUp(from, to);
        }

        if (_stretchColumns is { } stretchColumns)
        {
            // Only the button that started it ends it.
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            _stretchColumns = null;
            FinishStretch(stretchColumns, _stretchBy);
        }

        var movedSomething = _movingSelection && _moved;

        EndStroke();

        if (movedSomething)
        {
            StatusChanged?.Invoke($"Moved. {_selection.Describe()}");
            SelectionChanged?.Invoke();
        }

        _painting = false;
        _draggingRoutePoint = -1;

        if (_panning)
        {
            _panning = false;

            // Still holding Space: the next press pans again, and the hand says so.
            Cursor = _spaceHeld ? Cursors.Hand : Cursors.Default;
        }
    }

    /// <summary>
    /// Takes what a finished marquee covers.
    ///
    /// A click on bare floor with nothing dragged clears the selection,
    /// which is what clicking away from something means everywhere else.
    /// Holding Shift adds instead - or takes back out, if everything under
    /// the marquee was already picked, so the same gesture does both.
    /// </summary>
    private void PickUp(Point from, Point to)
    {
        FlushPendingSettle();

        var dragged = Math.Abs(to.X - from.X) > 3 || Math.Abs(to.Y - from.Y) > 3;
        var found = RcpSelection.In(_design, MarqueeCells(from, to));

        if (found.IsEmpty && !_marqueeAdds)
        {
            if (!dragged || !_selection.IsEmpty)
            {
                ClearSelection();
                StatusChanged?.Invoke("Nothing there. Drag a box round a piece of the plan to pick it up.");
            }

            return;
        }

        if (_marqueeAdds)
        {
            if (!found.IsEmpty && _selection.HoldsAllOf(found))
            {
                _selection.Remove(found);
            }
            else
            {
                _selection.Add(found);
            }
        }
        else
        {
            _selection = found;
        }

        AnnounceSelection();
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
        // WHY (20 Oct 2026, Bug Hunt 8, #83): a pure pan sets only _panning,
        // so it returned here and stayed on after Alt-Tab or a dialog took
        // the mouse - the view then followed every plain mouse move. A pan
        // now reaches the reset below too.
        if (Capture || (!_painting && !_panning && _draggingRoutePoint < 0))
        {
            return;
        }

        EndStroke();

        _painting = false;
        _draggingRoutePoint = -1;

        if (_panning)
        {
            _panning = false;

            // Still holding Space: the next press pans again, and the hand says so.
            Cursor = _spaceHeld ? Cursors.Hand : Cursors.Default;
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (Angled)
        {
            _angle = _angle.Zoomed(e.Delta / 120f);
            Invalidate();
            return;
        }

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

        // Nothing on the plan changes while it is seen angled - not a
        // nudge, a turn or a delete of something picked out of sight - or
        // while a stretch handle is held.
        if (Angled || _stretchColumns is not null)
        {
            return;
        }

        switch (e.KeyCode)
        {
            // Space on its own: Alt+Space is the window's own menu, and
            // Ctrl+Space is nothing to do with moving the view.
            case Keys.Space when e.Modifiers == Keys.None:
                // Held, not toggled: the view moves while it is down. Auto-
                // repeat sends it again and again, which changes nothing.
                if (!_spaceHeld)
                {
                    _spaceHeld = true;

                    if (!_panning)
                    {
                        Cursor = Cursors.Hand;
                    }
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
                break;

            // Shift+R: the racing line the other way round (the Reverse button).
            case Keys.R when e.Shift:
                ReverseRoute();
                e.Handled = true;
                break;

            // With the Pieces tool, R turns the next piece instead.
            case Keys.R when Tool == RcpTool.Piece:
                TurnPiece();
                StatusChanged?.Invoke(Item == RcpPaletteItem.CurveTile
                    ? $"The next outer curve tile has its turn across the {Words(PieceCorner)} corner."
                    : $"The next rail goes on the {PieceFacing.ToString().ToLowerInvariant()} edge.");
                e.Handled = true;
                break;

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

            // --- What is picked ---
            // The clipboard keys live on the window rather than here, so
            // picking the Select tool lights its button up as well - see
            // RcpTrackEditorForm.ProcessCmdKey.

            case Keys.Escape:
                if (HasSelection)
                {
                    ClearSelection();
                    StatusChanged?.Invoke("Dropped.");
                    e.Handled = true;
                }

                break;

            case Keys.Delete:
            case Keys.Back:
                if (HasSelection)
                {
                    DeleteSelection();
                    e.Handled = true;
                }

                break;

            case Keys.Left:
            case Keys.Right:
            case Keys.Up:
            case Keys.Down:
                if (HasSelection)
                {
                    NudgeSelection(
                        e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : 0,
                        e.KeyCode == Keys.Up ? -1 : e.KeyCode == Keys.Down ? 1 : 0);

                    e.Handled = true;
                }

                break;

            // Round brackets on the number row would be Shift+9, so the
            // square ones: ] clockwise, [ back the other way. Gated on
            // there being something picked, like the keys above - a key
            // that answers back with a complaint when another tool is in
            // hand is a key nobody presses twice.
            case Keys.OemCloseBrackets:
                if (HasSelection)
                {
                    TurnSelection(1);
                    e.Handled = true;
                }

                break;

            case Keys.OemOpenBrackets:
                if (HasSelection)
                {
                    TurnSelection(-1);
                    e.Handled = true;
                }

                break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.KeyCode == Keys.Space)
        {
            ReleaseSpace();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Space let go somewhere else - another window took the focus while it
    /// was down - must not leave the left button panning for good.
    /// </summary>
    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        ReleaseSpace();
    }

    private void ReleaseSpace()
    {
        if (!_spaceHeld)
        {
            return;
        }

        _spaceHeld = false;

        // A pan in progress keeps its own cursor until the button comes up.
        if (!_panning)
        {
            Cursor = Cursors.Default;
        }
    }

    // --- Paint ----------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using (var background = new SolidBrush(BoardFloor))
        {
            g.FillRectangle(background, ClientRectangle);
        }

        if (Angled)
        {
            RcpAngledView.Draw(g, _design, ClientSize, _angle, _paceTimer.Enabled ? PaceCarPosition() : null);
            DrawChips(g);
            DrawPaceReadout(g);
            return;
        }

        if (ShowGrid)
        {
            DrawGrid(g);
        }

        RcpTrackPainter.Draw(g, _design, _scale, _origin, ShowRoute, showLabels: true, backdrop: _backdrop);

        DrawCustomTiles(g);
        DrawSelection(g);
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
        DrawChips(g);
        DrawZoom(g);
        DrawPaceReadout(g);
    }

    /// <summary>
    /// While Play runs: the lap the car is on and how fast it is going -
    /// the lap's length over its time, at the speed Play is set to - over
    /// the board's bottom-left, above the ruler.
    /// </summary>
    private void DrawPaceReadout(Graphics g)
    {
        if (!_paceTimer.Enabled)
        {
            return;
        }

        var metres = RcpPlan.RouteLengthInTiles(_design) * _design.TileSizeMm / 1000.0;
        var speed = metres / Math.Max(1.0, LapSeconds);
        var text = $"Lap {_paceLaps + 1} · {speed:0.0} m/s ({speed * 3.6:0.0} km/h)"
                   + (Math.Abs(PaceRate - 1f) > 0.01f ? $" · shown at {PaceRate:0.#}x" : string.Empty);

        var font = AppTheme.SmallFont;
        var padX = UiLayout.AtTextSize(9);
        var padY = UiLayout.AtTextSize(4);
        var size = g.MeasureString(text, font);
        var box = new RectangleF(UiLayout.AtTextSize(10), Height - UiLayout.AtTextSize(44) - size.Height, size.Width + padX * 2, size.Height + padY * 2);

        using var fill = new SolidBrush(Color.FromArgb(215, 34, 37, 44));
        using var edge = new Pen(AppTheme.Accent, 1.4f);
        using var ink = new SolidBrush(ColorTranslator.FromHtml("#D9DEE7"));

        using (var path = AppTheme.RoundedRect(box, (int)Math.Max(4, box.Height / 2f)))
        {
            g.FillPath(fill, path);
            g.DrawPath(edge, path);
        }

        g.DrawString(text, font, ink, box.X + padX, box.Y + padY);
    }

    /// <summary>
    /// The zoom readout, in the board's bottom-right corner, in the chips'
    /// style - and a button: a click on it goes back to 100% (ZoomHome).
    /// </summary>
    private void DrawZoom(Graphics g)
    {
        var font = AppTheme.SmallFont;
        var text = $"{ZoomPercent}%";
        var padX = UiLayout.AtTextSize(9);
        var padY = UiLayout.AtTextSize(4);
        var inset = UiLayout.AtTextSize(10);

        var size = g.MeasureString(text, font);
        var box = new RectangleF(
            Width - inset - size.Width - padX * 2,
            Height - inset - size.Height - padY * 2,
            size.Width + padX * 2,
            size.Height + padY * 2);

        _zoomChip = box;

        using var fill = new SolidBrush(Color.FromArgb(215, 34, 37, 44));
        using var edge = new Pen(Color.FromArgb(90, BoardInk), 1f);
        using var ink = new SolidBrush(ColorTranslator.FromHtml("#D9DEE7"));

        using (var path = AppTheme.RoundedRect(box, (int)Math.Max(4, box.Height / 2f)))
        {
            g.FillPath(fill, path);
            g.DrawPath(edge, path);
        }

        g.DrawString(text, font, ink, box.X + padX, box.Y + padY);
    }

    /// <summary>
    /// Where a click will land: a ring round the grid point, so the snap is
    /// seen before the click rather than after it.
    /// </summary>
    private void DrawSnapPoint(Graphics g, PointF point, Color colour)
    {
        var ring = Math.Max(5f, _scale * 0.14f);

        using var halo = new SolidBrush(Color.FromArgb(70, colour));
        using var pen = new Pen(colour, 2f);
        using var dot = new SolidBrush(colour);

        g.FillEllipse(halo, point.X - ring, point.Y - ring, ring * 2f, ring * 2f);
        g.DrawEllipse(pen, point.X - ring, point.Y - ring, ring * 2f, ring * 2f);
        g.FillEllipse(dot, point.X - 3, point.Y - 3, 6, 6);
    }

    /// <summary>A ghost of a turn rail: its arc, in the tool's colour, as RcpTrackPainter.DrawCurve would place it.</summary>
    private void DrawGhostTurn(Graphics g, Point at, RcpQuadrant quadrant, float radiusTiles, Color colour)
    {
        var point = new PointF(_origin.X + at.X * _scale, _origin.Y + at.Y * _scale);
        var radius = Math.Max(0.25f, radiusTiles) * _scale;
        var (dx, dy) = RcpAutoRails.Step(quadrant);

        var start = quadrant switch
        {
            RcpQuadrant.NorthEast => 90f,
            RcpQuadrant.SouthEast => 180f,
            RcpQuadrant.SouthWest => 270f,
            _ => 0f,
        };

        var centre = new PointF(point.X + dx * radius, point.Y + dy * radius);

        using var halo = new Pen(Color.FromArgb(60, colour), Math.Max(8f, _scale * RcpTrackPainter.RailThickness * 2.4f));
        using var pen = new Pen(Color.FromArgb(200, colour), Math.Max(3f, _scale * RcpTrackPainter.RailThickness));

        g.DrawArc(halo, centre.X - radius, centre.Y - radius, radius * 2f, radius * 2f, start, 90f);
        g.DrawArc(pen, centre.X - radius, centre.Y - radius, radius * 2f, radius * 2f, start, 90f);
    }

    /// <summary>A ghost of a straight rail along a seam.</summary>
    private void DrawGhostRail(Graphics g, (int Col, int Row, bool Vertical) seam, Color colour)
    {
        var from = new PointF(_origin.X + seam.Col * _scale, _origin.Y + seam.Row * _scale);
        var to = seam.Vertical ? new PointF(from.X, from.Y + _scale) : new PointF(from.X + _scale, from.Y);

        using var halo = new Pen(Color.FromArgb(60, colour), Math.Max(8f, _scale * RcpTrackPainter.RailThickness * 2.4f));
        using var pen = new Pen(Color.FromArgb(200, colour), Math.Max(3f, _scale * RcpTrackPainter.RailThickness))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };

        g.DrawLine(halo, from, to);
        g.DrawLine(pen, from, to);
    }

    /// <summary>A wash over cells, dashed round each - the ghost of floor about to go down.</summary>
    private void DrawGhostCells(Graphics g, IEnumerable<Point> cells, Color colour)
    {
        using var wash = new SolidBrush(Color.FromArgb(60, colour));
        using var outline = new Pen(colour, 2f) { DashStyle = DashStyle.Dash };

        foreach (var cell in cells)
        {
            var rect = new RectangleF(_origin.X + cell.X * _scale, _origin.Y + cell.Y * _scale, _scale, _scale);

            g.FillRectangle(wash, rect);
            g.DrawRectangle(outline, rect.Left, rect.Top, rect.Width, rect.Height);
        }
    }

    /// <summary>
    /// The board's chips: a rounded pill for each line of Chips, along the
    /// top-left corner, in the board's own ink - so they read the same on
    /// every look, as the board does.
    /// </summary>
    private void DrawChips(Graphics g)
    {
        if (_chips.Count == 0)
        {
            return;
        }

        var font = AppTheme.SmallFont;
        var padX = UiLayout.AtTextSize(9);
        var padY = UiLayout.AtTextSize(4);
        var gap = UiLayout.AtTextSize(6);
        var x = (float)UiLayout.AtTextSize(10);
        var y = (float)UiLayout.AtTextSize(10);

        using var fill = new SolidBrush(Color.FromArgb(215, 34, 37, 44));
        using var edge = new Pen(Color.FromArgb(90, BoardInk), 1f);
        using var ink = new SolidBrush(ColorTranslator.FromHtml("#D9DEE7"));

        _lapChipBox = null;

        using var warnEdge = new Pen(AppTheme.Danger, 1.6f);
        using var warnInk = new SolidBrush(ColorTranslator.FromHtml("#FF8A80"));
        var first = true;

        foreach (var chip in _chips)
        {
            if (string.IsNullOrWhiteSpace(chip))
            {
                continue;
            }

            var warn = first && _chipWarning;
            first = false;

            var text = g.MeasureString(chip, font);
            var box = new RectangleF(x, y, text.Width + padX * 2, text.Height + padY * 2);

            // One under another when the board is too narrow to keep them
            // side by side - never cut off at its edge.
            if (box.Right > Width - gap && x > UiLayout.AtTextSize(10))
            {
                x = UiLayout.AtTextSize(10);
                y += box.Height + gap;
                box = new RectangleF(x, y, box.Width, box.Height);
            }

            using (var path = AppTheme.RoundedRect(box, (int)Math.Max(4, box.Height / 2f)))
            {
                g.FillPath(fill, path);
                g.DrawPath(warn ? warnEdge : edge, path);
            }

            g.DrawString(chip, font, warn ? warnInk : ink, box.X + padX, box.Y + padY);
            if (chip.StartsWith("Lap ", StringComparison.Ordinal))
            {
                _lapChipBox = box;
            }

            x = box.Right + gap;
        }
    }

    private void DrawGrid(Graphics g)
    {
        if (_scale < 12f)
        {
            return;
        }

        var dot = Math.Max(1f, _scale * 0.03f);

        using var brush = new SolidBrush(Color.FromArgb(70, BoardInk));

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

    /// <summary>
    /// The custom tiles marked on the board (railed tiles, Phase 2): a tile
    /// carrying a second rail, which no piece RCP sells does - the club cuts
    /// two more bolt holes and trims a rail to fit (Asher, 7 Nov 2026), as
    /// where a divider meets the outside wall. A dashed amber ring and a
    /// small "custom" tag, so it can't be missed and can't be mistaken for
    /// a mistake.
    /// </summary>
    private void DrawCustomTiles(Graphics g)
    {
        if (PieceReport.CustomTiles.Count == 0)
        {
            return;
        }

        var colour = AppTheme.Warning;

        using var ring = new Pen(colour, Math.Max(2f, _scale / 24f)) { DashStyle = DashStyle.Dash };
        using var tagBack = new SolidBrush(Color.FromArgb(220, BoardFloor));
        using var font = new Font(AppTheme.SmallFont.FontFamily, Math.Clamp(_scale / 7f, 7f, 11f), FontStyle.Bold, GraphicsUnit.Pixel);

        var inset = Math.Max(2f, _scale * 0.08f);

        foreach (var (col, row) in PieceReport.CustomTiles)
        {
            var rect = new RectangleF(_origin.X + col * _scale + inset, _origin.Y + row * _scale + inset, _scale - inset * 2, _scale - inset * 2);
            g.DrawRectangle(ring, rect.X, rect.Y, rect.Width, rect.Height);

            // The tag only once the tile is big enough to carry it.
            if (_scale >= 40f)
            {
                var size = TextRenderer.MeasureText("custom", font);
                var tag = new Rectangle((int)(rect.X + (rect.Width - size.Width) / 2f), (int)(rect.Y + (rect.Height - size.Height) / 2f), size.Width, size.Height);

                g.FillRectangle(tagBack, tag);
                TextRenderer.DrawText(g, "custom", font, tag, colour, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    private void DrawHover(Graphics g)
    {
        switch (Tool)
        {
            case RcpTool.Piece when _hoverCell is { } pieceCell:
            {
                // The whole piece that would go down: the tile, and the rail
                // on it - on the edge or across the corner it would take.
                var (facing, corner) = _painting ? (PieceFacing, PieceCorner) : _hoverPiece;

                DrawGhostCells(g, new[] { pieceCell }, AppTheme.Accent);

                switch (PieceFor(Item))
                {
                    case RcpPiece.Rail or RcpPiece.Finish or RcpPiece.Arched:
                        DrawGhostRail(g, RcpPlan.EdgeLine(pieceCell.X, pieceCell.Y, facing), AppTheme.Accent);
                        break;

                    case RcpPiece.OuterCurve:
                        var turn = RcpPieces.TurnFor(pieceCell.X, pieceCell.Y, corner);
                        DrawGhostTurn(g, turn.At, turn.Quadrant, 1f, AppTheme.Accent);
                        break;
                }

                break;
            }

            case RcpTool.Wall when _hoverEdge is { } edge:
            {
                // The rail that would go in, with a halo and its two ends
                // marked, so the seam a click lands on is plain to see.
                DrawGhostRail(g, edge, AppTheme.Accent);

                var from = new PointF(_origin.X + edge.Col * _scale, _origin.Y + edge.Row * _scale);
                var to = edge.Vertical ? new PointF(from.X, from.Y + _scale) : new PointF(from.X + _scale, from.Y);

                using var ends = new SolidBrush(AppTheme.Accent);
                g.FillEllipse(ends, from.X - 3, from.Y - 3, 6, 6);
                g.FillEllipse(ends, to.X - 3, to.Y - 3, 6, 6);
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

                DrawSnapPoint(g, point, AppTheme.Accent);
                break;
            }

            // The corner this click would make: the turn's arc, or for a
            // square corner the straights that come back.
            case RcpTool.Corner when _hoverCorner is { } bend:
            {
                var point = new PointF(_origin.X + bend.At.X * _scale, _origin.Y + bend.At.Y * _scale);

                if (RcpCorners.Next(_design, bend.At, out _) is not { } next)
                {
                    DrawSnapPoint(g, point, BoardInk);
                    break;
                }

                if (next.Radius > 0)
                {
                    DrawGhostTurn(g, bend.At, next.Quadrant, next.Radius, AppTheme.Accent);
                }
                else if (_design.CurveAt(bend.At.X, bend.At.Y) is { } turn)
                {
                    foreach (var seam in RcpAutoRails.Swept(turn))
                    {
                        DrawGhostRail(g, seam, AppTheme.Accent);
                    }
                }

                DrawSnapPoint(g, point, AppTheme.Accent);
                break;
            }

            // The ring the box will lay, or - too small - the box in red.
            case RcpTool.Lane when _ringFrom is { } ringFrom && _ringAt is { } ringTo:
            {
                if (RcpAutoRails.Ring(ringFrom, ringTo, LaneWidth) is { } ring)
                {
                    DrawGhostCells(g, ring, AppTheme.Accent);
                }
                else
                {
                    var left = Math.Min(ringFrom.X, ringTo.X);
                    var top = Math.Min(ringFrom.Y, ringTo.Y);
                    var cols = Math.Abs(ringTo.X - ringFrom.X) + 1;
                    var rows = Math.Abs(ringTo.Y - ringFrom.Y) + 1;

                    using var outline = new Pen(AppTheme.Danger, 2f) { DashStyle = DashStyle.Dash };
                    g.DrawRectangle(outline, _origin.X + left * _scale, _origin.Y + top * _scale, cols * _scale, rows * _scale);
                }

                break;
            }

            // The lane brush: as many tiles as the lane is wide.
            case RcpTool.Lane when _hoverCell is { } laneAt:
                DrawGhostCells(g, RcpAutoRails.Brush(laneAt, LaneWidth), AppTheme.Accent);
                break;

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

    /// <summary>
    /// What is picked, and the marquee picking it.
    ///
    /// Over the track rather than under it - a highlight drawn first is
    /// covered by the floor, which is painted opaque. So the tiles get a
    /// wash thin enough to read the foam through, and the rails get a
    /// DASHED line along them at their own thickness: the dashes let a
    /// white rail and a pink turn show through, where a solid line of the
    /// same width would simply paint over them and lose which piece is
    /// which.
    /// </summary>
    private void DrawSelection(Graphics g)
    {
        if (Tool != RcpTool.Select)
        {
            return;
        }

        if (!_selection.IsEmpty)
        {
            using var wash = new SolidBrush(Color.FromArgb(64, AppTheme.Accent));
            using var dot = new SolidBrush(Color.FromArgb(220, AppTheme.Accent));

            using var mark = new Pen(Color.FromArgb(235, AppTheme.Accent), Math.Max(2.5f, _scale * RcpTrackPainter.RailThickness))
            {
                DashStyle = DashStyle.Dash,
                StartCap = LineCap.Flat,
                EndCap = LineCap.Flat,
            };

            foreach (var tile in _selection.Tiles)
            {
                g.FillRectangle(wash, _origin.X + tile.Col * _scale, _origin.Y + tile.Row * _scale, _scale, _scale);
            }

            foreach (var decal in _selection.Decals)
            {
                g.FillRectangle(wash, _origin.X + decal.Col * _scale, _origin.Y + decal.Row * _scale, _scale, _scale);
            }

            foreach (var wall in _selection.Walls)
            {
                g.DrawLine(
                    mark,
                    _origin.X + wall.From.X * _scale,
                    _origin.Y + wall.From.Y * _scale,
                    _origin.X + wall.To.X * _scale,
                    _origin.Y + wall.To.Y * _scale);
            }

            foreach (var curve in _selection.Curves)
            {
                // The same arc as the painter draws: its centre one radius
                // into the quadrant, so the ends land on the two grid
                // lines (RcpTrackPainter.DrawCurve).
                var radius = Math.Max(0.25f, curve.Radius) * _scale;

                var (dx, dy) = curve.Quadrant switch
                {
                    RcpQuadrant.NorthEast => (1f, -1f),
                    RcpQuadrant.SouthEast => (1f, 1f),
                    RcpQuadrant.SouthWest => (-1f, 1f),
                    _ => (-1f, -1f),
                };

                var start = curve.Quadrant switch
                {
                    RcpQuadrant.NorthEast => 90f,
                    RcpQuadrant.SouthEast => 180f,
                    RcpQuadrant.SouthWest => 270f,
                    _ => 0f,
                };

                var centre = new PointF(
                    _origin.X + curve.Col * _scale + dx * radius,
                    _origin.Y + curve.Row * _scale + dy * radius);

                g.DrawArc(mark, centre.X - radius, centre.Y - radius, radius * 2f, radius * 2f, start, 90f);
            }

            foreach (var point in _selection.Route)
            {
                var size = Math.Max(3f, _scale * 0.1f);

                g.FillEllipse(
                    dot,
                    _origin.X + point.X * _scale - size,
                    _origin.Y + point.Y * _scale - size,
                    size * 2f,
                    size * 2f);
            }

            var box = _selection.Bounds();

            using var edge = new Pen(AppTheme.Accent, 1.6f) { DashStyle = DashStyle.Dash };

            g.DrawRectangle(
                edge,
                _origin.X + box.Left * _scale,
                _origin.Y + box.Top * _scale,
                Math.Max(1f, box.Width * _scale),
                Math.Max(1f, box.Height * _scale));

            DrawStretch(g);
        }

        // The marquee, snapped to the tiles it is going to take - so what
        // is drawn is what will be picked, rather than where the pointer
        // happens to be.
        if (_marqueeFrom is { } from && _marqueeAt is { } to)
        {
            var cells = MarqueeCells(from, to);

            using var wash = new SolidBrush(Color.FromArgb(38, AppTheme.Accent));
            using var edge = new Pen(AppTheme.Accent, 1.6f) { DashStyle = DashStyle.Dash };

            var x = _origin.X + cells.Left * _scale;
            var y = _origin.Y + cells.Top * _scale;
            var w = cells.Width * _scale;
            var h = cells.Height * _scale;

            g.FillRectangle(wash, x, y, w, h);
            g.DrawRectangle(edge, x, y, w, h);
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

        using var pen = new Pen(BoardInk, 2f);
        using var brush = new SolidBrush(BoardInk);

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
            _nudgeSettle.Stop();
            _nudgeSettle.Dispose();
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
        RcpPaletteItem.Kerb => "Apex curb",
        RcpPaletteItem.Split1 => "Split 1 loop",
        RcpPaletteItem.Split2 => "Split 2 loop",
        RcpPaletteItem.Split3 => "Split 3 loop",
        RcpPaletteItem.Label => "Tile label",
        RcpPaletteItem.RailTile => "Rail tile",
        RcpPaletteItem.CurveTile => "Outer curve tile",
        RcpPaletteItem.FinishTile => "Finish rail tile",
        RcpPaletteItem.ArchedTile => "Arched rail tile",
        RcpPaletteItem.BlankTile => "Blank tile",
        _ => "Rostrum / table",
    };

    public static string HintOf(RcpPaletteItem item) => item switch
    {
        RcpPaletteItem.Floor => "A plain tile, no rail. Drag to lay a stretch of floor. (1)",
        RcpPaletteItem.RailTile => "A tile with its rail on - click near the edge you want it on, or drag a row. R turns it. (2)",
        RcpPaletteItem.CurveTile => "The quarter-circle tile with its one-tile turn. Click in the corner the turn goes across. (3)",
        RcpPaletteItem.FinishTile => "A tile with the finish-line rail on it.",
        RcpPaletteItem.ArchedTile => "A rail tile whose rail arches into the lane, for a straight.",
        RcpPaletteItem.BlankTile => "A tile with no bolt holes - infield, lane widening. It takes no rail.",
        RcpPaletteItem.Blocked => "Floor you can't use - a pillar, a table leg.",
        RcpPaletteItem.Rail => "Drag to run rail. Corners turn themselves.",
        RcpPaletteItem.Divider => "Two back to back, for racing on both sides.",
        RcpPaletteItem.Turn => "A corner by hand, at whatever radius you set.",
        RcpPaletteItem.FinishLine => "The checkered line across the track.",
        RcpPaletteItem.GridBox => "One staggered starting box.",
        RcpPaletteItem.Post => "A quarter of the pink post. Click the tile's corner it goes in.",
        RcpPaletteItem.Kerb => "Red and white, for marking an apex.",
        RcpPaletteItem.Split1 or RcpPaletteItem.Split2 or RcpPaletteItem.Split3 =>
            "Where a split loop lies across the track. With the racing line through it, it measures each sector for sector speeds.",
        RcpPaletteItem.Label => "Name a tile - pit lane, marshal. Click a tile to type its name. (T)",
        _ => "Anything else standing on the floor.",
    };

    public static RcpTool ToolFor(RcpPaletteItem item) => item switch
    {
        RcpPaletteItem.Floor or RcpPaletteItem.Blocked => RcpTool.Floor,
        RcpPaletteItem.Rail or RcpPaletteItem.Divider => RcpTool.Wall,
        RcpPaletteItem.Turn => RcpTool.Curve,
        RcpPaletteItem.RailTile or RcpPaletteItem.CurveTile or RcpPaletteItem.FinishTile
            or RcpPaletteItem.ArchedTile or RcpPaletteItem.BlankTile => RcpTool.Piece,
        _ => RcpTool.Marker,
    };

    /// <summary>
    /// The pieces, each a tile with its rail already on, as they go down on
    /// a real track (railed tiles, Phase 2). The inner corner tile isn't
    /// among them: it is worked out at the inside of every bend.
    /// </summary>
    public static IReadOnlyList<RcpPaletteItem> Pieces { get; } = new[]
    {
        RcpPaletteItem.Floor,
        RcpPaletteItem.RailTile,
        RcpPaletteItem.CurveTile,
        RcpPaletteItem.FinishTile,
        RcpPaletteItem.ArchedTile,
        RcpPaletteItem.BlankTile,
        RcpPaletteItem.Blocked,
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
        RcpPaletteItem.Label,
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

            case RcpPaletteItem.Label:
                design.Tiles[0].Label = "Pit";
                break;

            // The pieces, drawn as Lay puts them down.
            case RcpPaletteItem.RailTile:
                RcpPieces.Lay(design, 0, 0, RcpPiece.Rail, RcpEdge.South, RcpQuadrant.NorthWest);
                break;

            case RcpPaletteItem.FinishTile:
                RcpPieces.Lay(design, 0, 0, RcpPiece.Finish, RcpEdge.South, RcpQuadrant.NorthWest);
                design.Decals.Add(new RcpDecal { Col = 0, Row = 0, Kind = RcpDecalKind.FinishLine });
                break;

            case RcpPaletteItem.ArchedTile:
                RcpPieces.Lay(design, 0, 0, RcpPiece.Arched, RcpEdge.South, RcpQuadrant.NorthWest);
                break;

            case RcpPaletteItem.CurveTile:
                RcpPieces.Lay(design, 0, 0, RcpPiece.OuterCurve, RcpEdge.North, RcpQuadrant.NorthWest);
                break;

            case RcpPaletteItem.BlankTile:
                design.Tiles[0].Piece = RcpPiece.Blank;
                break;
        }

        return design;
    }
}
