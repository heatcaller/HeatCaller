namespace HeatCaller;

/// <summary>
/// OFFLINE-FIRST, SAID OUT LOUD (11 Nov 2026, race-night build plan, Phase
/// 6.3): the line the director sees on Race Control's Next Up bar when the
/// club website can't be reached, and the standby PC's line when this PC is
/// one.
///
/// WHY a line at all: HeatCaller never needed the internet to run a race -
/// the decoder, the timing, the announcer, check-in at the desk, the night
/// plan, auto start and results are all on this PC, and everything bound for
/// the website goes into RelayPublisher's queue and is sent by its own
/// worker thread. But nothing said so. A director who saw the Wi-Fi drop
/// and the website stop updating had no way to know whether the night was
/// in danger, and the instinct is to stop and fix the internet. The answer
/// is "carry on, it will catch up", so the app says exactly that, on the
/// screen the director is already looking at, and only while it is true.
///
/// Mission Control's Website tile says the same in more words; this is the
/// one line for the bar.
/// </summary>
internal static class OfflineFirst
{
    /// <summary>The words, kept in one place so the docs and the tests quote the same thing.</summary>
    public const string CatchUp = "Online pages will catch up";

    /// <summary>
    /// The website line, or null when there is nothing to say: publishing
    /// off or not set up (the club's choice, not a problem), online, or not
    /// yet tried (no error yet and nothing waiting - the first send after
    /// startup has not happened, and saying "offline" then would be wrong).
    /// </summary>
    public static string? RelayLine(RelayStatus? relay)
    {
        if (relay is not { Enabled: true, Configured: true } || relay.Online)
        {
            return null;
        }

        if (relay.LastError is null && relay.Queued == 0)
        {
            return null;
        }

        var waiting = relay.Queued > 0 ? $" ({Plural.Of(relay.Queued, "update")} waiting)" : string.Empty;

        return $"Website offline - racing carries on. {CatchUp}{waiting}";
    }

    /// <summary>Everything worth a line on the bar, joined: the website first, then the standby PC.</summary>
    public static string? BarLine(RelayStatus? relay, WarmStandby.MirrorStatus? standby, DateTime nowUtc)
    {
        var parts = new List<string>();

        if (RelayLine(relay) is { } website)
        {
            parts.Add(website);
        }

        if (standby is not null && WarmStandby.Line(standby, nowUtc) is { } copy)
        {
            parts.Add(copy.TrimEnd('.'));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }
}
