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
/// </summary>
public sealed class ShortcutSheetForm : Form
{
    public ShortcutSheetForm()
    {
        Text = "Keyboard shortcuts";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        Font = AppTheme.BodyFont;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var columns = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
        };
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        columns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        // Race Control and the driver keys on the left, since they are the
        // ones looked for mid-race; the app-wide ones on the right.
        var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 24, 0) };
        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };

        var chips = new List<Label>();

        for (var i = 0; i < ShortcutCatalog.Sections.Count; i++)
        {
            var target = i < 2 ? left : right;
            target.Controls.Add(BuildSection(ShortcutCatalog.Sections[i], chips));
        }

        var footer = new Label
        {
            Text = "Press any key to close.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            Margin = new Padding(0, 12, 0, 0),
        };
        right.Controls.Add(footer);

        columns.Controls.Add(left, 0, 0);
        columns.Controls.Add(right, 1, 0);
        Controls.Add(columns);

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
    }

    private static Control BuildSection(ShortcutCatalog.Section section, List<Label> chips)
    {
        var table = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Margin = new Padding(0, 0, 0, 14),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label { Text = section.Title, AutoSize = true, Font = AppTheme.BoldFont, Margin = new Padding(0, 0, 0, 0) };
        table.Controls.Add(title, 0, 0);
        table.SetColumnSpan(title, 2);

        var note = new Label { Text = section.Note, AutoSize = true, Font = AppTheme.SmallFont, Margin = new Padding(0, 0, 0, 6) };
        table.Controls.Add(note, 0, 1);
        table.SetColumnSpan(note, 2);

        var row = 2;
        foreach (var item in section.Items)
        {
            var chip = new Label
            {
                Text = item.Keys,
                AutoSize = true,
                Font = AppTheme.BoldFont,
                Padding = new Padding(6, 2, 6, 2),
                Margin = new Padding(0, 2, 12, 2),
                TextAlign = ContentAlignment.MiddleCenter,
            };
            chips.Add(chip);

            var action = new Label { Text = item.Action, AutoSize = true, Margin = new Padding(0, 5, 0, 2) };

            table.Controls.Add(chip, 0, row);
            table.Controls.Add(action, 1, row);
            row++;
        }

        table.RowCount = row;
        return table;
    }
}
