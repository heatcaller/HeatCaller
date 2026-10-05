# Corrections and recovery

No decoder is perfect, and no laptop is either. This page covers fixing the count during a race (missed passes, double reads, on-track penalties) and getting a race back when something goes wrong: an accidental reset, a crash, the window being closed, or the decoder dropping out.

## Correcting laps and penalties

All corrections apply to the driver selected on the **Race Control** leaderboard. There are three ways to reach them:

- **The toolbar** under the leaderboard: **+1 Lap**, **-1 Lap**, **+5s Penalty**, **+10s Penalty**, **Clear Penalties**. Click a driver's row first. If nobody is selected you'll see "Select a racer in the leaderboard first."
- **Right-click** a driver's row: **Add a lap (+1)**, **Remove a lap (-1)**, **Penalty +5s**, **Penalty +10s**, **Penalty...**, **Clear penalty**. The menu always applies to the row you right-clicked, not the row that was selected before.
- **The keyboard**, with a driver selected: <kbd>+</kbd> and <kbd>-</kbd> for laps, <kbd>P</kbd> and <kbd>Shift</kbd>+<kbd>P</kbd> for 5 and 10-second penalties, <kbd>C</kbd> to clear penalties. See [Keyboard shortcuts](../reference/keyboard-shortcuts.md#the-selected-driver).

A correction updates the board and the announcer's running order straight away. The announcer doesn't treat it as a car crossing the line, so there's no lap call or buzzer.

<!-- screenshot: leaderboard right-click menu with lap and penalty items -->

### Adding a lap

Use **+1 Lap** when the decoder missed a pass. The lap count goes up by one. A lap added by hand has no lap time, so it doesn't change best laps, averages or the lap chart.

If the lap you add brings the driver to the race's lap limit, the checkered flag comes out just as if they had crossed the line. With **Leader takes the flag, others finish their lap** ticked, that driver has finished and everyone else finishes the next time they cross. With it unticked, the race ends. A lap added under a red flag doesn't bring the checkered flag out while the field is stopped. It comes out when you press **Green (Start)** to restart, in place of the green, and that driver has finished. With the box unticked, the race ends there.

### Removing a lap

Use **-1 Lap** when a car was counted twice, for example a double read on the line.

- Laps you added by hand are removed first.
- After that, the driver's last lap is merged into the lap before it rather than thrown away. Two crossings a moment apart were really one lap, and adding their times together gives that lap back. The laps still add up to the driver's total time.
- The driver's best lap, their clean run and the fastest lap of the race are then worked out again. If the phantom short "lap" had set the driver's best, or the race's fastest lap, it no longer counts.

If a phantom lap brought the checkered flag out early in a lap race, for example a car carried back across the loop after a crash, use **-1 Lap** on that driver straight away, before they come round again. Their real last lap then counts: the next time they cross, they take the flag. The checkered flag stays out for everyone else. A crossing they make while they're marked as finished isn't counted, even once the lap comes off, so if they've already come round, leave their count alone: the phantom lap stands in for the one they just finished. A driver who took the flag after it came out has finished, so taking a lap off them doesn't give them another lap to run.

The lap count never goes below zero.

### Time penalties

- **+5s Penalty** and **+10s Penalty** add that much to the driver's total time. The board orders drivers on the same lap by total time, so a penalty drops the driver behind anyone they're now slower than.
- **Penalty...** opens the **Time penalty** dialog. Enter any amount in **Seconds to add:** (in half-second steps, from −600 to 600) and press **Apply**. A negative number takes time off.
- The **Penalty** column shows each driver's total penalty. It stays applied for the rest of the race.
- **Clear Penalties** (toolbar) or **Clear penalty** (menu, which shows the current total, for example "Clear penalty (10.0s)") removes all of that driver's penalty time, including any automatic jump-start penalty.

### After the checkered flag

The result is saved to **Race History** the moment the race finishes. If you correct the board after that, the saved result isn't changed. The console says:

> Adjustment after the flag - history not updated. The saved result still has the figures it had at the checkered flag.

The saved result, the championship points and the qualifying figures keep the figures from the flag. Make corrections on the board while the race is still running, or correct the saved result afterwards with **Edit Result** (below).

!!! tip
    The lap-correction buttons aren't on the **Undo** list. To reverse a correction, use its opposite: **-1 Lap** after an extra **+1 Lap**, or **Clear Penalties** for a penalty given in error.

### Correcting a saved result

**Edit Result** on the **Race History** page corrects a race after it has been saved. It only works once **Allow corrections to saved results** is ticked on the **Local Screens** page (it's off by default); until then it says "Correcting saved results is switched off on the laptop." Only a real race can be corrected, not practice or a demo race.

1. Select the race on **Race History** and click **Edit Result**.
2. For each driver, set **Added Laps** (laps added, or taken away with a minus sign), **Time Penalty (s)**, **Lap Penalty** and **Outcome** (**Finished**, **DNF** or **DQ**). **New order** shows the result as it will be.
3. Type a reason in **Reason (required, kept with the result):** and click **Save Correction**.

Corrections are worked out from the race as it was actually run, not from the last correction, so setting everything back to zero and **Finished** gives the original result back. Qualifying, the mains, the championship and the results pages all use the corrected figures. Badges already earned for the race aren't worked out again. **Undo** takes a correction back.

The race-day manager's **Results** tab makes the same correction from a phone. See [Race-day manager](../screens/phone-and-web.md#race-day-manager).

## Reset and undo

**Reset** (in the flag row, or <kbd>F7</kbd>) clears the running race: the standings, the clock, the grid and any queued announcements. It also clears the round and the name on the **Session** row, so the next race starts from a clean slate, and it throws away the crash-recovery copy of the race. <kbd>F7</kbd> asks first if a race is staging, counting down or running; the button doesn't.

Reset doesn't start anything. Press **Stage** when you're ready.

If there was anything to lose, the reset goes onto the **Undo** list as **Reset race**. Hover over the **Undo** button at the bottom of the sidebar to see what it will undo, in order; right-click it to undo several steps at once. HeatCaller keeps the last 25 undoable actions from anywhere in the app.

Undoing a reset puts everything back as it was:

- laps, times and penalties
- the flag and the clock
- a checkered flag that was already out, and who had taken it
- the grid and any staggered releases
- jump-start flags
- the round and the name on the **Session** row.

The flag calls aren't made again, and neither are "last lap" and "one minute to go" if they had already been called. Ghost cars in the race carry on mid-lap, as though they had kept lapping; see [Ghost Cars](practice-and-ghosts.md#ghost-cars).

**Reset** during a demo ends the demo instead, and that can't be undone. Once **Countdown** or a first **Green (Start)** has taken over from a demo, the start on the board is a real race's, so a **Reset** pressed straight after can be undone as usual. While practice is running, **Reset** and **Checkered** are refused: use **Stop** or **Clear Stats** on the **Practice** page. See [Practice](practice-and-ghosts.md#practice).

### Why an undo can be refused

A reset stays on the **Undo** list until newer actions push it off. If you stage, start or finish another race after resetting, undoing the reset would put the old race back over the new one and wipe out the laps the new race counted.

So HeatCaller refuses the undo once a new race has been started (as soon as you've staged it), and nothing is changed. The console shows:

> [undo] FAILED to undo 'Reset race': a new race has been started since the reset, and undoing it now would overwrite that race. Nothing was changed.


### Staging over a running race

Pressing **Stage** or **Countdown** (or <kbd>F1</kbd>/<kbd>F2</kbd>) while a race is running would throw that race away, so HeatCaller asks first, in a **Race in progress** box: the running race would be abandoned, its laps cleared from the board and not saved to **Race History**. Choose **No** and use **Checkered** instead if you want to end the race and keep its result.

Starting a round from **Race Day → Next Steps** or **Start Next** while a race is running asks the same question ("Abandon the running race?") before anything is announced. A stage or countdown already confirmed on the phone remote isn't asked again on the laptop.

If you go ahead by mistake, **Undo** puts the abandoned race back, as long as the new race hasn't gone green yet. When the new race was started from the program or **Start Next**, the undo also puts back the abandoned race's round, track and label. After the green, the undo is refused, for the same reason as undoing a reset.

If the race finishes, is reset or changes in some other way while the question is still open, answering **Yes** does nothing. The log says "the session changed while the question was open". Press the button again if you still want it.

Over a demo, the question is **Demo in progress** instead: "A demo is running. ..." Ending a demo this way doesn't save it to **Race History** (even with the save box ticked), and **Undo** doesn't bring it back. See [Demo Mode](practice-and-ghosts.md#demo-mode).

A **Countdown** confirmed over a running race clears that race's flag, the same as **Stage** does. So if the running race was under a yellow or red flag, the new countdown still runs on to its automatic green (when the start sequence runs automatically), and that green is called as the new race's start. A yellow or red thrown during the new countdown still stops the automatic start, as it does for any start.

## Crash recovery

While a race is running, HeatCaller keeps a copy of it on disk. The copy is written after every crossing (at most a few times a second), and immediately after every flag and every correction. It holds:

- every driver's laps, lap times, penalties and clean runs
- the fastest lap
- the flag and the race clock
- whether the checkered flag was already out, and who had taken it
- the track, the limits and the minimum lap time the race was run under
- the start type, the grid, a staggered start's release schedule and clock, and any jump starts.

The copy is deleted the moment the race is saved to **Race History**, and when you press **Reset**. If it still exists the next time HeatCaller starts, the last race never finished. A copy saved more than 24 hours earlier isn't offered back.

Demo races and practice are never kept. A race that hadn't gone green (it was still staging or counting down), or had no laps yet, isn't offered back, because staging again is quicker. A copy written during staging or a countdown would only hold the previous race's laps.

If HeatCaller can't write the copy (a locked-down PC, a full disk), the console says "Race journal could not be written - if the app crashes now, this race cannot be recovered." HeatCaller tries again every 5 seconds and reports when writing works again. See [Data files](../admin/data-files.md) for where the file lives.

### Getting a race back

The next time HeatCaller opens after a crash, power cut or forced close, it asks **Restore the unfinished race?** It describes the race, for example how many drivers, how many laps and what time it started.

- **Yes.** The race comes back as it was: laps, penalties, positions, the fastest lap, the start type and grid, and any jump starts. The race clock picks up where it stopped, rather than counting the time the app was closed. A race that was under a red flag comes back with the clock where it froze, so the time under red still isn't counted. The race is saved again straight away with its original start time and track.
- **No.** The race isn't restored, but HeatCaller keeps a copy on disk, so this isn't your last chance. See [Data files](../admin/data-files.md).

A restored race comes back **stopped, under a red flag**, so nothing counts while the cars are put back on the track. Then:

1. Put the cars back on the track.
2. Press **Green (Start)** to resume. If you'd rather keep the race as it stands, press **Checkered** to save it.

!!! note
    Each car's first lap after the green is timed from its last crossing before the app closed, so it includes the stoppage and looks long. It counts as a lap, but never as a best lap, never toward a clean run or streak pace, and never in the average or consistency columns, even if you later remove laps. You don't need to remove it.

"Last lap" and "one minute to go" aren't called a second time after a restore if they were already called before the app closed.

If the race was on its last lap when the app stopped, the checkered flag is still out after the restore, and drivers who had already taken it stay finished. The 60-second wait for stragglers starts again when you go green.

HeatCaller only offers a race back when nothing else is running. If **Start automatically when a car crosses the gate** is ticked on the **Practice** page, a car crossing the line doesn't start practice until you've answered the question.

For a qualifying start, HeatCaller also corrects each driver's start reference by itself if the decoder was switched off and on while the app was closed.

## Closing the window during a race

If you close HeatCaller while a race is staging, counting down or running (with the X, Alt+F4 or the taskbar), HeatCaller asks first:

> A race is running. Close HeatCaller anyway?
>
> The race stops where it is. It can be offered back the next time the app opens, but nothing is timed while the app is closed.

Choose **No** to keep racing. If you choose **Yes**, HeatCaller writes the latest state of the race to disk before it closes, and offers the race back next time it opens (see [Crash recovery](#crash-recovery)).

If practice is running, the question starts "Practice is running. Close HeatCaller anyway?" instead. Practice isn't offered back: choosing **Yes** stops it the same as pressing **Stop**, and saves it to **Race History** if **Save automatically on stop** is ticked.

HeatCaller doesn't ask when Windows is shutting down.

Moving to another page in HeatCaller doesn't affect a race. Timing, time limits and the finish keep running in the background.

## The decoder reconnects during a race

If the decoder is reconnected mid-race (a USB glitch, a wrong port, or someone pressing **Connect**), its clock can restart from zero. HeatCaller spots this when a car next crosses the line and rejects that crossing, because there's no way to know how long the lap really took. The console says the decoder clock went backwards and that the decoder was most likely reconnected during the race.

HeatCaller then re-times from the decoder's new clock, so every lap after that is timed normally. Each car loses the one lap that spanned the reconnect, not the rest of the race. Check the board afterwards and use **+1 Lap** for any lap that should have counted.

<!-- TODO: confirm with a real reconnect test that every car (not only the first one through) loses exactly one lap; the code rejects each car's first crossing on the new clock. -->

For connection problems, see [Decoders](../timing/decoders.md) and [Troubleshooting](../admin/troubleshooting.md).

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| A driver's lap count is one too high after a double read | Two crossings close together, both over the minimum lap time | **-1 Lap**. If it happens often, raise **Min valid lap (ms):** in the race format. See [Running a race](running-a-race.md#minimum-lap-time). |
| A correction didn't change the saved result | It was made on the board after the checkered flag | Make corrections before the race finishes, or correct the saved result with **Edit Result** on **Race History** (see [Correcting a saved result](#correcting-a-saved-result)). The console confirms when a correction was made after the flag. |
| **Undo** didn't bring back a reset race | A new race was staged or run after the reset | The undo is refused on purpose, to protect the newer race. |
| No recovery offer after a crash | The race was still staging or counting down, had no laps yet, was a demo or practice, was already saved, or was saved more than 24 hours ago | Check **Race History**. |
| **Undo** didn't bring back a race abandoned by **Stage** | The new race had already gone green | The undo is refused on purpose, to protect the newer race. |
