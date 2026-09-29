# Program, heats and mains

The program is the race day's running order: every qualifying heat and main, for every class, in the order they go out. This page explains how to build it, adjust heats, start rounds, see who's qualified where, seed the mains, apply bump-ups, assign corner marshals, and print heat sheets and the run sheet.

All of this happens on **Race Day → Program** and **Race Day → Run Sheet**.

## Before you start

The program is built from the current race day, so set that up first on **Race Day → Event** (see [Events, entries and check-in](events-and-entries.md#race-day-event)):

- Start a race day with **New Race Day**.
- Tick the classes running today under **Classes racing:**.
- Set **Qualifying rounds:** and **Drivers per heat:**.
- Decide whether to tick **Only include drivers who have checked in**.
- Click **Save Changes**.

Each class also needs drivers: a driver is in a class when their profile says so (see [Driver Profiles](drivers-and-classes.md#driver-profiles)). How a class is scored — its qualifying method, main size and bump-ups — is set on the **Classes** page (see [Classes](drivers-and-classes.md#classes)).

## The Program tab

<!-- screenshot: Race Day Program tab with the NEXT STEP banner, the running order and the footer buttons -->

The tab has three parts.

**The banner** at the top says where the day has got to, in one sentence, with one button that does the next thing. See [The next-step banner](#the-next-step-banner).

**The running order** lists the race day's rounds, all classes together, in the order they run:

| Column | Shows |
|---|---|
| **#** | Position in the running order. |
| **Round** | The round's name, such as "Q2 Heat 1" or "A-Main". |
| **Class** | The class, in the class's color. |
| **Drivers** | How many drivers are in the round. |
| **Track** | The track layout, or "-". |
| **Status** | **Recorded** (run and saved), **NEXT** (the next round to run, highlighted), or **Scheduled**. |

Only rounds that belong to the current race day are listed, so last week's heats don't appear. The **Class:** filter above the list shows one class at a time (**All classes** shows everything). **NEXT** is always the next round for the whole day, whichever class you're looking at.

**The footer** has two groups of buttons:

- On the left, buttons that act on the **selected round**: **Start Selected** and **Edit Drivers**.
- On the right, tools for the day and its classes: **Timetable**, **Standings** (**Tonight's Standings** on a club night), **Marshals**, **Move the Top Finishers Up** (not on a club night) and **Glossary**.

The class tools work on the class chosen in the **Class:** filter. If the filter shows all classes, they use the class of the selected round, or the only class racing. If none of those settles it, HeatCaller asks you to pick a class.

Everything else is on the round menu. Right-click the list to open it: on a round to act on that round, or anywhere in the list for the commands that don't need one.

| Menu item | What it does |
|---|---|
| **Start This Round** | Same as **Start Selected**. |
| **Start as Demo...** | Runs the round as a simulated race. Nothing is recorded against it and no timing hardware is used. See [Practice, ghosts and demo mode](../race-control/practice-and-ghosts.md). |
| **Edit Drivers...** | Changes who's in the round. |
| **Move Drivers Between Heats...** | Opens the class's heats side by side. See [Balance heats](#balance-heats). |
| **Move Up** / **Move Down** | Moves the round one place in the running order. |
| **Build Tonight's Heats...** | Builds (or rebuilds) the qualifying heats. On a club night it's **Build Tonight's Races...**. |
| **Build the Finals from Qualifying...** | Seeds a class's mains. Not shown on a club night. |
| **Add a Main by Hand...** | Adds a main you choose the drivers for. Not shown on a club night. |
| **Save Heat Sheet...** | Saves a printable heat sheet for a class. |
| **Delete** | Deletes the selected rounds from the program (it says **Delete 3 Rounds** when several are selected). |

The banner is only a shortcut. Every command it offers is also in this menu, so you can build or seed at any time, not only when the banner suggests it.

## The next-step banner

The banner works out the single next thing to do, checking in this order:

| Situation | Headline | Button |
|---|---|---|
| No race day is open | "No race day is open." | **Go to the Event Tab** |
| The race day has no classes ticked | "This race day has no classes yet." | **Go to the Event Tab** |
| None of today's classes has any drivers | "None of today's classes has any drivers." | **Manage Drivers...** (opens Driver Profiles) |
| Nothing is scheduled yet | "Nothing is scheduled yet." | **Build the Qualifying Programme** |
| A lower main has finished and bump-ups are due | "B-Main has finished." | **Apply Bump-Ups...** |
| A round is waiting to run | "Next up: Q2 Heat 1" | **Start Q2 Heat 1** |
| Everything has run, and a class has qualified but has no mains | "Qualifying is complete for Stock." | **Seed the Mains** |
| Everything has run | "The programme is complete." | **Qualifying Standings...** |

The small line under the headline gives the numbers behind it — for example the class, driver count and track for the next round, and how many rounds have run and how many are left. If one class has no drivers while others do, it says that class will be skipped.

Bump-ups come *before* starting the next round on purpose: once the upper main has gone out, it's too late to add the bumped drivers.

## Build the qualifying program

1. Click **Build tonight's heats** on the **Next Steps** card above the program (or right-click the list and choose **Build Tonight's Heats...**).
2. Check the summary: how many heats per class, how many drivers, the heat size, the track, and whether only checked-in drivers are used. It also says how many unrun qualifiers will be replaced (only today's are counted and replaced).
3. Click **Yes**.

HeatCaller builds every planned qualifying round for every class that's racing and has drivers:

- **Heats per round.** Each class's drivers are split into as few heats as the **Drivers per heat:** setting allows. For example, 13 drivers with 8 per heat makes 2 heats (7 and 6). Drivers are dealt into the heats in turn, so heats come out within one driver of each other.
- **Rotation.** In each new round, the starting point in the driver list moves on by one heat's worth, so the same drivers aren't first out on a cold track every round.
- **Names.** Heats are called "Heat 1", "Heat 2" and so on when there's one qualifying round, or "Q1 Heat 1", "Q2 Heat 3" and so on when there's more than one.
- **Order.** All classes' Q1 heats come first, then all Q2 heats, and so on.
- **Track.** Every heat starts on the race day's **Track layout:**.

Rebuilding replaces any of today's qualifiers for those classes that haven't been run yet. Recorded rounds, and rounds from earlier race days, are never touched. You can undo the build with the **Undo** button in the sidebar.

If some of the new heats have been run by the time you undo, they're kept with their results, and the rest of the build is undone. A heat that's on track right now is kept too. A **Some rounds were kept** box lists them.

!!! note
    Building needs an open race day. If you're on the legacy event, the banner sends you to the Event tab instead.

## Adjust the program

### Change who's in a round

Select a round and click **Edit Drivers...**. In **Drivers in *round***, tick the drivers to include and use **Move Up** / **Move Down** to set their order — that's the order they appear on the heat sheet. **Select All** and **Clear All** tick or untick everyone.

The list shows the class's drivers. To add someone from another class (a late entry, say), click **Add Driver...** — they're shown with "(not in this class)" beside their name.

If the round has already run, HeatCaller warns you that changing its drivers only affects the program, not the recorded results.

### Balance heats

**Move Drivers Between Heats...** (on the round menu: right-click the list) shows all of a class's rounds for the day side by side, one column per round, so you can see everyone at once.

<!-- screenshot: Move drivers between heats window with several heat columns and the NOT IN A HEAT column -->

- Drag a driver from one heat to another, or up and down within a heat to change the order.
- Drag a driver onto **NOT IN A HEAT**, or select them and press <kbd>Delete</kbd>, to take them out. That column lists the class's drivers who aren't in any heat, and drivers who are missing from one of the qualifying rounds, marked with the rounds they're missing, for example "(not in Q2, Q3)". Drag from it to put someone in.
- A driver can only be in one heat of each qualifying round. Dropping a driver on a heat they're already in, or on a heat in a qualifying round they already have a heat in, is refused with a message saying where they are. That includes a heat they've already raced tonight ("... has already raced ..., which is in the same qualifying round as ..."). Mains aren't limited this way.
- Rounds that have already been recorded are marked "recorded, locked" and can't be changed.
- The line at the bottom shows the spread, for example "3 heats, between 6 and 7 drivers each."

Nothing is saved until you click **Save**, and **Cancel** discards everything. If a heat ends up empty, HeatCaller asks before saving. The whole session's changes are one step for **Undo**.

### Change the running order

Drag rounds up or down the list to reorder them — a line shows where they'll land. You can drag several selected rounds together. **Move Up** and **Move Down** on the round menu do the same one place at a time.

When the list is filtered to one class, moves are made relative to what you can see.

!!! note
    Reordering isn't added to Undo. Drag the round back instead. (Undo only holds three steps, and it's kept for changes that are hard to redo by hand.)

### Delete rounds

Select one or more rounds, right-click them and choose **Delete**. Deleting a round removes it from the program but doesn't delete any recorded results. Undo puts the rounds back in their original places in the running order.

## Start a round

Click the banner's **Start** button, select a round and click **Start Selected**, or double-click the round. HeatCaller asks you to confirm, then switches to **Race Control**, calls the grid, and begins staging and the countdown straight away. See [Running a race](../race-control/running-a-race.md).

You'll get an extra warning if the round has no drivers, or has already been run — starting a recorded round again records a new session and doesn't replace the existing result.

If a race is already running on **Race Control**, HeatCaller asks "Abandon the running race?" before anything is announced. See [Staging over a running race](../race-control/corrections-and-recovery.md#staging-over-a-running-race).

### Auto-advance

**Auto-advance** at the bottom right (on by default) decides what happens when a race finishes without a round selected on Race Control: HeatCaller records it against the next unrun round, so you work through the program without picking each round by hand. The **from:** list sets which round it picks:

- **the class that was racing** (the default) — the next round for the class most of the drivers on track belong to.
- **any class (programme order)** — the next unrun round overall.
- **only "*class*"** — the next round for that one class.

When auto-advance is off and no round is selected, the race is saved to history but isn't tied to any heat.

Auto-advance only picks from the open race day's rounds. With no race day open it picks nothing, since the only rounds left would be unrun ones from an earlier meeting; the race is saved but not tied to a heat, and the console says why. Open the race day, or pick the round in the **Round:** box before the start.

## How qualifying ranks drivers

Qualifying is worked out from a class's **recorded qualifying heats** in the active race day — even on a brand-new race day, earlier days' heats aren't used. With no race day open, nothing counts: **Standings...** and seeding the mains say "No race day is open, so there are no standings to show - qualifying counts one race day's heats. Open the race day, or start tonight's, to see them." instead of ranking every heat the class has ever run. The same today-only results feed seeding, standings and ranking. Each class's rules come from its settings on the **Classes** page. Ghost cars are never counted.

### Most laps, then lowest time

Each heat is scored on laps completed; ties are broken by the lower total time. With **Heats that count:** at 1, a driver's best heat decides their place. With 2 or more, their best heats are added together (laps and time) and the rest are dropped.

### Best consecutive laps

Each heat is scored on the driver's quickest unbroken run of N laps (N is **Consecutive laps to count:**). The run from the start to the car's first crossing of the line isn't a lap and is never part of a run. A driver who didn't complete N laps in a heat has no valid run for that heat.

With **Heats that count:** at 2 or more, a driver's best heats' runs are added together. Drivers are then ranked:

1. By **how many valid runs** make up their total — more runs rank higher.
2. Then by the **total time** of those runs — lower is better.
3. Drivers with no valid run at all go to the bottom, ranked among themselves by most laps, then lowest time.

Ranking by the number of runs first means you can't top qualifying by missing a heat: a one-heat total is never compared with a two-heat total. Running more heats can only help a driver.

A counted heat where the driver had no valid run is shown with **(no valid run)** after its name, so a total made of fewer runs than heats explains itself.

### Qualifying standings

Click **Standings...** to see the class's current qualifying order.

<!-- screenshot: Qualifying Standings window for a best-3-consecutive class with dropped heats -->

| Column | Shows |
|---|---|
| **Pos** | Qualifying position. |
| **Driver**, **Car** | Who. |
| **Best Laps** | Laps in the counted heats. |
| **Best 3 Consecutive** | *Best-consecutive classes only.* The run time, or its total over several heats ("Best 3 Consec. x2"). |
| **Avg / Lap** | *Best-consecutive classes only.* Average lap within the counted runs. |
| **From Lap** | *Best-consecutive classes only.* Which lap the run started on. |
| **Fastest Lap** | Fastest single lap in the counted heats. |
| **Heats** | Heats run, or counted/run (for example "2/3") when some were dropped. |
| **Counted Heat** | Which heat (and when) the figures came from, or the counted heats joined with "+". |
| **Dropped** | *Only when more than one heat counts.* The heats that didn't count, and what they scored. |

A driver who qualifies on their alternate transponder is ranked once, under their main transponder. If both of their cars ran in the same heat, the better result counts.

Drivers who couldn't be scored under the best-consecutive rule are grayed out. Double-click a row (even after sorting the table) to see exactly which session the figures came from, so you can check them in **Race Analytics → Lap Times**.

### Qualifying projection

While a qualifying heat is running, **Qualifying Projection** on the **Race Control** page opens a window showing where everyone in the class would qualify if the heat ended right now. It shows the whole class, not just the drivers on track, and updates as laps come in.

- Drivers on track are highlighted.
- The unlabeled column shows places gained (▲) or lost (▼) by this heat so far, or **NEW** if it's the driver's first heat.
- **Score** and **Heats** follow the class's qualifying rule.
- **Keep this window on top** keeps it above other windows — handy on a second screen facing the pits.

If no qualifier is running, the window says so.

## Seed the mains

When a class has finished qualifying, the **Next Steps** card offers **Build the finals from qualifying** (it's also **Build the Finals from Qualifying...** on the round menu, available at any time: right-click the list).

1. Click **Build the finals from qualifying**.
2. Check the summary — how many mains and how many drivers in each, and whether bump-ups are on.
3. Click **Yes**.

How the mains are built:

- Drivers are taken in qualifying order and split into mains of **Drivers per main:** (from the class settings). The fastest go in the **A-Main**, the next group in the **B-Main**, and so on.
- If the numbers don't divide evenly, the **last** (slowest) main is the short one, never the A-Main.
- The mains are added to the program **lowest first** — C, then B, then A — so a lower main has run before the one above it and its top finishers can bump up.
- Each main except the lowest is set to receive the class's **Bump up:** number of drivers from the main below.

Seeding only looks at the current race day's mains. If the class already has mains today that haven't run, seeding replaces them. If any of today's mains have already been run, seeding stops with **Mains already run** — delete or finish the existing mains first, so recorded results aren't orphaned. Mains from earlier race days are left alone and never block seeding. Seeding can be undone. As with the qualifying build, any new mains that have been run by the time you undo are kept, along with a main that's on track right now, and a **Some rounds were kept** box lists them.

### Add a main by hand

For a one-off main, right-click the list and choose **Add a Main by Hand...**. In the **Add Main** dialog, enter a **Round Label:** (it suggests "A-Main", then "B-Main", based on the mains already in today's program), pick a **Track Layout:**, and tick the drivers. **Top:** with **from Qualifying** ticks the top N qualifiers; **All** and **None** tick or clear everyone.

A hand-added main takes part in bump-ups only if its label names a tier — "A-Main", "B Main", "C" and so on. A label like "Consolation" isn't treated as a tier.

## Bump-ups

Bump-ups move the top finishers of a lower main up into the main above it. For example, with **Bump up:** set to 2, the top two in the B-Main join the A-Main. Only the current race day's mains are considered.

A bump-up can happen only when **all** of these are true:

- The lower (feeder) main has been run and recorded.
- The upper main hasn't been run yet.
- The upper main is set to receive bump-ups, and they haven't already been applied to it.

When a bump-up is due, the banner says so ("B-Main has finished.") and offers **Apply Bump-Ups...**. You can also click **Bump-Ups...** in the footer at any time.

1. Click **Apply Bump-Ups...** (or **Bump-Ups...**).
2. Check the drivers and positions listed. The list shows exactly who will move.
3. Choose:
    - **Yes** to move them up now.
    - **No** to skip bump-ups for that main and carry on. The main is marked as done, so the banner moves on. You can undo it.
    - **Cancel** to decide later. The banner keeps offering **Apply Bump-Ups...**.

If every driver who would bump up is already in the upper main, there's nobody to promote: HeatCaller says so in **Nothing to bump** and marks that main's bump-ups as done, so the program can move on.

The upper main **grows** to take the bumped drivers — nobody is moved down, because the lower main has already run. The bumped drivers join at the back of the upper main's list. Anyone who's already in the upper main (for example, moved there by hand), under either of their transponders, is left alone and not counted twice. A driver who raced the lower main on their alternate transponder is promoted under their main transponder and takes one place, not two. Bump-ups can only be applied once per main, and can be undone.

If no main is ready, **Nothing to bump** explains why for each main — for example "B-Main hasn't been run yet." or "This main has already been run - bump-ups have to happen before it runs."

!!! warning
    Apply bump-ups **before** starting the upper main. Once it's been run, its bump-ups can't be applied.

## Corner marshals

Many clubs have the drivers from the previous heat marshal the next one. **Marshals...** works out who marshals which corner for every round of a class on the current race day.

The number of posts comes from the track layout's **Corner marshal posts:** setting (see [Track Layouts](drivers-and-classes.md#track-layouts)), using the layout of the class's rounds. If it's 0, the window tells you to set it on **Track Layouts**.

<!-- screenshot: Corner Marshals window with one round highlighted for an uncovered post -->

For each round, the **Corner Marshals** window lists **Round**, **Marshalled By**, **Post** and **Marshal**:

- Each round is marshalled by the drivers of the class's previous round, one per post in order.
- **A driver who's racing in the round is never assigned to marshal it** — for example a late addition or a driver in back-to-back heats. They're skipped, and the next driver from the previous round takes the post.
- The first round has no previous heat, so its posts are left for you to assign by hand.
- If there aren't enough free drivers, the uncovered posts are shown and the round is highlighted, for example "Heat 2 has 3 drivers free for 4 posts - 1 corner uncovered. (1 more is driving in Q2 Heat 1.)"

**Copy as Text** copies the whole roster as plain text, ready to print or paste into a group chat.

The same marshal list is what the announcer reads out for the `{marshals}` placeholder in grid calls. See [Voice announcements](../announcer/voice.md).

!!! note
    The marshal roster and the heat sheet only list the current race day's rounds. With no race day open, they list every round saved for the class.

## Heat sheets

A heat sheet is a printable page for the pit table: the current race day's rounds of a class with its drivers, cars, transponders and blank columns to write laps and times in.

1. Right-click the list and choose **Save Heat Sheet...**. It uses the class in the **Class:** filter (or the selected round's class).
2. Choose where to save the PNG.
3. Open it and print it at full page size.

The sheet is black on white for easy reading, with a band of the class's color across the top and beside each round, so you can sort a pile of sheets by class at a glance. It's letter-size; if a class's rounds don't all fit, the sheet ends with a note to print one class at a time.

## Race Day → Run Sheet

The run sheet is the day's running order as a status board: what's done, what's on track now, and what's next. It combines the program's rounds with anything else happening that day — doors open, lunch, the prize-giving.

<!-- screenshot: Race Day Run Sheet tab with done, on-track and up-next lines -->

### Build it from the program

Click **Build from Program**. HeatCaller writes one line per round of the current race day's program. Lines you added by hand stay where they are; lines built from the program before are replaced.

Lines that came from the program look after themselves:

- Starting the round stamps the line with the actual start time.
- Recording the round ticks the line off.
- If the round is later deleted from the program, the line shows "(round deleted from program)".

### Add your own lines

**Add Entry...** adds a line with a **Time:** (or tick **No set time**), a **Class:**, and a **Description:** such as "Lunch". You need at least a class or a description. **Edit...**, **Delete**, **Move Up** and **Move Down** manage lines (**Move Up** and **Move Down** stay within the race day); you can select several lines to delete.

### Reading the sheet

The grid shows **Done**, **Time**, **Class**, **What** and **Status**. The **Status** column shows:

- **done** (and when it started) for finished lines, faded.
- **on track · started 14:05** for a line that's started but not finished, with how far off its slot it started — **on time**, **4 min late** or **3 min early** — when the slot is a clock time.
- **up next** for the first line not yet done.

Click the **Done** checkbox to tick a line off by hand. **Up Next:** above the grid names the next line.

### Reminder countdown

The **Reminder Countdown** panel announces a countdown to the next line through the announcer. Set **Minutes until start:** (1–120) and click **Start**. The announcer calls out at 5 minutes, 1 minute and when time's up; **Stop** cancels it quietly. A countdown of 5 minutes or less skips the "5 minutes" call.

### Print the run sheet

**Print Run Sheet...** saves the sheet as a black-on-white PNG for the drivers' stand, with a tick box, time, class and description for each line. Lines already done are ticked and struck through, and started lines show their start time. Open it and print at full page size.

**Clear Schedule** removes every line after you confirm. The confirmation explains: "Undo puts it back - but only until a few more changes are made or the app is closed." So if you clear it by mistake, click **Undo** in the sidebar straight away.

Undoing **Clear Schedule** or a run sheet edit only changes the race day it was made on, and keeps any lines you've typed since.
