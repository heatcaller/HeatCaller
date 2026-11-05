using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// Drivers' own requests, collected from the relay (22 Oct 2026, driver
/// experience): a change to their details, the battery pack they're
/// running, or a query about a result, sent from their own night page.
///
/// THE NOTES SHAPE: COLLECT, FILE, THEN CONFIRM (RelayPublisher.Notes). The
/// relay holds each request until this laptop says it has it, so a poll
/// whose answer never arrived loses nothing - it comes again, and the store
/// recognises it by its id. Nothing is confirmed until it is on disk
/// (DriverRequestStore.FileAll).
///
/// NOTHING COLLECTED HERE CHANGES ANYTHING BY ITSELF. Details and queries
/// wait for the desk (DriverRequestsForm), which approves, notes or declines
/// them; packs, cars, photos and setups are applied on the roster tick by
/// DriverRequestDesk.FileWaitingWrites (5 Nov 2026). Either way the phone
/// sees the answer in its driverlive entry.
///
/// THE SWITCH IS SENT, NOT INFERRED, as every other one here: open while
/// Driver requests is ticked, and a close - which also throws away every
/// request the relay still holds - owed until the relay takes it
/// (_asksClosePending, armed on every start while it is off).
/// </summary>
public sealed partial class RelayPublisher
{
    /// <summary>
    /// How often the inbox is read. Fifteen seconds: quicker than shout-outs,
    /// because a driver standing at the desk is waiting to see their change
    /// appear; slower than check-in, because nothing is urgent about it.
    /// </summary>
    private static readonly TimeSpan AsksPollEvery = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How often the open flag is told again even when nothing changed. The
    /// relay keeps it, but a relay whose data was lost (a new machine, a
    /// restored volume) would otherwise answer an empty inbox all night to a
    /// laptop sure it had said so. One small request every ten minutes.
    /// </summary>
    private static readonly TimeSpan AsksOpenRetellEvery = TimeSpan.FromMinutes(10);

    /// <summary>The relay takes at most this many ids per driver/collected.</summary>
    private const int AsksConfirmChunk = 200;

    /// <summary>When the inbox (or the close) is next due - its own tick.</summary>
    private long _nextAsksPollTick;

    /// <summary>When the open flag is told again regardless - see AsksOpenRetellEvery.</summary>
    private long _asksOpenRetellTick;

    /// <summary>The relay, club and token the switch was last told to - worker thread only. A different one is told again from scratch.</summary>
    private RelayTarget? _asksOpenToldTo;

    /// <summary>What it was told: open or closed.</summary>
    private bool _asksOpenAsTold;

    /// <summary>How many times the relay has taken an OPEN - see DriverAsksOpenedCount.</summary>
    private int _asksOpenedCount;

    /// <summary>What went wrong collecting drivers' requests, for the desk to show. Null while it is working.</summary>
    private volatile string? _asksProblem;

    /// <summary>The problem last said out loud, so it is said once rather than every pass.</summary>
    private volatile string? _asksProblemAsSaid;

    /// <summary>
    /// Where a collected request is filed. Set by MainForm.DriverRequests;
    /// null until then, and then nothing is collected - an unfiled request
    /// would be confirmed to the relay and lost.
    /// </summary>
    /// <remarks>Internal because DriverRequestStore is (CS0053 - see TonightsNotes).</remarks>
    internal Func<DriverRequestStore?>? TonightsDriverRequests { get; set; }

    /// <summary>What went wrong collecting drivers' requests from the relay, or null.</summary>
    public string? DriverRequestsProblem => _asksProblem;

    /// <summary>
    /// Goes up each time the relay takes an OPEN of drivers' requests. The
    /// relay blanks the driverme document whenever requests close, so
    /// whatever sends driverme "only when changed" must forget its last copy
    /// when this moves, or the relay is left blank after a reopen.
    /// </summary>
    public int DriverAsksOpenedCount => Volatile.Read(ref _asksOpenedCount);

    /// <summary>Records what went wrong and says it once, on a change - the VoteTrouble pattern.</summary>
    private void AsksTrouble(string? problem)
    {
        _asksProblem = problem;

        if (string.Equals(_asksProblemAsSaid, problem, StringComparison.Ordinal))
        {
            return;
        }

        _asksProblemAsSaid = problem;

        if (problem is { Length: > 0 })
        {
            Log?.Invoke($"[relay] Driver requests from the club's page: {problem}");
        }
        else
        {
            Log?.Invoke("[relay] Driver requests from the club's page is working again.");
        }
    }

    /// <summary>Clears the problem without saying anything, for the club switching requests off.</summary>
    private void ForgetAsksTrouble()
    {
        _asksProblem = null;
        _asksProblemAsSaid = null;
    }

    /// <summary>
    /// Once per pass, from the worker loop after sign-up (RelayPublisher.cs).
    /// On its own fifteen-second tick: collecting while Driver requests is
    /// ticked, the close while one is owed, and nothing at all otherwise.
    /// </summary>
    partial void CollectDriverAsksPass(CancellationToken token)
    {
        if (AsksWanted(_settings))
        {
            if (Environment.TickCount64 >= _nextAsksPollTick)
            {
                _nextAsksPollTick = Environment.TickCount64 + (long)AsksPollEvery.TotalMilliseconds;
                CollectDriverAsks(token);

                // Their car photos on the same tick (5 Nov 2026) - see
                // RelayPublisher.Photos.
                CollectDriverPhotos(token);
            }

            return;
        }

        // Switched off: the relay told, until it has taken it. On the same
        // tick, so a relay that is down is asked four times a minute rather
        // than twice a second.
        if (_asksClosePending)
        {
            if (Environment.TickCount64 >= _nextAsksPollTick)
            {
                _nextAsksPollTick = Environment.TickCount64 + (long)AsksPollEvery.TotalMilliseconds;
                SendAsksClose(token);
            }
        }
        else
        {
            ForgetAsksTrouble();
        }
    }

    /// <summary>
    /// Whether the relay should be taking drivers' requests: Driver requests
    /// ticked AND drivers' nights, which they are made from (design 1.3).
    /// Unticking nights closes requests as well (22 Oct 2026, driver
    /// experience): the relay already refuses a new one while nights are
    /// closed, but would have gone on holding those already sent, and a
    /// laptop that went on telling it "open" would have kept that so. The
    /// cost is a request sent in the last fifteen seconds before the untick,
    /// not yet collected, which the close throws away with the rest.
    /// </summary>
    private static bool AsksWanted(AppSettings settings) =>
        settings.RelayDriverRequests && settings.PublishDriverNightsToTheRelay;

    /// <summary>Tells the relay requests are open, reads the inbox, files what is there, then confirms what was filed.</summary>
    private void CollectDriverAsks(CancellationToken token)
    {
        if (TonightsDriverRequests?.Invoke() is not { } store)
        {
            return;
        }

        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            AsksTrouble(UnsafeAddressMessage);
            return;
        }

        var club = Uri.EscapeDataString(target.Club);

        if (!TellTheRelayAsksAreOpen(target, club, wanted: true, token))
        {
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{club}/driver/inbox");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = Send(request, token);

            if (!response.IsSuccessStatusCode)
            {
                AsksTrouble((int)response.StatusCode == 404
                    ? "The relay answered \"not found\" for driver requests. Check the club id on the Club Website page "
                      + "matches the relay's; if it does, the relay predates them - redeploy it (fly deploy --ha=false)."
                    : $"Could not collect driver requests: the relay answered HTTP {(int)response.StatusCode}.");

                return;
            }

            var text = response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult();
            var asks = DriverRequestStore.ParseInbox(text, DateTime.UtcNow, out var unusable);

            if (asks.Count == 0 && unusable.Count == 0)
            {
                AsksTrouble(null);
                return;
            }

            // FILED FIRST, confirmed after - and only what is on disk.
            var confirm = store.FileAll(asks, DateTime.UtcNow, out _).Concat(unusable).Distinct(StringComparer.Ordinal).ToList();

            if (confirm.Count > 0 && !ConfirmDriverAsksCollected(target, club, confirm, token))
            {
                // Left on the relay, so they come again; already filed, so
                // the duplicate is recognised by its id.
                return;
            }

            if (confirm.Count < asks.Count + unusable.Count)
            {
                AsksTrouble(store.LoadedUnreadable
                    ? "driver-requests.json couldn't be read at start-up, so requests are being left on the relay until HeatCaller is restarted and can read it."
                    : "Some driver requests couldn't be filed - driver-requests.json couldn't be written, or 500 are waiting - "
                      + "so they are being left on the relay until they can be.");
                return;
            }

            AsksTrouble(null);
        }
        catch (OperationCanceledException)
        {
            // Shutting down, or the pass was cancelled. Nothing to report.
        }
        catch (Exception ex)
        {
            AsksTrouble($"Could not collect driver requests: {ex.Message}");
        }
    }

    /// <summary>
    /// Tells the relay whether this club takes drivers' requests. Once per
    /// relay, club and token, and again when the setting changes - plus the
    /// ten-minute retell. False when it couldn't be told, and then nothing is
    /// collected.
    /// </summary>
    private bool TellTheRelayAsksAreOpen(RelayTarget target, string club, bool wanted, CancellationToken token)
    {
        if (_asksOpenToldTo == target && _asksOpenAsTold == wanted
            && (!wanted || Environment.TickCount64 < _asksOpenRetellTick))
        {
            return true;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/driver/open");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { open = wanted }), Encoding.UTF8, "application/json");

            using var response = Send(request, token);

            if (!wanted && IsGoneForAClose(response))
            {
                // Nothing there to close - an old relay, or no such club.
                _asksOpenToldTo = target;
                _asksOpenAsTold = false;
                return true;
            }

            if (!response.IsSuccessStatusCode)
            {
                AsksTrouble((int)response.StatusCode == 404
                    ? "The relay does not know about driver requests yet - redeploy it (fly deploy --ha=false)."
                    : $"Could not {(wanted ? "open" : "close")} driver requests on the relay: it answered HTTP {(int)response.StatusCode}.");

                return false;
            }

            var reopened = wanted && !(_asksOpenToldTo == target && _asksOpenAsTold);

            _asksOpenToldTo = target;
            _asksOpenAsTold = wanted;
            _asksOpenRetellTick = Environment.TickCount64 + (long)AsksOpenRetellEvery.TotalMilliseconds;

            if (reopened)
            {
                Interlocked.Increment(ref _asksOpenedCount);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AsksTrouble($"Could not reach the relay about driver requests: {ex.Message}");
            return false;
        }
    }

    /// <summary>Says the laptop has them, so the relay can let them go. False when it couldn't, and then they come again.</summary>
    private bool ConfirmDriverAsksCollected(RelayTarget target, string club, IReadOnlyList<string> ids, CancellationToken token)
    {
        try
        {
            for (var start = 0; start < ids.Count; start += AsksConfirmChunk)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/driver/collected");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new { ids = ids.Skip(start).Take(AsksConfirmChunk).ToList() }), Encoding.UTF8, "application/json");

                using var response = Send(request, token);

                if (!response.IsSuccessStatusCode)
                {
                    AsksTrouble($"Collected driver requests but could not clear them: the relay answered HTTP {(int)response.StatusCode}.");
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            AsksTrouble($"Collected driver requests but could not clear them: {ex.Message}");
            return false;
        }
    }

    /// <summary>The close, from the worker's pass while the switch is off. Clears _asksClosePending only once the relay has it (a 404 counts).</summary>
    private void SendAsksClose(CancellationToken token)
    {
        var target = _target;

        if (!IsSafeForToken(target.Root))
        {
            return;
        }

        if (TellTheRelayAsksAreOpen(target, Uri.EscapeDataString(target.Club), wanted: false, token))
        {
            _asksClosePending = false;
        }
    }
}
