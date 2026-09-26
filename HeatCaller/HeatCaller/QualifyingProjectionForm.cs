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
/// </summary>
public sealed class QualifyingProjectionForm : Form
{
    private readonly QualifyingProjector _projector;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    private readonly Label _lblHeading = new();
    private readonly Label _lblRule = new();
    private readonly Label _lblEmpty = new();
    private readonly DataGridView _grid = new();
    private readonly CheckBox _chkOnTop = new();

    public QualifyingProjectionForm(QualifyingProjector projector)
    {
        _projector = projector;

        Text = "Qualifying projection";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 620);
        MinimumSize = new Size(480, 400);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        Padding = new Padding(UiLayout.PageLeft);

        // The two heading lines live in one docked strip, positioned
        // inside it rather than docked themselves. Two Top controls in a
        // row work only if you know which way WinForms resolves the
        // z-order, and a layout that depends on knowing that is a layout
        // that breaks the first time somebody reorders an Add.
        var headingHeight = AppTheme.MicroFont.Height + 8;
        var ruleHeight = AppTheme.SmallFont.Height + 10;

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = headingHeight + ruleHeight,
            BackColor = AppTheme.Background,
        };

        _lblHeading.Dock = DockStyle.Top;
        _lblHeading.Height = headingHeight;
        _lblHeading.Font = AppTheme.MicroFont;
        _lblHeading.ForeColor = AppTheme.Primary;
        _lblHeading.TextAlign = ContentAlignment.MiddleLeft;

        _lblRule.Dock = DockStyle.Bottom;
        _lblRule.Height = ruleHeight;
        _lblRule.Font = AppTheme.SmallFont;
        _lblRule.ForeColor = AppTheme.TextMuted;
        _lblRule.TextAlign = ContentAlignment.MiddleLeft;

        header.Controls.Add(_lblHeading);
        header.Controls.Add(_lblRule);

        _chkOnTop.Dock = DockStyle.Bottom;
        _chkOnTop.Height = AppTheme.BodyFont.Height + 12;
        _chkOnTop.Text = "Keep this window on top";
        _chkOnTop.Font = AppTheme.BodyFont;
        _chkOnTop.CheckedChanged += (_, _) => TopMost = _chkOnTop.Checked;

        _lblEmpty.Dock = DockStyle.Fill;
        _lblEmpty.Font = AppTheme.BodyFont;
        _lblEmpty.ForeColor = AppTheme.TextMuted;
        _lblEmpty.TextAlign = ContentAlignment.MiddleCenter;
        _lblEmpty.Text = "No qualifier is running.\r\n\r\n"
                       + "Start a round marked as a qualifier from the Program tab and the order appears here, "
                       + "updating as the laps come in.";

        BuildGrid();

        // One Top, one Bottom, and two Fills of which exactly one is ever
        // visible - so there is nothing for the docking order to decide.
        Controls.Add(_grid);
        Controls.Add(_lblEmpty);
        Controls.Add(header);
        Controls.Add(_chkOnTop);

        _timer.Tick += (_, _) => Render(_projector.Current());
        _timer.Start();

        Render(_projector.Current());

        AppTheme.ApplyRecursively(this);
    }

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
    }

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

        var scoring = projection.Method == QualifyingMethod.BestConsecutiveLaps
            ? $"Best {projection.ConsecutiveLapCount} consecutive laps"
            : "Most laps, then lowest time";

        var counting = projection.CountBestRounds == 1
            ? "single best heat"
            : $"best {projection.CountBestRounds} heats added together";

        _lblRule.Text = $"{scoring}  -  {counting}. Drivers on track now are marked.";

        // Rebuilt rather than patched. The order changes on almost every
        // crossing, so matching rows up would be more work than this, and
        // the grid is one class deep.
        var selected = _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Tag as string : null;
        var scroll = _grid.FirstDisplayedScrollingRowIndex;

        _grid.Rows.Clear();

        foreach (var row in projection.Rows)
        {
            var change = row.IsNew
                ? "NEW"
                : row.Change > 0 ? $"▲{row.Change}"
                : row.Change < 0 ? $"▼{-row.Change}"
                : string.Empty;

            var score = projection.Method == QualifyingMethod.BestConsecutiveLaps
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
                line.DefaultCellStyle.BackColor = AppTheme.PrimaryTint;
                line.DefaultCellStyle.ForeColor = AppTheme.TextDark;
                line.DefaultCellStyle.Font = AppTheme.BoldFont;
            }
            else
            {
                line.DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }

            line.Cells["change"].Style.ForeColor = row.IsNew ? AppTheme.Accent
                : row.Change > 0 ? AppTheme.Success
                : row.Change < 0 ? AppTheme.Danger
                : AppTheme.TextMuted;
        }

        // Put the view back where it was, so a list longer than the window
        // does not jump to the top four times a second.
        if (scroll >= 0 && scroll < _grid.Rows.Count)
        {
            _grid.FirstDisplayedScrollingRowIndex = scroll;
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
