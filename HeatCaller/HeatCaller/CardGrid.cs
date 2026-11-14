namespace HeatCaller;

/// <summary>
/// Cards side by side in rows, as many to a row as fit (14 Nov 2026, Setup's
/// template cards): three on a wide window, two at a larger text size or on a
/// 1280-wide screen, one when narrow. The cards in a row are as tall as the
/// tallest of them, so their buttons line up along the bottom.
///
/// Sizes itself like Section: asked how tall it is at a width, it works it
/// out card by card at the width each card will have, so the page column it
/// sits in gives it exactly that.
/// </summary>
internal sealed class CardGrid : Panel
{
    /// <summary>The least width a card is given before the grid drops to fewer columns, at the normal text size.</summary>
    public int LeastCardWidth { get; set; } = 300;

    /// <summary>The most cards to a row.</summary>
    public int MostColumns { get; set; } = 3;

    public CardGrid()
    {
        LayoutFreeze.Adopt(this);

        AutoSize = true;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;
    }

    private static int Gap => UiLayout.AtDpi(UiLayout.RowGap + 4);

    private IReadOnlyList<Control> Cards => Controls.Cast<Control>().Where(c => c.Visible || !Visible).ToList();

    /// <summary>How many to a row at <paramref name="width"/>.</summary>
    internal int ColumnsAt(int width)
    {
        var least = UiLayout.AtTextSize(LeastCardWidth);
        var columns = Math.Max(1, (width + Gap) / Math.Max(1, least + Gap));

        return Math.Clamp(columns, 1, Math.Max(1, MostColumns));
    }

    private int CardWidth(int width, int columns) => Math.Max(1, (width - Gap * (columns - 1)) / columns);

    /// <summary>Each card's place at <paramref name="width"/>.</summary>
    private List<(Control Card, Rectangle Bounds)> Arrange(int width)
    {
        var placed = new List<(Control, Rectangle)>();
        var cards = Cards;

        if (cards.Count == 0)
        {
            return placed;
        }

        var columns = Math.Min(ColumnsAt(width), cards.Count);
        var cardWidth = CardWidth(width, columns);
        var y = 0;

        for (var start = 0; start < cards.Count; start += columns)
        {
            var row = cards.Skip(start).Take(columns).ToList();
            var height = row.Max(card => card.GetPreferredSize(new Size(cardWidth, 0)).Height);

            for (var i = 0; i < row.Count; i++)
            {
                placed.Add((row[i], new Rectangle(i * (cardWidth + Gap), y, cardWidth, height)));
            }

            y += height + Gap;
        }

        return placed;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Math.Max(Width, UiLayout.AtTextSize(LeastCardWidth));
        var placed = Arrange(width);

        return new Size(width, placed.Count == 0 ? 0 : placed.Max(p => p.Bounds.Bottom));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        foreach (var (card, bounds) in Arrange(Math.Max(1, ClientSize.Width)))
        {
            card.AutoSize = false;

            if (card.Bounds != bounds)
            {
                card.Bounds = bounds;
            }
        }
    }

    /// <summary>Takes every card out, disposing them - before the grid is filled again.</summary>
    public void Clear()
    {
        SuspendLayout();

        try
        {
            foreach (var card in Controls.Cast<Control>().ToList())
            {
                Controls.Remove(card);
                card.Dispose();
            }
        }
        finally
        {
            ResumeLayout(false);
        }
    }
}
