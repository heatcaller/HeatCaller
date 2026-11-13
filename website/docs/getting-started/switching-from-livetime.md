# Switching from LiveTime

This page is for a club that runs its nights in LiveTime (LiveTime Scoring Engine, with results on LiveRC) and wants to try HeatCaller. It covers what LiveTime's screens are called here, and how to bring your drivers, transponders and classes across, so your first night doesn't start with typing in the whole roster.

## Before you start

- Install HeatCaller and go through [Your first race night](first-race-night.md) once, with Demo Mode if you like. It's the quickest way to see the night from start to finish.
- Check your decoder. HeatCaller reads **EasyLap** and **I-Lap** decoders, plus the decoders listed under [The Beta decoders](../timing/decoders.md#the-beta-decoders). Most LiveTime tracks run MyLaps (AMB) decoders and transponders, which HeatCaller doesn't read. If that's you, the drivers and classes still come across, but each driver's transponder has to be changed to the one your HeatCaller decoder reads. See [Decoders](../timing/decoders.md).
- Set up your classes on the **Classes** page first. The import matches each entry's class to a class there by name, ignoring capitals, so "stock buggy" goes into **Stock Buggy**.

## What LiveTime's screens are called here

| In LiveTime | In HeatCaller | Where to read more |
|---|---|---|
| **Events**, an **Event** | A race day. Start one with **Start Tonight...** on **Race Day → Next Steps** or **Who's Racing**. A club night is a race day too. | [Events, entries and check-in](../race-day/events-and-entries.md) |
| The Event's **Classes** tab | The **Classes** page, and **Classes racing:** on **Race Day → Setup** for which of them run tonight. | [Classes](../race-day/drivers-and-classes.md#classes) |
| **Registrations** | **Race Day → Who's Racing**: who's here tonight, checked in, with their tech and what they've paid. | [Race Day → Who's Racing](../race-day/events-and-entries.md#race-day-whos-racing) |
| **Imports** (RCSignup, CSV) | **Import Entries** on **Who's Racing**. See [Bring your entries across](#bring-your-entries-across) below. | [Import entries](../race-day/events-and-entries.md#import-entries) |
| Racer profiles | **Driver Hub**. A driver's profile keeps their name, transponders, classes, cars and history from week to week. | [Driver Hub](../race-day/drivers-and-classes.md#driver-hub) |
| **Schedule**, lineups, **Seed Q1 Round**, **Create Round** | **Tonight's program** on **Race Day → Next Steps**. **Build tonight's heats** builds every qualifying round at once, and the heat sheets print from there. | [Program, qualifying and mains](../race-day/program-and-mains.md) |
| Drivers in back to back races | Heats are put in rest-friendly order when they're built, and Auto start gives a driver who just raced an extra 2 minutes. | [Build the qualifying program](../race-day/program-and-mains.md#build-the-qualifying-program) |
| **Seeding Options** and the **Seeding Preview**, bump-ups | **Build the finals from qualifying** on the next-step card. It shows every driver under their main, with the result that put them there and the bump-up places, before you click **Yes**. | [Seed the mains](../race-day/program-and-mains.md#seed-the-mains), [Bump-ups](../race-day/program-and-mains.md#bump-ups) |
| A class's **Timing** and **Rounds** settings | The class's settings on **Classes** (**Race Format**, **Qualifier Format**, **Drivers per main:**, **Bump up:**, **Sanctioning Preset**), and **Heats each driver gets:** and **Drivers per heat:** for the night on **Race Day → Setup**. | [Class settings](../race-day/drivers-and-classes.md#class-settings), [Building the program](../race-day/events-and-entries.md#building-the-program) |
| **Time Table** with **Countdowns Between Races** (auto-advance, auto-countdown, auto-start) | **Auto start** at the foot of the leaderboard on **Race Control**. It's on by default once there's a program: after each race it counts down the gap and starts the next race itself. | [Auto start between races](../race-control/running-a-race.md#auto-start-between-races) |
| Race adjustments (laps, penalties) | Corrections on **Race Control**, and on a saved result in **Race History**. | [Fixing mistakes and recovering](../race-control/corrections-and-recovery.md) |
| **Advanced Automated Announcing** | The **Voice** page: the race caller, and AI commentary if you want it. | [Voice announcements](../announcer/voice.md) |
| **RaceVision** on a TV | The **Big Screen**. | [Big screen](../screens/big-screen.md) |
| Results on LiveRC | Your club's own pages, from the **Club Website** page, and phones on the club Wi-Fi from **Local Screens**. | [Your club's online pages](../online/index.md), [Phones on the club Wi-Fi](../screens/phone-and-web.md) |
| **Parent/Child** mode (a backup timing PC) | Not yet. A standby PC that mirrors the race is planned. Today HeatCaller has crash recovery on the one PC. | [Crash recovery](../race-control/corrections-and-recovery.md#crash-recovery) |

Some of LiveTime's formats aren't in HeatCaller yet: ladder mains beyond the usual A, B and C, double and triple A-mains, and HRQF heads-up qualifying. Check that the formats your club races are covered before your first night. See [Race format and qualifier format](../race-day/drivers-and-classes.md#race-format-and-qualifier-format).

## Bring your entries across

HeatCaller reads three kinds of list, through the same window:

- **A LiveTime export.** Save your racers, or an event's registrations, from LiveTime as a CSV file. Columns like **Name**, **First Name**, **Last Name**, **Transponder** and **Class** are recognized. <!-- TODO: confirm LiveTime's menu path for its racer and registration exports -->
- **An RCSignup export.** Download the event's entries as a CSV file. Its **First Name**, **Last Name**, **Class**, **Transponder** and **Car Number** columns are recognized.
- **A LiveRC entry list.** Open your own track's entry list on LiveRC, select every class's table on the page and copy it (<kbd>Ctrl</kbd>+<kbd>C</kbd>).

Then:

1. On **Race Day → Who's Racing**, click **Import Entries**.
2. Choose **From a CSV File...** and pick the file, or **Paste a Table (LiveRC Entry List)...** for something you copied.
3. Check what each column is, in the list at the top of each one, and click **Next**.
4. Read what will happen to each row, decide on the two ticks, and click **Import**.

[Import entries](../race-day/events-and-entries.md#import-entries) explains the preview and how drivers are matched.

!!! tip
    Do this once before your first night, with **Check everyone on the list in** unticked, to bring the whole roster across. Then on race night, import that night's entries again with it ticked. Drivers already here are matched, not added twice.

!!! warning
    LiveRC's and most LiveTime tracks' transponder numbers are MyLaps numbers. If your HeatCaller decoder isn't MyLaps, change each driver's transponder on **Driver Hub** to the one your decoder reads before the first race. See [A driver's transponders](../race-day/drivers-and-classes.md#a-drivers-transponders).

## Your championship standings

Points from LiveTime come across separately, as each driver's starting total in a HeatCaller championship. Export the standings as a CSV file and use **Import from CSV** on **Driver Hub** with a **Points so far** column. See [Points so far](../race-day/drivers-and-classes.md#points-so-far).

Past race results can be brought into **Race History** one race at a time from LiveRC. They're kept for the record and don't score. See [Pasting a result from LiveRC](../race-control/results-and-championships.md#pasting-a-result-from-liverc).
