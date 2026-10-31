namespace HeatCaller;

/// <summary>
/// The lap breakdown windows (30 Oct 2026, asked for by Asher: several open
/// at once, as windows that can be dragged around - see LapDetailWindow).
///
/// ONE PER DRIVER. Asked for again for a driver whose window is open, the
/// double-click and the drawer's Lap breakdown button close it, as they did
/// the one popup (21 Oct 2026); the right-click menu's Lap breakdown brings
/// it to the front instead.
///
/// WHERE THEY OPEN. Each takes the lowest spot number free, and a spot
/// remembers where its window was last left, and how tall (AppSettings.
/// LapWindowSpots) - so the first window open goes back where the first one
/// was, on whichever monitor. A spot with nothing remembered, or remembered
/// on a monitor no longer there, opens under the driver's row like the
/// popup did, or a step down and right of the last window opened. Never on
/// the monitor the Big Screen is running full screen on.
///
/// A CLEAN SCREEN EACH HEAT (30 Oct 2026, Asher: "Each heat clean screen").
/// The next race being called - staging, or a start straight from the
/// green for practice and Quick Race - closes every lap window. Resetting
/// or finishing a race does not: the laps are still being read then, and
/// the windows say so when their driver has gone from the standings.
/// </summary>
public sealed partial class RaceControlControl
{
    /// <summary>The open lap windows, in the order they were opened.</summary>
    private readonly List<LapDetailWindow> _lapWindows = new();

    /// <summary>
    /// The race state the lap windows last saw - see CloseLapWindowsForNextRace.
    /// Idle to begin with, so a race called before any lap window has seen
    /// a state still counts as the next one.
    /// </summary>
    private RaceState _lapWindowsLastState = RaceState.Idle;

    private LapDetailWindow? LapWindowFor(string transponder) =>
        _lapWindows.FirstOrDefault(w => !w.IsDisposed && string.Equals(w.Transponder, transponder, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The lap breakdown as a toggle (21 Oct 2026): open for this driver
    /// already, it closes; otherwise it opens. The double-click on a driver
    /// and the drawer's Lap breakdown button.
    /// </summary>
    private void ToggleLapWindow(string transponder, int rowIndex)
    {
        if (LapWindowFor(transponder) is { } open)
        {
            open.Close();
            return;
        }

        OpenLapWindow(transponder, rowIndex);
    }

    /// <summary>
    /// Opens a driver's lap window - or, when one is open for them already,
    /// brings it to the front rather than opening a second.
    /// </summary>
    private void OpenLapWindow(string transponder, int rowIndex)
    {
        if (LapWindowFor(transponder) is { } open)
        {
            if (!open.Visible)
            {
                open.Show(FindForm());
            }

            open.BringToFront();
            return;
        }

        var slot = LapWindowPlacement.FreeSlot(_lapWindows.Select(w => w.Slot));
        var saved = _settings.LapWindowSpots.FirstOrDefault(s => s.Slot == slot);

        var window = new LapDetailWindow(transponder, DriverNameFor(transponder), LapWindowSubtitle(transponder),
            _race.StreakThreshold, slot, saved?.Rows ?? LapDetailWindow.DefaultRows, RaceKeyFromBroadcastWindow);

        window.Bounds = PlaceLapWindow(window, saved, rowIndex);

        window.Placed += (_, _) => RememberLapWindow(window);

        window.FormClosed += (_, _) =>
        {
            _lapWindows.Remove(window);

            // Closed by its X, Escape, Close all, the next heat or the page
            // going - remembered in every case, so the spot is there next
            // time. Shown with Show(), so closing it also disposes it.
            RememberLapWindow(window);
        };

        _lapWindows.Add(window);

        RefreshLapWindows(_race.GetOrderedStandings());

        // Owned by the main window: above it, minimised with it, closed
        // with it.
        if (FindForm() is { } owner)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }

    /// <summary>Whatever of the car, number and class is filled in, and the transponder - the card's second line.</summary>
    private string LapWindowSubtitle(string transponder)
    {
        var profile = _profiles.Get(transponder);

        // An empty profile should not produce a line of stray separators.
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile?.RaceNumber))
        {
            parts.Add($"#{profile!.RaceNumber.Trim()}");
        }

        // The car this row's transponder is fitted to, or their active car.
        if (_profiles.CarModelFor(transponder) is { Length: > 0 } car && car.Trim().Length > 0)
        {
            parts.Add(car.Trim());
        }

        if (!string.IsNullOrWhiteSpace(profile?.ClassName))
        {
            parts.Add(profile!.ClassName.Trim());
        }

        parts.Add(transponder);

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// Where a new lap window goes: its spot's remembered place when that
    /// is still on a screen; otherwise a step on from the last window
    /// opened, or under the driver's row for the first.
    /// </summary>
    private Rectangle PlaceLapWindow(LapDetailWindow window, LapWindowSpot? saved, int rowIndex)
    {
        var screens = LapWindowScreens();

        if (saved is not null && LapWindowPlacement.Restore(new Point(saved.X, saved.Y), window.Size, screens) is { } restored)
        {
            return restored;
        }

        var last = _lapWindows.LastOrDefault(w => !w.IsDisposed && w.Visible);

        var location = last is not null
            ? LapWindowPlacement.Cascade(last.Location, UiLayout.AtTextSize(28))
            : UnderDriverRow(window.Transponder, rowIndex);

        return LapWindowPlacement.Fit(new Rectangle(location, window.Size), screens);
    }

    /// <summary>
    /// Under the driver's own row, left-aligned with the grid, on the
    /// screen. Falls back to the top of the grid when the row can't be
    /// located - which can happen if the board rebuilt between the click
    /// and here.
    /// </summary>
    private Point UnderDriverRow(string transponder, int rowIndex)
    {
        var y = 0;

        try
        {
            if (rowIndex >= 0 && rowIndex < _dgvLeaderboard.Rows.Count)
            {
                // Under the driver's corrections drawer when it is open, not
                // over it (21 Oct 2026) - laid over the drawer, it covered
                // the very buttons that open and close it.
                if (rowIndex + 1 < _dgvLeaderboard.Rows.Count
                    && IsDrawerRow(rowIndex + 1)
                    && string.Equals(TransponderForRow(rowIndex + 1), transponder, StringComparison.OrdinalIgnoreCase))
                {
                    rowIndex++;
                }

                y = _dgvLeaderboard.GetRowDisplayRectangle(rowIndex, false).Bottom;
            }
        }
        catch
        {
            // Row scrolled out from under us - the fallback position is fine.
        }

        return _dgvLeaderboard.PointToScreen(new Point(0, y));
    }

    /// <summary>
    /// The screens a lap window may go on, as their working areas: all of
    /// them but the one the Big Screen covers, when it is open full screen
    /// - unless that is the only screen there is.
    /// </summary>
    private List<Rectangle> LapWindowScreens()
    {
        var all = Screen.AllScreens;
        Rectangle? bigScreen = null;

        try
        {
            if (_bigScreen is { IsDisposed: false, Visible: true } big
                && big.WindowState != FormWindowState.Minimized
                && Screen.FromControl(big) is { } covered
                && (big.WindowState == FormWindowState.Maximized || big.Bounds == covered.Bounds))
            {
                bigScreen = covered.Bounds;
            }
        }
        catch
        {
            // The Big Screen closing as it was asked - every screen will do.
        }

        var usable = all.Where(s => bigScreen is null || s.Bounds != bigScreen.Value).Select(s => s.WorkingArea).ToList();

        return usable.Count > 0 ? usable : all.Select(s => s.WorkingArea).ToList();
    }

    /// <summary>Keeps where a window's spot was left, and how tall, for the next one opened in it.</summary>
    private void RememberLapWindow(LapDetailWindow window)
    {
        if (window.WindowState != FormWindowState.Normal)
        {
            return;
        }

        var spot = _settings.LapWindowSpots.FirstOrDefault(s => s.Slot == window.Slot);

        if (spot is null)
        {
            spot = new LapWindowSpot { Slot = window.Slot };
            _settings.LapWindowSpots.Add(spot);
        }

        if (spot.X == window.Left && spot.Y == window.Top && spot.Rows == window.Rows)
        {
            return;
        }

        spot.X = window.Left;
        spot.Y = window.Top;
        spot.Rows = window.Rows;

        _settings.Save();
    }

    /// <summary>
    /// Feeds every open lap window its driver's current standing on every
    /// board refresh, so they update lap by lap instead of freezing on
    /// whatever was true when they were opened.
    /// </summary>
    private void RefreshLapWindows(IReadOnlyList<RacerStanding> ordered)
    {
        if (_lapWindows.Count == 0)
        {
            return;
        }

        foreach (var window in _lapWindows.ToList())
        {
            if (window.IsDisposed)
            {
                _lapWindows.Remove(window);
                continue;
            }

            RacerStanding? standing = null;
            var position = 0;

            for (var i = 0; i < ordered.Count; i++)
            {
                if (string.Equals(ordered[i].TransponderId, window.Transponder, StringComparison.OrdinalIgnoreCase))
                {
                    standing = ordered[i];
                    position = i + 1;
                    break;
                }
            }

            window.Apply(standing, position);
        }
    }

    /// <summary>Closes every lap window - Close all, the next heat, the page going.</summary>
    private void CloseLapWindows()
    {
        foreach (var window in _lapWindows.ToList())
        {
            if (!window.IsDisposed)
            {
                window.Close();
            }
        }

        _lapWindows.Clear();
    }

    /// <summary>
    /// Each heat on a clean screen: every lap window closes when the next
    /// race is called - the session moving from Idle or Finished into
    /// Staging or Countdown, or straight into Racing on a fresh green
    /// (practice, Quick Race). Moving on within a race - staging to
    /// countdown to green, a caution and back - leaves them open, so
    /// windows opened on the grid stay for the race.
    /// </summary>
    private void CloseLapWindowsForNextRace(RaceState state)
    {
        var previous = _lapWindowsLastState;
        _lapWindowsLastState = state;

        if (previous is not (RaceState.Idle or RaceState.Finished))
        {
            return;
        }

        if (state is RaceState.Staging or RaceState.Countdown
            || (state == RaceState.Racing && _race.GreenStartedTheRace))
        {
            CloseLapWindows();
        }
    }

    /// <summary>
    /// Line up: every open lap window in columns down the screen most of
    /// them are on, in the running order, each keeping its height.
    /// </summary>
    private void LineUpLapWindows()
    {
        var open = _lapWindows.Where(w => !w.IsDisposed && w.Visible).ToList();

        if (open.Count == 0)
        {
            return;
        }

        var order = _race.GetOrderedStandings()
            .Select((s, i) => (s.TransponderId, i))
            .ToDictionary(p => p.TransponderId, p => p.i, StringComparer.OrdinalIgnoreCase);

        open = open
            .OrderBy(w => order.TryGetValue(w.Transponder, out var place) ? place : int.MaxValue)
            .ThenBy(w => _lapWindows.IndexOf(w))
            .ToList();

        var screens = LapWindowScreens();

        // The screen most of them are on.
        var screen = screens
            .OrderByDescending(s => open.Count(w => LapWindowPlacement.ScreenFor(w.Bounds, screens) == s))
            .First();

        var placed = LapWindowPlacement.LineUp(open.Select(w => w.Size).ToList(), screen, UiLayout.AtTextSize(10));

        for (var i = 0; i < open.Count; i++)
        {
            open[i].Bounds = placed[i];
            open[i].BringToFront();
            RememberLapWindow(open[i]);
        }
    }
}
