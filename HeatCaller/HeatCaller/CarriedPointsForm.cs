namespace HeatCaller;

/// <summary>
/// The points each driver brings into a championship from before it ran in
/// HeatCaller (27 Sep 2026) - typed in here, or brought in with the drivers
/// by Driver Profiles' Import from CSV, and changed here either way.
///
/// On the layout pieces (DialogFit): the note on what the points are across
/// the top, the drivers and their points taking the height, two lines kept
/// under them for what is wrong with a row, and Clear All on the left of
/// the foot with Save and Cancel against the right-hand edge. It used to be
/// placed in pixels in a 520px-wide window: the note wrapped at a fixed
/// 488px, the list 300px tall under it, the window sized to where the
/// buttons ended up, and the list, the message and the buttons anchored
/// from there to follow a resize.
/// </summary>
internal sealed class CarriedPointsForm : Form
{
    /// <summary>The list shows its headings and at least this many rows before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    /// <summary>
    /// Lines kept for what is wrong with a row, so a message appearing
    /// moves nothing. A longer one still grows the label, and the list
    /// gives it the room.
    /// </summary>
    private const int ProblemLines = 2;

    private readonly DataGridView _grid = new();
    private readonly Label _lblProblem = new();

    /// <summary>What was entered, one entry per driver. Set when Save is pressed.</summary>
    public List<CarriedPointsEntry> Entries { get; private set; } = new();

    /// <param name="knownNames">Names the standings already know, offered as each row is typed.</param>
    public CarriedPointsForm(string seasonName, IEnumerable<CarriedPointsEntry> current, IEnumerable<string> knownNames)
    {
        Text = $"Points carried in - {(string.IsNullOrWhiteSpace(seasonName) ? "championship" : seasonName)}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var page = BuildLayout(current, knownNames);

        AppTheme.ApplyRecursively(this);

        // Every heading whole, the rest shared by weight - see Pieces.KeepHeadingsWhole.
        Pieces.KeepHeadingsWhole(_grid);

        // In the face it wears, as the label draws its lines (Pieces.LineHeight) - see ProblemLines.
        _lblProblem.MinimumSize = new Size(0, ProblemLines * Pieces.LineHeight(_lblProblem.Font) + _lblProblem.Padding.Vertical);

        // Wider than the 520 it was made at: Clear All on the left and Save
        // and Cancel on the right come to 498px with the gaps between them
        // at the normal text size, and at 520 the line inside the margins is
        // 488 - Save and Cancel would have gone onto a line of their own. The
        // height it had. It can be made bigger, for a long list.
        DialogFit.Keep(this, page, width: 540, height: 520);
    }

    /// <summary>The page, top to bottom, with the list taking the height the rest leave. Returns it, for DialogFit.</summary>
    private FillPage BuildLayout(IEnumerable<CarriedPointsEntry> current, IEnumerable<string> knownNames)
    {
        var lblIntro = new Label
        {
            Text = "Points each driver brings in from before this championship ran here - from the club's old software, "
                   + "say. They're added to what the driver scores, and never dropped by \"count each driver's best\". "
                   + "Use the name exactly as the standings show it. Leave a row empty, or delete it, to carry nothing in.",
            AutoSize = true,
            Font = AppTheme.BodyFont,
        };

        _grid.AllowUserToAddRows = true;
        _grid.AllowUserToDeleteRows = true;
        _grid.AllowUserToResizeRows = false;
        // A click picks the whole row, so Delete removes it - which the row
        // headers were there for. They were Windows' own grey stubs down
        // the left, the one part of the window the theme never reached
        // (layout pictures, 30 Sep); every other list in the app picks
        // whole rows without them.
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
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

        // Wraps to the width the page gives it; two lines kept for it once
        // the theme pass has left it in its face (see the constructor).
        _lblProblem.AutoSize = true;
        _lblProblem.ForeColor = AppTheme.Danger;
        _lblProblem.Font = AppTheme.BodyFont;

        var btnClear = new Button { Text = "Clear All" };
        var btnSave = new Button { Text = "Save" };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        btnClear.Click += (_, _) =>
        {
            _grid.Rows.Clear();
            _lblProblem.Text = string.Empty;
        };

        btnSave.Click += (_, _) => TrySave();

        // Clear All on the left, Save and Cancel against the right-hand
        // edge with Cancel furthest right, as they were - on one line
        // (SplitBar; see the width given to DialogFit.Keep). Tab still goes
        // Clear All, Save, Cancel: the right-hand bar takes its buttons right
        // to left, in the order they were added.
        var footer = new SplitBar(
            new ButtonBar().With(btnClear),
            new ButtonBar(alignRight: true).With(btnCancel, btnSave));

        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        var page = new FillPage();
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.AddFill(_grid, _ => _grid.ColumnHeadersHeight + LeastRows * _grid.RowTemplate.Height + 2, gapBelow: UiLayout.RowGap);
        page.Add(_lblProblem, gapBelow: UiLayout.RowGap);
        page.Add(footer, gapBelow: 0);

        Controls.Add(page);

        CancelButton = btnCancel;

        return page;
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
