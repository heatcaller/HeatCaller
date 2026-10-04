namespace HeatCaller;

/// <summary>
/// The brake on /api/command - the remote page's race-control buttons -
/// and on race.flag through /api/manage, the race-day manager's Race tab,
/// which reaches the same Trigger methods. One gate for both, so a RED from
/// each page at once is still one command at a time.
///
/// There was none (25 Sep audit). The check-in endpoint, which can only
/// sign a driver in, was rate-limited; this one, which can reset a running
/// race, was not, and a right PIN bought unlimited commands. Each command
/// holds a request thread for up to three seconds and queues race-control
/// work onto the UI thread, which still runs after the phone has stopped
/// waiting - so a few hundred at once (a script, or just a phone on a flaky
/// hotspot retrying) left the laptop working through resets and flag
/// changes, each with its own announcement, for minutes, while the race
/// tick and every repaint waited behind them.
///
/// Three limits, checked in this order:
/// <list type="number">
/// <item>One command at a time. A second that arrives while one is still
/// being carried out is answered at once, naming the one in progress -
/// two marshals pressing RED together, or one pressing it twice, is the
/// common case, and the second press is better told "RED is going out
/// now" than queued behind it.</item>
/// <item>Per device: <see cref="PerDevicePerMinute"/> a minute. A marshal
/// works a race with a handful.</item>
/// <item>All devices together: <see cref="AllDevicesPerMinute"/> a minute -
/// the backstop for a device that keeps changing its address, the way
/// PinLockout backs up the per-device PIN count.</item>
/// </list>
/// A command turned away for being busy costs nothing from either count.
///
/// "Carried out" ends when the phone has been answered - within three
/// seconds, see MainForm's RunCommand. A command Race Control is still
/// holding after that (a question up on the laptop) does not keep the gate
/// shut: a RED from another marshal must not be refused for as long as
/// somebody leaves a dialog open. At worst, then, one command every three
/// seconds reaches a laptop that is not keeping up, which is a backlog the
/// UI thread clears in moments rather than minutes.
/// </summary>
internal sealed class RemoteCommandGate
{
    /// <summary>Commands one device may send in a minute. Far above anybody flagging a race by hand.</summary>
    public const int PerDevicePerMinute = 20;

    /// <summary>Commands every device together may send in a minute.</summary>
    public const int AllDevicesPerMinute = 60;

    private const string AllDevices = "all";

    public enum Answer
    {
        /// <summary>Go ahead - and call <see cref="End"/> when the command has been dealt with.</summary>
        Go,

        /// <summary>Another command is still being carried out.</summary>
        Busy,

        /// <summary>This device, or everybody together, has sent too many this minute.</summary>
        TooMany,
    }

    private readonly WebServerService.AttemptLimiter _perDevice = new(PerDevicePerMinute, TimeSpan.FromMinutes(1), 1000);
    private readonly WebServerService.AttemptLimiter _allDevices = new(AllDevicesPerMinute, TimeSpan.FromMinutes(1), 1);
    private readonly object _gate = new();

    private string? _running;

    /// <param name="device">Who is asking - the web server's ClientKey.</param>
    /// <param name="command">What they asked for, as the phone shows it ("RED").</param>
    /// <param name="running">With <see cref="Answer.Busy"/>, the command still being carried out.</param>
    public Answer TryBegin(string device, string command, out string? running)
    {
        lock (_gate)
        {
            running = _running;

            if (_running is not null)
            {
                return Answer.Busy;
            }

            if (!_perDevice.TryReserve(device, out var ticket))
            {
                return Answer.TooMany;
            }

            if (!_allDevices.TryReserve(AllDevices, out _))
            {
                // Not this device's fault - its count is given back.
                _perDevice.Release(device, ticket);
                return Answer.TooMany;
            }

            _running = command;
            return Answer.Go;
        }
    }

    /// <summary>The command let through by the last <see cref="Answer.Go"/> has been dealt with.</summary>
    public void End()
    {
        lock (_gate)
        {
            _running = null;
        }
    }

    /// <summary>A fresh start of the web server: counts forgotten. A command still in progress stays so.</summary>
    public void Clear()
    {
        _perDevice.Clear();
        _allDevices.Clear();
    }
}
