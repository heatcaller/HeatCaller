namespace HeatCaller;

/// <summary>
/// Shows who marshals which corner for each round of a class.
///
/// Uncovered posts are highlighted rather than left blank: an uncovered
/// corner is the single thing on this sheet that needs acting on before
/// racing starts, so it has to be impossible to skim past.
/// </summary>
public sealed class MarshalAssignmentsForm : Form
{
    public MarshalAssignmentsForm(string className, IReadOnlyList<MarshalAssigner.Assignment> assignments)
    {
        Text = $"Corner Marshals - {className}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 520);
        MinimumSize = new Size(560, 400);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var grid = new DataGridView
        {
            Location = new Point(12, 12),
            Size = new Size(676, 420),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
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

        if (assignments.Count == 0)
        {
            var lblEmpty = new Label
            {
                Text = "No marshal posts are set for this track layout.\r\n\r\n"
                     + "Open Track Layouts, pick the layout, and set how many corner posts it has.",
                Location = new Point(16, 16),
                AutoSize = true,
                MaximumSize = new Size(660, 0),
                ForeColor = AppTheme.TextMuted,
            };
            Controls.Add(lblEmpty);
        }
        else
        {
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

            Controls.Add(grid);
        }

        var problems = assignments.Count(a => a.Problem is not null);
        var lblSummary = new Label
        {
            Text = problems == 0
                ? "Every post is covered."
                : $"{Plural.Of(problems, "round")} {(problems == 1 ? "needs" : "need")} attention - see the highlighted rows.",
            Location = new Point(12, 444),
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            ForeColor = problems == 0 ? AppTheme.TextMuted : AppTheme.Danger,
        };

        var btnCopy = new Button { Text = "Copy as Text", Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
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

        var btnClose = new Button { Text = "Close", Anchor = AnchorStyles.Bottom | AnchorStyles.Left, DialogResult = DialogResult.OK };

        AppTheme.LayoutButtonRow(ClientSize.Width - 240, 470, 8, btnCopy, btnClose);

        Controls.Add(lblSummary);
        Controls.Add(btnCopy);
        Controls.Add(btnClose);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);
    }
}
