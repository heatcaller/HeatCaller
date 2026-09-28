namespace HeatCaller;

/// <summary>
/// The preview before a driver list is imported (27 Sep 2026): the file's
/// first rows, with a list above each column to say what it is - guessed
/// from its heading and changeable - and where any "points so far" go.
/// </summary>
/// <remarks>
/// Any CSV, rather than only the app's own template: a club switching from
/// other timing software has its drivers - and its standings - in whatever
/// columns that software writes, and none of them publishes its format.
/// See DriverImport for the reading and the guesses.
/// </remarks>
internal sealed class CsvMappingForm : Form
{
    /// <summary>How many of the file's rows are shown under the lists.</summary>
    private const int PreviewRows = 8;

    private readonly DataGridView _grid = new();
    private readonly ComboBox _cmbSeason = new();
    private readonly Label _lblSeason = new();
    private readonly Label _lblSeasonHint = new();
    private readonly Label _lblProblem = new();
    private readonly IReadOnlyList<Season> _seasons;

    /// <summary>A choice in "Points so far go into". A null Season is "the championship for each driver's class".</summary>
    private sealed record SeasonChoice(Season? Season, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>What each column was set to, in the file's column order. Set when Import is pressed.</summary>
    public IReadOnlyList<ImportColumn> Columns { get; private set; } = Array.Empty<ImportColumn>();

    /// <summary>The championship "points so far" go into; null with <see cref="SeasonPerClass"/>, or when there are none to import.</summary>
    public string? SeasonId { get; private set; }

    /// <summary>Each driver's points go into the championship for their class.</summary>
    public bool SeasonPerClass { get; private set; }

    public CsvMappingForm(string fileName, IReadOnlyList<string> headers, IReadOnlyList<List<string>> rows, IReadOnlyList<ImportColumn> guesses, IReadOnlyList<Season> seasons)
    {
        _seasons = seasons;

        Text = "Import drivers - " + fileName;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        MaximizeBox = true;
        ShowInTaskbar = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ClientSize = new Size(900, 560);

        BuildLayout(headers, rows, guesses);

        AppTheme.ApplyRecursively(this);
        UpdateSeasonChoices();
    }

    private void BuildLayout(IReadOnlyList<string> headers, IReadOnlyList<List<string>> rows, IReadOnlyList<ImportColumn> guesses)
    {
        const int left = 16;
        var width = ClientSize.Width - left * 2;

        var lblIntro = new Label
        {
            Text = $"{Plural.Of(rows.Count, "row")} in the file. Pick what each column is from the list at the top of it - "
                   + "the guesses come from the headings. Columns left at (ignore) aren't imported.",
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            Location = new Point(left, 16),
            Font = AppTheme.BodyFont,
        };

        Controls.Add(lblIntro);

        var gridTop = UiLayout.Below(lblIntro, UiLayout.RowGap);

        // --- The grid: the lists on the first row, the file under them ---
        _grid.Location = new Point(left, gridTop);
        _grid.Size = new Size(width, 260);
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToOrderColumns = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;

        var captions = DriverImport.All.Select(DriverImport.Caption).ToArray();

        for (var i = 0; i < headers.Count; i++)
        {
            var heading = string.IsNullOrWhiteSpace(headers[i]) ? $"Column {i + 1}" : headers[i].Trim();

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "c" + i,
                HeaderText = heading,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = Math.Clamp(TextRenderer.MeasureText(heading, AppTheme.BoldFont).Width + 30, 140, 260),
            });
        }

        // The first row holds the lists. Frozen, so it stays put while the
        // file scrolls under it.
        var listRow = new DataGridViewRow { Frozen = true };
        listRow.CreateCells(_grid);

        for (var i = 0; i < headers.Count; i++)
        {
            var cell = new DataGridViewComboBoxCell
            {
                DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
                FlatStyle = FlatStyle.Flat,
            };

            cell.Items.AddRange(captions);
            cell.Value = DriverImport.Caption(i < guesses.Count ? guesses[i] : ImportColumn.Ignore);
            listRow.Cells[i] = cell;
        }

        listRow.DefaultCellStyle.Font = AppTheme.BoldFont;
        _grid.Rows.Add(listRow);

        foreach (var fields in rows.Take(PreviewRows))
        {
            var index = _grid.Rows.Add();
            var row = _grid.Rows[index];
            row.ReadOnly = true;

            for (var i = 0; i < headers.Count; i++)
            {
                row.Cells[i].Value = i < fields.Count ? fields[i] : string.Empty;
            }

            row.DefaultCellStyle.ForeColor = AppTheme.TextMuted;
        }

        // A pick takes effect as it is made, not when the cell is left.
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex == 0)
            {
                UpdateSeasonChoices();
            }
        };

        // A value the list doesn't hold can't happen here, but a grid that
        // throws a dialog of its own would be worse than ignoring it.
        _grid.DataError += (_, e) => e.ThrowException = false;

        Controls.Add(_grid);

        var y = _grid.Bottom + UiLayout.RowGap;

        var lblMatch = new Label
        {
            Text = "Drivers are matched by transponder: a new one becomes a new driver, and one already here is updated - only "
                   + "with cells that are filled in. With no transponder column, only points so far are imported, matched by "
                   + "driver name.",
            AutoSize = true,
            MaximumSize = new Size(width, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(left, y),
        };

        Controls.Add(lblMatch);

        y = UiLayout.Below(lblMatch, UiLayout.RowGap);

        // --- Where the points go ---
        _lblSeason.Text = "Points so far go into:";
        _lblSeason.AutoSize = true;
        _lblSeason.Font = AppTheme.BodyFont;

        _cmbSeason.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSeason.Font = AppTheme.BodyFont;
        _cmbSeason.Width = 360;

        Controls.Add(_lblSeason);
        Controls.Add(_cmbSeason);

        _lblSeason.Location = new Point(left, UiLayout.CentreOn(_lblSeason, y, _cmbSeason.Height));
        _cmbSeason.Location = new Point(UiLayout.After(_lblSeason), y);

        y = UiLayout.Below(_cmbSeason, UiLayout.HintGap);

        _lblSeasonHint.Text = "Each driver's points so far become their starting total in that championship, added to what they "
                              + "score here. Importing again replaces them. They can be changed on Championship → Standings "
                              + "with Points Carried In.";
        _lblSeasonHint.AutoSize = true;
        _lblSeasonHint.MaximumSize = new Size(width, 0);
        _lblSeasonHint.ForeColor = AppTheme.TextMuted;
        _lblSeasonHint.Font = AppTheme.SmallFont;
        _lblSeasonHint.Location = new Point(left, y);

        Controls.Add(_lblSeasonHint);

        y = UiLayout.Below(_lblSeasonHint, UiLayout.RowGap);

        // --- What's wrong, if anything, and the buttons ---
        _lblProblem.AutoSize = true;
        _lblProblem.MaximumSize = new Size(width, 0);
        _lblProblem.ForeColor = AppTheme.Danger;
        _lblProblem.Font = AppTheme.BodyFont;
        _lblProblem.Location = new Point(left, y);

        Controls.Add(_lblProblem);

        y += AppTheme.BodyFont.Height * 2 + UiLayout.RowGap;

        var btnImport = new Button { Text = "Import" };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        Controls.Add(btnImport);
        Controls.Add(btnCancel);

        btnImport.Size = new Size(UiLayout.WidthFor(btnImport), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);

        btnCancel.Location = new Point(left + width - btnCancel.Width, y);
        btnImport.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnImport.Width, y);

        btnImport.Click += (_, _) => TryImport();

        AcceptButton = btnImport;
        CancelButton = btnCancel;

        // Sized to what was just laid out, THEN anchored: an anchor keeps a
        // control's distance from the edge as it is when it is set, so one
        // set before the window fitted its contents would have kept the
        // buttons below the bottom edge. From here the grid takes any height
        // the window is given, and everything under it stays at the foot.
        ClientSize = new Size(ClientSize.Width, UiLayout.Below(btnImport, 16));
        MinimumSize = new Size(640, Math.Max(360, Height - 150));

        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

        foreach (var control in new Control[] { lblMatch, _lblSeason, _cmbSeason, _lblSeasonHint, _lblProblem })
        {
            control.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        }

        btnImport.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        btnCancel.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
    }

    /// <summary>What each column is set to now, in the file's order.</summary>
    private List<ImportColumn> CurrentColumns()
    {
        var byCaption = DriverImport.All.ToDictionary(DriverImport.Caption, column => column);
        var columns = new List<ImportColumn>();

        for (var i = 0; i < _grid.Columns.Count; i++)
        {
            var caption = _grid.Rows[0].Cells[i].Value as string;
            columns.Add(caption is not null && byCaption.TryGetValue(caption, out var column) ? column : ImportColumn.Ignore);
        }

        return columns;
    }

    /// <summary>
    /// The championships the points can go into - with "each driver's class"
    /// first when a class column is picked and a championship has a class.
    /// Off, and said to be, while no column is "Points so far".
    /// </summary>
    private void UpdateSeasonChoices()
    {
        var columns = CurrentColumns();
        var points = columns.Contains(ImportColumn.PointsSoFar);
        var perClass = columns.Contains(ImportColumn.ClassName) && _seasons.Any(s => !string.IsNullOrWhiteSpace(s.ClassName));

        var previous = _cmbSeason.SelectedItem as SeasonChoice;

        _cmbSeason.BeginUpdate();
        _cmbSeason.Items.Clear();

        if (perClass)
        {
            _cmbSeason.Items.Add(new SeasonChoice(null, "The championship for each driver's class"));
        }

        foreach (var season in _seasons)
        {
            var scope = string.IsNullOrWhiteSpace(season.ClassName) ? "every class" : season.ClassName.Trim();
            _cmbSeason.Items.Add(new SeasonChoice(season, $"{season.Name} ({scope})"));
        }

        _cmbSeason.EndUpdate();

        // The same choice as before when it is still offered.
        var keep = -1;

        for (var i = 0; i < _cmbSeason.Items.Count && previous is not null; i++)
        {
            if (_cmbSeason.Items[i] is SeasonChoice choice && ReferenceEquals(choice.Season, previous.Season))
            {
                keep = i;
            }
        }

        if (_cmbSeason.Items.Count > 0)
        {
            _cmbSeason.SelectedIndex = Math.Max(0, keep);
        }

        _cmbSeason.Enabled = points && _cmbSeason.Items.Count > 0;
        _lblSeason.Enabled = points;
        _lblSeasonHint.Text = !points
            ? "Pick a column as Points so far to bring each driver's championship points in with them."
            : _seasons.Count == 0
                ? "There's no championship to carry points into yet. Make one on Championship → Standings first, or set the "
                  + "points column to (ignore)."
                : "Each driver's points so far become their starting total in that championship, added to what they score "
                  + "here. Importing again replaces them. They can be changed on Championship → Standings with Points "
                  + "Carried In.";
    }

    /// <summary>Checks the picks make an import, and closes with them if they do; otherwise says what to put right.</summary>
    private void TryImport()
    {
        var columns = CurrentColumns();

        var twice = columns
            .Where(c => c != ImportColumn.Ignore)
            .GroupBy(c => c)
            .FirstOrDefault(g => g.Count() > 1);

        if (twice is not null)
        {
            _lblProblem.Text = $"\"{DriverImport.Caption(twice.Key)}\" is picked for more than one column. Pick it for one, and set the others to (ignore).";
            return;
        }

        var hasTransponder = columns.Contains(ImportColumn.Transponder);
        var hasName = columns.Contains(ImportColumn.DriverName) || columns.Contains(ImportColumn.FirstName) || columns.Contains(ImportColumn.LastName);
        var hasPoints = columns.Contains(ImportColumn.PointsSoFar);

        if (!hasTransponder && !(hasName && hasPoints))
        {
            _lblProblem.Text = "Pick the Transponder column - drivers are matched by it. Without one, only points so far can be "
                               + "imported, and that needs a name column and a Points so far column.";
            return;
        }

        if (hasPoints && _cmbSeason.SelectedItem is not SeasonChoice)
        {
            _lblProblem.Text = "There's no championship for the points to go into. Make one on Championship → Standings first, "
                               + "or set the points column to (ignore).";
            return;
        }

        Columns = columns;

        if (hasPoints && _cmbSeason.SelectedItem is SeasonChoice chosen)
        {
            SeasonId = chosen.Season?.Id;
            SeasonPerClass = chosen.Season is null;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
