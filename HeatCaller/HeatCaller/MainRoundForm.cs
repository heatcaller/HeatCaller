namespace HeatCaller;

/// <summary>
/// Dialog for adding a Main round: pick a label, optional track, and check
/// which class members are racing in it. The "select top N" helper uses
/// qualifying-ranked order if available, falling back to roster order.
///
/// On the layout pieces (DialogFit): the label and track lined up in a
/// field grid, the drivers taking the height, the picking buttons under
/// them and Save and Cancel at the foot. It used to be placed in pixels,
/// its captions at a fixed 110px that a larger text size ran past.
/// </summary>
public sealed class MainRoundForm : Form
{
    /// <summary>The drivers show at least this many before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    private readonly TextBox _txtLabel = new();
    private readonly ComboBox _cmbTrack = new();
    private readonly CheckedListBox _clbDrivers = new ThemedCheckedListBox();
    private readonly NumericUpDown _numTopN = new();

    public RoundDefinition? Result { get; private set; }

    public MainRoundForm(string className, List<DriverProfile> classRoster, List<string> rankedTransponders, TrackLayoutStore trackLayouts, string defaultLabel)
    {
        Text = $"Add Main - {className}";
        Font = AppTheme.BodyFont;

        _txtLabel.Text = defaultLabel;

        _cmbTrack.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrack.Items.Add("(none)");
        foreach (var t in trackLayouts.All)
        {
            _cmbTrack.Items.Add(t.Name);
        }
        _cmbTrack.SelectedIndex = 0;

        var lblDrivers = new Label { Text = "Drivers in this main:", AutoSize = true, Font = AppTheme.BoldFont };

        _clbDrivers.CheckOnClick = true;

        // Given the height the window has, not snapped to whole rows - see FillPage.AddFill.
        _clbDrivers.IntegralHeight = false;
        foreach (var d in classRoster)
        {
            var label = string.IsNullOrWhiteSpace(d.DriverName) ? d.Transponder : $"{d.DriverName} ({d.Transponder})";
            _clbDrivers.Items.Add(new DriverListItem(d.Transponder, label));
        }

        var lblTopN = new Label { Text = "Top:", AutoSize = true };
        _numTopN.Minimum = 0;
        _numTopN.Maximum = Math.Max(1, classRoster.Count);
        _numTopN.Value = Math.Min(6, classRoster.Count);

        var btnSelectTopN = new Button { Text = "from Qualifying" };
        btnSelectTopN.Click += (_, _) =>
        {
            var n = (int)_numTopN.Value;
            var order = rankedTransponders.Count > 0 ? rankedTransponders : classRoster.Select(d => d.Transponder).ToList();
            var top = order.Take(n).ToHashSet();
            for (var i = 0; i < _clbDrivers.Items.Count; i++)
            {
                var item = (DriverListItem)_clbDrivers.Items[i]!;
                _clbDrivers.SetItemChecked(i, top.Contains(item.Transponder));
            }
        };

        var btnSelectAll = new Button { Text = "All" };
        btnSelectAll.Click += (_, _) =>
        {
            for (var i = 0; i < _clbDrivers.Items.Count; i++)
            {
                _clbDrivers.SetItemChecked(i, true);
            }
        };

        var btnSelectNone = new Button { Text = "None" };
        btnSelectNone.Click += (_, _) =>
        {
            for (var i = 0; i < _clbDrivers.Items.Count; i++)
            {
                _clbDrivers.SetItemChecked(i, false);
            }
        };

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += BtnSave_Click;
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var fields = new FieldGrid();
        fields.AddRow("Round Label:", _txtLabel, stretch: true);
        fields.AddRow("Track Layout:", _cmbTrack);

        // "Top [6] from Qualifying", and All and None beside it - each only
        // as wide as its caption, so the line fits the dialog's width (see
        // the width given to DialogFit.Keep below).
        var picking = new FieldRow();
        picking.Add(lblTopN, _numTopN);
        picking.Add(null, new ButtonBar(tight: true).With(btnSelectTopN, btnSelectAll, btnSelectNone));

        var page = new FillPage();
        page.Add(fields);
        page.Add(lblDrivers, stretch: false, gapBelow: UiLayout.HintGap);
        page.AddFill(_clbDrivers, _ => LeastRows * _clbDrivers.ItemHeight + 4, gapBelow: UiLayout.RowGap);
        page.Add(picking);

        // Against the right-hand edge, Cancel furthest right, as a dialog's buttons go.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);

        Controls.Add(page);

        CancelButton = btnCancel;

        BackColor = AppTheme.Background;
        AppTheme.ApplyRecursively(this);

        // After the theme pass, in the faces it leaves them in: the label box
        // as wide as the grid gives it, the track as wide as its longest
        // layout's name, the count for two digits.
        (Font Font, int Count, int Width)? trackWidth = null;
        _cmbTrack.Width = Math.Max(UiLayout.AtTextSize(UiLayout.FieldWidth), Pieces.PickerWidth(_cmbTrack, ref trackWidth));
        _numTopN.Width = UiLayout.AtTextSize(UiLayout.NumericWidth);

        // Wide enough for "Top", its count and the three buttons on one
        // line at the normal text size: at 420 they came to 408px in a line
        // of 388, and the buttons went onto a line of their own (layout
        // pictures, 29 Sep).
        DialogFit.Keep(this, page, width: 460, height: 460);

        void BtnSave_Click(object? sender, EventArgs e)
        {
            var label = _txtLabel.Text.Trim();
            if (string.IsNullOrWhiteSpace(label))
            {
                AppDialog.Show(this, "Round label is required.", "Missing label", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var selected = new List<string>();
            for (var i = 0; i < _clbDrivers.Items.Count; i++)
            {
                if (_clbDrivers.GetItemChecked(i))
                {
                    selected.Add(((DriverListItem)_clbDrivers.Items[i]!).Transponder);
                }
            }

            if (selected.Count == 0)
            {
                AppDialog.Show(this, "Select at least one driver.", "No drivers selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result = new RoundDefinition
            {
                ClassName = className,
                RoundLabel = label,
                Kind = RoundKind.Main,
                // Inferred from the label so a hand-added "B-Main" can take
                // part in bump-ups. Without this it defaults to -1 (untiered)
                // and silently sits outside the whole bump-up mechanism.
                MainTier = TierFromLabel(label),
                TrackLayoutName = _cmbTrack.SelectedItem as string == "(none)" ? string.Empty : (string)(_cmbTrack.SelectedItem ?? string.Empty),
                AssignedTransponders = selected,
            };

            DialogResult = DialogResult.OK;
            Close();
        }
    }


    /// <summary>
    /// Reads a tier from a label: "A-Main" is 0, "B Main" is 1, and so on.
    /// Returns -1 when the label doesn't start with a letter that reads as a
    /// tier, which correctly leaves things like "Consolation" untiered
    /// rather than guessing.
    /// </summary>
    private static int TierFromLabel(string label)
    {
        var trimmed = label.TrimStart();
        if (trimmed.Length == 0)
        {
            return -1;
        }

        var first = char.ToUpperInvariant(trimmed[0]);
        if (first < 'A' || first > 'Z')
        {
            return -1;
        }

        // Only treat it as a tier when the letter is followed by a
        // separator or "main" - otherwise "Consolation" would read as tier 2.
        var rest = trimmed[1..].TrimStart();
        var looksLikeTier = rest.StartsWith("-") || rest.StartsWith(" ")
                            || rest.StartsWith("main", StringComparison.OrdinalIgnoreCase)
                            || rest.Length == 0;

        return looksLikeTier ? first - 'A' : -1;
    }

    private sealed record DriverListItem(string Transponder, string Label)
    {
        public override string ToString() => Label;
    }
}
