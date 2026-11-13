using System.Globalization;

namespace HeatCaller;

/// <summary>
/// The This Month window: every monthly allowance this PC uses - AI
/// commentary through the relay, the HeatCaller voice, a club's own
/// ElevenLabs key - as a line each (UsageLine), with Ask for More... on the
/// ones HeatCaller gives, the requests already made and still waiting, and
/// the tick box for the emails at 80% and 100%.
///
/// Opened from the chip at the foot of the sidebar, the AI Commentary
/// page's Details... and Mission Control's sense of what to look at. Not
/// modal, and one at a time (MainForm.ShowThisMonth), like Mission Control
/// and Photo Finish: it can sit open beside Race Control. It follows the
/// store (UsageMeters.Shared.Changed), so a figure that moves while it is
/// open moves here too, and Refresh asks the relay and ElevenLabs now.
///
/// On the layout pieces: the meters, the requests and the emails as cards
/// in a body that scrolls on a short screen, Refresh and Close at the foot.
/// </summary>
public sealed class ThisMonthForm : Form
{
    /// <summary>What it says with nothing metered - no relay set up for commentary or the voice, and no ElevenLabs key.</summary>
    internal const string NothingMetered = "Nothing on this PC is metered - the relay isn't set up and the voice is free.";

    private readonly AppSettings _settings;
    private readonly Func<VoiceProfile?> _voice;
    private readonly Func<IReadOnlyList<Meter>> _meters;
    private readonly Func<IReadOnlyList<TopUpAsk>> _requests;

    private readonly Dictionary<MeterKind, UsageLine> _lines = new();
    private readonly Label _lblNone = new();
    private readonly Section _requestsCard = new("Asked for more");
    private readonly Label _lblRequests = new();
    private readonly CheckBox _chkEmails = new ThemedCheckBox();
    private readonly Label _lblEmailTo = new();
    private readonly TextBox _txtEmailTo = new();
    private readonly SteadyLabel _lblChecked = new();
    private readonly Button _btnRefresh = new();

    /// <summary>A refresh on its way - guards a second press rather than disabling the button, which would move the focus.</summary>
    private bool _refreshing;

    /// <summary>The window for the app: the store's meters and requests, asked again as it opens.</summary>
    internal ThisMonthForm(AppSettings settings, Func<VoiceProfile?> voice)
        : this(settings, voice, () => UsageMeters.Shared.All, () => UsageMeters.Shared.Requests, refreshOnOpen: true)
    {
    }

    /// <summary>
    /// The window over meters and requests read from <paramref name="meters"/>
    /// and <paramref name="requests"/> - the store's, or the layout test's
    /// made-up ones, which must not be overwritten by a refresh on opening.
    /// </summary>
    internal ThisMonthForm(
        AppSettings settings,
        Func<VoiceProfile?> voice,
        Func<IReadOnlyList<Meter>> meters,
        Func<IReadOnlyList<TopUpAsk>> requests,
        bool refreshOnOpen)
    {
        _settings = settings;
        _voice = voice;
        _meters = meters;
        _requests = requests;

        Text = "This Month";

        var lblIntro = new Label
        {
            Text = "How much of each monthly allowance the club has used. The figures come from the relay and from ElevenLabs, "
                   + "and are checked every few minutes while HeatCaller is open.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // --- The meters, one line each, in the store's order ---
        var metersCard = new Section("This month");

        foreach (var kind in Enum.GetValues<MeterKind>())
        {
            var line = new UsageLine(settings) { Visible = false };
            _lines[kind] = line;
            metersCard.Add(line, gapBelow: UiLayout.SectionGap);
        }

        _lblNone.Text = NothingMetered;
        _lblNone.AutoSize = true;
        _lblNone.ForeColor = AppTheme.TextMuted;
        metersCard.Add(_lblNone, gapBelow: UiLayout.HintGap);

        _lblChecked.AutoSize = true;
        _lblChecked.ForeColor = AppTheme.TextMuted;
        _lblChecked.Wordings = new[] { "Checking...", "Checked at 23:59.", CheckedWording("The relay didn't accept this club's token.", "23:59") };
        metersCard.Add(_lblChecked, gapBelow: 0);

        // --- Requests already made, still waiting ---
        _lblRequests.AutoSize = true;
        _lblRequests.ForeColor = AppTheme.TextDark;
        _requestsCard.Add(_lblRequests, gapBelow: 0);

        // --- The emails ---
        _chkEmails.Text = "Email me at 80% and 100%";
        _chkEmails.AutoSize = true;
        _chkEmails.Checked = settings.UsageEmailsOn;
        _chkEmails.CheckedChanged += (_, _) =>
        {
            if (_settings.UsageEmailsOn != _chkEmails.Checked)
            {
                _settings.UsageEmailsOn = _chkEmails.Checked;
                _settings.Save();
            }

            ShowEmailTo();
        };

        _lblEmailTo.AutoSize = true;
        _lblEmailTo.ForeColor = AppTheme.TextMuted;

        // Where they go: blank for the club's own mailbox (the one the PINs
        // go out from), or the address of whoever pays for top-ups - a
        // treasurer who never reads the club's PIN mailbox would otherwise
        // hear about the allowance only when the commentary changed voice.
        //
        // Saved when the box is left and when the window closes, not on
        // every keystroke: half an address is not an address, and a save
        // per key is a settings file written thirty times for one email.
        _txtEmailTo.Text = settings.UsageEmailTo ?? string.Empty;
        _txtEmailTo.PlaceholderText = "Blank for the club's own mailbox";
        _txtEmailTo.MaxLength = 254;
        _txtEmailTo.AccessibleName = "Send the emails to";
        _txtEmailTo.Width = UiLayout.AtTextSize(320);
        _txtEmailTo.TextChanged += (_, _) => ShowEmailTo();
        _txtEmailTo.Leave += (_, _) => SaveEmailTo();
        FormClosing += (_, _) => SaveEmailTo();

        var sendTo = new FieldGrid();
        sendTo.AddRow("Send to:", _txtEmailTo);

        var emailsCard = new Section("Emails");
        emailsCard.Add(_chkEmails, stretch: false, gapBelow: UiLayout.HintGap);
        emailsCard.Add(sendTo, stretch: false, gapBelow: UiLayout.HintGap);
        emailsCard.Add(_lblEmailTo, gapBelow: 0);

        _btnRefresh.Text = "Refresh";
        _btnRefresh.Click += async (_, _) => await RefreshAsync();

        var btnClose = new Button { Text = "Close" };
        btnClose.Click += (_, _) => Close();

        var page = new FillPage();
        var body = DialogFit.Body(page);

        body.Add(lblIntro, gapBelow: UiLayout.RowGap);
        body.Add(metersCard);
        body.Add(_requestsCard);
        body.Add(emailsCard, gapBelow: 0);

        page.Add(new ButtonBar(alignRight: true).With(_btnRefresh, btnClose), gapBelow: 0);

        Controls.Add(page);
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // The hints keep the small face the theme pass gives every label
        // the body's.
        lblIntro.Font = AppTheme.SmallFont;
        _lblEmailTo.Font = AppTheme.SmallFont;
        _lblChecked.Font = AppTheme.SmallFont;
        _lblNone.Font = AppTheme.SmallFont;

        ShowAll();
        ShowEmailTo();

        DialogFit.Keep(this, page, width: 620, height: 560);

        // Shown beside the window it was opened from, not over it.
        ShowInTaskbar = true;

        // The store lives as long as the app: a handler left on it keeps
        // this window alive and posting into it. Off when it closes, and
        // again when it is disposed - a window built and disposed without
        // ever being shown (the layout sweep) never raises FormClosed.
        UsageMeters.Shared.Changed += OnStoreChanged;
        FormClosed += (_, _) => UsageMeters.Shared.Changed -= OnStoreChanged;
        Disposed += (_, _) => UsageMeters.Shared.Changed -= OnStoreChanged;

        if (refreshOnOpen)
        {
            Shown += async (_, _) => await RefreshAsync();
        }
    }

    /// <summary>What the requests card says: each open request on a line of its own - "Asked for more AI commentary on 19 Oct - waiting."</summary>
    internal static string DescribeRequests(IEnumerable<TopUpAsk> requests) =>
        string.Join(Environment.NewLine, requests
            .Where(r => r.Open)
            .Select(r => $"Asked for more {NameOf(r.Kind)} on "
                         + r.AskedUtc.ToLocalTime().ToString("d MMM", CultureInfo.InvariantCulture)
                         + " - waiting."));

    /// <summary>
    /// Where the emails go: the address set for them, or the club's own
    /// mailbox (AppSettings.UsageEmailTo blank) - or, with no mailbox set
    /// up, where to set one up. The emails go through the same mailbox as
    /// the racers' PINs, on the Club Website page.
    /// </summary>
    internal static string DescribeEmailTo(AppSettings settings) => DescribeEmailTo(settings, settings.UsageEmailTo);

    /// <summary>
    /// The same, for what is typed in the Send to box right now - so the
    /// line under it answers as the address is typed, before it is saved.
    /// </summary>
    internal static string DescribeEmailTo(AppSettings settings, string? typed)
    {
        if (!PinMailer.IsConfigured(settings))
        {
            return "Set up email on the Club Website page first (its \"Email (for racer PINs)\" card) - nothing can be sent until then.";
        }

        var chosen = (typed ?? string.Empty).Trim();

        // Said rather than silently ignored: the emails would go to the
        // club's mailbox instead, and whoever typed it should know.
        if (chosen.Length > 0 && !UsageMail.IsAddress(chosen))
        {
            return $"That isn't an email address yet - until it is, they go to {settings.SmtpUser.Trim()}, once per allowance each month.";
        }

        var to = chosen.Length > 0 ? chosen : settings.SmtpUser.Trim();
        return $"To {to}, once per allowance each month.";
    }

    /// <summary>The Send to box into the settings, trimmed, when it has changed.</summary>
    private void SaveEmailTo()
    {
        var typed = _txtEmailTo.Text.Trim();

        if (string.Equals(typed, (_settings.UsageEmailTo ?? string.Empty).Trim(), StringComparison.Ordinal))
        {
            return;
        }

        _settings.UsageEmailTo = typed;
        _settings.Save();
        ShowEmailTo();
    }

    /// <summary>What the foot of the meters says when the relay couldn't be read: why, and that the figures shown are the last it gave.</summary>
    internal static string CheckedWording(string problem, string at) =>
        $"{problem} The figures above are the last it gave (tried at {at}).";

    /// <summary>A meter's name, from the meter itself so the two never read differently.</summary>
    private static string NameOf(MeterKind kind) =>
        new Meter(kind, false, 0, 0, 0, null, DateTime.UtcNow, 0, null).Name;

    private void ShowEmailTo()
    {
        var text = DescribeEmailTo(_settings, _txtEmailTo.Text);

        if (_lblEmailTo.Text != text)
        {
            _lblEmailTo.Text = text;
        }
    }

    /// <summary>Everything read again from the store and shown. UI thread.</summary>
    private void ShowAll()
    {
        IReadOnlyList<Meter> meters;
        IReadOnlyList<TopUpAsk> requests;

        try
        {
            meters = _meters();
            requests = _requests();
        }
        catch (Exception)
        {
            // A store that can't be read must not take the window with it;
            // the window shows what it showed before.
            return;
        }

        foreach (var (kind, line) in _lines)
        {
            var meter = meters.FirstOrDefault(m => m.Kind == kind);

            line.Bind(meter);

            // Set every time: Visible reads false until the window is shown.
            line.Visible = meter is not null;
        }

        _lblNone.Visible = meters.Count == 0;

        var asked = DescribeRequests(requests);

        _lblRequests.Text = asked;
        _requestsCard.Visible = asked.Length > 0;
    }

    private void OnStoreChanged()
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(ShowAll);
        }
        catch (InvalidOperationException)
        {
            // The window went between the check and the post.
        }
    }

    /// <summary>Asks the relay and ElevenLabs now. The store never throws; what it finds arrives through Changed.</summary>
    private async Task RefreshAsync()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        _lblChecked.Text = "Checking...";

        try
        {
            // On the UI thread, as the store asks: it reads the settings and
            // the voice before its first await, and both belong to this thread.
            await UsageMeters.Shared.RefreshAsync(_settings, _voice());
        }
        catch (Exception)
        {
            // Never expected - RefreshAsync says it never throws - but a
            // window that sits open all night is no place to find out.
        }

        if (IsDisposed)
        {
            return;
        }

        _refreshing = false;

        var at = AppTime.TimeOfDay(DateTime.Now);
        var problem = UsageMeters.Shared.LastProblem;

        // A relay that didn't answer leaves the last figures standing; the
        // window says so rather than "Checked" over figures that weren't.
        _lblChecked.Text = problem is null ? $"Checked at {at}." : CheckedWording(problem, at);
        ShowAll();
        ShowEmailTo();
    }
}
