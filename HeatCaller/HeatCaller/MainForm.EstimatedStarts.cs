namespace HeatCaller;

/// <summary>Estimated start times for the phones' Schedule page (11 Nov 2026) - see NightPlan.About.</summary>
public partial class MainForm
{
    /// <summary>The estimated starts the schedule was last sent with, and when they were worked out.</summary>
    private (DateTime AtUtc, Func<string, string?> About)? _phoneEstimates;

    /// <summary>
    /// Each round's "about 20:42" for the phones' Schedule page (11 Nov 2026,
    /// NightPlan), worked out at most every 20 seconds - the schedule is
    /// built on every publish pass, and the plan reads the whole night.
    /// </summary>
    private Func<string, string?> EstimatedStartsForPhones()
    {
        if (_phoneEstimates is { } cached && DateTime.UtcNow - cached.AtUtc < TimeSpan.FromSeconds(20))
        {
            return cached.About;
        }

        Func<string, string?> about;

        try
        {
            about = NightPlan.AboutOf(_raceProgram?.Plan());
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] The schedule went without its estimated starts: {ex.Message}");
            about = _ => null;
        }

        _phoneEstimates = (DateTime.UtcNow, about);
        return about;
    }
}
