namespace HeatCaller;

/// <summary>
/// Dialog for adding a Main round: pick a label, optional track, and check
/// which class members are racing in it. The "select top N" helper uses
/// qualifying-ranked order if available, falling back to roster order.
/// </summary>
public sealed class MainRoundForm : Form
{
    private readonly TextBox _txtLabel = new();
    private readonly ComboBox _cmbTrack = new();
    private readonly CheckedListBox _clbDrivers = new();
    private readonly NumericUpDown _numTopN = new();

    public RoundDefinition? Result { get; private set; }

    public MainRoundForm(string className, List<DriverProfile> classRoster, List<string> rankedTransponders, TrackLayoutStore trackLayouts, string defaultLabel)
    {
        Text = $"Add Main - {className}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 460);
        MinimumSize = new Size(380, 380);
        Font = AppTheme.BodyFont;

        var lblLabel = new Label { Text = "Round Label:", Location = new Point(12, 14), AutoSize = true };
        _txtLabel.Location = new Point(110, 11);
        _txtLabel.Width = 280;
        _txtLabel.Text = defaultLabel;

        var lblTrack = new Label { Text = "Track Layout:", Location = new Point(12, 46), AutoSize = true };
        _cmbTrack.Location = new Point(110, 43);
        _cmbTrack.Width = 280;
        _cmbTrack.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrack.Items.Add("(none)");
        foreach (var t in trackLayouts.All)
        {
            _cmbTrack.Items.Add(t.Name);
        }
        _cmbTrack.SelectedIndex = 0;

        var lblDrivers = new Label { Text = "Drivers in this main:", Location = new Point(12, 78), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };

        _clbDrivers.Location = new Point(12, 100);
        _clbDrivers.Size = new Size(378, 260);
        _clbDrivers.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _clbDrivers.CheckOnClick = true;
        foreach (var d in classRoster)
        {
            var label = string.IsNullOrWhiteSpace(d.DriverName) ? d.Transponder : $"{d.DriverName} ({d.Transponder})";
            _clbDrivers.Items.Add(new DriverListItem(d.Transponder, label));
        }

        var lblTopN = new Label { Text = "Top:", Location = new Point(12, 370), AutoSize = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _numTopN.Location = new Point(50, 367);
        _numTopN.Width = 50;
        _numTopN.Minimum = 0;
        _numTopN.Maximum = Math.Max(1, classRoster.Count);
        _numTopN.Value = Math.Min(6, classRoster.Count);
        _numTopN.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

        var btnSelectTopN = new Button { Text = "from Qualifying", Location = new Point(110, 366), Width = 110, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
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

        var btnSelectAll = new Button { Text = "All", Location = new Point(288, 366), Width = 45, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        btnSelectAll.Click += (_, _) =>
        {
            for (var i = 0; i < _clbDrivers.Items.Count; i++)
            {
                _clbDrivers.SetItemChecked(i, true);
            }
        };

        var btnSelectNone = new Button { Text = "None", Location = new Point(336, 366), Width = 45, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        btnSelectNone.Click += (_, _) =>
        {
            for (var i = 0; i < _clbDrivers.Items.Count; i++)
            {
                _clbDrivers.SetItemChecked(i, false);
            }
        };

        var btnSave = new Button { Text = "Save", Location = new Point(228, 424), Width = 80, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        btnSave.Click += BtnSave_Click;
        var btnCancel = new Button { Text = "Cancel", Location = new Point(324, 424), Width = 80, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };

        Controls.Add(lblLabel);
        Controls.Add(_txtLabel);
        Controls.Add(lblTrack);
        Controls.Add(_cmbTrack);
        Controls.Add(lblDrivers);
        Controls.Add(_clbDrivers);
        Controls.Add(lblTopN);
        Controls.Add(_numTopN);
        Controls.Add(btnSelectTopN);
        Controls.Add(btnSelectAll);
        Controls.Add(btnSelectNone);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        CancelButton = btnCancel;

        BackColor = AppTheme.Background;
        AppTheme.ApplyRecursively(this);

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
