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

    /// <summary>What the last change did. As tall as the longest thing it says, so a new status never moves what is above it (SteadyLabel).</summary>
    private readonly SteadyLabel _lblStatus = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The rivalries' card and its buttons, which the column's width is measured from - see ListColumnWidth.</summary>
    private Section? _listCard;
    private ButtonBar? _listButtons;

    /// <summary>The driver pickers' widths as last measured - see Pieces.PickerWidth. Forgotten when their lists are refilled.</summary>
    private (Font Font, int Count, int Width)? _driverAWidth;
    private (Font Font, int Count, int Width)? _driverBWidth;

    /// <summary>The rivalries' column and the driver pickers at the normal text size.</summary>
    private const int ListWidth = 260;
    private const int DriverPickerWidth = 180;

    /// <summary>The fewest rivalries the list shows, and the fewest lines the phrasing box does, before the page scrolls instead.</summary>
    private const int ListLeastRows = 4;
    private const int TemplateLeastLines = 5;

    /// <summary>Said when there is nobody to pick - see RefreshDriverCombos. The longest of the statuses, and one of the wordings the status is kept tall enough for.</summary>
    private const string NoProfilesStatus = "No named driver profiles yet - add some in Driver Profiles first.";

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

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize. The page sits in a tab whose row is drawn in
        // the bold face, which it would otherwise take.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshDriverCombos();
        RefreshList();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The intro, then the rivalries down the left in a card of their own -
    /// the list taking the page's height, Add Rivalry and Delete under it -
    /// and beside them the selected rivalry: its two drivers and whether it
    /// is on, what the phrasings are and the button that puts the built-in
    /// ones back, the phrasing box taking the height the card has left, and
    /// under it the note for when the HeatCaller voice fixes the lines and
    /// what the last change did. On the layout pieces (LayoutPieces.cs), so
    /// nothing is placed by hand; the page scrolls when the window is too
    /// short for it (FillPage).
    ///
    /// It used to be placed in pixels: the list 260x340 and the phrasing box
    /// 450x188 whatever the window, the editor in a group box 480px wide
    /// grown once at build to what its rows came to, the pickers 180px wide
    /// at every text size, and Enabled at x=300 beside Driver A.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Fires a custom line whenever these two named drivers swap position in the standings - in either direction. " +
                   "Both names must currently be assigned to a driver profile (see Driver Profiles) for a rivalry to fire.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // --- The rivalries ---

        // Off, or the list rounds its height down to a whole number of rows
        // and leaves a gap under it that nothing in the layout explains.
        _list.IntegralHeight = false;
        _list.SelectedIndexChanged += (_, _) => LoadSelectedIntoUi();

        var btnAdd = new Button { Text = "Add Rivalry" };
        btnAdd.Click += BtnAdd_Click;

        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += BtnDelete_Click;

        _listButtons = new ButtonBar(tight: true).With(btnAdd, btnDelete);

        _listCard = new Section("Configured rivalries");

        _listCard.AddFill(_list, () => _list.ItemHeight * ListLeastRows + 4);
        _listCard.Add(_listButtons, gapBelow: 0);

        // --- The selected rivalry ---

        _cmbDriverA.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriverA.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        _cmbDriverB.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriverB.SelectedIndexChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        _chkEnabled.Text = "Enabled";
        _chkEnabled.AutoSize = true;
        _chkEnabled.Checked = true;
        _chkEnabled.CheckedChanged += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        // Under the two pickers, lined up with them, rather than out on its
        // own beside Driver A.
        var drivers = new FieldGrid();

        drivers.AddRow("Driver A:", _cmbDriverA);
        drivers.AddRow("Driver B:", _cmbDriverB);
        drivers.AddField(_chkEnabled);

        // Wraps to the card's width, and everything under it moves with it -
        // it once wrapped to a third line at the app's font and ran into a
        // button placed under it at a fixed y.
        var lblTemplates = new Label
        {
            Text = "Phrasing (one line per variant - a random one is picked each time). Placeholders: {overtaker} {overtaken}",
            AutoSize = true,
        };

        // Existing rivalries keep whatever lines they were created with, so
        // a new default set can never reach them on its own - this is the
        // only way to pick up the built-in phrasings after the fact.
        _btnRestoreLines.Text = "Use Default Lines";
        _btnRestoreLines.Click += (_, _) => RestoreDefaultLines();

        var lblRestoreHint = new Label
        {
            Text = "Replaces the lines below with the built-in set.",
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblRestoreHint, () => AppTheme.SmallFont);

        _txtTemplates.Multiline = true;
        _txtTemplates.AcceptsReturn = true;
        _txtTemplates.ScrollBars = ScrollBars.Vertical;
        _txtTemplates.Leave += (_, _) => { if (!_suppressEvents) SaveCurrentFromUi(); };

        // These lines are spoken by the announcer, so the HeatCaller voice
        // fixes them the same way it fixes the ones on Voice Settings: that
        // account is HeatCaller's, not the club's, and words somebody typed
        // are words somebody else pays for. A club that wants its own
        // rivalry lines uses its own ElevenLabs key or the free Windows
        // voice - where they are theirs to spend - and the hint says so.
        _lblHeatCallerLock.Text =
            "Fixed while the HeatCaller voice is speaking - use your own ElevenLabs key or the Windows voice to write your own.";
        // Under the lines box. Hidden, it takes no room: the box has it.
        _lblHeatCallerLock.AutoSize = true;
        _lblHeatCallerLock.ForeColor = AppTheme.Warning;
        _textSize.Wear(_lblHeatCallerLock, () => AppTheme.SmallFont);
        _lblHeatCallerLock.Visible = false;

        // At the foot of the editor, where it used to sit under the group box.
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Wordings = new[] { NoProfilesStatus, "Restored 99 default lines.", "Saved." };

        var edit = new Section("Edit selected rivalry");

        edit.Add(drivers);
        edit.Add(lblTemplates);

        // The hint beside the button, and under it when there is no room.
        edit.Add(FieldGrid.Inline(_btnRestoreLines, lblRestoreHint));
        edit.AddFill(_txtTemplates, () => Pieces.LineHeight(_txtTemplates.Font) * TemplateLeastLines + UiLayout.HintGap * 2, gapBelow: UiLayout.HintGap);
        edit.Add(_lblHeatCallerLock);
        edit.Add(_lblStatus, gapBelow: 0);

        // --- The page ---

        var band = new Beside(ListColumnWidth) { SideOnLeft = true };

        band.AddFill(edit, edit.LeastHeight);
        band.AddSideFill(_listCard, _listCard.LeastHeight, gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro);
        page.AddFill(band, band.LeastHeight, gapBelow: 0);

        Controls.Add(page);

        // Once the pickers are on the page and wear its face - what they are
        // measured in.
        _textSize.Keep(SizeFields);

        SetEditEnabled(false);
    }

    /// <summary>
    /// The driver pickers: as wide as the longest name in them, and never
    /// narrower than they were at the normal text size. Run again when the
    /// text size changes (TextSize.Keep) and when the names are refilled
    /// (RefreshDriverCombos).
    /// </summary>
    private void SizeFields()
    {
        var least = UiLayout.AtTextSize(DriverPickerWidth);

        Fit(_cmbDriverA, Math.Max(least, Pieces.PickerWidth(_cmbDriverA, ref _driverAWidth)));
        Fit(_cmbDriverB, Math.Max(least, Pieces.PickerWidth(_cmbDriverB, ref _driverBWidth)));

        // Only when it changed: a new width lays the grid out again.
        static void Fit(Control field, int width)
        {
            if (field.Width != width)
            {
                field.Width = width;
            }
        }
    }

    /// <summary>
    /// The rivalries' column: the width the list was at the normal text
    /// size, at the size in use - and never narrower than its caption or its
    /// two buttons on one line need.
    /// </summary>
    private int ListColumnWidth() =>
        Math.Max(
            UiLayout.AtTextSize(ListWidth),
            Math.Max(_listCard?.CaptionWidth ?? 0, _listButtons?.OneLineWidth ?? 0) + Section.Inset.Horizontal);

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
            _lblStatus.Text = NoProfilesStatus;
        }

        // The longest name may have come or gone: the pickers measured again.
        _driverAWidth = _driverBWidth = null;
        SizeFields();
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
