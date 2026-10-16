namespace HeatCaller;

/// <summary>
/// A soundscape puts the app in its own look (16 Oct 2026): applying Classic
/// on the Soundscapes page turns the app Synthwave, Pit Lane turns it hazard
/// yellow, and so on - see Theme.LookKey.
///
/// The look is saved as the app's look exactly as Themes' "Use for the App"
/// saves it, and like that button it needs a restart to show, because most of
/// the window takes its colours when it is built. So the club is asked
/// whether to restart now; no restart now leaves it for the next start. A
/// race or practice running is never restarted under - it is saved for the
/// next start and said in the console.
/// </summary>
public partial class MainForm
{
    private void PutOnSoundscapeLook(Theme theme)
    {
        try
        {
            if (Theme.LookFor(theme) is not { } key || Looks.Find(key) is not { } look)
            {
                return;
            }

            // Already the app's look - running, or saved for the next start.
            if (string.Equals(Looks.ForApp(_settings).Key, look.Key, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            SaveSoundscapeLook(look);
            _looksControl?.RefreshAll();

            var raceUnderWay = _race.IsPracticeMode
                || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;

            if (raceUnderWay)
            {
                OnClientRawMessage(
                    $"{AppTime.LogStampNow()}  [looks] {theme.Name} wears the \"{look.Name}\" look - the app switches to it "
                    + "when HeatCaller next starts, since a race is running.");

                return;
            }

            var restart = AppDialog.Show(
                this,
                $"{theme.Name} wears the \"{look.Name}\" look. The app needs to restart to put it on.\n\nRestart now?",
                "Change the look",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (restart == DialogResult.Yes)
            {
                RestartForNewLook();
                return;
            }

            OnClientRawMessage(
                $"{AppTime.LogStampNow()}  [looks] The app switches to \"{look.Name}\" when HeatCaller next starts.");
        }
        catch (Exception ex)
        {
            // A look is decoration - the soundscape is applied either way.
            OnClientRawMessage($"{AppTime.LogStampNow()}  [looks] Couldn't change the look: {ex.Message}");
        }
    }

    /// <summary>
    /// Saves a look as the app's - the same two settings Themes' "Use for the
    /// App" writes (LooksControl.SaveAppLook), so an older version opened on
    /// this settings file still starts in the nearest look it knows.
    /// </summary>
    private void SaveSoundscapeLook(Look look)
    {
        _settings.AppLook = look.Key;
        _settings.ThemeMode = look.Mode switch
        {
            AppTheme.Mode.Dark => 1,
            AppTheme.Mode.Synthwave => 2,
            _ => 0,
        };
        _settings.Save();
    }
}
