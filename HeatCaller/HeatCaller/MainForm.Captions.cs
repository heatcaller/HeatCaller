using System.Globalization;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The announcer's words as text (22 Oct 2026, driver experience, item 9):
/// filling <see cref="CaptionLog.Shared"/> from the announcer, and the
/// public <c>captions</c> snapshot on the relay for the club's live page.
/// </summary>
/// <remarks>
/// THE ONE PLACE THE LOG IS FILLED. The Big Screen, the Wi-Fi pages and the
/// OBS overlay all READ CaptionLog.Shared; only <see cref="WireCaptions"/>
/// writes to it. Two writers would number the same line twice, and a page
/// that tells new lines from old by their number would show it twice.
///
/// THREADS. LineHeard is raised on whatever thread the line plays from -
/// the voice queue's, a pool thread, now and then the UI thread. The
/// handler does one thing, CaptionLog.Add, which takes the log's own lock
/// and touches no control, so it is safe on any of them and never makes a
/// line late. Everything else here - the relay document, the "only if it
/// changed" cache, the settings - happens on the UI thread, on a timer of
/// its own (once a second) and on the roster tick (every two seconds),
/// which is where every other relay snapshot is built
/// (SendToRelayIfChanged's cache is a UI-thread dictionary).
///
/// COALESCED BY THE CLOCK. The one-second timer looks at the log's newest
/// number; three quick lines between two ticks go out as one document,
/// and a quiet second costs one integer compare. The publisher's queue
/// keeps only the newest copy of a snapshot anyway (latest wins).
///
/// PUBLIC, SO WITHHELD. Lines are kept in the log exactly as heard, because
/// the room's own screens show full names. The relay's copy leaves out every
/// line that mentions a driver who asked for initials only
/// (PublicPrivacy.MentionsWithheld, the listen-along rule; 22 Oct 2026,
/// security review), then still runs what is left through
/// PublicPrivacy.Current.Redact as a second layer - and because the roster
/// tick rebuilds it, a flag turned on mid-evening takes the lines already
/// published down within two seconds.
/// </remarks>
public partial class MainForm
{
    /// <summary>Lines in the public captions document - the design's "last 6".</summary>
    internal const int RelayCaptionLines = 6;

    /// <summary>
    /// The most the captions document may be. The relay's ceiling for the
    /// channel is 16 KB; a little under it leaves room for anything the
    /// publisher wraps round it. Six lines of 300 characters fit many times
    /// over - this only bites on a line of nothing but escaped characters.
    /// </summary>
    internal const int RelayCaptionBudgetBytes = 15 * 1024;

    /// <summary>The once-a-second check for new lines. Made in WireCaptions; UI thread only.</summary>
    private System.Windows.Forms.Timer? _captionsTimer;

    /// <summary>The newest line number the relay document was last built with, so a quiet second sends nothing.</summary>
    private long _captionsBuiltN;

    /// <summary>
    /// Whether captions have been switched on for the relay at any point in
    /// this run. Only then is the empty document sent when they are off -
    /// see PublishCaptionsToRelay.
    /// </summary>
    private bool _captionsWereOn;

    /// <summary>
    /// Whether captions were on for the relay the last time the document
    /// was built; null before the first build. With _captionsFloorN, keeps
    /// lines said while captions were OFF from appearing once they are
    /// switched on.
    /// </summary>
    private bool? _captionsLastOn;

    /// <summary>Only lines numbered above this go to the relay: the log's newest number when captions were last switched on.</summary>
    private long _captionsFloorN;

    /// <summary>When the relay's captions went empty (switched off, or nothing said yet), for the empty document's <c>at</c>. Null while it has lines.</summary>
    private DateTime? _captionsEmptySinceUtc;

    /// <summary>When the captions document was last forgotten from the "only if changed" cache - see RosterTickCaptions.</summary>
    private DateTime _captionsForgotUtc;

    partial void WireCaptions()
    {
        // Every line, whatever is switched on: the log is cheap, and the
        // Big Screen strip and the Wi-Fi pages read it with no relay at all.
        // Any thread - see the remarks. Never throws into the voice path.
        _announcer.LineHeard += text =>
        {
            try
            {
                CaptionLog.Shared.Add(text);
            }
            catch
            {
                // A caption is never worth a line of the broadcast.
            }

            // The driver introductions' Big Screen cards (1 Nov 2026) hear
            // every line too, to know the introductions when they come -
            // see IntroOnAir.
            try
            {
                IntroOnAir.Shared.Heard(text);
            }
            catch
            {
                // Nor is a card.
            }
        };

        _captionsForgotUtc = DateTime.UtcNow;

        // Once a second, on the UI thread. Made here rather than with an
        // initializer because this is a part file (fields with initializers
        // live in MainForm.cs). Stopped and disposed when the window closes.
        _captionsTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _captionsTimer.Tick += (_, _) =>
        {
            if (_closingDown)
            {
                return;
            }

            // Only when something new has been said; the roster tick below
            // looks after everything else.
            if (CaptionLog.Shared.LatestN != _captionsBuiltN)
            {
                PublishCaptionsToRelay();
            }
        };
        _captionsTimer.Start();

        FormClosed += (_, _) =>
        {
            _captionsTimer?.Stop();
            _captionsTimer?.Dispose();
            _captionsTimer = null;
        };
    }

    partial void RosterTickCaptions()
    {
        // Every ten minutes, forget what was sent, as PublishExtrasToRelay
        // does for its own snapshots (it can't know about this one): a
        // document counts as sent when it is queued, so one the relay lost
        // to a restart would otherwise stay missing until the next line.
        // Only while captions are on - the empty document needs saying once,
        // not every ten minutes.
        if (_settings.RelayCaptions && DateTime.UtcNow - _captionsForgotUtc > TimeSpan.FromMinutes(10))
        {
            _captionsForgotUtc = DateTime.UtcNow;
            _lastRelayDocuments.Remove(RelayChannel.Captions);
        }

        // Every tick, so the switch going on or off, and the privacy
        // register changing, reach the relay within two seconds. Sent only
        // when the document differs from the last one.
        PublishCaptionsToRelay();
    }

    /// <summary>
    /// Builds the public captions document and sends it if it changed. UI
    /// thread only (the timer and the roster tick).
    /// </summary>
    /// <remarks>
    /// OFF MEANS EMPTY, NOT SILENT. A club that unticks "captions" must see
    /// the last lines come off its page, so the document is replaced with
    /// one holding no lines - the way the check-in list withdraws itself.
    /// But only after captions have been on in this run: a club that has
    /// never used them, on a relay from before captions, would otherwise be
    /// told on every start that its relay "has no captions channel".
    /// </remarks>
    private void PublishCaptionsToRelay()
    {
        try
        {
            _captionsBuiltN = CaptionLog.Shared.LatestN;

            var on = _settings.RelayCaptions;

            // Switched on during the run: only what is said from now on. A
            // club may untick captions precisely to keep a stretch of the
            // evening off the internet, and ticking them again must not
            // publish that stretch after all. On from the start, everything
            // the log holds was said while they were on.
            //
            // WHY (23 Oct 2026, bug hunt 10): tracked BEFORE the relay check.
            // It was after it, so captions unticked and ticked again while the
            // club website was off were never seen, the floor stayed put, and
            // the lines said while they were off went out once the website
            // came back. Both callers run every two seconds whether or not the
            // relay is on, so the switch is seen either way.
            if (on && _captionsLastOn == false)
            {
                _captionsFloorN = CaptionLog.Shared.LatestN;
            }

            _captionsLastOn = on;

            if (!_settings.RelayEnabled)
            {
                return;
            }

            if (on)
            {
                _captionsWereOn = true;
            }
            else if (!_captionsWereOn)
            {
                return;
            }

            var floor = _captionsFloorN;
            IReadOnlyList<CaptionLine> lines = on
                ? CaptionLog.Shared.Last(RelayCaptionLines).Where(l => l.N > floor).ToList()
                : Array.Empty<CaptionLine>();

            var privacy = PublicPrivacy.Current;

            // A car said by its transponder number, and the club's "shorten
            // published names", reach the public copy too (23 Oct 2026, bug
            // hunt 10) - see CaptionsDocument. TextForPublicPages shortens only
            // while that box is ticked, and leaves the line alone otherwise -
            // whether or not the voices say names in full (24 Oct 2026).
            Func<string, bool> namesACarByNumber = AnnouncementService.NamesACarByNumber;
            Func<string, string>? shorten = _settings.RelayAbbreviateNames ? _announcer.TextForPublicPages : null;

            // The empty document's time is when it went empty, kept, so the
            // same empty document is the same bytes on every tick.
            //
            // WHY (23 Oct 2026, bug hunt 10): judged by the lines that will
            // actually be PUBLISHED, not the raw ones. When every recent line
            // was withheld (race control's calls, an initials-only driver) the
            // raw list was not empty, so the empty document was stamped "now"
            // and sent again on every two-second tick.
            DateTime? emptySince = null;

            if (PublicCaptionLines(lines, privacy, namesACarByNumber, shorten).Count == 0)
            {
                _captionsEmptySinceUtc ??= DateTime.UtcNow;
                emptySince = _captionsEmptySinceUtc;
            }
            else
            {
                _captionsEmptySinceUtc = null;
            }

            SendToRelayIfChanged(RelayChannel.Captions, "the captions",
                () => CaptionsDocument(lines, privacy, emptySince ?? DateTime.UtcNow, namesACarByNumber, shorten));
        }
        catch (Exception ex)
        {
            // SendToRelayIfChanged says its own build errors; this is only
            // for anything round it. Captions are never worth the tick.
            if (_relayBuildErrors.Add($"captions|{ex.Message}"))
            {
                OnClientRawMessage($"[relay] Could not send the captions: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The public <c>captions</c> document: <c>{at, lines:[{n,t,at}]}</c>,
    /// oldest line first, without lines naming an initials-only driver
    /// or race control's calls, the rest redacted for the internet. Its
    /// <c>at</c> is the newest line's time, or <paramref name="emptyAtUtc"/>
    /// when there are no lines - never "now", so the same lines are the same
    /// bytes and the "only if changed" cache works. Older lines are dropped
    /// first if it would be over <see cref="RelayCaptionBudgetBytes"/>.
    /// </summary>
    /// <param name="namesACarByNumber">
    /// Whether a line names a car by its transponder number
    /// (AnnouncementService.NamesACarByNumber); such a line is left out. Null
    /// leaves every line in, as tests do.
    /// </param>
    /// <param name="shorten">
    /// The club's "shorten published names" (AnnouncementService
    /// .TextForCloudVoice), or null when the box is unticked.
    /// </param>
    internal static string CaptionsDocument(
        IReadOnlyList<CaptionLine> lines,
        PublicPrivacy privacy,
        DateTime emptyAtUtc,
        Func<string, bool>? namesACarByNumber = null,
        Func<string, string>? shorten = null)
    {
        var kept = PublicCaptionLines(lines, privacy, namesACarByNumber, shorten);

        var json = Build();

        // Oldest first out, while there is anything left to drop.
        while (kept.Count > 0 && Encoding.UTF8.GetByteCount(json) > RelayCaptionBudgetBytes)
        {
            kept.RemoveAt(0);
            json = Build();
        }

        return json;

        string Build() => JsonSerializer.Serialize(new
        {
            at = CaptionStamp(kept.Count > 0 ? kept[^1].AtUtc : emptyAtUtc),
            lines = kept.Select(l => new { n = l.N, t = l.Text, at = CaptionStamp(l.AtUtc) }).ToList(),
        });
    }

    /// <summary>
    /// The lines the public captions document carries, oldest first: the
    /// newest <see cref="RelayCaptionLines"/>, less everything withheld, the
    /// rest redacted (and shortened) for the internet. Its own method
    /// (23 Oct 2026, bug hunt 10) so PublishCaptionsToRelay decides "empty"
    /// by the same rule the document is built by - see the WHY there.
    /// </summary>
    internal static List<CaptionLine> PublicCaptionLines(
        IReadOnlyList<CaptionLine> lines,
        PublicPrivacy? privacy,
        Func<string, bool>? namesACarByNumber = null,
        Func<string, string>? shorten = null)
    {
        var register = privacy ?? PublicPrivacy.Empty;

        // 6 Nov 2026, Bug hunt 11: every rule below judges l.Said - the whole
        // spoken line the piece came from - not the piece. A long line is
        // captioned a piece at a time (CaptionLog, 26 Oct), and judged piece
        // by piece only the first piece of a long black flag started "Black
        // flag," and only the piece naming a withheld driver named them, so
        // the typed reason and the rest of the line went online. Judged
        // whole, a line is left out with every one of its pieces.
        return lines
            .Skip(Math.Max(0, lines.Count - RelayCaptionLines))
            // Race control's warning and black-flag calls never go online
            // (22 Oct 2026, security review): they carry the reason the race
            // director typed about a named driver (RaceControlControl
            // .OnWarningIssued), and design 1.4 rule 1 keeps reasons to the
            // driver's own night and the room. The room still hears and
            // sees them; the public results carry the numbers.
            .Where(l => !AnnouncementService.IsRaceControlCall(l.Said))
            // A line that mentions an initials-only driver - by any name, or
            // any word of one - is left out whole, the way listen-along
            // withholds it (22 Oct 2026, security review). Redact alone let a
            // first name or a word the register doesn't hold through; it
            // stays below as a second layer.
            .Where(l => !register.MentionsWithheld(l.Said))
            // A line naming a car by its transponder number is left out
            // whole (23 Oct 2026, bug hunt 10), the way listen-along withholds
            // it: the announcer says an unnamed car's number, and a
            // transponder number must never go online.
            .Where(l => namesACarByNumber?.Invoke(l.Said) != true)
            .Select(l => PublicPiece(l, register, shorten))
            .Where(l => l.Text.Length > 0)
            .ToList();
    }

    /// <summary>
    /// One captioned piece as it goes online: redacted, and with "Shorten
    /// published names" (23 Oct 2026, bug hunt 10 - the pages showed "Sam S."
    /// while this showed "Sam Smith") shortened. Keeps the piece's number and
    /// time; its text may come out empty, and is then left out.
    /// </summary>
    private static CaptionLine PublicPiece(CaptionLine l, PublicPrivacy register, Func<string, string>? shorten)
    {
        var redacted = register.Redact(l.Text);

        // JUNIORS ALWAYS SHORTENED (15 Nov 2026, children's privacy plan
        // 1.3): Redact makes "Mia Kent" "Mia K" whatever the club's setting.
        // With the setting off and a junior in the register, the line still
        // goes the whole-line way below, so a piece cut inside a junior's
        // name ("...third is Mia" / "Kent in the...") can't let the surname out.
        if (shorten is null)
        {
            if (!register.AnyJuniors)
            {
                return new CaptionLine(l.N, redacted, l.AtUtc);
            }

            shorten = static text => text;
        }

        // 6 Nov 2026, Bug hunt 11b review: shortened piece by piece, a long
        // line cut inside a name ("...third is Sam" / "Smith in the...") gave
        // the shortener neither piece with the full name in it, so it changed
        // nothing and the full surname went online. A piece of a longer line
        // is now taken from the WHOLE line shortened (and redacted) first and
        // then cut again by CaptionLog's own rule - the piece at the same
        // place. A shortened line is never longer, so it cuts into as many
        // pieces or fewer: a later piece with nothing left comes out empty and
        // is left out, and the numbering of the rest is untouched. The room's
        // own captions keep the pieces as heard.
        var whole = l.WholeText is null ? null : CaptionLog.Pieces(l.WholeText);

        if (whole is null || whole.Count <= 1)
        {
            return new CaptionLine(l.N, shorten(redacted), l.AtUtc);
        }

        var cut = CaptionLog.Pieces(shorten(register.Redact(l.WholeText!)));
        var i = l.PieceIndex;

        var text = i >= cut.Count || i >= whole.Count
            ? string.Empty
            // The line's last piece carries whatever is left, should a
            // shortener ever make a line longer: nothing is dropped unseen.
            : i == whole.Count - 1 ? string.Join(" ", cut.Skip(i)) : cut[i];

        // Redacted again as the second layer it always was - the cut can't
        // join a name back together, but it costs nothing to be sure.
        return new CaptionLine(l.N, register.Redact(text), l.AtUtc);
    }

    /// <summary>A UTC time as the relay's documents write it, to the second: "2026-10-22T19:40:10Z".</summary>
    internal static string CaptionStamp(DateTime utc) =>
        (utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc)
        .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
