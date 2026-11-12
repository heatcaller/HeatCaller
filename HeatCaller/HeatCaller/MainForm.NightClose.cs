namespace HeatCaller;

/// <summary>
/// The facts behind the close-the-night checklist (11 Nov 2026, race-night
/// build plan, Phase 2 - see NightClose). Here because MainForm is what can
/// see the rentals, the relay, the night emails and the backups at once.
/// </summary>
public partial class MainForm
{
    /// <summary>How long a night-email count is reused: planning one builds every email, and the card refreshes often.</summary>
    private static readonly TimeSpan NightEmailCountFor = TimeSpan.FromSeconds(60);

    private (DateTime AtUtc, int? Count)? _nightEmailCount;

    /// <summary>What is still to do tonight, given the day's saved races.</summary>
    private NightClose.Facts NightClosingFacts(IReadOnlyList<SessionRecord> daySessions, RaceEvent? day)
    {
        // The day asked about - the closing checklist keeps to tonight after
        // Close Day, when it's no longer the active one.
        day ??= _raceEvents.Active;

        var rentalsOut = _rentals.OutNow
            .Select(unit => string.IsNullOrWhiteSpace(unit.CurrentHolderName) ? unit.DisplayName : unit.CurrentHolderName)
            .ToList();

        // Only with publishing on: with it off, "not on the website" is the
        // club's choice, not something left undone.
        int? notOnline = null;

        if (_relayPublisher.Status is { Enabled: true, Configured: true })
        {
            notOnline = daySessions.Count(s => !s.IsNotScored() && _relayPublisher.ResultIsOwed(s.Id));
        }

        return new NightClose.Facts(
            rentalsOut,
            NightEmailsToSend(),
            notOnline,
            _settings.LastAutoBackupUtc,
            day is { IsLegacy: false, IsOpen: true },
            OwingTonight(daySessions, day));
    }

    /// <summary>
    /// The drivers who raced tonight and still owe their entry (11 Nov 2026,
    /// Phase 4), or null when tonight's race day has no entry fee.
    /// </summary>
    private IReadOnlyList<string>? OwingTonight(IReadOnlyList<SessionRecord> daySessions, RaceEvent? raceDay)
    {
        if (raceDay is not { IsLegacy: false, EntryFee: > 0 } day)
        {
            return null;
        }

        return daySessions
            .Where(s => !s.IsNotScored())
            .SelectMany(s => s.Results.Select(r => r.Transponder))
            .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
            .Select(t => _profiles.PrimaryForAlternate(t) ?? t)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => _profiles.Get(t))
            .OfType<DriverProfile>()
            .Where(p => (_payments.Get(day.Id, p.Transponder)?.How ?? PaidHow.Owing) == PaidHow.Owing
                        && CheckInControl.FeeFor(p, day) > 0)
            .Select(p => string.IsNullOrWhiteSpace(p.DriverName) ? p.Transponder : p.DriverName)
            .ToList();
    }

    /// <summary>
    /// Night emails still to send, or null when they are switched off or
    /// can't go out (no mailbox set up). Reused for a minute - see
    /// NightEmailCountFor.
    /// </summary>
    private int? NightEmailsToSend()
    {
        var now = DateTime.UtcNow;

        if (_nightEmailCount is { } cached && now - cached.AtUtc < NightEmailCountFor)
        {
            return cached.Count;
        }

        int? count;

        try
        {
            var plan = PlanNightEmails();
            count = plan.Problem is null ? plan.Messages.Count : null;
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Couldn't count tonight's night emails: {ex.Message}");
            count = null;
        }

        _nightEmailCount = (now, count);
        return count;
    }

    private CloseNightForm? _closeNight;

    /// <summary>
    /// Opens the closing checklist (11 Nov 2026 - see CloseNightForm), or
    /// brings it forward when it is already open.
    /// </summary>
    private void OpenCloseNight()
    {
        if (_raceProgram is not { } program)
        {
            return;
        }

        if (_closeNight is { IsDisposed: false } open)
        {
            // Open for another night (a new day started since): start again.
            if (open.DayId != program.ActiveDayId)
            {
                open.Close();
            }
            else
            {
                open.Reload();
                open.Activate();
                return;
            }
        }

        var dayId = program.ActiveDayId;

        var form = new CloseNightForm(
            () => program.ClosingChecklist(dayId),
            (key, done) => program.TickClosingItem(key, done, dayId),
            kind => CloseNightGo(program, kind, dayId),
            () => _settings.CloseNightOwnItems,
            lines =>
            {
                _settings.CloseNightOwnItems = lines;
                _settings.Save();
                program.RefreshAll();
                _todayControl?.RefreshAll();
            })
        {
            DayId = dayId,
        };

        form.FormClosed += (_, _) => _closeNight = null;
        _closeNight = form;
        form.Show(this);
    }

    /// <summary>The button beside a line of the closing checklist: the screen that sorts it, or for the backup, a backup.</summary>
    private async void CloseNightGo(RaceProgramControl program, CloseNightKind kind, string? dayId)
    {
        switch (kind)
        {
            case CloseNightKind.Rentals:
                GoToPage("Rentals");
                break;

            case CloseNightKind.Money:
                GoToPage("Race Day");
                program.ShowEntriesTab();
                break;

            case CloseNightKind.Website:
            case CloseNightKind.NightEmails:
                GoToPage("Club Website");
                break;

            case CloseNightKind.CloseDay:
                GoToPage("Race Day");
                program.ShowSetupTab();
                break;

            case CloseNightKind.Backup:
                // A backup now, to the usual folder - the same zip Backup &
                // Restore's Back Up Now makes. Ticked off once it's taken.
                try
                {
                    var folder = BackupService.SafeBackupFolder(_settings.AutoBackupFolder);
                    var secrets = BackupService.CaptureSecrets();
                    var path = await Task.Run(() => BackupService.CreateBackup(folder, automatic: false, secrets));
                    OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Closing checklist: backed up to {path}");
                    program.TickClosingItem("backup", true, dayId);
                    _closeNight?.Reload();
                }
                catch (Exception ex)
                {
                    OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Closing checklist: backup failed: {ex.Message}");
                    MessageBox.Show(this, "The backup couldn't be taken: " + ex.Message, "Close the Night", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                break;
        }
    }
}
