using System.Drawing;

namespace HeatCaller;

/// <summary>
/// The Papers column on Who's Racing (12 Nov 2026, setup plan D9): the
/// waiver and the membership, beside the driver as they check in. Shown
/// only while the club checks one of them (Setup tab). Click the cell to
/// record a waiver signed tonight or a membership's number and expiry.
/// Recorded by hand - see DeskPapers.
/// </summary>
public sealed partial class CheckInControl
{
    /// <summary>Whether the club checks either paper.</summary>
    private bool ChecksPapers => _settings.AskForWaivers || _settings.CheckMemberships;

    /// <summary>What's not in order for a driver tonight.</summary>
    internal List<string> PapersProblems(DriverProfile profile) =>
        DeskPapers.Problems(profile, _settings.AskForWaivers, _settings.WaiverMonths, _settings.CheckMemberships, DateTime.Today);

    /// <summary>Fills a row's Papers cell.</summary>
    private void ShowPapers(DataGridViewRow row)
    {
        if (_grid.Columns["papers"] is not { } column || row.Tag is not DriverProfile profile || !ChecksPapers)
        {
            return;
        }

        // A chip says one thing (13 Nov 2026): the first problem, "+1" for
        // any more, and all of them in the cell's tip.
        var problems = PapersProblems(profile);
        var cell = row.Cells[column.Index];
        cell.Value = problems.Count switch
        {
            0 => DeskPapers.Cell(problems),
            1 => problems[0],
            _ => $"{problems[0]} +{problems.Count - 1}",
        };
        cell.ToolTipText = problems.Count > 1 ? DeskPapers.Cell(problems) : string.Empty;
        cell.Style.ForeColor = problems.Count == 0 ? AppTheme.Success : AppTheme.Warning;
    }

    /// <summary>The Papers cell was clicked: the papers window for that driver.</summary>
    private void EditPapers(int rowIndex)
    {
        if (_grid.Rows[rowIndex].Tag is DriverProfile profile)
        {
            EditPapersFor(profile);
        }
    }

    /// <summary>The papers window for one driver - from their Papers cell, or Desk mode's Record Papers.</summary>
    private void EditPapersFor(DriverProfile profile)
    {
        if (!ChecksPapers)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

        using var form = new PapersForm(name, profile, _settings.AskForWaivers, _settings.CheckMemberships);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SavePapers(profile, name, form);
    }

    /// <summary>Saves what the one-driver papers window says, once it closed with Save.</summary>
    private void SavePapers(DriverProfile profile, string name, PapersForm form)
    {
        // WHY (14 Nov 2026, bug hunt 19): the profile came from the list's row
        // or Desk mode's card, drawn before any phone change since - a car
        // picked for tonight, the driver's own edits approved. Saving that old
        // copy put those changes back, so the papers go on the driver as they
        // are on file now. Also covers a phone change while this window was open.
        if (_profiles.Get(profile.Transponder) is not { } onFile)
        {
            RefreshDriverList();
            return;
        }

        profile = onFile;

        var before = (profile.WaiverSignedOn, profile.IsJunior, profile.WaiverGuardian, profile.MembershipNumber, profile.MembershipExpires);

        form.WriteTo(profile);
        _profiles.Upsert(profile);

        var transponder = profile.Transponder;
        _undo.Capture($"Papers for {name}", () =>
        {
            if (_profiles.Get(transponder) is { } current)
            {
                (current.WaiverSignedOn, current.IsJunior, current.WaiverGuardian, current.MembershipNumber, current.MembershipExpires) = before;
                _profiles.Upsert(current);
            }

            if (!IsDisposed)
            {
                RefreshDriverList();
            }
        });

        RefreshDriverList();
    }

    /// <summary>
    /// The papers window for several drivers picked on the list (14 Nov 2026):
    /// marks their waivers signed and/or memberships good until a date, all at
    /// once. Only what's ticked is written; the rest of each driver's papers
    /// (membership number, guardian) stays as it was.
    /// </summary>
    private void EditPapersForMany(IReadOnlyList<DriverProfile> drivers)
    {
        if (!ChecksPapers || drivers.Count == 0)
        {
            return;
        }

        using var form = new PapersForm(drivers.Count, _settings.AskForWaivers, _settings.CheckMemberships);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        SavePapersForMany(drivers, form);
    }

    /// <summary>Saves what the several-driver papers window says, once it closed with Save.</summary>
    private void SavePapersForMany(IReadOnlyList<DriverProfile> drivers, PapersForm form)
    {
        if (!form.ChangesAnything)
        {
            return;
        }

        var before = new List<(string Transponder, DateTime? Waiver, DateTime? Expires)>();

        // As on file now, not the rows' copies (bug hunt 19 - see EditPapersFor).
        foreach (var profile in drivers.Select(d => _profiles.Get(d.Transponder)).OfType<DriverProfile>())
        {
            before.Add((profile.Transponder, profile.WaiverSignedOn, profile.MembershipExpires));
            form.WriteToMany(profile);
            _profiles.Upsert(profile);
        }

        _undo.Capture($"Papers for {Plural.Of(drivers.Count, "driver")}", () =>
        {
            foreach (var (transponder, waiver, expires) in before)
            {
                if (_profiles.Get(transponder) is { } current)
                {
                    current.WaiverSignedOn = waiver;
                    current.MembershipExpires = expires;
                    _profiles.Upsert(current);
                }
            }

            if (!IsDisposed)
            {
                RefreshDriverList();
            }
        });

        RefreshDriverList();
        SetScanStatus($"Papers recorded for {Plural.Of(drivers.Count, "driver")}.", AppTheme.Success);
    }
}

/// <summary>One driver's papers: the waiver (signed tonight, or on a date) and the membership.</summary>
internal sealed class PapersForm : Form
{
    private readonly CheckBox _chkWaiver = new ThemedCheckBox();
    private readonly DateTimePicker _dtpWaiver = new() { Format = DateTimePickerFormat.Short, Width = 130 };
    private readonly CheckBox _chkJunior = new ThemedCheckBox();
    private readonly TextBox _txtGuardian = new() { Width = 220 };
    private readonly TextBox _txtNumber = new() { Width = 220 };
    private readonly CheckBox _chkExpires = new ThemedCheckBox();
    private readonly DateTimePicker _dtpExpires = new() { Format = DateTimePickerFormat.Short, Width = 130 };

    public PapersForm(string name, DriverProfile profile, bool waivers, bool memberships)
    {
        Text = $"Papers - {name}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var lblHow = new Label
        {
            Text = "Recorded by hand, for the club's own records: HeatCaller doesn't take signatures or check anyone's membership.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = AppTheme.TextMuted,
        };

        _chkWaiver.Text = "Waiver signed on";
        _chkWaiver.AutoSize = true;
        _chkWaiver.Checked = profile.WaiverSignedOn is not null;
        _dtpWaiver.Value = profile.WaiverSignedOn ?? DateTime.Today;
        _dtpWaiver.Enabled = _chkWaiver.Checked;
        _chkWaiver.CheckedChanged += (_, _) => _dtpWaiver.Enabled = _chkWaiver.Checked;

        _chkJunior.Text = "Under 18 (signed by a parent or guardian)";
        _chkJunior.AutoSize = true;
        _chkJunior.Checked = profile.IsJunior;
        _txtGuardian.Text = profile.WaiverGuardian;
        _txtGuardian.Enabled = _chkJunior.Checked;
        _chkJunior.CheckedChanged += (_, _) => _txtGuardian.Enabled = _chkJunior.Checked;

        _txtNumber.Text = profile.MembershipNumber;

        _chkExpires.Text = "Runs out on";
        _chkExpires.AutoSize = true;
        _chkExpires.Checked = profile.MembershipExpires is not null;
        _dtpExpires.Value = profile.MembershipExpires ?? DateTime.Today.AddYears(1);
        _dtpExpires.Enabled = _chkExpires.Checked;
        _chkExpires.CheckedChanged += (_, _) => _dtpExpires.Enabled = _chkExpires.Checked;

        var grid = new FieldGrid();

        if (waivers)
        {
            grid.AddRow("Waiver:", FieldGrid.Inline(_chkWaiver, _dtpWaiver));
            grid.AddField(_chkJunior);
            grid.AddRow("Guardian:", _txtGuardian);
        }

        if (memberships)
        {
            grid.AddRow("Membership no.:", _txtNumber);
            grid.AddRow("Membership:", FieldGrid.Inline(_chkExpires, _dtpExpires));
        }

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };

        var page = new FillPage();
        page.Add(lblHow, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(grid, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 500, height: 360);
    }

    /// <summary>
    /// The papers window for several drivers at once: just the waiver date and
    /// the membership's expiry, each written to all of them only when ticked.
    /// The waiver starts ticked for today - marking them signed is what it's for.
    /// </summary>
    public PapersForm(int count, bool waivers, bool memberships)
    {
        Text = $"Papers for {Plural.Of(count, "Driver")}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var lblHow = new Label
        {
            Text = $"What's ticked is recorded for all {count} drivers; anything left unticked stays as it is for each of them. Recorded by hand, for the club's own records.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            ForeColor = AppTheme.TextMuted,
        };

        _chkWaiver.Text = "Waiver signed on";
        _chkWaiver.AutoSize = true;
        _chkWaiver.Checked = waivers;
        _dtpWaiver.Value = DateTime.Today;
        _dtpWaiver.Enabled = _chkWaiver.Checked;
        _chkWaiver.CheckedChanged += (_, _) => _dtpWaiver.Enabled = _chkWaiver.Checked;

        _chkExpires.Text = "Runs out on";
        _chkExpires.AutoSize = true;
        _chkExpires.Checked = !waivers && memberships;
        _dtpExpires.Value = DateTime.Today.AddYears(1);
        _dtpExpires.Enabled = _chkExpires.Checked;
        _chkExpires.CheckedChanged += (_, _) => _dtpExpires.Enabled = _chkExpires.Checked;

        _waiverShown = waivers;
        _membershipShown = memberships;

        var grid = new FieldGrid();

        if (waivers)
        {
            grid.AddRow("Waiver:", FieldGrid.Inline(_chkWaiver, _dtpWaiver));
        }

        if (memberships)
        {
            grid.AddRow("Membership:", FieldGrid.Inline(_chkExpires, _dtpExpires));
        }

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };

        var page = new FillPage();
        page.Add(lblHow, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(grid, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 500, height: 260);
    }

    private readonly bool _waiverShown;
    private readonly bool _membershipShown;

    /// <summary>Whether the several-driver window has anything ticked to write.</summary>
    public bool ChangesAnything => (_waiverShown && _chkWaiver.Checked) || (_membershipShown && _chkExpires.Checked);

    /// <summary>Puts what's ticked in the several-driver window onto one of the drivers.</summary>
    public void WriteToMany(DriverProfile profile)
    {
        if (_waiverShown && _chkWaiver.Checked)
        {
            profile.WaiverSignedOn = _dtpWaiver.Value.Date;
        }

        if (_membershipShown && _chkExpires.Checked)
        {
            profile.MembershipExpires = _dtpExpires.Value.Date;
        }
    }

    /// <summary>Puts what the window holds onto the profile.</summary>
    public void WriteTo(DriverProfile profile)
    {
        profile.WaiverSignedOn = _chkWaiver.Checked ? _dtpWaiver.Value.Date : null;
        profile.IsJunior = _chkJunior.Checked;
        profile.WaiverGuardian = _chkJunior.Checked ? _txtGuardian.Text.Trim() : string.Empty;
        profile.MembershipNumber = _txtNumber.Text.Trim();
        profile.MembershipExpires = _chkExpires.Checked ? _dtpExpires.Value.Date : null;
    }
}

/// <summary>Who said they're coming tonight, on Who's Racing (12 Nov 2026, setup plan C1).</summary>
public sealed partial class CheckInControl
{
    /// <summary>Shows "12 said they're coming · 9 here · still to come: ..." while anybody said so for tonight.</summary>
    private void RefreshComing()
    {
        var coming = ComingTonightList?.Invoke() ?? Array.Empty<PreEntry>();
        var line = ComingTonight.Line(coming, _checkIns.IsCheckedIn,
            t => _profiles.Get(t) is { DriverName.Length: > 0 } p ? p.DriverName.Trim() : t);
        var wanted = line is not null;

        if (line is not null && _lblComing.Text != line)
        {
            _lblComing.Text = line;
        }

        if (_comingShown == wanted)
        {
            return;
        }

        _comingShown = wanted;
        _lblComing.Visible = wanted;
    }
}
