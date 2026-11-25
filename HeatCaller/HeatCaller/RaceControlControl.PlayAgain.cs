namespace HeatCaller;

/// <summary>
/// PLAY AGAIN on Race Control (25 Nov 2026, asked for by Asher: "A bar, with
/// the split button from B, and a change the flags like in C" - board G of
/// plans/play-again-plan.md). The split button at the On Air bar's right
/// end: one press plays what the race calls for now (PlayAgain.MainPress),
/// the arrow lists the lines heard tonight. S and the phone remote's Say
/// again press the same button. The rules are PlayAgain's; the audio is the
/// announcer's (AnnouncementService.PlayAgain).
/// </summary>
public partial class RaceControlControl
{
    private readonly PlayAgainButton _playAgain = new();
    private ContextMenuStrip? _playAgainMenu;
    private bool _playAgainHooked;

    /// <summary>The Play Again button - for the tests.</summary>
    internal PlayAgainButton PlayAgainButton => _playAgain;

    /// <summary>Puts the button on the On Air bar and keeps it up to date. Once, from the layout.</summary>
    private void BuildPlayAgain()
    {
        _holdStrip.SetTrailing(_playAgain);
        _playAgain.Pressed += () => PlayAgainNow();
        _playAgain.ListWanted += OpenPlayAgainList;

        if (!_playAgainHooked)
        {
            _playAgainHooked = true;

            // Kept lines arrive on the voice's thread.
            _announcer.SaidChanged += () =>
            {
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(RefreshPlayAgain);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Closing.
                }
            };
        }

        RefreshPlayAgain();
    }

    /// <summary>Where the race is, for Play Again.</summary>
    private PlayAgainPhase PlayAgainPhaseNow => PlayAgain.PhaseOf(_race.State, _race.IsPracticeMode);

    /// <summary>What one press plays now.</summary>
    private PlayAgainPress PlayAgainPressNow()
    {
        var phase = PlayAgainPhaseNow;
        return PlayAgain.MainPress(_announcer.Said, phase, _announcer.Muted, _announcer.RaceFactsNow(),
            raceOverSince: phase is PlayAgainPhase.AfterTheFlag or PlayAgainPhase.BetweenRaces);
    }

    /// <summary>
    /// The button's words and the bar's "Last said", as the race and the
    /// log are now. On the clock's tick (with UpdateOnAirBar) and whenever a
    /// line is kept. Repaints only on a change.
    /// </summary>
    internal void RefreshPlayAgain()
    {
        if (IsDisposed)
        {
            return;
        }

        var press = PlayAgainPressNow();
        _playAgain.ShowPress(press.Caption, press.Enabled ? PlayAgainWords.WithLength(press.Detail, LengthOf(press.Lines)) : press.Detail, press.Enabled);

        var log = _announcer.Said;
        _holdStrip.SetLastSaid(log.Count == 0 ? null : new LastSaidShown(
            PlayAgain.KindWords(log[0].Kind),
            KindColour(log[0].Kind),
            AppTime.TimeOfDay(log[0].AtLocal),
            $"“{log[0].Shown}”"));
    }

    /// <summary>How long each kept line plays, worked out once from its audio - by its number in the log.</summary>
    private readonly Dictionary<long, double?> _playAgainLengths = new();

    /// <summary>How long <paramref name="lines"/> play together, in seconds - null when any of them has no audio kept.</summary>
    private double? LengthOf(IReadOnlyList<SaidLine> lines)
    {
        double total = 0;

        foreach (var line in lines)
        {
            if (!_playAgainLengths.TryGetValue(line.N, out var seconds))
            {
                seconds = null;

                try
                {
                    if (line.Audio is { Length: > 0 } audio)
                    {
                        var (pcm, format) = AnnouncementService.DecodeAudio(audio);
                        seconds = (double)pcm.Length / Math.Max(1, format.AverageBytesPerSecond);
                    }
                }
                catch
                {
                    // Not audio a decoder knows: no length shown.
                }

                if (_playAgainLengths.Count > 64)
                {
                    _playAgainLengths.Clear();
                }

                _playAgainLengths[line.N] = seconds;
            }

            if (seconds is not { } s)
            {
                return null;
            }

            total += s;
        }

        return lines.Count == 0 ? null : total;
    }

    /// <summary>The kind's colour as a dot for the list - dimmed for a line that can't be played.</summary>
    private static Bitmap KindDot(SaidKind kind, bool playable)
    {
        var size = UiLayout.AtDpi(10);
        var dot = new Bitmap(size, size);

        using var g = Graphics.FromImage(dot);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var colour = playable ? KindColour(kind) : Blend(KindColour(kind), AppTheme.Surface, 0.6f);
        using var brush = new SolidBrush(colour);
        g.FillEllipse(brush, 1, 1, size - 2, size - 2);
        return dot;
    }

    /// <summary>A kind's colour, as the list and the bar show it: call-ups cyan, the race's moments pink, the winner gold.</summary>
    internal static Color KindColour(SaidKind kind) => kind switch
    {
        SaidKind.CallUp => AppTheme.Accent,
        SaidKind.Winner or SaidKind.Record => AppTheme.Warning,
        SaidKind.Countdown or SaidKind.Start or SaidKind.Flag or SaidKind.LastLap or SaidKind.Finish => AppTheme.Primary,
        SaidKind.Commentary => Blend(AppTheme.Primary, AppTheme.Accent, 0.5f),
        _ => AppTheme.TextMuted,
    };

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    /// <summary>
    /// The main press: plays what the race calls for now - the bar's button,
    /// S, and the phone's Say again. Returns what happened, in words, for the
    /// phone.
    /// </summary>
    internal string PlayAgainNow()
    {
        var press = PlayAgainPressNow();

        if (!press.Enabled)
        {
            var why = press.Caption == "Muted" ? "Nothing played: the announcer is muted (M to unmute)." : $"Nothing played: {press.Detail}.";
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [voice] Play again - {why}");
            return why;
        }

        return PlayLinesAgain(press.Lines, press.Caption);
    }

    /// <summary>The S key: the bar's main press. Says why not in the log when it can't play.</summary>
    internal void TriggerPlayAgain() => PlayAgainNow();

    private string PlayLinesAgain(IReadOnlyList<SaidLine> lines, string caption)
    {
        if (_announcer.PlayAgainLines(lines) is { } refused)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [voice] Play again: not played - {refused}.");
            return $"Not played: {refused}.";
        }

        var what = PlayAgainWords.Summary(lines[0]);
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [voice] {caption}: {what}, queued to play again.");
        return $"{caption}: {what}";
    }

    /// <summary>
    /// The list under the arrow: tonight's lines, newest first - who, when,
    /// and the words - the out-of-date ones greyed with why. A row plays
    /// that line (and the rest of its exchange) once the menu has gone.
    /// </summary>
    private void OpenPlayAgainList()
    {
        _playAgainMenu?.Dispose();
        var menu = new ContextMenuStrip();
        _playAgainMenu = menu;

        var log = _announcer.Said;
        var phase = PlayAgainPhaseNow;
        var now = _announcer.RaceFactsNow();
        var over = phase is PlayAgainPhase.AfterTheFlag or PlayAgainPhase.BetweenRaces;
        var muted = _announcer.Muted;

        var head = new ToolStripMenuItem { Enabled = false };
        AppTheme.SetMenuNote(head, "Said tonight", log.Count == 0
            ? "Nothing yet - each line the room hears is kept here"
            : $"Last {log.Count} · newest first · S plays the one marked S");
        menu.Items.Add(head);
        menu.Items.Add(new ToolStripSeparator());

        // The lines one press (and S) plays now are marked, as the mockup's top row is.
        var pressed = PlayAgainPressNow();
        var pressedLines = pressed.Enabled ? pressed.Lines.Select(l => l.N).ToHashSet() : new HashSet<long>();

        foreach (var line in log)
        {
            var outOfDate = PlayAgain.WhyOutOfDate(line, now, over);
            var (title, note) = PlayAgainWords.Row(line, outOfDate);
            var item = new ToolStripMenuItem
            {
                Enabled = outOfDate is null && !muted,

                // The kind's colour, as a dot - the mockup's chip.
                Image = KindDot(line.Kind, outOfDate is null),
            };

            AppTheme.SetMenuNote(item, pressedLines.Contains(line.N) ? $"{title}  ·  S" : title, PlayAgainWords.Shortened(note));

            var chosen = line;
            item.Click += (_, _) => BeginInvoke(() =>
            {
                if (!IsDisposed)
                {
                    PlayLinesAgain(PlayAgain.WithItsExchange(_announcer.Said, chosen), "Play again");
                }
            });
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        var foot = new ToolStripMenuItem { Enabled = false };
        AppTheme.SetMenuNote(foot, muted ? "Muted - M to unmute" : "The same audio the room heard",
            "Waits for live calls · a flag call goes first");
        menu.Items.Add(foot);

        AppTheme.StyleContextMenu(menu);
        menu.Show(_playAgain, new Point(_playAgain.Width - menu.GetPreferredSize(Size.Empty).Width, _playAgain.Height + UiLayout.AtDpi(4)));
    }
}
