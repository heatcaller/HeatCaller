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
/// On the layout pieces (DialogFit), as rethought on 8 Nov 2026 (it had
/// become a pile of buttons - five rows of them over six tabs): a slim top
/// bar - the plan's name, Undo, Redo, Fit, Play, 3D, the File menu (with
/// Clear in it, in red) and "?" for the keys, and Save Layout and Cancel
/// against the right-hand edge. Under it, the tools in a rail down the left
/// (a Beside with its column on the left), the one in hand lit; beside them
/// the bar of that tool's options only (ContextStrip), the board, and the
/// status line under it; and down the right the inspector (a Beside): one
/// column of sections - Plan, Room, Lap, Parts, Auto - one open at a time,
/// each closed one's header saying its headline, which scrolls by itself
/// when the window is too short for it. P hides the inspector and gives the
/// board the whole width.
///
/// The new look (25 Oct 2026, Track Designer overhaul Phase 2): the palette
/// used to be a column of its own down the left, and every card a column
/// down the right, so the board - the thing the window is for - had what
/// the two left. The pieces are a tab of the one panel now; the board is on
/// a dark hall floor whatever the app's look, with the floor's size and the
/// lap on it (RcpTrackCanvas.Chips); Space and drag moves the view; and
/// Save as PNG went into an Export menu, which the later phases fill.
///
/// It opens at the size it always did, at the text size in use - never
/// bigger than the screen - and can be made bigger, or as narrow as it
/// always could; the board takes what it is given. It used to be
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
    /// needs, when that is more (SideWidth).
    /// </summary>
    private const int PaletteEntryWidth = 192;
    private const int PaletteEntryHeight = 54;

    /// <summary>The side panel's width at the normal text size, as it always was - or as wide as its widest line needs, when that is more (SideWidth).</summary>
    private const int SidePanelWidth = 314;

    /// <summary>The tile size's number box, and the photo's opacity slider's height, at the normal text size, as they were.</summary>
    private const int TileSizeBoxWidth = 90;
    private const int SliderHeight = 32;

    /// <summary>
    /// The window's layout held while it is built, and let go at the end of
    /// its constructor (18 Nov 2026) - see LayoutFreeze.HoldWhileBuilding.
    /// The first field, so the pieces the fields below make are made inside
    /// it. Built live, most of its parts had their windows before the theme
    /// pass, which made many of them again.
    /// </summary>
    private readonly IDisposable _building = LayoutFreeze.HoldWhileBuilding();

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

    // --- The room it has to go in (8 Oct 2026) ---
    private readonly NumericUpDown _numRoomWidth = new();
    private readonly NumericUpDown _numRoomDepth = new();
    private readonly Button _btnRoomFeet = new();
    private readonly Button _btnRoomMetres = new();
    private readonly Label _lblRoomFit = new();
    private readonly Label _lblRoomUnits = new();

    // --- Start from one of RCP's own layouts (8 Oct 2026) ---
    private readonly ComboBox _cmbStock = new();

    /// <summary>Held so they can be greyed when there is nothing to undo - see BuildToolbar.</summary>
    private Button? _btnUndo;
    private Button? _btnRedo;

    /// <summary>
    /// The buttons that act on what is picked - greyed when nothing is.
    /// A live Turn button with nothing selected is a button that does
    /// nothing, which is how an editor teaches somebody the wrong thing
    /// about itself.
    /// </summary>
    private Button[] _selectionButtons = Array.Empty<Button>();
    private readonly Label _lblStock = new();

    /// <summary>Set while the picker is being put back to its prompt, so that does not read as a choice.</summary>
    private bool _resettingStock;
    private readonly TextBox _txtName = new();
    private readonly CheckBox _chkClosedRoute = new ThemedCheckBox();
    private readonly CheckBox _chkUseAsPhoto = new ThemedCheckBox();

    /// <summary>The File menu's button, and the menu it opens - see BuildTopBar.</summary>
    private readonly Button _btnExport = new() { Text = "File ▾" };
    private readonly ContextMenuStrip _exportMenu = new();

    private readonly Button _btnView = new() { Text = "3D" };

    /// <summary>The "?" in the top bar, which opens the Keys section - lit while it shows.</summary>
    private readonly Button _btnKeys = new() { Text = "?" };

    /// <summary>The plan's name along the top bar, following the name box.</summary>
    private readonly Label _lblTitle = new();

    /// <summary>
    /// The inspector's sections and what each holds, in order - see
    /// BuildInspector: a header button that opens and closes it, saying the
    /// section's headline while it is closed, and the cards under it.
    /// </summary>
    private (string Name, SectionHeader Header, Section[] Cards)[] _tabs = Array.Empty<(string, SectionHeader, Section[])>();

    /// <summary>The panel's parts - the inspector's column - hidden together by TogglePanel.</summary>
    private Control[] _panelParts = Array.Empty<Control>();

    /// <summary>The bar over the board that holds the tool in hand's options - see BuildContextBar.</summary>
    private ContextStrip? _contextBar;

    /// <summary>
    /// Whether the panel is showing. Kept here rather than read off its
    /// parts: before the window is on screen every part says it is hidden.
    /// </summary>
    private bool _panelShown = true;

    /// <summary>The panel's holder of the tabs' columns, which places the one showing.</summary>
    private Beside? _board;

    /// <summary>
    /// The tab last looked at, kept while HeatCaller runs - so opening the
    /// designer for the next track goes back to it rather than to Pieces.
    /// </summary>
    private static string _lastTab = TabPlan;

    private const string TabPlan = "Plan";
    private const string TabRoom = "Room";
    private const string TabLap = "Lap";
    private const string TabKeys = "Keys";
    private const string TabStock = "Parts";
    private const string TabAuto = "Auto";

    // The Stock tab (Track Designer overhaul, Phase 4): the plan against
    // what the club owns, part by part.
    private RcpStock _stock = new();
    private readonly Func<RcpStock, bool>? _saveStock;
    private readonly Label _lblOwned = new();
    private readonly FieldGrid _stockGrid = new();
    private readonly List<(Label Caption, Label Value)> _stockRows = new();

    /// <summary>The tab the window was asked to open on, if any - see the constructor.</summary>
    private readonly string? _openOnTab;

    /// <summary>Whether the lap time the board was given is the track's own (Track Layouts' average lap) rather than the 12s stand-in - see RefreshChips.</summary>

    // The lap on the board (Track Designer overhaul, Phase 6): the laps
    // raced on this layout, the club's tuning of the estimate, the racing
    // line the window opened with (a line changed since takes the
    // estimate), and the lap last chosen (RcpLapModel.Choose).
    private readonly (double Seconds, int Laps)? _raced;
    private readonly double? _entered;
    private readonly double _tuning;
    private readonly int _tunedLayouts;
    private readonly List<RcpRoutePoint> _openedLine;
    private RcpLapTime _lap = RcpLapTime.Nothing;
    private readonly ContextMenuStrip _sectorMenu = new();
    private (Button Button, float Rate)[] _paceButtons = Array.Empty<(Button, float)>();
    private readonly Button _btnPlay = new();
    private readonly SliderControl _barBackdrop = new();

    private Button? _btn30;
    private Button? _btn50;
    private (Button Button, RcpTool Tool)[] _toolButtons = Array.Empty<(Button, RcpTool)>();
    private (Button Button, float Radius)[] _radiusButtons = Array.Empty<(Button, float)>();

    // The Lanes card (Track Designer overhaul, Phase 3): lane or ring, how
    // wide, and the turns the lanes put round their bends.
    private Button _btnLanePaint = new();
    private Button _btnLaneRing = new();
    private (Button Button, int Width)[] _laneWidthButtons = Array.Empty<(Button, int)>();
    private (Button Button, int Radius)[] _laneTurnButtons = Array.Empty<(Button, int)>();

    /// <summary>What the kit drop-down needs across for its longest choice, kept until its face or its list changes - see SideWidth.</summary>
    private (Font Font, int Count, int Width)? _kitWidth;
    private (Font Font, int Count, int Width)? _stockWidth;

    private readonly string _trackName;

    // WHY (20 Oct 2026): what the plan and its name were when the window
    // opened, so closing it without saving can ask first - see OnFormClosing.
    private readonly string _openedDesign;
    private readonly string _openedName;
    private readonly bool _openedUseAsPhoto;

    // WHY (20 Oct 2026): true when the window opens on drawing that was never
    // saved - Track Layouts opens it again that way after "Name already
    // used" - so closing it without saving asks even if nothing changed since.
    private readonly bool _unsavedOnOpen;

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

    /// <param name="openOnTab">
    /// The panel's tab to open on, without remembering it for the next window
    /// - for the layout tests, which have to see every tab. Null opens on the
    /// tab last used (see _lastTab).
    /// </param>
    public RcpTrackEditorForm(RcpTrackDesign? existing, string trackName, double? averageLapSeconds, bool unsavedOnOpen = false, bool useAsPhoto = true, string? openOnTab = null,
        RcpStock? stock = null, Func<RcpStock, bool>? saveStock = null,
        (double Seconds, int Laps)? raced = null, double tuning = 1.0, int tunedLayouts = 0,
        IReadOnlyList<RcpRoutePoint>? racedLine = null)
    {
        // Let go however the constructor ends - see _building.
        using var building = _building;

        _raced = raced;
        _entered = averageLapSeconds is > 0 ? averageLapSeconds : null;
        _tuning = tuning > 0 ? tuning : 1.0;
        _tunedLayouts = tunedLayouts;
        // The line the raced and entered times are for: the saved one when the
        // caller says (a reopen after a taken name opens on a drawing already
        // changed), otherwise the line the window opens with.
        _openedLine = (racedLine ?? existing?.Route ?? new List<RcpRoutePoint>())
            .Select(p => new RcpRoutePoint { X = p.X, Y = p.Y })
            .ToList();

        _openOnTab = openOnTab;

        // What the club owns: from its settings, and saved back there when
        // edited (saveStock), since it is the same cupboard for every plan.
        _stock = (stock ?? new RcpStock()).Tidied();
        _saveStock = saveStock;

        _unsavedOnOpen = unsavedOnOpen;

        _trackName = string.IsNullOrWhiteSpace(trackName) ? string.Empty : trackName;

        Text = string.IsNullOrEmpty(_trackName) ? "Track Designer" : $"Track Designer - {_trackName}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        KeyPreview = true;

        _canvas.Design = existing?.Clone() ?? new RcpTrackDesign();
        _canvas.LapSeconds = averageLapSeconds is > 0 ? averageLapSeconds.Value : 12;
        _canvas.DesignChanged += OnDesignChanged;
        _canvas.StatusChanged += message => _lblStatus.Text = message;
        _canvas.SelectionChanged += HighlightSelectionButtons;
        _canvas.LapChipClicked += ShowSectors;
        _canvas.LabelRequested += AskTileLabel;
        _canvas.PlayingChanged += () => _btnPlay.Text = _canvas.IsPlaying ? "Stop" : "Play";

        var page = BuildLayout();

        AppTheme.StyleToolTip(_tips);
        AppTheme.ApplyRecursively(this);

        // One button in the app's colour at the foot: Save Layout, which is
        // what the window is for (layout pictures, 30 Sep). Export and the
        // panel's switch are tools, not the window's answer.
        AppTheme.StyleButton(_btnExport, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleContextMenu(_exportMenu);

        // Lit after the theme pass, which gives every button its own
        // colours - as the tools' and the tile size's are below.
        ShowTab(_openOnTab ?? _lastTab, remember: _openOnTab is null);
        HighlightViewButton();

        // Nothing else owns the menu, so it goes with the window.
        Disposed += (_, _) => _exportMenu.Dispose();

        AppTheme.StyleContextMenu(_sectorMenu);
        _sectorMenu.Closed += (_, _) => _canvas.Focus();
        Disposed += (_, _) => _sectorMenu.Dispose();

        // WHY (23 Oct 2026, bug hunt 10): the "main picture" box was always
        // ticked, whatever the track was set to, so saving a plan for a track
        // whose club had chosen its photo as the main picture quietly switched
        // it to the drawn plan. The box now opens on what the caller passes
        // (ticked when it says nothing, as before), and before the "as opened"
        // snapshot below, so the box starting that way is not a change.
        _chkUseAsPhoto.Checked = useAsPhoto;

        _loading = false;

        // WHY (20 Oct 2026): taken once the side panel has been filled in, so
        // what it set while loading is part of the "as opened" picture.
        _openedDesign = DesignSnapshot(_canvas.Design);
        _openedName = TrackName;
        _openedUseAsPhoto = _chkUseAsPhoto.Checked;

        HighlightTileSize();
        HighlightRadius();
        HighlightLanes();
        HighlightPace();
        RefreshReadouts();
        SetTool(RcpTool.Floor);

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
        var topBar = BuildTopBar();
        var rail = BuildToolRail();
        var (pieceEntries, markingEntries) = BuildPalette();
        var (plan, room, lap, auto, photoParts) = BuildSidePanel();
        var context = BuildContextBar(pieceEntries, markingEntries, photoParts);
        var stock = new[] { BuildStockCard() };
        var keys = new[] { BuildKeysCard() };

        // Under the board and as wide as it, and as tall as the longest a
        // tool says there - so picking a tool never resizes the board.
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Wordings = Enum.GetValues<RcpTool>().Select(tool => ToolHint(tool)).ToArray();

        var inspector = BuildInspector(new (string, Section[])[]
        {
            (TabPlan, plan),
            (TabRoom, room),
            (TabLap, lap),
            (TabStock, stock),
            (TabAuto, auto),
            (TabKeys, keys),
        });

        var everyCard = plan.Concat(room).Concat(lap).Concat(stock).Concat(auto).Concat(keys).ToList();

        // The tool's options over the board, the board, and the line under
        // it, with the inspector down their right - one column of sections
        // that scrolls by itself when the window is too short for it.
        var board = new Beside(() => SideWidth(everyCard));
        board.Add(context, gapBelow: UiLayout.RowGap);
        board.AddFill(_canvas, _ => UiLayout.AtTextSize(CanvasLeastHeight), gapBelow: UiLayout.RowGap);
        board.Add(_lblStatus, gapBelow: 0);
        board.AddSideFill(inspector, _ => UiLayout.AtTextSize(ColumnLeastHeight), gapBelow: 0);

        _board = board;
        _panelParts = new Control[] { inspector };

        // The tools down the left, one under another, as wide as the
        // widest caption needs - and the rest beside them.
        var railed = new Beside { SideOnLeft = true };

        for (var i = 0; i < rail.Length; i++)
        {
            railed.AddSide(rail[i], gapBelow: i < rail.Length - 1 ? UiLayout.TightButtonGap : 0);
        }

        railed.AddFill(board, board.LeastHeight, gapBelow: 0);

        var page = new FillPage();
        page.Add(topBar, gapBelow: UiLayout.RowGap);
        page.AddFill(railed, railed.LeastHeight, gapBelow: 0);

        // The board has the focus as the window opens (Shown) and keeps Tab
        // for itself (it takes every key, see RcpTrackCanvas.IsInputKey);
        // the top bar comes after it in Tab's order, as the toolbar did.
        if (railed.Parent is { } boardHolder)
        {
            boardHolder.TabIndex = 0;
        }

        topBar.TabIndex = 1;

        Controls.Add(page);

        ShowTab(_openOnTab ?? _lastTab, remember: _openOnTab is null);

        return page;
    }

    /// <summary>
    /// The inspector (8 Nov 2026, in place of six tabs): one column - the
    /// sections' headers, one under another, then the open section's cards.
    /// The headers stay at the top whichever is open, so every section can
    /// be seen and reached without scrolling; a closed one's header says its
    /// headline - the plan's name, whether it fits the room, the lap, the
    /// parts - so most questions are answered without opening anything. One
    /// is open at a time, and its header again closes it. Keys has no
    /// header until the "?" opens it.
    /// </summary>
    private PageStack BuildInspector(IReadOnlyList<(string Name, Section[] Cards)> sections)
    {
        var column = CardColumn();
        var made = new List<(string, SectionHeader, Section[])>();

        foreach (var (name, cards) in sections)
        {
            var header = new SectionHeader(name);
            var captured = name;

            header.Click += (_, _) =>
            {
                ShowTab(_openTab == captured ? string.Empty : captured);
                _canvas.Focus();
            };

            _tips.SetToolTip(header, TabTip(name));

            column.Add(header, gapBelow: UiLayout.TightButtonGap);
            made.Add((name, header, cards));
        }

        foreach (var (_, cards) in sections)
        {
            foreach (var card in cards)
            {
                column.Add(card, gapBelow: UiLayout.RowGap);
            }
        }

        _tabs = made.ToArray();

        return column;
    }

    private static string TabTip(string tab) => tab switch
    {
        TabPlan => "The track's name, starting from one of RCP's layouts, the tile size and the kit it is checked against.",
        TabRoom => "The room the track goes in.",
        TabLap => "The lap, the split loops' sectors, and what the plan comes to.",
        TabStock => "The plan against what the club owns: each part used, owned and left.",
        TabAuto => "Rail the edge of the floor, or have layouts made to fit the room.",
        _ => "Every key the designer answers to.",
    };

    /// <summary>The inspector's section open now - empty with all of them closed.</summary>
    private string _openTab = string.Empty;

    /// <summary>
    /// Opens one of the inspector's sections and closes the rest; empty
    /// closes them all, and an unknown name opens the first. Remembered for
    /// the next window unless <paramref name="remember"/> says not - never
    /// Keys, which is looked up, not worked in.
    /// </summary>
    private void ShowTab(string name, bool remember = true)
    {
        if (_tabs.Length == 0)
        {
            return;
        }

        // The names the tabs had, which a window may still be asked to open
        // on: Stock is Parts now, and the pieces went to the bar over the board.
        name = name switch
        {
            "Stock" => TabStock,
            "Pieces" => TabPlan,
            _ => name,
        };

        if (name.Length > 0 && !_tabs.Any(tab => tab.Name == name))
        {
            name = _tabs[0].Name;
        }

        if (remember && name != TabKeys)
        {
            _lastTab = name;
        }

        _openTab = name;

        foreach (var (tabName, header, cards) in _tabs)
        {
            var open = tabName == name;

            foreach (var card in cards)
            {
                card.Visible = open;
            }

            header.Visible = tabName != TabKeys || open;
            header.BackColor = open ? AppTheme.Primary : AppTheme.SecondaryButton;
            header.ForeColor = open ? AppTheme.OnPrimary : AppTheme.TextDark;
            header.Invalidate();
        }

        // A card's gap under the last header showing, a button's gap
        // between the headers.
        var lastHeader = _tabs.Last(tab => tab.Name != TabKeys || tab.Name == name).Header;

        foreach (var (_, header, _) in _tabs)
        {
            var gap = ReferenceEquals(header, lastHeader) ? UiLayout.RowGap : UiLayout.TightButtonGap;

            if (header.Margin.Bottom != gap)
            {
                header.Margin = new Padding(header.Margin.Left, header.Margin.Top, header.Margin.Right, gap);
            }
        }

        _btnKeys.BackColor = name == TabKeys ? AppTheme.Primary : AppTheme.SecondaryButton;
        _btnKeys.ForeColor = name == TabKeys ? AppTheme.OnPrimary : AppTheme.TextDark;
        _btnKeys.FlatAppearance.BorderSize = 0;

        RefreshHeadlines();
    }

    /// <summary>
    /// Each header says its section's name, and - closed - its headline:
    /// the first thing its cards say. Asked after every change to the plan
    /// (RefreshReadouts) and to its name.
    /// </summary>
    private void RefreshHeadlines()
    {
        _lblTitle.Text = string.IsNullOrWhiteSpace(TrackName) ? "A new track" : TrackName;

        foreach (var (name, header, _) in _tabs)
        {
            var open = name == _openTab;
            var headline = open ? string.Empty : Headline(name);

            header.Headline = headline;
        }
    }

    /// <summary>What a closed section's header says after its name.</summary>
    private string Headline(string section) => section switch
    {
        TabPlan => (string.IsNullOrWhiteSpace(TrackName) ? "no name yet" : TrackName)
                   + $", {Result.TileSizeMm / 10:0} cm tiles",
        TabRoom => FirstSentence(_lblRoomFit.Text),
        // The lap as the board's corner says it - "Lap 86.1 ft · 14.3s as
        // entered" - or, with no line yet, the first thing the readout says.
        TabLap => _canvas.Chips.FirstOrDefault(chip => chip.StartsWith("Lap", StringComparison.Ordinal)) is { } chip
            ? chip["Lap".Length..].Trim()
            : FirstLine(_lblStats.Text),
        TabStock => FirstSentence(_lblOwned.Text),
        TabAuto => "rail the edge, build me a track",
        _ => string.Empty,
    };

    private static string FirstLine(string text)
    {
        var end = text.IndexOfAny(new[] { '\r', '\n' });

        return (end < 0 ? text : text[..end]).Trim();
    }

    private static string FirstSentence(string text)
    {
        var line = FirstLine(text);
        var end = line.IndexOf(". ", StringComparison.Ordinal);

        return (end < 0 ? line : line[..end]).TrimEnd('.');
    }

    /// <summary>
    /// Hides the inspector down the board's right, or brings it back.
    /// Hidden, it takes no room at all: the board has the window's whole
    /// width (Beside leaves out a column whose parts are all hidden).
    /// </summary>
    private void TogglePanel()
    {
        _panelShown = !_panelShown;

        foreach (var part in _panelParts)
        {
            part.Visible = _panelShown;
        }

        _board?.PerformLayout();
        _canvas.Focus();
    }

    /// <summary>The 3D button lit while the board shows the angled view.</summary>
    private void HighlightViewButton()
    {
        _btnView.BackColor = _canvas.Angled ? AppTheme.Primary : AppTheme.SecondaryButton;
        _btnView.ForeColor = _canvas.Angled ? AppTheme.OnPrimary : AppTheme.TextDark;
        _btnView.FlatAppearance.BorderSize = 0;
    }

    /// <summary>
    /// A button for the bars round the board. Every one but Undo and Redo
    /// changes the plan, which is done on the flat plan where it can be seen
    /// - so from the angled view they go back to it first. Undo and Redo
    /// show their result either way.
    /// </summary>
    private Button BarButton(string text, string tip, Action click, bool flat = true)
    {
        var button = new Button { Text = text };

        button.Click += (_, _) =>
        {
            if (flat && _canvas.Angled)
            {
                _canvas.ToggleAngled();
            }

            click();
            _canvas.Focus();
        };

        _tips.SetToolTip(button, tip);

        return button;
    }

    /// <summary>
    /// The Stock tab (Track Designer overhaul, Phase 4, 25 Oct 2026): every
    /// part the plan uses against what the club owns - "12 used of 24, 12
    /// left", in red when the plan is short - and the button to say what the
    /// club owns. A row for each part, kept for good and filled in by
    /// RefreshStock, so the card never changes shape as the plan changes.
    /// </summary>
    private Section BuildStockCard()
    {
        var card = new Section("What the club owns");

        _lblOwned.AutoSize = true;
        _lblOwned.Font = AppTheme.SmallFont;
        _lblOwned.ForeColor = AppTheme.TextMuted;

        foreach (var part in StockParts)
        {
            var value = new Label { AutoSize = true, Font = AppTheme.SmallFont };
            var caption = _stockGrid.AddRow(part + ":", value);
            caption.Font = AppTheme.SmallFont;
            _stockRows.Add((caption, value));
        }

        var btnEdit = new Button { Text = "Edit what we own..." };
        btnEdit.Click += (_, _) => EditStock();
        _tips.SetToolTip(btnEdit, "The club's boxed sets and loose parts - counted once, kept for every layout.");

        card.Add(_lblOwned, gapBelow: UiLayout.RowGap);
        card.Add(_stockGrid, gapBelow: UiLayout.RowGap);
        card.Add(new ButtonBar(tight: true).With(btnEdit), gapBelow: UiLayout.HintGap);
        card.Add(new Label
        {
            Text = "The plan can still be drawn past what you own - it's said here, never refused. "
                   + "A spare corner tile still does as floor, so plain floor is checked only as tiles.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        }, gapBelow: 0);

        return card;
    }

    /// <summary>The parts the Stock tab lists, in the order RcpStock.Check gives them.</summary>
    private static readonly string[] StockParts =
    {
        "Tiles, all kinds", "Plain and blank tiles", "Rail tiles", "Finish rail tiles", "Arched rail tiles", "Outer curve tiles", "Inner corner tiles",
        "Loose straight rails", "Loose turn rails", "Corner rail pieces", "End caps",
    };

    /// <summary>Opens What the club owns, and keeps what it comes back with - in the club's settings too.</summary>
    private void EditStock()
    {
        using var form = new RcpStockForm(_stock, Result.TileSizeMm);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            _canvas.Focus();
            return;
        }

        _stock = form.Result;

        var saved = _saveStock is null || _saveStock(_stock);

        RefreshReadouts();

        // On the Stock tab itself, after the refresh, so nothing writes over it.
        if (!saved)
        {
            _lblOwned.Text = "Couldn't write the settings file just now, so what the club owns isn't saved yet. "
                             + "It's used from here on, and saved with the next change to HeatCaller's settings.";
            _lblOwned.ForeColor = AppTheme.Danger;
        }
        _canvas.Focus();
    }

    /// <summary>
    /// The plan against the cupboard (RcpStock.Check): each part's row says
    /// used, owned and left, red when short, and the board's size chip goes
    /// red too. With nothing owned in the plan's tile size it says so and
    /// checks nothing.
    /// </summary>
    private void RefreshStock(RcpInventory inventory)
    {
        var lines = _stock.Check(inventory, Result.TileSizeMm);
        var owned = _stock.OwnedFor(Result.TileSizeMm);
        var sizeName = $"{Result.TileSizeMm / 10:0} cm";

        _canvas.ChipWarning = lines.Any(line => line.Short);

        if (lines.Count == 0)
        {
            _lblOwned.Text = _stock.IsEmpty
                ? "Nothing entered yet. Say what the club owns, and every part of the plan is checked against it."
                : $"Nothing entered in {sizeName} tiles, which this plan uses.";
            _lblOwned.ForeColor = AppTheme.TextMuted;
            _stockGrid.Visible = false;
            return;
        }

        _stockGrid.Visible = true;

        var shortOf = lines.Where(line => line.Short).ToList();

        _lblOwned.Text = (shortOf.Count == 0
                             ? $"The club has enough for this plan, in {sizeName} tiles."
                             : $"The club is short of {Plural.Of(shortOf.Count, "piece")} for this plan, in {sizeName} tiles - in red below.")
                         + (owned.Notes.Count > 0
                             ? $" The {string.Join(" and the ", owned.Notes)} {(owned.Notes.Count == 1 ? "has" : "have")} more straight rails than tiles to bolt them to, so the rest are counted as loose rails."
                             : string.Empty)
                         + (owned.Unlisted.Count > 0
                             ? $" RCP doesn't list the parts in the {string.Join(" or the ", owned.Unlisted.Distinct())}, so only its tiles are counted."
                             : string.Empty)
                         + (inventory.TwoTileTurns > 0
                             ? $" {Plural.Of(inventory.TwoTileTurns, "two-tile turn")} counted as loose turn rails - the club's pieces have only the one-tile turn."
                             : string.Empty);
        _lblOwned.ForeColor = shortOf.Count == 0 ? AppTheme.Success : AppTheme.Danger;

        for (var i = 0; i < _stockRows.Count; i++)
        {
            var (caption, value) = _stockRows[i];
            var line = lines.FirstOrDefault(l => l.Part == StockParts[i]);

            caption.Visible = line is not null;
            value.Visible = line is not null;

            if (line is null)
            {
                continue;
            }

            value.Text = !line.Known
                ? $"{line.Used} used, {line.Owned} counted"
                : line.Short
                    ? $"{line.Used} used of {line.Owned} - {-line.Left} short"
                    : $"{line.Used} used of {line.Owned}, {line.Left} left";
            value.ForeColor = line.Short ? AppTheme.Danger : line.Known ? AppTheme.TextDark : AppTheme.TextMuted;
        }
    }

    /// <summary>
    /// The Keys tab: every key the designer answers to, one to a line - the
    /// tooltips name each tool's, but nothing listed the rest.
    /// </summary>
    private static Section BuildKeysCard()
    {
        var card = new Section("Keys");

        var lines = new[]
        {
            "1, 2, 3 - a plain tile, a rail tile, an outer curve tile",
            "4 to 8 - Markings, Racing line, Erase, Photo, Select",
            "L - Lane, C - Corners, T - Tile label, V - the 3D view",
            "Space and drag - move the view (or drag with the middle button, or Shift and the right button)",
            "Wheel - zoom (with Photo, size the photo)",
            "F - fit the whole plan in view",
            "G - show or hide the grid",
            "R - turn the next piece or marking",
            "Shift+R - run the racing line the other way",
            "Rail the edge and Build me a track are in the Auto section; Clear is in the File menu",
            "Click the zoom in the corner of the board - back to 100%",
            "Right-click, or Ctrl and click - take off what is under the pointer",
            "Ctrl+Z / Ctrl+Y - undo / redo",
            "Ctrl+A - pick up the whole plan",
            "Ctrl+C, Ctrl+X, Ctrl+V - copy, cut, paste",
            "Ctrl+D - a second copy of what is picked",
            "Arrows - nudge what is picked",
            "[ and ] - turn what is picked",
            "Delete or Backspace - take off what is picked",
            "Esc - put down what is picked",
            "P - hide or show this column",
            "? - show or hide these keys",
            "Click the board first: the keys other than Ctrl+Z and Ctrl+Y work while it has the focus, so typing a track name is never taken for a key.",
        };

        for (var i = 0; i < lines.Length; i++)
        {
            card.Add(new Label
            {
                Text = lines[i],
                AutoSize = true,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
            }, gapBelow: i < lines.Length - 1 ? UiLayout.HintGap : 0);
        }

        return card;
    }

    /// <summary>
    /// The top bar: the plan's name, then Undo, Redo and Fit, then Play and
    /// 3D, then the File menu and the "?" for the keys - each group a row of
    /// buttons only as wide as their captions, a group that does not fit on
    /// the line starting the next (a FieldRow) - and Save and Cancel against
    /// the right-hand edge.
    /// </summary>
    private FieldRow BuildTopBar()
    {
        _lblTitle.AutoSize = true;
        _lblTitle.Font = AppTheme.BoldFont;
        _lblTitle.ForeColor = AppTheme.TextDark;
        _lblTitle.Anchor = AnchorStyles.Left;
        _lblTitle.MaximumSize = new Size(UiLayout.AtTextSize(360), 0);
        _lblTitle.Text = string.IsNullOrWhiteSpace(_trackName) ? "A new track" : _trackName;

        var undo = BarButton("Undo", "Ctrl+Z", () => _canvas.Undo(), flat: false);
        var redo = BarButton("Redo", "Ctrl+Y", () => _canvas.Redo(), flat: false);

        // Grey when there is nothing to take back. The canvas has always
        // known (CanUndo / CanRedo) and the buttons never asked, so they
        // looked live on an untouched plan and did nothing when pressed.
        _btnUndo = undo;
        _btnRedo = redo;
        var fit = BarButton("Fit", "Frame the whole plan. (F)", () => _canvas.Fit());

        _btnPlay.Text = "Play";
        _btnPlay.Click += (_, _) =>
        {
            _canvas.TogglePlay();
            _canvas.Focus();
        };

        _tips.SetToolTip(_btnPlay, "Send a car round the line at the lap time on the board.");

        // As wide saying Stop as saying Play, so the buttons after it never
        // move as it changes - it was a fixed 76px for the same reason. Its
        // row sizes it to its caption; this keeps it to the wider of the two,
        // measured the way the row measures (UiLayout.TightButtonSizeFor).
        _btnPlay.MinimumSize = new Size(Math.Max(TightWidth("Play"), TightWidth("Stop")), 0);

        // The angled view (Phase 8): the same plan tipped and turned, to see
        // how it will look. Picking a tool goes back to the flat plan.
        _btnView.Click += (_, _) =>
        {
            _canvas.ToggleAngled();
            _canvas.Focus();
        };
        _canvas.AngledChanged += HighlightViewButton;
        _tips.SetToolTip(_btnView, "See the track tipped and turned, in 3D: drag to turn it round and tip it, wheel to zoom. Lit while it shows; press again, or pick a tool, to draw. (V)");

        BuildFileMenu();

        _btnKeys.Click += (_, _) =>
        {
            ShowTab(_openTab == TabKeys ? _lastTab : TabKeys, remember: false);
            _canvas.Focus();
        };
        _tips.SetToolTip(_btnKeys, "Every key the designer answers to. (?)");

        var btnSave = new Button { Text = "Save Layout" };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        btnSave.Click += (_, _) =>
        {
            // 17 Oct 2026, Bug Hunt 8, A31-1: a nudge clicked away from
            // within its 350 ms settle would otherwise be saved with the
            // nudged pieces stacked on what they hit.
            _canvas.LandPendingNudge();

            if (string.IsNullOrWhiteSpace(TrackName))
            {
                AppDialog.Show(this,
                    "Give the track a name first - that's what it's saved and listed under.",
                    "Name needed", MessageBoxButtons.OK, MessageBoxIcon.Information);

                // The name box is in the Plan section, which may be closed.
                ShowTab(TabPlan);
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

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        // Right to left: Cancel furthest right, Save beside it.
        var answer = new ButtonBar(alignRight: true, tight: true).With(btnCancel, btnSave);

        var bar = new FieldRow();
        bar.Add(null, _lblTitle);
        bar.Add(null, new ButtonBar(tight: true).With(undo, redo, fit));
        bar.Add(null, new ButtonBar(tight: true).With(_btnPlay, _btnView));
        bar.Add(null, new ButtonBar(tight: true).With(_btnExport, _btnKeys));
        bar.AddLast(null, answer, () => answer.OneLineWidth);

        _txtName.TextChanged += (_, _) => RefreshHeadlines();

        return bar;

        static int TightWidth(string caption)
        {
            using var probe = new Button { Text = caption };

            return UiLayout.TightButtonSizeFor(probe).Width;
        }
    }

    /// <summary>
    /// The File menu (Phase 7, Export as was): everything that leaves the
    /// designer - the picture at a size chosen, the picture copied, the plan
    /// printed to scale with its parts and build lists, the plan as a file
    /// another club can open or one opened from another club - and, last and
    /// in red, Clear, which used to sit among the tools.
    /// </summary>
    private void BuildFileMenu()
    {
        var savePicture = new ToolStripMenuItem("Save picture");
        var pictureSizes = new List<(ToolStripMenuItem Item, string Name, int PixelsPerTile)>();

        foreach (var (name, pixels) in PictureSizes)
        {
            var item = new ToolStripMenuItem(name);
            var captured = pixels;
            item.Click += (_, _) => ExportPng(captured);
            savePicture.DropDownItems.Add(item);
            pictureSizes.Add((item, name, pixels));
        }

        var copyPicture = new ToolStripMenuItem("Copy picture");
        copyPicture.Click += (_, _) => CopyPicture();

        var print = new ToolStripMenuItem("Print track and parts list...");
        print.Click += (_, _) => PrintPlan();

        var openPlan = new ToolStripMenuItem("Open plan file...");
        openPlan.Click += (_, _) => OpenPlanFile();
        openPlan.ToolTipText = "Open a .hctrack plan another club sent, in place of what is drawn. Ctrl+Z puts this back.";

        var savePlan = new ToolStripMenuItem("Save plan file...");
        savePlan.Click += (_, _) => SavePlanFile();
        savePlan.ToolTipText = "Save this plan as a .hctrack file - floor, rails, lanes, markings and line - to send to another club or keep.";

        var clear = new ToolStripMenuItem("Clear the plan...") { Tag = AppTheme.DangerMenuItem };
        clear.Click += (_, _) =>
        {
            if (_canvas.Angled)
            {
                _canvas.ToggleAngled();
            }

            ClearAll();
        };

        _exportMenu.ShowItemToolTips = true;
        _exportMenu.Items.AddRange(new ToolStripItem[]
        {
            savePicture, copyPicture, print, new ToolStripSeparator(), openPlan, savePlan, new ToolStripSeparator(), clear,
        });

        // Each size says what it comes out at, worked out as the menu opens;
        // nothing to save from an empty board.
        _exportMenu.Opening += (_, _) =>
        {
            var empty = Result.IsEmpty;

            foreach (var item in new[] { savePicture, copyPicture, print, savePlan })
            {
                item.Enabled = !empty;
            }

            // Markings and a racing line are something to clear, as ClearAll
            // has it - a plan holding only those is "empty" by IsEmpty.
            clear.Enabled = !empty || Result.Decals.Count > 0 || Result.Route.Count > 0;

            foreach (var (item, name, pixels) in pictureSizes)
            {
                var size = PictureSize(Result, pixels);
                var tooBig = size.Width > MostPicturePixels || size.Height > MostPicturePixels;

                item.Text = empty ? name : $"{name} - {size.Width:N0} x {size.Height:N0}" + (tooBig ? " (too big)" : string.Empty);
                item.Enabled = !empty && !tooBig;
            }
        };

        _btnExport.Click += (_, _) => _exportMenu.Show(_btnExport, new Point(0, _btnExport.Height));

        // Back to the board when the menu goes, as every other button's
        // click does - or Space would press File again, not move the view.
        _exportMenu.Closed += (_, _) => _canvas.Focus();
        _tips.SetToolTip(_btnExport, "Save or copy a picture of the plan, print it to scale with its parts list, open or save a plan file, or clear the plan.");
    }

    /// <summary>
    /// The tool rail down the board's left: Select, then the tools that lay
    /// things down, then Erase and Photo - one under another, the one in
    /// hand lit.
    /// </summary>
    private Button[] BuildToolRail()
    {
        // Pieces: a tile with its rail on, as it goes down on a real track
        // (railed tiles, Phase 2, 7 Nov 2026). It took the place of Floor,
        // and of Rails and Turns, which laid rails on the seams afterwards.
        var select = BarButton("Select", "Drag a box round a piece of the plan, then move it, turn it, mirror it or copy it. (8)", () => SetTool(RcpTool.Select));
        var pieces = BarButton("Pieces", "Lay a tile with its rail already on - pick the piece in the bar over the board. 1 a plain tile, 2 a rail tile, 3 an outer curve tile; R turns it.", ChoosePieces);
        var lane = BarButton("Lane", "Paint a lane: rail tiles down both sides that follow it, and an outer curve tile round every bend. (L)", () => SetTool(RcpTool.Lane));
        var corners = BarButton("Corners", "Click a corner to make it an outer curve tile or square, in turn. (C)", () => SetTool(RcpTool.Corner));
        var marker = BarButton("Markings", "The line, grid boxes, curbs, split loops, labels. R turns them. (4)", () => SetTool(RcpTool.Marker));
        var route = BarButton("Racing line", "Draw the racing line, or have it traced for you. (5)", () => SetTool(RcpTool.Route));
        var erase = BarButton("Erase", "Take things off. Ctrl+click or right-click does this with any tool. (6)", () => SetTool(RcpTool.Erase));
        var photo = BarButton("Photo", "Trace a photo of the real track: drag it into place; the wheel sizes it. (7)", () => SetTool(RcpTool.Backdrop));

        _toolButtons = new[]
        {
            (select, RcpTool.Select), (pieces, RcpTool.Floor), (pieces, RcpTool.Piece), (lane, RcpTool.Lane),
            (corners, RcpTool.Corner), (marker, RcpTool.Marker), (route, RcpTool.Route), (erase, RcpTool.Erase),
            (photo, RcpTool.Backdrop),
        };

        return new[] { select, pieces, lane, corners, marker, route, erase, photo };
    }

    /// <summary>
    /// The bar over the board (8 Nov 2026): only the options of the tool in
    /// hand - the pieces to lay, a lane's width, the markings, the racing
    /// line's buttons, the photo's, what to do with a selection. It is as
    /// tall as the tallest of them at the board's width, so picking a tool
    /// never moves the board.
    /// </summary>
    private ContextStrip BuildContextBar(IReadOnlyList<PaletteButton> pieceEntries, IReadOnlyList<PaletteButton> markingEntries, IReadOnlyList<Control> photoParts)
    {
        static Label Caption(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.TextDark,
            Anchor = AnchorStyles.Left,
        };

        static Label Hint(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
            Anchor = AnchorStyles.Left,
        };

        ButtonBar Bar(string caption, params Control[] parts) =>
            new ButtonBar(tight: true).With(new Control[] { Caption(caption) }.Concat(parts).ToArray());

        var strip = new ContextStrip();

        strip.AddBar(ContextPieces, Bar("Pieces", pieceEntries.Cast<Control>().Append(Hint("R turns it")).ToArray()));

        strip.AddBar(ContextLane, BuildLaneBar(Caption("Lane"), Hint));

        strip.AddBar(ContextCorners, Bar("Corners", Hint("Click a corner: an outer curve tile, then square. What you set here, the lanes leave alone.")));

        strip.AddBar(ContextMarkings, Bar("Markings", markingEntries.Cast<Control>().Append(Hint("R turns it")).ToArray()));

        var traceRoute = BarButton("Trace it for me", "Follow the corridor between the rails and lay the racing line down the middle of it, however wide. Traced, it follows the track as you change it, until you move a point of it by hand.", () => _canvas.AutoRoute());
        var reverseRoute = BarButton("Reverse", "Turn the racing line round, so it runs the way the cars do - its arrows show which way. Sectors are measured in that direction. (Shift+R)", () => _canvas.ReverseRoute());

        _chkClosedRoute.Anchor = AnchorStyles.Left;
        strip.AddBar(ContextRoute, Bar("Racing line", traceRoute, reverseRoute, _chkClosedRoute));

        strip.AddBar(ContextErase, Bar("Erase", Hint("Click a tile to take it up with its rail, or right on a rail for just the rail. Right-click erases with any tool.")));

        strip.AddBar(ContextPhoto, Bar("Photo", photoParts.ToArray()));

        // What to do with whatever is picked. Every one of these turns or
        // mirrors it WHERE IT STANDS - the box round it keeps its top-left
        // corner, so a turn never slides a layout off the floor it was
        // lined up with (RcpSelection).
        var turn = BarButton("Turn", "Turn what's picked a quarter clockwise, where it stands. (])", () => _canvas.TurnSelection(1));
        var flipAcross = BarButton("Flip across", "Mirror what's picked left to right.", () => _canvas.MirrorSelection(leftToRight: true));
        var flipDown = BarButton("Flip down", "Mirror what's picked top to bottom.", () => _canvas.MirrorSelection(leftToRight: false));
        var duplicate = BarButton("Duplicate", "A second copy, one tile along, ready to drag where it belongs. (Ctrl+D)", () => _canvas.DuplicateSelection());
        var remove = BarButton("Remove", "Take what's picked off the plan. (Delete)", () => _canvas.DeleteSelection());

        _selectionButtons = new[] { turn, flipAcross, flipDown, duplicate, remove };

        strip.AddBar(ContextSelect, Bar("Select", turn, flipAcross, flipDown, duplicate, remove, Hint("Drag a box round part of the plan first")));

        _contextBar = strip;

        HighlightSelectionButtons();

        return strip;
    }

    private const string ContextPieces = "pieces";
    private const string ContextLane = "lane";
    private const string ContextCorners = "corners";
    private const string ContextMarkings = "markings";
    private const string ContextRoute = "route";
    private const string ContextErase = "erase";
    private const string ContextPhoto = "photo";
    private const string ContextSelect = "select";

    /// <summary>Which of the bar's rows a tool shows.</summary>
    private static string ContextFor(RcpTool tool) => tool switch
    {
        RcpTool.Lane => ContextLane,
        RcpTool.Corner => ContextCorners,
        RcpTool.Marker => ContextMarkings,
        RcpTool.Route => ContextRoute,
        RcpTool.Erase => ContextErase,
        RcpTool.Backdrop => ContextPhoto,
        RcpTool.Select => ContextSelect,
        _ => ContextPieces,
    };

    /// <summary>
    /// The Lane tool's row in the bar over the board (the Lanes card, Phase
    /// 3, 25 Oct 2026, as was): paint a lane or drag a ring, and how many
    /// tiles wide - the one in use lit.
    /// </summary>
    private ButtonBar BuildLaneBar(Label caption, Func<string, Label> hint)
    {
        Button Choice(string text, string tip, Action chosen)
        {
            var button = new Button { Text = text };

            button.Click += (_, _) =>
            {
                chosen();
                HighlightLanes();
                _canvas.Focus();
            };

            _tips.SetToolTip(button, tip);
            return button;
        }

        _btnLanePaint = Choice("Lane", "Drag to paint a lane where the pointer goes.", () =>
        {
            _canvas.LaneRing = false;
            SetTool(RcpTool.Lane);
        });

        _btnLaneRing = Choice("Ring", "Drag a box: a closed lane goes round just inside it.", () =>
        {
            _canvas.LaneRing = true;
            SetTool(RcpTool.Lane);
        });

        _laneWidthButtons = new[] { 1, 2, 3 }
            .Select(width => (Choice(width.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Lanes {width} {(width == 1 ? "tile" : "tiles")} wide.", () =>
            {
                _canvas.LaneWidth = width;
                SetTool(RcpTool.Lane);
            }), width))
            .ToArray();

        // No choice of turns any more (railed tiles, Phase 2, 7 Nov 2026):
        // the one-tile outer curve tile is the only turn a club has, and a
        // square outside corner would be two rails on one tile. A plan saved
        // with two-tile or square lane turns keeps them.
        return new ButtonBar(tight: true).With(new Control[] { caption, _btnLanePaint, _btnLaneRing, hint("Width") }
            .Concat(_laneWidthButtons.Select(pair => (Control)pair.Button))
            .Append(hint("tiles - rail tiles go down both sides, an outer curve tile round every bend"))
            .ToArray());
    }

    private void SetTool(RcpTool tool)
    {
        // A tool is for drawing, which is done on the flat plan.
        if (_canvas.Angled)
        {
            _canvas.ToggleAngled();
        }

        _canvas.Tool = tool;

        // A button can stand for more than one tool - Pieces lays a plain
        // tile (Floor) and the railed pieces (Piece) - so it is lit when any
        // of its tools is in hand.
        var lit = _toolButtons.Where(pair => pair.Tool == tool).Select(pair => pair.Button).ToHashSet();

        foreach (var button in _toolButtons.Select(pair => pair.Button).Distinct())
        {
            button.BackColor = lit.Contains(button) ? AppTheme.Primary : AppTheme.SecondaryButton;
            button.ForeColor = lit.Contains(button) ? AppTheme.OnPrimary : AppTheme.TextDark;
            button.FlatAppearance.BorderSize = 0;
        }

        _lblStatus.Text = ToolHint(tool);
        _contextBar?.ShowBar(ContextFor(tool));
    }

    /// <summary>The Pieces button: the piece picked last in the bar over the board, or a rail tile when the palette is on something else.</summary>
    private void ChoosePieces()
    {
        PickPiece(RcpTiles.Pieces.Contains(_canvas.Item) ? _canvas.Item : RcpPaletteItem.RailTile);
    }

    /// <summary>Picks a piece as its palette entry would - for the Pieces button and the 1, 2 and 3 keys.</summary>
    private void PickPiece(RcpPaletteItem item)
    {
        _canvas.Item = item;
        SetTool(RcpTiles.ToolFor(item));
        HighlightPalette();
    }

    /// <summary>How a tool works, as the status line says when it is picked.</summary>
    private static string ToolHint(RcpTool tool) => tool switch
    {
        RcpTool.Floor => "Drag to lay plain tiles - no rail on them. Ctrl-drag takes them back up.",
        RcpTool.Piece => "Click to lay the piece with its rail on - near an edge for a rail tile, in a corner for an outer curve. Drag to lay a row. R turns it; click it again for a plain tile.",
        RcpTool.Wall => "Drag along the grid to run rail; a turn rail drops in wherever you round a corner. Click a seam to add, double or remove one.",
        RcpTool.Curve => "Click near a corner of the grid - the arc follows whichever way the pointer is.",
        RcpTool.Marker => "Click to drop a marking. Click it again, or press R, to turn it.",
        RcpTool.Route => "Click to drop points along the racing line. Drag one to move it, right-click to remove it.",
        RcpTool.Erase => "Click a tile to take it up with its rail. Click right on a rail to take just the rail off its tile.",
        RcpTool.Select => "Drag a box round part of the plan to pick it up - the rails round a block of tiles come too. Then drag it to move, arrows to nudge, ] to turn, Delete to take it off.",
        RcpTool.Lane => "Drag to paint a lane - rail tiles down both sides, an outer curve tile round every bend. Start on a lane to carry it on. Ring: drag a box for a whole circuit.",
        RcpTool.Corner => "Click a corner: an outer curve tile, then square. What you set here, the lanes leave alone.",
        _ => "Drag the photo into place and use the wheel to size it until its tiles line up with the grid.",
    };

    /// <summary>Greys the buttons that act on a selection while there isn't one.</summary>
    private void HighlightSelectionButtons()
    {
        foreach (var button in _selectionButtons)
        {
            button.Enabled = _canvas.HasSelection;
        }
    }

    /// <summary>
    /// The pieces and the markings, for the bar over the board: an entry
    /// for each, its picture beside its name, its hint as its tooltip, the
    /// one in hand lit.
    /// </summary>
    private (IReadOnlyList<PaletteButton> Pieces, IReadOnlyList<PaletteButton> Markings) BuildPalette()
    {
        PaletteButton Entry(RcpPaletteItem item)
        {
            var button = new PaletteButton(item, compact: true);

            button.Chosen += chosen =>
            {
                _canvas.Item = chosen;
                SetTool(RcpTiles.ToolFor(chosen));
                HighlightPalette();
                _canvas.Focus();
            };

            _tips.SetToolTip(button, RcpTiles.HintOf(item));
            _paletteButtons.Add(button);

            return button;
        }

        // The pieces, each a tile with its rail already on, as a real RCP
        // track goes down (railed tiles, Phase 2, 7 Nov 2026).
        var pieces = RcpTiles.Pieces.Select(Entry).ToList();
        var markings = RcpTiles.Markings.Select(Entry).ToList();

        HighlightPalette();

        return (pieces, markings);
    }

    /// <summary>Lights the Lanes card's choices in use - the plan's turns follow Undo, so this runs after every change.</summary>
    private void HighlightLanes()
    {
        void Light(Button button, bool active)
        {
            button.BackColor = active ? AppTheme.Primary : AppTheme.SecondaryButton;
            button.ForeColor = active ? AppTheme.OnPrimary : AppTheme.TextDark;
            button.FlatAppearance.BorderSize = 0;
        }

        Light(_btnLanePaint, !_canvas.LaneRing);
        Light(_btnLaneRing, _canvas.LaneRing);

        foreach (var (button, width) in _laneWidthButtons)
        {
            Light(button, width == _canvas.LaneWidth);
        }

        foreach (var (button, radius) in _laneTurnButtons)
        {
            Light(button, radius == _canvas.Design.LaneTurnRadius);
        }
    }


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
    /// The inspector's cards, by the section they go under: Plan - the
    /// track's name, start from a kit, the tile size and the kit it is
    /// checked against; Room - the room; Lap - the lap and what the plan
    /// comes to; Auto - rail the edge and build me a track. Returns them,
    /// and the photo's controls, for the Photo tool's row over the board.
    /// </summary>
    private (Section[] Plan, Section[] Room, Section[] Lap, Section[] Auto, IReadOnlyList<Control> PhotoParts) BuildSidePanel()
    {
        // --- Track name ---
        _txtName.Text = _trackName;

        // The main-picture box with the name, as it was at the foot.
        _chkUseAsPhoto.Text = "Show this plan as the track's main picture";
        _chkUseAsPhoto.AutoSize = true;
        _chkUseAsPhoto.Checked = true;

        var nameCard = new Section("Track name");
        nameCard.Add(_txtName);
        nameCard.Add(_chkUseAsPhoto, stretch: false, gapBelow: 0);
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

            // Through the canvas, as an undo step (see RcpTrackCanvas.SetRoom).
            _canvas.SetTileSize((double)_numTileSize.Value);
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

        // As tall as it always was, at the text size in use - it draws its
        // track and handle in the middle of that.
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

            // An undo step, which the canvas only redraws for - so the undo
            // and redo buttons are put right here.
            _canvas.SetBackdropOpacity(_barBackdrop.Value);

            if (_btnUndo is not null)
            {
                _btnUndo.Enabled = _canvas.CanUndo;
            }

            if (_btnRedo is not null)
            {
                _btnRedo.Enabled = _canvas.CanRedo;
            }
        };

        var lblTraceHint = new Label
        {
            Text = "Drag the photo to line its tiles up with the grid; the wheel sizes it.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

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

            _canvas.SetRouteClosed(_chkClosedRoute.Checked);
            RefreshReadouts();
        };

        // As tall as the most it can say, at a big layout's numbers: a lap
        // round a floor with no rails on it yet, which is the most lines; no
        // lap yet, whose line about it is the longest; and rails, dividers
        // and a lap. The kit check under it then stays where it is as the
        // plan changes. It used to be given a fixed 190px, which the most it
        // can say ran past.
        // With corner tiles, so the readout's extra line is measured too.
        var floorOnly = new RcpInventory { Tiles = 480, Infield = 64, OuterCurveTiles = 12, InnerCornerTiles = 12 };
        var railed = new RcpInventory { Tiles = 480, Infield = 64, StraightRails = 240, Dividers = 36, TurnRails = 28, EndCaps = 6, OuterCurveTiles = 12, InnerCornerTiles = 12 };

        _lblStats.Wordings = new[]
        {
            Readout(floorOnly, lengthTiles: 160f, tileSizeMm: 500, lapSeconds: _canvas.LapSeconds),
            Readout(floorOnly, lengthTiles: 0f, tileSizeMm: 500, lapSeconds: _canvas.LapSeconds),
            Readout(railed, lengthTiles: 160f, tileSizeMm: 500, lapSeconds: _canvas.LapSeconds),

            // Every split loop placed: four sectors under the lap.
            Readout(floorOnly, lengthTiles: 160f, tileSizeMm: 500, lapSeconds: _canvas.LapSeconds,
                sectors: (new[] { 40f, 40f, 40f, 40f }, null)),

            // ...or the longest thing it says about why they can't be measured.
            Readout(railed, lengthTiles: 160f, tileSizeMm: 500, lapSeconds: _canvas.LapSeconds,
                sectors: (null, "Draw the racing line (or press Trace it for me) through the finish line and the split loops.")),

            // And the same again with rails standing on nothing, which is
            // two more lines under all of it. Left out, the card below
            // would jump down the moment somebody drew a rail past the
            // edge of the floor - which is exactly when it should not.
            Readout(railed, lengthTiles: 160f, tileSizeMm: 500, lapSeconds: _canvas.LapSeconds,
                sectors: (null, "Draw the racing line (or press Trace it for me) through the finish line and the split loops."),
                railsWithNoFloor: 12),

            // ...and custom tiles too, two lines more (railed tiles, Phase 2).
            Readout(railed, lengthTiles: 160f, tileSizeMm: 500, lapSeconds: _canvas.LapSeconds,
                sectors: (null, "Draw the racing line (or press Trace it for me) through the finish line and the split loops."),
                railsWithNoFloor: 12, customTiles: 12),
        };

        var lapCard = new Section("The lap");
        lapCard.Add(_lblStats, gapBelow: UiLayout.RowGap);

        // How fast Play goes (Phase 6): half speed, as the lap time, or double.
        _paceButtons = new[] { (0.5f, "0.5x"), (1f, "1x"), (2f, "2x") }
            .Select(pair =>
            {
                var button = new Button { Text = pair.Item2 };

                button.Click += (_, _) =>
                {
                    _canvas.PaceRate = pair.Item1;
                    HighlightPace();
                    _canvas.Focus();
                };

                return (button, pair.Item1);
            })
            .ToArray();

        lapCard.Add(new Label
        {
            Text = "Play runs the car round at:",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        }, gapBelow: UiLayout.HintGap);
        lapCard.Add(new ButtonBar(tight: true).With(_paceButtons.Select(pair => (Control)pair.Button).ToArray()), gapBelow: UiLayout.HintGap);
        lapCard.Add(new Label
        {
            Text = "Click the lap in the corner of the board for where its time comes from, and the sectors.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        }, gapBelow: 0);

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

            // The kit and the tile size it comes in, as one undo step. The
            // canvas announces the change, which puts the size box in line.
            var name = _cmbKit.SelectedIndex <= 0 ? string.Empty : (string)_cmbKit.SelectedItem!;

            _canvas.SetKit(name, RcpKit.ByName(name)?.TileSizeMm);
            HighlightTileSize();
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

        // --- The room it has to go in ---
        //
        // The grid is unbounded on purpose, so nothing here stops a plan
        // being drawn forty feet across. What a club actually needs to
        // know is whether it goes in the hall, which is arithmetic - see
        // RoomFit.
        _btnRoomFeet.Text = "Feet";
        _btnRoomMetres.Text = "Meters";

        _btnRoomFeet.Click += (_, _) => SetRoomUnits(feet: true);
        _btnRoomMetres.Click += (_, _) => SetRoomUnits(feet: false);

        foreach (var box in new[] { _numRoomWidth, _numRoomDepth })
        {
            box.Width = UiLayout.AtTextSize(TileSizeBoxWidth);
            box.Minimum = 0;
            box.Maximum = 500;
            box.DecimalPlaces = 1;
            box.Increment = 0.5m;
            box.ValueChanged += (_, _) =>
            {
                if (_loading)
                {
                    return;
                }

                SaveRoom();
                RefreshReadouts();
            };
        }

        var lblBy = new Label
        {
            Text = "by",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        _lblRoomUnits.Text = "feet";
        _lblRoomUnits.AutoSize = true;
        _lblRoomUnits.Font = AppTheme.SmallFont;
        _lblRoomUnits.ForeColor = AppTheme.TextMuted;

        // Last in the card, so the verdict can run to two lines without
        // moving the boxes above it.
        _lblRoomFit.AutoSize = true;
        _lblRoomFit.Font = AppTheme.SmallFont;

        var roomCard = new Section("The room it goes in");
        roomCard.Add(new ButtonBar(tight: true).With(_btnRoomFeet, _btnRoomMetres));
        roomCard.Add(FieldGrid.Inline(_numRoomWidth, lblBy, _numRoomDepth, _lblRoomUnits));
        roomCard.Add(_lblRoomFit, gapBelow: UiLayout.HintGap);
        roomCard.Add(new Label
        {
            Text = "To trace a photo of the real track, pick Photo on the left.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        }, gapBelow: 0);

        LoadRoom();

        // --- Start from a kit ---
        //
        // First in the column: it is the thing you do before you draw
        // anything, and after that it is a thing you never touch again.
        _cmbStock.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbStock.Items.Add(StockPrompt);

        foreach (var layout in RcpStockLayout.All)
        {
            _cmbStock.Items.Add(layout.Name);
        }

        _cmbStock.SelectedIndex = 0;
        _cmbStock.SelectedIndexChanged += (_, _) => StartFromKit();

        _lblStock.AutoSize = true;
        _lblStock.Font = AppTheme.SmallFont;
        _lblStock.ForeColor = AppTheme.TextMuted;
        _lblStock.Text = "Lays out one of RCP's own tracks to start from.";

        var stockCard = new Section("Start from a kit");
        stockCard.Add(_cmbStock);
        Pieces.KeepHeightTold(_cmbStock);
        stockCard.Add(_lblStock, gapBelow: 0);

        // Build me a track (Track Designer overhaul, Phase 5): layouts
        // worked out to fit the room, the lanes and the cupboard.
        var buildCard = new Section("Build me a track");
        var btnBuild = new Button { Text = "Build me a track..." };
        btnBuild.Click += (_, _) => BuildMeATrack();
        _tips.SetToolTip(btnBuild, "Layouts that fit the room in the Room section, at the Lane tool's width - RCP's own that fit, then ones worked out within what the club owns.");

        buildCard.Add(new Label
        {
            Text = "Layouts made to fit your room - an oval, an L, a hairpin, a serpentine and more - with RCP's own that fit.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        }, gapBelow: UiLayout.HintGap);
        buildCard.Add(new ButtonBar(tight: true).With(btnBuild), gapBelow: 0);

        // Rail the edge, which used to be a toolbar button among the tools.
        var railCard = new Section("Rail the edge");
        var btnRail = BarButton("Rail the edge", "Make every tile round the edge of the floor a rail tile facing out, and its outside corners outer curve tiles.", () => _canvas.WallThePerimeter());

        railCard.Add(new Label
        {
            Text = "Every tile round the edge of the floor becomes a rail tile facing out, its outside corners outer curve tiles.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        }, gapBelow: UiLayout.HintGap);
        railCard.Add(new ButtonBar(tight: true).With(btnRail), gapBelow: 0);

        // The photo's controls, for the Photo tool's row over the board -
        // they were the Room tab's Trace a photo card.
        _barBackdrop.Width = UiLayout.AtTextSize(160);
        _barBackdrop.Anchor = AnchorStyles.Left;

        var lblSeeThrough = new Label
        {
            Text = "See-through:",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
            Anchor = AnchorStyles.Left,
        };

        lblTraceHint.Anchor = AnchorStyles.Left;

        return (
            new[] { nameCard, stockCard, tileCard, kitCard },
            new[] { roomCard },
            new[] { lapCard },
            new[] { railCard, buildCard },
            new Control[] { btnTrace, btnHide, btnDropPhoto, lblSeeThrough, _barBackdrop, lblTraceHint });
    }

    /// <summary>
    /// The inspector's width: as wide as the side panel always was, at the
    /// text size in use - or, when one needs more, the kit drop-down's
    /// longest choice or a card's caption, in its card, beside the scrollbar
    /// the column takes its width from when it scrolls. Asked whenever the
    /// inspector is placed, so it follows the faces the theme pass gives them.
    /// </summary>
    private int SideWidth(IReadOnlyList<Section> cards)
    {
        // The layout picker as well as the kit's: "30cm Double Oval Wide"
        // is longer than any kit name, and a drop-down whose choices are
        // cut off is a drop-down you cannot choose from.
        var pickers = Math.Max(
            Pieces.PickerWidth(_cmbKit, ref _kitWidth),
            Pieces.PickerWidth(_cmbStock, ref _stockWidth));

        var widest = new[]
        {
            pickers,
            cards.Max(card => card.CaptionWidth),
        }.Max();

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

    // --- Behaviour ------------------------------------------------------------

    /// <summary>Frames the plan in the board at the size the board has now - see the constructor.</summary>
    private void FrameUntilShown(object? sender, EventArgs e) => _canvas.Fit();

    /// <summary>
    /// Feet or metres - how the club measured the hall, not how the tiles
    /// are sold. The plan keeps millimetres either way; this only changes
    /// what the two boxes are read as.
    /// </summary>
    private void SetRoomUnits(bool feet)
    {
        // Through the board, for the undo step - see SaveRoom.
        _canvas.SetRoom(Result.RoomWidthMm, Result.RoomDepthMm, feet);

        LoadRoom();
        RefreshReadouts();

        _canvas.Focus();
    }

    /// <summary>Millimetres in one of whichever unit the room is being given in.</summary>
    private double RoomUnitMm => Result.RoomInFeet ? 304.8 : 1000.0;

    /// <summary>Reads the two boxes into the plan.</summary>
    /// <summary>
    /// Writes the room's size onto the plan - through the board, so it takes
    /// an undo step of its own like every other edit on the plan does. It
    /// used to be written straight into the plan here, and Ctrl+Z then
    /// blanked it (see RcpTrackCanvas.SetRoom).
    /// </summary>
    private void SaveRoom() => _canvas.SetRoom(
        (double)_numRoomWidth.Value * RoomUnitMm,
        (double)_numRoomDepth.Value * RoomUnitMm,
        Result.RoomInFeet);

    /// <summary>
    /// Shows the plan's room in the boxes, in the units it was given in.
    ///
    /// Guarded with _loading, like every other control the side panel sets
    /// from the plan: writing a clamped value straight back would quietly
    /// change a room typed into a settings file by hand.
    /// </summary>
    private void LoadRoom()
    {
        var was = _loading;
        _loading = true;

        try
        {
            _numRoomWidth.Value = (decimal)Math.Clamp(Result.RoomWidthMm / RoomUnitMm, 0, 500);
            _numRoomDepth.Value = (decimal)Math.Clamp(Result.RoomDepthMm / RoomUnitMm, 0, 500);
            _lblRoomUnits.Text = Result.RoomInFeet ? "feet" : "meters";
        }
        finally
        {
            _loading = was;
        }

        HighlightRoomUnits();
    }

    private void HighlightRoomUnits()
    {
        void Mark(Button button, bool active)
        {
            button.BackColor = active ? AppTheme.Primary : AppTheme.SecondaryButton;
            button.ForeColor = active ? AppTheme.OnPrimary : AppTheme.TextDark;
            button.FlatAppearance.BorderSize = 0;
        }

        Mark(_btnRoomFeet, Result.RoomInFeet);
        Mark(_btnRoomMetres, !Result.RoomInFeet);
    }

    /// <summary>
    /// Whether what is drawn goes in the room, and by how much - see
    /// RoomFit. Measured over everything drawn rather than over the floor
    /// alone, because a rail past the edge of the tiles still has to fit
    /// through the door.
    /// </summary>
    private void RefreshRoomFit()
    {
        if (!Result.HasRoom)
        {
            _lblRoomFit.Text = "Enter the room's size to check the layout fits in it.";
            _lblRoomFit.ForeColor = AppTheme.TextMuted;
            return;
        }

        var bounds = Result.BoundsInTiles(includeRoute: false);

        var fit = RoomFit.Check(
            bounds.Width * Result.TileSizeMm,
            bounds.Height * Result.TileSizeMm,
            Result.RoomWidthMm,
            Result.RoomDepthMm);

        _lblRoomFit.Text = fit.Verdict == RoomFit.Verdict.Unknown
            ? "Nothing laid out yet."
            : fit.Says;

        _lblRoomFit.ForeColor = fit.Verdict switch
        {
            RoomFit.Verdict.Comfortable => AppTheme.TextMuted,
            RoomFit.Verdict.Unknown => AppTheme.TextMuted,
            _ => AppTheme.Accent,
        };
    }

    private void SetTileSize(double mm)
    {
        _canvas.SetTileSize(mm);

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

    /// <summary>What the picker says when nothing has been chosen from it.</summary>
    private const string StockPrompt = "Choose a layout...";

    /// <summary>
    /// Opens Build me a track, and puts the layout picked on the board in
    /// one step Undo takes back - asking first, as Start from a kit does,
    /// when it would replace work.
    /// </summary>
    private void BuildMeATrack()
    {
        RcpBuildForm form;

        // Working the layouts out takes a moment in a big hall.
        Cursor = Cursors.WaitCursor;

        try
        {
            form = new RcpBuildForm(Result, _stock, _canvas.LaneWidth);
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        using var dialog = form;

        if (form.ShowDialog(this) != DialogResult.OK || form.Chosen is not { } chosen)
        {
            _canvas.Focus();
            return;
        }

        var hasWork = !Result.IsEmpty || Result.Decals.Count > 0 || Result.Route.Count > 0;

        if (hasWork
            && AppDialog.Show(
                this,
                $"Replace what you have drawn with the {chosen.Name}? This can be undone with Ctrl+Z.",
                "Build me a track",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            _canvas.Focus();
            return;
        }

        _canvas.LoadBuilt(chosen.Design);
        _canvas.Fit();

        SyncSidePanel();
        RefreshReadouts();

        _lblStatus.Text = chosen.RcpOwn
            ? $"{chosen.Name} laid out. Reshape it as you like."
            : $"{chosen.Name} laid out - it's made of lanes, so the Lane tool reshapes it and its rails follow.";

        _canvas.Focus();
    }

    /// <summary>
    /// Lays out one of RCP's own tracks over whatever is drawn.
    ///
    /// The floor and the rails round it are exact - the tile count is
    /// published and factors into one sensible rectangle. What is inside
    /// is read off RCP's photographs, which are three-quarter renders
    /// rather than plans, so it is the right shape and not the right
    /// measurements. Said on the card afterwards rather than buried in a
    /// tooltip, because somebody laying out their hall from this is owed
    /// the distinction.
    /// </summary>
    private void StartFromKit()
    {
        if (_loading || _resettingStock || _cmbStock.SelectedIndex <= 0)
        {
            return;
        }

        var name = (string)_cmbStock.SelectedItem!;

        // Back to the prompt straight away, so the same layout can be
        // chosen twice running - after an Undo, say.
        _resettingStock = true;

        try
        {
            _cmbStock.SelectedIndex = 0;
        }
        finally
        {
            _resettingStock = false;
        }

        if (RcpStockLayout.ByName(name) is not { } choice || RcpStockLayout.Build(name) is not { } stock)
        {
            return;
        }

        // Same test ClearAll uses: markings and a racing line count as
        // work, even though IsEmpty is only about floor and rails.
        var hasWork = !Result.IsEmpty || Result.Decals.Count > 0 || Result.Route.Count > 0;

        if (hasWork)
        {
            var confirm = AppDialog.Show(
                this,
                $"Replace what you have drawn with the {name}? This can be undone with Ctrl+Z.",
                "Start from a kit",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
            {
                return;
            }
        }

        _canvas.LoadStock(stock);
        _canvas.Fit();

        SyncSidePanel();
        RefreshReadouts();

        _lblStock.Text = choice.What
            + "  The floor and its rails are exact; the circuit inside is read off RCP's photographs, so move it about to taste.";

        _canvas.Focus();
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

    /// <summary>The sizes Save picture offers: how many pixels each tile is.</summary>
    private static readonly (string Name, int PixelsPerTile)[] PictureSizes =
    {
        ("Small", 32),
        ("Medium", 64),
        ("Large", 128),
        ("Poster", 256),
    };

    /// <summary>The widest or tallest picture saved, in pixels - past this a bitmap runs a PC out of memory.</summary>
    private const int MostPicturePixels = 12000;

    /// <summary>The widest or tallest picture copied - kept smaller, since whatever it is pasted into has to hold it.</summary>
    private const int MostCopiedPixels = 4000;

    /// <summary>The part of the plan a picture shows, in tiles: the plan and its line, with a little room round them.</summary>
    private static RectangleF PictureArea(RcpTrackDesign design)
    {
        var area = design.BoundsInTiles(includeRoute: true);
        area.Inflate(0.6f, 0.6f);

        return area;
    }

    /// <summary>How big a picture of <paramref name="design"/> comes out at <paramref name="pixelsPerTile"/>.</summary>
    private static Size PictureSize(RcpTrackDesign design, int pixelsPerTile)
    {
        if (design.IsEmpty)
        {
            return Size.Empty;
        }

        var area = PictureArea(design);

        return new Size(
            Math.Max(16, (int)Math.Ceiling(area.Width * pixelsPerTile)),
            Math.Max(16, (int)Math.Ceiling(area.Height * pixelsPerTile)));
    }

    /// <summary>Saves a picture of the plan as a PNG, each tile <paramref name="pixelsPerTile"/> pixels.</summary>
    private void ExportPng(int pixelsPerTile)
    {
        var plan = Result;
        var size = PictureSize(plan, pixelsPerTile);

        if (plan.IsEmpty || size.Width > MostPicturePixels || size.Height > MostPicturePixels)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Save a picture of the track plan",
            Filter = "PNG image (*.png)|*.png",
            FileName = $"{Sanitise(string.IsNullOrEmpty(TrackName) ? "track" : TrackName)} layout.png",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            _canvas.Focus();
            return;
        }

        try
        {
            Cursor = Cursors.WaitCursor;

            using var bitmap = RcpTrackPainter.RenderRegion(plan, PictureArea(plan), pixelsPerTile, Color.White);
            bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);

            _lblStatus.Text = $"Saved to {Path.GetFileName(dialog.FileName)}, {size.Width:N0} x {size.Height:N0}.";
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't save that: {ex.Message}", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Cursor = Cursors.Default;
            _canvas.Focus();
        }
    }

    /// <summary>Copies a picture of the plan, to paste into an email, a post or a document.</summary>
    private void CopyPicture()
    {
        var plan = Result;

        if (plan.IsEmpty)
        {
            return;
        }

        // Medium, or smaller for a big hall, so what it goes into can hold it.
        var area = PictureArea(plan);
        var pixels = Math.Min(64f, MostCopiedPixels / Math.Max(area.Width, area.Height));

        try
        {
            using var bitmap = RcpTrackPainter.RenderRegion(plan, area, pixels, Color.White);
            Clipboard.SetImage(bitmap);

            _lblStatus.Text = "Picture copied - paste it where you like.";
        }
        catch (Exception ex)
        {
            // The clipboard can be held by another program for a moment.
            AppDialog.Show(this, $"Couldn't copy the picture: {ex.Message}", "Copy picture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _canvas.Focus();
    }

    /// <summary>Print track and parts list: the scale first, then the printer and a preview.</summary>
    private void PrintPlan()
    {
        var plan = Result;

        if (plan.IsEmpty)
        {
            return;
        }

        using (var form = new RcpPrintForm(plan))
        {
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                var stock = _stock.OwnedFor(plan.TileSizeMm).Anything ? _stock : null;

                RcpPlanPrinter.Print(this, new RcpPlanPrinter.Job(plan, TrackName, form.Ratio, form.Parts, stock));
            }
        }

        _canvas.Focus();
    }

    /// <summary>
    /// A tile clicked with Label: its name asked for, then put on it as one
    /// undo step. Empty takes the name off.
    /// </summary>
    private void AskTileLabel(Point cell, string current)
    {
        using (var prompt = new TextPromptForm(
                   "Tile label",
                   $"A name for this tile - \"Pit lane\", \"Marshal\" - up to {RcpTrackCanvas.MostLabel} letters. Leave it empty to take the name off.",
                   current))
        {
            if (prompt.ShowDialog(this) == DialogResult.OK)
            {
                _canvas.SetTileLabel(cell, prompt.Answer);
            }
        }

        _canvas.Focus();
    }

    /// <summary>Saves the plan as a .hctrack file - see RcpPlanFile.</summary>
    private void SavePlanFile()
    {
        var plan = Result;

        if (plan.IsEmpty)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Save the plan file",
            Filter = RcpPlanFile.Filter,
            DefaultExt = RcpPlanFile.Extension.TrimStart('.'),
            FileName = $"{Sanitise(string.IsNullOrEmpty(TrackName) ? "track" : TrackName)}{RcpPlanFile.Extension}",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            _canvas.Focus();
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, RcpPlanFile.Write(plan, TrackName));

            _lblStatus.Text = $"Saved to {Path.GetFileName(dialog.FileName)}. Another club opens it from Open plan file in the File menu.";
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't save that: {ex.Message}", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        _canvas.Focus();
    }

    /// <summary>
    /// Opens a .hctrack file in place of what is drawn, in one step Undo
    /// takes back - asking first when it would replace work, as Build me a
    /// track does. A file that isn't a plan, or is from a newer HeatCaller,
    /// is refused with why (RcpPlanFile.Read).
    /// </summary>
    private void OpenPlanFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open a plan file",
            Filter = RcpPlanFile.Filter,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            _canvas.Focus();
            return;
        }

        RcpTrackDesign plan;
        string name;

        try
        {
            if (new FileInfo(dialog.FileName).Length > RcpPlanFile.MostBytes)
            {
                AppDialog.Show(this, "That file is far bigger than any track plan, so it wasn't opened.", "Open plan file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _canvas.Focus();
                return;
            }

            var read = RcpPlanFile.Read(File.ReadAllText(dialog.FileName));

            if (read.Problem is not null || read.Design is null)
            {
                AppDialog.Show(this, read.Problem ?? "That file isn't a HeatCaller track plan.", "Open plan file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _canvas.Focus();
                return;
            }

            plan = read.Design;
            name = read.Name;
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't open that: {ex.Message}", "Open plan file", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _canvas.Focus();
            return;
        }

        var hasWork = !Result.IsEmpty || Result.Decals.Count > 0 || Result.Route.Count > 0;
        var what = name.Length > 0 ? $"the plan \"{name}\"" : "that plan";

        if (hasWork
            && AppDialog.Show(
                this,
                $"Replace what you have drawn with {what}? This can be undone with Ctrl+Z.",
                "Open plan file",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            _canvas.Focus();
            return;
        }

        var roomFromFile = _canvas.LoadPlanFile(plan);
        _canvas.Fit();

        SyncSidePanel();
        RefreshReadouts();

        // The track's name is left alone - it isn't part of the plan Undo
        // puts back, and this track is the club's whatever the file called
        // it - so the file's name is only said.
        _lblStatus.Text = $"Opened {Path.GetFileName(dialog.FileName)}"
                          + (name.Length > 0 ? $" (\"{name}\")." : ".")
                          + (roomFromFile ? " Its room came with it - check it in the Room section." : string.Empty);

        _canvas.Focus();
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

            // The room, in whichever unit the plan was given in. Undo can
            // take a room back off a plan as readily as it takes a tile
            // off the floor, and the boxes have to follow it.
            var unit = Result.RoomInFeet ? 304.8 : 1000.0;
            var width = (decimal)Math.Clamp(Result.RoomWidthMm / unit, 0, 500);
            var depth = (decimal)Math.Clamp(Result.RoomDepthMm / unit, 0, 500);

            if (_numRoomWidth.Value != width)
            {
                _numRoomWidth.Value = width;
            }

            if (_numRoomDepth.Value != depth)
            {
                _numRoomDepth.Value = depth;
            }

            _lblRoomUnits.Text = Result.RoomInFeet ? "feet" : "meters";
        }
        finally
        {
            _loading = false;
        }

        HighlightTileSize();
        HighlightRoomUnits();
        HighlightLanes();
    }

    private void RefreshReadouts()
    {
        if (_loading)
        {
            return;
        }

        ChooseLap();

        var inventory = RcpPlan.Count(Result);
        var lengthTiles = RcpPlan.RouteLengthInTiles(Result);

        // The lap chosen, as the chip has it - never the 12s stand-in Play
        // drives at without one. That made the card say "At a 12.0s lap"
        // while the chip said there was no lap time, and put 12.0s in place
        // of an entered time on a line of fewer than 3 points (10 Nov 2026,
        // bug hunt 16).
        _lblStats.Text = Readout(
            inventory,
            lengthTiles,
            Result.TileSizeMm,
            _lap.Seconds > 0 ? _lap.Seconds : 0,
            RcpPlan.MeasureSectors(Result),
            RcpPlan.RailsWithNoFloor(Result),
            _canvas.PieceReport.CustomTiles.Count);

        RefreshKitCheck(inventory);
        RefreshRoomFit();
        RefreshChips(lengthTiles);
        RefreshStock(inventory);
        RefreshHeadlines();

        if (_btnUndo is not null)
        {
            _btnUndo.Enabled = _canvas.CanUndo;
        }

        if (_btnRedo is not null)
        {
            _btnRedo.Enabled = _canvas.CanRedo;
        }

        HighlightSelectionButtons();
    }

    /// <summary>
    /// What the lap's readout says of a plan with <paramref name="inventory"/>
    /// in it and a racing line <paramref name="lengthTiles"/> tiles long, at
    /// <paramref name="tileSizeMm"/> a tile and <paramref name="lapSeconds"/>
    /// a lap - for the plan being drawn, and for the most it can say, which
    /// the readout is made as tall as (see BuildSidePanel).
    /// </summary>
    /// <param name="sectors">
    /// The sectors between the split loops on the plan (RcpPlan.MeasureSectors),
    /// or what stops them being measured; null with no split loops.
    /// </param>
    private static string Readout(RcpInventory inventory, float lengthTiles, double tileSizeMm, double lapSeconds,
        (float[]? Sectors, string? Problem)? sectors = null, int railsWithNoFloor = 0, int customTiles = 0)
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
                : "Lap: draw the racing line, or press Trace it for me",
        };

        // The corner tiles among them (Phase 4): RCP sells them apart.
        if (inventory.OuterCurveTiles + inventory.InnerCornerTiles > 0)
        {
            lines.Insert(1, $"  of them {inventory.OuterCurveTiles} outer curve, {inventory.InnerCornerTiles} inner corner");
        }

        // Each sector along the racing line, between the split loops -
        // what Race Control turns into sector speeds.
        if (sectors is { Sectors: { } measured })
        {
            for (var i = 0; i < measured.Length; i++)
            {
                var sectorMetres = measured[i] * tileSizeMm / 1000.0;
                lines.Add($"Sector {i + 1}: {sectorMetres:0.00} m  /  {sectorMetres * 3.28084:0.0} ft");
            }
        }
        else if (sectors is { Problem: { } problem })
        {
            lines.Add("Sectors: " + problem);
        }

        if (lengthTiles > 0 && lapSeconds > 0)
        {
            var speed = metres / lapSeconds;
            lines.Add($"At a {lapSeconds:0.0}s lap that's {speed:0.0} m/s ({speed * 3.6:0.0} km/h).");
        }

        if (inventory.Tiles > 0 && inventory.StraightRails == 0)
        {
            lines.Add(string.Empty);
            lines.Add("No rails yet - try Rail the edge, then lay rail tiles back to back for the dividers.");
        }

        // A rail bolts through the tile it runs along, so one out past the
        // edge of the floor is not a piece anybody can build - see
        // RcpPlan.RailsWithNoFloor.
        if (railsWithNoFloor > 0)
        {
            lines.Add(string.Empty);
            lines.Add(railsWithNoFloor == 1
                ? "1 rail has no floor under it - a rail bolts onto a tile, so it needs one."
                : $"{railsWithNoFloor} rails have no floor under them - a rail bolts onto a tile, so it needs one.");
        }

        // A tile with a second rail: no piece RCP sells, so the club makes
        // it (Asher, 7 Nov 2026). Marked on the board too.
        if (customTiles > 0)
        {
            lines.Add(string.Empty);
            lines.Add(customTiles == 1
                ? "1 custom tile, marked on the board: it carries a second rail - cut two more bolt holes and trim a rail to fit."
                : $"{customTiles} custom tiles, marked on the board: each carries a second rail - cut two more bolt holes and trim a rail to fit.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// The board's chips: how big the plan is, in the unit the room is given
    /// in, and how long the lap is - with the track's lap time when Track
    /// Layouts has one, and never the 12s stand-in Play uses without one.
    /// </summary>
    private void RefreshChips(float lengthTiles)
    {
        _canvas.Chips = Chips(Result, lengthTiles, _lap.Seconds > 0 ? _lap.Seconds : null, _lap.Says);
    }

    /// <summary>Lights the Play speed in use.</summary>
    private void HighlightPace()
    {
        foreach (var (button, rate) in _paceButtons)
        {
            var active = Math.Abs(rate - _canvas.PaceRate) < 0.01f;

            button.BackColor = active ? AppTheme.Primary : AppTheme.SecondaryButton;
            button.ForeColor = active ? AppTheme.OnPrimary : AppTheme.TextDark;
            button.FlatAppearance.BorderSize = 0;
        }
    }

    /// <summary>
    /// Chooses the lap the board shows (RcpLapModel.Choose): raced, then as
    /// entered, then the estimate - the estimate whenever the racing line is
    /// not the one the window opened with. Play drives at it; without one,
    /// at the 12s it always used.
    /// </summary>
    private void ChooseLap()
    {
        var lineChanged = !RcpLapModel.SameLine(Result.Route, _openedLine, Result.RouteClosed);

        _lap = RcpLapModel.Choose(Result, _raced, _entered, lineChanged, _tuning, _tunedLayouts);
        _canvas.LapSeconds = _lap.Seconds > 0 ? _lap.Seconds : 12;
    }

    /// <summary>
    /// The lap chip clicked: the sector lengths along the racing line, when
    /// the plan has split loops (RcpPlan.MeasureSectors), in a small list
    /// under the chip - or why they can't be measured.
    /// </summary>
    private void ShowSectors(Point at)
    {
        foreach (var old in _sectorMenu.Items.Cast<ToolStripItem>().ToList())
        {
            old.Dispose();
        }

        _sectorMenu.Items.Clear();

        var lines = new List<string>
        {
            _lap.Source switch
            {
                RcpLapSource.Raced => $"{_lap.Seconds:0.00}s, the median of {_lap.Laps} laps raced on this layout",
                RcpLapSource.Entered => $"{_lap.Seconds:0.00}s, the average lap entered on Track Layouts",
                RcpLapSource.Estimate when _lap.Tuned > 0 => $"About {_lap.Seconds:0.0}s, estimated - tuned to {Plural.Of(_lap.Tuned, "layout")} raced here",
                RcpLapSource.Estimate => $"About {_lap.Seconds:0.0}s, estimated for a stock Mini-Z - it tunes itself once layouts are raced",
                _ => "No lap time - draw the racing line, or press Trace it for me",
            },
        };

        switch (RcpPlan.MeasureSectors(Result))
        {
            case { Sectors: { } sectors }:
                for (var i = 0; i < sectors.Length; i++)
                {
                    var metres = sectors[i] * Result.TileSizeMm / 1000.0;
                    lines.Add($"Sector {i + 1}: {metres:0.00} m / {metres * 3.28084:0.0} ft");
                }

                break;

            case { Problem: { } problem }:
                lines.Add("Sectors: " + problem);
                break;

            default:
                lines.Add("No split loops on the plan, so no sectors.");
                break;
        }

        foreach (var line in lines)
        {
            _sectorMenu.Items.Add(new ToolStripMenuItem(line) { Enabled = false });
        }

        _sectorMenu.Show(_canvas, at);
    }

    /// <summary>
    /// What the board's chips say about <paramref name="design"/> - see
    /// RefreshChips. <paramref name="lapSays"/> is where the lap time comes
    /// from ("from 42 laps raced", "as entered", "estimate"), said after it.
    /// </summary>
    internal static IReadOnlyList<string> Chips(RcpTrackDesign design, float lengthTiles, double? lapSeconds, string? lapSays = null)
    {
        var chips = new List<string>();
        var feet = design.RoomInFeet;
        var unit = feet ? "ft" : "m";

        double Length(double tiles) => tiles * design.TileSizeMm / (feet ? 304.8 : 1000.0);

        if (!design.IsEmpty)
        {
            var bounds = design.BoundsInTiles(includeRoute: false);
            var cols = (int)Math.Round(bounds.Width);
            var rows = (int)Math.Round(bounds.Height);

            chips.Add($"{Length(bounds.Width):0.0} x {Length(bounds.Height):0.0} {unit} · {cols} x {rows} tiles");
        }

        if (lengthTiles > 0)
        {
            chips.Add(lapSeconds is > 0
                ? $"Lap {Length(lengthTiles):0.0} {unit} · {lapSeconds:0.0}s" + (string.IsNullOrEmpty(lapSays) ? string.Empty : " " + lapSays)
                : $"Lap {Length(lengthTiles):0.0} {unit}");
        }
        else if (!design.IsEmpty)
        {
            chips.Add("No racing line yet");
        }

        return chips;
    }

    private void RefreshKitCheck(RcpInventory inventory)
    {
        var kit = RcpKit.ByName(Result.KitName);

        if (kit is null)
        {
            _lblKitCheck.Text = "Pick a kit to check the layout fits in the box.";
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
        //
        // 17 Oct 2026, Bug Hunt 8, A31-5: the same for the tile-size and
        // room number boxes, whose edit box has its own undo too.
        if (_txtName.ContainsFocus
            || _numTileSize.ContainsFocus
            || _numRoomWidth.ContainsFocus
            || _numRoomDepth.ContainsFocus)
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

        // The clipboard keys live here rather than on the board, so the
        // ones that need the Select tool light its button up as they take
        // it - the board cannot reach the toolbar's highlight.
        if (keyData == (Keys.Control | Keys.A))
        {
            SetTool(RcpTool.Select);
            _canvas.SelectEverything();
            return true;
        }

        if (keyData == (Keys.Control | Keys.C))
        {
            _canvas.CopySelection();
            return true;
        }

        if (keyData == (Keys.Control | Keys.X))
        {
            // Cut changes the plan: done where it can be seen.
            if (_canvas.Angled)
            {
                _canvas.ToggleAngled();
            }

            _canvas.CutSelection();
            return true;
        }

        if (keyData == (Keys.Control | Keys.V))
        {
            SetTool(RcpTool.Select);
            _canvas.PasteClipboard();
            return true;
        }

        if (keyData == (Keys.Control | Keys.D))
        {
            SetTool(RcpTool.Select);
            _canvas.DuplicateSelection();
            return true;
        }

        if (keyData == Keys.P)
        {
            TogglePanel();
            return true;
        }

        // ? - the keys, as the top bar's "?" opens them.
        if (keyData == (Keys.Shift | Keys.OemQuestion))
        {
            ShowTab(_openTab == TabKeys ? _lastTab : TabKeys, remember: false);
            return true;
        }

        if (keyData == Keys.V)
        {
            _canvas.ToggleAngled();
            return true;
        }

        if (keyData == Keys.L)
        {
            SetTool(RcpTool.Lane);
            return true;
        }

        if (keyData == Keys.C)
        {
            SetTool(RcpTool.Corner);
            return true;
        }

        // T for the tile label: the Markings tool with Label picked.
        if (keyData == Keys.T)
        {
            _canvas.Item = RcpPaletteItem.Label;
            SetTool(RcpTool.Marker);
            HighlightPalette();
            _lblStatus.Text = "Click a tile to give it a name - \"Pit lane\", \"Marshal\". Click it again to change it.";
            return true;
        }

        // 1, 2 and 3 pick the pieces (railed tiles, Phase 2, 7 Nov 2026): a
        // plain tile, a rail tile, an outer curve tile - the same keys that
        // picked Floor, Rails and Turns, which they took the place of.
        var piece = keyData switch
        {
            Keys.D1 => RcpPaletteItem.Floor,
            Keys.D2 => RcpPaletteItem.RailTile,
            Keys.D3 => RcpPaletteItem.CurveTile,
            _ => (RcpPaletteItem?)null,
        };

        if (piece is { } pickedPiece)
        {
            PickPiece(pickedPiece);
            return true;
        }

        var tool = keyData switch
        {
            Keys.D4 => RcpTool.Marker,
            Keys.D5 => RcpTool.Route,
            Keys.D6 => RcpTool.Erase,
            Keys.D7 => RcpTool.Backdrop,
            Keys.D8 => RcpTool.Select,
            _ => (RcpTool?)null,
        };

        if (tool is { } chosen)
        {
            SetTool(chosen);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>
    /// WHY (20 Oct 2026): Escape in any box on the side panel (it reaches
    /// the Cancel button from everywhere but the board) or the title bar's X
    /// used to close the designer at once and throw away everything drawn,
    /// with no question and no undo - while Clear and Start from a kit both
    /// ask. Now a close that is not Save Layout asks first when the plan,
    /// its name or the main-picture box is not as it was when the window
    /// opened, and No keeps the window open. Windows shutting down, the app
    /// exiting or the owner closing are never held up.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (ShouldAskBeforeClosing(DialogResult, e.CloseReason, e.Cancel, HasUnsavedChanges()))
        {
            var answer = AppDialog.Show(this,
                "Close the designer and lose the changes to this layout?",
                "Discard changes",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
            {
                e.Cancel = true;
                DialogResult = DialogResult.None;
            }
        }

        base.OnFormClosing(e);
    }

    /// <summary>
    /// Whether closing should ask before losing changes. Save Layout (OK)
    /// never asks. The Cancel button and Escape close a modal dialog with
    /// CloseReason.None, and the X with UserClosing - both ask; every other
    /// reason (shutdown, Task Manager, the owner or the app closing) does not.
    /// </summary>
    internal static bool ShouldAskBeforeClosing(DialogResult result, CloseReason reason, bool alreadyCancelled, bool changed) =>
        changed
        && !alreadyCancelled
        && result != DialogResult.OK
        && reason is CloseReason.UserClosing or CloseReason.None;

    private bool HasUnsavedChanges()
    {
        // A nudge still settling is part of what would be lost.
        _canvas.LandPendingNudge();

        return _unsavedOnOpen
            || !string.Equals(TrackName, _openedName, StringComparison.Ordinal)
            || _chkUseAsPhoto.Checked != _openedUseAsPhoto
            || !string.Equals(DesignSnapshot(_canvas.Design), _openedDesign, StringComparison.Ordinal);
    }

    /// <summary>
    /// The plan as text, to tell whether it changed. Compared rather than
    /// going by CanUndo, so drawing something and undoing it back to the
    /// start does not count as a change.
    /// </summary>
    internal static string DesignSnapshot(RcpTrackDesign design) =>
        System.Text.Json.JsonSerializer.Serialize(design, SnapshotOptions);

    /// <summary>
    /// WHY (20 Oct 2026): the default options throw on a NaN or infinite
    /// float, and a snapshot is taken in the constructor and on every close -
    /// so one bad photo position would stop the designer opening, or
    /// closing. Only ever compared, never read back, so any spelling of it
    /// will do.
    /// </summary>
    private static readonly System.Text.Json.JsonSerializerOptions SnapshotOptions = new()
    {
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

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

        /// <summary>
        /// A chip for the bar over the board (8 Nov 2026): the picture
        /// beside the name on one line, as tall as a button and as wide as
        /// its name needs, the hint left to its tooltip.
        /// </summary>
        public bool Compact { get; }

        public PaletteButton(RcpPaletteItem item, bool compact = false)
        {
            Item = item;
            Compact = compact;
            _sample = RcpTiles.Sample(item);

            if (compact)
            {
                using var probe = new Button { Text = "Ag", Font = AppTheme.BodyFont };
                var height = UiLayout.TightButtonSizeFor(probe).Height;
                var nameWidth = TextRenderer.MeasureText(RcpTiles.NameOf(item), AppTheme.BodyFont).Width;

                Size = new Size(CompactEdge + (height - 2 * CompactInset) + UiLayout.AtTextSize(6) + nameWidth + CompactEdge, height);
            }

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

            if (Compact)
            {
                PaintCompact(g);
                return;
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

        private static int CompactInset => UiLayout.AtTextSize(4);
        private static int CompactEdge => UiLayout.AtTextSize(6);

        /// <summary>The chip: the picture at the left, the name beside it, both on the middle line.</summary>
        private void PaintCompact(Graphics g)
        {
            var size = Math.Max(1, Height - 2 * CompactInset);
            var origin = new PointF(CompactEdge, CompactInset);

            var state = g.Save();
            g.SetClip(new RectangleF(origin.X - 2, origin.Y - 2, size + 4, size + 4));

            RcpTrackPainter.Draw(g, _sample, size, origin, showRoute: false, showLabels: false, detailThreshold: 20f);

            g.Restore(state);

            var textLeft = (int)(origin.X + size + UiLayout.AtTextSize(6));

            TextRenderer.DrawText(g, RcpTiles.NameOf(Item), AppTheme.BodyFont,
                new Rectangle(textLeft, 0, Math.Max(1, Width - textLeft - CompactEdge / 2), Height),
                AppTheme.TextDark,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
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

    /// <summary>
    /// A section's header in the inspector (8 Nov 2026): its name in bold
    /// at the left, and - closed - its headline after it, muted, cut short
    /// with "..." where the column ends. Drawn rather than a button's
    /// caption, since the headline is meant to be cut short and a button
    /// whose caption does not fit is a fault (LayoutAudit). Clicking it
    /// opens or closes the section.
    /// </summary>
    private sealed class SectionHeader : Control
    {
        private string _headline = string.Empty;
        private bool _hovered;

        public SectionHeader(string title)
        {
            Text = title;

            SetStyle(ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);

            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            TabStop = false;
            Tag = AppTheme.KeepBackColor;

            using var probe = new Button { Text = title, Font = AppTheme.BoldFont };
            Height = UiLayout.ButtonSizeFor(probe).Height;
        }

        /// <summary>What it says after its name; empty for none.</summary>
        public string Headline
        {
            get => _headline;
            set
            {
                if (_headline != value)
                {
                    _headline = value;
                    Invalidate();
                }
            }
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

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var page = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
            {
                g.FillRectangle(page, ClientRectangle);
            }

            var lit = BackColor == AppTheme.Primary;
            var fill = _hovered && !lit ? ControlPaint.Dark(BackColor, 0.05f) : BackColor;

            using (var brush = new SolidBrush(fill))
            using (var path = AppTheme.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), AppTheme.CornerRadius))
            {
                g.FillPath(brush, path);
            }

            var edge = UiLayout.AtTextSize(12);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix
                        | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

            var titleWidth = TextRenderer.MeasureText(g, Text, AppTheme.BoldFont, Size.Empty, flags).Width;
            TextRenderer.DrawText(g, Text, AppTheme.BoldFont, new Rectangle(edge, 0, Math.Max(1, Width - 2 * edge), Height), ForeColor, flags);

            if (_headline.Length == 0)
            {
                return;
            }

            var left = edge + titleWidth + UiLayout.AtTextSize(10);

            TextRenderer.DrawText(g, _headline, AppTheme.SmallFont,
                new Rectangle(left, 0, Math.Max(1, Width - left - edge), Height),
                lit ? AppTheme.OnPrimary : AppTheme.TextMuted,
                flags | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>
    /// The bar over the board (8 Nov 2026): one row of options for each
    /// tool, only the tool in hand's showing. It is as tall as the tallest
    /// row at its width, whichever is showing, so picking a tool never
    /// moves the board under it; each row wraps at the bar's width.
    /// </summary>
    private sealed class ContextStrip : Panel
    {
        private readonly List<(string Key, Control Bar)> _bars = new();

        public ContextStrip()
        {
            LayoutFreeze.Adopt(this);

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Tag = AppTheme.KeepBackColor;
        }

        /// <summary>Adds a tool's row, hidden until ShowBar asks for it.</summary>
        public void AddBar(string key, Control bar)
        {
            // Sized here, to the bar's width, so it wraps there rather than
            // growing to one long line.
            bar.AutoSize = false;
            bar.Margin = Padding.Empty;
            bar.Visible = _bars.Count == 0;

            _bars.Add((key, bar));
            Controls.Add(bar);
        }

        /// <summary>Shows the row for <paramref name="key"/> and hides the others.</summary>
        public void ShowBar(string key)
        {
            foreach (var (barKey, bar) in _bars)
            {
                bar.Visible = barKey == key;
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width;

            return new Size(width, Tallest(width));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            foreach (var (_, bar) in _bars)
            {
                var bounds = new Rectangle(0, 0, ClientSize.Width, bar.GetPreferredSize(new Size(Math.Max(1, ClientSize.Width), 0)).Height);

                if (bar.Bounds != bounds)
                {
                    bar.Bounds = bounds;
                }
            }

            base.OnLayout(levent);
        }

        private int Tallest(int width) =>
            _bars.Count == 0 ? 0 : _bars.Max(pair => pair.Bar.GetPreferredSize(new Size(Math.Max(1, width), 0)).Height);
    }
}
