namespace HeatCaller;

/// <summary>
/// Edits which drivers are in a round, and the order they're listed in.
///
/// Rounds previously had no roster editor at all - heats were generated and
/// then fixed, so a late entry or a no-show meant deleting the round and
/// regenerating the lot. Since that's an entirely normal thing to happen on
/// a race day, it needed to be a direct edit.
///
/// Drivers not in the class are shown too, greyed, because someone turning
/// up to run a class they're not registered in is common enough that
/// hiding them entirely just raises the question of where they went.
///
/// On the layout pieces (DialogFit): the hint across the top, the drivers
/// taking the height with their buttons in a column beside them (Beside),
/// and the count, Save and Cancel at the foot. It used to be placed in
/// pixels in a 520x540 window whatever the text size: the buttons stepped
/// down a standard button's height apart, which the theme draws taller at a
/// larger text size, and the footer at a fixed height from the bottom.
/// </summary>
public sealed class RoundRosterForm : Form
{
    /// <summary>The drivers show at least this many before the window scrolls instead - or as many as the button column beside them is tall, when that is more.</summary>
    private const int LeastRows = 4;

    private readonly ProfileStore _profiles;
    private readonly CheckedListBox _list = new ThemedCheckedListBox();
    private readonly Label _lblCount = new();

    /// <summary>Transponders in this round, in running order.</summary>
    public List<string> Result { get; private set; }

    public RoundRosterForm(RoundDefinition round, ProfileStore profiles, IReadOnlyList<DriverProfile> classRoster)
    {
        _profiles = profiles;
        Result = round.AssignedTransponders.ToList();

        Text = $"Drivers in {round.RoundLabel}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblHint = new Label
        {
            Text = "Tick a driver to include them. Use Move Up / Move Down to set the running order - "
                 + "that's the order they'll appear on the heat sheet.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _list.CheckOnClick = true;
        _list.BackColor = AppTheme.Surface;

        // Given the height the window has, not snapped to whole rows - see FillPage.AddFill.
        _list.IntegralHeight = false;

        _list.ItemCheck += (_, _) =>
        {
            // ItemCheck fires before the state flips, so the count has to be
            // read after the event settles - hence the deferral.
            //
            // Guarded because the constructor below adds items in a checked
            // state, which raises ItemCheck before this form has a window
            // handle - and BeginInvoke on a handle-less control throws. The
            // constructor calls UpdateCount itself once everything is added,
            // so nothing is lost by skipping these early events.
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(UpdateCount);
            }
        };

        // Assigned drivers first, in running order, then the rest of the
        // class - so the order in the list IS the running order.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var transponder in round.AssignedTransponders)
        {
            _list.Items.Add(new RosterItem(transponder, LookupName(transponder, classRoster), InClass: true), isChecked: true);
            seen.Add(transponder);
        }

        foreach (var driver in classRoster.Where(d => !seen.Contains(d.Transponder)))
        {
            _list.Items.Add(new RosterItem(driver.Transponder, driver.DriverName, InClass: true), isChecked: false);
            seen.Add(driver.Transponder);
        }

        var btnUp = new Button { Text = "Move Up" };
        btnUp.Click += (_, _) => MoveSelected(-1);

        var btnDown = new Button { Text = "Move Down" };
        btnDown.Click += (_, _) => MoveSelected(1);

        var btnAll = new Button { Text = "Select All" };
        btnAll.Click += (_, _) => SetAll(true);

        var btnNone = new Button { Text = "Clear All" };
        btnNone.Click += (_, _) => SetAll(false);

        var btnAddOther = new Button { Text = "Add Driver..." };
        btnAddOther.Click += (_, _) => AddDriverFromOutsideClass();

        _lblCount.AutoSize = true;
        _lblCount.Font = AppTheme.BodyFont;
        _lblCount.ForeColor = AppTheme.TextMuted;

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += (_, _) =>
        {
            Result = _list.CheckedItems.Cast<RosterItem>().Select(i => i.Transponder).ToList();
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // --- Layout ---
        //
        // The buttons in one column down the right of the list, all as wide
        // as the widest caption needs, two to a group: a button's gap inside
        // a group and a section's between groups. The list takes the width
        // the column leaves and the height down to the footer.
        var roster = new Beside();
        roster.AddFill(_list, _ => LeastRows * _list.ItemHeight + 4);
        roster.AddSide(btnUp, gapBelow: UiLayout.ButtonGap);
        roster.AddSide(btnDown, gapBelow: UiLayout.SectionGap);
        roster.AddSide(btnAll, gapBelow: UiLayout.ButtonGap);
        roster.AddSide(btnNone, gapBelow: UiLayout.SectionGap);
        roster.AddSide(btnAddOther, gapBelow: 0);

        // The footer: the count on the left, Save and Cancel against the
        // right-hand edge, Cancel furthest right - on one line (SplitBar;
        // see the width given to DialogFit.Keep below). Tab still reaches
        // Save before Cancel, as it did: the bar takes them right to left,
        // in the order they were added.
        var footer = new SplitBar(
            new ButtonBar().With(_lblCount),
            new ButtonBar(alignRight: true).With(btnCancel, btnSave));

        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        var page = new FillPage();
        page.Add(lblHint, gapBelow: UiLayout.RowGap);
        page.AddFill(roster, roster.LeastHeight);
        page.Add(footer, gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        UpdateCount();
        AppTheme.ApplyRecursively(this);

        // Unticks, nothing more - Cancel still undoes it. The theme reads
        // "Clear" as destructive and painted it red, which it is not.
        AppTheme.StyleButton(btnNone, AppTheme.ButtonKind.Secondary);

        // Wider than the 520 it was made at: at 520 the count, Save and
        // Cancel came to about 502px in a line of 488 at the normal text
        // size, and Save and Cancel went onto a line of their own. The
        // height it had.
        DialogFit.Keep(this, page, width: 560, height: 540);
    }

    private string LookupName(string transponder, IReadOnlyList<DriverProfile> roster)
    {
        var fromRoster = roster.FirstOrDefault(d => string.Equals(d.Transponder, transponder, StringComparison.OrdinalIgnoreCase));
        if (fromRoster is not null && !string.IsNullOrWhiteSpace(fromRoster.DriverName))
        {
            return fromRoster.DriverName;
        }

        // Assigned but no longer in the class - still needs a readable name
        // rather than showing a bare transponder id.
        var profile = _profiles.Get(transponder);
        return string.IsNullOrWhiteSpace(profile?.DriverName) ? "(unnamed)" : profile!.DriverName;
    }

    private void UpdateCount() =>
        _lblCount.Text = $"{Plural.Of(_list.CheckedItems.Count, "driver")} in this round";

    private void SetAll(bool state)
    {
        for (var i = 0; i < _list.Items.Count; i++)
        {
            _list.SetItemChecked(i, state);
        }
        UpdateCount();
    }

    /// <summary>Moves the selected driver up or down, keeping its ticked state - the list order is the running order.</summary>
    private void MoveSelected(int direction)
    {
        var index = _list.SelectedIndex;
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _list.Items.Count)
        {
            return;
        }

        var item = _list.Items[index];
        var wasChecked = _list.GetItemChecked(index);

        _list.Items.RemoveAt(index);
        _list.Items.Insert(target, item);
        _list.SetItemChecked(target, wasChecked);
        _list.SelectedIndex = target;
    }

    /// <summary>
    /// Adds someone who isn't in this class. A driver turning up to run a
    /// class they aren't registered for is normal on a race day, and having
    /// to go and edit their profile first just to add them to one heat was
    /// more friction than the situation deserves.
    /// </summary>
    private void AddDriverFromOutsideClass()
    {
        var existing = _list.Items.Cast<RosterItem>()
            .Select(i => i.Transponder)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = _profiles.AllRaceable
            .Where(p => !existing.Contains(p.Transponder))
            .OrderBy(p => p.DriverName)
            .ToList();

        if (candidates.Count == 0)
        {
            AppDialog.Show(this, "Every saved driver is already listed here.", "No one to add",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new DriverPicker(candidates);

        if (picker.ShowDialog(this) == DialogResult.OK && picker.Chosen is { } chosen)
        {
            _list.Items.Add(OutsideClass(chosen), isChecked: true);
            _list.SelectedIndex = _list.Items.Count - 1;
            UpdateCount();
        }
    }

    /// <summary>A driver from outside the class, as the lists here show them - "(unnamed)" for one with no name.</summary>
    private static RosterItem OutsideClass(DriverProfile driver) =>
        new(driver.Transponder,
            string.IsNullOrWhiteSpace(driver.DriverName) ? "(unnamed)" : driver.DriverName,
            InClass: false);

    private sealed record RosterItem(string Transponder, string Name, bool InClass)
    {
        public override string ToString()
        {
            // A ghost's transponder is a meaningless GUID fragment, so show
            // the marker instead - it's the useful information.
            if (GhostService.IsGhost(Transponder))
            {
                return InClass ? $"{Name}   [ghost]" : $"{Name}   [ghost]  (not in this class)";
            }

            return InClass ? $"{Name}   [{Transponder}]" : $"{Name}   [{Transponder}]  (not in this class)";
        }
    }

    /// <summary>
    /// The window Add Driver... opens: the saved drivers who are not listed
    /// yet, to pick one to add - see AddDriverFromOutsideClass. A class of
    /// its own rather than built inside that method, and internal, so the
    /// layout test can build it (WindowCatalog) without a click.
    ///
    /// On the layout pieces (DialogFit): the drivers taking the height, and
    /// Add and Cancel at the foot, as wide as the longest name needs. It
    /// used to be built in pixels in a 360x380 window - a list of a fixed
    /// height, and the two buttons at fixed places and widths under it,
    /// Cancel narrower than its caption at a larger text size.
    /// </summary>
    internal sealed class DriverPicker : Form
    {
        /// <summary>The drivers show at least this many before the window scrolls instead.</summary>
        private const int LeastRows = 4;

        private readonly IReadOnlyList<DriverProfile> _candidates;
        private readonly ListBox _list = new();

        /// <param name="candidates">The drivers to offer, in the order to list them. The first is selected.</param>
        public DriverPicker(IReadOnlyList<DriverProfile> candidates)
        {
            _candidates = candidates;

            Text = "Add a driver to this round";
            Font = AppTheme.BodyFont;
            BackColor = AppTheme.Background;

            _list.BackColor = AppTheme.Surface;

            // Given the height the window has, not snapped to whole rows - see FillPage.AddFill.
            _list.IntegralHeight = false;

            // In the order given, so a row's index is the candidate's - see Chosen.
            foreach (var driver in candidates)
            {
                _list.Items.Add(OutsideClass(driver));
            }

            if (_list.Items.Count > 0)
            {
                _list.SelectedIndex = 0;
            }

            var ok = new Button { Text = "Add", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

            var page = new FillPage();
            page.AddFill(_list, _ => LeastRows * _list.ItemHeight + 4);

            // Against the right-hand edge, Cancel furthest right, as a
            // dialog's buttons go - and Tab still reaches Add first, as it
            // did: the bar takes them right to left, in the order added.
            page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
            ok.TabIndex = 0;
            cancel.TabIndex = 1;

            Controls.Add(page);

            AcceptButton = ok;
            CancelButton = cancel;

            AppTheme.ApplyRecursively(this);

            // The width it was made at, at the text size in use - or wider,
            // up to the screen, for the longest name on the list: at 360px a
            // long name ran off the list's edge, its transponder number and
            // "(not in this class)" with it (layout pictures, 29 Sep).
            // Measured in the face the theme pass left, with room for the
            // list's scrollbar, its edge and a little either side of the text.
            var widest = _list.Items.Cast<object>()
                .Take(400)
                .Select(item => TextRenderer.MeasureText(_list.GetItemText(item) ?? string.Empty, _list.Font).Width)
                .DefaultIfEmpty(0)
                .Max();

            var width = Math.Max(
                UiLayout.AtTextSize(360),
                UiLayout.PageLeft + widest + SystemInformation.VerticalScrollBarWidth + 8 + UiLayout.PageRight);

            DialogFit.Keep(this, page, width: width, height: 380, widthMeasured: true);
        }

        /// <summary>The driver picked, or null for none. Only meaningful after DialogResult.OK.</summary>
        public DriverProfile? Chosen =>
            _list.SelectedIndex >= 0 && _list.SelectedIndex < _candidates.Count
                ? _candidates[_list.SelectedIndex]
                : null;
    }
}
