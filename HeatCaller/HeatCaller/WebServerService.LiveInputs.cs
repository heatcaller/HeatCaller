namespace HeatCaller;

/// <summary>
/// The window's side of the live state (/api/state and the relay's copy):
/// what BuildStateJsonCore reads from the stores the UI thread edits - the
/// web page's columns, on deck, who presents the broadcast's moments - read in one go, and the
/// profiles it names drivers from.
/// </summary>
/// <remarks>
/// WHY (24 Oct 2026, timing layer, P8): with timing on its own thread the
/// live state is built off the UI thread (MainForm.LiveState), so the
/// phones and the relay no longer freeze when the window does. These are
/// the parts only the UI thread may read: they are captured on the
/// window's 500 ms tick (CaptureUiSide) and the build reads the capture -
/// during a freeze they stay as they were, while the board itself comes
/// live from the timing thread. Inline nothing is captured: every build
/// reads them there and then, on the UI thread, exactly as it always did.
/// </remarks>
public sealed partial class WebServerService
{
    /// <summary>
    /// The window's side of the live state, read in one go on the UI thread.
    /// Immutable: the lists are copies. Presented is null when the sponsors
    /// could not be read, and the broadcast extras are then left out, as
    /// they were when the read failed inside AddBroadcast.
    /// </summary>
    private sealed record UiSide(
        IReadOnlyList<string> WebKpis,
        IReadOnlyList<string> RelayKpis,
        string OnDeck,
        IReadOnlyList<(string Moment, string Sponsor)>? Presented);

    /// <summary>The last CaptureUiSide, threaded mode only. Replaced whole.</summary>
    private volatile UiSide? _uiSide;

    /// <summary>
    /// The window's side, read now: the same expressions BuildStateJsonCore
    /// and AddBroadcast used to read in place. On the UI thread.
    /// </summary>
    private UiSide UiSideNow()
    {
        var webKpis = (_settings?.WebKpis ?? new List<string>()).ToList();
        var relayKpis = (_settings?.RelayKpis ?? new List<string>()).ToList();

        string onDeck = string.Empty;
        var next = _schedule?.NextUp;
        if (next is not null)
        {
            onDeck = string.Join("  •  ", new[] { next.ScheduledTime, next.ClassName, next.Description }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        // The sponsor slides themselves are no longer on the Wi-Fi pages
        // (13 Nov 2026): they only ever showed on Live Results, which was
        // taken off the LAN. The broadcast graphics' "presented by" is all
        // the live state carries about sponsors now.
        //
        // Who presents the moments the graphics show - see AddBroadcast.
        // Worked out here rather than copying the sponsor list: a booking's
        // Presents list is edited in place on the Sponsors page, so only the
        // answer is safe to hand to another thread. Guarded on its own, as
        // AddBroadcast's was: the board must not go for the sake of them.
        List<(string Moment, string Sponsor)>? presented;

        try
        {
            var sponsors = _sponsors?.Current.Sponsors;
            presented = new List<(string Moment, string Sponsor)>();

            foreach (var moment in new[]
                     {
                         SponsoredMoment.FastestLap, SponsoredMoment.LeadChange, SponsoredMoment.Battle,
                         SponsoredMoment.DriverIntros, SponsoredMoment.QualifyingRecap, SponsoredMoment.Milestones,
                         SponsoredMoment.RaceResult, SponsoredMoment.RunThrough,
                     })
            {
                if (SponsoredMoments.SponsorFor(sponsors, moment) is { } sponsor)
                {
                    presented.Add((moment.ToString(), sponsor.Name.Trim()));
                }
            }
        }
        catch
        {
            presented = null;
        }

        return new UiSide(webKpis, relayKpis, onDeck, presented);
    }

    /// <summary>
    /// Threaded mode, on the UI thread: captures the window's side for the
    /// next builds off the UI (MainForm.RefreshFrozenLiveInputs, every 500
    /// ms). Throws what the stores throw; the caller keeps the last capture.
    /// </summary>
    public void CaptureUiSide() => _uiSide = UiSideNow();

    /// <summary>
    /// The window's side for this build: inline read now, as always;
    /// threaded the last capture.
    /// </summary>
    /// <remarks>
    /// Threaded before the first capture it is read now, wherever the build
    /// is. MainForm captures before it starts building off the UI
    /// (StartLiveStateOffUi), so in practice that only ever happens on the
    /// UI thread - Start's first publish.
    /// </remarks>
    private UiSide CurrentUiSide() => _race.IsThreaded ? (_uiSide ?? UiSideNow()) : UiSideNow();

    /// <summary>
    /// Who each transponder belongs to, for the names on the live state:
    /// inline the store itself, as always; threaded its frozen view, safe
    /// off the UI thread (ProfileStore.GetForAnyThread).
    /// </summary>
    private IDriverLookup Names => _race.IsThreaded ? _profiles.AnyThread : _profiles;

    /// <summary>The document /api/state serves now - for the tests.</summary>
    internal string PublishedState => _publishedState;

    /// <summary>
    /// Builds the LAN document and publishes it, both inside _stateGate, so
    /// two publishes - the window's forced one (a target lap set) and the
    /// build off the UI - can never publish out of the order they built in.
    /// Inline everything is on the UI thread and this is what PublishState
    /// always did.
    /// </summary>
    private void BuildAndPublish()
    {
        lock (_stateGate)
        {
            _publishedState = BuildStateJsonCore(false, false);
        }
    }
}
