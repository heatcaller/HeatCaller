using System.Globalization;

namespace HeatCaller;

/// <summary>
/// The tiles over Backup and Restore's cards (17 Nov 2026, page jazz-up):
/// when the last backup was made, how many are in the folder, whether the
/// copies are reaching the cloud, and what this PC is to a standby. They
/// only show what the cards already know - each card's refresh brings them
/// up to date (RefreshBackupList, RefreshLastBackupLabel, RefreshCloudStatus,
/// RefreshStandby) - so nothing about backing up changes for them.
/// </summary>
public sealed partial class BackupRestoreControl
{
    private readonly StatTile[] _backupTiles = { new(), new(), new(), new() };

    private static readonly string[] BackupTileCaptions = { "Last backup", "Backups in folder", "Cloud copies", "Standby PC" };

    /// <summary>The cloud and standby tiles' values, drawn as chips in their state's colour.</summary>
    private ConnectionTileChip? _cloudChip;
    private ConnectionTileChip? _standbyChip;

    /// <summary>The list as the card list it is drawn as.</summary>
    private BackupFileList BackupRows => (BackupFileList)_lstBackups;

    private void BuildBackupTiles()
    {
        for (var i = 0; i < _backupTiles.Length; i++)
        {
            _textSize.Wear(_backupTiles[i].Value, () => AppTheme.TitleFont);
            _textSize.Wear(_backupTiles[i].Caption, () => AppTheme.MicroFont);
            _backupTiles[i].Set("-", BackupTileCaptions[i]);
        }

        _cloudChip = new ConnectionTileChip(_backupTiles[2]);
        _standbyChip = new ConnectionTileChip(_backupTiles[3]);
    }

    /// <summary>The last backup, the folder's count and the cloud copies, from what the cards show now. UI thread.</summary>
    private void ShowBackupTiles()
    {
        _backupTiles[0].Set(_settings.LastAutoBackupUtc is { } last ? LastBackupWhen(last.ToLocalTime(), DateTime.Now) : "Never", BackupTileCaptions[0]);

        var count = _lstBackups.Items.Count;
        _backupTiles[1].Set(count == 0 ? "None" : count.ToString("N0", CultureInfo.CurrentCulture), BackupTileCaptions[1]);

        // The light's own words and colour (RefreshCloudStatus), so the
        // tile and the card can never disagree.
        if (_cloudChip is not null && !string.IsNullOrEmpty(_lblCloudLight.Text))
        {
            _backupTiles[2].Set(_lblCloudLight.Text, BackupTileCaptions[2]);
            _cloudChip.Tone = _lblCloudLight.ForeColor;
        }
    }

    /// <summary>What this PC is to a standby: off, the timing PC it copies, a standby and how old its copy is, or one that took over.</summary>
    private void ShowStandbyTile(bool tookOver, WarmStandby.MirrorStatus status)
    {
        if (_standbyChip is null)
        {
            return;
        }

        string value;
        Color tone;

        if (tookOver)
        {
            (value, tone) = ("Took over", AppTheme.Warning);
        }
        else if (_settings.StandbyModeEnabled)
        {
            (value, tone) = status.LastCopyUtc is { } copied
                ? ($"Standby, copy {WarmStandby.Age(DateTime.UtcNow - copied)} old", status.LastError is null ? AppTheme.Success : AppTheme.Warning)
                : ("Standby, no copy yet", AppTheme.Warning);
        }
        else if (_settings.StandbyServeEnabled)
        {
            (value, tone) = ("Timing PC", AppTheme.Success);
        }
        else
        {
            (value, tone) = ("Off", AppTheme.TextMuted);
        }

        _backupTiles[3].Set(value, BackupTileCaptions[3]);
        _standbyChip.Tone = tone;
    }

    /// <summary>"Today, 9:04 PM", "Yesterday, 9:04 PM", "Tue 11 Nov", "11 Nov 2025".</summary>
    internal static string LastBackupWhen(DateTime local, DateTime now)
    {
        if (local.Date == now.Date)
        {
            return $"Today, {AppTime.TimeOfDay(local)}";
        }

        if (local.Date == now.Date.AddDays(-1))
        {
            return $"Yesterday, {AppTime.TimeOfDay(local)}";
        }

        return local.Year == now.Year
            ? local.ToString("ddd d MMM", CultureInfo.CurrentCulture)
            : local.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
    }
}
