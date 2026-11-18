using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// STREAM OVERLAYS, YOUR OWN (14 Nov 2026, stream overlay plan section 4,
/// Asher: "the ability to roll your own"). Opened from the Web Server page's
/// Stream overlay row.
///
/// THE STUDIO (17 Nov 2026, plans/overlay-editor-mockups.html, option A,
/// with B's part-of-the-night dots and C's Pick a look). Three panes: the
/// layout's graphics down the left, the live picture in the middle, the
/// picked graphic's settings on the right.
///
///   The picture is the real overlay page (OverlayPreview), running the
///   demo race held at the part of the night the tabs above it pick. Drag
///   a graphic to move it, drag the pink dot to resize it, arrow keys nudge
///   it. "Play the night" lets the demo run from between races to the
///   result, lighting each tab as it gets there.
///
///   The list shows all sixteen graphics: an eye to turn each on and off,
///   and four dots for when it shows - grid, racing, result, between -
///   with the part the picture is showing ringed.
///
///   The settings: nine places to put it (or Usual place, where the page
///   has always put it), Across, Down, Size and - for the positions only -
///   Drivers; when it shows (Auto, or the parts ticked); and the settings
///   for the whole layout in a card of their own.
///
/// A PRESET COPIES ITSELF ON THE FIRST CHANGE. Nothing is greyed out: the
/// first change to a preset goes into "&lt;name&gt; (our copy)", a strip says
/// so, and Undo takes the copy away again. The preset never changes.
///
/// NOTHING IS STUCK AT THE USUAL PLACE. Moving a graphic that is where the
/// page puts it - a drag, a nudge, a number - first puts it in the one of
/// the nine places it was already in (UsualPlace), so it moves from where
/// it is rather than the boxes sitting disabled until a place is picked.
///
/// CHANGES SAVE AS THEY ARE MADE (OverlayStore.Update), and an open overlay
/// page redraws when its layout changes - so a club editing with OBS open
/// sees each change on the stream there and then. That is also why there is
/// no OK and Cancel, only Close.
/// </summary>
public sealed class OverlayEditorForm : Form
{
    private readonly OverlayStore _store;
    private readonly Func<string, string> _addressOf;

    private readonly ComboBox _cmbLayouts = new();
    private readonly Button _btnLook = new() { Text = "Pick a look..." };
    private readonly Button _btnRename = new() { Text = "Rename..." };
    private readonly Button _btnDelete = new() { Text = "Delete" };
    private readonly Button _btnUndo = new() { Text = "Undo" };
    private readonly Button _btnRedo = new() { Text = "Redo" };
    private readonly TextBox _txtAddress = new() { ReadOnly = true };
    private readonly Button _btnCopyAddress = new() { Text = "Copy" };
    private readonly Button _btnBrowser = new() { Text = "Open in browser" };
    private readonly Button _btnObsHelp = new() { Text = "Add it to OBS..." };

    private readonly Label _lblNotice = new() { AutoSize = true };
    private readonly Button _btnNoticeUndo = new() { Text = "Undo" };
    private readonly Button _btnNoticeOk = new() { Text = "OK" };
    private readonly TableLayoutPanel _notice;

    private readonly OverlayLayersList _lstLayers = new();

    private readonly Button[] _btnParts =
    {
        new() { Text = "The grid" }, new() { Text = "Racing" }, new() { Text = "The result" }, new() { Text = "Between races" },
    };

    private readonly Button _btnPlay = new() { Text = "Play the night" };
    private readonly CheckBox _chkGhosts = new ThemedCheckBox { Text = "Show hidden ones", Checked = true };
    private readonly CheckBox _chkPhone = new ThemedCheckBox { Text = "As on a phone" };
    private readonly OverlayPreview _preview = new();

    /// <summary>Under the picture: what covers what, and how to move things.</summary>
    private Label _lblClash => _preview.Clash;
    private Label _lblHint => _preview.Hint;

    private readonly Label _lblWhat = new() { AutoSize = true };
    private readonly Label _lblWhatNote = new() { AutoSize = true };
    private readonly NinePlaces _nine = new();
    private readonly Button _btnUsual = new() { Text = "Usual place" };
    private readonly NumericUpDown _numX = new();
    private readonly NumericUpDown _numY = new();
    private readonly NumericUpDown _numScale = new();
    private readonly NumericUpDown _numRows = new();
    private readonly FieldGrid _rowsGrid;
    private readonly Label _lblRows;
    private readonly Label _lblRowsHint;
    /// <summary>When it shows: Auto, then the four parts of the night, as pill buttons (TogglePills).</summary>
    private readonly TogglePills _pillsShows = new();
    private const string AutoPill = "auto";
    private readonly Label _lblShowsHint = new() { AutoSize = true };
    private readonly NumericUpDown _numGlass = new();
    private readonly NumericUpDown _numTextScale = new();
    private readonly Button _btnBasics = new() { Text = "Basics cards..." };

    /// <summary>The club's settings, for the basics cards - null leaves that button off.</summary>
    private readonly AppSettings? _settings;

    /// <summary>Each change's layout as it was before, newest last, for Undo. This window's changes only.</summary>
    private readonly List<OverlayLayout> _undo = new();

    /// <summary>What Undo took back, newest last, for Redo. Emptied by any new change.</summary>
    private readonly List<OverlayLayout> _redo = new();

    /// <summary>The copies this window made of a preset on its first change, and the preset each came from - Undo with nothing else to take back removes the copy.</summary>
    private readonly Dictionary<string, string> _copiedFrom = new(StringComparer.Ordinal);

    /// <summary>Waits a moment after a change before reopening the picture, so a spinner held down does not reload it at every step.</summary>
    private readonly System.Windows.Forms.Timer _reload = new() { Interval = 150 };

    /// <summary>Set while the controls are being filled, so filling them is not taken as an edit.</summary>
    private bool _filling;

    private OverlayElementKind _kind = OverlayElementKind.Tower;
    private OverlayDuring _part = OverlayDuring.Racing;
    private bool _playing;
    private bool _gallery;

    private static readonly OverlayElementKind[] Kinds = Enum.GetValues<OverlayElementKind>();

    /// <summary>What each graphic is called here - the words a club would use, not the page's names.</summary>
    internal static string KindWords(OverlayElementKind kind) => kind switch
    {
        OverlayElementKind.Tower => "Positions down the side (the pylon)",
        OverlayElementKind.Battle => "Battle box",
        OverlayElementKind.Fastest => "Fastest lap",
        OverlayElementKind.Moment => "Lead changes and milestones",
        OverlayElementKind.Grid => "The grid",
        OverlayElementKind.Podium => "The podium",
        OverlayElementKind.Champ => "Championship if it ended now",
        OverlayElementKind.Captions => "Captions (what the announcer says)",
        OverlayElementKind.Status => "Race and clock",
        OverlayElementKind.Ticker => "Positions along the bottom",
        OverlayElementKind.Bug => "Corner bug",
        OverlayElementKind.Sponsor => "Sponsor logo",
        OverlayElementKind.ClubLogo => "Club logo",
        OverlayElementKind.Schedule => "Up next",
        OverlayElementKind.Basics => "New to RC racing?",
        OverlayElementKind.Mark => "HeatCaller's mark (always on)",
        _ => kind.ToString(),
    };

    /// <summary>
    /// The parts of the night a graphic left on Auto shows in - when the
    /// overlay page has something for it to show (WebServerService.Camera's
    /// draw(): the champ table only while racing, the ticker on track, the
    /// bug from the grid on). For the list's dots.
    /// </summary>
    internal static OverlayDuring NaturalParts(OverlayElementKind kind) => kind switch
    {
        OverlayElementKind.Tower or OverlayElementKind.Ticker => OverlayDuring.Racing | OverlayDuring.Finished,
        OverlayElementKind.Battle or OverlayElementKind.Fastest or OverlayElementKind.Moment or OverlayElementKind.Champ => OverlayDuring.Racing,
        OverlayElementKind.Grid => OverlayDuring.Staging,
        OverlayElementKind.Podium => OverlayDuring.Finished,
        OverlayElementKind.Status => OverlayDuring.Staging | OverlayDuring.Racing | OverlayDuring.Finished,
        OverlayElementKind.Schedule or OverlayElementKind.Basics => OverlayDuring.Between,
        _ => OverlayDuring.Staging | OverlayDuring.Racing | OverlayDuring.Finished | OverlayDuring.Between,
    };

    /// <summary>
    /// Which of the nine places a graphic at its usual place is already in,
    /// and how far in it sits there - the page's own CSS for it, in vw. A
    /// graphic moved from its usual place starts from here.
    /// </summary>
    internal static (OverlayAnchor Anchor, double X, double Y) UsualPlace(OverlayElementKind kind) => kind switch
    {
        OverlayElementKind.Tower => (OverlayAnchor.TopLeft, 2, 3),
        OverlayElementKind.Champ => (OverlayAnchor.TopLeft, 2, 3),
        OverlayElementKind.Battle => (OverlayAnchor.Bottom, 0, 3),
        OverlayElementKind.Fastest => (OverlayAnchor.BottomRight, 2, 3),
        OverlayElementKind.Moment => (OverlayAnchor.Top, 0, 3),
        OverlayElementKind.Grid or OverlayElementKind.Podium or OverlayElementKind.Schedule => (OverlayAnchor.Middle, 0, 0),
        OverlayElementKind.Captions => (OverlayAnchor.Bottom, 0, 1),
        OverlayElementKind.Ticker => (OverlayAnchor.Bottom, 0, 2),
        OverlayElementKind.Basics => (OverlayAnchor.Bottom, 0, 3),
        OverlayElementKind.ClubLogo => (OverlayAnchor.TopLeft, 2, 2),
        OverlayElementKind.Mark => (OverlayAnchor.BottomRight, 2, 2),
        _ => (OverlayAnchor.TopRight, 2, 2),
    };

    private static readonly (OverlayDuring Part, string Name, string At)[] PartsOfTheNight =
    {
        // Where in the demo's loop each part is (the page's &hold= and &at=):
        // the racing one starts just before the lead change, so the
        // battle box and the banner are up to be placed.
        (OverlayDuring.Staging, "staging", "22"),
        (OverlayDuring.Racing, "racing", "95"),
        (OverlayDuring.Finished, "finished", "142"),
        (OverlayDuring.Between, "between", "2"),
    };

    public OverlayEditorForm(OverlayStore store, Func<string, string> addressOf, string? pick = null, AppSettings? clubSettings = null)
    {
        _store = store;
        _addressOf = addressOf;
        _settings = clubSettings;

        Text = "Stream overlays";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        // --- The top: the layout, and its address for OBS ---
        _cmbLayouts.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbLayouts.Width = UiLayout.AtTextSize(280);
        _cmbLayouts.SelectedIndexChanged += (_, _) =>
        {
            if (!_filling)
            {
                _gallery = false;
                ShowLayout();
            }
        };
        _btnLook.Click += (_, _) => ShowGallery();
        _btnRename.Click += (_, _) => RenameLayout();
        _btnDelete.Click += (_, _) => DeleteLayout();
        _btnUndo.Click += (_, _) => UndoLast();
        _btnRedo.Click += (_, _) => RedoLast();
        _txtAddress.Width = UiLayout.AtTextSize(330);
        _btnCopyAddress.Click += (_, _) => CopyAddress();
        _btnBrowser.Click += (_, _) =>
        {
            if (_txtAddress.Text.Length > 0)
            {
                AppSupport.Open(_txtAddress.Text + "?demo=1");
            }
        };
        _btnObsHelp.Click += (_, _) => AppDialog.Show(this,
            "In OBS: Sources, then +, then Browser. Call it \"HeatCaller\".\n\n"
            + "Paste the address from here into URL" + (_txtAddress.Text.Length > 0 ? " (" + _txtAddress.Text + ")" : string.Empty) + ".\n\n"
            + "Set Width to 1920 and Height to 1080, and leave \"Shutdown source when not visible\" unticked.\n\n"
            + "A change you make here shows in OBS straight away - there is nothing to paste again.",
            "Add it to OBS");

        _lblNotice.ForeColor = AppTheme.Warning;
        _btnNoticeUndo.Click += (_, _) => UndoLast();
        _btnNoticeOk.Click += (_, _) => _notice!.Visible = false;
        // One strip: the words wrap in the room left of the two buttons.
        var strip = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty };
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _lblNotice.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _btnNoticeUndo.AutoSize = true;
        _btnNoticeOk.AutoSize = true;
        _btnNoticeUndo.Anchor = AnchorStyles.None;
        _btnNoticeOk.Anchor = AnchorStyles.None;
        strip.Controls.Add(_lblNotice, 0, 0);
        strip.Controls.Add(_btnNoticeUndo, 1, 0);
        strip.Controls.Add(_btnNoticeOk, 2, 0);
        _notice = strip;
        _notice.Visible = false;

        // --- The graphics down the left ---
        _lstLayers.StateOf = StateOf;
        _lstLayers.SelectedIndexChanged += (_, _) =>
        {
            if (_lstLayers.Picked is { } kind && kind != _kind)
            {
                _kind = kind;
                ShowElement();
                TellPictureWhichIsPicked();
            }
        };
        _lstLayers.EyeClicked += kind =>
        {
            // The mark can be moved but never turned off.
            if (kind == OverlayElementKind.Mark)
            {
                return;
            }

            var on = Current?.Element(kind) is { On: true };
            _kind = kind;
            Edit(layout => ElementOf(layout, kind).On = !on);
        };

        // --- The picture in the middle ---
        for (var i = 0; i < _btnParts.Length; i++)
        {
            var part = PartsOfTheNight[i].Part;
            _btnParts[i].Click += (_, _) =>
            {
                _playing = false;
                _part = part;
                ShowParts();
                ReloadPicture();
            };
        }

        _btnPlay.Click += (_, _) =>
        {
            _playing = !_playing;
            ShowParts();
            ReloadPicture();
        };
        _chkGhosts.CheckedChanged += (_, _) => ReloadPicture();
        _chkPhone.CheckedChanged += (_, _) => ReloadPicture();
        _lblClash.ForeColor = AppTheme.Danger;
        _lblHint.ForeColor = AppTheme.TextMuted;
        _preview.Message += OnPictureMessage;
        _reload.Tick += (_, _) =>
        {
            _reload.Stop();
            ShowPicture();
        };

        // --- The picked graphic, on the right ---
        _lblWhat.Font = new Font(AppTheme.BodyFont, FontStyle.Bold);
        _lblWhatNote.ForeColor = AppTheme.TextMuted;
        _nine.Picked += anchor => EditElement(e =>
        {
            e.Anchor = anchor;

            // Out of the usual place into one of the nine: from its edge,
            // as the usual place had it, rather than numbers left over.
            if (anchor != OverlayAnchor.Default && (Math.Abs(e.X) > 20 || Math.Abs(e.Y) > 20))
            {
                e.X = 2;
                e.Y = 2;
            }
        });
        _btnUsual.Click += (_, _) => EditElement(e => e.Anchor = OverlayAnchor.Default);

        Spinner(_numX, -50, 50, 0.5m, 1);
        Spinner(_numY, -50, 50, 0.5m, 1);
        Spinner(_numScale, 50, 200, 5, 0);
        Spinner(_numRows, 0, 20, 1, 0);
        Spinner(_numGlass, 40, 100, 5, 0);
        Spinner(_numTextScale, 80, 160, 5, 0);
        _numX.ValueChanged += (_, _) => EditElement(e => { OutOfUsualPlace(e); e.X = (double)_numX.Value; });
        _numY.ValueChanged += (_, _) => EditElement(e => { OutOfUsualPlace(e); e.Y = (double)_numY.Value; });
        _numScale.ValueChanged += (_, _) => EditElement(e => { OutOfUsualPlace(e); e.Scale = (double)_numScale.Value / 100; });
        _numRows.ValueChanged += (_, _) => EditElement(e => e.Rows = (int)_numRows.Value);
        _numGlass.ValueChanged += (_, _) => Edit(layout => layout.Glass = (double)_numGlass.Value / 100);
        _numTextScale.ValueChanged += (_, _) => Edit(layout => layout.TextScale = (double)_numTextScale.Value / 100);
        _pillsShows.SetPills(new[] { new TogglePills.Pill(AutoPill, "Auto") }
            .Concat(PartsOfTheNight.Select(p => new TogglePills.Pill(p.Name, PartWords(p.Part)))).ToList());
        _pillsShows.Clicked += ShowsPillClicked;

        _lblShowsHint.ForeColor = AppTheme.TextMuted;
        _btnBasics.Click += (_, _) => EditBasics();
        _btnBasics.Visible = clubSettings is not null;
        _btnBasics.AutoSize = true;

        // --- Layout ---
        var place = new FieldGrid();
        place.AddRow("Across:", _numX);
        place.AddRow("Down:", _numY);
        place.AddRow("Size (%):", _numScale);
        // Drivers in a grid of its own, hidden whole for the graphics it means nothing to.
        _rowsGrid = new FieldGrid();
        _lblRows = _rowsGrid.AddRow("Drivers:", _numRows);
        _lblRowsHint = _rowsGrid.AddHint("How many drivers it lists. 0 is its usual number.");

        var graphic = new Section("The picked graphic");
        graphic.Add(_lblWhat, gapBelow: 0);
        graphic.Add(_lblWhatNote, gapBelow: UiLayout.RowGap);
        graphic.Add(FieldGrid.Inline(_nine, _btnUsual), gapBelow: UiLayout.RowGap);
        graphic.Add(place, gapBelow: 0);
        graphic.Add(_rowsGrid, gapBelow: UiLayout.RowGap);
        graphic.Add(new Label { Text = "When it shows", AutoSize = true, ForeColor = AppTheme.TextMuted, Padding = new Padding(0, UiLayout.RowGap, 0, 0) }, gapBelow: UiLayout.AtDpi(4));
        graphic.Add(_pillsShows, gapBelow: UiLayout.RowGap);
        graphic.Add(_lblShowsHint, gapBelow: 0);

        var whole = new FieldGrid();
        whole.AddRow("Boxes (% solid):", _numGlass);
        whole.AddRow("Text size (%):", _numTextScale);
        whole.AddHint("Every graphic bigger or smaller at once - bigger reads better on a phone.");

        var wholeCard = new Section("The whole layout");
        wholeCard.Add(whole, gapBelow: UiLayout.RowGap);
        wholeCard.Add(_btnBasics, stretch: false, gapBelow: 0);

        _chkGhosts.AutoSize = true;
        _chkPhone.AutoSize = true;
        var partsRow = FieldGrid.Inline(_btnParts[0], _btnParts[1], _btnParts[2], _btnParts[3], _btnPlay, _chkGhosts, _chkPhone);

        var middle = new Beside(() => UiLayout.AtTextSize(300));
        middle.Add(partsRow, gapBelow: UiLayout.RowGap);
        middle.AddFill(_preview, width => Math.Max(UiLayout.AtTextSize(180), width * 9 / 16) + AppTheme.BodyFont.Height * 3 + UiLayout.RowGap, gapBelow: UiLayout.RowGap);
        middle.AddSideCard(graphic, gapBelow: UiLayout.RowGap);
        middle.AddSideCard(wholeCard, gapBelow: 0);

        var columns = new Beside(() => UiLayout.AtTextSize(270)) { SideOnLeft = true };
        columns.AddFill(middle, middle.LeastHeight);
        columns.AddSideFill(_lstLayers, _ => _lstLayers.ItemHeight * 8 + 4, gapBelow: 0);

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        var page = new FillPage();
        page.Add(FieldGrid.Inline(new Label { Text = "Layout:" }, _cmbLayouts, _btnLook, _btnRename, _btnDelete, _btnUndo, _btnRedo), gapBelow: UiLayout.RowGap);
        page.Add(FieldGrid.Inline(new Label { Text = "For OBS:" }, _txtAddress, _btnCopyAddress, _btnBrowser, _btnObsHelp), gapBelow: UiLayout.RowGap);
        page.Add(_notice, gapBelow: UiLayout.RowGap);
        page.AddFill(columns, columns.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.StyleButton(_btnCopyAddress, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(_btnObsHelp, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnLook, AppTheme.ButtonKind.Secondary);
        AppTheme.ApplyRecursively(this);
        _lstLayers.BackColor = AppTheme.Surface;
        _lstLayers.ForeColor = AppTheme.TextDark;

        FillLayouts(pick);

        // A club with no layouts of its own yet starts by picking a look.
        _gallery = pick is null && _store.Layouts.Count == 0;
        ShowParts();
        DialogFit.Keep(this, page, width: 1320, height: 820);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // The picture's engine starts only once the window is on screen.
        ShowPicture();
        _preview.Start();
    }

    private static void Spinner(NumericUpDown box, decimal least, decimal most, decimal step, int places)
    {
        box.Minimum = least;
        box.Maximum = most;
        box.Increment = step;
        box.DecimalPlaces = places;
        box.Width = UiLayout.AtTextSize(UiLayout.NumericWidth);
    }

    /// <summary>A layout in the list: the presets, then the club's own.</summary>
    private sealed record Item(OverlayLayout Layout, bool Preset)
    {
        public override string ToString() => Preset ? Layout.Name + " (preset)" : Layout.Name;
    }

    private void FillLayouts(string? pick)
    {
        _filling = true;

        try
        {
            _cmbLayouts.BeginUpdate();
            _cmbLayouts.Items.Clear();

            foreach (var preset in OverlayLayouts.BuiltIns.Where(l => l.Id != OverlayLayouts.ResultsId))
            {
                _cmbLayouts.Items.Add(new Item(preset, Preset: true));
            }

            foreach (var mine in _store.Layouts)
            {
                _cmbLayouts.Items.Add(new Item(mine, Preset: false));
            }

            _cmbLayouts.EndUpdate();

            // Wide enough for the longest name, within reason.
            var widest = _cmbLayouts.Items.Cast<object>().Select(i => TextRenderer.MeasureText(i.ToString(), _cmbLayouts.Font).Width).DefaultIfEmpty(0).Max();
            _cmbLayouts.Width = Math.Clamp(widest + UiLayout.AtTextSize(40), UiLayout.AtTextSize(280), UiLayout.AtTextSize(460));

            var at = _cmbLayouts.Items.Cast<Item>().ToList().FindIndex(i => i.Layout.Id == pick);
            _cmbLayouts.SelectedIndex = at >= 0 ? at : 0;
        }
        finally
        {
            _filling = false;
        }

        ShowLayout();
    }

    private Item? Picked => _cmbLayouts.SelectedItem as Item;

    /// <summary>The picked layout as it is now: a preset as built, the club's own fresh from the store.</summary>
    private OverlayLayout? Current => Picked is not { } item ? null : item.Preset ? item.Layout : _store.Get(item.Layout.Id) ?? item.Layout;

    private (bool On, OverlayDuring Shows) StateOf(OverlayElementKind kind)
    {
        var e = Current?.Element(kind);

        if (e is not { On: true })
        {
            return (false, 0);
        }

        return (true, e.During == OverlayDuring.Always ? NaturalParts(kind) : e.During);
    }

    private void ShowLayout()
    {
        var item = Picked;
        var layout = Current;

        _filling = true;

        try
        {
            _numGlass.Value = (decimal)Math.Round(Math.Clamp(layout?.Glass ?? 0.82, 0.4, 1) * 100);
            _numTextScale.Value = (decimal)Math.Round(Math.Clamp(layout?.TextScale ?? 1, 0.8, 1.6) * 100);
            _txtAddress.Text = layout is null ? string.Empty : _addressOf(layout.Id);

            if (_lstLayers.SelectedIndex != Array.IndexOf(Kinds, _kind))
            {
                _lstLayers.SelectedIndex = Array.IndexOf(Kinds, _kind);
            }
        }
        finally
        {
            _filling = false;
        }

        var own = item is { Preset: false };
        _btnRename.Enabled = own;
        _btnDelete.Enabled = own;
        _btnUndo.Enabled = item is not null && (_undo.Any(u => u.Id == item.Layout.Id) || _copiedFrom.ContainsKey(item.Layout.Id));
        _btnRedo.Enabled = item is not null && _redo.Any(u => u.Id == item.Layout.Id);
        _btnCopyAddress.Enabled = _txtAddress.Text.Length > 0;
        _btnBrowser.Enabled = _txtAddress.Text.Length > 0;
        _preview.BrowserAddress = _txtAddress.Text.Length > 0 ? _txtAddress.Text + "?demo=1" : string.Empty;

        // Until the picture measures them, the overlaps worked out from the layout.
        var warnings = layout is null ? Array.Empty<string>() : OverlayOverlap.Warnings(layout, KindWords);
        if (!_preview.Live)
        {
            _lblClash.Text = string.Join(" ", warnings.Take(3));
        }

        _lstLayers.Invalidate();
        ShowElement();
    }

    private void ShowElement()
    {
        var layout = Current;
        var kind = _kind;
        var element = layout?.Element(kind);
        var on = element is { On: true };
        var usual = element is null || element.Anchor == OverlayAnchor.Default;
        var (usualAnchor, usualX, usualY) = UsualPlace(kind);

        _filling = true;

        try
        {
            _lblWhat.Text = KindWords(kind).Replace(" (always on)", string.Empty, StringComparison.Ordinal);
            _lblWhatNote.Text = !on
                ? "Off in this layout. Click its eye in the list to turn it on."
                : usual ? "At its usual place. Drag it, or pick one of the nine places."
                : "Moved by this layout.";
            _nine.Anchor9 = usual ? OverlayAnchor.Default : element!.Anchor;
            _nine.Usual = usualAnchor;
            _numX.Value = (decimal)Math.Clamp(usual ? usualX : element!.X, -50, 50);
            _numY.Value = (decimal)Math.Clamp(usual ? usualY : element!.Y, -50, 50);
            _numScale.Value = (decimal)Math.Round(Math.Clamp(usual ? 1 : element!.Scale, 0.5, 2) * 100);
            _numRows.Value = Math.Clamp(element?.Rows ?? 0, 0, 20);
            var during = element?.During ?? OverlayDuring.Always;
            var auto = during == OverlayDuring.Always;
            var shows = auto ? NaturalParts(kind) : during;
            _pillsShows.SetState(AutoPill, auto ? TogglePills.State.On : TogglePills.State.Off);
            foreach (var (part, name, _) in PartsOfTheNight)
            {
                _pillsShows.SetState(name, !shows.HasFlag(part) ? TogglePills.State.Off : auto ? TogglePills.State.Auto : TogglePills.State.On);
            }

            _lblShowsHint.Text = auto
                ? "Whenever it has something to show - the dashed ones are when that is. Click a part to choose for yourself."
                : "Only in the parts in pink. Auto hands it back.";
        }
        finally
        {
            _filling = false;
        }

        // A preset is editable too: its first change makes the club's copy.
        var editable = on;
        _nine.Enabled = editable;
        _btnUsual.Enabled = editable && !usual;
        var at = usual ? usualAnchor : element!.Anchor;
        _numX.Enabled = editable && at is not (OverlayAnchor.Top or OverlayAnchor.Middle or OverlayAnchor.Bottom);
        _numY.Enabled = editable && at is not (OverlayAnchor.Left or OverlayAnchor.Middle or OverlayAnchor.Right);
        _numScale.Enabled = editable;

        var rows = kind is OverlayElementKind.Tower or OverlayElementKind.Ticker;
        _rowsGrid.Visible = rows;
        _numRows.Enabled = editable && rows;

        var choosable = editable && kind != OverlayElementKind.Mark;
        _pillsShows.Enabled = choosable;
    }

    /// <summary>The part-of-the-night buttons: the one the picture shows filled, and Play's caption.</summary>
    private void ShowParts()
    {
        for (var i = 0; i < _btnParts.Length; i++)
        {
            AppTheme.StyleButton(_btnParts[i], PartsOfTheNight[i].Part == _part ? AppTheme.ButtonKind.Primary : AppTheme.ButtonKind.Secondary);
        }

        _btnPlay.Text = _playing ? "Stop playing" : "Play the night";
        _lstLayers.Showing = _part;
        _lstLayers.Invalidate();
    }

    /// <summary>A part's name on its pill and its tab: "The grid", "Racing", "The result", "Between races".</summary>
    private static string PartWords(OverlayDuring part) => part switch
    {
        OverlayDuring.Staging => "The grid",
        OverlayDuring.Racing => "Racing",
        OverlayDuring.Finished => "The result",
        _ => "Between races",
    };

    /// <summary>
    /// A pill clicked. Auto on: back to whenever it has something to show.
    /// Auto off: the parts it was showing in, so nothing on the stream
    /// changes until a part is clicked. A part: on or off - from Auto, out
    /// of the parts Auto had. The last part can't go: none would mean
    /// "always", the opposite of what was clicked; the eye turns it off.
    /// </summary>
    internal void ShowsPillClicked(string key)
    {
        if (Current?.Element(_kind) is not { On: true } element)
        {
            return;
        }

        var auto = element.During == OverlayDuring.Always;
        var shows = auto ? NaturalParts(_kind) : element.During;

        if (key == AutoPill)
        {
            EditElement(e => e.During = auto ? NaturalParts(_kind) : OverlayDuring.Always);
            return;
        }

        if (PartsOfTheNight.FirstOrDefault(p => p.Name == key) is not { Name: not null } picked)
        {
            return;
        }

        var after = shows ^ picked.Part;

        if (after == OverlayDuring.Always)
        {
            _lblShowsHint.Text = "It has to show some time - to take it off the stream, click its eye in the list.";
            return;
        }

        EditElement(e => e.During = after);
    }

    // --- The picture ---

    /// <summary>Reopens the picture in a moment - after a change, or a new part of the night.</summary>
    private void ReloadPicture()
    {
        _reload.Stop();
        _reload.Start();
    }

    /// <summary>The overlay page as the picture shows it: edit mode, the demo held at the part picked (or playing), the picked graphic outlined.</summary>
    internal string PictureAddress()
    {
        var layout = Current;
        var address = layout is null ? string.Empty : _addressOf(layout.Id);

        if (address.Length == 0)
        {
            return string.Empty;
        }

        if (_gallery)
        {
            return Uri.TryCreate(address, UriKind.Absolute, out var uri)
                ? uri.GetLeftPart(UriPartial.Authority) + WebServerService.OverlayGalleryPath
                : string.Empty;
        }

        var part = PartsOfTheNight.First(p => p.Part == _part);
        var demo = _playing ? "demo=1&at=0" : "demo=1&hold=" + part.Name + "&at=" + part.At;

        if (_chkPhone.Checked)
        {
            return address + "?phone=1&" + demo;
        }

        return address + "?" + demo + "&edit=1&sel=" + OverlayLayouts.ElementName(_kind) + (_chkGhosts.Checked ? string.Empty : "&ghosts=0");
    }

    private void ShowPicture() => _preview.Show(PictureAddress());

    private void ShowGallery()
    {
        _gallery = true;
        _playing = false;
        ShowParts();
        ShowPicture();
    }

    private void TellPictureWhichIsPicked() => _preview.Post(new { t = "select", k = OverlayLayouts.ElementName(_kind) });

    private static OverlayElementKind? KindNamed(string? name) =>
        Kinds.Cast<OverlayElementKind?>().FirstOrDefault(k => OverlayLayouts.ElementName(k!.Value) == name);

    /// <summary>What the overlay page says: a graphic picked, placed, the overlaps it measured, the part the demo is in, a look picked.</summary>
    private void OnPictureMessage(JsonElement m)
    {
        var t = m.TryGetProperty("t", out var tv) && tv.ValueKind == JsonValueKind.String ? tv.GetString() : null;

        switch (t)
        {
            case "pick":
                if (KindNamed(Str(m, "k")) is { } picked && picked != _kind)
                {
                    _kind = picked;
                    _lstLayers.SelectedIndex = Array.IndexOf(Kinds, picked);
                    ShowElement();
                }

                break;

            case "place":
                if (KindNamed(Str(m, "k")) is { } kind && Enum.TryParse<OverlayAnchor>(Str(m, "anchor"), out var anchor) && anchor != OverlayAnchor.Default
                    && Number(m, "x") is { } x && Number(m, "y") is { } y && Number(m, "scale") is { } scale)
                {
                    _kind = kind;
                    Edit(layout =>
                    {
                        var e = ElementOf(layout, kind);
                        e.Anchor = anchor;
                        e.X = Math.Clamp(x, -50, 50);
                        e.Y = Math.Clamp(y, -50, 50);
                        e.Scale = Math.Clamp(scale, 0.5, 2);
                    });
                }

                break;

            case "clash":
                _lblClash.Text = ClashWords(m);
                break;

            case "part":
                if (_playing && PartsOfTheNight.FirstOrDefault(p => p.Name == Str(m, "p")) is { Name: not null } reached)
                {
                    _part = reached.Part;
                    ShowParts();
                }

                break;

            case "pickLayout":
                if (Str(m, "id") is { } id)
                {
                    _gallery = false;
                    FillLayouts(id);
                    ShowPicture();
                }

                break;
        }
    }

    private static string? Str(JsonElement m, string name) =>
        m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Number(JsonElement m, string name) =>
        m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;

    /// <summary>"Covering each other: Battle box and Fastest lap" - the page's measured pairs in the club's words.</summary>
    internal static string ClashWords(JsonElement m)
    {
        if (!m.TryGetProperty("pairs", out var pairs) || pairs.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var said = new List<string>();

        foreach (var pair in pairs.EnumerateArray())
        {
            if (pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() == 2
                && KindNamed(pair[0].GetString()) is { } a && KindNamed(pair[1].GetString()) is { } b)
            {
                said.Add(Short(a) + " and " + Short(b));
            }
        }

        if (said.Count == 0)
        {
            return string.Empty;
        }

        return "Covering each other now: " + string.Join("; ", said.Take(3)) + (said.Count > 3 ? $"; and {said.Count - 3} more" : string.Empty) + ".";

        static string Short(OverlayElementKind kind) => KindWords(kind).Split(" (")[0];
    }

    private void CopyAddress()
    {
        try
        {
            if (_txtAddress.Text.Length > 0)
            {
                Clipboard.SetText(_txtAddress.Text);
                _btnCopyAddress.Text = "Copied";
                var back = new System.Windows.Forms.Timer { Interval = 1500 };
                back.Tick += (_, _) =>
                {
                    back.Dispose();
                    if (!_btnCopyAddress.IsDisposed)
                    {
                        _btnCopyAddress.Text = "Copy";
                    }
                };
                back.Start();
            }
        }
        catch
        {
            // Another program holds the clipboard; the address is on screen.
        }
    }

    // --- Changes ---

    /// <summary>The element of this kind in the layout, added (off, where it usually goes) when the layout has none yet.</summary>
    private static OverlayElement ElementOf(OverlayLayout layout, OverlayElementKind kind)
    {
        if (layout.Element(kind) is { } e)
        {
            return e;
        }

        var added = new OverlayElement { Kind = kind, On = false };
        layout.Elements.Add(added);
        return added;
    }

    /// <summary>A graphic at its usual place, moved by a number: into the one of the nine it was already in, from there.</summary>
    private void OutOfUsualPlace(OverlayElement e)
    {
        if (e.Anchor != OverlayAnchor.Default)
        {
            return;
        }

        var (anchor, x, y) = UsualPlace(e.Kind);
        e.Anchor = anchor;
        e.X = x;
        e.Y = y;
        e.Scale = 1;
    }

    /// <summary>
    /// The club's own copy to change: the picked layout itself, or - for a
    /// preset - a copy made now, "&lt;name&gt; (our copy)", picked in its place
    /// and said so. Null when there is nothing picked.
    /// </summary>
    private OverlayLayout? OwnToChange()
    {
        if (Picked is not { } item)
        {
            return null;
        }

        if (!item.Preset)
        {
            return _store.Get(item.Layout.Id);
        }

        var name = item.Layout.Name + " (our copy)";
        var n = 2;

        while (_store.Layouts.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = item.Layout.Name + $" (our copy {n++})";
        }

        var copy = _store.AddCopy(item.Layout, name);
        _copiedFrom[copy.Id] = item.Layout.Id;
        FillLayouts(copy.Id);

        _lblNotice.Text = $"\"{item.Layout.Name}\" is a preset, so your change went into a copy of your own: \"{copy.Name}\". Its address is new - copy it for OBS. The preset stays as it was.";
        _notice.Visible = true;

        return _store.Get(copy.Id);
    }

    /// <summary>One change to the picked layout, saved at once. A preset's first change makes the club's copy. Nothing while filling.</summary>
    private void Edit(Action<OverlayLayout> change)
    {
        if (_filling || OwnToChange() is not { } layout)
        {
            return;
        }

        _undo.Add(layout.Copy());
        if (_undo.Count > 50)
        {
            _undo.RemoveAt(0);
        }

        _redo.RemoveAll(r => r.Id == layout.Id);
        change(layout);
        _store.Update(layout);
        ShowLayout();
        ReloadPicture();
    }

    private void EditElement(Action<OverlayElement> change) => Edit(layout => change(ElementOf(layout, _kind)));

    /// <summary>Puts the picked layout back as it was before its last change in this window - and, with none left, takes away the copy a preset's first change made.</summary>
    private void UndoLast()
    {
        if (Picked is not { Preset: false } item)
        {
            return;
        }

        var at = _undo.FindLastIndex(u => u.Id == item.Layout.Id);

        if (at < 0)
        {
            if (_copiedFrom.Remove(item.Layout.Id, out var preset))
            {
                _redo.RemoveAll(r => r.Id == item.Layout.Id);
                _store.Remove(item.Layout.Id);
                _notice.Visible = false;
                FillLayouts(preset);
                ReloadPicture();
            }

            return;
        }

        var before = _undo[at];
        _undo.RemoveAt(at);

        // WHY (15 Nov 2026, bug hunt 21): Undo takes back a change to the
        // graphics, not a Rename made since - which is not on the undo list.
        // The copy was taken before the rename, so it keeps the name as it
        // is now; otherwise the layout went back to its old name while this
        // window's list still showed the new one.
        if (_store.Get(before.Id) is { } now)
        {
            before.Name = now.Name;
            _redo.Add(now);
        }

        _store.Update(before);
        ShowLayout();
        ReloadPicture();
    }

    /// <summary>Makes again the change Undo last took back.</summary>
    private void RedoLast()
    {
        if (Picked is not { Preset: false } item)
        {
            return;
        }

        var at = _redo.FindLastIndex(r => r.Id == item.Layout.Id);

        if (at < 0 || _store.Get(item.Layout.Id) is not { } now)
        {
            return;
        }

        var again = _redo[at];
        _redo.RemoveAt(at);
        again.Name = now.Name;
        _undo.Add(now);
        _store.Update(again);
        ShowLayout();
        ReloadPicture();
    }

    /// <summary>
    /// The club's own "New to RC racing?" cards (overlay plan 7.6): one a
    /// line, "Heading | words". Emptied, HeatCaller's own come back.
    /// </summary>
    private void EditBasics()
    {
        if (_settings is null)
        {
            return;
        }

        using var ask = new Form { Text = "Basics cards", Font = AppTheme.BodyFont, BackColor = AppTheme.Background };
        var text = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            WordWrap = true,
            Text = string.IsNullOrWhiteSpace(_settings.StreamBasics) ? StreamBasics.BuiltInText : _settings.StreamBasics,
            Height = UiLayout.AtTextSize(260),
        };
        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var reset = new Button { Text = "HeatCaller's own" };
        reset.Click += (_, _) => text.Text = StreamBasics.BuiltInText;

        var page = new FillPage();
        page.Add(new Label
        {
            Text = "One card a line: a heading, a bar, then the words - \"Tonight's classes | Junior: under-16s on 1/28 cars\". "
                 + "They show between races, one every twelve seconds, on layouts with New to RC racing? on.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        }, gapBelow: UiLayout.RowGap);
        page.AddFill(text, UiLayout.AtTextSize(200), gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(cancel, ok, reset), gapBelow: 0);
        ask.Controls.Add(page);
        ask.AcceptButton = null;
        ask.CancelButton = cancel;
        AppTheme.ApplyRecursively(ask);
        DialogFit.Keep(ask, page, width: 720, height: 460);

        if (ask.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // The built-in cards saved unchanged are kept as "HeatCaller's own",
        // so a later version's better wording still reaches the club.
        var typed = text.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        _settings.StreamBasics = typed == StreamBasics.BuiltInText.Replace("\r\n", "\n", StringComparison.Ordinal) ? string.Empty : typed;
        _settings.Save();
        ReloadPicture();
    }

    private void RenameLayout()
    {
        if (Picked is not { Preset: false } || Current is not { } layout)
        {
            return;
        }

        var name = AskName("Rename", "A new name for this layout. Its address stays the same, so OBS keeps working.", layout.Name);

        if (name is null)
        {
            return;
        }

        layout.Name = name;
        _store.Update(layout);
        FillLayouts(layout.Id);
    }

    private void DeleteLayout()
    {
        if (Picked is not { Preset: false } || Current is not { } layout)
        {
            return;
        }

        if (AppDialog.Show(this,
                $"Delete \"{layout.Name}\"? An OBS source still using its address will say the overlay has gone.",
                "Delete layout", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _store.Remove(layout.Id);
        _copiedFrom.Remove(layout.Id);
        _notice.Visible = false;
        FillLayouts(null);
        ReloadPicture();
    }

    /// <summary>A name, asked for in a small box of the app's own; null when cancelled or left blank.</summary>
    private string? AskName(string title, string prompt, string suggestion)
    {
        using var ask = new Form
        {
            Text = title,
            Font = AppTheme.BodyFont,
            BackColor = AppTheme.Background,
        };

        var text = new TextBox { Text = suggestion, Width = UiLayout.AtTextSize(360) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        page.Add(new Label { Text = prompt, AutoSize = true, ForeColor = AppTheme.TextMuted }, gapBelow: UiLayout.RowGap);
        page.Add(text, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
        ask.Controls.Add(page);
        ask.AcceptButton = ok;
        ask.CancelButton = cancel;
        AppTheme.ApplyRecursively(ask);
        DialogFit.Keep(ask, page, width: 420);

        return ask.ShowDialog(this) == DialogResult.OK && text.Text.Trim().Length > 0 ? text.Text.Trim() : null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reload.Dispose();
        }

        base.Dispose(disposing);
    }
}
