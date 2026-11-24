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
    private RoundDefinition? NextRaceOf(string transponder)
    {
        // The round in the Round box is passed over only while it is the one
        // on track (bug hunt 17). Picked but not started - before the first
        // race, or while On Air opens it - it is the race the driver's phone
        // shows as next, and a "Skip me" used to take them out of the race
        // after it instead.
        var onTrack = ActiveRoundStillToRun(_race.State, _race.IsDemoMode, _race.IsPracticeMode, HasUnfiledRaces)
            ? null
            : _activeRound?.Invoke()?.Id;

        return OrderedProgram()
            .FirstOrDefault(r => !r.IsRecorded
                                 && !string.Equals(r.Id, onTrack, StringComparison.Ordinal)
                                 && SeatOf(r, transponder) is not null);
    }

    /// <summary>
    /// The id <paramref name="transponder"/>'s driver is on
    /// <paramref name="race"/>'s grid under, or null when they aren't on it.
    /// </summary>
    private string? SeatOf(RoundDefinition race, string transponder) =>
        SeatOn(race.AssignedTransponders, transponder, _profiles.PrimaryForAlternate);

    /// <summary>
    /// The grid entry that is <paramref name="transponder"/> itself or,
    /// failing that, another of the same driver's transponders (bug hunt 19):
    /// a driver seated on their second car's transponder is still in the
    /// race, and their phone, which asks by the main one, must find them.
    /// <paramref name="mainOf"/> is ProfileStore.PrimaryForAlternate.
    /// </summary>
    internal static string? SeatOn(IReadOnlyCollection<string> grid, string transponder, Func<string, string?> mainOf)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return null;
        }

        var id = transponder.Trim();
        var main = mainOf(id) ?? id;

        return grid.FirstOrDefault(t => string.Equals(t?.Trim(), id, StringComparison.OrdinalIgnoreCase))
               ?? grid.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)
                                           && string.Equals(mainOf(t.Trim()) ?? t.Trim(), main, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether the round in the Round box, not recorded, is a race still to
    /// run rather than the one on track: nothing staged or racing (a demo on
    /// it is only a rehearsal, and open practice before the first race runs
    /// on the same clock without being this round at all), and no finished
    /// race still waiting to be filed - that one may be this round's, run
    /// and not linked to it yet. The same test of "on track" as the phones'
    /// list (UpcomingRaces), so "Skip me" takes the driver out of the race
    /// their phone showed.
    /// </summary>
    internal static bool ActiveRoundStillToRun(RaceState state, bool demo, bool practice, bool raceWaitingToBeFiled) =>
        !raceWaitingToBeFiled && (demo || practice || state is RaceState.Idle or RaceState.Finished);

    /// <summary>
    /// The grid a driver was skipped from, by "round id|transponder", so the
    /// Next Up bar's "Put back" can give them their slot again. Not saved:
    /// after a restart they go to the back of the grid.
    /// </summary>
    private readonly Dictionary<string, List<string>> _gridBeforeSkip = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Takes the driver out of their next race, scored DNS, with one undo.
    /// Returns the race, or null when they are in none still to run.
    /// <paramref name="inRace"/> is the race the desk's menu named; without
    /// one it is the driver's own next race.
    /// </summary>
    internal RoundDefinition? SkipDriverDns(string transponder, string name, string why, RoundDefinition? inRace = null)
    {
        if ((inRace ?? NextRaceOf(transponder)) is not { } race)
        {
            return null;
        }

        // The id they are on this grid under - their second car's, it may
        // be, when the phone asked by their main one (bug hunt 19).
        transponder = SeatOf(race, transponder) ?? transponder;

        if (SkipDns.Skip(race, transponder) is not { } undo)
        {
            return null;
        }

        _rounds?.Update(race);

        var roundId = race.Id;
        _gridBeforeSkip[$"{roundId}|{transponder}"] = undo.GridBefore;

        // WHY (23 Nov 2026, bug hunt 24): each skip remembered the grid as it
        // was at that skip, without the drivers already skipped from it. Skip
        // A then B (A on pole, B second), put A back then B back: B's grid
        // had nobody ahead of them, so B went in front of A. Now the drivers
        // still skipped from this race go back into the remembered grid in
        // their own slots, so a later put-back finds them ahead.
        var earlier = (race.SkippedDns ?? new List<string>())
            .Where(t => !string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase))
            .Select(t => (t, _gridBeforeSkip.TryGetValue($"{roundId}|{t}", out var g) ? (IReadOnlyList<string>?)g : null))
            .ToList();

        if (earlier.Count > 0)
        {
            _gridBeforeSkip[$"{roundId}|{transponder}"] = GridWithEarlierSkips(undo.GridBefore, earlier);
        }

        _undo.Capture($"Skip {name} in {race.RoundLabel} (DNS)", () =>
        {
            var current = _rounds?.Get(roundId) ?? race;
            var result = current.IsRecorded ? _sessionHistory.Get(current.LinkedSessionId!) : null;
            SkipDns.PutBack(current, transponder, undo, result);
            _rounds?.Update(current);
            _gridBeforeSkip.Remove($"{roundId}|{transponder}");

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
            if (SelectedTransponder() is { Length: > 0 } transponder && NextRound() is { } next)
            {
                SkipFromDesk(transponder, next);
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

    /// <summary>
    /// The desk's skip, said in the console like the phone's. From
    /// <paramref name="next"/>, the race its menu named - not whichever race
    /// the driver is in first, which can be the one in the Round box.
    /// </summary>
    private void SkipFromDesk(string transponder, RoundDefinition next)
    {
        var profile = _profiles.Get(transponder);
        var name = string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;

        if (next.IsRecorded || SkipDriverDns(transponder, name, "skipped at the desk", next) is null)
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
                item.Click += (_, _) => SkipFromDesk(who, next);
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

        _gridBeforeSkip.Remove($"{race.Id}|{transponder}", out var gridBefore);

        if (!race.AssignedTransponders.Contains(transponder, StringComparer.OrdinalIgnoreCase))
        {
            race.AssignedTransponders = BackInTheirSlot(race.AssignedTransponders, gridBefore, transponder);
        }

        _rounds?.Update(race);
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [program] {SpokenNameOf(transponder)} is back in {race.RoundLabel} - no DNS.");
        RefreshNextUpAfterSkip();
    }

    /// <summary>
    /// The grid a driver was skipped from, with the drivers still skipped
    /// from the same race before them put back in it in their own slots
    /// (bug hunt 24), so putting back in any order keeps the grid's order.
    /// </summary>
    internal static List<string> GridWithEarlierSkips(IReadOnlyList<string> gridBefore,
        IEnumerable<(string Transponder, IReadOnlyList<string>? GridBefore)> stillSkipped)
    {
        var grid = gridBefore.ToList();

        // In skip order, each by the grid they were skipped from. One with
        // none remembered (a restart since) is left out rather than guessed.
        foreach (var (them, theirs) in stillSkipped)
        {
            if (theirs is null || grid.Contains(them, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            grid = BackInTheirSlot(grid, theirs, them);
        }

        return grid;
    }

    /// <summary>
    /// The grid with a skipped driver put back where they were (bug hunt 17:
    /// they used to go on the end, so the top qualifier put back in the
    /// A-Main was gridded last). <paramref name="gridBefore"/> is the grid
    /// they were skipped from: they go straight after the nearest driver who
    /// was ahead of them then and is still on the grid, or to the front when
    /// none is - so it holds up when the grid has been edited since. Without
    /// it (a restart since the skip) they go on the end, as before.
    /// </summary>
    internal static List<string> BackInTheirSlot(IReadOnlyList<string> grid, IReadOnlyList<string>? gridBefore, string transponder)
    {
        var back = grid.ToList();
        var was = gridBefore?.ToList().FindIndex(t => string.Equals(t, transponder, StringComparison.OrdinalIgnoreCase)) ?? -1;

        if (was < 0)
        {
            back.Add(transponder);
            return back;
        }

        var slot = 0;

        for (var ahead = was - 1; ahead >= 0; ahead--)
        {
            var at = back.FindIndex(t => string.Equals(t, gridBefore![ahead], StringComparison.OrdinalIgnoreCase));

            if (at >= 0)
            {
                slot = at + 1;
                break;
            }
        }

        back.Insert(slot, transponder);
        return back;
    }
}
