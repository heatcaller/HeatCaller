namespace HeatCaller.Tests;

/// <summary>
/// The layout pieces (LayoutPieces.cs) on a page of their own, measured by
/// the same check the layout test runs over the real pages.
///
/// A page rebuilt on these has to come through that check with nothing to
/// say at every size, so the pieces had better: here they are at two text
/// sizes on a wide page and two on a narrow one, where the captions grow,
/// the hint wraps and the buttons run onto a second row. And the one piece
/// that shares out height - FillPage - is held to its promise both ways:
/// the list takes the room that is left, and a window too short for the
/// least the list can do with scrolls instead of squashing it. So is the
/// same promise made through a Beside, the way Race Control is built: a
/// Session row handed over afterwards, a column down the side, and a card
/// with a list that fills it.
/// </summary>
public sealed class LayoutPiecesTests
{
    private static volatile string _now = "starting";

    [Fact]
    public void APageBuiltFromThePiecesFitsAtEverySize()
    {
        var sizes = new (float Scale, Size Page, bool Narrow)[]
        {
            (1.0f, new Size(1100, 640), false),
            (2.0f, new Size(1100, 640), false),
            (1.0f, new Size(560, 480), true),
            (1.5f, new Size(560, 480), true),
        };

        var problems = StaThread.Run(
            () => sizes.SelectMany(size => CheckStack(size.Scale, size.Page, size.Narrow)).ToList(),
            TimeSpan.FromMinutes(2),
            () => _now);

        Assert.True(problems.Count == 0, "The pieces do not fit:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void TheFillingPartTakesWhatIsLeftAndScrollsRatherThanSquash()
    {
        var problems = StaThread.Run(CheckFill, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, "FillPage did not share out the height:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void AColumnBesideTheListFitsAndThePageScrollsRatherThanSquashIt()
    {
        var problems = StaThread.Run(CheckBeside, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, "Beside did not share out the page:\n" + string.Join("\n", problems));
    }

    private static List<string> CheckStack(float scale, Size size, bool narrow)
    {
        _now = $"a stack at text {scale * 100:0}%, {size.Width}x{size.Height}";

        var problems = new List<string>();

        void Threw(object? sender, ThreadExceptionEventArgs e) =>
            problems.Add($"{_now}: {e.Exception.GetType().Name}: {e.Exception.Message}");

        Application.ThreadException += Threw;
        AppTheme.SetFontScale(scale);

        try
        {
            using var window = new QuietWindow();
            var page = new Panel { Bounds = new Rectangle(Point.Empty, size) };

            window.Controls.Add(page);

            var stack = new PageStack();
            page.Controls.Add(stack);

            var format = stack.Add(new Section("Race format"));
            var grid = format.Add(new FieldGrid());

            grid.AddRow("Time limit (seconds):", new NumericUpDown { Maximum = 3600, Value = 300 });

            var hint = grid.AddHint(
                "How long the race runs from the green. It ends at the leader's first crossing after this, "
                + "so the leader always finishes the lap they are on.");

            grid.AddRow("Laps:", new NumericUpDown { Maximum = 999, Value = 12 });

            var track = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            track.Items.AddRange(new object[] { "Club circuit", "Reverse", "Short loop" });
            track.SelectedIndex = 0;

            grid.AddRow("Track layout:", track, stretch: true);
            grid.AddRow("Session name:", FieldGrid.Inline(new TextBox { Width = 220 }, new Button { Text = "Use Heat Name" }));
            grid.AddWide(new CheckBox { Text = "Announce lap times", AutoSize = true });

            var actions = stack.Add(new Section("Actions"));
            var bar = actions.Add(new ButtonBar());

            var buttons = new[]
            {
                new Button { Text = "Stage" },
                new Button { Text = "Start Countdown" },
                new Button { Text = "Green (Start)" },
                new Button { Text = "Yellow" },
                new Button { Text = "Red Flag" },
                new Button { Text = "Checkered Flag" },
                new Button { Text = "Reset Race..." },
            };

            bar.With(buttons);

            // As a real page gets it, once built.
            AppTheme.ApplyRecursively(page);

            window.Show();
            StaThread.Settle();

            foreach (var finding in LayoutAudit.Check(page, "Pieces", StaThread.Settle))
            {
                problems.Add($"{_now}: [{finding.Kind}] {finding.Detail}");
            }

            if (narrow)
            {
                // The things that only happen when room is short - which is
                // the point of the pieces - checked to have happened, not
                // just to have done no harm.
                if (hint.Height < 2 * hint.Font.Height)
                {
                    problems.Add($"{_now}: the hint is {hint.Height}px tall - it should have wrapped onto two lines or more");
                }

                if (buttons.Select(button => button.Top).Distinct().Count() < 2)
                {
                    problems.Add($"{_now}: the seven buttons are on one row - they should have wrapped");
                }
            }

            if (buttons.Any(button => button.Text.EndsWith("...", StringComparison.Ordinal)))
            {
                problems.Add($"{_now}: a caption kept its \"...\" - the button was measured with it on");
            }
        }
        finally
        {
            Application.ThreadException -= Threw;
            AppTheme.SetFontScale(1f);
        }

        return problems;
    }

    private static List<string> CheckFill()
    {
        _now = "a fill page";

        const int least = 240;
        var problems = new List<string>();

        void Threw(object? sender, ThreadExceptionEventArgs e) =>
            problems.Add($"{_now}: {e.Exception.GetType().Name}: {e.Exception.Message}");

        Application.ThreadException += Threw;

        try
        {
            using var window = new QuietWindow();
            var page = new Panel { Bounds = new Rectangle(0, 0, 900, 700) };

            window.Controls.Add(page);

            var fill = new FillPage();
            page.Controls.Add(fill);

            var next = fill.Add(new Section("Next up"));
            next.Add(new Label { Text = "Heat 3 of 12 - Stock Buggy", AutoSize = true });

            var list = fill.AddFill(new DataGridView { ColumnCount = 3 }, least);

            // Enough rows to scroll, so the grid gets a themed scrollbar.
            for (var row = 1; row <= 40; row++)
            {
                list.Rows.Add($"Driver {row}", "12", "18.4");
            }

            fill.Add(new ButtonBar().With(new Button { Text = "Export" }, new Button { Text = "Print" }), gapBelow: 0);

            AppTheme.ApplyRecursively(page);

            window.Show();
            StaThread.Settle();

            _now = "a fill page, 900x700";

            // A themed scrollbar is parked beside what it scrolls - in the
            // panel FillPage holds the list in. In the table above that it
            // would become one more row of the table instead.
            if (list.Parent?.Parent?.Controls.OfType<ThinScrollBar>().Any() == true)
            {
                problems.Add($"{_now}: the list's themed scrollbar was put in the table, where it becomes a row");
            }

            if (fill.VerticalScroll.Visible)
            {
                problems.Add($"{_now}: the page shows a scrollbar - everything fits");
            }

            // Laid out again with nothing changed, nothing should move. A
            // scrolling range that fed on itself grew by a margin each time.
            var settled = list.Height;
            fill.PerformLayout();
            fill.PerformLayout();

            if (list.Height != settled)
            {
                problems.Add($"{_now}: the list went from {settled}px to {list.Height}px when the page was laid out again with nothing changed");
            }

            if (list.Height <= least)
            {
                problems.Add($"{_now}: the list is {list.Height}px - it should take the room left over, well over {least}px");
            }

            if (fill.AutoScrollMinSize.Height > fill.ClientSize.Height)
            {
                problems.Add($"{_now}: the page scrolls ({fill.AutoScrollMinSize.Height}px of content in {fill.ClientSize.Height}px) - it should fit");
            }

            foreach (var finding in LayoutAudit.Check(page, "Fill", StaThread.Settle))
            {
                problems.Add($"{_now}: [{finding.Kind}] {finding.Detail}");
            }

            page.Height = 360;
            StaThread.Settle();

            _now = "a fill page, 900x360";

            if (list.Height < least)
            {
                problems.Add($"{_now}: the list was squashed to {list.Height}px - the least it should have is {least}px");
            }

            if (fill.AutoScrollMinSize.Height <= fill.ClientSize.Height)
            {
                problems.Add($"{_now}: the page does not scroll ({fill.AutoScrollMinSize.Height}px of content in {fill.ClientSize.Height}px)");
            }

            if (fill.DisplayRectangle.Width > fill.ClientSize.Width - fill.Padding.Horizontal)
            {
                problems.Add($"{_now}: the page scrolls sideways ({fill.DisplayRectangle.Width}px across in {fill.ClientSize.Width - fill.Padding.Horizontal}px)");
            }

            foreach (var finding in LayoutAudit.Check(page, "Fill", StaThread.Settle))
            {
                problems.Add($"{_now}: [{finding.Kind}] {finding.Detail}");
            }
        }
        finally
        {
            Application.ThreadException -= Threw;
        }

        return problems;
    }

    private static List<string> CheckBeside()
    {
        _now = "a page with a column beside it";

        var problems = new List<string>();

        void Threw(object? sender, ThreadExceptionEventArgs e) =>
            problems.Add($"{_now}: {e.Exception.GetType().Name}: {e.Exception.Message}");

        Application.ThreadException += Threw;

        try
        {
            using var window = new QuietWindow();
            var page = new Panel { Bounds = new Rectangle(0, 0, 1000, 760) };

            window.Controls.Add(page);

            var fill = new FillPage();
            page.Controls.Add(fill);

            // A part that does not size itself, as the Next Up bar does not.
            var bar = fill.Add(new Panel { Height = 52 }, gapBelow: UiLayout.RowGap);

            var body = new Beside(sideLeastWidth: 260);

            var flags = body.Add(new Section("Flags"));

            var flagButtons = new[] { "Stage", "Countdown", "Green (Start)", "Yellow", "Red", "Checkered", "Reset..." }
                .Select(caption => new Button { Text = caption })
                .ToArray();

            flags.Add(new ButtonBar(tight: true).With(flagButtons));
            flags.Add(new Label { Text = "Format (app default):  open-ended - all away together - auto 15+5s", AutoSize = true });

            var board = new Section("Board");
            var grid = new DataGridView { ColumnCount = 3 };

            for (var row = 1; row <= 40; row++)
            {
                grid.Rows.Add($"Driver {row}", "12", "18.4");
            }

            int Least() => grid.ColumnHeadersHeight + 3 * grid.RowTemplate.Height;

            board.AddFill(grid, Least);

            var tools = new[] { "+1 Lap", "Clear Penalties", "Columns..." }
                .Select(caption => new Button { Text = caption })
                .ToArray();

            board.Add(new ButtonBar(sameWidth: true).With(tools), gapBelow: 0);
            body.AddFill(board, board.LeastHeight);

            var clock = body.AddSide(new Panel { Height = 200 });

            var sideButtons = new[] { "Quick Race", "Open Big Screen", "Open Announcer Screen" }
                .Select(caption => body.AddSide(new Button { Text = caption }))
                .ToArray();

            var tiles = body.AddSide(new Panel { Height = 50 });

            fill.AddFill(body, body.LeastHeight, gapBelow: 0);

            // Handed over after the page is built, as MainForm hands Race
            // Control its Session row - and it has to end up on top.
            var session = new Section("Session");
            var sessionRow = session.Add(new FieldRow());

            var nameCaption = new Label { Text = "Name:" };
            var name = new TextBox { Width = 170 };
            var trackCaption = new Label { Text = "Track:" };
            var round = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };

            round.Items.Add("(freeform - no race program round)");
            round.SelectedIndex = 0;

            sessionRow.Add(nameCaption, name);
            sessionRow.Add(trackCaption, new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList });
            sessionRow.AddLast(
                new Label { Text = "Round:" },
                round,
                () => TextRenderer.MeasureText("(freeform - no race program round)", round.Font).Width
                      + SystemInformation.VerticalScrollBarWidth + 24);

            fill.AddFirst(session, gapBelow: UiLayout.RowGap);

            AppTheme.ApplyRecursively(page);

            window.Show();
            StaThread.Settle();

            _now = "a page with a column beside it, 1000x760";

            if (session.Bottom > bar.Top || bar.Bottom > body.Parent!.Top)
            {
                problems.Add($"{_now}: the parts are out of order - the Session row, handed over last, belongs on top "
                    + $"(the Session row at {session.Top}, the bar at {bar.Top}, the rest at {body.Parent!.Top})");
            }

            if (fill.AutoScrollMinSize.Height > fill.ClientSize.Height)
            {
                problems.Add($"{_now}: the page scrolls ({fill.AutoScrollMinSize.Height}px of content in {fill.ClientSize.Height}px) - it should fit");
            }

            if (grid.Height <= Least())
            {
                problems.Add($"{_now}: the list is {grid.Height}px - it should take the room left over, well over {Least()}px");
            }

            CheckColumn();
            CheckSnug();
            Audit();

            // Too short for the column, and for the least the list can do
            // with under the flags.
            page.Height = 420;
            StaThread.Settle();

            _now = "a page with a column beside it, 1000x420";

            if (fill.AutoScrollMinSize.Height <= fill.ClientSize.Height)
            {
                problems.Add($"{_now}: the page does not scroll ({fill.AutoScrollMinSize.Height}px of content in {fill.ClientSize.Height}px)");
            }

            if (grid.Height < Least())
            {
                problems.Add($"{_now}: the list was squashed to {grid.Height}px - the least it should have is {Least()}px");
            }

            if (tiles.Bottom > body.ClientSize.Height)
            {
                problems.Add($"{_now}: the column runs {tiles.Bottom - body.ClientSize.Height}px off the bottom of the part it is in");
            }

            if (fill.DisplayRectangle.Width > fill.ClientSize.Width - fill.Padding.Horizontal)
            {
                problems.Add($"{_now}: the page scrolls sideways ({fill.DisplayRectangle.Width}px across in {fill.ClientSize.Width - fill.Padding.Horizontal}px)");
            }

            CheckColumn();
            CheckSnug();
            Audit();

            // Narrow: the round picker goes onto a line of its own rather
            // than being squeezed beside the track, and the flags wrap.
            page.Bounds = new Rectangle(0, 0, 700, 760);
            StaThread.Settle();

            _now = "a page with a column beside it, 700x760";

            if (round.Top < name.Bottom)
            {
                problems.Add($"{_now}: the round picker is on the Name box's line, {round.Width}px wide - it should have gone onto a line of its own");
            }

            if (flagButtons.Select(button => button.Top).Distinct().Count() < 2)
            {
                problems.Add($"{_now}: the seven flags are on one row - they should have wrapped");
            }

            CheckColumn();
            CheckSnug();
            Audit();

            // A pair hidden: the line closes up behind it.
            nameCaption.Visible = false;
            name.Visible = false;
            StaThread.Settle();

            _now = "a page with a column beside it, the Name pair hidden";

            if (trackCaption.Left != 0)
            {
                problems.Add($"{_now}: the Track caption is at x={trackCaption.Left} - the row should have closed up to 0");
            }

            CheckSnug();
            Audit();

            // Wide again. What wrapped onto more lines while the page was
            // narrow is back on one, and the cards have to come back down
            // with it: a table that sizes itself keeps the height it grew to
            // (dotnet/winforms issue 10379), which is how Race Control's
            // cards once kept a second row of flags' height under one.
            nameCaption.Visible = true;
            name.Visible = true;
            page.Bounds = new Rectangle(0, 0, 1000, 760);
            StaThread.Settle();

            _now = "a page with a column beside it, narrow and then 1000x760 again";

            if (round.Top >= name.Bottom)
            {
                problems.Add($"{_now}: the round picker is still on a line of its own - it fits beside the track again");
            }

            CheckColumn();
            CheckSnug();
            Audit();

            if (flagButtons.Concat(tools).Any(button => button.Text.EndsWith("...", StringComparison.Ordinal)))
            {
                problems.Add($"{_now}: a caption kept its \"...\" - the button was measured with it on");
            }

            if (tools.Select(button => button.Width).Distinct().Count() != 1)
            {
                problems.Add($"{_now}: the buttons in a bar of one width are {string.Join(", ", tools.Select(button => button.Width))}px wide");
            }

            // A card that sizes itself is as tall as what is in it and its
            // inset - no taller. The audit cannot see empty space.
            void CheckSnug()
            {
                foreach (var card in new[] { session, flags })
                {
                    var bottom = card.Controls.Cast<Control>()
                        .Where(part => part.Visible && part is not ThinScrollBar)
                        .Max(part => part.Bottom);

                    var spare = card.Height - bottom - Section.Inset.Bottom;

                    if (Math.Abs(spare) > 1)
                    {
                        problems.Add($"{_now}: the \"{card.Controls[0].Text}\" card is {card.Height}px tall and has "
                            + $"{spare + Section.Inset.Bottom}px under its last part - it should have its inset, {Section.Inset.Bottom}px");
                    }
                }
            }

            void Audit()
            {
                foreach (var finding in LayoutAudit.Check(page, "Beside", StaThread.Settle))
                {
                    problems.Add($"{_now}: [{finding.Kind}] {finding.Detail}");
                }
            }

            void CheckColumn()
            {
                var column = new Control[] { clock, tiles }.Concat(sideButtons).ToList();

                if (column.Select(part => part.Width).Distinct().Count() != 1 || clock.Width < 260)
                {
                    problems.Add($"{_now}: the column's parts are {string.Join(", ", column.Select(part => part.Width))}px wide - one width, never under 260px");
                }

                if (clock.Right != body.ClientSize.Width)
                {
                    problems.Add($"{_now}: the column ends at {clock.Right}, not against the right-hand edge at {body.ClientSize.Width}");
                }

                if (flags.Parent!.Right > clock.Left - UiLayout.RowGap)
                {
                    problems.Add($"{_now}: the parts on the left reach {flags.Parent!.Right}, into the column's gap at {clock.Left}");
                }

                // Parked beside the list, not loose in the Beside, where it
                // would sit over the column.
                if (body.Controls.OfType<ThinScrollBar>().Any())
                {
                    problems.Add($"{_now}: the list's themed scrollbar was parked in the Beside rather than beside the list");
                }
            }
        }
        finally
        {
            Application.ThreadException -= Threw;
        }

        return problems;
    }

    /// <summary>A window nobody sees: off-screen, transparent, never activated, not on the taskbar.</summary>
    private sealed class QuietWindow : Form
    {
        public QuietWindow()
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            Opacity = 0d;
            Size = new Size(200, 200);
            Font = AppTheme.BodyFont;
            BackColor = AppTheme.Background;
        }

        protected override bool ShowWithoutActivation => true;
    }
}
