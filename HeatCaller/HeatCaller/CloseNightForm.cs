namespace HeatCaller;

/// <summary>
/// The closing checklist window (11 Nov 2026 - see CloseNightChecklist):
/// what is left before lights out, one line each, with a button that goes
/// to the screen that sorts it and a box to tick it off by hand.
///
/// WHY a window that stays open rather than a dialog: closing the night is
/// a walk around the app - Rentals, Who's Racing, the club website, Backup -
/// and a dialog would have to be shut to reach any of them. This one stays
/// up beside them, and reads the app again every couple of seconds, so a
/// rental checked back in on the Rentals page ticks itself here.
/// </summary>
internal sealed class CloseNightForm : Form
{
    /// <summary>How often the list is read again while the window is open.</summary>
    private const int RefreshMs = 2000;

    private readonly Func<List<CloseNightChecklist.Item>?> _read;
    private readonly Action<string, bool> _tick;
    private readonly Action<CloseNightKind> _go;
    private readonly Func<IReadOnlyList<string>> _ownLines;
    private readonly Action<List<string>> _saveOwnLines;

    private readonly Label _lblHeadline = new();
    private readonly TableLayoutPanel _rows = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = RefreshMs };

    /// <summary>What the rows were last built from, so an unchanged list isn't rebuilt (and doesn't flicker).</summary>
    private string _shown = string.Empty;

    /// <param name="read">Tonight's list (RaceProgramControl.ClosingChecklist).</param>
    /// <param name="tick">Ticks a line off by hand, or back on.</param>
    /// <param name="go">Goes to the screen that sorts a line (or, for the backup, takes one).</param>
    /// <param name="ownLines">The club's own lines.</param>
    /// <param name="saveOwnLines">Saves the club's own lines.</param>
    public CloseNightForm(
        Func<List<CloseNightChecklist.Item>?> read,
        Action<string, bool> tick,
        Action<CloseNightKind> go,
        Func<IReadOnlyList<string>> ownLines,
        Action<List<string>> saveOwnLines)
    {
        _read = read;
        _tick = tick;
        _go = go;
        _ownLines = ownLines;
        _saveOwnLines = saveOwnLines;

        Text = "Close the Night";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        _lblHeadline.AutoSize = true;
        _lblHeadline.Font = AppTheme.HeadingFont;

        var lblHow = new Label
        {
            Text = "Ticks itself as the app sees each one done. Dealt with something it can't see - a rental handed back, "
                   + "an entry paid another way? Tick it off by hand.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            ForeColor = AppTheme.TextMuted,
        };

        _rows.AutoSize = true;
        _rows.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _rows.ColumnCount = 3;
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _rows.Dock = DockStyle.Top;

        var scroller = new Panel { AutoScroll = true };
        scroller.Controls.Add(_rows);

        var btnOwn = new Button { Text = "Your Own Lines...", AutoSize = true };
        btnOwn.Click += (_, _) => EditOwnLines();

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true };
        btnClose.Click += (_, _) => Close();

        var page = new FillPage();
        page.Add(_lblHeadline, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(lblHow, stretch: false, gapBelow: UiLayout.RowGap);
        page.AddFill(scroller, 240, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnClose, btnOwn), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 640, height: 600);

        _timer.Tick += (_, _) => Reload();
        Shown += (_, _) => _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();

        Reload();
    }

    /// <summary>Reads the list again and, if anything changed, redraws it.</summary>
    internal void Reload()
    {
        var items = _read() ?? new List<CloseNightChecklist.Item>();
        var shown = string.Join("\n", items.Select(i => $"{i.Key}|{i.Words}|{i.SeenDone}|{i.Ticked}"));

        if (shown == _shown && _rows.Controls.Count > 0)
        {
            return;
        }

        _shown = shown;

        var left = items.Count(i => !i.Done);
        _lblHeadline.Text = left == 0 ? "All wrapped up - lights out." : $"Before lights out: {Plural.Of(left, "thing")} to do";

        _rows.SuspendLayout();

        foreach (Control old in _rows.Controls.Cast<Control>().ToList())
        {
            old.Dispose();
        }

        _rows.Controls.Clear();
        _rows.RowStyles.Clear();
        _rows.RowCount = 0;

        foreach (var item in items)
        {
            AddRow(item);
        }

        _rows.ResumeLayout();
        AppTheme.ApplyRecursively(_rows);
        ColourRows(items);
    }

    private void AddRow(CloseNightChecklist.Item item)
    {
        var row = _rows.RowCount++;
        _rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // The tick: done by the app is ticked and can't be unticked - it
        // isn't the director's to undo, it's a fact.
        var tick = new ThemedCheckBox
        {
            Checked = item.Done,
            Enabled = !item.SeenDone,
            AutoSize = true,
            Margin = new Padding(0, 6, 8, 6),
            Tag = item.Key,
        };
        tick.CheckedChanged += (_, _) => _tick(item.Key, tick.Checked);

        var words = new Label
        {
            Text = Sentence(item.Words) + (item.SeenDone ? "  ✓" : string.Empty),
            AutoSize = true,
            MaximumSize = new Size(380, 0),
            Margin = new Padding(0, 8, 8, 6),
            Tag = item.Key,
        };

        // Clicking the words ticks it too, as it would beside a tick box.
        words.Click += (_, _) =>
        {
            if (tick.Enabled)
            {
                tick.Checked = !tick.Checked;
            }
        };

        _rows.Controls.Add(tick, 0, row);
        _rows.Controls.Add(words, 1, row);

        if (!item.Done && GoCaption(item.Kind) is { } caption)
        {
            var go = new Button { Text = caption, AutoSize = true, MinimumSize = new Size(140, 0), Margin = new Padding(0, 2, 0, 2) };
            go.Click += (_, _) => _go(item.Kind);
            _rows.Controls.Add(go, 2, row);
        }
    }

    /// <summary>A line as it stands on its own: capital first, since the card's wording runs on from "Before lights out:".</summary>
    internal static string Sentence(string words) =>
        words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];

    /// <summary>Done lines greyed, so what's left stands out.</summary>
    private void ColourRows(IReadOnlyList<CloseNightChecklist.Item> items)
    {
        var done = items.Where(i => i.Done).Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (Control control in _rows.Controls)
        {
            if (control is Label label && label.Tag is string key)
            {
                label.ForeColor = done.Contains(key) ? AppTheme.TextMuted : AppTheme.TextDark;
            }
        }
    }

    /// <summary>What the button beside a line says, or null for a line only a tick can settle.</summary>
    internal static string? GoCaption(CloseNightKind kind) => kind switch
    {
        CloseNightKind.Rentals => "Rentals",
        CloseNightKind.Money => "Who's Racing",
        CloseNightKind.Website => "Club Website",
        CloseNightKind.NightEmails => "Club Website",
        CloseNightKind.Backup => "Back Up Now",
        CloseNightKind.CloseDay => "Setup",
        _ => null,
    };

    /// <summary>The club's own lines, one per line of a box.</summary>
    private void EditOwnLines()
    {
        using var form = new Form
        {
            Text = "Your Own Lines",
            Font = AppTheme.BodyFont,
            BackColor = AppTheme.Background,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
        };

        var lbl = new Label
        {
            Text = "Anything else your club does before lights out, one per line - \"Lock the clubhouse\", "
                   + "\"Pack up the timing loop\". They're on the list every night, ticked off by hand.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
        };

        var box = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, _ownLines()),
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK };

        var page = new FillPage();
        page.Add(lbl, stretch: false, gapBelow: UiLayout.RowGap);
        page.AddFill(box, 160, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        form.Controls.Add(page);
        form.CancelButton = btnCancel;

        AppTheme.ApplyRecursively(form);
        DialogFit.Keep(form, page, width: 500, height: 380);

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _saveOwnLines(CloseNightChecklist.Own(box.Lines));
            _shown = string.Empty;
            Reload();
        }
    }
}
