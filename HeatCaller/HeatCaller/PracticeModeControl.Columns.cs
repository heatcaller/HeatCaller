namespace HeatCaller;

/// <summary>
/// PRACTICE'S COLUMNS AND DOCK INBOX, AS RACE CONTROL HAS THEM (24 Nov 2026,
/// asked for by Asher: "on the practice screen, implement the same custom
/// columns thing we did on race control then remove the columns button.
/// also add the same button to dock the inbox on practice like we have on
/// race control").
///
/// The table's columns are chosen by right-clicking it: a drop-down of
/// ticks and arrows (ColumnsPopover) that changes the table straight away,
/// saves when it closes, and puts the whole visit on the Undo list as one
/// step. Its "More column options..." opens the full picker, which is what
/// the Choose Columns button did. A right-click on a heading starts the
/// list on that heading's column. Practice has no menu of its own on a
/// row, so a right-click anywhere on the table opens it.
///
/// Dock Inbox is Race Control's button (DockInboxSquare), beside Expand.
/// </summary>
public sealed partial class PracticeModeControl
{
    /// <summary>Dock Inbox, beside Expand.</summary>
    private readonly DockInboxSquare _btnDockInbox = new();

    /// <summary>Dock Inbox was pressed: the host docks the inbox (true) or makes it a window of its own again (false).</summary>
    internal event Action<bool>? DockInboxRequested;

    /// <summary>The Dock Inbox button - for the tests.</summary>
    internal Button DockInboxButton => _btnDockInbox;

    /// <summary>The inbox docked, undocked or closed, from anywhere - for Dock Inbox's caption.</summary>
    internal void ShowInboxDocked(bool dockedOnScreen) => _btnDockInbox.ShowDocked(dockedOnScreen);

    /// <summary>Wires the table's right-click and Dock Inbox. Called once, as the page is built.</summary>
    private void BuildColumnsAndDock()
    {
        _btnDockInbox.DockRequested += dock => DockInboxRequested?.Invoke(dock);

        _grid.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var hit = _grid.HitTest(e.X, e.Y);
            var key = hit.Type == DataGridViewHitTestType.ColumnHeader && hit.ColumnIndex >= 0 && hit.ColumnIndex < _grid.Columns.Count
                ? _grid.Columns[hit.ColumnIndex].Name
                : null;
            var x = key is null ? 0 : e.X;

            OpenColumnsPopover(new Point(x, _grid.ColumnHeadersHeight), key);
        };
    }

    /// <summary>The table's columns as a drop-down of ticks and arrows - see the class notes.</summary>
    private void OpenColumnsPopover(Point at, string? focusKey)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        var visit = BeginColumnsVisit();

        ColumnsPopover.Show(_grid, at, visit.Choices, focusKey, KpiCatalog.DefaultPractice,
            closed: () => EndColumnsVisit(visit),
            more: PickColumns);
    }

    /// <summary>
    /// Starts a visit to the columns drop-down: the list starts from the
    /// table's columns, and every change to it is put on the table at once.
    /// Apart from showing the menu, so it can be tested without one.
    /// </summary>
    internal RaceControlControl.ColumnsVisit BeginColumnsVisit()
    {
        var shownBefore = _columns.Select(k => k.Key).ToList();
        var visit = new RaceControlControl.ColumnsVisit(new ColumnChoices(shownBefore, KpiCatalog.All), _settings.PracticeKpis.ToList(), shownBefore);

        visit.Choices.Changed += (_, _) =>
        {
            if (IsDisposed)
            {
                return;
            }

            _settings.PracticeKpis = visit.Choices.ShownKeys;
            RebuildColumns();
            RefreshGrid();
        };

        return visit;
    }

    /// <summary>
    /// Ends a visit when the drop-down closes: saves, and puts the whole
    /// visit on the Undo list as one step - nothing, when the table ended as
    /// it started.
    /// </summary>
    internal void EndColumnsVisit(RaceControlControl.ColumnsVisit visit)
    {
        if (visit.Choices.ShownKeys.SequenceEqual(visit.ShownBefore, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.Save();

        var savedBefore = visit.SavedBefore.ToList();
        _undo?.Capture("Change the Practice columns", () => PutColumnsBack(savedBefore));
    }

    /// <summary>The full column picker, with Undo - what the Choose Columns button did until 24 Nov 2026.</summary>
    private void PickColumns()
    {
        using var form = new KpiPickerForm("Practice", _settings.PracticeKpis, KpiCatalog.DefaultPractice);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // The picker's Restore Defaults lands here too, so the old list is
        // kept for Undo. This page lives as long as the app.
        var before = _settings.PracticeKpis.ToList();

        if (!before.SequenceEqual(form.SelectedKeys))
        {
            _undo?.Capture("Change the Practice columns", () => PutColumnsBack(before));
        }

        _settings.PracticeKpis = form.SelectedKeys;
        _settings.Save();
        RebuildColumns();
        RefreshGrid();
    }

    private void PutColumnsBack(List<string> saved)
    {
        _settings.PracticeKpis = saved.ToList();
        _settings.Save();

        if (!IsDisposed)
        {
            RebuildColumns();
            RefreshGrid();
        }
    }
}
