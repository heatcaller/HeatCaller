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
/// </summary>
public sealed class LookEditorForm : Form
{
    private const int Inset = 16;

    /// <summary>The key never changes: an edit replaces the look with the same key, a new look arrives with a fresh one.</summary>
    private readonly string _key;

    private readonly TextBox _txtName = new();
    private readonly TextBox _txtDescription = new();
    private readonly ComboBox _cmbStyle = new();
    private readonly LookPreview _preview = new();
    private readonly Label _lblWarnings = new();
    private readonly List<ColourRow> _rows = new();

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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        BuildLayout(look);
        UpdatePreview();

        // Every row is placed from measured fonts above; the dialog repair
        // pass would only nudge the columns out of line.
        LayoutDoctor.LeaveAlone(this);
        AppTheme.ApplyRecursively(this);
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

    private void BuildLayout(Look look)
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

        var body = AppTheme.BodyFont;

        var lblName = new Label { Text = "Name:", AutoSize = true, Font = body };
        var lblDescription = new Label { Text = "Description:", AutoSize = true, Font = body };
        var lblStyle = new Label { Text = "Style:", AutoSize = true, Font = body };

        var colourLabels = definitions.Select(d => new Label { Text = d.Caption + ":", AutoSize = true, Font = body }).ToList();

        // One caption column for every row, measured in the font it is
        // drawn in, so the swatches and boxes line up down the page.
        var captionWidth = new[] { lblName, lblDescription, lblStyle }.Concat(colourLabels)
            .Max(l => TextRenderer.MeasureText(l.Text, body).Width);

        var fieldLeft = Inset + captionWidth + UiLayout.LabelGap;

        const int swatchWidth = 46;
        var hexWidth = TextRenderer.MeasureText("#DDDDDD", body).Width + 28;
        var colourRight = fieldLeft + swatchWidth + UiLayout.LabelGap + hexWidth;
        var textWidth = Math.Max(300, colourRight - fieldLeft);
        var leftColumnRight = fieldLeft + textWidth;

        var y = Inset;

        _txtName.Font = body;
        _txtName.Width = textWidth;
        _txtName.MaxLength = 40;
        _txtName.Text = look.Name;
        _txtName.TextChanged += (_, _) => UpdatePreview();
        y = Row(lblName, _txtName, fieldLeft, y);

        _txtDescription.Font = body;
        _txtDescription.Width = textWidth;
        _txtDescription.MaxLength = 200;
        _txtDescription.Text = look.Description;
        _txtDescription.PlaceholderText = "Optional - a line about what it's for";
        y = Row(lblDescription, _txtDescription, fieldLeft, y);

        _cmbStyle.Font = body;
        _cmbStyle.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbStyle.Width = Math.Min(textWidth, 240);

        foreach (var (label, _) in Styles)
        {
            _cmbStyle.Items.Add(label);
        }

        var familyIndex = Array.FindIndex(Styles, s => s.Family == look.Family);
        _cmbStyle.SelectedIndex = familyIndex < 0 ? 1 : familyIndex;
        _cmbStyle.SelectedIndexChanged += (_, _) => UpdatePreview();

        lblStyle.Location = new Point(Inset, UiLayout.CentreOn(lblStyle, y, _cmbStyle.Height));
        _cmbStyle.Location = new Point(fieldLeft, y);
        y = UiLayout.Below(_cmbStyle, UiLayout.HintGap);

        var lblStyleHint = new Label
        {
            Text = "Light or Dark sets how phones and the online pages treat the page. Synthwave effects adds the "
                 + "animated sunset header and the neon race clock.",
            Location = new Point(fieldLeft, y),
            AutoSize = true,
            MaximumSize = new Size(textWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        y = UiLayout.Below(lblStyleHint, UiLayout.RowGap + 4);

        var lblColours = new Label { Text = "Colours", AutoSize = true, Font = AppTheme.BoldFont, Location = new Point(Inset, y) };
        y = UiLayout.Below(lblColours, UiLayout.HintGap + 2);

        Controls.AddRange(new Control[] { lblName, _txtName, lblDescription, _txtDescription, lblStyle, _cmbStyle, lblStyleHint, lblColours });

        for (var i = 0; i < definitions.Length; i++)
        {
            var (caption, get, set) = definitions[i];
            var label = colourLabels[i];

            var box = new TextBox
            {
                Font = body,
                Width = hexWidth,
                MaxLength = 7,
                CharacterCasing = CharacterCasing.Upper,
                AccessibleName = caption,
            };

            var swatch = new ColourSwatch
            {
                Size = new Size(swatchWidth, box.Height),
                AccessibleName = $"Pick the {caption.ToLowerInvariant()} colour",
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

            label.Location = new Point(Inset, UiLayout.CentreOn(label, y, box.Height));
            swatch.Location = new Point(fieldLeft, y);
            box.Location = new Point(fieldLeft + swatchWidth + UiLayout.LabelGap, y);

            // Tab goes caption-free: swatch, then its box, row by row.
            Controls.Add(label);
            Controls.Add(swatch);
            Controls.Add(box);

            _rows.Add(row);
            y = UiLayout.Below(box, 6);
        }

        var leftBottom = y;

        // --- Right column: the preview and what might be hard to read ---
        var rightLeft = leftColumnRight + 28;
        const int rightWidth = 420;

        var lblPreview = new Label { Text = "Preview", AutoSize = true, Font = AppTheme.BoldFont, Location = new Point(rightLeft, Inset) };

        _preview.Location = new Point(rightLeft, UiLayout.Below(lblPreview, UiLayout.HintGap + 2));
        _preview.Size = new Size(rightWidth, 250);

        var lblReadability = new Label
        {
            Text = "Readability",
            AutoSize = true,
            Font = AppTheme.BoldFont,
            Location = new Point(rightLeft, UiLayout.Below(_preview, UiLayout.RowGap + 4)),
        };

        _lblWarnings.Location = new Point(rightLeft, UiLayout.Below(lblReadability, UiLayout.HintGap + 2));
        _lblWarnings.AutoSize = false;
        _lblWarnings.Font = AppTheme.SmallFont;
        _lblWarnings.Size = new Size(rightWidth, Math.Max(AppTheme.SmallFont.Height * 8, leftBottom - _lblWarnings.Top));

        Controls.Add(lblPreview);
        Controls.Add(_preview);
        Controls.Add(lblReadability);
        Controls.Add(_lblWarnings);

        var contentBottom = Math.Max(leftBottom, _lblWarnings.Bottom + UiLayout.RowGap);
        var right = rightLeft + rightWidth;

        var btnSave = new Button { Text = "Save", Font = body };
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button { Text = "Cancel", Font = body, DialogResult = DialogResult.Cancel };

        // Right-aligned, Save first, as in the app's other dialogs.
        var saveWidth = UiLayout.WidthFor(btnSave);
        var cancelWidth = UiLayout.WidthFor(btnCancel);
        btnCancel.Size = new Size(cancelWidth, UiLayout.ButtonHeight);
        btnSave.Size = new Size(saveWidth, UiLayout.ButtonHeight);
        btnCancel.Location = new Point(right - cancelWidth, contentBottom + UiLayout.RowGap);
        btnSave.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - saveWidth, btnCancel.Top);

        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        ClientSize = new Size(right + Inset, btnCancel.Bottom + Inset);

        // Tab order follows the page: name, description, style, then each
        // colour's swatch and box, then Save and Cancel.
        var order = 0;
        _txtName.TabIndex = order++;
        _txtDescription.TabIndex = order++;
        _cmbStyle.TabIndex = order++;

        foreach (var row in _rows)
        {
            row.Swatch.TabIndex = order++;
            row.Box.TabIndex = order++;
        }

        btnSave.TabIndex = order++;
        btnCancel.TabIndex = order;
    }

    private static int Row(Label caption, Control field, int fieldLeft, int y)
    {
        caption.Location = new Point(Inset, UiLayout.CentreOn(caption, y, field.Height));
        field.Location = new Point(fieldLeft, y);
        return UiLayout.Below(field, UiLayout.RowGap);
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
            problems.Add($"\"{row.Caption}\" isn't a colour yet - use a code like #1A2B3C.");
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
                AppDialog.Show(this, $"\"{row.Caption}\" needs a colour code like #1A2B3C, or pick one with the swatch beside it.",
                    "Colour needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
