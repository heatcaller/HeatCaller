using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The classes that race here, and the rules each one runs under.
///
/// A PAGE, not a dialog behind a button on another page - which is what it
/// was, and why it took hunting to find. Everything that decides how a
/// class is scored lives on a class: its qualifying method, how many heats
/// count, how big a main is, whether bump-ups happen, its formats, its
/// tech checklist, its colour, and now which sanctioning body's rules it
/// follows. All of that was reachable only through Driver Profiles, then a
/// button, then a list, then Edit - four steps into a page that is about
/// people rather than rules.
///
/// The grid is the point. Opening the editor tells you about ONE class;
/// the fault it fixes is the one you only see side by side - a class
/// scored on laps next to one scored on consecutive laps, a ten-car main
/// next to an eight, bump-ups on for one class and off for another. That
/// is what the columns below are for.
/// </summary>
public sealed class RaceClassesControl : UserControl
{
    private readonly RaceClassStore _store;
    private readonly UndoService? _undo;

    // Where each class's handicap mode is kept, by class name. Only here so
    // a delete can forget the deleted class's mode (DeleteSelected). Null
    // where the caller has none to give, and then a delete leaves the mode
    // as it always did.
    private readonly HandicapStore? _handicaps;

    private readonly DataGridView _grid = new();
    private readonly Label _lblSummary = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    private readonly Button _btnAdd = new() { Text = "Add a Class..." };
    private readonly Button _btnEdit = new() { Text = "Edit..." };
    private readonly Button _btnPreset = new() { Text = "Sanctioning Preset..." };
    private readonly Button _btnDelete = new() { Text = "Delete" };

    /// <summary>The page the rest is laid out on - see LeastHeight.</summary>
    private readonly FillPage _page = new();

    public RaceClassesControl(RaceClassStore store, UndoService? undo = null, HandicapStore? handicaps = null)
    {
        _store = store;
        _undo = undo;
        _handicaps = handicaps ?? store.Handicaps;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No MinimumSize. It was 700x420, which WinForms honours over the
        // size the page is given - so a window shorter than that had the
        // buttons under the grid off the bottom of the page. A short window
        // scrolls the page instead (FillPage).
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
    /// The intro, then one card: how many classes there are, the grid of
    /// them taking the height that is left, and the buttons under it. On the
    /// layout pieces (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// It used to be: the grid stretched and the buttons flowed on every
    /// resize, over a 700x420 floor the page kept however small its window,
    /// with the intro capped at a width that fitted that floor and wrapped
    /// short of the page on any wider one.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "A class is the rulebook a group of drivers races under: how qualifying is scored, how many heats "
                 + "count, how big a main is, whether bump-ups happen, and what a scrutineer checks. Everything here "
                 + "is per class, because a club rarely runs two classes the same way.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _lblSummary.AutoSize = true;
        _lblSummary.ForeColor = AppTheme.TextMuted;

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "class", HeaderText = "Class", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "qualifying", HeaderText = "Qualifying", FillWeight = 120 },
            new DataGridViewTextBoxColumn { Name = "mains", HeaderText = "Mains", FillWeight = 100 },
            new DataGridViewTextBoxColumn { Name = "format", HeaderText = "Race format", FillWeight = 130 },
            new DataGridViewTextBoxColumn { Name = "tech", HeaderText = "Tech", FillWeight = 55 });

        _grid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                EditSelected();
            }
        };

        _grid.SelectionChanged += (_, _) => UpdateButtons();

        _btnAdd.Click += (_, _) => AddNew();
        _btnEdit.Click += (_, _) => EditSelected();
        _btnPreset.Click += (_, _) => ApplyPresetToSelected();
        _btnDelete.Click += (_, _) => DeleteSelected();

        // No caption: the page's heading already says "Classes", and the
        // count under it says how many.
        var list = new Section();

        list.Add(_lblSummary);
        list.AddFill(_grid, LeastGridHeight);
        list.Add(new ButtonBar().With(_btnAdd, _btnEdit, _btnPreset, _btnDelete), gapBelow: 0);

        _page.Add(lblIntro);
        _page.AddFill(list, list.LeastHeight, gapBelow: 0);

        Controls.Add(_page);
    }

    /// <summary>
    /// The least the page can do with at <paramref name="width"/> - its
    /// intro, the count, the grid's headings and three classes, and its
    /// buttons - for the Race Classes window, which gives the page the
    /// height it has (RaceClassManagerForm).
    /// </summary>
    internal int LeastHeight(int width) => _page.LeastHeight(width);

    /// <summary>The least the grid can do with: its headings and three classes. Below that the page scrolls instead - see FillPage.</summary>
    private int LeastGridHeight() =>
        _grid.ColumnHeadersHeight + 3 * _grid.RowTemplate.Height;

    /// <summary>Called when the page becomes visible - a class can be edited from the driver pages too.</summary>
    public void RefreshAll()
    {
        var selectedId = SelectedClass()?.Id;

        _grid.Rows.Clear();

        foreach (var raceClass in _store.All)
        {
            var index = _grid.Rows.Add(
                raceClass.Name,
                DescribeQualifying(raceClass),
                DescribeMains(raceClass),
                DescribeFormat(raceClass),
                raceClass.TechCheckItems.Count == 0 ? "-" : raceClass.TechCheckItems.Count.ToString());

            var row = _grid.Rows[index];

            row.Tag = raceClass.Id;

            // The class's own colour on its name, the same one its heat
            // sheets are banded with - so this page and the paper on the
            // pit table are recognisably about the same thing.
            row.Cells["class"].Style.ForeColor = OnPageBackground(raceClass.Colour);
            row.Cells["class"].Style.Font = AppTheme.BoldFont;

            if (raceClass.Id == selectedId)
            {
                _grid.CurrentCell = row.Cells[0];
                row.Selected = true;
            }
        }

        _lblSummary.Text = _store.All.Count switch
        {
            0 => "No classes yet. Add one and it becomes available on Race Day, in driver profiles and in the program.",
            1 => "1 class.",
            var n => $"{n} classes.",
        };

        UpdateButtons();
    }

    /// <summary>
    /// A class colour as it should show on this page. The stored colour is
    /// chosen to read on white paper, so a light look shows it as printed;
    /// a dark look lifts it (see OnDarkBackground). Asked of the look's
    /// family rather than assumed, now that a club can run a light look -
    /// lifted on white, a pale class colour all but disappears.
    /// </summary>
    private static Color OnPageBackground(Color printed) =>
        AppTheme.Active.Light ? printed : OnDarkBackground(printed);

    /// <summary>
    /// A printed colour lifted for a dark screen. Same treatment the
    /// programme grid gives it, for the same reason: the stored colour is
    /// chosen to read on white paper.
    /// </summary>
    private static Color OnDarkBackground(Color printed) => Color.FromArgb(
        Math.Min(255, printed.R + 90),
        Math.Min(255, printed.G + 90),
        Math.Min(255, printed.B + 90));

    private static string DescribeQualifying(RaceClass raceClass)
    {
        var method = raceClass.QualifyingMethod == QualifyingMethod.BestConsecutiveLaps
            ? $"Best {raceClass.ConsecutiveLapCount} consecutive"
            : "Most laps, then time";

        return raceClass.CountBestRounds <= 1
            ? $"{method}  ·  best heat"
            : $"{method}  ·  best {raceClass.CountBestRounds} heats";
    }

    private static string DescribeMains(RaceClass raceClass) =>
        raceClass.BumpUpCount <= 0
            ? $"{raceClass.MainSize} per main  ·  no bump-ups"
            : $"{raceClass.MainSize} per main  ·  top {raceClass.BumpUpCount} bump up";

    /// <summary>
    /// The race format, and the qualifier's separately when it has one -
    /// the commonest club shape is five-minute quals and eight-minute
    /// mains, and a page that showed only one of them would be showing
    /// half the answer.
    /// </summary>
    private static string DescribeFormat(RaceClass raceClass)
    {
        var race = raceClass.Format?.Describe() ?? "app default";

        return raceClass.QualifierFormat is null
            ? race
            : $"{race}   //   quals: {raceClass.QualifierFormat.Describe()}";
    }

    private RaceClass? SelectedClass() =>
        _grid.SelectedRows.Count == 1 && _grid.SelectedRows[0].Tag is string id
            ? _store.Get(id)
            : null;

    private void UpdateButtons()
    {
        var one = SelectedClass() is not null;

        _btnEdit.Enabled = one;
        _btnPreset.Enabled = one;
        _btnDelete.Enabled = one;
    }

    private void AddNew()
    {
        using var form = new RaceClassEditForm(null, _store);

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _store.Upsert(form.Result);
            RefreshAll();
        }
    }

    private void EditSelected()
    {
        if (SelectedClass() is not { } existing)
        {
            return;
        }

        // Both taken before the dialog opens, because the dialog edits the
        // stored class in place: once it closes there is no "before" left
        // to read the old name or the old settings from.
        var oldName = existing.Name;
        var beforeJson = JsonSerializer.Serialize(existing);
        var before = JsonSerializer.Deserialize<RaceClass>(beforeJson);

        using var form = new RaceClassEditForm(existing, _store);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            // Cancel has to mean cancel. The dialog's own dialogs - the
            // sanctioning preset, the tech checklist - write onto the stored
            // class as they go, so without this a preset applied and then
            // cancelled stayed on the class in memory and was saved with
            // the next change to any class.
            //
            // Put back onto the stored class itself (RevertEdit, against the
            // class as the dialog left it) rather than swapping the snapshot
            // in: something else may hold the stored class, such as the race
            // format undo on Race Control, which checks the format is still
            // the very one it set. The snapshot only goes in if the class has
            // somehow gone from the store.
            var cancelledJson = JsonSerializer.Serialize(existing);

            if (before is not null && cancelledJson != beforeJson)
            {
                if (!_store.RevertEdit(before, cancelledJson, keepName: false))
                {
                    _store.Upsert(before);
                }

                RefreshAll();
            }

            return;
        }

        var newName = form.Result.Name;

        // The class as this edit leaves it, so the undo can tell which
        // settings the edit changed from ones changed since.
        var afterJson = JsonSerializer.Serialize(form.Result);
        var store = _store;

        // A rename has to take everything that names the class with it -
        // drivers, rounds, championships, tech inspections, race days.
        // Renaming the class alone used to strand all of them under a name
        // that no longer existed. Ordinal, so a change of case alone
        // ("stock" to "Stock") is carried through too.
        if (!string.Equals(oldName, newName, StringComparison.Ordinal) && _store.RenameCascade is { } cascade)
        {
            ClassRenameReceipt? receipt = null;

            // One undo for the whole thing: the class back as it was, and
            // exactly what moved moved back. Captured before the change, as
            // the undo stack expects, and closing over the receipt that is
            // filled in just below.
            _undo?.Capture($"Rename class '{oldName}' to '{newName}'", () =>
            {
                if (receipt is not null)
                {
                    cascade.Undo(receipt);
                }

                // The name, and whatever else the same dialog changed - it
                // used to put back only the name, so a rename made together
                // with a new main size or format kept those after the undo.
                // Setting by setting (RevertEdit), not the whole pre-rename
                // class: an edit made since to anything else survives. Only
                // when the class has gone altogether is the snapshot used, so
                // the drivers and rounds just moved back to the old name
                // have a class to belong to.
                if (before is not null && !store.RevertEdit(before, afterJson, keepName: false))
                {
                    store.Upsert(before);
                }

                if (!IsDisposed && !Disposing)
                {
                    RefreshAll();
                }
            });

            receipt = cascade.Rename(oldName, newName);

            _store.Upsert(form.Result);
            RefreshAll();

            var moved = new List<string>();

            if (receipt.Drivers.Count > 0)
            {
                moved.Add(Plural.Of(receipt.Drivers.Count, "driver"));
            }

            if (receipt.Rounds.Count > 0)
            {
                moved.Add(Plural.Of(receipt.Rounds.Count, "round"));
            }

            if (receipt.Championships.Count > 0)
            {
                moved.Add(Plural.Of(receipt.Championships.Count, "championship"));
            }

            if (receipt.Inspections.Count > 0)
            {
                moved.Add(Plural.Of(receipt.Inspections.Count, "tech inspection"));
            }

            if (receipt.RaceDays.Count > 0)
            {
                moved.Add(Plural.Of(receipt.RaceDays.Count, "race day"));
            }

            if (receipt.RunSheetLines.Count > 0)
            {
                moved.Add(Plural.Of(receipt.RunSheetLines.Count, "run sheet line"));
            }

            AppDialog.Show(this,
                $"Renamed \"{oldName}\" to \"{newName}\"."
                + (moved.Count > 0 ? $"\r\n\r\nMoved to the new name: {string.Join(", ", moved)}." : string.Empty)
                + (_undo is not null ? "\r\n\r\nUndo puts the old name back everywhere." : string.Empty),
                "Class renamed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // OK pressed with nothing changed: no do-nothing step on Undo.
        if (before is not null && afterJson != beforeJson)
        {
            // keepName false: the name is unchanged here, unless there is no
            // rename cascade, when the name is all a rename is.
            _undo?.Capture($"Edit class '{newName}'", () =>
            {
                if (!store.RevertEdit(before, afterJson, keepName: false))
                {
                    throw new InvalidOperationException($"the class \"{newName}\" has been deleted since, so there is nothing to put back.");
                }
            });
        }

        _store.Upsert(form.Result);
        RefreshAll();
    }

    /// <summary>
    /// The second door to the sanctioning presets.
    ///
    /// The first is inside the class editor, which is the right place for
    /// it and the wrong place to FIND it - somebody looking for "what does
    /// PN Racing say" is looking at a list of classes, not at one class's
    /// settings dialog.
    /// </summary>
    private void ApplyPresetToSelected()
    {
        if (SelectedClass() is not { } existing)
        {
            return;
        }

        // Taken before the preset writes onto the stored object.
        var before = JsonSerializer.Deserialize<RaceClass>(JsonSerializer.Serialize(existing));

        using var form = new SanctioningPresetForm(existing, _store.AppDefaultFormat?.Invoke());

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (before is not null)
        {
            var afterJson = JsonSerializer.Serialize(existing);
            var store = _store;
            var preset = form.Chosen is { } chosen ? $"{chosen.Body} - {chosen.Name}" : "sanctioning";

            // Only the settings the preset changed go back (RevertEdit), so
            // anything edited on the class since is left as it is.
            _undo?.Capture($"Apply the {preset} preset to '{existing.Name}'", () =>
            {
                if (!store.RevertEdit(before, afterJson, keepName: true))
                {
                    throw new InvalidOperationException($"the class \"{before.Name}\" has been deleted since, so there is nothing to put back.");
                }
            });
        }

        // The preset wrote onto the stored object directly, so this is a
        // save rather than a copy-back.
        _store.Upsert(existing);
        RefreshAll();
    }

    private void DeleteSelected()
    {
        if (SelectedClass() is not { } doomed)
        {
            return;
        }

        if (AppDialog.Show(this,
                $"Delete class \"{doomed.Name}\"? Drivers keep their label but it won't appear in the class list anymore.",
                "Delete class", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var classes = _store;
        var handicaps = _handicaps;

        // The handicap mode is kept by class name, not on the class, so
        // deleting the class used to leave it behind - and a class added
        // later under the same name was scored with time credits (or
        // whatever the old one had) from its first race, without anyone
        // choosing that. Forgotten just after the delete below, and handed
        // back by the undo: the undo reads this variable when it runs, so it
        // puts back exactly what this delete removed.
        var removedMode = HandicapMode.None;

        _undo?.Capture($"Delete class '{doomed.Name}'", () =>
        {
            if (classes.Get(doomed.Id) is not null)
            {
                return;
            }

            // A class added since under the same name has the old one's
            // drivers now (classes are matched by name); two of them would
            // share everything without anyone meaning them to.
            if (classes.NameTaken(doomed.Name, doomed.Id))
            {
                throw new InvalidOperationException($"a new class called \"{doomed.Name}\" has been added since. Delete or rename that one first to have this one back.");
            }

            classes.Upsert(doomed);

            // Only when the delete actually removed one: RestoreClassMode
            // with None would clear a mode, and there is none to clear.
            if (removedMode != HandicapMode.None)
            {
                handicaps?.RestoreClassMode(doomed.Name, removedMode);
            }

            if (!IsDisposed && !Disposing)
            {
                RefreshAll();
            }
        });

        _store.Delete(doomed.Id);
        removedMode = handicaps?.RemoveClassMode(doomed.Name) ?? HandicapMode.None;
        RefreshAll();
    }
}
