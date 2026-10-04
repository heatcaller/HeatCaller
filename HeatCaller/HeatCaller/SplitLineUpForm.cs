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
///
/// A split transponder nobody has yet - the usual case the first time, with
/// the car's second transponder just fitted - is paired with the nearest
/// line pass by anybody. When every such pairing names the same split
/// transponder and the same driver, at least <see cref="LeastToAdd"/>
/// times, that is <see cref="NewSplit"/>: those gaps count, and Line Up
/// offers to add it to that driver. Mixed up (two unknown transponders, or
/// one paired with two drivers) and they don't count.
/// </summary>
public sealed class LineUpPairing
{
    /// <summary>The furthest apart the two readings of one crossing can be.</summary>
    public const long Window = 1500;

    /// <summary>Gaps needed before there is an answer.</summary>
    public const int Least = 3;

    /// <summary>Pairings needed before an unknown split transponder is taken as a driver's.</summary>
    public const int LeastToAdd = 2;

    /// <summary>One crossing found: whose, the gap, and the split transponder when it is nobody's yet.</summary>
    public sealed record Gap(string Driver, long GapMs, string? NewSplitId);

    /// <summary>A pass waiting for the other loop's. Driver is null for a split transponder nobody has.</summary>
    private sealed record Waiting(string? Driver, long Ms, string? SplitId);

    private readonly List<Waiting> _line = new();
    private readonly List<Waiting> _split = new();
    private readonly List<Gap> _gaps = new();

    /// <summary>Every gap found so far, oldest first: positive when the split loop read the car later than the line.</summary>
    public IReadOnlyList<Gap> Gaps => _gaps;

    /// <summary>
    /// The split transponder nobody has that has been paired with one
    /// driver's line passes, and only theirs - at least
    /// <see cref="LeastToAdd"/> times. Null when there is none, or it is
    /// mixed up.
    /// </summary>
    public (string SplitId, string Driver)? NewSplit
    {
        get
        {
            var unknown = _gaps.Where(g => g.NewSplitId is not null).ToList();

            if (unknown.Count < LeastToAdd)
            {
                return null;
            }

            var ids = unknown.Select(g => g.NewSplitId!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var drivers = unknown.Select(g => g.Driver).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            return ids.Count == 1 && drivers.Count == 1 ? (ids[0], drivers[0]) : null;
        }
    }

    /// <summary>The gaps the answer is taken from: every known split's, and an unknown one's only when it is <see cref="NewSplit"/>.</summary>
    public IReadOnlyList<Gap> Counted
    {
        get
        {
            var added = NewSplit;

            return _gaps
                .Where(g => g.NewSplitId is null
                            || added is { } a && string.Equals(g.NewSplitId, a.SplitId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>The median of the <see cref="Counted"/> gaps, rounded to a millisecond, or null with fewer than <see cref="Least"/>.</summary>
    public int? Answer
    {
        get
        {
            var counted = Counted;

            if (counted.Count < Least)
            {
                return null;
            }

            var sorted = counted.Select(g => g.GapMs).OrderBy(ms => ms).ToList();
            var middle = sorted.Count / 2;

            var median = sorted.Count % 2 == 1
                ? sorted[middle]
                : (sorted[middle - 1] + sorted[middle]) / 2.0;

            return (int)Math.Round((double)median, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>A line pass by this driver at this time on the line's clock. Returns the gap when it paired.</summary>
    public long? AddLine(string driver, long lineMs)
    {
        Forget(_split, lineMs);

        // Their own split transponder, or one nobody has.
        var match = Nearest(_split, lineMs, p => p.Driver is null || Same(p.Driver, driver));

        if (match is null)
        {
            _line.Add(new Waiting(driver, lineMs, null));
            return null;
        }

        _split.Remove(match);
        return Found(driver, match.Ms - lineMs, match.SplitId);
    }

    /// <summary>A split pass by this driver at this time on the line's clock (before calibration). Returns the gap when it paired.</summary>
    public long? AddSplit(string driver, long lineMs)
    {
        Forget(_line, lineMs);

        var match = Nearest(_line, lineMs, p => Same(p.Driver, driver));

        if (match is null)
        {
            _split.Add(new Waiting(driver, lineMs, null));
            return null;
        }

        _line.Remove(match);
        return Found(driver, lineMs - match.Ms, null);
    }

    /// <summary>
    /// A split pass by a split transponder nobody has, at this time on the
    /// line's clock (before calibration) - paired with anybody's line pass.
    /// Returns the gap when it paired.
    /// </summary>
    public long? AddUnknownSplit(string splitId, long lineMs)
    {
        Forget(_line, lineMs);

        var match = Nearest(_line, lineMs, _ => true);

        if (match?.Driver is not { } driver)
        {
            _split.Add(new Waiting(null, lineMs, splitId));
            return null;
        }

        _line.Remove(match);
        return Found(driver, lineMs - match.Ms, splitId);
    }

    private long Found(string driver, long gap, string? newSplitId)
    {
        _gaps.Add(new Gap(driver, gap, newSplitId));
        return gap;
    }

    /// <summary>Forgets what is too old to pair with anything any more.</summary>
    private static void Forget(List<Waiting> waiting, long ms) =>
        waiting.RemoveAll(p => Math.Abs(ms - p.Ms) > Window * 4);

    private static Waiting? Nearest(List<Waiting> waiting, long ms, Func<Waiting, bool> fits) =>
        waiting
            .Where(p => fits(p) && Math.Abs(ms - p.Ms) <= Window)
            .OrderBy(p => Math.Abs(ms - p.Ms))
            .FirstOrDefault();

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// A split transponder nobody had, to add to this driver (their main
    /// transponder) - the one the crossings were measured with. Set by
    /// Save only.
    /// </summary>
    public (string SplitId, string Driver)? AddSplit { get; private set; }

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
                   + "If the car's split transponder isn't added to its driver yet, Save adds it. "
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
            AddSplit = _pairing.NewSplit;
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

        ShowCrossing(_pairing.AddLine(driver.Transponder, lap.DeviceTimeMs));
    }

    private void OnSplitPass(SplitPass pass)
    {
        if (pass.RawLineMs is not { } raw)
        {
            return;
        }

        // A split transponder nobody has yet - most likely the one just
        // fitted to the car being driven over: paired with whoever crossed
        // the line, and offered to them on Save.
        if (pass.Driver is null)
        {
            if (pass.Transponder.Length > 0)
            {
                ShowCrossing(_pairing.AddUnknownSplit(pass.Transponder, raw));
            }

            return;
        }

        if (_profiles.Get(pass.Driver) is not { } driver)
        {
            return;
        }

        ShowCrossing(_pairing.AddSplit(driver.Transponder, raw));
    }

    private string NameOf(string transponder) =>
        _profiles.Get(transponder) is { } driver && !string.IsNullOrWhiteSpace(driver.DriverName)
            ? driver.DriverName.Trim()
            : transponder;

    private void ShowCrossing(long? paired)
    {
        if (paired is null || IsDisposed || _pairing.Gaps.Count == 0)
        {
            return;
        }

        var gap = _pairing.Gaps[^1];
        var name = NameOf(gap.Driver);

        if (gap.NewSplitId is { } id)
        {
            name += $" (split {id}, not added yet)";
        }

        var said = gap.GapMs >= 0 ? $"{gap.GapMs} ms later" : $"{-gap.GapMs} ms earlier";

        var item = _list.Items.Add(new ListViewItem(new[] { name, said }));
        item.EnsureVisible();

        var count = _pairing.Counted.Count;

        if (_pairing.Answer is { } answer)
        {
            var adding = _pairing.NewSplit is { } added
                ? $" and add split transponder {added.SplitId} to {NameOf(added.Driver)}"
                : string.Empty;

            _lblAnswer.Text = $"{Plural.Of(count, "crossing")} so far. The split loop reads {Math.Abs(answer)} ms "
                              + $"{(answer >= 0 ? "late" : "early")} - click Save to keep it{adding}.";
            _btnSave.Enabled = true;
        }
        else
        {
            _lblAnswer.Text = $"{Plural.Of(count, "crossing")} so far - {LineUpPairing.Least} are needed.";
            _btnSave.Enabled = false;
        }
    }

    private void FitColumns()
    {
        var width = Math.Max(1, _list.ClientSize.Width);
        _list.Columns[0].Width = width * 60 / 100;
        _list.Columns[1].Width = width - _list.Columns[0].Width;
    }
}
