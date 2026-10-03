namespace HeatCaller;

/// <summary>
/// Transponders that crossed the line with no driver, waiting for someone
/// to say whose they are - the list behind the banner on Race Control and
/// Driver Profiles, and the count on the sidebar.
///
/// It used to be a question asked with a modal box as each session ended,
/// one transponder at a time, which only ever offered to make a NEW driver.
/// A list that waits means nothing pops up over the race desk, the
/// operator answers when it suits, and each one can be given to a driver
/// already on file, made a new driver, or registered as a club rental.
///
/// This session only: a transponder still unnamed at the next start turns
/// up again the first time it crosses the line.
///
/// Thread-safe: noted from the lap pipeline's side, read by the UI.
/// Changed may be raised on any thread.
/// </summary>
public sealed class UnknownTransponders
{
    /// <summary>What can be done with one - the banner's three buttons.</summary>
    public enum Choice
    {
        BelongsTo,
        NewDriver,
        ClubRental,
    }

    /// <summary>One unknown transponder: where it was first seen and, once a session has ended, what it did there.</summary>
    public sealed record Sighting(string Id, string Where, int Laps, long BestLapMs);

    private readonly object _lock = new();
    private readonly List<Sighting> _pending = new();
    private bool _snoozed;

    /// <summary>Raised whenever the list or the snooze changes. May come from any thread.</summary>
    public event Action? Changed;

    /// <summary>Raised when a banner button is pressed - MainForm does the work, as it has the stores.</summary>
    public event Action<string, Choice>? Requested;

    /// <summary>The transponders waiting, oldest first.</summary>
    public IReadOnlyList<Sighting> Pending
    {
        get
        {
            lock (_lock)
            {
                return _pending.ToList();
            }
        }
    }

    /// <summary>
    /// True after Later, until another unknown transponder turns up: the
    /// banner stays out of the way, the sidebar count stays.
    /// </summary>
    public bool Snoozed
    {
        get
        {
            lock (_lock)
            {
                return _snoozed;
            }
        }
    }

    /// <summary>
    /// A transponder nobody has crossed the line. Listed once; a new one
    /// brings the banner back after Later.
    /// </summary>
    public void Note(string transponder, string where)
    {
        var id = transponder?.Trim() ?? string.Empty;

        if (id.Length == 0)
        {
            return;
        }

        lock (_lock)
        {
            if (_pending.Any(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            _pending.Add(new Sighting(id, where?.Trim() ?? string.Empty, 0, 0));
            _snoozed = false;
        }

        Changed?.Invoke();
    }

    /// <summary>What the transponder did in the session it was seen in - set as that session ends.</summary>
    public void SetLaps(string transponder, int laps, long bestLapMs)
    {
        var changed = false;

        lock (_lock)
        {
            var index = _pending.FindIndex(s => string.Equals(s.Id, transponder?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (index >= 0 && (_pending[index].Laps != laps || _pending[index].BestLapMs != bestLapMs))
            {
                _pending[index] = _pending[index] with { Laps = laps, BestLapMs = bestLapMs };
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Takes a transponder off the list - it has a driver now.</summary>
    public void Resolve(string transponder)
    {
        bool removed;

        lock (_lock)
        {
            removed = _pending.RemoveAll(s => string.Equals(s.Id, transponder?.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;
        }

        if (removed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Drops every transponder that has a driver now, however it got one -
    /// a profile added by hand, a phone sign-up, an import.
    /// </summary>
    public void DropKnown(Func<string, bool> isKnown)
    {
        bool removed;

        lock (_lock)
        {
            removed = _pending.RemoveAll(s => isKnown(s.Id)) > 0;
        }

        if (removed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Later: hides the banner until another unknown transponder turns up.</summary>
    public void Snooze()
    {
        lock (_lock)
        {
            if (_snoozed)
            {
                return;
            }

            _snoozed = true;
        }

        Changed?.Invoke();
    }

    /// <summary>A banner button was pressed.</summary>
    public void Request(string transponder, Choice choice) => Requested?.Invoke(transponder, choice);

    /// <summary>"14 laps in Heat 2 · best 7.905", or what is known of it.</summary>
    public static string Describe(Sighting sighting)
    {
        var where = sighting.Where.Length > 0 ? $" in {sighting.Where}" : string.Empty;

        if (sighting.Laps <= 0)
        {
            return sighting.Where.Length > 0 ? $"Seen{where}" : "Seen on track";
        }

        var laps = sighting.Laps == 1 ? "1 lap" : $"{sighting.Laps} laps";
        var best = sighting.BestLapMs > 0 ? $" · best {sighting.BestLapMs / 1000.0:0.000}" : string.Empty;
        return $"{laps}{where}{best}";
    }
}

/// <summary>
/// The banner over Race Control and Driver Profiles: one line for each
/// transponder that crossed the line with no driver, with Belongs To, New
/// Driver and Club Rental beside it, and Later to put it away until
/// another turns up.
///
/// Hidden while there is nothing to show, while snoozed, and whenever
/// <see cref="ShowWhen"/> says no (Race Control: not while a race is on).
/// </summary>
internal sealed class UnknownTransponderBanner : TableLayoutPanel
{
    private UnknownTransponders? _unknowns;

    /// <summary>An extra condition for showing - Race Control's "no race on". Null always allows.</summary>
    public Func<bool>? ShowWhen { get; set; }

    public UnknownTransponderBanner()
    {
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ColumnCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Padding = new Padding(14, 10, 14, 10);
        Visible = false;
        DoubleBuffered = true;
    }

    /// <summary>Connects the banner to the list it shows. Once.</summary>
    public void Attach(UnknownTransponders unknowns)
    {
        _unknowns = unknowns;
        unknowns.Changed += OnChanged;
        Rebuild();
    }

    /// <summary>Shows or hides it again - for when ShowWhen's answer may have changed.</summary>
    public void RefreshVisibility()
    {
        if (IsDisposed)
        {
            return;
        }

        var show = _unknowns is { Snoozed: false }
                   && _unknowns.Pending.Count > 0
                   && (ShowWhen?.Invoke() ?? true);

        // Always assigned, never compared: a control reads as hidden while
        // its page is, so comparing would leave a banner that should go
        // away still set to show when its page comes back.
        Visible = show;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _unknowns is not null)
        {
            _unknowns.Changed -= OnChanged;
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        // The warning colour round the edge, so the banner reads as
        // something to deal with on every look, light or dark.
        using var pen = new Pen(AppTheme.Warning, 2);
        e.Graphics.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
    }

    private void OnChanged()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            if (IsHandleCreated)
            {
                try
                {
                    BeginInvoke(Rebuild);
                }
                catch (InvalidOperationException)
                {
                    // The window is going away.
                }
            }

            return;
        }

        Rebuild();
    }

    /// <summary>Lays the lines out again from the list as it is now.</summary>
    private void Rebuild()
    {
        if (IsDisposed || _unknowns is null)
        {
            return;
        }

        var pending = _unknowns.Pending;

        SuspendLayout();

        try
        {
            foreach (Control old in Controls.Cast<Control>().ToList())
            {
                Controls.Remove(old);
                old.Dispose();
            }

            RowStyles.Clear();
            RowCount = 0;

            if (pending.Count > 0)
            {
                var title = new Label
                {
                    Text = pending.Count == 1
                        ? "A transponder crossed the line with no driver"
                        : $"{pending.Count} transponders crossed the line with no driver",
                    AutoSize = true,
                    Font = AppTheme.BoldFont,
                    Anchor = AnchorStyles.Left,
                };

                var later = new Button { Text = "Later" };
                later.Click += (_, _) => _unknowns.Snooze();

                AddLine(new ButtonBar(tight: true).With(title, later));

                foreach (var sighting in pending)
                {
                    var id = sighting.Id;

                    var lblId = new Label { Text = id, AutoSize = true, Font = AppTheme.BoldFont, Anchor = AnchorStyles.Left };
                    var lblWhat = new Label { Text = UnknownTransponders.Describe(sighting), AutoSize = true, Anchor = AnchorStyles.Left };

                    var belongs = new Button { Text = "Belongs To..." };
                    belongs.Click += (_, _) => _unknowns.Request(id, UnknownTransponders.Choice.BelongsTo);

                    var newDriver = new Button { Text = "New Driver" };
                    newDriver.Click += (_, _) => _unknowns.Request(id, UnknownTransponders.Choice.NewDriver);

                    var rental = new Button { Text = "Club Rental" };
                    rental.Click += (_, _) => _unknowns.Request(id, UnknownTransponders.Choice.ClubRental);

                    AddLine(new ButtonBar(tight: true).With(lblId, lblWhat, belongs, newDriver, rental));
                }

                BackColor = AppTheme.Surface;
                AppTheme.ApplyRecursively(this);

                // After the theme pass, which picks a button's colour from
                // its caption: the title in the warning colour, and Belongs
                // To - the usual answer - stands out from the other two.
                title.ForeColor = AppTheme.Warning;

                foreach (var bar in Controls.OfType<ButtonBar>())
                {
                    foreach (var button in bar.Controls.OfType<Button>())
                    {
                        AppTheme.StyleButton(button, button.Text.StartsWith("Belongs", StringComparison.Ordinal)
                            ? AppTheme.ButtonKind.Warning
                            : AppTheme.ButtonKind.Secondary);
                    }
                }
            }
        }
        finally
        {
            ResumeLayout(true);
        }

        RefreshVisibility();
        Invalidate();
    }

    private void AddLine(Control line)
    {
        line.Dock = DockStyle.Fill;
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(line, 0, RowCount);
        RowCount++;
    }
}
