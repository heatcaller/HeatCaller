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
/// </summary>
public sealed class RcpTrackEditorForm : Form
{
    private readonly RcpTrackCanvas _canvas = new();
    private readonly Panel _paletteHost = new();
    private readonly List<PaletteButton> _paletteButtons = new();
    private readonly ToolTip _tips = new();

    private readonly Label _lblStats = new();
    private readonly Label _lblKitCheck = new();
    private readonly Label _lblStatus = new();
    private readonly ComboBox _cmbKit = new();
    private readonly NumericUpDown _numTileSize = new();
    private readonly TextBox _txtName = new();
    private readonly CheckBox _chkClosedRoute = new();
    private readonly CheckBox _chkUseAsPhoto = new();
    private readonly Button _btnPlay = new();
    private readonly SliderControl _barBackdrop = new();

    private Button? _btn30;
    private Button? _btn50;
    private (Button Button, RcpTool Tool)[] _toolButtons = Array.Empty<(Button, RcpTool)>();
    private (Button Button, float Radius)[] _radiusButtons = Array.Empty<(Button, float)>();

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
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1120, 700);
        ClientSize = new Size(1360, 840);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        KeyPreview = true;

        _canvas.Design = existing?.Clone() ?? new RcpTrackDesign();
        _canvas.LapSeconds = averageLapSeconds is > 0 ? averageLapSeconds.Value : 12;
        _canvas.DesignChanged += RefreshReadouts;
        _canvas.StatusChanged += message => _lblStatus.Text = message;

        var toolbar = BuildToolbar();
        BuildPalette();
        var side = BuildSidePanel();
        var footer = BuildFooter();

        var canvasHost = new Panel { Dock = DockStyle.Fill, BackColor = AppTheme.Background };

        _canvas.Dock = DockStyle.Fill;
        canvasHost.Controls.Add(_canvas);

        Controls.Add(canvasHost);
        Controls.Add(side);
        Controls.Add(_paletteHost);
        Controls.Add(toolbar);
        Controls.Add(footer);

        AppTheme.StyleToolTip(_tips);
        AppTheme.ApplyRecursively(this);

        _loading = false;

        HighlightTileSize();
        HighlightRadius();
        RefreshReadouts();
        _canvas.Fit();

        // Why the plan's traced photo is missing, if it is. Said when the
        // plan was loaded, before the status line existed to show it - and
        // the toolbar's first tool then wrote over it anyway.
        if (_canvas.BackdropProblem is { } why)
        {
            _lblStatus.Text = why;
        }

        Shown += (_, _) => _canvas.Focus();
    }

    // --- Chrome ---------------------------------------------------------------

    private Panel BuildToolbar()
    {
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 54, BackColor = AppTheme.Surface };

        var x = 12;

        Button Add(string text, string tip, Action click)
        {
            var button = new Button
            {
                Text = text,
                Location = new Point(x, 11),
                Height = 32,
                Width = Math.Max(78, TextRenderer.MeasureText(text, AppTheme.BodyFont).Width + 24),
            };

            button.Click += (_, _) => { click(); _canvas.Focus(); };

            _tips.SetToolTip(button, tip);

            toolbar.Controls.Add(button);
            x += button.Width + 6;

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

        x += 12;

        Add("Wall the edge", "Put a rail round every open edge of the floor.", () => _canvas.WallThePerimeter());
        Add("Trace route", "Follow the corridor between the rails and lay the racing line down it.", () => _canvas.AutoRoute());

        _btnPlay.Text = "Play";
        _btnPlay.Location = new Point(x, 11);
        _btnPlay.Height = 32;
        _btnPlay.Width = 76;
        _btnPlay.Click += (_, _) =>
        {
            _canvas.TogglePlay();
            _btnPlay.Text = _canvas.IsPlaying ? "Stop" : "Play";
            _canvas.Focus();
        };

        _tips.SetToolTip(_btnPlay, "Send a car round the line you drew.");
        toolbar.Controls.Add(_btnPlay);
        x += _btnPlay.Width + 6;

        x += 12;

        Add("Undo", "Ctrl+Z", () => _canvas.Undo());
        Add("Redo", "Ctrl+Y", () => _canvas.Redo());
        Add("Fit", "Frame the whole plan. (F)", () => _canvas.Fit());
        Add("Clear", "Start again.", ClearAll);

        SetTool(RcpTool.Floor);

        return toolbar;
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

        _lblStatus.Text = tool switch
        {
            RcpTool.Floor => "Drag to paint floor tiles - the whole track sits on these. Ctrl-drag takes them back off.",
            RcpTool.Wall => "Drag along the grid to run rail, and a turn rail drops in wherever you round a corner. A single click on a seam still adds one, doubles it up, or takes it away.",
            RcpTool.Curve => "Click near a corner of the grid - the arc follows whichever way the pointer is.",
            RcpTool.Marker => "Click to drop a marking. Click it again, or press R, to turn it.",
            RcpTool.Route => "Click to drop points along the racing line. Drag one to move it, right-click to remove it.",
            RcpTool.Erase => "Click to take off whatever is under the pointer.",
            _ => "Drag the photo into place and use the wheel to size it until its tiles line up with the grid.",
        };
    }

    private void BuildPalette()
    {
        _paletteHost.Dock = DockStyle.Left;
        _paletteHost.Width = 224;
        _paletteHost.BackColor = AppTheme.Surface;
        _paletteHost.Padding = new Padding(10);
        _paletteHost.AutoScroll = true;

        var y = 8;

        void Heading(string text, string note)
        {
            var label = new Label
            {
                Text = AppTheme.Spaced(text.ToUpperInvariant()),
                Location = new Point(10, y),
                AutoSize = true,
                Font = AppTheme.MicroFont,
                ForeColor = AppTheme.TextMuted,
            };

            _paletteHost.Controls.Add(label);
            y += 20;

            var hint = new Label
            {
                Text = note,
                Location = new Point(10, y),
                AutoSize = true,
                MaximumSize = new Size(190, 0),
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
            };

            _paletteHost.Controls.Add(hint);
            y += hint.PreferredHeight + 8;
        }

        void AddItem(RcpPaletteItem item)
        {
            var button = new PaletteButton(item)
            {
                Location = new Point(10, y),
                Size = new Size(192, 54),
            };

            button.Chosen += chosen =>
            {
                _canvas.Item = chosen;
                SetTool(RcpTiles.ToolFor(chosen));
                HighlightPalette();
                _canvas.Focus();
            };

            _paletteButtons.Add(button);
            _paletteHost.Controls.Add(button);

            y += button.Height + 6;
        }

        Heading("Floor", "One continuous field of tiles. Lay it first.");

        foreach (var item in RcpTiles.Floor)
        {
            AddItem(item);
        }

        y += 8;
        Heading("Barriers", "Rails stand ON the floor, along the seams.");

        foreach (var item in RcpTiles.Barriers)
        {
            AddItem(item);
        }

        // Turn radius, since a sweeper and a hairpin are the same piece here.
        var lblRadius = new Label
        {
            Text = "Turn radius, in tiles:",
            Location = new Point(10, y),
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        _paletteHost.Controls.Add(lblRadius);
        y += 22;

        var radii = new[] { 0.5f, 1f, 1.5f, 2f };
        var buttons = new List<(Button, float)>();
        var rx = 10;

        foreach (var radius in radii)
        {
            var button = new Button
            {
                Text = radius.ToString("0.#"),
                Location = new Point(rx, y),
                Size = new Size(44, 30),
            };

            var captured = radius;

            button.Click += (_, _) =>
            {
                _canvas.CurveRadius = captured;
                SetTool(RcpTool.Curve);
                HighlightRadius();
                _canvas.Focus();
            };

            _paletteHost.Controls.Add(button);
            buttons.Add((button, radius));

            rx += 48;
        }

        _radiusButtons = buttons.ToArray();
        y += 40;

        Heading("Markings", "Flat things stuck on the floor.");

        foreach (var item in RcpTiles.Markings)
        {
            AddItem(item);
        }

        HighlightPalette();
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

    private Panel BuildSidePanel()
    {
        var side = new Panel
        {
            Dock = DockStyle.Right,
            Width = 314,
            BackColor = AppTheme.Surface,
            Padding = new Padding(12),
            AutoScroll = true,
        };

        var y = 12;

        void Caption(string text)
        {
            var label = new Label
            {
                Text = AppTheme.Spaced(text.ToUpperInvariant()),
                Location = new Point(12, y),
                AutoSize = true,
                Font = AppTheme.MicroFont,
                ForeColor = AppTheme.TextMuted,
            };

            side.Controls.Add(label);
            y += 24;
        }

        Caption("Track name");

        _txtName.Location = new Point(12, y);
        _txtName.Width = 276;
        _txtName.Text = _trackName;
        side.Controls.Add(_txtName);

        y += 40;

        Caption("Tile size");

        _btn30 = new Button { Text = "30 cm", Location = new Point(12, y), Size = new Size(84, 32) };
        _btn50 = new Button { Text = "50 cm", Location = new Point(102, y), Size = new Size(84, 32) };

        _btn30.Click += (_, _) => SetTileSize(300);
        _btn50.Click += (_, _) => SetTileSize(500);

        side.Controls.Add(_btn30);
        side.Controls.Add(_btn50);

        _numTileSize.Location = new Point(196, y + 2);
        _numTileSize.Width = 90;
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

        side.Controls.Add(_numTileSize);

        side.Controls.Add(new Label
        {
            Text = "mm per tile",
            Location = new Point(196, y + 36),
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        });

        y += 62;

        Caption("Trace a photo");

        var btnTrace = new Button { Text = "Choose photo...", Location = new Point(12, y), Size = new Size(140, 32) };
        btnTrace.Click += (_, _) => ChooseBackdrop();

        var btnHide = new Button { Text = "Hide", Location = new Point(158, y), Size = new Size(64, 32) };
        btnHide.Click += (_, _) => { _canvas.ToggleBackdrop(); _canvas.Focus(); };

        var btnDropPhoto = new Button { Text = "Remove", Location = new Point(228, y), Size = new Size(74, 32) };
        btnDropPhoto.Click += (_, _) => { _canvas.ClearBackdrop(); _canvas.Focus(); };

        side.Controls.Add(btnTrace);
        side.Controls.Add(btnHide);
        side.Controls.Add(btnDropPhoto);

        y += 38;

        _barBackdrop.Location = new Point(10, y);
        _barBackdrop.Width = 280;
        _barBackdrop.Minimum = 5;
        _barBackdrop.Maximum = 100;
        _barBackdrop.Value = Math.Clamp(Result.BackdropOpacity, 5, 100);
        _barBackdrop.ValueChanged += (_, _) =>
        {
            Result.BackdropOpacity = _barBackdrop.Value;
            _canvas.Invalidate();
        };

        side.Controls.Add(_barBackdrop);

        y += 52;

        side.Controls.Add(new Label
        {
            Text = "Drop in a photo of the real track, line its tiles up with the grid using the Photo tool, "
                   + "then draw over it. That's the quickest way to get a layout in exactly.",
            Location = new Point(12, y),
            AutoSize = true,
            MaximumSize = new Size(276, 0),
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        });

        y += 62;

        Caption("The lap");

        _chkClosedRoute.Text = "The line closes into a lap";
        _chkClosedRoute.Location = new Point(12, y);
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

        side.Controls.Add(_chkClosedRoute);
        y += 34;

        _lblStats.Location = new Point(12, y);
        _lblStats.AutoSize = true;
        _lblStats.MaximumSize = new Size(276, 0);
        side.Controls.Add(_lblStats);

        y += 190;

        Caption("Check against a kit");

        _cmbKit.Location = new Point(12, y);
        _cmbKit.Width = 276;
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

        side.Controls.Add(_cmbKit);
        y += 40;

        _lblKitCheck.Location = new Point(12, y);
        _lblKitCheck.AutoSize = true;
        _lblKitCheck.MaximumSize = new Size(276, 0);
        _lblKitCheck.Font = AppTheme.SmallFont;
        side.Controls.Add(_lblKitCheck);

        return side;
    }

    private Panel BuildFooter()
    {
        // Width BEFORE the children go in, and that is the whole point.
        //
        // A Panel starts 200 wide. These buttons are placed from
        // ClientSize.Width - 1360 - and anchored Right, and an anchor
        // preserves the margins a child had when it was ADDED. Added to a
        // 200-wide footer, "Cancel" at x=1250 got a right margin of
        // 200 - 1346 = -1146, and kept it: once the footer docked out to
        // 1360, Save, Cancel and Save as PNG sat about 1150px past the
        // right edge of the window, where nobody could reach them. The
        // Track Designer had no working Save button.
        //
        // Docking will set this width again in a moment. Setting it here
        // is what makes the margins right.
        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            Width = ClientSize.Width,
            BackColor = AppTheme.Surface,
        };

        _lblStatus.Location = new Point(16, 20);
        _lblStatus.AutoSize = true;
        _lblStatus.MaximumSize = new Size(560, 0);
        _lblStatus.ForeColor = AppTheme.TextMuted;
        footer.Controls.Add(_lblStatus);

        var btnSave = new Button { Text = "Save Layout", Width = 130, Height = 34, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        var btnCancel = new Button { Text = "Cancel", Width = 96, Height = 34, Anchor = AnchorStyles.Right | AnchorStyles.Top, DialogResult = DialogResult.Cancel };
        var btnPng = new Button { Text = "Save as PNG...", Width = 140, Height = 34, Anchor = AnchorStyles.Right | AnchorStyles.Top };

        btnSave.Location = new Point(ClientSize.Width - 246, 13);
        btnCancel.Location = new Point(ClientSize.Width - 110, 13);
        btnPng.Location = new Point(ClientSize.Width - 396, 13);

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

            Result.RouteClosed = _chkClosedRoute.Checked;
            DialogResult = DialogResult.OK;
            Close();
        };

        btnPng.Click += (_, _) => ExportPng();

        _chkUseAsPhoto.Text = "Show this plan as the track's main picture";
        _chkUseAsPhoto.Location = new Point(ClientSize.Width - 740, 20);
        _chkUseAsPhoto.AutoSize = true;
        _chkUseAsPhoto.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        _chkUseAsPhoto.Checked = true;

        footer.Controls.Add(_chkUseAsPhoto);
        footer.Controls.Add(btnPng);
        footer.Controls.Add(btnSave);
        footer.Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        return footer;
    }

    // --- Behaviour ------------------------------------------------------------

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
        if (Result.IsEmpty)
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

    private void RefreshReadouts()
    {
        if (_loading)
        {
            return;
        }

        var inventory = RcpPlan.Count(Result);
        var lengthTiles = RcpPlan.RouteLengthInTiles(Result);
        var metres = lengthTiles * Result.TileSizeMm / 1000.0;
        var feet = metres * 3.28084;

        var perTile = Result.TileSizeMm / 1000.0;
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

        if (lengthTiles > 0 && _canvas.LapSeconds > 0)
        {
            var speed = metres / _canvas.LapSeconds;
            lines.Add($"At a {_canvas.LapSeconds:0.0}s lap that's {speed:0.0} m/s ({speed * 3.6:0.0} km/h).");
        }

        if (inventory.Tiles > 0 && inventory.StraightRails == 0)
        {
            lines.Add(string.Empty);
            lines.Add("No rails yet - try Wall the edge, then add the dividers.");
        }

        _lblStats.Text = string.Join(Environment.NewLine, lines);

        RefreshKitCheck(inventory);
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

            var size = Height - 14;
            var origin = new PointF(10, 7);

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

            var textLeft = origin.X + size + 12;
            var textWidth = Width - textLeft - 10;

            g.DrawString(RcpTiles.NameOf(Item), AppTheme.BoldFont, nameBrush,
                new RectangleF(textLeft, 7, textWidth, 20), format);

            // Disposed like the one above: made fresh on every repaint (each
            // hover in and out), it was left for the finaliser every time.
            using var hintFormat = new StringFormat { Trimming = StringTrimming.EllipsisWord };

            g.DrawString(RcpTiles.HintOf(Item), AppTheme.SmallFont, hintBrush,
                new RectangleF(textLeft, 25, textWidth, 24),
                hintFormat);
        }
    }
}
