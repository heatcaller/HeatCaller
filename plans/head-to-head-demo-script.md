# Head-to-head demo script: HeatCaller vs LiveTime

Race-night build plan, item 0.3 (11 Nov 2026). The same night on both products, timed, so "faster than LiveTime" is a number rather than a claim. Run it once before Phase 1 (the baseline), then again after each phase.

## Setup (identical on both)

- **LiveTime:** the 30-day free trial from livetimescoring.com, on the same Windows laptop. Each product uses its own simulated decoder or demo mode, so there are no real cars.
- **The entry list:** 15 drivers in 3 classes: Stock Buggy (6), Mod Buggy (5), Short Course (4).
  - Each driver has a transponder number.
  - Two drivers enter two classes.
  - One driver is a late entry (see step 4).
- **The format:**
  - 2 qualifying rounds of 4-minute heats, ranked on laps and time.
  - Mains of 10 minutes, with 2 bump-ups from B to A in Stock Buggy (the only class big enough for a B-main once mains are 4 drivers each).
- **Rules for the person timing:**
  - Use a stopwatch and a click counter.
  - Start the clock at the first action, and stop it when the named thing is on screen and ready.
  - The operator may read each product's own help but must not ask anyone.
  - Do each product twice, by different people if possible, and keep the better run.

## The four timings

| # | From | To | Count |
|---|---|---|---|
| 1 | First launch, with no data | First heat goes green | Seconds, clicks, keystrokes |
| 2 | One heat's checkered flag | The next heat's green (no help from auto-start in the baseline run) | Seconds, clicks |
| 3 | The last qualifier is saved | The first main goes green, with bump-ups ready | Seconds, clicks |
| 4 | A late entry turns up after Q1 | They're in a Q2 heat, and the posted heats that didn't need to change are untouched | Seconds, clicks, and how many drivers moved who didn't need to |

## What to record

- The four timings for each product and each run.
- Each product's night-pace numbers where it has them. For HeatCaller, take the event report's **Night pace** section: green to green, finish to next green, on-track share, and the longest gap with its cause.
- Screenshots of each product at the end of step 3, the mains with bump-ups.
- Anything the operator had to look up, and where they found it.

## Results

| Run | Product | 1. Launch to green | 2. Heat to heat | 3. Quals to first main | 4. Late entry | Notes |
|---|---|---|---|---|---|---|
| Baseline | HeatCaller | 98 clicks, 231 keystrokes (app 23 s + 9 s start sequence) | 4 clicks (app 0.4 s + 9 s start sequence) | 10 clicks (app 1.1 s + 9 s start sequence) | 8 clicks, 14 keystrokes (app 0.9 s); 0 drivers moved | Scripted run, clicks counted; human time still to be stopwatched. See "Scripted baseline, 20 Nov 2026" below. |
| Baseline | LiveTime | | | | | |

## Scripted baseline, 20 Nov 2026

**Scripted run, clicks counted; human time still to be stopwatched.** A throwaway test (not committed) drove the real HeatCaller windows on main d91d46d5 plus the seeding fix below. It clicked the real buttons, ticked the real boxes and typed into the real fields, starting from an empty data folder. Races were run by Demo Mode, at real speed, with the script's 4-minute heats.

How things were counted:
- Each control action is one click, and each typed character is one keystroke (Enter and Tab are one each).
- A number box is a double-click and the digits, or one click per arrow for a step of up to three.
- **App time** is how long the app took to respond to every action, measured until it went idle. It leaves out the person's thinking and race running time.
- **Start sequence** is the wait from Yes to the green: On Air, then the demo's 5 s staging and 3 s countdown. All three are fresh-install defaults.

The entry list:
- 13 people, each with one transponder: Stock Buggy 6, Mod Buggy 5 (two of the Stock drivers race Mod too), Short Course 4. That gives 15 class entries.
- Qualifying was 2 rounds, 8 per heat, so each class got one heat per round, 6 heats in all. Heats were 4 minutes, ranked on most laps then time.
- Mains were 10 minutes. Stock Buggy had 4 drivers per main and 2 bump-ups; the other classes kept the default of 8.
- The script says only Stock is big enough for a B-main with mains of 4, but Mod has 5 drivers, so mains of 4 would split Mod too. That is why only Stock was set to 4.

### 1. First launch to first green: 98 clicks, 231 keystrokes, app 23.4 s, start sequence 9.0 s

The route:
1. **Welcome wizard (12 clicks, 46 keys):**
   - Get started, then Next on the look and decoder steps.
   - Typed the track name, then the three class names.
   - Race length set to 10 for the mains.
   - Skipped the drivers step, unticked the tour, Finish.
2. **Offer and Start Tonight:** Yes to "Would you like a hand setting up a race night?", then Start Tonight.
3. **Set Up Tonight (47 clicks, 181 keys):**
   - Back to the Classes step, and qualifying rounds set from 3 to 2.
   - The 13 drivers added with Add Driver: name, Tab, transponder, class ticks, Add Driver. This is most of the step-1 count, 40 clicks and 181 keys.
   - Build It, then Done.
4. **Classes page (27 clicks, 3 keys):** for each class, Edit, then Qualifier Format..., tick "Use a custom format", time limit 4, OK, Save. Stock also had Drivers per main set from 8 to 4.
5. **Demo Mode (2 clicks):** ticked "Let a demo fill in the race program".
6. **Race Day > Next Steps (5 clicks):** Post the heats. The two print boxes were unticked because there is no printer.
7. **Start the first heat (3 clicks):** right-click the heat on the run sheet, Start as Demo..., Yes.

Friction:
- **Welcome wizard, classes step:** one race length for every class. There is no way to say heats are 4 minutes and mains 10, so the qualifier length has to be set class by class in Classes > Edit > Qualifier Format. That costs 3 trips through two dialogs.
- **Welcome wizard, drivers step:** every driver typed there goes into one class. A three-class list with two double entries can't go in there, so the step was skipped.
- **Set Up Tonight opens on Drivers when Start Tonight comes first.** The Classes step is skipped, and qualifying rounds (3 by default) are only seen by someone who thinks to press Back. Start Tonight doesn't ask either.
- **Set Up Tonight's Format button edits only the class's one race format.** Qualifier length, drivers per main and bump-ups all need the Classes page afterwards.
- **"Drivers per main" is 8 by default.** With 4 wanted, it has to be found inside each class's editor.
- **Demo Mode files nothing against a heat unless "Let a demo fill in the race program" is ticked**, and it is off by default. Left off, the night never moves on, and Next Steps doesn't say why.
- **Post the Heats has "Print the heat sheets" and "Print the marshal list" ticked by default.** That is two unticks on a laptop with no printer.
- **Starting a heat as a demo takes 3 clicks** (right-click, menu, Yes). Holding the Next Steps Start button for a second does it in one press, but a hold reads the real mouse and could not be scripted.
- **The Next Steps card says "Timer not connected. Laps won't be counted."** during a demo night, although the demo's laps are counted.

### 2. Heat to heat: 4 clicks, 0 keystrokes, app 0.3-0.9 s, start sequence 9.0 s (all 5 changeovers the same)

- **Route:** the Race Day sidebar item (a demo start moves the window to Race Control), then right-click the next heat, Start as Demo..., Yes.
- **Auto-start:** it is on by default, but it never arms after a demo, so it gave no help here.
- **Wall time:** checkered to filed took 4-6 s, which is the field finishing its lap (race time). Filed to next green took 9.4-10.5 s, and almost all of that is the start sequence.

### 3. Last qualifier saved to first main green: 10 clicks, 0 keystrokes, app 1.1 s, start sequence 9.0 s

- **Route:** Race Day, then 3 rounds of "Build the finals from qualifying" and Yes, one per class. Then Start as Demo on the first main, which is Mod Buggy A-Main.
- **Bump-ups:** Stock's seeding question listed "A-Main (plus the top 2 of the B-Main)" with both B-Main drivers marked "[bump-up place]". The B-Main (2 drivers) is scheduled before the A-Main (4 seeded, bump-up count 2).
- **Bug found and fixed (commit 3e2aa4e3):** with three classes, seeding the first class made its A-Main the next race, so Next Steps offered "Start A-Main" while Stock and Short Course had no finals yet. They were only offered after that main had run. Next Steps now seeds every class first, once no qualifier is left to run. On main the same step would have been 4 clicks, plus a hunt for the other two classes' seeding on the run sheet's right-click menu or More.
- **Screenshots:** none were taken of the mains with bump-ups. The rounds above come from the program itself.

### 4. Late entry after Q1: 8 clicks, 14 keystrokes, app 0.9 s; 0 drivers moved

- **Route:** Race Day > Who's Racing, Add Driver, then name, Tab, transponder, the Short Course tick and Add Driver. Then the Next Steps tab, where the card already offered "Add them to the heats". Then "Add the Ticked".
- **Result:** the window named the heat before adding: "Goes in: Q2 Heat 1". The late driver went into Short Course Q2 Heat 1 (4 to 5 drivers). Every other posted Q2 heat was unchanged, so 0 drivers moved who didn't need to.
- **Friction:** none found.

### Night pace

The event report's Night pace section has nothing to show for this run. By design it leaves out demo races (`NightPace.For` skips `IsNotScored()` sessions, and every demo is one).

The same measures, worked out from the run's own green and filed times:
- **Green to green:** 254-257 s. That is a 4:00 race, a 4-6 s finish lap, and about 10 s to the next green.
- **Finish to next green:** 9.4-10.5 s.
- **On-track share:** 96.8%.
- **Longest gap:** 10.5 s, twice: after Q1, where the late entry (about 1 s) fell into the gap, and before the first main, where all three classes were seeded.

A real stopwatched night will add the operator's own time to every one of these.

### Not measured

- Human seconds: these need the stopwatch run.
- The LiveTime side.
- Screenshots of the mains.
- The real decoder path. The simulated decoder on the Connection page only laps 8 of the club's transponders, so a 13-driver night can't run on it.
