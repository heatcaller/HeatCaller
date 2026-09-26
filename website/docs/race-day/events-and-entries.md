# Events, entries and check-in

This page covers the start of a race day: creating the event, getting drivers checked in at the desk, printing QR check-ins, scrutineering cars, and checking that every transponder is being heard before the first race.

The **Race Day** page has five tabs, in the order the day runs: **Event**, **Entries**, **Program**, **Run Sheet** and **Tech Log**. This page covers **Event**, **Entries** and **Tech Log**. The program and run sheet are covered in [Program, heats and mains](program-and-mains.md).

## How events work

An event (a race day) is what keeps one meeting's records separate from the next. Check-ins, tech inspections, the program and the run sheet are all filed against the **current** event. Starting a new race day starts all of them fresh, and last week's records stay filed under last week's event instead of being cleared away.

Events matter most for qualifying. Qualifying only counts the rounds that belong to the event, so a second race day doesn't seed its mains from both days' heats mixed together. This holds even on a brand-new race day before any heats have run: earlier days' results aren't borrowed. Seeding mains, bump-ups, marshal assignments and heat sheets also only look at the current race day's rounds. With no race day open, everything recorded is used.

Anything recorded before you started using events lives in a built-in **legacy event**. You can look at it, but you can't edit it, and it's never the default.

## Race Day → Event

<!-- screenshot: Race Day Event tab with a race day selected and classes ticked -->

### Start a race day

1. Go to **Race Day → Event**.
2. Click **New Race Day**. HeatCaller creates an event named after today's date (for example "Race Day 21 Sep 2026"), makes it current, and puts the cursor in the name box.
3. Type a proper name if you want one, and fill in the fields below.
4. Tick the classes that are racing today under **Classes racing:**.
5. Click **Save Changes**.

!!! warning
    Changes on this tab aren't saved until you click **Save Changes** — including the classes you tick.

### Event fields

| Field | What it does | Default |
|---|---|---|
| **Working on:** | Picks which event the fields below show. Real events are listed newest first, with their date and whether they're open or closed; the legacy event is always last. | The current event |
| **Name:** | The event's name. | "Race Day" and the date |
| **Venue:** | Where it's being run. Pick a saved venue or type any name — you don't need a venue profile. | Empty |
| **Track layout:** | The layout every generated round starts on. Pick a saved layout or type a name. | Empty |
| **Championship:** | The championship this event counts toward, or **(standalone - no championship)**. See [Results and championships](../race-control/results-and-championships.md). | Standalone |
| **Qualifying rounds:** | How many qualifying rounds each class runs (1–10). | 3 |
| **Classes racing:** | Tick the classes running today. The Program tab builds the qualifying program for all of them in one go. | None |
| **Drivers per heat:** | How many drivers share a heat when the program is built (2–20). | 8 |
| **Only include drivers who have checked in** | When ticked, building the program only uses drivers checked in on the Entries tab. When unticked, it uses every driver in the class. | Off |

The line under the picker shows what the selected event already holds, for example: "CURRENT - the whole page is about this event. 14 checked in · 2 classes racing · 12 rounds in the programme, 4 recorded · 12 run sheet lines".

!!! tip
    Leave **Only include drivers who have checked in** off if your club doesn't run a sign-on desk. With it on and nobody checked in, the program would have no one in it.

### Switch, close and reopen events

- **Make Current** points check-in, tech, the program and the run sheet at the selected event. It's grayed out when the selected event is already current. You can make the legacy event current to look at older records.
- **Close Day** stops new rounds joining the event. Its results and standings are kept and can still be reported. If you close the current event, "current" falls back to the legacy event. The button changes to **Reopen Day** for a closed event.

For a list of every event, with the days each one ran and how many rounds it holds, see [Calendar](#championship-calendar) below.

## Race Day → Entries

The Entries tab is the sign-on desk: who's here today, whether their waiver is signed, and whether their car has passed tech.

<!-- screenshot: Race Day Entries tab with some drivers ticked in and the Scan to Check In panel -->

Each row is a driver profile. The class shown is the class on the driver's profile — to move a driver to another class, edit their profile on the [Driver Profiles](drivers-and-classes.md#driver-profiles) page.

| Column | What it shows |
|---|---|
| **Checked In** | Tick when the driver arrives. |
| **Waiver Signed** | Tick if your venue tracks waivers. |
| **Driver** | The driver's name. |
| **Class** | The driver's class. |
| **Tech** | The result of the driver's most recent inspection today in their class: **Passed**, **FAILED**, **Passed (note)**, or **-** if they haven't been inspected. Double-click the cell to inspect them. |

A single click on a checkbox ticks or unticks it straight away. Check-ins and waivers are both kept per event, so a new race day starts with an empty sheet.

The **Class:** filter at the top shows one class at a time, and HeatCaller remembers your choice. The summary beside it reads, for example, "9 of 12 checked in, 7 waivers signed (21 total checked in across all classes)".

The buttons under the list:

- **Check In All Shown** ticks in every driver currently in the list (so it respects the class filter).
- **Check Out Everyone** clears all check-ins for the current event, after you confirm. Waivers aren't touched. You can undo it with the **Undo** button in the sidebar: the drivers are checked back in to the event they were checked out of, and anyone who checked in since stays checked in. You don't normally need it — a new race day already starts with an empty sheet.
- **Inspect...** opens a tech inspection for the selected driver. See [Tech Log](#race-day-tech-log).

### Drivers checking themselves in

Drivers can also check in from their phones, and their check-ins appear on this tab as they arrive:

- On the club Wi-Fi, from the laptop's own check-in page. See [Local Screens](../screens/phone-and-web.md) and the [self check-in poster](#self-check-in-poster) below.
- Over the internet, from your club's online check-in page. See [Online check-in](../online/check-in.md).

Online taps reach the laptop a few seconds late. If the desk changes a driver's check-in, an online tap that was made *before* the desk's change but arrives after it won't undo what the desk did. This holds even if the laptop's clock is wrong, because HeatCaller allows for the difference between its clock and the relay's.

### Scan QR check-ins

The **Scan to Check In** panel lets you check drivers in by scanning their QR check-in with a USB barcode scanner. Scanners plug in and type like a keyboard, so there's nothing to set up.

1. Click in the box that says **Waiting for scan...**.
2. Scan a driver's QR check-in.

The driver is checked in immediately, and the panel tells you what happened:

| Message | Meaning |
|---|---|
| **Checked in: *name*** | Done. |
| ***name* was already checked in.** | Nothing changed. |
| **No driver profile found for transponder *number*.** | The QR check-in is for a transponder that has no profile. |
| **That QR check-in is out of date - print a new one.** | An online QR check-in whose code no driver here has any more: printed before **Reissue All QR Check-ins** was used, printed on another laptop's QR check-in key, or for a driver since deleted. Print a new QR check-in. |
| **That doesn't look like a HeatCaller QR check-in.** | Whatever was scanned isn't a HeatCaller QR check-in — for example a product barcode. |

The scanner reads both kinds of QR check-in described below, so QR check-ins you printed earlier keep working, except online QR check-ins printed before **Reissue All QR Check-ins** was used (see [Reissue all QR check-ins](../online/check-in.md#reissue-all-qr-check-ins)).

### Print QR check-ins

1. On the Entries tab, set the **Class:** filter to the drivers you want QR check-ins for (or all classes).
2. Click **Print QR Check-ins...** and choose where to save.
3. Print the saved images, cut the QR check-ins out and hand them to drivers.

HeatCaller saves the QR check-ins as PNG images sized for letter paper, 12 QR check-ins (3 across, 4 down) per page, with dashed cut lines. If there's more than one page, each page is saved as its own file (`driver-QR check-ins-page1.png`, `driver-QR check-ins-page2.png` and so on). Each QR check-in shows a QR code, the driver's name, and their class and transponder.

There are two kinds of QR check-in, and HeatCaller picks one automatically:

- **Desk QR check-in.** The QR code holds the driver's transponder in a form only HeatCaller's scanner reads. This is what you get when online check-in isn't set up.
- **Online QR check-in.** When online check-in is set up — on the **Club Website** page, **Publish to the relay** and **Let drivers check in from their own phones** are both ticked and the relay address and club are filled in — the QR code is a web link to your club's online check-in page for that driver. Drivers can scan it with their own phone camera — no app needed — and the QR check-in says **Scan with your phone camera to check in**. The desk scanner still reads it too. See [Online check-in](../online/check-in.md).

!!! note
    If you turn online check-in on or off, print the QR check-ins again so they carry the right kind of code. Old desk QR check-ins still scan at the desk either way.

### Self check-in poster

**Self Check-In Poster...** opens a poster with a QR code that takes a phone to the laptop's check-in page, where drivers find their own name and tap it. The poster reads **CHECK IN** with the line "Scan, find your name, tap it. Same Wi-Fi as the timing laptop."

The laptop's web server must be running first, and phones must be on the same Wi-Fi as the laptop. If it isn't running, you'll see **Web server is not running**. Start it on the **Local Screens** page — see [Local Screens](../screens/phone-and-web.md).

The poster opens in its own window, so you can leave it on a second screen by the door while racing carries on:

- **Print...** prints it at full printer resolution.
- **Full screen** fills the screen. Press <kbd>Esc</kbd> to leave full screen, and <kbd>Esc</kbd> again to close the window.
- **Close** closes it.

## Race Day → Tech Log

The Tech Log records scrutineering: which cars have been checked and cleared to race. Each class's checklist is set on the **Classes** page — see [Tech checklist](drivers-and-classes.md#tech-checklist).

<!-- screenshot: Race Day Tech Log tab listing today's inspections -->

The list shows **When**, **Driver**, **Class**, **Car**, **Result**, **Checks** (for example "5 ok, 1 fail"), **Inspector** and **Notes**. **Today only** (on by default) limits it to today's inspections, and **Class:** filters by class. The summary reads, for example, "12 inspected, 1 NOT cleared."

### Inspect a car

1. Click **New Inspection...** (or use **Inspect...** on the Entries tab, which fills in the driver for you). If the car was already inspected today (or in the last 6 hours, for a club night that runs past midnight), **Inspect...** opens that inspection to edit instead of starting a new one. An older inspection, such as Saturday's at a weekend meeting, is left as it was, and a new one is recorded for today. To change an older inspection, pick it in the Tech Log list and use **Edit...**.
2. Pick the **Driver:**. The **Class:** and **Car:** fill in from their profile; you can change them.
3. For each item on the class's checklist, choose **OK**, **FAIL** or leave it at **not checked**. The line under the checklist sums it up: "FAILS - 1 check failed.", "2 checks not looked at yet." or "All checks passed."
4. Enter the **Inspector:** and any **Notes:**.
5. Click **Save**.

The result is worked out from the checks:

- Any **FAIL** records the car as **FAILED**.
- All checks **OK** records **Passed**.
- If some checks are still **not checked**, HeatCaller asks what you meant: **Yes** records **Passed (note)**, **No** records a full pass anyway, and **Cancel** takes you back to finish the checklist.

If the class has no checklist, choose the outcome in the **Result:** list instead: **Passed**, **Passed with a note** or **FAILED**. **Passed** and **Passed (note)** both clear the car to race.

If you edit a car that was failed by hand (with no checklist) and its class has since gained a checklist, HeatCaller asks whether to keep it **FAILED**: **Yes** keeps it failed, **No** records the result of the checklist instead, and **Cancel** goes back to the checklist.

If a check has since been taken off the class's checklist, older inspections still show it, marked "(no longer on the checklist)".

A driver who runs two classes is inspected separately in each, because they may have two cars.

### Who's outstanding?

Pick a class in **Class:** and click **Who's Outstanding?** to list every driver in that class who isn't cleared yet at the current event — either not inspected or failed. It needs a specific class selected.

Inspections are filed against the current event, so a car cleared on the first day of a two-day meeting stays cleared on the second. Re-inspecting a car files the new inspection under the race day that's open now. With no race day open, a pass counts for 18 hours after it was recorded, so a club night that runs past midnight doesn't lose its passes.

The Entries tab, **Who's Outstanding?** and the tech warning on Race Control all use the same rule, so they agree on who's cleared.

**Edit...** (or double-click) changes an inspection, and **Delete** removes the selected ones. Both can be undone. **Cancel** in the inspection window leaves the inspection as it was. Changing only the notes, the inspector or the car doesn't change the inspection's time.

### Tech warnings on Race Control

HeatCaller can remind you, at the moment it matters, that a car in the next heat hasn't been through tech. This is controlled per class by **Warn when a driver in a heat hasn't passed tech today** in the class editor on the **Classes** page. It's on by default. See [Class settings](drivers-and-classes.md#class-settings).

The warning only applies when you're running tech for that class: the class has a tech checklist, or someone has been inspected in that class today or at the current event. Classes you don't scrutineer never get warnings.

When you stage a heat (or throw the green straight from idle):

- The Race Control log shows a "[tech] WARNING" line listing the drivers on the round who haven't been cleared in the Tech Log.
- The Next Up bar adds "· N not through tech".

The warning never stops the race; whether an uninspected car runs is your call. Practice, demo races and races with no round selected are never checked.

## Pre-race check

Before a race, it's worth checking that every car on the grid is actually being heard by the decoder. A flat or badly mounted transponder found now costs a driver two minutes; found after the race, it costs everyone a re-run.

On the **Race Control** page, click **Pre-race check...**. Have the drivers put their cars on the track (or run them over the timing loop) first.

<!-- screenshot: Pre-race check window with one car not heard and one weak -->

HeatCaller checks against the selected round's drivers. If no round is selected, it uses the drivers already on the leaderboard. Each car gets one of these marks:

| Mark | Meaning |
|---|---|
| **OK** | Heard recently, with a good signal. The detail shows when and how many passes, and the signal strength if the decoder reports it. |
| **!** | Heard, but only just. Most of its passes were caught with fewer than 3 hits, so it's likely to be missed at racing speed. Check the battery and how the transponder is mounted. |
| **X** | Not heard at all. The transponder is off, flat or not fitted. |
| **?** | Heard on track but **not** in this race — a driver in the wrong heat, or a car from the last race still circulating. |

"Recently" means in the last 12 minutes. Decoders that don't report a hit count (such as I-Lap) never mark a car as weak, and show no signal figure.

The headline sums it up, for example "All 8 clear. Every car on the roster for Q1 Heat 2 is being heard. Good to go." or "1 not heard at all, 1 only just being heard." Put the problem cars on the track and click **Check again**.

For more on signal strength and transponder problems, see [Transponders](../timing/transponders.md).

## Championship → Calendar

The **Calendar** tab, on the **Championship** page, lists every event with what it covered. It's where you look back at past race days, and it's another place to switch the active event.

<!-- screenshot: Championship Calendar tab listing events -->

The heading shows the active event ("Active: *name*") or "No active event - rounds aren't being grouped". The list has these columns: **Event**, **State** (**Open** or **Closed**), **Started**, **Days run** (worked out from when its rounds were actually raced), **Rounds**, **Recorded** and **Notes**. The active event is marked **[ACTIVE]**.

| Button | What it does |
|---|---|
| **New Event...** | Creates an event with a **Name:** and **Notes:**, and makes it active. |
| **Edit...** | Renames the event or changes its notes. |
| **Make Active** | New rounds join this event. You can also double-click an event. A closed event has to be reopened first. |
| **Close Event** | Stops new rounds joining the selected event. Select a closed event and click it again to reopen it. |
| **Rounds...** | Lists the event's rounds by class, and whether each has been run. |
| **Delete** | Deletes the event. Its rounds and results are **not** deleted — they just stop being grouped, so qualifying will count them alongside everything else. |

All of these can be undone with the **Undo** button in the sidebar. An undo only reverses that one action: rounds generated since are kept, and if it changes which event is active, check-in, tech and the run sheet switch with it.

!!! tip
    **Make Active** here and **Make Current** on the **Race Day → Event** tab do the same thing. Use whichever page you happen to be on.
