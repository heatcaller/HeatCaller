namespace HeatCaller;

/// <summary>
/// Makes or changes one of the club's own looks: a name, a style, and the
/// eleven colours every screen is painted from.
///
/// Only ever handed a custom look - a built-in is copied into a new one by
/// the Looks tab before it gets here, so nothing the app ships can be
/// changed. The look is saved here (Looks.SaveCustom) when Save is pressed,
/// and the dialog stays open if the save fails, so a club never loses an
/// evening's colour-picking to a locked file.
///
/// Warnings about hard-to-read combinations are shown, never enforced: a
/// club can save a look that fails them - it's their wall.
///
/// On the layout pieces (DialogFit): down the left, one field grid - the
/// name, the description, the style and the note under it, then the colours
/// under their heading, each a swatch and its code beside its caption - so
/// every caption is in one column and the swatches and boxes line up down
/// the window. Down the right, beside it (a Beside), the preview under its
/// heading, as big as it was made at the normal text size, at the size in
/// use, and what might be hard to read under that, down to the foot of the
/// colours. The two are the window's body, which scrolls on a screen too
/// short for them, and Save and Cancel stay in view under it. The preview is
/// drawn as it always was (LookPreview). It used to be placed in pixels:
/// each row stepped down from the one above, the fields at an x worked out
/// from the widest caption and a fixed 300px wide, the right-hand column a
/// fixed 420px beside them with the preview a fixed 250px tall whatever the
/// text size - at 200% too short for what it draws - and the window sized
/// to the two, with nothing to keep it to the screen.
/// </summary>
public sealed class LookEditorForm : Form
{
    /// <summary>
    /// How wide the window is inside its frame at the normal text size - what
    /// the old one worked out there from its two columns: the captions, the
    /// 300px fields, and the 420px preview beside them.
    /// </summary>
    private const int DialogWidth = 980;

    /// <summary>The preview's size at the normal text size, as it was made. It is drawn in the app's own faces, so it grows with the text size.</summary>
    private const int PreviewWidth = 420;
    private const int PreviewHeight = 250;

    /// <summary>The style picker's width at the normal text size, as it was - wider when its longest choice needs it.</summary>
    private const int StyleWidth = 240;

    /// <summary>A colour's swatch across at the normal text size, as it was. It is as tall as the code box beside it.</summary>
    private const int SwatchWidth = 46;

    /// <summary>Lines kept for what might be hard to read, whatever it says - the eight it always had at the least.</summary>
    private const int WarningLines = 8;

    /// <summary>
    /// What the fields keep between themselves and the preview, on top of the
    /// row's gap the Beside keeps between its two sides: a section's gap in
    /// all, about the room there was between the two columns.
    /// </summary>
    private const int FieldsGap = UiLayout.SectionGap - UiLayout.RowGap;

    /// <summary>The key never changes: an edit replaces the look with the same key, a new look arrives with a fresh one.</summary>
    private readonly string _key;

    private readonly TextBox _txtName = new();
    private readonly TextBox _txtDescription = new();
    private readonly ComboBox _cmbStyle = new();
    private readonly LookPreview _preview = new();
    private readonly Label _lblWarnings = new();
    private readonly List<ColourRow> _rows = new();

    /// <summary>The fields beside the preview and the warnings - laid out again when the warnings are rewritten (UpdatePreview). Null only while the window is being built.</summary>
    private Beside? _band;

    /// <summary>
    /// The least width the fields can do with, at the text size in use, the
    /// gap before the preview included - worked out once the theme pass has
    /// been (SizeFields); 0 until then. See PreviewColumnWidth.
    /// </summary>
    private int _fieldsLeast;

    /// <summary>The saved look, once Save has succeeded.</summary>
    public Look? Result { get; private set; }

    /// <summary>The styles offered, in combo order, with the family each one stores.</summary>
    private static readonly (string Label, string Family)[] Styles =
    {
        ("Light", Look.FamilyLight),
        ("Dark", Look.FamilyDark),
        ("Synthwave effects", Look.FamilySynthwave),
    };

    public LookEditorForm(Look look, bool isNew)
    {
        _key = look.Key;

        Text = isNew ? "New Look" : $"Edit Look - {look.Name}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var (page, body) = BuildLayout(look);
        UpdatePreview();

        AppTheme.ApplyRecursively(this);

        // In the faces the theme pass leaves them in - see SizeFields.
        SizeFields();

        // As tall as the fields and the preview need - the body scrolling on
        // a screen too short for them - and the width it had.
        DialogFit.Keep(this, page, width: DialogWidth, body: body);
    }

    /// <summary>One colour: its caption, the swatch that opens the colour picker, and the hex box.</summary>
    private sealed class ColourRow
    {
        public required string Caption { get; init; }
        public required Func<LookColors, string> Get { get; init; }
        public required Action<LookColors, string> Set { get; init; }
        public required ColourSwatch Swatch { get; init; }
        public required TextBox Box { get; init; }

        /// <summary>The last value that was a real colour - what the preview keeps showing while the box holds something half-typed.</summary>
        public string LastGood { get; set; } = "#000000";
    }

    /// <summary>Builds the window's content, as the class notes describe, and returns its page and the body in it.</summary>
    private (FillPage Page, PageStack Body) BuildLayout(Look look)
    {
        var colours = look.Colors ?? new LookColors();

        // Plain-English captions, in the order someone builds a look: the
        // big surfaces first, then the colours that sit on them.
        var definitions = new (string Caption, Func<LookColors, string> Get, Action<LookColors, string> Set)[]
        {
            ("Page background", c => c.Bg, (c, v) => c.Bg = v),
            ("Cards", c => c.Surface, (c, v) => c.Surface = v),
            ("Alternate rows", c => c.RowAlt, (c, v) => c.RowAlt = v),
            ("Primary (buttons, headings)", c => c.Primary, (c, v) => c.Primary = v),
            ("Text on primary", c => c.OnPrimary, (c, v) => c.OnPrimary = v),
            ("Accent (links, highlights)", c => c.Accent, (c, v) => c.Accent = v),
            ("Text", c => c.Text, (c, v) => c.Text = v),
            ("Hints", c => c.Muted, (c, v) => c.Muted = v),
            ("Lines", c => c.Border, (c, v) => c.Border = v),
            ("Good (gains, OK)", c => c.Good, (c, v) => c.Good = v),
            ("Bad (errors, losses)", c => c.Bad, (c, v) => c.Bad = v),
        };

        var bodyFont = AppTheme.BodyFont;

        _txtName.Font = bodyFont;
        _txtName.MaxLength = 40;
        _txtName.Text = look.Name;
        _txtName.TextChanged += (_, _) => UpdatePreview();

        _txtDescription.Font = bodyFont;
        _txtDescription.MaxLength = 200;
        _txtDescription.Text = look.Description;
        _txtDescription.PlaceholderText = "Optional - a line about what it's for";

        _cmbStyle.Font = bodyFont;
        _cmbStyle.DropDownStyle = ComboBoxStyle.DropDownList;

        foreach (var (label, _) in Styles)
        {
            _cmbStyle.Items.Add(label);
        }

        var familyIndex = Array.FindIndex(Styles, s => s.Family == look.Family);
        _cmbStyle.SelectedIndex = familyIndex < 0 ? 1 : familyIndex;
        _cmbStyle.SelectedIndexChanged += (_, _) => UpdatePreview();

        // One caption column for every row - the name's, the style's and each
        // colour's - as wide as the widest caption in the font it is drawn
        // in, so the swatches and boxes line up down the window. The name and
        // the description run to the grid's right-hand edge.
        var fields = new FieldGrid();
        fields.AddRow("Name:", _txtName, stretch: true);
        fields.AddRow("Description:", _txtDescription, stretch: true);
        fields.AddRow("Style:", _cmbStyle);
        fields.AddHint("Light or Dark sets how phones and the online pages treat the page. Synthwave effects adds the "
                       + "animated sunset header and the neon race clock.");

        var lblColours = fields.AddWide(new Label { Text = "Colors", AutoSize = true, Font = AppTheme.BoldFont }, stretch: false);

        // Nearer the colours it heads than the note above it, as it was: a
        // row's gap and a hint's above it, a hint's and two more below.
        lblColours.Margin = new Padding(0, UiLayout.HintGap, 0, UiLayout.HintGap + 2);

        foreach (var (caption, get, set) in definitions)
        {
            // Its width is set once the theme pass is done (SizeFields).
            var box = new TextBox
            {
                Font = bodyFont,
                MaxLength = 7,
                CharacterCasing = CharacterCasing.Upper,
                AccessibleName = caption,
            };

            // Sized once the theme pass has given the box beside it its
            // height (SizeFields).
            var swatch = new ColourSwatch
            {
                AccessibleName = $"Pick the {caption.ToLowerInvariant()} color",
            };

            var row = new ColourRow { Caption = caption, Get = get, Set = set, Swatch = swatch, Box = box };
            var start = Looks.IsHex(get(colours)) ? get(colours).ToUpperInvariant() : get(Looks.FamilyBase(look.Family).Colors);
            row.LastGood = start;
            box.Text = start;
            Looks.TryParse(start, out var startColour);
            swatch.Colour = startColour;

            box.TextChanged += (_, _) => OnHexChanged(row);

            // A pasted "1A2B3C" gets its # on the way out of the box, so
            // what is typed and what is saved read the same.
            box.Leave += (_, _) =>
            {
                if (Normalise(box.Text) is { } hex && hex != box.Text)
                {
                    box.Text = hex;
                }
            };

            swatch.Click += (_, _) => PickColour(row);

            // The swatch, then its box, beside the caption. Tab goes
            // caption-free: swatch, then its box, row by row.
            fields.AddRow(caption + ":", FieldGrid.Inline(swatch, box));

            _rows.Add(row);
        }

        // --- Down the right: the preview, and what might be hard to read ---
        _preview.Height = UiLayout.AtTextSize(PreviewHeight);
        _lblWarnings.Font = AppTheme.SmallFont;

        // As tall as the taller of its two sides, in the body - see the class
        // notes. The preview's column is as wide as PreviewColumnWidth says.
        var band = new Beside(() => PreviewColumnWidth(_band?.Width ?? 0))
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        band.Add(fields, gapBelow: 0);
        fields.Margin = new Padding(0, 0, FieldsGap, 0);

        band.AddSide(Heading("Preview"), gapBelow: UiLayout.HintGap + 2);
        band.AddSide(_preview, gapBelow: UiLayout.RowGap + 4);
        band.AddSide(Heading("Readability"), gapBelow: UiLayout.HintGap + 2);

        // Whatever it says, wrapped to the column - never less than the lines
        // kept for it - and down to the foot of the colours when they come
        // further down the window. Rewritten as colours are typed, and laid
        // out again then (UpdatePreview).
        band.AddSideFill(
            _lblWarnings,
            width => Math.Max(
                WarningLines * Pieces.LineHeight(_lblWarnings.Font),
                _lblWarnings.GetPreferredSize(new Size(width, 0)).Height),
            gapBelow: 0);

        _band = band;

        var btnSave = new Button { Text = "Save", Font = bodyFont };
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button { Text = "Cancel", Font = bodyFont, DialogResult = DialogResult.Cancel };

        var page = new FillPage();

        // The fields and the preview: on a screen too short for them -
        // 1280x720, or any at a larger text size - they scroll, and Save and
        // Cancel stay in view under them.
        var body = DialogFit.Body(page);
        body.Add(band, gapBelow: 0);

        // Right-aligned, Save first, as in the app's other dialogs: Cancel
        // furthest right. Tab still reaches Save before Cancel - the bar
        // takes them right to left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        // Tab order follows the page, as it did: name, description, style,
        // then each colour's swatch and box, then Save and Cancel - the order
        // the rows are added to the grid in, and the fields before the
        // buttons.
        return (page, body);

        // A heading over the preview or over the warnings: one line of the
        // bold face. The column gives it its width, and this its height.
        static Label Heading(string text)
        {
            var heading = new Label { Text = text, Font = AppTheme.BoldFont };
            heading.Height = heading.GetPreferredSize(Size.Empty).Height;
            return heading;
        }
    }

    /// <summary>
    /// The fields' sizes, in the faces the theme pass left them in: the style
    /// picker the width it had at the normal text size, at the size in use,
    /// or as wide as its longest choice needs; each code box as wide as a
    /// colour code in its font, with room either side, as it was; and each
    /// swatch the width it had at the normal text size, at the size in use,
    /// and as tall as the box beside it. Then the least the fields can do
    /// with across (see PreviewColumnWidth), from those widths - kept in
    /// locals, never read back from the controls. Once: the dialog is built
    /// at the text size in use and is not kept in step with a change made
    /// while it is up (see DialogFit).
    /// </summary>
    private void SizeFields()
    {
        (Font Font, int Count, int Width)? measured = null;
        var styleWidth = Math.Max(UiLayout.AtTextSize(StyleWidth), Pieces.PickerWidth(_cmbStyle, ref measured));
        _cmbStyle.Width = styleWidth;

        var swatchWidth = UiLayout.AtTextSize(SwatchWidth);
        var hexWidth = TextRenderer.MeasureText("#DDDDDD", Font).Width + 28;

        foreach (var row in _rows)
        {
            row.Box.Width = hexWidth;

            // The box's height is the theme's, from its font (InputOutline),
            // and settled by now.
            row.Swatch.Size = new Size(swatchWidth, row.Box.Height);
        }

        // The caption column as the grid works it out: the widest caption and
        // the gap after it.
        var captionColumn = new[] { "Name:", "Description:", "Style:" }
            .Concat(_rows.Select(row => row.Caption + ":"))
            .Max(caption => TextRenderer.MeasureText(caption, Font).Width) + UiLayout.LabelGap;

        // A colour's row on one line, as its ButtonBar counts it: the swatch
        // and the gap after it, the box with a pixel either side for its ring,
        // and the gap after that.
        var colourRow = swatchWidth + UiLayout.ButtonGap + 1 + hexWidth + 1 + UiLayout.ButtonGap;

        _fieldsLeast = captionColumn + Math.Max(styleWidth, colourRow) + FieldsGap;
    }

    /// <summary>
    /// The preview's column: as wide as the preview was made at the normal
    /// text size, at the size in use - or, in a window too narrow for that
    /// beside the least the fields can do with (a larger text size on a
    /// small screen, where DialogFit keeps the window to the screen), what
    /// the fields leave it. The fields come first: they keep their least,
    /// and the preview - a drawn sample, readable narrower - gives way.
    /// </summary>
    private int PreviewColumnWidth(int band)
    {
        var made = UiLayout.AtTextSize(PreviewWidth);

        if (band <= 0 || _fieldsLeast <= 0)
        {
            return made;
        }

        return Math.Clamp(band - UiLayout.RowGap - _fieldsLeast, 1, made);
    }

    /// <summary>"#1a2b3c", "1A2B3C" and " #1A2B3C " all come back as "#1A2B3C"; anything else as null.</summary>
    private static string? Normalise(string? text)
    {
        var t = (text ?? string.Empty).Trim();

        if (t.Length == 6)
        {
            t = "#" + t;
        }

        return Looks.IsHex(t) ? t.ToUpperInvariant() : null;
    }

    private void OnHexChanged(ColourRow row)
    {
        if (Normalise(row.Box.Text) is { } hex && Looks.TryParse(hex, out var colour))
        {
            row.LastGood = hex;
            row.Swatch.Colour = colour;
            row.Box.ForeColor = AppTheme.TextDark;
        }
        else
        {
            // Left as typed - it may be half-way to a colour - but marked,
            // and listed under Readability until it is one.
            row.Box.ForeColor = AppTheme.Danger;
        }

        UpdatePreview();
    }

    private void PickColour(ColourRow row)
    {
        using var dialog = new ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = row.Swatch.Colour,

            // The look's own colours as the custom swatches, so matching
            // one colour to another is a click rather than retyping a code.
            // Windows wants these as 0x00BBGGRR.
            CustomColors = _rows
                .Select(r => Looks.TryParse(r.LastGood, out var c) ? c.R | (c.G << 8) | (c.B << 16) : 0xFFFFFF)
                .ToArray(),
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Through the box, so the swatch, the preview and the warnings all
        // follow from the one place a colour is held.
        row.Box.Text = Looks.ToHex(dialog.Color);
    }

    private string SelectedFamily() =>
        _cmbStyle.SelectedIndex >= 0 && _cmbStyle.SelectedIndex < Styles.Length
            ? Styles[_cmbStyle.SelectedIndex].Family
            : Look.FamilyDark;

    /// <summary>The colours as they stand, using each row's last real colour where its box is mid-edit.</summary>
    private LookColors CurrentColours()
    {
        var colours = new LookColors();

        foreach (var row in _rows)
        {
            row.Set(colours, row.LastGood);
        }

        return colours;
    }

    private void UpdatePreview()
    {
        // Called while the rows are still being built (the name box's first
        // TextChanged); the constructor calls it again once they all exist.
        if (_rows.Count == 0)
        {
            return;
        }

        var name = _txtName.Text.Trim();
        var look = new Look
        {
            Key = _key,
            Name = name.Length == 0 ? "New look" : name,
            Family = SelectedFamily(),
            Colors = CurrentColours(),
        };

        _preview.Look = Looks.Resolve(look);

        var problems = new List<string>();

        foreach (var row in _rows.Where(r => Normalise(r.Box.Text) is null))
        {
            problems.Add($"\"{row.Caption}\" isn't a color yet - use a code like #1A2B3C.");
        }

        problems.AddRange(Looks.ReadabilityWarnings(look.Colors));

        if (problems.Count == 0)
        {
            _lblWarnings.Text = "Everything reads comfortably.";
            _lblWarnings.ForeColor = AppTheme.TextMuted;
        }
        else
        {
            _lblWarnings.Text = string.Join("\n", problems.Select(p => "• " + p))
                + "\n\nYou can still save it - these are only warnings.";
            _lblWarnings.ForeColor = AppTheme.Danger;
        }

        // The warnings are sized by the band beside the fields, not by
        // themselves, so being rewritten does not lay anything out - and a
        // longer list can need the column taller than the colours, and the
        // body taller with it (Beside.AddSideFill).
        _band?.PerformLayout();
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var name = _txtName.Text.Trim();

        if (name.Length == 0)
        {
            AppDialog.Show(this, "Give the look a name first.", "Name needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _txtName.Focus();
            return;
        }

        var colours = new LookColors();

        foreach (var row in _rows)
        {
            if (Normalise(row.Box.Text) is not { } hex)
            {
                AppDialog.Show(this, $"\"{row.Caption}\" needs a color code like #1A2B3C, or pick one with the swatch beside it.",
                    "Color needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
                row.Box.Focus();
                row.Box.SelectAll();
                return;
            }

            row.Set(colours, hex);
        }

        var look = new Look
        {
            Key = _key,
            Name = name,
            Description = _txtDescription.Text.Trim(),
            Family = SelectedFamily(),
            Colors = colours,
            IsBuiltIn = false,
        };

        if (!Looks.SaveCustom(look))
        {
            AppDialog.Show(this,
                "The look could not be saved. Check the app's data folder isn't read-only or full, then try again.",
                "Look not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // What was saved, as Looks keeps it (trimmed, capped).
        Result = Looks.Find(_key) ?? look;
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// A clickable colour chip that opens the colour picker.
    ///
    /// Not a Button: the theming pass restyles every Button in the app in
    /// the theme's own colours, and this one has to show the look's.
    /// Focusable, with Space to open it, so the whole dialog works from the
    /// keyboard.
    /// </summary>
    private sealed class ColourSwatch : Control
    {
        private Color _colour = Color.Black;

        public ColourSwatch()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw
                     | ControlStyles.Selectable, true);

            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }

        public Color Colour
        {
            get => _colour;
            set
            {
                _colour = value;
                Invalidate();
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);

            if (e.KeyCode == Keys.Space)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (var back = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
            {
                g.FillRectangle(back, ClientRectangle);
            }

            var chip = new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f);

            using var path = AppTheme.RoundedRect(chip, 5);
            using var fill = new SolidBrush(_colour);
            using var edge = new Pen(Focused ? AppTheme.Primary : AppTheme.Border, Focused ? 2f : 1f);

            g.FillPath(fill, path);
            g.DrawPath(edge, path);
        }
    }
}
