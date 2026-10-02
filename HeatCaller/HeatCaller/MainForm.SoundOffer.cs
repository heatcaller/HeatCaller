namespace HeatCaller;

/// <summary>
/// MainForm's side of the sound the setup wizards offer (SoundOffer): what
/// they are handed, and the two applies they ask for - a soundscape, the
/// same apply with the same undo as on the Soundscapes page, or lap times
/// only (LapTimesOnly). Both refresh the pages that show the sound, as an
/// apply on the Soundscapes page does.
/// </summary>
public partial class MainForm
{
    /// <summary>The Soundscapes page, to mark the soundscape applied from elsewhere - see ShowSoundscapeApplied.</summary>
    private ThemesControl? _themesControl;

    /// <summary>The Mixer, whose "Bring up:" ticks lap times only changes - see AfterSoundscapeApplied.</summary>
    private MixerControl? _mixerControl;

    /// <summary>The AI Commentary page, whose spoken intros, recaps and teaser lap times only changes - see AfterSoundscapeApplied.</summary>
    private CommentaryControl? _commentaryControl;

    /// <summary>What a setup wizard is handed to offer the sound.</summary>
    private SoundOfferHost MakeSoundOfferHost() => new(
        Soundscapes: () => _themeStore.All,
        SetNow: SoundSetNow,
        ApplySoundscape: ApplySoundscapeFromWizard,
        ApplyLapTimesOnly: ApplyLapTimesOnlyFromWizard);

    /// <summary>What the sound was last set to, for "Keep what's set now (...)": lap times only, the soundscape applied last, or nothing known.</summary>
    private string SoundSetNow()
    {
        if (LapTimesOnly.IsOn(_settings))
        {
            return "lap times only";
        }

        return _themeStore.ActiveThemeId is { } id && _themeStore.Get(id) is { } theme ? theme.Name : string.Empty;
    }

    /// <summary>
    /// A soundscape chosen in a setup wizard, applied as the Soundscapes
    /// page applies one (ThemesControl.ApplyTheme) - without its question,
    /// which the wizard's choice answers - leaving lap times only first if
    /// that was the sound. One undo puts all of it back.
    /// </summary>
    private void ApplySoundscapeFromWizard(Theme theme)
    {
        // Taken before anything changes - see LapTimesOnly.Leave.
        var putBack = ThemeStore.CaptureForUndo(theme, _voiceStore, _soundStore, _ambienceStore);
        var putBackLapTimesOnly = LapTimesOnly.Leave(_settings, _voiceStore);
        var themeStore = _themeStore;

        _undo.Capture($"Apply the {theme.Name} soundscape", () =>
        {
            putBack();
            putBackLapTimesOnly?.Invoke();
            themeStore.NotifyRestored(liveSettingsRestored: true);
        });

        ThemeStore.Apply(theme, _voiceStore, _soundStore, _ambienceStore, _musicStore);
        _themeStore.MarkApplied(theme);

        ShowSoundscapeApplied();
        OnClientRawMessage($"{AppTime.LogStampNow()}  [voice] Applied the \"{theme.Name}\" soundscape from setup.");
    }

    /// <summary>
    /// Lap times only, chosen in a setup wizard: the settings changed
    /// (LapTimesOnly.Apply), and music and ambience stopped if they are
    /// playing - lap times only has neither, and a mute in force does not
    /// bring them back when it is lifted. One undo puts the settings back;
    /// what was stopped stays stopped, as anything stopped by hand does.
    /// </summary>
    private void ApplyLapTimesOnlyFromWizard()
    {
        var putBack = LapTimesOnly.Apply(_settings, _voiceStore, _soundStore, _themeStore);
        var themeStore = _themeStore;

        _undo.Capture("Set the sound to lap times only", () =>
        {
            putBack();
            themeStore.NotifyRestored(liveSettingsRestored: true);
        });

        if (_music.IsPlaying)
        {
            _music.Stop();
        }

        if (_ambience.IsRunning)
        {
            _ambience.Stop();
        }

        _musicPausedByMute = false;
        _ambienceStoppedByMute = false;

        ShowSoundscapeApplied();
        OnClientRawMessage($"{AppTime.LogStampNow()}  [voice] Sound set to lap times only from setup.");
    }

    /// <summary>
    /// After the sound was set from somewhere other than the Soundscapes
    /// page: that page's cards mark what is applied now, and every page that
    /// shows the sound catches up (AfterSoundscapeApplied).
    /// </summary>
    private void ShowSoundscapeApplied()
    {
        _themesControl?.ShowApplied();
        AfterSoundscapeApplied();
    }
}
