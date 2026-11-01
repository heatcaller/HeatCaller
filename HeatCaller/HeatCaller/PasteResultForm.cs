using System.Globalization;

namespace HeatCaller;

/// <summary>
/// Paste a Result (1 Nov 2026, asked for by Asher): the race result copied
/// off another timing program's results page - LiveRC's is the one it is
/// made for - shown as it will come in, with the class, the round and the
/// day it was run to set, before it is put into Race History.
///
/// The race comes in marked as imported (<see cref="SessionRecord.ImportedFrom"/>),
/// so it is kept for the record and never scores: no personal bests, track
/// records, badges, points or commentary. It is not linked to a round of the
/// race day either - a linked round reads as run, and feeds qualifying and
/// points - so the class and round are kept in its label.
///
/// On the layout pieces (DialogFit): the note across the top, the class,
/// round and day under it, the drivers as they will come in taking the
/// height, two lines for what couldn't be read, and Import and Cancel
/// against the right-hand edge.
/// </summary>
internal sealed class PasteResultForm : Form
{
    /// <summary>The list shows its headings and at least this many rows before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    /// <summary>Lines kept for what couldn't be read, so a message appearing moves nothing.</summary>
    private const int ProblemLines = 2;

    private readonly PastedResult.Race _race;
    private readonly string _source;
    private readonly Func<string, DriverProfile?> _match;
    private readonly ComboBox _cmbClass = new();
    private readonly TextBox _txtRound = new();
    private readonly DateTimePicker _dtDay = new();
    private readonly TimePicker _tpTime = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblProblems = new();

    /// <summary>The race as it goes into Race History. Set when Import is pressed.</summary>
    public SessionRecord? Session { get; private set; }

    /// <param name="race">The result read off the clipboard.</param>
    /// <param name="source">Where it came from, for "Imported from" - see <see cref="PastedResult.SourceName"/>.</param>
    /// <param name="title">The race's name when the copy took it in ("Exp. 1-8 Electric Buggy A-Main"), or null.</param>
    /// <param name="classNames">The club's own classes, offered for the class.</param>
    /// <param name="match">The profile for a pasted name, or null - see <see cref="PastedResult.MatchByName"/>.</param>
    /// <param name="runLocal">The day and time the race is first set to.</param>
    public PasteResultForm(
        PastedResult.Race race,
        string source,
        string? title,
        IReadOnlyList<string> classNames,
        Func<string, DriverProfile?> match,
        DateTime runLocal)
    {
        _race = race;
        _source = source;
        _match = match;

        Text = "Paste a result";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var page = BuildLayout(title, classNames, runLocal);

        AppTheme.ApplyRecursively(this);
        Pieces.KeepHeadingsWhole(_grid);

        _lblProblems.MinimumSize = new Size(0, ProblemLines * Pieces.LineHeight(_lblProblems.Font) + _lblProblems.Padding.Vertical);

        DialogFit.Keep(this, page, width: 640, height: 560);
    }

    private FillPage BuildLayout(string? title, IReadOnlyList<string> classNames, DateTime runLocal)
    {
        var lblIntro = new Label
        {
            Text = $"This race comes into Race History as imported from {_source}. It's kept for the record and never "
                   + "scores: no personal bests, records, badges, points or commentary. Drivers are matched to your "
                   + "profiles by name, and one that isn't matched comes in by name alone.",
            AutoSize = true,
            Font = AppTheme.BodyFont,
        };

        var (className, round) = PastedResult.SplitTitle(title, classNames);

        _cmbClass.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbClass.Items.AddRange(classNames.Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
        _cmbClass.Text = className;

        _txtRound.Text = round;

        _dtDay.Format = DateTimePickerFormat.Short;
        _dtDay.Value = runLocal.Date;

        // TimePicker for the time of day, as the rest of the app uses - see
        // RaceDayEventControl's first race.
        _tpTime.AutoSize = true;
        _tpTime.Value = runLocal.TimeOfDay;

        var fields = new FieldGrid();
        fields.AddRow("Class:", _cmbClass, stretch: true);
        fields.AddRow("Round:", _txtRound, stretch: true);
        fields.AddRow("Run on:", FieldGrid.Inline(_dtDay, _tpTime));

        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        void Column(string name, string heading, float weight) =>
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = heading, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });

        Column("pos", "Pos", 10);
        Column("driver", "Driver", 34);
        Column("profile", "Profile", 22);
        Column("laps", "Laps", 10);
        Column("time", "Time", 14);
        Column("fast", "Fast lap", 14);

        var unmatched = 0;

        foreach (var row in _race.Rows)
        {
            var profile = _match(row.Name);

            if (profile is null)
            {
                unmatched++;
            }

            _grid.Rows.Add(
                row.Position.ToString(CultureInfo.InvariantCulture),
                row.Name,
                profile is null ? "None, by name" : "Matched",
                row.Laps.ToString(CultureInfo.InvariantCulture),
                TimeText(row.ElapsedMs),
                TimeText(row.FastLapMs));
        }

        _lblProblems.AutoSize = true;
        _lblProblems.Font = AppTheme.BodyFont;
        _lblProblems.ForeColor = AppTheme.TextMuted;
        _lblProblems.Text = ProblemsText(_race, unmatched);

        var btnImport = new Button { Text = "Import" };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        btnImport.Enabled = _race.Rows.Count > 0;
        btnImport.Click += (_, _) => Import();

        var footer = new ButtonBar(alignRight: true).With(btnCancel, btnImport);

        var page = new FillPage();
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(fields, gapBelow: UiLayout.RowGap);
        page.AddFill(_grid, _ => _grid.ColumnHeadersHeight + LeastRows * _grid.RowTemplate.Height + 2, gapBelow: UiLayout.RowGap);
        page.Add(_lblProblems, gapBelow: UiLayout.RowGap);
        page.Add(footer, gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnImport;
        CancelButton = btnCancel;

        return page;
    }

    /// <summary>What the list below the drivers says: rows left out, and drivers with no profile.</summary>
    internal static string ProblemsText(PastedResult.Race race, int unmatched)
    {
        var lines = new List<string>();

        if (race.Rows.Count == 0)
        {
            lines.Add("No driver in the pasted table could be read.");
        }

        if (unmatched > 0)
        {
            lines.Add(unmatched == 1
                ? "1 driver has no profile here with that name, so comes in by name alone."
                : $"{unmatched} drivers have no profile here with that name, so come in by name alone.");
        }

        lines.AddRange(race.Problems.Take(3));

        if (race.Problems.Count > 3)
        {
            lines.Add($"And {race.Problems.Count - 3} more rows left out.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>A time in a results list - "10:10.631", "31.234" - or nothing for none.</summary>
    internal static string TimeText(long ms)
    {
        if (ms <= 0)
        {
            return string.Empty;
        }

        var time = TimeSpan.FromMilliseconds(ms);

        return time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss\.fff", CultureInfo.InvariantCulture)
            : time.TotalMinutes >= 1
                ? time.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture)
                : time.ToString(@"s\.fff", CultureInfo.InvariantCulture);
    }

    private void Import()
    {
        var runLocal = _dtDay.Value.Date + _tpTime.Value;

        Session = PastedResult.ToSession(_race, _cmbClass.Text, _txtRound.Text, runLocal, _source, _match);
        DialogResult = DialogResult.OK;
        Close();
    }
}
