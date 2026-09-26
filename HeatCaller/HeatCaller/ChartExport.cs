namespace HeatCaller;

/// <summary>
/// The colours a chart draws itself with. Charts read from this rather
/// than AppTheme directly, so the same drawing code can produce a dark
/// on-screen chart and a light printable one without duplicating anything.
/// </summary>
public sealed class ChartPalette
{
    public required Color Background { get; init; }
    public required Color Text { get; init; }
    public required Color Muted { get; init; }
    public required Color Grid { get; init; }
    public required Color Axis { get; init; }
    public required Color Highlight { get; init; }
    public required Color[] Series { get; init; }

    /// <summary>Matches the app's current theme - what's shown on screen.</summary>
    public static ChartPalette Screen() => new()
    {
        Background = AppTheme.Surface,
        Text = AppTheme.TextDark,
        Muted = AppTheme.TextMuted,
        Grid = Color.FromArgb(60, AppTheme.TextMuted),
        Axis = AppTheme.Border,
        Highlight = AppTheme.Success,
        Series = new[]
        {
            ColorTranslator.FromHtml("#FF2E9A"),
            ColorTranslator.FromHtml("#2EE6FF"),
            ColorTranslator.FromHtml("#FFA646"),
            ColorTranslator.FromHtml("#2EE6C4"),
            ColorTranslator.FromHtml("#B79FDB"),
            ColorTranslator.FromHtml("#FF5C5C"),
            ColorTranslator.FromHtml("#7ED321"),
            ColorTranslator.FromHtml("#F5A623"),
        },
    };

    /// <summary>
    /// White background with dark ink - the point being that a dark chart
    /// covers a whole sheet in toner for no benefit. Series colours are
    /// darkened versions of the screen set: still distinguishable from each
    /// other, but readable as lines on paper rather than neon on black.
    /// </summary>
    public static ChartPalette Print() => new()
    {
        Background = Color.White,
        Text = Color.FromArgb(20, 20, 20),
        Muted = Color.FromArgb(110, 110, 110),
        Grid = Color.FromArgb(225, 225, 225),
        Axis = Color.FromArgb(150, 150, 150),
        Highlight = Color.FromArgb(0, 130, 70),
        Series = new[]
        {
            ColorTranslator.FromHtml("#C2185B"),
            ColorTranslator.FromHtml("#00796B"),
            ColorTranslator.FromHtml("#E65100"),
            ColorTranslator.FromHtml("#1565C0"),
            ColorTranslator.FromHtml("#6A1B9A"),
            ColorTranslator.FromHtml("#B71C1C"),
            ColorTranslator.FromHtml("#33691E"),
            ColorTranslator.FromHtml("#4E342E"),
        },
    };

    /// <summary>
    /// Also white, but every series is a shade of grey - for printing on a
    /// mono laser, where coloured lines all come out as similar greys and
    /// become impossible to tell apart.
    /// </summary>
    public static ChartPalette Monochrome() => new()
    {
        Background = Color.White,
        Text = Color.Black,
        Muted = Color.FromArgb(100, 100, 100),
        Grid = Color.FromArgb(230, 230, 230),
        Axis = Color.FromArgb(120, 120, 120),
        Highlight = Color.Black,
        Series = new[]
        {
            Color.FromArgb(20, 20, 20),
            Color.FromArgb(110, 110, 110),
            Color.FromArgb(60, 60, 60),
            Color.FromArgb(160, 160, 160),
            Color.FromArgb(90, 90, 90),
            Color.FromArgb(35, 35, 35),
            Color.FromArgb(135, 135, 135),
            Color.FromArgb(75, 75, 75),
        },
    };

    public static ChartPalette For(string styleKey) => styleKey switch
    {
        "print" => Print(),
        "mono" => Monochrome(),
        _ => Screen(),
    };
}

/// <summary>What the user picked in the export dialog.</summary>
public sealed class ChartExportOptions
{
    public string StyleKey { get; set; } = "print";
    public Size ImageSize { get; set; } = new(1280, 680);
}

/// <summary>Asks how a chart image should be rendered before saving it.</summary>
public sealed class ChartExportForm : Form
{
    private readonly ComboBox _cmbStyle = new();
    private readonly ComboBox _cmbSize = new();
    private readonly Panel _preview = new();
    private readonly Action<Graphics, Rectangle, ChartPalette> _drawPreview;

    public ChartExportOptions Options { get; } = new();

    public ChartExportForm(AppSettings settings, Action<Graphics, Rectangle, ChartPalette> drawPreview)
    {
        _drawPreview = drawPreview;

        Text = "Save chart as image";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 420);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblStyle = new Label { Text = "Style:", Location = new Point(16, 20), AutoSize = true };
        _cmbStyle.Location = new Point(110, 17);
        _cmbStyle.Width = 380;
        _cmbStyle.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbStyle.Items.AddRange(new object[]
        {
            new StyleItem("print", "Print friendly - white background, dark ink"),
            new StyleItem("mono", "Monochrome - white background, grey lines (mono printers)"),
            new StyleItem("screen", "Match the app's look - as shown on screen"),
        });
        _cmbStyle.SelectedIndexChanged += (_, _) => _preview.Invalidate();

        var lblSize = new Label { Text = "Size:", Location = new Point(16, 56), AutoSize = true };
        _cmbSize.Location = new Point(110, 53);
        _cmbSize.Width = 380;
        _cmbSize.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSize.Items.AddRange(new object[]
        {
            new SizeItem(new Size(1280, 680), "Standard  (1280 x 680)"),
            new SizeItem(new Size(1920, 1020), "Large  (1920 x 1020)"),
            new SizeItem(new Size(2560, 1360), "Presentation  (2560 x 1360)"),
        });

        // Restore the previous choice - people tend to want the same output
        // every time, and re-picking it on every export gets old fast.
        SelectStyle(settings.ChartExportStyle);
        SelectSize(settings.ChartExportWidth);

        var lblPreview = new Label { Text = "Preview:", Location = new Point(16, 94), AutoSize = true, Font = AppTheme.BoldFont };

        _preview.Location = new Point(16, 116);
        _preview.Size = new Size(474, 226);
        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.Paint += (_, e) =>
            _drawPreview(e.Graphics, _preview.ClientRectangle, ChartPalette.For(CurrentStyleKey()));

        var btnSave = new Button { Text = "Save...", Location = new Point(310, 366), Width = 90 };
        btnSave.Click += (_, _) =>
        {
            Options.StyleKey = CurrentStyleKey();
            Options.ImageSize = (_cmbSize.SelectedItem as SizeItem)?.Value ?? new Size(1280, 680);

            settings.ChartExportStyle = Options.StyleKey;
            settings.ChartExportWidth = Options.ImageSize.Width;
            settings.Save();

            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", Location = new Point(408, 366), Width = 84, DialogResult = DialogResult.Cancel };

        Controls.Add(lblStyle);
        Controls.Add(_cmbStyle);
        Controls.Add(lblSize);
        Controls.Add(_cmbSize);
        Controls.Add(lblPreview);
        Controls.Add(_preview);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
    }

    private string CurrentStyleKey() => (_cmbStyle.SelectedItem as StyleItem)?.Key ?? "print";

    private void SelectStyle(string key)
    {
        for (var i = 0; i < _cmbStyle.Items.Count; i++)
        {
            if (_cmbStyle.Items[i] is StyleItem item && item.Key == key)
            {
                _cmbStyle.SelectedIndex = i;
                return;
            }
        }
        _cmbStyle.SelectedIndex = 0;
    }

    private void SelectSize(int width)
    {
        for (var i = 0; i < _cmbSize.Items.Count; i++)
        {
            if (_cmbSize.Items[i] is SizeItem item && item.Value.Width == width)
            {
                _cmbSize.SelectedIndex = i;
                return;
            }
        }
        _cmbSize.SelectedIndex = 0;
    }

    private sealed record StyleItem(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record SizeItem(Size Value, string Label)
    {
        public override string ToString() => Label;
    }
}
