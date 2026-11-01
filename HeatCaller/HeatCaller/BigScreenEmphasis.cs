using System.Drawing;

namespace HeatCaller;

/// <summary>
/// How the Big Screen colours a board cell: the quickest of anyone in the
/// look's accent, a driver's own good news in its "good" colour, and
/// nothing else at all - from across the room a warned driver shouldn't
/// turn the board red.
/// </summary>
public static class BigScreenEmphasis
{
    /// <summary>The colour for <paramref name="emphasis"/>, or null for plain text.</summary>
    public static Color? Colour(KpiEmphasis emphasis, Color accent, Color good) => emphasis switch
    {
        KpiEmphasis.Highlight => accent,
        KpiEmphasis.Good => good,
        _ => null,
    };
}
