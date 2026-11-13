namespace HeatCaller;

/// <summary>
/// Scrutineering: recording which cars have been checked and cleared.
///
/// Organised around today rather than all history, because "has this car
/// passed tech?" almost always means "today" - a car that passed last month
/// says nothing about its condition this morning. History is still reachable
/// but it isn't what the page leads with.
/// </summary>
public sealed class TechInspectionControl : UserControl
{
    private readonly TechInspectionStore _store;
    private readonly ProfileStore _profiles;
    private readonly RaceClassStore _classes;
    private readonly UndoService _undo;

    private readonly ComboBox _cmbClass = new();
    private readonly CheckBox _chkTodayOnly = new ThemedCheckBox();
    private readonly DataGridView _grid = new();
    private readonly Label _lblSummary = new();
    private readonly Label _lblStatus = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The page the rest is laid out on - see LeastHeight.</summary>
    private readonly FillPage _page = new();

    /// <summary>The class filter, Today only and the count along one line - held so a refilled class list can re-measure it.</summary>
    private readonly FieldRow _filterRow = new();

    /// <summary>The class filter's width, measured from its choices - see Pieces.PickerWidth. Forgotten when the list is refilled.</summary>
    private (Font Font, int Count, int Width)? _classWidth;

    /// <summary>The class filter's width at the normal text size - what it always had, and the least it has now.</summary>
    private const int ClassFilterLeast = 220;

    /// <summary>The longest the count usually comes to, for the room kept for it at the end of the filters' line - see SummaryWidth.</summary>
    private const string LongestSummary = "999 inspected, 999 NOT cleared.";

    public TechInspectionControl(TechInspectionStore store, ProfileStore profiles, RaceClassStore classes, UndoService undo)
    {
        _store = store;
        _profiles = profiles;
        _classes = classes;
        _undo = undo;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No design size and no MinimumSize. The 900x540 floor was honoured
        // over the size the page was given, so a window shorter than that -
        // its own window on a 1280x720 screen - had the buttons under the
        // grid off the bottom of the page. A short window scrolls the page
        // instead (FillPage), and the window can ask how short that is
        // (LeastHeight).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The intro, then one card: the filters along a line - the class, Today
    /// only, and the count at the end of the line - the inspections taking
    /// the height that is left, the buttons under them and the line saying
    /// what the last one did. On the layout pieces (LayoutPieces.cs), so
    /// nothing is placed by hand.
    ///
    /// It used to be placed in pixels: each row put under the one above by
    /// measuring it, the class filter 220px wide at every text size and the
    /// count placed after Today only by hand, the grid 380px tall and
    /// anchored to all four sides, the buttons and the status line pinned to
    /// the bottom edge - over a 900x540 floor the page kept however small
    /// its window, with the intro capped at that floor's width and every
    /// face set once, so none of it followed the text size.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Every inspection, with its car, checks, inspector and notes. Each class's checklist is set on the "
                 + "Classes page, and Who's Racing has a Tech column and a filter for cars still to check. Race Control "
                 + "warns when a staged heat has a driver who hasn't passed today (a warning only; it can be switched "
                 + "off per class).",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // --- The filters ---

        var lblClass = new Label { Text = "Class:" };

        _cmbClass.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbClass.SelectedIndexChanged += (_, _) => RefreshGrid();

        _chkTodayOnly.Text = "Today only";
        _chkTodayOnly.AutoSize = true;
        _chkTodayOnly.Checked = true;
        _chkTodayOnly.CheckedChanged += (_, _) => RefreshGrid();

        // How many, and how many not cleared, in the bold face at the end of
        // the filters' line - or on a line of its own when it no longer fits
        // there (FieldRow), never cut short: the room it is kept is its
        // longest wording's (SummaryWidth).
        _lblSummary.AutoSize = false;
        _lblSummary.AutoEllipsis = true;
        _textSize.Wear(_lblSummary, () => AppTheme.BoldFont);

        _filterRow.Add(lblClass, _cmbClass, ClassFilterWidth);
        _filterRow.Add(null, _chkTodayOnly);
        _filterRow.AddLast(null, _lblSummary, SummaryWidth);

        // --- The inspections ---

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "when", HeaderText = "When", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 75 },
            new DataGridViewTextBoxColumn { Name = "class", HeaderText = "Class", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "car", HeaderText = "Car", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "result", HeaderText = "Result", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "checks", HeaderText = "Checks", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "inspector", HeaderText = "Inspector", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "notes", HeaderText = "Notes", FillWeight = 90 }
        );

        // Every heading whole, at the text size in use, and the time, the
        // result and the count of checks each kept wide enough for its
        // widest - see Pieces.KeepHeadingsWhole. "Passed (note)" read
        // "Passed (..." and "5 ok, 1 fail" read "5 ok, 1 ..." (layout
        // pictures, 30 Sep).
        //
        // The result is in bold, set on the column rather than on each
        // cell: measured in the face it is drawn in, and a new text size
        // reaches the rows already listed - a face set on each cell stayed
        // at the size the list was filled at.
        _textSize.Keep(() =>
        {
            _grid.Columns["result"]!.DefaultCellStyle.Font = AppTheme.BoldFont;

            KeepColumnsWhole();
        });

        _grid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                AddOrEdit(Selected());
            }
        };

        var btnInspect = new Button { Text = "New Inspection..." };
        btnInspect.Click += (_, _) => AddOrEdit(null);

        var btnEdit = new Button { Text = "Edit..." };
        btnEdit.Click += (_, _) => AddOrEdit(Selected());

        // "Who's Outstanding?" used to sit here. It made you pick a class
        // first - "outstanding" only meant something within one - and then
        // printed a list of names into a message box, which you then had to
        // go somewhere else to act on.
        //
        // Who's Racing answers the same question better: tick "Only those not
        // through tech" and the roster narrows to exactly those cars, across
        // every class at once, with Inspect Selected Car beside each name. The
        // answer and the thing you do about it are in the same place.
        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += (_, _) => DeleteSelected();

        // What the last command did.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        // No caption: the window it opens in is already called Scrutineering
        // log, and the count on the filters' line says how many.
        var records = new Section();

        records.Add(_filterRow);
        records.AddFill(_grid, LeastGridHeight);
        records.Add(new ButtonBar().With(btnInspect, btnEdit, btnDelete));
        records.Add(_lblStatus, gapBelow: 0);

        // --- The page ---

        _page.Add(lblIntro);
        _page.AddFill(records, records.LeastHeight, gapBelow: 0);

        Controls.Add(_page);
    }

    /// <summary>
    /// The least the page can do with at <paramref name="width"/> - its
    /// intro, the filters, the grid's headings and three inspections, the
    /// buttons and the status line - for the window it is shown in, which
    /// gives the page the height it has (PageWindow.ScrutineeringLog).
    /// </summary>
    internal int LeastHeight(int width) => _page.LeastHeight(width);

    /// <summary>The least the grid can do with: its headings and three inspections. Below that the page scrolls instead - see FillPage.</summary>
    private int LeastGridHeight() =>
        _grid.ColumnHeadersHeight + 3 * _grid.RowTemplate.Height + 2;

    /// <summary>
    /// The class filter: wide enough for the longest class name, and never
    /// under the 220px it always had at the normal text size.
    /// </summary>
    private int ClassFilterWidth() =>
        Math.Max(UiLayout.AtTextSize(ClassFilterLeast), Pieces.PickerWidth(_cmbClass, ref _classWidth));

    /// <summary>
    /// The least room the count is kept at the end of the filters' line: what
    /// it says now, or its usual longest, whichever is wider - so it takes a
    /// line of its own rather than end in "...", and does not hop between
    /// lines as the filters change what it says.
    /// </summary>
    private int SummaryWidth() =>
        Math.Max(
            _lblSummary.GetPreferredSize(Size.Empty).Width,
            TextRenderer.MeasureText(LongestSummary, _lblSummary.Font).Width + _lblSummary.Padding.Horizontal);

    private TechInspection? Selected() =>
        _grid.CurrentRow?.Tag is string id ? _store.Get(id) : null;

    private string? SelectedClass =>
        _cmbClass.SelectedItem as string is { } s && s != "(all classes)" ? s : null;

    public void RefreshAll()
    {
        RefreshClassCombo();
        RefreshGrid();
    }

    private void RefreshClassCombo()
    {
        var previous = _cmbClass.SelectedItem as string;

        _cmbClass.Items.Clear();
        _cmbClass.Items.Add("(all classes)");
        foreach (var raceClass in _classes.All)
        {
            _cmbClass.Items.Add(raceClass.Name);
        }

        var index = previous is null ? 0 : _cmbClass.Items.IndexOf(previous);
        _cmbClass.SelectedIndex = index >= 0 ? index : 0;

        // The longest class may have come or gone: measured again, and the
        // filters' line with it.
        _classWidth = null;
        _filterRow.PerformLayout();
    }

    private const string WhenWidestTimeOnly = "88:88 PM";
    private const string WhenWidestWithDate = "88/88/8888 88:88 PM";

    /// <summary>What the When column is kept wide enough for - see RefreshGrid.</summary>
    private string _whenWidest = WhenWidestTimeOnly;

    /// <summary>
    /// The When cell: the time alone for an inspection made today, and the
    /// date with it for any other day.
    ///
    /// It was the time alone for every row. The log lists a whole weekend
    /// meeting (and, with Today only unticked, everything), so Saturday's
    /// and Sunday's inspections of one car both read "9:05 AM" and Edit or
    /// Delete could land on the wrong day's (bug hunt 17).
    /// </summary>
    internal static string WhenText(DateTime local, DateTime today) =>
        local.Date == today.Date ? AppTime.TimeOfDay(local) : AppTime.DateAndTime(local);

    private void KeepColumnsWhole() =>
        Pieces.KeepHeadingsWhole(
            _grid,
            ("when", _whenWidest),
            ("result", "Passed (note)"),
            ("checks", "88 ok, 88 fail"));

    private void RefreshGrid()
    {
        _grid.Rows.Clear();

        // "Today" as clearance means it - this race day, or the recent
        // window when none is open (TechInspectionStore.CountingNow) - not
        // the calendar date, which emptied the log at midnight and on the
        // second day of a weekend meeting while every car was still cleared.
        var inspections = (_chkTodayOnly.Checked ? _store.CountingNow() : _store.All)
            .Where(i => SelectedClass is null || string.Equals(i.ClassName, SelectedClass, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var today = DateTime.Now;

        // The When column is kept wide enough for a date only while a row
        // shows one, so a single night's log keeps the narrow column it had.
        var whenWidest = inspections.Any(i => i.InspectedAtUtc.ToLocalTime().Date != today.Date)
            ? WhenWidestWithDate
            : WhenWidestTimeOnly;

        if (whenWidest != _whenWidest)
        {
            _whenWidest = whenWidest;
            KeepColumnsWhole();
        }

        foreach (var inspection in inspections)
        {
            var checks = inspection.ItemsFailed.Count > 0
                ? $"{inspection.ItemsPassed.Count} ok, {inspection.ItemsFailed.Count} fail"
                : $"{inspection.ItemsPassed.Count} ok";

            var rowIndex = _grid.Rows.Add(
                WhenText(inspection.InspectedAtUtc.ToLocalTime(), today),
                string.IsNullOrWhiteSpace(inspection.DriverName) ? inspection.Transponder : inspection.DriverName,
                string.IsNullOrWhiteSpace(inspection.ClassName) ? "-" : inspection.ClassName,
                string.IsNullOrWhiteSpace(inspection.CarModel) ? "-" : inspection.CarModel,
                inspection.ResultText,
                checks,
                string.IsNullOrWhiteSpace(inspection.Inspector) ? "-" : inspection.Inspector,
                inspection.Notes.Replace('\r', ' ').Replace('\n', ' '));

            _grid.Rows[rowIndex].Tag = inspection.Id;

            _grid.Rows[rowIndex].Cells[4].Style.ForeColor = inspection.Result switch
            {
                TechResult.Failed => AppTheme.Danger,
                TechResult.PassedWithNote => AppTheme.Accent,
                _ => AppTheme.Success,
            };
        }

        var failed = inspections.Count(i => !i.IsCleared);
        _lblSummary.Text = inspections.Count == 0
            ? "No inspections recorded."
            : failed == 0
                ? $"{inspections.Count} inspected, all cleared."
                : $"{inspections.Count} inspected, {failed} NOT cleared.";
        _lblSummary.ForeColor = failed == 0 ? AppTheme.TextMuted : AppTheme.Danger;
    }

    private void AddOrEdit(TechInspection? existing)
    {
        // Taken BEFORE the dialog opens. Taken after, it could already hold
        // the edit - the dialog used to write onto the stored inspection
        // directly - and then Undo put back the edited version and appeared
        // to do nothing. The dialog now edits a copy, but the snapshot
        // belongs before anything that could touch the store either way.
        var before = existing is null ? null : _store.CopyOf(existing.Id);

        using var form = new TechInspectionEditForm(existing, _profiles, _classes);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Just this inspection goes back (or, for a new one, goes away) -
        // not the whole log, which wiped out every inspection made since.
        // The store, not this page, in the undo: the page may be gone by
        // then, and re-reads the store when it is next shown.
        var store = _store;
        var id = form.Result.Id;
        var who = form.Result.DriverName;

        _undo.Capture(existing is null ? $"Record tech inspection for {who}" : $"Edit {who}'s tech inspection", () =>
        {
            if (!store.UndoUpsert(id, before))
            {
                throw new InvalidOperationException($"{who}'s inspection has been deleted since, so there is nothing to put back.");
            }
        });

        _store.Upsert(form.Result);
        RefreshGrid();
        _lblStatus.Text = $"{form.Result.DriverName}: {form.Result.ResultText}.";
    }

    private void DeleteSelected()
    {
        var removed = _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(r => r.Tag as string)
            .Where(id => id is not null)
            .Select(id => _store.Get(id!))
            .Where(i => i is not null)
            .Select(i => i!)
            .ToList();

        if (removed.Count == 0)
        {
            return;
        }

        var what = removed.Count == 1
            ? $"Delete {removed[0].DriverName}'s inspection?"
            : $"Delete these {removed.Count} inspections?";

        if (AppDialog.Show(this, what, "Delete inspection", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var store = _store;
        var placed = removed.Select(i => (Inspection: i, Index: _store.IndexOf(i.Id))).ToList();

        _undo.Capture(removed.Count == 1 ? $"Delete {removed[0].DriverName}'s tech inspection" : $"Delete {removed.Count} tech inspections", () =>
        {
            store.RestoreDeleted(placed);
        });

        foreach (var inspection in removed)
        {
            _store.Delete(inspection.Id);
        }

        RefreshGrid();
    }

}
