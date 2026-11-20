namespace HeatCaller;

/// <summary>
/// Cards in two columns side by side, each as tall as it needs to be, one
/// under another in its column - or, when the page is narrower than
/// <see cref="TwoColumnsFrom"/>, every card in one column: the left-hand
/// ones, then the right-hand ones (17 Nov 2026, Setup's Tonight tab,
/// mock-up B). The columns share the width equally, a section gap apart.
///
/// Sizes itself, as tall as the taller column: put it in a PageStack or a
/// Section, which gives it the width. A card is placed by hand, as
/// Beside's column places its cards, so it is not left to size itself
/// (a panel would give it its width on one line); and whenever a card lays
/// itself out - a part shown, a line wrapping - the columns are placed
/// again, since only the card sees that.
/// </summary>
internal sealed class CardColumns : Panel
{
    private readonly List<Control> _left = new();
    private readonly List<Control> _right = new();

    public CardColumns()
    {
        // Held while the window is being built - see LayoutFreeze.
        LayoutFreeze.Adopt(this);

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;
    }

    /// <summary>The narrowest the page can be for two columns, asked whenever they are placed - so it can follow the text size.</summary>
    public Func<int> TwoColumnsFrom { get; init; } = () => UiLayout.AtTextSize(1000);

    /// <summary>Adds a card to the bottom of the left-hand column.</summary>
    public T AddLeft<T>(T card) where T : Control => Place(card, _left);

    /// <summary>Adds a card to the bottom of the right-hand column.</summary>
    public T AddRight<T>(T card) where T : Control => Place(card, _right);

    /// <summary>Whether the cards are in two columns at <paramref name="width"/>.</summary>
    public bool IsTwoColumns(int width) => width >= TwoColumnsFrom() && _left.Count > 0 && _right.Count > 0;

    private static int Gap => UiLayout.SectionGap;

    private T Place<T>(T card, List<Control> column) where T : Control
    {
        card.AutoSize = false;
        card.Margin = Padding.Empty;
        column.Add(card);
        Controls.Add(card);

        card.Layout += (_, _) =>
        {
            PerformLayout();
            ArrangeAgainAfterward();
        };
        card.VisibleChanged += (_, _) => PerformLayout();

        return card;
    }

    /// <summary>Set while a second look is waiting in the message queue, so a burst of layouts asks for one.</summary>
    private bool _againQueued;

    /// <summary>
    /// The columns placed once more after the card has finished laying
    /// itself out (19 Nov 2026). A card's Layout event comes at the START of
    /// its layout, so the height asked for straight away can be the one
    /// from before a row inside it wraps: Connection's Race tools card was
    /// given room for its buttons on one line the first time the page was
    /// shown, and Photo Finish, wrapped onto a second line, was cut off
    /// until something laid the page out again. Nothing moves if the height
    /// was right the first time (Arrange only sets bounds that differ).
    /// </summary>
    private void ArrangeAgainAfterward()
    {
        if (_againQueued || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        _againQueued = true;

        BeginInvoke(() =>
        {
            _againQueued = false;

            if (!IsDisposed)
            {
                PerformLayout();
            }
        });
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width;

        return new Size(width, Arrange(Math.Max(1, width)).Height);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        // Asks to be given its new height first, as Beside does in a
        // PageStack: asked after, the stack would resize this while it was
        // still laying out, and that layout is dropped.
        var (height, bounds) = Arrange(Math.Max(1, ClientSize.Width));

        if (AutoSize && Parent is not null && Height != height)
        {
            Parent.PerformLayout(this, nameof(PreferredSize));
        }

        foreach (var (card, place) in bounds)
        {
            if (card.Bounds != place)
            {
                card.Bounds = place;
            }
        }

        base.OnLayout(levent);
    }

    /// <summary>Coming into view: while hidden, a card hidden in its own right could not be told from the rest.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            PerformLayout();
        }
    }

    /// <summary>Where every card shown goes at <paramref name="width"/>, and how tall that comes to.</summary>
    private (int Height, List<(Control Card, Rectangle Bounds)> Bounds) Arrange(int width)
    {
        var bounds = new List<(Control, Rectangle)>();

        if (IsTwoColumns(width))
        {
            var columnWidth = Math.Max(1, (width - Gap) / 2);
            var left = Stack(_left, 0, 0, columnWidth, bounds);
            var right = Stack(_right, width - columnWidth, 0, columnWidth, bounds);

            return (Math.Max(left, right), bounds);
        }

        var top = Stack(_left, 0, 0, width, bounds);

        if (top > 0 && _right.Any(Counts))
        {
            top += Gap;
        }

        top = Stack(_right, 0, top, width, bounds);

        return (top, bounds);
    }

    /// <summary>Cards one under another from <paramref name="top"/>; where the last one ends.</summary>
    private int Stack(List<Control> cards, int left, int top, int width, List<(Control, Rectangle)> bounds)
    {
        var y = top;
        var first = true;

        foreach (var card in cards.Where(Counts))
        {
            if (!first)
            {
                y += Gap;
            }

            var height = card.GetPreferredSize(new Size(width, 0)).Height;

            bounds.Add((card, new Rectangle(left, y, width, height)));
            y += height;
            first = false;
        }

        return y;
    }

    /// <summary>Hidden in its own right leaves a card out - askable only while this is on screen, as in Pieces.TrailingGap.</summary>
    private bool Counts(Control card) => !Visible || card.Visible;
}
