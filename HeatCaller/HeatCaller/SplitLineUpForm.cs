namespace HeatCaller;

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
    // Wide enough that Save, Set to Zero and Cancel stay on ONE line at the
    // normal text size: at 520 the three ran out of room, the bar wrapped,
    // and Save - added last, so furthest left - went onto a second row below
    // the fold, where a race director lining the loop up never found it.
    // Tall enough that the intro, the list and the answer do not push the
    // buttons out of view either. Held to this at every text size by the
    // layout test, now that the window is on LayoutAudit.ConvertedWindows.
    private const int DialogWidth = 620;
    private const int DialogHeight = 480;

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
        else if (count >= LineUpPairing.Least)
        {
            // Enough of them, but they don't agree - a car read twice, or
            // one crossing that only one loop saw cleanly. More settle it.
            _lblAnswer.Text = $"{Plural.Of(count, "crossing")} so far, but they don't agree yet - drive across a few more times.";
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
