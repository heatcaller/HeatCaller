namespace HeatCaller;

/// <summary>
/// Kept for its one constant. The online pages' looks moved into Looks
/// (Looks.cs) in 0.48, where the same set drives the app, the Big Screen,
/// the Wi-Fi pages and the relay.
/// </summary>
public static class SiteThemes
{
    /// <summary>Follow the app's look. Same value as Looks.MatchApp, so saved settings keep meaning the same thing.</summary>
    public const string MatchApp = Looks.MatchApp;
}
