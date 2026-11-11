using System.Globalization;

namespace HeatCaller;

/// <summary>
/// The preview of an entry import (11 Nov 2026, build plan 7.1), after the
/// column picks (CsvMappingForm) and before anything is saved: every row
/// with what it will do - a new driver, a driver updated and how, a driver
/// already here with nothing to change, or a row skipped and why - and the
/// two choices that change the outcome: whether everyone on the list is
/// checked in to tonight's race day, and whether details already on a
/// profile that the sheet says differently are replaced.
///
/// WHY A PREVIEW AT ALL. Driver Hub's roster import goes straight in and
/// reports afterwards. An entry list is mostly regulars who are already
/// here, so most of its rows CHANGE somebody - and a desk that can't see
/// "Sam Smith: adds class Mod, keeps race number 12 (the sheet says 7)"
/// before it happens is having the profiles overwritten silently, which
/// the build plan says this import must never do. The plan behind it is
/// pure (EntryImport.Plan) and holds copies, so Cancel here leaves
/// everything as it was.
///
/// On the layout pieces (DialogFit): the note across the top, the summary,
/// the rows taking the height, the classes the club hasn't got, the two
/// ticks, and Import and Cancel against the right-hand edge. The summary
/// and the classes line are as tall as the longest they can say
/// (SteadyLabel), so ticking a box moves nothing.
/// </summary>
internal sealed class EntryImportForm : Form
{
    /// <summary>The list shows its headings and at least this many rows before the window scrolls instead.</summary>
    private const int LeastRows = 5;

    private readonly IReadOnlyList<EntryImport.EntryRow> _rows;
    private readonly IReadOnlyList<DriverProfile> _profiles;
    private readonly IReadOnlyCollection<string> _classes;
    private readonly DataGridView _grid = new();
    private readonly SteadyLabel _lblSummary = new();
    private readonly SteadyLabel _lblClasses = new();
    private readonly CheckBox _chkCheckIn = new ThemedCheckBox();
    private readonly CheckBox _chkReplace = new ThemedCheckBox();
    private readonly Button _btnImport = new() { Text = "Import" };

    /// <summary>The import as it stands with the ticks as they are now.</summary>
    public EntryImport.EntryPlan Plan { get; private set; }

    /// <summary>Whether everyone on the list is to be checked in. Read when Import is pressed.</summary>
    public bool CheckIn => _chkCheckIn.Enabled && _chkCheckIn.Checked;

    /// <param name="source">Where the list came from, for the note at the top - the file's name, or "a pasted table".</param>
    /// <param name="rows">The list, read through the column picks.</param>
    /// <param name="profiles">Every driver here now - read, never changed.</param>
    /// <param name="classes">The classes on the Classes page.</param>
    /// <param name="checkInTo">
    /// What the check-in tick says it checks them in to: "\"Club Night 11 Nov\"" for an open race day, an empty
    /// string when no race day is open (the desk still checks in), or null when it can't - the day is closed.
    /// </param>
    public EntryImportForm(
        string source,
        IReadOnlyList<EntryImport.EntryRow> rows,
        IReadOnlyList<DriverProfile> profiles,
        IReadOnlyCollection<string> classes,
        string? checkInTo)
    {
        _rows = rows;
        _profiles = profiles;
        _classes = classes;

        Text = "Import entries";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        Plan = EntryImport.Plan(rows, profiles, classes, replaceDiffering: false);

        var page = BuildLayout(source, checkInTo);

        AppTheme.ApplyRecursively(this);
        Pieces.KeepHeadingsWhole(_grid);

        ShowPlan(Plan);

        DialogFit.Keep(this, page, width: 900, height: 600);
    }

    private FillPage BuildLayout(string source, string? checkInTo)
    {
        var lblIntro = new Label
        {
            Text = $"The entries from {source}, and what each row will do. Nothing has changed yet. A class on a row is added "
                   + "to the classes the driver races - their main class stays as it is - and a detail already on a profile "
                   + "is only replaced when you tick the box below. The whole import is one step for Undo.",
            AutoSize = true,
            Font = AppTheme.BodyFont,
        };

        // The summary with either tick - the counts can differ, a driver
        // with only a kept detail becoming an updated one.
        var replaced = EntryImport.Plan(_rows, _profiles, _classes, replaceDiffering: true);
        _lblSummary.Wordings = new[] { Plan.Summary(), replaced.Summary() };
        _lblSummary.Font = AppTheme.BoldFont;

        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        void Column(string name, string heading, float weight) =>
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = heading, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });

        Column("row", "Row", 8);
        Column("driver", "Driver", 22);
        Column("transponder", "Transponder", 14);
        Column("classes", "Classes", 18);
        Column("what", "What happens", 38);

        _lblClasses.Font = AppTheme.SmallFont;
        _lblClasses.ForeColor = AppTheme.TextMuted;
        _lblClasses.Wordings = new[] { UnknownClassesText(Plan.UnknownClasses) };

        _chkCheckIn.AutoSize = true;

        if (checkInTo is null)
        {
            _chkCheckIn.Text = "Check them in - not while tonight's race day is closed";
            _chkCheckIn.Checked = false;
            _chkCheckIn.Enabled = false;
        }
        else
        {
            _chkCheckIn.Text = checkInTo.Length == 0
                ? "Check everyone on the list in on Who's Racing"
                : $"Check everyone on the list in to {checkInTo}";

            // On for a race day - an entry list IS who is racing - and off
            // without one, when the list may be for another night.
            _chkCheckIn.Checked = checkInTo.Length > 0;
        }

        _chkReplace.AutoSize = true;
        _chkReplace.Text = "Replace details that differ (race number, car, team, sponsors, email, notes)";
        _chkReplace.CheckedChanged += (_, _) =>
        {
            Plan = EntryImport.Plan(_rows, _profiles, _classes, _chkReplace.Checked);
            ShowPlan(Plan);
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        _btnImport.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        var page = new FillPage();
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(_lblSummary, gapBelow: UiLayout.RowGap);
        page.AddFill(_grid, _ => _grid.ColumnHeadersHeight + LeastRows * _grid.RowTemplate.Height + 2, gapBelow: UiLayout.RowGap);
        page.Add(_lblClasses, gapBelow: UiLayout.RowGap);
        page.Add(_chkCheckIn, gapBelow: UiLayout.HintGap);
        page.Add(_chkReplace, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, _btnImport), gapBelow: 0);

        Controls.Add(page);

        AcceptButton = _btnImport;
        CancelButton = btnCancel;

        return page;
    }

    /// <summary>The plan in the list and the lines around it.</summary>
    private void ShowPlan(EntryImport.EntryPlan plan)
    {
        _lblSummary.Text = plan.Summary();
        _lblClasses.Text = UnknownClassesText(plan.UnknownClasses);

        _grid.SuspendLayout();
        _grid.Rows.Clear();

        foreach (var entry in plan.Entries)
        {
            var index = _grid.Rows.Add(
                entry.Line.ToString(CultureInfo.InvariantCulture),
                entry.Name,
                entry.Transponder,
                string.Join(", ", entry.Classes),
                WhatHappens(entry));

            var row = _grid.Rows[index];

            if (entry.Action == EntryImport.EntryAction.Skip)
            {
                row.DefaultCellStyle.ForeColor = AppTheme.Danger;
            }
            else if (entry.Action == EntryImport.EntryAction.Unchanged)
            {
                row.DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }
        }

        _grid.ResumeLayout();

        // Nothing to import when every row is skipped - and when a row
        // does something, even only a check-in, there is.
        _btnImport.Enabled = plan.Entries.Any(e => e.Action != EntryImport.EntryAction.Skip);
    }

    /// <summary>A row's "What happens": "New driver - class Stock", "Skipped: ...", "Updated - adds class Mod; keeps race number 12 (the sheet says 7)".</summary>
    internal static string WhatHappens(EntryImport.PlannedEntry entry) => entry.Action switch
    {
        EntryImport.EntryAction.Skip => $"Skipped: {entry.Reason}.",
        EntryImport.EntryAction.New => entry.Changes.Count == 0 ? "New driver" : "New driver - " + string.Join("; ", entry.Changes),
        EntryImport.EntryAction.Update => "Updated - " + string.Join("; ", entry.Changes),
        _ => "Already here - " + string.Join("; ", entry.Changes),
    };

    /// <summary>The line under the list about classes the club hasn't got - empty when every class is known.</summary>
    internal static string UnknownClassesText(IReadOnlyList<string> unknown) =>
        unknown.Count == 0
            ? string.Empty
            : $"Not on the Classes page yet: {string.Join(", ", unknown)}. Drivers are entered in "
              + (unknown.Count == 1 ? "it" : "them")
              + " as the sheet spells it - add or rename the class on Classes before building tonight's heats.";
}
