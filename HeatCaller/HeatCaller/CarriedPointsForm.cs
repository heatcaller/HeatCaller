namespace HeatCaller;

/// <summary>
/// The points each driver brings into a championship from before it ran in
/// HeatCaller (27 Sep 2026) - typed in here, or brought in with the drivers
/// by Driver Profiles' Import from CSV, and changed here either way.
/// </summary>
internal sealed class CarriedPointsForm : Form
{
    private readonly DataGridView _grid = new();
    private readonly Label _lblProblem = new();

    /// <summary>What was entered, one entry per driver. Set when Save is pressed.</summary>
    public List<CarriedPointsEntry> Entries { get; private set; } = new();

    /// <param name="knownNames">Names the standings already know, offered as each row is typed.</param>
    public CarriedPointsForm(string seasonName, IEnumerable<CarriedPointsEntry> current, IEnumerable<string> knownNames)
    {
        Text = $"Points carried in - {(string.IsNullOrWhiteSpace(seasonName) ? "championship" : seasonName)}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ClientSize = new Size(520, 520);

        BuildLayout(current, knownNames);

        AppTheme.ApplyRecursively(this);
    }

    private void BuildLayout(IEnumerable<CarriedPointsEntry> current, IEnumerable<string> knownNames)
    {
        const int left = 16;
        var width = ClientSize.Width - left * 2;

        var lblIntro = new Label
        {
            Text = "Points each driver brings in from before this championship ran here - from the club's old software, "
                   + "say. They're added to what the driver scores, and never dropped by \"count each driver's best\". "
                   + "Use the name exactly as the standings show it. Leave a row empty, or delete it, to carry nothing in.",
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            Location = new Point(left, 16),
            Font = AppTheme.BodyFont,
        };

        Controls.Add(lblIntro);

        _grid.Location = new Point(left, UiLayout.Below(lblIntro, UiLayout.RowGap));
        _grid.Size = new Size(width, 300);
        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = true;
        _grid.RowHeadersWidth = 28;
        // Row headers select the row, so Delete removes it.
        _grid.SelectionMode = DataGridViewSelectionMode.RowHeaderSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;

        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 70, SortMode = DataGridViewColumnSortMode.NotSortable });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "points", HeaderText = "Points", FillWeight = 30, SortMode = DataGridViewColumnSortMode.NotSortable });

        foreach (var entry in current.OrderByDescending(e => e.Points).ThenBy(e => e.DriverName, StringComparer.CurrentCultureIgnoreCase))
        {
            _grid.Rows.Add(entry.DriverName, entry.Points.ToString());
        }

        // Names the standings know, offered while the Driver cell is typed
        // in - a name spelled differently from the results scores separately.
        var names = new AutoCompleteStringCollection();
        names.AddRange(knownNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());

        _grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is TextBox box)
            {
                var driverColumn = _grid.CurrentCell?.ColumnIndex == 0;

                box.AutoCompleteMode = driverColumn ? AutoCompleteMode.SuggestAppend : AutoCompleteMode.None;
                box.AutoCompleteSource = driverColumn ? AutoCompleteSource.CustomSource : AutoCompleteSource.None;
                box.AutoCompleteCustomSource = names;
            }
        };

        Controls.Add(_grid);

        var y = UiLayout.Below(_grid, UiLayout.RowGap);

        _lblProblem.AutoSize = true;
        _lblProblem.MaximumSize = new Size(width, 0);
        _lblProblem.ForeColor = AppTheme.Danger;
        _lblProblem.Font = AppTheme.BodyFont;
        _lblProblem.Location = new Point(left, y);

        Controls.Add(_lblProblem);

        y += AppTheme.BodyFont.Height * 2 + UiLayout.RowGap;

        var btnClear = new Button { Text = "Clear All" };
        var btnSave = new Button { Text = "Save" };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        Controls.Add(btnClear);
        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        foreach (var button in new[] { btnClear, btnSave, btnCancel })
        {
            button.Size = new Size(UiLayout.WidthFor(button), UiLayout.ButtonHeight);
        }

        btnClear.Location = new Point(left, y);
        btnCancel.Location = new Point(left + width - btnCancel.Width, y);
        btnSave.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnSave.Width, y);

        btnClear.Click += (_, _) =>
        {
            _grid.Rows.Clear();
            _lblProblem.Text = string.Empty;
        };

        btnSave.Click += (_, _) => TrySave();

        CancelButton = btnCancel;

        // Sized to what was laid out, then anchored - see CsvMappingForm.
        ClientSize = new Size(ClientSize.Width, UiLayout.Below(btnSave, 16));
        MinimumSize = new Size(420, Math.Max(320, Height - 200));

        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _lblProblem.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        btnClear.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        btnSave.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        btnCancel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
    }

    /// <summary>Checks every row and closes with them; otherwise says which row to put right.</summary>
    private void TrySave()
    {
        // Whatever is being typed counts.
        _grid.EndEdit();

        var entries = new List<CarriedPointsEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < _grid.Rows.Count; i++)
        {
            var row = _grid.Rows[i];

            if (row.IsNewRow)
            {
                continue;
            }

            var name = (row.Cells[0].Value?.ToString() ?? string.Empty).Trim();
            var pointsText = (row.Cells[1].Value?.ToString() ?? string.Empty).Trim();

            if (name.Length == 0 && pointsText.Length == 0)
            {
                continue;
            }

            if (name.Length == 0)
            {
                _lblProblem.Text = $"Row {i + 1} has points but no driver.";
                return;
            }

            // A driver with nothing beside them carries nothing in.
            if (pointsText.Length == 0)
            {
                continue;
            }

            if (!DriverImport.TryParsePoints(pointsText, out var points))
            {
                _lblProblem.Text = $"{name}'s points aren't a number.";
                return;
            }

            if (!seen.Add(name))
            {
                _lblProblem.Text = $"{name} is on the list twice. Keep one row for them.";
                return;
            }

            if (points != 0)
            {
                entries.Add(new CarriedPointsEntry { DriverName = name, Points = points });
            }
        }

        Entries = entries;
        DialogResult = DialogResult.OK;
        Close();
    }
}
