namespace HeatCaller;

/// <summary>
/// Configure custom voice lines for specific driver rivalries - fires
/// whenever the two named drivers swap position in the live standings,
/// in either direction. Driver names are picked from existing driver
/// profiles rather than typed freely, since a rivalry only means anything
/// once both names resolve to an actual transponder on track.
/// </summary>
public sealed class RivalriesControl : UserControl
{
    private readonly RivalryStore _rivalries;
    private readonly ProfileStore _profiles;
    private readonly UndoService _undo;

    private readonly ListBox _list = new();
    private readonly ComboBox _cmbDriverA = new();
    private readonly ComboBox _cmbDriverB = new();
    private readonly CheckBox _chkEnabled = new();
    private readonly TextBox _txtTemplates = new();
    private readonly Button _btnRestoreLines = new();
    private readonly Label _lblHeatCallerLock = new();
    private readonly Label _lblStatus = new();

    private bool _suppressEvents;

    /// <summary>
    /// Which voice is speaking, so the lines here can be fixed while it is
    /// the HeatCaller one. A function rather than the store itself: this
    /// page has no other reason to know about voices, and the host is where
    /// the wiring belongs.
    /// </summary>
    internal Func<bool>? WordingIsFixed { get; set; }

    /// <summary>
    /// Fixes or frees the rivalry lines, whenever this page comes up -
    /// the voice can be changed on another page while this one sits there.
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            ApplyWordingLock();
        }
    }

    private void ApplyWordingLock()
    {
        var locked = WordingIsFixed?.Invoke() == true;

        _txtTemplates.ReadOnly = locked;
        _btnRestoreLines.Enabled = !locked;
        _lblHeatCallerLock.Visible = locked;
    }

    public RivalriesControl(RivalryStore rivalries, ProfileStore profiles, UndoService undo)
    {
        _rivalries = rivalries;
        _profiles = profiles;
        _undo = undo;

        Dock = DockStyle.Fill;
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshDriverCombos();
        RefreshList();
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Fires a custom line whenever these two named drivers swap position in the standings - in either direction. " +
                   "Both names must currently be assigned to a driver profile (see Driver Profiles) for a rivalry to fire.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = AppTheme.TextMuted,
        };

        var lblListCaption = new Label { Text = "Configured rivalries:", Location = new Point(12, 60), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };

        _list.Location = new Point(12, 82);
        _list.Size = new Size(260, 340);
        _list.Font = AppTheme.BodyFont;
        _list.SelectedIndexChanged += (_, _) => LoadSelectedIntoUi();

        var btnAdd = new Button { Text = "Add Rivalry", Location = new Point(12, 430), Width = 120 };
        btnAdd.Click += BtnAdd_Click;

        var btnDelete = new Button { Text = "Delete", Location = new Point(140, 430), Width = 90 };
        btnDelete.Click += BtnDelete_Click;

        var grpEdit = new GroupBox { Text = "Edit Selected Rivalry", Location = new Point(288, 60), Size = new Size(480, 400) };

        var lblA = new Label { Text = "Driver A:", Location = new Point(12, 28), AutoSize = true };
        _cmbDriverA.Location = new Point(90, 25);
        _cmbDriverA.Width = 180;
        _cmbDriverA.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriverA.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        var lblB = new Label { Text = "Driver B:", Location = new Point(12, 58), AutoSize = true };
        _cmbDriverB.Location = new Point(90, 55);
        _cmbDriverB.Width = 180;
        _cmbDriverB.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriverB.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        _chkEnabled.Text = "Enabled";
        _chkEnabled.Location = new Point(300, 28);
        _chkEnabled.AutoSize = true;
        _chkEnabled.Checked = true;
        _chkEnabled.CheckedChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        var lblTemplates = new Label
        {
            Text = "Phrasing (one line per variant - a random one is picked each time). Placeholders: {overtaker} {overtaken}",
            Location = new Point(12, 92),
            AutoSize = true,
            MaximumSize = new Size(450, 0),

            // Set here so the wrap is measured in the font this is drawn
            // in. Without it the caption wraps to two lines at the WinForms
            // default and three at the app's, and the button placed under
            // it at a fixed 126 ended up inside that third line.
            Font = AppTheme.BodyFont,
        };

        // Existing rivalries keep whatever lines they were created with, so
        // a new default set can never reach them on its own - this is the
        // only way to pick up the built-in phrasings after the fact.
        _btnRestoreLines.Text = "Use Default Lines";
        _btnRestoreLines.Width = UiLayout.WidthFor(_btnRestoreLines);
        _btnRestoreLines.Height = UiLayout.ButtonHeight;
        _btnRestoreLines.Location = new Point(12, UiLayout.Below(lblTemplates, UiLayout.RowGap));
        _btnRestoreLines.Click += (_, _) => RestoreDefaultLines();

        var lblRestoreHint = new Label
        {
            Text = "Replaces the lines below with the built-in set.",
            AutoSize = true,
            MaximumSize = new Size(292, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        lblRestoreHint.Location = new Point(
            UiLayout.After(_btnRestoreLines) + UiLayout.RowGap,
            UiLayout.CentreOn(lblRestoreHint, _btnRestoreLines.Top, _btnRestoreLines.Height));

        _txtTemplates.Multiline = true;
        _txtTemplates.AcceptsReturn = true;
        _txtTemplates.ScrollBars = ScrollBars.Vertical;
        _txtTemplates.Location = new Point(12, UiLayout.Below(_btnRestoreLines, UiLayout.RowGap));
        _txtTemplates.Size = new Size(450, 188);
        _txtTemplates.Leave += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        // These lines are spoken by the announcer, so the HeatCaller voice
        // fixes them the same way it fixes the ones on Voice Settings: that
        // account is HeatCaller's, not the club's, and words somebody typed
        // are words somebody else pays for. A club that wants its own
        // rivalry lines uses its own ElevenLabs key or the free Windows
        // voice - where they are theirs to spend - and the hint says so.
        _lblHeatCallerLock.Text =
            "Fixed while the HeatCaller voice is speaking - use your own ElevenLabs key or the Windows voice to write your own.";
        // Under the lines box, not beside the Restore button: this layout is
        // fixed pixels, and that row already holds the button and its hint.
        _lblHeatCallerLock.Location = new Point(12, UiLayout.Below(_txtTemplates, UiLayout.HintGap));
        _lblHeatCallerLock.AutoSize = true;
        _lblHeatCallerLock.MaximumSize = new Size(450, 0);
        _lblHeatCallerLock.ForeColor = AppTheme.Warning;
        _lblHeatCallerLock.Font = AppTheme.SmallFont;
        _lblHeatCallerLock.Visible = false;

        // Tall enough for whatever the measured rows above came to, rather
        // than the flat 400 they now run past.
        grpEdit.Height = Math.Max(
            grpEdit.Height,
            UiLayout.GroupHeightFor(_txtTemplates.Bottom + UiLayout.HintGap + UiLayout.HintHeight * 2));

        grpEdit.Controls.AddRange(new Control[]
        {
            lblA, _cmbDriverA, lblB, _cmbDriverB, _chkEnabled, lblTemplates,
            _btnRestoreLines, lblRestoreHint, _lblHeatCallerLock, _txtTemplates,
        });

        // Under the edit box, wherever that now ends - the box grew when
        // its rows were measured, and 468 was inside it.
        _lblStatus.Location = new Point(grpEdit.Left, UiLayout.Below(grpEdit, UiLayout.RowGap));
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        Controls.Add(lblIntro);
        Controls.Add(lblListCaption);
        Controls.Add(_list);
        Controls.Add(btnAdd);
        Controls.Add(btnDelete);
        Controls.Add(grpEdit);
        Controls.Add(_lblStatus);

        SetEditEnabled(false);
    }

    /// <summary>Repopulates the driver dropdowns from current profiles - call whenever this tab becomes visible, since profiles can be added/renamed elsewhere while this tab isn't active.</summary>
    public void RefreshDriverCombos()
    {
        // The rivalries too, from the store: this is what runs when the page
        // is shown and after an Undo, and the list used to keep showing what
        // an undo had just changed as it was before. First, so the boxes hold
        // the stored values before refilling the dropdowns saves them back.
        RefreshList(SelectedRivalry()?.Id);

        var names = _profiles.AllRaceable
            .Select(p => p.DriverName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        foreach (var combo in new[] { _cmbDriverA, _cmbDriverB })
        {
            var previous = combo.SelectedItem as string;
            combo.Items.Clear();
            foreach (var name in names)
            {
                combo.Items.Add(name);
            }
            if (previous is not null && combo.Items.Contains(previous))
            {
                combo.SelectedItem = previous;
            }
        }

        if (names.Count == 0)
        {
            _lblStatus.Text = "No named driver profiles yet - add some in Driver Profiles first.";
        }
    }

    private void RefreshList(string? selectId = null)
    {
        _list.Items.Clear();
        foreach (var rivalry in _rivalries.All)
        {
            _list.Items.Add(new RivalryListItem(rivalry));
        }

        if (selectId is not null)
        {
            for (var i = 0; i < _list.Items.Count; i++)
            {
                if (((RivalryListItem)_list.Items[i]).Rivalry.Id == selectId)
                {
                    _list.SelectedIndex = i;
                    return;
                }
            }
        }

        if (_list.Items.Count > 0 && _list.SelectedIndex < 0)
        {
            _list.SelectedIndex = 0;
        }
        else if (_list.Items.Count == 0)
        {
            SetEditEnabled(false);
        }
    }

    private RivalryLine? SelectedRivalry() => (_list.SelectedItem as RivalryListItem)?.Rivalry;

    private void LoadSelectedIntoUi()
    {
        var rivalry = SelectedRivalry();
        if (rivalry is null)
        {
            SetEditEnabled(false);
            return;
        }

        _suppressEvents = true;
        try
        {
            SetEditEnabled(true);
            // By index, so a name with no profile any more leaves the box
            // EMPTY. Assigning SelectedItem a name that is not in the list
            // left the previous rivalry's driver showing, and the next save
            // wrote that driver into this rivalry.
            _cmbDriverA.SelectedIndex = _cmbDriverA.Items.IndexOf(rivalry.DriverA);
            _cmbDriverB.SelectedIndex = _cmbDriverB.Items.IndexOf(rivalry.DriverB);
            _chkEnabled.Checked = rivalry.Enabled;
            _txtTemplates.Text = string.Join(Environment.NewLine, rivalry.Templates);
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void SetEditEnabled(bool enabled)
    {
        _cmbDriverA.Enabled = enabled;
        _cmbDriverB.Enabled = enabled;
        _chkEnabled.Enabled = enabled;
        _txtTemplates.Enabled = enabled;
        _btnRestoreLines.Enabled = enabled;
    }

    /// <summary>
    /// Replaces the selected rivalry's lines with the built-in defaults.
    ///
    /// Reads them from a fresh RivalryLine rather than a copy kept here, so
    /// there's exactly one place the default phrasings are defined and the
    /// two can't drift apart.
    /// </summary>
    private void RestoreDefaultLines()
    {
        if (SelectedRivalry() is not { } rivalry)
        {
            return;
        }

        var defaults = new RivalryLine().Templates;

        if (AppDialog.Show(this,
                $"Replace the phrasings for this rivalry with the built-in set of {defaults.Count}?\r\n\r\n"
                + "Any lines you've written here will be lost.",
                "Use default lines", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        // Undoable, like deleting a rivalry - this discards lines someone
        // may have written by hand, which is the same kind of loss.
        //
        // The store only, not this page's boxes: the page re-reads the store
        // when it is shown (RefreshDriverCombos). And only while the rivalry
        // still exists - writing onto the object and saving it used to bring
        // back a rivalry deleted since.
        var previous = rivalry.Templates.ToList();
        var store = _rivalries;
        var id = rivalry.Id;
        _undo.Capture($"Use default lines for {new RivalryListItem(rivalry)}", () =>
        {
            if (store.Get(id) is not { } live)
            {
                throw new InvalidOperationException("that rivalry has been deleted since, so there are no lines to put back.");
            }

            live.Templates = previous.ToList();
            store.Upsert(live);
        });

        // Suppressed so writing the box doesn't re-enter SaveCurrentFromUi
        // through the Leave/TextChanged path while it's being populated.
        _suppressEvents = true;
        try
        {
            _txtTemplates.Text = string.Join(Environment.NewLine, defaults);
        }
        finally
        {
            _suppressEvents = false;
        }

        rivalry.Templates = defaults.ToList();

        // Upsert, matching how every other edit on this page persists -
        // the store has no bare Save().
        _rivalries.Upsert(rivalry);

        _lblStatus.Text = $"Restored {defaults.Count} default lines.";
        _lblStatus.ForeColor = AppTheme.Success;
    }

    private void SaveCurrentFromUi()
    {
        var rivalry = SelectedRivalry();
        if (rivalry is null)
        {
            return;
        }

        // An empty box keeps the stored name: it is empty because that
        // driver has no profile now (or the list is being refilled), not
        // because anybody chose "nobody".
        rivalry.DriverA = _cmbDriverA.SelectedItem as string ?? rivalry.DriverA;
        rivalry.DriverB = _cmbDriverB.SelectedItem as string ?? rivalry.DriverB;
        rivalry.Enabled = _chkEnabled.Checked;
        rivalry.Templates = _txtTemplates.Text
            .Split('\n')
            .Select(l => l.Trim('\r', ' '))
            .Where(l => l.Length > 0)
            .ToList();

        _rivalries.Upsert(rivalry);

        var index = _list.SelectedIndex;
        if (index >= 0)
        {
            _list.Items[index] = new RivalryListItem(rivalry);
            _list.SelectedIndex = index;
        }

        _lblStatus.Text = "Saved.";
    }

    private void BtnAdd_Click(object? sender, EventArgs e)
    {
        if (_profiles.AllRaceable.Count(p => !string.IsNullOrWhiteSpace(p.DriverName)) < 2)
        {
            AppDialog.Show(this, "Add at least two named driver profiles first (see the Driver Profiles tab).",
                "Not enough drivers", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var rivalry = new RivalryLine();
        _rivalries.Upsert(rivalry);
        RefreshList(rivalry.Id);
    }

    private void BtnDelete_Click(object? sender, EventArgs e)
    {
        var rivalry = SelectedRivalry();
        if (rivalry is null)
        {
            AppDialog.Show(this, "Select a rivalry first.", "No rivalry selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (AppDialog.Show(this, "Delete this rivalry?", "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var store = _rivalries;
        var index = _rivalries.IndexOf(rivalry.Id);
        _undo.Capture($"Delete rivalry {new RivalryListItem(rivalry)}", () => store.RestoreDeleted(rivalry, index));

        _rivalries.Delete(rivalry.Id);
        RefreshList();
    }

    private sealed record RivalryListItem(RivalryLine Rivalry)
    {
        public override string ToString()
        {
            var a = string.IsNullOrWhiteSpace(Rivalry.DriverA) ? "(unset)" : Rivalry.DriverA;
            var b = string.IsNullOrWhiteSpace(Rivalry.DriverB) ? "(unset)" : Rivalry.DriverB;
            var suffix = Rivalry.Enabled ? "" : "  [disabled]";
            return $"{a} vs. {b}{suffix}";
        }
    }
}
