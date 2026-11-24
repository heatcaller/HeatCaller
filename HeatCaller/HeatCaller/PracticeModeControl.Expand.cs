namespace HeatCaller;

/// <summary>
/// Practice's Expand and Restore (21 Nov 2026).
///
/// THE FOLD ROLLS UP. The Setup and Ghost cars band and everything under the
/// clock (Open Big Screen and the tiles) roll up together, each into the top
/// of its own room, while the session table rises and grows into it. Pressed
/// again, they roll back down. It used to be Race Control's slide
/// (Beside.SlideTo), which slides only the column under the clock: the two
/// cards just vanished, and it was choppy, because the session table was
/// laid out again at every frame of the sidebar narrowing beside it.
///
/// What makes it smooth: for the length of the fold, each part that folds,
/// and the session table, is a picture of itself (FoldPanel, GridStandIn).
/// Moving and clipping a picture costs next to nothing; laying out the
/// table's rows, the setup fields and the ghost list again sixty times a
/// second was what made the frames stutter. The real controls are back the
/// moment it ends. One timer drives both folds, so they move as one.
/// </summary>
public sealed partial class PracticeModeControl
{
    /// <summary>How long the fold takes: as long as the sidebar's slide (MainForm.NavSlide), so the two move together.</summary>
    private const double FoldMs = 280;

    /// <summary>The Setup and Ghost cars band, in the cards' column.</summary>
    private FoldPanel? _cardsFold;

    /// <summary>Open Big Screen and the tiles, in the clock's column under the clock.</summary>
    private FoldPanel? _sideFold;

    /// <summary>The page's Beside: the cards, the clock column, and the table under both.</summary>
    private Beside? _body;

    /// <summary>The session card's picture, held while the fold runs.</summary>
    private SessionStandIn? _sessionStandIn;

    /// <summary>The panel the session card fills - the Beside's filling part, which the fold moves.</summary>
    private Control? _sessionHolder;

    private System.Windows.Forms.Timer? _foldTimer;
    private readonly SlideTime _foldClock = new();
    private bool _finerFoldTimer;
    private double _foldFrom = 1;
    private double _foldTo = 1;

    /// <summary>Where each moving part starts and ends, worked out once as the fold begins - the frames only move them.</summary>
    private readonly Dictionary<Control, (Rectangle From, Rectangle To)> _foldPaths = new();

    /// <summary>The panels whose layout is held while the fold runs.</summary>
    private readonly List<Control> _heldLayouts = new();

    private IEnumerable<FoldPanel> Folds => new[] { _cardsFold, _sideFold }.OfType<FoldPanel>();

    /// <summary>Folded or open at once, as AppSettings.PracticeExpanded says - as the page is built, and with no window to slide in.</summary>
    private void ShowFolded()
    {
        var open = _settings.PracticeExpanded ? 0d : 1d;

        FinishFold(open);
        ShowExpandCaption();
    }

    /// <summary>
    /// Rolls the folds up or down to what AppSettings.PracticeExpanded says.
    /// Pressed again part of the way, it turns round from where it is.
    ///
    /// Everything is worked out here, once: the page laid out where the fold
    /// starts and where it ends, with pictures standing in for what moves.
    /// The frames then only move three pictures - the two folds and the
    /// session card - and lay nothing out. Run after the sidebar has taken
    /// its new width (MainForm.SlideNavIconsOnly lays the page out at it
    /// before its first frame), so the pictures are taken at the width the
    /// page will have.
    /// </summary>
    private void SlideFolded()
    {
        if (_body is null || _cardsFold is null || _sessionHolder is null || !_body.IsHandleCreated || !_body.Visible)
        {
            ShowFolded();
            return;
        }

        var from = _foldTimer is not null
            ? _cardsFold.Open
            : _cardsFold.Visible ? _cardsFold.Open : 0d;

        var to = _settings.PracticeExpanded ? 0d : 1d;

        // WHY (24 Nov 2026, Asher: "when restoring the screen on practice I
        // see native scroll bars flash during the animation"): working the
        // paths out lays the page out at the start, at the end and at the
        // start again, and at the end the page needed more than its height
        // for a moment - its scrolling panel turned Windows' own scroll bar
        // on, drawn on screen before the slim bar covered it. All of it is
        // done with painting held, and drawn once - see ShowWithoutFlash.
        ShowWithoutFlash.Hold(this, () => StartFold(from, to));

        _foldFrom = from;
        _foldTo = to;
        _foldClock.Reset();

        // Sixty-odd frames a second rather than Windows' usual 64 ticks
        // with gaps - see Beside.FrameMs.
        _finerFoldTimer = TimeBeginPeriod(1) == 0;
        _foldTimer = new System.Windows.Forms.Timer { Interval = 10 };
        _foldTimer.Tick += (_, _) => FoldFrame();
        _foldTimer.Start();

        ShowExpandCaption();
    }

    /// <summary>SlideFolded's setting up, with painting held: the pictures in, and each moving part's way worked out.</summary>
    private void StartFold(double from, double to)
    {
        if (_body is null || _cardsFold is null || _sessionHolder is null)
        {
            return;
        }

        // A fold still running is stopped where it is, laid out for real.
        FinishFold(from);

        // Coming back: in the layout again, at nothing, so each is laid out
        // at its column's width before its picture is taken.
        foreach (var fold in Folds)
        {
            if (!fold.Visible)
            {
                fold.Open = 0;
                fold.Visible = true;
            }
        }

        LayOutAt(from);

        // Every slim bar over its native one before the pictures are taken
        // (24 Nov 2026): a list in a fold coming back from hidden has just
        // been laid out, and a picture of it taken with Windows' own bar
        // showing would have shown that bar for the whole fold.
        ThinScrollBar.SyncNow();

        foreach (var fold in Folds)
        {
            fold.HoldPicture();
        }

        _sessionStandIn?.Hold();

        // Where each moving part is now, then where it goes - and back to
        // the start, all with the pictures in, so these layouts are cheap
        // and none of them is drawn.
        var moving = Folds.Cast<Control>().Append(_sessionHolder).ToList();
        var starts = moving.ToDictionary(part => part, part => part.Bounds);

        LayOutAt(to);
        var ends = moving.ToDictionary(part => part, part => part.Bounds);

        LayOutAt(from);

        _foldPaths.Clear();

        foreach (var part in moving)
        {
            _foldPaths[part] = (starts[part], ends[part]);
        }

        // Held until the fold ends: a frame moves the parts and nothing
        // lays them out again behind it.
        foreach (var panel in new[] { _body, _cardsFold.Parent }.OfType<Control>())
        {
            panel.SuspendLayout();
            _heldLayouts.Add(panel);
        }
    }

    /// <summary>The folds at <paramref name="open"/>, and the page laid out for it.</summary>
    private void LayOutAt(double open)
    {
        foreach (var fold in Folds)
        {
            fold.Open = open;
        }

        _body?.PerformLayout();
        _cardsFold?.Parent?.PerformLayout();
        _body?.PerformLayout();
    }

    /// <summary>One frame: every moving part to the same point of its way, and nothing laid out.</summary>
    private void FoldFrame()
    {
        if (_body is null || IsDisposed)
        {
            StopFoldTimer();
            return;
        }

        var t = Math.Clamp(_foldClock.Tick() / FoldMs, 0, 1);
        var e = SlideTime.Ease(t);

        foreach (var fold in Folds)
        {
            fold.Open = _foldFrom + (_foldTo - _foldFrom) * e;
        }

        foreach (var (part, (start, end)) in _foldPaths)
        {
            int Mix(int a, int b) => a + (int)Math.Round((b - a) * e);

            var bounds = Rectangle.FromLTRB(Mix(start.Left, end.Left), Mix(start.Top, end.Top), Mix(start.Right, end.Right), Mix(start.Bottom, end.Bottom));

            if (part.Bounds != bounds)
            {
                part.Bounds = bounds;
            }
        }

        if (t >= 1)
        {
            // The real controls back in place of their pictures, laid out
            // once, with painting held for the same reason as at the start.
            ShowWithoutFlash.Hold(this, () => FinishFold(_foldTo));
        }
    }

    /// <summary>
    /// The fold over, at <paramref name="open"/>: the timer gone, the real
    /// controls back in place of their pictures, a fold that is shut out of
    /// the layout, and the page laid out for real once.
    /// </summary>
    private void FinishFold(double open)
    {
        StopFoldTimer();
        _foldPaths.Clear();

        _foldFrom = _foldTo = open;

        foreach (var fold in Folds)
        {
            fold.Open = open;
            fold.LetGo();

            if (fold.Visible != open > 0)
            {
                fold.Visible = open > 0;
            }
        }

        _sessionStandIn?.LetGo();

        // Inner first, as they were held outer first.
        for (var i = _heldLayouts.Count - 1; i >= 0; i--)
        {
            _heldLayouts[i].ResumeLayout(false);
        }

        _heldLayouts.Clear();

        if (_body is not null)
        {
            LayOutAt(open);
        }
    }

    private void StopFoldTimer()
    {
        _foldTimer?.Stop();
        _foldTimer?.Dispose();
        _foldTimer = null;
        _foldClock.Reset();

        if (_finerFoldTimer)
        {
            TimeEndPeriod(1);
            _finerFoldTimer = false;
        }
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint milliseconds);

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint milliseconds);

    /// <summary>
    /// Holds one part and shows as much of it as Open says, from its foot
    /// up: at 1 all of it, at 0 none, and part of the way the part rolled up
    /// under the top edge. As tall as that, so whatever is under it rises as
    /// it rolls. The gap under the part is inside it, so that folds away too.
    ///
    /// While the fold runs (HoldPicture) it shows a picture of the part and
    /// the part itself is hidden, so nothing in it is laid out again frame
    /// by frame.
    /// </summary>
    private sealed class FoldPanel : Panel
    {
        private readonly Control _content;
        private readonly int _gapBelow;
        private Bitmap? _picture;

        public FoldPanel(Control content, int gapBelow, bool sizesItself)
        {
            _content = content;
            _gapBelow = Math.Max(0, gapBelow);

            DoubleBuffered = true;
            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Tag = AppTheme.KeepBackColor;

            // In a column of a Beside's left-hand side it sizes itself, as the
            // band did; in the clock's column the Beside sizes it.
            AutoSize = sizesItself;

            if (sizesItself)
            {
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
            }

            Controls.Add(content);
        }

        private double _open = 1;

        /// <summary>How much of the part shows, from 0 to 1.</summary>
        public double Open
        {
            get => _open;
            set => _open = Math.Clamp(value, 0, 1);
        }

        /// <summary>
        /// The part's own height at <paramref name="width"/> - its picture's
        /// while there is one. The part is given that width first: the
        /// Setup and Ghost cars band works out how wide the ghosts' card is
        /// from its own width as it is (PracticeModeControl.GhostsWidth), so
        /// asked at another it answered for the width it had - a line too
        /// tall, and the fold ran 17px past the foot of its column (layout
        /// test, 1920x1080, 21 Nov 2026).
        /// </summary>
        private int ContentHeight(int width)
        {
            if (_picture is not null)
            {
                return _picture.Height;
            }

            width = Math.Max(1, width);

            if (_content.Width != width)
            {
                _content.Width = width;
            }

            return Math.Max(0, _content.GetPreferredSize(new Size(width, 0)).Height);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width;

            return new Size(width, (int)Math.Round((ContentHeight(width) + _gapBelow) * _open));
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            if (_picture is not null)
            {
                Invalidate();
                return;
            }

            var width = ClientSize.Width;
            var height = ContentHeight(width);

            // Its foot at the foot of the room, over the gap: what is cut off
            // is its top, so it rolls up rather than being cut off below.
            var bounds = new Rectangle(0, ClientSize.Height - _gapBelow - height, width, height);

            if (_content.Bounds != bounds)
            {
                _content.Bounds = bounds;
            }

            // Something in it grew or shrank: the column round it is told,
            // as a Beside that sizes itself tells its own.
            if (AutoSize && Parent is not null && Height != GetPreferredSize(new Size(Width, 0)).Height)
            {
                Parent.PerformLayout(this, nameof(PreferredSize));
            }
        }

        /// <summary>A picture of the part laid out at this width, shown in its place until LetGo.</summary>
        public void HoldPicture()
        {
            if (_picture is not null || !IsHandleCreated)
            {
                return;
            }

            var width = Math.Max(1, ClientSize.Width);
            var height = ContentHeight(width);

            if (height <= 0)
            {
                return;
            }

            _content.Bounds = new Rectangle(0, ClientSize.Height - _gapBelow - height, width, height);
            _content.PerformLayout();

            Bitmap? picture = null;

            try
            {
                picture = new Bitmap(width, height);
                _content.DrawToBitmap(picture, new Rectangle(0, 0, width, height));
            }
            catch
            {
                // No picture: the fold still runs, with the part itself.
                picture?.Dispose();
                return;
            }

            _picture = picture;
            _content.Visible = false;
            Invalidate();
        }

        /// <summary>The part itself back in place of its picture.</summary>
        public void LetGo()
        {
            if (_picture is null)
            {
                return;
            }

            _picture.Dispose();
            _picture = null;
            _content.Visible = true;
            PerformLayout();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_picture is not null)
            {
                e.Graphics.DrawImageUnscaled(_picture, 0, ClientSize.Height - _gapBelow - _picture.Height);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _picture?.Dispose();
                _picture = null;
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// What sits under the clock - Open Big Screen and the two rows of tiles
    /// - one under another, at the column's width: a button as tall as its
    /// caption needs (as the column made it), anything else as tall as it
    /// says it needs at that width. In one part, so they fold as one.
    /// </summary>
    private sealed class SideStack : Panel
    {
        private readonly (Control Part, int GapBelow)[] _parts;

        public SideStack(params (Control Part, int GapBelow)[] parts)
        {
            _parts = parts;

            Margin = Padding.Empty;
            Padding = Padding.Empty;
            Tag = AppTheme.KeepBackColor;

            foreach (var (part, _) in parts)
            {
                // Sized here, never by the layout - see Beside.Side.
                part.AutoSize = false;
                part.Margin = Padding.Empty;
                Controls.Add(part);

                if (part is Button button)
                {
                    button.TextChanged += (_, _) => Changed();
                    button.FontChanged += (_, _) => Changed();
                }
                else
                {
                    part.Layout += (_, _) => Changed();
                }
            }
        }

        private void Changed()
        {
            PerformLayout();
            Parent?.PerformLayout();
        }

        private static int HeightOf(Control part, int width) =>
            part is Button button
                ? UiLayout.ButtonSizeFor(button).Height
                : part.GetPreferredSize(new Size(Math.Max(1, width), 0)).Height;

        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width;
            var height = _parts.Sum(entry => HeightOf(entry.Part, width) + entry.GapBelow);

            return new Size(width, height);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            var width = ClientSize.Width;
            var y = 0;

            foreach (var (part, gapBelow) in _parts)
            {
                var bounds = new Rectangle(0, y, width, HeightOf(part, width));

                if (part.Bounds != bounds)
                {
                    part.Bounds = bounds;
                }

                y += bounds.Height + gapBelow;
            }
        }
    }


    /// <summary>
    /// Stands in for the whole session card while the fold runs: a picture
    /// of the card as it was, filling the card's room - its top (heading,
    /// status line and table) from the top, its foot (the buttons and the
    /// card's lower edge) against the bottom, and one empty line of the
    /// table stretched between them as the room grows, or the top cut short
    /// as it shrinks. So nothing in the card is laid out at any frame.
    /// </summary>
    private sealed class SessionStandIn : Control
    {
        private readonly Control _session;
        private readonly Control _grid;
        private Bitmap? _picture;

        /// <summary>The line of the picture stretched to fill - just inside the foot of the table.</summary>
        private int _stretchRow;

        public SessionStandIn(Control session, Control grid)
        {
            _session = session;
            _grid = grid;

            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            Visible = false;
            Tag = AppTheme.KeepBackColor;
            TabStop = false;
        }

        public void Hold()
        {
            if (_picture is not null || !_session.IsHandleCreated || !_session.Visible || _session.Width <= 0 || _session.Height <= 2)
            {
                return;
            }

            Bitmap? picture = null;

            try
            {
                picture = new Bitmap(_session.Width, _session.Height);
                _session.DrawToBitmap(picture, new Rectangle(Point.Empty, _session.Size));
            }
            catch
            {
                picture?.Dispose();
                return;
            }

            var gridFoot = _grid.IsHandleCreated
                ? _session.PointToClient(_grid.PointToScreen(new Point(0, _grid.Height))).Y
                : picture.Height / 2;

            _stretchRow = Math.Clamp(gridFoot - 3, 1, picture.Height - 2);
            _picture = picture;

            Visible = true;
            BringToFront();
            _session.Visible = false;
        }

        public void LetGo()
        {
            if (_picture is null)
            {
                return;
            }

            _session.Visible = true;
            Visible = false;
            _picture.Dispose();
            _picture = null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;

            g.Clear(Parent?.BackColor ?? AppTheme.Background);

            if (_picture is not { } picture)
            {
                return;
            }

            // Pixel for pixel: the stretched line is drawn as it is, not blurred.
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

            var width = picture.Width;
            var height = ClientSize.Height;
            var foot = picture.Height - _stretchRow;
            var top = Math.Max(0, Math.Min(_stretchRow, height - foot));

            if (top > 0)
            {
                g.DrawImage(picture, new Rectangle(0, 0, width, top), new Rectangle(0, 0, width, top), GraphicsUnit.Pixel);
            }

            var gap = height - foot - top;

            if (gap > 0)
            {
                g.DrawImage(picture, new Rectangle(0, top, width, gap), new Rectangle(0, _stretchRow, width, 1), GraphicsUnit.Pixel);
            }

            g.DrawImage(picture, new Rectangle(0, height - foot, width, foot), new Rectangle(0, _stretchRow, width, foot), GraphicsUnit.Pixel);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _picture?.Dispose();
                _picture = null;
            }

            base.Dispose(disposing);
        }
    }
}