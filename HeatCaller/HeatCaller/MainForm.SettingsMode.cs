namespace HeatCaller;

/// <summary>
/// MainForm's side of Simple and Advanced mode (1 Nov 2026, round 1 - see
/// SettingsModes): deciding the mode once for an install from before the
/// modes, turning Advanced on behind its warning (AdvancedModeForm) from the
/// Settings mode card on Backup &amp; Restore, going back to Simple from that
/// card or the chip, and the amber "Advanced" chip at the foot of the
/// sidebar while Advanced is on.
///
/// Round 1 hides nothing: the mode is saved and shown, and round 2 hides
/// what SettingsTable says is HiddenInSimple.
/// </summary>
public partial class MainForm
{
    /// <summary>The chip at the foot of the sidebar, shown in Advanced mode only.</summary>
    private readonly AdvancedChip _advancedChip = new();

    /// <summary>
    /// Whether the chip is in the footer. Kept rather than read from the
    /// chip, for the reason _usageChipShown is: a control reports itself
    /// hidden while the window is.
    /// </summary>
    private bool _advancedChipShown;

    /// <summary>The Backup &amp; Restore page, for its Settings mode card. Null until that page is built.</summary>
    private BackupRestoreControl? _backupRestore;

    /// <summary>What the first look at the settings decided - kept for the one-time note once the window is up.</summary>
    private SettingsModes.Decision _settingsModeDecision = SettingsModes.Decision.AlreadyDecided;

    /// <summary>
    /// Called once, from the constructor, after the settings and the sound
    /// stores are loaded and before the pages are built: decides the mode if
    /// it never has been, and puts the chip in the footer.
    /// </summary>
    private void WireSettingsMode()
    {
        _settingsModeDecision = SettingsModes.Decide(_settings, () => SettingsModes.ChangedHidden(
            _settings,
            _voiceStore.Current,
            _musicStore.Current,
            _ambienceStore.Current,
            BroadcastImaging.SettingsCopy()));

        if (_settingsModeDecision != SettingsModes.Decision.AlreadyDecided)
        {
            _settings.Save();
            SessionLog.Write($"[settings] Settings mode decided: {_settings.SettingsMode} ({_settingsModeDecision}).");
        }

        _advancedChip.Click += (_, _) => TurnOnSimpleMode();
        _footerTooltip.SetToolTip(_advancedChip, SettingsModes.ChipTooltip);
        pnlSidebarFooter.Controls.Add(_advancedChip);

        // Before the pages are built, so each tags its Advanced-only parts
        // already in the mode it opens in (round 2 - see SimpleMode).
        SimpleMode.AskForAdvanced = TurnOnAdvancedMode;
        SimpleMode.Apply(_settings.SettingsMode == SettingsMode.Advanced);
        Disposed += (_, _) => SimpleMode.Reset();

        ShowSettingsMode();
    }

    /// <summary>The one-time note for an install that has just updated into the modes. A startup step, once the window is up.</summary>
    private void ShowSettingsModeNote()
    {
        if (SettingsModes.UpdateNote(_settingsModeDecision) is not { } note)
        {
            return;
        }

        _settingsModeDecision = SettingsModes.Decision.AlreadyDecided;

        AppDialog.Show(this, note, "Simple mode and Advanced mode", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>The Settings mode card's button: Advanced behind the warning, or back to Simple.</summary>
    private void ToggleSettingsMode()
    {
        if (_settings.SettingsMode == SettingsMode.Advanced)
        {
            TurnOnSimpleMode();
        }
        else
        {
            TurnOnAdvancedMode();
        }
    }

    /// <summary>Asks first (AdvancedModeForm), then turns Advanced mode on and saves. UI thread.</summary>
    internal void TurnOnAdvancedMode()
    {
        if (_settings.SettingsMode == SettingsMode.Advanced)
        {
            return;
        }

        var raceRunning = _race.IsPracticeMode || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;

        if (!AdvancedModeForm.Ask(this, raceRunning))
        {
            return;
        }

        SetSettingsMode(SettingsMode.Advanced);
    }

    /// <summary>
    /// Back to Simple mode, with no warning - nothing is lost going this way.
    /// When anything about to be hidden is not at its default, a short note
    /// says so after: it keeps working while hidden. (Round 4 adds Restore
    /// Defaults to that note.) UI thread.
    /// </summary>
    internal void TurnOnSimpleMode()
    {
        if (_settings.SettingsMode == SettingsMode.Simple)
        {
            return;
        }

        SetSettingsMode(SettingsMode.Simple);

        if (SettingsModes.ChangedHidden(
                _settings,
                _voiceStore.Current,
                _musicStore.Current,
                _ambienceStore.Current,
                BroadcastImaging.SettingsCopy()).Count > 0)
        {
            AppDialog.Show(this, SettingsModes.HidingChangedNote, "Simple mode", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>
    /// The mode set, saved and shown. Not an undo step: it is a view, like
    /// the text size and the narrowed sidebar, and Ctrl+Z after it should
    /// take back the club's last real change.
    /// </summary>
    private void SetSettingsMode(SettingsMode mode)
    {
        _settings.SettingsMode = mode;
        _settings.Save();

        OnClientRawMessage($"{AppTime.LogStampNow()}  [settings] {mode} mode is on.");

        // Every tagged tab, card and row shown or hidden where it stands, and
        // the sidebar without Venues in Simple - no restart.
        SimpleMode.Apply(mode == SettingsMode.Advanced);
        RebuildNav();

        ShowSettingsMode();
    }

    /// <summary>
    /// For the layout test: the mode set as the Settings mode card sets it,
    /// with no warning and no note - the test measures every page in
    /// Advanced, then again in Simple where something is hidden.
    /// </summary>
    internal void SetSettingsModeForLayoutCheck(SettingsMode mode) => SetSettingsMode(mode);

    /// <summary>The card and the chip brought up to date with the mode, and the footer made room for the chip - or given it back. UI thread.</summary>
    private void ShowSettingsMode()
    {
        if (_closingDown || IsDisposed)
        {
            return;
        }

        _backupRestore?.ShowMode(_settings.SettingsMode);

        var show = _settings.SettingsMode == SettingsMode.Advanced;

        // Manage Navigation is tuning too: Simple keeps the sidebar as it ships.
        if (btnManageNav.Visible != show)
        {
            btnManageNav.Visible = show;
        }

        if (show == _advancedChipShown)
        {
            PlaceSidebar();
            return;
        }

        _advancedChipShown = show;
        _advancedChip.Visible = show;
        PlaceSidebar();
    }
}
