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

    /// <summary>
    /// Second and third place on a results card (ResultsCardRenderer):
    /// silver and bronze, as dark or light as the background needs to be
    /// read on. First place is the Highlight. Silver and bronze were fixed
    /// colours on every style, and on the white ones second place was a
    /// pale grey that all but disappeared, and on the monochrome one third
    /// place was still bronze (layout pictures, 30 Sep).
    /// </summary>
    public Color Second { get; init; } = ColorTranslator.FromHtml("#C7CCD6");

    /// <inheritdoc cref="Second"/>
    public Color Third { get; init; } = ColorTranslator.FromHtml("#C98A4B");

    /// <summary>Matches the app's current theme - what's shown on screen.</summary>
    public static ChartPalette Screen() => new()
    {
        Background = AppTheme.Surface,
        Text = AppTheme.TextDark,
        Muted = AppTheme.TextMuted,
        Grid = Color.FromArgb(60, AppTheme.TextMuted),
        Axis = AppTheme.Border,
        Highlight = AppTheme.Success,

        // On a light look, the darker pair the print style uses: the pale
        // silver all but vanished on a white card.
        Second = Looks.Luminance(AppTheme.Surface) > 0.5 ? Color.FromArgb(84, 96, 112) : ColorTranslator.FromHtml("#C7CCD6"),
        Third = Looks.Luminance(AppTheme.Surface) > 0.5 ? Color.FromArgb(150, 84, 30) : ColorTranslator.FromHtml("#C98A4B"),
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

        // A slate silver and a dark bronze: each as dark as the body text's
        // grey, so both read on white paper.
        Second = Color.FromArgb(84, 96, 112),
        Third = Color.FromArgb(150, 84, 30),
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

        // No colour at all on a mono printer: the podium is in the text's
        // black, as first place is.
        Second = Color.Black,
        Third = Color.Black,
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

/// <summary>
/// Asks how a chart image should be rendered before saving it.
///
/// On the layout pieces (DialogFit): the style and the size lined up in a
/// field grid, each drop-down running to the right-hand edge, in an Image
/// card with a line on what they are for; the preview under them in a card
/// of its own, taking the width and the height that are left (18 Nov 2026,
/// dialog jazz-up); Save, the main button, and Cancel at the foot against
/// the right-hand edge. It opens at the size it
/// had - wider when the longest style needs more - and can be made bigger,
/// the preview growing with it. The preview draws itself as it always did;
/// only the room it is given is new. It used to be placed in pixels in a
/// fixed 520x420 window: the drop-downs at a fixed 380px beside captions
/// at a fixed 110px, which cut the monochrome style's description short
/// even at the normal text size, and the preview a fixed 474x226.
/// </summary>
public sealed class ChartExportForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size, at the least - the width it had, wider when the drop-downs need more.</summary>
    private const int DialogWidth = 520;

    /// <summary>How tall it opens inside its frame at the normal text size - the 420 it had, and the room the two cards' captions, hint and insets take, so the preview keeps the height it had.</summary>
    private const int DialogHeight = 500;

    /// <summary>The drop-downs' width at the normal text size - the width they had, wider when the longest choice needs more.</summary>
    private const int DropDownWidth = 380;

    /// <summary>The preview's least height at the normal text size, below which the window scrolls instead.</summary>
    private const int PreviewLeastHeight = 160;

    private readonly ComboBox _cmbStyle = new();
    private readonly ComboBox _cmbSize = new();
    private readonly Panel _preview = new();
    private readonly Action<Graphics, Rectangle, ChartPalette> _drawPreview;

    public ChartExportOptions Options { get; } = new();

    /// <param name="chooseSize">
    /// False for an image drawn at its own fixed size - the results card - which the
    /// Size list did nothing for, while the choice made there was saved as the
    /// charts' own (2 Nov 2026). The row is left out and the charts' size kept.
    /// </param>
    public ChartExportForm(AppSettings settings, Action<Graphics, Rectangle, ChartPalette> drawPreview, bool chooseSize = true)
    {
        _drawPreview = drawPreview;

        Text = "Save chart as image";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        _cmbStyle.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbStyle.Items.AddRange(new object[]
        {
            new StyleItem("print", "Print friendly - white background, dark ink"),
            new StyleItem("mono", "Monochrome - white background, gray lines (mono printers)"),
            new StyleItem("screen", "Match the app's look - as shown on screen"),
        });
        _cmbStyle.SelectedIndexChanged += (_, _) => _preview.Invalidate();

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


        _preview.BorderStyle = BorderStyle.FixedSingle;
        _preview.Paint += (_, e) =>
            _drawPreview(e.Graphics, _preview.ClientRectangle, ChartPalette.For(CurrentStyleKey()));

        // The preview is drawn to its whole size, and it now grows and
        // shrinks with the window. A panel resized repaints only the strip
        // the resize uncovers, which left the drawing made for the old size
        // in the rest - so all of it is drawn again.
        _preview.Resize += (_, _) => _preview.Invalidate();

        var btnSave = new Button { Text = "Save..." };
        btnSave.Click += (_, _) =>
        {
            Options.StyleKey = CurrentStyleKey();
            Options.ImageSize = (_cmbSize.SelectedItem as SizeItem)?.Value ?? new Size(1280, 680);

            settings.ChartExportStyle = Options.StyleKey;

            if (chooseSize)
            {
                settings.ChartExportWidth = Options.ImageSize.Width;
            }

            settings.Save();

            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Both drop-downs run to the right-hand edge, where the preview
        // ends, as they did beside their captions.
        var fields = new FieldGrid();
        var captions = new List<Label> { fields.AddRow("Style:", _cmbStyle, stretch: true) };

        if (chooseSize)
        {
            captions.Add(fields.AddRow("Size:", _cmbSize, stretch: true));
        }

        // Two cards (18 Nov 2026, dialog jazz-up): what the image is to be,
        // with a line on what the choices are for, and the preview taking the
        // height that is left.
        var imageCard = new Section("Image");
        imageCard.Add(CardHint(chooseSize
            ? "How the saved picture looks, and how big it is. Kept for next time."
            : "How the saved picture looks. Kept for next time."));
        imageCard.Add(fields, gapBelow: 0);

        var previewCard = new Section("Preview");
        previewCard.AddFill(_preview, () => UiLayout.AtTextSize(PreviewLeastHeight), gapBelow: 0);

        var page = new FillPage();
        page.Add(imageCard, gapBelow: UiLayout.RowGap);
        page.AddFill(previewCard, previewCard.LeastHeight, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Save first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // As wide as it was at the normal text size, at the size in use - or
        // wider when a drop-down needs more for its longest choice: at a
        // fixed 380px, "Monochrome - white background, grey lines (mono
        // printers)" came out cut short once it was picked. Measured in the
        // faces the theme pass left them in, and never read back from the
        // drop-downs, which take the whole of the fields' column (stretch)
        // and have already been cut to a window not sized yet. The caption
        // column beside them as the grid works it out: its widest caption
        // and the gap after it.
        (Font Font, int Count, int Width)? styleWidth = null;
        (Font Font, int Count, int Width)? sizeWidth = null;

        var fieldColumn = Math.Max(
            UiLayout.AtTextSize(DropDownWidth),
            Math.Max(Pieces.PickerWidth(_cmbStyle, ref styleWidth), Pieces.PickerWidth(_cmbSize, ref sizeWidth)));
        var captionColumn = captions.Max(caption => caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal);

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + Section.Inset.Horizontal + captionColumn + fieldColumn + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, height: DialogHeight, widthMeasured: true);
    }

    /// <summary>A card's line saying what it is for, in the small muted face.</summary>
    private static Label CardHint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = AppTheme.TextMuted,
        Font = AppTheme.SmallFont,
    };

    private string CurrentStyleKey() =>(_cmbStyle.SelectedItem as StyleItem)?.Key ?? "print";

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
