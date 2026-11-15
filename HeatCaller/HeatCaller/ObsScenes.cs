namespace HeatCaller;

/// <summary>
/// The OBS scene for each part of the night (14 Nov 2026, stream overlay
/// plan 6.1) - see AppSettings.ObsSwitchScenes. Blank leaves OBS alone.
/// </summary>
internal static class ObsScenes
{
    public static string For(AppSettings settings, RaceState state) => (state switch
    {
        RaceState.Staging or RaceState.Countdown => settings.ObsSceneStaging,
        RaceState.Racing => settings.ObsSceneRacing,
        RaceState.Finished => settings.ObsSceneFinished,
        _ => settings.ObsSceneBetween,
    } ?? string.Empty).Trim();
}
