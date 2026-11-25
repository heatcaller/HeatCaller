namespace HeatCaller;

/// <summary>Where each part of the foot of the board goes - see <see cref="BoardFootRow.Place"/>.</summary>
/// <param name="Lefts">Each left-hand part's x, in order.</param>
/// <param name="LabelX">The format line's x.</param>
/// <param name="LabelWidth">The format line's width - shortened to what is left, 0 when there is no room for it.</param>
/// <param name="PenX">The format button's x.</param>
/// <param name="RightX">The Focus button's x: against the right-hand edge.</param>
/// <param name="RightOnSecondRow">The Focus button went under the rest - there was no room beside them.</param>
internal readonly record struct BoardFootPlaces(IReadOnlyList<int> Lefts, int LabelX, int LabelWidth, int PenX, int RightX, bool RightOnSecondRow);

/// <summary>
/// THE FOOT OF RACE CONTROL'S BOARD, OPTION B (24 Nov 2026, asked for by
/// Asher: "this is confusing", option B of six in
/// plans/race-control-foot-plan.md):
///
/// <c>(● On Air) (○ Warm-up) (● Auto Start) | 12 laps · all away together ✎          [ Focus | ^ ]</c>
///
/// The three lit pills from the left, a hairline, the format line and its
/// pen, and the Focus split button against the right-hand edge. The format
/// line gives way first - shortened with "…", its whole text in its tooltip -
/// so the pills and Focus always show whole. Only when even they cannot sit
/// side by side does Focus go under them, still on the right.
///
/// It used to be a ButtonBar of three two-part switches, Format, Expand and
/// the dock square, with the format on a line of its own above.
/// </summary>
internal sealed class BoardFootRow : Panel
{
    private readonly Control[] _lefts;
    private readonly Panel _separator = new();
    private readonly Label _label;
    private readonly Control _pen;
    private readonly Control _right;

    // WHY (25 Nov 2026, Asher: "for all the tool tips here can we put them
    // behind a small circle with an i in it?"): each part's explanation is in
    // a small "i" circle beside it - after each pill, after the pen (for the
    // format line and the pen together), and after Focus - rather than
    // popping up off the part itself whenever the mouse crossed it. Place
    // still works in whole parts: a part with a circle is just that much
    // wider, and the circle is put in the room at its end. See InfoDot.
    private readonly InfoDot?[] _leftDots;
    private readonly InfoDot? _penDot;
    private readonly InfoDot? _rightDot;

    public BoardFootRow(Control[] lefts, Label label, Control pen, Control right,
        InfoDot?[]? leftDots = null, InfoDot? penDot = null, InfoDot? rightDot = null)
    {
        _lefts = lefts;
        _label = label;
        _pen = pen;
        _right = right;
        _leftDots = new InfoDot?[lefts.Length];

        if (leftDots is not null)
        {
            Array.Copy(leftDots, _leftDots, Math.Min(leftDots.Length, lefts.Length));
        }

        _penDot = penDot;
        _rightDot = rightDot;

        LayoutFreeze.Adopt(this);

        // The card's colour, inherited: the theme pass leaves it alone, as it
        // does the ButtonBar this row replaced, or it is painted the page's
        // darker background - a band across the foot of the card.
        Tag = AppTheme.KeepBackColor;
        _separator.Tag = AppTheme.KeepBackColor;

        _label.AutoSize = false;
        _label.AutoEllipsis = true;
        _label.TextAlign = ContentAlignment.MiddleLeft;
        _label.UseMnemonic = false;

        Controls.AddRange(lefts);
        Controls.Add(_separator);
        Controls.Add(_label);
        Controls.Add(_pen);
        Controls.Add(_right);

        foreach (var dot in _leftDots.Append(_penDot).Append(_rightDot))
        {
            if (dot is not null)
            {
                Controls.Add(dot);
            }
        }

        foreach (Control part in Controls)
        {
            part.SizeChanged += (_, _) => PerformLayout();
        }

        _label.TextChanged += (_, _) => PerformLayout();
    }

    /// <summary>The gap between parts.</summary>
    private static int Gap => UiLayout.AtDpi(10);

    /// <summary>The row's height: its tallest part - two rows' worth once Focus has gone under the rest.</summary>
    private int RowHeight => Math.Max(_right.Height, Math.Max(_pen.Height, _lefts.Length == 0 ? 0 : _lefts.Max(c => c.Height)));

    private int LabelWanted => _label.GetPreferredSize(Size.Empty).Width;

    /// <summary>The room a circle takes beside its part, or none.</summary>
    private static int DotRoom(InfoDot? dot) => dot is null ? 0 : InfoDot.Room;

    private List<int> LeftWidths() => _lefts.Select((c, i) => c.Width + DotRoom(_leftDots[i])).ToList();

    private int PenWidth => _pen.Width + DotRoom(_penDot);

    private int RightWidth => _right.Width + DotRoom(_rightDot);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        var places = Place(LeftWidths(), LabelWanted, PenWidth, RightWidth, width, Gap, UiLayout.AtDpi(1));
        var row = RowHeight;
        return new Size(width, places.RightOnSecondRow ? row * 2 + Gap / 2 : row);
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        var row = RowHeight;
        var places = Place(LeftWidths(), LabelWanted, PenWidth, RightWidth, ClientSize.Width, Gap, UiLayout.AtDpi(1));

        int Middle(Control c) => Math.Max(0, (row - c.Height) / 2);
        var dotGap = UiLayout.AtDpi(InfoDot.GapAt100);

        for (var i = 0; i < _lefts.Length; i++)
        {
            Move(_lefts[i], new Point(places.Lefts[i], Middle(_lefts[i])));

            if (_leftDots[i] is { } dot)
            {
                Move(dot, new Point(places.Lefts[i] + _lefts[i].Width + dotGap, Middle(dot)));
            }
        }

        var line = UiLayout.AtDpi(1);
        var sepX = (_lefts.Length == 0 ? 0 : places.Lefts[^1] + _lefts[^1].Width + DotRoom(_leftDots[^1])) + Gap;
        _separator.BackColor = AppTheme.Border;
        _separator.Visible = !places.RightOnSecondRow;
        SetBounds(_separator, new Rectangle(sepX, (row - UiLayout.AtDpi(26)) / 2, line, UiLayout.AtDpi(26)));

        // On two rows, the line, its pen and Focus are all on the second.
        var under = places.RightOnSecondRow ? row + Gap / 2 : 0;

        _label.Visible = places.LabelWidth > 0;
        SetBounds(_label, new Rectangle(places.LabelX, under, Math.Max(0, places.LabelWidth), row));
        Move(_pen, new Point(places.PenX, under + Middle(_pen)));

        if (_penDot is not null)
        {
            Move(_penDot, new Point(places.PenX + _pen.Width + dotGap, under + Middle(_penDot)));
        }

        // Focus's circle after it, at the right-hand edge, as every circle
        // follows its own part - before it, it sat against the format's
        // circle and the two read as one pair.
        Move(_right, new Point(places.RightX, under + Middle(_right)));

        if (_rightDot is not null)
        {
            Move(_rightDot, new Point(places.RightX + _right.Width + dotGap, under + Middle(_rightDot)));
        }

        var height = places.RightOnSecondRow ? row * 2 + Gap / 2 : row;

        if (Height != height)
        {
            Height = height;
        }
    }

    private static void Move(Control c, Point at)
    {
        if (c.Location != at)
        {
            c.Location = at;
        }
    }

    private static void SetBounds(Control c, Rectangle to)
    {
        if (c.Bounds != to)
        {
            c.Bounds = to;
        }
    }

    /// <summary>
    /// Where each part goes in a row <paramref name="available"/> wide: the
    /// left-hand parts in order, a hairline (<paramref name="separator"/>
    /// wide) between them and the format line, the line as wide as it wants
    /// or what is left, its pen after it, and the right-hand part against
    /// the right-hand edge. Pure, for the tests.
    /// </summary>
    internal static BoardFootPlaces Place(IReadOnlyList<int> lefts, int labelWanted, int pen, int right, int available, int gap, int separator)
    {
        var xs = new List<int>(lefts.Count);
        var x = 0;

        foreach (var width in lefts)
        {
            xs.Add(x);
            x += width + gap;
        }

        // The hairline, then the line.
        x += separator + gap;
        var labelX = x;

        var rightX = Math.Max(0, available - right);
        var least = UiLayout.AtDpi(48);

        // One row while the pills, the pen and Focus fit side by side, with a
        // readable line or none.
        if (labelX + pen + gap + right <= available)
        {
            var room = rightX - gap - labelX - pen - gap;
            var labelWidth = Math.Clamp(Math.Min(labelWanted, room), 0, Math.Max(0, labelWanted));

            // Too little to read is none at all - its tooltip still has it.
            if (labelWidth < least)
            {
                labelWidth = 0;
            }

            var penX = labelWidth > 0 ? labelX + labelWidth + gap : labelX;
            return new BoardFootPlaces(xs, labelX, labelWidth, penX, rightX, RightOnSecondRow: false);
        }

        // Not even that: the pills keep the first row, and the line, its pen
        // and Focus go under them - the line from the left, Focus at the
        // right, so the pen is never pushed off the card.
        var underRoom = rightX - gap - pen - gap;
        var underWidth = Math.Clamp(Math.Min(labelWanted, underRoom), 0, Math.Max(0, labelWanted));

        if (underWidth < least)
        {
            underWidth = 0;
        }

        var underPen = underWidth > 0 ? underWidth + gap : 0;
        return new BoardFootPlaces(xs, 0, underWidth, underPen, rightX, RightOnSecondRow: true);
    }
}
