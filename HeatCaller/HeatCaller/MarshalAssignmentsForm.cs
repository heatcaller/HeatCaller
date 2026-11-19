namespace HeatCaller;

/// <summary>
/// Shows who marshals which corner for each round of a class.
///
/// Uncovered posts are highlighted rather than left blank: an uncovered
/// corner is the single thing on this sheet that needs acting on before
/// racing starts, so it has to be impossible to skim past.
///
/// On the layout pieces (DialogFit): tiles for the rounds, the posts
/// covered and those uncovered across the top (18 Nov 2026, dialog
/// jazz-up), the posts taking the height, the summary under them and the
/// buttons at the foot, Copy as Text in the app's colour. With no posts
/// set, a card saying how to set them up. It used to be placed in
/// pixels in a 700x520 window - the buttons at a fixed height the summary
/// ran into at a larger text size.
/// </summary>
public sealed class MarshalAssignmentsForm : Form
{
    /// <summary>The list shows its headings and at least this many rows before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    /// <param name="swap">
    /// Swap... (11 Nov 2026, Phase 4): asks who covers the picked post and
    /// records it, true when it did - the window then closes with Retry so
    /// its opener shows the roster again. Null: no Swap button.
    /// </param>
    public MarshalAssignmentsForm(string className, IReadOnlyList<MarshalAssigner.Assignment> assignments,
        Func<MarshalAssigner.Assignment, MarshalAssigner.Post, bool>? swap = null)
    {
        Text = $"Corner Marshals - {className}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var page = new FillPage();

        // Only when there is something to list; with nothing, a note says
        // what to set up instead.
        DataGridView? grid = null;

        // Copy as Text, made below when there is a roster to copy.
        Button? copyButton = null;

        if (assignments.Count == 0)
        {
            // THE LOOK (18 Nov 2026, dialog jazz-up): what fills the sheet,
            // in a card of its own, with how to set it up as its hint.
            var noneCard = new Section("No marshal posts yet");
            noneCard.Add(new Label
            {
                Text = "No marshal posts are set for this track layout.",
                AutoSize = true,
            }, gapBelow: UiLayout.HintGap);
            noneCard.Add(new Label
            {
                Text = "Open Track Layouts, pick the layout, and set how many corner posts it has. Who marshals each round shows here.",
                AutoSize = true,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
            }, gapBelow: 0);

            page.Add(noneCard);
        }
        else
        {
            // THE LOOK (18 Nov 2026, dialog jazz-up): the sheet at a glance
            // in tiles across the top - the rounds, the posts covered, and
            // the posts nobody is on, red while there are any - over the
            // list. The window is taller by the tiles, so the list keeps its
            // height. The dialog is built at the text size in use, so the
            // tiles take their faces here.
            var tiles = new[] { new StatTile(), new StatTile(), new StatTile() };

            foreach (var tile in tiles)
            {
                tile.Value.Font = AppTheme.TitleFont;
                tile.Caption.Font = AppTheme.MicroFont;
            }

            var culture = System.Globalization.CultureInfo.CurrentCulture;
            var posts = assignments.SelectMany(a => a.Posts).ToList();
            var uncovered = posts.Count(p => !p.IsFilled);

            tiles[0].Set(assignments.Count.ToString(culture), assignments.Count == 1 ? "Round" : "Rounds");
            tiles[1].Set($"{posts.Count - uncovered} of {posts.Count}", "Posts covered");
            tiles[2].Set(uncovered.ToString(culture), "Uncovered");

            if (uncovered > 0)
            {
                tiles[2].Value.ForeColor = AppTheme.Danger;
            }

            page.Add(new TileStrip(tiles), gapBelow: UiLayout.RowGap);

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
                new DataGridViewTextBoxColumn { Name = "by", HeaderText = "Marshaled By", FillWeight = 70 },
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
                        post.IsFilled ? post.DriverName ?? string.Empty : "- UNCOVERED -");

                    grid.Rows[rowIndex].Tag = (assignment, post);

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
            copyButton = btnCopy;
        }

        if (swap is not null && grid is not null)
        {
            var btnSwap = new Button { Text = "Swap...", Enabled = false };
            var list = grid;

            (MarshalAssigner.Assignment, MarshalAssigner.Post)? Picked() =>
                list.SelectedRows.Count > 0 && list.SelectedRows[0].Tag is ValueTuple<MarshalAssigner.Assignment, MarshalAssigner.Post> tag ? tag : null;

            list.SelectionChanged += (_, _) => btnSwap.Enabled = Picked() is { } p && p.Item2.IsFilled && p.Item1.MarshalledBy is not null;
            btnSwap.Click += (_, _) =>
            {
                if (Picked() is { } p && swap(p.Item1, p.Item2))
                {
                    DialogResult = DialogResult.Retry;
                    Close();
                }
            };

            buttons.With(btnSwap);
        }

        Controls.Add(page);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // Copy as Text in the app's colour: sending the sheet round is what
        // it is opened for. After the theme pass, which does not know it.
        if (copyButton is not null)
        {
            AppTheme.StyleButton(copyButton, AppTheme.ButtonKind.Primary);
        }

        // Every heading whole, the rest shared by weight - see Pieces.KeepHeadingsWhole.
        if (grid is not null)
        {
            Pieces.KeepHeadingsWhole(grid);
        }

        // With nothing to list, as tall as the card that says so; with a
        // list, the 520 it had and taller by the tiles (DialogHeight).
        DialogFit.Keep(this, page, width: 700, height: assignments.Count == 0 ? null : DialogHeight);
    }

    /// <summary>How tall the window is inside its frame at the normal text size, with a list: the 520 it had, and room for the tiles.</summary>
    private const int DialogHeight = 520 + 70;
}
