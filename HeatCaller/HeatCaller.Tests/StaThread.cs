using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace HeatCaller.Tests;

/// <summary>
/// Runs a test's windows on a thread of their own, made for it and gone
/// when it finishes.
///
/// Windows need a single-threaded apartment - drag and drop refuses
/// anything else, and several pages switch it on - and xUnit's own threads
/// are not. A thread per test rather than one shared: when a thread ends,
/// everything its windows left running ends with it - a page's timer, work
/// posted to a window already closed - so nothing one test built can go off
/// in the middle of the next.
///
/// A message box would wait for ever on windows nobody can see, so for the
/// length of the test AppDialog throws instead of showing one. And a test
/// that hangs regardless fails at its time limit rather than holding
/// check.cmd for ever: the thread is a background one, so it does not keep
/// the test run alive either.
/// </summary>
internal static class StaThread
{
    /// <summary>A test's thread that ran past its limit and was left running, and what it was doing.</summary>
    private static (Thread Thread, string Where)? _leftRunning;

    /// <param name="whereItWas">What the work was doing, for the message if it never finishes.</param>
    /// <param name="stuckAfter">
    /// For long work that says what it is on as it goes (a sweep of many
    /// windows): how long it may stay on one thing before it counts as
    /// stuck. A window waiting for an answer is caught that long after it
    /// opened, rather than when the whole limit runs out - which a sweep of
    /// many windows needs to be generous, and which it outgrew as windows
    /// were added (the window sweep, 30 Sep: 90 windows at five sizes took
    /// longer than its five minutes).
    /// </param>
    public static T Run<T>(Func<T> work, TimeSpan limit, Func<string> whereItWas, TimeSpan? stuckAfter = null)
    {
        // An earlier test's windows still going would share this one's text
        // size and data folder - both are the whole process's - and whatever
        // failed next would be blamed on the wrong test.
        if (_leftRunning is { } stuck && !stuck.Thread.Join(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException(
                $"Not started: an earlier test's windows are still running, stuck on {stuck.Where}.");
        }

        _leftRunning = null;

        var result = default(T);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            // Exceptions from inside a window's messages go to the test's
            // ThreadException handler even under a debugger, which otherwise
            // lets them loose. First, while this thread has no windows - it
            // cannot be changed after.
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException, true);
            }
            catch (InvalidOperationException)
            {
                // Too late for this thread; the default stands.
            }

            AppDialog.ThrowInsteadOfShowing = true;
            Application.EnterThreadModal += CloseWhatOpened;

            try
            {
                MeasureAt96Dpi();
                result = work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Application.EnterThreadModal -= CloseWhatOpened;
                AppDialog.ThrowInsteadOfShowing = false;
            }
        })
        {
            IsBackground = true,
            Name = "test-windows",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var onSince = System.Diagnostics.Stopwatch.StartNew();
        var on = whereItWas();

        // Once a second: what the work is on now, and for how long.
        while (!thread.Join(TimeSpan.FromSeconds(1)))
        {
            var now = whereItWas();

            if (!string.Equals(now, on, StringComparison.Ordinal))
            {
                on = now;
                onSince.Restart();
            }

            // Not "stuck": the check of an earlier test's thread, above,
            // declared that name for the whole method.
            if (stuckAfter is { } stillFor && onSince.Elapsed > stillFor)
            {
                _leftRunning = (thread, now);

                throw new TimeoutException(
                    $"Stuck for {stillFor.TotalSeconds:0} seconds on: {now}. "
                    + "A window waiting for an answer that cannot come would do this.");
            }

            if (clock.Elapsed > limit)
            {
                _leftRunning = (thread, now);

                throw new TimeoutException(
                    $"Did not finish in {limit.TotalMinutes:0.#} minutes. It was on: {now}. "
                    + "A window waiting for an answer that cannot come would do this.");
            }
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        return result!;
    }

    /// <summary>
    /// A window of the app's own that opened and waits for an answer - not a
    /// message (AppDialog throws instead of showing one in a test) but a
    /// dialog a page opened with ShowDialog. Closed as soon as it is up, and
    /// said out loud: the exception reaches the test's ThreadException
    /// handler, which reports it against whatever the test was doing.
    /// </summary>
    private static void CloseWhatOpened(object? sender, EventArgs e)
    {
        SynchronizationContext.Current?.Post(_ =>
        {
            // This thread's own: OpenForms is the whole process's.
            var dialog = Application.OpenForms.Cast<Form>().LastOrDefault(form => form.Modal && !form.InvokeRequired);

            if (dialog is null)
            {
                return;
            }

            var title = dialog.Text;
            dialog.Close();

            throw new InvalidOperationException(
                $"A window opened waiting for an answer (\"{title}\"), which nobody can give in a test - it was closed.");
        }, null);
    }

    /// <summary>
    /// Lets a window catch up: anything it posted to itself while being
    /// built or shown (a BeginInvoke from a layout handler) runs, so it is
    /// done before anything is measured. Twice, because work run by the
    /// first pass can post more.
    /// </summary>
    public static void Settle()
    {
        Application.DoEvents();
        Application.DoEvents();
    }

    /// <summary>
    /// Windows made on this thread see 96 dpi whatever the screen - which is
    /// what the app always sees, since it runs without DPI awareness.
    /// TestSetup asks for that for the whole process; asked again here for
    /// this thread in case something got there first.
    /// </summary>
    private static void MeasureAt96Dpi()
    {
        try
        {
            SetThreadDpiAwarenessContext(DpiAwarenessContextUnaware);
        }
        catch (EntryPointNotFoundException)
        {
            // Older than Windows 10 1607. The process setting stands.
        }
    }

    /// <summary>DPI_AWARENESS_CONTEXT_UNAWARE.</summary>
    private static readonly IntPtr DpiAwarenessContextUnaware = new(-1);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);
}
