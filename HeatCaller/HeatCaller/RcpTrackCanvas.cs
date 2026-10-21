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
                ClearSelection();
            }

            Invalidate();
        }
    }

    private RcpTool _tool = RcpTool.Floor;

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

        _design.Tiles.AddRange(stock.Tiles);
        _design.Walls.AddRange(stock.Walls);
        _design.Curves.AddRange(stock.Curves);
        _design.Decals.AddRange(stock.Decals);

        _design.TileSizeMm = stock.TileSizeMm;
        _design.KitName = stock.KitName;

        RaiseChanged();
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
            Unpick(_selection.Forget(curve));
            RaiseChanged();
            return;
        }

        if (EdgeAt(pixel) is { } edge && _design.WallAt(edge.Col, edge.Row, edge.Vertical) is { } wall)
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

        if (_design.At(cell.X, cell.Y) is { } tile)
        {
            Remember();
            _design.Tiles.Remove(tile);
            Unpick(_selection.Forget(tile));
            RaiseChanged();
        }
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

        // Brand-new points, so a selection holding the old ones would be
        // left pointing at pieces that are no longer on the plan - drawn as
        // dots over bare floor, and counted in the box round the selection.
        // Every other path that replaces part of the plan drops it too.
        ClearSelection();

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
        if ((_movingSelection && _moved) || _nudgeSettle.Enabled)
        {
            _selection.Settle(_design);
            _moved = false;
        }

        _nudgeSettle.Stop();

        _selection = new RcpSelection();

        SelectionChanged?.Invoke();
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

        Remember();
        _selection.DeleteFrom(_design);
        _selection = new RcpSelection();

        SelectionChanged?.Invoke();
        StatusChanged?.Invoke("Cut. Ctrl+V puts it down where the pointer is.");
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
                _draggingBackdrop = true;
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
        if (Tool == RcpTool.Select && !_panning)
        {
            var wanted = PointerOnSelection(e.Location) ? Cursors.SizeAll : Cursors.Default;

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

        // What the marquee caught, before EndStroke clears it. A marquee
        // abandoned by Alt-Tab goes through EndStroke without coming here,
        // so it is dropped rather than applied.
        if (e.Button == MouseButtons.Left && _marqueeFrom is { } from && _marqueeAt is { } to)
        {
            PickUp(from, to);
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
            Cursor = Cursors.Default;
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
        _ => "Rostrum / table",
    };

    public static string HintOf(RcpPaletteItem item) => item switch
    {
        RcpPaletteItem.Floor => "The foam. Drag to lay the whole floor first.",
        RcpPaletteItem.Blocked => "Floor you can't use - a pillar, a table leg.",
        RcpPaletteItem.Rail => "Drag to run rail. Corners turn themselves.",
        RcpPaletteItem.Divider => "Two back to back, for racing on both sides.",
        RcpPaletteItem.Turn => "A corner by hand, at whatever radius you set.",
        RcpPaletteItem.FinishLine => "The checkered line across the track.",
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
