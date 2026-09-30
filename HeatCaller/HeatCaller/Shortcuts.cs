namespace HeatCaller;

/// <summary>
/// Every keyboard shortcut in the app, in one list - the cheat sheet (the ?
/// key) is drawn from this, so what it says and what the keys do are kept
/// side by side. The keys themselves are handled in MainForm
/// (MainForm_KeyDown and ProcessCmdKey).
/// </summary>
public static class ShortcutCatalog
{
    public sealed record Shortcut(string Keys, string Action);

    public sealed record Section(string Title, string Note, IReadOnlyList<Shortcut> Items);

    public static readonly IReadOnlyList<Section> Sections = new[]
    {
        new Section("Race Control", "While Race Control is showing, or in Race Mode", new[]
        {
            new Shortcut("F1", "Stage"),
            new Shortcut("F2", "Countdown"),
            new Shortcut("F3", "Green flag"),
            new Shortcut("F4", "Yellow flag"),
            new Shortcut("F5", "Red flag"),
            new Shortcut("F6", "Checkered flag"),
            new Shortcut("F7", "Reset (asks first if a race is running)"),
            new Shortcut("F8", "Quick Race"),
            new Shortcut("B", "Open the Big Screen"),
            new Shortcut("R", "Pre-race check"),
            new Shortcut("A", "Announcer screen"),
        }),

        new Section("Selected driver", "Click a driver on the leaderboard first", new[]
        {
            new Shortcut("+", "Add a lap"),
            new Shortcut("-", "Remove a lap"),
            new Shortcut("P", "+5s penalty"),
            new Shortcut("Shift+P", "+10s penalty"),
            new Shortcut("C", "Clear penalties"),
            new Shortcut("W", "Warning (then 1-7 picks the reason, 0 types one)"),
            new Shortcut("Shift+W", "Take back the last warning"),
            new Shortcut("L", "Lap breakdown"),
            new Shortcut("E", "Edit driver"),
        }),

        new Section("Anywhere", "Letters are ignored while you are typing in a box", new[]
        {
            new Shortcut("?", "This list"),
            new Shortcut("Shift+S", "Set up a race night, step by step"),
            new Shortcut("Shift+T", "Take the app tour"),
            new Shortcut("M", "Mute / unmute all sound"),
            new Shortcut("Ctrl+Z", "Undo"),
            new Shortcut("Ctrl+1 ... 9", "Go to that page in the sidebar"),
            new Shortcut("Ctrl+F", "Search pages"),
            new Shortcut("Ctrl + / Ctrl -", "Bigger / smaller text"),
            new Shortcut("Ctrl+0", "Normal text size"),
            new Shortcut("F11", "Race Mode on / off (Esc leaves)"),
        }),
    };
}

/// <summary>
/// The ? cheat sheet: every shortcut, grouped, with the keys drawn as
/// chips. Any key or a click closes it, so it is a glance, not a window
/// to manage.
///
/// On the layout pieces (DialogFit): two columns in a Beside - Race
/// Control's keys and the driver keys down the left, the app-wide ones down
/// the right - each a field grid, the chips in a column as wide as the
/// widest of them and what each does beside it, wrapping to the room there.
/// The columns are the window's body, which scrolls on a screen too short
/// for all thirty shortcuts, and "Press any key to close." is under it at
/// the foot, where it stays in view. It opens as wide as the two columns
/// need on one line each, measured at the text size in use. It used to be a
/// window that sized itself to two tables side by side, as tall as all of
/// them - taller than a 1366x768 screen has room for at the normal text
/// size, and than any screen at 200%, so the last shortcuts and the note
/// were off the bottom of the screen.
/// </summary>
public sealed class ShortcutSheetForm : Form
{
    /// <summary>Room between a key's chip and what it does, on top of the chip's own padding - as the sheet always had it.</summary>
    private const int ChipGap = 12;

    /// <summary>
    /// Between one shortcut and the next. Closer than a form's fields
    /// (UiLayout.RowGap), as they always were: the sheet is thirty lines
    /// read down at a glance, and at a row gap each it is a sixth taller.
    /// </summary>
    private const int ShortcutGap = 4;

    /// <summary>Under a group's note, before its first shortcut.</summary>
    private const int NoteGap = 6;

    /// <summary>Between the last shortcut of one group and the title of the next down the same column.</summary>
    private const int GroupGap = 16;

    public ShortcutSheetForm()
    {
        Text = "Keyboard shortcuts";
        KeyPreview = true;
        Font = AppTheme.BodyFont;

        var chips = new List<Label>();

        // Race Control and the driver keys on the left, since they are the
        // ones looked for mid-race; the app-wide ones on the right. A grid
        // for each column: the two groups on the left share one, so their
        // keys line up down it.
        var left = new FieldGrid();
        var right = new FieldGrid();
        var leftHeadings = new List<Label>();
        var rightHeadings = new List<Label>();

        for (var i = 0; i < ShortcutCatalog.Sections.Count; i++)
        {
            var onLeft = i < 2;
            AddGroup(onLeft ? left : right, ShortcutCatalog.Sections[i], chips, onLeft ? leftHeadings : rightHeadings);
        }

        // Each column as wide as its widest line on one line - a key and
        // what it does, or a group's title or note. Measured now, before the
        // columns are in the window, so the window is never laid out with a
        // column of no width: every line in them was given its face as it
        // was made (AddGroup), and the theme pass changes none of them.
        var leftWidth = OneLineWidth(left, leftHeadings);
        var rightWidth = OneLineWidth(right, rightHeadings);

        // The left column is the Beside's own - it has twice the shortcuts,
        // so it is the taller, and the Beside is as tall as it - and a
        // section gap wider than its lines: the room between the two
        // columns, less the row gap the Beside keeps between them itself.
        // The right takes the width left beside it.
        var columns = new Beside(leftWidth + UiLayout.SectionGap - UiLayout.RowGap)
        {
            SideOnLeft = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        columns.AddSideFill(left, columnWidth => left.GetPreferredSize(new Size(columnWidth, 0)).Height, gapBelow: 0);
        columns.Add(right, gapBelow: 0);

        var footer = new Label
        {
            Text = "Press any key to close.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
        };

        // The columns are the body, which scrolls on a screen too short for
        // them; the note is under it at the foot, in view either way, where
        // a dialog's buttons go.
        var page = new FillPage();
        var body = DialogFit.Body(page);
        body.Add(columns, gapBelow: 0);
        page.Add(new ButtonBar(alignRight: true).With(footer), gapBelow: 0);

        Controls.Add(page);

        AppTheme.ApplyRecursively(this);

        // After the theme pass, which would otherwise flatten the chips back
        // to the page colour.
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;
        footer.ForeColor = AppTheme.TextMuted;

        foreach (var chip in chips)
        {
            chip.BackColor = AppTheme.Surface;
            chip.ForeColor = AppTheme.Accent;
        }

        KeyDown += (_, e) =>
        {
            e.Handled = true;
            e.SuppressKeyPress = true;

            // Shift on its own is the first half of "?" - closing on it
            // would make the sheet flash shut as it opened on a repeat.
            if (e.KeyCode is Keys.ShiftKey or Keys.ControlKey or Keys.Menu)
            {
                return;
            }

            Close();
        };

        Click += (_, _) => Close();

        // The page covers the window now, and its margins are where the
        // window's own were - the rim a click closed it from.
        page.Click += (_, _) => Close();

        // The two columns and the section gap between them, the page's
        // margins round them - and room for the body's scrollbar, which
        // takes its width from the right-hand column on a screen too short
        // for them.
        var width = UiLayout.PageLeft
            + leftWidth
            + UiLayout.SectionGap
            + rightWidth
            + SystemInformation.VerticalScrollBarWidth
            + UiLayout.PageRight;

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }

    /// <summary>
    /// One group of shortcuts added to a column's grid: its title and its
    /// note across the grid, then a line for each shortcut - the keys in a
    /// chip in the grid's caption column, what they do beside them. Spaced
    /// as the sheet always was, closer than a form's fields: the grid's own
    /// gaps are replaced as each part goes in. The chips go in
    /// <paramref name="chips"/> to be coloured once the theme pass is done,
    /// and the title and note in <paramref name="headings"/>, whose widths
    /// the column's is measured with (OneLineWidth).
    /// </summary>
    private static void AddGroup(FieldGrid grid, ShortcutCatalog.Section section, List<Label> chips, List<Label> headings)
    {
        // A group under another in the same column keeps a gap above its
        // title; the first in a column starts at the top.
        var first = grid.RowCount == 0;

        var title = grid.AddWide(new Label { Text = section.Title, AutoSize = true, Font = AppTheme.BoldFont });
        title.Margin = new Padding(0, first ? 0 : GroupGap - ShortcutGap, 0, 0);

        var note = grid.AddWide(new Label { Text = section.Note, AutoSize = true, Font = AppTheme.SmallFont });
        note.Margin = new Padding(0, 0, 0, NoteGap);

        headings.Add(title);
        headings.Add(note);

        foreach (var item in section.Items)
        {
            // In the body face the window wears, given here rather than left
            // to be inherited, so the column can be measured before it is in
            // the window.
            var action = new Label { Text = item.Action, AutoSize = true, Font = AppTheme.BodyFont };

            // The grid makes the caption - the chip - and centres it and what
            // it names on their line.
            var chip = grid.AddRow(item.Keys, action, stretch: true);
            chip.Font = AppTheme.BoldFont;
            chip.Padding = new Padding(6, 2, 6, 2);
            chip.TextAlign = ContentAlignment.MiddleCenter;
            chip.Margin = new Padding(0, 0, ChipGap, ShortcutGap);

            action.Margin = new Padding(0, 0, 0, ShortcutGap);

            chips.Add(chip);
        }
    }

    /// <summary>How wide a column's grid is with each line on one line: its keys and what they do, or a title or note when that is wider (the grid does not count what goes across it).</summary>
    private static int OneLineWidth(FieldGrid grid, IReadOnlyList<Label> headings) =>
        Math.Max(
            grid.GetPreferredSize(Size.Empty).Width,
            headings.Max(heading => heading.GetPreferredSize(Size.Empty).Width + heading.Margin.Horizontal));
}
