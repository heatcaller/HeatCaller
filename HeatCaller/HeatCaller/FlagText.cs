namespace HeatCaller;

/// <summary>
/// A flag as people read it (14 Nov 2026, with the warm-up flag): "Green",
/// "Checkered", "Warm-up". Every screen that names the flag goes through
/// here, so the warm-up reads "Warm-up" - with its hyphen, as the rest of
/// the app writes it - and never the enum's own "WarmUp".
///
/// The web feed keeps sending the enum's name (WebServerService), which the
/// pages turn into a style class: that is a wire value, not words.
/// </summary>
internal static class FlagText
{
    /// <summary>The warm-up's blue - the clock's ring during a warm-up, the Warm-up button, the flag label.</summary>
    internal static readonly Color WarmUpColour = Color.FromArgb(79, 142, 247);

    /// <summary>The flag's name, as a word: "Green", "Warm-up". "None" stays "None", as the label always said.</summary>
    internal static string Name(FlagState flag) => flag == FlagState.WarmUp ? "Warm-up" : flag.ToString();

    /// <summary>"GREEN FLAG", "WARM-UP FLAG" - the boards' line while a flag is out.</summary>
    internal static string Shouted(FlagState flag) => $"{Name(flag).ToUpperInvariant()} FLAG";
}
