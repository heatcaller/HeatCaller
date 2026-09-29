namespace HeatCaller;

/// <summary>
/// Shows who marshals which corner for each round of a class.
///
/// Uncovered posts are highlighted rather than left blank: an uncovered
/// corner is the single thing on this sheet that needs acting on before
/// racing starts, so it has to be impossible to skim past.
///
/// On the layout pieces (DialogFit): the posts taking the height, the
/// summary under them and the buttons at the foot. It used to be placed in
/// pixels in a 700x520 window - the buttons at a fixed height the summary
/// ran into at a larger text size.
/// </summary>
public sealed class MarshalAssignmentsForm : Form
{
    /// <summary>The list shows its headings and at least this many rows before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    public MarshalAssignmentsForm(string className, IReadOnlyList<MarshalAssigner.Assignment> assignments)
    {
        Text = $"Corner Marshals - {className}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var page = new FillPage();

        // Only when there is something to list; with nothing, a note says
        // what to set up instead.
        DataGridView? grid = null;

        if (assignments.Count == 0)
        {
            page.Add(new Label
            {
                Text = "No marshal posts are set for this track layout.\r\n\r\n"
                     + "Open Track Layouts, pick the layout, and set how many corner posts it has.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            });
        }
        else
        {
            grid = new DataGridView
            {
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };

            grid.Columns.AddRange(
                new DataGridViewTextBoxColumn { Name = "round", HeaderText = "Round", FillWeight = 60 },
                new DataGridViewTextBoxColumn { Name = "by", HeaderText = "Marshalled By", FillWeight = 70 },
                new DataGridViewTextBoxColumn { Name = "post", HeaderText = "Post", FillWeight = 60 },
                new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Marshal", FillWeight = 90 }
            );

            foreach (var assignment in assignments)
            {
                for (var i = 0; i < assignment.Posts.Count; i++)
                {
                    var post = assignment.Posts[i];

                    // Round and source shown once per block, so the eye
                    // groups the posts that belong together.
                    var rowIndex = grid.Rows.Add(
                        i == 0 ? assignment.Round.RoundLabel : string.Empty,
                        i == 0 ? assignment.MarshalledBy?.RoundLabel ?? "(not assigned)" : string.Empty,
                        post.Name,
                        post.IsFilled ? post.DriverName : "- UNCOVERED -");

                    if (!post.IsFilled)
                    {
                        grid.Rows[rowIndex].Cells[3].Style.ForeColor = AppTheme.Danger;
                        grid.Rows[rowIndex].Cells[3].Style.Font = AppTheme.BoldFont;
                    }

                    if (i == 0)
                    {
                        grid.Rows[rowIndex].Cells[0].Style.Font = AppTheme.BoldFont;
                    }
                }
            }

            var list = grid;
            page.AddFill(list, _ => list.ColumnHeadersHeight + LeastRows * list.RowTemplate.Height + 2);
        }

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        // Not under the note that no posts are set: "Every post is covered"
        // said the opposite of it.
        if (assignments.Count > 0)
        {
            var problems = assignments.Count(a => a.Problem is not null);
            var lblSummary = new Label
            {
                Text = problems == 0
                    ? "Every post is covered."
                    : $"{Plural.Of(problems, "round")} {(problems == 1 ? "needs" : "need")} attention - see the highlighted rows.",
                AutoSize = true,
                ForeColor = problems == 0 ? AppTheme.TextMuted : AppTheme.Danger,
            };

            page.Add(lblSummary);
        }

        // Against the right-hand edge, Close furthest right, as a dialog's
        // buttons go - and Copy as Text beside it only when there is a
        // roster to copy: with no posts set it copied a heading and nothing
        // under it.
        var buttons = page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        if (assignments.Count > 0)
        {
            var btnCopy = new Button { Text = "Copy as Text" };
            btnCopy.Click += (_, _) =>
            {
                // Plain text so it can go straight into a group chat or be
                // printed - far more useful on a race day than a screenshot.
                // The clipboard can be held open by another program for a
                // moment; that is a "try again", not an error dialog.
                try
                {
                    Clipboard.SetText(MarshalAssigner.ToText(className, assignments));
                    btnCopy.Text = "Copied";
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    btnCopy.Text = "Try Again";
                }
            };

            buttons.With(btnCopy);
        }

        Controls.Add(page);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // Every heading whole, the rest shared by weight - see Pieces.KeepHeadingsWhole.
        if (grid is not null)
        {
            Pieces.KeepHeadingsWhole(grid);
        }

        // With nothing to list, as tall as the note that says so.
        DialogFit.Keep(this, page, width: 700, height: assignments.Count == 0 ? null : 520);
    }
}
