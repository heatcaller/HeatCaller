# Running a race

The **Race Control** page is where you run every race: you pick what's about to run, call the cars to the grid, start the race, throw flags, and watch the live leaderboard. This page walks through a race from start to finish and explains each part of the screen.

!!! note
    HeatCaller runs its own race control, reading the decoder (EasyLap or I-Lap) directly. See [Decoders](../timing/decoders.md).

<!-- screenshot: Race Control page during a race, flag buttons, clock column and leaderboard visible -->

## The page at a glance

From top to bottom, the page has:

- **The Session row.** Choose the round, the track and the session name.
- **The Next Up bar.** Shows what's running now, what's next, and how the day is going.
- **The Race Control card.** Holds the flag buttons, the current **State:** and **Flag:**, the format chip and the **Race Format** button.
- **The Leaderboard card.** Takes up the rest of the page. The correction toolbar, under **Selected racer:**, sits along its bottom edge.
- **The clock column** on the right. It holds the race clock, then **Quick Race**, **Open Big Screen**, **Open Announcer Screen**, **Qualifying Projection** and **Pre-race check**, then two stat tiles: **Laps** (the leader's lap count) and **Fastest** (the fastest lap of the race so far).

## Before you start

- Your decoder is connected. See [Decoders](../timing/decoders.md).
- Your drivers have profiles, so the board shows names instead of transponder numbers. See [Drivers and classes](../race-day/drivers-and-classes.md).
- If you're running a structured day, your rounds are on **Race Day → Next Steps**. See [Program and mains](../race-day/program-and-mains.md). You don't need a program: **Quick Race** works without one.

## Choose what's about to run

Use the **Session** row at the top of the page:

| Control | What it does |
|---|---|
| **Name:** | An optional name for a freeform session, for example "Friday Main". The saved race uses this name. It's hidden while a round is chosen. |
| **Track:** | The track layout. HeatCaller uses it for track records and saves it with the result. |
| **Round:** | Any round from the program that hasn't run yet, shown as `[Class] Round (Kind)`. Choose **(freeform - no race program round)** to run without one. |

When you choose a round, HeatCaller fills in the track and the name (as "Class - Round") and locks them; the **Name:** box is hidden, since the round says it already. The finished race is saved against that round, so the program shows it as run. **Reset** clears the round and the name as well as the race (see [Reset and undo](corrections-and-recovery.md#reset-and-undo)).

If you don't choose a round and **Start the next race automatically** is ticked on **Race Day → Setup** (it is by default), HeatCaller ties the finished race to the next round that hasn't run yet. By default it picks from the class that was racing. See [Which round a race counts for](../race-day/program-and-mains.md#which-round-a-race-counts-for).

### The race format

The format chip under the flag buttons shows which rules apply to the next race and where they come from. For example: `Format (Stock class):  5 min · all away together · auto 15+5s`.

HeatCaller uses the first of these that exists:

1. The round's own override.
2. For a qualifying round, the class's **Qualifier Format**.
3. The class's **Race Format**.
4. The app default.

**Race Format** opens the format that's currently in force, wherever it comes from, so you always edit the one you can see. Class formats are also set on the **Classes** page. If you change the format while a race is running, the chip updates straight away, but the running race keeps the rules it started with.

| Setting | What it does | App default |
|---|---|---|
| **Lap limit (0 = none):** | Ends the race after the leader completes this many laps. | 0 |
| **Time limit, min (0 = none):** | Ends the race after this many minutes of racing. | 0 |
| **Min valid lap (ms):** | Ignores a pass that comes sooner than this after the car's previous pass. See [Minimum lap time](#minimum-lap-time). | 3000 |
| **Streak window ±%:** / **faster too** | How much a lap can differ from the one before it and still extend a clean run. If **faster too** is ticked, a much quicker lap also ends the run. | 25, ticked |
| **Start:** | How the field is released. See [Start types](#start-types). | Standard |
| **Stagger gap (s):** / clock | The gap between releases for a staggered start, and whether each car gets its own clock (**Clock: own release**) or everyone shares one (**Clock: shared green**). | 5, own release |
| **Warm-up (s, 0 = none):** | Free laps before the race, up to 30 minutes. See [Warm-up](#warm-up). | 0 (off) |
| **Run the start sequence automatically (Stage → Countdown → Green)** | When you press **Stage**, the start runs through to the green on its own. | Ticked |
| **Stage (s):** / **Countdown (s):** | How long each step lasts in the timed Quick Race sequence. | 15 / 5 |
| **Leader takes the flag, others finish their lap** | Changes how a race ends. See [Finishing](#finishing). | Ticked |

For a class or a round, untick **Use a custom format (untick to follow the app default)** to go back to the app default.

## Start the race

1. Pick the round (or leave the session freeform).
2. Press **Stage** (<kbd>F1</kbd>). The board clears, the announcer calls the cars to the grid, and the state changes to **Staging**.
3. Press **Countdown** (<kbd>F2</kbd>). The state changes to **Countdown**. From now on, a car that crosses the line counts as a [jump start](#jump-starts).
4. Press **Green (Start)** (<kbd>F3</kbd>). The race starts and the clock begins counting.

With **Run the start sequence automatically (Stage → Countdown → Green)** ticked (the default), you only need to press **Stage**. HeatCaller moves on to the countdown when the staging announcement finishes, and drops the green when the countdown announcement finishes. It follows the audio rather than a fixed timer, so the start never talks over itself or leaves a silence.

The chain stops if anything else happens first. Pressing **Stage**, **Countdown**, **Green (Start)**, **Yellow**, **Red** or **Reset** stops the automatic sequence, and your button press wins. **Checkered** doesn't: before the green it's refused and changes nothing, so the start carries on, automatic green included (see [Flags](#flags)).

A yellow or red thrown during staging or the countdown also stops the automatic start (and **Quick Race**). The log says the start stopped because the flag is out: "Press Green when the track is ready, or Stage again to restart the sequence." The race still hasn't started, so the green you press next is a start, not a restart: the start horn and the **Race started** call play when it drops.

!!! warning
    **Green (Start)** starts a race straight away even if you haven't staged. A green with no countdown gives the jump-start check nothing to watch. Use **Stage** for a proper start.

The green always waits for the countdown announcement to finish, however the start was triggered. If you press **Reset**, **Stage** or **Red** during that wait (or a demo is stopped), the green is canceled and your later decision stands. The log says "Green flag not dropped - " and what happened. **Checkered** pressed during the wait is refused and leaves the green alone.

### Other ways to start

- **Quick Race** runs a race with whoever is on track, using the app default format and no round, event or check-in. It names the session "Quick Race HH:mm" and runs Stage, Countdown and Green on a timer, using **Stage (s):** and **Countdown (s):**. If the app default format has a warm-up, that runs first.
- **Start Next** on the Next Up bar loads the next round from the program and presses **Stage** for you. With the start sequence running automatically, the start then runs through to the green. See [The Next Up bar](#the-next-up-bar).
- You can also start a round from **Race Day → Next Steps**, and throw flags from the phone remote. See [Phone and Web](../screens/phone-and-web.md).

**Quick Race** and **Start Next** do nothing while a session is already under way. Finish or reset the current session first.

**Stage** and **Countdown** pressed while a race is running ask first, because they would abandon the running race without saving it. Starting a round from **Race Day → Next Steps** asks too, before anything is announced. To end a race and keep its result, use **Checkered**. Once you confirm, the abandoned race's flag goes with it, so a new start made over a race under yellow or red runs on to its automatic green like any other. See [Staging over a running race](corrections-and-recovery.md#staging-over-a-running-race).

While practice is running, **Stage**, **Countdown**, **Green (Start)**, **Checkered**, **Reset** and starting a round from the program or the Next Up bar are refused with "Stop practice first on the Practice page". See [Practice](practice-and-ghosts.md#practice).

During a demo, **Stage**, **Countdown**, **Reset** or a first **Green (Start)** on Race Control ends the demo so a real race can take its place. Starting a round from the program or the Next Up bar ends a demo too. Once the demo is racing, **Green (Start)** after a caution and **Checkered** still act on the demo. See [Demo Mode](practice-and-ghosts.md#demo-mode).

If the round's class runs tech, staging it (or pressing **Green (Start)** from idle) also checks that everyone in it has passed tech today. Anyone who hasn't is listed in a "[tech] WARNING" line in the log. It's only a reminder; the race starts as normal. See [Tech warnings on Race Control](../race-day/events-and-entries.md#tech-warnings-on-race-control).

### Warm-up

A race format can give the cars some free laps before the race: set **Warm-up (s, 0 = none):** in the format. It's off (0) unless you set it.

With a warm-up set, pressing **Stage** (or **Quick Race**) between races opens the warm-up first. The state shows **Warm-up**, the announcer says the warm-up is open and how long it lasts, and laps aren't counted. On a warm-up longer than a minute, it calls "Thirty seconds left in warm-up." When the time runs out, the race is staged as usual. To cut it short, press **Stage** (or **Quick Race**) again: the rest of the warm-up is skipped and the race is staged straight away.

**Reset** cancels a warm-up. A warm-up never opens during practice, or over a race that's staged, counting down or running.

## Start types

Choose the start type in **Start:** in the race format.

| Start | How it works |
|---|---|
| **Standard - all away together, one clock** | Everyone goes on the green. |
| **Staggered - grid order** | Cars are released one at a time, **Stagger gap (s):** apart. The first car on the grid goes first. |
| **Staggered - reverse grid** | The same, but the last car on the grid goes first, so the quick drivers have to come through the field. |
| **Staggered - handicap** | A pursuit start based on each driver's handicap time credit. See below. |
| **Qualifying - own clock from 1st crossing** | Everyone goes on the green, but each driver is timed from their own first crossing. See below. |

### Where the grid comes from

A staggered start needs a grid order. HeatCaller uses the selected round's roster, in the order the drivers were assigned. With no round selected, it uses the finishing order of the last race. If neither exists, it starts everyone together and says so in the console.

The grid also records where everyone started, which the **Gained** column uses. See [The leaderboard](#the-leaderboard).

### Own release or shared green

For a grid-order or reverse-grid stagger, you choose which clock each car runs on:

- **Clock: own release.** Each car's clock starts when that car is released, so everyone gets the same amount of racing and the result comes down to pace. In a timed race, cars released later keep running after the leader's time is up, until their own time runs out.
- **Clock: shared green.** Everyone runs on one clock that starts at the green. A car released later simply has less time, and the first car across the line wins.

The board greys out cars that are waiting to be released. The announcer calls each release when it's due.

### Handicap (pursuit) start

**Staggered - handicap** uses the time credits on the **Handicaps** page. The driver with the biggest credit (the slowest driver) goes first, on the green. Everyone else waits for the difference between the biggest credit and their own. Scratch drivers, with no credit, wait the longest.

A handicap start always runs on one shared clock from the green, whatever the format says. On each driver's own clock the head start would count for nothing. The format's own clock setting applies again for the next race.

!!! warning
    If the class's handicap mode is also **Time credit (seconds off total)**, each credit counts twice: once as the head start and again off the result. HeatCaller warns you in the console. For a handicap start, set the class's mode to **Off - scratch racing**. See [Handicaps](practice-and-ghosts.md#handicaps).

### Qualifying start

With **Qualifying - own clock from 1st crossing**, everyone leaves the grid on the green, but each driver's clock starts at their own first crossing of the line. The run from the grid to the timing loop costs nobody anything, and each driver is measured over their own laps only.

That first crossing counts as the driver's start lap, with a time of 0.000. The time limit still runs from the green.

The hint in the format dialog explains the two qualifying conventions. **All away together** is a grid start on one shared clock, where the run to the loop counts against your time. **Own clock from 1st crossing** is the IFMAR convention.

## Jump starts

HeatCaller checks for jump starts only during the **Countdown**. While cars are being staged, they're carried out and rolled over the loop to their grid slots, so passes during **Staging** are ignored.

A **Countdown** or **Green (Start)** pressed without **Stage** first still starts a fresh jump-start check with the class's own rules. Nothing carries over from the previous race.

A car that crosses the line during the countdown is flagged as a jump starter. In a staggered start, a car that crosses before its own release time is flagged too. Each car is flagged at most once per race.

A car left sitting on the loop isn't a jump start. The decoder keeps reporting a car parked on the loop every couple of seconds. If it has been seen there three times in a row, no more than 3 seconds apart, by the time of its pass in the countdown, the console says it's sitting on the loop and it isn't flagged. Move it back behind the line. At the green it's treated like any car already past the line. A car moved onto the loop during the countdown is still flagged at its first pass.

When a car jumps the start:

- The announcer calls it.
- Its row on the leaderboard is tinted red for the rest of the race. Hover over the first cell to see "Jump start - crossed before the green."
- If the class sets a penalty, it's added to the driver's time on their first counted lap. It shows in the **Penalty** column, and **Clear Penalties** removes it like any other penalty.

You set the jump-start rules on the class:

| Class setting | What it does |
|---|---|
| **Detect jump starts (crossing before the green)** | Turns jump-start detection on or off. It's on for freeform races, which have no class. |
| **Automatic penalty:** | Seconds added to the driver's time. **0** flags the jump without a penalty (the default for freeform races). |
| **A jump start also costs the lap** | Off by default. When it's off, a car that was already past the line when the green dropped is credited with its start lap, so the jump costs only the time penalty. This doesn't apply to staggered starts or qualifying starts. |

## Flags

| Button | Key | What happens |
|---|---|---|
| **Yellow** | <kbd>F4</kbd> | Caution. Laps still count and the clock keeps running. |
| **Red** | <kbd>F5</kbd> | Stops the race. The clock pauses and shows **PAUSED**, laps are not counted, and cars waiting to be released stay held. A timed race can't run out while the red flag is out. |
| **Green (Start)** | <kbd>F3</kbd> | During a race, this resumes racing after a yellow or red. Laps and times are kept. |
| **Checkered** | <kbd>F6</kbd> | Ends the race now, for everyone, and saves the result. Only works while a race is running. |
| **Reset** | <kbd>F7</kbd> | Clears the race, and the round and name on the **Session** row. The key asks first if a race is under way. See [Corrections and recovery](corrections-and-recovery.md#reset-and-undo). |

Going from red to yellow also restarts the race clock, because laps count under yellow.

The presses below are refused and change nothing. No box appears: the log gives the reason after "Checkered not thrown." or "Green not thrown." The phone remote shows it too, for example "CHECKERED not done: The race has already finished.", and so does the race-day manager's **Race** tab.

| Press | When | Reason given |
|---|---|---|
| **Checkered** | During staging or the countdown. The start carries on, automatic green included. | "The race hasn't gone green yet. To call off this start, press Reset." |
| **Checkered** | After the race has finished. | "The race has already finished." |
| **Checkered** | With nothing staged. | "No race is running." |
| **Green (Start)** | On the last lap, with the checkered flag out. | "The checkered flag is out - the race is finishing." |

**Green (Start)** pressed while the race is already green does nothing at all: no horn and no call. The log says "Green pressed - the race is already green, so nothing changed.", and the phone remote shows "Already green."

The lit flag button always shows the flag in effect, however it was thrown: a button, a function key, the phone remote or the automatic start. **Stage** and **Countdown** light up while the start is at that step.

The function keys work only while the **Race Control** page is showing, and only on their own. Alt+F4, or an F-key with Ctrl or Shift, never throws a flag. See [Keyboard shortcuts](../reference/keyboard-shortcuts.md).

## Lap-limited and timed races

- **Lap limit:** The clock's progress ring and the text under the clock count laps: "5 LAPS TO GO", then "1 LAP TO GO". The announcer calls the last lap when the leader starts it.
- **Time limit:** The ring and the text count down the time, for example "02:30 REMAINING". The announcer calls the final minute.
- **Both:** The race ends at whichever limit is reached first, and the clock shows the laps remaining.
- **Neither:** The race runs until you press **Checkered**.

The clock always shows elapsed racing time. Time under a red flag isn't counted.

## Finishing

With **Leader takes the flag, others finish their lap** ticked (the default), a race ends the way real racing does:

- **Lap limit.** When the leader completes the last lap, the checkered flag comes out and the leader has finished. Every other car finishes the next time it crosses the line.
- **Time limit.** When time runs out, the checkered flag comes out and every car finishes the next time it crosses the line. On a staggered start with **Clock: own release**, cars released later keep racing until their own time is up.
- When every car on the board has taken the flag, the race is over and the result is saved.
- **Stragglers.** If a car has stopped on track, HeatCaller doesn't wait for it forever. It ends the race 60 seconds after the checkered flag came out (longer on an own-release staggered start). If you throw a red flag during the last lap, the 60 seconds start again when racing resumes.

With the box unticked, the race stops for everyone the moment a limit is reached.

Pressing **Checkered** while a race is running always ends it immediately.

When a race finishes, HeatCaller:

- saves it to **Race History**, leaving out ghost cars
- links it to its round
- works out any badges earned
- saves a results card image
- plays the teaser and the race recap, if they're set up.

See [Results and championships](results-and-championships.md). If transponders without a profile were on track, HeatCaller asks whether to name them after the race. See [Transponders](../timing/transponders.md).

## Minimum lap time

A transponder sitting near the timing loop can trigger the decoder over and over, and a car that crashes on the line can read twice. **Min valid lap (ms):** filters these out. A pass that comes sooner than the minimum after that car's previous pass isn't counted as a lap. The previous pass counts even if it was also rejected.

Rejected passes:

- break the driver's clean run
- are counted in the saved result
- show up in the **Clean %** column.

Set the minimum a little under the quickest real lap anyone could do on your track.

!!! tip
    The first counted pass after the green is the start lap. Its time runs from the green to the line, so it counts toward the lap total but never counts as a best lap. In the lap breakdown it's labeled **START**.

## The leaderboard

The board orders drivers by most laps, then by lowest total time. Total time includes penalties.

- **Double-click** a driver to open their lap breakdown. The breakdown shows every lap with a pace bar, how far off their best it was, and their clean run. With a [split loop](../timing/decoders.md#a-split-loop), each lap's sector times replace the pace bar, the driver's best in each sector is in green, and the line at the foot gives their **Ideal lap**. It updates live, so you can leave it open for a whole race. Click anywhere else to close it.
- **Right-click** a driver for more options: edit or assign a driver, the lap breakdown, show or hide their recent laps on the board, lap and penalty corrections, their battery pack, **Exclude from records**, and **Copy transponder ID**. The pack item lists the driver's packs and the club's, with the one in use ticked. During a race it reads ***name*'s pack in the car**: picking one puts it in their car, so the race is recorded on it. Once the race is saved it reads ***name*'s pack for this race**: picking one records it for the race just run, or **No pack recorded**, and a pack also goes in their car. See [A driver's batteries](../race-day/drivers-and-classes.md#a-drivers-batteries).
- **Show {name}'s laps on the board** opens a strip of recent lap times under the driver's row. You can open strips for several drivers at once to compare them. In the strip, a star (★) marks a lap inside a clean run, and a broken circle (◌) marks the lap where a run ended.
- The driver with the fastest lap of the race has their **Best Lap** highlighted.

### Columns

Press **Columns** to choose the columns and their order. In the dialog, click the left edge of a row to show or hide that column, and use **Move Up** and **Move Down** to reorder them. **Restore Defaults** puts back **Pos**, **Driver**, **Car**, **Laps**, **Best Lap**, **Gap to P1** and **Streak**.

| Column | Shows |
|---|---|
| **Pos** | Running position. |
| **Driver** | Driver name, or the transponder number if there's no profile. |
| **Car** | Car model from the driver's profile. |
| **Transponder** | Transponder number. |
| **Laps** | Laps completed. |
| **Last Lap** | Most recent counted lap. |
| **Best Lap** | Fastest lap this session. Highlighted when it's the quickest of anyone. |
| **S1**, **S2**, **S3**, **S4** | With a [split loop](../timing/decoders.md#a-split-loop): each sector of the last lap. S1 runs from the line to the split loop, S2 from the split loop back to the line. Highlighted when it's the quickest of anyone this session, green when it's the driver's own best. |
| **Ideal Lap** | The driver's best sectors added up: their lap with every sector at its best. |
| **S1 mph**, **S2 mph**, **S3 mph**, **S4 mph** | Average speed through each sector on the last lap. Needs the split loops placed on the layout's plan in the Track Designer, so the sectors' lengths are known. |
| **Top Speed** | The fastest the driver has gone through any sector this session, in mph. Highlighted for the fastest on the board. With the split loop a few feet past the line, this is a speed trap. |
| **At Split** | Live, the moment a car crosses the split loop: how far up (green, −0.08) or down (red, +0.12) it is on its own best lap to that point. |
| **Badges** | How many achievement badges this driver has earned. |
| **vs Best** | How far the last lap was off this driver's own best. Shows **BEST** when it was their best. |
| **Qual** | During a qualifying round: where this driver would qualify overall if the heat ended now, and how many places this heat has moved them. |
| **Gap to P1** | Time or laps behind the leader, for example +2.35 or +1L. |
| **Interval** | Gap to the car directly ahead. |
| **Best Pace Gap** | How far this driver's best lap is off the quickest lap of the session. |
| **Average** | Mean of every counted lap after the start lap. |
| **Rolling Avg** | Mean of the last five laps. |
| **Std Dev** | Lap-time spread. Lower means more consistent. |
| **Streak** | Consecutive clean laps right now. Turns green at the streak threshold. |
| **Best Streak** | Longest clean run this session. |
| **Streak Pace** | Average lap within the current streak. |
| **Trend** | The second half of the session compared with the first: ▲ finding time, ▼ fading, or "level". Needs at least six laps. |
| **Time Lost** | Total time given away against this driver's own best lap. |
| **Gained** | Places made up (+) or lost (−) since the grid. Shows a dash when the grid wasn't known. |
| **Clean %** | Share of this driver's passes that counted as laps. |
| **Led** | Laps completed while leading. |
| **PBs** | How many times this driver has beaten their own best this session. |
| **Penalty** | Time penalties applied. |
| **Total Time** | Elapsed race time for this driver. |

The same column list is available for the Big Screen and web pages. See [Big Screen](../screens/big-screen.md).

### Streaks and callouts

A clean run (streak) is a run of counted laps in a row. A run breaks on a rejected pass, or on a lap outside the **Streak window ±%:** of the lap before it. With **faster too** ticked, a much quicker lap also breaks it. Each lap is compared with the one before it, not with an average, so a driver who slows a little every lap can't keep a run going forever.

The number of laps that counts as a streak comes from **Sounds → Effects** (**Streak threshold - races:**). The **Streak** column turns green at that number, the same point where the announcer starts calling it.

During a race, the announcer also calls:

- a driver beating their own best lap (not on their first flying lap)
- a new fastest lap of the race (not the first one)
- a driver on track-record pace (within 2% of the record, called once per driver per race)
- a new track record.

A lap that beats the record by an implausible margin doesn't set the record. The console explains why and the lap still counts in the race. Demo and practice laps never set records, and neither do drivers marked **Exclude from records** or rental transponders. **Exclude from records** also takes the driver out of championship points; the log says the driver is "excluded from track records and championship points". See [Voice announcements](../announcer/voice.md).

## The Next Up bar

The bar across the top of the page has three parts, and a fourth when timing goes wrong:

- **TIMING** (in red, first): only during a race, when HeatCaller thinks laps aren't being counted. See [Timing warnings](#timing-warnings).
- **NOW**: the round or session that's running and its state, for example "Q1 Heat 2 (Stock) · racing".
- **NEXT**: the next round that hasn't run yet, how many drivers are in it, how many of them aren't checked in (shown in amber), and who marshals it (the heat before). When there are no rounds left it says "program complete". With no program it says "no program - Quick Race is one press away".
- **DAY**: how many rounds have been recorded, for example "6/14 recorded". If the timetable gives the next round a time, it also shows how far ahead or behind you are, once that's 3 minutes or more.

If the next round's class runs tech and someone in it hasn't passed tech today, **NEXT** also shows "· N not through tech", ahead of time. See [Tech warnings on Race Control](../race-day/events-and-entries.md#tech-warnings-on-race-control).

**Start Next** loads the next round and stages it (the announcer makes the grid call first). While a session is running, the button reads **Race Running** and is grayed out, so a stray click can't launch the next heat.

If something is owed before the next race can go out, the bar says so in place of the next round and the button does it instead: **Move the top finishers up** when a lower main has finished and its bump-ups are due, or **Build the finals from qualifying** when a class has finished qualifying. That way the upper main can't go out before its bump-ups. See [Program and mains](../race-day/program-and-mains.md#the-next-step-card).

**Redo Remaining Heats** rebuilds the rosters of every qualifying heat that hasn't run yet from the drivers checked in right now, after asking. Use it for a no-show or a late entry. Recorded heats and mains aren't changed, and **Undo** puts the old rosters back. See [Events and entries](../race-day/events-and-entries.md).

## Timing warnings

During a race, HeatCaller watches for timing going wrong without anyone noticing, the kind of fault that otherwise only shows when the results are wrong. When it spots one, the Next Up bar shows it first, in red, under **TIMING**, and the console gets a line starting `[timing] WARNING:`. When things are right again, the warning goes and the console says so.

It checks these, most serious first, and shows only the first that fails:

| Warning | What it means | What to do |
|---|---|---|
| `The decoder isn't connected - no laps are being counted. Connect it on the Connection page.` | The decoder in use says it isn't connected. | Check its cable, then connect it on the **Connection** page (or let **Auto-connect** find it). |
| `Nothing from the decoder for 8s - check its USB cable.` | An EasyLap sends a signal once a second, and it has stopped, even though Windows hasn't said the decoder has gone. | Check the USB cable and the decoder's power. |
| `No car has crossed the line for 45s - check the loop and its cable.` | The decoder is working, but no car has crossed for three typical laps (and at least 30 seconds). | Check the loop cable at the decoder. |
| `Not seen: Frank (25s) - stopped, or a flat transponder?` | A car that has crossed in this race hasn't crossed again for three of its typical laps (and at least 20 seconds) while the others keep crossing. | Look for the car on track. If it's running, its transponder may be flat or loose. |

It only watches real races while they're running: not practice, not a demo, not under a red flag, and not before anyone has been released in a hand-released or staggered start. A car released later is only expected from its own release. After a red flag, or once the decoder is back after a warning, every count starts again. Once the checkered is out, cars that have finished aren't expected to cross, so only a car still out there and silent is named. A black-flagged driver isn't expected either.

A car whose transponder was flat before the race never crosses at all, so it can't be missed: use **Pre-race check** before the heat for that. See [Transponders](../timing/transponders.md).

## The clock column buttons

- **Quick Race** (<kbd>F8</kbd>) runs a race with whoever is on track. See [Other ways to start](#other-ways-to-start).
- **Open Big Screen** (<kbd>B</kbd>) opens the audience display. See [Big Screen](../screens/big-screen.md).
- **Open Announcer Screen** (<kbd>A</kbd>) opens the Announcer screen in a window of its own, beside this one or on a second monitor. Its **Race Control** button brings the main window back to the front. See [Announcer screen](../announcer/voice.md#announcer-screen).
- **Qualifying Projection** opens or closes a window that shows where the class would qualify if the running heat ended now. See [Program and mains](../race-day/program-and-mains.md#qualifying-projection).
- **Pre-race check** (<kbd>R</kbd>) checks which cars in the round (or on the board) the decoder has heard. See [Pre-race check](../race-day/events-and-entries.md#pre-race-check) and [Transponders](../timing/transponders.md).

## Tips

- You can leave the **Race Control** page during a race. Timing, time limits and the finish keep running in the background.
- You can't undo a lap correction, but you can reverse it with the opposite correction. See [Corrections and recovery](corrections-and-recovery.md).
- To rehearse the whole flow without cars, use [Demo Mode](practice-and-ghosts.md#demo-mode).
