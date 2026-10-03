namespace HeatCaller;

/// <summary>
/// Pairs the line's passes with the split loop's for Line Up: with both
/// loops at the same spot, one car crossing is read by both, and the gap
/// between the two readings is how late the split loop reads (its decoder's
/// own delay, the serial line's, and whatever the clock tie leaves).
///
/// A pass is paired with the other loop's nearest unpaired pass by the same
/// driver within <see cref="Window"/>, whichever arrives first. The answer
/// is the median gap, so one odd reading can't move it.
/// </summary>
public sealed class LineUpPairing
{
    /// <summary>The furthest apart the two readings of one crossing can be.</summary>
    public const long Window = 1500;

    /// <summary>Gaps needed before there is an answer.</summary>
    public const int Least = 3;

    private readonly List<(string Driver, long Ms)> _line = new();
    private readonly List<(string Driver, long Ms)> _split = new();
    private readonly List<(string Driver, long GapMs)> _gaps = new();

    /// <summary>Every gap found so far, oldest first: positive when the split loop read the car later than the line.</summary>
    public IReadOnlyList<(string Driver, long GapMs)> Gaps => _gaps;

    /// <summary>The median gap, rounded to a millisecond, or null with fewer than <see cref="Least"/>.</summary>
    public int? Answer
    {
        get
        {
            if (_gaps.Count < Least)
            {
                return null;
            }

            var sorted = _gaps.Select(g => g.GapMs).OrderBy(ms => ms).ToList();
            var middle = sorted.Count / 2;

            var median = sorted.Count % 2 == 1
                ? sorted[middle]
                : (sorted[middle - 1] + sorted[middle]) / 2.0;

            return (int)Math.Round((double)median, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>A line pass by this driver at this time on the line's clock. Returns the gap when it paired.</summary>
    public long? AddLine(string driver, long lineMs) => Add(driver, lineMs, isLine: true);

    /// <summary>A split pass by this driver at this time on the line's clock (before calibration). Returns the gap when it paired.</summary>
    public long? AddSplit(string driver, long lineMs) => Add(driver, lineMs, isLine: false);

    private long? Add(string driver, long ms, bool isLine)
    {
        var mine = isLine ? _line : _split;
        var theirs = isLine ? _split : _line;

        // Forget what is too old to pair with anything any more.
        theirs.RemoveAll(p => Math.Abs(ms - p.Ms) > Window * 4);

        var match = theirs
            .Where(p => string.Equals(p.Driver, driver, StringComparison.OrdinalIgnoreCase) && Math.Abs(ms - p.Ms) <= Window)
            .OrderBy(p => Math.Abs(ms - p.Ms))
            .Select(p => ((string Driver, long Ms)?)p)
            .FirstOrDefault();

        if (match is not { } other)
        {
            mine.Add((driver, ms));
            return null;
        }

        theirs.Remove(other);

        var gap = isLine ? other.Ms - ms : ms - other.Ms;
        _gaps.Add((driver, gap));
        return gap;
    }
}

/// <summary>
/// Line Up, from the Connection page: measures how much later the split
/// loop reads a car than the line, so split times can be corrected for it.
///
/// Both loops go side by side at the same spot - the split loop's antenna
/// or sensor laid right next to the line's - and one car with both its
/// transponders fitted is driven across them a few times. Each crossing
/// both read is listed with its gap; Save keeps the median gap
/// (AppSettings.SplitLoopOffsetMs), which is then taken off every split
/// time. Then the split loop goes back to its place round the track.
/// </summary>
public sealed class SplitLineUpForm : Form
{
    private const int DialogWidth = 520;
    private const int DialogHeight = 420;

    private readonly SplitLoop _splitLoop;
    private readonly ProfileStore _profiles;
    private readonly Action<Action<DirectLapEvent>> _unsubscribe;
    private readonly LineUpPairing _pairing = new();
    private readonly ListView _list = new();
    private readonly Label _lblAnswer = new() { AutoSize = true };
    private readonly Button _btnSave = new() { Text = "Save" };

    /// <summary>What to save: the measured gap, or 0 for Set to Zero - null when nothing was saved.</summary>
    public int? OffsetMs { get; private set; }

    public SplitLineUpForm(
        SplitLoop splitLoop,
        ProfileStore profiles,
        int currentOffsetMs,
        Action<Action<DirectLapEvent>> subscribeToLine,
        Action<Action<DirectLapEvent>> unsubscribeFromLine)
    {
        _splitLoop = splitLoop;
        _profiles = profiles;
        _unsubscribe = unsubscribeFromLine;

        Text = "Line Up the Split Loop";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var intro = new Label
        {
            Text = "Lay the split loop right beside the start/finish line, at the same spot. Then drive a car with both of its "
                   + "transponders fitted across them, five or six times. Each crossing both loops read is listed below with how "
                   + "much later the split loop read it. Save keeps the middle one, and it's taken off every split time. "
                   + "Then put the split loop back in its place round the track.",
            AutoSize = true,
        };

        var current = new Label
        {
            Text = currentOffsetMs == 0
                ? "Not lined up yet: split times aren't corrected."
                : $"Lined up now: the split loop reads {currentOffsetMs} ms late.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _list.Columns.Add("Driver");
        _list.Columns.Add("Split read it");
        _list.ClientSizeChanged += (_, _) => FitColumns();

        _lblAnswer.Text = $"Waiting for crossings - {LineUpPairing.Least} are needed.";

        _btnSave.Enabled = false;
        _btnSave.Click += (_, _) =>
        {
            if (_pairing.Answer is not { } answer)
            {
                return;
            }

            OffsetMs = answer;
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnZero = new Button { Text = "Set to Zero" };
        btnZero.Click += (_, _) =>
        {
            OffsetMs = 0;
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        page.Add(intro, gapBelow: UiLayout.RowGap);
        page.Add(current, gapBelow: UiLayout.RowGap);
        page.AddFill(_list, _ => UiLayout.AtTextSize(120), gapBelow: UiLayout.RowGap);
        page.Add(_lblAnswer, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnZero, _btnSave), gapBelow: 0);
        _btnSave.TabIndex = 0;

        Controls.Add(page);
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(_btnSave, AppTheme.ButtonKind.Primary);
        current.ForeColor = AppTheme.TextMuted;

        DialogFit.Keep(this, page, DialogWidth, DialogHeight);

        subscribeToLine(OnLinePass);
        _splitLoop.PassRecorded += OnSplitPass;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _unsubscribe(OnLinePass);
        _splitLoop.PassRecorded -= OnSplitPass;
        base.OnFormClosed(e);
    }

    private void OnLinePass(DirectLapEvent lap)
    {
        if (lap.IsSynthetic || _profiles.Get(lap.TransponderId) is not { } driver)
        {
            return;
        }

        Show(driver, _pairing.AddLine(driver.Transponder, lap.DeviceTimeMs));
    }

    private void OnSplitPass(SplitPass pass)
    {
        if (pass.Driver is null || pass.RawLineMs is not { } raw || _profiles.Get(pass.Driver) is not { } driver)
        {
            return;
        }

        Show(driver, _pairing.AddSplit(driver.Transponder, raw));
    }

    private void Show(DriverProfile driver, long? gap)
    {
        if (gap is not { } ms || IsDisposed)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(driver.DriverName) ? driver.Transponder : driver.DriverName.Trim();
        var said = ms >= 0 ? $"{ms} ms later" : $"{-ms} ms earlier";

        var item = _list.Items.Add(new ListViewItem(new[] { name, said }));
        item.EnsureVisible();

        var count = _pairing.Gaps.Count;

        if (_pairing.Answer is { } answer)
        {
            _lblAnswer.Text = $"{Plural.Of(count, "crossing")} so far. The split loop reads {answer} ms "
                              + $"{(answer >= 0 ? "late" : "early")} - click Save to keep it.";
            _btnSave.Enabled = true;
        }
        else
        {
            _lblAnswer.Text = $"{Plural.Of(count, "crossing")} so far - {LineUpPairing.Least} are needed.";
        }
    }

    private void FitColumns()
    {
        var width = Math.Max(1, _list.ClientSize.Width);
        _list.Columns[0].Width = width * 60 / 100;
        _list.Columns[1].Width = width - _list.Columns[0].Width;
    }
}
