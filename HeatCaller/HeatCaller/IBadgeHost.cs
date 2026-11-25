namespace HeatCaller;

/// <summary>
/// A part that carries its "i" badge inside itself, to the upper right of
/// its words, and leaves room there for it.
///
/// WHY (25 Nov 2026, Asher: "they're too small. go back to older size. put in
/// the pill upper right of text"): on the part's corner the badge hung over
/// the pill's edge, and at 14 px it was too small to find. Inside the pill,
/// just after the words and a little above them, it reads as part of the
/// pill, so it can be the 18 px it was.
/// </summary>
internal interface IBadgeHost
{
    /// <summary>The width of the badge to leave room for after the words, or 0 for none.</summary>
    int BadgeRoom { get; set; }

    /// <summary>Where a badge of <paramref name="badge"/>'s size goes, in the part's own coordinates.</summary>
    Point BadgeSpot(Size badge);
}
