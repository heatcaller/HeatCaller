namespace HeatCaller;

/// <summary>
/// The drivers' night emails, as MainForm gathers and sends them (22 Oct
/// 2026, driver experience, item 11) - see <see cref="NightEmail"/>, which
/// decides who and writes every word. This file only fetches what it is
/// built from (tonight's races, each driver's facts and report card, the
/// badge key), sends it through PinMailer.SendBatchAsync, and records who
/// was sent theirs so nobody is emailed twice for one night.
///
/// Two ways in, both through <see cref="SendNightEmailsAsync"/>:
///   - "Email Tonight's Nights..." on the Club Website page, which asks
///     first with the count (RelaySettingsControl.NightEmails);
///   - by itself when tonight's race day is closed, when the club has
///     ticked "Send automatically" (RaceEventStore.Closed).
/// </summary>
public partial class MainForm
{
    /// <summary>
    /// True while night emails are going. UI thread only. A second send
    /// while one is under way is refused rather than queued: the second
    /// would be worked out before the first had recorded who it sent, and
    /// email the same drivers again.
    /// </summary>
    private bool _nightEmailSending;

    /// <summary>
    /// The night send under way and the night it is for, or null. UI thread
    /// only.
    ///
    /// WHY (23 Oct 2026, bug hunt 10): who was emailed used to be recorded
    /// only by SendNightEmailsAsync once the whole batch came back, and not
    /// at all while closing. Closing HeatCaller mid-send (closing the race
    /// day starts the automatic send, and quitting is the usual next step)
    /// stopped the batch, but the drivers already emailed were never written
    /// to settings.json - so a send later that night emailed them again.
    /// Kept here so the window's close can stop the send and record what
    /// went before its last save (<see cref="RecordNightEmailsOnClose"/>).
    /// </summary>
    private (string Night, Task<MailResult> Send)? _nightEmailInFlight;

    partial void WireNightEmail()
    {
        // The page is built later (PopulateTabs) by MainForm.Pages, so it is
        // reached the way QrBadgeRenderer.OnlineBadgeUrl is: a hook set here,
        // read by the page when its button is pressed.
        RelaySettingsControl.NightEmails = new NightEmailDesk(
            PlanNightEmails,
            SendNightEmailsAsync,
            () => _nightEmailSending);

        _raceEvents.Closed += OnRaceDayClosedForNightEmail;
    }

    /// <summary>
    /// What tonight's send would be, worked out now: the emails themselves,
    /// so the count the page asks about is the count that would go. On the
    /// UI thread, where settings and profiles are read.
    /// </summary>
    private NightEmail.Plan PlanNightEmails()
    {
        var nightLocal = TonightAtTheTrack.NightOf(DateTime.Now);
        var night = NightEmail.NightKey(nightLocal);

        if (!_settings.NightEmailOffered)
        {
            return NightEmail.Plan.Nothing(night, "Offering drivers their night by email is switched off.");
        }

        if (PinMailer.ConfigurationProblem(_settings) is { } problem)
        {
            return NightEmail.Plan.Nothing(night, problem + " Set up the Email card on the Club Website page first.");
        }

        var tonight = RacesTonight()
            .Where(race => race is not null && !race.IsDemoSession())
            .ToList();

        if (tonight.Count == 0)
        {
            return NightEmail.Plan.Nothing(night, "Nobody has raced tonight yet, so there is no night to email.");
        }

        // One per DRIVER, by their main transponder (22 Oct 2026, second
        // transponder on the same night): a driver who raced their second
        // transponder as a car of its own used to be counted twice - two
        // emails, the second linking a night page under a badge nobody holds.
        var raced = tonight
            .SelectMany(DriversIn)
            .Select(NightMainOf)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var choice = NightEmail.Who(_settings, transponder => _profiles.Get(transponder), raced, night);
        var club = PinMailer.ClubName(_settings);
        var dayName = DayNameFor(tonight);

        // Whether podium videos go to the night page at all - the switch on
        // the Recording page, and a relay that can take them. Read once.
        bool clubSharesVideos;

        try
        {
            clubSharesVideos = RaceAudioStore.Load().SharePodiumVideos && _relayPublisher.CanSharePodiumVideos;
        }
        catch
        {
            clubSharesVideos = false;
        }

        var messages = new List<(string Key, string To, string ToName, string Subject, string Body)>();

        foreach (var recipient in choice.ToSend)
        {
            var profile = _profiles.Get(recipient.Transponder);

            // Their races in DriverCut's own words, exactly as the night page
            // has them - car by car when they raced a second transponder as
            // a car of its own, led by a car with something to say, as the
            // page is (PublishNightsFor). None (only practice tonight) and
            // there is nothing to send them.
            var driver = NightOwner.Group(new[] { recipient.Transponder }, tonight, NightMainOf).FirstOrDefault()
                         ?? new NightDriver(recipient.Transponder, new[] { new NightCarRef(recipient.Transponder, "c1", NightOwner.LabelFor(true, 0), null, true) });
            var carFacts = driver.Cars
                .Select(car => DriverNight.Lines(DriverCut.FactsFor(car.Transponder, recipient.ToName, tonight, _trackLayouts)))
                .ToList();
            var lead = carFacts.FindIndex(lines => lines.Length > 0);

            if (lead < 0)
            {
                continue;
            }

            if (lead > 0)
            {
                driver = NightOwner.WithFirst(driver, lead);
                carFacts = NightOwner.FirstAt(carFacts, lead);
            }

            var facts = carFacts[0];

            // Other drivers named the way the public pages name them - the
            // same card their night page carries (PublishNightsFor).
            var coach = CoachReportFor(driver.Cars[0].Transponder, publicNames: true);
            var link = NightEmail.NightLink(_settings, recipient.Transponder);
            var podium = clubSharesVideos && profile is { NoVideoOnline: false }
                         && driver.Cars.Any(car => MadeThePodium(tonight, car.Transponder));

            // Each car kept apart, as on the page - or null for one car,
            // which leaves the email exactly what it was.
            var cars = driver.Split
                ? driver.Cars
                    .Select((car, i) => new NightEmail.EmailCar(
                        NightEmail.CarHeading(car.Label, car.Name),
                        carFacts[i],
                        i == 0 ? coach : CoachReportFor(car.Transponder, publicNames: true)))
                    .ToList()
                : null;

            messages.Add((
                recipient.Transponder,
                recipient.To,
                recipient.ToName,
                NightEmail.Subject(club, nightLocal),
                NightEmail.Build(recipient.FirstName, club, nightLocal, dayName, facts, coach, podium, link, cars)));
        }

        return new NightEmail.Plan(night, messages, choice.AlreadySent, choice.NoAddress, null);
    }

    /// <summary>
    /// What the club called tonight's race day, from the day its races are
    /// filed under - not the current day, which closing it has just cleared.
    /// </summary>
    private string? DayNameFor(IReadOnlyList<SessionRecord> tonight)
    {
        foreach (var race in tonight)
        {
            if (!string.IsNullOrEmpty(race.RoundId)
                && _raceEvents.EventForRound(race.RoundId) is { IsLegacy: false } day
                && !string.IsNullOrWhiteSpace(day.Name))
            {
                return day.Name.Trim();
            }
        }

        return null;
    }

    /// <summary>A top-three finish in any race tonight - when a podium video may have been made of them.</summary>
    private static bool MadeThePodium(IEnumerable<SessionRecord> tonight, string transponder) =>
        tonight.Any(race => race.SessionType == RaceSessionType.Race
                            && race.Results.Any(row => row.Position is >= 1 and <= 3
                                                       && string.Equals(row.Transponder?.Trim(), transponder, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Sends tonight's night emails and records who got theirs. Started on
    /// the UI thread, and back on it after the send (no ConfigureAwait), which
    /// is where settings are changed and saved.
    /// </summary>
    private async Task<MailResult> SendNightEmailsAsync(IProgress<string>? progress, CancellationToken cancel)
    {
        if (_nightEmailSending)
        {
            return new MailResult { Ok = false, Message = "Tonight's night emails are already being sent." };
        }

        var plan = PlanNightEmails();

        if (plan.Problem is { } problem)
        {
            return new MailResult { Ok = false, Message = problem };
        }

        if (plan.Messages.Count == 0)
        {
            return new MailResult { Ok = true, Message = "Nobody to email - everybody who asked has had theirs, or nobody asked." };
        }

        _nightEmailSending = true;
        MailResult result;

        // WHY (23 Oct 2026, bug hunt 10): the send from the page ran on the
        // page's own token, which closing the window never cancelled, so it
        // could not be stopped and recorded at close. Linked to
        // _pinMailCancel, closing stops either send - see
        // RecordNightEmailsOnClose.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancel, _pinMailCancel.Token);

        try
        {
            var send = PinMailer.SendBatchAsync(_settings, plan.Messages, progress, stop.Token);
            _nightEmailInFlight = (plan.Night, send);
            result = await send;
        }
        catch (Exception ex)
        {
            result = new MailResult { Ok = false, Message = ex.Message };
        }
        finally
        {
            _nightEmailSending = false;
            _nightEmailInFlight = null;
        }

        // Whatever went is recorded, even if the send stopped partway or the
        // page that asked for it has gone - a driver sent theirs must not be
        // sent it again.
        //
        // WHY (23 Oct 2026, bug hunt 10): this was skipped while closing
        // (!_closingDown), which is exactly when a send stops partway. Now
        // always recorded; MarkSent adds each driver once, so recording
        // here after RecordNightEmailsOnClose already did is harmless.
        if (NightEmail.MarkSent(_settings, plan.Night, result.Sent.Select(sent => sent.Transponder)))
        {
            _settings.Save();
        }

        return result;
    }

    /// <summary>
    /// For the window's close, just before its last settings save: stops a
    /// night send still under way and records, into settings (not saved
    /// here - the close's own save writes it), the drivers it had already
    /// emailed. Without this, the record waited for SendNightEmailsAsync to
    /// come back on the UI thread, which once the window has closed may
    /// never happen. Does nothing when no night send is going. UI thread.
    ///
    /// Waiting here cannot deadlock: PinMailer.SendBatchAsync runs on the
    /// thread pool with ConfigureAwait(false) throughout, and stops promptly
    /// once cancelled. Should it still not be back within the wait, nothing
    /// is recorded - as it was before.
    /// </summary>
    private void RecordNightEmailsOnClose()
    {
        if (_nightEmailInFlight is not { } inFlight)
        {
            return;
        }

        try
        {
            _pinMailCancel.Cancel();

            if (inFlight.Send.Wait(TimeSpan.FromSeconds(5)))
            {
                NightEmail.MarkSent(_settings, inFlight.Night, inFlight.Send.Result.Sent.Select(sent => sent.Transponder));
            }
        }
        catch
        {
            // Best effort - closing must not be stopped by a send that failed.
        }
    }

    /// <summary>
    /// A race day was closed: the night emails go by themselves when the club
    /// has asked for that and it was tonight's day. Closing an old day from
    /// the calendar sends nothing - its night is not tonight's.
    /// </summary>
    private void OnRaceDayClosedForNightEmail(RaceEvent day)
    {
        // Copied now: the handler may run on whatever thread closed the day.
        var roundIds = day.RoundIds.ToHashSet(StringComparer.Ordinal);

        TryPost(() =>
        {
            if (_closingDown || !_settings.NightEmailOffered || !_settings.NightEmailAutomatic)
            {
                return;
            }

            var tonights = RacesTonight().Any(race => !string.IsNullOrEmpty(race.RoundId) && roundIds.Contains(race.RoundId));

            if (!tonights)
            {
                return;
            }

            _ = EmailNightsAutomaticallyAsync();
        });
    }

    /// <summary>The automatic send, fire and forget - one line in the console for what it came to.</summary>
    private async Task EmailNightsAutomaticallyAsync()
    {
        try
        {
            var result = await SendNightEmailsAsync(null, _pinMailCancel.Token);

            if (_closingDown || result.Cancelled)
            {
                return;
            }

            var line = result.Message;

            if (result.Failures.Count > 0)
            {
                line += " Couldn't send to: " + string.Join("; ", result.Failures.Take(5))
                        + (result.Failures.Count > 5 ? $" and {result.Failures.Count - 5} more." : ".");
            }

            OnClientRawMessage($"[email] Race day closed - drivers' nights: {line}");
        }
        catch (Exception ex)
        {
            // Fire and forget, so nothing else would ever see this.
            OnClientRawMessage($"[email] Could not email the drivers' nights: {ex.Message}");
        }
    }
}
