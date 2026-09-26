namespace HeatCaller;

/// <summary>
/// A guided look around the app: a small card in the bottom-right corner
/// of the main window that steps through the sidebar, opening each page as
/// it describes it, so what the card talks about is on screen behind it.
///
/// Separate from the first-run setup and never required. It is offered at
/// the end of setup, can be skipped at any stop (Skip, Esc, or closing it),
/// and can be taken again at any time with Shift+T.
///
/// Not modal: the app works normally while it is open, so someone can poke
/// at a page the card is describing and then carry on.
/// </summary>
public sealed class AppTour : Form
{
    private sealed record Stop(string? Page, string Title, string Body);

    private static readonly Stop[] Stops =
    {
        new(null, "A quick look around",
            "This takes about two minutes. Each stop opens a page so you can see it behind this card.\r\n\r\n"
            + "Every page is in the sidebar on the left: Essentials for running races at any track, then Club "
            + "Tools for events with entries, championships and your club's screens and website. The search box "
            + "finds pages and settings by name, and pressing ? anywhere lists the keyboard shortcuts."),

        // The order these four come in is the order a club actually needs
        // them, which is not the order they used to be shown in. The tour
        // opened with Race Control and reached Classes and Driver Profiles
        // two stops after Race Day - so it demonstrated the one screen
        // that needs nothing set up first, and introduced the two things
        // everything else is built from last.
        new("Classes", "Classes: start here",
            "A class is the rulebook a group of drivers races under - lap or time limits, minimum lap time, start "
            + "type and qualifying rules. Everything else is grouped by class, so this is the first thing to set "
            + "up. If everyone races together, one class called \"Open\" is all you need."),

        new("Driver Profiles", "Driver Profiles",
            "Every driver and their transponder, class, photo and personal bests. Giving a driver a class is what "
            + "puts them in that class's heats. A transponder the app doesn't know is offered for naming the first "
            + "time it crosses the line, so nobody has to be typed in twice."),

        new("Race Day", "Race Day: the night itself",
            "Start here on race night. Next Steps tells you the one next thing to do and has the button that does it - "
            + "name the day, pick the classes racing, tick people in as they arrive, then build the heats in one "
            + "press. The tabs behind it are the detail for each of those, in the order the day runs."),

        new("Race Control", "Race Control",
            "Where a race is actually run: stage, start, flags, the live leaderboard and the finish. Race Day "
            + "hands it each heat in order. F1-F6 work the flags from the keyboard, clicking a driver gives laps, "
            + "penalties and warnings, and F11 is full-screen Race Mode."),

        new("Practice", "Practice",
            "Open lapping with rolling averages and best laps, kept apart from race results. Ghost Cars lets drivers "
            + "chase a recorded lap."),

        new("Race History", "Race History and Race Analytics",
            "Every saved race, with corrections if a result needs fixing. Race Analytics, just below it in the "
            + "sidebar, has lap charts, consistency, head-to-heads and trends."),

        new("Championship", "Championship",
            "Seasons and points. Results from each event add up automatically, with drop rounds and CSV export."),

        new("Voice", "Voice",
            "The announcer: which voice speaks (the free Windows voice, your own ElevenLabs key, or the HeatCaller "
            + "voice) and every line it can say, from the start to lead changes and track records."),

        new("Sounds", "Sounds",
            "Soundscapes set the mood in one click: sound effects, a crowd that reacts to the racing, and music "
            + "between races. Each part can be tuned on its own tab."),

        new("Local Screens", "Local Screens",
            "Screens and phones at the track, over the laptop's own Wi-Fi - no internet needed. Live results on "
            + "any phone, a remote for the race director, self check-in and an OBS overlay."),

        new("Club Website", "Club Website",
            "Your club's pages on the internet: live results, schedule and standings anyone can open, plus online "
            + "check-in and driver sign-up. Set up once with an address and token."),

        new("Themes", "Themes",
            "The look of the app and of everything racers see - colours, and your club logo on screens, pages and "
            + "printed QR check-ins."),

        new("Connection", "Connection",
            "Where the app talks to your timing decoder. Pick EasyLap or I-Lap, press Connect, and the status "
            + "shows as soon as crossings arrive. The log at the bottom shows exactly what the decoder sends."),

        new("Backup & Restore", "Backup & Restore",
            "Automatic and manual backups of everything. Moving to a new laptop is a restore away."),

        new("Demo Mode", "Try it now",
            "Demo Mode runs a simulated race with pretend drivers - the best way to see the announcer, the "
            + "leaderboard and the screens working without a decoder.\r\n\r\n"
            + "That's the tour. Press Shift+T any time to see it again."),
    };

    private readonly Form _host;
    private readonly Action<string> _goToPage;

    private readonly Panel _accent = new();
    private readonly Label _lblStep = new();
    private readonly Label _lblTitle = new();
    private readonly Label _lblBody = new();
    private readonly Button _btnSkip = new();
    private readonly Button _btnBack = new();
    private readonly Button _btnNext = new();

    private int _index;

    public AppTour(Form host, Action<string> goToPage)
    {
        _host = host;
        _goToPage = goToPage;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Surface;
        Size = new Size(440, 260);

        BuildLayout();
        AppTheme.ApplyRecursively(this);
        ApplyColours();

        _host.Move += HostMoved;
        _host.Resize += HostMoved;
        FormClosed += (_, _) =>
        {
            _host.Move -= HostMoved;
            _host.Resize -= HostMoved;
        };
    }

    private void BuildLayout()
    {
        _accent.Dock = DockStyle.Top;
        _accent.Height = 4;

        _lblStep.AutoSize = true;
        _lblStep.Font = AppTheme.SmallFont;
        _lblStep.Location = new Point(20, 18);

        _lblTitle.AutoSize = true;
        _lblTitle.Font = AppTheme.HeadingFont;
        _lblTitle.MaximumSize = new Size(Width - 40, 0);
        _lblTitle.Location = new Point(20, 40);

        _lblBody.AutoSize = true;
        _lblBody.MaximumSize = new Size(Width - 40, 0);
        _lblBody.Location = new Point(20, 76);

        _btnSkip.Text = "Skip tour";
        _btnSkip.Width = 110;
        _btnSkip.Click += (_, _) => Close();

        _btnBack.Text = "Back";
        _btnBack.Width = 90;
        _btnBack.Click += (_, _) => ShowStop(_index - 1);

        _btnNext.Text = "Next";
        _btnNext.Width = 110;
        _btnNext.Click += (_, _) => NextClicked();

        Controls.AddRange(new Control[] { _accent, _lblStep, _lblTitle, _lblBody, _btnSkip, _btnBack, _btnNext });

        CancelButton = _btnSkip;
        AcceptButton = _btnNext;
    }

    private void ApplyColours()
    {
        BackColor = AppTheme.Surface;
        _accent.BackColor = AppTheme.Primary;
        _lblStep.ForeColor = AppTheme.TextMuted;
        _lblTitle.ForeColor = AppTheme.Primary;
        _lblBody.ForeColor = AppTheme.TextDark;

        foreach (var label in new[] { _lblStep, _lblTitle, _lblBody })
        {
            label.BackColor = AppTheme.Surface;
        }

        AppTheme.StyleButton(_btnNext, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(_btnBack, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnSkip, AppTheme.ButtonKind.Secondary);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ShowStop(0);
    }

    /// <summary>A thin frame, since the window has no border of its own.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var edge = new Pen(AppTheme.Border);
        e.Graphics.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
    }

    /// <summary>The arrow keys step through the tour. Taken here, before a focused button can use them to move focus.</summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Right:
                NextClicked();
                return true;
            case Keys.Left when _index > 0:
                ShowStop(_index - 1);
                return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void NextClicked()
    {
        if (_index >= Stops.Length - 1)
        {
            Close();
            return;
        }

        ShowStop(_index + 1);
    }

    private void ShowStop(int index)
    {
        _index = Math.Clamp(index, 0, Stops.Length - 1);
        var stop = Stops[_index];

        if (stop.Page is { } page)
        {
            try
            {
                _goToPage(page);
            }
            catch
            {
                // A page that can't be opened just isn't shown behind the
                // card - the card itself still makes sense.
            }
        }

        _lblStep.Text = _index == 0 ? "App tour" : $"App tour  ·  {_index} of {Stops.Length - 1}";
        _lblTitle.Text = stop.Title;
        _lblBody.Text = stop.Body;

        var last = _index == Stops.Length - 1;
        _btnBack.Visible = _index > 0;
        _btnSkip.Visible = !last;
        _btnNext.Text = last ? "Done" : (_index == 0 ? "Start" : "Next");

        LayOut();

        // Opening a page moves focus into it; the card keeps it so Enter,
        // Esc and the arrow keys keep working.
        Activate();
        _btnNext.Focus();
    }

    /// <summary>Sizes the card to its text and pins it to the host's bottom-right corner.</summary>
    private void LayOut()
    {
        var buttonTop = Math.Max(_lblBody.Bottom + 20, 150);

        _btnNext.Location = new Point(Width - 20 - _btnNext.Width, buttonTop);
        _btnBack.Location = new Point(_btnNext.Left - 10 - _btnBack.Width, buttonTop);
        _btnSkip.Location = new Point(20, buttonTop);

        Height = _btnNext.Bottom + 20;
        HostMoved(this, EventArgs.Empty);
        Invalidate();
    }

    private void HostMoved(object? sender, EventArgs e)
    {
        if (IsDisposed || _host.IsDisposed || _host.WindowState == FormWindowState.Minimized)
        {
            return;
        }

        var corner = _host.PointToScreen(new Point(_host.ClientSize.Width, _host.ClientSize.Height));
        Location = new Point(corner.X - Width - 24, corner.Y - Height - 24);
    }
}
