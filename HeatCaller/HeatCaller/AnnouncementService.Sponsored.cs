namespace HeatCaller;

/// <summary>
/// Sponsored segments and broadcast moments - the two things the announcer
/// hands the rest of the broadcast besides its voice.
///
/// SPONSORED SEGMENTS: a sponsor ticked on the Sponsors page for a moment
/// (SponsoredMoments) has their credit - "That fastest lap is brought to
/// you by ..." - added to that moment's call,
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
    ///
    /// WHY (20 Oct 2026, Bug Hunt 8): the ration is reserved here, as the
    /// line is built, but a routine call (fast lap, lead change, battle) is
    /// often then dropped or refused by the queue - speaker busy, gap not
    /// run, muted ghost, post-race quiet. Nothing gave the reservation back,
    /// so the sponsor lost "Presented by" on every such call for the next
    /// three minutes over a line nobody heard. It now also hands back a
    /// Release to run when EnqueueVoiceWork turns the line down; Release
    /// only undoes this reservation, never a later one or a real mention.
    /// The result still deconstructs into (Tag, Count) for callers that
    /// have not picked Release up yet.
    /// </summary>
    private SponsorTagResult SponsorTag(AnnouncementEventType type)
    {
        if (SponsoredMoments.ForEvent(type) is not { } moment
            || SponsorOf(moment) is not { } sponsor
            || !SponsorMentions.TryReserve(moment, out var stamp, out var previous))
        {
            return new SponsorTagResult(string.Empty, null, null);
        }

        // A whole sentence about the moment, said brightly (21 Oct 2026) -
        // see SponsoredMoments.Tag for why the old " Presented by X." sounded
        // robotic - and the sponsor's tagline after it, where they gave one.
        var tag = SponsoredMoments.Tag(moment, sponsor.Name, sponsor.Tagline);

        if (tag.Length == 0)
        {
            // WHY (20 Oct 2026, Bug Hunt 8): a blank sponsor name says
            // nothing, so it must not use up the ration either.
            SponsorMentions.Release(moment, stamp, previous);
            return new SponsorTagResult(string.Empty, null, null);
        }

        var id = sponsor.Id;
        return new SponsorTagResult(tag,
            () => SponsorMentions.Mentioned(moment, id),
            () => SponsorMentions.Release(moment, stamp, previous));
    }

    /// <summary>
    /// What <see cref="SponsorTag"/> hands back: the tag, what to run once
    /// the line goes to the speaker (Count), and what to run if the queue
    /// turns the line down or loses it (Release) so the ration is not spent
    /// on a line nobody heard. Deconstructs as (Tag, Count) or
    /// (Tag, Count, Release).
    /// </summary>
    private readonly record struct SponsorTagResult(string Tag, Action? Count, Action? Release)
    {
        public void Deconstruct(out string tag, out Action? count)
        {
            tag = Tag;
            count = Count;
        }
    }

    /// <summary>
    /// The sponsor presenting <paramref name="moment"/> in a line the AI
    /// commentary writes (18 Oct 2026) - the intros, the recap, a segment -
    /// with the ration taken, and what to run once the line is actually
    /// going out, which is when it counts for the sponsor report. Null when
    /// nobody presents it or the ration is holding it back.
    /// </summary>
    internal (string Name, Action Count)? TakeSponsor(SponsoredMoment moment) =>
        TakeSponsorWithRelease(moment) is { } taken ? (taken.Name, taken.Count) : null;

    /// <summary>
    /// The same, with how to give the ration back for a line that never went
    /// out (22 Oct 2026, bug hunt) - a written segment that came back empty
    /// used to hold the sponsor's credit off the announcer's own calls for
    /// three minutes. TakeSponsor is this without the give-back.
    /// </summary>
    internal (string Name, Action Count, Action Release)? TakeSponsorWithRelease(SponsoredMoment moment)
    {
        if (SponsorOf(moment) is not { } sponsor
            || !SponsorMentions.TryReserve(moment, out var stamp, out var previous))
        {
            return null;
        }

        var name = (sponsor.Name ?? string.Empty).Trim().TrimEnd('.', '!', ' ');

        if (name.Length == 0)
        {
            // WHY (20 Oct 2026, Bug Hunt 8): no name, no mention - give the
            // ration back rather than spend it on nothing.
            SponsorMentions.Release(moment, stamp, previous);
            return null;
        }

        var id = sponsor.Id;
        return (name, () => SponsorMentions.Mentioned(moment, id), () => SponsorMentions.Release(moment, stamp, previous));
    }

    /// <summary>
    /// The tagline of the sponsor credited by <paramref name="sponsorName"/>
    /// in tonight's rotation, or null - what CommentaryService.SponsorTagline
    /// is wired to, for the credits on the lines the AI commentary writes
    /// (21 Oct 2026). See SponsoredMoments.TaglineOf.
    /// </summary>
    internal string? TaglineOf(string? sponsorName)
    {
        try
        {
            return SponsoredMoments.TaglineOf(SponsorsProvider?.Invoke(), sponsorName);
        }
        catch
        {
            // Read off the UI thread while the Sponsors page may be editing
            // the list - the credit without its tagline, for once.
            return null;
        }
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
