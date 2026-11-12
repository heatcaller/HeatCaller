# Quicker race-night setup: plan

12 Nov 2026. This plan has three parts:

1. **Part 1** checks [race-night-build-plan.md](race-night-build-plan.md) against the code on main (7b465e7) and the user guide.
2. **Part 2** maps what a race director does today, from opening the app to the first green flag.
3. **Part 3** suggests ways to make that quicker.
4. **Part 4** is a race director's review of Parts 2 and 3, which changes the measure and the order.

**The short version:** clicks aren't what slow a club night. People, cutoffs and handoffs are. So the plan now starts with D1–D5 in Part 4: entries close, posting heats, the practice transponder check, marshals in place, and planned breaks. Only then does it do the click-saving in Part 3.

C# paths are relative to `HeatCaller/HeatCaller/`.

---

## Part 1: the build plan against the code and the guide

Most of the plan is built. Phases 0–7 were merged in PRs #140–#150, #156 and #160. Here is where each item stands.

| Status | Items |
|---|---|
| **Built** | 1.1 auto-start (`AutoStart.cs`), 1.4 late or skip asks, 2.1 curfew planner (`NightPlan.cs`), 2.4 close the night, 3.2 mains preview, 3.4 late entry (`LateEntry.cs`), 4.1 mixed-class heats, 5.1 points qualifying, 5.2 heads-up, 5.3 multi-leg A-mains, 6.1 hand scoring, 6.2 standby PC, 6.3 offline-first, 7.1 entry import, 7.2 LiveTime guide, 7.3 leader behind, 7.4 race lines |
| **Partial** | 0.1, 0.2, 0.3, 1.2, 1.3, 1.5, 1.6, 2.2, 2.3, 3.1, 3.3, 4.2, 4.3 (what's missing is listed below) |
| **Missing** | 5.4 ROAR, EFRA and NZRCA presets. This was left out on purpose: only figures from a rulebook we can check are allowed. 7.5 cross-club index: a strategy call, still not decided |

**What the partial items are missing:**

- **0.1 and 0.2:**
  - Heats get no "called" or "saved" time.
  - There's no worst green-to-green figure.
  - "Idle director" and "break" aren't tagged as causes.
  - Hold and +1 min don't log a cause.
  - Night pace isn't on the shareable results page.
- **0.3:** the demo script is written, but there's no seeded demo data set, and it hasn't been timed against LiveTime. *This is the measure for Part 3 too.*
- **1.2:**
  - There are no keys for Hold or Start now.
  - The phone remote has no Start now.
  - There's no director-side "skip driver, score DNS".
- **1.3:** the countdown is on Race Control and the Big Screen. Phones show estimates, not the live countdown.
- **1.5:** the rest gap is fixed at one race, and moving drivers by hand doesn't warn.
- **1.6:** there's no 5-minute push for marshals.
- **2.2:** estimated start times aren't on the Big Screen.
- **2.3:** "We're done after this round" doesn't offer shorter or fewer mains.
- **3.1:**
  - The protest window opens only after the last qualifier.
  - It isn't on the Big Screen.
  - Queries aren't moved to the top of Driver Requests.
- **3.3:** there's no "number order" seeding.
- **4.2:**
  - There's one fee plus an extra-class fee, not a fee per class.
  - Phone check-in doesn't show paid or owing.
- **4.3:** marshal swaps are desk-only. There's no ask-and-accept on the phone.

**Where the user guide disagrees with the code:**

- **`race-day/drivers-and-classes.md` (the warning on sanctioning presets):** says "HeatCaller doesn't model multi-leg mains". The comment near `SanctioningPreset.cs:136` says the same. Both are out of date since `AMainLegs.cs`.
- **`getting-started/first-race-night.md`, step 4:** says to click **New Race Day**. But a fresh install has already made an empty race day, named after the install date (`EventSpine.StartFirstDayIfNever`). Following the guide leaves that day behind.
- **`race-day/events-and-entries.md:54`:** says the Set Up Tonight offer appears "on a start with no race day open". `OfferRaceDaySetupOnce` (`MainForm.Navigation.cs:916`) returns when a day is open, and the auto-made day is open. So a new club probably never sees the offer. This comes from reading the code; I haven't run it.
- **`first-race-night.md`, step 6:** says the Next Steps button "does that instead". The button opens the six-step wizard, while **New Race Day** opens the two-question window. The guide never says the two differ.

---

## Part 2: what setting up a night takes today

**A repeat weekly night**, best case: about 10 clicks plus one action per driver.

1. **Connect the decoder:** 0–2 clicks. Auto-connect is off by default.
2. **New Race Day, then Start the Day:** about 3 clicks. The new day copies these from the last one: venue, layout, championship, kind of night, classes, heats each driver gets, drivers per heat, and the out-by time (`RaceDayEventControl.cs:1650`).
3. **Retype what isn't copied:** first race at, entry fee, extra-class fee, first-round seeding, share-a-heat-under and between-race talk. These go back to defaults every week. That's 2–8 edits for a club that charges fees or keeps a timetable.
4. **Start the phone server** on Local Screens. This is needed every night unless auto-start is ticked, which is off by default.
5. **Check drivers in.**
   - A driver racing a different class tonight means a trip to Driver Hub, because the Class column on Who's Racing is read-only (`CheckInControl.cs:360`).
6. **Build tonight's heats, then confirm:** 3 clicks.
   - Anyone who checks in after this doesn't get in by themselves.
   - Each late driver means right-click, **Add a Late Entry**, then pick the driver, once per class.
7. **Pre-race check, then Start Q1 Heat 1:** 1–3 clicks. From here, auto-start runs the night.

**A first night adds:**

- the first-run wizard
- classes, which have **no race length** until someone sets one
- drivers: one by one, by CSV, or with Import Entries
- a track layout
- "Set up phone pages" (a Windows admin prompt)
- online check-in, with its staff PIN and QR sheets

**The biggest friction points:**

1. Settings reset every week (step 3).
2. Late check-ins don't flow into the program.
3. The class can't be changed at the desk.
4. There are three different ways to start a day.
5. The ghost first day, and the wizard offer it probably hides.
6. The "only checked-in drivers" gate comes back on every new race day. Clubs without a sign-on desk have to untick it each week.
7. Decoder auto-connect and phone-server auto-start are both opt-in.
8. A new class races with no limit until someone notices.

---

## Part 3: ways to make it quicker

**The target:** a repeat night takes **one click, then check-ins, then racing**. A first night goes from install to a timed heat in **under 15 minutes**. Measure both with the 0.3 demo script. Part 4 adds the two measures that matter more on the night: **entries close to heats posted** and **doors open to first green**.

> **Changed by Part 4:** B2 no longer adds late check-ins by itself, and C1 moves up. See D6 and the revised order at the end.

### A. Quick wins (about a day each)

| # | Change | Saves |
|---|---|---|
| A1 | **Copy every per-night setting from the last day.** Add first race at (same weekday), the fees, seeding, share-under, between-race talk and the check-in gate to what `StartEvent` copies. The check-in gate becomes the club's choice, not a result of the kind of night. | 2–8 edits a week, plus the "nobody checked in" surprise |
| A2 | **Turn auto-connect and the phone server's auto-start on once they've worked.** The first time the decoder connects or the server starts, tick its auto option and say so. | 2–4 clicks a night |
| A3 | **Class drop-down at the desk.** Make the Class column on Who's Racing editable, listing the driver's classes plus "Add a class for tonight...". | A trip to Driver Hub for each driver who switches |
| A4 | **Fix the first-launch path.** Don't make an empty day on a fresh install, or let the Set Up Tonight offer ignore that day. Then fix `first-race-night.md` steps 4 and 6. | A confused first night |
| A5 | **No class without a length.** Add a Class starts at 5 minutes, and the Classes step in the wizards asks for a length. | A race that never ends on the first night |
| A6 | **Fix the outdated doc lines** from Part 1 (multi-leg mains, the wizard offer, the Next Steps button). | Trust in the guide |

### B. Bigger changes (2–4 days each)

| # | Change | Why |
|---|---|---|
| B1 | **"Start tonight like last week"**: one button on the Next Steps card when no day is open, such as "Start Club Night like Tue 4 Nov". It starts the day with everything from A1, ticks its classes, and applies the sound. **Change something...** opens Set Up Tonight on The day step. This one path replaces the three ways in. | A repeat night is one click |
| B2 | **The program builds itself, and late check-ins slot in.** At a set time before first race at, or when the director closes check-in, Next Steps builds the heats. A countdown and **Hold** follow the auto-start rules, so nothing starts without warning. After that, each new check-in goes into the smallest heat that hasn't run, through `LateEntry`, with a line on Next Steps and Undo. | It removes the build step and the per-driver late-entry clicks, which is the biggest saving on a busy night |
| B3 | **Ready to race checklist**, the start-of-night twin of Close the night: decoder heard, server up, relay reachable, voice lines prepared, rentals in, last backup, first heat's cars all seen. Each line has a fix button and ticks itself. Build it on the Ready to Race bar and `CloseNightForm`'s pattern. | One place to look before the first green, instead of five pages |
| B4 | **Night templates**, such as "Tuesday club night" or "Points round". A template saves the kind of night, classes, formats, fees, times and sound. The Championship Calendar can put templates on dates, so the day is already set up when the director arrives. | Clubs that run two kinds of night switch between them in one click |

### C. Bigger moves (a week or more)

| # | Change | Why |
|---|---|---|
| C1 | **Drivers say "I'm coming" from their phone** before the night, on the Me page: tonight, with these classes. The desk sees an expected list, B2 can draft heats early, and arriving is a one-tap check-in. Needs a relay deploy. | The program is mostly built before the doors open |
| C2 | **First-night import inside the first-run wizard.** Bring a club over from LiveTime, RCSignup or a CSV: drivers, transponders and classes in one step, using `EntryImport`. Class lengths are offered as "starting points", not presets, to keep the rule of only using figures we can check. | First install to first heat in under 15 minutes |

---

## Part 4: a race director's review

This part reads Parts 2 and 3 the way the person running the desk on a Tuesday night would.

### What Part 3 misses

1. **Clicks are the wrong measure.** Starting the day takes 30 seconds. The real time goes on:
   - the queue at the desk
   - drivers choosing their class at the window
   - chasing money
   - finding a dead transponder after the heats are posted

   Measure **entries close to heats posted** (target under 5 minutes) and **doors open to first green**.
2. **There's no entries-close time.** Every club runs on "entries close 6:30, racing 7:00". Without a cutoff, B2's auto-build has nothing to fire on.
3. **Slotting late entries in by themselves causes arguments.** Once heats are posted, drivers have looked up their heat, and marshal duty is worked out from it. Heats that keep changing lose the board's trust.
4. **It assumes one director who knows the app.** At most clubs the role rotates, and the director races too. Two things matter more than click counts:
   - a once-a-month volunteer can run the night from the Ready to race checklist
   - the desk can be handed over for the director's own heat

### Open questions to settle

- **Where does a late entry go on a pace-seeded first round?** "Smallest heat" can put a fast driver in the slow heat. They should go in the heat nearest their pace that has room, never past the track's limit for one heat.
- **What does an "about" time promise?** Drivers need to know whether the heat waits for it or can go early. Suggested rule: never more than 2 minutes early, because drivers are in the pits working on cars.
- **What should a new class's length be?** A5's 5 minutes is fine. But the class must also say how it finishes: a hard stop at the time, or the time plus finishing the lap. On-road and off-road clubs differ.
- **One transponder in two classes.** Drivers often move one transponder between cars. Check-in, the pre-race check, the rest guard and D3 all have to handle the same number being in two classes tonight. The code hasn't been checked for this yet.

### D. What a director needs (new items)

Checked in the code on 12 Nov 2026. A few pieces already exist: practice rounds (`RoundKind.Practice`), time and lap penalties (`ResultEditor`, `TimePenaltyForm`), heat-sheet printing (`HeatSheetRenderer`), the announcer's long-break lines (`BetweenRaces.LongBreakMinutes`) and a free-text skill level on the profile. None of the D items below exist yet.

| # | Change | Size | Why |
|---|---|---|---|
| D1 | **Entries close at.** A time on the day, copied by A1. The announcer calls "Entries close in 10 minutes" and "Entries are closed", and Next Steps counts down to it. At the cutoff the program builds (B2) and waits to be posted. After it, a late entry is the director's call. | S | The cutoff is the director's main tool, and B2 needs it |
| D2 | **Post the heats.** A clear **Posted** state. Posting prints the heat sheets, the estimated times and the marshal list in one action, and pushes them to the Schedule page and the Big Screen. Posted heats are locked: nothing moves anyone without asking. | M | Drivers check the board, so it has to stay right |
| D3 | **Practice is the transponder check.** At posting, Next Steps lists every checked-in driver whose transponder wasn't seen in open practice tonight, for example "4 not seen in practice: Al Gorithim, Rhoda Rage…", with a rental button beside each. | S | Dead batteries and wrong numbers get caught before Q1, not mid-heat |
| D4 | **Marshals in place.** Before auto-start goes green, the marshal posts are confirmed, either from the marshals' phones or ticked by the director. The announcer calls missing marshals by name and post. No-shows are logged for the club's marshal rule, such as losing your best qualifier. Marshal no-shows are the most common delay. | M | Auto-start (1.1) mustn't go green with empty posts |
| D5 | **Planned breaks.** Add breaks to the night ahead of time, such as "water the track after Q2, 10 minutes" or "dinner after Q3, 20 minutes". The curfew planner, the estimated times and the announcer all use them. Without them, every estimate after the first break is wrong. | S | The estimated times stay right all night |
| D6 | **B2, changed: asks first after posting.** Before posting, check-ins flow in freely. After posting, a late driver only fills an empty slot in a round that hasn't run, in the heat nearest their pace, and nobody else moves. Marshal duty is worked out again, with a line naming who changed. By default Next Steps asks ("Add Max Power to Q2 Heat 3?"); adding by itself is a setting. | – | Keeps B2's saving without breaking the board's trust |
| D7 | **Class running order.** A setting on the day: which class goes first and how the classes rotate. The rest guard checks it, and "juniors first" is one option. The order decides who marshals for whom and the turnaround for drivers in two classes. | S | Parents want juniors done early, and two-class drivers need their gap |
| D8 | **Desk handover.** **Hand the desk to...** passes Hold, Start now and the next-step card to a deputy's phone while the director races. It builds on the phone remote and auto-start. Add a **desk volunteer** view that only does check-ins and money, so nobody rebuilds the program by accident. | M | The director races too |
| D9 | **Waivers and membership.** Check-in shows "Waiver missing" (new drivers, and a guardian for anyone under 18) and "Membership expired 3 Oct" (the club, or a national body's racing insurance) before the driver goes in a heat. Membership numbers and dates are recorded by hand, the same rule as money. | M | Many clubs and every sanctioned one need it. For some it decides whether they can use HeatCaller |
| D10 | **Drivers' meeting sheet.** A one-page read-out, printed or read by the announcer: class order, tonight's marshal rule, curfew, breaks, track changes. Built from what the day already knows. | S | Two minutes before Q1, said the same way every week |
| D11 | **Penalties as a flow.** A marshal or the director logs a penalty during the race: jump start, cutting the track, or a missed marshal post. It carries a reason, shows on the result and the phone, and can be challenged in the protest window (3.1). It builds on the existing time and lap penalties. | M | It's a different job from correcting a result |
| D12 | **A novice flag.** Ticked on the profile, it means: no bump-up into a novice's main, a heat with other slow drivers, a welcome line from the announcer, and a mentor named at the desk. It replaces the free-text skill level for this purpose. | S | Keeping new drivers is how clubs survive, and this costs very little |
| D13 | **Before the night.** Post tonight's classes and running order the day before. **Racing's off tonight** sends one message to everyone who usually comes, by push and email. | S | This pairs with C1 |

### Revised order

1. **D1, D2, D3 and D5:** entries close, posting, the practice transponder check and breaks. They change the night more than any click saving. D1 and D5 are small, so they can go with A1.
2. **A1–A6** as one PR. They're small, mostly in `RaceDayEventControl`, `EventSpine`, `CheckInControl` and the docs.
3. **D4** (marshals in place) and **D7** (class order). These go next to the auto-start code.
4. **B1**, then **B2 as D6**, on its own branch with wide tests.
5. **C1** (pre-entry) together with **D13** and **D9**. Pre-entry is the biggest saving at the desk, and it's why RCSignup exists, so it moves up from last.
6. **B3** with D10, **D8**, then **D11**, **D12**, **B4** and **C2**.

Before and after each step, time the 0.3 demo for a repeat night and a first night. Use both measures: clicks, then entries close to heats posted and doors open to first green.
