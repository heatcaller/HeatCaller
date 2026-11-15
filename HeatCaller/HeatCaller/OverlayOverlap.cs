namespace HeatCaller;

/// <summary>
/// Which graphics of a layout would sit on top of each other (14 Nov 2026,
/// stream overlay plan section 4, step 5). Worked out from a rough box for
/// each graphic - its usual size, in hundredths of the picture's width -
/// where the layout puts it, and only for two that show in the same part
/// of the night. A warning in the editor, never a refusal: a club may mean
/// the sponsor's logo to sit on the corner bug.
/// </summary>
internal static class OverlayOverlap
{
    /// <summary>A graphic's usual size, width and height in vw (the picture is 100 wide, 56.25 tall).</summary>
    internal static (double W, double H) SizeOf(OverlayElementKind kind) => kind switch
    {
        OverlayElementKind.Tower => (21, 24),
        OverlayElementKind.Battle => (34, 5),
        OverlayElementKind.Fastest => (18, 5),
        OverlayElementKind.Moment => (34, 7),
        OverlayElementKind.Grid or OverlayElementKind.Podium => (32, 20),
        OverlayElementKind.Champ => (21, 4),
        OverlayElementKind.Captions => (50, 4),
        OverlayElementKind.Status => (20, 3.5),
        OverlayElementKind.Ticker => (92, 4),
        OverlayElementKind.Bug => (19, 8),
        OverlayElementKind.Sponsor => (16, 6),
        OverlayElementKind.ClubLogo => (14, 6.5),
        OverlayElementKind.Schedule => (44, 20),
        OverlayElementKind.Basics => (56, 8),
        OverlayElementKind.Mark => (10, 3),
        _ => (10, 5),
    };

    /// <summary>Where a graphic left where it usually goes sits, as the broadcast page puts it.</summary>
    private static (OverlayAnchor Anchor, double X, double Y) UsualPlace(OverlayElementKind kind) => kind switch
    {
        OverlayElementKind.Tower => (OverlayAnchor.TopLeft, 2, 3),
        OverlayElementKind.Champ => (OverlayAnchor.TopLeft, 2, 28),
        OverlayElementKind.Battle => (OverlayAnchor.Bottom, 0, 3),
        OverlayElementKind.Fastest => (OverlayAnchor.BottomRight, 2, 3),
        OverlayElementKind.Moment => (OverlayAnchor.Top, 0, 3),
        OverlayElementKind.Grid or OverlayElementKind.Podium or OverlayElementKind.Schedule => (OverlayAnchor.Middle, 0, 0),
        OverlayElementKind.Captions => (OverlayAnchor.Bottom, 0, 0.8),
        OverlayElementKind.Status or OverlayElementKind.Bug or OverlayElementKind.Sponsor => (OverlayAnchor.TopRight, 2, 2),
        OverlayElementKind.Ticker => (OverlayAnchor.Bottom, 0, 2),
        OverlayElementKind.ClubLogo => (OverlayAnchor.TopLeft, 2, 2),
        OverlayElementKind.Basics => (OverlayAnchor.Bottom, 0, 3),
        OverlayElementKind.Mark => (OverlayAnchor.BottomRight, 2, 2),
        _ => (OverlayAnchor.Middle, 0, 0),
    };

    private const double Height = 56.25;

    /// <summary>The box a graphic takes: left, top, right, bottom, in vw.</summary>
    internal static (double L, double T, double R, double B) BoxOf(OverlayElement e)
    {
        var (w, h) = SizeOf(e.Kind);
        var placed = e.Anchor != OverlayAnchor.Default;
        var (anchor, x, y) = placed ? (e.Anchor, e.X, e.Y) : UsualPlace(e.Kind);

        if (placed)
        {
            w *= e.Scale;
            h *= e.Scale;
        }

        var left = anchor switch
        {
            OverlayAnchor.TopLeft or OverlayAnchor.Left or OverlayAnchor.BottomLeft => x,
            OverlayAnchor.TopRight or OverlayAnchor.Right or OverlayAnchor.BottomRight => 100 - x - w,
            _ => 50 - w / 2,
        };

        var top = anchor switch
        {
            OverlayAnchor.TopLeft or OverlayAnchor.Top or OverlayAnchor.TopRight => y,
            OverlayAnchor.BottomLeft or OverlayAnchor.Bottom or OverlayAnchor.BottomRight => Height - y - h,
            _ => Height / 2 - h / 2,
        };

        return (left, top, left + w, top + h);
    }

    private static readonly (OverlayDuring Part, string Words)[] Parts =
    {
        (OverlayDuring.Staging, "on the grid"),
        (OverlayDuring.Racing, "while racing"),
        (OverlayDuring.Finished, "at the result"),
        (OverlayDuring.Between, "between races"),
    };

    /// <summary>Whether a graphic can show in this part of the night.</summary>
    private static bool ShowsIn(OverlayElement e, OverlayDuring part) =>
        e.During == OverlayDuring.Always ? NaturallyIn(e.Kind, part) : e.During.HasFlag(part);

    /// <summary>The parts of the night a graphic left to "whenever" actually has something to show in.</summary>
    private static bool NaturallyIn(OverlayElementKind kind, OverlayDuring part) => kind switch
    {
        OverlayElementKind.Grid => part == OverlayDuring.Staging,
        OverlayElementKind.Podium => part == OverlayDuring.Finished,
        OverlayElementKind.Battle or OverlayElementKind.Fastest or OverlayElementKind.Champ => part == OverlayDuring.Racing,
        OverlayElementKind.Tower or OverlayElementKind.Ticker => part is OverlayDuring.Racing or OverlayDuring.Finished,
        OverlayElementKind.Schedule or OverlayElementKind.Basics => part == OverlayDuring.Between,
        _ => true,
    };

    /// <summary>"Corner bug and Sponsor logo overlap while racing." - every pair that would, once each.</summary>
    internal static IReadOnlyList<string> Warnings(OverlayLayout layout, Func<OverlayElementKind, string> wordsOf)
    {
        // Not the captions (a strip along the very bottom, on top of nothing
        // that matters), and not the mark where it usually goes - in the
        // pylon's foot, not a box of its own.
        var on = layout.Elements.Where(e => e.On && e.Kind != OverlayElementKind.Captions
                                           && !(e.Kind == OverlayElementKind.Mark && e.Anchor == OverlayAnchor.Default)).ToList();
        var said = new List<string>();

        for (var i = 0; i < on.Count; i++)
        {
            for (var j = i + 1; j < on.Count; j++)
            {
                var a = BoxOf(on[i]);
                var b = BoxOf(on[j]);
                var overlap = a.L < b.R && b.L < a.R && a.T < b.B && b.T < a.B;

                if (!overlap)
                {
                    continue;
                }

                var when = Parts.Where(p => ShowsIn(on[i], p.Part) && ShowsIn(on[j], p.Part)).Select(p => p.Words).ToList();

                if (when.Count > 0)
                {
                    said.Add($"{wordsOf(on[i].Kind)} and {wordsOf(on[j].Kind)} overlap {string.Join(" and ", when)}.");
                }
            }
        }

        return said;
    }
}
