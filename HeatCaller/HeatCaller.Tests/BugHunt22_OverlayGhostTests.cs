namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 22 (20 Nov 2026), the overlay editor's picture: a graphic "not
/// showing now" is hidden by its CSS (Championship is display:none) or
/// pushed off the picture (Fastest lap's translateX), and its box is drawn
/// clamped and padded. Dragging it moved nothing, and the place saved came
/// from the hidden node - Championship into the very corner, Fastest lap
/// 18 vw off the right edge. The box itself is now moved and saved.
/// </summary>
public sealed class BugHunt22_OverlayGhostTests
{
    private static readonly string Script = WebServerService.OverlayEditScript;

    [Fact]
    public void A_ghost_is_dragged_by_its_box_and_saved_where_the_box_was_dropped()
    {
        Assert.Contains("function isGhost(k){ return !!(boxes[k] && boxes[k].classList.contains('gh')); }", Script);
        Assert.Contains("w0: (ghost ? b0.width : node.getBoundingClientRect().width) || 1", Script);
        Assert.Contains("drag.b.style.left = (r0.left + dx) + 'px';", Script);
        Assert.Contains("if (d.ghost) finishAt(d.k, d.b.getBoundingClientRect(), scale); else finish(d.k, d.node, scale);", Script);
    }

    [Fact]
    public void A_ghost_is_nudged_from_its_box_too()
    {
        Assert.Contains("const base = gn._hcGhostRect || boxes[gk].getBoundingClientRect();", Script);
        Assert.Contains("finishAt(gk, r, scaleOf(gk))", Script);
    }

    [Fact]
    public void A_graphic_on_show_is_still_saved_from_where_it_really_is()
    {
        Assert.Contains("function finish(k, node, scale){ finishAt(k, node.getBoundingClientRect(), scale); }", Script);
    }
}
