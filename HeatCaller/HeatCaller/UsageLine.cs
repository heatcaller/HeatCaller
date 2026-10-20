namespace HeatCaller;

/// <summary>
/// One monthly allowance, said in full: its name and how much is used, the
/// bar (UsageMeterBar), the figures ("212 of 450 lines · 38 tonight ·
/// starts again 1 Nov"), how many more nights like tonight it will last,
/// and - once it is running low - what can be done about it: Ask for
/// More... for the two HeatCaller looks after (TopUpRequest), or a link to
/// the club's own ElevenLabs plan for a key of its own.
///
/// Used on the AI Commentary page's Connection card, under the voice on
/// Voice Settings, and once per meter in the This Month window
/// (ThisMonthForm). A page that only wants one meter kept up to date calls
/// <see cref="Follow"/>; the window binds each line itself (<see cref="Bind"/>).
///
/// Sizes itself, as StatusLine does: everything in it is placed here, each
/// line of text wrapped to the width it is given, and the line as tall as
/// what is showing - put it in a Section or a PageStack, which give it its
/// width. A part with nothing to say takes no room at all.
/// </summary>
internal sealed class UsageLine : Panel
{
    /// <summary>The button's caption - shown without its dots (AppTheme.Caption); the dots say a question comes first.</summary>
    internal const string AskCaption = "Ask for More...";

    /// <summary>The button's caption while the request is on its way. Set after the theme pass, so its dots stay.</summary>
    private const string AskingCaption = "Asking...";

    /// <summary>The club's own ElevenLabs plan, where more characters are bought.</summary>
    internal const string ElevenLabsPlanUrl = "https://elevenlabs.io/app/subscription";

    private const string UpgradeText = "Upgrade on elevenlabs.io";

    private readonly AppSettings _settings;

    private readonly Label _title = new();
    private readonly UsageMeterBar _bar = new();
    private readonly Label _summary = new();
    private readonly Label _forecast = new();
    private readonly Label _note = new();
    private readonly Button _ask = new();
    private readonly Label _reply = new();
    private readonly LinkLabel _upgrade = new();
    private readonly LinkLabel _details = new();

    private Meter? _meter;

    /// <summary>
    /// A request on its way. Guards a second press INSTEAD of disabling the
    /// button: disabling the focused control pushes the focus on to the next
    /// one, and a page that scrolls then jumps to it (CommentaryControl's
    /// Test Connection learnt this).
    /// </summary>
    private bool _asking;

    /// <summary>Set while this line is asking its parent for a new height - see OnLayout.</summary>
    private bool _askingParent;

    // --- Following one meter for a page (Follow) ---

    private MeterKind? _following;
    private Control? _host;
    private Func<bool>? _wanted;
    private bool _subscribed;

    public UsageLine(AppSettings settings)
    {
        // Held while the window is being built - see LayoutFreeze.
        LayoutFreeze.Adopt(this);

        _settings = settings;

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;

        // Placed here, not sized by themselves: a label that sizes itself
        // keeps to one line whatever width it has.
        foreach (var label in new[] { _title, _summary, _forecast, _note, _reply })
        {
            label.AutoSize = false;
            label.Margin = Padding.Empty;
            label.UseMnemonic = false;
        }

        _title.ForeColor = AppTheme.TextDark;
        _summary.ForeColor = AppTheme.TextDark;
        _forecast.ForeColor = AppTheme.TextMuted;
        _note.ForeColor = AppTheme.TextMuted;
        _reply.ForeColor = AppTheme.TextMuted;

        _ask.Text = AskCaption;
        _ask.Margin = Padding.Empty;
        _ask.Click += AskClicked;

        _upgrade.AutoSize = false;
        _upgrade.Margin = Padding.Empty;
        _upgrade.Text = UpgradeText;
        _upgrade.LinkColor = AppTheme.Accent;
        _upgrade.ActiveLinkColor = AppTheme.Accent;
        _upgrade.VisitedLinkColor = AppTheme.Accent;
        _upgrade.LinkClicked += (_, _) => AppSupport.Open(ElevenLabsPlanUrl);

        Controls.Add(_title);
        Controls.Add(_bar);
        Controls.Add(_summary);
        Controls.Add(_forecast);
        Controls.Add(_note);
        Controls.Add(_ask);
        Controls.Add(_reply);
        Controls.Add(_upgrade);
        Controls.Add(_details);

        _details.AutoSize = false;
        _details.Margin = Padding.Empty;
        _details.Text = "Details...";
        _details.LinkColor = AppTheme.Accent;
        _details.ActiveLinkColor = AppTheme.Accent;
        _details.VisitedLinkColor = AppTheme.Accent;
        _details.Visible = false;
        _details.LinkClicked += (_, _) => _openDetails?.Invoke();

        WearSmallFaces();
        Bind(null);

        Disposed += (_, _) => StopFollowing();
    }

    private Action? _openDetails;

    /// <summary>
    /// What "Details..." at the foot of the line opens - the This Month
    /// window - or null for no link (the window's own lines have none).
    /// </summary>
    public Action? OpenDetails
    {
        get => _openDetails;
        set
        {
            _openDetails = value;
            _details.Visible = value is not null;
            Relayout();
        }
    }

    /// <summary>Raised when <see cref="RefreshNow"/> shows or hides the line, so a page can show or hide the caption beside it.</summary>
    public event Action<bool>? ShowingChanged;

    /// <summary>
    /// Where the first line is, from the top - what FieldGrid levels a
    /// caption with, as it does a StatusLine's: a caption centred on a line
    /// four rows tall sat beside the bar instead of the name.
    /// </summary>
    internal (int Top, int Bottom) FirstLine => (0, Pieces.LineHeight(_title.Font) + _title.Padding.Vertical);

    /// <summary>
    /// Ask for More... is for the allowances HeatCaller gives the club - AI
    /// commentary and the HeatCaller voice - and only once one is running
    /// low or gone: before then there is nothing to ask for. A key of the
    /// club's own is topped up on its own ElevenLabs plan instead.
    /// </summary>
    internal static bool OffersAsk(Meter? meter) =>
        meter is { CanAskForMore: true, Level: MeterLevel.Watch or MeterLevel.Spent };

    /// <summary>The ElevenLabs link, for a key of the club's own that is running low or gone.</summary>
    internal static bool OffersUpgrade(Meter? meter) =>
        meter is { Kind: MeterKind.ElevenLabsKey, Level: MeterLevel.Watch or MeterLevel.Spent };

    /// <summary>The first line: what it is, and how much is used when that is known.</summary>
    internal static string TitleFor(Meter meter) => meter.Level switch
    {
        MeterLevel.Spent => $"{meter.Name} - used up",
        MeterLevel.Unknown => meter.Name,
        _ => $"{meter.Name} - {meter.ShortPercent()} used",
    };

    /// <summary>
    /// Shows <paramref name="meter"/>, or nothing for null. On the UI
    /// thread. What the last Ask for More... said stays, for the same kind
    /// of meter, so a refresh straight after asking does not wipe the answer.
    /// </summary>
    public void Bind(Meter? meter)
    {
        if (meter is null || _meter is null || meter.Kind != _meter.Kind)
        {
            _reply.Text = string.Empty;
        }

        _meter = meter;

        if (meter is null)
        {
            _title.Text = string.Empty;
            _summary.Text = string.Empty;
            _forecast.Text = string.Empty;
            _note.Text = string.Empty;
            _bar.Show(0, MeterLevel.Unknown);
        }
        else
        {
            _title.Text = TitleFor(meter);
            _bar.Show(meter.Fraction, meter.Level);

            // A figure we could not read says why instead of a number that
            // means nothing (Meter.Known).
            _summary.Text = meter.Known ? meter.Summary() : string.Empty;
            _forecast.Text = meter.Known ? meter.Forecast() ?? string.Empty : string.Empty;
            _note.Text = meter.Known
                ? string.Empty
                : string.IsNullOrWhiteSpace(meter.Note) ? "Couldn't read this month's figure yet." : meter.Note!;

            _title.ForeColor = meter.Level switch
            {
                MeterLevel.Spent => AppTheme.Danger,
                MeterLevel.Watch => AppTheme.Warning,
                _ => AppTheme.TextDark,
            };
        }

        _summary.Visible = _summary.Text.Length > 0;
        _forecast.Visible = _forecast.Text.Length > 0;
        _note.Visible = _note.Text.Length > 0;
        _ask.Visible = OffersAsk(meter);
        _reply.Visible = _reply.Text.Length > 0;
        _upgrade.Visible = OffersUpgrade(meter);

        Relayout();
    }

    /// <summary>
    /// Keeps this line showing the store's meter of <paramref name="kind"/>
    /// (UsageMeters.Shared), and shown only while there is one and
    /// <paramref name="wanted"/> says the page wants it - the relay in use,
    /// the engine chosen. The store says it changed on any thread; the
    /// change is brought over through <paramref name="host"/>, the page,
    /// which has a window handle while this line - hidden - may not. Stops
    /// when the line is disposed, so a page that is rebuilt (Voice
    /// Settings is, on a theme change) leaves nothing subscribed behind it.
    /// </summary>
    public void Follow(MeterKind kind, Control host, Func<bool> wanted)
    {
        _following = kind;
        _host = host;
        _wanted = wanted;

        if (!_subscribed)
        {
            _subscribed = true;
            UsageMeters.Shared.Changed += OnStoreChanged;

            // A page built before it had a handle catches up when it gets
            // one, and every time it is shown: what wanted asks - the Club
            // Website page set up, say - is usually changed on another page.
            host.HandleCreated += (_, _) => RefreshNow();
            host.VisibleChanged += (_, _) =>
            {
                if (host.Visible)
                {
                    RefreshNow();
                }
            };
        }

        RefreshNow();
    }

    /// <summary>Reads the followed meter again and shows or hides the line. UI thread - the page calls it when what <c>wanted</c> asks has changed.</summary>
    public void RefreshNow()
    {
        if (IsDisposed || _following is not { } kind)
        {
            return;
        }

        var meter = UsageMeters.Shared.Get(kind);
        var show = meter is not null && (_wanted?.Invoke() ?? true);

        Bind(show ? meter : null);

        // Set every time rather than compared first: Visible reads false
        // while the page itself is hidden, so a line that should go could
        // not be told from one already gone. The same goes for the event.
        Visible = show;
        ShowingChanged?.Invoke(show);
    }

    private void OnStoreChanged()
    {
        var host = _host;

        if (host is null || host.IsDisposed || host.Disposing || !host.IsHandleCreated)
        {
            return;
        }

        try
        {
            host.BeginInvoke(RefreshNow);
        }
        catch (InvalidOperationException)
        {
            // The page went between the check and the post - nothing left to show it on.
        }
    }

    private void StopFollowing()
    {
        if (_subscribed)
        {
            _subscribed = false;
            UsageMeters.Shared.Changed -= OnStoreChanged;
        }
    }

    private async void AskClicked(object? sender, EventArgs e)
    {
        if (_asking || _meter is not { } meter || !OffersAsk(meter))
        {
            return;
        }

        // Says exactly what will happen, which depends on whether the club's
        // mailbox is set up (TopUpRequest.SendAsync): sent from it, or handed
        // to the email program to be sent by hand - a club told "it's sent"
        // that never pressed Send would wait for an answer that never comes.
        var how = PinMailer.IsConfigured(_settings)
            ? $"It is noted on the relay and emailed to {AppSupport.Email} from the club's mailbox. You'll hear back at {_settings.SmtpUser.Trim()}."
            : $"It is noted on the relay, and your email program opens with the request to {AppSupport.Email} written out - press Send there. You'll hear back by email.";

        var answer = AppDialog.Show(
            FindForm(),
            $"Ask for more {meter.Name} {meter.Unit} this month?\n\n" + how,
            "Ask for More",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes || IsDisposed)
        {
            return;
        }

        _asking = true;
        _ask.Text = AskingCaption;

        string said;

        try
        {
            said = await TopUpRequest.SendAsync(_settings, meter, null);
        }
        catch (Exception ex)
        {
            // SendAsync says what happened in a sentence of its own; this is
            // for the case it could not even do that.
            said = $"Couldn't send the request ({ex.Message}). Email {AppSupport.Email} to ask instead.";
        }

        if (IsDisposed)
        {
            return;
        }

        _asking = false;
        _ask.Text = AskCaption;
        _reply.Text = said;
        _reply.Visible = said.Length > 0;
        Relayout();
    }

    // ------------------------------------------------------------------
    //  Laying it out
    // ------------------------------------------------------------------

    /// <summary>The small face the lesser lines wear, again whenever the text size changes - the small font is replaced then, not resized.</summary>
    private void WearSmallFaces()
    {
        foreach (var label in new Label[] { _forecast, _note, _reply, _upgrade, _details })
        {
            if (!Equals(label.Font, AppTheme.SmallFont))
            {
                label.Font = AppTheme.SmallFont;
            }
        }
    }

    /// <summary>The window's own font change reaches this line (it has none of its own), and is the cue - as it is for Section's caption.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        WearSmallFaces();
    }

    /// <summary>What it says changed: laid out again, and whatever holds it told when that changes its height.</summary>
    private void Relayout()
    {
        if (IsDisposed)
        {
            return;
        }

        // A part that grew changes the least the page can do with - see
        // FillPage.LeastHeightMayHaveChanged.
        Pieces.FillPageOf(this)?.LeastHeightMayHaveChanged();
        PerformLayout();
    }

    /// <summary>Coming into view: while hidden, its parts all reported themselves hidden, so nothing was laid out.</summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            PerformLayout();
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        // A label treats a width of 1 as none at all; so does this.
        var width = proposedSize.Width is > 1 and < int.MaxValue / 4
            ? proposedSize.Width
            : UiLayout.AtTextSize(UiLayout.WideFieldWidth);

        return new Size(width, Arrange(width, place: false));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        // One that sizes itself asks to be given its new height first - as
        // Beside does - or it would lay out inside the height it had.
        var wanted = Arrange(Math.Max(2, Width), place: false);

        if (AutoSize && Parent is not null && Height != wanted && !_askingParent)
        {
            // Once per pass: a parent that does not give the height asked
            // for (a fixed row) would otherwise be asked again from the
            // layout its own answer starts.
            _askingParent = true;

            try
            {
                Parent.PerformLayout(this, nameof(PreferredSize));
            }
            finally
            {
                _askingParent = false;
            }
        }

        Arrange(Math.Max(2, ClientSize.Width), place: true);
    }

    /// <summary>
    /// Every part that is showing, one under the next, each as wide as the
    /// line and as tall as it needs at that width - a small gap between them,
    /// none under the last. Returns how tall that comes to, and places them
    /// there when <paramref name="place"/> is set.
    /// </summary>
    private int Arrange(int width, bool place)
    {
        var gap = UiLayout.AtTextSize(UiLayout.HintGap);
        var y = 0;
        var any = false;

        void Put(Control part, Size size)
        {
            // Hidden in its own right leaves the part out. While the line
            // itself is hidden that cannot be told, so then every part with
            // something to say counts - the height comes out right either way
            // the moment it is shown (OnVisibleChanged).
            if (!Showing(part))
            {
                return;
            }

            if (any)
            {
                y += gap;
            }

            if (place)
            {
                var bounds = new Rectangle(0, y, size.Width, size.Height);

                if (part.Bounds != bounds)
                {
                    part.Bounds = bounds;
                }
            }

            y += size.Height;
            any = true;
        }

        Put(_title, Wrapped(_title, width));
        Put(_bar, new Size(width, UiLayout.AtTextSize(UsageMeterBar.BarHeight)));
        Put(_summary, Wrapped(_summary, width));
        Put(_forecast, Wrapped(_forecast, width));
        Put(_note, Wrapped(_note, width));
        Put(_ask, AskSize(width));
        Put(_reply, Wrapped(_reply, width));
        Put(_upgrade, Wrapped(_upgrade, width));
        Put(_details, Wrapped(_details, width));

        return y;
    }

    /// <summary>Whether a part takes a row: shown in its own right, or - while the whole line is hidden - when it has something to say.</summary>
    private bool Showing(Control part)
    {
        if (Visible)
        {
            return part.Visible;
        }

        return part switch
        {
            Button => OffersAsk(_meter),
            UsageMeterBar => _meter is not null,
            LinkLabel link when ReferenceEquals(link, _details) => _openDetails is not null,
            LinkLabel => OffersUpgrade(_meter),
            _ => part.Text.Length > 0,
        };
    }

    /// <summary>A line of text across the width, as tall as it wraps to there - never less than one line.</summary>
    private static Size Wrapped(Label label, int width)
    {
        var room = Math.Max(2, width);
        var line = Pieces.LineHeight(label.Font) + label.Padding.Vertical;
        var height = Math.Max(line, label.GetPreferredSize(new Size(room, 0)).Height);

        return new Size(room, height);
    }

    /// <summary>
    /// The button as wide as the longer of its two captions, so "Asking..."
    /// does not resize it mid-request - and never wider than the line.
    /// </summary>
    private static Size AskSize(int width)
    {
        var resting = UiLayout.ButtonSizeFor(AppTheme.Caption(AskCaption));
        var asking = UiLayout.ButtonSizeFor(AskingCaption);

        return new Size(
            Math.Min(Math.Max(2, width), Math.Max(resting.Width, asking.Width)),
            Math.Max(resting.Height, asking.Height));
    }
}
