using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// A driver's own night, on its way to the relay.
///
/// THE LAPTOP'S HALF (13 Oct 2026). The relay side shipped first: a
/// private `nights` collection, a switch the club holds, a POST that
/// answers one document to somebody holding that driver's badge id and the
/// same bare 404 to everybody else, and - since the gate was widened - a
/// publish route that refuses a night to a club that has not said yes.
/// This is the half that puts the documents there. See
/// <see cref="DriverNight"/> for what one is.
///
/// THROUGH THE RESULTS QUEUE, unlike listening along
///
/// A night is two to four kilobytes, published once per driver per race
/// filed. Listening along goes round that queue because an evening of
/// announcing would have pushed a session's results off the front of it -
/// hundreds of lines, each worth nothing a minute later. Nights are the
/// opposite shape: a handful per race, each one the finished article, and
/// each one worth retrying exactly the way a result is. So they go in the
/// queue, keyed by badge id, and a night sent twice simply replaces itself.
///
/// WHY THE SWITCH IS SENT AND NOT INFERRED
///
/// The same reason shout-outs and listening both send theirs: the relay
/// cannot tell a club that has switched this off from a club that has not
/// raced this week. Left to be inferred, it would go on serving a driver's
/// evening from a club that had withdrawn it. So it is told - on every
/// start, and again whenever it changes - and switching it OFF is what
/// makes the relay throw away every night it holds.
/// </summary>
public sealed partial class RelayPublisher
{
    /// <summary>
    /// How long before the open flag is tried again after it failed. The
    /// same thirty seconds listening uses, and for the same reason: there
    /// is no poll tick here either - the flag is sent on the pass a night
    /// is queued on - so a relay answering 404 because it has not been
    /// redeployed would otherwise be asked twice a second all evening.
    /// </summary>
    private static readonly TimeSpan NightsOpenRetryEvery = TimeSpan.FromSeconds(30);

    /// <summary>When the open flag may be tried again, after a failure.</summary>
    private long _nextNightsOpenTick;

    /// <summary>
    /// The relay, club and token the backoff above was earned against.
    /// WHY (20 Oct 2026, Bug Hunt 8, #85): the thirty seconds outlived the
    /// settings it was about. A club that typed its id wrong got a 404,
    /// fixed it and pressed Save - and the new club was not asked for up to
    /// thirty seconds, while every driver's night for tonight was queued,
    /// sent to a club with no nights flag, refused with a 403 and dropped
    /// for good. Now a backoff only holds back the target that failed; a
    /// different relay, club or token is asked on the very next pass.
    /// </summary>
    private RelayTarget? _nightsOpenBackoffFor;

    /// <summary>What went wrong offering drivers their night, for the page to show. Null while it is working.</summary>
    private volatile string? _nightsProblem;

    /// <summary>Whether the relay has been told - so the flag goes once rather than every pass.</summary>
    private volatile bool _nightsOpenTold;

    /// <summary>The state last told to the relay, so a club switching it off is actually sent the change.</summary>
    private volatile bool _nightsOpenAsTold;

    /// <summary>Whether the box was unticked and the relay has not yet taken the close.</summary>
    private volatile bool _nightsClosePending;

    /// <summary>What went wrong offering drivers their own night, or null.</summary>
    public string? DriverNightsProblem => _nightsProblem;

    /// <summary>The problem last said out loud, so it is said once rather than every pass.</summary>
    private volatile string? _nightsProblemAsSaid;

    /// <summary>
    /// Records what went wrong and says it once, on a change - the pattern
    /// VoteTrouble set (17 Oct 2026, Bug Hunt 8, A17-2). The problem was
    /// recorded in a field nothing read, so a route that silently stopped
    /// working on a race night left no trace in the console or the log.
    /// </summary>
    private void NightsTrouble(string? problem)
    {
        _nightsProblem = problem;

        if (string.Equals(_nightsProblemAsSaid, problem, StringComparison.Ordinal))
        {
            return;
        }

        _nightsProblemAsSaid = problem;

        if (problem is { Length: > 0 })
        {
            Log?.Invoke($"[relay] Drivers' own nights on the club's page: {problem}");
        }
        else
        {
            Log?.Invoke("[relay] Drivers' own nights on the club's page is working again.");
        }
    }

    /// <summary>
    /// Clears the problem without saying anything, for the club switching
    /// the route off - not a fault, so no "working again" on its way out.
    /// </summary>
    private void ForgetNightsTrouble()
    {
        _nightsProblem = null;
        _nightsProblemAsSaid = null;
    }

    /// <summary>
    /// Queues one driver's night, keyed by their badge id.
    ///
    /// THE ID IS CHECKED HERE, and that is not belt and braces. The relay
    /// validates a badge id on the way OUT - it is what tells a real id
    /// from a guess - and stores whatever it is given on the way in. So a
    /// document under an id of the wrong shape would sit in the club's
    /// collection, count against its budget, roll somebody else's night off
    /// the end when it filled, and never be readable by anybody. Refused
    /// here instead, where it costs nothing.
    /// </summary>
    /// <returns>False when there was nothing to queue or the id was not one.</returns>
    public bool PublishDriverNight(string? driverId, string? json)
    {
        if (!DriverNight.IsADriverId(driverId) || string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        return Enqueue(DriverNight.Channel, driverId, json);
    }

    /// <summary>
    /// Tells the relay this club offers drivers their own night, on the
    /// worker's own pass.
    ///
    /// SENT EVEN WITH NOTHING QUEUED, because the relay REFUSES a night
    /// published to a club it has not been told about - and a 403 per
    /// driver per race, discovered after a night of racing, is a worse way
    /// to find that out than one request per start.
    /// </summary>
    private void SendNightsOpen(CancellationToken token)
    {
        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            NightsTrouble(UnsafeAddressMessage);
            return;
        }

        if (TellTheRelayNightsAreOpen(target, Uri.EscapeDataString(target.Club), wanted: true, token))
        {
            NightsTrouble(null);
        }
    }

    /// <summary>
    /// Tells the relay whether this club offers drivers their own night at
    /// all.
    ///
    /// Sent once rather than every pass - the relay keeps the flag, and a
    /// request per pass for a value that changes about twice a season would
    /// be most of what this costs.
    /// </summary>
    private bool TellTheRelayNightsAreOpen(RelayTarget target, string club, bool wanted, CancellationToken token)
    {
        if (_nightsOpenTold && _nightsOpenAsTold == wanted)
        {
            return true;
        }

        // Backing off a flag that has already failed. Not applied to the
        // first attempt of a session, which is why the tick starts at zero.
        if (Environment.TickCount64 < _nextNightsOpenTick && target == _nightsOpenBackoffFor)
        {
            return false;
        }

        _nextNightsOpenTick = Environment.TickCount64 + (long)NightsOpenRetryEvery.TotalMilliseconds;
        _nightsOpenBackoffFor = target;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/{DriverNight.Channel}/open");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { open = wanted }), Encoding.UTF8, "application/json");

            using var response = Send(request, token);

            if (!wanted && IsGoneForAClose(response))
            {
                // Nothing there to close (17 Oct 2026, Bug Hunt 8, A16-1).
                _nextNightsOpenTick = 0;
                return true;
            }

            if (!response.IsSuccessStatusCode)
            {
                NightsTrouble((int)response.StatusCode == 404
                    ? "The relay does not know about drivers' own nights yet - redeploy it (fly deploy --ha=false)."
                    : $"Could not offer drivers their own night on the relay: it answered HTTP {(int)response.StatusCode}.");

                return false;
            }

            // Told the club it was sent to, which may not be this one any
            // more (10 Nov 2026, bug hunt 16 - the guard bug hunt 12 #25
            // gave shout-outs): a Save while this was in flight cleared the
            // flag for the new club, and marking it told here put it back,
            // so the new club refused every driver's night.
            if (target != _target)
            {
                _nextNightsOpenTick = 0;
                return true;
            }

            _nightsOpenAsTold = wanted;
            _nightsOpenTold = true;
            _nextNightsOpenTick = 0;

            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            NightsTrouble($"Could not reach the relay about drivers' own nights: {Plain(ex)}");
            return false;
        }
    }

    /// <summary>
    /// Tells the relay nobody may read a night any more, which also throws
    /// away every night it is holding.
    ///
    /// Called when the box is unticked rather than left to the pass loop,
    /// because the pass loop stops publishing the moment it is off - so
    /// without this the relay would go on handing out the evenings of a
    /// club that had just said it did not want that. These are named
    /// people's results; "we stopped sending" is not the same as "take them
    /// down".
    ///
    /// MARKED HERE AND SENT BY THE WORKER, until the relay takes it - the
    /// lesson listening along learned the hard way, where a close sent once
    /// on a thread of its own left the relay open all night when the untick
    /// landed during a Wi-Fi blip.
    /// </summary>
    public void CloseDriverNightsOnTheRelay()
    {
        _nightsClosePending = true;
        _wake.Set();
    }

    /// <summary>
    /// The close, from the worker's pass while the box is unticked. Clears
    /// <see cref="_nightsClosePending"/> only once the relay has it.
    /// </summary>
    private void SendNightsClose(CancellationToken token)
    {
        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            return;
        }

        // Only while it is still the club it was sent to (10 Nov 2026, bug
        // hunt 16) - see SendNotesClose.
        if (TellTheRelayNightsAreOpen(target, Uri.EscapeDataString(target.Club), wanted: false, token) && target == _target)
        {
            _nightsClosePending = false;
        }
    }
}
