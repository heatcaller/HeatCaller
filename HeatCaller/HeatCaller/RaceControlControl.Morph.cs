namespace HeatCaller;

/// <summary>
/// THE MORPHING BAR, HOLD TO ACT AND NOW PLAYING (25 Oct 2026), as picked on
/// the Race Control layouts canvas ("Morphing bar + hold to act").
///
///   - The flag row shows only the buttons that mean something at this
///     moment of the race - see RaceBar for which, and why. The keys and the
///     phone remote are untouched: they still reach every action.
///   - Red, Checkered and Reset have to be held for about a second - see
///     HoldToAct. Yellow, and everything else, is still one click.
///   - The strip under the row says what a hold is about to do while it is
///     held, and which buttons want holding while nothing is - see HoldStrip.
///     It is always there, so the page never moves when a hold starts.
///   - At the right of the row: the level meters, VOICE, SFX, MUSIC and
///     CROWD (26 Oct 2026, see ChannelStrips). What is going out right now -
///     the imaging piece or sound element, or nothing - is in cyan at the
///     right end of the strip under the row since then (see NowPlaying).
/// </summary>
public partial class RaceControlControl
{
    private FlagRow? _flagRow;
    private readonly HoldStrip _holdStrip = new();
    private readonly List<HoldToAct> _holds = new();

    /// <summary>The buttons on the row now, or null before the first look - so a change is acted on once.</summary>
    private RaceBarButtons? _barShown;

    /// <summary>The flag row: the word, the buttons, and Now Playing. Built once, by BuildLayout.</summary>
    private FlagRow BuildFlagRow(ButtonBar bar)
    {
        // The card's title too, and the On Air bar under the buttons - see
        // FlagRow, and BuildLayout's card.
        // The meters out of the row since 6 Nov 2026 - under Broadcast, in
        // the right-hand column (BuildLayout) - so the buttons and the On Air
        // bar have the card's width, and the card is no taller than they are.
        _flagRow = new FlagRow(bar, "Race Control", _holdStrip, metersInRow: false);
        return _flagRow;
    }

    /// <summary>
    /// Makes <paramref name="button"/> one that has to be held, doing
    /// <paramref name="act"/> once it has been. Its Click is left unwired:
    /// a click is a hold let go too soon.
    /// </summary>
    private void HoldToUse(Button button, string doing, Action act)
    {
        var hold = new HoldToAct(button, doing, act);

        hold.Changed += h => _holdStrip.Follow(h);
        hold.Changed += _ => MoveDeskIfOwed();
        hold.CalledOff += _ => _holdStrip.CalledOff();

        if (button is FlagButton flag)
        {
            // A flag button paints every pixel itself and raises no Paint
            // event, so it draws the clock through its own overlay.
            flag.Overlay = hold.PaintOver;
        }
        else
        {
            button.Paint += (_, e) => hold.PaintOver(e.Graphics, button.ClientRectangle, button.ForeColor, null);
        }

        _holds.Add(hold);
    }

    /// <summary>
    /// Shows the buttons for where the race is, and the word for it. Run
    /// with the flag colours - on every change of state and flag, and on the
    /// clock tick, which is what catches practice, a warm-up and On Air
    /// starting and stopping. Does nothing to the row when the buttons are
    /// the same as last time.
    /// </summary>
    private void UpdateMorphingBar(FlagState flag)
    {
        if (_flagRow is null || IsDisposed || Disposing)
        {
            return;
        }

        var state = _race.State;
        var practice = _race.IsPracticeMode;
        var warmingUp = _race.IsWarmingUp;
        var phase = RaceBar.PhaseFor(state, flag, practice, warmingUp);

        _flagRow.SetPhase(RaceBar.Word(phase), PhaseInk(phase));

        var shown = RaceBar.ButtonsFor(state, flag, practice, warmingUp, _nextWaiting, _settings.BetweenRacesFlagButtons);

        if (_barShown == shown)
        {
            return;
        }

        _barShown = shown;

        ShowOnBar(_btnWarmUp, shown, RaceBarButtons.WarmUp);
        ShowOnBar(_btnStage, shown, RaceBarButtons.Stage);
        ShowOnBar(_btnCountdown, shown, RaceBarButtons.Countdown);
        ShowOnBar(_btnGreen, shown, RaceBarButtons.Green);
        ShowOnBar(_btnYellow, shown, RaceBarButtons.Yellow);
        ShowOnBar(_btnRed, shown, RaceBarButtons.Red);
        ShowOnBar(_btnCheckered, shown, RaceBarButtons.Checkered);
        ShowOnBar(_btnBarSkip, shown, RaceBarButtons.Skip);
        ShowOnBar(_btnBarHoldTalk, shown, RaceBarButtons.HoldTalk);
        ShowOnBar(_btnBarCallUp, shown, RaceBarButtons.CallUp);
        ShowOnBar(_btnBarEditResult, shown, RaceBarButtons.EditResult);
        ShowOnBar(_btnReset, shown, RaceBarButtons.Reset);

        _holdStrip.SetResting(RaceBar.RestingHint(shown, practice));
        _flagRow.Rearrange();
    }

    private static void ShowOnBar(Button button, RaceBarButtons shown, RaceBarButtons which)
    {
        // Set every time, not on a change: while Race Control is off screen
        // (another page, or a race restored before the window is up)
        // Visible reads false whatever the button's own setting, so a "has
        // it changed" test skipped every hide - and the page came back with
        // all eight buttons until the phase next moved. Setting true on a
        // button already showing raises no VisibleChanged, so a hold in
        // progress is not called off by it.
        button.Visible = shown.HasFlag(which);
    }

    /// <summary>The word's colour: the flag's own while racing, muted otherwise.</summary>
    private static Color PhaseInk(RacePhase phase) => phase switch
    {
        RacePhase.Racing => AppTheme.Success,
        RacePhase.Caution => AppTheme.Warning,
        RacePhase.Stopped => AppTheme.Danger,
        _ => AppTheme.TextMuted,
    };

    /// <summary>On the clock tick, on the UI thread: the On Air bar - see UpdateOnAirBar.</summary>
    private void UpdateNowPlaying() => UpdateOnAirBar();

    /// <summary>
    /// THE ON AIR BAR STAYS UP WHILE ANYTHING IS GOING (26 Oct 2026, asked
    /// for by Asher: "I want the on air bar to remain up while everything is
    /// going. instead of it saying show is going out it should list the
    /// actual segment that is playing. the imaging element to the far right
    /// should be incorporated into it").
    ///
    /// The strip under the flag row is On Air's card whenever something is
    /// going out: the announcer speaking, the recap show, a replayed moment,
    /// an imaging piece or bed, a sponsor video, the music or the crowd - and
    /// while On Air is live, between the jingle and the staging. It says
    /// "On air: " and the segment being spoken ("On air: Race Recap"), and
    /// the imaging element going out sits inside it (HoldStrip). When nothing
    /// is going out it goes back to its resting hint.
    ///
    /// While On Air writes and voices the opening, its own red card with the
    /// bar filling is left alone (PaintOnAirProgress) - the imaging element
    /// is shown inside that too. A hold takes the whole strip, as before.
    /// </summary>
    private void UpdateOnAirBar()
    {
        if (_flagRow is null || IsDisposed || Disposing)
        {
            return;
        }

        // Play Again's press follows the flags and the mute (25 Nov 2026) - see
        // RaceControlControl.PlayAgain. Repaints only on a change.
        RefreshPlayAgain();

        var playing = NowPlaying.Describe(NowPlaying.Shown);
        _flagRow.SetNowPlaying(playing);
        _holdStrip.SetNowPlaying(playing);

        if (_onAirProgressShowing)
        {
            return;
        }

        var segment = SegmentOnAir();

        // Between races, a held talk says so where the race director is
        // looking (the plan's section 8), not only in the Broadcast window.
        var talkHeldBetweenRaces = TalkHeld && _race.State is RaceState.Finished or RaceState.Idle;

        if (segment is null && playing.Length == 0 && !_onAirLive)
        {
            if (talkHeldBetweenRaces)
            {
                _holdStrip.ShowWork(GapWords.TalkHeld, null, string.Empty, AppTheme.Warning);
                return;
            }

            _holdStrip.EndWork();
            return;
        }

        var words = segment is not null
            ? $"On air: {segment}"
            : _onAirLive ? "On air" : string.Empty;

        _holdStrip.ShowWork(words, null, _onAirLive ? OnAirSkipNote : SkipNote(segment), OnAirLiveCyan);
    }

    /// <summary>
    /// What Skip would skip, at the right of the On Air bar while the flag
    /// row's Skip is showing and can act ("Skip: Race Recap") - so a click is
    /// never a guess. Empty otherwise.
    /// </summary>
    private string SkipNote(string? segment) =>
        segment is { Length: > 0 } && _btnBarSkip.Visible && _btnBarSkip.Enabled ? GapWords.SkipNote(segment) : string.Empty;

    /// <summary>
    /// The segment going out now, as the bar names it: the line being
    /// spoken (the part of the broadcast it is, as AnnouncementService
    /// names it - "Race Recap", "Driver Intros"), else the show whose music
    /// is under it, else the recap show or a replayed moment between their
    /// lines. Null when none of them is on.
    /// </summary>
    private string? SegmentOnAir()
    {
        if (_speaking)
        {
            if (_announcer.OnAirNow is { Length: > 0 } line && line.Trim().Length > 0)
            {
                return line.Trim();
            }

            return ShowOnAir.Caption is { Length: > 0 } underIt ? underIt : "the announcer";
        }

        // Not once it has been stopped (Skip, Reset, the next race): its
        // sequence can take a moment to unwind, and the bar must not name a
        // recap over silence while it does.
        if (Volatile.Read(ref _recapShow) is { Stop.IsCancellationRequested: false })
        {
            // The show's own name, as typed on the AI Commentary page (26 Oct
            // 2026, asked for by Asher: "instead of saying recap show use the
            // name in the AI commentary if configured").
            return ShowOnAir.Caption is { Length: > 0 } show ? show : RecapShowNamed;
        }

        return _replayCut is not null ? "a replayed moment" : null;
    }

    /// <summary>
    /// The recap show as the On Air bar names it: the name on the AI
    /// Commentary page's Recap show card (AppSettings.RecapShowName) when one
    /// is typed, else "the recap show".
    /// </summary>
    private string RecapShowNamed => RecapShowLabel(_settings.RecapShowName);

    /// <summary>See RecapShowNamed - for the tests.</summary>
    internal static string RecapShowLabel(string? configured) =>
        (configured ?? string.Empty).Trim() is { Length: > 0 } name ? name : "the recap show";

    /// <summary>The holds' timers and handlers, let go with the page.</summary>
    private void DisposeHolds()
    {
        foreach (var hold in _holds)
        {
            hold.Dispose();
        }

        _holds.Clear();

        // And 0's hold, On Air's key - see OnAirKey.
        _onAirKey?.Dispose();
        _onAirKey = null;
    }
}
