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

    private readonly Panel _grid = new() { Dock = DockStyle.Fill };
    private readonly Label _lblStatus = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button _btnUndo = new() { Text = "Undo last tap", AutoSize = true };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 250 };

    /// <summary>The buttons, by transponder, so a refresh only touches what changed.</summary>
    private readonly Dictionary<string, CarButton> _buttons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The cars tapped, latest last - what Undo last tap takes back.</summary>
    private readonly Stack<string> _taps = new();

    /// <summary>The order the buttons were last laid out in; a new car on the board lays them out again.</summary>
    private string _layoutKey = string.Empty;

    public HandScoringForm(
        Func<IReadOnlyList<HandScoring.Car>> cars,
        Func<string, int, string?> lap,
        Func<bool> isOn,
        Action stop)
    {
        _cars = cars;
        _lap = lap;
        _isOn = isOn;
        _stop = stop;

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

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(UiLayout.RowGap),
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(title, 0, 0);
        top.Controls.Add(_btnUndo, 1, 0);
        top.Controls.Add(btnStop, 2, 0);
        top.Controls.Add(btnClose, 3, 0);
        top.Controls.Add(_lblStatus, 0, 1);
        top.SetColumnSpan(_lblStatus, 4);
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

    /// <summary>Brings the buttons up to date with the board: new cars, lap counts, and the race ending.</summary>
    private void RefreshCars()
    {
        if (IsDisposed)
        {
            return;
        }

        if (!_isOn())
        {
            _refresh.Stop();

            foreach (var button in _buttons.Values)
            {
                button.Enabled = false;
            }

            _btnUndo.Enabled = false;
            Say("Hand scoring is over for this race - the race has ended, or it was stopped. Correct the saved result with Edit Result on Race History.");
            return;
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

    private void Tap(string transponder, int delta)
    {
        var refusal = _lap(transponder, delta);

        if (refusal is not null)
        {
            Say(refusal);
            return;
        }

        if (delta > 0)
        {
            _taps.Push(transponder);
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

        Tap(_taps.Pop(), -1);
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
