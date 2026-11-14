using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// The rows of the Championship page's "Can they still win?" card (13 Nov
/// 2026): each driver's place, name, gap to the leader, and a chip - Leading,
/// Still in it, Out of reach, or Champion once the season is over. The
/// drivers who can still win come first in the table anyway; past the first
/// few out of reach, the rest are one line ("and 6 more out of reach").
/// </summary>
internal sealed class TitleRaceView : Control
{
    /// <summary>Level: the season is over with more than one driver level on points at the top.</summary>
    public enum State { Leading, StillIn, OutOfReach, Champion, Level }

    public sealed record Row(int Place, string Driver, int Points, int Gap, State State);

    private IReadOnlyList<Row> _rows = Array.Empty<Row>();

    /// <summary>How many drivers out of reach get a row of their own before the rest are counted.</summary>
    private const int OutOfReachShown = 3;

    public TitleRaceView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        Tag = AppTheme.KeepBackColor;
        AutoSize = true;
        Margin = Padding.Empty;
        AccessibleRole = AccessibleRole.List;
    }

    public IReadOnlyList<Row> Rows => _rows;

    public void SetRows(IReadOnlyList<Row> rows)
    {
        if (_rows.SequenceEqual(rows))
        {
            return;
        }

        var before = Shown().Count + (Hidden() > 0 ? 1 : 0);
        _rows = rows.ToList();
        AccessibleName = string.Join(", ", _rows.Select(r => $"{r.Driver} {Words(r.State)}"));

        if (Shown().Count + (Hidden() > 0 ? 1 : 0) != before)
        {
            Parent?.PerformLayout();
        }

        Invalidate();
    }

    public void Remeasure() => Parent?.PerformLayout();

    /// <summary>Everybody who can still win, and the first few who can't.</summary>
    private List<Row> Shown()
    {
        var shown = _rows.Where(r => r.State != State.OutOfReach).ToList();
        shown.AddRange(_rows.Where(r => r.State == State.OutOfReach).Take(OutOfReachShown));
        return shown.OrderBy(r => r.Place).ToList();
    }

    private int Hidden() => Math.Max(0, _rows.Count(r => r.State == State.OutOfReach) - OutOfReachShown);

    private static int RowHeight => AppTheme.BoldFont.Height + UiLayout.AtDpi(14);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var lines = Shown().Count + (Hidden() > 0 ? 1 : 0);
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width;
        return new Size(width, lines * RowHeight);
    }

    private static string Words(State state) => state switch
    {
        State.Leading => "Leading",
        State.StillIn => "Still in it",
        State.Champion => "Champion",
        State.Level => "Level at the top",
        _ => "Out of reach",
    };

    private static Color Tone(State state) => state switch
    {
        State.Leading => AppTheme.Success,
        State.StillIn => AppTheme.Warning,
        State.Champion => AppTheme.Warning,
        State.Level => AppTheme.Warning,
        _ => AppTheme.TextMuted,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        const TextFormatFlags line = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
        var y = 0;
        var placeSide = AppTheme.BoldFont.Height + UiLayout.AtDpi(4);

        using var rule = new Pen(AppTheme.Border);

        foreach (var row in Shown())
        {
            var box = new Rectangle(0, y + (RowHeight - placeSide) / 2, placeSide, placeSide);
            var medal = row.Place switch
            {
                1 => Color.FromArgb(0xFF, 0xCF, 0x5A),
                2 => Color.FromArgb(0xC9, 0xC4, 0xDD),
                3 => Color.FromArgb(0xD9, 0x89, 0x5A),
                _ => AppTheme.SecondaryButton,
            };

            using (var path = AppTheme.RoundedRect(box, UiLayout.AtDpi(5)))
            using (var fill = new SolidBrush(medal))
            {
                g.FillPath(fill, path);
            }

            TextRenderer.DrawText(g, row.Place.ToString(), AppTheme.BoldFont, box, row.Place <= 3 ? Color.FromArgb(0x24, 0x1A, 0x10) : AppTheme.TextDark,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            var words = Words(row.State);
            var chipWidth = TextRenderer.MeasureText(g, words, AppTheme.SmallFont, Size.Empty, TextFormatFlags.NoPadding).Width + UiLayout.AtDpi(34);
            var gap = row.Place == 1 ? $"{row.Points}" : $"−{-row.Gap}";
            var gapWidth = TextRenderer.MeasureText(g, "−999", AppTheme.SmallFont, Size.Empty, TextFormatFlags.NoPadding).Width + UiLayout.AtDpi(8);

            var nameX = box.Right + UiLayout.AtDpi(10);
            var nameWidth = Math.Max(1, Width - nameX - chipWidth - gapWidth - UiLayout.AtDpi(8));

            TextRenderer.DrawText(g, row.Driver, AppTheme.BoldFont, new Rectangle(nameX, y, nameWidth, RowHeight),
                row.State == State.OutOfReach ? AppTheme.TextMuted : AppTheme.TextDark, line);

            TextRenderer.DrawText(g, gap, AppTheme.SmallFont, new Rectangle(nameX + nameWidth, y, gapWidth, RowHeight), AppTheme.TextMuted,
                line | TextFormatFlags.Right);

            RosterInk.DrawChip(g, new Rectangle(Width - chipWidth, y, chipWidth, RowHeight), words, Tone(row.State), AppTheme.SmallFont);

            y += RowHeight;
            g.DrawLine(rule, 0, y - 1, Width, y - 1);
        }

        if (Hidden() > 0)
        {
            TextRenderer.DrawText(g, $"and {Hidden()} more out of reach", AppTheme.SmallFont, new Rectangle(0, y, Width, RowHeight), AppTheme.TextMuted, line);
        }
    }
}
