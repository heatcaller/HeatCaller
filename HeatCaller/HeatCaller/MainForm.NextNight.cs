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

        // Whether that reached the phones - see CalledOffWords.
        var websiteOn = _settings.RelayEnabled;

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
            if (CalledOffWords(websiteOn, emailsTried: false, emailsOk: true, null, pushed) is { } said && !IsDisposed)
            {
                AppDialog.Show(this, said, "Called off", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            // Its own words for what went: left to SendBatchAsync it read
            // "Emailed 12 drivers their night", as if the recap had gone.
            result = await PinMailer.SendBatchAsync(_settings, messages, null, _pinMailCancel.Token,
                n => $"Told {Plural.Of(n, "driver")} the night is off.");
        }
        catch (Exception ex)
        {
            result = new MailResult { Ok = false, Message = ex.Message };
        }

        OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Called-off emails: {result.Message}");

        if (!IsDisposed)
        {
            AppDialog.Show(this, CalledOffWords(websiteOn, emailsTried: true, result.Ok, result.Message, pushed) ?? string.Empty,
                "Called off", MessageBoxButtons.OK, result.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// What the desk is told once a night is called off, or null for nothing
    /// to say: whether the phones have it, how the emails went, and the push.
    /// </summary>
    /// <remarks>
    /// WHY (23 Nov 2026, bug hunt 24): the words always began "The night is
    /// off on the phones". With the club website switched off nothing is sent
    /// to the phones at all (PublishNextNightNow), so the director was told
    /// they showed the night as off when they still showed it as on - and
    /// with no email to send, was told nothing. Now it says what Announce
    /// the next night says: saved, but the phones won't see it until the
    /// website is on.
    /// </remarks>
    internal static string? CalledOffWords(bool websiteOn, bool emailsTried, bool emailsOk, string? emailMessage, string? pushed)
    {
        if (websiteOn)
        {
            if (!emailsTried)
            {
                return pushed is null ? null : $"The night is off on the phones - and {pushed}";
            }

            return (emailsOk
                    ? $"The night is off on the phones, and the emails went: {emailMessage}"
                    : $"The night is off on the phones, but the emails didn't all go: {emailMessage}")
                + (pushed is null ? string.Empty : $"\r\n\r\nPush: {pushed}");
        }

        var words = "The night is saved as called off - but the club website is switched off on the Club Website page, so the phones won't see it until it's on.";

        if (emailsTried)
        {
            words += "\r\n\r\n" + (emailsOk ? $"The emails went: {emailMessage}" : $"The emails didn't all go: {emailMessage}");
        }

        return words + (pushed is null ? string.Empty : $"\r\n\r\nPush: {pushed}");
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
                _settings.NextNight is { } night ? NextNight.Document(night, PhonesCanSayComing(_settings)) : NextNight.Withdrawn());
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[program] The next race night couldn't be sent: {ex.Message}");
        }
    }

    /// <summary>
    /// Whether a phone's "I'm coming" can get anywhere: Driver requests ticked
    /// AND drivers' nights, the same two the relay's gate and the laptop's
    /// collecting need (RelayPublisher.Requests.AsksWanted). Driver requests
    /// stays ticked when nights are switched off, so on its own it offered a
    /// "Coming?" button on Home that led nowhere.
    /// </summary>
    internal static bool PhonesCanSayComing(AppSettings settings) =>
        settings.RelayDriverRequests && settings.PublishDriverNightsToTheRelay;

    /// <summary>Who said they're coming tonight (the night that began at 6am), for Who's Racing.</summary>
    private IReadOnlyList<PreEntry> ComingTonightList() =>
        _preEntries.For(NextNight.KeyOf(TonightsSightings.NightStartUtc(DateTime.Now).ToLocalTime()));
}
