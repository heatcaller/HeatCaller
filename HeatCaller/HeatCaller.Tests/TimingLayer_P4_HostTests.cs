using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// A stand-in for the UI thread's SynchronizationContext: a post is only
/// queued, and runs when the test pumps it - on the test's own thread. Never
/// pumped, it is a frozen window.
/// </summary>
internal sealed class QueueingSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posted = new();

    /// <summary>Posts waiting to run.</summary>
    public int Pending => _posted.Count;

    public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

    public override void Send(SendOrPostCallback d, object? state) =>
        throw new InvalidOperationException("Nothing may wait on the UI thread.");

    /// <summary>Runs every post waiting, and any they post in turn, here and now. Returns how many ran.</summary>
    public int Pump()
    {
        var previous = Current;
        SetSynchronizationContext(this);

        try
        {
            var ran = 0;

            while (_posted.TryDequeue(out var post))
            {
                post.Callback(post.State);
                ran++;
            }

            return ran;
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}

/// <summary>
/// P4 of the timing layer (23 Oct 2026): the timing thread itself - one
/// thread, one queue, in order; inline when called from itself; a failing
/// item never kills it; awaits resume on it; Dispose drains and stops.
/// </summary>
public sealed class TimingLayer_P4_HostTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public void Work_runs_in_the_order_it_was_given_on_the_one_timing_thread()
    {
        using var host = new TimingHost();
        var order = new ConcurrentQueue<int>();
        var threads = new ConcurrentBag<int>();
        string? name = null;

        for (var i = 0; i < 200; i++)
        {
            var n = i;
            Assert.True(host.Post(() =>
            {
                order.Enqueue(n);
                threads.Add(Environment.CurrentManagedThreadId);
                name = Thread.CurrentThread.Name;
            }));
        }

        // A barrier: an Invoke runs after everything posted before it.
        Assert.Equal(200, host.Invoke(() => order.Count));

        Assert.Equal(Enumerable.Range(0, 200), order);
        Assert.Single(threads.Distinct());
        Assert.NotEqual(Environment.CurrentManagedThreadId, threads.First());
        Assert.Equal("HeatCaller timing", name);
        Assert.False(host.IsTimingThread);
        Assert.True(host.Invoke(() => host.IsTimingThread));
    }

    [Fact]
    public void An_invoke_from_the_timing_thread_runs_inline_rather_than_waiting_on_itself()
    {
        using var host = new TimingHost();

        // Nested three deep: each would deadlock if it queued and waited.
        var answer = host.Invoke(() => host.Invoke(() => host.Invoke(() => 42)));

        Assert.Equal(42, answer);
    }

    [Fact]
    public void A_failing_item_is_logged_and_the_thread_carries_on_and_an_invoke_gets_the_exception()
    {
        var log = new ConcurrentQueue<string>();
        using var host = new TimingHost(log: log.Enqueue);

        host.Post(() => throw new InvalidOperationException("posted boom"));
        var ex = Assert.Throws<ArgumentException>(() => host.Invoke<int>(() => throw new ArgumentException("invoked boom")));
        Assert.Equal("invoked boom", ex.Message);

        // Still alive, still in order.
        Assert.Equal(7, host.Invoke(() => 7));
        Assert.Contains(log, line => line.Contains("posted boom", StringComparison.Ordinal));

        // An invoke's exception goes to its caller, not to the log.
        Assert.DoesNotContain(log, line => line.Contains("invoked boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_await_on_the_timing_thread_resumes_on_the_timing_thread()
    {
        using var host = new TimingHost();
        var gate = new TaskCompletionSource();
        int? resumedOn = null;
        var timingThread = host.Invoke(() => Environment.CurrentManagedThreadId);

        async Task Waits()
        {
            await gate.Task;
            resumedOn = Environment.CurrentManagedThreadId;
        }

        var running = host.Invoke(Waits);
        Assert.False(running.IsCompleted);

        // Completed from a thread-pool thread: the continuation must still
        // come back to the timing thread (its SynchronizationContext).
        await Task.Run(() => gate.SetResult());
        await running.WaitAsync(Patience); // throws if it never resumes

        Assert.Equal(timingThread, resumedOn);
    }

    [Fact]
    public void The_clock_ticks_on_the_timing_thread_while_nothing_else_is_given()
    {
        using var host = new TimingHost();
        var ticks = 0;
        var onTiming = true;

        host.StartTicking(() =>
        {
            onTiming &= host.IsTimingThread;
            Interlocked.Increment(ref ticks);
        }, TimeSpan.FromMilliseconds(10));

        var deadline = DateTime.UtcNow + Patience;
        while (Volatile.Read(ref ticks) < 5 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(5);
        }

        Assert.True(Volatile.Read(ref ticks) >= 5);
        Assert.True(onTiming);
    }

    [Fact]
    public void The_tick_is_not_starved_by_a_steady_stream_of_work()
    {
        using var host = new TimingHost();
        var ticks = 0;
        host.StartTicking(() => Interlocked.Increment(ref ticks), TimeSpan.FromMilliseconds(20));

        // Keep the queue busy for half a second.
        var until = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
        while (DateTime.UtcNow < until)
        {
            host.Post(() => Thread.SpinWait(2_000));
            host.Post(() => Thread.SpinWait(2_000));
            Thread.Sleep(1);
        }

        host.Invoke(() => { });
        Assert.True(Volatile.Read(ref ticks) >= 5, $"only {ticks} ticks");
    }

    [Fact]
    public void Dispose_runs_what_was_queued_then_stops_and_later_calls_run_on_the_caller()
    {
        var log = new ConcurrentQueue<string>();
        var host = new TimingHost(log: log.Enqueue);
        var ran = 0;

        for (var i = 0; i < 50; i++)
        {
            host.Post(() =>
            {
                Thread.Sleep(1);
                Interlocked.Increment(ref ran);
            });
        }

        host.Dispose();

        Assert.Equal(50, ran);
        Assert.True(host.HasStopped);

        // Nothing more is queued...
        Assert.False(host.Post(() => Interlocked.Increment(ref ran)));
        Assert.Equal(50, ran);

        // ...but the race can still be read while the window tears down.
        Assert.Equal(Environment.CurrentManagedThreadId, host.Invoke(() => Environment.CurrentManagedThreadId));

        // Twice is fine.
        host.Dispose();
    }

    [Fact]
    public void A_send_through_the_timing_context_waits_and_a_post_queues()
    {
        using var host = new TimingHost();
        var sent = 0;
        var posted = 0;

        host.Context.Send(_ => sent = host.IsTimingThread ? 1 : -1, null);
        host.Context.Post(_ => posted = host.IsTimingThread ? 1 : -1, null);
        host.Invoke(() => { });

        Assert.Equal(1, sent);
        Assert.Equal(1, posted);
        Assert.Same(host.Context, host.Context.CreateCopy());
    }
}
