namespace HeatCaller;

/// <summary>
/// Skip a driver in their next race, scored DNS (12 Nov 2026, setup plan
/// step 8) - from the desk: right-click a driver on the board before the
/// race, or right-click the Next Up bar - and the phone's approved "Skip me"
/// does the same. They come off the grid, the round remembers them as DNS,
/// and the saved result carries them (SkipDns). Undo puts them back. Also
/// here: the Big Screen's "PROVISIONAL UNTIL 20:42", which MainForm works out.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>The Big Screen's finished-board line inside a protest window, or null. Set by MainForm.</summary>
    internal Func<string?>? ProvisionalBoard { get; set; }

    /// <summary>Tonight's "Races off between a driver's races:" (setup plan step 8). Set by MainForm; one race with none.</summary>
    internal Func<int>? RestGap { get; set; }

    /// <summary>
    /// Who raced too recently to go straight out in <paramref name="next"/>
    /// (12 Nov 2026, setup plan step 8): the drivers of the last N races run,
    /// N being tonight's races off - the race just run, and with a gap of 2
    /// or 3 the ones before it. Auto start rests them. Null with a gap of 0.
    /// </summary>
    private List<string>? RecentlyRan(RoundDefinition next, RoundDefinition? lastRound)
    {
        var gap = Math.Clamp(RestGap?.Invoke() ?? RestOrder.DefaultGap, 0, RestOrder.MostGap);

        if (gap == 0)
        {
            return null;
        }

        var ran = (lastRound?.AssignedTransponders ?? new List<string>()).ToList();

        if (gap > 1)
        {
            foreach (var earlier in OrderedProgram()
                         .Where(r => r.IsRecorded && r.Kind != RoundKind.Practice && r.Order < next.Order
                                     && !string.Equals(r.Id, lastRound?.Id, StringComparison.Ordinal))
                         .OrderByDescending(r => r.Order)
                         .Take(gap - 1))
            {
                ran.AddRange(earlier.AssignedTransponders);
            }
        }

        return ran;
    }

    /// <summary>
    /// The race still to run that the driver is in next - never the one on
    /// track - or null.
    /// </summary>
    private RoundDefinition? NextRaceOf(string transponder) =>
        OrderedProgram()
            .FirstOrDefault(r => !r.IsRecorded
                                 && !string.Equals(r.Id, _activeRound?.Invoke()?.Id, StringComparison.Ordinal)
                                 && r.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Takes the driver out of their next race, scored DNS, with one undo.
    /// Returns the race, or null when they are in none still to run.
    /// </summary>
    internal RoundDefinition? SkipDriverDns(string transponder, string name, string why)
    {
        if (NextRaceOf(transponder) is not { } race || SkipDns.Skip(race, transponder) is not { } undo)
        {
            return null;
        }

        _rounds?.Update(race);

        var roundId = race.Id;
        _undo.Capture($"Skip {name} in {race.RoundLabel} (DNS)", () =>
        {
            var current = _rounds?.Get(roundId) ?? race;
            var result = current.IsRecorded ? _sessionHistory.Get(current.LinkedSessionId!) : null;
            SkipDns.PutBack(current, transponder, undo, result);
            _rounds?.Update(current);

            if (result is not null)
            {
                _sessionHistory.SaveEdited(result);
            }

            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [program] {name} is back in {current.RoundLabel} - no DNS.");
        });

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [program] {name} is out of {race.RoundLabel}, scored DNS - {why}. Undo puts them back.");
        return race;
    }

    /// <summary>The board's "Skip in this race (DNS)" item: shown for a driver in the next race, before it starts.</summary>
    private ToolStripMenuItem BuildSkipDnsItem()
    {
        var item = new ToolStripMenuItem("Skip in this race (DNS)");
        item.Click += (_, _) =>
        {
            if (SelectedTransponder() is { Length: > 0 } transponder)
            {
                SkipFromDesk(transponder);
            }
        };

        return item;
    }

    /// <summary>For the board's menu as it opens: whether to show the skip, and its words.</summary>
    private void FillSkipDnsItem(ToolStripMenuItem item, string transponder, string name)
    {
        var next = NextRound();
        var show = next is not null
                   && _race.State is RaceState.Idle or RaceState.Finished
                   && next.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase);

        item.Visible = show;

        if (show)
        {
            item.Text = $"Skip {name} in {next!.RoundLabel} (DNS)";
        }
    }

    /// <summary>The desk's skip, said in the console like the phone's.</summary>
    private void SkipFromDesk(string transponder)
    {
        var profile = _profiles.Get(transponder);
        var name = string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;

        if (SkipDriverDns(transponder, name, "skipped at the desk") is null)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [program] {name} isn't in a race still to run.");
            return;
        }

        RefreshNextUpAfterSkip();
    }

    private void RefreshNextUpAfterSkip()
    {
        UpdateNextUpBar();
        RenderLeaderboard();
    }

    /// <summary>
    /// The Next Up bar's right-click menu: each driver of the next race, to
    /// skip (DNS), and the drivers already skipped from it, to put back.
    /// Built as it opens, so it is always the next race's.
    /// </summary>
    private void BuildNextUpSkipMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("..."));

        menu.Opening += (_, e) =>
        {
            menu.Items.Clear();

            if (NextRound() is not { } next || _race.State is not (RaceState.Idle or RaceState.Finished))
            {
                e.Cancel = true;
                return;
            }

            var heading = new ToolStripMenuItem($"Skip in {next.RoundLabel} (DNS)") { Enabled = false };
            menu.Items.Add(heading);

            foreach (var transponder in next.AssignedTransponders.Where(t => !GhostService.IsGhost(t)))
            {
                var who = transponder;
                var item = new ToolStripMenuItem(SpokenNameOf(who));
                item.Click += (_, _) => SkipFromDesk(who);
                menu.Items.Add(item);
            }

            if (next.SkippedDns is { Count: > 0 } skipped)
            {
                menu.Items.Add(new ToolStripSeparator());

                foreach (var transponder in skipped.ToList())
                {
                    var who = transponder;
                    var item = new ToolStripMenuItem($"Put {SpokenNameOf(who)} back in {next.RoundLabel}");
                    item.Click += (_, _) => PutBackFromDesk(next, who);
                    menu.Items.Add(item);
                }
            }

            AppTheme.StyleContextMenu(menu);
        };

        _nextUpBar.ContextMenuStrip = menu;
    }

    /// <summary>Puts a skipped driver back on the next race's grid, DNS off.</summary>
    private void PutBackFromDesk(RoundDefinition race, string transponder)
    {
        if (race.IsRecorded)
        {
            return;
        }

        race.SkippedDns?.RemoveAll(t => string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase));

        if (race.SkippedDns is { Count: 0 })
        {
            race.SkippedDns = null;
        }

        if (!race.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase))
        {
            race.AssignedTransponders = race.AssignedTransponders.Append(transponder).ToList();
        }

        _rounds?.Update(race);
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [program] {SpokenNameOf(transponder)} is back in {race.RoundLabel} - no DNS.");
        RefreshNextUpAfterSkip();
    }
}
