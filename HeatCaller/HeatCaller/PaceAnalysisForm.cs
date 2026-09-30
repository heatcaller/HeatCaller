namespace HeatCaller;

/// <summary>
/// A window around <see cref="PaceAnalysisControl"/>, for opening the pace
/// table on one particular race straight after it finishes.
///
/// The table itself moved into a control when Race Analytics gained a Pace
/// tab. The two views differ by a combo box - which is not a reason to keep
/// two copies of a table with twelve columns and a paragraph of tooltips in
/// it - so this is now a frame and nothing else.
///
/// On the layout pieces (DialogFit): the pace view taking the whole window,
/// never less than the least it can do with - its summary, and the table's
/// headings and a few drivers - and no buttons, as it never had any. It
/// opens at the size it had, at the text size in use, and can be made
/// bigger, or as narrow as it always could be. It used to be the view docked to fill a window 1120x620 inside
/// its frame at every text size, which could be made as small as 880x460
/// frame and all.
/// </summary>
public sealed class PaceAnalysisForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size - the width it had.</summary>
    private const int DialogWidth = 1120;

    /// <summary>How tall it opens inside its frame at the normal text size - the height it had.</summary>
    private const int DialogHeight = 620;

    /// <summary>
    /// The pace view's least height at the normal text size - its summary,
    /// and the table's headings and four drivers, about - for the one case
    /// its own page cannot be asked (see the constructor).
    /// </summary>
    private const int PaceLeastHeight = 300;

    /// <summary>How narrow the window can be made, frame and all, at the normal text size - as narrow as it always could be.</summary>
    private const int LeastWindowWidth = 880;

    public PaceAnalysisForm(SessionRecord session)
    {
        Text = $"Pace - {(string.IsNullOrWhiteSpace(session.Label) ? "Race" : session.Label)}";

        var pace = new PaceAnalysisControl(session);

        // The least the view can do with at the width it is given is its
        // own page's - the view is one FillPage, laid out as on Race
        // Analytics' Pace tab - so that page is asked, as the view does not
        // say it itself. Should the view ever be built some other way, the
        // least falls back to a fixed few rows' worth at the text size in
        // use rather than to nothing.
        var inner = pace.Controls.OfType<FillPage>().FirstOrDefault();

        // No margins round the view: it keeps its own, as it does on Race
        // Analytics, and this page's on top of them would put the table
        // twice as far from the window's edges.
        var page = new FillPage { Padding = Padding.Empty };
        page.AddFill(
            pace,
            width => inner?.LeastHeight(width) ?? UiLayout.AtTextSize(PaceLeastHeight),
            gapBelow: 0);

        Controls.Add(page);

        AppTheme.ApplyRecursively(this);

        // The size it had at the normal text size, at the size in use. It
        // can be made bigger - the table is twelve columns wide.
        DialogFit.Keep(this, page, width: DialogWidth, height: DialogHeight);

        // And as narrow as it always could be, at the text size in use:
        // nothing in it is measured for its width - the table scrolls
        // sideways in its own box and the summary wraps. After DialogFit's
        // own Load handler, added above, which sets the least to the width
        // it opened at.
        Load += (_, _) => MinimumSize = new Size(
            Math.Min(MinimumSize.Width, UiLayout.AtTextSize(LeastWindowWidth)),
            MinimumSize.Height);
    }
}
