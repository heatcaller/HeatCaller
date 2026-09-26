using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Loans a unit to a driver.
///
/// The holder is a combo rather than a plain text box so an existing driver
/// can be picked, but it stays editable - walk-ins without a profile are
/// exactly the people who need a rental, and forcing a profile first would
/// put paperwork in front of the thing they came to do.
/// </summary>
public sealed class RentalCheckOutForm : Form
{
    private readonly ComboBox _cmbHolder = new();
    private readonly TextBox _txtDeposit = new();

    public string HolderName => _cmbHolder.Text.Trim();
    public string Deposit => _txtDeposit.Text.Trim();

    public RentalCheckOutForm(RentalTransponder unit, ProfileStore profiles)
    {
        Text = $"Check Out - {unit.DisplayName}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 230);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lblUnit = new Label
        {
            Text = $"Transponder {unit.Transponder}",
            Location = new Point(16, 16),
            AutoSize = true,
            Font = AppTheme.BoldFont,
        };

        var lblHolder = new Label { Text = "Loan to:", Location = new Point(16, 56), AutoSize = true };
        _cmbHolder.Location = new Point(110, 53);
        _cmbHolder.Width = 290;
        _cmbHolder.DropDownStyle = ComboBoxStyle.DropDown;
        foreach (var name in profiles.All.Select(p => p.DriverName)
                     .Where(n => !string.IsNullOrWhiteSpace(n))
                     .Distinct()
                     .OrderBy(n => n))
        {
            _cmbHolder.Items.Add(name);
        }

        var lblHolderHint = new Label
        {
            Text = "Pick a saved driver or just type a name - a walk-in doesn't need a profile first.",
            Location = new Point(110, 82),
            AutoSize = true,
            MaximumSize = new Size(290, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var lblDeposit = new Label { Text = "Deposit held:", Location = new Point(16, 122), AutoSize = true };
        _txtDeposit.Location = new Point(110, 119);
        _txtDeposit.Width = 290;
        _txtDeposit.PlaceholderText = "car key, licence, £20...";

        var lblDepositHint = new Label
        {
            Text = "Whatever you're holding until it comes back. Recorded so whoever is on the counter later knows what to return.",
            Location = new Point(110, 148),
            AutoSize = true,
            MaximumSize = new Size(290, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var btnOk = new Button { Text = "Check Out", Location = new Point(226, 190), Width = 96 };
        btnOk.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_cmbHolder.Text))
            {
                AppDialog.Show(this, "Enter who's taking it.", "Missing name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", Location = new Point(330, 190), Width = 86, DialogResult = DialogResult.Cancel };

        Controls.AddRange(new Control[]
        {
            lblUnit, lblHolder, _cmbHolder, lblHolderHint,
            lblDeposit, _txtDeposit, lblDepositHint, btnOk, btnCancel,
        });

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
    }
}

/// <summary>Adds or edits a rental unit.</summary>
public sealed class RentalUnitEditForm : Form
{
    private readonly TextBox _txtTransponder = new();
    private readonly TextBox _txtAssetTag = new();
    private readonly TextBox _txtNotes = new();
    private readonly CheckBox _chkRetired = new();

    /// <summary>
    /// What Save hands back - a COPY of the stored unit when editing. The
    /// dialog used to write straight onto the stored unit, so the undo
    /// snapshot the page takes afterwards already held the edit and Undo
    /// had nothing to put back. A copy leaves the store untouched until the
    /// page saves it, and makes Cancel a true cancel as well.
    /// </summary>
    public RentalTransponder Result { get; private set; }

    public RentalUnitEditForm(RentalTransponder? existing)
    {
        Result = existing is null
            ? new RentalTransponder { Transponder = string.Empty }
            : JsonSerializer.Deserialize<RentalTransponder>(JsonSerializer.Serialize(existing)) ?? existing;

        Text = existing is null ? "Add Rental Unit" : $"Edit {Result.DisplayName}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 300);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lblTransponder = new Label { Text = "Transponder ID:", Location = new Point(16, 20), AutoSize = true };
        _txtTransponder.Location = new Point(130, 17);
        _txtTransponder.Width = 270;
        _txtTransponder.Text = Result.Transponder;

        // The ID is the identity - changing it would orphan the unit's loan
        // history and any lap data recorded under it.
        _txtTransponder.ReadOnly = existing is not null;
        if (existing is not null)
        {
            _txtTransponder.BackColor = AppTheme.Surface;
        }

        var lblIdHint = new Label
        {
            Text = existing is null
                ? "Exactly as the decoder reports it."
                : "Can't be changed - it ties this unit to its loan history.",
            Location = new Point(130, 46),
            AutoSize = true,
            MaximumSize = new Size(270, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var lblTag = new Label { Text = "Asset tag:", Location = new Point(16, 78), AutoSize = true };
        _txtAssetTag.Location = new Point(130, 75);
        _txtAssetTag.Width = 270;
        _txtAssetTag.Text = Result.AssetTag;
        _txtAssetTag.PlaceholderText = "R4, sticker number...";

        var lblTagHint = new Label
        {
            Text = "What people call it at the counter.",
            Location = new Point(130, 104),
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var lblNotes = new Label { Text = "Notes:", Location = new Point(16, 134), AutoSize = true };
        _txtNotes.Location = new Point(130, 131);
        _txtNotes.Width = 270;
        _txtNotes.Height = 70;
        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Text = Result.Notes;

        _chkRetired.Text = "Retired (out of service - keeps its history)";
        _chkRetired.Location = new Point(130, 210);
        _chkRetired.AutoSize = true;
        _chkRetired.Checked = Result.Retired;

        var btnOk = new Button { Text = "Save", Location = new Point(226, 250), Width = 96 };
        btnOk.Click += BtnOk_Click;

        var btnCancel = new Button { Text = "Cancel", Location = new Point(330, 250), Width = 86, DialogResult = DialogResult.Cancel };

        Controls.AddRange(new Control[]
        {
            lblTransponder, _txtTransponder, lblIdHint,
            lblTag, _txtAssetTag, lblTagHint,
            lblNotes, _txtNotes, _chkRetired, btnOk, btnCancel,
        });

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        var id = _txtTransponder.Text.Trim();
        if (string.IsNullOrWhiteSpace(id))
        {
            AppDialog.Show(this, "A transponder ID is required.", "Missing ID", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result.Transponder = id;
        Result.AssetTag = _txtAssetTag.Text.Trim();
        Result.Notes = _txtNotes.Text;
        Result.Retired = _chkRetired.Checked;

        DialogResult = DialogResult.OK;
        Close();
    }
}
