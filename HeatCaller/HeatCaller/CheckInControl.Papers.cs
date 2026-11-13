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

        var problems = PapersProblems(profile);
        var cell = row.Cells[column.Index];
        cell.Value = DeskPapers.Cell(problems);
        cell.Style.ForeColor = problems.Count == 0 ? AppTheme.Success : AppTheme.Warning;
    }

    /// <summary>The Papers cell was clicked: the papers window for that driver.</summary>
    private void EditPapers(int rowIndex)
    {
        if (_grid.Rows[rowIndex].Tag is not DriverProfile profile || !ChecksPapers)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

        using var form = new PapersForm(name, profile, _settings.AskForWaivers, _settings.CheckMemberships);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

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
