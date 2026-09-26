using System.Drawing.Printing;

namespace HeatCaller;

/// <summary>
/// The scan-to-join poster, full screen for a room to see or sent to a
/// printer to tape on a wall.
///
/// A form of its own rather than a panel on the Local Screens page: the point
/// of it is to be big and to be the only thing on the screen, which a panel
/// sharing a settings page can't be.
/// </summary>
public sealed class JoinPosterForm : Form
{
    private readonly string _url;
    private readonly string _heading;
    private readonly string _subtitle;

    private Bitmap? _poster;

    public JoinPosterForm(string url, string heading, string subtitle)
    {
        _url = url;
        _heading = heading;
        _subtitle = subtitle;

        Text = $"{heading} - scan to join";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 820);
        BackColor = Color.White;
        DoubleBuffered = true;
        KeyPreview = true;

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = Color.White };

        var btnPrint = new Button { Text = "Print...", Location = new Point(12, 10), Width = 110, Height = 30 };
        btnPrint.Click += (_, _) => Print();

        var btnFullScreen = new Button { Text = "Full screen", Location = new Point(130, 10), Width = 110, Height = 30 };
        btnFullScreen.Click += (_, _) => ToggleFullScreen();

        var btnClose = new Button { Text = "Close", Location = new Point(248, 10), Width = 110, Height = 30 };
        btnClose.Click += (_, _) => Close();

        var hint = new Label
        {
            Text = "Esc leaves full screen.",
            Location = new Point(370, 17),
            AutoSize = true,
            ForeColor = Color.FromArgb(120, 120, 120),
        };

        bar.Controls.AddRange(new Control[] { btnPrint, btnFullScreen, btnClose, hint });
        Controls.Add(bar);

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

        Resize += (_, _) => { DisposePoster(); Invalidate(); };
        Paint += OnPaintPoster;
    }

    private void ToggleFullScreen()
    {
        if (FormBorderStyle == FormBorderStyle.None)
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            WindowState = FormWindowState.Normal;
            Controls[0].Visible = true;
        }
        else
        {
            // Normal first: a window already maximised won't go full screen
            // when the border is removed, it just loses its title bar.
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            Controls[0].Visible = false;
        }

        DisposePoster();
        Invalidate();
    }

    private void OnPaintPoster(object? sender, PaintEventArgs e)
    {
        var barHeight = Controls[0].Visible ? Controls[0].Height : 0;
        var available = Math.Min(ClientSize.Width, ClientSize.Height - barHeight);

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
            _poster = JoinPosterRenderer.Render(_url, _heading, _subtitle, renderSize);
        }

        e.Graphics.DrawImage(_poster, (ClientSize.Width - available) / 2, 0, available, available);
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
                using var poster = JoinPosterRenderer.Render(_url, _heading, _subtitle, Math.Min(2000, side));

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
}
