namespace HeatCaller;

/// <summary>
/// Sponsored segments and broadcast moments - the two things the announcer
/// hands the rest of the broadcast besides its voice.
///
/// SPONSORED SEGMENTS: a sponsor ticked on the Sponsors page for a moment
/// (SponsoredMoments) has " Presented by ..." added to that moment's call,
/// rationed by <see cref="SponsorMentions"/> and counted for the sponsor
/// report once the line is actually on its way to the speaker.
///
/// BROADCAST MOMENTS: a lead change and a milestone are posted for the
/// stream's banner (WebServerService.Moments) whether or not the voice says
/// anything - a club can mute the announcer and still have the graphics.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// The club's sponsors, read fresh each time a moment might carry one -
    /// a sponsor sold at the sign-up desk mid-evening counts from their next
    /// moment. Set by MainForm. Null leaves every call unsponsored.
    /// </summary>
    public Func<IReadOnlyList<SponsorBooking>>? SponsorsProvider { get; set; }

    /// <summary>Tonight's "presented by" mentions and the ration on them. Read by the Sponsors page for the report.</summary>
    internal SponsoredMomentGate SponsorMentions { get; } = new();

    /// <summary>
    /// Something worth a banner on the stream - a lead change, a milestone.
    /// Raised on whichever thread noticed it; the handler must not touch UI.
    /// </summary>
    public event Action<BroadcastMoment>? MomentHappened;

    /// <summary>The sponsor presenting <paramref name="moment"/> now, or null.</summary>
    private SponsorBooking? SponsorOf(SponsoredMoment moment)
    {
        try
        {
            return SponsoredMoments.SponsorFor(SponsorsProvider?.Invoke(), moment);
        }
        catch
        {
            // Read from a lap thread while the Sponsors page may be editing
            // the list. Unsponsored for once beats a call that never comes.
            return null;
        }
    }

    /// <summary>
    /// The " Presented by ..." for a call of this type, and what to run once
    /// the call is actually going to the speaker - which is when it counts.
    /// Empty, with nothing to run, for a type nobody can sponsor, a moment
    /// nobody presents, or one the ration is holding back.
    /// </summary>
    private (string Tag, Action? Count) SponsorTag(AnnouncementEventType type)
    {
        if (SponsoredMoments.ForEvent(type) is not { } moment
            || SponsorOf(moment) is not { } sponsor
            || !SponsorMentions.TryReserve(moment))
        {
            return (string.Empty, null);
        }

        var tag = SponsoredMoments.Tag(sponsor.Name);

        if (tag.Length == 0)
        {
            return (string.Empty, null);
        }

        var id = sponsor.Id;
        return (tag, () => SponsorMentions.Mentioned(moment, id));
    }

    /// <summary>Raises <see cref="MomentHappened"/> without letting a bad handler reach the lap pipeline.</summary>
    private void RaiseMoment(BroadcastMoment moment)
    {
        try
        {
            MomentHappened?.Invoke(moment);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[broadcast] Couldn't post a moment to the graphics: {ex.Message}");
        }
    }

    /// <summary>The banner for a lead change: "Sam takes the lead from Bill" and the count once there is one.</summary>
    private void PostLeadChangeMoment(LeadChange change)
    {
        var line = $"{ScreenNameFor(change.NewLeader)} takes the lead from {ScreenNameFor(change.Previous)}";

        if (change.Number >= 2)
        {
            line += $" · lead change {change.Number}"
                + (change.Leaders >= 2 ? $" among {change.Leaders} drivers" : string.Empty);
        }

        RaiseMoment(new BroadcastMoment("lead", "Lead change", line, SponsorOf(SponsoredMoment.LeadChange)?.Name.Trim()));
    }

    /// <summary>The banner for an achievement: its name over the driver's.</summary>
    private void PostMilestoneMoment(string transponderId, string badgeName) =>
        RaiseMoment(new BroadcastMoment("milestone", badgeName, ScreenNameFor(transponderId),
            SponsorOf(SponsoredMoment.Milestones)?.Name.Trim()));

    /// <summary>
    /// A driver's name as a screen should show it - the real name, not the
    /// respelling the voice is given (DriverProfile.AnnouncerName), which
    /// would put "Zhock" on the stream.
    /// </summary>
    private string ScreenNameFor(string transponder)
    {
        var profile = _profiles.Get(transponder);

        // Never the transponder: the banner goes out in /api/state, which any
        // phone on the club Wi-Fi can read with no PIN.
        return !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName : RelayDocuments.UnnamedDriver;
    }
}
