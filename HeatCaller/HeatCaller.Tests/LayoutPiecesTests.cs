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
/// with a list that fills it. And the pieces Race Day's tabs are built
/// from: grids in separate cards lined up down the page, captions level
/// with the first line of a field more than a line tall, a hint that holds
/// still when it is reworded, a filter row whose summary moves onto a line
/// of its own, two cards side by side in a page that sizes its parts, two
/// bars of buttons against opposite edges - and the fonts they keep in step
/// with the text size. And a grid whose columns fill it, which has to share
/// its width out by their weights however it came to that width.
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
            grid.AddWide(new ThemedCheckBox { Text = "Announce lap times", AutoSize = true });

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

    [Fact]
    public void CardsLineUpHoldStillAndComeBackDownWhenWidenedAgain()
    {
        var problems = StaThread.Run(CheckCards, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, "The cards did not lay out as they should:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void WhatAPageKeepsFollowsTheTextSize()
    {
        var problems = StaThread.Run(CheckTextSize, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, "TextSize did not follow the text size:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void AGridSharesItsWidthByItsWeightsHoweverItGrew()
    {
        var problems = StaThread.Run(CheckGridWeights, TimeSpan.FromMinutes(1), () => _now);

        Assert.True(problems.Count == 0, "The grid did not share out its width:\n" + string.Join("\n", problems));
    }

    /// <summary>
    /// A page built the way Race Day's tabs are: field grids in separate
    /// cards sharing one caption column, a hint that says one of two
    /// things, a filter row whose summary moves onto a line of its own when
    /// it no longer fits, two cards side by side in a page that sizes its
    /// parts, and two bars of buttons against opposite edges. Wide, narrow,
    /// and wide again - where what wrapped has to come back up.
    /// </summary>
    private static List<string> CheckCards()
    {
        _now = "a page of cards";

        var problems = new List<string>();

        void Threw(object? sender, ThreadExceptionEventArgs e) =>
            problems.Add($"{_now}: {e.Exception.GetType().Name}: {e.Exception.Message}");

        Application.ThreadException += Threw;

        try
        {
            using var window = new QuietWindow();
            var page = new Panel { Bounds = new Rectangle(0, 0, 1200, 900) };

            window.Controls.Add(page);

            var stack = new PageStack();
            page.Controls.Add(stack);

            // --- Two grids in two cards, lined up ---
            var about = stack.Add(new Section("About the day"));
            var aboutGrid = about.Add(new FieldGrid(), gapBelow: 0);

            var name = new TextBox { Width = 200 };
            aboutGrid.AddRow("Name:", name);

            const string shortWording = "Qualifying heats, then finals.";
            const string longWording =
                "A flat list of races - no finals, no seeding, no bump-ups. Everyone in a racing class is in them, "
                + "checked in or not, and the night's standings are simply the order they finished in.";

            var hint = aboutGrid.AddHint(new SteadyLabel { Wordings = new[] { shortWording, longWording } });
            hint.Text = shortWording;

            var only = aboutGrid.AddField(new ThemedCheckBox { Text = "Checked-in drivers only", AutoSize = true });

            // A row of controls that wraps when the page is narrow, and a box
            // of several lines: their captions beside their first lines.
            var dayPicker = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            dayPicker.Items.Add("Club night, 29 September");
            dayPicker.SelectedIndex = 0;

            var wizard = new Button { Text = "Set Up With the Wizard..." };
            var workingCaption = aboutGrid.AddRow("Working on:", FieldGrid.Inline(dayPicker, new Button { Text = "New Day..." }, wizard));

            var notes = new TextBox { Multiline = true, Width = 300, Height = 70 };
            var notesCaption = aboutGrid.AddRow("Notes:", notes);

            var program = stack.Add(new Section("Building the program"));
            var programGrid = program.Add(new FieldGrid(), gapBelow: 0);

            var rounds = new NumericUpDown { Width = 70, Minimum = 1, Maximum = 10, Value = 3 };
            var roundsCaption = programGrid.AddRow("Heats each driver gets:", rounds);

            var scope = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            scope.Items.Add("the class that was racing");
            scope.SelectedIndex = 0;
            programGrid.AddRow("Next race is:", scope);

            // --- Two cards side by side, in a page that sizes its parts ---
            var step = new Section();
            step.Add(new Label { Text = "NEXT STEP", AutoSize = true });
            step.Add(new Label { Text = "Build tonight's heats", AutoSize = true });
            step.Add(new ButtonBar().With(new Button { Text = "Build" }), gapBelow: 0);

            var ready = new Section("Ready to race");
            var todo = ready.Add(new Label
            {
                Text = "Still to do: choose tonight's classes  ·  add drivers to tonight's classes  ·  build the program  ·  connect the timer",
                AutoSize = true,
            });
            var finish = ready.Add(new ButtonBar().With(new Button { Text = "Finish Setting Up..." }), gapBelow: 0);

            var band = stack.Add(new Beside(() => 260) { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink });
            band.AddFill(step, width => step.GetPreferredSize(new Size(width, 0)).Height);
            band.AddSideCard(ready);

            // --- A filter row with a summary that moves ---
            var filters = stack.Add(new Section("Drivers"));
            var row = filters.Add(new FieldRow(), gapBelow: 0);

            var classes = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
            classes.Items.Add("(all classes)");
            classes.SelectedIndex = 0;

            var tech = new ThemedCheckBox { Text = "Only those not through tech", AutoSize = true };
            var summary = new Label { AutoSize = false, AutoEllipsis = true, Text = "3 of 12 checked in" };

            row.Add(new Label { Text = "Class:" }, classes);
            row.Add(null, tech);
            row.AddLast(null, summary, () => TextRenderer.MeasureText(summary.Text, summary.Font).Width);

            // --- Two bars against opposite edges ---
            var bars = stack.Add(new Section("Tonight's program"), gapBelow: 0);

            var commands = new[] { "Start Selected", "Edit Drivers...", "More..." }
                .Select(caption => new Button { Text = caption })
                .ToArray();

            var tools = new[] { "Glossary...", "Standings...", "Timetable..." }
                .Select(caption => new Button { Text = caption })
                .ToArray();

            var split = bars.Add(new SplitBar(new ButtonBar().With(commands), new ButtonBar(alignRight: true).With(tools)), gapBelow: 0);

            // Where a button in either bar is in the SplitBar - each bar
            // places its own buttons from its own top.
            static int TopIn(Button button) => button.Parent!.Top + button.Top;
            static int BottomIn(Button button) => button.Parent!.Top + button.Bottom;

            AppTheme.ApplyRecursively(page);

            // After the theme pass, as a page does it: the captions are in
            // the font they will be drawn in by then.
            FieldGrid.LineUp(new[] { aboutGrid, programGrid }, "Races each driver gets:");

            window.Show();
            StaThread.Settle();

            _now = "a page of cards, 1200 wide";

            CheckLinedUp();
            CheckSnug();
            CheckBand();
            CheckFirstLines(wrapped: false);
            Audit();

            if (commands.Concat(tools).Select(TopIn).Distinct().Count() != 1)
            {
                problems.Add($"{_now}: the two bars are not on one line - there is room for both");
            }

            if (tools[0].Right != tools[0].Parent!.Width || tools[0].Parent!.Right != split.ClientSize.Width)
            {
                problems.Add($"{_now}: the glossary ends at {tools[0].Right} in a bar ending at {tools[0].Parent!.Right} - both should be against the right-hand edge ({split.ClientSize.Width})");
            }

            if (summary.Top > classes.Bottom)
            {
                problems.Add($"{_now}: the summary is on a line of its own - it fits beside the filter");
            }

            // Reworded: the hint is as tall as its longer wording already,
            // so nothing under it moves.
            var onlyTop = only.Top;
            hint.Text = longWording;
            StaThread.Settle();

            if (only.Top != onlyTop)
            {
                problems.Add($"{_now}: the check box under the hint moved from {onlyTop} to {only.Top} when the hint was reworded");
            }

            // A summary that no longer fits beside the filter goes onto a
            // line of its own - told by nothing but its own text changing.
            summary.Text = "3 of 12 checked in (40 total checked in across all classes)   —   heats are built from checked-in drivers only, "
                + "and everyone else waits for the next race day";
            StaThread.Settle();

            if (summary.Top <= classes.Bottom)
            {
                problems.Add($"{_now}: the long summary is still beside the filter, {summary.Width}px wide - it should have gone onto a line of its own");
            }

            CheckSnug();
            Audit();

            page.Bounds = new Rectangle(0, 0, 560, 900);
            StaThread.Settle();

            _now = "a page of cards, 560 wide";

            if (tools.Select(TopIn).Min() < commands.Select(BottomIn).Max())
            {
                problems.Add($"{_now}: the day tools are beside the round commands - there is not room, and they should have gone under");
            }

            if (tools[0].Parent!.Right != split.ClientSize.Width)
            {
                problems.Add($"{_now}: the day tools end at {tools[0].Parent!.Right}, not against the right-hand edge at {split.ClientSize.Width}");
            }

            if (todo.Height < 2 * todo.Font.Height)
            {
                problems.Add($"{_now}: what is left to do is {todo.Height}px tall - in a 260px card it should wrap");
            }

            CheckLinedUp();
            CheckSnug();
            CheckBand();
            CheckFirstLines(wrapped: true);
            Audit();

            summary.Text = "3 of 12 checked in";
            page.Bounds = new Rectangle(0, 0, 1200, 900);
            StaThread.Settle();

            _now = "a page of cards, narrow and then 1200 wide again";

            if (commands.Concat(tools).Select(TopIn).Distinct().Count() != 1)
            {
                problems.Add($"{_now}: the two bars did not come back onto one line");
            }

            if (summary.Top > classes.Bottom)
            {
                problems.Add($"{_now}: the short summary did not come back beside the filter");
            }

            CheckLinedUp();
            CheckSnug();
            CheckBand();
            CheckFirstLines(wrapped: false);
            Audit();

            // The top margin, the column, and the bottom margin or the
            // theme's room under the last control, whichever is more - what
            // the page needs not to scroll.
            var contentHeight = stack.Padding.Top + bars.Bottom + Math.Max(stack.Padding.Bottom, stack.AutoScrollMargin.Height);

            if (stack.ContentHeight != contentHeight)
            {
                problems.Add($"{_now}: the page says its content is {stack.ContentHeight}px tall - it is {contentHeight}px");
            }

            // The two grids share a caption column, however wide their own
            // captions are, so their fields start at one x.
            void CheckLinedUp()
            {
                if (name.Left != rounds.Left || hint.Left != name.Left || only.Left != name.Left)
                {
                    problems.Add($"{_now}: the fields start at {name.Left} (Name), {hint.Left} (the hint), {only.Left} (the check box) "
                        + $"and {rounds.Left} (Heats) - one column, lined up");
                }

                var reworded = TextRenderer.MeasureText("Races each driver gets:", roundsCaption.Font).Width;

                if (roundsCaption.Width < reworded)
                {
                    problems.Add($"{_now}: the caption column is {roundsCaption.Width}px - the other wording needs {reworded}px, so rewording would move the fields");
                }
            }

            // A card or grid that sizes itself is as tall as what is in it
            // and no taller - including after it was narrow. The audit
            // cannot see empty space.
            void CheckSnug()
            {
                foreach (var card in new[] { about, program, filters, bars })
                {
                    var bottom = card.Controls.Cast<Control>()
                        .Where(part => part.Visible && part is not ThinScrollBar)
                        .Max(part => part.Bottom);

                    var spare = card.Height - bottom - Section.Inset.Bottom;

                    if (Math.Abs(spare) > 1)
                    {
                        problems.Add($"{_now}: the \"{card.Controls[0].Text}\" card has {spare + Section.Inset.Bottom}px under its last part - it should have its inset, {Section.Inset.Bottom}px");
                    }
                }

                foreach (var grid in new[] { aboutGrid, programGrid })
                {
                    var bottom = grid.Controls.Cast<Control>().Where(part => part.Visible).Max(part => part.Bottom);

                    if (Math.Abs(grid.Height - bottom) > 1)
                    {
                        problems.Add($"{_now}: a field grid is {grid.Height}px tall and its last row ends at {bottom}");
                    }
                }
            }

            // A caption beside a field more than a line tall is level with
            // its first line - "Working on:" with the picker when the buttons
            // after it have wrapped under it, "Notes:" with the first line of
            // the notes - and one beside a row on one line is centred on it,
            // as every other caption is.
            void CheckFirstLines(bool wrapped)
            {
                static int Middle(Control control) => control.Top + control.Height / 2;

                var row = dayPicker.Parent!;

                if (wrapped != (wizard.Top >= dayPicker.Bottom))
                {
                    problems.Add(wrapped
                        ? $"{_now}: the wizard's button is still beside the picker, in a row {row.Width}px wide - it should have wrapped"
                        : $"{_now}: the wizard's button is under the picker, in a row {row.Width}px wide - there is room beside it");
                }

                var pickerTop = row.Top + dayPicker.Top;
                var pickerBottom = row.Top + dayPicker.Bottom;

                if (Middle(workingCaption) < pickerTop || Middle(workingCaption) > pickerBottom)
                {
                    problems.Add($"{_now}: \"Working on:\" is {workingCaption.Top}-{workingCaption.Bottom}, not beside the picker at {pickerTop}-{pickerBottom}");
                }

                if (!wrapped && Math.Abs(Middle(workingCaption) - Middle(row)) > 1)
                {
                    problems.Add($"{_now}: \"Working on:\" is not centred on its row of one line ({workingCaption.Top}-{workingCaption.Bottom} beside {row.Top}-{row.Bottom})");
                }

                var firstLineBottom = notes.Top + 4 + Pieces.LineHeight(notes.Font);

                if (Middle(notesCaption) < notes.Top || Middle(notesCaption) > firstLineBottom)
                {
                    problems.Add($"{_now}: \"Notes:\" is {notesCaption.Top}-{notesCaption.Bottom}, not beside the first line of the notes at {notes.Top}-{firstLineBottom}");
                }
            }

            // Side by side: the card on the left as tall as the pair, the
            // one on the right against the right-hand edge, at least 260px.
            void CheckBand()
            {
                if (step.Height != band.ClientSize.Height || ready.Height > band.ClientSize.Height)
                {
                    problems.Add($"{_now}: the cards are {step.Height}px and {ready.Height}px tall in a band {band.ClientSize.Height}px tall - "
                        + "the left one should fill it, and the right one fit in it");
                }

                if (ready.Right != band.ClientSize.Width || ready.Width < 260)
                {
                    problems.Add($"{_now}: the side card is {ready.Width}px wide ending at {ready.Right} - at least 260, against the edge at {band.ClientSize.Width}");
                }

                if (finish.Bottom > ready.ClientSize.Height)
                {
                    problems.Add($"{_now}: the side card's button runs off its bottom");
                }
            }

            void Audit()
            {
                foreach (var finding in LayoutAudit.Check(page, "Cards", StaThread.Settle))
                {
                    problems.Add($"{_now}: [{finding.Kind}] {finding.Detail}");
                }
            }
        }
        finally
        {
            Application.ThreadException -= Threw;
        }

        return problems;
    }

    /// <summary>
    /// A face worn through TextSize, and a label with lines kept for it,
    /// after the text size changes - the fonts are replaced then, not
    /// resized, so anything handed one has to be handed the new one.
    /// </summary>
    private static List<string> CheckTextSize()
    {
        _now = "a text size change";

        var problems = new List<string>();

        try
        {
            AppTheme.SetFontScale(1f);

            var keeper = new TextSize();

            using var hint = keeper.Wear(new Label { Text = "A hint." }, () => AppTheme.SmallFont);
            using var status = keeper.Wear(new Label { Text = string.Empty, AutoSize = true }, () => AppTheme.BoldFont);

            keeper.ReserveLines(status, 2);

            var kept = 0;
            keeper.Keep(() => kept++);

            AppTheme.SetFontScale(1.5f);
            keeper.Apply();

            if (!Equals(hint.Font, AppTheme.SmallFont))
            {
                problems.Add($"{_now}: the hint is still in {hint.Font.Size}pt - the small face is {AppTheme.SmallFont.Size}pt now");
            }

            if (status.MinimumSize.Height != 2 * Pieces.LineHeight(AppTheme.BoldFont))
            {
                problems.Add($"{_now}: the status keeps {status.MinimumSize.Height}px - two lines of the bold face are {2 * Pieces.LineHeight(AppTheme.BoldFont)}px now");
            }

            if (kept != 2)
            {
                problems.Add($"{_now}: something kept was done {kept} times - once when kept, once at the new size");
            }
        }
        finally
        {
            AppTheme.SetFontScale(1f);
        }

        return problems;
    }

    /// <summary>
    /// Lap Beeps' grid: five columns filling it, the last with a blank
    /// heading and so a minimum of next to nothing. Built too narrow for
    /// its headings, so every column starts at its minimum, then widened,
    /// narrowed, narrowed until the widest heading is more than its column's
    /// share, and widened again. At each width the columns have to fill the
    /// grid without running past its edge, none narrower than its heading,
    /// and those not held at their heading's width sharing the rest by
    /// their weights. Growing from its minimums, the blank one took most of
    /// the width and the rest were left bunched at the left (layout
    /// pictures, 29 Sep) - see Pieces.KeepFillWeights.
    /// </summary>
    private static List<string> CheckGridWeights()
    {
        _now = "a grid filling its width";

        var problems = new List<string>();

        void Threw(object? sender, ThreadExceptionEventArgs e) =>
            problems.Add($"{_now}: {e.Exception.GetType().Name}: {e.Exception.Message}");

        Application.ThreadException += Threw;

        try
        {
            AppTheme.SetFontScale(1f);

            using var window = new QuietWindow();

            var grid = new DataGridView
            {
                Bounds = new Rectangle(0, 0, 120, 300),
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };

            grid.Columns.AddRange(
                new DataGridViewTextBoxColumn { HeaderText = "Driver", FillWeight = 90 },
                new DataGridViewTextBoxColumn { HeaderText = "Transponder", FillWeight = 60 },
                new DataGridViewTextBoxColumn { HeaderText = "Class", FillWeight = 60 },
                new DataGridViewTextBoxColumn { HeaderText = "Cue", FillWeight = 70 },
                new DataGridViewTextBoxColumn { HeaderText = "", FillWeight = 40 });

            // The weights it was built with - the ones set on the grid are
            // made to allow for the headings where a share is short of one.
            var columns = grid.Columns.Cast<DataGridViewColumn>().ToList();
            var built = columns.Select(column => column.FillWeight).ToList();

            window.Size = new Size(1700, 400);
            window.Controls.Add(grid);

            Pieces.KeepHeadingsWhole(grid);

            window.Show();
            StaThread.Settle();

            // Narrow enough that Transponder's heading is more than its
            // share, with room over the headings for the others to share.
            var narrow = columns.Sum(column => column.MinimumWidth) + 60;

            foreach (var width in new[] { 1600, 700, narrow, 1600 })
            {
                _now = $"a grid filling its width, {width}px wide";

                grid.Width = width;
                StaThread.Settle();

                var widths = string.Join(", ", columns.Select(column => column.Width));

                if (grid.Controls.OfType<HScrollBar>().Any(bar => bar.Visible))
                {
                    problems.Add($"{_now}: the columns run past the grid's edge ({widths})");
                }

                // Held at its heading's width, give or take a pixel of rounding.
                bool Held(DataGridViewColumn column) => column.Width <= column.MinimumWidth + 1;

                var sharing = columns.Where(column => !Held(column)).ToList();
                var room = sharing.Sum(column => column.Width);
                var weights = sharing.Sum(column => built[column.Index]);

                foreach (var column in columns)
                {
                    var name = column.HeaderText.Length == 0 ? "the blank-headed column" : column.HeaderText;

                    var share = room * built[column.Index] / weights;

                    if (column.Width < column.MinimumWidth)
                    {
                        problems.Add($"{_now}: {name} is {column.Width}px, narrower than its heading's {column.MinimumWidth}px ({widths})");
                    }
                    else if (Held(column))
                    {
                        // Only a heading wider than the column's share holds it.
                        if (share > column.MinimumWidth + 2)
                        {
                            problems.Add($"{_now}: {name} is held at its heading's {column.MinimumWidth}px - its weight's share "
                                       + $"would be {share:0}px ({widths})");
                        }
                    }
                    else
                    {
                        if (Math.Abs(column.Width - share) > 2)
                        {
                            problems.Add($"{_now}: {name} is {column.Width}px - its weight's share of the {room}px the columns "
                                       + $"not held at their headings have is {share:0}px ({widths})");
                        }
                    }
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
