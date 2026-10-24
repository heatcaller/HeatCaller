namespace HeatCaller;

/// <summary>
/// Read-only ranked results view, computed by QualifyingCalculator.
///
/// On the layout pieces (DialogFit): the standings taking the height, the
/// rule they are ranked by under them and Close at the foot. It used to be
/// placed in pixels, the rule and Close at fixed heights under a list of a
/// fixed height, so a larger text size ran the rule into Close.
/// </summary>
public sealed class QualifyingStandingsForm : Form
{
    /// <summary>The standings show their headings and at least this many drivers before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    public QualifyingStandingsForm(string className, List<QualifyingCalculator.Entry> entries, RaceClass? raceClass = null)
    {
        Text = $"Qualifying Standings - {className}";
        // Widened when the consecutive-lap columns are present - nine
        // fill-weighted columns in 560px would squeeze them all unreadably.
        var wide = (raceClass?.QualifyingMethod ?? QualifyingMethod.TotalLaps) == QualifyingMethod.BestConsecutiveLaps;
        // Extra width when the Dropped column is present, or eleven
        // fill-weighted columns squeeze each other unreadably.
        var dropping = (raceClass?.CountBestRounds ?? 1) > 1;

        // Declared before the grid is built - the column set depends on all
        // three, so they can't be worked out further down any more.
        var method = raceClass?.QualifyingMethod ?? QualifyingMethod.TotalLaps;
        var lapCount = Math.Max(1, raceClass?.ConsecutiveLapCount ?? 3);
        var countBest = Math.Max(1, raceClass?.CountBestRounds ?? 1);
        Font = AppTheme.BodyFont;

        var page = new FillPage();

        var grid = new DataGridView
        {
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "pos", HeaderText = "Pos", FillWeight = 30 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 110 },
            new DataGridViewTextBoxColumn { Name = "car", HeaderText = "Car", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "laps", HeaderText = "Best Laps", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "fast", HeaderText = "Fastest Lap", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "heats", HeaderText = "Heats", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "source", HeaderText = "Counted Heat", FillWeight = 110 }
        );

        if (countBest > 1)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "dropped",
                HeaderText = "Dropped",
                FillWeight = 110,
            });
        }

        if (method == QualifyingMethod.BestConsecutiveLaps)
        {
            // Inserted before Heats so the scoring figures sit next to the
            // laps they came from rather than at the far right.
            grid.Columns.Insert(4, new DataGridViewTextBoxColumn
            {
                Name = "window",
                HeaderText = countBest > 1 ? $"Best {lapCount} Consec. x{countBest}" : $"Best {lapCount} Consecutive",
                FillWeight = 95,
            });
            grid.Columns.Insert(5, new DataGridViewTextBoxColumn
            {
                Name = "windowavg",
                HeaderText = "Avg / Lap",
                FillWeight = 65,
            });
            grid.Columns.Insert(6, new DataGridViewTextBoxColumn
            {
                Name = "fromlap",
                HeaderText = "From Lap",
                FillWeight = 60,
            });
        }

        page.AddFill(grid, _ => grid.ColumnHeadersHeight + LeastRows * grid.RowTemplate.Height + 2);

        if (entries.Count == 0)
        {
            page.Add(new Label
            {
                Text = "No recorded qualifying heats yet for this class.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            }, gapBelow: UiLayout.RowGap);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var values = new List<object>
            {
                i + 1,
                e.DriverName,
                string.IsNullOrWhiteSpace(e.CarModel) ? "-" : e.CarModel,
                e.BestLaps,
            };

            if (method == QualifyingMethod.BestConsecutiveLaps)
            {
                // A driver who never completed N consecutive laps has no
                // window - shown as a dash rather than a misleading zero.
                values.Add(e.BestConsecutiveMs > 0 ? $"{e.BestConsecutiveMs / 1000.0:0.000}s" : "-");
                values.Add(e.BestConsecutiveMs > 0 ? $"{e.ConsecutiveAverageMs(lapCount) / 1000.0:0.000}s" : "-");
                values.Add(e.ConsecutiveStartLap > 0 ? e.ConsecutiveStartLap.ToString() : "-");
            }

            values.Add(e.BestFastLapMs > 0 ? $"{e.BestFastLapMs / 1000.0:0.000}s" : "-");
            // "2/3" when some heats were dropped, so it's obvious the
            // figures are a total of a subset rather than everything run.
            values.Add(e.HeatsCounted < e.HeatsRun ? $"{e.HeatsCounted}/{e.HeatsRun}" : e.HeatsRun.ToString());
            // Label AND time, because the Lap Times session picker lists
            // sessions by time - a round label alone can't be matched to it.
            var sourceCell = string.IsNullOrWhiteSpace(e.SourceRoundLabel)
                ? "-"
                : e.SourceSessionStartedUtc is { } started
                    ? $"{e.SourceRoundLabel}  ({AppTime.TimeOfDay(started.ToLocalTime())})"
                    : e.SourceRoundLabel;

            values.Add(sourceCell);

            if (countBest > 1)
            {
                values.Add(string.IsNullOrWhiteSpace(e.DroppedSummary) ? "-" : e.DroppedSummary);
            }

            var rowIndex = grid.Rows.Add(values.ToArray());

            // The entry rides on its row, so a double-click still finds the
            // right driver after the grid has been sorted by a column.
            grid.Rows[rowIndex].Tag = e;

            // Flag anyone who couldn't be scored under the chosen rule, so
            // a bottom-of-the-list position doesn't look like a ranking.
            if (method == QualifyingMethod.BestConsecutiveLaps && e.BestConsecutiveMs <= 0)
            {
                grid.Rows[rowIndex].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }
        }

        // The time, lap and heat cells are formatted strings, and a header
        // click sorted them as text - "10.123s" above "9.500s", lap 10
        // above lap 2, "2/3" above "3" - so a slower driver was read out
        // above a quicker one. Those columns now sort on the figures behind
        // them, taken from the entry on each row. A dash (no figure) sorts
        // after every real one. Pos, Best Laps and the text columns already
        // sort correctly and are left to the grid.
        grid.SortCompare += (_, args) =>
        {
            if (grid.Rows[args.RowIndex1].Tag is not QualifyingCalculator.Entry a
                || grid.Rows[args.RowIndex2].Tag is not QualifyingCalculator.Entry b)
            {
                return;
            }

            static long OrLast(long value) => value > 0 ? value : long.MaxValue;

            int? order = args.Column.Name switch
            {
                "window" => OrLast(a.BestConsecutiveMs).CompareTo(OrLast(b.BestConsecutiveMs)),
                "windowavg" => OrLast(a.ConsecutiveAverageMs(lapCount)).CompareTo(OrLast(b.ConsecutiveAverageMs(lapCount))),
                "fromlap" => OrLast(a.ConsecutiveStartLap).CompareTo(OrLast(b.ConsecutiveStartLap)),
                "fast" => OrLast(a.BestFastLapMs).CompareTo(OrLast(b.BestFastLapMs)),
                "heats" => a.HeatsCounted != b.HeatsCounted
                    ? a.HeatsCounted.CompareTo(b.HeatsCounted)
                    : a.HeatsRun.CompareTo(b.HeatsRun),
                _ => (int?)null,
            };

            if (order is not { } result)
            {
                return;
            }

            // Ties fall back to the computed standing, so equal figures keep
            // their ranked order rather than shuffling.
            args.SortResult = result != 0 ? result : entries.IndexOf(a).CompareTo(entries.IndexOf(b));
            args.Handled = true;
        };

        // WHY (23 Oct 2026, bug hunt 10): the consecutive Window and Avg / Lap
        // figures include penalties but nothing said so; the rule now does.
        var lblRule = new Label
        {
            Text = countBest > 1
                ? (method == QualifyingMethod.BestConsecutiveLaps
                    ? $"Each heat is scored on its quickest unbroken run of {Plural.Of(lapCount, "lap")}, penalties included. The best {countBest} heats "
                      + "are totaled and the rest dropped - \"Heats\" shows counted/run. Double-click a row for detail."
                    : $"Each heat is scored on laps then time. The best {countBest} heats are totaled and the rest "
                      + "dropped - \"Heats\" shows counted/run. Double-click a row for detail.")
                : method == QualifyingMethod.BestConsecutiveLaps
                ? $"Ranked on each driver's quickest unbroken run of {Plural.Of(lapCount, "lap")}, penalties included. A driver's BEST heat counts - "
                  + "double-click a row to see exactly which session to open in Lap Times."
                : "Ranked on most laps, then lowest time. A driver's BEST heat counts - \"Counted Heat\" shows which one.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };
        page.Add(lblRule);

        grid.CellDoubleClick += (_, args) =>
        {
            // Read from the row, not from the list by position: sorting the
            // grid reorders the rows but not the list, so row 3 of a sorted
            // grid was somebody else's entry and the detail named the wrong
            // driver.
            if (args.RowIndex < 0 || args.RowIndex >= grid.Rows.Count
                || grid.Rows[args.RowIndex].Tag is not QualifyingCalculator.Entry entry)
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(entry.SourceRoundLabel))
            {
                AppDialog.Show(this,
                    $"{entry.DriverName}'s figures aren't tied to a recorded heat.",
                    "No source heat", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var when = entry.SourceSessionStartedUtc is { } started
                ? AppTime.DateAndTime(started.ToLocalTime())
                : "(unknown time)";

            if (entry.HeatsCounted > 1)
            {
                AppDialog.Show(this,
                    $"{entry.DriverName}'s figures are the total of their best {entry.HeatsCounted} heats:\r\n\r\n"
                    + $"   {entry.SourceRoundLabel}\r\n\r\n"
                    + (string.IsNullOrWhiteSpace(entry.DroppedSummary)
                        ? "No heats were dropped."
                        : $"Dropped:\r\n   {entry.DroppedSummary}")
                    + "\r\n\r\nOpen each counted session in Lap Times to check the figures individually.",
                    "Where these numbers came from", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // WHY (23 Oct 2026, bug hunt 10): since 20 Oct the consecutive
            // figure has the driver's race, time and lap penalties added in,
            // but this said the laps alone "should total" it - so for a
            // penalized driver the Lap Times figures could never match and
            // the director was told correct maths was broken. The wording
            // now comes from ConsecutiveDetail, which says so. The penalty
            // share now comes from the calculator (Entry.ConsecutivePenaltyMs),
            // so the laps and the penalties are itemized separately.
            var detail = method == QualifyingMethod.BestConsecutiveLaps && entry.BestConsecutiveMs > 0
                ? ConsecutiveDetail(entry.ConsecutiveStartLap, lapCount, entry.BestConsecutiveMs, penaltyMs: entry.ConsecutivePenaltyMs)
                : string.Empty;

            AppDialog.Show(this,
                $"{entry.DriverName}'s figures come from:\r\n\r\n"
                + $"   {entry.SourceRoundLabel}\r\n   {when}\r\n\r\n"
                + $"Open Lap Times and pick that session, then tick {entry.DriverName}." + detail,
                "Where these numbers came from", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);
        Controls.Add(page);

        CancelButton = btnClose;

        BackColor = AppTheme.Background;
        AppTheme.ApplyRecursively(this);

        // Every heading whole, the rest shared by weight - see Pieces.KeepHeadingsWhole.
        Pieces.KeepHeadingsWhole(grid);

        DialogFit.Keep(this, page, width: (wide ? 980 : 660) + (dropping ? 160 : 0), height: 448);
    }

    /// <summary>
    /// The "check laps A to B" line of the double-click detail for a single
    /// counted heat under the consecutive rule. totalMs is the ranked figure,
    /// which includes any penalty. penaltyMs is the penalty share of it when
    /// known: above zero it is itemized, zero means none, and null means the
    /// caller can't tell, so the text says a penalty may be inside the figure.
    /// </summary>
    internal static string ConsecutiveDetail(int startLap, int lapCount, long totalMs, long? penaltyMs)
    {
        var range = $"\r\n\r\nCheck laps {startLap} to {startLap + Math.Max(1, lapCount) - 1} - ";

        if (penaltyMs is not { } penalty)
        {
            return range + $"together with any penalty race control gave, they make {totalMs / 1000.0:0.000}s. "
                + "A penalty is added to this figure but never to the lap times, so with one the laps alone come to less.";
        }

        if (penalty <= 0)
        {
            return range + $"they should total {totalMs / 1000.0:0.000}s.";
        }

        return range + $"they should total {(totalMs - penalty) / 1000.0:0.000}s, plus {penalty / 1000.0:0.000}s "
            + $"in penalties = {totalMs / 1000.0:0.000}s.";
    }
}
