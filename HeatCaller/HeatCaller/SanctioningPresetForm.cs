namespace HeatCaller;

/// <summary>
/// Picks a sanctioning body's rules to apply to a class.
///
/// Most of this dialog is PROSE, which is unusual for a settings screen
/// and is the point of it. The numbers a preset carries are the easy part;
/// what a race director needs before pressing Apply is where they came
/// from, how current they are, and - above all - which parts of the
/// rulebook are NOT in here. A preset that quietly leaves the points scale
/// unset is fine. A preset that leaves it unset and does not say so is how
/// a championship gets scored wrong for a season.
///
/// So the three blocks on the right are fixed furniture: source, what it
/// sets, what it does not. They are not a tooltip and they are not
/// collapsible.
/// </summary>
public sealed class SanctioningPresetForm : Form
{
    private readonly RaceClass _raceClass;
    private readonly ListBox _list = new();
    private readonly Label _lblSource = new();
    private readonly Label _lblEncodes = new();
    private readonly Label _lblCaveats = new();
    private readonly Label _lblBase = new();
    private readonly Button _btnApply = new() { Text = "Apply to This Class" };

    /// <summary>What the applied preset actually changed. Empty until OK.</summary>
    public IReadOnlyList<string> Applied { get; private set; } = new List<string>();

    /// <summary>The preset that was applied, for the caller's status line.</summary>
    public SanctioningPreset? Chosen { get; private set; }

    /// <summary>
    /// What a class that follows the app default is running now, so a
    /// preset changes only the lines it names on top of the club's own
    /// settings. A bare new RaceFormat() here put the built-in minimum lap
    /// time, staging and streak window back on such a class.
    /// </summary>
    private readonly RaceFormat _appDefault;

    public SanctioningPresetForm(RaceClass raceClass, RaceFormat? appDefault = null)
    {
        _raceClass = raceClass;
        _appDefault = appDefault ?? new RaceFormat();

        Text = $"Sanctioning preset - {(string.IsNullOrWhiteSpace(raceClass.Name) ? "class" : raceClass.Name)}";
        StartPosition = FormStartPosition.CenterParent;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        const int left = 16;
        const int listWidth = 250;
        const int proseWidth = 430;

        var proseLeft = left + listWidth + UiLayout.SectionGap;

        var lblIntro = new Label
        {
            Text = "Applies only the settings the rulebook actually states. Everything else on this class is left "
                 + "exactly as it is - a preset never fills a gap in with a plausible number.",
            Location = new Point(left, 14),
            AutoSize = true,
            MaximumSize = new Size(listWidth + UiLayout.SectionGap + proseWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.BodyFont,
        };

        var top = UiLayout.Below(lblIntro, UiLayout.RowGap);

        _list.Location = new Point(left, top);
        _list.Size = new Size(listWidth, AppTheme.BodyFont.Height * 12);
        _list.IntegralHeight = false;
        _list.Font = AppTheme.BodyFont;
        _list.BackColor = AppTheme.Surface;
        _list.SelectedIndexChanged += (_, _) => ShowDetail();

        foreach (var preset in SanctioningPresets.All)
        {
            _list.Items.Add(preset);
        }

        var y = top;

        y = AddBlock("WHERE IT CAME FROM", _lblSource, proseLeft, y, proseWidth);
        y = AddBlock("WHAT IT SETS", _lblEncodes, proseLeft, y, proseWidth);
        y = AddBlock("WHAT IT DOES NOT SET", _lblCaveats, proseLeft, y, proseWidth);

        _lblCaveats.ForeColor = AppTheme.Accent;

        _lblBase.Location = new Point(left, Math.Max(y, UiLayout.Below(_list, UiLayout.RowGap)));
        _lblBase.AutoSize = true;
        _lblBase.MaximumSize = new Size(listWidth + UiLayout.SectionGap + proseWidth, 0);
        _lblBase.Font = AppTheme.SmallFont;
        _lblBase.ForeColor = AppTheme.TextMuted;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        _btnApply.Size = new Size(UiLayout.WidthFor(_btnApply), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);

        _btnApply.Click += (_, _) => Apply();

        var exitTop = UiLayout.Below(_lblBase, UiLayout.SectionGap);
        var right = proseLeft + proseWidth;

        btnCancel.Location = new Point(right - btnCancel.Width, exitTop);
        _btnApply.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - _btnApply.Width, exitTop);

        Controls.Add(lblIntro);
        Controls.Add(_list);
        Controls.Add(_lblBase);
        Controls.Add(_btnApply);
        Controls.Add(btnCancel);

        CancelButton = btnCancel;

        ClientSize = new Size(right + left, exitTop + UiLayout.ButtonHeight + left);

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        ShowDetail();

        AppTheme.ApplyRecursively(this);
    }

    /// <summary>
    /// One heading and one wrapped paragraph, returning the y under it.
    /// Fixed heights rather than measured, because the paragraph changes
    /// with the selection and a dialog that resized itself every time
    /// somebody moved down the list would be unusable.
    /// </summary>
    private int AddBlock(string heading, Label body, int x, int y, int width)
    {
        var label = new Label
        {
            Text = AppTheme.Spaced(heading),
            Location = new Point(x, y),
            AutoSize = true,
            Font = AppTheme.MicroFont,
            ForeColor = AppTheme.Primary,
        };

        body.Location = new Point(x, UiLayout.Below(label, UiLayout.HintGap));
        body.Size = new Size(width, AppTheme.SmallFont.Height * 5);
        body.Font = AppTheme.SmallFont;
        body.ForeColor = AppTheme.TextDark;

        Controls.Add(label);
        Controls.Add(body);

        return UiLayout.Below(body, UiLayout.RowGap);
    }

    private void ShowDetail()
    {
        if (_list.SelectedItem is not SanctioningPreset preset)
        {
            _lblSource.Text = _lblEncodes.Text = _lblCaveats.Text = string.Empty;
            _btnApply.Enabled = false;
            _lblBase.Text = string.Empty;
            return;
        }

        _btnApply.Enabled = true;

        _lblSource.Text = preset.Source;
        _lblEncodes.Text = preset.Encodes;
        _lblCaveats.Text = preset.Caveats;

        // Said out loud only when it is true, because it is the one thing
        // here that is about this app rather than about the rulebook: a
        // class that has never had a format of its own FOLLOWS the app
        // default, and applying a preset that writes a format ends that.
        var writesFormat = preset.Main is not null || preset.Qualifier is not null;
        var followsDefault = _raceClass.Format is null;

        _lblBase.Text = writesFormat && followsDefault
            ? "Note: this class currently follows the app's default race format. Applying a preset that sets a "
              + "duration gives it a format of its own, so later changes to the app default will stop reaching it."
            : string.Empty;
    }

    private void Apply()
    {
        if (_list.SelectedItem is not SanctioningPreset preset)
        {
            return;
        }

        // Tried on a copy first. It used to be applied to the class itself
        // before the question was asked, so answering No still changed the
        // class - and that change was saved with the next edit to any class.
        var trial = System.Text.Json.JsonSerializer.Deserialize<RaceClass>(System.Text.Json.JsonSerializer.Serialize(_raceClass)) ?? new RaceClass();
        var changes = preset.ApplyTo(trial, _appDefault);

        if (changes.Count == 0)
        {
            AppDialog.Show(this, "That preset sets nothing this class does not already have.",
                "Nothing to change", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var summary = string.Join("\r\n", changes.Select(c => $"   - {c}"));

        if (AppDialog.Show(this,
                $"Apply {preset.Body} - {preset.Name} to \"{_raceClass.Name}\"?\r\n\r\n{summary}\r\n\r\n"
                + "Everything else on this class is left alone. This is not saved until you press Save on the "
                + "class itself.",
                "Apply preset", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        preset.ApplyTo(_raceClass, _appDefault);

        Applied = changes;
        Chosen = preset;

        DialogResult = DialogResult.OK;
        Close();
    }
}
