using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The announcer's voice, going out to anyone listening from home.
///
/// THE LAPTOP'S HALF OF LISTENING ALONG (13 Oct 2026)
///
/// The relay side of this shipped first: a "listen" collection, a switch
/// the club holds, a publish gate that refuses lines while it is off, and
/// a player on the club page. This is the half that puts something there
/// to play.
///
/// SENT ON ITS OWN, NOT THROUGH THE RESULTS QUEUE
///
/// Everything else with an item id goes through <c>Enqueue</c> and comes
/// out of <c>_results</c>. These do not, and the reason is in
/// <see cref="ListenAlong"/>: that queue drops its oldest when it fills
/// and it is shared, so an evening of announcing would have quietly pushed
/// a session's results off the front of it. Results are what a club
/// actually needs the relay for. Nothing about listening along is allowed
/// to cost one.
///
/// WHY THE SWITCH IS SENT AND NOT INFERRED
///
/// The same reason the shout-outs switch is (see
/// <see cref="CloseClubNotesOnTheRelay"/>): the relay cannot tell a club
/// that has switched listening off from a club whose announcer has gone
/// quiet. Left to be inferred, the page would go on offering a Listen
/// button that plays the last few lines of a club that has withdrawn
/// consent to be broadcast at all. So it is told - on every start, and
/// again whenever it changes.
/// </summary>
public sealed partial class RelayPublisher
{
    // ON A THREAD OF ITS OWN (19 Nov 2026, the music "skips or drops").
    // Lines and stretches of the room's sound went out on the publisher's
    // worker, first in each pass and between the documents it sent - but a
    // stretch offered while a document was uploading still waited for that
    // upload (up to a minute on a slow uplink) or for a poll, and a stretch
    // is thrown away after ListenAlong.SegmentGoesStale. Now listening has
    // its own thread and its own client, so nothing the worker does can
    // hold it up. What it shares with the worker it shares carefully:
    //  - the target, read once per send, as the worker reads it;
    //  - the gates the worker passes through - publishing on, set up, not
    //    quiesced (Delete Everything Published), not backing off - so it
    //    never sends where the worker would not;
    //  - the backoff itself, under _gate: a relay that cannot be reached
    //    from here backs the worker off too, and the other way round, so
    //    the two never take turns hammering a relay that is down;
    //  - the seq numbers, under _gate (NextSeq).

    /// <summary>Listening's own client, so a document upload on the worker's never holds a stretch up. The same rules as the worker's: no redirects, a small answer.</summary>
    private readonly HttpClient _listenHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(60),
        MaxResponseContentBufferSize = 64 * 1024,
    };

    /// <summary>Set when a line or stretch is offered, or the switch changes.</summary>
    private readonly ManualResetEventSlim _listenWake = new(false);

    /// <summary>Set whenever the listen thread is between passes. See QuiesceAsync.</summary>
    private readonly ManualResetEventSlim _listenIdle = new(true);

    /// <summary>The listen thread's current pass, so QuiesceAsync can cut it short. Guarded by _gate.</summary>
    private CancellationTokenSource? _listenPass;

    /// <summary>The listen thread - see the comment above. Started by the constructor, stopped by Dispose.</summary>
    private readonly Thread _listener;

    /// <summary>
    /// How long before the open flag is tried again after it failed.
    ///
    /// WITHOUT THIS IT WAS TWICE A SECOND. Listening has no poll tick of
    /// its own - it goes the moment the announcer speaks, which is the
    /// point of it - so a relay that answers 404 because it has not been
    /// redeployed, or a club that is offline, would have been asked on
    /// every 500ms pass of the worker for the whole evening. Nothing else
    /// here does that: notes ask every thirty seconds, check-in every
    /// four.
    /// </summary>
    private static readonly TimeSpan ListenOpenRetryEvery = TimeSpan.FromSeconds(30);

    /// <summary>When the open flag may be tried again, after a failure.</summary>
    private long _nextListenOpenTick;

    /// <summary>
    /// The relay, club and token the backoff above was earned against.
    /// WHY (2026-11-05, Bug hunt 11: RelayPublisher): the drivers'-nights
    /// flag got this in #85 and listening, the same shape, did not. A club
    /// id typed wrong got a 404, was fixed and saved - and the corrected
    /// club was not asked for up to thirty seconds, so it drew no Listen
    /// button, the stale 404 stayed on the page, and the announcer's lines
    /// and room sound of that half-minute were never sent. Now a backoff
    /// only holds back the target that failed.
    /// </summary>
    private RelayTarget? _listenOpenBackoffFor;

    /// <summary>What went wrong sending the announcer's lines, for the page to show. Null while it is working.</summary>
    private volatile string? _listenProblem;

    /// <summary>Whether the relay has been told this club is letting people listen - so the flag goes once rather than every pass.</summary>
    private volatile bool _listenOpenTold;

    /// <summary>The state last told to the relay, so a club switching it off is actually sent the change.</summary>
    private volatile bool _listenOpenAsTold;

    /// <summary>
    /// Whether the box was unticked and the relay has not yet taken the
    /// close. Cleared only when it has - see <see cref="SendListenClose"/>.
    /// </summary>
    private volatile bool _listenClosePending;

    /// <summary>What went wrong sending the announcer's lines out, or null.</summary>
    public string? ListenProblem => _listenProblem;

    /// <summary>The problem last said out loud, so it is said once rather than every pass.</summary>
    private volatile string? _listenProblemAsSaid;

    /// <summary>
    /// Records what went wrong and says it once, on a change - the pattern
    /// VoteTrouble set (17 Oct 2026, Bug Hunt 8, A17-2). The problem was
    /// recorded in a field nothing read, so a route that silently stopped
    /// working on a race night left no trace in the console or the log.
    /// </summary>
    private void ListenTrouble(string? problem)
    {
        _listenProblem = problem;

        if (string.Equals(_listenProblemAsSaid, problem, StringComparison.Ordinal))
        {
            return;
        }

        _listenProblemAsSaid = problem;

        if (problem is { Length: > 0 })
        {
            Log?.Invoke($"[relay] Listening along on the club's page: {problem}");
        }
        else
        {
            Log?.Invoke("[relay] Listening along on the club's page is working again.");
        }
    }

    /// <summary>
    /// Clears the problem without saying anything, for the club switching
    /// the route off - not a fault, so no "working again" on its way out.
    /// </summary>
    private void ForgetListenTrouble()
    {
        _listenProblem = null;
        _listenProblemAsSaid = null;
    }

    /// <summary>
    /// The queue of lines waiting to go, and the policy that decides what
    /// is worth sending.
    ///
    /// INTERNAL because <see cref="ListenAlong"/> is: a public property on
    /// a public class may not name an internal type, which is CS0053 and
    /// the whole solution. The same rule <see cref="TonightsNotes"/> got
    /// wrong once and now says so.
    /// </summary>
    internal ListenAlong Listening { get; } = new();

    /// <summary>
    /// Offers a line the announcer has just said.
    ///
    /// Called from the audio side once the line is an mp3, so it does the
    /// least possible: a lock, a queue operation, and a set of the
    /// worker's wake handle. Everything slow - the encoding, the network -
    /// happens either side of it. A line that made the announcer late
    /// would be a feature nobody would keep switched on.
    /// </summary>
    /// <param name="dayId">
    /// Tonight's race day, on the same call as the line. There is no
    /// race-day-changed event in this app, so the day goes in with the
    /// work and ListenAlong notices for itself - the pattern
    /// ClubNotes.NewMeeting and RoomVote.NewMeeting both use.
    /// </param>
    internal ListenAlong.Verdict OfferToListeners(string? dayId, string? text, byte[]? mp3, DateTime atUtc)
    {
        Listening.NewMeeting(dayId);

        // READ FROM SETTINGS HERE, not left to the worker's pass to latch.
        // The worker sets this too, but only every 500ms, so a line
        // offered in the moment after the box was ticked would have been
        // refused for nobody listening - and the encoding it had just paid
        // for thrown away. Setting it to false is what empties the queue
        // (ListenAlong.Wanted), so this is also the fast path for
        // unticking.
        Listening.Wanted = _settings.ListenAlongOnTheRelay;

        var verdict = Listening.Offer(text, mp3, atUtc);

        if (verdict == ListenAlong.Verdict.Held)
        {
            // Woken rather than left to the next 500ms tick: half a second
            // on top of the encode is already most of a word.
            _listenWake.Set();
        }

        return verdict;
    }

    /// <summary>
    /// Offers a stretch of the room's sound (2 Nov 2026 - see ListenStream).
    /// The same queue and the same wake as a line; called on the stream's
    /// encoder thread, so it does as little as <see cref="OfferToListeners"/>.
    /// </summary>
    internal ListenAlong.Verdict OfferStreamToListeners(string? dayId, ListenSegment segment)
    {
        Listening.NewMeeting(dayId);
        Listening.Wanted = _settings.ListenAlongOnTheRelay;

        var verdict = Listening.OfferSegment(segment);

        if (verdict == ListenAlong.Verdict.Held)
        {
            _listenWake.Set();
        }

        return verdict;
    }

    /// <summary>
    /// The listen thread (19 Nov 2026 - see the comment on _listenHttp):
    /// woken when a line or a stretch is offered or the switch changes, and
    /// every half second besides, it does what the worker's pass used to do
    /// for listening - and nothing else, so nothing else can hold it up.
    /// Through the same gates as the worker's pass: publishing on, set up,
    /// not backing off, not quiesced.
    /// </summary>
    private void ListenLoop()
    {
        var token = _stopping.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var backingOff = BackoffRemaining();

                _listenWake.Wait(backingOff > TimeSpan.Zero ? backingOff : TimeSpan.FromMilliseconds(500), token);
                _listenWake.Reset();

                if (token.IsCancellationRequested)
                {
                    break;
                }

                if (!_settings.RelayEnabled || !IsConfigured() || BackoffRemaining() > TimeSpan.Zero)
                {
                    continue;
                }

                CancellationTokenSource? pass = null;

                lock (_gate)
                {
                    if (!_quiesced)
                    {
                        pass = CancellationTokenSource.CreateLinkedTokenSource(token);
                        _listenPass = pass;
                        _listenIdle.Reset();
                    }
                }

                if (pass is null)
                {
                    continue;
                }

                try
                {
                    ListenPass(pass.Token);
                }
                finally
                {
                    lock (_gate)
                    {
                        _listenPass = null;
                    }

                    _listenIdle.Set();
                    pass.Dispose();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Never worth the thread: the next wake tries again.
            }
        }

        try
        {
            _listenIdle.Set();
        }
        catch (ObjectDisposedException)
        {
            // Disposed on the way out; nobody is waiting on it.
        }
    }

    /// <summary>
    /// One pass of the listen thread. On NO tick of its own: a line that
    /// arrives late is worse than a line that never arrives, so it goes the
    /// moment it is offered. The cost of having no tick is paid by the open
    /// flag's own backoff, the shared backoff, and the queue being allowed
    /// to throw work away.
    /// </summary>
    private void ListenPass(CancellationToken token)
    {
        if (_settings.ListenAlongOnTheRelay)
        {
            Listening.Wanted = true;
            SendListenLines(token);
            return;
        }

        if (Listening.Wanted)
        {
            // Unticked while running. This is the queue letting go of what
            // it was holding, so a box unticked mid-race does not send three
            // more lines after it.
            Listening.Wanted = false;
            ForgetListenTrouble();
        }

        // And the relay told, until it has taken it - see
        // CloseListeningOnTheRelay.
        if (_listenClosePending)
        {
            SendListenClose(token);
        }
    }

    /// <summary>
    /// Tells the relay whether this club is letting people listen, and
    /// sends whatever is waiting, oldest first, until the queue is empty or
    /// a send fails. On the listen thread, so nothing on this path touches
    /// the UI thread or waits on the worker.
    /// </summary>
    private void SendListenLines(CancellationToken token)
    {
        var target = _target;
        var club = Uri.EscapeDataString(target.Club);

        if (!IsSafeForToken(target.Root))
        {
            ListenTrouble(UnsafeAddressMessage);
            return;
        }

        // SENT EVEN WITH NOTHING WAITING. A club that ticks the box and
        // then looks at its own page before the announcer has said a word
        // must see the Listen button, or it will reasonably conclude the
        // feature is broken - the relay only draws that button for a club
        // it has been told is open.
        if (!TellTheRelayListeningIsOpen(target, club, wanted: true, token))
        {
            return;
        }

        while (!token.IsCancellationRequested)
        {
            // Nothing to send is not the relay working again (22 Oct 2026,
            // bug hunt): stale lines leave the queue empty while the relay
            // is still down, and this logged "working again" between every
            // failure. Cleared only by a line that is actually sent.
            if (Listening.Take(DateTime.UtcNow) is not { } line)
            {
                return;
            }

            switch (SendOneListenLine(target, club, line, token))
            {
                case ListenSend.Sent:
                    Listening.Done(line);
                    ListenTrouble(null);
                    break;

                case ListenSend.LetGo:
                    // Would fail the same way every pass. Dropped here
                    // WITHOUT being counted as sent - see
                    // ListenAlong.LetGo - and the next line tried, because
                    // one unsendable line is no reason to stop the evening.
                    Listening.LetGo(line);
                    break;

                default:
                    // Left where it is, which IS the retry: it goes next
                    // pass, or is thrown away for being stale, or is
                    // pushed off the front by newer lines. See
                    // ListenAlong.Done.
                    return;
            }
        }
    }

    /// <summary>One request on listening's own client, with the worker's time limit for its size (see SendAsync).</summary>
    private HttpResponseMessage SendListen(HttpRequestMessage request, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeoutFor(request));

        return _listenHttp.SendAsync(request, limit.Token).GetAwaiter().GetResult();
    }

    /// <summary>
    /// The relay answered listening: whatever backoff the two threads were
    /// in is over. The worker's last error is left for its own next send to
    /// clear - it may be about a document, not about reaching the relay.
    /// </summary>
    private void ListenReached()
    {
        lock (_gate)
        {
            _failures = 0;
            _nextAttemptTick = 0;
            _online = true;
            _lastSuccessUtc = DateTime.UtcNow;
        }
    }

    /// <summary>The relay could not be reached, or failed in itself (5xx): both threads back off - see <see cref="Failed"/>.</summary>
    private void ListenUnreachable() => Failed();

    /// <summary>What became of one line on the wire. See <see cref="SendOneListenLine"/>.</summary>
    private enum ListenSend
    {
        /// <summary>The relay has it.</summary>
        Sent,

        /// <summary>It did not land and is worth another go next pass.</summary>
        TryAgain,

        /// <summary>It would fail identically every pass, so it is dropped and not counted as sent.</summary>
        LetGo,
    }

    /// <summary>
    /// One line, to the listen collection.
    ///
    /// <see cref="ListenSend.TryAgain"/> means it did not land and is
    /// worth another go. The cases that are NOT tried again are the ones
    /// that would fail identically every pass - a document larger than the
    /// relay takes, audio that will not package - and those are let go
    /// with the reason recorded rather than left to be retried until they
    /// go stale. See <see cref="ListenAlong.MostBytes"/>, which exists so
    /// that the size case does not arise in the first place.
    /// </summary>
    private ListenSend SendOneListenLine(
        RelayTarget target,
        string club,
        ListenAlong.Spoken line,
        CancellationToken token)
    {
        string body;

        try
        {
            body = ListenAlong.Body(line);
        }
        catch (Exception ex)
        {
            ListenTrouble($"Could not package a line for listeners: {ex.Message}");
            return ListenSend.LetGo;
        }

        if (Encoding.UTF8.GetByteCount(body) > ListenAlong.MostDocumentBytes)
        {
            ListenTrouble("A line was too large to send to listeners, and was passed over.");
            return ListenSend.LetGo;
        }

        try
        {
            var path = $"{target.Root}/api/v1/c/{club}/publish/{ListenAlong.Channel}/{line.Id}?seq={NextSeq()}";

            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = SendListen(request, token);

            if (response.IsSuccessStatusCode)
            {
                ListenReached();
                return ListenSend.Sent;
            }

            var code = (int)response.StatusCode;

            if (code >= 500)
            {
                ListenUnreachable();
            }

            ListenTrouble(code switch
            {
                404 => "The relay does not know about listening along yet - redeploy it (fly deploy --ha=false).",

                // The relay's own gate, and worth seeing: it means this
                // laptop thinks listening is open and the relay does not.
                // Told again next pass rather than argued with.
                403 => "The relay says this club is not letting anybody listen along. "
                       + "It will be told again; if this stays, untick and re-tick the box on the Club Website page.",

                // Bigger than one listen document. Not retried - see the
                // summary - and let go by returning true.
                413 => "A line was longer than the relay accepts, and was passed over.",

                _ => $"Could not send a line to listeners: the relay answered HTTP {code}.",
            });

            if (code is 403)
            {
                // Say it again next pass. The relay is the one holding the
                // line here, and a laptop that keeps posting into a 403
                // is a laptop with a stale idea of the switch. The backoff
                // then keeps that re-telling to twice a minute rather than
                // twice a second.
                _listenOpenTold = false;
            }

            return code is 413 ? ListenSend.LetGo : ListenSend.TryAgain;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return ListenSend.TryAgain;
        }
        catch (Exception ex)
        {
            ListenTrouble($"Could not reach the relay with a line for listeners: {Plain(ex)}");
            ListenUnreachable();
            return ListenSend.TryAgain;
        }
    }

    /// <summary>
    /// Tells the relay whether this club lets people listen along at all.
    ///
    /// Sent once rather than every pass - the relay keeps the flag, and a
    /// request per pass for a value that changes about twice a season would
    /// be most of what this costs. Sent again whenever it changes, which
    /// <see cref="CloseListeningOnTheRelay"/> handles for the off
    /// direction.
    ///
    /// False when it could not be sent, and then nothing is sent either -
    /// the relay would answer 403 anyway.
    /// </summary>
    private bool TellTheRelayListeningIsOpen(RelayTarget target, string club, bool wanted, CancellationToken token)
    {
        if (_listenOpenTold && _listenOpenAsTold == wanted)
        {
            return true;
        }

        // Backing off a flag that has already failed. Not applied to the
        // first attempt of a session, which is why the tick starts at zero.
        if (Environment.TickCount64 < _nextListenOpenTick && target == _listenOpenBackoffFor)
        {
            return false;
        }

        _nextListenOpenTick = Environment.TickCount64 + (long)ListenOpenRetryEvery.TotalMilliseconds;
        _listenOpenBackoffFor = target;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/{ListenAlong.Channel}/open");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { open = wanted }), Encoding.UTF8, "application/json");

            using var response = SendListen(request, token);

            if ((int)response.StatusCode >= 500)
            {
                ListenUnreachable();
            }
            else
            {
                ListenReached();
            }

            if (!wanted && IsGoneForAClose(response))
            {
                // Nothing there to close (17 Oct 2026, Bug Hunt 8, A16-1):
                // the close is armed on every start now, and an old relay
                // must not be asked again every thirty seconds forever.
                _nextListenOpenTick = 0;
                return true;
            }

            if (!response.IsSuccessStatusCode)
            {
                ListenTrouble((int)response.StatusCode == 404
                    ? "The relay does not know about listening along yet - redeploy it (fly deploy --ha=false)."
                    : $"Could not open listening on the relay: it answered HTTP {(int)response.StatusCode}.");

                return false;
            }

            // Told the club it was sent to, which may not be this one any
            // more - see TellTheRelayNotesAreOpen. A Save while this was in
            // flight cleared the flag for the new club; marking it told here
            // would let a close owed to the new club pass as already sent.
            if (target != _target)
            {
                return true;
            }

            _listenOpenAsTold = wanted;
            _listenOpenTold = true;
            _nextListenOpenTick = 0;

            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            ListenTrouble($"Could not reach the relay about listening along: {Plain(ex)}");
            ListenUnreachable();
            return false;
        }
    }

    /// <summary>
    /// Tells the relay nobody may listen any more, which also throws away
    /// the lines it was holding and takes the button off the club's page.
    ///
    /// Called when the box is unticked rather than left to the pass loop,
    /// because the pass loop stops sending the moment it is off - so
    /// without this the relay would go on offering the club's own
    /// announcer to anybody with the address, from a club that had just
    /// said it did not want that. This is the club's voice on a public
    /// page; "quiet" is not the same as "closed".
    ///
    /// THE WORKER SENDS IT, AND KEEPS SENDING IT. This used to send once
    /// on a thread of its own and forget the answer, so an untick during a
    /// Wi-Fi blip left the relay open all night. It also sent nothing when
    /// this run had not opened it yet, when a previous run had. Now it is
    /// marked here and the listen thread's pass (the worker's, before 19 Nov
    /// 2026) tries it - with the open flag's own backoff - until the relay
    /// takes it.
    /// </summary>
    public void CloseListeningOnTheRelay()
    {
        // Let go here as well as on the relay, so a box unticked while
        // three lines were waiting does not send them a moment later.
        Listening.Wanted = false;

        _listenClosePending = true;
        _listenWake.Set();
    }

    /// <summary>
    /// The close, from the listen thread's pass while the box is unticked. Clears
    /// <see cref="_listenClosePending"/> only once the relay has it.
    /// </summary>
    private void SendListenClose(CancellationToken token)
    {
        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            return;
        }

        var club = Uri.EscapeDataString(target.Club);

        // Only while it is still the club it was sent to (10 Nov 2026, bug
        // hunt 16) - see SendNotesClose.
        if (TellTheRelayListeningIsOpen(target, club, wanted: false, token) && target == _target)
        {
            _listenClosePending = false;
        }
    }
}
