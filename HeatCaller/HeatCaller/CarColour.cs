namespace HeatCaller;

/// <summary>
/// A car's body colour as it is kept and sent (14 Nov 2026, stream overlay
/// plan 6.5): "#RRGGBB" in capitals, or empty for none. Anything else - a
/// hand-edited profile, a backup from elsewhere - is treated as none, so the
/// stream never draws a CSS value somebody typed.
/// </summary>
internal static class CarColour
{
    /// <summary>The colour in the one shape that is kept, or empty.</summary>
    public static string Clean(string? text)
    {
        var t = (text ?? string.Empty).Trim();

        if (t.Length != 7 || t[0] != '#')
        {
            return string.Empty;
        }

        for (var i = 1; i < 7; i++)
        {
            if (!Uri.IsHexDigit(t[i]))
            {
                return string.Empty;
            }
        }

        return t.ToUpperInvariant();
    }

    public static string FromColour(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";

    public static Color? ToColour(string? text) =>
        Clean(text) is { Length: 7 } hex ? ColorTranslator.FromHtml(hex) : null;
}
