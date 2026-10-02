using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace HeatCaller;

/// <summary>
/// The RCP track designer.
///
/// It works the way the track does: lay the floor, then stand the rails on
/// it. An RCP circuit is a continuous field of interlocking foam tiles with
/// silver rails along the seams and pink turn rails at the corners - the
/// floor under the racing line and the floor under the infield are the same
/// tile, and a rail down the middle has racing on both sides of it.
///
/// To get a real layout in exactly, drop a photo of it underneath with
/// Trace photo and draw over the top.
///
/// On the layout pieces (DialogFit): the toolbar along the top - the tools,
/// then Wall the edge, Trace route and Play, then Undo, Redo, Fit and Clear,
/// each group a row of buttons only as wide as their captions and the three
/// a section gap apart, a group that does not fit on the line starting the
/// next (a FieldRow). Under it the board takes the room, with the status line
/// under it; the palette is a column down its left and the side panel one
/// down its right (two Besides, one inside the other), each a column of cards
/// that scrolls by itself when the window is too short for all of it. At the
/// foot, against the right-hand edge: the main-picture box, Save as PNG...,
/// Save Layout and Cancel. It opens at the size it always did, at the text
/// size in use - never bigger than the screen - and can be made bigger, or as
/// narrow as it always could; the board takes what it is given. It used to be
/// placed in pixels: a 1360x840 window - taller than a 1280x720 screen has
/// room for - with a toolbar strip and a footer of fixed heights docked along
/// the top and the bottom and the palette and the side panel of fixed widths
/// docked down either side, every control in them at a fixed place and most
/// at a fixed size; the toolbar's buttons at widths worked out in the body
/// face though drawn in the bold one; the status line wrapped at a fixed
/// 560px; and the footer's buttons placed from the window's width.
/// </summary>
public sealed class RcpTrackEditorForm : Form
{
    /// <summary>
    /// How big the window opens inside its frame at the normal text size -
    /// the size it always opened at. It opens this big at the text size in
    /// use, never bigger than the screen, and can be made bigger: the board
    /// takes the room.
    /// </summary>
    private const int DialogWidth = 1360;
    private const int DialogHeight = 840;

    /// <summary>How narrow the window could always be made, frame and all, at the normal text size - see the constructor.</summary>
    private const int LeastWindowWidth = 1120;

    /// <summary>The board's least height at the normal text size - below it, the window scrolls instead.</summary>
    private const int CanvasLeastHeight = 240;

    /// <summary>The least the palette and the side panel show at the normal text size before each scrolls by itself.</summary>
    private const int ColumnLeastHeight = 160;

    /// <summary>
    /// A palette entry at the normal text size, as it always was - its
    /// sample's size, and its least height, taller when its hint needs its
    /// two lines (PaletteButton.EntryHeight). The palette is as wide as one
    /// in its card, beside its scrollbar - or as its row of turn radii
    /// needs, when that is more (PaletteWidth).
    /// </summary>
    private const int PaletteEntryWidth = 192;
    private const int PaletteEntryHeight = 54;

    /// <summary>The side panel's width at the normal text size, as it always was - or as wide as its widest line needs, when that is more (SideWidth).</summary>
    private const int SidePanelWidth = 314;

    /// <summary>The tile size's number box, and the photo's opacity slider's height, at the normal text size, as they were.</summary>
    private const int TileSizeBoxWidth = 90;
    private const int SliderHeight = 32;

    private readonly RcpTrackCanvas _canvas = new();
    private readonly List<PaletteButton> _paletteButtons = new();
    private readonly ToolTip _tips = new();

    /// <summary>What the plan comes to - as tall as the most it can say (see BuildSidePanel), so the kit check under it stays where it is as the plan changes.</summary>
    private readonly SteadyLabel _lblStats = new();
    private readonly Label _lblKitCheck = new();

    /// <summary>How the tool in hand works, or what the board has just done - as tall as the longest a tool says (ToolHint), so the board over it keeps its size as tools are picked.</summary>
    private readonly SteadyLabel _lblStatus = new();
    private readonly ComboBox _cmbKit = new();
    private readonly NumericUpDown _numTileSize = new();
    private readonly TextBox _txtName = new();
    private readonly CheckBox _chkClosedRoute = new ThemedCheckBox();
    private readonly CheckBox _chkUseAsPhoto = new ThemedCheckBox();

    /// <summary>Save as PNG - kept to be given its colour after the theme pass (see the constructor).</summary>
    private readonly Button _btnPng = new() { Text = "Save as PNG..." };
    private readonly Button _btnPlay = new();
    private readonly SliderControl _barBackdrop = new();

    private Button? _btn30;
    private Button? _btn50;
    private (Button Button, RcpTool Tool)[] _toolButtons = Array.Empty<(Button, RcpTool)>();
    private (Button Button, float Radius)[] _radiusButtons = Array.Empty<(Button, float)>();

    /// <summary>What the kit drop-down needs across for its longest choice, kept until its face or its list changes - see SideWidth.</summary>
    private (Font Font, int Count, int Width)? _kitWidth;

    private readonly string _trackName;

    private bool _loading = true;

    /// <summary>
    /// The plan being drawn - always the canvas's own. It used to be the
    /// object handed to the canvas when the form opened, but the canvas's
    /// Undo and Redo swap in a different copy of the plan: after one Undo,
    /// every stroke went to that copy while Save Layout returned the old
    /// object - with the undone change still in it and everything drawn
    /// since missing - and the tile size, kit, opacity and lap choices were
    /// written to a plan nobody was looking at any more.
    /// </summary>
    public RcpTrackDesign Result => _canvas.Design;

    /// <summary>Whether the plan should be the track's main picture.</summary>
    public bool UseAsPhoto => _chkUseAsPhoto.Checked;

    /// <summary>The name typed on the side panel.</summary>
    public string TrackName => _txtName.Text.Trim();

    public RcpTrackEditorForm(RcpTrackDesign? existing, string trackName, double? averageLapSeconds)
    {
        _trackName = string.IsNullOrWhiteSpace(trackName) ? string.Empty : trackName;

        Text = string.IsNullOrEmpty(_trackName) ? "Track Designer" : $"Track Designer - {_trackName}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        KeyPreview = true;

        _canvas.Design = existing?.Clone() ?? new RcpTrackDesign();
        _canvas.LapSeconds = averageLapSeconds is > 0 ? averageLapSeconds.Value : 12;
        _canvas.DesignChanged += OnDesignChanged;
        _canvas.StatusChanged += message => _lblStatus.Text = message;

        var page = BuildLayout();

        AppTheme.StyleToolTip(_tips);
        AppTheme.ApplyRecursively(this);

        // One button in the app's colour at the foot: Save Layout, which is
        // what the window is for. The theme goes by the caption, and "Save
        // as PNG" has a save in it too - two orange buttons side by side,
        // neither saying which one keeps the track (layout pictures, 30 Sep).
        AppTheme.StyleButton(_btnPng, AppTheme.ButtonKind.Secondary);

        _loading = false;

        HighlightTileSize();
        HighlightRadius();
        RefreshReadouts();

        // The plan framed in the board. It used to be framed here, in a
        // window already at its full size; the board has its size only once
        // the window is sized as it opens (DialogFit, on Load) - so it is
        // framed each time the board is resized until the window is on
        // screen, and left as it is after that, as it always was.
        _canvas.SizeChanged += FrameUntilShown;

        // Why the plan's traced photo is missing, if it is. Said when the
        // plan was loaded, before the status line existed to show it - and
        // the toolbar's first tool then wrote over it anyway.
        if (_canvas.BackdropProblem is { } why)
        {
            _lblStatus.Text = why;
        }

        Shown += (_, _) =>
        {
            _canvas.SizeChanged -= FrameUntilShown;
            _canvas.Focus();
        };

        DialogFit.Keep(this, page, width: DialogWidth, height: DialogHeight);

        // And it can be made as narrow as it always could: 1120px, frame and
        // all, at the text size in use. The palette and the side panel keep
        // their widths, the toolbar goes onto a second line, and the board
        // takes what is left. After DialogFit's own Load handler, added
        // above, which sets the least to the width it opened at.
        Load += (_, _) => MinimumSize = new Size(
            Math.Min(MinimumSize.Width, UiLayout.AtTextSize(LeastWindowWidth)),
            MinimumSize.Height);
    }

    // --- Chrome ---------------------------------------------------------------

    /// <summary>The window's page, built as the class notes describe. Returned for DialogFit to size the window to.</summary>
    private FillPage BuildLayout()
    {
        var toolbar = BuildToolbar();
        var (palette, paletteWidth) = BuildPalette();
        var (side, sideWidth) = BuildSidePanel();
        var footer = BuildFooter();

        // Under the board and as wide as it, and as tall as the longest a
        // tool says there - so picking a tool never resizes the board.
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Wordings = Enum.GetValues<RcpTool>().Select(tool => ToolHint(tool)).ToArray();

        // The board and its status line, with the side panel down their
        // right; the board takes the height the status line leaves it.
        var board = new Beside(sideWidth);
        board.AddFill(_canvas, _ => UiLayout.AtTextSize(CanvasLeastHeight), gapBelow: UiLayout.RowGap);
        board.Add(_lblStatus, gapBelow: 0);
        board.AddSideFill(side, _ => UiLayout.AtTextSize(ColumnLeastHeight), gapBelow: 0);

        // And the palette down the left of all of it.
        var band = new Beside(paletteWidth) { SideOnLeft = true };
        band.AddFill(board, board.LeastHeight);
        band.AddSideFill(palette, _ => UiLayout.AtTextSize(ColumnLeastHeight), gapBelow: 0);

        var page = new FillPage();
        page.Add(toolbar, gapBelow: UiLayout.RowGap);
        page.AddFill(band, band.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(footer, gapBelow: 0);

        // Tab goes from the board to the side panel, the palette, the toolbar
        // and the buttons at the foot, as it always did - the board has the
        // focus as the window opens (Shown), and Tab from it reaches the
        // track's name first. The toolbar is at the top of the page, so it is
        // put after the band here.
        if (band.Parent is { } bandHolder)
        {
            bandHolder.TabIndex = 0;
        }

        toolbar.TabIndex = 1;
        footer.TabIndex = 2;

        Controls.Add(page);

        return page;
    }

    /// <summary>
    /// The toolbar: the seven tools, then Wall the edge, Trace route and
    /// Play, then Undo, Redo, Fit and Clear - each group a row of buttons
    /// only as wide as their captions (a tight ButtonBar), the three a
    /// section gap apart, and a group that does not fit on the line starting
    /// the next (a FieldRow).
    /// </summary>
    private FieldRow BuildToolbar()
    {
        Button Add(string text, string tip, Action click)
        {
            var button = new Button { Text = text };

            button.Click += (_, _) => { click(); _canvas.Focus(); };

            _tips.SetToolTip(button, tip);

            return button;
        }

        var floor = Add("Floor", "Lay the foam. Drag to fill an area. (1)", () => SetTool(RcpTool.Floor));
        var wall = Add("Rails", "Stand a rail along a seam. Click it again to double it. (2)", () => SetTool(RcpTool.Wall));
        var curve = Add("Turns", "Swing a run of rail through 90 degrees. (3)", () => SetTool(RcpTool.Curve));
        var marker = Add("Markings", "The line, grid boxes, kerbs, posts. R turns them. (4)", () => SetTool(RcpTool.Marker));
        var route = Add("Route", "Draw the racing line. (5)", () => SetTool(RcpTool.Route));
        var erase = Add("Erase", "Take things off. Ctrl+click or right-click does this with any tool. (6)", () => SetTool(RcpTool.Erase));
        var photo = Add("Photo", "Drag the traced photo into place; the wheel sizes it. (7)", () => SetTool(RcpTool.Backdrop));

        _toolButtons = new[]
        {
            (floor, RcpTool.Floor), (wall, RcpTool.Wall), (curve, RcpTool.Curve),
            (marker, RcpTool.Marker), (route, RcpTool.Route), (erase, RcpTool.Erase),
            (photo, RcpTool.Backdrop),
        };

        var wallEdge = Add("Wall the edge", "Put a rail round every open edge of the floor.", () => _canvas.WallThePerimeter());
        var traceRoute = Add("Trace route", "Follow the corridor between the rails and lay the racing line down it.", () => _canvas.AutoRoute());

        _btnPlay.Text = "Play";
        _btnPlay.Click += (_, _) =>
        {
            _canvas.TogglePlay();
            _btnPlay.Text = _canvas.IsPlaying ? "Stop" : "Play";
            _canvas.Focus();
        };

        _tips.SetToolTip(_btnPlay, "Send a car round the line you drew.");

        // As wide saying Stop as saying Play, so the buttons after it never
        // move as it changes - it was a fixed 76px for the same reason. Its
        // row sizes it to its caption; this keeps it to the wider of the two,
        // measured the way the row measures (UiLayout.TightButtonSizeFor).
        _btnPlay.MinimumSize = new Size(Math.Max(TightWidth("Play"), TightWidth("Stop")), 0);

        var undo = Add("Undo", "Ctrl+Z", () => _canvas.Undo());
        var redo = Add("Redo", "Ctrl+Y", () => _canvas.Redo());
        var fit = Add("Fit", "Frame the whole plan. (F)", () => _canvas.Fit());
        var clear = Add("Clear", "Start again.", ClearAll);

        SetTool(RcpTool.Floor);

        var toolbar = new FieldRow();
        toolbar.Add(null, new ButtonBar(tight: true).With(floor, wall, curve, marker, route, erase, photo));
        toolbar.Add(null, new ButtonBar(tight: true).With(wallEdge, traceRoute, _btnPlay));
        toolbar.Add(null, new ButtonBar(tight: true).With(undo, redo, fit, clear));

        return toolbar;

        static int TightWidth(string caption)
        {
            using var probe = new Button { Text = caption };

            return UiLayout.TightButtonSizeFor(probe).Width;
        }
    }

    private void SetTool(RcpTool tool)
    {
        _canvas.Tool = tool;

        foreach (var (button, owned) in _toolButtons)
        {
            button.BackColor = owned == tool ? AppTheme.Primary : AppTheme.SecondaryButton;
            button.ForeColor = owned == tool ? AppTheme.OnPrimary : AppTheme.TextDark;
            button.FlatAppearance.BorderSize = 0;
        }

        _lblStatus.Text = ToolHint(tool);
    }

    /// <summary>How a tool works, as the status line says when it is picked.</summary>
    private static string ToolHint(RcpTool tool) => tool switch
    {
        RcpTool.Floor => "Drag to paint floor tiles - the whole track sits on these. Ctrl-drag takes them back off.",
        RcpTool.Wall => "Drag along the grid to run rail, and a turn rail drops in wherever you round a corner. A single click on a seam still adds one, doubles it up, or takes it away.",
        RcpTool.Curve => "Click near a corner of the grid - the arc follows whichever way the pointer is.",
        RcpTool.Marker => "Click to drop a marking. Click it again, or press R, to turn it.",
        RcpTool.Route => "Click to drop points along the racing line. Drag one to move it, right-click to remove it.",
        RcpTool.Erase => "Click to take off whatever is under the pointer.",
        _ => "Drag the photo into place and use the wheel to size it until its tiles line up with the grid.",
    };

    /// <summary>
    /// The palette: the floor, the barriers - with the turn radius under
    /// them - and the markings, each a card of entries under a line of help,
    /// in a column that scrolls by itself. Returns the column, and the rule
    /// for its width (PaletteWidth).
    /// </summary>
    private (PageStack Column, Func<int> Width) BuildPalette()
    {
        PaletteButton Entry(RcpPaletteItem item)
        {
            // As wide as its card, and as tall as its name and two lines of
            // its hint need (PaletteButton.EntryHeight) - it draws itself to
            // the size it is given.
            var button = new PaletteButton(item) { Height = PaletteButton.EntryHeight() };

            button.Chosen += chosen =>
            {
                _canvas.Item = chosen;
                SetTool(RcpTiles.ToolFor(chosen));
                HighlightPalette();
                _canvas.Focus();
            };

            _paletteButtons.Add(button);

            return button;
        }

        // A card for each group: its caption, a line of help, and its
        // entries as close together as they always were - with room under
        // the last for whatever the card goes on to hold.
        Section Group(string caption, string note, IReadOnlyList<RcpPaletteItem> items, int gapAfterLast = 0)
        {
            var card = new Section(caption);

            card.Add(new Label
            {
                Text = note,
                AutoSize = true,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
            });

            for (var i = 0; i < items.Count; i++)
            {
                card.Add(Entry(items[i]), gapBelow: i < items.Count - 1 ? UiLayout.TightButtonGap : gapAfterLast);
            }

            return card;
        }

        var floor = Group("Floor", "One continuous field of tiles. Lay it first.", RcpTiles.Floor);
        var barriers = Group("Barriers", "Rails stand ON the floor, along the seams.", RcpTiles.Barriers, gapAfterLast: UiLayout.RowGap);

        // Turn radius, since a sweeper and a hairpin are the same piece here.
        var lblRadius = new Label
        {
            Text = "Turn radius, in tiles:",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        var radii = new[] { 0.5f, 1f, 1.5f, 2f };
        var buttons = new List<(Button, float)>();

        foreach (var radius in radii)
        {
            var button = new Button { Text = radius.ToString("0.#") };

            var captured = radius;

            button.Click += (_, _) =>
            {
                _canvas.CurveRadius = captured;
                SetTool(RcpTool.Curve);
                HighlightRadius();
                _canvas.Focus();
            };

            buttons.Add((button, radius));
        }

        _radiusButtons = buttons.ToArray();

        // Only as wide as their numbers, and the four on one line: the
        // palette is never narrower than they need (PaletteWidth).
        var radiusRow = new ButtonBar(tight: true).With(_radiusButtons.Select(pair => (Control)pair.Button).ToArray());

        barriers.Add(lblRadius, gapBelow: UiLayout.HintGap);
        barriers.Add(radiusRow, gapBelow: 0);

        var markings = Group("Markings", "Flat things stuck on the floor.", RcpTiles.Markings);

        var column = CardColumn();
        column.Add(floor, gapBelow: UiLayout.RowGap);
        column.Add(barriers, gapBelow: UiLayout.RowGap);
        column.Add(markings, gapBelow: 0);

        HighlightPalette();

        return (column, () => PaletteWidth(radiusRow));
    }

    /// <summary>
    /// The palette's width: an entry as wide as it always was, at the text
    /// size in use - or the turn radii on one line, when they need more - in
    /// its card, beside the scrollbar the column takes its width from when it
    /// scrolls. Asked whenever the column is placed.
    /// </summary>
    private static int PaletteWidth(ButtonBar radii) =>
        Math.Max(UiLayout.AtTextSize(PaletteEntryWidth), radii.OneLineWidth)
        + Section.Inset.Horizontal
        + SystemInformation.VerticalScrollBarWidth;

    private void HighlightPalette()
    {
        foreach (var button in _paletteButtons)
        {
            button.Selected = button.Item == _canvas.Item;
        }
    }

    private void HighlightRadius()
    {
        foreach (var (button, radius) in _radiusButtons)
        {
            var active = Math.Abs(radius - _canvas.CurveRadius) < 0.01f;

            button.BackColor = active ? AppTheme.Primary : AppTheme.SecondaryButton;
            button.ForeColor = active ? AppTheme.OnPrimary : AppTheme.TextDark;
            button.FlatAppearance.BorderSize = 0;
        }
    }

    /// <summary>
    /// The side panel: the track's name, the tile size, the photo being
    /// traced, the lap, and the kit it is checked against - a card each, in
    /// a column that scrolls by itself. Returns the column, and the rule for
    /// its width (SideWidth).
    /// </summary>
    private (PageStack Column, Func<int> Width) BuildSidePanel()
    {
        // --- Track name ---
        _txtName.Text = _trackName;

        var nameCard = new Section("Track name");
        nameCard.Add(_txtName, gapBelow: 0);
        Pieces.KeepHeightTold(_txtName);

        // --- Tile size ---
        _btn30 = new Button { Text = "30 cm" };
        _btn50 = new Button { Text = "50 cm" };

        _btn30.Click += (_, _) => SetTileSize(300);
        _btn50.Click += (_, _) => SetTileSize(500);

        _numTileSize.Width = UiLayout.AtTextSize(TileSizeBoxWidth);
        _numTileSize.Minimum = 100;
        _numTileSize.Maximum = 2000;
        _numTileSize.Increment = 10;
        _numTileSize.Value = (decimal)Math.Clamp(Result.TileSizeMm, 100, 2000);
        _numTileSize.ValueChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            Result.TileSizeMm = (double)_numTileSize.Value;
            HighlightTileSize();
            RefreshReadouts();
        };

        var lblUnits = new Label
        {
            Text = "mm per tile",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        // The size in mm and its unit keep together: beside the two buttons,
        // level with their middles - or, when the column is too narrow for
        // all of it, on a line of their own under them.
        var units = FieldGrid.Inline(_numTileSize, lblUnits);
        units.Anchor = AnchorStyles.Left;

        var tileCard = new Section("Tile size");
        tileCard.Add(new ButtonBar(tight: true).With(_btn30, _btn50, units), gapBelow: 0);

        // --- Trace a photo ---
        var btnTrace = new Button { Text = "Choose photo..." };
        btnTrace.Click += (_, _) => ChooseBackdrop();

        var btnHide = new Button { Text = "Hide" };
        btnHide.Click += (_, _) => { _canvas.ToggleBackdrop(); _canvas.Focus(); };

        var btnDropPhoto = new Button { Text = "Remove" };
        btnDropPhoto.Click += (_, _) => { _canvas.ClearBackdrop(); _canvas.Focus(); };

        // Only as wide as their captions, and the three on one line: the
        // side panel is never narrower than they need (SideWidth).
        var traceButtons = new ButtonBar(tight: true).With(btnTrace, btnHide, btnDropPhoto);

        // As wide as the card, and as tall as it always was, at the text size
        // in use - it draws its track and handle in the middle of that.
        _barBackdrop.Height = UiLayout.AtTextSize(SliderHeight);
        _barBackdrop.Minimum = 5;
        _barBackdrop.Maximum = 100;
        _barBackdrop.Value = Math.Clamp(Result.BackdropOpacity, 5, 100);
        _barBackdrop.ValueChanged += (_, _) =>
        {
            // Not while the panel is being brought back into line with the
            // plan after an Undo or Redo (SyncSidePanel): the slider only
            // goes from 5 to 100, so writing its value back would quietly
            // change a plan whose opacity was saved outside that range.
            if (_loading)
            {
                return;
            }

            Result.BackdropOpacity = _barBackdrop.Value;
            _canvas.Invalidate();
        };

        var lblTraceHint = new Label
        {
            Text = "Drop in a photo of the real track, line its tiles up with the grid using the Photo tool, "
                   + "then draw over it. That's the quickest way to get a layout in exactly.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        var traceCard = new Section("Trace a photo");
        traceCard.Add(traceButtons);
        traceCard.Add(_barBackdrop);
        traceCard.Add(lblTraceHint, gapBelow: 0);

        // --- The lap ---
        _chkClosedRoute.Text = "The line closes into a lap";
        _chkClosedRoute.AutoSize = true;
        _chkClosedRoute.Checked = Result.RouteClosed;
        _chkClosedRoute.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            Result.RouteClosed = _chkClosedRoute.Checked;
            RefreshReadouts();
        };

        // As tall as the most it can say, at a big layout's numbers: a lap
        // round a floor with no rails on it yet, which is the most lines; no
        // lap yet, whose line about it is the longest; and rails, dividers
        // and a lap. The kit check under it then stays where it is as the
        // plan changes. It used to be given a fixed 190px, which the most it
        // can say ran past.
        var floorOnly = new RcpInventory { Tiles = 480, Infield = 64 };
        var railed = new RcpInventory { Tiles = 480, Infield = 64, StraightRails = 240, Dividers = 36, TurnRails = 28, EndCaps = 6 };

        _lblStats.Wordings = new[]
        {
            Readout(floorOnly, lengthTiles: 160f, tileSizeMm: 500, lapSeconds: _canvas.LapSeconds),
            Readout(floorOnly, lengthTiles: 0f, tileSizeMm: 500, lapSeconds: _canvas.LapSeconds),
            Readout(railed, lengthTiles: 160f, tileSizeMm: 500, lapSeconds: _canvas.LapSeconds),
        };

        var lapCard = new Section("The lap");
        lapCard.Add(_chkClosedRoute, stretch: false);
        lapCard.Add(_lblStats, gapBelow: 0);

        // --- Check against a kit ---
        _cmbKit.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbKit.Items.Add("Don't check");

        foreach (var kit in RcpKit.All)
        {
            _cmbKit.Items.Add(kit.Name);
        }

        _cmbKit.SelectedIndex = 0;

        if (!string.IsNullOrWhiteSpace(Result.KitName))
        {
            var saved = _cmbKit.Items.IndexOf(Result.KitName);

            if (saved >= 0)
            {
                _cmbKit.SelectedIndex = saved;
            }
        }

        _cmbKit.SelectedIndexChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            Result.KitName = _cmbKit.SelectedIndex <= 0 ? string.Empty : (string)_cmbKit.SelectedItem!;

            if (RcpKit.ByName(Result.KitName) is { } kit)
            {
                SetTileSize(kit.TileSizeMm);
            }

            RefreshReadouts();
        };

        // Last in the column, so what it says can grow without moving anything.
        _lblKitCheck.AutoSize = true;
        _lblKitCheck.Font = AppTheme.SmallFont;

        // The drop-down as wide as the card, as it always was as wide as the
        // panel - never narrower than its longest kit (SideWidth).
        var kitCard = new Section("Check against a kit");
        kitCard.Add(_cmbKit);
        Pieces.KeepHeightTold(_cmbKit);
        kitCard.Add(_lblKitCheck, gapBelow: 0);

        var cards = new[] { nameCard, tileCard, traceCard, lapCard, kitCard };
        var column = CardColumn();

        for (var i = 0; i < cards.Length; i++)
        {
            column.Add(cards[i], gapBelow: i < cards.Length - 1 ? UiLayout.RowGap : 0);
        }

        return (column, () => SideWidth(traceButtons, cards));
    }

    /// <summary>
    /// The side panel's width: as wide as it always was, at the text size in
    /// use - or, when one needs more, as the photo's three buttons on one
    /// line, the kit drop-down's longest choice or a card's caption, in its
    /// card, beside the scrollbar the column takes its width from when it
    /// scrolls. Asked whenever the column is placed, so it follows the faces
    /// the theme pass gives them.
    /// </summary>
    private int SideWidth(ButtonBar traceButtons, IReadOnlyList<Section> cards)
    {
        var widest = Math.Max(
            Math.Max(traceButtons.OneLineWidth, Pieces.PickerWidth(_cmbKit, ref _kitWidth)),
            cards.Max(card => card.CaptionWidth));

        return Math.Max(
            UiLayout.AtTextSize(SidePanelWidth),
            widest + Section.Inset.Horizontal + SystemInformation.VerticalScrollBarWidth);
    }

    /// <summary>
    /// A column of cards down one side of the board: placed by the Beside it
    /// is in rather than docked, with no margins of its own - the page's are
    /// round it - and no room under its last card. It scrolls by itself when
    /// the window is too short for all of it, as the palette and the side
    /// panel always did.
    /// </summary>
    private static PageStack CardColumn()
    {
        var column = new PageStack { Dock = DockStyle.None, Padding = Padding.Empty };
        ScrollTheming.NoRoomBelow(column);

        return column;
    }

    /// <summary>
    /// The foot: the main-picture box, Save as PNG..., Save Layout and Cancel
    /// against the right-hand edge, Cancel furthest right, as they were. The
    /// bar holds them right to left, in the order they are added, so Tab's
    /// order is set back to left to right, as it was.
    /// </summary>
    private ButtonBar BuildFooter()
    {
        var btnSave = new Button { Text = "Save Layout" };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var btnPng = _btnPng;

        btnSave.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(TrackName))
            {
                AppDialog.Show(this,
                    "Give the track a name first - that's what it's saved and listed under.",
                    "Name needed", MessageBoxButtons.OK, MessageBoxIcon.Information);

                _txtName.Focus();
                return;
            }

            // The plan's own lap setting is what is saved. This used to copy
            // the checkbox into it here, and the checkbox could be behind the
            // plan: Trace route closes the lap in the plan, and Undo or Redo
            // bring back whatever the lap was then, so unticking the box and
            // pressing Trace route drew a closed lap and saved an open one.
            // The box now follows the plan (SyncSidePanel), and ticking it
            // writes straight into the plan, so there is nothing to copy.
            DialogResult = DialogResult.OK;
            Close();
        };

        btnPng.Click += (_, _) => ExportPng();

        _chkUseAsPhoto.Text = "Show this plan as the track's main picture";
        _chkUseAsPhoto.AutoSize = true;
        _chkUseAsPhoto.Checked = true;

        // Level with the middle of the buttons beside it (see FieldGrid.Inline).
        _chkUseAsPhoto.Anchor = AnchorStyles.Left;

        var footer = new ButtonBar(alignRight: true).With(btnCancel, btnSave, btnPng, _chkUseAsPhoto);

        _chkUseAsPhoto.TabIndex = 0;
        btnPng.TabIndex = 1;
        btnSave.TabIndex = 2;
        btnCancel.TabIndex = 3;

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        return footer;
    }

    // --- Behaviour ------------------------------------------------------------

    /// <summary>Frames the plan in the board at the size the board has now - see the constructor.</summary>
    private void FrameUntilShown(object? sender, EventArgs e) => _canvas.Fit();

    private void SetTileSize(double mm)
    {
        Result.TileSizeMm = mm;

        if ((double)_numTileSize.Value != mm)
        {
            _numTileSize.Value = (decimal)Math.Clamp(mm, 100, 2000);
        }

        HighlightTileSize();
        RefreshReadouts();
    }

    /// <summary>Lights the size button that matches what is actually being drawn.</summary>
    private void HighlightTileSize()
    {
        void Mark(Button? button, bool active)
        {
            if (button is null)
            {
                return;
            }

            button.BackColor = active ? AppTheme.Primary : AppTheme.SecondaryButton;
            button.ForeColor = active ? AppTheme.OnPrimary : AppTheme.TextDark;
            button.FlatAppearance.BorderSize = 0;
        }

        Mark(_btn30, Math.Abs(Result.TileSizeMm - 300) < 0.5);
        Mark(_btn50, Math.Abs(Result.TileSizeMm - 500) < 0.5);
    }

    private void ChooseBackdrop()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a photo of the track to trace",
            Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Sized up from its length and its header before the canvas decodes
        // it (see ImageProbe), and turned away here, before the plan is
        // touched. It used to go straight in, so a huge photo, or a small
        // file whose header claims tens of thousands of pixels each way,
        // froze the designer while GDI+ built the whole bitmap, or crashed
        // it out of memory.
        string? problem;

        try
        {
            // Not from another computer. The plan keeps the photo's path and
            // opens it again every time the designer opens; a path to a
            // network share makes Windows sign in to that computer with this
            // account's credentials, and a backup could plant one. So only a
            // photo on this PC is traced (see RcpTrackCanvas.ReloadBackdrop).
            problem = PlainFileName.IsNetworkPath(dialog.FileName)
                ? "That photo is on another computer. Copy it to this PC first, then choose the copy."
                : ImageProbe.PickedProblem(dialog.FileName, "trace over", "Choose a smaller copy.", out _);
        }
        catch (Exception ex)
        {
            problem = $"Couldn't open that photo: {ex.Message}";
        }

        if (problem is not null)
        {
            AppDialog.Show(this, problem, "Photo not loaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // A photo that passed the size check and still wouldn't open - a
        // damaged file. Said in a box: the status line's reason used to be
        // written over straight away by the Photo tool being chosen, which
        // then sat there with no photo to move. The plan keeps the photo it
        // had, so there is nothing to put back.
        if (_canvas.LoadBackdrop(dialog.FileName) is { } why)
        {
            AppDialog.Show(this, why, "Photo not loaded", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetTool(RcpTool.Backdrop);
        _canvas.Fit();
    }

    private void ClearAll()
    {
        // Markings and the line count as something to clear. IsEmpty is
        // about whether there is a plan to show (floor and rails), so a
        // plan holding only a line or markings is "empty" by it - and the
        // Clear button used to do nothing at all for one.
        if (Result.IsEmpty && Result.Decals.Count == 0 && Result.Route.Count == 0)
        {
            return;
        }

        var confirm = AppDialog.Show(this,
            "Clear the whole plan? This can be undone with Ctrl+Z.",
            "Clear layout", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm == DialogResult.Yes)
        {
            _canvas.ClearAll();
        }
    }

    private void ExportPng()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save the track plan",
            Filter = "PNG image (*.png)|*.png",
            FileName = $"{Sanitise(string.IsNullOrEmpty(TrackName) ? "track" : TrackName)} layout.png",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            using var bitmap = RcpTrackPainter.Render(Result, new Size(2200, 1600), Color.White);
            bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);

            _lblStatus.Text = $"Saved to {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't save that: {ex.Message}", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string Sanitise(string name) =>
        string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Whenever the canvas says the plan has changed: the side panel is put
    /// back in line with the plan, then the readouts are worked out again.
    /// </summary>
    private void OnDesignChanged()
    {
        SyncSidePanel();
        RefreshReadouts();
    }

    /// <summary>
    /// Shows on the side panel what the plan being drawn actually holds for
    /// the tile size, the photo's opacity, the lap and the kit. The canvas
    /// changes those itself: Undo and Redo swap in a whole copy of the plan
    /// taken earlier, with the size, kit, opacity and lap it had then, and
    /// Trace route closes the lap. The panel used to go on showing what was
    /// last picked on it - after laying a tile, choosing 30 cm and pressing
    /// Ctrl+Z, the box said 300 while the plan, its readouts and the saved
    /// file went by 500. Done with _loading set, so the controls' own
    /// handlers leave the plan alone while they are moved, and each control
    /// is only touched when it is wrong, so nothing flickers on an ordinary
    /// stroke. Runs on the UI thread, as the canvas raises DesignChanged
    /// from its own mouse, key and button handling.
    /// </summary>
    private void SyncSidePanel()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;

        try
        {
            var tileSize = (decimal)Math.Clamp(Result.TileSizeMm, 100, 2000);

            if (_numTileSize.Value != tileSize)
            {
                _numTileSize.Value = tileSize;
            }

            var opacity = Math.Clamp(Result.BackdropOpacity, _barBackdrop.Minimum, _barBackdrop.Maximum);

            if (_barBackdrop.Value != opacity)
            {
                _barBackdrop.Value = opacity;
            }

            if (_chkClosedRoute.Checked != Result.RouteClosed)
            {
                _chkClosedRoute.Checked = Result.RouteClosed;
            }

            // A kit the list doesn't know (no kit, or one saved by another
            // version) shows as "Don't check", as it does when the window
            // opens; the plan keeps the name it has either way.
            var kitIndex = string.IsNullOrWhiteSpace(Result.KitName) ? 0 : _cmbKit.Items.IndexOf(Result.KitName);

            if (kitIndex < 0)
            {
                kitIndex = 0;
            }

            if (_cmbKit.SelectedIndex != kitIndex)
            {
                _cmbKit.SelectedIndex = kitIndex;
            }
        }
        finally
        {
            _loading = false;
        }

        HighlightTileSize();
    }

    private void RefreshReadouts()
    {
        if (_loading)
        {
            return;
        }

        var inventory = RcpPlan.Count(Result);
        var lengthTiles = RcpPlan.RouteLengthInTiles(Result);

        _lblStats.Text = Readout(inventory, lengthTiles, Result.TileSizeMm, _canvas.LapSeconds);

        RefreshKitCheck(inventory);
    }

    /// <summary>
    /// What the lap's readout says of a plan with <paramref name="inventory"/>
    /// in it and a racing line <paramref name="lengthTiles"/> tiles long, at
    /// <paramref name="tileSizeMm"/> a tile and <paramref name="lapSeconds"/>
    /// a lap - for the plan being drawn, and for the most it can say, which
    /// the readout is made as tall as (see BuildSidePanel).
    /// </summary>
    private static string Readout(RcpInventory inventory, float lengthTiles, double tileSizeMm, double lapSeconds)
    {
        var metres = lengthTiles * tileSizeMm / 1000.0;
        var feet = metres * 3.28084;

        var perTile = tileSizeMm / 1000.0;
        var area = inventory.Tiles * perTile * perTile;

        var lines = new List<string>
        {
            $"Floor: {inventory.Tiles} tiles"
            + (inventory.Infield > 0 ? $"  (+{inventory.Infield} blocked out)" : string.Empty),

            $"Area: {area:0.0} m²   ({area * 10.7639:0} sq ft)",
            string.Empty,
            $"Straight rails: {inventory.StraightRails}"
            + (inventory.Dividers > 0 ? $"   ({inventory.Dividers} as dividers)" : string.Empty),

            $"Turn rails: {inventory.TurnRails}",
            $"End caps: {inventory.EndCaps}",
            string.Empty,
            lengthTiles > 0
                ? $"Lap: {metres:0.00} m  /  {feet:0.0} ft"
                : "Lap: draw the racing line, or press Trace route",
        };

        if (lengthTiles > 0 && lapSeconds > 0)
        {
            var speed = metres / lapSeconds;
            lines.Add($"At a {lapSeconds:0.0}s lap that's {speed:0.0} m/s ({speed * 3.6:0.0} km/h).");
        }

        if (inventory.Tiles > 0 && inventory.StraightRails == 0)
        {
            lines.Add(string.Empty);
            lines.Add("No rails yet - try Wall the edge, then add the dividers.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void RefreshKitCheck(RcpInventory inventory)
    {
        var kit = RcpKit.ByName(Result.KitName);

        if (kit is null)
        {
            _lblKitCheck.Text = "Pick a kit and this will tell you whether your layout fits in the box.";
            _lblKitCheck.ForeColor = AppTheme.TextMuted;
            return;
        }

        var shortfalls = inventory.Shortfalls(kit);

        if (shortfalls.Count == 0)
        {
            _lblKitCheck.Text = $"Fits a {kit.Name} kit ({inventory.Tiles} of {kit.TotalTiles} tiles, "
                                + $"{inventory.StraightRails} of {kit.TotalStraightRails} straight rails). Box makes {kit.Footprint}.";
            _lblKitCheck.ForeColor = AppTheme.Success;
            return;
        }

        _lblKitCheck.Text = $"Short for a {kit.Name}:" + Environment.NewLine
                            + string.Join(Environment.NewLine, shortfalls.Select(s => "  • " + s));
        _lblKitCheck.ForeColor = AppTheme.Danger;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Typing in the name box keeps the box's own Ctrl+Z. It used to
        // take back the last stroke on the plan instead of the last
        // keystroke - out of sight, behind the box being typed in.
        if (_txtName.ContainsFocus)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        if (keyData == (Keys.Control | Keys.Z))
        {
            _canvas.Undo();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Y))
        {
            _canvas.Redo();
            return true;
        }

        // Tool keys only while the board has focus, so they can never eat a
        // keystroke meant for the name box.
        if (!_canvas.Focused)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        var tool = keyData switch
        {
            Keys.D1 => RcpTool.Floor,
            Keys.D2 => RcpTool.Wall,
            Keys.D3 => RcpTool.Curve,
            Keys.D4 => RcpTool.Marker,
            Keys.D5 => RcpTool.Route,
            Keys.D6 => RcpTool.Erase,
            Keys.D7 => RcpTool.Backdrop,
            _ => (RcpTool?)null,
        };

        if (tool is { } chosen)
        {
            SetTool(chosen);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _canvas.StopPlaying();
        base.OnFormClosed(e);
    }

    /// <summary>
    /// One palette entry, drawn with the same painter the canvas uses - so
    /// what you pick up always looks like what gets laid down.
    ///
    /// Its sample, name and hint are placed at the text size in use: at the
    /// normal one exactly where they always were, and further apart as the
    /// entry grows with the text, so the name and the hint have the room
    /// their faces need rather than the 20px and 24px they had at 100%.
    /// </summary>
    private sealed class PaletteButton : Control
    {
        private readonly RcpTrackDesign _sample;
        private bool _hovered;

        public RcpPaletteItem Item { get; }
        public bool Selected { get; set; }

        public event Action<RcpPaletteItem>? Chosen;

        public PaletteButton(RcpPaletteItem item)
        {
            Item = item;
            _sample = RcpTiles.Sample(item);

            SetStyle(ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);

            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hovered = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Chosen?.Invoke(Item);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            using (var background = new SolidBrush(AppTheme.Surface))
            {
                g.FillRectangle(background, ClientRectangle);
            }

            var bounds = new RectangleF(0, 0, Width - 1, Height - 1);

            AppTheme.DrawCard(g, bounds, Selected ? AppTheme.Primary : null);

            if (_hovered && !Selected)
            {
                using var wash = new SolidBrush(Color.FromArgb(28, AppTheme.Primary));
                using var path = AppTheme.RoundedRect(bounds, AppTheme.CornerRadius);
                g.FillPath(wash, path);
            }

            // The room round the sample, and either side of the words, at the
            // text size in use - see the class notes. The sample is the size
            // it always was, at the top: the entry is taller than that now,
            // for the words (EntryHeight).
            var inset = UiLayout.AtTextSize(7);
            var edge = UiLayout.AtTextSize(10);

            var size = Math.Min(Height, UiLayout.AtTextSize(PaletteEntryHeight)) - 2 * inset;
            var origin = new PointF(edge, inset);

            var state = g.Save();
            g.SetClip(new RectangleF(origin.X - 3, origin.Y - 3, size + 6, size + 6));

            RcpTrackPainter.Draw(g, _sample, size, origin, showRoute: false, showLabels: false, detailThreshold: 20f);

            g.Restore(state);

            using var nameBrush = new SolidBrush(AppTheme.TextDark);
            using var hintBrush = new SolidBrush(AppTheme.TextMuted);
            using var format = new StringFormat
            {
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter,
                LineAlignment = StringAlignment.Center,
            };

            var textLeft = origin.X + size + UiLayout.AtTextSize(12);
            var textWidth = Width - textLeft - edge;

            g.DrawString(RcpTiles.NameOf(Item), AppTheme.BoldFont, nameBrush,
                new RectangleF(textLeft, inset, textWidth, UiLayout.AtTextSize(20)), format);

            // Disposed like the one above: made fresh on every repaint (each
            // hover in and out), it was left for the finaliser every time.
            //
            // Whole lines only, ending in "..." when the hint runs past its
            // two: a box 24px tall at the normal text size cut its second
            // line in half, at every size (layout pictures, 30 Sep).
            using var hintFormat = new StringFormat
            {
                Trimming = StringTrimming.EllipsisWord,
                FormatFlags = StringFormatFlags.LineLimit,
            };

            g.DrawString(RcpTiles.HintOf(Item), AppTheme.SmallFont, hintBrush,
                new RectangleF(textLeft, HintTop(), textWidth, HintLines * HintLineHeight(g)),
                hintFormat);
        }

        /// <summary>How many lines of its hint an entry shows.</summary>
        private const int HintLines = 2;

        /// <summary>Where the hint starts, under the name - where it always did, at the text size in use.</summary>
        private static int HintTop() => UiLayout.AtTextSize(25);

        /// <summary>A line of the hint as DrawString sets it, a pixel over for rounding.</summary>
        private static int HintLineHeight(Graphics g) => (int)Math.Ceiling(AppTheme.SmallFont.GetHeight(g)) + 1;

        /// <summary>
        /// An entry's height: its name, two whole lines of its hint and the
        /// room under them - never less than the 54px it had at the normal
        /// text size, at the size in use.
        /// </summary>
        public static int EntryHeight()
        {
            using var screen = Graphics.FromHwnd(IntPtr.Zero);

            var wanted = HintTop() + HintLines * HintLineHeight(screen) + UiLayout.AtTextSize(7);

            return Math.Max(UiLayout.AtTextSize(PaletteEntryHeight), wanted);
        }
    }
}
