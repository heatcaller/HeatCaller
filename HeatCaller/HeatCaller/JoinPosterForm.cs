using System.Drawing.Printing;

namespace HeatCaller;

/// <summary>
/// The scan-to-join poster, full screen for a room to see or sent to a
/// printer to tape on a wall.
///
/// A form of its own rather than a panel on the Local Screens page: the point
/// of it is to be big and to be the only thing on the screen, which a panel
/// sharing a settings page can't be.
///
/// On the layout pieces (DialogFit): the poster taking the height the
/// window has - drawn on a panel of its own, as big as fits it - and Print,
/// Full screen and Close under it with the note about Esc after them,
/// wrapping onto another line in a narrow window. Full screen hides the
/// buttons, and the poster takes the room they leave. It used to be placed
/// in pixels: the poster drawn on the window itself, sized from the window
/// less a strip of fixed height for the buttons, which sat at fixed places
/// along it, in a window 820px tall inside its frame - taller than a
/// 1366x768 screen has room for.
/// </summary>
public sealed class JoinPosterForm : Form
{
    /// <summary>
    /// The least room the poster has before the window scrolls instead -
    /// the smallest the renderer draws it (JoinPosterRenderer.Render), below
    /// which it is a bigger picture shrunk. Not at the text size: the poster
    /// is drawn to the room it has, whatever size the text is.
    /// </summary>
    private const int PosterLeast = 220;

    private readonly string _url;
    private readonly string? _printed;
    private readonly string _heading;
    private readonly string _subtitle;

    /// <summary>What the poster is drawn on: the room the window leaves above its buttons, or - full screen - all of it.</summary>
    private readonly PosterCanvas _canvas = new();

    /// <summary>The buttons under the poster, hidden while it is full screen.</summary>
    private readonly ButtonBar _bar;

    private Bitmap? _poster;

    /// <param name="printed">The address printed under the code when it isn't the one encoded; null prints the encoded one.</param>
    public JoinPosterForm(string url, string heading, string subtitle, string? printed = null)
    {
        _url = url;
        _printed = printed;
        _heading = heading;
        _subtitle = subtitle;

        Text = $"{heading} - scan to join";
        BackColor = Color.White;
        KeyPreview = true;

        var btnPrint = new Button { Text = "Print..." };
        btnPrint.Click += (_, _) => Print();

        var btnFullScreen = new Button { Text = "Full screen" };
        btnFullScreen.Click += (_, _) => ToggleFullScreen();

        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (_, _) => Close();

        var hint = new Label
        {
            Text = "Esc leaves full screen.",
            AutoSize = true,
            ForeColor = Color.FromArgb(120, 120, 120),

            // Centred up and down on the buttons' line, as a label among
            // buttons is (FieldGrid.Inline).
            Anchor = AnchorStyles.Left,
        };

        // The app's buttons and faces, at the text size in use, on the
        // poster's white: Windows' own grey buttons in its 9pt face were all
        // that did not grow at 200% (layout pictures, 30 Sep). Only these -
        // the window stays white, whatever look the app is wearing.
        Font = AppTheme.BodyFont;
        hint.Font = AppTheme.SmallFont;
        // Print in the app's own colour, the window's main button as in every
        // other window (18 Nov 2026, dialog jazz-up). The poster itself, on
        // screen and on paper, is JoinPosterRenderer's and is not changed.
        AppTheme.StyleButton(btnPrint, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(btnFullScreen);
        AppTheme.StyleButton(btnClose, AppTheme.ButtonKind.Secondary);

        // Along the foot, in the order they always were, the note after
        // them - each button as wide as its caption needs.
        _bar = new ButtonBar().With(btnPrint, btnFullScreen, btnClose, hint);

        // The poster is drawn again at the canvas's new size, as it was at
        // the window's.
        _canvas.Resize += (_, _) => { DisposePoster(); _canvas.Invalidate(); };
        _canvas.Paint += OnPaintPoster;

        // The poster to the window's edges, as it was always drawn - in full
        // screen, the whole screen - and the page's margins round the buttons
        // only, which take them with them when they are hidden (the table
        // and FillPage leave a hidden part out, margins and all).
        var page = new FillPage { Padding = Padding.Empty };
        page.AddFill(_canvas, PosterLeast, gapBelow: 0);
        page.Add(_bar, gapBelow: 0);
        _bar.Margin = new Padding(UiLayout.PageLeft, UiLayout.RowGap, UiLayout.PageRight, UiLayout.PageTop);

        Controls.Add(page);

        // Escape gets you out of full screen, and out of the form when you
        // are not in it - the two things anyone tries when a window has
        // covered the screen mid-race night.
        KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape)
            {
                return;
            }

            if (FormBorderStyle == FormBorderStyle.None)
            {
                ToggleFullScreen();
            }
            else
            {
                Close();
            }
        };

        // Never given the theme pass, and still not: the poster is on white
        // whatever the app is wearing, and so is the window round it. Its
        // buttons are styled one by one, above.
        DialogFit.Keep(this, page, width: 760, height: 820);

        // A window of its own, shown beside the app rather than over it
        // (Local Screens', Who's Racing's and Club Website's posters are all
        // shown modeless) - often on a second screen - so it can be
        // minimized and is on the taskbar, as it always was. Keep dresses
        // every window as a dialog.
        MinimizeBox = true;
        ShowInTaskbar = true;

        // And it can be made far narrower than it opened, as it always could:
        // the poster is drawn to whatever room it has, and the buttons wrap
        // onto more lines. Only as narrow as keeps the widest of them whole,
        // at the text size in use - with the page's margins, the gap the bar
        // keeps after a button, and the scrollbar the page takes when the
        // wrapped buttons leave it too short. After DialogFit's own Load
        // handler, added above, which sets the least to the width it opened
        // at.
        var leastWidth = UiLayout.PageLeft
            + new[] { btnPrint, btnFullScreen, btnClose }.Max(button => UiLayout.ButtonSizeFor(button).Width)
            + UiLayout.ButtonGap
            + SystemInformation.VerticalScrollBarWidth
            + UiLayout.PageRight;

        Load += (_, _) => MinimumSize = new Size(
            Math.Min(MinimumSize.Width, Width - ClientSize.Width + leastWidth),
            MinimumSize.Height);
    }

    private void ToggleFullScreen()
    {
        if (FormBorderStyle == FormBorderStyle.None)
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Normal;
            _bar.Visible = true;
        }
        else
        {
            // Normal first: a window already maximised won't go full screen
            // when the border is removed, it just loses its title bar.
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            _bar.Visible = false;
        }

        DisposePoster();
        _canvas.Invalidate();
    }

    private void OnPaintPoster(object? sender, PaintEventArgs e)
    {
        // The canvas is the room the poster has: what the window leaves above
        // its buttons, or all of it with the buttons hidden.
        var room = _canvas.ClientSize;
        var available = Math.Min(room.Width, room.Height);

        if (available < 60)
        {
            return;
        }

        // Rebuilt only when the size it should be drawn at has changed -
        // repainting a form shouldn't re-encode a QR code. Compared with
        // the size the renderer will actually make, which it clamps: past
        // its ceiling (a big wall display) the two never matched, and the
        // poster was re-rendered on every single paint.
        var renderSize = Math.Clamp(available, 220, 4000);

        if (_poster is null || _poster.Width != renderSize)
        {
            DisposePoster();
            _poster = JoinPosterRenderer.Render(_url, _heading, _subtitle, renderSize, printed: _printed);
        }

        e.Graphics.DrawImage(_poster, (room.Width - available) / 2, 0, available, available);
    }

    private void Print()
    {
        try
        {
            using var doc = new PrintDocument();

            doc.PrintPage += (_, e) =>
            {
                var bounds = e.MarginBounds;
                var side = Math.Min(bounds.Width, bounds.Height);

                // Rendered at print resolution rather than scaling up the
                // screen copy - an enlarged 700px bitmap is exactly the
                // fuzzy-edged code that phones struggle with.
                //
                // MarginBounds is in hundredths of an inch, not printer
                // dots, so "side" alone (about 650 on Letter) gave a bitmap
                // no bigger than the on-screen one, stretched to ~100 DPI.
                // Converting through the printer's own DPI gets the real
                // dot count; the 2000px ceiling (about 300 DPI over 6.5in,
                // plenty for text and a QR code) keeps a 1200 DPI printer
                // from spooling a huge bitmap. A Graphics that reports no
                // sensible DPI falls back to 300, the common print default.
                var dpi = e.Graphics is { DpiX: > 0f } printerGraphics ? printerGraphics.DpiX : 300f;
                var pixels = (int)Math.Min(2000f, side * dpi / 100f);

                using var poster = JoinPosterRenderer.Render(_url, _heading, _subtitle, pixels, forPrint: true, printed: _printed);

                e.Graphics?.DrawImage(
                    poster,
                    bounds.Left + (bounds.Width - side) / 2,
                    bounds.Top + (bounds.Height - side) / 2,
                    side,
                    side);

                e.HasMorePages = false;
            };

            using var dialog = new PrintDialog { Document = doc, UseEXDialog = true };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                doc.Print();
            }
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't print: {ex.Message}", "Print", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DisposePoster()
    {
        _poster?.Dispose();
        _poster = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposePoster();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// The panel the poster is drawn on, filling the room the page gives it.
    /// Buffered, as the whole window was when the poster was drawn straight
    /// onto it.
    /// </summary>
    private sealed class PosterCanvas : Panel
    {
        public PosterCanvas()
        {
            DoubleBuffered = true;
        }
    }
}
