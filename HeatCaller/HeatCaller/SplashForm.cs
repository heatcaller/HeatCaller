using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// What you look at while the app builds itself.
///
/// There are thirty-odd pages behind the main window and every one of them
/// is constructed up front, so there is a real second or two before
/// anything is worth showing. Windows fills that gap with an empty white
/// rectangle - the wrong colour for every theme this app has, and the first
/// thing anyone sees.
///
/// This is shown INSTEAD, from the first line of Main, before the main
/// window exists at all. It is drawn rather than laid out - no child
/// controls, one OnPaint - so it appears immediately rather than joining
/// the queue of things being built.
/// </summary>
public sealed class SplashForm : Form
{
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 33 };
    private float _sweep;
    private string _stage = "Starting up...";

    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;

        // Above whatever else is on screen. Starting the app and then
        // finding its startup screen behind the window you launched it
        // from is its own small annoyance.
        TopMost = true;

        // At the screen's DPI, as the point-size text drawn on it is - on a
        // screen the app draws at 150% (--dpi-aware) the name alone is half
        // as tall again, and would run into the lines under it. Everything
        // OnPaint places is at the same scale, so the picture keeps its shape.
        Size = new Size(UiLayout.AtDpi(520), UiLayout.AtDpi(300));

        // The theme has not necessarily been chosen yet - this runs before
        // settings load on a cold start - so the splash carries its own
        // colours rather than reading a palette that may still be the
        // default. Dark either way, which is the point: no white.
        BackColor = Color.FromArgb(14, 10, 28);

        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);

        DoubleBuffered = true;

        // Rounded, like every other surface in the app.
        using var path = Rounded(new Rectangle(0, 0, Width, Height), UiLayout.AtDpi(14));
        Region = new Region(path);

        _tick.Tick += (_, _) =>
        {
            _sweep += 0.02f;

            if (_sweep > 1f)
            {
                _sweep = 0f;
            }

            Invalidate();
        };

        _tick.Start();
    }

    /// <summary>
    /// Off-screen compositing, for the same reason the main window uses it
    /// while it starts: a WinForms window is registered with the system
    /// COLOR_WINDOW brush, and Windows fills a new window with that brush
    /// before the app gets its first WM_PAINT. On a default theme that
    /// brush is white - so the screen whose entire job is to stop a white
    /// flash would otherwise open with one of its own.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
            return cp;
        }
    }

    /// <summary>
    /// What the app is doing, shown under the bar.
    ///
    /// Called from the startup path, so it has to survive being called
    /// before the handle exists and from a window that may already be
    /// closing.
    /// </summary>
    public void Say(string stage)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            _stage = stage;
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => Say(stage)));
            return;
        }

        _stage = stage;
        Invalidate();
        Update();
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;

        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var pink = Color.FromArgb(255, 45, 149);
        var violet = Color.FromArgb(124, 58, 237);
        var muted = Color.FromArgb(150, 142, 178);

        // Every place and size below is at the screen's DPI, as the window
        // and its point-size text are (see the constructor). The grid's and
        // the edge's hairlines stay one pixel.
        var skyHeight = UiLayout.AtDpi(150);
        var sunSize = UiLayout.AtDpi(110);
        var sunLeft = Width / 2 - UiLayout.AtDpi(55);
        var sunTop = UiLayout.AtDpi(34);
        var horizon = UiLayout.AtDpi(152);

        // A horizon: the sun sitting on a grid.
        using (var sky = new LinearGradientBrush(
                   new Rectangle(0, 0, Width, skyHeight), Color.FromArgb(38, 16, 64), Color.FromArgb(14, 10, 28), 90f))
        {
            g.FillRectangle(sky, 0, 0, Width, skyHeight);
        }

        using (var sun = new LinearGradientBrush(
                   new Rectangle(sunLeft, sunTop, sunSize, sunSize), Color.FromArgb(255, 196, 61), pink, 90f))
        {
            g.FillEllipse(sun, sunLeft, sunTop, sunSize, sunSize);
        }

        // The grid, drawn over the sun's lower half so the sun appears to
        // be setting into it.
        using (var line = new Pen(Color.FromArgb(90, violet), 1f))
        {
            for (var y = UiLayout.AtDpi(118); y < horizon; y += UiLayout.AtDpi(8))
            {
                g.DrawLine(line, 0, y, Width, y);
            }
        }

        using (var cover = new SolidBrush(Color.FromArgb(14, 10, 28)))
        {
            g.FillRectangle(cover, 0, horizon, Width, Height - horizon);
        }

        // Built through FontLibrary rather than with new Font("Oxanium", ...).
        //
        // Oxanium is shipped in Assets\Fonts, not installed on the machine,
        // and GDI+ cannot find a privately loaded font by NAME - it only
        // knows the FontFamily objects in its own collection. So a font
        // asked for by name here was never Oxanium at all: GDI+ quietly
        // substituted a default sans-serif, and the startup screen was the
        // one place in the app not using the app's own typeface. It looked
        // deliberate, which is why it survived this long.
        //
        // FontLibrary.Create hands back the real family object when the
        // font is bundled, and falls back to the installed one when it is
        // not.

        using (var nameFont = FontLibrary.Create(AppTheme.DisplayFamily, 30F, FontStyle.Bold))
        using (var brush = new SolidBrush(Color.White))
        {
            var text = "HEATCALLER";
            var size = g.MeasureString(text, nameFont);
            g.DrawString(text, nameFont, brush, (Width - size.Width) / 2f, UiLayout.AtDpi(168));
        }

        using (var versionFont = FontLibrary.Create(AppTheme.DisplayFamily, 11F, FontStyle.Bold))
        using (var brush = new SolidBrush(pink))
        {
            var text = AppVersion.Short;
            var size = g.MeasureString(text, versionFont);
            g.DrawString(text, versionFont, brush, (Width - size.Width) / 2f, UiLayout.AtDpi(212));
        }

        // The sweeping bar - a car going round, near enough. Deliberately
        // not a percentage: the startup has no honest progress to report,
        // and a bar that pretends to know is worse than one that just says
        // something is happening.
        var trackY = UiLayout.AtDpi(244);
        var trackLeft = UiLayout.AtDpi(60);
        var trackWidth = Width - trackLeft * 2;
        var trackHeight = UiLayout.AtDpi(3);

        using (var track = new SolidBrush(Color.FromArgb(40, 30, 70)))
        {
            g.FillRectangle(track, trackLeft, trackY, trackWidth, trackHeight);
        }

        var lightWidth = trackWidth * 0.28f;
        var travel = trackWidth + lightWidth;
        var lightLeft = trackLeft - lightWidth + travel * _sweep;

        // g.Clip hands back a new Region each time - disposed, as DriverPhotos.Draw does.
        using var clip = g.Clip;
        g.SetClip(new RectangleF(trackLeft, trackY, trackWidth, trackHeight));

        using (var light = new LinearGradientBrush(
                   new RectangleF(lightLeft, trackY, lightWidth, trackHeight),
                   Color.FromArgb(0, pink), pink, 0f))
        {
            g.FillRectangle(light, lightLeft, trackY, lightWidth, trackHeight);
        }

        g.Clip = clip;

        using (var stageFont = FontLibrary.Create(AppTheme.DisplayFamily, 9F, FontStyle.Regular))
        using (var brush = new SolidBrush(muted))
        {
            var size = g.MeasureString(_stage, stageFont);
            g.DrawString(_stage, stageFont, brush, (Width - size.Width) / 2f, UiLayout.AtDpi(258));
        }

        using (var edge = new Pen(Color.FromArgb(60, violet)))
        using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), UiLayout.AtDpi(14)))
        {
            g.DrawPath(edge, path);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tick.Stop();
            _tick.Dispose();
        }

        base.Dispose(disposing);
    }
}
