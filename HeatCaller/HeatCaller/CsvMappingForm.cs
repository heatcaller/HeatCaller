namespace HeatCaller;

/// <summary>
/// The preview before a driver list is imported (27 Sep 2026): the file's
/// first rows, with a list above each column to say what it is - guessed
/// from its heading and changeable - and where any "points so far" go.
///
/// On the layout pieces (DialogFit): what to do across the top, the grid
/// taking the height, and under it the note on how drivers are matched,
/// where the points go with the hint under it, the line that says what is
/// wrong with the picks, and Import and Cancel at the foot. The hint and
/// that line are each as tall as the longest thing they can say
/// (SteadyLabel), so nothing moves as columns are picked or Import is
/// pressed. It used to be placed in pixels in a window 900px wide at every
/// text size, the grid 260px tall and anchored to take any height the
/// window was given, and the columns between 140px and 260px wide whatever
/// the text size - "Announcer name" was cut short in the lists above them
/// at every size, and most of the choices at a larger one.
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

    /// <summary>The grid shows its headings and at least this many rows - the lists, and the file's first rows under them - before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    /// <summary>The choice in "Points so far go into" that sends each driver's points to the championship for their class.</summary>
    private const string PerClassChoice = "The championship for each driver's class";

    // What the hint under "Points so far go into" says - see UpdateSeasonChoices.

    private const string NoPointsHint = "Pick a column as Points so far to bring each driver's championship points in with them.";

    private const string NoChampionshipHint =
        "There's no championship to carry points into yet. Make one on Championship → Standings first, or set the "
        + "points column to (ignore).";

    private const string ChampionshipHint =
        "Each driver's points so far become their starting total in that championship, added to what they score "
        + "here. Importing again replaces them. They can be changed on Championship → Standings with Points "
        + "Carried In.";

    // What is wrong with the picks, when Import is pressed - see TryImport
    // and PickedTwice.

    private const string NoTransponderProblem =
        "Pick the Transponder column - drivers are matched by it. Without one, only points so far can be "
        + "imported, and that needs a name column and a Points so far column.";

    private const string NoChampionshipProblem =
        "There's no championship for the points to go into. Make one on Championship → Standings first, "
        + "or set the points column to (ignore).";

    private readonly DataGridView _grid = new();
    private readonly ComboBox _cmbSeason = new();
    private readonly Label _lblSeason = new();
    private readonly SteadyLabel _lblSeasonHint = new();
    private readonly SteadyLabel _lblProblem = new();
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

    /// <param name="rowsAre">Where the rows are, for the line across the top: "in the file", or "in the table" when it was pasted.</param>
    public CsvMappingForm(string fileName, IReadOnlyList<string> headers, IReadOnlyList<List<string>> rows, IReadOnlyList<ImportColumn> guesses, IReadOnlyList<Season> seasons, string rowsAre = "in the file")
    {
        _seasons = seasons;

        Text = "Import drivers - " + fileName;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var page = BuildLayout(headers, rows, guesses, rowsAre);

        AppTheme.ApplyRecursively(this);
        UpdateSeasonChoices();

        // After the theme pass and the list's first filling, in the face it
        // is drawn in: as wide as it was made at the normal text size, at the
        // size in use, or as its longest championship if that is wider. The
        // choice for each driver's class comes and goes as a Class column is
        // picked, so it is counted whenever it can be offered at all, and the
        // box never has to change width while the window is up.
        (Font Font, int Count, int Width)? seasonWidth = null;
        var perClass = _seasons.Any(s => !string.IsNullOrWhiteSpace(s.ClassName)) ? PerClassChoice : string.Empty;
        _cmbSeason.Width = Math.Max(UiLayout.AtTextSize(360), Pieces.PickerWidth(_cmbSeason, ref seasonWidth, perClass));

        // The size it had at the normal text size. It can be made taller, or
        // as big as the screen, to see more of a wide file.
        DialogFit.Keep(this, page, width: 900, height: 560);
    }

    /// <summary>
    /// The page: what to do across the top, the grid taking the height left,
    /// and under it the note on matching, the championship picker and its
    /// hint, the problem line and the buttons - each as tall as it needs at
    /// the width the window has.
    /// </summary>
    private FillPage BuildLayout(IReadOnlyList<string> headers, IReadOnlyList<List<string>> rows, IReadOnlyList<ImportColumn> guesses, string rowsAre)
    {
        var lblIntro = new Label
        {
            Text = $"{Plural.Of(rows.Count, "row")} {rowsAre}. Pick what each column is from the list at the top of it - "
                   + "the guesses come from the headings. Columns left at (ignore) aren't imported.",
            AutoSize = true,
            Font = AppTheme.BodyFont,
        };

        // --- The grid: the lists on the first row, the file under them ---
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

        // Each column as wide as its heading and room either side, between
        // two widths - the room and the widths chosen at the normal text size
        // and followed at the size in use - so a long heading ends in "..."
        // rather than taking the width, and a wide file scrolls sideways
        // inside the grid. And never so narrow that the longest choice in
        // the list at its top is cut short: as wide as Pieces.PickerWidth
        // makes a drop-down for it, the arrow and room either side of the
        // text, which the cell's own padding comes out of. At 140px,
        // "Announcer name" was cut short at every text size.
        var widestChoice = captions.Max(caption => TextRenderer.MeasureText(caption, AppTheme.BoldFont).Width)
                           + DropArrow.Room;
        var narrowest = Math.Max(UiLayout.AtTextSize(140), widestChoice);
        var widest = Math.Max(UiLayout.AtTextSize(260), narrowest);

        for (var i = 0; i < headers.Count; i++)
        {
            var heading = string.IsNullOrWhiteSpace(headers[i]) ? $"Column {i + 1}" : headers[i].Trim();

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "c" + i,
                HeaderText = heading,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = Math.Clamp(TextRenderer.MeasureText(heading, AppTheme.BoldFont).Width + UiLayout.AtTextSize(30), narrowest, widest),
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

        // The lists wear the app's chevron, at the text size in use
        // (DropArrow), as every other drop-down does. The grid's own was a
        // few pixels of dots that stayed that size at 200% (layout
        // pictures, 30 Sep).
        _grid.CellPainting += PaintListCell;

        // One click on a list opens it, wherever on the cell it lands - its
        // drawn arrow is wider than the button the grid itself answers to.
        // After the click, once the grid has put the list in the cell.
        _grid.CellClick += (_, e) =>
        {
            if (e.RowIndex != 0 || e.ColumnIndex < 0)
            {
                return;
            }

            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed
                    && _grid.CurrentCell is { RowIndex: 0 } current
                    && current.ColumnIndex == e.ColumnIndex
                    && _grid.EditingControl is ComboBox { DroppedDown: false } list)
                {
                    list.DroppedDown = true;
                }
            }));
        };

        // And the list itself, while it is in a cell being picked from, has
        // the app's closed field and chevron (ComboBoxSkin) rather than the
        // grid's. Only the skin: styling it the way AppTheme.StyleComboBox
        // does changes its FlatStyle and DrawMode, and either makes Windows
        // build its window again - which the grid, holding it in the cell
        // as it shows it, is not expecting. The grid makes one list and
        // uses it for every cell; the skin goes on it once.
        _grid.EditingControlShowing += (_, e) =>
        {
            if (e.Control is ComboBox list)
            {
                ComboBoxSkin.Attach(list);
            }
        };

        var lblMatch = new Label
        {
            Text = "Drivers are matched by transponder: a new one becomes a new driver, and one already here is updated - only "
                   + "with cells that are filled in. With no transponder column, only points so far are imported, matched by "
                   + "driver name.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // --- Where the points go ---
        _lblSeason.Text = "Points so far go into:";
        _lblSeason.AutoSize = true;
        _lblSeason.Font = AppTheme.BodyFont;

        // Its width is set once the list has been filled - see the constructor.
        _cmbSeason.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSeason.Font = AppTheme.BodyFont;

        var seasonRow = new FieldRow();
        seasonRow.Add(_lblSeason, _cmbSeason);

        // Reworded as columns are picked (UpdateSeasonChoices), and as tall
        // as the longer of the two things it can say in this window - with
        // championships to pick from or without - so the lines under it
        // never move while it is used.
        _lblSeasonHint.Wordings = new[] { NoPointsHint, _seasons.Count == 0 ? NoChampionshipHint : ChampionshipHint };
        _lblSeasonHint.ForeColor = AppTheme.TextMuted;
        _lblSeasonHint.Font = AppTheme.SmallFont;

        // --- What's wrong, if anything, and the buttons ---

        // Empty until Import is pressed with picks that can't be imported,
        // and as tall as the longest thing it can say then from the start,
        // so saying it moves nothing - the old window kept two lines of the
        // body face for it.
        _lblProblem.Wordings = DriverImport.All
            .Where(column => column != ImportColumn.Ignore)
            .Select(PickedTwice)
            .Append(NoTransponderProblem)
            .Append(NoChampionshipProblem)
            .ToList();
        _lblProblem.ForeColor = AppTheme.Danger;
        _lblProblem.Font = AppTheme.BodyFont;

        var btnImport = new Button { Text = "Import" };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        btnImport.Click += (_, _) => TryImport();

        // Spaced as the old window spaced them: a row's gap between parts,
        // and a hint's under the picker the hint is about.
        var page = new FillPage();
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.AddFill(_grid, _ => _grid.ColumnHeadersHeight + LeastRows * _grid.RowTemplate.Height + 2, gapBelow: UiLayout.RowGap);
        page.Add(lblMatch, gapBelow: UiLayout.RowGap);
        page.Add(seasonRow, gapBelow: UiLayout.HintGap);
        page.Add(_lblSeasonHint, gapBelow: UiLayout.RowGap);
        page.Add(_lblProblem, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Cancel furthest right, as a dialog's
        // buttons go - and Tab still reaches Import first, as it did: the bar
        // takes them right to left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnImport), gapBelow: 0);
        btnImport.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnImport;
        CancelButton = btnCancel;

        return page;
    }

    /// <summary>
    /// A list at the top of a column: the cell as the grid paints it, less
    /// its own arrow and text, and then the pick and the app's chevron
    /// (DropArrow) - the text kept clear of the chevron's zone.
    /// </summary>
    private void PaintListCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex != 0 || e.ColumnIndex < 0 || e.Graphics is null)
        {
            return;
        }

        e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground | DataGridViewPaintParts.Border);

        var bounds = e.CellBounds;
        var style = e.CellStyle ?? _grid.DefaultCellStyle;
        var selected = (e.State & DataGridViewElementStates.Selected) != 0;
        var ink = selected ? style.SelectionForeColor : style.ForeColor;
        var zone = DropArrow.ZoneIn(bounds.Width, bounds.Height);

        zone.Offset(bounds.Left, bounds.Top);

        var text = new Rectangle(
            bounds.Left + style.Padding.Left + UiLayout.AtDpi(4),
            bounds.Top,
            Math.Max(1, zone.Left - bounds.Left - style.Padding.Left - UiLayout.AtDpi(4)),
            bounds.Height - 1);

        TextRenderer.DrawText(
            e.Graphics,
            e.FormattedValue as string ?? string.Empty,
            style.Font ?? _grid.Font,
            text,
            ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        DropArrow.Draw(e.Graphics, zone, AppTheme.TextMuted);

        e.Handled = true;
    }

    /// <summary>What is wrong when one kind of column is picked for two: "Transponder" is picked for more than one column...</summary>
    private static string PickedTwice(ImportColumn column) =>
        $"\"{DriverImport.Caption(column)}\" is picked for more than one column. Pick it for one, and set the others to (ignore).";

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
            _cmbSeason.Items.Add(new SeasonChoice(null, PerClassChoice));
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

        // The caption greyed with its drop-down, and only then: with points
        // picked and no championship to put them in, it was black beside an
        // empty, greyed drop-down (layout pictures, 30 Sep).
        _lblSeason.Enabled = _cmbSeason.Enabled;
        _lblSeasonHint.Text = !points
            ? NoPointsHint
            : _seasons.Count == 0
                ? NoChampionshipHint
                : ChampionshipHint;
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
            _lblProblem.Text = PickedTwice(twice.Key);
            return;
        }

        var hasTransponder = columns.Contains(ImportColumn.Transponder);
        var hasName = columns.Contains(ImportColumn.DriverName) || columns.Contains(ImportColumn.FirstName) || columns.Contains(ImportColumn.LastName);
        var hasPoints = columns.Contains(ImportColumn.PointsSoFar);

        if (!hasTransponder && !(hasName && hasPoints))
        {
            _lblProblem.Text = NoTransponderProblem;
            return;
        }

        if (hasPoints && _cmbSeason.SelectedItem is not SeasonChoice)
        {
            _lblProblem.Text = NoChampionshipProblem;
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
