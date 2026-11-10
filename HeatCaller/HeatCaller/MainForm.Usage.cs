namespace HeatCaller;

/// <summary>
/// MainForm's side of this month's allowances (UsageMeters): starting the
/// store, asking the relay and ElevenLabs for the figures once the window
/// is up and every five minutes after, the chip at the foot of the sidebar
/// (UsageChip), the This Month window (ThisMonthForm), and what Mission
/// Control's Allowances tile is judged from.
///
/// A meter that runs out mid-race turns the chip red and the tile to a
/// problem or a look, and the store and the clients say so in the console.
/// Nothing goes on the TIMING line: Next Up turns into a red alarm for
/// anything there, and an allowance is not a timing fault.
/// </summary>
public partial class MainForm
{
    /// <summary>The This Month window while it is open. One at a time.</summary>
    private ThisMonthForm? _thisMonthForm;

    /// <summary>
    /// Whether the chip has anything to say, and so a line in the footer.
    /// Kept rather than read from the chip: a control reports itself hidden
    /// while the window is, and the footer is placed before it is shown.
    /// </summary>
    private bool _usageChipShown;

    /// <summary>Called once, from the constructor, before the pages that show the meters are built.</summary>
    private void WireUsage()
    {
        // What was seen last time (this month's figures, which emails have
        // gone) and the emails at 80% and 100%. The store's console lines
        // come from any thread; the console marshals for itself.
        UsageMeters.Shared.Initialise(_settings, line => OnClientRawMessage($"{AppTime.LogStampNow()}  {line}"));

        _usageChip.Click += (_, _) => ShowThisMonth();
        pnlSidebarFooter.Controls.Add(_usageChip);

        // Raised on any thread; TryPost brings it over, or drops it while
        // the window is closing. Taken off with the window: the store is a
        // static that outlives it (a test that builds a MainForm, a second
        // window), and a handler left on it keeps the window alive.
        Action usageChanged = () => TryPost(ShowUsageChip);
        UsageMeters.Shared.Changed += usageChanged;
        Disposed += (_, _) => UsageMeters.Shared.Changed -= usageChanged;

        // The relay answered an Ask for More with a top-up there and then
        // (Bug hunt 15): the month is no longer spent, so the meters are read
        // again now, not at the next probe, and a voice resting on Kokoro
        // over the spent allowance is tried again on its next line.
        Action<MeterKind> autoToppedUp = kind => TryPost(() =>
        {
            if (kind == MeterKind.HeatCallerVoice)
            {
                _announcer.ResetHeatCallerVoiceState("the relay gave this month's HeatCaller voice a top-up");
            }

            RefreshUsage();
        });
        TopUpRequest.AutoToppedUp += autoToppedUp;
        Disposed += (_, _) => TopUpRequest.AutoToppedUp -= autoToppedUp;

        // Once the window is up: the first ask goes to the relay, which a
        // window still being built has no business waiting on.
        Shown += (_, _) =>
        {
            ShowUsageChip();
            RefreshUsage();
            _usageRefreshTimer.Start();
        };

        _usageRefreshTimer.Tick += (_, _) => RefreshUsage();
    }

    /// <summary>
    /// Asks the store for this month's figures again. Called on the UI
    /// thread, as the store asks: it reads the settings and the voice
    /// before its first await, then waits on the network without holding
    /// this thread. RefreshAsync never throws; what it finds arrives
    /// through Changed.
    /// </summary>
    private void RefreshUsage()
    {
        if (_closingDown)
        {
            return;
        }

        _ = UsageMeters.Shared.RefreshAsync(_settings, _voiceStore.Current);
    }

    /// <summary>The chip brought up to date with the store, and the footer made room for it - or given it back. UI thread.</summary>
    private void ShowUsageChip()
    {
        if (_closingDown || IsDisposed)
        {
            return;
        }

        var meters = UsageMeters.Shared.All;

        _usageChip.Show(meters);

        var show = _usageChip.Text.Length > 0;

        _footerTooltip.SetToolTip(_usageChip, show ? UsageGlance.Tooltip(meters) : string.Empty);

        if (show == _usageChipShown)
        {
            return;
        }

        _usageChipShown = show;
        _usageChip.Visible = show;
        PlaceSidebar();
    }

    /// <summary>Opens the This Month window, or brings it to the front when it is open already.</summary>
    internal void ShowThisMonth()
    {
        if (_thisMonthForm is { IsDisposed: false } open)
        {
            if (open.WindowState == FormWindowState.Minimized)
            {
                open.WindowState = FormWindowState.Normal;
            }

            open.Activate();
            return;
        }

        _thisMonthForm = new ThisMonthForm(_settings, () => _voiceStore.Current);
        _thisMonthForm.FormClosed += (_, _) => _thisMonthForm = null;
        _thisMonthForm.Show(this);
    }

    /// <summary>
    /// What Mission Control's Allowances tile judges. The commentary meter
    /// only while commentary is on: a club that has switched it off has
    /// nothing riding on its lines tonight. Each voice meter only while that
    /// voice is the one chosen: the store forgets the other at its next
    /// refresh, up to five minutes away, and a switch to the club's own key
    /// must not leave "Kokoro is speaking" on the wall until then. UI thread.
    /// </summary>
    private MissionControl.AllowanceState UsageForMissionControl()
    {
        var engine = _voiceStore.Current.EffectiveEngine;

        var meters = UsageMeters.Shared.All
            .Where(m => m.Kind switch
            {
                MeterKind.Commentary => _settings.CommentaryEnabled,
                MeterKind.HeatCallerVoice => engine == VoiceEngine.HeatCaller,
                MeterKind.ElevenLabsKey => engine == VoiceEngine.ElevenLabs,
                _ => true,
            })
            .ToList();

        return new MissionControl.AllowanceState(meters, LocalModelThere());
    }

    /// <summary>Whether the model on this PC was there when last looked, and when (Environment.TickCount64).</summary>
    private bool _localModelThere;

    private long _localModelLookedTicks;

    /// <summary>
    /// Whether the model on this PC is there to write (BundledLlmClient.IsModelAvailableWith, the Commentary page's model file included),
    /// looked for at most every half minute: Mission Control reads its checks
    /// once a second, and the answer is the size of a file on disk.
    /// </summary>
    private bool LocalModelThere()
    {
        var now = Environment.TickCount64;

        if (_localModelLookedTicks == 0 || now - _localModelLookedTicks >= 30_000)
        {
            _localModelLookedTicks = now;
            // A model file named on the Commentary page counts, as it does
            // for the switch-over itself (CommentaryControl).
            _localModelThere = BundledLlmClient.IsModelAvailableWith(_settings.CommentaryLocalModelPath);
        }

        return _localModelThere;
    }
}
