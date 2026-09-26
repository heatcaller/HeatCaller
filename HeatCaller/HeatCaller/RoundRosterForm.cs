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
/// </summary>
public sealed class RoundRosterForm : Form
{
    private readonly ProfileStore _profiles;
    private readonly CheckedListBox _list = new();
    private readonly Label _lblCount = new();

    /// <summary>Transponders in this round, in running order.</summary>
    public List<string> Result { get; private set; }

    public RoundRosterForm(RoundDefinition round, ProfileStore profiles, IReadOnlyList<DriverProfile> classRoster)
    {
        _profiles = profiles;
        Result = round.AssignedTransponders.ToList();

        Text = $"Drivers in {round.RoundLabel}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 540);
        MinimumSize = new Size(420, 420);
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

        _list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _list.CheckOnClick = true;
        _list.BackColor = AppTheme.Surface;
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
        _lblCount.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _lblCount.ForeColor = AppTheme.TextMuted;

        var btnSave = new Button { Text = "Save", Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        btnSave.Click += (_, _) =>
        {
            Result = _list.CheckedItems.Cast<RosterItem>().Select(i => i.Transponder).ToList();
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };

        Controls.AddRange(new Control[]
        {
            lblHint, _list, btnUp, btnDown, btnAll, btnNone, btnAddOther, _lblCount, btnSave, btnCancel,
        });

        // --- Layout, measured ---
        //
        // Every button here used to sit at a remembered y - 58, 96, 146, 184,
        // 234 - which was cut for a shorter button than the theme draws, so
        // the pairs touched and the gaps between the groups came out
        // uneven. Now one column, one width, the list's top edge as its
        // top edge: two buttons to a group, a fixed gap inside a group and a
        // wider one between them.
        var width = ClientSize.Width - UiLayout.PageLeft - UiLayout.PageRight;

        lblHint.MaximumSize = new Size(width, 0);
        lblHint.Location = new Point(UiLayout.PageLeft, UiLayout.PageTop);

        var column = new[] { btnUp, btnDown, btnAll, btnNone, btnAddOther };
        var columnWidth = column.Max(UiLayout.WidthFor);
        var columnLeft = ClientSize.Width - UiLayout.PageRight - columnWidth;
        var top = UiLayout.Below(lblHint, UiLayout.RowGap);

        var y = top;

        foreach (var group in new[] { new[] { btnUp, btnDown }, new[] { btnAll, btnNone }, new[] { btnAddOther } })
        {
            foreach (var button in group)
            {
                button.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                button.Bounds = new Rectangle(columnLeft, y, columnWidth, UiLayout.ButtonHeight);
                y += UiLayout.ButtonHeight + UiLayout.ButtonGap;
            }

            y += UiLayout.SectionGap - UiLayout.ButtonGap;
        }

        // The footer: the count on the left, Save and Cancel on the right.
        var footerY = ClientSize.Height - UiLayout.PageTop - UiLayout.ButtonHeight;

        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);
        btnCancel.Location = new Point(ClientSize.Width - UiLayout.PageRight - btnCancel.Width, footerY);

        btnSave.Size = new Size(UiLayout.WidthFor(btnSave), UiLayout.ButtonHeight);
        btnSave.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnSave.Width, footerY);

        _lblCount.Font = AppTheme.BodyFont;
        _lblCount.Location = new Point(UiLayout.PageLeft, UiLayout.CentreOn(_lblCount, footerY, UiLayout.ButtonHeight));

        // The list fills what is left: from the hint to the footer, and up
        // to the button column.
        _list.Bounds = new Rectangle(
            UiLayout.PageLeft,
            top,
            columnLeft - UiLayout.SectionGap - UiLayout.PageLeft,
            footerY - UiLayout.SectionGap - top);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        UpdateCount();
        AppTheme.ApplyRecursively(this);

        // Unticks, nothing more - Cancel still undoes it. The theme reads
        // "Clear" as destructive and painted it red, which it is not.
        AppTheme.StyleButton(btnNone, AppTheme.ButtonKind.Secondary);
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

        using var picker = new Form
        {
            Text = "Add a driver to this round",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(360, 380),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = Font,
            BackColor = AppTheme.Background,
        };

        var pickList = new ListBox { Location = new Point(12, 12), Size = new Size(336, 300), BackColor = AppTheme.Surface };
        foreach (var driver in candidates)
        {
            pickList.Items.Add(new RosterItem(driver.Transponder,
                string.IsNullOrWhiteSpace(driver.DriverName) ? "(unnamed)" : driver.DriverName,
                InClass: false));
        }
        pickList.SelectedIndex = 0;

        var ok = new Button { Text = "Add", Location = new Point(166, 326), Width = 84, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Location = new Point(258, 326), Width = 90, DialogResult = DialogResult.Cancel };

        picker.Controls.AddRange(new Control[] { pickList, ok, cancel });
        picker.AcceptButton = ok;
        picker.CancelButton = cancel;

        AppTheme.ApplyRecursively(picker);

        if (picker.ShowDialog(this) == DialogResult.OK && pickList.SelectedItem is RosterItem chosen)
        {
            _list.Items.Add(chosen, isChecked: true);
            _list.SelectedIndex = _list.Items.Count - 1;
            UpdateCount();
        }
    }

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
}
