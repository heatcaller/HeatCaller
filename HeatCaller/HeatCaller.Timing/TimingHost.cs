using System.Runtime.ExceptionServices;

namespace HeatCaller;

/// <summary>
/// The timing thread: one dedicated thread that runs everything given to it,
/// one item at a time, in the order it was given - and, when asked, the race
/// clock's own tick. With "Run timing on its own thread" on, the race engine
/// lives here and nowhere else (see <see cref="RaceTiming"/>).
/// </summary>
/// <remarks>
/// <para>
/// WHY (23 Oct 2026, timing layer, P4): a frozen window, a stuck sound card or
/// a closed Race Control page must never hold up a lap, the time limit or the
/// checkered flag. Today all of that runs on the UI thread, so it waits on
/// whatever the window is doing. Here it runs on a thread that has nothing
/// else to do.
/// </para>
/// <para>
/// THE ONE RULE: this thread never waits on the UI thread. It only ever
/// posts to it. The UI (or any other thread) may wait on this one - that is
/// what <see cref="Invoke{T}"/> is - and because the waiting only ever runs
/// one way, the two can never deadlock.
/// </para>
/// <para>
/// Its own SynchronizationContext is installed on the thread, and posts back
/// onto the same queue, so an <c>await</c> inside the engine
/// (GreenFlagAfterAudioAsync waiting for the countdown audio) carries on
/// here when it resumes, never on a thread-pool thread.
/// </para>
/// <para>
/// A failing item is logged and the thread carries on; an <see cref="Invoke{T}"/>
/// caller gets the exception back, exactly as if it had made the call itself.
/// No WinForms in here: it moves to the timing class library (P6).
/// </para>
/// </remarks>
public sealed class TimingHost : IDisposable
{
    /// <summary>How often the race clock ticks here - five times as often as MainForm's 500 ms timer did.</summary>
    public static readonly TimeSpan DefaultTickInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How long Dispose waits for the queue to drain and the thread to stop. It runs on the UI thread at close.</summary>
    public static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(2);

    private readonly object _gate = new();
    private readonly Queue<Action> _queue = new();
    private readonly Thread _thread;
    private readonly int _threadId;
    private readonly Action<string>? _log;

    /// <summary>Serializes callers once the thread has stopped - see RunAfterStop.</summary>
    private readonly object _afterStopGate = new();

    // All under _gate.
    private Action? _tick;
    private long _tickEveryMs;
    private long _nextTickAtMs;
    private bool _stopping;
    private bool _droppedReported;

    private volatile bool _exited;
    private int _stuckReported;

    /// <summary>Starts the thread. It waits for work straight away.</summary>
    /// <param name="log">Where a failing item, a dropped post or a slow stop is reported. Called on whichever thread it happened on; must not touch the UI.</param>
    /// <param name="name">The thread's name, as a debugger and the session log show it.</param>
    public TimingHost(Action<string>? log = null, string name = "HeatCaller timing")
    {
        _log = log;
        Context = new TimingSynchronizationContext(this);

        _thread = new Thread(Run)
        {
            // Background, so a stuck item can never keep the app's process
            // alive after the window has gone.
            IsBackground = true,
            Name = name,
        };

        try
        {
            // Above the UI thread, so a busy window cannot starve a lap.
            _thread.Priority = ThreadPriority.AboveNormal;
        }
        catch
        {
            // Not allowed here (some hosts refuse it) - normal priority will do.
        }

        _threadId = _thread.ManagedThreadId;
        _thread.Start();
    }

    /// <summary>The timing thread's own context: a post to it is a <see cref="Post"/>, a send an <see cref="Invoke"/>.</summary>
    public SynchronizationContext Context { get; }

    /// <summary>True on the timing thread itself.</summary>
    public bool IsTimingThread => Environment.CurrentManagedThreadId == _threadId;

    /// <summary>True once the thread has finished - after <see cref="Dispose"/> has drained the queue.</summary>
    public bool HasStopped => _exited;

    /// <summary>
    /// Ticks <paramref name="tick"/> on the timing thread every
    /// <paramref name="every"/> (DefaultTickInterval when left out), on its
    /// own clock, between work items. A tick that falls due while an item is
    /// running waits for it; after a long item the next tick comes at once and
    /// then on the beat again, never a burst of the ones missed. Calling it
    /// again replaces the tick; a null tick stops it.
    /// </summary>
    public void StartTicking(Action? tick, TimeSpan? every = null)
    {
        var everyMs = Math.Max(1, (long)(every ?? DefaultTickInterval).TotalMilliseconds);

        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            _tick = tick;
            _tickEveryMs = everyMs;
            _nextTickAtMs = TimingClock.NowMs + everyMs;
            Monitor.PulseAll(_gate);
        }
    }

    /// <summary>
    /// Queues <paramref name="work"/> to run on the timing thread and returns
    /// at once. False when it was not queued: the host is stopping (only the
    /// timing thread itself can still add work then, so a chain it started can
    /// finish draining).
    /// </summary>
    public bool Post(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);

        lock (_gate)
        {
            if (_stopping && !IsTimingThread)
            {
                if (!_droppedReported)
                {
                    _droppedReported = true;
                    Report("[timing] Work handed to the timing thread after it was told to stop was not run (reported once).");
                }

                return false;
            }

            _queue.Enqueue(work);
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the timing thread and waits for it.
    /// On the timing thread itself it simply runs it, there and then - which
    /// is what lets engine code call back into the race (EditStanding's change).
    /// An exception it throws comes back out of here, with its own stack.
    /// </summary>
    /// <remarks>Never call this while holding anything the timing thread could need.</remarks>
    public T Invoke<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (IsTimingThread)
        {
            return work();
        }

        if (_exited)
        {
            return RunAfterStop(work);
        }

        var call = new Call<T>(work);

        if (!Post(call.Run))
        {
            // Stopping: what is queued drains first, then this runs here.
            return RunAfterStop(work);
        }

        // WHY (23 Oct 2026, timing layer, review): not disposed. The wait can
        // end while the timing thread is still inside Set, and a
        // ManualResetEventSlim must not be disposed under another member;
        // with no wait handle ever asked for, it holds nothing to free.
        call.Done.Wait();
        call.Error?.Throw();
        return call.Result!;
    }

    /// <summary>The same, for work that answers nothing.</summary>
    public void Invoke(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        Invoke<bool>(() =>
        {
            work();
            return true;
        });
    }

    /// <summary>
    /// Stops: no more ticks, everything already queued runs, then the thread
    /// ends - waited for up to <see cref="DisposeWait"/>. From then on an
    /// Invoke runs on its caller's thread (one caller at a time), so a window
    /// tearing down can still read the race; a Post is not run.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            _tick = null;
            Monitor.PulseAll(_gate);
        }

        if (IsTimingThread)
        {
            // It ends itself once this item returns and the queue is empty.
            return;
        }

        if (!_thread.Join(DisposeWait))
        {
            // Known stuck now: a late Invoke does not wait for it again (see RunAfterStop).
            Interlocked.Exchange(ref _stuckReported, 1);
            Report($"[timing] The timing thread did not stop within {DisposeWait.TotalSeconds:0}s - carrying on closing without it.");
        }
    }

    private void Run()
    {
        SynchronizationContext.SetSynchronizationContext(Context);

        try
        {
            // WHY (24 Oct 2026, timing bug hunt): a tick that itself takes a
            // whole beat or more left the next one already due, and a due tick
            // always went first - so queued passes and every Invoke waited for
            // ever. After a tick, one queued item now runs before the next tick.
            var lastWasTick = false;

            while (true)
            {
                Action? work = null;
                var isTick = false;

                lock (_gate)
                {
                    while (true)
                    {
                        if (_tick is { } tick)
                        {
                            var now = TimingClock.NowMs;
                            var wait = _nextTickAtMs - now;

                            // The tick first when it is due, so a steady
                            // stream of passes can never hold the clock up.
                            if (wait <= 0 && !(lastWasTick && _queue.Count > 0))
                            {
                                _nextTickAtMs += _tickEveryMs;

                                if (_nextTickAtMs <= now)
                                {
                                    _nextTickAtMs = now + _tickEveryMs;
                                }

                                work = tick;
                                isTick = true;
                                break;
                            }

                            if (_queue.Count > 0)
                            {
                                work = _queue.Dequeue();
                                break;
                            }

                            Monitor.Wait(_gate, (int)Math.Min(wait, int.MaxValue));
                            continue;
                        }

                        if (_queue.Count > 0)
                        {
                            work = _queue.Dequeue();
                            break;
                        }

                        if (_stopping)
                        {
                            return;
                        }

                        Monitor.Wait(_gate);
                    }
                }

                lastWasTick = isTick;

                try
                {
                    work();
                }
                catch (Exception ex)
                {
                    Report($"[timing] {(isTick ? "The race clock's tick" : "A timing work item")} failed ({ex.GetType().Name}: {ex.Message}) - timing carries on.");
                }
            }
        }
        finally
        {
            _exited = true;
        }
    }

    /// <summary>
    /// An Invoke once the thread has stopped (or is stopping): run on the
    /// caller, one at a time. Waits for the thread to finish first; if it is
    /// stuck, it is said once and the call runs anyway - the app is closing.
    /// </summary>
    private T RunAfterStop<T>(Func<T> work)
    {
        // WHY (23 Oct 2026, timing layer, review): once found stuck, not
        // waited for again - every read in the window's teardown would
        // otherwise cost another two seconds.
        if (!_exited && Volatile.Read(ref _stuckReported) == 0 && !_thread.Join(DisposeWait)
            && Interlocked.Exchange(ref _stuckReported, 1) == 0)
        {
            Report("[timing] The timing thread is stuck; a late call to the race was run on its caller's thread instead.");
        }

        lock (_afterStopGate)
        {
            return work();
        }
    }

    private void Report(string message)
    {
        try
        {
            _log?.Invoke(message);
        }
        catch
        {
            // Reporting must never be what stops timing.
        }
    }

    /// <summary>One Invoke: the work, and what it answered or threw.</summary>
    private sealed class Call<T>
    {
        private readonly Func<T> _work;

        public Call(Func<T> work) => _work = work;

        public ManualResetEventSlim Done { get; } = new(false);

        public T? Result { get; private set; }

        public ExceptionDispatchInfo? Error { get; private set; }

        public void Run()
        {
            try
            {
                Result = _work();
            }
            catch (Exception ex)
            {
                // Back to the caller, not to the host's log: whoever called
                // handles it, as they would have done inline.
                Error = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                Done.Set();
            }
        }
    }

    /// <summary>
    /// The timing thread's SynchronizationContext: an await on the timing
    /// thread resumes on it. Post queues; Send runs and waits (inline when
    /// already on the timing thread).
    /// </summary>
    private sealed class TimingSynchronizationContext : SynchronizationContext
    {
        private readonly TimingHost _host;

        public TimingSynchronizationContext(TimingHost host) => _host = host;

        public override void Post(SendOrPostCallback d, object? state) => _host.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state) => _host.Invoke(() => d(state));

        public override SynchronizationContext CreateCopy() => this;
    }
}
