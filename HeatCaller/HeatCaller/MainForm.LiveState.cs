namespace HeatCaller;

/// <summary>
/// Threaded mode (AppSettings.TimingOnItsOwnThread): the live state - the
/// document /api/state serves the phones, and the relay's copy of it - built
/// off the UI thread, on a thread-pool timer, so they no longer freeze when
/// this window does. None of this runs inline: there the race tick publishes
/// /api/state and _relayStateTimer sends the relay's frame, on the UI
/// thread, exactly as they always did.
/// </summary>
/// <remarks>
/// WHY (24 Oct 2026, timing layer, P8): with timing on its own thread the
/// race goes on whatever the window is doing, but the document was still
/// built on the window's 500 ms tick - so during a freeze every phone and
/// the relay showed a race that had stopped. A pool timer, not the timing
/// tick: the timing thread must stay free of JSON work (the columns, the
/// serialising, an achievements file read), and a pool thread depends on
/// neither side. It waits on the timing thread for its reads, never on this
/// window.
///
/// LIVE through a freeze: the state, flag, countdown, the standings and
/// their cells, each car's laps and gap ahead, captions and the broadcast
/// moments. STALE through a freeze - as this window last read them on its
/// 500 ms tick (RefreshFrozenLiveInputs): the race in hand, the grid and
/// the drivers still to race, the track map, the championship, the lead
/// change count, on deck, who presents the moments and the web page's
/// columns. Names are from the profiles as last saved.
/// </remarks>
public partial class MainForm
{
    /// <summary>The live state's own timer, threaded mode only. Null until StartLiveStateOffUi, and again once stopped.</summary>
    private System.Threading.Timer? _liveStateTimer;

    /// <summary>1 while a build is running - one at a time, a tick that finds one running is skipped. Interlocked.</summary>
    private int _liveStateBusy;

    /// <summary>Builds so far: the relay's frame goes every second one, once a second as _relayStateTimer sent it. The worker's alone.</summary>
    private int _liveStateTicks;

    /// <summary>Set as the window closes: a tick already queued does nothing.</summary>
    private volatile bool _liveStateStopped;

    /// <summary>PublishStateToRelay's _lastRelayRaceState, threaded: the worker's alone.</summary>
    private RaceState _lastRelayRaceStateOffUi;

    /// <summary>The phones' countdown last sent with the live state, threaded: the worker's alone (setup plan step 8).</summary>
    private string _lastRelayCountdownOffUi = string.Empty;

    /// <summary>The phones' countdown last sent with the live state, inline: the UI thread's (setup plan step 8).</summary>
    private string _lastRelayCountdown = string.Empty;

    /// <summary>
    /// Whether the next race's countdown has changed since it was last sent
    /// (12 Nov 2026, setup plan step 8). The live state goes to the relay only
    /// while a race is on or as one stops, so without this the phones would
    /// never hear that a gap opened, was held or resumed. Safe on any thread.
    /// </summary>
    private bool CountdownChanged(ref string last)
    {
        var key = PhoneCountdown.KeyOf(_raceControlControl?.CountdownForPhones);

        if (key == last)
        {
            return false;
        }

        last = key;
        return true;
    }

    /// <summary>
    /// How often an idle board still goes to the relay (14 Nov 2026, bug hunt
    /// 19 #12). The relay shows the club's stream as live only while the race
    /// state is under two minutes old, so a quiet break between races with no
    /// frame at all tore the drivers' docked video down every race break.
    /// </summary>
    internal const long IdleStateEveryMs = 60_000;

    /// <summary>When PublishStateToRelay last sent a frame (Environment.TickCount64), inline: the UI thread's.</summary>
    private long _lastRelayStateSentMs;

    /// <summary>When PublishStateToRelayOffUi last sent a frame, threaded: the worker's alone.</summary>
    private long _lastRelayStateSentMsOffUi;

    /// <summary>
    /// Whether a live-state frame goes now: every tick while a race is on,
    /// the one that records a change, and once a minute while idle so the
    /// relay knows the laptop is still there.
    /// </summary>
    internal static bool StateFrameDue(bool live, bool changed, long nowMs, long lastSentMs) =>
        live || changed || nowMs - lastSentMs >= IdleStateEveryMs;

    /// <summary>The relay settings PublishStateToRelay reads, as last read on the UI thread.</summary>
    private volatile bool _frozenRelayEnabled;

    /// <summary>See _frozenRelayEnabled.</summary>
    private volatile bool _frozenRelayAbbreviate;

    /// <summary>The web server's window-owned callbacks, as frozen feeds (LiveFeed): each one's Refresh and Forget. Threaded mode only; UI thread only.</summary>
    private List<(Action Refresh, Action Forget)>? _frozenLiveFeeds;

    /// <summary>
    /// One of the web server's callbacks that only this window may answer
    /// (the race in hand, the call-ups, the championship, the track map,
    /// the lead changes): inline, the callback itself, as it always was;
    /// threaded, a FrozenFeed of it - answered on the UI tick by
    /// RefreshFrozenLiveInputs, and read by the build off the UI. Before its
    /// first answer it throws, and the build leaves that part out, as every
    /// one of those reads is already guarded to. UI thread, at wiring.
    /// </summary>
    private Func<T> LiveFeed<T>(Func<T> source)
    {
        if (!_race.IsThreaded)
        {
            return source;
        }

        var feed = new FrozenFeed<T>(source);
        (_frozenLiveFeeds ??= new List<(Action Refresh, Action Forget)>()).Add((feed.Refresh, feed.Forget));
        return feed.Get;
    }

    /// <summary>
    /// Threaded mode, on the UI thread from the 500 ms race tick (in place of
    /// publishing the state there): reads the frozen feeds again, captures
    /// the web server's window side (WebServerService.CaptureUiSide) and the
    /// relay's settings, for the next builds off the UI.
    /// </summary>
    /// <remarks>
    /// The feeds only while the web server is serving: stopped, nobody reads
    /// them, and inline they are not asked then either. Forgotten instead,
    /// so a server started later never serves a track map from long ago -
    /// its first document leaves them out until the next tick.
    /// </remarks>
    private void RefreshFrozenLiveInputs()
    {
        var serving = _webServer.IsRunning;

        if (_frozenLiveFeeds is { } feeds)
        {
            foreach (var (refresh, forget) in feeds)
            {
                if (serving)
                {
                    refresh();
                }
                else
                {
                    forget();
                }
            }
        }

        try
        {
            _webServer.CaptureUiSide();
        }
        catch
        {
            // The last capture stands.
        }

        _frozenRelayEnabled = _settings.RelayEnabled;
        _frozenRelayAbbreviate = _settings.RelayAbbreviateNames;
    }

    /// <summary>
    /// Starts building the live state off the UI. Threaded mode only; once.
    /// UI thread, where the race tick starts. Everything it reads is
    /// captured first, so the first build off the UI has it to hand.
    /// </summary>
    private void StartLiveStateOffUi()
    {
        if (!_race.IsThreaded || _liveStateTimer is not null)
        {
            return;
        }

        _lastRelayRaceStateOffUi = _lastRelayRaceState;
        RefreshFrozenLiveInputs();
        _liveStateTimer = new System.Threading.Timer(PublishLiveStateOffUi, null, 500, 500);
    }

    /// <summary>
    /// Stops the live state's timer and waits (up to two seconds) for a build
    /// under way to finish - as the window closes, before the web server, the
    /// relay and the timing thread go. UI thread. The UI may wait on the
    /// worker: the worker never waits on the UI.
    /// </summary>
    private void StopLiveStateOffUi()
    {
        _liveStateStopped = true;

        var timer = _liveStateTimer;

        if (timer is null)
        {
            return;
        }

        _liveStateTimer = null;

        // Not disposed after a wait that ran out: the timer still signals it
        // when the build ends, and a disposed handle would throw there.
        var done = new ManualResetEvent(false);

        if (timer.Dispose(done) && done.WaitOne(TimeSpan.FromSeconds(2)))
        {
            done.Dispose();
        }
    }

    /// <summary>
    /// The live state's tick, on a thread-pool thread, every 500 ms: /api/state
    /// (WebServerService.PublishState, which does nothing while the server is
    /// stopped), then the relay's frame every second tick.
    /// </summary>
    private void PublishLiveStateOffUi(object? state)
    {
        if (_liveStateStopped || Interlocked.Exchange(ref _liveStateBusy, 1) == 1)
        {
            return;
        }

        try
        {
            _webServer.PublishState();

            if ((++_liveStateTicks & 1) == 0)
            {
                PublishStateToRelayOffUi();
            }
        }
        catch
        {
            // PublishState reports its own failures; a timer callback that
            // threw would take the app down.
        }
        finally
        {
            Volatile.Write(ref _liveStateBusy, 0);
        }
    }

    /// <summary>
    /// PublishStateToRelay, off the UI: the same frame on the same rule (once
    /// a second while a race is on, and the frame that records it stopping),
    /// with the relay settings as last read on the UI thread. A failure goes
    /// to the console, which posts its line to the window.
    /// </summary>
    private void PublishStateToRelayOffUi()
    {
        if (!_frozenRelayEnabled)
        {
            return;
        }

        var state = _race.State;
        var live = state is RaceState.Staging or RaceState.Countdown or RaceState.Racing;
        var changed = state != _lastRelayRaceStateOffUi;

        _lastRelayRaceStateOffUi = state;
        changed |= CountdownChanged(ref _lastRelayCountdownOffUi);

        var now = Environment.TickCount64;

        if (!StateFrameDue(live, changed, now, _lastRelayStateSentMsOffUi))
        {
            return;
        }

        _lastRelayStateSentMsOffUi = now;

        try
        {
            _relayPublisher.PublishSnapshot(
                RelayChannel.State,
                RelayStateJson(_frozenRelayAbbreviate));
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build the live state: {ex.Message}");
        }
    }

    /// <summary>
    /// The relay's live frame, with how old each car's newest lap is put on
    /// its row (13 Nov 2026, bug hunt 17, #173 - see RelayLapAge). Either
    /// thread: the race is read in one trip, from one moment, as the frame's
    /// own build reads it. A failure to read it sends the frame without -
    /// the relay then goes by when the frame arrived, as it always did.
    /// </summary>
    private string RelayStateJson(bool abbreviateNames)
    {
        var json = _webServer.BuildStateJson(abbreviateNames, forRelay: true);

        try
        {
            var view = _race.ReadLiveView();

            return RelayLapAge.Stamp(json, view.Ordered, view.DeviceNowMs, transponder => _webServer.RelayRowTag(transponder));
        }
        catch
        {
            return json;
        }
    }
}
