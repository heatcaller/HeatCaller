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

    /// <summary>The start-of-night list (setup plan B3), not the closing one.</summary>
    private readonly bool _ready;

    private readonly Label _lblHeadline = new();

    /// <summary>
    /// How the night went, under the headline on the closing list (12 Nov
    /// 2026, setup plan step 8): the night pace and the race director's two
    /// measures, "entries close to heats posted" and "doors open to first
    /// green". Hidden when there is nothing to say, and on Ready to race.
    /// </summary>
    private readonly Label _lblPace = new();

    private readonly TableLayoutPanel _rows = new();

    /// <summary>The night pace line (RaceProgramControl.ClosingPaceLine); null leaves it out.</summary>
    public Func<string>? PaceLine { get; init; }
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = RefreshMs };

    /// <summary>The race day this window is for - kept after Close Day, when the day stops being the active one.</summary>
    public string? DayId { get; init; }

    /// <summary>What the rows were last built from, so an unchanged list isn't rebuilt (and doesn't flicker).</summary>
    private string _shown = string.Empty;

    /// <param name="read">Tonight's list (RaceProgramControl.ClosingChecklist).</param>
    /// <param name="tick">Ticks a line off by hand, or back on.</param>
    /// <param name="go">Goes to the screen that sorts a line (or, for the backup, takes one).</param>
    /// <param name="ownLines">The club's own lines.</param>
    /// <param name="saveOwnLines">Saves the club's own lines.</param>
    /// <param name="ready">
    /// The start-of-night list instead (12 Nov 2026, setup plan B3 - see
    /// ReadyChecklist): the same window, with its own title and words.
    /// </param>
    public CloseNightForm(
        Func<List<CloseNightChecklist.Item>?> read,
        Action<string, bool> tick,
        Action<CloseNightKind> go,
        Func<IReadOnlyList<string>> ownLines,
        Action<List<string>> saveOwnLines,
        bool ready = false)
    {
        _read = read;
        _tick = tick;
        _go = go;
        _ownLines = ownLines;
        _saveOwnLines = saveOwnLines;
        _ready = ready;

        Text = ready ? "Ready to Race" : "Close the Night";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        _lblHeadline.AutoSize = true;
        _lblHeadline.Font = AppTheme.HeadingFont;

        var lblHow = new Label
        {
            Text = ready
                ? "Ticks itself as the app sees each one done. Sorted something it can't see - "
                  + "the meeting held, a car checked on the bench? Tick it off by hand."
                : "Ticks itself as the app sees each one done. Dealt with something it can't see - a rental handed back, "
                  + "an entry paid another way? Tick it off by hand.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
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

        _lblPace.AutoSize = true;
        _lblPace.MaximumSize = new Size(520, 0);
        _lblPace.Visible = false;

        // 18 Nov 2026, the dialog version of the Venues look: one card, the
        // headline its heading (green once everything is done), the night's
        // pace and the hint line under it, then the list; the club's own
        // lines on the left at the foot and Close on the right. No caption
        // over the headline, which says what the card is: at 1280x720, where
        // the window is as tall as the screen allows, the list keeps the
        // height it had. The window is 40px taller for a bigger screen.
        scroller.BackColor = AppTheme.Surface;
        scroller.Tag = AppTheme.KeepBackColor;
        _rows.BackColor = AppTheme.Surface;
        _rows.Tag = AppTheme.KeepBackColor;

        var card = new Section();
        card.Add(_lblHeadline, stretch: false, gapBelow: UiLayout.HintGap);
        card.Add(_lblPace, stretch: false, gapBelow: UiLayout.HintGap);
        card.Add(lblHow, gapBelow: UiLayout.RowGap);
        card.AddFill(scroller, () => 240, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);

        // Tab still goes Close, then Your Own Lines, as it did when the two
        // shared one bar.
        var ownBar = new ButtonBar().With(btnOwn);
        var closeBar = new ButtonBar(alignRight: true).With(btnClose);
        page.Add(new SplitBar(ownBar, closeBar), gapBelow: 0);
        closeBar.TabIndex = 0;
        ownBar.TabIndex = 1;

        Controls.Add(page);
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 640, height: 640);

        _timer.Tick += (_, _) => Reload();
        Shown += (_, _) => _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();

        Reload();
    }

    /// <summary>
    /// Reads the list once more as the window loads, before it is sized and
    /// shown.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        // WHY (23 Nov 2026, bug hunt 24): the constructor's Reload runs before
        // the caller's object initializer sets PaceLine, so the night's pace
        // line was left empty and hidden, and only popped in at the first
        // two-second tick, pushing the hint and the list down while the
        // director was reading them. Read again here, by when PaceLine is set,
        // and before base.OnLoad so DialogFit sizes the window with the line
        // in it. An unchanged list is not rebuilt.
        Reload();
        base.OnLoad(e);
    }

    /// <summary>Reads the list again and, if anything changed, redraws it.</summary>
    internal void Reload()
    {
        var pace = _ready ? string.Empty : PaceLine?.Invoke() ?? string.Empty;

        if (_lblPace.Text != pace)
        {
            _lblPace.Text = pace;
        }

        _lblPace.Visible = pace.Length > 0;

        var items = _read() ?? new List<CloseNightChecklist.Item>();
        var shown = string.Join("\n", items.Select(i => $"{i.Key}|{i.Words}|{i.SeenDone}|{i.Ticked}"));

        if (shown == _shown && _rows.Controls.Count > 0)
        {
            return;
        }

        _shown = shown;

        var left = items.Count(i => !i.Done);
        _lblHeadline.Text = _ready
            ? left == 0 ? "All set - ready to race." : $"Before the first race: {Plural.Of(left, "thing")} to do"
            : left == 0 ? "All wrapped up - lights out." : $"Before lights out: {Plural.Of(left, "thing")} to do";

        // Green once there is nothing left, so a glance across the room says so.
        _lblHeadline.ForeColor = left == 0 ? AppTheme.Success : AppTheme.TextDark;

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
        CloseNightKind.Video => "Club Website",

        // Ready to race (setup plan B3).
        CloseNightKind.Decoder => "Connection",
        CloseNightKind.PhoneServer => "Local Screens",
        CloseNightKind.Voice => "Mixer",
        CloseNightKind.Heats => "Next Steps",
        CloseNightKind.Cars => "Race Control",
        CloseNightKind.Papers => "Who's Racing",
        CloseNightKind.Meeting => "Meeting Sheet...",
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
            Text = _ready
                ? "Anything else your club does before the first race, one per line - \"Water the track\", "
                  + "\"Set out the marshal posts\". They're on the list every night, ticked off by hand."
                : "Anything else your club does before lights out, one per line - \"Lock the clubhouse\", "
                  + "\"Pack up the timing loop\". They're on the list every night, ticked off by hand.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
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

        // The box in a card, its hint line under the caption, as the list
        // that opens it is.
        var card = new Section("Your own lines");
        card.Add(lbl, gapBelow: UiLayout.RowGap);
        card.AddFill(box, () => 160, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(card, card.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        form.Controls.Add(page);
        form.CancelButton = btnCancel;

        AppTheme.ApplyRecursively(form);
        DialogFit.Keep(form, page, width: 500, height: 420);

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _saveOwnLines(CloseNightChecklist.Own(box.Lines));
            _shown = string.Empty;
            Reload();
        }
    }
}
