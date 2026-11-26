# Corrections and recovery

No decoder is perfect, and no laptop is either. This page covers fixing the count during a race (missed passes, double reads, on-track penalties) and getting a race back when something goes wrong: an accidental reset, a crash, the window being closed, or the decoder dropping out.

## Correcting laps and penalties

All corrections apply to one driver on the **Race Control** leaderboard. There are three ways to reach them:

- **The drawer.** Click a driver's row and a drawer slides open under it: **+1 Lap**, **−1 Lap**, **+5s**, **+10s**, **⚠ Warning**, **Clear Penalties**, **Share video** and **Lap breakdown**. The buttons work once the drawer is fully open. Click the same driver again to close it, or another driver to move it to them. Double-clicking a driver opens their lap breakdown instead.
- **Right-click** a driver's row: **Add a lap (+1)**, **Remove a lap (-1)**, **Penalty +5s**, **Penalty +10s**, **Penalty...**, **Clear penalty** and **Warn** *name* (see [Driver warnings](#driver-warnings)). The menu always applies to the row you right-clicked, not the row that was selected before.
- **The keyboard**, with a driver selected: <kbd>+</kbd> and <kbd>-</kbd> for laps, <kbd>P</kbd> and <kbd>Shift</kbd>+<kbd>P</kbd> for 5 and 10-second penalties, <kbd>C</kbd> to clear penalties, <kbd>W</kbd> to give a warning and <kbd>Shift</kbd>+<kbd>W</kbd> to take the last one back. See [Keyboard shortcuts](../reference/keyboard-shortcuts.md#the-selected-driver).

A correction shows on the board straight away.

<!-- screenshot: a driver's drawer open under their row on the leaderboard -->

### Adding a lap

Use **+1 Lap** when the decoder missed a pass. The lap count goes up by one. A lap added by hand has no lap time, so it doesn't change best laps, averages or the lap chart.

If the lap you add brings the driver to the race's lap limit, the checkered flag comes out just as if they had crossed the line. With **Leader takes the flag, others finish their lap** ticked, that driver has finished and everyone else finishes the next time they cross. With it unticked, the race ends. A lap added under a red flag doesn't bring the checkered flag out while the field is stopped. It comes out when you press **Green** to restart, in place of the green, and that driver has finished. With the box unticked, the race ends there.

### Removing a lap

Use **−1 Lap** when a car was counted twice, for example a double read on the line.

- Laps you added by hand are removed first.
- After that, one lap comes off the driver's count.
- Best laps and the fastest lap of the race are updated to match.

If a phantom lap brought the checkered flag out early in a lap race, for example a car carried back across the loop after a crash, use **−1 Lap** on that driver straight away, before they come round again. Their real last lap then counts: the next time they cross, they take the flag. The checkered flag stays out for everyone else. A crossing they make while they're marked as finished isn't counted, even once the lap comes off, so if they've already come round, leave their count alone: the phantom lap stands in for the one they just finished. A driver who took the flag after it came out has finished, so taking a lap off them doesn't give them another lap to run.

The lap count never goes below zero.

### Time penalties

- **+5s** and **+10s** add that much to the driver's total time. The board orders drivers on the same lap by total time, so a penalty drops the driver behind anyone they're now slower than.
- **Penalty...** opens the **Time penalty** dialog. Enter any amount in **Seconds to add:** (in half-second steps, from −600 to 600) and press **Apply**. A negative number gives back penalty time the driver has already been given, never more than they carry: it can't take time off a driver with no penalty. The dialog says so: "Added to the driver's race time. Less than 0 gives back penalty time."
- The **Penalty** column shows each driver's total penalty. It stays applied for the rest of the race.
- **Clear Penalties** (drawer) or **Clear penalty** (menu, which shows the current total, for example "Clear penalty (10.0s)") removes all of that driver's penalty time, including any automatic jump-start penalty, and takes back their warnings too. With nothing to clear, the log says the driver "has no penalties or warnings to clear."

### Driver warnings

A warning is race control telling a driver off for something on track: rough driving, cutting the track, blocking. Each class has a **warning ladder** that decides what a driver's first, second, third warning of the race costs them, so you don't have to work out the penalty in the heat of the moment.

**Giving a warning.** Select the driver, then use any of these. They all open the same list:

- **⚠ Warning** in the driver's drawer.
- **Warn** *name* on the right-click menu. Once the driver has warnings, it shows how many, for example "Warn Sam (2 warnings so far)".
- <kbd>W</kbd> on the keyboard. The list opens at the driver's row. If no driver is selected, the log says "W pressed with no driver selected - click a driver on the board first."

The top line of the list says what this warning will cost, for example "2nd warning for Sam: +5s penalty". Then pick a reason. Each one has a number, so after <kbd>W</kbd> you can press the number instead of clicking:

| Key | Reason |
|---|---|
| <kbd>1</kbd> | Rough driving |
| <kbd>2</kbd> | Track cutting |
| <kbd>3</kbd> | Blocking |
| <kbd>4</kbd> | Unsportsmanlike conduct |
| <kbd>5</kbd> | Ignoring the marshal |
| <kbd>6</kbd> | Jump start |
| <kbd>7</kbd> | Not marshaling |
| <kbd>0</kbd> | **Other reason...** opens a small **Warning for** *name* window. Type a reason of up to 80 characters and press **Warn**. |

When a warning is given:

- The **Warning or black flag** sound plays (see [Sounds and music](../announcer/sounds-and-music.md)), then the announcer says it: "Warning, Sam. Blocking." For a time penalty it adds "That's a 5 second penalty." For a black flag: "Black flag, Sam. Please bring your car in and report to race control."
- A line goes in the log, for example "Sam: Warning 2 - Blocking (+5s) at 3:12."
- The driver's name on the board gets a mark after it: ⚠ and the count, or **⚑ BLACK FLAG** once they've been black-flagged.
- A time penalty is added exactly as the **+5s** and **+10s** buttons add one, so it shows in the **Penalty** column.

**The warning ladder.** Set it per class: open the class on the **Classes** page and click **Warnings...** (see [Classes](../race-day/drivers-and-classes.md#warnings)). Unless you change it, every class uses the default:

| Warning | What it costs |
|---|---|
| 1st | Warning only |
| 2nd | +5 seconds |
| 3rd | +10 seconds |
| 4th | Black flag (DQ) |

After the last step, that step repeats: a fifth warning on the default ladder is another black flag, and on a ladder that ends at +10s, every warning after it is another +10s. Warnings are counted per race, so every driver starts each race clean.

**Black flag = disqualified.** A black-flagged driver is called in. The race doesn't wait for them at the finish, and the timing watch stops expecting them to cross. When the race is saved, the driver's result is marked **DQ** and the log says so. The DQ goes into the result's correction log as "Black flag from race control (warning limit reached)", so if it was wrong, open the race in **Race History** and use **Edit Result** to put it back (see [Correcting a saved result](#correcting-a-saved-result)). Demo races are never marked.

**Taking a warning back.** Gave the wrong driver a warning, or picked the wrong reason? Any of these takes back the driver's most recent warning, along with any penalty time it added:

- **Remove last warning** at the bottom of the warnings list. It names the warning it will remove, for example "Remove last warning (Warning 2 - Blocking (+5s) at 3:12)".
- <kbd>Shift</kbd>+<kbd>W</kbd>.
- **Undo** (<kbd>Ctrl</kbd>+<kbd>Z</kbd>) straight after giving it. Undo only takes it back if it's still the driver's newest warning.

Taking a black flag back before the race is saved means the driver isn't marked DQ. **Clear Penalties** takes back all of a driver's warnings at once, along with their penalty time.

**Where warnings show.**

- **The Warnings column.** Add it to any leaderboard with its column chooser (on Race Control, right-click a column heading; see [Columns](running-a-race.md#columns)). It shows "-" for a clean driver, ⚠ and the count, or the count and **⚑ BLACK** after a black flag. On Race Control it's in red.
- **Race History** has a **Warnings** column for every saved race. Hover over it to see each warning. See [Results and championships](results-and-championships.md).
- **The Big Screen** shows a warning as a banner across the screen rather than in red on the board. See [The Big Screen](../screens/big-screen.md#the-banner-across-the-screen).
- **Driver rating** takes 5 off for each warning.

### After the checkered flag

The result is saved to **Race History** the moment the race finishes. If you correct the board after that, the saved result isn't changed. The console says:

> Adjustment after the flag - history not updated. The saved result still has the figures it had at the checkered flag.

The saved result, the championship points and the qualifying figures keep the figures from the flag. Make corrections on the board while the race is still running, or correct the saved result afterwards with **Edit Result** (below).

!!! tip
    Each correction is on the **Undo** list (the button at the bottom of the sidebar, or <kbd>Ctrl</kbd>+<kbd>Z</kbd>), and Undo takes back just that correction, leaving any laps counted since. You can also use its opposite: **−1 Lap** after an extra **+1 Lap**.

### Correcting a saved result

**Edit Result** on the **Race History** page corrects a race after it has been saved. It only works once **Allow corrections to saved results** is ticked on the **Local Screens** page, in the **Race-day manager (phone)** card. It's off by default; until then it says "Correcting saved results is switched off on the laptop." Only a real race can be corrected, not practice or a demo race. A result pasted in from LiveRC can't be corrected either: delete it and paste it again.

1. Select the race on **Race History** and click **Edit Result**. From the race program on **Race Day → Next Steps**, right-click a round that has been raced and choose **Edit Result** to go straight there, even after the next race is under way (it's grayed out for a round not raced yet). After the flag, **Edit Result** on the Race Control flag row does the same for the race just run.
2. For each driver, set **Added Laps** (laps added, or taken away with a minus sign), **Time Penalty (s)**, **Lap Penalty** and **Outcome** (**Finished**, **DNF** or **DQ**). **New order** shows the result as it will be.
3. Type a reason in **Reason:** (it's needed to save, and kept with the result) and click **Save Correction**.

The tiles across the top of the window sum the correction up as you go: **Drivers**, **Winner**, **Changed** (how many drivers' figures you've changed) and **Order moves** (how many drivers end up in a different place). In **New order**, a driver who has moved shows "▲ was P3" or "▼ was P2".

Corrections are worked out from the race as it was actually run, not from the last correction, so setting everything back to zero and **Finished** gives the original result back. Qualifying, the mains, the championship and the results pages all use the corrected figures. Badges already earned for the race aren't worked out again. **Undo** takes a correction back.

The race-day manager's **Results** tab makes the same correction from a phone. See [Race-day manager](../screens/phone-and-web.md#race-day-manager).

**Typing a whole result's lap counts.** The **Laps** column shows each driver's laps as the result will have them. Type the lap count straight in and **Added Laps** changes to match, with any **Lap Penalty** still taken off on top. Press <kbd>Enter</kbd> to go down to the next driver, so you can type a whole result off a paper lap sheet in one go. Changing **Added Laps** or **Lap Penalty** updates **Laps**. Laps added this way show as counted by hand ("12*") in the result, like any lap added after the race.

## When the decoder goes down mid-race

If the decoder or the loop stops working during a race, you don't have to stop the race and run it again. Score it by hand instead: one big button per car, and a tap each time a car crosses the line.

**Switching it on.** When the [timing warning](running-a-race.md#timing-warnings) says the decoder isn't connected, has gone quiet, or no car has crossed the line, a **Score by Hand** button appears on the Next Up bar. Click it. You can also right-click any driver on the leaderboard during a race and choose **Score the race by hand (decoder down)...**. HeatCaller doesn't switch to hand scoring by itself, because only you can tell a dead loop from a long pause on track.

**The grid.** A window fills the screen with one button per car, showing the driver's name and their laps. The buttons stay in grid order (the order the cars lined up in), so they never move while you tap, even as the running order changes.

- **Tap** (or click) a car each time it crosses the line. The button flashes green.
- **Right-click** a car to take a lap off, or use **Undo last tap** (or <kbd>Ctrl</kbd>+<kbd>Z</kbd>) for the last car you tapped.
- A second tap on the same car within a second is ignored, so a finger bouncing on a touch screen doesn't count two laps.
- **Close (keep scoring)** hides the grid and leaves hand scoring on. **Score by Hand** on the Next Up bar brings it back.
- **Stop hand scoring** goes back to counting laps from the loop, for example once the decoder is working again. It asks first. Laps already counted by hand stay.

While hand scoring is on, the leaderboard's right-click item reads **Open the hand-scoring grid...**. Before the first car has crossed, the grid says "No car on the board yet" and that each car gets a button once it crosses the line.

Each tap is the same as **+1 Lap** in the drawer: it has no lap time, so it never sets a best lap or a record, it's on the **Undo** list, and a lap that reaches the lap limit brings the checkered flag out as usual. A car joins the grid once it has crossed the line in this race, or when you add it (below).

**A car the decoder never saw.** If the loop died before a car crossed the line, it has no button yet. Click **Add a Car...** at the top of the grid and pick it: the cars in this heat come first, marked "(in this heat)", then every other known transponder by name. It gets a button with no laps; tap it each time it crosses. **Undo** takes it off again while it still has no laps. If the decoder comes back, that car's next crossing is timed as if it had never been added, from the green. A car can't be added once the checkered is out; add its laps afterwards with **Edit Result**.

**On a phone or tablet.** On the race-day manager's **Race** tab (on the venue's Wi-Fi), a **Decoder down?** section shows during a race. Tap **SCORE BY HAND**, and the same grid appears on the phone, two cars to a row, with **Undo last tap** and **Stop hand scoring** under it, and a list to add a car that never crossed the line (pick it, then **Add this car**). A tap on the phone is the same lap as a tap at the desk, so two people can share the field, one counting half the cars each. See [Race-day manager](../screens/phone-and-web.md#race-day-manager).

**The announcer.** When you switch it on, the announcer says "Timing is down. Laps are being counted by hand." While it's on, it doesn't call laps from the loop at all, so a flicker from a half-working decoder isn't read out as a lap time or a fastest lap. Everything else, such as the time left and the finish, is announced as normal.

**The result.** Hand scoring lasts until the race ends, and the next race starts with the decoder trusted again. In the saved result, every lap counted by hand is marked with an asterisk ("12*"), the same as any lap added with **+1 Lap**. Cars level on laps are placed by their last real crossing, so check the finishing order afterwards. If anything needs putting right, use **Edit Result**, where you can type the whole result's lap counts in one go (see [Correcting a saved result](#correcting-a-saved-result)).

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

Calls already made aren't repeated. Ghost cars in the race carry on mid-lap, as though they had kept lapping; see [Ghost Cars](practice-and-ghosts.md#ghost-cars).

**Reset** during a demo ends the demo instead, and that can't be undone. Once **Countdown** or a first **Green** has taken over from a demo, the start on the board is a real race's, so a **Reset** pressed straight after can be undone as usual. While practice is running, **Reset** and **Checkered** are refused: use **Stop** or **Clear Stats** on the **Practice** page. See [Practice](practice-and-ghosts.md#practice).

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

A **Countdown** confirmed over a running race begins a fresh start, as **Stage** does.

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
- **No.** The race isn't restored, but HeatCaller keeps a copy on disk, each declined race under its own name, so this isn't your last chance. See [Data files](../admin/data-files.md). If the copy can't be made (another program has the file open), the log says "Unfinished race not restored, and it could not be set aside", and the race is offered again the next time HeatCaller opens.

A restored race comes back **stopped, under a red flag**, so nothing counts while the cars are put back on the track. Then:

1. Put the cars back on the track.
2. Press **Green** to resume. If you'd rather keep the race as it stands, press **Checkered** to save it.

!!! note
    Each car's first lap after the green is timed from its last crossing before the app closed, so it includes the stoppage and looks long. It counts as a lap, but never as a best lap, never toward a clean run or streak pace, and never in the average or consistency columns, even if you later remove laps. You don't need to remove it.

Calls already made before the app closed aren't repeated after a restore, and the time calls carry on as normal.

If the race was on its last lap when the app stopped, the checkered flag is still out after the restore, and drivers who had already taken it stay finished. The 60-second wait for stragglers starts again when you go green.

HeatCaller only offers a race back when nothing else is running. If **Start automatically when a car crosses the gate** is ticked on the **Practice** page, a car crossing the line doesn't start practice until you've answered the question.

A qualifying start comes back with each driver's times as they were, even if the decoder was switched off and on while the app was closed.

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
| A driver's lap count is one too high after a double read | Two crossings close together, both over the minimum lap time | **−1 Lap**. If it happens often, raise **Min valid lap (ms):** in the race format. See [Running a race](running-a-race.md#minimum-lap-time). |
| A correction didn't change the saved result | It was made on the board after the checkered flag | Make corrections before the race finishes, or correct the saved result with **Edit Result** on **Race History** (see [Correcting a saved result](#correcting-a-saved-result)). The console confirms when a correction was made after the flag. |
| **Undo** didn't bring back a reset race | A new race was staged or run after the reset | The undo is refused on purpose, to protect the newer race. |
| No recovery offer after a crash | The race was still staging or counting down, had no laps yet, was a demo or practice, was already saved, or was saved more than 24 hours ago | Check **Race History**. |
| **Undo** didn't bring back a race abandoned by **Stage** | The new race had already gone green | The undo is refused on purpose, to protect the newer race. |
