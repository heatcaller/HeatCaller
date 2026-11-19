namespace HeatCaller;

/// <summary>
/// The live qualifying order, while the heat is still running.
///
/// A window of its own rather than a panel wedged into Race Control, and
/// that is the point of it: the people who want this are not the person
/// driving the laptop. It goes on the second screen, or the projector, or
/// gets turned round to face the pit table - none of which a panel sharing
/// the race director's page can do.
///
/// It shows the WHOLE class, not just the drivers in this heat, because
/// "am I still in the A-main" is a question about everyone. Drivers on
/// track are marked so it is obvious which lines are still moving.
///
/// Closing it costs nothing: it holds no state, it is rebuilt from the
/// projector on every tick, and the projector is cached so an open window
/// that nobody is watching is a dictionary lookup four times a second.
///
/// On the layout pieces (DialogFit): the class and the rule it is ranked
/// by across the top, each wrapping to the window's width; the order
/// taking the height under them - or, in its place, the note that no
/// qualifier is running; and the keep-on-top box at the foot. It used to be
/// placed in pixels: the two heading lines in a docked strip of fixed
/// height, a line each, so a class and round long enough to wrap onto a
/// second line lost it.
/// </summary>
public sealed class QualifyingProjectionForm : Form
{
    /// <summary>The order shows its headings and at least this many drivers before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    private readonly QualifyingProjector _projector;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    private readonly Label _lblHeading = new();
    private readonly Label _lblRule = new();
    private readonly ListEmptyNote _lblEmpty = new();
    private readonly DataGridView _grid = new();
    private readonly CheckBox _chkOnTop = new ThemedCheckBox();

    public QualifyingProjectionForm(QualifyingProjector projector)
    {
        _projector = projector;

        Text = "Qualifying projection";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        // Both sized to what they say, wrapping to the page's width: the
        // heading carries the class and round names, which can be long.
        _lblHeading.AutoSize = true;
        _lblHeading.Font = AppTheme.MicroFont;
        _lblHeading.ForeColor = AppTheme.Primary;

        _lblRule.AutoSize = true;
        _lblRule.Font = AppTheme.SmallFont;
        _lblRule.ForeColor = AppTheme.TextMuted;

        _chkOnTop.AutoSize = true;
        _chkOnTop.Text = "Keep this window on top";
        _chkOnTop.Font = AppTheme.BodyFont;
        _chkOnTop.CheckedChanged += (_, _) => TopMost = _chkOnTop.Checked;

        // On a soft panel where the order goes, with a heading (18 Nov 2026,
        // dialog jazz-up, third batch) - see ListEmptyNote.
        _lblEmpty.Dock = DockStyle.Fill;
        _lblEmpty.Font = AppTheme.BodyFont;
        _lblEmpty.ForeColor = AppTheme.TextMuted;
        _lblEmpty.Heading = "No qualifier is running";
        _lblEmpty.Text = "Start a round marked as a qualifier from the program on Race Day > Next Steps and the order appears here, "
                       + "updating as the laps come in.";

        BuildGrid();

        // The order and the note that nothing is running take turns in one
        // place: both fill the panel they share, and exactly one of them is
        // ever visible (Render) - so there is nothing for the docking order
        // to decide, and the page sees one part whichever is showing.
        var order = new Panel { Tag = AppTheme.KeepBackColor };
        order.Controls.Add(_grid);
        order.Controls.Add(_lblEmpty);

        var page = new FillPage();
        page.Add(_lblHeading, gapBelow: UiLayout.HintGap);
        page.Add(_lblRule, gapBelow: UiLayout.RowGap);
        page.AddFill(order, OrderLeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(_chkOnTop, stretch: false, gapBelow: 0);

        Controls.Add(page);

        _timer.Tick += (_, _) => Render(_projector.Current());

        // Ticking from when the window is shown rather than from when it is
        // built - the same moment in practice, since Race Control shows it
        // as soon as it is made, and the first order is drawn below either
        // way. A window built and never shown (the layout test's) never
        // reaches OnFormClosed, where the timer is stopped, and would have
        // left it ticking on after the window was gone.
        Load += (_, _) => _timer.Start();

        Render(_projector.Current());

        AppTheme.ApplyRecursively(this);

        // Every heading whole, the rest shared by weight - and the place, its
        // change and the heats wide enough for their values: tenth place and
        // on showed as "1..." (layout pictures, 30 Sep). See
        // Pieces.KeepHeadingsWhole.
        Pieces.KeepHeadingsWhole(_grid, ("pos", "99"), ("change", "\u25B299"), ("heats", "9 of 9"));

        DialogFit.Keep(this, page, width: 620, height: 620);

        // A window of its own, shown beside the app rather than over it
        // (Race Control's ToggleProjectionWindow) - often on a second screen
        // - so it can be minimized and is on the taskbar, as it always was.
        // Keep dresses every window as a dialog.
        MinimizeBox = true;
        ShowInTaskbar = true;

        // And it can be made narrower than it opened, down to the 480 it
        // always could, at the text size in use: nothing in it is measured
        // for its width - the headings wrap and the order fills what it has
        // - and a projector can be a smaller screen than the one it opened
        // on. After DialogFit's own Load handler, added above, which sets
        // the least to the width it opened at.
        Load += (_, _) => MinimumSize = new Size(
            Math.Min(MinimumSize.Width, Width - ClientSize.Width + UiLayout.AtTextSize(480)),
            MinimumSize.Height);
    }

    /// <summary>
    /// The least the order's place can do with at <paramref name="width"/>:
    /// the order's headings and a few drivers, or the whole of the note that
    /// nothing is running, wrapped to that width - whichever is taller, so
    /// the window's least does not change when a qualifier starts or ends.
    /// The note is measured as it draws itself, heading and all (ListEmptyNote).
    /// </summary>
    private int OrderLeastHeight(int width) =>
        Math.Max(_grid.ColumnHeadersHeight + LeastRows * _grid.RowTemplate.Height + 2, _lblEmpty.LeastHeight(width));

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "pos", HeaderText = "#", FillWeight = 20 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 110 },
            new DataGridViewTextBoxColumn { Name = "change", HeaderText = "", FillWeight = 30 },
            new DataGridViewTextBoxColumn { Name = "score", HeaderText = "Score", FillWeight = 85 },
            new DataGridViewTextBoxColumn { Name = "heats", HeaderText = "Heats", FillWeight = 45 });

        // The lines still moving get a bar of the app's colour down their
        // left edge as well as their tint (18 Nov 2026, dialog jazz-up), so
        // they stand out from across the pits on a washed-out projector.
        _grid.RowPostPaint += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < _grid.Rows.Count && _grid.Rows[e.RowIndex].Tag is string transponder && _live.Contains(transponder))
            {
                RaceNightTiles.MarkRow(e.Graphics, e.RowBounds, AppTheme.Primary);
            }
        };
    }

    /// <summary>The transponders on track in the order last drawn - the lines RowPostPaint marks.</summary>
    private readonly HashSet<string> _live = new(StringComparer.Ordinal);

    /// <summary>The projection last drawn - the projector hands back the same object until something changes.</summary>
    private QualifyingProjector.Projection? _shown;

    private void Render(QualifyingProjector.Projection? projection)
    {
        // Nothing has changed since the last tick: the grid is left alone
        // rather than rebuilt twice a second, which reset a scroll or a
        // selection in progress and flickered on the projector.
        if (projection is not null && ReferenceEquals(projection, _shown))
        {
            return;
        }

        _shown = projection;

        if (projection is null)
        {
            _grid.Visible = false;
            _lblEmpty.Visible = true;
            _lblHeading.Text = AppTheme.Spaced("QUALIFYING PROJECTION");
            _lblRule.Text = string.Empty;
            return;
        }

        _lblEmpty.Visible = false;
        _grid.Visible = true;

        _lblHeading.Text = AppTheme.Spaced($"{projection.ClassName.ToUpperInvariant()}  -  IF {projection.RoundLabel.ToUpperInvariant()} ENDED NOW");

        var scoring = projection.Method switch
        {
            QualifyingMethod.BestConsecutiveLaps => $"Best {projection.ConsecutiveLapCount} consecutive laps",
            // 11 Nov 2026, Phase 5.1: this heat is ranked with the rest of its round.
            QualifyingMethod.RoundPoints => "Points per round (laps then time across the round's heats), fewest wins",
            _ => "Most laps, then lowest time",
        };

        var counting = projection.Method == QualifyingMethod.RoundPoints
            ? projection.CountBestRounds == 1 ? "best single round" : $"best {projection.CountBestRounds} rounds added together"
            : projection.CountBestRounds == 1
            ? "single best heat"
            : $"best {projection.CountBestRounds} heats added together";

        _lblRule.Text = $"{scoring}  -  {counting}. Drivers on track now are marked.";

        // Rebuilt rather than patched. The order changes on almost every
        // crossing, so matching rows up would be more work than this, and
        // the grid is one class deep.
        var selected = _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as string : null;
        var scroll = _grid.FirstDisplayedScrollingRowIndex;

        _grid.Rows.Clear();
        _live.Clear();

        foreach (var row in projection.Rows)
        {
            var change = row.IsNew
                ? "NEW"
                : row.Change > 0 ? $"▲{row.Change}"
                : row.Change < 0 ? $"▼{-row.Change}"
                : string.Empty;

            var score = projection.Method == QualifyingMethod.RoundPoints
                ? row.PointsText
                : projection.Method == QualifyingMethod.BestConsecutiveLaps
                ? row.BestConsecutiveMs > 0 ? $"{row.BestConsecutiveMs / 1000.0:0.000}s" : "-"
                : $"{Plural.Of(row.Laps, "lap")}  {row.ElapsedMs / 1000.0:0.00}s";

            var heats = row.HeatsCounted >= row.HeatsRun
                ? row.HeatsRun.ToString()
                : $"{row.HeatsCounted} of {row.HeatsRun}";

            var index = _grid.Rows.Add(row.Position, row.DriverName, change, score, heats);
            var line = _grid.Rows[index];

            line.Tag = row.Transponder;

            if (row.InLiveHeat)
            {
                // On track right now: this line is still moving.
                if (row.Transponder is { } live)
                {
                    _live.Add(live);
                }

                line.DefaultCellStyle.BackColor = AppTheme.PrimaryTint;
                line.DefaultCellStyle.ForeColor = AppTheme.TextDark;
                line.DefaultCellStyle.Font = AppTheme.BoldFont;
            }
            else
            {
                line.DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }

            var changeInk = row.IsNew ? AppTheme.Accent
                : row.Change > 0 ? AppTheme.Success
                : row.Change < 0 ? AppTheme.Danger
                : AppTheme.TextMuted;

            // Selected as well: a picked row's cells are drawn in the
            // selection's colour, and the arrow in the picked row came out
            // black - up and down looked alike (layout pictures, 30 Sep).
            line.Cells["change"].Style.ForeColor = changeInk;
            line.Cells["change"].Style.SelectionForeColor = changeInk;
        }

        // Put the view back where it was, so a list longer than the window
        // does not jump to the top four times a second.
        // Guarded (6 Nov 2026): the grid refuses a scroll it has no room to
        // show, and a minimized window has no room at all. The timer runs on
        // regardless, so the refusal reached the crash dialog on a crossing.
        if (scroll >= 0 && scroll < _grid.Rows.Count)
        {
            try
            {
                _grid.FirstDisplayedScrollingRowIndex = scroll;
            }
            catch (InvalidOperationException)
            {
            }
            catch (ArgumentOutOfRangeException)
            {
            }
        }

        if (selected is not null)
        {
            foreach (DataGridViewRow candidate in _grid.Rows)
            {
                if (candidate.Tag as string == selected)
                {
                    candidate.Selected = true;
                    break;
                }
            }
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();

        base.OnFormClosed(e);
    }
}
