using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Records one car's inspection against its class checklist.
///
/// Each check has three states rather than a tickbox, because "not looked
/// at" and "looked at and failed" are different things and collapsing them
/// would make an incomplete inspection read as a clean one.
/// </summary>
public sealed class TechInspectionEditForm : Form
{
    private const string StateUnchecked = "not checked";
    private const string StatePassed = "OK";
    private const string StateFailed = "FAIL";

    private readonly ProfileStore _profiles;
    private readonly RaceClassStore _classes;

    private readonly ComboBox _cmbDriver = new();
    private readonly ComboBox _cmbClass = new();
    private readonly TextBox _txtCar = new();
    private readonly TextBox _txtInspector = new();
    private readonly TextBox _txtNotes = new();
    private readonly Panel _checksPanel = new();
    private readonly Label _lblVerdict = new();

    private readonly Dictionary<string, ComboBox> _checkControls = new();

    /// <summary>
    /// Checks this inspection recorded that are no longer on the class's
    /// checklist. Shown so they can be seen and changed - dropping them
    /// silently is how a failed car used to come back as passed.
    /// </summary>
    private readonly HashSet<string> _staleItems = new(StringComparer.Ordinal);

    /// <summary>
    /// The verdict by hand, for when there is no checklist to decide it.
    /// Before this existed an inspection with no checklist could only ever
    /// be saved as Passed - a car could not be failed at all, and editing a
    /// failed one turned it into a pass.
    /// </summary>
    private readonly Label _lblManualVerdict = new();
    private readonly ComboBox _cmbManualVerdict = new();

    /// <summary>The stored inspection as it was before this dialog, or null for a new one. Never written to.</summary>
    private readonly TechInspection? _original;

    /// <summary>
    /// What Save hands back. A COPY of the stored inspection when editing,
    /// never the stored object itself: the callers take their undo snapshot
    /// after this dialog closes, and when the dialog wrote straight onto the
    /// stored record that snapshot already held the edit, so Undo restored
    /// the edited version and did nothing.
    /// </summary>
    public TechInspection Result { get; private set; }

    public TechInspectionEditForm(TechInspection? existing, ProfileStore profiles, RaceClassStore classes,
        string? presetTransponder = null)
    {
        _profiles = profiles;
        _classes = classes;

        // presetTransponder only applies when this is genuinely a NEW
        // inspection - it pre-selects who it's for (jumping in from
        // Check-In, say) without turning the dialog into an edit of a
        // record that doesn't exist yet.
        _original = existing;
        Result = existing is null
            ? new TechInspection { Transponder = presetTransponder ?? string.Empty }
            : JsonSerializer.Deserialize<TechInspection>(JsonSerializer.Serialize(existing)) ?? existing;

        Text = existing is null ? "New Tech Inspection" : $"Edit Inspection - {Result.DriverName}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 600);
        MinimumSize = new Size(480, 520);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        BuildLayout(existing);
        AppTheme.ApplyRecursively(this);
    }

    private void BuildLayout(TechInspection? existing)
    {
        var lblDriver = new Label { Text = "Driver:", Location = new Point(16, 20), AutoSize = true };
        _cmbDriver.Location = new Point(110, 17);
        _cmbDriver.Width = 380;
        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var profile in _profiles.All.OrderBy(p => p.DriverName))
        {
            _cmbDriver.Items.Add(new DriverItem(profile));
        }
        SelectDriver(Result.Transponder);
        _cmbDriver.SelectedIndexChanged += (_, _) => OnDriverChanged();

        var lblClass = new Label { Text = "Class:", Location = new Point(16, 56), AutoSize = true };
        _cmbClass.Location = new Point(110, 53);
        _cmbClass.Width = 240;
        _cmbClass.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var raceClass in _classes.All)
        {
            _cmbClass.Items.Add(raceClass.Name);
        }
        if (!string.IsNullOrWhiteSpace(Result.ClassName))
        {
            // A class that has since been deleted is kept as a choice rather
            // than dropped: selecting a name that is not in the list selects
            // nothing, and Save then wiped the inspection's class.
            var match = _cmbClass.Items.Cast<object>()
                .OfType<string>()
                .FirstOrDefault(n => string.Equals(n, Result.ClassName, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                _cmbClass.Items.Add(Result.ClassName);
                match = Result.ClassName;
            }

            _cmbClass.SelectedItem = match;
        }
        _cmbClass.SelectedIndexChanged += (_, _) => BuildChecklist();

        var lblCar = new Label { Text = "Car:", Location = new Point(16, 92), AutoSize = true };
        _txtCar.Location = new Point(110, 89);
        _txtCar.Width = 380;
        _txtCar.Text = Result.CarModel;

        var lblChecks = new Label { Text = "Checklist", Location = new Point(16, 128), AutoSize = true, Font = AppTheme.BoldFont };

        _checksPanel.Location = new Point(16, 152);
        _checksPanel.Size = new Size(474, 230);
        _checksPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _checksPanel.AutoScroll = true;

        _lblVerdict.Location = new Point(16, 392);
        _lblVerdict.AutoSize = true;
        _lblVerdict.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _lblVerdict.Font = AppTheme.BoldFont;

        _lblManualVerdict.Text = "Result:";
        _lblManualVerdict.Location = new Point(16, 392);
        _lblManualVerdict.AutoSize = true;
        _lblManualVerdict.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

        _cmbManualVerdict.Location = new Point(110, 388);
        _cmbManualVerdict.Width = 240;
        _cmbManualVerdict.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _cmbManualVerdict.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbManualVerdict.Items.AddRange(new object[] { VerdictPassed, VerdictPassedWithNote, VerdictFailed });

        // An existing inspection keeps the result it was saved with until
        // somebody changes it here; a new one starts at a pass, as it
        // always did.
        _cmbManualVerdict.SelectedItem = Result.Result switch
        {
            TechResult.Failed => VerdictFailed,
            TechResult.PassedWithNote => VerdictPassedWithNote,
            _ => VerdictPassed,
        };

        var lblInspector = new Label { Text = "Inspector:", Location = new Point(16, 424), AutoSize = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _txtInspector.Location = new Point(110, 421);
        _txtInspector.Width = 380;
        _txtInspector.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _txtInspector.Text = Result.Inspector;

        var lblNotes = new Label { Text = "Notes:", Location = new Point(16, 456), AutoSize = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        _txtNotes.Location = new Point(110, 453);
        _txtNotes.Size = new Size(380, 80);
        _txtNotes.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Text = Result.Notes;

        var btnSave = new Button { Text = "Save", Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button { Text = "Cancel", Anchor = AnchorStyles.Bottom | AnchorStyles.Left, DialogResult = DialogResult.Cancel };

        AppTheme.LayoutButtonRow(300, 550, 8, btnSave, btnCancel);

        Controls.AddRange(new Control[]
        {
            lblDriver, _cmbDriver, lblClass, _cmbClass, lblCar, _txtCar,
            lblChecks, _checksPanel, _lblVerdict, _lblManualVerdict, _cmbManualVerdict,
            lblInspector, _txtInspector, lblNotes, _txtNotes, btnSave, btnCancel,
        });

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        BuildChecklist();
    }

    private void SelectDriver(string transponder)
    {
        for (var i = 0; i < _cmbDriver.Items.Count; i++)
        {
            if (_cmbDriver.Items[i] is DriverItem item
                && string.Equals(item.Profile.Transponder, transponder, StringComparison.OrdinalIgnoreCase))
            {
                _cmbDriver.SelectedIndex = i;
                return;
            }
        }

        if (_cmbDriver.Items.Count > 0)
        {
            _cmbDriver.SelectedIndex = 0;
        }
    }

    /// <summary>Pre-fills class and car from the driver's profile - almost always right, and still editable.</summary>
    private void OnDriverChanged()
    {
        if (_cmbDriver.SelectedItem is not DriverItem item)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(item.Profile.ClassName) && _cmbClass.Items.Contains(item.Profile.ClassName))
        {
            _cmbClass.SelectedItem = item.Profile.ClassName;
        }

        if (string.IsNullOrWhiteSpace(_txtCar.Text))
        {
            _txtCar.Text = item.Profile.CarModel;
        }
    }

    /// <summary>Rebuilds the checklist from the selected class's configured items.</summary>
    private void BuildChecklist()
    {
        // Taken out AND disposed. Controls.Clear only unparents them, so
        // every rebuild - and this rebuilds on every change of class -
        // leaked the last set of checkboxes and their labels, each holding
        // a window handle. A scrutineer working down a queue, switching
        // class per car, does this a hundred times in an evening.
        var previous = _checksPanel.Controls.Cast<Control>().ToList();

        _checksPanel.Controls.Clear();

        foreach (var child in previous)
        {
            child.Dispose();
        }

        _checkControls.Clear();
        _staleItems.Clear();

        var className = _cmbClass.SelectedItem as string;
        var raceClass = className is null
            ? null
            : _classes.All.FirstOrDefault(rc => string.Equals(rc.Name, className, StringComparison.OrdinalIgnoreCase));

        var items = (raceClass?.TechCheckItems ?? new List<string>()).ToList();

        // Checks this inspection recorded against ITS class that the class's
        // checklist no longer has. Only for the class it was recorded in -
        // under another class they would be somebody else's rules.
        if (className is not null && string.Equals(className, Result.ClassName, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var recorded in Result.ItemsFailed.Concat(Result.ItemsPassed))
            {
                if (!items.Contains(recorded) && _staleItems.Add(recorded))
                {
                    items.Add(recorded);
                }
            }
        }

        ShowManualVerdict(items.Count == 0);

        if (items.Count == 0)
        {
            _checksPanel.Controls.Add(new Label
            {
                Text = className is null
                    ? "Pick a class to see its checklist."
                    : $"{className} has no checklist set. Add one on the Classes page, or just record a pass/fail below.",
                Location = new Point(4, 4),
                AutoSize = true,
                MaximumSize = new Size(450, 0),
                ForeColor = AppTheme.TextMuted,
            });

            UpdateVerdict();
            return;
        }

        var y = 4;
        foreach (var item in items)
        {
            _checksPanel.Controls.Add(new Label
            {
                Text = _staleItems.Contains(item) ? $"{item}  (no longer on the checklist)" : item,
                Location = new Point(4, y + 4),
                AutoSize = true,
                MaximumSize = new Size(300, 0),
            });

            var combo = new ComboBox
            {
                Location = new Point(320, y),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            combo.Items.AddRange(new object[] { StateUnchecked, StatePassed, StateFailed });

            combo.SelectedItem = Result.ItemsFailed.Contains(item) ? StateFailed
                : Result.ItemsPassed.Contains(item) ? StatePassed
                : StateUnchecked;

            combo.SelectedIndexChanged += (_, _) => UpdateVerdict();

            _checksPanel.Controls.Add(combo);
            _checkControls[item] = combo;

            y += 32;
        }

        UpdateVerdict();
    }

    /// <summary>
    /// The verdict follows the checks: any failure fails the car, and an
    /// incomplete checklist is called out rather than quietly passing.
    /// </summary>
    private void UpdateVerdict()
    {
        var failed = _checkControls.Count(kv => (string?)kv.Value.SelectedItem == StateFailed);

        // A check that is no longer on the list and has been set back to
        // "not checked" has been taken off this inspection, not left to do.
        var unchecked_ = _checkControls.Count(kv => (string?)kv.Value.SelectedItem == StateUnchecked && !_staleItems.Contains(kv.Key));

        if (failed > 0)
        {
            _lblVerdict.Text = $"FAILS - {Plural.Of(failed, "check")} failed.";
            _lblVerdict.ForeColor = AppTheme.Danger;
        }
        else if (unchecked_ > 0)
        {
            _lblVerdict.Text = $"{Plural.Of(unchecked_, "check")} not looked at yet.";
            _lblVerdict.ForeColor = AppTheme.Accent;
        }
        else if (_checkControls.Count > 0)
        {
            _lblVerdict.Text = "All checks passed.";
            _lblVerdict.ForeColor = AppTheme.Success;
        }
        else
        {
            _lblVerdict.Text = string.Empty;
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (_cmbDriver.SelectedItem is not DriverItem driver)
        {
            AppDialog.Show(this, "Pick a driver.", "No driver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var passed = _checkControls.Where(kv => (string?)kv.Value.SelectedItem == StatePassed).Select(kv => kv.Key).ToList();
        var failed = _checkControls.Where(kv => (string?)kv.Value.SelectedItem == StateFailed).Select(kv => kv.Key).ToList();
        var notChecked = _checkControls.Count(kv => (string?)kv.Value.SelectedItem == StateUnchecked && !_staleItems.Contains(kv.Key));

        // A car failed BY HAND - recorded as FAILED with no check on a list
        // saying why, because its class had no checklist then. If the class
        // has gained one since, the hand-picked result is not on screen, and
        // saving a corrected note used to turn the failure into a pass on
        // the strength of a checklist nobody had filled in. Asked rather
        // than assumed: the inspector may have re-checked the car and it
        // now passes.
        var keepHandFailure = false;

        if (_checkControls.Count > 0
            && failed.Count == 0
            && _original is { Result: TechResult.Failed, ItemsFailed.Count: 0 })
        {
            var keepAnswer = AppDialog.Show(this,
                "This car was recorded as FAILED, but no check on the list is marked FAIL.\r\n\r\n"
                + "Yes  - keep it FAILED\r\n"
                + "No   - record the result of the checklist instead\r\n"
                + "Cancel - go back to the checklist",
                "Car was failed", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (keepAnswer == DialogResult.Cancel)
            {
                return;
            }

            keepHandFailure = keepAnswer == DialogResult.Yes;
        }

        TechResult verdict;
        if (_checkControls.Count == 0)
        {
            // No checklist, so nothing on it can decide: the result is the
            // one picked by hand, which on an edit starts as the stored one.
            verdict = _cmbManualVerdict.SelectedItem switch
            {
                VerdictFailed => TechResult.Failed,
                VerdictPassedWithNote => TechResult.PassedWithNote,
                _ => TechResult.Passed,
            };
        }
        else if (failed.Count > 0)
        {
            verdict = TechResult.Failed;
        }
        else if (keepHandFailure)
        {
            verdict = TechResult.Failed;
        }
        else if (notChecked > 0)
        {
            // An incomplete checklist shouldn't silently become a clean
            // pass - confirm what's intended rather than guessing.
            var answer = AppDialog.Show(this,
                $"{Plural.Of(notChecked, "check")} {(notChecked == 1 ? "hasn't" : "haven't")} been looked at.\r\n\r\n"
                + "Yes  - record as passed with a note\r\n"
                + "No   - record as a full pass anyway\r\n"
                + "Cancel - go back and finish the checklist",
                "Incomplete checklist", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel)
            {
                return;
            }

            verdict = answer == DialogResult.Yes ? TechResult.PassedWithNote : TechResult.Passed;
        }
        else
        {
            verdict = TechResult.Passed;
        }

        var className = _cmbClass.SelectedItem as string ?? string.Empty;

        // The time is when the car was LOOKED AT. Correcting a note, the
        // inspector's name or a typo in the car is not a re-inspection, and
        // re-stamping it moved an old inspection onto today - which is what
        // decides whether a car counts as inspected at all.
        var reinspected = _original is null
            || verdict != _original.Result
            || !new HashSet<string>(passed, StringComparer.Ordinal).SetEquals(_original.ItemsPassed)
            || !new HashSet<string>(failed, StringComparer.Ordinal).SetEquals(_original.ItemsFailed)
            || !string.Equals(driver.Profile.Transponder, _original.Transponder, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(className, _original.ClassName, StringComparison.OrdinalIgnoreCase);

        Result.Transponder = driver.Profile.Transponder;
        Result.DriverName = driver.Profile.DriverName;
        Result.ClassName = className;
        Result.CarModel = _txtCar.Text.Trim();
        Result.Inspector = _txtInspector.Text.Trim();
        Result.Notes = _txtNotes.Text;
        Result.ItemsPassed = passed;
        Result.ItemsFailed = failed;
        Result.Result = verdict;

        if (reinspected)
        {
            Result.InspectedAtUtc = DateTime.UtcNow;

            // A re-inspection happens at the race day open NOW, not the one
            // the car was first looked at under. Keeping the old event left
            // a car inspected before the race day was opened (filed under
            // "no race day") uncountable however many times it was edited:
            // the tech warning only reads the current race day. Emptied
            // rather than set here because this dialog does not know the
            // current race day - the store's Upsert stamps it on the way in.
            Result.EventId = string.Empty;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private const string VerdictPassed = "Passed";
    private const string VerdictPassedWithNote = "Passed with a note";
    private const string VerdictFailed = "FAILED";

    /// <summary>The hand-picked result shows only when there is no checklist to decide it - two verdicts on one screen would be one too many.</summary>
    private void ShowManualVerdict(bool show)
    {
        _lblManualVerdict.Visible = show;
        _cmbManualVerdict.Visible = show;
        _lblVerdict.Visible = !show;
    }

    private sealed record DriverItem(DriverProfile Profile)
    {
        public override string ToString() =>
            string.IsNullOrWhiteSpace(Profile.DriverName)
                ? Profile.Transponder
                : $"{Profile.DriverName}   [{Profile.Transponder}]";
    }
}
