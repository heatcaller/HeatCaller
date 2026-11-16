namespace HeatCaller;

/// <summary>
/// Export this driver's data and Forget this driver, on the driver's "..."
/// menu (15 Nov 2026, children's-privacy plan 2.5). The page asks and says;
/// the host does the work (MainForm.Forget), since it holds the stores.
/// Each is hidden until the host sets it.
/// </summary>
public sealed partial class ProfileManagerControl
{
    private ToolStripMenuItem? _miExport;
    private ToolStripMenuItem? _miForget;

    /// <summary>Forgets a driver everywhere - set by the host. Null hides "Forget".</summary>
    internal Func<DriverProfile, DriverForget.Outcome>? ForgetDriverWith { get; set; }

    /// <summary>A driver's data as one HTML page - set by the host. Null hides "Export".</summary>
    internal Func<DriverProfile, string>? ExportDriverWith { get; set; }

    private void BuildPrivacyMenuItems()
    {
        _miExport = new ToolStripMenuItem("Export Driver's Data...", null, (_, _) => ExportShownDriver());
        _miForget = new ToolStripMenuItem("Forget Driver...", null, (_, _) => ForgetShownDriver()) { Tag = AppTheme.DangerMenuItem };
    }

    private void ShowPrivacyMenuItems(string named)
    {
        if (_miExport is not null)
        {
            _miExport.Visible = ExportDriverWith is not null;
            _miExport.Text = $"Export {named}'s Data...";
        }

        if (_miForget is not null)
        {
            _miForget.Visible = ForgetDriverWith is not null;
            _miForget.Text = $"Forget {named}...";
        }
    }

    /// <summary>
    /// The question Forget asks. Says plainly what goes, what is kept as
    /// "Removed driver", and that it can't be undone - unlike Delete.
    /// </summary>
    internal static string ForgetQuestion(DriverProfile profile)
    {
        var named = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName.Trim();

        return $"Forget {named} for good?\r\n\r\n"
            + "This erases everything HeatCaller holds about them on this laptop: profile, photo, crew and guardian, "
            + "check-ins, payments, rentals, handicap, tech inspections, setups, cars, phone requests and emails. "
            + "Their race results stay, shown as \"Removed driver\".\r\n\r\n"
            + "It is taken off the club website too.\r\n\r\n"
            + "This can't be undone. Export their data first if it's wanted.";
    }

    private void ExportShownDriver()
    {
        if (ExportDriverWith is not { } export || CardProfile() is not { } profile)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Title = "Save driver's data",
            Filter = "Web page (*.html)|*.html",
            FileName = DriverDataExport.FileNameFor(profile, DateTime.Now),
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, export(profile), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            AppDialog.Show(this,
                $"Saved to {dialog.FileName}.\r\n\r\nIt holds their personal details - keep it safe, and delete it once it has been handed over.",
                "Driver's data saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't save it: {ex.Message}", "Export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ForgetShownDriver()
    {
        if (ForgetDriverWith is not { } forget || CardProfile() is not { } profile)
        {
            return;
        }

        // No is the default: a stray Enter must not erase a driver.
        if (AppDialog.Show(this, ForgetQuestion(profile), "Forget driver", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
            != DialogResult.Yes)
        {
            return;
        }

        DriverForget.Outcome outcome;

        try
        {
            outcome = forget(profile);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Forgetting stopped partway: {ex.Message}\r\n\r\nTry Forget again on anything left.", "Forget driver", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshAll();
            return;
        }

        RefreshAll();

        AppDialog.Show(this,
            "Forgotten. Removed from this laptop:\r\n\r\n   "
            + string.Join("\r\n   ", outcome.Lines)
            + $"\r\n\r\nTheir results now read \"{outcome.Placeholder}\". Backups made before today still hold them until they are deleted.",
            "Driver forgotten", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
