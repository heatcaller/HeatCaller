namespace HeatCaller;

/// <summary>
/// COACHING A DRIVER (22 Oct 2026), from the board's right-click: Coach
/// (name), with
///
///   At the split            - up or down on their best at the split (the split coach, RaceControlControl.Splits)
///   Target lap...           - each lap against a target you set: "Two tenths off"
///   Consistency             - every few clean laps, how far apart they were
///   Tones instead of words  - a short tone in place of the target-lap and split words; the
///                             consistency line is still spoken, since a tone can't say a spread
///   Stop coaching (name)
///
/// ONE DRIVER AT A TIME, for everything that speaks or sounds. None of
/// these lines says whose lap it was - "Two tenths off", a high note - and
/// two coached drivers would hear each other's coaching with no way to tell
/// it apart. So coaching another driver moves it to them, as the split
/// coach always has. The TARGET LAP is the exception: it is the driver's
/// for the night (DriverTargets), so coaching moves away and back without
/// anyone typing a target twice, and the phone's coach page can show every
/// driver's.
///
/// The coaching ends where the split coach's always did (EndSplitCoaching):
/// at Reset, when the race it was picked for is filed, or at Stop coaching
/// - and when the last option is unticked.
///
/// The words are the club's lines (Target lap (coach), Consistency (coach)
/// on Voice - Announcements), on in practice and off in races until a club
/// ticks them. The tones are the desk's choice for that driver, not an
/// announcement, so only the mute stops them.
/// </summary>
public partial class RaceControlControl
{
    /// <summary>What the coached driver is being coached on.</summary>
    private sealed class CoachOptions
    {
        public CoachOptions(string transponder) => Transponder = transponder;

        public string Transponder { get; }

        /// <summary>Up or down at the split (SplitDelta).</summary>
        public bool AtSplit { get; set; }

        /// <summary>The consistency block's line (ConsistencyCoach).</summary>
        public bool Consistency { get; set; }

        /// <summary>A tone in place of the target-lap and split words, at the line and at the split. The consistency line is still spoken - a tone can't say a spread.</summary>
        public bool Tones { get; set; }

        /// <summary>The gap to their own-best ghost on track, at the line (GhostGap, 22 Oct 2026).</summary>
        public bool GhostGap { get; set; }

        /// <summary>Clean laps since the last consistency line.</summary>
        public int CleanLapsSinceConsistency { get; set; }

        /// <summary>The spread the last consistency line said, for "tighter than the five before".</summary>
        public long? LastSpreadMs { get; set; }

        /// <summary>Whether anything is left ticked - a target counts, since it is coached at the line.</summary>
        public bool AnythingTicked => AtSplit || Consistency || Tones || GhostGap || DriverTargets.TargetMs(Transponder) is not null;
    }

    /// <summary>The driver being coached, and on what - or null.</summary>
    private CoachOptions? _coach;

    /// <summary>The driver coached at the split, or null - the split coach's own question (CallSplitCoach).</summary>
    private string? _coachAtSplit => _coach is { AtSplit: true } coach ? coach.Transponder : null;

    /// <summary>Whether this driver is the one being coached, on anything.</summary>
    private bool IsCoached(string transponder) =>
        _coach is { } coach && string.Equals(coach.Transponder, transponder, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The coaching for this driver - started when there is none, and moved
    /// to them, with a console line, when another driver was being coached.
    /// </summary>
    private CoachOptions CoachFor(string transponder)
    {
        if (_coach is { } coach && string.Equals(coach.Transponder, transponder, StringComparison.OrdinalIgnoreCase))
        {
            return coach;
        }

        if (_coach is { } before)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [coach] Stopped coaching {CoachNameOf(before.Transponder)} - coaching one driver at a time, and {CoachNameOf(transponder)} is next.");
        }

        _coach = new CoachOptions(transponder);
        return _coach;
    }

    /// <summary>Ends the coaching once nothing is left ticked - a driver coached on nothing is not being coached.</summary>
    private void EndCoachingIfNothingTicked()
    {
        if (_coach is { AnythingTicked: false })
        {
            EndSplitCoaching("nothing is ticked for them now");
        }
    }

    /// <summary>A driver's name for the console, or their transponder.</summary>
    private string CoachNameOf(string transponder)
    {
        var name = _profiles.Get(transponder)?.DriverName;

        return string.IsNullOrWhiteSpace(name) ? transponder : name;
    }

    /// <summary>
    /// The coached driver finished a lap: a tone, or their pace against the
    /// target in words, and every block of clean laps the consistency line.
    /// Called for every counted lap (OnLapProcessed); returns at once for
    /// anyone else.
    /// </summary>
    private void CoachAtTheLine(RacerStanding standing, bool isValid, long thisLapMs)
    {
        if (_coach is not { } coach
            || !string.Equals(coach.Transponder, standing.TransponderId, StringComparison.OrdinalIgnoreCase)
            || !isValid
            || thisLapMs <= 0
            || GhostService.IsGhost(standing.TransponderId))
        {
            return;
        }

        // The best BEFORE this lap: the counted laps less the start lap
        // (index 0, the green to the line) and less this one.
        var timed = standing.LapTimesMs.Skip(1).ToList();
        var earlier = timed.Take(Math.Max(0, timed.Count - 1)).Where(ms => ms > 0).ToList();
        var bestBefore = earlier.Count > 0 ? earlier.Min() : 0;
        var target = DriverTargets.TargetMs(standing.TransponderId);
        var reference = bestBefore > 0 ? bestBefore : target ?? 0;
        var clean = CoachingWords.IsCleanLap(thisLapMs, reference);

        if (coach.Tones)
        {
            if (CoachingWords.ToneFor(thisLapMs, bestBefore, target) is { } tone)
            {
                _announcer.PlayCoachTone(tone);
            }
        }
        else if (target is { } targetMs && clean && _announcer.WillCall(AnnouncementEventType.TargetPace))
        {
            _announcer.NotifyTargetPace(ForAnnouncer(standing), thisLapMs, targetMs);
        }

        // THE GAP TO THEIR GHOST (22 Oct 2026): where their own-best ghost is
        // on the track as they cross the line - "Two tenths behind your
        // ghost". Every lap, clean or not: the gap on the track is true after
        // a spin too, and that is when it is worth hearing.
        if (coach.GhostGap
            && GhostOf(standing.TransponderId) is { } ghost
            && OwnBestGhost.GapToGhostMs(
                SinceGreenAtLastCrossingMs(standing, _race.StartType, _race.StaggerClockMode),
                SinceGreenAtLastCrossingMs(ghost, _race.StartType, _race.StaggerClockMode),
                GhostLapMs(ghost)) is { } gapMs
            && _announcer.WillCall(AnnouncementEventType.GhostGap))
        {
            _announcer.NotifyGhostGap(ForAnnouncer(standing), gapMs);
        }

        if (!coach.Consistency || !clean)
        {
            return;
        }

        coach.CleanLapsSinceConsistency++;
        var block = _announcer.ConsistencyBlockLaps;

        if (coach.CleanLapsSinceConsistency < block)
        {
            return;
        }

        var laps = CoachingWords.CleanLaps(timed);

        if (laps.Count < block)
        {
            return;
        }

        var spread = CoachingWords.Spread(laps.Skip(laps.Count - block).ToList());
        var before = coach.LastSpreadMs;

        coach.CleanLapsSinceConsistency = 0;
        coach.LastSpreadMs = spread;

        if (_announcer.WillCall(AnnouncementEventType.ConsistencyCoach))
        {
            _announcer.NotifyConsistency(ForAnnouncer(standing), block, spread, before);
        }
    }

    /// <summary>
    /// Puts on (or brings up to date) a driver's own-best ghost and says
    /// what it did - MainForm's, which owns the ghosts. Null hides the
    /// Coach menu's "Chase ... best (ghost)" (22 Oct 2026).
    /// </summary>
    internal Func<string, string?>? ChaseOwnBest { get; set; }

    /// <summary>
    /// Fills the board's Coach (name) menu for the driver under the pointer
    /// - called each time the right-click menu opens. Built here, beside the
    /// coaching it drives, rather than in the menu's long list.
    /// </summary>
    private void FillCoachItems(ToolStripMenuItem coachMenu, string transponder, string name)
    {
        // The last opening's items go - disposed, so a menu opened all
        // night does not keep every one it ever filled.
        foreach (var old in coachMenu.DropDownItems.Cast<ToolStripItem>().ToList())
        {
            old.Dispose();
        }

        coachMenu.DropDownItems.Clear();
        coachMenu.Text = IsCoached(transponder) ? $"Coaching {name}" : $"Coach {name}";

        var coached = IsCoached(transponder) ? _coach : null;
        var target = DriverTargets.TargetMs(transponder);

        // At the split only with a split loop - there is nothing to say
        // there without one.
        if (_race.SplitCount > 0)
        {
            var atSplit = new ToolStripMenuItem("At the split") { Checked = IsCoachedAtSplit(transponder) };
            atSplit.Click += (_, _) => ToggleSplitCoach(transponder, name);
            coachMenu.DropDownItems.Add(atSplit);
        }

        var targetLap = new ToolStripMenuItem(target is { } ms ? $"Target lap ({Sectors.Format(ms)}s)..." : "Target lap...")
        {
            Checked = target is not null,
        };
        targetLap.Click += (_, _) => SetTargetLap(transponder, name);
        coachMenu.DropDownItems.Add(targetLap);

        if (target is not null)
        {
            var clearTarget = new ToolStripMenuItem("Clear target");
            clearTarget.Click += (_, _) => ClearTargetLap(transponder, name);
            coachMenu.DropDownItems.Add(clearTarget);
        }

        var consistency = new ToolStripMenuItem("Consistency") { Checked = coached?.Consistency == true };
        consistency.Click += (_, _) => ToggleConsistency(transponder, name);
        coachMenu.DropDownItems.Add(consistency);

        var tones = new ToolStripMenuItem("Tones instead of words") { Checked = coached?.Tones == true };
        tones.Click += (_, _) => ToggleTones(transponder, name);
        coachMenu.DropDownItems.Add(tones);

        // The gap to their ghost, once they have one on track.
        if (GhostOf(transponder) is not null)
        {
            var ghostGap = new ToolStripMenuItem("Gap to their ghost") { Checked = coached?.GhostGap == true };
            ghostGap.Click += (_, _) => ToggleGhostGap(transponder, name);
            coachMenu.DropDownItems.Add(ghostGap);
        }

        // CHASE THEIR BEST (22 Oct 2026): the own-best ghost the Practice
        // page's "Chase a Best Lap..." puts on, from here too - the board is
        // where the driver is being coached, and asking again brings the
        // ghost up to their new best ("now beat that"). See OwnBestGhost.
        if (ChaseOwnBest is { } chaseOwnBest)
        {
            var chase = new ToolStripMenuItem($"Chase {name}'s best (ghost)");
            chase.Click += (_, _) =>
            {
                if (chaseOwnBest(transponder) is { Length: > 0 } said)
                {
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [coach] {said}");
                }
            };
            coachMenu.DropDownItems.Add(chase);
        }

        if (coached is not null)
        {
            coachMenu.DropDownItems.Add(new ToolStripSeparator());

            var stop = new ToolStripMenuItem($"Stop coaching {name}");
            stop.Click += (_, _) => EndSplitCoaching("stopped from the board");
            coachMenu.DropDownItems.Add(stop);
        }
    }

    /// <summary>Asks for a target lap for this driver - prefilled with the one they have, or their best - and coaches them to it.</summary>
    private void SetTargetLap(string transponder, string name)
    {
        var standing = _race.GetOrderedStandings().FirstOrDefault(s =>
            string.Equals(s.TransponderId, transponder, StringComparison.OrdinalIgnoreCase));

        var start = DriverTargets.TargetMs(transponder)
                    ?? (standing is { FastestLapMs: > 0 } ? standing.FastestLapMs : 10_000);

        using var dialog = new TargetLapForm(name, start);

        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.TargetMs is not { } targetMs || !DriverTargets.Set(transponder, targetMs))
        {
            return;
        }

        CoachFor(transponder);

        var why = _announcer.WillCall(AnnouncementEventType.TargetPace) || _coach?.Tones == true
            ? string.Empty
            : $" \"Target lap (coach)\" is switched off for {(_race.IsPracticeMode ? "practice" : "races")} on Voice - Announcements, so nothing will be said until it is ticked.";

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [coach] Coaching {name} to a {Sectors.Format(targetMs)}s lap: each lap against it at the line.{why}");
    }

    private void ClearTargetLap(string transponder, string name)
    {
        if (DriverTargets.Clear(transponder))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [coach] Cleared {name}'s target lap.");
        }

        EndCoachingIfNothingTicked();
    }

    /// <summary>
    /// Ghost transponder to the driver it chases, for every own-best ghost
    /// switched on - MainForm's, which owns the ghosts (22 Oct 2026). Used to
    /// put each ghost under its driver on the practice board and for the
    /// gap to the ghost. Null: no ghosts are known here.
    /// </summary>
    internal Func<IReadOnlyDictionary<string, string>>? OwnBestGhosts { get; set; }

    /// <summary>This driver's own-best ghost's row on the board, or null when it isn't running.</summary>
    private RacerStanding? GhostOf(string transponder)
    {
        if (OwnBestGhosts?.Invoke() is not { Count: > 0 } chasedBy)
        {
            return null;
        }

        return _race.GetOrderedStandings().FirstOrDefault(s =>
            chasedBy.TryGetValue(s.TransponderId, out var driver)
            && string.Equals(driver, transponder, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A car's last crossing as ms since the green - the one clock both the
    /// driver and their ghost really share (22 Oct 2026).
    /// </summary>
    // WHY (22 Oct 2026): the gap to the ghost was taken from the two cars'
    // ElapsedMs, which are NOT on one clock. A driver's ElapsedMs has their
    // penalties added (a 2 s jump start moved every call 2 s), a qualifier's
    // clock starts at each car's own first crossing (the ghost's included),
    // and with OwnRelease a real car's clock starts at its release while a
    // ghost's runs from the green. So "Two tenths behind your ghost" could
    // be said with the ghost two seconds up the road. Now each car's elapsed
    // has its penalties taken off and its own zero put back, so both are
    // measured from the green.
    internal static long SinceGreenAtLastCrossingMs(RacerStanding s, RaceStartType startType, StaggerClock staggerClock)
    {
        var zeroMs = startType == RaceStartType.Qualifier
            ? Math.Max(0, s.QualifyingZeroMs)
            : staggerClock == StaggerClock.OwnRelease && !GhostService.IsGhost(s.TransponderId)
                ? s.ReleaseOffsetMs
                : 0;

        return s.ElapsedMs - Math.Max(0, s.PenaltyMs) + zeroMs;
    }

    /// <summary>The ghost's lap time: its last lap, or its best before it has a last one.</summary>
    private static long GhostLapMs(RacerStanding ghost) =>
        ghost.LapTimesMs.Count > 1 && ghost.LapTimesMs[^1] > 0 ? ghost.LapTimesMs[^1] : ghost.FastestLapMs;

    private void ToggleGhostGap(string transponder, string name)
    {
        if (IsCoached(transponder) && _coach!.GhostGap)
        {
            _coach!.GhostGap = false;
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [coach] Stopped saying {name}'s gap to their ghost.");
            EndCoachingIfNothingTicked();
            return;
        }

        CoachFor(transponder).GhostGap = true;

        var why = _announcer.WillCall(AnnouncementEventType.GhostGap)
            ? string.Empty
            : $" \"Gap to their ghost (coach)\" is switched off for {(_race.IsPracticeMode ? "practice" : "races")} on Voice - Announcements, so nothing will be said until it is ticked.";

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [coach] Coaching {name} against their ghost: at the line, how far behind or ahead of it they are.{why}");
    }

    private void ToggleConsistency(string transponder, string name)
    {
        if (IsCoached(transponder) && _coach!.Consistency)
        {
            _coach!.Consistency = false;
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [coach] Stopped the consistency coaching for {name}.");
            EndCoachingIfNothingTicked();
            return;
        }

        var coach = CoachFor(transponder);
        coach.Consistency = true;
        coach.CleanLapsSinceConsistency = 0;
        coach.LastSpreadMs = null;

        var why = _announcer.WillCall(AnnouncementEventType.ConsistencyCoach)
            ? string.Empty
            : $" \"Consistency (coach)\" is switched off for {(_race.IsPracticeMode ? "practice" : "races")} on Voice - Announcements, so nothing will be said until it is ticked.";

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [coach] Coaching {name} on consistency: every {_announcer.ConsistencyBlockLaps} clean laps, how far apart they were.{why}");
    }

    private void ToggleTones(string transponder, string name)
    {
        if (IsCoached(transponder) && _coach!.Tones)
        {
            _coach!.Tones = false;
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [coach] {name} hears words again, not tones.");
            EndCoachingIfNothingTicked();
            return;
        }

        CoachFor(transponder).Tones = true;
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [coach] {name} hears tones instead of words at the line and the split: high is quicker, low is slower, two rising notes a new best. The consistency line is still spoken.");
    }

    /// <summary>
    /// The window Coach (name) &gt; Target lap... opens: the lap time to
    /// coach a driver to, in seconds to the hundredth. A class of its own,
    /// and internal, so the layout test can build it (WindowCatalog) - laid
    /// out as the Penalty... window beside it is (TimePenaltyForm).
    /// </summary>
    internal sealed class TargetLapForm : Form
    {
        /// <summary>How wide the window is inside its frame at the normal text size - the penalty window's.</summary>
        private const int DialogWidth = 360;

        /// <summary>The seconds box's width at the normal text size - wider if its widest value needs more.</summary>
        private const int SecondsWidth = 90;

        private readonly NumericUpDown _seconds = new()
        {
            DecimalPlaces = 2,
            Increment = 0.05m,
            Minimum = 1m,
            Maximum = 600m,
        };

        /// <summary>The target chosen, in milliseconds - only meaningful after DialogResult.OK.</summary>
        public long? TargetMs => CoachingWords.ParseTarget(_seconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public TargetLapForm(string name, long startMs)
        {
            Text = $"Target lap for {name}";
            BackColor = AppTheme.Background;
            Font = AppTheme.BodyFont;

            _seconds.Value = Math.Clamp(Math.Round(startMs / 1000m, 2), _seconds.Minimum, _seconds.Maximum);

            var ok = new Button { Text = "Coach", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

            var row = new FieldRow();
            row.Add(new Label { Text = "Target lap (seconds):" }, _seconds);

            var page = new FillPage();
            page.Add(row);
            page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
            ok.TabIndex = 0;
            cancel.TabIndex = 1;

            Controls.Add(page);

            AcceptButton = ok;
            CancelButton = cancel;

            AppTheme.ApplyRecursively(this);

            // After the theme pass, in the face it leaves the box in - as the
            // penalty window sizes its own.
            var widest = _seconds.Maximum.ToString("F" + _seconds.DecimalPlaces, System.Globalization.CultureInfo.CurrentCulture);

            _seconds.Width = Math.Max(
                UiLayout.AtTextSize(SecondsWidth),
                TextRenderer.MeasureText(widest, _seconds.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);

            DialogFit.Keep(this, page, width: DialogWidth);
        }
    }
}
