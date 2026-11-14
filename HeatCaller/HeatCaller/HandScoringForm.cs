namespace HeatCaller;

/// <summary>
/// The desk's hand-scoring grid (11 Nov 2026, decoder-down mode): the whole
/// window is one big button per car, and a tap - mouse, touch screen or a
/// tablet driving the laptop - is a lap. The rules are HandScoring's and the
/// lap itself is Race Control's +1 Lap (RaceControlControl.HandLap); this
/// is only the sheet the director taps on.
///
/// WHY IT LOOKS LIKE THIS. The director is watching the track, not the
/// screen, so the buttons are as big as the window allows (HandScoring.
/// GridShape), they never move (HandScoring.CarsFor), and each says only
/// what a glance needs: who, and how many laps. A tap flashes the button so
/// the eye at the edge of vision knows it landed. A right-click - or
/// "Undo last tap" - takes a lap back, because the wrong car will get
/// tapped once a night.
///
/// Modeless and maximised over Race Control: the race runs on underneath,
/// the flag keys still work from the main window, and closing this leaves
/// the mode on (Score by Hand on the Next Up bar brings it back).
/// </summary>
public sealed class HandScoringForm : Form
{
    private readonly Func<IReadOnlyList<HandScoring.Car>> _cars;
    private readonly Func<string, int, string?> _lap;
    private readonly Func<bool> _isOn;
    private readonly Action _stop;
    private readonly Func<IReadOnlyList<HandScoring.Addable>>? _addable;
    private readonly Func<string, string?>? _add;

    private readonly Panel _grid = new() { Dock = DockStyle.Fill };
    private readonly Label _lblStatus = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _btnUndo = new() { Text = "Undo last tap", AutoSize = true };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 250 };

    /// <summary>The buttons, by transponder, so a refresh only touches what changed.</summary>
    private readonly Dictionary<string, CarButton> _buttons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The cars tapped, latest last - what Undo last tap takes back.</summary>
    private readonly List<string> _taps = new();

    /// <summary>The order the buttons were last laid out in; a new car on the board lays them out again. Null lays them out whatever is on it.</summary>
    private string? _layoutKey = string.Empty;

    /// <summary>True once the mode has lapsed with this window still up - the race ended, or it was stopped.</summary>
    private bool _over;

    public HandScoringForm(
        Func<IReadOnlyList<HandScoring.Car>> cars,
        Func<string, int, string?> lap,
        Func<bool> isOn,
        Action stop,
        Func<IReadOnlyList<HandScoring.Addable>>? addable = null,
        Func<string, string?>? add = null)
    {
        // Seen only once painted - see DialogFit.RevealWhenPainted.
        DialogFit.RevealWhenPainted(this);

        _cars = cars;
        _lap = lap;
        _isOn = isOn;
        _stop = stop;
        _addable = addable;
        _add = add;

        Text = "Scoring by hand";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1100, 720);
        MinimumSize = new Size(520, 360);
        WindowState = FormWindowState.Maximized;
        KeyPreview = true;

        var title = new Label
        {
            Text = "SCORING BY HAND  ·  tap a car each time it crosses the line  ·  right-click takes a lap off",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = AppTheme.BoldFont,
        };

        _btnUndo.Click += (_, _) => UndoLastTap();

        var btnStop = new Button { Text = "Stop hand scoring", AutoSize = true };
        btnStop.Click += (_, _) =>
        {
            if (AppDialog.Show(this,
                    "Stop scoring by hand? Laps are counted from the loop again and the announcer calls them. The laps counted by hand stay.",
                    "Stop hand scoring", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _stop();
                Close();
            }
        };

        var btnClose = new Button { Text = "Close (keep scoring)", AutoSize = true };
        btnClose.Click += (_, _) => Close();

        // A car the decoder never saw before it died (11 Nov 2026) - on the
        // heat's roster, or any known transponder - has no button until it
        // is put on the board. See RaceControlControl.HandAddCar.
        var btnAdd = new Button { Text = HandScoring.AddCarCaption, AutoSize = true, Visible = _addable is not null && _add is not null };
        btnAdd.Click += (_, _) => AddCar();

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 5,
            RowCount = 2,
            Padding = new Padding(UiLayout.RowGap),
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(title, 0, 0);
        top.Controls.Add(btnAdd, 1, 0);
        top.Controls.Add(_btnUndo, 2, 0);
        top.Controls.Add(btnStop, 3, 0);
        top.Controls.Add(btnClose, 4, 0);
        top.Controls.Add(_lblStatus, 0, 1);
        top.SetColumnSpan(_lblStatus, 5);
        _lblStatus.Height = Pieces.LineHeight(AppTheme.SmallFont) + 4;
        _lblStatus.Font = AppTheme.SmallFont;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        Controls.Add(_grid);
        Controls.Add(top);

        _grid.Resize += (_, _) => LayOutButtons();
        _refresh.Tick += (_, _) => RefreshCars();

        AppTheme.ApplyRecursively(this);

        // Ctrl+Z takes back the last tap here, as the app's Undo would.
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.Z)
            {
                UndoLastTap();
                e.Handled = true;
            }
        };

        Shown += (_, _) =>
        {
            RefreshCars();
            _refresh.Start();
        };

        FormClosed += (_, _) =>
        {
            _refresh.Stop();
            _refresh.Dispose();
        };

        Say("Every tap is a lap counted by hand - it shows as \"12*\" in the result.");
    }

    private void Say(string text) => _lblStatus.Text = text;

    /// <summary>
    /// Add a Car...: pick a car the decoder never saw - this heat's roster
    /// first - and it gets a button with no laps, ready to be tapped.
    /// </summary>
    private void AddCar()
    {
        if (_addable is null || _add is null)
        {
            return;
        }

        var choices = _addable();

        if (choices.Count == 0)
        {
            Say("Every car on this heat's roster, and every known transponder, is on the grid already.");
            return;
        }

        using var picker = new Form
        {
            Text = "Add a car to the grid",
            Font = AppTheme.BodyFont,
            BackColor = AppTheme.Background,
            ForeColor = AppTheme.TextDark,
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(460, 520),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
        };

        var hint = new Label
        {
            Text = "A car that never crossed the line before the decoder stopped. It goes on the grid with no laps; tap it each time it crosses.",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = Pieces.LineHeight(AppTheme.BodyFont) * 3,
            Padding = new Padding(UiLayout.RowGap),
        };

        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };

        foreach (var choice in choices)
        {
            list.Items.Add(choice.OnRoster ? $"{choice.Name}  (in this heat)" : $"{choice.Name}  ({choice.Transponder})");
        }

        list.SelectedIndex = 0;

        var ok = new Button { Text = "Add", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(UiLayout.RowGap) };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        list.DoubleClick += (_, _) => picker.DialogResult = DialogResult.OK;

        picker.Controls.Add(list);
        picker.Controls.Add(hint);
        picker.Controls.Add(buttons);
        picker.AcceptButton = ok;
        picker.CancelButton = cancel;
        AppTheme.ApplyRecursively(picker);

        if (picker.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0)
        {
            return;
        }

        var chosen = choices[list.SelectedIndex];

        if (_add(chosen.Transponder) is { } refusal)
        {
            Say(refusal);
            return;
        }

        Say($"{chosen.Name} is on the grid with no laps - tap them each time they cross.");
        RefreshCars();
    }

    /// <summary>Brings the buttons up to date with the board: new cars, lap counts, and the race ending.</summary>
    private void RefreshCars()
    {
        if (IsDisposed)
        {
            return;
        }

        if (!_isOn())
        {
            // The timer is left running: the window is often still up when
            // the mode is switched on again - the next race with the decoder
            // still down - and it has to come back to life then. Stopped
            // here, it sat greyed out showing the last race's cars and
            // counted nothing.
            if (_over)
            {
                return;
            }

            _over = true;

            foreach (var button in _buttons.Values)
            {
                button.Enabled = false;
            }

            _btnUndo.Enabled = false;
            Say("Hand scoring is over for this race - the race has ended, or it was stopped. Correct the saved result with Edit Result on Race History.");
            return;
        }

        if (_over)
        {
            // On again. Fresh buttons - the old ones are greyed out, and may
            // be another race's cars - and the last race's taps are not this
            // one's to undo.
            _over = false;
            _taps.Clear();
            _layoutKey = null;
            Say("Every tap is a lap counted by hand - it shows as \"12*\" in the result.");
        }

        var cars = _cars();
        var key = string.Join("|", cars.Select(c => c.Transponder));

        if (!string.Equals(key, _layoutKey, StringComparison.Ordinal))
        {
            Rebuild(cars);
        }

        foreach (var car in cars)
        {
            if (_buttons.TryGetValue(car.Transponder, out var button))
            {
                button.SetCar(car);
            }
        }

        _btnUndo.Enabled = _taps.Count > 0;
    }

    /// <summary>One button per car, in HandScoring's order.</summary>
    private void Rebuild(IReadOnlyList<HandScoring.Car> cars)
    {
        _grid.SuspendLayout();

        foreach (var old in _buttons.Values)
        {
            _grid.Controls.Remove(old);
            old.Dispose();
        }

        _buttons.Clear();

        foreach (var car in cars)
        {
            var button = new CarButton(car.Transponder);
            button.MouseUp += (_, e) => Tap(button.Transponder, e.Button == MouseButtons.Right ? -1 : 1);
            _buttons[car.Transponder] = button;
            _grid.Controls.Add(button);
        }

        _layoutKey = string.Join("|", cars.Select(c => c.Transponder));
        _grid.ResumeLayout();

        LayOutButtons();

        if (cars.Count == 0)
        {
            Say("No car is on the board yet. A car is added here once it has crossed the line in this race.");
        }
    }

    /// <summary>The buttons in the grid HandScoring.GridShape picks for the window's size.</summary>
    private void LayOutButtons()
    {
        if (_buttons.Count == 0)
        {
            return;
        }

        const int gap = 8;
        var area = _grid.ClientRectangle;
        var (columns, rows) = HandScoring.GridShape(_buttons.Count, area.Width, area.Height);

        var width = Math.Max(40, (area.Width - gap * (columns + 1)) / columns);
        var height = Math.Max(30, (area.Height - gap * (rows + 1)) / rows);

        var i = 0;

        foreach (var button in _buttons.Values)
        {
            var column = i % columns;
            var row = i / columns;

            button.Bounds = new Rectangle(gap + column * (width + gap), gap + row * (height + gap), width, height);
            i++;
        }
    }

    private void Tap(string transponder, int delta, bool undo = false)
    {
        var refusal = _lap(transponder, delta);

        if (refusal is not null)
        {
            Say(refusal);
            return;
        }

        if (delta > 0)
        {
            _taps.Add(transponder);
        }
        else if (!undo)
        {
            // A right-click has taken this car's latest tap back, so it is
            // not there for Undo last tap to take back a second time.
            var latest = _taps.FindLastIndex(t => string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase));

            if (latest >= 0)
            {
                _taps.RemoveAt(latest);
            }
        }

        if (_buttons.TryGetValue(transponder, out var button))
        {
            button.Flash(delta > 0);
        }

        Say(delta > 0 ? "Counted." : "A lap taken off.");
        RefreshCars();
    }

    private void UndoLastTap()
    {
        if (_taps.Count == 0)
        {
            return;
        }

        var last = _taps[^1];
        _taps.RemoveAt(_taps.Count - 1);

        Tap(last, -1, undo: true);
    }

    /// <summary>One car: its name big, its laps under it, drawn to fill the button.</summary>
    private sealed class CarButton : Control
    {
        private HandScoring.Car? _car;
        private long _flashUntil;
        private bool _flashAdded;
        private readonly System.Windows.Forms.Timer _flashTimer = new() { Interval = 60 };

        public CarButton(string transponder)
        {
            Transponder = transponder;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
            _flashTimer.Tick += (_, _) =>
            {
                if (Environment.TickCount64 >= _flashUntil)
                {
                    _flashTimer.Stop();
                }

                Invalidate();
            };
        }

        public string Transponder { get; }

        public void SetCar(HandScoring.Car car)
        {
            if (_car == car)
            {
                return;
            }

            _car = car;
            AccessibleName = $"{car.Name}, {Plural.Of(car.Laps, "lap")}";
            Invalidate();
        }

        public void Flash(bool added)
        {
            _flashAdded = added;
            _flashUntil = Environment.TickCount64 + 350;
            _flashTimer.Start();
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var flashing = Environment.TickCount64 < _flashUntil;
            var back = !Enabled ? AppTheme.Border
                : flashing ? (_flashAdded ? AppTheme.Success : AppTheme.Warning)
                : AppTheme.Primary;
            var ink = !Enabled ? AppTheme.TextMuted
                : flashing ? (_flashAdded ? AppTheme.OnSuccess : AppTheme.OnWarning)
                : AppTheme.OnPrimary;

            using (var brush = new SolidBrush(back))
            {
                g.FillRectangle(brush, ClientRectangle);
            }

            if (_car is not { } car)
            {
                return;
            }

            // The name on the top half, the laps - bigger - under it.
            var nameBox = new Rectangle(6, 4, Width - 12, Height * 2 / 5);
            var lapsBox = new Rectangle(6, nameBox.Bottom, Width - 12, Height - nameBox.Bottom - 4);

            DrawFitted(g, car.Name, nameBox, ink, 0.55f);
            DrawFitted(g, car.LapsText, lapsBox, ink, 0.75f);
        }

        /// <summary>Text as big as fits the box, centred, never over <paramref name="ofHeight"/> of the box's height.</summary>
        private static void DrawFitted(Graphics g, string text, Rectangle box, Color ink, float ofHeight)
        {
            if (box.Width <= 4 || box.Height <= 4 || string.IsNullOrEmpty(text))
            {
                return;
            }

            var size = Math.Max(6f, box.Height * ofHeight);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

            for (var tries = 0; tries < 12; tries++)
            {
                using var font = new Font(AppTheme.DisplayFamily, size, FontStyle.Bold, GraphicsUnit.Pixel);
                var measured = TextRenderer.MeasureText(g, text, font, box.Size, flags);

                if (measured.Width <= box.Width || size <= 8f)
                {
                    TextRenderer.DrawText(g, text, font, box, ink, flags | TextFormatFlags.EndEllipsis);
                    return;
                }

                size = Math.Max(8f, size * box.Width / (float)measured.Width * 0.95f);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _flashTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
