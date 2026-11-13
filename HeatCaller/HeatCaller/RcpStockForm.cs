namespace HeatCaller;

/// <summary>
/// What the club has in the cupboard: how many of each boxed RCP set, and
/// the loose parts, 30 cm and 50 cm apart (Track Designer overhaul, Phase
/// 4, 25 Oct 2026). Opened from the Track Designer's Stock tab; what it
/// holds is saved with the club's settings (AppSettings.TrackStock), since
/// it is the same cupboard for every layout.
///
/// On the layout pieces (DialogFit): a line of what it is for, the boxes
/// in one measured column of captions and number boxes, the loose parts
/// the same under a 30 cm / 50 cm switch, and the buttons at the foot. On
/// a screen too short for all of it, the body scrolls and the buttons stay
/// in view.
/// </summary>
public sealed class RcpStockForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size.</summary>
    private const int DialogWidth = 520;

    private readonly RcpStock _stock;
    private readonly List<(RcpKit Kit, NumericUpDown Box)> _sets = new();
    private readonly List<(Func<RcpLooseParts, int> Read, Action<RcpLooseParts, int> Write, NumericUpDown Box)> _loose = new();
    private readonly Button _btn30 = new() { Text = "30 cm" };
    private readonly Button _btn50 = new() { Text = "50 cm" };

    /// <summary>The tile size whose loose parts the boxes show.</summary>
    private double _looseSize;

    /// <summary>Set while the boxes are being filled in, so their handlers leave the stock alone.</summary>
    private bool _filling;

    /// <summary>What the club owns, as edited. Only meaningful after DialogResult.OK.</summary>
    public RcpStock Result => _stock.Tidied();

    /// <param name="stock">What is owned now - copied, so Cancel leaves it as it was.</param>
    /// <param name="tileSizeMm">The plan's tile size, which the loose parts open on.</param>
    public RcpStockForm(RcpStock? stock, double tileSizeMm)
    {
        Text = "What the club owns";
        Font = AppTheme.BodyFont;

        _stock = (stock ?? new RcpStock()).Tidied();
        _looseSize = Math.Abs(tileSizeMm - 300) < 1 ? 300 : 500;

        var lblIntro = new Label
        {
            Text = "Count it once and every layout is checked against it: each part shows as used, owned and left.",
            AutoSize = true,
            Font = AppTheme.BodyFont,
        };

        // --- Boxed sets ---
        var setsCard = new Section("Boxed sets");
        var setsGrid = new FieldGrid();

        foreach (var kit in RcpKit.All)
        {
            var box = Number(_stock.Sets.TryGetValue(kit.Name, out var count) ? count : 0, 99);
            var captured = kit;

            box.ValueChanged += (_, _) =>
            {
                if (!_filling)
                {
                    _stock.Sets[captured.Name] = (int)box.Value;
                }
            };

            setsGrid.AddRow(kit.Name + ":", box);
            _sets.Add((kit, box));
        }

        setsCard.Add(setsGrid, gapBelow: UiLayout.HintGap);
        setsCard.Add(Note("Each box counts as the pieces it makes, with every rail bolted to a tile. Some boxes come with only a tile count from RCP. For those, only the tiles are counted."), gapBelow: 0);

        // --- Loose parts ---
        var looseCard = new Section("Loose parts");
        var looseGrid = new FieldGrid();

        void Part(string caption, Func<RcpLooseParts, int> read, Action<RcpLooseParts, int> write)
        {
            var box = Number(0, 9999);

            box.ValueChanged += (_, _) =>
            {
                if (!_filling)
                {
                    write(_stock.LooseFor(_looseSize), (int)box.Value);
                }
            };

            looseGrid.AddRow(caption, box);
            _loose.Add((read, write, box));
        }

        // In pieces, as the cupboard holds them: the rails stay on their
        // tiles (railed tiles, Phase 4, 13 Nov 2026).
        Part("Plain and blank tiles:", p => p.Tiles, (p, v) => p.Tiles = v);
        Part("Rail tiles:", p => p.RailTiles, (p, v) => p.RailTiles = v);
        Part("Finish rail tiles:", p => p.FinishTiles, (p, v) => p.FinishTiles = v);
        Part("Arched rail tiles:", p => p.ArchedTiles, (p, v) => p.ArchedTiles = v);
        Part("Outer curve tiles:", p => p.OuterCurveTiles, (p, v) => p.OuterCurveTiles = v);
        Part("Inner corner tiles:", p => p.InnerCornerTiles, (p, v) => p.InnerCornerTiles = v);
        Part("Loose straight rails:", p => p.StraightRails, (p, v) => p.StraightRails = v);
        Part("Loose turn rails:", p => p.TurnRails, (p, v) => p.TurnRails = v);
        Part("Loose corner pieces:", p => p.InnerCornerPieces, (p, v) => p.InnerCornerPieces = v);
        Part("End caps:", p => p.EndCaps, (p, v) => p.EndCaps = v);

        _btn30.Click += (_, _) => ShowLoose(300);
        _btn50.Click += (_, _) => ShowLoose(500);

        looseCard.Add(Note("Pieces not in a box - blank tiles, a lane widening kit, spares. Count each tile as it is, with its rail on. Tile sizes don't mix, so each size is counted on its own:"), gapBelow: UiLayout.HintGap);
        looseCard.Add(new ButtonBar(tight: true).With(_btn30, _btn50), gapBelow: UiLayout.RowGap);
        looseCard.Add(looseGrid, gapBelow: 0);

        // --- The page ---
        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        var body = DialogFit.Body(page);
        body.Add(lblIntro, gapBelow: UiLayout.RowGap);
        body.Add(setsCard, gapBelow: UiLayout.RowGap);
        body.Add(looseCard, gapBelow: 0);

        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // After the theme pass, which would otherwise grey them both.
        ShowLoose(_looseSize);

        DialogFit.Keep(this, page, width: DialogWidth, body: body);
    }

    private static Label Note(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = AppTheme.SmallFont,
        ForeColor = AppTheme.TextMuted,
    };

    /// <summary>A count box: whole numbers from 0, wide enough for the largest it takes.</summary>
    private static NumericUpDown Number(int value, int most)
    {
        var box = new NumericUpDown
        {
            Minimum = 0,
            Maximum = most,
            Value = Math.Clamp(value, 0, most),
        };

        box.Width = Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(new string('0', most.ToString().Length), box.Font).Width
            + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);

        return box;
    }

    /// <summary>Shows one tile size's loose parts in the boxes, and lights its button.</summary>
    private void ShowLoose(double size)
    {
        _looseSize = size;

        var parts = _stock.Loose.FirstOrDefault(p => Math.Abs(p.TileSizeMm - size) < 1) ?? new RcpLooseParts { TileSizeMm = size };

        _filling = true;

        try
        {
            foreach (var (read, _, box) in _loose)
            {
                box.Value = Math.Clamp(read(parts), (int)box.Minimum, (int)box.Maximum);
            }
        }
        finally
        {
            _filling = false;
        }

        foreach (var (button, active) in new[] { (_btn30, size < 400), (_btn50, size >= 400) })
        {
            button.BackColor = active ? AppTheme.Primary : AppTheme.SecondaryButton;
            button.ForeColor = active ? AppTheme.OnPrimary : AppTheme.TextDark;
            button.FlatAppearance.BorderSize = 0;
        }
    }
}
