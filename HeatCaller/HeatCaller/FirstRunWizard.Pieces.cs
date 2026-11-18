using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// The wizard's own pieces (18 Nov 2026, dialog jazz-up): a step on the
/// rail with a numbered mark before its name, and the bar over the step
/// that shows how far through setup it is. Only the wizard uses them.
/// </summary>
public sealed partial class FirstRunWizard
{
    /// <summary>Where a step on the rail stands against the one on show.</summary>
    private enum RailState
    {
        Done,
        Current,
        Ahead,
    }

    /// <summary>The mark's size and the room after it, at the normal text size.</summary>
    private const int RailMarkSize = 22;

    private const int RailMarkGap = 10;

    /// <summary>
    /// A step's name on the rail, with a round mark in the room before it:
    /// its number (the welcome and the last page a dot), filled in the
    /// accent while it is the step on show, a tick once it is behind, and
    /// an outline while it is still ahead. The name itself is the label's
    /// own text, drawn by the label after the room its padding keeps.
    /// </summary>
    private sealed class RailStep : Label
    {
        /// <summary>The number in the mark - empty for a dot.</summary>
        public string Mark { get; init; } = string.Empty;

        public RailState State { get; set; } = RailState.Ahead;

        /// <summary>Where the mark starts - the rail's own inset.</summary>
        public int MarkLeft { get; init; }

        public RailStep()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var size = UiLayout.AtTextSize(RailMarkSize);
            var mark = new Rectangle(MarkLeft, (Height - size) / 2, size, size);
            var g = e.Graphics;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            switch (State)
            {
                case RailState.Current:
                {
                    using var fill = new SolidBrush(AppTheme.Primary);
                    g.FillEllipse(fill, mark);
                    DrawMark(g, mark, AppTheme.OnPrimary);
                    break;
                }

                case RailState.Done:
                {
                    using var fill = new SolidBrush(AppTheme.SidebarBorder);
                    g.FillEllipse(fill, mark);
                    DrawTick(g, mark, AppTheme.SidebarText);
                    break;
                }

                default:
                {
                    using var ring = new Pen(AppTheme.SidebarTextMuted, Math.Max(1f, UiLayout.AtDpi(1) * 1.5f));
                    g.DrawEllipse(ring, mark.X + 1, mark.Y + 1, mark.Width - 2, mark.Height - 2);
                    DrawMark(g, mark, AppTheme.SidebarTextMuted);
                    break;
                }
            }
        }

        /// <summary>The number, centred in the mark - or a dot when there is none.</summary>
        private void DrawMark(Graphics g, Rectangle mark, Color colour)
        {
            if (Mark.Length == 0)
            {
                var dot = Math.Max(4, mark.Width / 4);
                using var fill = new SolidBrush(colour);
                g.FillEllipse(fill, mark.X + (mark.Width - dot) / 2, mark.Y + (mark.Height - dot) / 2, dot, dot);
                return;
            }

            TextRenderer.DrawText(g, Mark, AppTheme.MicroFont, mark, colour,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        /// <summary>A tick, drawn rather than typed - the faces in use do not all have one.</summary>
        private static void DrawTick(Graphics g, Rectangle mark, Color colour)
        {
            using var pen = new Pen(colour, Math.Max(1.5f, mark.Width / 9f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };

            var w = mark.Width;
            g.DrawLines(pen, new[]
            {
                new PointF(mark.X + w * 0.28f, mark.Y + w * 0.52f),
                new PointF(mark.X + w * 0.44f, mark.Y + w * 0.68f),
                new PointF(mark.X + w * 0.73f, mark.Y + w * 0.36f),
            });
        }
    }

    /// <summary>
    /// How far through setup it is: a segment for each counted step, those
    /// done and the one on show in the accent, the rest in the border's
    /// colour. As tall as it is told; as wide as the page gives it.
    /// </summary>
    private sealed class StepTrack : Control
    {
        public int Count { get; init; } = 1;

        /// <summary>The step on show, counted from 1.</summary>
        public int Current
        {
            get => _current;
            set
            {
                if (_current != value)
                {
                    _current = value;
                    Invalidate();
                }
            }
        }

        private int _current;

        public StepTrack()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Tag = AppTheme.KeepBackColor;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
            {
                g.FillRectangle(behind, ClientRectangle);
            }

            if (Count <= 0 || Width <= 0)
            {
                return;
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;

            var gap = UiLayout.AtDpi(6);
            var room = Math.Max(0, Width - gap * (Count - 1));

            for (var i = 0; i < Count; i++)
            {
                var left = i * (room / Count) + i * gap;
                var width = i == Count - 1 ? Width - left : room / Count;
                var colour = i < _current ? AppTheme.Primary : AppTheme.Border;

                using var fill = new SolidBrush(colour);
                using var path = Rounded(new RectangleF(left, 0, Math.Max(1, width), Height), Height / 2f);
                g.FillPath(fill, path);
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));

            if (d <= 0)
            {
                path.AddRectangle(r);
                return path;
            }

            path.AddArc(r.X, r.Y, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            path.CloseFigure();
            return path;
        }
    }
}
