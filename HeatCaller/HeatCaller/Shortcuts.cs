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
            new Shortcut("F1", "Start the race - through On Air when Start races On Air is ticked"),
            new Shortcut("0", "On Air (hold for a second while it runs to skip to Stage)"),
            new Shortcut("F2", "Countdown"),
            new Shortcut("F3", "Green flag"),
            new Shortcut("F4", "Yellow flag"),
            new Shortcut("F5", "Red flag"),
            new Shortcut("F6", "Checkered flag"),
            new Shortcut("F7", "Reset (asks first if a race is running)"),
            new Shortcut("F8", "Quick Race"),
            new Shortcut("F9", "Between races: skip the line on air"),
            new Shortcut("F10", "Between races: Hold Talk / Resume Talk"),
            new Shortcut("F12", "Between races: call up the next race"),
            new Shortcut("H", "Between races: hold / resume Auto start"),
            new Shortcut("N", "Between races: Auto start's Start now"),
            new Shortcut("S", "Play again: the winner after the flag, the call-up between races, the last line during a race"),
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
/// Control's keys down the left, the driver keys and the app-wide ones down
/// the right - each group a card (18 Nov 2026) with its note as the hint
/// line and a field grid under it, the chips in a column as wide as the
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

    public ShortcutSheetForm()
    {
        // Built with its layout held - see LayoutFreeze.HoldWhileBuilding
        // (18 Nov 2026): built live, most parts had their windows before the
        // theme pass, which made many of them again.
        using var building = LayoutFreeze.HoldWhileBuilding();

        Text = "Keyboard shortcuts";
        KeyPreview = true;
        Font = AppTheme.BodyFont;

        var chips = new List<Label>();
        var notes = new List<Label>();

        // THE LOOK (18 Nov 2026, dialog pass 6): each group a card, its
        // title the card's caption and its note the card's hint line. Race
        // Control's keys down the left, the driver keys at the top of the
        // right - the ones looked for mid-race at the top of each - and the
        // app-wide ones under them. The driver keys moved across from under
        // Race Control's: with the cards' edges and captions the left-hand
        // column ran taller than a 1920x1080 screen, and the sheet scrolled;
        // the two columns are near the same height now, and shorter.
        var cards = ShortcutCatalog.Sections
            .Select(section => GroupCard(section, chips, notes))
            .ToList();

        // Each column as wide as its widest card's lines on one line - a key
        // and what it does, or a group's note or caption - and the card's
        // edges. Measured now, before the cards are in the window, so the
        // window is never laid out with a column of no width: every line in
        // them was given its face as it was made (GroupCard), and the theme
        // pass changes none of them.
        var leftWidth = cards[0].Width;
        var rightWidth = cards.Skip(1).Max(card => card.Width);

        // The left column is the Beside's own, exactly as tall as its
        // shortcuts, not the column's height - see Beside.AddSideMeasured.
        // The right takes the width left beside it, the row gap the Beside
        // keeps between them away; the Beside is as tall as the taller.
        var columns = new Beside(leftWidth)
        {
            SideOnLeft = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        columns.AddSideCard(cards[0].Card, gapBelow: 0);

        for (var i = 1; i < cards.Count; i++)
        {
            columns.Add(cards[i].Card, gapBelow: i == cards.Count - 1 ? 0 : UiLayout.RowGap);
        }

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

        // The chips in the page's colour on the card's, as keys stand out
        // of a keyboard - on the card, the card's own colour would lose them.
        foreach (var chip in chips)
        {
            chip.BackColor = AppTheme.Background;
            chip.ForeColor = AppTheme.Accent;
        }

        foreach (var note in notes)
        {
            note.ForeColor = AppTheme.TextMuted;
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

        // The two columns and the row gap between them, the page's margins
        // round them - and room for the body's scrollbar, which takes its
        // width from the right-hand column on a screen too short for them.
        var width = UiLayout.PageLeft
            + leftWidth
            + UiLayout.RowGap
            + rightWidth
            + SystemInformation.VerticalScrollBarWidth
            + UiLayout.PageRight;

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);
    }

    /// <summary>
    /// One group of shortcuts as a card: its title the caption, its note
    /// the hint line under it, then a grid with a line for each shortcut -
    /// the keys in a chip in the grid's caption column, what they do beside
    /// them. Spaced as the sheet always was, closer than a form's fields:
    /// the grid's own gaps are replaced as each part goes in. The chips go
    /// in <paramref name="chips"/> and the note in <paramref name="notes"/>
    /// to be coloured once the theme pass is done. With the card, how wide
    /// it is with every line on one line, its edges included.
    /// </summary>
    private static (Section Card, int Width) GroupCard(ShortcutCatalog.Section section, List<Label> chips, List<Label> notes)
    {
        var grid = new FieldGrid();

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

        var note = new Label { Text = section.Note, AutoSize = true, Font = AppTheme.SmallFont };
        notes.Add(note);

        var card = new Section(section.Title);
        card.Add(note, gapBelow: NoteGap);
        card.Add(grid, gapBelow: 0);

        // The grid's keys and what they do on one line, the note, or the
        // caption - whichever is widest - and the card's edges.
        var lines = Math.Max(
            grid.GetPreferredSize(Size.Empty).Width,
            Math.Max(note.GetPreferredSize(Size.Empty).Width, card.CaptionWidth));

        return (card, lines + Section.Inset.Horizontal);
    }
}
