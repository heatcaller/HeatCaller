namespace HeatCaller;

/// <summary>
/// THE RACE VIDEO CARD (14 Nov 2026, race video plan Phase 0). The club
/// streams from a second PC with OBS to its own YouTube channel; this card
/// says where OBS is, how far before the green each race's video opens, and
/// whether video shows on public results - and lists tonight's stream, one
/// row a piece, for pasting the YouTube link and nudging it into line.
/// "Stream went live now" / "Stream stopped" are for a club without the OBS
/// link. Advanced only: the club decides to stream.
/// </summary>
public sealed partial class RelaySettingsControl
{
    private readonly CheckBox _chkVideo = new ThemedCheckBox();
    private readonly CheckBox _chkVideoPublic = new ThemedCheckBox();
    private readonly TextBox _txtObsHost = new();
    private readonly NumericUpDown _numObsPort = new();
    private readonly TextBox _txtObsPassword = new();
    private readonly NumericUpDown _numLeadIn = new();
    private readonly Label _lblObsState = new() { AutoSize = true };
    private readonly FlowLayoutPanel _pieces = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
    private readonly System.Windows.Forms.Timer _obsTimer = new() { Interval = 1000 };

    private Section BuildVideoCard()
    {
        _chkVideo.Text = "Race video: put each race's moment in the club's YouTube stream on the drivers' pages";
        _chkVideo.AutoSize = true;
        _chkVideo.CheckedChanged += (_, _) => { if (!_loading) { _settings.VideoEnabled = _chkVideo.Checked; _settings.Save(); } };

        _chkVideoPublic.Text = "Show it on the public results too (juniors' races stay on their own night)";
        _chkVideoPublic.AutoSize = true;
        _chkVideoPublic.CheckedChanged += (_, _) => { if (!_loading) { _settings.VideoOnPublicResults = _chkVideoPublic.Checked; _settings.Save(); } };

        _txtObsHost.PlaceholderText = "the streaming PC's address, e.g. 192.168.1.40";
        _txtObsHost.TextChanged += (_, _) => { if (!_loading) _settings.VideoObsHost = _txtObsHost.Text.Trim(); };
        _txtObsHost.Leave += (_, _) => _settings.Save();
        _numObsPort.Minimum = 1;
        _numObsPort.Maximum = 65535;
        _numObsPort.ValueChanged += (_, _) => { if (!_loading) { _settings.VideoObsPort = (int)_numObsPort.Value; _settings.Save(); } };
        _txtObsPassword.UseSystemPasswordChar = true;
        _txtObsPassword.TextChanged += (_, _) => { if (!_loading) _settings.VideoObsPassword = _txtObsPassword.Text; };
        _txtObsPassword.Leave += (_, _) => _settings.Save();
        _numLeadIn.Minimum = 0;
        _numLeadIn.Maximum = 30;
        _numLeadIn.ValueChanged += (_, _) => { if (!_loading) { _settings.VideoLeadInSeconds = (int)_numLeadIn.Value; _settings.Save(); } };

        var grid = new FieldGrid();
        grid.AddRow("OBS on:", _txtObsHost);
        grid.AddRow("Port:", _numObsPort);
        grid.AddRow("OBS password:", _txtObsPassword);
        grid.AddRow("Lead-in (seconds):", _numLeadIn);
        _linedUp.Add(grid);

        var live = new Button { Text = "Stream went live now", AutoSize = true };
        live.Click += (_, _) => Stream?.Started(DateTime.UtcNow);
        var stopped = new Button { Text = "Stream stopped", AutoSize = true };
        stopped.Click += (_, _) => Stream?.Stopped(DateTime.UtcNow);

        var hint = HintLabel(
            "Stream from a second PC with OBS (never the timing laptop) to the club's own YouTube channel, with YouTube's "
            + "Auto-start and Auto-stop on and Ultra-low latency. In OBS turn on Tools > WebSocket Server Settings. "
            + "HeatCaller notes when the stream starts and stops; paste each piece's YouTube link below, before or after. "
            + "If a race opens in the wrong place, nudge that piece - every race in it moves.");

        var card = new Section("Race video (YouTube)");
        card.Add(_chkVideo, stretch: false);
        card.Add(_chkVideoPublic, stretch: false);
        card.Add(grid, gapBelow: UiLayout.HintGap);
        card.Add(hint);
        card.Add(_lblObsState, gapBelow: UiLayout.HintGap);
        card.Add(_pieces, stretch: false);
        card.Add(new ButtonBar().With(live, stopped), gapBelow: 0);

        var deadSeen = string.Empty;
        _obsTimer.Tick += (_, _) =>
        {
            _lblObsState.Text = ObsWords();

            // Redrawn when the relay's word on a video changes (V7).
            var dead = string.Join(",", (DeadVideos?.Invoke() ?? Array.Empty<string>()).Order(StringComparer.Ordinal));
            if (dead != deadSeen)
            {
                deadSeen = dead;
                DrawPieces();
            }
        };
        _obsTimer.Start();
        Disposed += (_, _) => _obsTimer.Dispose();

        if (Stream is { } stream)
        {
            stream.Changed += () => { if (IsHandleCreated && !IsDisposed) BeginInvoke(DrawPieces); };
        }

        DrawPieces();
        return card;
    }

    /// <summary>Fills the card from settings - called with the rest in LoadFromSettings.</summary>
    private void LoadVideoSettings()
    {
        _chkVideo.Checked = _settings.VideoEnabled;
        _chkVideoPublic.Checked = _settings.VideoOnPublicResults;
        _txtObsHost.Text = _settings.VideoObsHost;
        _numObsPort.Value = Math.Clamp(_settings.VideoObsPort, 1, 65535);
        _txtObsPassword.Text = _settings.VideoObsPassword;
        _numLeadIn.Value = Math.Clamp(_settings.VideoLeadInSeconds, 0, 30);
    }

    private string ObsWords()
    {
        if (!_settings.VideoEnabled) return "Race video is off.";
        var state = Obs?.State ?? "off";
        var live = Stream?.Live == true ? " The stream is live." : string.Empty;
        return state switch
        {
            "off" => "Not watching OBS - put in the streaming PC's address above." + live,
            "connecting" => "Connecting to OBS..." + live,
            "connected" => "OBS link: connected." + live,
            "streaming" => "OBS link: connected, and streaming.",
            _ => "OBS link: " + state + live,
        };
    }

    /// <summary>One row a piece: when it ran, its YouTube link, and the nudge.</summary>
    private void DrawPieces()
    {
        _pieces.SuspendLayout();
        _pieces.Controls.Clear();
        var pieces = Stream?.Pieces ?? Array.Empty<StreamTonight.Piece>();

        if (pieces.Count == 0)
        {
            _pieces.Controls.Add(new Label { AutoSize = true, Text = "Nothing streamed tonight yet." });
        }

        for (var i = 0; i < pieces.Count; i++)
        {
            var index = i;
            var p = pieces[i];
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var when = p.StartedUtc.ToLocalTime().ToShortTimeString() + (p.EndedUtc is { } e ? " - " + e.ToLocalTime().ToShortTimeString() : " - live");
            row.Controls.Add(new Label { AutoSize = true, Text = $"Piece {i + 1}, {when}:", Margin = new Padding(0, 6, 6, 0) });
            var link = new TextBox { Width = UiLayout.AtTextSize(260), Text = p.VideoId, PlaceholderText = "YouTube link" };
            link.Leave += (_, _) =>
            {
                if (Stream?.SetLink(index, link.Text) == false)
                {
                    MessageBox.Show(this, "That isn't a YouTube link HeatCaller can read. Paste the link from YouTube Studio or the watch page.", "Race video");
                }
            };
            row.Controls.Add(link);

            foreach (var (label, seconds) in new[] { ("-5s", -5.0), ("-1s", -1.0), ("+1s", 1.0), ("+5s", 5.0) })
            {
                var b = new Button { Text = label, AutoSize = true };
                b.Click += (_, _) => Stream?.Nudge(index, seconds);
                row.Controls.Add(b);
            }

            row.Controls.Add(new Label { AutoSize = true, Text = p.NudgeSeconds == 0 ? string.Empty : $"nudged {p.NudgeSeconds:+0;-0}s", Margin = new Padding(6, 6, 0, 0) });
            _pieces.Controls.Add(row);

            // V7: the relay asked YouTube, and YouTube won't play this one.
            if (p.VideoId is { Length: > 0 } id && DeadVideos?.Invoke().Contains(id) == true)
            {
                _pieces.Controls.Add(new Label
                {
                    AutoSize = true,
                    ForeColor = AppTheme.Warning,
                    Text = "YouTube won't play this video for drivers. Check in YouTube Studio that it's Public or Unlisted and that Allow embedding is on.",
                });
            }
        }

        _pieces.ResumeLayout();
    }
}
