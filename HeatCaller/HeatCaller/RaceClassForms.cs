namespace HeatCaller;

/// <summary>Small add/edit dialog for a single RaceClass.</summary>
public sealed class RaceClassEditForm : Form
{
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtNotes = new();
    private readonly ComboBox _cmbQualifying = new();
    private readonly NumericUpDown _numConsecutive = new();
    private readonly Label _lblConsecutive = new();
    private readonly NumericUpDown _numMainSize = new();
    private readonly NumericUpDown _numBumpUp = new();
    private readonly NumericUpDown _numCountBest = new();
    private readonly CheckBox _chkJumpStart = new();
    private readonly NumericUpDown _numJumpPenalty = new();
    private readonly CheckBox _chkJumpCostsLap = new();
    private readonly Label _lblJumpPenalty = new();
    private readonly CheckBox _chkWarnUninspected = new();

    /// <summary>
    /// The class's colour, shown rather than named. A swatch is the only
    /// honest control for a colour - a hex value in a text box tells a race
    /// director nothing about whether two classes' sheets will be telling
    /// one from the other on a pit table at arm's length.
    /// </summary>
    private readonly Panel _swatch = new();

    /// <summary>
    /// Zero means "follow the name", which is the default and stays the
    /// default until somebody deliberately picks something. Held here
    /// rather than written straight onto Result so that Cancel actually
    /// cancels.
    /// </summary>
    private int _colourArgb;

    /// <summary>Built on first use, shared by the swatch and the button.</summary>
    private ContextMenuStrip? _colourMenu;

    public RaceClass Result { get; private set; }

    /// <summary>The other classes, for refusing a name one of them already has. Null skips the check.</summary>
    private readonly RaceClassStore? _store;

    public RaceClassEditForm(RaceClass? existing, RaceClassStore? store = null)
    {
        Result = existing ?? new RaceClass();
        _store = store;

        Text = existing is null ? "Add Class" : "Edit Class";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        // Tall enough for the Race Format row added under Tech Checklist;
        // 522 was measured before it existed.
        ClientSize = new Size(420, 566);
        Font = AppTheme.BodyFont;

        // Placed further down, once the dialog's final width is known -
        // see the name row after ClientSize is settled.
        var lblName = new Label { Text = "Name:", AutoSize = true, Font = AppTheme.BodyFont };

        _txtName.Font = AppTheme.BodyFont;
        _txtName.Text = Result.Name;

        _colourArgb = Result.ColorArgb;

        _swatch.Tag = AppTheme.KeepBackColor;
        _swatch.BorderStyle = BorderStyle.FixedSingle;
        _swatch.Cursor = Cursors.Hand;
        _swatch.Click += (_, _) => ShowColourMenu(_swatch, new Point(0, _swatch.Height));

        // A derived colour follows the name as it is typed, so the swatch
        // is showing what this class will actually look like rather than
        // what it looked like when the dialog opened.
        _txtName.TextChanged += (_, _) =>
        {
            if (_colourArgb == 0)
            {
                UpdateSwatch();
            }
        };

        var lblNotes = new Label { Text = "Notes:", Location = new Point(16, 50), AutoSize = true };
        _txtNotes.Multiline = true;
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Location = new Point(16, 72);
        _txtNotes.Size = new Size(344, 80);
        _txtNotes.Text = Result.Notes;

        // --- Qualifying rules ---
        var lblQualifying = new Label { Text = "Qualifying:", Location = new Point(16, 165), AutoSize = true };
        _cmbQualifying.Location = new Point(96, 162);
        _cmbQualifying.Width = 304;
        _cmbQualifying.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbQualifying.Items.AddRange(new object[]
        {
            new MethodItem(QualifyingMethod.TotalLaps, "Most laps, then lowest time"),
            new MethodItem(QualifyingMethod.BestConsecutiveLaps, "Best consecutive laps (fastest clean run)"),
        });
        SelectMethod(Result.QualifyingMethod);
        _cmbQualifying.SelectedIndexChanged += (_, _) => UpdateConsecutiveEnabled();

        _lblConsecutive.Text = "Consecutive laps to count:";
        _lblConsecutive.Location = new Point(16, 199);
        _lblConsecutive.AutoSize = true;

        _numConsecutive.Location = new Point(200, 196);
        _numConsecutive.Width = 60;
        _numConsecutive.Minimum = 2;
        _numConsecutive.Maximum = 20;
        _numConsecutive.Value = Math.Clamp(Result.ConsecutiveLapCount, 2, 20);

        var lblCountBest = new Label { Text = "Heats that count:", Location = new Point(16, 233), AutoSize = true };
        _numCountBest.Location = new Point(148, 230);
        _numCountBest.Width = 60;
        _numCountBest.Minimum = 1;
        _numCountBest.Maximum = 20;
        _numCountBest.Value = Math.Clamp(Result.CountBestRounds, 1, 20);

        var lblQualHint = new Label
        {
            Text = "Best-consecutive scores the quickest unbroken run of N laps. \"Heats that count\" totals that many of "
                 + "each driver's best heats and drops the rest - set 2 for \"best 2 of 3\". Leave at 1 to count only their single best heat.",
            Location = new Point(16, 262),
            AutoSize = true,
            MaximumSize = new Size(384, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // --- Mains ---
        var lblMains = new Label { Text = "Mains", Location = new Point(16, 316), AutoSize = true, Font = AppTheme.BoldFont };

        var lblMainSize = new Label { Text = "Drivers per main:", Location = new Point(16, 343), AutoSize = true };
        _numMainSize.Location = new Point(148, 340);
        _numMainSize.Width = 60;
        _numMainSize.Minimum = 2;
        _numMainSize.Maximum = 40;
        _numMainSize.Value = Math.Clamp(Result.MainSize, 2, 40);

        var lblBumpUp = new Label { Text = "Bump up:", Location = new Point(226, 343), AutoSize = true };
        _numBumpUp.Location = new Point(300, 340);
        _numBumpUp.Width = 60;
        _numBumpUp.Minimum = 0;
        _numBumpUp.Maximum = 10;
        _numBumpUp.Value = Math.Clamp(Result.BumpUpCount, 0, 10);

_chkJumpStart.Text = "Detect jump starts (crossing before the green)";
        _chkJumpStart.Location = new Point(16, 410);
        _chkJumpStart.AutoSize = true;
        _chkJumpStart.Checked = Result.DetectJumpStarts;
        _chkJumpStart.CheckedChanged += (_, _) => UpdateJumpStartEnabled();

        _lblJumpPenalty.Text = "Automatic penalty:";
        _lblJumpPenalty.Location = new Point(36, 439);
        _lblJumpPenalty.AutoSize = true;

        _numJumpPenalty.Location = new Point(176, 436);
        _numJumpPenalty.Width = 60;
        _numJumpPenalty.Minimum = 0;
        _numJumpPenalty.Maximum = 120;
        _numJumpPenalty.Value = Math.Clamp(Result.JumpStartPenaltySeconds, 0, 120);

        var lblJumpUnit = new Label { Text = "seconds (0 = flag only)", Location = new Point(244, 439), AutoSize = true };

        _chkJumpCostsLap.Text = "A jump start also costs the lap";
        _chkJumpCostsLap.Location = new Point(36, 468);
        _chkJumpCostsLap.AutoSize = true;
        _chkJumpCostsLap.Checked = Result.JumpStartCostsTheLap;

        // The setting the Tech Log has always promised and nothing showed:
        // stored on every class (on by default) but with no way to turn it
        // off. Race Control reads it when a heat of this class is staged.
        _chkWarnUninspected.Text = "Warn when a driver in a heat hasn't passed tech today";
        _chkWarnUninspected.Location = new Point(16, 498);
        _chkWarnUninspected.AutoSize = true;
        _chkWarnUninspected.Checked = Result.WarnOnUninspected;

        var warnTip = new ToolTip();
        AppTheme.StyleToolTip(warnTip);
        warnTip.SetToolTip(_chkWarnUninspected,
            "When a heat of this class is staged, Race Control's log lists anyone on its roster without a passing "
            + "inspection in the Tech Log, and the Next Up bar counts them. A warning only - the heat still runs. "
            + "Only applies once tech is being run for the class: it has a checklist, or someone has been inspected in it today.");

        var lblMainsHint = new Label
        {
            Text = "Qualifying is split into A/B/C mains of this size. Bump up is how many finishers move up a tier - set 0 to disable.",
            Location = new Point(16, 376),
            AutoSize = true,
            MaximumSize = new Size(384, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

var btnTechList = new Button { Text = "Tech Checklist..." };
        btnTechList.Click += (_, _) => EditTechChecklist();

        // The class's race format - limits, start procedure, streak rules.
        // Opens the shared editor; "untick custom" hands the class back to
        // the app default rather than freezing a copy of it.
        var btnFormat = new Button { Text = "Race Format..." };
        btnFormat.Click += (_, _) =>
        {
            using var form = new RaceFormatForm(
                Result.Format,
                $"Race Format - {(string.IsNullOrWhiteSpace(_txtName.Text) ? "this class" : _txtName.Text)}",
                allowUseDefault: true);

            if (form.ShowDialog(this) == DialogResult.OK)
            {
                Result.Format = form.Result;
            }
        };

        // Qualifiers get their own, falling back to the race format above.
        // Five-minute quals and eight-minute mains is the commonest club
        // shape there is, and it should not take a per-round override.
        var btnQualFormat = new Button { Text = "Qualifier Format..." };
        btnQualFormat.Click += (_, _) =>
        {
            using var form = new RaceFormatForm(
                Result.QualifierFormat,
                $"Qualifier Format - {(string.IsNullOrWhiteSpace(_txtName.Text) ? "this class" : _txtName.Text)}",
                allowUseDefault: true,
                fallback: Result.Format);

            if (form.ShowDialog(this) == DialogResult.OK)
            {
                Result.QualifierFormat = form.Result;
            }
        };

        // The visible door to the colour. The swatch beside the name is the
        // shortcut; this is what somebody finds without knowing to click a
        // coloured square.
        var btnColour = new Button { Text = "Colour..." };
        btnColour.Click += (_, _) => ShowColourMenu(btnColour, new Point(0, btnColour.Height));

        // What a sanctioning body says, as far as it could be read - and,
        // just as importantly, what it does not say.
        var btnPreset = new Button { Text = "Sanctioning Preset..." };
        btnPreset.Click += (_, _) => ApplySanctioningPreset();

        // What each warning from Race Control costs in this class's races.
        var btnWarnings = new Button { Text = "Warnings..." };
        btnWarnings.Click += (_, _) =>
        {
            using var form = new WarningLadderForm(
                Result.WarningLadder,
                $"Warnings - {(string.IsNullOrWhiteSpace(_txtName.Text) ? "this class" : _txtName.Text)}");

            if (form.ShowDialog(this) == DialogResult.OK)
            {
                Result.WarningLadder = form.Result;
            }
        };

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += BtnSave_Click;
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Two rows with two jobs: the class's sub-editors on one, the exit
        // buttons on the other, bottom-right where a dialog keeps them.
        // These used to share rows at hand-picked x's, which is why they
        // never lined up with each other.
        // WRAPPED, not one long row. Five sub-editors at their measured
        // widths come to something like eight hundred pixels, and a dialog
        // that wide to hold one column of fields reads as a mistake. The
        // row takes a second line instead and the dialog grows downwards,
        // which is the direction nobody minds.
        const int featureWidth = 620;

        var featureBottom = UiLayout.FlowButtons(
            16, 530, 16 + featureWidth,
            btnTechList, btnFormat, btnQualFormat, btnColour, btnPreset, btnWarnings);

        ClientSize = new Size(
            Math.Max(420, featureWidth + 32),
            featureBottom + UiLayout.RowGap + UiLayout.ButtonHeight + 16);

        // Everything that should reach the dialog's right edge is placed
        // from that edge rather than from a width typed in when the dialog
        // was narrower. The row of sub-editors above decides how wide this
        // dialog is, and it grows whenever a caption does - which used to
        // leave the name box, the notes box and both hints stranded two
        // thirds of the way across.
        var contentRight = ClientSize.Width - 16;

        lblName.Location = new Point(16, UiLayout.CentreOn(lblName, 15, _txtName.Height));

        _swatch.Size = new Size(38, _txtName.Height);
        _swatch.Location = new Point(contentRight - _swatch.Width, 15);

        _txtName.Location = new Point(UiLayout.After(lblName, AppTheme.BodyFont), 15);
        _txtName.Width = Math.Max(120, _swatch.Left - UiLayout.LabelGap - _txtName.Left);

        _txtNotes.Width = Math.Max(200, contentRight - _txtNotes.Left);
        _cmbQualifying.Width = Math.Max(200, contentRight - _cmbQualifying.Left);

        lblQualHint.MaximumSize = new Size(Math.Max(200, contentRight - lblQualHint.Left), 0);
        lblMainsHint.MaximumSize = new Size(Math.Max(200, contentRight - lblMainsHint.Left), 0);

        btnSave.Size = new Size(UiLayout.WidthFor(btnSave), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);

        var exitTop = featureBottom + UiLayout.RowGap;

        btnCancel.Location = new Point(ClientSize.Width - 16 - btnCancel.Width, exitTop);
        btnSave.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnSave.Width, exitTop);

        Controls.Add(lblName);
        Controls.Add(_txtName);
        Controls.Add(lblNotes);
        Controls.Add(_txtNotes);
        Controls.Add(lblCountBest);
        Controls.Add(_numCountBest);
        Controls.Add(_chkJumpStart);
        Controls.Add(_lblJumpPenalty);
        Controls.Add(_numJumpPenalty);
        Controls.Add(_chkJumpCostsLap);
        Controls.Add(_chkWarnUninspected);
        Controls.Add(lblJumpUnit);
        Controls.Add(lblMains);
        Controls.Add(lblMainSize);
        Controls.Add(_numMainSize);
        Controls.Add(lblBumpUp);
        Controls.Add(_numBumpUp);
        Controls.Add(lblMainsHint);
        Controls.Add(lblQualifying);
        Controls.Add(_cmbQualifying);
        Controls.Add(_lblConsecutive);
        Controls.Add(_numConsecutive);
        Controls.Add(lblQualHint);
        Controls.Add(btnTechList);
        Controls.Add(btnFormat);
        Controls.Add(btnQualFormat);
        Controls.Add(btnColour);
        Controls.Add(btnPreset);
        Controls.Add(btnWarnings);
        Controls.Add(_swatch);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        CancelButton = btnCancel;

        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;
        UpdateConsecutiveEnabled();
        UpdateJumpStartEnabled();
        UpdateSwatch();
        AppTheme.ApplyRecursively(this);
    }


    private QualifyingMethod CurrentMethod() =>
        (_cmbQualifying.SelectedItem as MethodItem)?.Method ?? QualifyingMethod.TotalLaps;

    private void SelectMethod(QualifyingMethod method)
    {
        for (var i = 0; i < _cmbQualifying.Items.Count; i++)
        {
            if (_cmbQualifying.Items[i] is MethodItem item && item.Method == method)
            {
                _cmbQualifying.SelectedIndex = i;
                return;
            }
        }
        _cmbQualifying.SelectedIndex = 0;
    }

    /// <summary>The lap count only means anything for the consecutive method, so it's disabled rather than hidden for the other.</summary>
    private void UpdateConsecutiveEnabled()
    {
        var enabled = CurrentMethod() == QualifyingMethod.BestConsecutiveLaps;
        _numConsecutive.Enabled = enabled;
        _lblConsecutive.ForeColor = enabled ? AppTheme.TextDark : AppTheme.TextMuted;
    }

    private sealed record MethodItem(QualifyingMethod Method, string Label)
    {
        public override string ToString() => Label;
    }


    /// <summary>The penalty only means anything when detection is on, so it's disabled rather than hidden.</summary>

    /// <summary>
    /// Edits this class's scrutineering checklist. Held on the class because
    /// the rules differ between them - a stock class cares about motor and
    /// tyres where a modified one might only check the body.
    /// </summary>

    private void EditTechChecklist()
    {
        using var dialog = new Form
        {
            Text = $"Tech Checklist - {(_txtName.Text.Trim() is { Length: > 0 } n ? n : "class")}",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(420, 420),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = Font,
            BackColor = AppTheme.Background,
            ForeColor = AppTheme.TextDark,
        };

        var lblHint = new Label
        {
            Text = "What a scrutineer checks for this class. Each item becomes an OK / FAIL / not-checked row on the inspection form.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(396, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var list = new ListBox { Location = new Point(12, 60), Size = new Size(270, 300), BackColor = AppTheme.Surface };
        foreach (var item in Result.TechCheckItems)
        {
            list.Items.Add(item);
        }

        var btnAdd = new Button { Text = "Add...", Location = new Point(292, 60), Width = 116 };
        btnAdd.Click += (_, _) =>
        {
            var text = PromptForText("Add check", "What should be checked?", string.Empty);
            if (!string.IsNullOrWhiteSpace(text))
            {
                list.Items.Add(text.Trim());
            }
        };

        var btnEditItem = new Button { Text = "Rename...", Location = new Point(292, 98), Width = 116 };
        btnEditItem.Click += (_, _) =>
        {
            if (list.SelectedIndex < 0) { return; }
            var index = list.SelectedIndex;
            var text = PromptForText("Rename check", "What should be checked?", list.Items[index]?.ToString() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(text))
            {
                list.Items[index] = text.Trim();
                list.SelectedIndex = index;
            }
        };

        var btnRemove = new Button { Text = "Remove", Location = new Point(292, 136), Width = 116 };
        btnRemove.Click += (_, _) =>
        {
            if (list.SelectedIndex >= 0) { list.Items.RemoveAt(list.SelectedIndex); }
        };

        var btnUp = new Button { Text = "Move Up", Location = new Point(292, 186), Width = 116 };
        btnUp.Click += (_, _) => MoveItem(list, -1);

        var btnDown = new Button { Text = "Move Down", Location = new Point(292, 224), Width = 116 };
        btnDown.Click += (_, _) => MoveItem(list, 1);

        var btnPreset = new Button { Text = "Add Common", Location = new Point(292, 274), Width = 116 };
        btnPreset.Click += (_, _) =>
        {
            // A starting point rather than a rulebook - every club differs,
            // so these are only suggestions and all of them are editable.
            foreach (var preset in new[]
                     {
                         "Body secure and undamaged",
                         "Motor legal for class",
                         "Tyres legal for class",
                         "Ride height",
                         "Transponder fitted and working",
                         "No loose parts",
                     })
            {
                if (!list.Items.Cast<object>().Any(i => string.Equals(i?.ToString(), preset, StringComparison.OrdinalIgnoreCase)))
                {
                    list.Items.Add(preset);
                }
            }
        };

        var ok = new Button { Text = "OK", Location = new Point(228, 376), Width = 84, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Location = new Point(320, 376), Width = 88, DialogResult = DialogResult.Cancel };

        dialog.Controls.AddRange(new Control[]
        {
            lblHint, list, btnAdd, btnEditItem, btnRemove, btnUp, btnDown, btnPreset, ok, cancel,
        });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        AppTheme.ApplyRecursively(dialog);

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Result.TechCheckItems = list.Items.Cast<object>().Select(i => i.ToString() ?? string.Empty).ToList();
        }
    }

    private static void MoveItem(ListBox list, int direction)
    {
        var index = list.SelectedIndex;
        var target = index + direction;
        if (index < 0 || target < 0 || target >= list.Items.Count)
        {
            return;
        }

        var item = list.Items[index];
        list.Items.RemoveAt(index);
        list.Items.Insert(target, item);
        list.SelectedIndex = target;
    }

    /// <summary>Small single-line prompt.</summary>
    private string? PromptForText(string title, string prompt, string defaultValue)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(400, 130),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = Font,
            BackColor = AppTheme.Background,
            ForeColor = AppTheme.TextDark,
        };

        var lbl = new Label { Text = prompt, Location = new Point(12, 16), AutoSize = true };
        var txt = new TextBox { Location = new Point(12, 40), Width = 372, Text = defaultValue };
        var ok = new Button { Text = "OK", Location = new Point(206, 82), Width = 84, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Location = new Point(298, 82), Width = 86, DialogResult = DialogResult.Cancel };

        dialog.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        AppTheme.ApplyRecursively(dialog);
        return dialog.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
    }

    /// <summary>
    /// Applies a sanctioning body's rules to this class.
    ///
    /// The preset writes onto Result directly, which is the object Save
    /// assigns the dialog's controls onto - so the controls have to be
    /// brought back into step immediately afterwards. Without that, the
    /// preset appeared to work and then Save quietly put every one of the
    /// old values back over the top of it.
    /// </summary>
    private void ApplySanctioningPreset()
    {
        // Named now rather than on Save, so the preset dialog's title and
        // its confirmation say which class they are about - and put back
        // straight afterwards. Result is the STORED class when editing, so
        // leaving the typed name on it meant a rename that was then
        // cancelled still renamed the class, with none of the drivers,
        // rounds or championships following it. Save is the only place a
        // name is committed.
        var savedName = Result.Name;
        Result.Name = _txtName.Text.Trim();

        using var form = new SanctioningPresetForm(Result, _store?.AppDefaultFormat?.Invoke());

        var applied = form.ShowDialog(this) == DialogResult.OK;

        Result.Name = savedName;

        if (!applied)
        {
            return;
        }

        SelectMethod(Result.QualifyingMethod);

        _numConsecutive.Value = Math.Clamp(Result.ConsecutiveLapCount, _numConsecutive.Minimum, _numConsecutive.Maximum);
        _numCountBest.Value = Math.Clamp(Result.CountBestRounds, _numCountBest.Minimum, _numCountBest.Maximum);
        _numMainSize.Value = Math.Clamp(Result.MainSize, _numMainSize.Minimum, _numMainSize.Maximum);
        _numBumpUp.Value = Math.Clamp(Result.BumpUpCount, _numBumpUp.Minimum, _numBumpUp.Maximum);

        UpdateConsecutiveEnabled();
    }

    /// <summary>What this class's colour is right now, chosen or derived.</summary>
    private Color CurrentColour() =>
        _colourArgb != 0
            ? Color.FromArgb(_colourArgb)
            : RaceClass.DefaultColourFor(_txtName.Text.Trim());

    private void UpdateSwatch() => _swatch.BackColor = CurrentColour();

    /// <summary>
    /// Both colour commands in one list, so the way back to the derived
    /// colour is as visible as the way away from it. A colour picked by
    /// hand and then regretted is otherwise unreachable - there is no
    /// "none" on a colour dialog.
    /// </summary>
    private void ShowColourMenu(Control anchor, Point at)
    {
        // Built once and kept. A menu created per click would have to be
        // disposed somewhere, and the only obvious place - its own Closed
        // event - runs while the menu is still delivering the click that
        // closed it.
        if (_colourMenu is null)
        {
            var menu = new ContextMenuStrip { Font = AppTheme.BodyFont, ShowImageMargin = false };

            var choose = new ToolStripMenuItem("Choose a Colour...", null, (_, _) => ChooseColour());

            var derive = new ToolStripMenuItem("Use the Colour from the Name", null, (_, _) =>
            {
                _colourArgb = 0;
                UpdateSwatch();
            });

            menu.Items.Add(choose);
            menu.Items.Add(derive);

            // Greyed rather than hidden when there is nothing to revert to,
            // so the menu is the same shape every time it opens.
            menu.Opening += (_, _) => derive.Enabled = _colourArgb != 0;

            AppTheme.StyleContextMenu(menu);

            _colourMenu = menu;
        }

        _colourMenu.Show(anchor, at);
    }

    /// <summary>
    /// The colour menu is not in Controls, so nothing else disposes it -
    /// and it owns a window handle like any other control.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _colourMenu?.Dispose();
            _colourMenu = null;
        }

        base.Dispose(disposing);
    }

    private void ChooseColour()
    {
        var derived = RaceClass.DefaultColourFor(_txtName.Text.Trim());

        using var dialog = new ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = CurrentColour(),

            // The name's own colour, one click away in the custom row, so
            // the default is offered rather than only described.
            CustomColors = new[] { ColorTranslator.ToOle(derived) },
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Picking exactly the name's own colour is taken as "follow the
        // name" rather than as a choice that happens to match today - so a
        // class renamed later still re-colours itself.
        _colourArgb = dialog.Color.ToArgb() == derived.ToArgb() ? 0 : dialog.Color.ToArgb();

        UpdateSwatch();
    }

    private void UpdateJumpStartEnabled()
    {
        _numJumpPenalty.Enabled = _chkJumpStart.Checked;
        _chkJumpCostsLap.Enabled = _chkJumpStart.Checked;
        _lblJumpPenalty.ForeColor = _chkJumpStart.Checked ? AppTheme.TextDark : AppTheme.TextMuted;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var name = _txtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            AppDialog.Show(this, "Name is required.", "Missing name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Refused rather than allowed, because everything that refers to a
        // class does it by name, ignoring case: a second "Stock" would share
        // the first one's drivers, heats and championship.
        if (_store is not null && _store.NameTaken(name, Result.Id))
        {
            AppDialog.Show(this, $"There is already a class called \"{name}\". Choose a different name.",
                "Name already used", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Assign onto the existing object rather than building a new one.
        // Rebuilding meant every property had to be re-listed here, and
        // anything missed was silently discarded on save - which is exactly
        // what happened to the tech checklist, edited in its own dialog and
        // then thrown away the moment Save was pressed.
        Result.Name = name;
        Result.Notes = _txtNotes.Text;
        Result.QualifyingMethod = CurrentMethod();
        Result.ConsecutiveLapCount = (int)_numConsecutive.Value;
        Result.CountBestRounds = (int)_numCountBest.Value;
        Result.MainSize = (int)_numMainSize.Value;
        Result.DetectJumpStarts = _chkJumpStart.Checked;
        Result.JumpStartPenaltySeconds = (int)_numJumpPenalty.Value;
        Result.JumpStartCostsTheLap = _chkJumpCostsLap.Checked;
        Result.WarnOnUninspected = _chkWarnUninspected.Checked;
        Result.BumpUpCount = (int)_numBumpUp.Value;
        Result.ColorArgb = _colourArgb;
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>
/// The classes list as a dialog, for the places that reach it from
/// somewhere else - Driver Profiles, where you are naming a driver's class
/// and realise the class does not exist yet.
///
/// A SHELL around the page, not a second copy of it. There were two lists
/// of classes with two sets of Add/Edit/Delete before this, and the moment
/// the page grew a Sanctioning Preset button the dialog did not have one -
/// which is precisely the kind of drift that makes two doors into two
/// different rooms.
/// </summary>
public sealed class RaceClassManagerForm : Form
{
    public RaceClassManagerForm(RaceClassStore store, UndoService? undo = null)
    {
        Text = "Race Classes";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(940, 560);
        MinimumSize = new Size(760, 480);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        // PLACED, not docked. A Fill child and a Bottom child in the same
        // form resolve by z-order, and which way round that goes is the
        // one WinForms rule this app has been bitten by often enough to
        // stop relying on. Two rectangles, set from one size, in one
        // place.
        var page = new RaceClassesControl(store, undo) { Dock = DockStyle.None };

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK, Font = AppTheme.BodyFont };

        btnClose.Size = new Size(UiLayout.WidthFor(btnClose), UiLayout.ButtonHeight);

        void Place()
        {
            var barHeight = UiLayout.ButtonHeight + UiLayout.RowGap * 2;

            page.Bounds = new Rectangle(0, 0, ClientSize.Width, Math.Max(200, ClientSize.Height - barHeight));

            btnClose.Location = new Point(
                Math.Max(UiLayout.PageLeft, ClientSize.Width - UiLayout.PageRight - btnClose.Width),
                page.Bottom + UiLayout.RowGap);
        }

        Controls.Add(page);
        Controls.Add(btnClose);

        Resize += (_, _) => Place();
        Place();

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);
    }
}
