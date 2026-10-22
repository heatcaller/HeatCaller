namespace HeatCaller;

/// <summary>
/// Plays one of the club's sponsor videos in a window of its own, from the
/// Sponsors page, so an operator can see and hear what they uploaded before
/// it goes on the Big Screen in front of the crowd (20 Oct 2026).
///
/// It plays through the same LibVLC engine the Big Screen uses (see
/// SponsorVideoEngine), so a video that plays here plays there - this is the
/// real player, not a guess at it. It has its own SponsorVideoSurface, so it
/// never touches whatever the Big Screen is showing.
///
/// The engine can take a long time to load the first time on a slow PC (see
/// SponsorVideoEngine.Settled), and that must never freeze the page. So the
/// window opens at once, says the player is starting, warms the engine on a
/// background thread, and starts the video as soon as it is ready.
///
/// It always plays WITH sound, whatever the "Play a sponsor video's own
/// sound" setting says - hearing the ad is half of checking it.
/// </summary>
internal sealed class SponsorVideoPreviewForm : Form
{
    private readonly string _path;
    private readonly Panel _screen = new();
    private readonly Label _lblStatus = new();
    private readonly Button _btnPause = new();
    private readonly Button _btnReplay = new();
    private readonly Button _btnClose = new();
    private readonly System.Windows.Forms.Timer _waitForEngine = new() { Interval = 250 };
    private SponsorVideoSurface? _surface;

    internal SponsorVideoPreviewForm(string path)
    {
        _path = path;

        Text = "Preview - " + Path.GetFileName(path);
        Font = AppTheme.BodyFont;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        KeyPreview = true;
        ClientSize = new Size(UiLayout.AtTextSize(960), UiLayout.AtTextSize(600));
        MinimumSize = new Size(UiLayout.AtTextSize(480), UiLayout.AtTextSize(340));

        // The video area: black like a TV, with a line of text in the middle
        // while the player starts or when it can't.
        _screen.Dock = DockStyle.Fill;
        _screen.BackColor = Color.Black;

        _lblStatus.Dock = DockStyle.Fill;
        _lblStatus.TextAlign = ContentAlignment.MiddleCenter;
        _lblStatus.ForeColor = Color.Gainsboro;
        _lblStatus.BackColor = Color.Black;
        _lblStatus.Text = "Starting the video player...";
        _screen.Controls.Add(_lblStatus);

        _btnPause.Text = "Pause";
        _btnPause.AutoSize = true;
        _btnPause.Enabled = false;
        _btnPause.Click += (_, _) => TogglePause();

        _btnReplay.Text = "Play From Start";
        _btnReplay.AutoSize = true;
        _btnReplay.Enabled = false;
        _btnReplay.Click += (_, _) => StartVideo();

        _btnClose.Text = "Close";
        _btnClose.AutoSize = true;
        _btnClose.DialogResult = DialogResult.Cancel;

        // Close furthest right, as in every other window in the app.
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(UiLayout.AtTextSize(8)),
            WrapContents = false,
        };
        bar.Controls.Add(_btnClose);
        bar.Controls.Add(_btnReplay);
        bar.Controls.Add(_btnPause);

        Controls.Add(_screen);
        Controls.Add(bar);

        CancelButton = _btnClose;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(_btnReplay, AppTheme.ButtonKind.Primary);

        // The theme pass recolours everything; the video area stays black.
        _screen.BackColor = Color.Black;
        _lblStatus.BackColor = Color.Black;
        _lblStatus.ForeColor = Color.Gainsboro;

        _waitForEngine.Tick += (_, _) => TryStart();

        Shown += (_, _) =>
        {
            // Never TryGet on the UI thread before the engine has settled -
            // on a slow PC that is a freeze of most of a minute. Warm it on
            // a background thread and poll until it is done.
            if (SponsorVideoEngine.Settled)
            {
                TryStart();
            }
            else
            {
                SponsorVideoEngine.WarmUp();
                _waitForEngine.Start();
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Space && _btnPause.Enabled)
            {
                TogglePause();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };

        FormClosing += (_, _) =>
        {
            _waitForEngine.Stop();
            _surface?.Stop();
        };
    }

    /// <summary>Makes the surface once the engine has settled, and starts the video on it.</summary>
    private void TryStart()
    {
        if (!SponsorVideoEngine.Settled || IsDisposed)
        {
            return;
        }

        _waitForEngine.Stop();

        if (_surface is null)
        {
            _surface = new SponsorVideoSurface();

            if (!_surface.Available)
            {
                ShowStatus("This video can't be previewed: "
                           + (_surface.Problem ?? SponsorVideoEngine.Problem ?? "the video player would not start."));
                return;
            }

            _surface.View.Dock = DockStyle.Fill;
            _screen.Controls.Add(_surface.View);
            _surface.View.BringToFront();

            // Arrives on a LibVLC thread - only hand the fact over here.
            _surface.Failed += () => _failed = true;
            _surface.Finished += () => SafeInvoke(OnFinished);
        }

        StartVideo();
    }

    private void StartVideo()
    {
        if (_surface is null || !_surface.Available)
        {
            return;
        }

        if (!File.Exists(_path))
        {
            ShowStatus("The video file is missing:\n" + _path);
            return;
        }

        _surface.Stop();
        _failed = false;

        if (!_surface.Play(_path, withSound: true))
        {
            ShowStatus("The video player couldn't open this file. It may be damaged, or in a format the player "
                       + "doesn't read. Try exporting it again as an MP4 (H.264).");
            return;
        }

        _lblStatus.Visible = false;
        _surface.View.Visible = true;
        _btnPause.Text = "Pause";
        _btnPause.Enabled = true;
        _btnReplay.Enabled = true;
        _btnPause.Focus();
    }

    private void TogglePause()
    {
        if (_surface is null)
        {
            return;
        }

        _btnPause.Text = _surface.TogglePause() ? "Resume" : "Pause";
    }

    /// <summary>Set from the player's own thread when the video failed part way through.</summary>
    private volatile bool _failed;

    private void OnFinished()
    {
        _btnPause.Text = "Pause";
        _btnPause.Enabled = false;
        _btnReplay.Enabled = true;

        if (_surface is not null)
        {
            _surface.View.Visible = false;
        }

        // A video that broke part way through says so (22 Oct 2026, bug
        // hunt) - it used to say it had played fine.
        ShowStatus(_failed
            ? "The video player hit an error part way through this file. It may be damaged, or in a format the player "
              + "doesn't read. Try exporting it again as an MP4 (H.264)."
            : "End of video.\n\nThat is how it will look on the Big Screen. Press Play From Start to watch it again.");
        _btnReplay.Focus();
    }

    private void ShowStatus(string text)
    {
        _lblStatus.Text = text;
        _lblStatus.Visible = true;
        _lblStatus.BringToFront();

        if (_surface is not null && _surface.View.Visible && !_btnPause.Enabled)
        {
            _surface.View.Visible = false;
        }
    }

    private void SafeInvoke(Action action)
    {
        try
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(action);
        }
        catch
        {
            // The window closed while the video was finishing.
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _waitForEngine.Dispose();
            _surface?.Dispose();
            _surface = null;
        }

        base.Dispose(disposing);
    }
}
