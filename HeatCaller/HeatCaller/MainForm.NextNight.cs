namespace HeatCaller;

/// <summary>
/// Telling the drivers about the next race night (12 Nov 2026, setup plan
/// D13 and C1): announcing it on the phones' Home page, calling it off with
/// one message to the regulars, and the "I'm coming" answers that come back.
/// The Setup tab's buttons ask; this does it, because it needs the relay and
/// the mailbox. See NextNight.
/// </summary>
public partial class MainForm
{
    /// <summary>Who said they're coming, night by night (setup plan C1).</summary>
    private readonly PreEntryStore _preEntries = PreEntryStore.Load();

    /// <summary>"Announce the next night..." on Setup.</summary>
    private void AnnounceNextNight(IWin32Window owner)
    {
        var last = _raceEvents.Real.OrderByDescending(e => e.StartedUtc).FirstOrDefault();
        var start = _settings.NextNight is { } current && current.Day.Date >= DateTime.Today
            ? current
            : NextNight.FromLastDay(last, DateTime.Today);

        using var form = new AnnounceNightForm(start, _raceClasses.All.Select(c => c.Name).ToList(), _settings.NextNight is not null);

        var answer = form.ShowDialog(owner);

        if (answer == DialogResult.Abort)
        {
            _settings.NextNight = null;
            _settings.Save();
            OnClientRawMessage($"{AppTime.LogStampNow()}  [program] The next race night was taken off the phones.");
            PublishNextNightNow();
            return;
        }

        if (answer != DialogResult.OK)
        {
            return;
        }

        _settings.NextNight = form.Night;
        _settings.Save();
        OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Next race night announced: {NextNight.When(form.Night.Day)}.");
        PublishNextNightNow();

        if (!_settings.RelayEnabled)
        {
            AppDialog.Show(owner, "Saved - but the club website is switched off on the Club Website page, so the phones won't see it until it's on.",
                "Announce the next night", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>"Call it off..." on Setup: the card says it's off, and the regulars are emailed.</summary>
    private async void CallOffNextNight(IWin32Window owner)
    {
        if (_settings.NextNight is not { } night)
        {
            AppDialog.Show(owner, "Announce the night first (Announce the next night...), then you can call it off.",
                "Call it off", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var regulars = NextNight.Regulars(_profiles.AllRaceable, _sessionHistory.All, _preEntries.For(night.DayKey).Select(p => p.Transponder), DateTime.UtcNow);
        var mailProblem = PinMailer.ConfigurationProblem(_settings);

        using var form = new CallOffForm(night, regulars.Count, mailProblem, canPush: _settings.RelayEnabled);

        if (form.ShowDialog(owner) != DialogResult.OK)
        {
            return;
        }

        night.Off = true;
        night.OffText = form.Words;
        _settings.NextNight = night;
        _settings.Save();
        PublishNextNightNow();
        OnClientRawMessage($"{AppTime.LogStampNow()}  [program] {NextNight.When(night.Day)} called off.");

        // A push to every phone signed up for alerts (setup plan step 7).
        string? pushed = null;

        if (form.Push)
        {
            var (title, words) = NextNight.OffPush(night, PinMailer.ClubName(_settings));
            pushed = await _relayPublisher.BroadcastPushAsync(title, words, _pinMailCancel.Token);
            OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Called off - push: {pushed}");
        }

        if (!form.Email || regulars.Count == 0 || mailProblem is not null)
        {
            if (pushed is not null && !IsDisposed)
            {
                AppDialog.Show(this, $"The night is off on the phones - and {pushed}", "Called off", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            return;
        }

        var club = PinMailer.ClubName(_settings);
        var messages = regulars
            .Select(p =>
            {
                var name = string.IsNullOrWhiteSpace(p.DriverName) ? "there" : p.DriverName.Trim().Split(' ')[0];
                var (subject, body) = NextNight.OffEmail(night, club, name);
                return ($"off|{night.DayKey}|{p.Transponder}", p.Email.Trim(), p.DriverName, subject, body);
            })
            .ToList();

        MailResult result;

        try
        {
            result = await PinMailer.SendBatchAsync(_settings, messages, null, _pinMailCancel.Token);
        }
        catch (Exception ex)
        {
            result = new MailResult { Ok = false, Message = ex.Message };
        }

        OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Called-off emails: {result.Message}");

        if (!IsDisposed)
        {
            var push = pushed is null ? string.Empty : $"\r\n\r\nPush: {pushed}";
            AppDialog.Show(this, (result.Ok ? $"The night is off on the phones, and the emails went: {result.Message}" : $"The night is off on the phones, but the emails didn't all go: {result.Message}") + push,
                "Called off", MessageBoxButtons.OK, result.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }

    /// <summary>Sends the next-night card now rather than on the next tick.</summary>
    private void PublishNextNightNow()
    {
        if (!_settings.RelayEnabled)
        {
            return;
        }

        try
        {
            _relayPublisher.PublishSnapshot(RelayChannel.NextNight,
                _settings.NextNight is { } night ? NextNight.Document(night, _settings.RelayDriverRequests) : NextNight.Withdrawn());
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[program] The next race night couldn't be sent: {ex.Message}");
        }
    }

    /// <summary>Who said they're coming tonight (the night that began at 6am), for Who's Racing.</summary>
    private IReadOnlyList<PreEntry> ComingTonightList() =>
        _preEntries.For(NextNight.KeyOf(TonightsSightings.NightStartUtc(DateTime.Now).ToLocalTime()));
}
