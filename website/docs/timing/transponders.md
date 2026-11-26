# Transponders

Every car carries a transponder, and the decoder reports each pass as a transponder number. HeatCaller turns those numbers into drivers, checks that every car in the next race is being heard, and throws out passes that are too quick to be real laps. This page explains how.

## How a transponder becomes a driver

HeatCaller matches each pass to a driver by the **Transponder:** number on their driver profile (**Driver Hub** page). The driver's name, car, class and photo all come from that profile, and so do announcements, results and records.

- Matching ignores upper and lower case, so `8467be` on a profile matches `8467BE` from the decoder.
- Each transponder number belongs to one profile. Once a profile is saved, its transponder number can't be edited; to move a driver to a different transponder, create a new profile.
- A transponder number can't be on two profiles. If you add or edit a profile and its **Transponder:** or one of its other **Transponders:** is already another driver's, HeatCaller refuses with a "Transponder already in use" warning that names that driver. The profile form stays open so you can correct the number.

What the number looks like depends on your decoder:

| Decoder | Transponder number |
|---|---|
| Direct EasyLap Decoder (USB) | Six hexadecimal characters, for example `8467BE`. |
| I-Lap Decoder (Serial) | 3 to 8 digits, with leading zeros removed. |
| MyLaps / AMB P3, ZRound / RiCino, Cano / RCHourglass, OpenStint (Beta) | In decimal, the number printed on the transponder. |
| EasyLap / Robitronic Serial (Beta) | Six hexadecimal characters, as over USB. |

To find a transponder's number, pass it over the loop with the decoder connected and read the `[LAP EVENT] transponder=...` line in the Developer Console on the **Connection** page. See [Connecting your decoder](decoders.md). During a race you can also right-click a driver on the **Race Control** leaderboard and choose **Copy transponder ID**.

For adding and editing profiles, see [Drivers, classes and venues](../race-day/drivers-and-classes.md).

### Alternate transponder

A driver can have more than one transponder: one in each car, a spare, or a replacement for one that died. The profile's **Transponder:** number is their main one. The others are on the **Transponders:** line of the profile form: type a number and click **Add**.

Every one of a driver's transponders counts as that driver: names, announcements, results and the driver card's figures all use the profile. The driver card lists them all after **Transponders**.

When a driver stops using a transponder, pick it on the **Transponders:** line and click **Retire**. HeatCaller asks "Are you sure you want to retire transponder *number*?" first, with **No** as the default; click **Yes**. A retired transponder is shown as "(retired)" and is no longer in use, but it is still the driver's: everything timed on it stays theirs, and if it turns up again its laps are still filed under them. **Bring Back** puts it back in use. **Remove** takes it off the driver altogether, for a number added by mistake. Laps already timed on a removed transponder no longer count as theirs, so the form asks first. The main transponder can be retired but not removed, because the driver's results are filed under it.

Whichever decoder you use, HeatCaller decides what to do with each of a driver's other transponders once per race, the first time it crosses the line:

| When it's first heard | What happens for the rest of that race |
|---|---|
| None of the driver's other cars is on track yet | Its laps go in the driver's own row, as if it were the main car. |
| The main car, or another of the driver's cars, is already on track | It's timed as a separate row, still showing the driver's name. The Race Control log says so. |
| It was timed under its own number earlier in this race (it was given to the driver mid-race) | It keeps its own row, so its laps stay together. |

#### Which car is announced

A transponder can be fitted to one of the driver's cars, or move between their cars. Set this with **Fitted to:** on the driver's **Transponders** tab (see [A driver's transponders](../race-day/drivers-and-classes.md#a-drivers-transponders)), or when you give an unknown transponder to a driver with **Belongs To**.

When a transponder fitted to a car crosses the line, that car is the one announced, shown on the race board and recorded in the results for that row. This applies even when its laps go in the driver's own row. A transponder that moves between cars uses the driver's active car, and so does one fitted to a car that's since been retired. Click **Put Back** on the car and it's the one announced again.

If the main car turns up after another car's laps have already gone into the driver's row, the Race Control log warns that the row's lap times can't be trusted, because two cars' passes are mixed together.

!!! warning
    Don't run two of a driver's cars in the same heat. One car per driver per heat keeps the lap times clean.

A transponder listed on two profiles, perhaps in a file saved by an older version, is ignored for matching and arrives as an unknown transponder.

## Unknown transponders

A transponder that isn't on any profile still gets timed. It appears on the leaderboard under its number.

HeatCaller never interrupts a race to ask about it. Instead:

1. The first time an unknown transponder crosses the line in a session, the console on the **Connection** page shows "[race] New transponder *number* is on track - it's listed above the board once the session ends, to say whose it is."
2. It goes on a list shown as a banner at the top of the **Driver Hub** page, and on the **Race Control** page between races. The banner says "A transponder crossed the line with no driver" (or how many did). Each one has a line with what it did, for example "14 laps in Heat 2 · best 7.905". The sidebar shows the count next to **Driver Hub**.
3. On its line, choose what it is:
    - **Belongs To** gives it to a driver already on file. The **Who is on transponder *number*?** window lists your drivers; type in **Find:** to narrow the list, and pick one. Under **Where is it?**, choose **In one of their cars:** and pick the car from their garage, or **Their own transponder - it moves between cars**. If it replaces one of theirs, pick that one in **It replaces:**; the old one is retired, and everything timed on it stays theirs. Click **Give *number* to *driver***. The laps it has already done count for that driver.
    - **New Driver** opens a profile form with the number already filled in. Enter the driver's name and anything else you know, and click **Save**.
    - **Club Rental** registers it as one of the club's loan units, asking what it's called at the counter (an asset tag such as R4, or nothing). It gets a rental profile named "Rental R4" (or "Rental *number*"), so its laps have a name and stay out of the club's records. It appears on the **Rentals** page.
4. **Later** hides the banner until another unknown transponder turns up. The sidebar count stays.

Each choice can be undone with **Undo**, which puts the transponder back on the list.

On the **Race Control** leaderboard you can also right-click an unknown transponder's row and choose **Give *number* to an existing driver**, which opens the same window, or **New driver on *number***.

If a number already belongs to another driver, you get the same "Transponder already in use" warning as on the **Driver Hub** page. A transponder you name some other way, for example from the **Driver Hub** page or a phone sign-up, leaves the list by itself within a few seconds.

The list lasts until HeatCaller is closed. A transponder still unnamed is listed again the next time it crosses the line.

Unknown transponders are detected on every decoder on the **Connection** page, the Beta ones included.

On the phone and web pages served by the laptop, an unnamed driver shows as their transponder number. On the club's online pages they show as "Unnamed driver" instead, so transponder numbers are never published. See [Local Screens](../screens/phone-and-web.md) and [The online relay](../online/index.md).

## Rental transponders

The **Rentals** page tracks the club's loan transponders: who has each one, what deposit you're holding, and whether it's come back. See [Drivers, classes and venues](../race-day/drivers-and-classes.md) for how to use it.

Checking a unit out on the **Rentals** page records the loan. It doesn't change whose name the laps appear under. Laps from a rental transponder are named from the driver profile that has that transponder number, like any other.

On a profile, tick **Rental / loaner transponder** to mark the transponder as a rental. The profile form explains: "Both keep a driver out of all-time records - for guests, staff cars and loaners." (the other option being **Exclude from track records and points**). A rental profile doesn't set track records, and its results and badges are left out of the club's online records.

## Ghost cars

Ghost cars are simulated drivers you can add to a race (see [Practice, ghosts and demo mode](../race-control/practice-and-ghosts.md)). Each ghost gets a made-up transponder number that starts with `GHOST-`, for example `GHOST-3FA9C2`. You never need to type it.

Because ghosts are stored like drivers, they appear on the leaderboard and in race rosters. HeatCaller keeps them out of the places where only real cars make sense:

- They aren't listed for check-in, tech inspection, batteries or the **Garage**.
- They never count as evidence that a transponder is working, so they don't appear in the pre-race check.
- They never affect how real cars are timed.

## The pre-race check

The pre-race check answers one question before the green flag: is every car that should be in this race being heard by the decoder? Finding a flat or badly mounted transponder here costs one driver two minutes. Finding it after the race costs everyone a re-run.

### Run the check

1. Connect your decoder. See [Connecting your decoder](decoders.md).
2. Ask drivers to put their cars on the track, or roll them over the loop.
3. On the **Race Control** page, click **Pre-race check** (or press <kbd>R</kbd>).
4. Read the headline and the list. Fix any problems, have those cars cross the loop again, and click **Check again**.

<!-- screenshot: Pre-race check window with one driver marked X (not heard) and one marked ! (weak) -->

The check compares two lists:

- **Who should be there.** The selected round's roster, if a round is selected. Otherwise, the drivers already on the leaderboard.
- **Who the decoder has heard.** Every transponder that has crossed the loop in the last 12 minutes, whether or not a race was running.

### Reading the results

| Mark | Meaning | What the window says |
|---|---|---|
| X | On the roster but not heard at all. | "Not heard at all. Transponder off, flat, or not fitted." |
| ! | Heard, but only just. | "Heard ..., but only just - ... Likely to be missed at racing speed. Check the battery and how it is mounted." |
| ? | Heard, but not on the roster. | "On track but not in this race - ... Either they are in the wrong heat, or this is a car from the last one still circulating." |
| OK | Heard clearly. | "Heard just now, *n* passes, signal *n*." |

Four tiles at the top count the cars in each group: **Heard**, **Weak**, **Not heard** and **Not in race**. The list under them has **Driver**, **Transponder** and **What the decoder hears**, with problems listed first. When everything is fine, the headline reads "All *n* clear. Every car on the ... is being heard. Good to go." Otherwise it sums up what's wrong and ends "Put the cars that failed on the track and press Check again."

Opened from **Race Control**, the window also has **Mission Control**, which opens [Mission Control](../race-control/running-a-race.md#mission-control) for the wider check (the decoder, timing, the website) and closes this one.

### Signal strength

The EasyLap decoder reports how many times it saw the transponder during each pass (hits) and how strong the signal was. HeatCaller treats a pass with fewer than 3 hits as marginal, and marks a transponder as weak ("!") when at least half of its recent passes were marginal. The **OK** line shows the average signal strength.

Of the Beta decoders, MyLaps P3, RCHourglass, OpenStint and the serial EasyLap report hits too, and are judged the same way.

The I-Lap decoder doesn't report signal information, and nor do ZRound and a plain Cano. Their transponders show no signal figure and are never marked as weak; the check can still tell you whether each one has been heard.

!!! note
    What the decoder has heard is kept in memory only, for 12 minutes. Restarting HeatCaller starts the list afresh.

### Signal over time

A transponder rarely dies all at once. A battery going flat, a cracked case or a transponder knocked loose in its mount shows first as a slow slide in signal over a few nights, each night still "fine" on its own. So HeatCaller also keeps a record of how strongly each transponder is read, night by night, and compares each night with that transponder's usual.

A transponder is called **fading** when, on its latest night:

- its average signal is 25% or more below its usual, or
- 30% or more of its passes were only just caught (fewer than 3 hits), when it's normally read cleanly.

"Its usual" is the middle value of its earlier nights, up to eight of them, so one odd night doesn't set the bar. A transponder needs at least three earlier nights, and at least 5 passes on a night, before it's judged.

When a transponder is fading:

- The console says so, once a night, for example "Frank's transponder 4385919 is reading 32% weaker than usual - check its battery and how it's mounted before it drops out of a race."
- The **Transponders** tile in [Mission Control](../race-control/running-a-race.md#mission-control) lists it while its latest night is within the last month.

A night runs from 6am to 6am, so a club night that goes past midnight counts as one night. The record is per transponder, not per driver: a driver's second car has its own. Ghost cars aren't recorded.

This needs a decoder that reports hits: the EasyLap, or one of the Beta decoders that does (see [Signal strength](#signal-strength)). The I-Lap decoder doesn't, so its transponders are never called fading.

## Minimum lap time and rejected laps

A transponder sitting near the loop, or a car that bounces over it after a crash, can be read twice in quick succession. HeatCaller rejects any pass that comes too soon after the previous one from the same transponder.

The limit is **Min valid lap (ms):** in the race format. The default is 3000 ms (3 seconds), and you can set anything from 100 to 60000 ms.

- To change it for the current race, click the pen beside the format at the foot of the **Race Control** leaderboard.
- Each class can have its own format, and a round can override its class. See [Running a race](../race-control/running-a-race.md).
- A venue can carry its own default as **Minimum lap time (ms):** on the **Venues** page.

How a pass is judged:

- The first crossing of a race always counts.
- After that, a pass counts only if it comes at least the minimum lap time after the previous pass from that transponder, counted or rejected. Judging against rejected passes too stops a stationary transponder that retriggers every couple of seconds from sneaking every second read through as a lap.
- The lap time shown is measured from the driver's last counted lap.

When a pass is rejected:

- The console shows "Lap rejected (too soon: ...ms after the previous pass, ...ms minimum; ...ms since the last counted lap): *transponder*".
- The driver's rejected-pass count goes up. The **Clean %** leaderboard column shows what share of a driver's passes counted as laps.
- If you've turned on the **Invalid lap** sound on the **Sounds → Effects** page, it plays. It's off by default. See [Sounds and music](../announcer/sounds-and-music.md).

A lap that lands within 10% of the minimum lap time is never accepted as a new track record, because that is what a retriggering transponder looks like.

!!! tip
    Set the minimum comfortably below the quickest real lap anyone will do on the layout. Too high, and a fast driver's genuine laps are thrown away. Too low, and double reads count as laps.

If a real lap was rejected or a double read was counted, fix it with the lap adjustments on the **Race Control** page. See [Corrections and recovery](../race-control/corrections-and-recovery.md).

Separately, the I-Lap and the Beta decoders treat two reads of the same transponder within 0.8 seconds as one pass before the minimum lap time is even checked.
