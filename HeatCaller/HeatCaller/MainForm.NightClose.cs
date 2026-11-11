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
    private NightClose.Facts NightClosingFacts(IReadOnlyList<SessionRecord> daySessions)
    {
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
            _raceEvents.Active is { IsLegacy: false, IsOpen: true });
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
}
